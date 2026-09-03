# DS_Zipline art pipeline

The original licensed Blender scene is preserved at
`source/sonic_zipline_original.blend`. It has no image textures; its black,
steel, white, and red appearance comes from material colors. The CC BY bolt-cutter
FBX and original 2048px PBR maps are preserved under `source/tool-boltcutter/`;
see `ATTRIBUTION.md`.

## Regenerate game-ready art

First regenerate the downsampled/packed runtime tool textures (requires Pillow),
then run Blender:

```bash
python3 tools/prepare_tool_textures.py
blender -b art/source/sonic_zipline_original.blend \
  --python tools/prepare_model.py -- \
  unity/DSZiplineAssets/Assets/Models \
  art/generated/dszipline.meshbin
```

The preparation script:

- scales the post so its cable contact is exactly 2.55 m above its base;
- rebases the post at ground level;
- places the trolley pivot on the cable and points local +Z along travel;
- preserves the complete fixed cable as a reference model;
- constructs the primitive tier as a dedicated rough-hewn timber mast, crossbeam,
  braces, rope lashings, and forged cable eye centered at 2.55 m;
- combines and normalizes the bolt cutters to 0.55 m with a black-grip-center pivot;
- transfers Blender material colors and metal/roughness values;
- includes the bolt-cutter UV mesh in the runtime payload; and
- renders `server/UIAtlases/ItemIconAtlas/DSZiplineTool.png`, updating Blender's
  dependency graph before framing the rotated grip-pivot object;
- renders `DSZiplineAnchor.png` directly from the normalized original Sonic post; and
- renders `DSZiplineWoodAnchor.png` from the generated wooden model with procedural
  grain and a weathered, high-contrast inventory treatment.

`prepare_tool_textures.py` creates 1024px albedo, AO, Unity-DXT normal, and
metallic/smoothness maps under `art/generated/tool/`. These and the mesh payload
ship only in client staging; the original textures remain source artifacts.

The Unity 2022.3.62f2 project under `unity/DSZiplineAssets` remains useful for
prefab/material previews. Its AssetBundles are **not** shipped: 7DTD V3.2's
player rejects bundles from Unity's later same-version respin as incompatible.
The client therefore constructs ordinary Unity `Mesh` and `Material` objects from
`dszipline.meshbin` in the client DLL. The server safely loads the vanilla fence
prefab. Client Harmony patches select the tier model at final ModelEntity activation,
restore it after damage and chunk render toggles, disable the fallback prefab's LOD
controllers, reshape the root interaction collider, and replace held wire-tool
renderers after `ItemClass.CloneModel`.

## Confirmed held-tool presentation (V0.2.5)

The bolt-cutter export is 0.55 m long, source `-X` runs from the selected grip
toward the jaws, and the origin is the world-bounds center of source object
`Handle_Lowpoly`. Preserve that grasp pivot when regenerating the mesh.

The inherited wire-tool first- and third-person roots do not share an upright axis:

| Mesh purpose | Local position | Local rotation |
|---|---:|---|
| `Local` (first person) | `(0,0,-0.065)` | `Euler(0,90,0)` |
| `Hold` (third person) | `(0,0,+0.065)` | `AngleAxis(180,X) * Euler(0,90,0)` |

This is graphically confirmed final: one black grip is seated in the right hand,
the grips are vertical, and the jaws rise above the hand in both views. Do not add
the discarded V0.2.2–V0.2.4 screen-space tilt/yaw corrections. For future held
models, solve in this order: physical scale, semantic forward axis, grasp pivot,
per-purpose axis mapping, then small per-purpose translation. Test first and third
person separately and change only one category at a time.

The icon deliberately uses all five supplied PBR maps rather than albedo alone. Its
material multiplies baked AO and deterministic generated-coordinate grime into the
base color, applies the normal/roughness/metallic maps, and uses a darker saturated
icon-only grade under a smaller 500-energy key light. This stylization is intentional:
vanilla inventory icons are grittier and higher-contrast than a clean catalog render.

The source scene's fixed cable remains reference-only. Actual routes vary from
4–96 m and use the procedural sag mesh shared with rider movement.
