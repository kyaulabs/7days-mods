#!/usr/bin/env python3
"""DS Weapon Mastery - XML modlet generator.

Reads vanilla 7DTD configs and emits XPath modlets that:
  1. Rescale weapon + tool crafting skills to max 600 (quality = skill/100)
  2. Spread item effect tables from the 1-6 scale to the 1-600 scale
  3. Rescale perk CSM LootProb requirements to the new skill max
  4. Magazines grant a study buff instead of levels
"""
import os
import re
import sys
import xml.etree.ElementTree as ET

CFG = "/srv/7days/Data/Config"
OUT = "/srv/7days/mod-src/DS_WeaponMastery/server/Config"

# skill -> (item skill tags (comma separated), original max level, magazine, buff, display name)
SKILLS = {
    "craftingKnuckles":        ("knuckleSkill",                    75, "knucklesSkillMagazine",        "buffDSFocusKnuckles",        "Knuckles"),
    "craftingBlades":          ("bladeSkill",                      75, "bladesSkillMagazine",          "buffDSFocusBlades",          "Blades"),
    "craftingClubs":           ("clubSkill",                       75, "clubsSkillMagazine",           "buffDSFocusClubs",           "Clubs"),
    "craftingSledgehammers":   ("sledgeSkill",                     75, "sledgehammersSkillMagazine",   "buffDSFocusSledgehammers",   "Sledgehammers"),
    "craftingSpears":          ("spearSkill",                      75, "spearsSkillMagazine",          "buffDSFocusSpears",          "Spears"),
    "craftingBows":            ("bowSkill",                        75, "bowsSkillMagazine",            "buffDSFocusBows",            "Bows"),
    "craftingHandguns":        ("handgunSkill",                   100, "handgunsSkillMagazine",        "buffDSFocusHandguns",        "Handguns"),
    "craftingShotguns":        ("shotgunSkill",                   100, "shotgunsSkillMagazine",        "buffDSFocusShotguns",        "Shotguns"),
    "craftingRifles":          ("rifleSkill",                     100, "riflesSkillMagazine",          "buffDSFocusRifles",          "Rifles"),
    "craftingMachineGuns":     ("machinegunSkill",                100, "machineGunsSkillMagazine",     "buffDSFocusMachineguns",     "Machine Guns"),
    "craftingExplosives":      ("explosivesSkill",                100, "explosivesSkillMagazine",      "buffDSFocusExplosives",      "Explosives"),
    "craftingRobotics":        ("roboticsSkill",                  100, "roboticsSkillMagazine",        "buffDSFocusRobotics",        "Robotics"),
    # tools - level by use
    "craftingHarvestingTools": ("harvestingSkill",                100, "harvestingToolsSkillMagazine", "buffDSFocusHarvestingTools", "Harvesting Tools"),
    "craftingSalvageTools":    ("salvagingSkill",                  75, "salvageToolsSkillMagazine",    "buffDSFocusSalvageTools",    "Salvage Tools"),
    "craftingRepairTools":     ("repairingSkill,repairingTools",   50, "repairToolsSkillMagazine",     "buffDSFocusRepairTools",     "Repair Tools"),
}
MAX_LEVEL = 600
QUALITY_TIERS = "100,200,300,400,500,600"
TIER_BOUNDS = [int(x) for x in QUALITY_TIERS.split(",")]

ET.register_namespace("", "")


def serialize(elem):
    """Serialize an ElementTree element to a bare XML string (no namespace prefix)."""
    return ET.tostring(elem, encoding="unicode").strip()


def study_buff_duration_seconds():
    """Read StudyBuffDurationSeconds from server/DSConfig.xml — build-time knob for the
    generated buffs.xml duration and the buff/magazine description texts. 600 = 10 min."""
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "DSConfig.xml")
    try:
        root = ET.parse(path).getroot()
        for el in root.iter("StudyBuffDurationSeconds"):
            v = int(float(el.text.strip()))
            if v > 0:
                return v
    except Exception as e:
        print(f"WARN: reading StudyBuffDurationSeconds: {e}", file=sys.stderr)
    return 600


def parse_config(name):
    return ET.parse(os.path.join(CFG, name)).getroot()


def xpath_escape(s):
    return s.replace("'", "''")


def all_skill_tags():
    tags = set()
    for t, _, _, _, _ in SKILLS.values():
        tags.update(x for x in t.split(",") if x)
    return tags


