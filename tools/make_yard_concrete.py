"""Farmyard slab texture (phase 5): Poly Haven `dirty_concrete` (CC0, 3 m) stretched over one 4 m slab, with a sealed joint
along two edges (dark in the albedo, a groove in the normal map), so the tiled yard shows 4 m slabs like the generated texture did.

    python tools/make_yard_concrete.py      (after tools/fetch_polyhaven.py)

-> Assets/Textures/Env/yard_concrete_diff.jpg / _nor.jpg (1024 px = 4 m). ProjectSetup uses them for the yard (`Ground_Concrete`).
"""
import pathlib

from PIL import Image, ImageChops, ImageFilter, ImageStat

TEX = pathlib.Path(__file__).resolve().parent.parent / "Assets" / "Textures" / "Env"
N = 1024
JOINT = 5            # px (~2 cm at 4 m per 1024 px)


def main():
    diff = Image.open(TEX / "dirty_concrete_diff.jpg").convert("RGB").resize((N, N), Image.LANCZOS)
    # Halve the stains' contrast: at full strength they repeat visibly every 4 m slab across the 52 m yard.
    mean = tuple(int(m) for m in ImageStat.Stat(diff).mean)
    diff = Image.blend(diff, Image.new("RGB", (N, N), mean), 0.5)
    nor = Image.open(TEX / "dirty_concrete_nor.jpg").convert("RGB").resize((N, N), Image.LANCZOS)

    # Joint mask: a line along the left and the bottom edge, soft so it doesn't alias when tiled.
    mask = Image.new("L", (N, N), 0)
    px = mask.load()
    for i in range(N):
        for j in range(JOINT):
            px[j, i] = 255
            px[i, N - 1 - j] = 255
    mask = mask.filter(ImageFilter.GaussianBlur(1.2))

    # Albedo: the joint sealant is dark, a little grime spreads beside it.
    dark = Image.new("RGB", (N, N), (38, 36, 33))
    grime = mask.filter(ImageFilter.GaussianBlur(10)).point(lambda v: int(v * 0.35))
    out = Image.composite(ImageChops.multiply(diff, Image.new("RGB", (N, N), (150, 145, 138))), diff, grime)
    out = Image.composite(dark, out, mask)
    out.save(TEX / "yard_concrete_diff.jpg", quality=92)

    # Normal map (OpenGL): the groove's walls lean towards its centre line.
    n = nor.load()
    for i in range(N):
        for j in range(JOINT + 2):
            t = (j - (JOINT + 1) * 0.5) / ((JOINT + 1) * 0.5)          # -1 .. 1 across the groove
            r, g, b = n[j, i]
            n[j, i] = (int(128 - 70 * t), g, b)                         # left edge: tilt in X
            r, g, b = n[i, N - 1 - j]
            n[i, N - 1 - j] = (r, int(128 + 70 * t), b)                 # bottom edge: tilt in Y
    nor.save(TEX / "yard_concrete_nor.jpg", quality=95)
    print("wrote yard_concrete_diff.jpg / _nor.jpg")


if __name__ == "__main__":
    main()
