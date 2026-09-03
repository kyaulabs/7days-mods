#!/usr/bin/env python3
"""Build deterministic 1024px Unity runtime maps from the CC BY tool textures."""
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "art/source/tool-boltcutter/textures"
OUTPUT = ROOT / "art/generated/tool"
SIZE = (1024, 1024)
PREFIX = "GAP_2DAE03_Gärtling_Nikolas_"


def load(suffix: str, mode: str) -> Image.Image:
    with Image.open(SOURCE / f"{PREFIX}{suffix}.jpg") as image:
        return image.convert(mode).resize(SIZE, Image.Resampling.LANCZOS)


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    load("C", "RGB").save(OUTPUT / "tool_albedo.jpg", quality=92, optimize=True, progressive=False)
    load("AO", "L").save(OUTPUT / "tool_ao.jpg", quality=92, optimize=True, progressive=False)

    source_normal = load("N", "RGB")
    red, green, _ = source_normal.split()
    opaque = Image.new("L", SIZE, 255)
    # Unity Standard's desktop normal unpack reads X from alpha and Y from green.
    Image.merge("RGBA", (opaque, green, opaque, red)).save(
        OUTPUT / "tool_normal_dxt.png", optimize=True, compress_level=9)

    metallic = load("M", "L")
    roughness = load("R", "L")
    smoothness = roughness.point(lambda value: 255 - value)
    zero = Image.new("L", SIZE, 0)
    Image.merge("RGBA", (metallic, zero, zero, smoothness)).save(
        OUTPUT / "tool_metallic_smoothness.png", optimize=True, compress_level=9)

    for path in sorted(OUTPUT.iterdir()):
        print(f"generated {path.relative_to(ROOT)} ({path.stat().st_size // 1024} KiB)")


if __name__ == "__main__":
    main()
