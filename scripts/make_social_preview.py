"""Produce a 1280x640 (2:1) social preview image from the hero banner.

GitHub's social preview slot is 1280x640. The hero is 1536x1024 (3:2), so we
center-crop to a 2:1 region (keeping the icon + wordmark band) then resize.
"""
from PIL import Image

SRC = r"C:\git\家里\ServicePilot\Assets\servicepilot-hero.png"
DST = r"C:\git\家里\ServicePilot\Assets\servicepilot-social.png"

im = Image.open(SRC).convert("RGB")
w, h = im.size  # 1536 x 1024

target_ratio = 1280 / 640  # 2.0
# Crop full width, take a centered 2:1 slice of the height.
crop_h = int(round(w / target_ratio))  # 768
top = (h - crop_h) // 2
box = im.crop((0, top, w, top + crop_h))
out = box.resize((1280, 640), Image.LANCZOS)
out.save(DST, format="PNG")
print("wrote", DST, out.size)
