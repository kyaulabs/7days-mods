# DS Log Spikes — the classic Log Spike traps are back

The original 7 Days to Die Log Spike trap line (removed from the game) returns with
the classic **six-tier upgrade path all the way to Steel**, using the original
sharpened-log **cone** look: a 1m cone built from the built-in `cone1m` shape with a
distinct texture per tier, and the `shapeCone1m` icon tinted per tier — no custom
assets needed.

| Tier | Block | Damage | HP | Repair | Upgrade cost | Craft (in hand) |
|------|-------|-------:|---:|--------|--------------|-----------------|
| 1 | Wooden Log Spike | 40 | 150 | 10 Wood | → 2: 10 Wood | 30 Wood |
| 2 | Reinforced Wood Log Spike | 45 | 200 | 10 Wood + 1 Forged Iron | → 3: 15 Wood | 45 Wood |
| 3 | Reinforced Metal Wood Log Spike | 50 | 300 | 10 Wood + 2 Forged Iron | → 4: 3 Forged Iron | 30 Wood + 2 Forged Iron |
| 4 | Scrap Iron Log Spike | 55 | 400 | 3 Forged Iron | → 5: 5 Forged Iron | 6 Forged Iron |
| 5 | Reinforced Scrap Iron Log Spike | 62 | 500 | 4 Forged Iron | → 6: 4 Forged Steel | 8 Forged Iron |
| 6 | Steel Log Spike | 75 | 750 | 2 Forged Steel | — (max tier) | 5 Forged Steel |

## Gameplay notes
- **Place** with the secondary action, like vanilla spikes.
- **Upgrade in place** with the upgrade tool (4 hits per step).
- **Downgrade on destruction**: like the classic log spikes, a destroyed spike
  reverts to the previous tier instead of vanishing (tiers 2–6), so a wrecked
  horde line leaves recoverable salvage behind.
- **Repair** with the tier's materials (see table).
- Every tier is **hand-craftable** (backpack crafting, no schematic needed) and
  visible in the creative menu.
- Steel tier is the final upgrade: 75 damage per hit, 750 HP.
- Tiers 3+ use the trap iron material (metal damage category), steel uses the
  steel material; the tier 1–2 wood tiers are burnable (FuelValue 300).

## Install
- **Server:** `Mods/1_DS_LogSpikes/` (deployed by `build.sh`).
- **Clients:** the mod is included in the AfterHours client modpack
  (`AfterHours_ClientMods.zip`), built by
  `mod-src/build.py`. It must be installed client-side —
  block definitions are client data.

## Assets
- Model: `@:Shapes/cone1m.fbx` (built-in cone shape, same one the classic log
  spikes were built from)
- Textures: 21/22 (wood) → 379 (reinforced wood) → 380 (wood+metal) → 307 (scrap
  iron) → 352 (reinforced scrap iron) → 356/355 (steel) — the original log-spike
  texture family, still present in the game's texture atlas
- Icon: `shapeCone1m` + per-tier `CustomIconTint` (brown → copper → metal →
  steel silver)
- All display names + descriptions ship in `Config/Localization.csv`

## Rebuild / deploy
```bash
mod-src/DS_LogSpikes/build.sh   # generate + verify + deploy + refresh dashboard
```

## Files
- `tools/generate_xml.py` — tier table → `mod/Config/{blocks,recipes}.xml` + `Localization.csv`
- `tools/verify_xml.py` — simulates patch application against `Data/Config` and
  validates every cross-reference (upgrade/downgrade targets, recipes,
  ingredients, models, textures, materials, icons, localization)
- `mod/` — generated modlet (the deploy source)