# ---------------------------------------------------------------------------
# 1. Progression.xml - rescale weapon/tool crafting skills
# ---------------------------------------------------------------------------
def gen_progression():
    root = parse_config("progression.xml")
    out = ['<?xml version="1.0" encoding="UTF-8"?>\n<configs>']
    for skill_name, (tags, orig_max, magazine, buff, display) in SKILLS.items():
        scale = MAX_LEVEL / orig_max
        node = None
        for cs in root.iter("crafting_skill"):
            if cs.get("name") == skill_name:
                node = cs
                break
        if node is None:
            print(f"WARN: crafting skill {skill_name} not found", file=sys.stderr)
            continue

        attrs = dict(node.attrib)
        attrs["max_level"] = str(MAX_LEVEL)
        attrs["base_exp_cost"] = "1000"
        attrs["cost_multiplier_per_level"] = "1"

        entries = []
        for i, de in enumerate(node.iter("display_entry")):
            # Keep every entry (item-based weapons AND icon-based tools/explosives — the
            # old code dropped icon entries, so tools showed no unlock rows at all) and
            # stagger the unlock levels: row i shows "unlocks at (i+1)*100" (first tier
            # 100, second 200, ... final tier 500 with max 600; the last row carries
            # 500,600 so the UI shows the 600 max during final-tier leveling).
            new_de = ET.Element("display_entry")
            for k, v in de.attrib.items():
                if k != "unlock_level":
                    new_de.set(k, v)
            idx = min(i, len(TIER_BOUNDS) - 1)
            new_de.set("unlock_level", ",".join(str(x) for x in TIER_BOUNDS[idx:]))
            for child in de:
                new_de.append(child)
            entries.append("\t\t" + serialize(new_de))

        effects = []
        recipe_tags = []
        recipe_idx = 0
        # Align recipe gates with the display model (entry k unlocks at (k+1)*100) when
        # entries and gates are 1:1 — true for every skill except Explosives (5 entries
        # vs 11 gates), which keeps the vanilla-scaled levels. Recipes without the
        # "learnable" tag are always craftable, so their gates are cosmetic either way.
        n_gates = sum(1 for pe in node.iter("passive_effect") if pe.get("name") == "RecipeTagUnlocked")
        gate_model = len(entries) == n_gates
        for pe in node.iter("passive_effect"):
            if pe.get("name") == "RecipeTagUnlocked":
                tags_attr = pe.get("tags")
                lvl = pe.get("level", "1")
                first = int(lvl.split(",")[0])
                if gate_model:
                    new_first = (recipe_idx + 1) * 100
                else:
                    new_first = max(1, round(first * scale))
                recipe_idx += 1
                recipe_tags.append(tags_attr)
                effects.append(f'\t\t\t<passive_effect name="RecipeTagUnlocked" operation="base_set" level="{new_first},{MAX_LEVEL}" value="1" tags="{tags_attr}"/>')
        for tags_attr in recipe_tags:
            effects.append(f'\t\t\t<passive_effect name="CraftingTier" operation="base_add" level="{QUALITY_TIERS}" value="1,2,3,4,5,6" tags="{tags_attr}"/>')

        attr_str = " ".join(f'{k}="{v}"' for k, v in attrs.items())
        out.append(f'\t<remove xpath="/progression/crafting_skills/crafting_skill[@name=\'{xpath_escape(skill_name)}\']"/>')
        out.append(f'\t<append xpath="/progression/crafting_skills">')
        out.append(f'\t\t<crafting_skill {attr_str}>')
        out.extend(entries)
        out.append("\t\t<effect_group>")
        out.extend(effects)
        out.append("\t\t</effect_group>")
        out.append("\t\t</crafting_skill>")
        out.append("\t</append>")
    out.append("</configs>")
    return out


# ---------------------------------------------------------------------------
# 2. Items_WeaponTables.xml - spread weapon/tool item effect tables to 1-600
# ---------------------------------------------------------------------------
def scale_table(table, is_single=False):
    nums = [float(x) for x in table.split(",")]
    scaled = [int(round(n * 100)) for n in nums]
    if not is_single:
        if scaled[-1] < MAX_LEVEL:
            scaled[-1] = MAX_LEVEL
        # Anchor the table at quality 1. Items exist at ANY quality 1-600: crafted
        # items are quality = skill (so 1-99 at low skill), trader/quest/legacy
        # items are still on the vanilla 1-6 roll. Without a level-1 anchor those
        # items get NO passive effects - DegradationMax=0 makes MaxUseTimes=0, and
        # the game's right-click Repair action is hidden when MaxUseTimes <= 1.
        if scaled[0] > 1:
            scaled.insert(0, 1)
        return ",".join(str(x) for x in scaled)
    return f"1,{MAX_LEVEL}"


def prepend_anchor_value(pe, axis_v):
    """Value to prepend for the quality-1 anchor, matching the original tier start."""
    vals = (pe.get("value") or "").split(",")
    if not vals:
        return "0"
    op = (pe.get("operation") or "base_set").lower()
    first_tier = int(axis_v.split(",")[0])
    if first_tier > 1 and op in ("base_add", "base_subtract", "perc_add", "perc_subtract"):
        # effect originally started at tier 2+: neutral at quality 1-199
        return "0"
    # effect applied from tier 1: quality 1-99 gets the lowest tier's stats
    return vals[0].strip()


