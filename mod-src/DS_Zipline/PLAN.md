# DS_Zipline — Implementation Plan

**Target game:** 7 Days to Die V3.2.0 b9

**Source folder:** `mod-src/DS_Zipline/`

**Planned deployment:** `Mods/1_DS_Zipline/`
**Distribution:** required on the dedicated server and in `AfterHours_ClientMods.zip`

**Current status (2026-09-02):** Phase 0 is built as V0.3.5. A clean headless
boot resolved the custom block class and merged all XML with no DS_Zipline errors.
Graphical testing through V0.1.4 confirms smooth first/third-person travel,
no camera shake or server rubber-banding, normal walking restoration, correct
arrival/early-release fall damage, launch tangent alignment, free look, a cable
above rather than through the rider, no falling animation, and a convincing
procedural overhead hand grip. V0.1.4 left the arms raised after detach because
`IKController.SetTargets` does not restore modified rig constraints. V0.1.5 now
calls the built-in cleanup/rig restoration path on every stop reason; this cleanup
awaits client confirmation. V0.1.7 replaces the vanilla anchor art with a normalized
custom 2.72 m post and adds a red/black trolley that follows the runtime cable curve.
The first V0.1.6 AssetBundles were rejected by 7DTD's same-version Unity respin;
V0.1.7 instead constructs meshes and materials from a client-side binary payload.
V0.1.8 moved anchor replacement from `CloneModel` to the pool-create callback,
but the fallback still survived. V0.1.9 retains that hook and adds the final
authoritative `OnBlockEntityTransformBeforeActivated` hook on the actual block
entity transform, after all pool/setup work; it also logs the first replacement.
V0.1.10 applied the first art-polish pass: -0.5 m grounding offset, clockwise
90° local Y rotation, and texture-backed material colors; all three were confirmed.
V0.1.11 reverses triangle winding after the export's handedness reflection and
centers the modeled cable socket on the procedural endpoint. Graphical testing confirms
the complete art pass: grounded, correctly rotated/colored, solid from all viewpoints,
and aligned with the cord. V0.1.12 added a custom thin-pole shape, native 1x3x1
multiblock, and safe legacy migration, but the hidden fallback Physics collider still
won raycasts and damage triggered magenta/fallback render state. V0.1.13 fixed the
magenta propagation, but disabling Physics left no reliable E target. V0.1.14 keeps
that proven root Physics target and reshapes it to the visible 2.718 m black pole;
V0.2.4 focus/direct-Use testing confirms interaction now follows the visible pole. V0.2.0 added the CC BY textured bolt-cutter
held model and generated item icon. V0.2.1 moved its origin to one grip and replaced
placement-ghost geometry. V0.2.2 gives first-/third-person roots separate presentation
angles and fixes stale dependency-graph bounds that placed the icon high in its slot.
V0.2.3 added opposite 3.5 cm local-Z corrections and graphically seated the grip.
V0.2.4 added state-aware center-screen instructions/direct E-to-ride, now graphically
confirmed, but its yaw was on the wrong axis. V0.2.5 directly maps both view models to
an upright grips-in-hand/jaws-above carry, now graphically confirmed final. V0.2.6
preserved that pose while reducing the oversized 0.85 m geometry to a compact 0.55 m.
V0.2.7 lowers both view models 3 cm into the fist and rebuilds the icon with full PBR,
procedural grime, and darker vanilla-style grading. V0.2.8 corrects the reversed left
ride wrist and raises the body while moving hand targets inward/forward to produce
elbow bend instead of locked vertical shoulders. V0.2.9 preserves that geometry and
rolls the left palm 180° around its longitudinal axis instead of bending the wrist
around root Z. The complete V0.2.9 ride pose is now graphically confirmed final.
V0.2.10 replaces the black gameplay cord with the source Sonic Zipline's cyan HDR
emission across the full procedural cable. V0.2.11 disables the inherited electrical
wire pulse phase and assigns that HDR cyan to both shader endpoints, producing a solid
non-directional glow instead of moving stripes; this is graphically confirmed final.
V0.3.0 adds a dedicated weathered timber/rope/iron lower-tier model and icon, static
black basic cable, 4 m/s basic rides, 16 m/s Sonic rides, chained crafting, and
client/server same-tier connection enforcement. It also fixes fallback fence
reappearance: `Chunk.SetBlockEntityRendering` blindly enabled every renderer after
nearby door/occlusion changes, so the client now reapplies the custom visual after
that method and after the actual block-entity activation boundary while disabling
inherited LOD controllers. V0.3.1 fixes repeat-ride hand IK: `AddIKController`
reuses its component, but `SetTargets` alone does not rebuild an already-started
rig, so the tier ridden second could lose the overhead grip. Reused controllers
now call `ModifyRig()` immediately. V0.3.1 also replaces the last tinted vanilla
fence icon with a generated render of the normalized Sonic model; both anchor icons
are now model-derived. V0.3.2 adds iron bands, nail heads, and a tied rope knot to
wooden anchors, then applies deterministic dirt/soot/oxidation breakup to both anchor
tiers at runtime and in their icons. It moves both anchors into Building, makes the
wood tier a no-unlock hand recipe from common materials, gates the workbench Sonic
tier with the Electrician tier-4/car-battery unlock and end-game materials, and gates
the cheaper hand-crafted connection tool with the regular wrench unlock. V0.3.3
adds the missing progression display entries, placing the Sonic anchor visibly in
Electrician tier 4 and the connection tool visibly in the regular wrench row; recipe
gating was already tied to those tiers. V0.3.4 raises wooden range to 250 m and
Sonic range to 500 m. Because vanilla `FastWireNode` hard-stops at 256 m and only
draws while both endpoint chunks are loaded, all zipline routes now use a scalable
6-sided custom cable mesh built from the shared ride curve. The client sends the
remembered endpoint coordinates directly on the second click; the server enforces
tier range/proximity and safely loads the distant endpoint before allowing vanilla
power-graph persistence. Child TileEntities persist the parent coordinate so either
loaded end can render across unloaded chunks. A second-client observation remains; later phases
below remain planned rather than implemented. Runtime testing uses the sole
normal development service on port 26900; never start a parallel test server on
this machine.

