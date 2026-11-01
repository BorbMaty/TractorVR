"""Keyboard and mouse picture for the start menu's CONTROLS page and the README (phase 6).

The used keys are drawn on a keyboard layout in their group's colour; a legend beside it explains each key, the mouse below.
Letters and legend are large: the picture is read on a board in VR.

    python tools/keyboard_guide.py      -> docs/images/guide_keyboard.png + Assets/Textures/Guide/guide_keyboard.png
"""
import os

from PIL import Image, ImageDraw, ImageFont

W, H = 2540, 1250
BG = (236, 238, 232)
INK = (30, 34, 30)
KEY = (250, 250, 246)
EDGE = (150, 154, 146)
F_KEY = ImageFont.truetype("arialbd.ttf", 34)
F_SMALL = ImageFont.truetype("arialbd.ttf", 22)
F_HEAD = ImageFont.truetype("arialbd.ttf", 36)
F_TEXT = ImageFont.truetype("arial.ttf", 28)
F_TITLE = ImageFont.truetype("arialbd.ttf", 46)

GROUPS = [
    ("Driving", (92, 168, 70), [
        ("W / S", "drive / brake, then reverse"), ("A / D", "steer"), ("X / Z", "shift up / down"),
        ("T", "gearbox auto / manual"), ("[ / ]", "hand throttle - / +")]),
    ("Hitch and implements", (226, 176, 40), [
        ("Q / E", "rear hitch up / down"), ("Ctrl + Q / E", "front linkage"), ("J", "attach / detach"),
        ("P", "PTO on / off"), ("I / K", "tip the trailer up / down")]),
    ("Lights and sky", (232, 120, 50), [
        ("G", "road lights"), ("N", "work lights"), ("O", "beacon"), ("Y", "hazards"),
        (", / .", "indicator left / right"), ("U", "day / dusk / night")]),
    ("Tractor and view", (70, 130, 200), [
        ("F / L / B", "4WD / diff lock / brakes"), ("H", "horn"), ("C", "cab / outside view"), ("R", "look ahead"),
        ("V", "comfort vignette"), ("Shift", "hold: seat adjust"), ("M", "menu"), ("F9 / F11", "try VR / full screen")]),
]

KEYMAP = {}
for _, colour, items in GROUPS:
    for keys, _ in items:
        for k in keys.replace("+", "/").split("/"):
            k = k.strip()
            if k == "Ctrl":
                KEYMAP["Ctrl"] = colour
            elif k == "Shift":
                KEYMAP["Shift"] = colour
            elif k in ("F", "L", "B") and keys.startswith("F /"):
                KEYMAP[k] = colour
            else:
                KEYMAP[k] = colour

ROWS = [
    (0.0, ["Esc", None, "F1", "F2", "F3", "F4", None, "F5", "F6", "F7", "F8", None, "F9", "F10", "F11", "F12"]),
    (0.0, ["`", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=", ("Back", 2.0)]),
    (0.0, [("Tab", 1.5), "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]", ("\\", 1.5)]),
    (0.0, [("Caps", 1.75), "A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'", ("Enter", 2.25)]),
    (0.0, [("Shift", 2.25), "Z", "X", "C", "V", "B", "N", "M", ",", ".", "/", ("Shift ", 2.75)]),
    (0.0, [("Ctrl", 1.5), ("Win", 1.25), ("Alt", 1.25), ("", 6.25), ("Alt ", 1.25), ("Ctrl ", 1.5)]),
]


def draw():
    img = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(img)
    d.text((60, 40), "Keyboard and mouse (PC desktop version)", font=F_TITLE, fill=INK)
    u = 92            # key pitch
    x0, y0 = 60, 140
    for r, (_, keys) in enumerate(ROWS):
        x = x0
        y = y0 + r * (u + (14 if r == 0 else 0)) + (0 if r == 0 else 14)
        for k in keys:
            if k is None:
                x += u * 0.5
                continue
            name, width = (k, 1.0) if isinstance(k, str) else k
            w = u * width - 8
            label = name.strip()
            colour = KEYMAP.get(label)
            fill = colour if colour else KEY
            d.rounded_rectangle([x, y, x + w, y + u - 8], radius=10, fill=fill, outline=EDGE, width=2)
            font = F_KEY if len(label) <= 2 else F_SMALL
            tw = d.textlength(label, font=font)
            d.text((x + (w - tw) / 2, y + (u - 8) / 2 - font.size * 0.6), label,
                   font=font, fill=(255, 255, 255) if colour else (120, 124, 118))
            x += u * width
    kb_bottom = y0 + 6 * u + 30

    # Mouse, under the keyboard.
    mx, my = 120, kb_bottom + 40
    d.rounded_rectangle([mx, my, mx + 150, my + 230], radius=70, fill=KEY, outline=EDGE, width=3)
    d.line([mx + 75, my, mx + 75, my + 90], fill=EDGE, width=3)
    d.line([mx, my + 90, mx + 150, my + 90], fill=EDGE, width=3)
    d.pieslice([mx, my, mx + 150, my + 180], 180, 270, fill=(92, 168, 70))
    d.pieslice([mx, my, mx + 150, my + 180], 270, 360, fill=(70, 130, 200))
    d.rounded_rectangle([mx + 66, my + 22, mx + 84, my + 66], radius=8, fill=(226, 176, 40))
    lines = [((92, 168, 70), "Left button: press switches and buttons, drag the wheel, the levers, the doors"),
             ((70, 130, 200), "Right button (hold): look around; outside view: orbit"),
             ((226, 176, 40), "Wheel: zoom")]
    for i, (c, t) in enumerate(lines):
        y = my + 10 + i * 64
        d.rectangle([mx + 200, y + 6, mx + 228, y + 34], fill=c)
        d.text((mx + 245, y), t, font=F_TEXT, fill=INK)

    # Legend, right of the keyboard.
    lx, ly = 60 + 15 * u + 60, 120
    for name, colour, items in GROUPS:
        d.rectangle([lx, ly + 6, lx + 30, ly + 36], fill=colour)
        d.text((lx + 44, ly), name, font=F_HEAD, fill=INK)
        ly += 44
        for keys, text in items:
            d.text((lx + 44, ly), keys, font=ImageFont.truetype("arialbd.ttf", 28), fill=colour)
            d.text((lx + 250, ly), text, font=F_TEXT, fill=INK)
            ly += 34
        ly += 14
    return img


if __name__ == "__main__":
    img = draw()
    for out in ("docs/images/guide_keyboard.png", "Assets/Textures/Guide/guide_keyboard.png"):
        os.makedirs(os.path.dirname(out), exist_ok=True)
        img.save(out, optimize=True)
        print(out)
