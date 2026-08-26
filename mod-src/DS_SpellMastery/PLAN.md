# DS_SpellMastery — Implementation Plan

Status: **approved for implementation after technical vetting** (revised 2026-08-11).
Game: 7 Days to Die **V3.1.0 b14** (Unity engine 2022.3.62f2).
Execution order: milestones M0 → M8 on this server; M9 custom art runs on the owner's laptop.

## 0. Locked implementation decisions

These decisions resolve the architecture questions found during review:

1. **Shared progression core:** add `0_DS_MasteryCore`, loaded by both DS Weapon Mastery and DS Spell Mastery on client and server. It owns the single global `ProgressionValue.Read/Write` UInt16 serializer patch. Spell Mastery has no dependency on Weapon Mastery, but both depend on the core.
2. **Books cast; toolbelt staves amplify:** the held spell book remains the casting implement. The strongest eligible staff anywhere on the player's toolbelt acts as their focus. Staff bonuses never stack.
3. **Reading consumes the book:** a successful first read consumes one book, grants school XP, and learns that exact recipe. A successful reread consumes one book and grants Focused Study.
4. **Tier-first power scaling:** spell tier sets base power. Item quality adds a configurable bonus from 0% at quality 1 to 25% at quality 600:
   `qualityFactor = 1 + MaxQualityPowerBonus × (clamp(quality,1,600) - 1) / 599`.
5. **Recipe learning uses persistent CVars:** school level gates reading and casting in C#. A recipe becomes craftable only when its recipe CVar is set by reading or by the Archmage quest. School progression effects do not independently unlock learned recipes.

---

## 1. Vision

A Dungeon Siege II–style magic system: **two spell schools that level by use**, tiered spells gated by school level, a server-authoritative mana economy with a custom HUD bar, consumable spell books as loot and learned recipes, and high-risk casting with deliberate friendly fire and self-damage.

`DS_SpellMastery` is a standalone gameplay mod patterned after `DS_WeaponMastery`: client-reported local actions, server validation and effects, guarded progression, generated XML, and XML-backed configuration. It does **not** require Weapon Mastery. Both mods use the shared infrastructure mod `0_DS_MasteryCore` and coexist in the skill window.

Development starts at `0.1.0`; every implementation change bumps all relevant `ModInfo.xml` versions. M8 promotes the finished release to `1.0.0`.

---

## 2. Locked gameplay design

### 2.1 Schools and spells

**Primal — “Life & Radiance.”** Progression name: `magicPrimal`.

| Spell | Unlock tiers | Mechanic |
|---|---|---|
| **Mend** | 1 / 100 / 200 / 300 | Instant, ~20 m line-of-sight ray. A damageable living target takes 50% of the corresponding heal amount. If no living target is hit, the caster is healed for the full amount. |
| **Regrowth** | 100 / 200 / 300 / 400 | Self HoT: 10 s, tick every 2 s, no cures. |
| **Aegis** | 200 / 300 / 400 / 500 | Self buff for 30 s: finite server-authoritative absorption pool plus armor bonus. The pool is implemented in C# damage interception; XML supplies the visible buff and armor effect. |
| **Sun Lance** | 300 / 400 / 500 / 600 | Channeled beam with server ticks and mana/second. ×1.5 against entities carrying the `zombie` tag, including infected animals; slight zombie slow. |
| **Radiance** | 400 + Archmage quest | Large self-heal, removes the configured infection and bleeding effects, then grants ~2 s damage immunity. Long cooldown and high mana cost. |

**Forbidden — “Fire & Lightning.”** Progression name: `magicForbidden`.

| Spell | Unlock tiers | Mechanic |
|---|---|---|
| **Fireball** | 1 / 100 / 200 / 300 | Server-validated impact point up to ~40 m; 4 m AoE plus 5 s burning. Caster is hit when inside the AoE. |
| **Chain Lightning** | 100 / 200 / 300 / 400 | First line-of-sight living target, then up to two nearest other living entities within ~8 m. Allies are valid; caster is intentionally excluded from arcs. Electrical damage plus short stun chance. |
| **Arc Beam** | 200 / 300 / 400 / 500 | Channeled lightning beam with server ticks and mana/second. |
| **Soulburn** | 300 / 400 / 500 / 600 | Heavy single-target line-of-sight bolt, high mana, long cooldown. |
| **Meteor** | 400 + Archmage quest | Server-validated target point; ~1.5 s warning, then 6 m AoE, heavy damage, burning and stun. Caster is hit when inside the AoE. |

