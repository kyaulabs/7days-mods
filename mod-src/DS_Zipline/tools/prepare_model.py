#!/usr/bin/env python3
"""Normalize licensed zipline/tool art and export runtime meshes plus tool icon.

Run through Blender, not CPython:
    blender -b art/source/sonic_zipline_original.blend \
      --python tools/prepare_model.py -- unity/DSZiplineAssets/Assets/Models

The original scene is roughly 13 m long and uses Blender Z-up.  The reusable
post is scaled so its modeled cable contact lands at the mod's 2.55 m runtime
anchor height. A dedicated weathered-timber lower-tier anchor is generated around
the same centered cable point. The fixed cable is retained as a reference asset,
while the game continues rendering its variable-length procedural cable. The CC BY bolt-cutter
FBX is combined, normalized to 0.55 m, included in the runtime payload, and
rendered into the inventory icon.
"""
from __future__ import annotations

import math
import os
import struct
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


def output_paths() -> tuple[Path, Path]:
    args = sys.argv
    if "--" not in args or len(args) <= args.index("--") + 1:
        raise SystemExit("usage: blender ... --python prepare_model.py -- FBX_DIR [MESHBIN]")
    values = args[args.index("--") + 1:]
    fbx_dir = Path(values[0]).resolve()
    meshbin = Path(values[1]).resolve() if len(values) > 1 else fbx_dir / "dszipline.meshbin"
    fbx_dir.mkdir(parents=True, exist_ok=True)
    meshbin.parent.mkdir(parents=True, exist_ok=True)
    return fbx_dir, meshbin


def world_bounds(obj: bpy.types.Object) -> tuple[Vector, Vector]:
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return (
        Vector(tuple(min(v[i] for v in corners) for i in range(3))),
        Vector(tuple(max(v[i] for v in corners) for i in range(3))),
    )


def material_color(material: bpy.types.Material):
    if material.use_nodes and material.node_tree:
        for node in material.node_tree.nodes:
            if node.type == "BSDF_PRINCIPLED" and "Base Color" in node.inputs:
                return tuple(node.inputs["Base Color"].default_value)
            if node.type == "BSDF_DIFFUSE" and "Color" in node.inputs:
                return tuple(node.inputs["Color"].default_value)
    return tuple(material.diffuse_color)


def normalize_materials() -> None:
    """Make Blender's procedural colors survive FBX material export."""
    for material in bpy.data.materials:
        color = material_color(material)
        material.diffuse_color = color
        material.roughness = max(0.2, min(0.8, material.roughness))


def evaluated_mesh(source: bpy.types.Object, name: str) -> bpy.types.Object:
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = source.evaluated_get(depsgraph)
    mesh = bpy.data.meshes.new_from_object(evaluated, depsgraph=depsgraph)
    mesh.transform(source.matrix_world)
    result = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(result)
    return result


def transform_mesh(obj: bpy.types.Object, origin: Vector, scale: float, rotate_z: float = 0.0) -> None:
    # Vertex coordinates are already in the source scene's world space.
    translate = Matrix.Translation(-origin)
    rotate = Matrix.Rotation(rotate_z, 4, "Z")
    resize = Matrix.Scale(scale, 4)
    # Blender's FBX exporter plus Unity's baked-axis importer leaves X/Y in
    # place and flips FBX Z for this project. Pre-map Z-up source coordinates
    # so Unity receives X-right, Y-up, Z-forward with positive cable travel.
    axis_to_fbx = Matrix((
        (1.0, 0.0, 0.0, 0.0),
        (0.0, 0.0, 1.0, 0.0),
        (0.0, -1.0, 0.0, 0.0),
        (0.0, 0.0, 0.0, 1.0),
    ))
    obj.data.transform(axis_to_fbx @ resize @ rotate @ translate)
    obj.matrix_world = Matrix.Identity(4)


