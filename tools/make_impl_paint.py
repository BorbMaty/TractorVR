"""Worn-paint texture for the implements (phase 5 step 2): Poly Haven `green_metal_rust` (CC0, 1 m) divided by its own mean colour,
so the paint comes out near white (~0.85) and the material tint gives the colour (blue plough frame, red mower, green trailer ...),
while drips, rust spots and scratches stay as darker / warmer variation.

    python tools/make_impl_paint.py      (after tools/fetch_polyhaven.py)

-> Assets/Textures/Env/impl_paint_diff.jpg / _nor.jpg (the normal map is copied unchanged).
"""
import pathlib
import shutil

from PIL import Image, ImageStat

TEX = pathlib.Path(__file__).resolve().parent.parent / "Assets" / "Textures" / "Env"


def main():
    src = Image.open(TEX / "green_metal_rust_diff.jpg").convert("RGB")
    mean = ImageStat.Stat(src).mean
    bands = [b.point(lambda v, m=m: min(255, int(v / m * 218))) for b, m in zip(src.split(), mean)]
    Image.merge("RGB", bands).save(TEX / "impl_paint_diff.jpg", quality=92)
    shutil.copyfile(TEX / "green_metal_rust_nor.jpg", TEX / "impl_paint_nor.jpg")
    print("wrote impl_paint_diff.jpg / _nor.jpg, source mean", [round(m) for m in mean])


if __name__ == "__main__":
    main()