## 1. Decision

A player-usable zipline is viable, but it cannot be XML-only. It requires a C# DLL on both the client and server. The recommended implementation reuses the vanilla powered-block wire graph and cable renderer rather than inventing a second persistence and synchronization system.

The highest-risk area is smooth local-player movement and camera behavior. Build a narrow runtime spike before committing to recipes, art, balancing, or polished multiplayer presentation.

## 2. V3.2.0 findings

The following APIs were rechecked against the installed V3.2.0 b9 `Assembly-CSharp.dll` after the server update:

- `BlockPowered` still supports custom powered blocks and activation commands.
- `TileEntityPowered` still persists `wireDataList` and `parentPosition` and provides `DrawWires` and `SetParentWithWireTool`.
- `ItemActionConnectPower` still reads an XML `MaxWireLength` property; its vanilla default remains 15 m.
- `FastWireNode.BuildMesh` still creates a 16-segment sagging cable and has a hard 256 m render limit.
- `EntityPlayerLocal.Update` and `SetPosition(Vector3, bool)` remain patchable.
- `NetPackageEntityRelPosAndRot` still accepts movement from the owning sender and applies it to the server entity.

A compile-only API probe containing a custom powered anchor, activation command, cable-render patch, player update patch, and custom net package builds successfully against V3.2.0 b9. Runtime behavior still needs testing in a graphical client.

## 3. MVP behavior

The first usable version should deliberately stay narrow:

- Players craft and place zipline anchors.
- A dedicated zipline tool connects exactly two anchors.
- Each anchor supports one cable in the MVP.
- Wooden cables may be 4–250 m; Sonic cables may be 4–500 m.
- The high endpoint must be at least 2 m above the low endpoint.
- Interacting with the high anchor offers **Ride Zipline**.
- The player accelerates downhill along the rendered cable, up to a configured speed cap.
- Jump or Use detaches early.
- Arrival places the player in a checked landing area beside the low anchor.
- Destroying either anchor removes the cable; an active rider detaches immediately.
- Ziplines are player-only. Zombies and other AI do not use them.

Out of scope for the MVP:

- Uphill or motorized travel
- Branching stations and route-selection UI
- Independent cable health
- Custom pulley/trolley art
- Perfect third-person riding animation
- AI navigation over ziplines

