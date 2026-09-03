#!/usr/bin/env python3
"""Verify the Blender source, generated FBX files, and runtime mesh payload."""
from pathlib import Path
import struct

ROOT = Path(__file__).resolve().parents[1]
EXPECTED = {
    "DSZiplineAnchor": ((0.10971, 2.71836, 0.69974), 3813),
    "DSZiplineWoodAnchor": ((0.78, 2.64, 0.38), 3156),
    "DSZiplineTrolley": ((0.55, 0.66, 0.19), 5386),
    "DSZiplineCableReference": ((0.01, 0.01, 12.91895), 1104),
    "DSZiplineTool": ((0.55, 0.12, 0.04), 6610),
}


def read_exact(stream, size):
    value = stream.read(size)
    assert len(value) == size, "truncated runtime mesh payload"
    return value


def read_u32(stream):
    return struct.unpack("<I", read_exact(stream, 4))[0]


def read_string(stream):
    return read_exact(stream, read_u32(stream)).decode("utf-8")


def main() -> None:
    source = ROOT / "art/source/sonic_zipline_original.blend"
    assert source.is_file() and source.stat().st_size > 100_000, "missing source Blender scene"

    tool_source = ROOT / "art/source/tool-boltcutter"
    assert (tool_source / "Boldcutter_Lowpoly.fbx").stat().st_size > 100_000, "missing bolt-cutter FBX"
    for suffix in ("C", "N", "R", "AO", "M"):
        texture = tool_source / "textures" / f"GAP_2DAE03_Gärtling_Nikolas_{suffix}.jpg"
        assert texture.stat().st_size > 100_000, f"missing bolt-cutter {suffix} texture"
    generated_tool = ROOT / "art/generated/tool"
    for name in ("tool_albedo.jpg", "tool_ao.jpg", "tool_normal_dxt.png", "tool_metallic_smoothness.png"):
        texture = generated_tool / name
        assert texture.is_file() and texture.stat().st_size > 100_000, f"missing generated tool texture: {name}"

    for icon_name in ("DSZiplineTool.png", "DSZiplineAnchor.png", "DSZiplineWoodAnchor.png"):
        icon = ROOT / "server/UIAtlases/ItemIconAtlas" / icon_name
        assert icon.is_file() and icon.stat().st_size > 10_000, f"missing generated icon: {icon_name}"
        with icon.open("rb") as stream:
            assert read_exact(stream, 8) == b"\x89PNG\r\n\x1a\n"
            assert read_exact(stream, 4) == b"\x00\x00\x00\r" and read_exact(stream, 4) == b"IHDR"
            assert struct.unpack(">II", read_exact(stream, 8)) == (512, 512), f"{icon_name} must be 512x512"

    for model in ("DSZiplineAnchor.fbx", "DSZiplineWoodAnchor.fbx", "DSZiplineTrolley.fbx", "DSZiplineCableReference.fbx", "DSZiplineTool.fbx"):
        path = ROOT / "unity/DSZiplineAssets/Assets/Models" / model
        assert path.is_file() and path.stat().st_size > 1_000, f"missing generated model: {model}"

    payload = ROOT / "art/generated/dszipline.meshbin"
    assert payload.is_file(), "missing runtime mesh payload"
    found = {}
    with payload.open("rb") as stream:
        assert read_exact(stream, 4) == b"DSZM", "invalid runtime mesh magic"
        assert read_u32(stream) == 1, "unsupported runtime mesh version"
        for _ in range(read_u32(stream)):
            name = read_string(stream)
            vertex_count = read_u32(stream)
            vertices = [struct.unpack("<8f", read_exact(stream, 32)) for _ in range(vertex_count)]
            material_count = read_u32(stream)
            read_exact(stream, material_count * 24)
            outward = 0
            triangles = 0
            for _ in range(material_count):
                index_count = read_u32(stream)
                assert index_count % 3 == 0
                indices = struct.unpack(f"<{index_count}I", read_exact(stream, index_count * 4)) if index_count else ()
                assert all(index < vertex_count for index in indices)
                for offset in range(0, index_count, 3):
                    a, b, c = (vertices[indices[offset + i]] for i in range(3))
                    edge1 = tuple(b[i] - a[i] for i in range(3))
                    edge2 = tuple(c[i] - a[i] for i in range(3))
                    cross = (
                        edge1[1] * edge2[2] - edge1[2] * edge2[1],
                        edge1[2] * edge2[0] - edge1[0] * edge2[2],
                        edge1[0] * edge2[1] - edge1[1] * edge2[0],
                    )
                    normal = tuple(a[i + 3] + b[i + 3] + c[i + 3] for i in range(3))
                    outward += sum(cross[i] * normal[i] for i in range(3)) >= -1e-8
                    triangles += 1
            size = tuple(max(v[i] for v in vertices) - min(v[i] for v in vertices) for i in range(3))
            assert triangles and outward / triangles > 0.99, f"{name} has reversed triangle winding"
            found[name] = (size, vertex_count)
        assert stream.read(1) == b"", "unexpected trailing runtime mesh data"

    assert found.keys() == EXPECTED.keys(), f"unexpected runtime models: {sorted(found)}"
    for name, (expected_size, expected_vertices) in EXPECTED.items():
        size, vertices = found[name]
        assert vertices == expected_vertices, f"{name} vertex count changed: {vertices}"
        assert all(abs(actual - expected) < 0.02 for actual, expected in zip(size, expected_size)), (
            f"{name} dimensions changed: {size}"
        )

    print(f"DS_Zipline runtime mesh checks passed ({payload.stat().st_size / 1024:.0f} KiB, {len(found)} models)")


if __name__ == "__main__":
    main()