For regular spell *n* (1–4), the four tier gates are based on `100 × (n−1)` with the first usable level clamped to 1. The resulting gates are the values shown above. Uber spells have one recipe and require both school level 400 and the Archmage unlock.

### 2.2 Spell power and quality

Each book item stores quality 1–600. The server reads quality from the validated held `ItemValue`; it never trusts packet-supplied quality.

```text
final magnitude = configured base
                × spell-tier multiplier
                × qualityFactor
                × active staff modifier
                × target/self/ally modifier
```

- `qualityFactor` ranges from 1.00 at quality 1 to 1.25 at quality 600 by default.
- Crafted quality is the relevant school’s exact current level, clamped 1–600. This requires Spell Mastery’s own client crafting-quality patch, scoped only to magic item tags.
- Looted books use vanilla Q1–Q6 rolls normalized to quality 100–600. Quality-1 books can still exist through admin/legacy paths and must work.
- Tier determines the major power jump; quality is a smaller continuous bonus, not a replacement for tiers.

### 2.3 Mana

- Maximum: `100 + Intellect × 10`; regeneration: `2 + Intellect × 0.5` per second. Values are configurable.
- Intellect is read server-side from the vanilla attribute. Maximum mana is recalculated and current mana clamped when the attribute changes.
- Pool, regeneration, deductions, channel costs and persistence are server-authoritative.
- Runtime mana may be fractional. Persistence uses a hidden progression entry (`magicManaCurrent`) with configurable fixed-point precision and the shared UInt16 serializer. Missing state refills to maximum.
- The server’s `SavePlayerData` rewrite touches only Spell Mastery school/mana entries. `EntityNetworkStats.ToEntity` capture/restore prevents stale client pushes from clobbering them.
- HUD: custom XUi controller and generated `Config/XUi_InGame/windows.xml`, bottom-left above health/stamina, numeric, completely hidden until `magicUnlocked=1`.
- Mana updates are sent on dirty changes and at a capped rate while regenerating/channeling.
- Mana Tonic uses a custom request: server validates the sender, inventory slot and item, consumes one tonic, returns the correct empty jar, restores mana, then relies on vanilla inventory synchronization.
- Regeneration continues while channeling by default; `RegenPauseOnChannel` can disable it.

### 2.4 Unlock quest line

```text
[Player level ≥ 10]
      │  recoverable note is delivered when eligible
      │  read note → magicNoteRead = 1
      ▼
[Rekt] “The Hidden Tome” special fetch quest
      │  rewards: magicUnlocked = 1, Mend I, Fireball I,
      │  and Primitive Staff recipe
      ▼
[max(Primal, Forbidden) ≥ 400 + Hidden Tome complete]
      │  server sets magicArchmageEligible = 1
      ▼
[Rekt] “The Archmage’s Testament” special fetch/kill quest
      │  rewards both Radiance and Meteor recipe CVars
```

- The note uses the vanilla note/book visual family.
- Eligibility is checked on login/spawn and character-level changes, so players already above level 10 are supported.
- The note-delivered flag is set only after the item is successfully placed. Full inventories retry later.
- If the note is lost before being read, login/spawn recovery reissues it while the player remains eligible and has no note, active quest or completed unlock.
- Reading is server-validated and consumes the note only after acceptance.
- The Rekt quest list uses persistent CVar gates where vanilla XML permits it. C# maintains threshold CVars such as `magicArchmageEligible`; a trader-window patch is a fallback, not the primary design.
- Both quests are unique, non-repeatable and non-shareable. Each player completes them independently.
- The Archmage quest unlocks both Uber recipes, but each Uber spell still requires its own school at level 400 to cast.
- Spell books and tonic are added only to Rekt’s specialty/secret-stash tier groups.

