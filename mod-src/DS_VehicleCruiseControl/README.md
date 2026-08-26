# DS Vehicle Cruise Control (AfterHours)

Press **Q** while riding any vehicle to cycle cruise control **Off → Slow → Sprint**.
A HUD bar appears above the fuel bar showing the current mode — no more holding W
down on long trips.

- **Client-side mod.** Ships in `AfterHours_ClientMods.zip`; every player needs it
  installed, or they see the default HUD with no cruise control.
- **Mod folder:** `1_DS_VehicleCruiseControl` (server `Mods/` folder + client pack).
  Renamed from the old `VehicleCruiseControl` on 2026-08-10 to match the AfterHours
  `N_DS_*` mod convention. Players re-extracting the client pack must **delete the
  stale `VehicleCruiseControl` folder** from their game's `Mods` directory, or both
  versions load and double-patch the vehicle code.

## Attribution

Ported for the AfterHours server from **The Winchester** modpack by
**w00kie n00kie** (<https://github.com/w00kie-n00kie/TheWinchester>), where it ships
as `WMMVehicleCruiseControl`. The Winchester README states: *"All the modules are
open source and feel free to re-use them. Credits are nice but not needed :)"* —
credit kept anyway.

The deployed binary is the AfterHours rewrite ("recreated from the ground up") of
that original. It keeps the same assembly/class names
(`WMMVehicleCruiseControl.dll`, `XUiC_WMMCruiseControl`) so the XUi controller
binding in `mod/Config/XUi_InGame/windows.xml` keeps resolving. The pristine
upstream artifacts are preserved under `vendor/TheWinchester/` for provenance.

## Files

- `ModInfo.xml` — mod metadata (**bump `Version` here on every change**)
- `src/` — the AfterHours C# rewrite: `WMMVehicleCruiseControl.cs`,
  `XUiC_WMMCruiseControl.cs`, `WMMVehicleCruiseControl.csproj` (net48; built by
  build.sh, vendored 2026-08-11 from the original build dir /tmp/ccbuild — this
  is the exact source that produced the deployed DLL on 2026-08-02)
- `mod/` — assembled deployable → copied to `Mods/1_DS_VehicleCruiseControl`
- `mod/Config/XUi_InGame/windows.xml` — inserts the cruise-control HUD bar above the fuel bar
- `vendor/TheWinchester/` — untouched upstream artifacts from w00kie n00kie's The Winchester
- `build.sh` — builds `src/`, assembles `mod/`, deploys, refreshes the dashboard client modpack

## Building & deploying

    ./build.sh

Builds the DLL from `src/`, assembles `mod/`, deploys to
`Mods/1_DS_VehicleCruiseControl/`, then re-runs
`mod-src/build.py` (rebuilds `AfterHours_ClientMods.zip` +
`mods.json`). The pristine upstream binary lives in `vendor/TheWinchester/`.
