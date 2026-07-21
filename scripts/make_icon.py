"""Build clean transparent icons (app.ico + app.png) from the V1 source.

The V1 source has a WHITE (opaque) background around the teal squircle, which
shows up as a white halo in the title bar and taskbar. We detect the teal
squircle bounds, cut everything outside a rounded-rect mask to transparent, and
re-export a centered square with a small transparent margin.
"""
from PIL import Image, ImageDraw

SRC = r"C:\Users\11467\.cursor\projects\c-git-ServicePilot\assets\servicepilot_icon_v1.png"
ICO = r"C:\git\家里\ServicePilot\ServicePilot\Resources\Icons\app.ico"
PNG = r"C:\git\家里\ServicePilot\ServicePilot\Resources\Icons\app.png"

img = Image.open(SRC).convert("RGBA")
w, h = img.size
px = img.load()


def is_teal(r, g, b):
    return g > 120 and b > 120 and r < 160 and (g + b) - 2 * r > 60


# Detect teal squircle bounds robustly across several scan lines.
lefts, rights, tops, bottoms = [], [], [], []
for frac in [0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8]:
    y = int(h * frac)
    row = [i for i in range(w) if is_teal(*px[(i, y)][:3])]
    if row:
        lefts.append(row[0]); rights.append(row[-1])
    x = int(w * frac)
    col = [i for i in range(h) if is_teal(*px[(x, i)][:3])]
    if col:
        tops.append(col[0]); bottoms.append(col[-1])

left, right = min(lefts), max(rights)
top, bottom = min(tops), max(bottoms)

# Crop to the squircle bounding box (tiny outward pad to keep the teal edge).
pad = 2
left = max(0, left - pad); top = max(0, top - pad)
right = min(w - 1, right + pad); bottom = min(h - 1, bottom + pad)
box = img.crop((left, top, right + 1, bottom + 1))
bw, bh = box.size

# Build a rounded-rectangle (squircle-ish) alpha mask and apply it so the white
# corners/halo become fully transparent.
radius = int(min(bw, bh) * 0.22)
mask = Image.new("L", (bw, bh), 0)
d = ImageDraw.Draw(mask)
d.rounded_rectangle([0, 0, bw - 1, bh - 1], radius=radius, fill=255)

# Combine with the existing alpha (in case some was already transparent).
r, g, b, a = box.split()
from PIL import ImageChops
new_alpha = ImageChops.multiply(a, mask)
box = Image.merge("RGBA", (r, g, b, new_alpha))

# Center on a square canvas with a small transparent margin.
side = max(bw, bh)
margin = int(side * 0.05)
canvas = side + margin * 2
square = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
square.paste(box, ((canvas - bw) // 2, (canvas - bh) // 2), box)

# Export multi-size ICO (exe + taskbar) and a single clean PNG (title bar).
master = square.resize((256, 256), Image.LANCZOS)
sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
master.save(ICO, format="ICO", sizes=sizes)
square.resize((128, 128), Image.LANCZOS).save(PNG)
print("wrote", ICO)
print("wrote", PNG)

# Report corner alpha to confirm no white halo remains.
chk = square.resize((32, 32), Image.LANCZOS)
print("corner (0,0):", chk.getpixel((0, 0)))
print("corner (2,2):", chk.getpixel((2, 2)))
print("center      :", chk.getpixel((16, 16)))