def select_only(obj: bpy.types.Object) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    obj.hide_set(False)
    obj.hide_render = False
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def export_fbx(obj: bpy.types.Object, path: Path) -> None:
    select_only(obj)
    bpy.ops.export_scene.fbx(
        filepath=str(path),
        use_selection=True,
        object_types={"MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="AUTO",
        embed_textures=False,
        mesh_smooth_type="FACE",
        use_mesh_modifiers=True,
    )


def simple_material(name: str, color, metallic: float, roughness: float):
    material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    material.diffuse_color = color
    material.metallic = metallic
    material.roughness = roughness
    material.use_nodes = True
    principled = material.node_tree.nodes.get("Principled BSDF")
    if principled is not None:
        principled.inputs["Base Color"].default_value = color
        principled.inputs["Metallic"].default_value = metallic
        principled.inputs["Roughness"].default_value = roughness
    return material


def add_icon_grime(material, color_output=None, metallic=False):
    """Overlay deterministic dirt/rust breakup on an icon material."""
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    principled = nodes.get("Principled BSDF")
    if color_output is None:
        base = nodes.new("ShaderNodeRGB")
        base.outputs[0].default_value = tuple(material.diffuse_color)
        color_output = base.outputs[0]
    texcoord = nodes.new("ShaderNodeTexCoord")
    noise = nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 7.5
    noise.inputs["Detail"].default_value = 7.0
    noise.inputs["Roughness"].default_value = 0.78
    noise.inputs["Distortion"].default_value = 0.28
    ramp = nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.31
    ramp.color_ramp.elements[0].color = (
        (0.22, 0.055, 0.012, 1.0) if metallic else (0.10, 0.055, 0.022, 1.0))
    ramp.color_ramp.elements[1].position = 0.66
    ramp.color_ramp.elements[1].color = (0.92, 0.86, 0.72, 1.0)
    multiply = nodes.new("ShaderNodeMixRGB")
    multiply.blend_type = "MULTIPLY"
    multiply.inputs[0].default_value = 1.0
    links.new(texcoord.outputs["Generated"], noise.inputs["Vector"])
    links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    links.new(color_output, multiply.inputs[1])
    links.new(ramp.outputs["Color"], multiply.inputs[2])
    links.new(multiply.outputs["Color"], principled.inputs["Base Color"])
    return multiply.outputs["Color"]


def create_wood_anchor() -> bpy.types.Object:
    """Build a grounded, setting-appropriate timber anchor around a centered eye."""
    wood = simple_material("DSZiplineWood", (0.34, 0.17, 0.07, 1.0), 0.0, 0.82)
    rope = simple_material("DSZiplineRope", (0.20, 0.105, 0.035, 1.0), 0.0, 0.9)
    iron = simple_material("DSZiplineWoodIron", (0.055, 0.048, 0.042, 1.0), 0.72, 0.58)
    parts = []

    def finish_part(obj, material, bevel=0.0):
        bpy.context.view_layer.objects.active = obj
        obj.data.materials.append(material)
        if bevel > 0.0:
            modifier = obj.modifiers.new("Hand-hewn edges", "BEVEL")
            modifier.width = bevel
            modifier.segments = 2
            bpy.ops.object.modifier_apply(modifier=modifier.name)
        bpy.ops.object.shade_smooth_by_angle()
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        parts.append(obj)
        return obj

    def timber(name, center, size, bevel=0.018):
        bpy.ops.mesh.primitive_cube_add(location=center)
        obj = bpy.context.object
        obj.name = name
        obj.scale = Vector(size) * 0.5
        return finish_part(obj, wood, bevel)

    def diagonal_timber(name, start, end, width=0.12, depth=0.15):
        start = Vector(start)
        end = Vector(end)
        direction = end - start
        bpy.ops.mesh.primitive_cube_add(location=(start + end) * 0.5)
        obj = bpy.context.object
        obj.name = name
        obj.scale = Vector((width * 0.5, depth * 0.5, direction.length * 0.5))
        obj.rotation_mode = "QUATERNION"
        obj.rotation_quaternion = Vector((0.0, 0.0, 1.0)).rotation_difference(direction.normalized())
        return finish_part(obj, wood, 0.012)

    def detail_box(name, center, size, material, bevel=0.004):
        bpy.ops.mesh.primitive_cube_add(location=center)
        obj = bpy.context.object
        obj.name = name
        obj.scale = Vector(size) * 0.5
        return finish_part(obj, material, bevel)

    def detail_rod(name, start, end, radius, material, vertices=10):
        start = Vector(start)
        end = Vector(end)
        direction = end - start
        bpy.ops.mesh.primitive_cylinder_add(
            vertices=vertices, radius=radius, depth=direction.length,
            location=(start + end) * 0.5)
        obj = bpy.context.object
        obj.name = name
        obj.rotation_mode = "QUATERNION"
        obj.rotation_quaternion = Vector((0.0, 0.0, 1.0)).rotation_difference(direction.normalized())
        return finish_part(obj, material, 0.002)

    # A rough-hewn central mast, broad top beam, and splayed braces read as a
    # survivor-built structure rather than a recolored futuristic pole.
    timber("WoodAnchor_Mast", (0.0, 0.0, 1.20), (0.24, 0.24, 2.40), 0.025)
    timber("WoodAnchor_Crossbeam", (0.0, 0.0, 2.39), (0.76, 0.22, 0.20), 0.025)
    timber("WoodAnchor_Foot", (0.0, 0.0, 0.09), (0.52, 0.34, 0.18), 0.02)
    diagonal_timber("WoodAnchor_LeftBrace", (-0.33, 0.0, 0.12), (-0.10, 0.0, 1.53))
    diagonal_timber("WoodAnchor_RightBrace", (0.33, 0.0, 0.12), (0.10, 0.0, 1.53))

    # Survivor-made iron straps, clenched nail heads, and a visible rope knot
    # break up the broad timber surfaces without changing the established
    # silhouette or centered cable socket. The hardware intentionally looks
    # mismatched and hand-fitted rather than factory-clean.
    for band_index, z in enumerate((0.43, 2.08)):
        detail_box(f"WoodAnchor_Band{band_index}_Front", (0.0, -0.126, z), (0.29, 0.018, 0.065), iron)
        detail_box(f"WoodAnchor_Band{band_index}_Back", (0.0, 0.126, z), (0.29, 0.018, 0.065), iron)
        detail_box(f"WoodAnchor_Band{band_index}_Left", (-0.126, 0.0, z), (0.018, 0.29, 0.065), iron)
        detail_box(f"WoodAnchor_Band{band_index}_Right", (0.126, 0.0, z), (0.018, 0.29, 0.065), iron)

    for nail_index, (x, z) in enumerate(((-0.25, 2.39), (0.25, 2.39), (-0.10, 1.48), (0.10, 1.48))):
        detail_rod(
            f"WoodAnchor_Nail{nail_index}", (x, -0.142, z), (x, -0.118, z),
            0.022, iron, 12)

    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=0.052, location=(0.0, -0.158, 1.63))
    finish_part(bpy.context.object, rope).name = "WoodAnchor_RopeKnot"
    detail_rod("WoodAnchor_RopeTailLeft", (-0.018, -0.154, 1.61), (-0.055, -0.16, 1.43), 0.012, rope, 8)
    detail_rod("WoodAnchor_RopeTailRight", (0.018, -0.154, 1.61), (0.062, -0.16, 1.47), 0.012, rope, 8)

    # Rope lashings around the mast and crossbeam add primitive construction
    # detail. The dark forged collar/socket lands exactly at cable height 2.55 m.
    for index, z in enumerate((1.56, 1.63, 1.70)):
        bpy.ops.mesh.primitive_torus_add(
            major_radius=0.145, minor_radius=0.014,
            major_segments=20, minor_segments=6,
            location=(0.0, 0.0, z))
        finish_part(bpy.context.object, rope)
        bpy.context.object.name = f"WoodAnchor_Lashing_{index}"

    bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.13, depth=0.16, location=(0.0, 0.0, 2.48))
    finish_part(bpy.context.object, iron, 0.008).name = "WoodAnchor_IronCollar"
    bpy.ops.mesh.primitive_uv_sphere_add(segments=20, ring_count=10, radius=0.09, location=(0.0, 0.0, 2.55))
    finish_part(bpy.context.object, iron).name = "WoodAnchor_CableEye"

    bpy.ops.object.select_all(action="DESELECT")
    for part in parts:
        part.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    anchor = bpy.context.object
    anchor.name = "DSZiplineWoodAnchorModel"

    # Joining primitives can duplicate slots even when they reference the same
    # material. Collapse to a stable [wood, rope, iron] contract for runtime setup.
    old_materials = list(anchor.data.materials)
    material_for_polygon = [old_materials[p.material_index] for p in anchor.data.polygons]
    anchor.data.materials.clear()
    for material in (wood, rope, iron):
        anchor.data.materials.append(material)
    indices = {material.name: index for index, material in enumerate((wood, rope, iron))}
    for polygon, material in zip(anchor.data.polygons, material_for_polygon):
        polygon.material_index = indices[material.name]
    return anchor


