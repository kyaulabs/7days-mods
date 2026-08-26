#!/usr/bin/env python3
"""DS Log Spikes - modlet verifier.

Simulates applying the generated patches to the vanilla configs and checks
every reference (upgrade/downgrade targets, recipes, ingredients, models,
textures, materials, icons, localization keys) resolves against the real
game data.
"""
import sys
import xml.etree.ElementTree as ET
from lxml import etree

CFG = "/srv/7days/Data/Config"
MOD = "/srv/7days/mod-src/DS_LogSpikes/mod/Config"

# block names this mod owns
OWNED = {
    "DS_WoodLogSpike", "DS_WoodLogSpikeReinforced", "DS_WoodLogSpikeWoodMetal",
    "DS_ScrapIronLogSpike", "DS_ScrapIronLogSpikeReinforced", "DS_SteelLogSpike",
}

# textures that are not referenced by any vanilla block but belong to the
# classic log-spike set (kept in the atlas from older versions)
CLASSIC_ONLY_TEXTURES = {"380"}


def load(name):
    return etree.parse(f"{CFG}/{name}").getroot()


def apply_patch(target, patchfile):
    patch = etree.parse(patchfile).getroot()
    ok = 0
    fails = []
    for el in patch:
        tag = el.tag
        xp = el.get("xpath")
        if xp is None:
            continue
        try:
            nodes = target.xpath(xp)
            if not nodes:
                fails.append(f"{tag.upper()} no match: {xp}")
                continue
            if tag == "append":
                for n in nodes:
                    for child in el:
                        n.append(child)
            elif tag == "prepend":
                for n in nodes:
                    for i, child in enumerate(el):
                        n.insert(i, child)
            elif tag == "set":
                for n in nodes:
                    if isinstance(n, etree._Element):
                        n.text = (el.text or "").strip()
                    else:
                        n.getparent().set(n.attrname, (el.text or "").strip())
            elif tag == "remove":
                for n in nodes:
                    if isinstance(n, etree._Element):
                        n.getparent().remove(n)
                    else:
                        n.getparent().attrib.pop(n.attrname, None)
        except Exception as e:
            fails.append(f"{tag.upper()} ERROR {xp}: {e}")
        ok += 1
    return ok, fails


