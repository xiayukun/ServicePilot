"""Build square-canvas transparent icons (app.ico + app.png).

The preferred source already has an alpha channel, but its artwork still
contains an opaque white plate and a thin white matte around the teal squircle.
Keep a full square canvas, isolate the teal artwork with an alpha mask, and
scale the visible artwork back to its previous size inside that canvas. This
keeps the four corners as transparent pixels instead of relying on a cropped
bounding box. The old opaque V1 source remains a fallback.

Each output size is resized independently with premultiplied alpha so hidden
source background pixels cannot bleed into the transparent edge.
"""
import os

from PIL import Image, ImageChops, ImageDraw

SRC = r"C:\Users\11467\.cursor\projects\c-git-ServicePilot\assets\servicepilot_icon_final.png"
LEGACY_SRC = r"C:\Users\11467\.cursor\projects\c-git-ServicePilot\assets\servicepilot_icon_v1.png"
ICO = r"C:\git\家里\ServicePilot\ServicePilot\Resources\Icons\app.ico"
PNG = r"C:\git\家里\ServicePilot\ServicePilot\Resources\Icons\app.png"


def is_teal(r, g, b):
    return g > 120 and b > 120 and r < 160 and (g + b) - 2 * r > 60


def find_teal_bounds(image):
    """Find the teal squircle bounds without including the white plate."""
    width, height = image.size
    pixels = image.load()
    lefts, rights, tops, bottoms = [], [], [], []
    for fraction in [0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8]:
        y = int(height * fraction)
        row = [x for x in range(width) if is_teal(*pixels[x, y][:3])]
        if row:
            lefts.append(row[0]); rights.append(row[-1])
        x = int(width * fraction)
        column = [y for y in range(height) if is_teal(*pixels[x, y][:3])]
        if column:
            tops.append(column[0]); bottoms.append(column[-1])
    if not lefts or not tops:
        raise RuntimeError(f"could not find teal artwork bounds in {image.size}")
    return min(lefts), max(rights), min(tops), max(bottoms)


def resize_rgba(image, size):
    """Resize through premultiplied-alpha pixels to prevent edge color bleed."""
    return image.convert("RGBa").resize(size, Image.Resampling.LANCZOS).convert("RGBA")


def build_square_canvas(image, edge_fraction, content_fraction):
    """Keep a full source-sized canvas with a transparent outer border."""
    left, right, top, bottom = find_teal_bounds(image)
    left = max(0, left); top = max(0, top)
    right = min(image.width - 1, right); bottom = min(image.height - 1, bottom)
    box = image.crop((left, top, right + 1, bottom + 1))
    box_width, box_height = box.size

    edge_trim = max(1, int(min(box_width, box_height) * edge_fraction))
    radius = max(1, int(min(box_width, box_height) * 0.22) - edge_trim)
    mask = Image.new("L", (box_width, box_height), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [edge_trim, edge_trim, box_width - 1 - edge_trim, box_height - 1 - edge_trim],
        radius=radius,
        fill=255,
    )
    red, green, blue, alpha = box.split()
    box = Image.merge("RGBA", (red, green, blue, ImageChops.multiply(alpha, mask)))

    # Normalize the nearly-square artwork, then place it on the original
    # square canvas. The visible subject remains about 91% of the canvas, as
    # in the previous cropped output, while all four canvas corners are alpha 0.
    artwork_side = max(box_width, box_height)
    artwork = Image.new("RGBA", (artwork_side, artwork_side), (0, 0, 0, 0))
    artwork.alpha_composite(
        box,
        dest=((artwork_side - box_width) // 2, (artwork_side - box_height) // 2),
    )
    target_side = max(1, int(round(image.width * content_fraction)))
    artwork = resize_rgba(artwork, (target_side, target_side))
    square = Image.new("RGBA", image.size, (0, 0, 0, 0))
    square.alpha_composite(
        artwork,
        dest=((image.width - target_side) // 2, (image.height - target_side) // 2),
    )
    return square, edge_trim, target_side


source_path = SRC if os.path.exists(SRC) else LEGACY_SRC
source = Image.open(source_path).convert("RGBA")
square, edge_trim, target_side = build_square_canvas(
    source,
    0.02 if source_path == SRC else 0.08,
    0.91,
)


# Export multi-size ICO (exe + taskbar) and a clean PNG (title bar).
sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
frames = [resize_rgba(square, size) for size in sizes]
frames[-1].save(ICO, format="ICO", sizes=sizes, append_images=frames[:-1])
frames[sizes.index((128, 128))].save(PNG)
print("source", source_path)
print("edge trim", edge_trim)
print("canvas", square.size, "visible target", target_side)
print("wrote", ICO)
print("wrote", PNG)

# Report corner alpha to confirm no opaque halo remains.
chk = frames[sizes.index((32, 32))]
print("corner (0,0):", chk.getpixel((0, 0)))
print("corner (2,2):", chk.getpixel((2, 2)))
print("center      :", chk.getpixel((16, 16)))