## 4. Architecture

### 4.1 Anchor and persistent link

Create `BlockZiplineAnchor : BlockPowered` and configure it with `RequiredPower=0`.
The server retains a vanilla relay/fence-post model as a safe fallback. On clients,
V0.1.7 replaces its cloned renderer with custom runtime mesh data while retaining
the vanilla persistent wire graph and procedural variable-length cable.

Use the existing `TileEntityPowered` relationship as the authoritative cable link:

- parent endpoint: `wireDataList` contains the child position;
- child endpoint: `parentPosition` identifies the parent;
- `PowerManager` persists the relationship;
- vanilla block removal tears down the power node and wire data;
- vanilla tile-entity synchronization sends endpoint data to clients.

Do not create a custom tile-entity type unless V3.2 runtime testing reveals a blocker. Extending the tile-entity enum and serialization would add unnecessary save-compatibility risk.

### 4.2 Connection tool

Add a dedicated tool based on `ItemActionConnectPower`; XML permits the 500 m
interaction while client/server code enforces 250 m wooden and 500 m Sonic limits.

Client and server validation must require:

1. both endpoints are `BlockZiplineAnchor`;
2. endpoints are distinct and currently loaded;
3. distance is between 4 m and the endpoint tier's 250/500 m limit;
4. vertical drop is at least 2 m;
5. neither anchor already has a zipline link;
6. the power graph would not become circular;
7. the player is close enough to the endpoint they are operating;
8. the cable path is sufficiently clear, or the UI warns that an obstruction will stop riders.

Patch or wrap the vanilla wire action so ordinary electrical blocks cannot be linked with the zipline tool and zipline anchors cannot be linked to electrical devices. Repeat validation on the server; do not trust the client-side range check.

For the first spike, tool durability may represent cable cost. Before release, consume a cable resource proportional to rounded cable length on successful server validation. Avoid refunds initially to prevent duplication edge cases.

### 4.3 Cable rendering

Patch `TileEntityPowered.DrawWires` only when the source block is a zipline anchor.

For each zipline wire node:

- call `SetWireCanHide(false)`;
- keep it visible without the wire tool equipped;
- use a thicker radius than an electrical wire;
- render wooden-tier routes as stationary black cable;
- render Sonic-tier routes with the original model's stationary cyan HDR emission;
- calculate sag from horizontal length, clamped to a configured range;
- call `BuildMesh` after changing radius and dip.

Put the curve calculation in a shared `ZiplineCurve` helper. Rendering, player movement, collision checks, and server validation must all use the same endpoint offsets and sag equation so the player follows the visible cable.

V0.3.4 no longer uses `FastWireNode.BuildMesh` for routes: its 256 m hard cap and
loaded-endpoint dependency are incompatible with the 500 m Sonic tier. Keep vanilla
wire nodes hidden and pooled for graph bookkeeping while the custom cable renderer
uses persisted endpoint coordinates. V0.3.5 establishes distant links directly
between persistent `PowerItem`s: `WorldChunkCache.GetChunkSync` is only a cache lookup,
not a force-load API, so V0.3.4 rejected the link whenever the first endpoint had
actually unloaded.

### 4.4 Rider controller

Maintain one local `ZiplineRideState` containing:

- start and end block positions;
- resolved cable endpoints;
- curve progress/distance;
- current speed;
- previous controller and movement state;
- server-approved ride token/state;
- detach reason.

On ride start:

1. verify this is the upper endpoint and the player is not dead, attached to a vehicle, swimming, climbing, or already riding;
2. ask the server to authorize the ride;
3. snap to a safe handle position below the cable;
4. suppress normal movement/gravity without skipping unrelated player updates;
5. clear motion and stale fall-distance state.

Each frame:

1. revalidate both anchors and their connection;
2. integrate speed from cable slope, gravity, drag, minimum launch speed, and maximum speed;
3. calculate the next point and tangent on the shared curve;
4. capsule-cast from the current point to the next point;
5. detach on collision, invalid chunks, death, teleport, or destroyed anchors;
6. apply the position after vanilla movement processing;
7. continue resetting fall state while attached.