def prepare_tool() -> tuple[bpy.types.Object, float]:
    """Import, combine, and normalize the CC BY bolt-cutter hand tool."""
    source = Path(__file__).resolve().parents[1] / "art" / "source" / "tool-boltcutter"
    fbx = source / "Boldcutter_Lowpoly.fbx"
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(fbx))
    imported = [obj for obj in bpy.data.objects if obj not in before and obj.type == "MESH"]
    if not imported:
        raise RuntimeError("bolt-cutter FBX did not contain mesh objects")
    grip = next((obj for obj in imported if obj.name == "Handle_Lowpoly"), None)
    if grip is None:
        raise RuntimeError("bolt-cutter FBX is missing Handle_Lowpoly grip")
    grip_minimum, grip_maximum = world_bounds(grip)

    parts = [evaluated_mesh(obj, "DSZiplineToolPart") for obj in imported]
    bpy.ops.object.select_all(action="DESELECT")
    for part in parts:
        part.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    tool = bpy.context.view_layer.objects.active
    tool.name = "DSZiplineToolModel"

    # The supplied FBX has UVs but no material bindings. All parts use the same
    # baked PBR atlas, so one material/submesh is correct.
    material = bpy.data.materials.get("DSZiplineToolPBR") or bpy.data.materials.new("DSZiplineToolPBR")
    material.diffuse_color = (1.0, 1.0, 1.0, 1.0)
    material.metallic = 0.0
    material.roughness = 0.5
    tool.data.materials.clear()
    tool.data.materials.append(material)
    for polygon in tool.data.polygons:
        polygon.material_index = 0

    minimum, maximum = world_bounds(tool)
    # Normalize the 7.985-unit source to a compact 0.55 m one-handed tool. Pivot at
    # the center of one black grip so the inherited one-hand animation actually
    # closes around a handle; the jaws extend along local -X.
    scale = 0.55 / (maximum.x - minimum.x)
    origin = (grip_minimum + grip_maximum) * 0.5
    transform_mesh(tool, origin, scale)
    return tool, scale


