"""Draws the DriverHaven icon: a shield (the "haven") holding a chip with an upward arrow (driver updates).

Writes src/DriverUpdater.App/Assets/DriverHaven.ico (all Windows icon sizes) and DriverHaven-256.png (preview).
Each size is drawn at 8x and downsampled; sizes up to 32 px use a simplified design so they stay crisp.

    python -I tools/icon/make_icon.py
"""
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "src" / "DriverUpdater.App" / "Assets"
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SUPERSAMPLE = 8

TOP_COLOR = (20, 184, 166)     # teal
BOTTOM_COLOR = (29, 78, 216)   # blue
HALO_COLOR = (23, 62, 160)     # separates the arrow from the chip outline
WHITE = (255, 255, 255, 255)


def shield_points(s: float) -> list[tuple[float, float]]:
    """Flat top with rounded corners, straight sides, curving to a point at the bottom."""
    x0, x1, y0 = 0.12 * s, 0.88 * s, 0.06 * s
    side_end, tip = 0.50 * s, (0.5 * s, 0.95 * s)
    r = 0.10 * s

    def quad(p0, c, p1, steps=40):
        return [((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * c[0] + t * t * p1[0],
                 (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * c[1] + t * t * p1[1])
                for t in (i / steps for i in range(steps + 1))]

    pts = quad((x0, y0 + r), (x0, y0), (x0 + r, y0))              # top-left corner
    pts += quad((x1 - r, y0), (x1, y0), (x1, y0 + r))             # top-right corner
    pts += quad((x1, side_end), (x1, 0.80 * s), tip)              # right side to tip
    pts += quad(tip, (x0, 0.80 * s), (x0, side_end))              # tip back up the left side
    return pts


def arrow_points(s: float, simple: bool) -> list[tuple[float, float]]:
    cx = 0.5 * s
    head_top = 0.17 * s if simple else 0.16 * s
    head_bottom = 0.47 * s if simple else 0.42 * s
    head_half = 0.25 * s if simple else 0.21 * s
    shaft_half = 0.095 * s if simple else 0.075 * s
    shaft_bottom = 0.78 * s if simple else 0.66 * s
    return [(cx, head_top), (cx + head_half, head_bottom), (cx + shaft_half, head_bottom),
            (cx + shaft_half, shaft_bottom), (cx - shaft_half, shaft_bottom), (cx - shaft_half, head_bottom),
            (cx - head_half, head_bottom)]


def draw_chip(draw: ImageDraw.ImageDraw, s: float) -> None:
    """A rounded square with three pins on each side, in the lower half of the shield."""
    size, cx, cy = 0.30 * s, 0.5 * s, 0.62 * s
    stroke = max(1, round(0.032 * s))
    left, top, right, bottom = cx - size / 2, cy - size / 2, cx + size / 2, cy + size / 2
    draw.rounded_rectangle((left, top, right, bottom), radius=0.05 * s, outline=WHITE, width=stroke)

    pin_len = 0.06 * s
    for i in (-1, 0, 1):
        offset = i * size * 0.28
        draw.line((left - pin_len, cy + offset, left, cy + offset), fill=WHITE, width=stroke)
        draw.line((right, cy + offset, right + pin_len, cy + offset), fill=WHITE, width=stroke)
        draw.line((cx + offset, bottom, cx + offset, bottom + pin_len), fill=WHITE, width=stroke)


def render(size: int) -> Image.Image:
    s = size * SUPERSAMPLE
    simple = size <= 32

    # Shield filled with a vertical teal-to-blue gradient.
    gradient = Image.new("RGBA", (1, 256))
    for y in range(256):
        t = y / 255
        gradient.putpixel((0, y), tuple(round(a + (b - a) * t) for a, b in zip(TOP_COLOR, BOTTOM_COLOR)) + (255,))
    gradient = gradient.resize((s, s))
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).polygon(shield_points(s), fill=255)
    image = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    image.paste(gradient, (0, 0), mask)

    draw = ImageDraw.Draw(image)
    if not simple:
        draw_chip(draw, s)

    # The arrow gets a dark halo where it crosses the chip, so the two shapes read separately.
    arrow_mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(arrow_mask).polygon(arrow_points(s, simple), fill=255)
    if not simple:
        halo_px = round(0.035 * s) * 2 + 1
        halo = arrow_mask.filter(ImageFilter.MaxFilter(halo_px))
        halo = ImageChops.multiply(halo, mask)
        image.paste(Image.new("RGBA", (s, s), HALO_COLOR + (255,)), (0, 0), halo)
    image.paste(Image.new("RGBA", (s, s), WHITE), (0, 0), arrow_mask)

    return image.resize((size, size), Image.LANCZOS)


def main() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    images = {size: render(size) for size in SIZES}

    ico_path = ASSETS / "DriverHaven.ico"
    largest = images[SIZES[-1]]
    largest.save(ico_path, format="ICO", sizes=[(n, n) for n in SIZES],
                 append_images=[images[n] for n in SIZES[:-1]])
    largest.save(ASSETS / "DriverHaven-256.png")

    with Image.open(ico_path) as ico:
        print(f"{ico_path.relative_to(ROOT)}: sizes {sorted(ico.info['sizes'])}")


if __name__ == "__main__":
    main()