On normal arrival, verify clear standing room near the low anchor before release. On early release, retain a capped tangent velocity so jumping off feels natural, but do not carry a velocity that creates unavoidable lethal fall damage.

Candidate Harmony hooks for the spike:

- `EntityPlayerLocal.Update` postfix for final rail position;
- a narrow movement hook to suppress ordinary input while riding;
- death/teleport/attach hooks to force cleanup;
- `TileEntityPowered.DrawWires` postfix for rendering.

Do not prefix-skip the whole player `Update` or `OnUpdateLive`; those methods also maintain camera, stats, buffs, challenges, and other unrelated state.

### 4.5 Networking

Compile identically named package classes into both client and server builds. Use `Sender.entityId` on the server rather than a client-supplied player ID.

Planned packages:

- `NetPackageDSZiplineRideRequest`: requested start anchor;
- `NetPackageDSZiplineRideResult`: accepted endpoints/token or rejection reason;
- `NetPackageDSZiplineRideEnd`: completion or detach reason;
- optional later `NetPackageDSZiplineRideVisual`: remote pose/trolley state.

Vanilla player position replication should carry actual movement to the server and other clients. The server ride record exists to validate the start, reject impossible states, and detect gross deviation from the approved cable—not to send a position every frame.

Single-player/listen-server must use an in-process authorization path because a dedicated-server package channel is not always present in offline mode.

### 4.6 Remote presentation

For the MVP, remote players may use an existing climbing or driving movement state while moving along the cable. Expect some visual sliding until a dedicated pose or trolley is added.

Do not spawn an invisible `EntityVehicle` for the first implementation. It would improve attachment semantics but adds entity spawning, ownership, seats, persistence, physics, and cleanup complexity. Keep it only as a fallback if direct rail movement cannot be made smooth.

## 5. Configuration

Add `ZiplineConfig.xml` with conservative defaults:

- `WoodMaxLength=250`
- `SonicMaxLength=500`
- `MinLength=4`
- `MinVerticalDrop=2`
- `WoodSpeed=4`
- `SonicSpeed=16`
- `GravityScale=1`
- `Drag=0.35`
- `MinSag=0.25`
- `MaxSag=2.5`
- `RiderHangOffset=1.25`
- `CollisionRadius=0.35`
- `AllowEarlyDetach=true`
- `DebugLogging=false`

Clamp every loaded value to safe limits. The server config is authoritative for validation and movement bounds; the client receives or duplicates matching values for prediction.

## 6. Planned source layout

```text
mod-src/DS_Zipline/
├── PLAN.md
├── README.md
├── build.sh
├── src/
│   ├── Client/
│   │   ├── RideController.cs
│   │   ├── RidePatches.cs
│   │   └── WireRenderPatch.cs
│   ├── Server/
│   │   ├── RideAuthority.cs
│   │   └── WireValidationPatch.cs
│   ├── Shared/
│   │   ├── BlockZiplineAnchor.cs
│   │   ├── ZiplineConfig.cs
│   │   ├── ZiplineCurve.cs
│   │   └── NetPackageDSZipline*.cs
│   ├── DSZiplineClient/
│   │   └── DSZiplineClient.csproj
│   └── DSZiplineServer/
│       └── DSZiplineServer.csproj
├── server/
│   ├── Config/
│   │   ├── blocks.xml
│   │   ├── items.xml
│   │   ├── recipes.xml
│   │   └── Localization.csv
│   ├── ModInfo.xml
│   └── ZiplineConfig.xml
└── client/                  # generated staging, not hand-edited
```

Both builds should emit the same deployed DLL name, `DSZipline.dll`, while including the shared block class and package names. The server build excludes graphical/input patches; the client build includes them.

Register the mod in `mod-src/build.py` only once the spike is ready to build. Add `1_DS_Zipline` to `CLIENT_MODS` and use a client staging override, as done for Weapon Mastery and Water Douse, so the server DLL is never shipped accidentally.

## 7. Delivery phases

### Phase 0 — Runtime spike

- Create one reusable anchor block using vanilla art.
- Connect two anchors with a 30–50 m test cable.
- Force the cable to remain visible.
- Ride from high to low with fixed speed.
- Test first-person camera, normal input suppression, fall state, and vanilla position replication.
- Observe the rider from a second client.