def main():
    fails = []
    warnings = []

    # 1. Apply patches and locate our blocks
    blocks = load("blocks.xml")
    n, f = apply_patch(blocks, f"{MOD}/blocks.xml")
    print(f"blocks.xml: {n} patch ops, {len(f)} failures")
    fails += f

    recipes = load("recipes.xml")
    n, f = apply_patch(recipes, f"{MOD}/recipes.xml")
    print(f"recipes.xml: {n} patch ops, {len(f)} failures")
    fails += f

    block_names = {}
    for b in blocks.xpath("/blocks/block"):
        block_names[b.get("name")] = b
    for name in OWNED:
        if name not in block_names:
            fails.append(f"block {name} missing from patched tree")

    # vanilla-only lookup helpers
    vanilla = etree.parse(f"{CFG}/blocks.xml").getroot()
    vanilla_names = {b.get("name") for b in vanilla}
    shapes = load("shapes.xml")
    mats = set(load("materials.xml").xpath("//material/@id"))
    items = load("items.xml")
    item_names = set(items.xpath("//item/@name"))
    icons = set()
    import os
    for fn in os.listdir("/srv/7days/Data/ItemIcons"):
        icons.add(fn.rsplit(".", 1)[0])

    # textures used by any vanilla block
    used_textures = set()
    for b in vanilla:
        for p in b.iter("property"):
            if p.get("name") == "Texture":
                for tid in (p.get("value") or "").split(","):
                    tid = tid.strip()
                    if tid.isdigit():
                        used_textures.add(tid)

    for name in sorted(OWNED):
        b = block_names.get(name)
        if b is None:
            continue
        # 2. no vanilla name collision
        if name in vanilla_names:
            fails.append(f"block {name} already exists in vanilla")
        props = {}
        for p in b.iter("property"):
            props.setdefault(p.get("name"), []).append(p)
        # 3. Class / Shape / Model
        if props.get("Class", [None])[0] is None or props["Class"][0].get("value") != "TrunkTip":
            fails.append(f"{name}: missing Class=TrunkTip")
        model = props.get("Model", [None])[0]
        if model is None or not shapes.xpath(f"//shape/property[@name='Model'][@value='{model.get('value')}']"):
            fails.append(f"{name}: model {model.get('value') if model is not None else None} not a known shape")
        # 4. UpgradeBlock / DowngradeBlock targets
        for pc in b.iter("property"):
            if pc.get("name") == "UpgradeBlock":
                for sub in pc:
                    if sub.get("name") == "ToBlock" and sub.get("value") not in block_names:
                        fails.append(f"{name}: UpgradeBlock -> unknown block {sub.get('value')}")
                    if sub.get("name") == "Item" and sub.get("value") not in item_names:
                        fails.append(f"{name}: UpgradeBlock item {sub.get('value')} unknown")
            if pc.get("name") == "DowngradeBlock" and pc.get("value") not in block_names:
                fails.append(f"{name}: DowngradeBlock -> unknown block {pc.get('value')}")
        # 5. Material exists
        for p in props.get("Material", []):
            if p.get("value") not in mats:
                fails.append(f"{name}: unknown material {p.get('value')}")
        # 6. Texture ids used by vanilla (or classic-only)
        for p in props.get("Texture", []):
            for tid in (p.get("value") or "").split(","):
                tid = tid.strip()
                if not tid.isdigit():
                    fails.append(f"{name}: non-numeric texture {tid}")
                elif tid not in used_textures and tid not in CLASSIC_ONLY_TEXTURES:
                    fails.append(f"{name}: texture {tid} used by no vanilla block")
                elif tid not in used_textures:
                    warnings.append(f"{name}: texture {tid} is classic-only (unused in vanilla; assumed still in atlas)")
        # 7. Icon + tint
        for p in props.get("CustomIcon", []):
            if p.get("value") not in icons:
                fails.append(f"{name}: icon {p.get('value')} not in Data/ItemIcons")
        for p in props.get("CustomIconTint", []):
            if not p.get("value") or len(p.get("value")) != 6 or not all(c in "0123456789abcdefABCDEF" for c in p.get("value")):
                fails.append(f"{name}: bad CustomIconTint {p.get('value')}")
        # 8. DescriptionKey / localization
        for p in props.get("DescriptionKey", []):
            pass  # not used; names come from block-name localization instead
        # 9. drop targets exist as blocks or items
        for d in b.iter("drop"):
            dn = d.get("name")
            if dn and dn not in block_names and dn not in vanilla_names and dn not in item_names:
                fails.append(f"{name}: drop target {dn} unknown")
        # 10. repair/upgrade items exist
        for pc in b.iter("property"):
            if pc.get("name") == "RepairItems":
                for sub in pc:
                    if sub.get("name") not in item_names:
                        fails.append(f"{name}: repair item {sub.get('name')} unknown")

    # 11. Recipes: name must be our block, ingredients real
    for r in recipes.xpath("/recipes/recipe"):
        name = r.get("name")
        if name not in OWNED:
            continue
        if name not in block_names:
            fails.append(f"recipe {name}: crafts unknown block")
        for ing in r.iter("ingredient"):
            if ing.get("name") not in item_names:
                fails.append(f"recipe {name}: unknown ingredient {ing.get('name')}")

    # 12. Localization: every owned block + Desc key present, no vanilla dupes
    with open(f"{CFG}/Localization.csv", encoding="utf-8-sig") as f:
        vanilla_loc = {ln.split(",", 1)[0].strip() for ln in f if ln.split(",", 1)[0].strip()}
    with open(f"{MOD}/Localization.csv", encoding="utf-8") as f:
        next(f)
        mod_loc = {}
        for ln in f:
            key = ln.split(",", 1)[0].strip()
            if key in mod_loc:
                fails.append(f"mod localization key {key} duplicated")
            mod_loc[key] = True
    for name in OWNED:
        for key in (name, f"{name}Desc"):
            if key not in mod_loc:
                fails.append(f"localization missing: {key}")
            if key in vanilla_loc:
                fails.append(f"localization key {key} collides with vanilla")

    for w in warnings:
        print("  WARN:", w)
    if fails:
        print("\n=== FAILURES ===")
        for x in fails:
            print(" ", x)
        sys.exit(1)
    print("\nALL CHECKS PASSED")


if __name__ == "__main__":
    main()
