# DS_SpellMastery — 3D Art and Animation Pipeline

Status: **M9 research and production guide**. Do not begin production assets until the asset-loading, rig and replication proof succeeds.

This work runs on the owner's laptop with Blender and Unity. The target game is 7 Days to Die V3.1.0 b14, whose log reports Unity **2022.3.62f2**. Use that exact editor version to minimize compatibility risk; matching the editor alone does not guarantee correct bundles, shaders, prefab structure or animation integration.

## 1. Intended assets

| Asset | Purpose | Notes |
|---|---|---|
| `PrimitiveStaff` / `ArcaneStaff` | Optional toolbelt focus models | Bonuses come from Spell Mastery’s toolbelt scan, not from holding the staff. A back-holstered visual is cosmetic and may require client C# attachment logic. |
| `SpellBook` | Shared held casting implement | The 34 book items may share one mesh with tint/emissive variants. Needs first-person held/read presentation. |
| Spell FX prefabs | Primal/Forbidden impact, beam and warning visuals | Must be driven from accepted `NetPackageSpellFx` events so other players see them. |
| Item/object animation clips | Book/staff object motion | Does not automatically animate the game’s player hands or third-person character rig. |
| Optional player-rig clips | First- and third-person cast gestures | Requires a verified compatible game rig/avatar and a replicated animator path. Treat as advanced scope. |
| Icons | Replacement for procedural v1 icons | 34 books + two staves + tonic + note + tome/presentation item where used. |

## 2. Required research spike

Before modeling final assets, produce one deliberately simple cube/staff test bundle and answer:

1. What bundle compression and build target does V3.1.0 b14 load for the supported client platforms?
2. What is the exact mod-relative asset reference syntax accepted by `Meshfile`/`HandMeshfile`?
3. Which prefab components, layers, tags, shaders and material setup survive loading?
4. Which item properties control first-person, world/drop and third-person meshes?
5. Does the game render a non-selected toolbelt focus on a holster automatically? If not, where should Spell Mastery’s client code attach the chosen focus model?
6. Which vanilla animation states can be safely reused for v1?
7. What rig/avatar and animator parameters are required for custom first-person hands and third-person replicated gestures?

Record the proven answers and exact XML line in this document before production. Do not use guessed `bundleName:PrefabName` syntax.

## 3. Software setup

1. Blender 4.x LTS or another project-pinned Blender version.
2. Unity Hub with Unity **2022.3.62f2**.
3. The client-platform modules required for every platform the modpack supports. At minimum, build and test the platform used by AfterHours players.
4. Optional Blender/Unity MCP integrations for iteration. Batch-mode Unity builds remain the reproducible path.
5. A local 7DTD V3.1.0 b14 client for actual loading, equip and multiplayer tests.

MCP assists tool operation; it does not solve 7DTD-specific bundle, rig, shader or replication requirements.

## 4. Blender workflow

- Blender uses **Z-up**; Unity uses **Y-up**. Keep Blender scene units in meters and use a documented FBX export axis conversion.
- Primitive Staff: approximately 1.4 m, wood/bone/rope, low-poly held prop.
- Arcane Staff: approximately 1.4 m, carved body and emissive crystal detail.
- Spell Book: approximately 0.25 m, origin and grip chosen only after the test prefab establishes the game’s hand orientation.
- Keep meshes, materials and pivots separate enough to correct hand/holster offsets without remodeling.
- Export binary FBX. Include an armature only for clips that use a verified compatible rig.
- Render icons orthographically with transparent backgrounds at 256×256, then downscale through the repository generator.

Do not author player-hand animation against an arbitrary Blender armature. The clip will not drive 7DTD’s hands unless its skeleton/avatar compatibility has been proven.

## 5. Unity bundle workflow

1. Create a Unity 2022.3.62f2 project and commit/pin `ProjectVersion.txt`.
2. Import test and production FBX files into `Assets/Models/`.
3. Create prefabs with the minimum proven components. Add named FX sockets only after verifying how Spell Mastery locates them.
4. Add a repository-owned `Assets/Editor/BuildBundles.cs` that:
   - selects an explicit build target;
   - applies the proven bundle options/compression;
   - writes deterministic logs and manifests;
   - outputs to a staging directory, not directly into the live server tree.
