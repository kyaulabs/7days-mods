# DS Weapon Mastery — Dungeon Siege-style weapon skills

Weapon and tool skills level by **use**. Kills with a weapon class raise its crafting
skill from 1 to 600; hitting things with a weapon also grants a small random chance of
progress. Tools (pickaxes, shovels, axes, wrenches, hammers) level from use alone.
Your skill level *is* your craft quality on the old 1-600 scale: skill 437 -> craft a
"Quality 437" weapon. Reading magazines no longer levels skills - instead it grants a
30-minute "Focused Study" buff that doubles skill gain.

## Server mod (installed)

Craft costs follow the vanilla quality curve (a quality-200 item costs like a
vanilla tier-2, quality 600 like tier 6); mod slots grow +1 per 100 quality from
the item's tier-1 count up to its vanilla max; and until the repair skill reaches
100, using any tool also earns repair-skill progress (the claw hammer unlocks at
100 and then levels repair the normal way).
`Mods/1_DS_WeaponMastery/` — XML + WeaponMastery.dll. Requires TFP_Harmony (present).

## Client mod (players must install!)
Download the AfterHours client modpack (`AfterHours_ClientMods.zip`) from the
AfterHours website download page and extract it into the game root — it is built
by `mod-src/build.py` and includes every client-side mod.
**Required** — the server uses an extended progression format; vanilla clients will
misread skill levels. Ships the same XML modlets (600-scale progression, study buffs,
spread item tables) plus the DLL for 1-600 quality display and exact-skill crafting.

## Tuning (server: `Mods/1_DS_WeaponMastery/DSConfig.xml`)
- `KillsPerLevelStart` / `KillsPerLevelMax` / `CurvePower` — XP curve.
  Defaults: ~1 kill per level early, ~200 kills for the final level, ~4500 kills to master.
- `StudyBuffMultiplier` — magazine buff strength (2 = double).
- `WeaponUseChance` — chance per weapon hit to grant kill-equivalent progress (0.05 = 5%).
- `ToolUseChance` — chance per tool use to grant progress (0.15 = 15%).
- `UseXpCooldownSeconds` — min seconds between use-grants per player+skill (1).
- `LootQualityBonusPerSkill` — looted weapon/tool quality bonus (0.5 = +300 at skill 600).
- `ResetOnFirstLogin` — one-time skill wipe per player (leave true after deploy;
  set false later to keep progress).

## Admin commands
- `ds reset` — zero all online players' weapon skills (offline players on next login)
- `ds set <player> <skill> <level>` — set a skill (testing)
- `ds xp <player> <skill> <kills>` — simulate kills
- `ds info [player]` — show skill levels

## Test server (for validation)
Validate changes on an isolated test server before production deployment. Keep
console ports and credentials in local server configuration, never in source
control.

## Known limits
- Weapon "use" XP counts successful hits (block or entity); air swings don't count.
- Turret kills credit the owner only while they are online.
- Crafting quality is set client-side (by the client mod); the server trusts it (vanilla behavior).
- Removing the mods later requires a progression reset (save format change).