**Go/no-go gate:** no persistent rubber-banding, camera corruption, or server correction during a simple ride. If this fails, investigate a trolley entity before building the rest.

### Phase 1 — World and rendering MVP

- Add validated 4–250 m wooden and 4–500 m Sonic connections.
- Add slope checks and one-link-per-anchor restriction.
- Share the exact sag curve between mesh and rider.
- Handle chunk reload, world restart, anchor pickup, destruction, and structural collapse.
- Add recipes, item icons using vanilla assets, and localization.

### Phase 2 — Safe movement

- Add gravity-based acceleration and speed cap.
- Add capsule collision and safe endpoint dismount.
- Add jump/use detach and tangent exit velocity.
- Handle damage, death, teleport, vehicle attachment, water, and invalid chunks.
- Add debug logging and configurable values.

### Phase 3 — Multiplayer authority

- Add ride request/result/end packages.
- Validate sender, proximity, endpoints, connection, slope, and state server-side.
- Track active rides and terminate invalid sessions.
- Test high latency, packet loss, simultaneous riders, disconnect, and reconnect.

### Phase 4 — Polish

- Add proportional cable resource cost.
- Improve sounds, HUD prompts, and remote movement pose.
- Custom anchor/trolley runtime meshes added in V0.1.7; validate alignment and camera clearance.
- Consider powered uphill travel as a separate anchor variant.

## 8. Test matrix

Run gameplay tests against one dedicated test instance. Do not run parallel server
processes against the same save.

### Connection and persistence

- minimum, typical, maximum, and over-limit distances;
- insufficient and valid vertical drops;
- attempted anchor-to-electrical-device connection;
- duplicate/branch/circular connection attempts;
- endpoints in the same chunk and across chunk boundaries;
- server restart and client reconnect;
- removal of parent and child endpoints.

### Riding

- first-person and third-person camera modes;
- low, normal, and high frame rates;
- jump/use detach at start, middle, and end;
- blocked cable and blocked landing area;
- anchor destruction and structural collapse during travel;
- death, damage, teleport, logout, and server shutdown during travel;
- water intersection and destination near a ledge;
- two players on one cable in both close and separated positions.

### Multiplayer

- dedicated server with one client;
- dedicated server with two observing clients;
- listen server and single-player;
- artificial latency and packet loss;
- stale client mod and missing package behavior;
- server rejection of malformed endpoint and ride requests.

### Regression

- ordinary electrical wires still connect, render, and disconnect normally;
- vehicles, ladders, jumping, swimming, and fall damage behave normally when not riding;
- `python3 mod-src/build.py verify` passes;
- client pack hash changes when the client DLL is updated.

## 9. Acceptance criteria for 1.0.0

- A player can place, connect, save, reload, and remove a zipline without corrupting the world or power graph.
- Maximum-range wooden and Sonic ziplines render consistently and follow the same curve used for movement.
- A rider reaches a clear lower endpoint smoothly without false fall damage.
- Collision or invalid state causes a safe detach rather than clipping through blocks.
- Other clients see the rider move without major warping.
- Destroying either anchor cleans up the cable and active ride.
- Ordinary electrical systems are unaffected.
- Server-side requests reject invalid endpoint types, excessive length, bad slope, and spoofed players.
- The mod builds reproducibly, deploys to `Mods/1_DS_Zipline`, and is included in the generated client pack.

## 10. Principal risks

| Risk | Mitigation |
|---|---|
| Player controller fights rail positioning | Prove in Phase 0; patch narrow movement paths and apply rail position after vanilla movement. |
| Cable and movement curves diverge | Use one shared curve implementation and endpoint offsets. |
| Long cable crosses unloaded chunks | Persist both endpoint coordinates, render from either loaded endpoint, and defer ride relationship checks while both ends are unloaded. |
| Client-authoritative movement is abused | Require server authorization and monitor deviation from the approved curve. |
| Anchor links interfere with electricity | Enforce anchor-only links on client and server; regression-test vanilla wiring. |
| Remote rider looks unpolished | Use an existing movement state for MVP; add synced pose/trolley later. |
| Game update breaks Harmony hooks | Keep patches narrow, log failed targets, and compile/test against each game update. |
