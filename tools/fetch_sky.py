"""Skies from Poly Haven (CC0, "pure sky" HDRIs: sky only, no ground in the picture) for DAY / DUSK / NIGHT.

    python tools/fetch_sky.py            # missing files only
    python tools/fetch_sky.py --force    # download again

Writes Assets/Textures/Sky/<id>_2k.hdr (the skybox, imported as a cubemap by ProjectSetup) and Assets/Textures/Sky/sky.json
with what was measured in each picture (from the 1k file, decoded here with numpy):
  sun_u, sun_v   where the sun (the moon at night) is: u 0..1 across the panorama, v 0 top .. 1 bottom
  sun_elevation  degrees above the horizon
  horizon        mean linear colour of the band 0..6 degrees above the horizon (the fog / haze colour, so the scene's haze and
                 the far hills blend into the picture)
  exposure       skybox exposure that brings the sky's median brightness (sun excluded) to the target of that time of day
TractorLights turns the sun light to the picture's sun and swaps the skybox per time of day; ProjectSetup sets the
skybox rotation (`_Rotation`) so the picture's sun lies where the light comes from.
"""
import json
import pathlib
import sys
import urllib.request

import numpy as np

ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT = ROOT / "Assets" / "Textures" / "Sky"
UA = {"User-Agent": "TractorVR-university-project"}

# role -> (Poly Haven id, target median sky brightness (linear) for the skybox exposure)
SKIES = {
    "day": ("kloofendal_38d_partly_cloudy_puresky", 0.2),  # partly cloudy, sun 38 deg up (the scene was lit at 35 deg)
    "dusk": ("kloppenheim_06_puresky", 0.22),   # sunset with clouds, Central European countryside
    "night": ("kloppenheim_02_puresky", 0.012), # clear night, moon
}


def get(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA)) as r:
        return r.read()


def read_hdr(data):
    """Radiance .hdr (RGBE, new-style run-length scanlines) -> float32 array (h, w, 3)."""
    end = data.index(b"\n\n") + 2
    line_end = data.index(b"\n", end)
    dims = data[end:line_end].split()
    h, w = int(dims[1]), int(dims[3])
    pos = line_end + 1
    buf = np.frombuffer(data, dtype=np.uint8)
    img = np.zeros((h, w, 4), dtype=np.uint8)
    for y in range(h):
        if buf[pos] != 2 or buf[pos + 1] != 2:
            raise ValueError("only run-length encoded HDR scanlines are handled")
        pos += 4
        for c in range(4):
            x = 0
            row = img[y, :, c]
            while x < w:
                n = int(buf[pos]); pos += 1
                if n > 128:
                    n -= 128
                    row[x:x + n] = buf[pos]; pos += 1
                else:
                    row[x:x + n] = buf[pos:pos + n]; pos += n
                x += n
    e = img[:, :, 3].astype(np.int32)
    scale = np.where(e > 0, np.ldexp(1.0, e - 136), 0.0).astype(np.float32)
    return img[:, :, :3].astype(np.float32) * scale[:, :, None]


def measure(img, target):
    h, w, _ = img.shape
    lum = img @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    upper = lum[: h // 2]
    # Sun / moon: the centroid of the brightest 0.02 % of the upper half.
    thr = np.quantile(upper, 0.9998)
    ys, xs = np.nonzero(upper >= thr)
    wts = upper[ys, xs]
    # x wraps around: average the angle, not the pixel column.
    ang = xs / w * 2 * np.pi
    a = np.arctan2((np.sin(ang) * wts).sum(), (np.cos(ang) * wts).sum())
    sun_u = float((a / (2 * np.pi)) % 1.0)
    sun_v = float((ys * wts).sum() / wts.sum() / h)
    elevation = 90.0 - sun_v * 180.0
    band = img[int(h * (0.5 - 6 / 180)): h // 2]
    horizon = band.reshape(-1, 3).mean(axis=0)
    sky = upper[upper < thr * 0.05]
    median = float(np.median(sky))
    exposure = target / max(median, 1e-6)
    return dict(sun_u=round(sun_u, 4), sun_v=round(sun_v, 4), sun_elevation=round(elevation, 2),
                horizon=[round(float(c * exposure), 4) for c in horizon], exposure=round(exposure, 4))


def main():
    force = "--force" in sys.argv
    OUT.mkdir(parents=True, exist_ok=True)
    info = {}
    lic = ["Skies from Poly Haven (https://polyhaven.com), CC0 (public domain):"]
    for role, (pid, target) in SKIES.items():
        files = json.loads(get(f"https://api.polyhaven.com/files/{pid}"))
        path = OUT / f"{pid}_2k.hdr"
        if force or not path.exists():
            print("downloading", pid, "2k")
            path.write_bytes(get(files["hdri"]["2k"]["hdr"]["url"]))
        small = get(files["hdri"]["1k"]["hdr"]["url"])
        m = measure(read_hdr(small), target)
        info[role] = dict(id=pid, file=path.name, **m)
        print(role, info[role])
        lic.append(f"  {pid}  https://polyhaven.com/a/{pid}")
    (OUT / "sky.json").write_text(json.dumps(info, indent=2) + "\n")
    (OUT / "LICENSE.txt").write_text("\n".join(lic) + "\n")


if __name__ == "__main__":
    main()
