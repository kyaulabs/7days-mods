# Vanilla+ — four classic quality-of-life mods, merged into one

A curated bundle of four TheMeanOneMods tweaks, wrapped into a single
server-side mod. Everything is verified against this server's game version
(V 3.1.0 b14); see "Compatibility work" below.

| Feature | Source mod | How it works |
|---|---|---|
| **Zombies can't dig** | TheMeanOne's Zombies Cant Dig v2.3 | Harmony patches on `EntityMoveHelper.DigStart` / `UpdateMoveHelper`. Whitelist mode: empty allow list = no zombie digs. |
| **Long lasting loot bags** | TheMeanOne's Long Lasting Loot Bags N More v2.2 | XML patch: zeds drop loot bags more often (feral 10%, radiated 20%, charged/infernal 15%, wights 15%) and bags persist **2 hours** (was 1). |
| **Enhanced air drops** | TheMeanOne's Air Drops PLUS v2.4 | Harmony patches on `AIDirectorAirDropComponent.SpawnSupplyCrate` / `Tick`. **3 crates** per drop (vanilla 1), dropped in 5s intervals. |
| **Traders always open** | TheMeanOne's Trader Never Closes N More v4.1 | XML patch: removes open/close times for all NPC traders, restock daily (was 3 days), vending machines restock every 2 days with friendlier prices, rentable machines cost more but rent longer, and the trader dialog gains a *Reset Quests* option. |
| **Supply crate guards** | *new (Vanilla+ own DLL)* | When any alive player comes within 12 m of a landed supply crate, a horde of 5 zombies spawns around it (once per crate) — including the extra crates from Air Drops Plus. Server-side Harmony patch on `EntitySupplyCrate.Update`. |

## Compatibility work (done in this repo)

- **TMO Core requirement removed.** Both original DLLs gate their `InitMod` on
  `ModManager.GetMod("TMO Core")` — without that (unprovided) mod, Zombies Can't
  Dig silently exits and Air Drops Plus throws at init. Since neither DLL ever
  references core types, `tools/patch_tmo_dlls/` rewrites both `InitMod` bodies
  with Mono.Cecil to the standalone flow:
  `load config from mod path → Harmony.PatchAll`. Verified by decompiling the
  output and by loading + invoking the patched DLLs.
- **Patch targets verified** against this game version: `EntityMoveHelper.DigStart`,
  `EntityMoveHelper.UpdateMoveHelper`, `AIDirectorAirDropComponent.SpawnSupplyCrate`
  and `Tick` all exist.
- **Loot-bag xpaths completed.** The original only edited `LootDropProb` where a
  zombie class already had the property — most *Radiated* classes (and a few
  newer Feral/Charged/Infernal ones) inherit it and were silently skipped. The
  patch now appends the property to those classes too, so every zombie of each
  tier drops at the intended rate.
- **Trader "always open" completed.** The original left trader id 9 on its
  open/close schedule; id 9 is now included.
- `removeattribute` (traders) verified as a valid patch op in this version.
- Harmonies use the server's `0_TFP_Harmony` (0Harmony 2.13) — do not remove it.

## Config (server: `Mods/1_VanillaPlus/`)
- `NoZombieDiggingConfig.xml` — `WhitelistDigging=true` + empty `AllowList` stops
  ALL zombies from digging. Add entity names (comma separated) to allow specific
  zombies to dig.
- `SupplyManager.xml` — `ExtraDrops=2` (max 3). 0 = vanilla single crate.
- `CrateGuardConfig.xml` — supply crate guard horde:
  - `TriggerDistance` (12) — meters; any alive player closer than this triggers
    the spawn (once per crate, after it lands)
  - `ZombieCount` (5) — zombies spawned in a ring 3–8 m around the crate
  - `EntityGroup` (`ZombiesAll`) — any entity group from `entitygroups.xml`

## Install
- **Server:** `Mods/1_VanillaPlus/` (deployed by `build.sh`).
- **Clients:** nothing. All four features are server-side logic; no client modpack
  changes required.

## Rebuild / deploy
```bash
mod-src/VanillaPlus/build.sh   # patch DLLs + assemble + verify + deploy + refresh dashboard
```

## Files
- `src/original/` — untouched TheMeanOne DLLs (provenance)
- `src/Config/` — the three XML patch modlets
- `src/*.xml` — DLL runtime configs (read from the mod root at load)
- `tools/patch_tmo_dlls/` — Mono.Cecil patcher that strips the TMO Core gate
- `src/CrateGuards/` — Vanilla+ own DLL (net48, Harmony): supply crate guard horde
- `tools/verify_xml.py` — simulates every xpath op against `Data/Config` (fails
  on any silent no-op) and checks the deployed DLLs are the patched builds
- `mod/` — assembled deploy source