### 2.5 Spell books, reading and recipes

- **Held book:** left-click casts; right-click invokes the custom read action.
- **First read:** server verifies exact book, school level and recipe CVar. On success it consumes one book, grants configured first-read school XP, and sets the exact recipe-unlock CVar.
- **Reread:** if the recipe CVar is already set, server consumes one book and applies **Focused Study: Magic** for 10 minutes by default. The buff doubles eligible cast XP.
- Failed reads—insufficient level, invalid item, stale slot or server rejection—consume nothing.
- Recipes are hand-crafted from paper and duct tape, with tier-scaled configurable costs.
- Regular learned recipes are gated only by their read CVar. School level is enforced before the CVar can be earned and again when casting.
- Uber recipe CVars are granted by the Archmage quest.
- Total gameplay items: 34 spell books, two staves, tonic, note and tome/quest presentation item = 39 generated icons/items where applicable.

### 2.6 Cast XP rules

XP is granted server-side and never per victim without a cap.

- Instant offensive spells grant once when at least one eligible hostile target actually takes damage.
- Self-damage and ally/player damage never qualify for cast XP or mastery kill credit.
- Mend damage qualifies only when it damages an eligible hostile; Mend healing qualifies only for health actually restored.
- Regrowth/Radiance qualify only when they restore health or remove a configured harmful effect.
- Aegis qualifies when it creates a new shield or replaces the current pool with a meaningfully larger pool; repeatedly refreshing a weaker/equal shield gives no XP.
- Channels grant at most one XP event per configured server interval while damaging an eligible hostile.
- AoE and chain spells grant one event per accepted cast/tick, not one per target.
- Mana and cooldown costs still apply to accepted casts that fail to qualify for XP.

### 2.7 Friendly fire

Spell geometry does not apply party, ally or PvP filters. Any damageable living entity selected by the spell—including players and the caster where the spell’s geometry includes them—receives damage.

- AoE self-damage is explicit for Fireball and Meteor.
- Chain Lightning intentionally excludes the caster from secondary arcs, as specified above.
- Direct beams start beyond the caster collider and do not fabricate self-hits.
- Config exposes self and ally multipliers, but values are clamped to a positive minimum so the hard rule cannot be disabled by setting zero.
- Invulnerable traders or other engine-protected entities remain protected by the game.

### 2.8 Staves

Staves are optional, non-stacking toolbelt focuses. Casting still requires holding a spell book.

| Staff | Unlock | Default effects |
|---|---|---|
| **Primitive Staff** | Hidden Tome completion | +10% spell power and mana regeneration |
| **Arcane Staff** | `max(Primal, Forbidden) ≥ 200` | +25% spell power and reduced mana costs |

- The server scans the 15-slot toolbelt and selects the strongest eligible staff. Duplicate staves and multiple staff types do not stack.
- Staff quality scales its configured bonuses. The server validates the actual toolbelt `ItemValue`.
- Staff effects are calculated by Spell Mastery code rather than vanilla held-item passive effects, because the player is holding a book while casting.
- `NetPackageSpellState` mirrors the chosen focus to the owning client. M9 may use that state to attach a cosmetic staff to a back holster; this visual is not required for bonuses.

### 2.9 Console commands

`spell info <player>` · `spell set <player> <school|both> <level>` · `spell reset <player>` · `spell xp <player> <school> <amount>` · `spell mana <player> <amount>` · `spell unlock <player>` · `spell book <player> <spell> <tier>`

`spell reset` resets schools, mana and active channel/cooldowns but does not silently erase completed vanilla quests or learned recipe CVars. A separate explicit full-reset/debug option may do that later.

### 2.10 Configuration — `SpellMastery.xml`

Load grouped values with `Descendants()`.

Configuration includes mana formulas/fixed-point precision; spell base values/tier multipliers/costs/cooldowns/ranges/radii/chains; maximum quality bonus; XP eligibility/rates/study multiplier/duration; channel tick/heartbeat/timeout; tonic recipe/restore; quest thresholds/IDs; friendly-fire multipliers and positive minimum; staff bonuses; loot/trader weights; mana/FX network rates; and debug logging.

