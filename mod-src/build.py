#!/usr/bin/env python3
"""AfterHours — master build script (single source of truth for Mods/).

mod-src is the canonical source for every mod in the live Mods/ tree. This
script builds, deploys, packages and verifies the whole tree:

    python3 mod-src/build.py                 build + deploy ALL mods, then pack
    python3 mod-src/build.py <mod>           build + deploy one mod (folder name,
                                             e.g. DS_WeaponMastery, ForgeThreeInputSlots)
    python3 mod-src/build.py pack            only regenerate AfterHours_ClientMods.zip
                                             + mods.json (from the live Mods/ tree)
    python3 mod-src/build.py verify          diff-check deployed Mods/ vs mod-src/

Per-mod build.sh files are thin wrappers that `exec` this script, so the logic
lives here and nowhere else. Order matters: TFP_Harmony (build dependency)
first, KYAU_Dashboard last.

After a build of server-side code, restart the game server (telnet `shutdown`,
`systemctl --user start 7dtd.service`). Website changes need no restart.

Prerequisites (checked up front by the build): dotnet SDK (DOTNET var below),
rsync, python3 with the `lxml` module (used by the XML verifier tools).
Arch:   sudo pacman -S dotnet-sdk-8.0 rsync python-lxml
Debian: sudo apt install dotnet-sdk-8.0 rsync python3-lxml
"""
import datetime
import hashlib
import importlib.util
import json
import os
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

SRC = os.path.dirname(os.path.abspath(__file__))
SERVER_ROOT = os.environ.get("SEVENDAYS_ROOT", "/srv/7days")
MODS_DIR = os.path.join(SERVER_ROOT, "Mods")
DOTNET = "/bin/dotnet"
WEBROOT = os.path.join(MODS_DIR, "ZZ_KYAU_Dashboard", "webroot")
ZIP_PATH = os.path.join(WEBROOT, "downloads", "AfterHours_ClientMods.zip")
JSON_PATH = os.path.join(WEBROOT, "assets", "mods.json")

# --- client modpack configuration -------------------------------------------

# Mods players must install client-side (zipped into the modpack).
CLIENT_MODS = [
    "0_DS_BiggerInventory",
    "1_DS_LogSpikes",
    "1_DS_VehicleCruiseControl",
    "1_DS_WaterDouse",
    "1_DS_WeaponMastery",
    "1_DS_Zipline",
    "1_ForgeThreeInputSlots",
]

# Mods that are part of the custom AfterHours experience (highlighted on the site).
HIGHLIGHT_MODS = {
    "0_DS_BiggerInventory",
    "1_DS_LogSpikes",
    "1_DS_WaterDouse",
    "1_DS_WeaponMastery",
    "1_DS_VehicleCruiseControl",
    "1_DS_Zipline",
    "1_ForgeThreeInputSlots",
    "1_VanillaPlus",
    "2_KYAU_AfterHoursApi",
    "ZZ_KYAU_Dashboard",
}

# Mods that are pure infrastructure and not interesting to list publicly.
HIDDEN_MODS = {"0_TFP_Harmony"}

# Client/server split mods: the live Mods/ folder holds the SERVER build; the
# modpack must ship the CLIENT build staged by the build steps below. Shipping
# the server DLL to clients loses the client-side patches (crafting/quality
# display, context-menu hooks).
CLIENT_PACK_OVERRIDES = {
    "1_DS_WeaponMastery": os.path.join(SRC, "DS_WeaponMastery", "client"),
    "1_DS_WaterDouse": os.path.join(SRC, "DS_WaterDouse", "client"),
    "1_DS_Zipline": os.path.join(SRC, "DS_Zipline", "client"),
}

# Server runtime-state files that must never ship in the client modpack.
EXCLUDE_FILES = {"DSResetDone.txt"}