def render_tool_icon(tool: bpy.types.Object, path: Path) -> None:
    """Render the normalized textured tool into the standard mod item-icon path."""
    source = Path(__file__).resolve().parents[1] / "art" / "source" / "tool-boltcutter" / "textures"
    icon = tool.copy()
    icon.data = tool.data.copy()
    bpy.context.collection.objects.link(icon)
    icon.rotation_euler.z = math.radians(-35.0)
    # Rotation changes the asymmetric grip-pivot bounds. Force dependency-graph
    # evaluation before framing or the icon is centered using the unrotated matrix.
    bpy.context.view_layer.update()

    material = bpy.data.materials.new("DSZiplineToolIconMaterial")
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    principled = nodes.get("Principled BSDF")

    def texture_node(suffix: str, label: str, non_color: bool = False):
        node = nodes.new("ShaderNodeTexImage")
        node.label = label
        node.image = bpy.data.images.load(
            str(source / f"GAP_2DAE03_Gärtling_Nikolas_{suffix}.jpg"),
            check_existing=True,
        )
        if non_color:
            node.image.colorspace_settings.name = "Non-Color"
        return node

    # Use the complete supplied PBR set. The old albedo-only icon discarded the
    # baked occlusion, surface scratches, roughness, and metallic response, which
    # made it look unusually clean beside vanilla inventory art.
    albedo = texture_node("C", "Tool albedo")
    ao = texture_node("AO", "Tool ambient occlusion", True)
    roughness = texture_node("R", "Tool roughness", True)
    metallic = texture_node("M", "Tool metallic", True)
    normal = texture_node("N", "Tool normal", True)
    multiply = nodes.new("ShaderNodeMixRGB")
    multiply.blend_type = "MULTIPLY"
    multiply.inputs[0].default_value = 1.0
    texcoord = nodes.new("ShaderNodeTexCoord")
    grime = nodes.new("ShaderNodeTexNoise")
    grime.inputs["Scale"].default_value = 10.0
    grime.inputs["Detail"].default_value = 6.0
    grime.inputs["Roughness"].default_value = 0.72
    grime.inputs["Distortion"].default_value = 0.2
    grime_ramp = nodes.new("ShaderNodeValToRGB")
    grime_ramp.color_ramp.elements[0].position = 0.38
    grime_ramp.color_ramp.elements[0].color = (0.12, 0.09, 0.07, 1.0)
    grime_ramp.color_ramp.elements[1].position = 0.64
    grime_ramp.color_ramp.elements[1].color = (0.95, 0.90, 0.82, 1.0)
    grime_multiply = nodes.new("ShaderNodeMixRGB")
    grime_multiply.blend_type = "MULTIPLY"
    grime_multiply.inputs[0].default_value = 1.0
    icon_grade = nodes.new("ShaderNodeHueSaturation")
    icon_grade.inputs["Saturation"].default_value = 1.18
    icon_grade.inputs["Value"].default_value = 0.65
    normal_map = nodes.new("ShaderNodeNormalMap")
    normal_map.inputs["Strength"].default_value = 0.85
    links.new(albedo.outputs["Color"], multiply.inputs[1])
    links.new(ao.outputs["Color"], multiply.inputs[2])
    links.new(texcoord.outputs["Generated"], grime.inputs["Vector"])
    links.new(grime.outputs["Fac"], grime_ramp.inputs["Fac"])
    links.new(multiply.outputs["Color"], grime_multiply.inputs[1])
    links.new(grime_ramp.outputs["Color"], grime_multiply.inputs[2])
    links.new(grime_multiply.outputs["Color"], icon_grade.inputs["Color"])
    links.new(icon_grade.outputs["Color"], principled.inputs["Base Color"])
    links.new(roughness.outputs["Color"], principled.inputs["Roughness"])
    links.new(metallic.outputs["Color"], principled.inputs["Metallic"])
    links.new(normal.outputs["Color"], normal_map.inputs["Color"])
    links.new(normal_map.outputs["Normal"], principled.inputs["Normal"])
    icon.data.materials.clear()
    icon.data.materials.append(material)

    corners = [icon.matrix_world @ Vector(corner) for corner in icon.bound_box]
    minimum = Vector(tuple(min(v[i] for v in corners) for i in range(3)))
    maximum = Vector(tuple(max(v[i] for v in corners) for i in range(3)))
    center = (minimum + maximum) * 0.5

    bpy.ops.object.camera_add(location=(center.x, center.y, center.z + 3.0))
    camera = bpy.context.object
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = max(maximum.x - minimum.x, maximum.y - minimum.y) * 1.05
    bpy.context.scene.camera = camera

    bpy.ops.object.light_add(type="AREA", location=(center.x - 1.0, center.y - 1.0, center.z + 2.0))
    key = bpy.context.object
    key.data.energy = 500.0
    key.data.shape = "DISK"
    key.data.size = 1.5

    scene = bpy.context.scene
    try:
        scene.render.engine = "BLENDER_EEVEE_NEXT"
    except TypeError:
        scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 512
    scene.render.resolution_y = 512
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(path)
    try:
        scene.view_settings.look = "AgX - Medium High Contrast"
    except TypeError:
        scene.view_settings.look = "Medium High Contrast"
    path.parent.mkdir(parents=True, exist_ok=True)

    # Hide every scene object except the derived icon, camera, and key light.
    for obj in scene.objects:
        obj.hide_render = obj not in {icon, camera, key}
    bpy.ops.render.render(write_still=True)
    print(f"DSZIPLINE_TOOL_ICON={path}")


