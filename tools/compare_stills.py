"""Quality-level comparison sheets (phase 4) from VersionStills (Logs/Quality/<level>_<view>.png, 1600x900, 60 deg)
-> docs/images/quality_compare.png (whole views) and docs/images/quality_detail.png (2x zoom on the tractor).

Usage: python tools/compare_stills.py   (after Tractor VR > Render Quality Comparison)
"""
from PIL import Image, ImageDraw, ImageFont

SRC = "Logs/Quality"
LEVELS = [("quest", "Quest standalone (quality level Quest)"), ("pc", "PC VR + PC desktop (quality level PC)")]
W, H, PAD, HEAD = 960, 540, 12, 46


def font(size):
    for f in ("arialbd.ttf", "DejaVuSans-Bold.ttf"):
        try:
            return ImageFont.truetype(f, size)
        except OSError:
            pass
    return ImageFont.load_default()


def sheet(views, crop, out, title):
    """crop: (left, top, right, bottom) in still pixels, or None for the whole still."""
    img = Image.new("RGB", (PAD + len(LEVELS) * (W + PAD), HEAD + PAD + len(views) * (H + PAD)), (24, 24, 24))
    d = ImageDraw.Draw(img)
    d.text((PAD, 10), title, fill=(240, 235, 200), font=font(22))
    for c, (level, label) in enumerate(LEVELS):
        x = PAD + c * (W + PAD)
        for r, view in enumerate(views):
            im = Image.open(f"{SRC}/{level}_{view}.png").convert("RGB")
            if crop:
                im = im.crop(crop)
            im = im.resize((W, H), Image.NEAREST if crop else Image.LANCZOS)
            y = HEAD + PAD + r * (H + PAD)
            img.paste(im, (x, y))
            text = f"{label}: {view}"
            d.rectangle((x, y, x + 12 + 10 * len(text), y + 30), fill=(0, 0, 0))
            d.text((x + 8, y + 5), text, fill=(255, 255, 255), font=font(18))
    img.save(out)
    print(out, img.size)


sheet(["cab", "third", "close"], None, "docs/images/quality_compare.png",
      "Same viewpoints at both quality levels (1600×900, 60°): left Quest, right PC")
sheet(["close", "far"], (500, 250, 1100, 588), "docs/images/quality_detail.png",
      "Detail (centre, ~2.7× zoom): tractor shadow, mesh LODs, antialiasing")