# --- mod registry ------------------------------------------------------------
# (mod-src dir, deployed folder, verify staging subdir or None,
#  repackage client pack after single-mod build?, build steps)
# Steps run with cwd = mod-src/<dir>; {MODS} and {DOTNET} are substituted.
MODS = [
    ("TFP_Harmony", "0_TFP_Harmony", "mod", False, [
        "rm -rf {MODS}/0_TFP_Harmony",
        "cp -r mod {MODS}/0_TFP_Harmony",
    ]),
    ("DS_BiggerInventory", "0_DS_BiggerInventory", "mod", True, [
        "{DOTNET} build src/BiggerInventory.csproj -c Release -v q --nologo",
        "cp src/bin/Release/BiggerInventory.dll mod/",
        "cp {MODS}/0_TFP_Harmony/0Harmony.dll mod/",
        "rm -rf {MODS}/0_DS_BiggerInventory",
        "cp -r mod {MODS}/0_DS_BiggerInventory",
    ]),
    ("DS_LogSpikes", "1_DS_LogSpikes", "mod", True, [
        "python3 tools/generate_xml.py",
        "python3 tools/verify_xml.py",
        "cp ModInfo.xml mod/",
        "cp README.md mod/README_DS_LogSpikes.txt",
        "rm -rf {MODS}/1_DS_LogSpikes",
        "cp -r mod {MODS}/1_DS_LogSpikes",
    ]),
    ("ForgeThreeInputSlots", "1_ForgeThreeInputSlots", "mod", True, [
        "rm -rf {MODS}/1_ForgeThreeInputSlots",
        "cp -r mod {MODS}/1_ForgeThreeInputSlots",
    ]),
    ("VanillaPlus", "1_VanillaPlus", "mod", True, [
        "rm -rf mod && mkdir -p mod",
        "cd tools/patch_tmo_dlls && {DOTNET} run -c Release -- {SRC}",
        "cd src/CrateGuards && {DOTNET} build -c Release -v q --nologo",
        "cp src/CrateGuards/bin/Release/VanillaPlus.dll mod/",
        "cp -r src/Config mod/Config",
        "cp src/NoZombieDiggingConfig.xml mod/",
        "cp src/SupplyManager.xml mod/",
        "cp src/CrateGuardConfig.xml mod/",
        "cp ModInfo.xml mod/",
        "cp README.md mod/README_VanillaPlus.txt",
        "python3 tools/verify_xml.py",
        "rm -rf {MODS}/1_VanillaPlus",
        "cp -r mod {MODS}/1_VanillaPlus",
    ]),
    ("DS_VehicleCruiseControl", "1_DS_VehicleCruiseControl", "mod", True, [
        "{DOTNET} build src/WMMVehicleCruiseControl.csproj -c Release -v q --nologo",
        "cp src/bin/Release/WMMVehicleCruiseControl.dll mod/",
        "cp ModInfo.xml mod/",
        "cp README.md mod/README_DS_VehicleCruiseControl.txt",
        "rm -rf {MODS}/1_DS_VehicleCruiseControl",
        "cp -r mod {MODS}/1_DS_VehicleCruiseControl",
    ]),
    ("DS_WaterDouse", "1_DS_WaterDouse", "server", True, [
        "{DOTNET} build src/DouseServer/DouseServer.csproj -c Release -v q --nologo",
        "{DOTNET} build src/DouseClient/DouseClient.csproj -c Release -v q --nologo",
        "cp src/DouseServer/bin/Release/Douse.dll server/",
        "cp {MODS}/0_TFP_Harmony/0Harmony.dll server/",
        # Client pack staging (ships as Douse.dll under the same folder name)
        "rm -rf client && mkdir -p client",
        "cp src/DouseClient/bin/Release/DouseClient.dll client/Douse.dll",
        "cp {MODS}/0_TFP_Harmony/0Harmony.dll client/",
        "cp server/DouseConfig.xml client/",
        "cp server/ModInfo.xml client/",
        "cp -r server/Config client/Config",
        "cp README.md client/README_WaterDouse_Client.txt",
        "rm -rf {MODS}/1_DS_WaterDouse",
        "cp -r server {MODS}/1_DS_WaterDouse",
    ]),
    ("DS_Zipline", "1_DS_Zipline", "server", True, [
        "python3 tools/verify_assets.py",
        "rm -rf server/Resources",
        "python3 tools/verify_xml.py",
        "{DOTNET} build src/DSZiplineServer/DSZiplineServer.csproj -c Release -v q --nologo",
        "{DOTNET} build src/DSZiplineClient/DSZiplineClient.csproj -c Release -v q --nologo",
        "cp src/DSZiplineServer/bin/Release/DSZipline.dll server/",
        "cp {MODS}/0_TFP_Harmony/0Harmony.dll server/",
        "cp art/ATTRIBUTION.md server/ATTRIBUTION_DS_Zipline.md",
        # Client pack staging (ships client build under the same DLL name)
        "rm -rf client && mkdir -p client",
        "cp src/DSZiplineClient/bin/Release/DSZipline.dll client/DSZipline.dll",
        "cp {MODS}/0_TFP_Harmony/0Harmony.dll client/",
        "cp server/ModInfo.xml client/",
        "cp -r server/Config client/Config",
        "cp -r server/UIAtlases client/UIAtlases",
        "mkdir -p client/Resources/tool",
        "cp art/generated/dszipline.meshbin client/Resources/",
        "cp art/generated/tool/* client/Resources/tool/",
        "cp README.md client/README_DS_Zipline.txt",
        "cp art/ATTRIBUTION.md client/ATTRIBUTION_DS_Zipline.md",
        "rm -rf {MODS}/1_DS_Zipline",
        "cp -r server {MODS}/1_DS_Zipline",
    ]),
    ("DS_WeaponMastery", "1_DS_WeaponMastery", "server", True, [
        "python3 tools/generate_xml.py",
        "{DOTNET} build src/WeaponMasteryServer/WeaponMasteryServer.csproj -c Release -v q --nologo",
        "{DOTNET} build src/WeaponMasteryClient/WeaponMasteryClient.csproj -c Release -v q --nologo",
        "cp src/WeaponMasteryServer/bin/Release/WeaponMastery.dll server/",
        "cp {MODS}/0_TFP_Harmony/0Harmony.dll server/",
        # Client pack staging (ships as WeaponMastery.dll under the same folder name)
        "rm -rf client && mkdir -p client",
        "cp src/WeaponMasteryClient/bin/Release/WeaponMasteryClient.dll client/WeaponMastery.dll",
        "cp {MODS}/0_TFP_Harmony/0Harmony.dll client/",
        "cp server/DSConfig.xml client/",
        "cp server/ModInfo.xml client/",
        "cp -r server/Config client/Config",
        "cp README.md client/README_WeaponMastery_Client.txt",
        # preserve runtime state (one-time skill reset tracker) across redeploys
        # (`|| true`: no state file on a fresh server — that's fine)
        "[ -f {MODS}/1_DS_WeaponMastery/DSResetDone.txt ] && cp {MODS}/1_DS_WeaponMastery/DSResetDone.txt /tmp/DSResetDone.txt.bak || true",
        "rm -rf {MODS}/1_DS_WeaponMastery",
        "cp -r server {MODS}/1_DS_WeaponMastery",
        "[ -f /tmp/DSResetDone.txt.bak ] && mv /tmp/DSResetDone.txt.bak {MODS}/1_DS_WeaponMastery/DSResetDone.txt || true",
    ]),
    ("KYAU_AfterHoursApi", "2_KYAU_AfterHoursApi", None, False, [
        "{DOTNET} build src/AfterHoursApi.csproj -c Release -v q --nologo",
        "mkdir -p {MODS}/2_KYAU_AfterHoursApi",
        "cp src/bin/Release/AfterHoursApi.dll {MODS}/2_KYAU_AfterHoursApi/",
        "cp ModInfo.xml {MODS}/2_KYAU_AfterHoursApi/",
    ]),
    ("TFP_CommandExtensions", "TFP_CommandExtensions", "mod", False, [
        "rm -rf {MODS}/TFP_CommandExtensions",
        "cp -r mod {MODS}/TFP_CommandExtensions",
    ]),
    ("Xample_MarkersMod", "Xample_MarkersMod", "mod", False, [
        "rm -rf {MODS}/Xample_MarkersMod",
        "cp -r mod {MODS}/Xample_MarkersMod",
    ]),
    ("KYAU_Dashboard", "ZZ_KYAU_Dashboard", "webroot", True, [
        "mkdir -p {MODS}/ZZ_KYAU_Dashboard",
        "rsync -a --delete --exclude='downloads/' --exclude='assets/mods.json' webroot/ {MODS}/ZZ_KYAU_Dashboard/webroot/",
        "cp ModInfo.xml {MODS}/ZZ_KYAU_Dashboard/",
    ]),
]