def render_sonic_anchor_icon(anchor: bpy.types.Object, path: Path) -> None:
    """Render the normalized original Sonic post instead of a vanilla fence icon."""
    icon = anchor.copy()
    icon.data = anchor.data.copy()
    bpy.context.collection.objects.link(icon)
    icon.rotation_euler[1] = math.radians(-8.0)
    bpy.context.view_layer.update()

    # The runtime payload uses these same source-material colors. Give the icon
    # independent node-backed materials so Eevee renders the red/black model
    # faithfully rather than depending on the source scene's viewport settings.
    for index, slot in enumerate(icon.material_slots):
        source = slot.material
        if source is None:
            continue
        color = tuple(source.diffuse_color)
        material = simple_material(
            f"DSZiplineSonicIconMaterial{index}", color,
            float(source.metallic), float(source.roughness))
        add_icon_grime(material, metallic=float(source.metallic) > 0.35)
        slot.material = material

    corners = [icon.matrix_world @ Vector(corner) for corner in icon.bound_box]
    minimum = Vector(tuple(min(v[i] for v in corners) for i in range(3)))
    maximum = Vector(tuple(max(v[i] for v in corners) for i in range(3)))
    center = (minimum + maximum) * 0.5

    # The Sonic post is much broader on prepared Z than X. View mostly along X
    # so its red cap and side structure read clearly instead of edge-on.
    camera_location = center + Vector((6.4, 1.5, -2.5))
    bpy.ops.object.camera_add(location=camera_location)
    camera = bpy.context.object
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = (maximum.y - minimum.y) * 1.14
    forward = (center - camera.location).normalized()
    world_up = Vector((0.0, 1.0, 0.0))
    right = forward.cross(world_up).normalized()
    screen_up = right.cross(forward).normalized()
    camera.rotation_euler = Matrix((right, screen_up, -forward)).transposed().to_euler()
    bpy.context.scene.camera = camera

    bpy.ops.object.light_add(type="AREA", location=center + Vector((3.0, 5.0, -3.0)))
    key = bpy.context.object
    key.data.energy = 900.0
    key.data.shape = "DISK"
    key.data.size = 2.0
    key.rotation_euler = (center - key.location).to_track_quat("-Z", "Y").to_euler()

    scene = bpy.context.scene
    try:
        scene.render.engine = "BLENDER_EEVEE_NEXT"
    except TypeError:
        scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 512
    scene.render.resolution_y = 512
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(path)
    try:
        scene.view_settings.look = "AgX - Medium High Contrast"
    except TypeError:
        scene.view_settings.look = "Medium High Contrast"
    path.parent.mkdir(parents=True, exist_ok=True)
    for obj in scene.objects:
        obj.hide_render = obj not in {icon, camera, key}
    bpy.ops.render.render(write_still=True)
    print(f"DSZIPLINE_SONIC_ANCHOR_ICON={path}")


