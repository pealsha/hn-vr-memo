"""Generate the HnMemo application icon (D1: shared prototype base + open note frame and pen)."""
import argparse
import io
import math
import struct
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src/HandwriteMemo.App/Assets"
SIZES = (16, 24, 32, 48, 64, 256)

# Prototype palette (product-icon-base) + UiDesign.Ink
BEIGE = (233, 221, 203, 255)   # #E9DDCB
PURPLE = (136, 98, 156, 255)   # #88629C
INK = (31, 34, 41, 255)        # #1F2229


def make_base(size: int, scale: int = 4) -> Image.Image:
    """Draw the rounded-square beige base with the upper-right purple arc."""
    canvas_size = size * scale
    corner_radius = canvas_size * (16.0 / 112.0)
    mask = Image.new("L", (canvas_size, canvas_size), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        (0, 0, canvas_size - 1, canvas_size - 1), radius=corner_radius, fill=255
    )

    base = Image.new("RGBA", (canvas_size, canvas_size), BEIGE)
    draw = ImageDraw.Draw(base)
    draw.rectangle((canvas_size // 2, 0, canvas_size, canvas_size // 2), fill=PURPLE)
    draw.ellipse((0, 0, canvas_size - 1, canvas_size - 1), fill=BEIGE)

    clipped = Image.new("RGBA", (canvas_size, canvas_size), (0, 0, 0, 0))
    clipped.paste(base, (0, 0), mask)
    return clipped


def draw_smooth_stroke(draw: ImageDraw.ImageDraw, points: list[tuple[float, float]], fill: tuple[int, int, int, int], width: float) -> None:
    radius = width / 2.0
    for i in range(len(points) - 1):
        draw.line([points[i], points[i + 1]], fill=fill, width=max(1, round(width)))
    for x, y in points:
        draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=fill)


def arc_points(cx: float, cy: float, radius: float, start_deg: float, end_deg: float, steps: int = 12) -> list[tuple[float, float]]:
    return [
        (
            cx + math.cos(math.radians(start_deg + (end_deg - start_deg) * i / steps)) * radius,
            cy + math.sin(math.radians(start_deg + (end_deg - start_deg) * i / steps)) * radius,
        )
        for i in range(steps + 1)
    ]


def draw_pen_layer(canvas_size: int, unit: float, tip_x: float, tip_y: float, length: float, width: float, angle_deg: float) -> Image.Image:
    layer = Image.new("RGBA", (canvas_size, canvas_size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    rad = math.radians(angle_deg)
    dx, dy = math.cos(rad), math.sin(rad)
    nx, ny = -dy, dx

    def point(along: float, across: float) -> tuple[float, float]:
        return ((tip_x + dx * along + nx * across) * unit, (tip_y + dy * along + ny * across) * unit)

    half_width = width / 2.0
    nib_length = width * 1.05
    draw.polygon(
        [
            point(0, 0),
            point(nib_length, -half_width),
            point(length, -half_width),
            point(length, half_width),
            point(nib_length, half_width),
        ],
        fill=INK,
    )
    cap_x, cap_y = point(length, 0)
    cap_r = half_width * unit
    draw.ellipse((cap_x - cap_r, cap_y - cap_r, cap_x + cap_r, cap_y + cap_r), fill=INK)
    for start, end in ((nib_length + 4, nib_length + 12), (length - 16, length - 9)):
        draw.polygon(
            [
                point(start, -half_width - 2),
                point(end, -half_width - 2),
                point(end, half_width + 2),
                point(start, half_width + 2),
            ],
            fill=(0, 0, 0, 0),
        )
    return layer


def render_icon(size: int) -> Image.Image:
    scale = 4
    canvas_size = size * scale
    image = make_base(size, scale)
    draw = ImageDraw.Draw(image)
    unit = canvas_size / 256.0
    stroke_width = (15 if size >= 32 else 20) * unit

    left, top, right, bottom, corner = 48 * unit, 64 * unit, 184 * unit, 200 * unit, 22 * unit
    frame_points = (
        [(118 * unit, top)]
        + arc_points(left + corner, top + corner, corner, -90, -180)
        + arc_points(left + corner, bottom - corner, corner, 180, 90)
        + arc_points(right - corner, bottom - corner, corner, 90, 0)
        + [(right, 134 * unit)]
    )
    draw_smooth_stroke(draw, frame_points, INK, stroke_width)
    pen = draw_pen_layer(
        canvas_size,
        unit,
        tip_x=96,
        tip_y=154,
        length=114,
        width=30 if size >= 32 else 34,
        angle_deg=-45,
    )
    image.alpha_composite(pen)
    return image.resize((size, size), Image.Resampling.LANCZOS)


def write_ico(path: Path) -> None:
    frames = []
    for size in SIZES:
        buffer = io.BytesIO()
        render_icon(size).save(buffer, format="PNG")
        frames.append((size, buffer.getvalue()))

    offset = 6 + 16 * len(frames)
    directory = bytearray(struct.pack("<HHH", 0, 1, len(frames)))
    for size, data in frames:
        directory += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    path.write_bytes(directory + b"".join(data for _, data in frames))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-directory", default="")
    args = parser.parse_args()

    ASSETS.mkdir(parents=True, exist_ok=True)
    write_ico(ASSETS / "HnMemo.ico")

    if args.output_directory:
        out_dir = ROOT / args.output_directory
        out_dir.mkdir(parents=True, exist_ok=True)
        for size in SIZES:
            render_icon(size).save(out_dir / f"HnMemo-{size}.png")


if __name__ == "__main__":
    main()