# --- packaging (AfterHours_ClientMods.zip + mods.json) ------------------------

def read_modinfo(path):
    tree = ET.parse(path)
    data = {}
    for prop in tree.getroot():
        data[prop.tag.lower()] = prop.get("value", "")
    return data


def pack_info(zip_path):
    """Content-addressed modpack version: short sha1 of the zip bytes.

    Same content -> same version (no spurious 'update' notices). The 'built'
    timestamp is carried over from the previous mods.json when the hash is
    unchanged, so it reflects when the pack CONTENT last changed.
    """
    with open(zip_path, "rb") as f:
        digest = hashlib.sha1(f.read()).hexdigest()
    version = digest[:8]
    built = None
    if os.path.isfile(JSON_PATH):
        try:
            with open(JSON_PATH) as f:
                old = json.load(f)
            if old.get("pack", {}).get("version") == version:
                built = old["pack"].get("built")
        except Exception:
            pass
    if not built:
        built = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    return {"version": version, "built": built}


def build_zip():
    os.makedirs(os.path.dirname(ZIP_PATH), exist_ok=True)
    if os.path.exists(ZIP_PATH):
        os.remove(ZIP_PATH)
    with zipfile.ZipFile(ZIP_PATH, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for mod in CLIENT_MODS:
            src = CLIENT_PACK_OVERRIDES.get(mod, os.path.join(MODS_DIR, mod))
            if not os.path.isdir(src):
                print(f"WARNING: client mod missing: {mod} ({src})")
                continue
            for root, _dirs, files in os.walk(src):
                for fn in sorted(files):
                    if fn in EXCLUDE_FILES:
                        continue
                    full = os.path.join(root, fn)
                    rel = os.path.relpath(full, src)
                    # Deterministic zip: fixed entry timestamps so the pack hash
                    # is truly content-addressed (same content, same version) —
                    # mtime-embedded zips re-hash on every identical redeploy.
                    info = zipfile.ZipInfo(os.path.join("Mods", mod, rel), (1980, 1, 1, 0, 0, 0))
                    info.compress_type = zipfile.ZIP_DEFLATED
                    info.file_size = os.path.getsize(full)
                    with open(full, "rb") as f:
                        z.writestr(info, f.read())
    size = os.path.getsize(ZIP_PATH)
    print(f"wrote {ZIP_PATH} ({size/1024:.0f} KiB)")
    return pack_info(ZIP_PATH)


def pack():
    """Regenerate mods.json + the client modpack zip from the live Mods/ tree."""
    mods = []
    for folder in sorted(os.listdir(MODS_DIR)):
        moddir = os.path.join(MODS_DIR, folder)
        info_path = os.path.join(moddir, "ModInfo.xml")
        if not os.path.isdir(moddir) or not os.path.isfile(info_path):
            continue
        if folder in HIDDEN_MODS:
            continue
        info = read_modinfo(info_path)
        version = info.get("version", "")
        if version in ("1.0.0.0", "0.0.0.0"):
            version = ""  # TFP default placeholder, meaningless to players
        mods.append({
            "id": folder,
            "name": info.get("displayname", folder),
            "description": info.get("description", ""),
            "author": info.get("author", ""),
            "version": version,
            "website": info.get("website", ""),
            "client": folder in CLIENT_MODS,
            "highlight": folder in HIGHLIGHT_MODS,
        })
    p = build_zip()  # pack first so mods.json can carry the pack version
    os.makedirs(os.path.dirname(JSON_PATH), exist_ok=True)
    with open(JSON_PATH, "w") as f:
        json.dump({"pack": p, "mods": mods}, f, indent=2)
    print(f"wrote {JSON_PATH} ({len(mods)} mods, pack v{p['version']} built {p['built']})")


# --- build / verify ----------------------------------------------------------

def run_steps(name, steps):
    moddir = os.path.join(SRC, name)
    print(f"\n================ building {name} ================")
    for step in steps:
        cmd = step.format(MODS=MODS_DIR, DOTNET=DOTNET, SRC=moddir)
        print(f"  $ {cmd}")
        subprocess.run(cmd, shell=True, cwd=moddir, check=True)


def verify():
    """Diff-check every deployed mod against its mod-src staging dir."""
    ok = True
    for name, deployed, staging, _pack, _steps in MODS:
        if staging is None:
            # binary-only deploy; check the artifacts exist
            dll = os.path.join(MODS_DIR, deployed, "AfterHoursApi.dll")
            info = os.path.join(MODS_DIR, deployed, "ModInfo.xml")
            state = "ok" if os.path.isfile(dll) and os.path.isfile(info) else "DIFFERS"
            print(f"{state:8} {name}")
            ok = ok and state == "ok"
            continue
        src = os.path.join(SRC, name, staging)
        dst = os.path.join(MODS_DIR, deployed, staging) if name == "KYAU_Dashboard" else os.path.join(MODS_DIR, deployed)
        ex = ["-x", "DSResetDone.txt", "-x", "downloads", "-x", "mods.json"]
        r = subprocess.run(["diff", "-r", *ex, src, dst], capture_output=True, text=True)
        if r.returncode == 0:
            print(f"ok:      {name}")
        else:
            ok = False
            print(f"DIFFERS: {name}")
            print("".join(r.stdout.splitlines(True)[:10]))
    if ok:
        print("\nverify: Mods/ is fully reproducible from mod-src/")
    else:
        print("\nverify: differences found (see above)")
    return ok


def preflight():
    """Fail fast with an actionable message when build prerequisites are missing."""
    missing = []
    # dotnet: DOTNET is a command or an absolute path
    if "/" in DOTNET:
        if not os.path.isfile(DOTNET):
            missing.append(f"dotnet SDK (DOTNET={DOTNET} not found)")
    elif shutil.which(DOTNET) is None:
        missing.append(f"dotnet SDK (no '{DOTNET}' on PATH)")
    if shutil.which("rsync") is None:
        missing.append("rsync (used by the KYAU_Dashboard deploy step)")
    if importlib.util.find_spec("lxml") is None:
        missing.append("python module 'lxml' (used by the XML verifier tools)")
    if missing:
        print("ERROR: missing build prerequisites:", file=sys.stderr)
        for m in missing:
            print(f"  - {m}", file=sys.stderr)
        print(file=sys.stderr)
        print("  Arch:   sudo pacman -S dotnet-sdk-8.0 rsync python-lxml", file=sys.stderr)
        print("  Debian: sudo apt install dotnet-sdk-8.0 rsync python3-lxml", file=sys.stderr)
        print(file=sys.stderr)
        print("  Then set DOTNET at the top of this file if the SDK is not on PATH.", file=sys.stderr)
        sys.exit(1)


def main():
    args = sys.argv[1:]
    if len(args) > 1:
        print(__doc__)
        sys.exit(1)
    cmd = args[0] if args else "all"

    # pack/verify are pure-python/diff; only builds need the toolchain
    if cmd not in ("pack", "verify"):
        preflight()


    if cmd == "all":
        for name, _d, _s, _p, steps in MODS:
            run_steps(name, steps)
        print("\n======== full build done — Mods/ regenerated from mod-src ========")
        pack()
    elif cmd == "pack":
        pack()
    elif cmd == "verify":
        sys.exit(0 if verify() else 1)
    else:
        for name, deployed, _s, repack, steps in MODS:
            if cmd in (name, deployed):
                run_steps(name, steps)
                if repack:
                    print("\n== refreshing dashboard client modpack ==")
                    pack()
                return
        print(f"ERROR: unknown mod '{cmd}'", file=sys.stderr)
        print(f"Known mods: {', '.join(m[0] for m in MODS)}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