def gen_item_tables():
    root = parse_config("items.xml")
    skill_tags = all_skill_tags()
    out = ['<?xml version="1.0" encoding="UTF-8"?>\n<configs>']
    count = 0
    for item in root.iter("item"):
        tags = None
        for prop in item.iter("property"):
            if prop.get("name") == "Tags":
                tags = prop.get("value", "")
                break
        if tags is None:
            continue
        tagset = set(tags.split(","))
        if not tagset.intersection(skill_tags):
            continue
        item_name = item.get("name")
        axes = {}
        for pe in item.iter("passive_effect"):
            for axis in ("level", "tier", "duration"):
                v = pe.get(axis)
                if v is None:
                    continue
                if not re.fullmatch(r"[0-9]+(\s*,\s*[0-9]+)*", v):
                    continue
                name = pe.get("name")
                axes.setdefault((name, axis, v), []).append(pe)
        for (name, axis, v), pes in axes.items():
            is_single = len(v.split(",")) == 1
            if name == "ModSlots" and not is_single:
                # Mod slots: +1 slot per 100 quality levels, starting from the item's
                # vanilla tier-1 count, never above the vanilla max (pipe baton 1 ->
                # 3, stun baton starts at 2 -> 4). ItemValue.CalcModSlotCount evaluates
                # the effect at level = quality and truncates the result to int, so
                # flat steps anchored at 100/200/... land exactly on the boundary:
                # quality 200 = vanilla tier-2 slots, 300 = tier-3 slots, etc.
                vals = [int(float(x)) for x in (pes[0].get("value") or "0").split(",")]
                base_slots = vals[0]
                max_slots = max(vals)
                newvals = ",".join(str(min(base_slots + i, max_slots)) for i in range(6))
                newvals = str(base_slots) + "," + newvals  # 7 values for the 7 anchors
                newlevels = "1,100,200,300,400,500,600"
                for i, pe in enumerate(pes):
                    idx = "" if len(pes) == 1 else f"[{i + 1}]"
                    bxp = (f"/items/item[@name='{xpath_escape(item_name)}']"
                           f"//passive_effect[@name='ModSlots']")
                    # value rule FIRST: xpath rules apply sequentially, and the tier
                    # rule below changes the axis attribute the value rule matches on
                    xpv = f"{bxp}[@{axis}='{xpath_escape(v)}']{idx}/@value"
                    out.append(f'\t<set xpath="{xpv}">{newvals}</set>')
                    count += 1
                    xp = f"{bxp}[@{axis}='{xpath_escape(v)}']{idx}/@{axis}"
                    out.append(f'\t<set xpath="{xp}">{newlevels}</set>')
                    count += 1
                continue
            newv = scale_table(v, is_single=is_single)
            extended = not is_single and newv.count(",") > v.count(",")
            for i, pe in enumerate(pes):
                idx = "" if len(pes) == 1 else f"[{i + 1}]"
                base = (f"/items/item[@name='{xpath_escape(item_name)}']"
                        f"//passive_effect[@name='{xpath_escape(name)}']")
                if extended:
                    # prepend the matching anchor value FIRST (xpath rules apply
                    # sequentially, and the tier rule below changes the axis attr)
                    newvals = prepend_anchor_value(pe, v) + "," + pe.get("value", "")
                    xpv = f"{base}[@{axis}='{xpath_escape(v)}']{idx}/@value"
                    out.append(f'\t<set xpath="{xpv}">{newvals}</set>')
                    count += 1
                xp = f"{base}[@{axis}='{xpath_escape(v)}']{idx}/@{axis}"
                out.append(f'\t<set xpath="{xp}">{newv}</set>')
                count += 1
    out.append("</configs>")
    print(f"  item table rules: {count}")
    return out


# ---------------------------------------------------------------------------
# 3. Perks.xml - rescale CSM LootProb requirements to the new skill max
# ---------------------------------------------------------------------------
def gen_perks():
    root = parse_config("progression.xml")
    out = ['<?xml version="1.0" encoding="UTF-8"?>\n<configs>']
    count = 0
    for pe in root.iter("passive_effect"):
        if pe.get("name") != "LootProb":
            continue
        tags = pe.get("tags", "")
        if "CSM" not in tags:
            continue
        for req in pe.iter("requirement"):
            pn = req.get("progression_name")
            if pn in SKILLS:
                try:
                    old_val = int(req.get("value", ""))
                except ValueError:
                    continue
                _, orig_max, _, _, _ = SKILLS[pn]
                new_val = max(1, round(old_val * MAX_LEVEL / orig_max))
                old = req.get("operation", "LT")
                xp = (f"//passive_effect[@name='LootProb'][@tags='{xpath_escape(tags)}']"
                      f"//requirement[@progression_name='{pn}'][@operation='{old}']/@value")
                out.append(f'\t<set xpath="{xp}">{new_val}</set>')
                count += 1
    out.append("</configs>")
    print(f"  perk CSM rules: {count}")
    return out


