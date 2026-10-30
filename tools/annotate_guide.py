"""Draws the cab guide: text boxes in margins left and right of each render, with arrows to the controls.

Input:  Logs/Guide/<name>.png + <name>.json (from CabGuide.Run in Unity: title, text, x, y per control)
Output: docs/images/<name>.png

    python tools/annotate_guide.py            (all guide_* renders that have labels)
"""
import glob
import json
import math
import os
import textwrap

from PIL import Image, ImageDraw, ImageFont

MARGIN = 470          # px of label column on each side
BOX_W = 420
PAD = 12
GAP = 14              # between boxes
TITLE = ImageFont.truetype("arialbd.ttf", 27)
BODY = ImageFont.truetype("arial.ttf", 22)
INK = (30, 34, 30)
ACCENT = (54, 124, 43)        # John Deere green
BOX_FILL = (250, 250, 246)
BG = (236, 238, 232)


def box_height(label):
    lines = textwrap.wrap(label["text"], 36)
    label["lines"] = lines
    return PAD * 2 + 32 + 27 * len(lines)


def layout(labels, height):
    """Boxes in one column, each as close as possible to its target's height, without overlapping."""
    labels.sort(key=lambda l: l["y"])
    y = PAD
    for l in labels:
        h = box_height(l)
        top = max(y, l["y"] - h / 2)
        l["top"], l["h"] = top, h
        y = top + h + GAP
    # Then from the bottom up, so the last ones don't run off the picture.
    y = height - PAD
    for l in reversed(labels):
        bottom = min(y, l["top"] + l["h"])
        l["top"] = bottom - l["h"]
        y = l["top"] - GAP


def arrow(draw, start, end):
    draw.line([start, end], fill=ACCENT, width=4)
    ang = math.atan2(end[1] - start[1], end[0] - start[0])
    size = 18
    tip = end
    left = (end[0] - size * math.cos(ang - 0.4), end[1] - size * math.sin(ang - 0.4))
    right = (end[0] - size * math.cos(ang + 0.4), end[1] - size * math.sin(ang + 0.4))
    draw.polygon([tip, left, right], fill=ACCENT)
    r = 7
    draw.ellipse([end[0] - r, end[1] - r, end[0] + r, end[1] + r], outline=(255, 255, 255), width=3)


def annotate(name):
    shot = Image.open(f"Logs/Guide/{name}.png").convert("RGB")
    labels = [l for l in json.load(open(f"Logs/Guide/{name}.json", encoding="utf-8"))["labels"] if l["visible"]]
    if not labels:   # nothing to point at (the cab panel: its buttons are labelled)
        shot.save(f"docs/images/{name}.png", optimize=True)
        print(f"docs/images/{name}.png: as rendered")
        return True
    w, h = shot.size
    canvas = Image.new("RGB", (w + 2 * MARGIN, h), BG)
    canvas.paste(shot, (MARGIN, 0))
    draw = ImageDraw.Draw(canvas)
    left = [l for l in labels if l["x"] < w / 2]
    right = [l for l in labels if l["x"] >= w / 2]
    layout(left, h)
    layout(right, h)
    for side, group in (("L", left), ("R", right)):
        for l in group:
            x0 = (MARGIN - BOX_W) / 2 if side == "L" else MARGIN + w + (MARGIN - BOX_W) / 2
            y0 = l["top"]
            draw.rounded_rectangle([x0, y0, x0 + BOX_W, y0 + l["h"]], radius=10, fill=BOX_FILL, outline=ACCENT, width=3)
            draw.text((x0 + PAD, y0 + PAD - 2), l["title"], font=TITLE, fill=ACCENT)
            for i, line in enumerate(l["lines"]):
                draw.text((x0 + PAD, y0 + PAD + 34 + 27 * i), line, font=BODY, fill=INK)
            start = (x0 + BOX_W, y0 + l["h"] / 2) if side == "L" else (x0, y0 + l["h"] / 2)
            arrow(draw, start, (MARGIN + l["x"], l["y"]))
    os.makedirs("docs/images", exist_ok=True)
    canvas.save(f"docs/images/{name}.png", optimize=True)
    print(f"docs/images/{name}.png: {len(labels)} labels")
    return True


if __name__ == "__main__":
    for path in sorted(glob.glob("Logs/Guide/guide_*.json")):
        annotate(os.path.splitext(os.path.basename(path))[0])
