#!/usr/bin/env python3
"""DS Log Spikes - XML modlet generator.

Emits the Config modlets for the Log Spike trap line:
  * blocks.xml        - 6 blocks, one per tier (classic sharpened-log cone look)
  * recipes.xml       - 1 hand-crafting recipe per tier
  * Localization.csv  - clean display names + descriptions per tier

Design follows the classic log spikes: a single 1m cone (the sharpened log)
built from the built-in cone shape (@:Shapes/cone1m.fbx) with a distinct
texture per tier, and the shapeCone1m icon tinted per tier - no custom
assets required. Each tier is standalone (no Extends), so there is no
inheritance ambiguity.

Re-run after any balance change:

    python3 /srv/7days/mod-src/DS_LogSpikes/tools/generate_xml.py
"""
import os

OUT = "/srv/7days/mod-src/DS_LogSpikes/mod/Config"

MODEL = "@:Shapes/cone1m.fbx"
ICON = "shapeCone1m"

# ---------------------------------------------------------------------------
# Tier table.  damage/max_damage follow the vanilla trap scale (TrunkTip
# spikes deal 33 dmg; these hit harder and last longer, per tier).
# ---------------------------------------------------------------------------
TIERS = [
    dict(
        name="DS_WoodLogSpike",
        display="Wooden Log Spike",
        desc="A simple sharpened log spike. Cheap, effective, and great for early defenses.",
        tint="A0522D",                     # saddle brown
        texture="21,21,22,22,22,22",       # wood planks / wood
        damage=40, max_damage=150,
        material="MtrapSpikesWood",
        hardened=False,
        fuel=300,
        repair=[("resourceWood", 10)],
        upgrade=("DS_WoodLogSpikeReinforced", "resourceWood", 10),
        recipe=[("resourceWood", 30)],
        downgrade=None,
        drop_destroy=[("resourceWood", "2,6")],
        drop_fall="terrDestroyedWoodDebris",
        upgrade_sound="place_block_wood",
        economic=30, bundle=20,
        sort2="0000",
    ),
    dict(
        name="DS_WoodLogSpikeReinforced",
        display="Reinforced Wood Log Spike",
        desc="A sturdier wooden spike strengthened with extra supports to last longer.",
        tint="B87333",                     # copper
        texture="379",                     # reinforced wood (rWoodMaster)
        damage=45, max_damage=200,
        material="MtrapSpikesWood",
        hardened=False,
        fuel=300,
        repair=[("resourceWood", 10), ("resourceForgedIron", 1)],
        upgrade=("DS_WoodLogSpikeWoodMetal", "resourceWood", 15),
        recipe=[("resourceWood", 45)],
        downgrade="DS_WoodLogSpike",
        drop_destroy=[("resourceWood", "2,6")],
        drop_fall="terrDestroyedWoodDebris",
        upgrade_sound="place_block_wood",
        economic=0, bundle=0,
        sort2="0001",
    ),
    dict(
        name="DS_WoodLogSpikeWoodMetal",
        display="Reinforced Metal Wood Log Spike",
        desc="Wooden core wrapped in metal bracing for tougher durability and extra bite.",
        tint="8B4513",                     # saddle brown, darker
        texture="380",                     # wood + metal (classic log spike 3)
        damage=50, max_damage=300,
        material="MtrapSpikesIron",
        hardened=True,
        fuel=0,
        repair=[("resourceWood", 10), ("resourceForgedIron", 2)],
        upgrade=("DS_ScrapIronLogSpike", "resourceForgedIron", 3),
        recipe=[("resourceWood", 30), ("resourceForgedIron", 2)],
        downgrade="DS_WoodLogSpikeReinforced",
        drop_destroy=[("resourceWood", "0"), ("resourceScrapIron", "8")],
        drop_fall="scrapMetalPile",
        upgrade_sound="place_block_metal",
        economic=0, bundle=0,
        sort2="0002",
    ),
    dict(
        name="DS_ScrapIronLogSpike",
        display="Scrap Iron Log Spike",
        desc="A heavy scrap-iron spike forged from salvaged metal, ideal for mid-game traps.",
        tint="B0C4DE",                     # light steel blue
        texture="307",                     # scrap iron (scrapIronNoUpgradeMaster)
        damage=55, max_damage=400,
        material="MtrapSpikesIron",
        hardened=True,
        fuel=0,
        repair=[("resourceForgedIron", 3)],
        upgrade=("DS_ScrapIronLogSpikeReinforced", "resourceForgedIron", 5),
        recipe=[("resourceForgedIron", 6)],
        downgrade="DS_WoodLogSpikeWoodMetal",
        drop_destroy=[("resourceWood", "0"), ("resourceScrapIron", "15")],
        drop_fall="scrapMetalPile",
        upgrade_sound="place_block_metal",
        economic=0, bundle=0,
        sort2="0003",
    ),
    dict(
        name="DS_ScrapIronLogSpikeReinforced",
        display="Reinforced Scrap Iron Log Spike",
        desc="Thicker, reinforced scrap-iron construction that thrives under sustained horde pressure.",
        tint="DAA520",                     # goldenrod
        texture="352",                     # reinforced scrap iron (rScrapIronMaster)
        damage=62, max_damage=500,
        material="MtrapSpikesIron",
        hardened=True,
        fuel=0,
        repair=[("resourceForgedIron", 4)],
        upgrade=("DS_SteelLogSpike", "resourceForgedSteel", 4),
        recipe=[("resourceForgedIron", 8)],
        downgrade="DS_ScrapIronLogSpike",
        drop_destroy=[("resourceWood", "0"), ("resourceScrapIron", "15")],
        drop_fall="scrapMetalPile",
        upgrade_sound="place_block_metal",
        economic=0, bundle=0,
        sort2="0004",
    ),
    dict(
        name="DS_SteelLogSpike",
        display="Steel Log Spike",
        desc="High-grade steel spike engineered for maximum damage and extreme longevity.",
        tint="C0C0C0",                     # silver
        texture="356,355,356,356,356,356", # steel (steelMaster)
        damage=75, max_damage=750,
        material="Msteel",
        hardened=True,
        fuel=0,
        repair=[("resourceForgedSteel", 2)],
        upgrade=None,                      # final tier
        recipe=[("resourceForgedSteel", 5)],
        downgrade="DS_ScrapIronLogSpikeReinforced",
        drop_destroy=[("resourceScrapIron", "10,20")],
        drop_fall="scrapMetalPile",
        upgrade_sound="place_block_metal",
        economic=0, bundle=0,
        sort2="0005",
    ),
]