def render_wood_anchor_icon(anchor: bpy.types.Object, path: Path) -> None:
    """Render the dedicated wooden tier with procedural grain and vanilla-style contrast."""
    icon = anchor.copy()
    icon.data = anchor.data.copy()
    bpy.context.collection.objects.link(icon)
    icon.rotation_euler[1] = math.radians(-8.0)
    bpy.context.view_layer.update()

    wood = bpy.data.materials.new("DSZiplineWoodIconMaterial")
    wood.use_nodes = True
    nodes = wood.node_tree.nodes
    links = wood.node_tree.links
    principled = nodes.get("Principled BSDF")
    texcoord = nodes.new("ShaderNodeTexCoord")
    wave = nodes.new("ShaderNodeTexWave")
    wave.wave_type = "BANDS"
    wave.bands_direction = "X"
    wave.inputs["Scale"].default_value = 8.0
    wave.inputs["Distortion"].default_value = 7.0
    wave.inputs["Detail"].default_value = 5.0
    wave.inputs["Detail Scale"].default_value = 2.5
    ramp = nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (0.055, 0.018, 0.006, 1.0)
    ramp.color_ramp.elements[0].position = 0.18
    ramp.color_ramp.elements[1].color = (0.38, 0.16, 0.045, 1.0)
    ramp.color_ramp.elements[1].position = 0.78
    bump = nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.38
    bump.inputs["Distance"].default_value = 0.12
    links.new(texcoord.outputs["Generated"], wave.inputs["Vector"])
    links.new(wave.outputs["Color"], ramp.inputs["Fac"])
    add_icon_grime(wood, ramp.outputs["Color"])
    links.new(wave.outputs["Color"], bump.inputs["Height"])
    links.new(bump.outputs["Normal"], principled.inputs["Normal"])
    principled.inputs["Roughness"].default_value = 0.82
    icon.data.materials[0] = wood

    # Rope and forged hardware need the same weathered treatment as the timber;
    # otherwise the extra survivor-built details read as pristine plastic.
    for index in range(1, len(icon.data.materials)):
        source = icon.data.materials[index]
        if source is None:
            continue
        material = simple_material(
            f"DSZiplineWoodDetailIconMaterial{index}", tuple(source.diffuse_color),
            float(source.metallic), float(source.roughness))
        add_icon_grime(material, metallic=float(source.metallic) > 0.35)
        icon.data.materials[index] = material

    corners = [icon.matrix_world @ Vector(corner) for corner in icon.bound_box]
    minimum = Vector(tuple(min(v[i] for v in corners) for i in range(3)))
    maximum = Vector(tuple(max(v[i] for v in corners) for i in range(3)))
    center = (minimum + maximum) * 0.5

    # Prepared runtime geometry is Unity-oriented (Y-up), even though Blender's
    # viewport remains Z-up. Orbit in X/Z and use only a modest positive Y lift
    # so the tall mast reads upright rather than pointing into the camera.
    camera_location = center + Vector((4.2, 1.5, -6.4))
    bpy.ops.object.camera_add(location=camera_location)
    camera = bpy.context.object
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = (maximum.y - minimum.y) * 1.14
    forward = (center - camera.location).normalized()
    world_up = Vector((0.0, 1.0, 0.0))
    right = forward.cross(world_up).normalized()
    screen_up = right.cross(forward).normalized()
    camera.rotation_euler = Matrix((right, screen_up, -forward)).transposed().to_euler()
    bpy.context.scene.camera = camera

    bpy.ops.object.light_add(type="AREA", location=center + Vector((-3.0, 5.0, -4.0)))
    key = bpy.context.object
    key.data.energy = 650.0
    key.data.shape = "DISK"
    key.data.size = 2.2
    key.rotation_euler = (center - key.location).to_track_quat("-Z", "Y").to_euler()

    scene = bpy.context.scene
    try:
        scene.render.engine = "BLENDER_EEVEE_NEXT"
    except TypeError:
        scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 512
    scene.render.resolution_y = 512
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(path)
    try:
        scene.view_settings.look = "AgX - Medium High Contrast"
    except TypeError:
        scene.view_settings.look = "Medium High Contrast"
    path.parent.mkdir(parents=True, exist_ok=True)
    for obj in scene.objects:
        obj.hide_render = obj not in {icon, camera, key}
    bpy.ops.render.render(write_still=True)
    print(f"DSZIPLINE_WOOD_ANCHOR_ICON={path}")