---

## 3. Architecture

### 3.1 Shared mastery core

New infrastructure mod:

```text
mod-src/DS_MasteryCore/
├── build.sh
├── ModInfo.xml
├── mod/
└── src/
    └── MasteryCore.csproj
        └── ProgressionValueSerialization.cs
```

Installed as `Mods/0_DS_MasteryCore/`, included in the client pack, and hidden from the public mod list as infrastructure. It applies the UInt16 progression serializer exactly once on client and server.

M0 removes the duplicate serializer source from DSWM builds, bumps DSWM’s version, and verifies DSWM levels/saves before Spell Mastery progression is added. Each gameplay mod retains its own narrow `SavePlayerData` blob rewrite and `ToEntity` state guard.

### 3.2 Spell Mastery layout

```text
mod-src/DS_SpellMastery/
├── build.sh                     # thin wrapper around mod-src/build.py
├── PLAN.md
├── README.md
├── docs/ART_PIPELINE.md
├── tools/
│   ├── spells.json              # single source of truth
│   ├── generate_xml.py
│   ├── generate_icons.py
│   └── verify_xml.py
├── server/                      # server ModInfo, config and SpellMastery.dll
├── client/                      # client ModInfo, config, XUi and SpellMastery.dll
└── src/
    ├── Shared/                  # config models and identical package classes
    ├── SpellMasteryServer/
    └── SpellMasteryClient/
```

Both sides ship an assembly named `SpellMastery.dll`. Shared package classes have identical full names. Calls to `PooledBinaryWriter.Write` use a `System.IO.BinaryWriter` cast to avoid the known CS0518 overload-resolution failure.

### 3.3 Authority and networking

| Concern | Authority |
|---|---|
| Local input and predicted first-person animation | Client |
| Cast acceptance, held-item validation and aim sanitization | Server |
| Damage, healing, buffs, shield pool, chains and meteor scheduling | Server |
| Mana, staff selection, cooldowns, channel state and XP | Server |
| Persistent school/mana state | Server guards + shared serializer |
| Recipe/quest flags | Persistent CVars set after server validation |
| Accepted world FX and third-person animation events | Server broadcast to nearby clients |

Packages compiled into both assemblies:

- `NetPackageSpellCastRequest` (client→server): action type, held slot/item hint and aim direction/target-point hint. It never supplies trusted origin, quality, mana, damage or caster ID.
- `NetPackageSpellChannel` (client→server): start/heartbeat/stop with cast sequence. Server times out missing heartbeats and cancels on death, stun, item switch, disconnect, range failure or insufficient mana.
- `NetPackageSpellCastResult` (server→owning client): accepted/rejected result, sequence, sanitized impact/targets, cooldown and reconciliation data.
- `NetPackageSpellFx` (server→nearby clients): accepted spell, caster, sanitized positions/targets and start/stop/tick event needed for world FX and third-person presentation.
- `NetPackageSpellMana` (server→owning client): current/max/regen, dirty and rate-limited.
- `NetPackageSpellState` (server→owning client, and focus presentation where needed): unlock state, school levels, chosen staff focus and channel cancellation.
- `NetPackageSpellRead` / `NetPackageSpellTonic` (client→server): inventory-slot requests validated against `Sender.entityId`.

Server cast validation:

1. Resolve player exclusively from `NetPackage.Sender`.
2. Require alive, unlocked, not otherwise blocked, and rate-limit malformed/spam requests.
3. Validate the exact held slot, item name, tier and server-side `ItemValue.Quality`.
4. Validate school and spell gate, mana, cooldown and channel state.
5. Build origin from the server entity’s eye/head position. Treat client aim as a hint, clamp angular/range deviation, and perform server line-of-sight raycasts.
6. Deduct mana/start cooldown atomically before applying an accepted effect.
7. Apply effects and XP server-side, then broadcast the sanitized accepted result/FX.

