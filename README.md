# 🧟 AfterHours 7 Days to Die Mods

[https://7days.kyaulabs.com/](https://7days.kyaulabs.com/)

[![Contributor Covenant](https://img.shields.io/badge/contributor%20covenant-2.1-4baaaa.svg?logo=open-source-initiative&logoColor=4baaaa)](CODE_OF_CONDUCT.md) &nbsp; [![Conventional Commits](https://img.shields.io/badge/conventional%20commits-1.0.0-fe5196?logo=conventionalcommits)](https://www.conventionalcommits.org/en/v1.0.0/) &nbsp; [![GitHub license](https://img.shields.io/github/license/kyaulabs/7days-mods)](LICENSE) &nbsp; [![Gitleaks](https://img.shields.io/badge/protected%20by-gitleaks-seagreen?logo=git)](https://github.com/gitleaks/gitleaks)

Source archive and build tooling for the custom mods used by the **AfterHours**
7 Days to Die server. The repository is intentionally designed to coexist with
a dedicated-server installation while tracking only mod source and selected
upstream inputs.

## Contents

- [Repository scope](#repository-scope)
- [Mods](#mods)
- [Layout](#layout)
- [Building](#building)
- [Installing](#installing)
- [Contributing](#contributing)
- [Licensing](#licensing)

## Repository scope

Tracked:

- C#, Python, shell, XML, web and configuration source under `mod-src/`
- generated XML modlets that are useful for review
- required upstream or binary-only inputs where no buildable source is available
- repository documentation, contribution templates and security checks

Not tracked:

- the live `Mods/` deployment tree
- 7 Days to Die server binaries, data files, logs, configs or service files
- saves, worlds, player data, map tiles or runtime state
- compiler output (`bin/`, `obj/`), generated DLLs, PDBs and client-pack ZIPs
- production deployment topology and credentials

The root `.gitignore` is deny-by-default so a future game update cannot silently
add new server files to Git.

## Mods

| Source | Deployed mod | Type | Summary |
|---|---|---|---|
| `DS_BiggerInventory` | `0_DS_BiggerInventory` | Client + server | 15-slot toolbelt and 60-slot backpack with save/network support. |
| `DS_LogSpikes` | `1_DS_LogSpikes` | Client + server | Restores classic log spikes with six wood-to-steel tiers. |
| `DS_SpellMastery` | Not deployed | Design / research | Work-in-progress Dungeon Siege-inspired spell mastery design. |
| `DS_VehicleCruiseControl` | `1_DS_VehicleCruiseControl` | Client | Q-to-cycle Off/Slow/Sprint vehicle cruise control with HUD status. |
| `DS_VehicleAdaptations` | `1_DS_VehicleAdaptations` | Client + server | Regenerating static vehicles with delayed fire, explosions and chain reactions. |
| `DS_WaterDouse` | `1_DS_WaterDouse` | Client + server | Adds a water-item action that removes the 3.x scent effect. |
| `DS_WeaponMastery` | `1_DS_WeaponMastery` | Client + server | Use-based weapon/tool skills and 1–600 crafting quality. |
| `ForgeThreeInputSlots` | `1_ForgeThreeInputSlots` | Client + server | XML-only third forge input slot. |
| `VanillaPlus` | `1_VanillaPlus` | Server | No digging, improved drops/traders, air drops and crate guards. |
| `KYAU_AfterHoursApi` | `2_KYAU_AfterHoursApi` | Server | Public read-only status API for the community site. |
| `KYAU_Dashboard` | `ZZ_KYAU_Dashboard` | Web | Full webroot replacement, live map/status and modpack UI. |
| `TFP_Harmony` | `0_TFP_Harmony` | Dependency | Official Harmony wrapper and required upstream assemblies. |
| `TFP_CommandExtensions` | `TFP_CommandExtensions` | Server | Official binary-only server command extensions, with decompiled reference. |
| `Xample_MarkersMod` | `Xample_MarkersMod` | Server + web | Official example marker backend, with decompiled reference. |

Most mod directories include a README with implementation and deployment details;
XML-only or compact mods document their metadata directly alongside the source.

## Layout

```text
mod-src/
├── build.py                 # canonical build/deploy/package registry
├── <Mod>/
│   ├── src/                 # C# or other authored source, when applicable
│   ├── tools/               # generators, verifiers and reference utilities
│   ├── mod|server/          # XML/config source and deployment staging
│   ├── vendor/              # selected upstream provenance inputs
│   └── build.sh             # thin wrapper around mod-src/build.py
└── KYAU_Dashboard/webroot/  # canonical website source
```

## Building

The current build system targets a Linux dedicated-server installation rooted at
`/srv/7days` and references the game's managed assemblies from that installation.
The game files are proprietary and are **not** included here.

Prerequisites:

- a compatible 7 Days to Die dedicated server installation
- .NET SDK 8
- Python 3 with `lxml`
- `rsync`

Build and deploy every registered mod into the local server's ignored `Mods/`
tree, then regenerate the client pack:

```bash
python3 mod-src/build.py
```

Build one mod:

```bash
python3 mod-src/build.py DS_WeaponMastery
```

Other useful commands:

```bash
python3 mod-src/build.py pack
python3 mod-src/build.py verify
```

> Building writes to the local server installation. Review `mod-src/build.py`
> before running it anywhere other than the intended development server.

## Installing

Compiled releases and the client modpack are distributed through the AfterHours
website rather than committed to this source archive. Manual installation varies
by mod; consult the README inside the relevant `mod-src/<Mod>/` directory.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Changes use
[Conventional Commits](https://www.conventionalcommits.org/) and should preserve
upstream attribution. Never commit game files, server configuration, saves,
credentials or generated deployment artifacts.

## Licensing

Original KYAU Labs source is licensed under the [GNU AGPL v3](LICENSE).
Third-party code, binaries, research material, fonts and media retain their own
licenses and copyrights; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and
per-mod READMEs. 7 Days to Die and The Fun Pimps are trademarks of their
respective owners. This project is not affiliated with or endorsed by The Fun
Pimps.