SORT_ORDER1 = "B760"
GROUP = "Tools/Traps"
FILTER_TAGS = "MC_building,SC_traps"


def prop(name, value, extra=""):
    return f'\t\t<property name="{name}" value="{value}" {extra}/>'.replace("  />", " />")


def gen_blocks():
    out = ['<?xml version="1.0" encoding="UTF-8"?>\n<configs>', '\t<append xpath="/blocks">']
    for t in TIERS:
        n = t["name"]
        out.append(f'\t\t<!-- {n} - {t["display"]} -->')
        out.append(f'\t\t<block name="{n}">')
        out.append('\t\t\t<property name="SellableToTrader" value="false"/>')
        out.append('\t\t\t<property name="CreativeMode" value="Player"/>')
        out.append(prop("CustomIcon", ICON))
        out.append(prop("CustomIconTint", t["tint"]))
        out.append('\t\t\t<property name="Class" value="TrunkTip"/>')
        out.append('\t\t\t<property name="BlockTag" value="Spike"/>')
        out.append(prop("Damage", t["damage"]))
        out.append(prop("Damage_received", "33"))
        if t["hardened"]:
            out.append('\t\t\t<property name="DisplayType" value="blockHardened"/>')
        out.append(prop("Material", t["material"]))
        out.append('\t\t\t<property name="Shape" value="New"/>')
        out.append('\t\t\t<property name="LightOpacity" value="6"/>')
        out.append('\t\t\t<property name="Path" value="solid"/>')
        out.append(prop("Model", MODEL))
        out.append(prop("Texture", t["texture"]))
        out.append('\t\t\t<property name="UseGlobalUV" value="Local"/>')
        if t["fuel"]:
            out.append(prop("FuelValue", t["fuel"]))
        out.append('\t\t\t<property class="RepairItems">')
        for item, count in t["repair"]:
            out.append(f'\t\t\t\t<property name="{item}" value="{count}"/>')
        out.append("\t\t\t</property>")
        if t["upgrade"]:
            to, item, count = t["upgrade"]
            out.append('\t\t\t<property class="UpgradeBlock">')
            out.append(f'\t\t\t\t<property name="ToBlock" value="{to}"/>')
            out.append(f'\t\t\t\t<property name="Item" value="{item}"/>')
            out.append(f'\t\t\t\t<property name="ItemCount" value="{count}"/>')
            out.append('\t\t\t\t<property name="UpgradeHitCount" value="4"/>')
            out.append("\t\t\t</property>")
        out.append(prop("UpgradeSound", t["upgrade_sound"]))
        if t["downgrade"]:
            out.append(prop("DowngradeBlock", t["downgrade"]))
        for drop_name, drop_count in t["drop_destroy"]:
            out.append(f'\t\t\t<drop event="Destroy" name="{drop_name}" count="{drop_count}"/>')
        out.append(f'\t\t\t<drop event="Fall" name="{t["drop_fall"]}" count="1" prob="0.75" stick_chance="1"/>')
        out.append(prop("Group", GROUP))
        out.append(prop("EconomicValue", t["economic"]))
        if t["bundle"]:
            out.append(prop("EconomicBundleSize", t["bundle"]))
        out.append(prop("FilterTags", FILTER_TAGS))
        out.append(prop("SortOrder1", SORT_ORDER1))
        out.append(prop("SortOrder2", t["sort2"]))
        out.append("\t\t</block>")
    out.append("\t</append>")
    out.append("</configs>")
    return "\n".join(out) + "\n"


