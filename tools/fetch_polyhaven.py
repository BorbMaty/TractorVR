"""Download the Poly Haven textures the environment uses (CC0) into Assets/Textures/Env.

    python tools/fetch_polyhaven.py            # missing files only
    python tools/fetch_polyhaven.py --force    # download again

Each texture gets <id>_diff.jpg and <id>_nor.jpg (OpenGL normal map, what Unity expects) at 1k.
The Blender scripts (tools/blender/) read the real-world size from TEXTURES to set the UV scale,
so a texture repeats at its true size on every asset. Keep the two lists in sync.
"""
import json
import pathlib
import sys
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT = ROOT / "Assets" / "Textures" / "Env"

# id -> what it is used for. The size comes from the API (dimensions in mm).
TEXTURES = {
    "weathered_plank_siding": "barn walls, doors (vertical boards)",
    "clay_roof_tiles_02": "roofs (red clay tiles)",
    "white_rough_plaster": "whitewashed plinths (flaking)",
    "corrugated_iron_02": "machine shed roof (weathered corrugated sheets)",
    "dirty_concrete": "farmyard slabs (tools/make_yard_concrete.py adds the 4 m joints)",
    "brown_mud_dry": "earth practice ramp (dry, rutted soil)",
    "green_metal_rust": "implements: tools/make_impl_paint.py turns it into a neutral worn-paint texture, tinted per colour",
}

UA = {"User-Agent": "TractorVR-university-project"}


def get(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA)) as r:
        return r.read()


def main():
    force = "--force" in sys.argv
    OUT.mkdir(parents=True, exist_ok=True)
    sizes = {}
    for tid, use in TEXTURES.items():
        info = json.loads(get(f"https://api.polyhaven.com/info/{tid}"))
        files = json.loads(get(f"https://api.polyhaven.com/files/{tid}"))
        sizes[tid] = [round(v / 1000.0, 3) for v in info["dimensions"]]  # metres
        for key, suffix in (("Diffuse", "diff"), ("nor_gl", "nor")):
            dest = OUT / f"{tid}_{suffix}.jpg"
            if dest.exists() and not force:
                continue
            dest.write_bytes(get(files[key]["1k"]["jpg"]["url"]))
            print("downloaded", dest.relative_to(ROOT))
    (OUT / "sizes.json").write_text(json.dumps(sizes, indent=2) + "\n")
    lines = ["Textures from Poly Haven (https://polyhaven.com), licence CC0 (public domain).",
             "Downloaded by tools/fetch_polyhaven.py at 1k.", ""]
    lines += [f"- {tid}: https://polyhaven.com/a/{tid} ({use}, {sizes[tid][0]} x {sizes[tid][1]} m)"
              for tid, use in TEXTURES.items()]
    (OUT / "LICENSE.txt").write_text("\n".join(lines) + "\n")
    print("sizes", sizes)


if __name__ == "__main__":
    main()