def write_string(stream, value: str) -> None:
    encoded = value.encode("utf-8")
    stream.write(struct.pack("<I", len(encoded)))
    stream.write(encoded)


def unity_vector(value: Vector) -> tuple[float, float, float]:
    """Map prepared FBX coordinates to the coordinates Unity's importer produced."""
    return value.x, value.y, -value.z


def write_mesh(stream, name: str, obj: bpy.types.Object) -> None:
    mesh = obj.data
    mesh.calc_loop_triangles()
    uv_layer = mesh.uv_layers.active.data if mesh.uv_layers.active else None
    corner_normals = mesh.corner_normals
    vertices = []
    lookup = {}
    submeshes = [[] for _ in mesh.materials]

    for triangle in mesh.loop_triangles:
        material_index = min(triangle.material_index, len(submeshes) - 1)
        triangle_indices = []
        for loop_index in triangle.loops:
            loop = mesh.loops[loop_index]
            position = unity_vector(mesh.vertices[loop.vertex_index].co)
            normal = unity_vector(corner_normals[loop_index].vector)
            uv = tuple(uv_layer[loop_index].uv) if uv_layer else (0.0, 0.0)
            values = position + normal + uv
            key = tuple(round(value, 7) for value in values)
            index = lookup.get(key)
            if index is None:
                index = len(vertices)
                lookup[key] = index
                vertices.append(values)
            triangle_indices.append(index)

        # unity_vector reflects FBX Z (negative determinant), which reverses
        # handedness. Swap winding so Unity culls the inside and renders the
        # intended outer faces instead of the view-dependent cross-section.
        submeshes[material_index].extend((
            triangle_indices[0], triangle_indices[2], triangle_indices[1]))

    write_string(stream, name)
    stream.write(struct.pack("<I", len(vertices)))
    for vertex in vertices:
        stream.write(struct.pack("<8f", *vertex))

    stream.write(struct.pack("<I", len(mesh.materials)))
    for material in mesh.materials:
        color = tuple(material.diffuse_color)
        stream.write(struct.pack(
            "<6f", color[0], color[1], color[2], color[3],
            float(material.metallic), 1.0 - float(material.roughness)))
    for indices in submeshes:
        stream.write(struct.pack("<I", len(indices)))
        if indices:
            stream.write(struct.pack(f"<{len(indices)}I", *indices))

    print(f"DSZIPLINE_MESHBIN_MODEL={name} vertices={len(vertices)} triangles={sum(map(len, submeshes)) // 3}")


