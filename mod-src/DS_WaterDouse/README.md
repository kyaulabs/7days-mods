# Water Douse (1_DS_WaterDouse)

Wash off the 7DTD 3.x **scent** mechanic by dousing yourself with water, instead of
having to find a lake/river and wade through it.

## How it works

In 3.x every player carries a scent aura (`PlayerStealth`, 0–100 m) driven by the
smelly items in their inventory. Zombies track it from up to 100 m. Walking through
water (wetness) clears it — but only fully, and only when you can get wet.

This mod adds a **Douse** option to the item context menu (right-click / E) of water
items, separate from Drink:

| Item | Effect |
|---|---|
| Murky Water (`drinkJarRiverWater`) | removes **25 m** of scent radius |
| Water (`drinkJarBoiledWater`) | removes **25 m** of scent radius |
| Pure Mineral Water (`drinkJarPureMineralWater`) | removes **ALL** scent (same as the wet clear) |

Each douse consumes one unit of the item. A douse also washes off the **eating-smell**
source (`ItemActionEat.SmellUse` — food you ate leaves a lingering scent that would
otherwise re-emit): once your radius hits 0 and you carry no smelly items, the scent
stays gone. After a partial douse the aura only regrows (vanilla 5 m/s) while you
keep carrying smelly items.

The entry is disabled when you have no scent to wash off.

## Configuration (`DouseConfig.xml`, client + server)

```xml
<Config>
    <DefaultMetersRemoved>25</DefaultMetersRemoved>  <!-- meters per regular-water douse -->
    <MaxMetersRemoved>100</MaxMetersRemoved>         <!-- hard cap / anti-cheat clamp -->
    <SoundName>bucketpour_concrete</SoundName>       <!-- sound group played on douse -->
    <DebugLogging>false</DebugLogging>               <!-- log douses server-side -->
</Config>
```

Per-item override (see `Config/items.xml`): add `DouseSmellMeters` (meters) or
`DouseSmellFull` (true = full clear) properties to any item to make it douseable.

## Architecture

- **Client DLL** (`Douse.dll` in the client pack):
  - `ContextMenuPatch` — postfix on `XUiC_ItemActionList.AddActionActions` adds the
    Douse entry for items with a douse property.
  - `ItemActionEntryDouse` — consumes one unit, cuts the local scent radius,
    plays a sound, shows feedback.
  - The client's local `PlayerStealth` is client-computed, but on a dedicated server
    its `smellRadius` never moves (vanilla only moves the server copy), so the
    enabled-state and feedback use the server-synced `smell` cvar.
- **Server DLL** (`Douse.dll` in `Mods/1_DS_WaterDouse/`):
  - `NetPackageDSDouse` (shared class, both assemblies — name-synced by
    `NetPackageManager`) reports the douse client→server.
  - `DouseApply` (shared) cuts the server's authoritative `smellRadius` instantly
    (zombie AI + "N M" display) and refreshes `buffSmellCheck`, mirroring the
    vanilla 20-tick cvar update. The server-side handler validates the player
    actually carries a douseable water item (forged packages are ignored) and
    clamps the meters.
- `StealthAccess` (shared) mutates the `PlayerStealth` struct fields in place — the
  smell fields are publicized public in the game assembly, so a `ref` to the struct
  field (`EntityPlayer.Stealth`) gives direct read/write without boxing.
- Eating-smell handling: a douse (partial or full) clears `smellEatRadius` /
  `smellEatTicks` and forces an immediate target recompute (`smellUpdateItemsTicks
  = 0`), so the client re-sends an eat-free radius target to the server within one
  tick and the scent cannot regrow from the washed-off food source.

SP / listen-server: there is no net package channel in offline mode, so
`DouseClient` runs `DouseApply` directly in-process (exactly one application,
item validation skipped since the item was already consumed from the shared
inventory).

## Deployment

- `./build.sh` builds both DLLs, assembles `server/` + `client/`, deploys to
  `Mods/1_DS_WaterDouse/` and rebuilds the dashboard client modpack
  (`AfterHours_ClientMods.zip`).
- The mod is client+server; **players must re-extract the client pack** after
  deploying. The server requires a restart to load the new mod — a client connecting
  without the client-side mod (missing `NetPackageDSDouse`) is disconnected, same as
  Weapon Mastery's package.
- `mod-src/build.py` pulls the client build from `mod-src/DS_WaterDouse/client/`
  (override in `CLIENT_PACK_OVERRIDES`), never from the live server folder.