The caster may play a lightweight predicted local animation immediately, but damaging/projectile/world FX are reconciled from the accepted server event. Other players never depend on the caster’s local-only action.

### 3.4 Progression and recipes

- Visible progression entries: `magicPrimal` and `magicForbidden`, levels 1–600, generated display rows matching spell gates.
- Hidden progression entry: `magicManaCurrent` only. Quest, unlock and learned-recipe flags use CVars instead of fake progression entries.
- `0_DS_MasteryCore` owns the only global UInt16 `ProgressionValue` patch.
- Spell Mastery’s `SavePlayerData` prefix rewrites only its two school entries and mana entry from the live server entity. It must parse and preserve every unrelated entry byte-for-byte semantically.
- Its `EntityNetworkStats.ToEntity` guard captures/restores only those three entries.
- School level changes are pushed to the owning client with `NetPackageEntitySetSkillLevelClient`, matching DSWM.
- Learned recipes use unique CVars set on successful read/quest reward. `RecipeTagUnlocked` checks the CVar; school progression does not independently unlock the same recipe.

### 3.5 Generated XML

`tools/spells.json` drives generated:

- items and recipes;
- progression and display rows;
- buffs and visible status effects;
- loot and Rekt trader patches;
- quests and Rekt quest-list entries;
- localization;
- `Config/XUi_InGame/windows.xml` for the mana HUD;
- icon manifest and verification expectations.

Finite Aegis absorption, authoritative damage/healing, read/tonic consumption, channel state and delayed Meteor logic remain C# systems even when XML supplies their visible buffs/definitions.

Every passive table is anchored at quality 1. Generator verification checks every emitted XPath against vanilla configuration and later against `ConfigsDump` after deployment.

---

## 4. Milestones

Every code/content change bumps the current development version before build.

- **M0 — Shared core and scaffold (0.1.0):** create `0_DS_MasteryCore`; move the global UInt16 serializer responsibility out of DSWM; bump/build/test DSWM with the core; scaffold Spell Mastery server/client projects and packages; register both mods in `mod-src/build.py`. Add `0_DS_MasteryCore` and `1_DS_SpellMastery` to `CLIENT_MODS`, add Spell Mastery to `HIGHLIGHT_MODS`, add the Spell Mastery client staging path to `CLIENT_PACK_OVERRIDES`, add both registry entries, and hide MasteryCore publicly. Verify DSWM saves/levels and deterministic pack contents before continuing.
- **M1 — Config, console and generator:** implement `SpellConfig.Load(Descendants())`, defaults, console commands, `spells.json`, XML/icon/verifier skeletons and debug logging.
- **M2 — School progression:** generate visible skills/display rows; implement narrow capture/restore and blob rewrite; push levels to clients; milestone announcements; coexistence tests proving DSWM and Spell Mastery levels both survive client pushes and restart.
- **M3 — Mana/HUD vertical slice:** hidden mana entry, server regeneration/deduction, Intellect recalculation, mana packages, generated XUi HUD, unlock hiding and server-authoritative tonic. Add a non-damaging test cast request/result to prove item validation and accepted FX broadcast before content expansion.
- **M4 — Quest line:** recoverable level-10 note, server-validated read, CVar-gated Rekt special quest, Hidden Tome fetch, completion unlocks/rewards and mana-bar reveal. Verify existing level-10+ players, full inventory retry and lost-note recovery.
- **M5 — Books and instant spells:** generate all 34 books/recipes/icons, consuming first-read/reread flow, recipe CVars, quality crafting/loot normalization, Rekt loot/trader groups, Focused Study, accepted-cast result/FX broadcast, Mend, Regrowth, Aegis, Fireball and Soulburn. Verify hostile/ally/self behavior, XP eligibility and multiplayer FX.
- **M6 — Channels and chains:** heartbeat-based Sun Lance and Arc Beam, cancellation matrix, capped channel XP, Chain Lightning target selection and replicated start/stop/tick FX.
- **M7 — Toolbelt staves and Uber content:** strongest-focus scan with non-stacking quality-scaled bonuses; Primitive/Arcane recipes; Archmage eligibility/quest; Radiance and delayed Meteor. Verify focus switching, duplicate staffs, cures/immunity and Meteor attribution/self-damage.
- **M8 — Polish and 1.0.0 release:** cooldown presentation, cosmetic projectile improvements, balance/config pass, localization, dashboard/client-pack rebuild and full verification. Bump to `1.0.0` only for the release candidate.
- **M9 — Custom art and advanced animation:** follow `docs/ART_PIPELINE.md` after its asset-loading/rig/replication research spike. Release as a later version bump.

