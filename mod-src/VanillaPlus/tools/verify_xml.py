#!/usr/bin/env python3
"""Vanilla+ - modlet verifier.

Simulates applying the Config patches to the vanilla game data and verifies
every xpath op matches (no silent no-ops). Also checks the deployed TMO DLLs
are the patched (standalone) builds and the DLL config files are present.
"""
import os
import sys
from lxml import etree

CFG = "/srv/7days/Data/Config"
MOD = "/srv/7days/mod-src/VanillaPlus/mod"


def load(name):
    return etree.parse(f"{CFG}/{name}").getroot()


def apply_patch(target, patchfile, label):
    patch = etree.parse(patchfile).getroot()
    total = 0
    no_match = []
    for el in patch:
        tag = el.tag
        xp = el.get("xpath")
        if xp is None:
            continue
        nodes = target.xpath(xp)
        total += 1
        if not nodes:
            no_match.append(f"{tag.upper()} no match: {xp}")
            continue
        if tag == "append":
            for n in nodes:
                for child in el:
                    n.append(etree.fromstring(etree.tostring(child)))
        elif tag == "prepend":
            for n in nodes:
                for i, child in enumerate(el):
                    n.insert(i, etree.fromstring(etree.tostring(child)))
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
        elif tag == "removeattribute":
            # lxml xpath on @attr returns plain strings; split the xpath into
            # element path + attribute name and pop the attribute instead.
            el_xp, _, attr = xp.rpartition("/@")
            if not attr:
                no_match.append(f"removeattribute bad xpath: {xp}")
                continue
            el_nodes = target.xpath(el_xp)
            if not el_nodes:
                no_match.append(f"removeattribute no match: {xp}")
                continue
            for n in el_nodes:
                n.attrib.pop(attr, None)
    print(f"  {label}: {total} ops, {len(no_match)} no-match")
    return no_match


def main():
    fails = []

    # 1. entityclasses.xml (loot bags)
    ec = load("entityclasses.xml")
    fails += apply_patch(ec, f"{MOD}/Config/entityclasses.xml", "entityclasses.xml")
    # sanity: values landed
    def ldp(name):
        for e in ec.xpath(f"/entity_classes/entity_class[@name='{name}']"):
            for p in e.iter("property"):
                if p.get("name") == "LootDropProb":
                    return p.get("value")
        return None
    for name, expect in [("zombieTemplateMale", ".05"), ("zombieBoeFeral", ".1"),
                         ("zombieBoeRadiated", ".2"), ("zombieArleneCharged", ".15"),
                         ("zombieWightFeral", ".15"), ("zombieBoeInfernal", ".15"),
                         ("zombiePlagueSpitterRadiated", ".2"), ("zombiePlagueSpitterFeral", ".1")]:
        got = ldp(name)
        if got != expect:
            fails.append(f"entity {name}: LootDropProb={got} expected {expect}")
    for e in ec.xpath("/entity_classes/entity_class[@name='DroppedLootContainer']"):
        for p in e.iter("property"):
            if p.get("name") == "TimeStayAfterDeath" and p.get("value") != "7200":
                fails.append(f"DroppedLootContainer TimeStayAfterDeath={p.get('value')}")

    # 2. traders.xml
    tr = load("traders.xml")
    fails += apply_patch(tr, f"{MOD}/Config/traders.xml", "traders.xml")
    for ti in tr.xpath("/traders/trader_info"):
        tid = ti.get("id")
        if tid in ("1", "2", "6", "7", "8", "9"):
            if "open_time" in ti.attrib or "close_time" in ti.attrib:
                fails.append(f"trader {tid}: open/close time not removed")
            if ti.get("reset_interval") != "1":
                fails.append(f"trader {tid}: reset_interval={ti.get('reset_interval')}")

    # 3. dialogs.xml
    dl = load("dialogs.xml")
    fails += apply_patch(dl, f"{MOD}/Config/dialogs.xml", "dialogs.xml")
    start = dl.xpath("/dialogs/dialog[@id='trader']/statement[@id='start']")[0]
    if not any(r.get("id") == "resetquests" for r in start.iter("response_entry")):
        fails.append("trader start statement missing resetquests response")

    # 4. patched DLLs carry our Harmony ids (proves the patched build, not the original)
    for dll, marker in [("TheMeanOnes_ZombiesDontDig.dll", b"VanillaPlus.ZombiesCantDig"),
                        ("TheMeanOnes_AirDropsPlus.dll", b"VanillaPlus.AirDropsPlus")]:
        path = os.path.join(MOD, dll)
        if not os.path.isfile(path):
            fails.append(f"missing {dll}")
            continue
        data = open(path, "rb").read()
        # Cecil stores ldstr operands as UTF-16LE in the #US heap
        marker16 = marker.decode().encode("utf-16-le")
        if marker not in data and marker16 not in data:
            fails.append(f"{dll}: patched marker missing (original build deployed?)")
            fails.append(f"{dll}: patched marker missing (original build deployed?)")
        else:
            print(f"  {dll}: patched build OK ({len(data)} bytes)")

    # 5. DLL config files present at mod root (the DLLs read them from the mod path)
    for cfg in ("NoZombieDiggingConfig.xml", "SupplyManager.xml", "CrateGuardConfig.xml"):
        if not os.path.isfile(os.path.join(MOD, cfg)):
            fails.append(f"missing {cfg}")

    # 6. VanillaPlus.dll (crate guards) present and patched marker embedded
    vp = os.path.join(MOD, "VanillaPlus.dll")
    if not os.path.isfile(vp):
        fails.append("missing VanillaPlus.dll (crate guards)")
    else:
        data = open(vp, "rb").read()
        if "VanillaPlus.CrateGuards".encode("utf-16-le") not in data:
            fails.append("VanillaPlus.dll: harmony id marker missing")
        else:
            print(f"  VanillaPlus.dll: Crate Guards build OK ({len(data)} bytes)")

    # 7. configured entity group must exist in vanilla entitygroups.xml
    eg = etree.parse(f"{CFG}/entitygroups.xml").getroot()
    group_names = set(eg.xpath("/entitygroups/entitygroup/@name"))
    from xml.etree import ElementTree as ET2
    cfg_root = ET2.parse(os.path.join(MOD, "CrateGuardConfig.xml")).getroot()
    for p in cfg_root:
        if p.get("name") == "EntityGroup":
            grp = p.get("value", "").strip()
            if grp not in group_names:
                fails.append(f"CrateGuardConfig EntityGroup '{grp}' not found in entitygroups.xml")
            else:
                print(f"  entity group '{grp}' OK")

    if fails:
        print("\n=== FAILURES ===")
        for x in fails:
            print(" ", x)
        sys.exit(1)
    print("\nALL CHECKS PASSED")


if __name__ == "__main__":
    main()