def write_meshbin(path: Path, objects: list[tuple[str, bpy.types.Object]]) -> None:
    with path.open("wb") as stream:
        stream.write(b"DSZM")
        stream.write(struct.pack("<II", 1, len(objects)))
        for name, obj in objects:
            write_mesh(stream, name, obj)
    print(f"DSZIPLINE_MESHBIN={path} bytes={path.stat().st_size}")


def main() -> None:
    out, meshbin = output_paths()
    normalize_materials()

    post = bpy.data.objects.get("ZipPostLeave")
    trolley = bpy.data.objects.get("Trolley")
    cable = bpy.data.objects.get("ZipLine")
    if not post or not trolley or not cable:
        raise RuntimeError("expected ZipPostLeave, Trolley, and ZipLine in source scene")

    post_min, post_max = world_bounds(post)
    cable_min, cable_max = world_bounds(cable)
    cable_center = (cable_min + cable_max) * 0.5

    # Match ZiplineLink.AnchorOffset.y exactly.  The source post's bottom is the
    # reusable prefab origin and the source cable center is its attachment point.
    scale = 2.55 / (cable_center.z - post_min.z)
    post_origin = Vector(((post_min.x + post_max.x) * 0.5,
                          (post_min.y + post_max.y) * 0.5,
                          post_min.z))

    anchor_out = evaluated_mesh(post, "DSZiplineAnchorModel")
    transform_mesh(anchor_out, post_origin, scale)
    export_fbx(anchor_out, out / "DSZiplineAnchor.fbx")

    wood_anchor_out = create_wood_anchor()
    transform_mesh(wood_anchor_out, Vector((0.0, 0.0, 0.0)), 1.0)
    export_fbx(wood_anchor_out, out / "DSZiplineWoodAnchor.fbx")

    trolley_min, trolley_max = world_bounds(trolley)
    trolley_origin = Vector(((trolley_min.x + trolley_max.x) * 0.5,
                             cable_center.y,
                             cable_center.z))
    trolley_out = evaluated_mesh(trolley, "DSZiplineTrolleyModel")
    # Source cable travels toward -X; rotate that direction to Blender +Y,
    # which the FBX Unity convention imports as local +Z (forward).
    transform_mesh(trolley_out, trolley_origin, scale, -math.pi / 2.0)
    export_fbx(trolley_out, out / "DSZiplineTrolley.fbx")

    cable_out = evaluated_mesh(cable, "DSZiplineCableReferenceModel")
    cable_start = Vector((cable_max.x, cable_center.y, cable_center.z))
    transform_mesh(cable_out, cable_start, scale, -math.pi / 2.0)
    export_fbx(cable_out, out / "DSZiplineCableReference.fbx")

    tool_out, tool_scale = prepare_tool()
    export_fbx(tool_out, out / "DSZiplineTool.fbx")
    icon_dir = Path(__file__).resolve().parents[1] / "server" / "UIAtlases" / "ItemIconAtlas"
    render_tool_icon(tool_out, icon_dir / "DSZiplineTool.png")
    render_sonic_anchor_icon(anchor_out, icon_dir / "DSZiplineAnchor.png")
    render_wood_anchor_icon(wood_anchor_out, icon_dir / "DSZiplineWoodAnchor.png")

    write_meshbin(meshbin, [
        ("DSZiplineAnchor", anchor_out),
        ("DSZiplineWoodAnchor", wood_anchor_out),
        ("DSZiplineTrolley", trolley_out),
        ("DSZiplineCableReference", cable_out),
        ("DSZiplineTool", tool_out),
    ])

    print(f"DSZIPLINE_MODEL_SCALE={scale:.9f}")
    print(f"DSZIPLINE_TOOL_SCALE={tool_scale:.9f}")
    print(f"DSZIPLINE_ANCHOR_SIZE={tuple(round(v * scale, 5) for v in (post_max - post_min))}")
    print(f"DSZIPLINE_CABLE_LENGTH={(cable_max.x - cable_min.x) * scale:.5f}")
    print(f"Exported Unity-ready FBX files to {out}")


if __name__ == "__main__":
    main()