---

## 5. Verification checklist

- [ ] Shared core is the only owner of the UInt16 `ProgressionValue` patch on client and server.
- [ ] DSWM and Spell Mastery levels both survive client stat pushes, save/restart and reconnect.
- [ ] `ConfigsDump/items.xml` contains 34 books, two staves and tonic; generated XPaths matched.
- [ ] `ConfigsDump/recipes.xml` uses read/quest CVars and does not unlock learned recipes from school level alone.
- [ ] Crafted quality equals school level; looted quality is normalized to 100–600; quality-1 books function.
- [ ] Tier/quality power math matches configuration at qualities 1, 100, 300 and 600.
- [ ] Mana formulas, Intellect changes, persistence, tonic consumption/jar return and HUD visibility are correct.
- [ ] Cast requests reject spoofed item/quality/slot, impossible aim, insufficient mana, cooldown spam and stale sequences.
- [ ] Other clients see accepted instant, channel and delayed-spell FX; rejected casts do not create authoritative world FX.
- [ ] Channels cancel on release, heartbeat timeout, death, stun, item switch, disconnect and insufficient mana.
- [ ] Friendly fire and positive multiplier clamp work for zombie, infected animal, ally and caster cases.
- [ ] XP is once-per-cast/tick and excludes self/ally-only damage and ineffective healing/shield refreshes.
- [ ] Aegis pool absorbs a finite amount, persists only for its duration and synchronizes its visible state.
- [ ] Note delivery supports existing level-10+ players, full inventories and lost-note recovery.
- [ ] Rekt alone offers the magic quests and carries the configured spell inventory.
- [ ] Toolbelt staff bonuses select the strongest eligible staff and never stack.
- [ ] Client pack contains MasteryCore, SpellMastery client DLL/XML/XUi/assets, and updated Weapon Mastery.
- [ ] Clean server restart has no new errors; known `System.Private.CoreLib` startup noise is ignored.

---

## 6. Key risks

1. **Global serializer compatibility:** resolved structurally through `0_DS_MasteryCore`; do not reintroduce serializer copies into gameplay mods.
2. **Client-reported aim/input:** sender identity, item state, quality, range and line of sight remain server-validated.
3. **Channel lifecycle:** heartbeat timeout and all cancellation paths must be tested before damaging channels ship.
4. **FX replication:** local prediction is cosmetic; accepted server broadcasts are the multiplayer truth.
5. **Recipe learning:** read/quest CVars provide the unlock. Do not add a school-level `RecipeTagUnlocked` path that bypasses reading.
6. **Quality coverage:** all effects and custom calculations accept quality 1–600.
7. **Finite shield:** Aegis requires server-side damage interception and cleanup, not XML alone.
8. **Quest recovery:** note/quest state must never permanently strand a player.
9. **Art/animation:** custom bundle syntax, rigs, holsters and third-person replication require a dedicated M9 proof before production assets.

---

## 7. Build and release procedure

1. Bump every changed mod’s source `ModInfo.xml` version before building. A shared-core change also requires rebuilding/testing both mastery mods and repackaging clients.
2. Run the relevant thin `build.sh`, which delegates to `mod-src/build.py`; use the master all/verify commands for cross-mod changes.
3. Gracefully stop with telnet `shutdown`. After the user service is inactive—or after observing its configured auto-restart state—start only with:
   `systemctl --user start 7daystodie.service`.
4. Announce that players must re-extract `AfterHours_ClientMods.zip`.
5. Complete the M8 verification checklist before calling the build `1.0.0`.
