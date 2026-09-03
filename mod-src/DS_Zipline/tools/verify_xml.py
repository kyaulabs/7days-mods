#!/usr/bin/env python3
"""Apply the DS_Zipline append modlets to V3.2 vanilla configs and validate references."""
from pathlib import Path
import csv
from copy import deepcopy
from lxml import etree

ROOT = Path(__file__).resolve().parents[3]
MOD = Path(__file__).resolve().parents[1] / "server" / "Config"
DATA = ROOT / "Data" / "Config"


def load(path):
    return etree.parse(str(path))


def apply_append(target, patch_path):
    patch = load(patch_path)
    failures = []
    for operation in patch.xpath("/configs/append"):
        nodes = target.xpath(operation.get("xpath"))
        if not nodes:
            failures.append(operation.get("xpath"))
            continue
        for node in nodes:
            for child in operation:
                node.append(deepcopy(child))
    return failures


def main():
    blocks = load(DATA / "blocks.xml")
    items = load(DATA / "items.xml")
    recipes = load(DATA / "recipes.xml")
    progression = load(DATA / "progression.xml")
    failures = []
    failures += apply_append(blocks, MOD / "blocks.xml")
    failures += apply_append(items, MOD / "items.xml")
    failures += apply_append(recipes, MOD / "recipes.xml")
    failures += apply_append(progression, MOD / "progression.xml")

    anchor = blocks.xpath("/blocks/block[@name='DSZiplineAnchor']")
    wood_anchor = blocks.xpath("/blocks/block[@name='DSZiplineAnchorWood']")
    tool = items.xpath("/items/item[@name='DSZiplineTool']")
    assert len(anchor) == 1, "expected one DSZiplineAnchor"
    assert len(wood_anchor) == 1, "expected one DSZiplineAnchorWood"
    assert len(tool) == 1, "expected one DSZiplineTool"
    assert tool[0].xpath("property[@name='CustomIcon'][@value='DSZiplineTool']")
    assert tool[0].xpath("property[@name='UnlockedBy'][@value='craftingSalvageTools']")
    assert not tool[0].xpath("property[@name='CustomIconTint']")
    assert anchor[0].xpath("property[@name='Class'][@value='DSZipline.ZiplineAnchor, DSZipline']")
    assert anchor[0].xpath("property[@name='RequiredPower'][@value='0']")
    assert anchor[0].xpath("property[@name='UnlockedBy'][@value='craftingElectrician']")
    assert anchor[0].xpath("property[@name='Group'][@value='Building']")
    assert anchor[0].xpath("property[@name='CustomIcon'][@value='DSZiplineAnchor']")
    assert not anchor[0].xpath("property[@name='CustomIconTint']")
    assert anchor[0].xpath(
        "property[@name='Shape'][@value='DSZipline.ZiplineAnchor, DSZipline']"
    )
    assert anchor[0].xpath("property[@name='MultiBlockDim'][@value='1,3,1']")
    assert anchor[0].xpath(
        "property[@name='Model'][@value='@:Entities/Electrical/electric_fencePrefab.prefab']"
    )
    assert wood_anchor[0].xpath(
        "property[@name='Class'][@value='DSZipline.ZiplineAnchorWood, DSZipline']"
    )
    assert wood_anchor[0].xpath(
        "property[@name='Shape'][@value='DSZipline.ZiplineAnchor, DSZipline']"
    )
    assert wood_anchor[0].xpath("property[@name='MultiBlockDim'][@value='1,3,1']")
    assert wood_anchor[0].xpath("property[@name='CustomIcon'][@value='DSZiplineWoodAnchor']")
    assert wood_anchor[0].xpath("property[@name='Group'][@value='Building']")
    assert tool[0].xpath("property[@class='Action1']/property[@name='Class'][@value='ConnectPower']")
    assert tool[0].xpath("property[@class='Action1']/property[@name='MaxWireLength'][@value='505']")

    zipline_recipes = {
        recipe.get("name"): recipe
        for recipe in recipes.xpath("/recipes/recipe[starts-with(@name,'DSZipline')]")
    }
    assert set(zipline_recipes) == {"DSZiplineAnchorWood", "DSZiplineAnchor", "DSZiplineTool"}
    wood_recipe = zipline_recipes["DSZiplineAnchorWood"]
    sonic_recipe = zipline_recipes["DSZiplineAnchor"]
    tool_recipe = zipline_recipes["DSZiplineTool"]
    assert wood_recipe.get("craft_area") is None and wood_recipe.get("tags") is None
    assert sonic_recipe.get("craft_area") == "workbench"
    assert {"learnable", "carBattery"} <= set(sonic_recipe.get("tags", "").split(","))
    assert tool_recipe.get("craft_area") is None
    assert {"learnable", "meleeToolSalvageT1Wrench"} <= set(tool_recipe.get("tags", "").split(","))

    def ingredients(recipe):
        return {node.get("name"): int(node.get("count")) for node in recipe.xpath("ingredient")}

    assert ingredients(wood_recipe) == {
        "resourceWood": 60, "resourceScrapIron": 20, "resourceLeather": 4,
    }
    assert ingredients(sonic_recipe) == {
        "DSZiplineAnchorWood": 1, "resourceForgedSteel": 20,
        "resourceMechanicalParts": 10, "resourceElectricParts": 12,
        "carBattery": 1,
    }
    assert ingredients(tool_recipe) == {
        "resourceForgedIron": 4, "resourceMechanicalParts": 2, "resourceDuctTape": 1,
    }

    electrician_t4 = progression.xpath(
        "/progression/crafting_skills/crafting_skill[@name='craftingElectrician']"
        "/display_entry[@name_key='electricianT4']"
    )
    wrench_tier = progression.xpath(
        "/progression/crafting_skills/crafting_skill[@name='craftingSalvageTools']"
        "/display_entry[@item='meleeToolSalvageT1Wrench']"
    )
    assert len(electrician_t4) == 1 and electrician_t4[0].xpath(
        "unlock_entry[@item='DSZiplineAnchor'][@unlock_tier='1']"
    ), "Sonic anchor must be listed in Electrician tier 4"
    assert len(wrench_tier) == 1 and wrench_tier[0].xpath(
        "unlock_entry[@item='DSZiplineTool'][@unlock_tier='1']"
    ), "Zipline Tool must be listed with the wrench"

    item_names = set(items.xpath("/items/item/@name")) | set(blocks.xpath("/blocks/block/@name"))
    for recipe in recipes.xpath("/recipes/recipe[starts-with(@name,'DSZipline')]"):
        assert recipe.get("name") in item_names, "unknown recipe output " + recipe.get("name")
        for ingredient in recipe.xpath("ingredient"):
            assert ingredient.get("name") in item_names, "unknown ingredient " + ingredient.get("name")

    with (MOD / "Localization.csv").open(newline="") as source:
        rows = list(csv.reader(source))
    assert rows and all(len(row) == 20 for row in rows), "Localization.csv must have 20 columns"
    keys = {row[0] for row in rows[1:]}
    required = {
        "DSZiplineAnchor", "DSZiplineTool", "DSZiplineRide", "DSZiplineNoRoute",
        "DSZiplineStarted", "DSZiplineRidePrompt", "DSZiplineWirePrompt",
        "DSZiplineLowerPrompt", "DSZiplineAnchorWood",
        "DSZiplineAnchorOnly", "DSZiplineTierMismatch", "DSZiplineRangeExceeded",
    }
    assert required <= keys, "missing localization keys: " + ", ".join(sorted(required - keys))

    if failures:
        raise SystemExit("unmatched xpath operations: " + ", ".join(failures))
    print("DS_Zipline XML checks passed")


if __name__ == "__main__":
    main()