# ---------------------------------------------------------------------------
# 4. Items_Magazines.xml - magazines grant study buff instead of levels
# ---------------------------------------------------------------------------
def gen_magazines():
    out = ['<?xml version="1.0" encoding="UTF-8"?>\n<configs>']
    for skill_name, (tags, orig_max, magazine, buff, display) in SKILLS.items():
        out.append(f'\t<remove xpath="/items/item[@name=\'{magazine}\']//triggered_effect[@action=\'AddProgressionLevel\']"/>')
        out.append(f'\t<append xpath="/items/item[@name=\'{magazine}\']/effect_group[1]">')
        out.append(f'\t\t<triggered_effect trigger="onSelfPrimaryActionEnd" action="AddBuff" buff="{buff}"/>')
        out.append("\t</append>")
    out.append("</configs>")
    return out


# ---------------------------------------------------------------------------
# 5. Buffs.xml - the study buffs
# ---------------------------------------------------------------------------
def gen_buffs():
    # Buff icons must be ui_game_symbol_* sprites (the buff UI cannot use item icons),
    # so reuse the icon of the matching crafting_skill from progression.xml.
    root = parse_config("progression.xml")
    icons = {}
    for cs in root.iter("crafting_skill"):
        icons[cs.get("name")] = cs.get("icon")
    duration = study_buff_duration_seconds()
    out = ['<?xml version="1.0" encoding="UTF-8"?>\n<configs>']
    for skill_name, (tags, orig_max, magazine, buff, display) in SKILLS.items():
        icon = icons.get(skill_name) or "ui_game_symbol_crafting"
        out.append('\t<append xpath="/buffs">')
        out.append(f'\t\t<buff name="{buff}" name_key="{buff}Name" description_key="{buff}Desc" icon="{icon}" duration="{duration}" display_in_hud="true" icon_color="255,255,255">')
        out.append("\t\t\t<effect_group/>")
        out.append("\t\t</buff>")
        out.append("\t</append>")
    out.append("</configs>")
    return out


# ---------------------------------------------------------------------------
# 6. Localization.csv - buff names + descriptions
# ---------------------------------------------------------------------------
def gen_localization():
    # The game only loads mod localization from <mod>/Config/Localization.csv and its
    # patch parser rejects any header column that is not pure latin letters (or starts
    # with "context"), so use the exact vanilla header. English text only; the patch
    # loader skips empty cells and maps columns by name.
    header = ("Key,File,Type,UsedInMainMenu,NoTranslate,KeepLoaded,english,Context / Alternate Text,"
              "german,spanish,french,italian,japanese,koreana,polish,brazilian,russian,turkish,schinese,tchinese")
    n_cols = len(header.split(","))
    english_idx = header.split(",").index("english")
    minutes = max(1, round(study_buff_duration_seconds() / 60))
    lines = [header]
    for skill_name, (tags, orig_max, magazine, buff, display) in SKILLS.items():
        name = f"Focused Study: {display}"
        desc = f"Your study of {display.lower()} sharpens your focus. Weapon skill gain from use is doubled for {minutes} minutes."
        # override the vanilla magazine tooltip ("Improves X crafting skill" is stale —
        # magazines grant the study buff now, not skill levels)
        mag_desc = f"Reading this magazine grants Focused Study: {display} — a temporary buff that doubles weapon skill gain from use for {minutes} minutes."
        for key, text, ftype, ffile in (
            (f"{buff}Name", name, "Buff", "ui"),
            (f"{buff}Desc", desc, "Buff", "ui"),
            (f"{magazine}Desc", mag_desc, "Gun", "items"),
        ):
            cols = [""] * n_cols
            cols[0] = key
            cols[1] = ffile
            cols[2] = ftype
            cols[english_idx] = text
            lines.append(",".join(cols))
    with open(os.path.join(OUT, "Localization.csv"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")


def write(name, content):
    path = os.path.join(OUT, name)
    with open(path, "w", encoding="utf-8") as f:
        f.write(content)
    print(f"  wrote {name}")


def merge(target, parts):
    body = []
    for p in parts:
        body.extend(p[1:-1])  # drop [0] (xml decl + <configs>) and last (</configs>)
    content = '<?xml version="1.0" encoding="UTF-8"?>\n<configs>\n' + "\n".join(body) + "\n</configs>\n"
    write(target, content)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    merge("progression.xml", [gen_progression(), gen_perks()])
    merge("items.xml", [gen_item_tables(), gen_magazines()])
    merge("buffs.xml", [gen_buffs()])
    gen_localization()
    print("done")
