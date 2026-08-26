#!/usr/bin/env python3
"""Simulate 7DTD modlet application to verify every xpath matches and the end state is correct."""
import sys
import xml.etree.ElementTree as ET
from lxml import etree

CFG = "/srv/7days/Data/Config"
MOD = "/srv/7days/mod-src/DS_WeaponMastery/server/Config"

def load(name):
    return etree.parse(f"{CFG}/{name}").getroot()

def apply_patch(target, patchfile):
    root = etree.parse(patchfile).getroot()
    ok = 0
    fails = []
    for el in root:
        tag = el.tag
        xp = el.get("xpath")
        if xp is None:
            continue
        try:
            if tag == "set":
                nodes = target.xpath(xp)
                if not nodes:
                    fails.append(f"SET no match: {xp}")
                    continue
                for n in nodes:
                    if isinstance(n, etree._Element):
                        n.text = (el.text or "").strip()
                    else:
                        n.getparent().set(n.attrname, (el.text or "").strip())
            elif tag == "remove":
                nodes = target.xpath(xp)
                if not nodes:
                    fails.append(f"REMOVE no match: {xp}")
                    continue
                for n in nodes:
                    if isinstance(n, etree._Element):
                        n.getparent().remove(n)
                    else:
                        n.getparent().attrib.pop(n.attrname, None)
            elif tag == "append":
                nodes = target.xpath(xp)
                if not nodes:
                    fails.append(f"APPEND no match: {xp}")
                    continue
                for n in nodes:
                    if isinstance(n, etree._Element):
                        for child in el:
                            n.append(child)
            elif tag == "prepend":
                nodes = target.xpath(xp)
                if not nodes:
                    fails.append(f"PREPEND no match: {xp}")
                    continue
                for n in nodes:
                    if isinstance(n, etree._Element):
                        for i, child in enumerate(el):
                            n.insert(i, child)
        except Exception as e:
            fails.append(f"{tag} ERROR {xp}: {e}")
        ok += 1
    return ok, fails

def main():
    total_fails = []
    # Progression
    prog = load("progression.xml")
    n, f = apply_patch(prog, f"{MOD}/progression.xml")
    print(f"Progression.xml: {n} ops, {len(f)} failures")
    total_fails += f
    # verify skills
    for skill, maxlvl in [("craftingBows", 600), ("craftingHandguns", 600), ("craftingKnuckles", 600)]:
        el = prog.xpath(f"//crafting_skill[@name='{skill}']")[0]
        print(f"  {skill}: max_level={el.get('max_level')}, base_exp_cost={el.get('base_exp_cost')}, entries={len(el.xpath('display_entry'))}")
        ct = el.xpath(".//passive_effect[@name='CraftingTier']")[0]
        print(f"    CraftingTier level={ct.get('level')} value={ct.get('value')}")
    # Items
    items = load("items.xml")
    n, f = apply_patch(items, f"{MOD}/items.xml")
    print(f"Items_WeaponTables.xml: {n} ops, {len(f)} failures")
    total_fails += f
    # spot check bow
    bow = items.xpath("//item[@name='gunBowT1WoodenBow']")[0]
    for pe in bow.xpath(".//passive_effect[@name='EntityDamage']"):
        if pe.get("tier"):
            print(f"  gunBowT1WoodenBow EntityDamage tier={pe.get('tier')}")
    mag = items.xpath("//item[@name='bowsSkillMagazine']")[0]
    print("  bowsSkillMagazine AddProgressionLevel remaining:", len(mag.xpath(".//triggered_effect[@action='AddProgressionLevel']")), "AddBuff:", len(mag.xpath(".//triggered_effect[@action='AddBuff']")))
    # Buffs
    buffs = load("buffs.xml")
    n, f = apply_patch(buffs, f"{MOD}/buffs.xml")
    print(f"Buffs.xml: {n} ops, {len(f)} failures")
    total_fails += f
    # Perks (merged into progression.xml - already applied above)
    n = 0
    for req in prog.xpath("//requirement[@progression_name='craftingBows']"):
        print(f"  req craftingBows op={req.get('operation')} value={req.get('value')}")

    if total_fails:
        print("\n=== FAILURES ===")
        for x in total_fails:
            print(" ", x)
        sys.exit(1)
    print("\nALL XPATHS VALID")

if __name__ == "__main__":
    main()