5. Copy approved client assets into `mod-src/DS_SpellMastery/client/Resources/` through the master build. If server XML needs matching paths, stage only the required metadata/resources under `server/`.
6. The deployed client path is `Mods/1_DS_SpellMastery/Resources/`.
7. Reference assets with the proven 7DTD item property—normally `Meshfile` and, where required, `HandMeshfile`/`DropMeshfile`—using the exact syntax established by the research spike.

The generated `items.xml` remains authoritative. Store proven asset references in `tools/spells.json` or another generator input; never hand-edit generated output.

## 6. Animation strategy

### V1 fallback

Use verified vanilla hold/fire/reload/swing states plus accepted server-broadcast particles, lights and sounds. This requires no custom player rig and is the release-safe baseline.

### Object-only animation

An Animator on the book/staff prefab can animate that object and child FX sockets. It does **not** automatically animate the first-person arms or third-person player body.

### Custom player gestures

Custom hand/body casting requires:

- the correct first-person and third-person game skeletons/avatars;
- compatible clips and animator-controller integration;
- client code that starts/stops the state from accepted cast/channel events;
- third-person replication to observers;
- cancellation/recovery for channel stop, death, stun and item switch.

Treat first-person and third-person gestures as separate deliverables. A successful local book animation is not proof that other players see a cast.

## 7. Toolbelt focus and holster presentation

Staff bonuses are independent of visuals: the server selects the strongest eligible staff on the toolbelt and mirrors the focus state.

For the cosmetic back staff:

1. Test whether vanilla automatically holsters the selected focus while a book is held.
2. If not, locate the verified third-person attach transform.
3. Add client code that creates/removes the correct staff prefab from `NetPackageSpellState`/replicated focus state.
4. Prevent duplicate models during item switching, respawn, reconnect and focus changes.
5. Verify that quality/tier visual variants do not affect authoritative bonuses.

## 8. FX replication

- The casting client may play a small predicted first-person wind-up.
- Impact, beam, meteor warning and third-person cast FX start from accepted `NetPackageSpellFx` data.
- Channel FX must handle start, tick/heartbeat presentation, stop, timeout and cancellation.
- Nearby observers must see sanitized server positions/targets, not untrusted client aim.
- Late or out-of-range observers need no persistent historical effect, but current channel state must not leave orphaned FX.

## 9. Verification

### Asset-loading proof

- [ ] Test bundle loads with no missing shader/material errors attributable to the mod.
- [ ] Proven `Meshfile`/`HandMeshfile` reference is documented.
- [ ] Correct client build target and compression are documented.
- [ ] First-person, drop/world and third-person mesh paths are understood.

### Per production asset

- [ ] Item crafts/equips and has correct scale, pivot and hand orientation.
- [ ] Book read/cast presentation does not clip the camera excessively.
- [ ] Chosen toolbelt staff appears once on the back, or the documented fallback intentionally omits it.
- [ ] Predicted local animation reconciles cleanly with accepted/rejected casts.
- [ ] Other players see accepted instant/channel/delayed FX and any third-person gesture.
- [ ] Channel release/cancellation returns all animators and FX to idle.
- [ ] Icons replace procedural versions in the rebuilt client pack.

## 10. Delivery

- [ ] Source `.blend`, exported FBX, Unity project metadata and build script are archived outside generated bundle output.
- [ ] Approved client bundles/resources are staged under `mod-src/DS_SpellMastery/client/Resources/`.
- [ ] Generator inputs contain the proven asset references.
- [ ] Spell Mastery version is bumped before build.
- [ ] `mod-src/DS_SpellMastery/build.sh` deploys through `mod-src/build.py` and rebuilds the client pack.
- [ ] Server restart and multiplayer verification are complete.
- [ ] Players are told to re-extract `AfterHours_ClientMods.zip`.