def gen_recipes():
    out = ['<?xml version="1.0" encoding="UTF-8"?>\n<configs>', '\t<append xpath="/recipes">']
    for t in TIERS:
        out.append(f'\t\t<recipe name="{t["name"]}" count="1" tags="packMuleCrafting">')
        for item, count in t["recipe"]:
            out.append(f'\t\t\t<ingredient name="{item}" count="{count}"/>')
        out.append("\t\t</recipe>")
    out.append("\t</append>")
    out.append("</configs>")
    return "\n".join(out) + "\n"


def gen_localization():
    header = ("Key,File,Type,UsedInMainMenu,NoTranslate,KeepLoaded,english,Context / Alternate Text,"
              "german,spanish,french,italian,japanese,koreana,polish,brazilian,russian,turkish,schinese,tchinese")
    n_cols = len(header.split(","))
    lines = [header]
    for t in TIERS:
        for key, text in ((t["name"], t["display"]), (f"{t['name']}Desc", t["desc"])):
            row = [""] * n_cols
            row[0] = key
            row[1] = "blocks"
            row[2] = "Trap"
            row[6] = text
            lines.append(",".join(row))
    return "\n".join(lines) + "\n"


def write(name, content):
    with open(os.path.join(OUT, name), "w", encoding="utf-8") as f:
        f.write(content)
    print(f"  wrote {name}")


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    write("blocks.xml", gen_blocks())
    write("recipes.xml", gen_recipes())
    write("Localization.csv", gen_localization())
    print(f"  {len(TIERS)} tiers, {len(TIERS)} blocks, {len(TIERS)} recipes")
    print("done")
