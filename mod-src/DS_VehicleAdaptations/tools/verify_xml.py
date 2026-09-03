#!/usr/bin/env python3
"""Apply and validate Vehicle Adaptations against the installed vanilla blocks."""
from pathlib import Path
from lxml import etree

ROOT = Path(__file__).resolve().parents[3]
MOD = Path(__file__).resolve().parents[1]


def main():
    blocks = etree.parse(str(ROOT / "Data" / "Config" / "blocks.xml"))
    patch = etree.parse(str(MOD / "Config" / "blocks.xml"))

    matched = 0
    for operation in patch.xpath("/configs/set"):
        nodes = blocks.xpath(operation.get("xpath"))
        if not nodes:
            raise SystemExit("unmatched xpath: " + operation.get("xpath"))
        matched += len(nodes)
        for node in nodes:
            node.getparent().set(node.attrname, operation.text or "")

    adapted = blocks.xpath(
        "/blocks/block[contains(property[@name='Tags']/@value,'challenge_cars')]"
        "/property[@class='CompositeFeatures']"
        "/property[@class='TEFeatureVehicleAdaptation']"
    )
    remaining = blocks.xpath(
        "/blocks/block[contains(property[@name='Tags']/@value,'challenge_cars')]"
        "/property[@class='CompositeFeatures']"
        "/property[@class='TEFeatureExplodable']"
    )
    assert matched == 19, f"expected 19 static-vehicle feature definitions, got {matched}"
    assert len(adapted) == 19, f"expected 19 adapted definitions, got {len(adapted)}"
    assert not remaining, "one or more static vehicles still define vanilla immediate explosions"

    names = {node.getparent().getparent().get("name") for node in adapted}
    required_direct = {
        "cntCar03SedanDamage0Master", "cntCar03SedanDamage1Master",
        "cntCar03SedanDamage2Master", "cntSedan01White", "cntMinivan01White",
        "cntSUV01White", "cntPickupTruck01White", "cntSemiTruck01White",
        "cntPoliceCar01PickedLockBonus", "cntPoliceCar01AlarmUnlocked",
        "cntFireTruck01White", "cntBusSchool", "forkliftWhite", "forklift2White",
    }
    missing = required_direct - names
    assert not missing, "missing static vehicle definitions: " + ", ".join(sorted(missing))

    by_name = {node.get("name"): node for node in blocks.xpath("/blocks/block")}

    def effective_explosion_feature(name, seen=None):
        seen = set() if seen is None else seen
        if not name or name in seen or name not in by_name:
            return None
        seen.add(name)
        node = by_name[name]
        classes = node.xpath("property[@class='CompositeFeatures']/property/@class")
        if "TEFeatureVehicleAdaptation" in classes:
            return "TEFeatureVehicleAdaptation"
        if "TEFeatureExplodable" in classes:
            return "TEFeatureExplodable"
        parent = node.xpath("string(property[@name='Extends']/@value)")
        return effective_explosion_feature(parent, seen)

    # These derive from cntBusSchool and prove that construction/industrial
    # vehicle families inherit the adaptation even though they do not repeat the
    # challenge_cars tag or feature XML on each color variant.
    inherited_industrial = {
        "tractorWhite", "tractorRed", "excavatorWhite", "excavatorClawWhite",
        "backhoeWhite", "forkliftRed", "forklift2Blue",
    }
    bad = {name for name in inherited_industrial
           if effective_explosion_feature(name) != "TEFeatureVehicleAdaptation"}
    assert not bad, "industrial vehicles are not adapted: " + ", ".join(sorted(bad))

    resolved = sum(effective_explosion_feature(name) == "TEFeatureVehicleAdaptation"
                   for name in by_name)
    print(f"Vehicle Adaptations XML checks passed "
          f"({matched} base definitions, {resolved} resolved variants)")


if __name__ == "__main__":
    main()
