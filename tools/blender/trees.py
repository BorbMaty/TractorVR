"""Trees of the land around Reghin (phase 5 step 4): walnut, wild pear, black locust (acacia), Lombardy poplar, white willow.

One asset, one part per species and distance (tvr.build_parts): <Species> = the near tree (Full on PC, Light on the Quest),
<Species>Far = a handful of triangles for the trees beyond the tier's detail distance (Light used on both tiers). The far
trees are low-poly meshes rather than flat billboards: no alpha test (expensive on the Quest's tile GPU) and no turning to
the camera, and at 120 m+ the difference is a few pixels. Crowns are lumpy smooth-shaded blobs (Builder.blob) with the AO
baked over 3 m, so the inner and lower leaf masses go dark like a real crown. Unity draws them instanced (Trees.cs), placed
by TreeLayout (Ground.cs).

Unity coordinates, origin = trunk foot on the ground (sunk 0.3 m so slopes don't show a gap).
"""
import math
import random


def taper(b, p0, p1, r0, r1, mat, segs=8, detail=False):
    """Tapered limb from p0 to p1 (Unity), radius r0 -> r1."""
    dx, dy, dz = (p1[i] - p0[i] for i in range(3))
    length = math.sqrt(dx * dx + dy * dy + dz * dz)
    pitch = math.degrees(math.acos(max(-1.0, min(1.0, dy / length))))
    yaw = math.degrees(math.atan2(dx, dz))
    b.revolve(tuple(p0), [(r0, 0.0), (r1, length)], mat, segs=segs, pitch=pitch, yaw=yaw, cell=4.0, detail=detail)


def crown(b, rnd, centre, radii, n, mat, size=0.42, seed=0, fork=None, limb_r=0.1, bark="Env_Bark", limbs=4):
    """Cauliflower crown: a core blob filling the ellipsoid, n smaller lumps sitting on its surface (more on top than below),
    and `limbs` limbs from the fork into the lowest lumps (inside the crown, so only their bases show)."""
    cx, cy, cz = centre
    b.blob(centre, (radii[0] * 0.82, radii[1] * 0.8, radii[2] * 0.82), mat, seed=seed * 31, rough=0.2, flat=0.25)
    pts = []
    for k in range(n):
        a = 2 * math.pi * (k + rnd.uniform(-0.3, 0.3)) / n * 2.618   # golden-angle spread
        e = math.asin(rnd.uniform(-0.45, 0.95))
        p = (cx + radii[0] * 0.78 * math.cos(a) * math.cos(e), cy + radii[1] * 0.72 * math.sin(e),
             cz + radii[2] * 0.78 * math.sin(a) * math.cos(e))
        s = rnd.uniform(0.75, 1.15) * size
        m = min(radii)
        b.blob(p, (m * s * 1.1, m * s * 0.85, m * s * 1.1), mat, seed=seed * 31 + k + 1, rough=0.3, flat=0.3,
               subdiv=(2, 1))
        pts.append(p)
    if fork is not None:
        for k, p in enumerate(sorted(pts, key=lambda q: q[1])[:limbs]):
            end = (fork[0] + (p[0] - fork[0]) * 0.6, fork[1] + (p[1] - fork[1]) * 0.6, fork[2] + (p[2] - fork[2]) * 0.6)
            taper(b, fork, end, limb_r, limb_r * 0.45, bark, segs=6, detail=k >= 2)


def walnut(b):
    """Juglans regia: short thick trunk, a few heavy limbs, broad round dense crown ~12 m high, 12 m across."""
    rnd = random.Random(11)
    taper(b, (0, -0.3, 0), (0.1, 2.6, 0.05), 0.32, 0.26, "Env_Bark", segs=10)
    crown(b, rnd, (0, 7.0, 0), (5.6, 4.2, 5.4), 14, "Env_LeafWalnut", size=0.42, seed=1, fork=(0.1, 2.6, 0.05), limb_r=0.15)


def pear(b):
    """Wild pear (vadkörte), the lone tree of the Mezőség balks: straight trunk, upright irregular oval crown ~10 m."""
    rnd = random.Random(23)
    taper(b, (0, -0.3, 0), (-0.1, 2.6, 0.1), 0.22, 0.17, "Env_Bark", segs=8)
    crown(b, rnd, (0, 6.4, 0), (3.4, 4.6, 3.4), 11, "Env_LeafPear", size=0.5, seed=2, fork=(-0.1, 2.6, 0.1), limb_r=0.1)


def acacia(b):
    """Black locust (akác): slim crooked trunk, open, patchy crown high up, light green; ~13 m."""
    rnd = random.Random(37)
    pts = [(0, -0.3, 0), (0.25, 3.0, 0.1), (-0.15, 6.0, 0.35), (0.3, 8.5, 0.0)]
    for p0, p1, r in zip(pts, pts[1:], (0.2, 0.16, 0.12)):
        taper(b, p0, p1, r, r * 0.8, "Env_Bark", segs=7)
    fork = pts[-1]
    for k, (x, y, z, s) in enumerate([(1.8, 10.0, 0.6, 1.0), (-1.6, 9.4, -0.8, 0.9), (0.2, 11.4, -0.3, 0.85), (0.9, 8.2, -1.9, 0.75),
                                      (-1.2, 7.8, 1.8, 0.75), (2.4, 8.6, -0.6, 0.6), (-2.2, 10.6, 0.9, 0.6), (0.6, 12.4, 1.0, 0.55)]):
        b.blob((x, y, z), (2.0 * s, 1.6 * s, 1.9 * s), "Env_LeafAcacia", seed=40 + k, rough=0.38)
        if k < 5:
            taper(b, fork, (x * 0.7, y - 0.6, z * 0.7), 0.08, 0.03, "Env_Bark", segs=5, detail=k >= 2)


def poplar(b):
    """Lombardy poplar (jegenye), the road trees: a narrow column ~22 m high, 4 m across, pale bark."""
    taper(b, (0, -0.3, 0), (0, 6.0, 0), 0.3, 0.22, "Env_BarkPale", segs=8)
    for k, (y, r, h) in enumerate([(5.5, 1.7, 3.2), (9.0, 2.1, 4.0), (13.0, 2.0, 4.0), (17.0, 1.6, 3.6), (20.5, 1.0, 2.6)]):
        dx = 0.25 * math.sin(k * 2.1)
        b.blob((dx, y, 0.2 * math.cos(k * 1.7)), (r, h, r * 0.95), "Env_LeafPoplar", seed=60 + k, rough=0.22, flat=0.1)


def willow(b):
    """White willow (fűzfa) of the wet hollows and the stream: a thick leaning trunk, pollarded into several stems, a wide
    rounded silvery crown ~9 m."""
    rnd = random.Random(53)
    taper(b, (0, -0.3, 0), (0.6, 2.2, 0.2), 0.42, 0.36, "Env_BarkPale", segs=9)
    crown(b, rnd, (0.6, 5.2, 0.2), (4.4, 3.6, 4.2), 13, "Env_LeafWillow", size=0.45, seed=5, fork=(0.6, 2.2, 0.2), limb_r=0.16,
          bark="Env_BarkPale")


# Far trees: a trunk prism and one or two blobs of 20-80 triangles; same size and colours as the near ones.
def walnut_far(b):
    taper(b, (0, -0.3, 0), (0.1, 3.2, 0.05), 0.32, 0.22, "Env_Bark", segs=4)
    b.blob((0, 7.0, 0), (6.0, 4.6, 5.8), "Env_LeafWalnut", seed=1, rough=0.3, subdiv=(2, 1))


def pear_far(b):
    taper(b, (0, -0.3, 0), (-0.1, 3.5, 0.1), 0.22, 0.15, "Env_Bark", segs=4)
    b.blob((0, 6.4, 0), (3.8, 4.9, 3.8), "Env_LeafPear", seed=2, rough=0.3, subdiv=(2, 1))


def acacia_far(b):
    taper(b, (0, -0.3, 0), (0.2, 8.5, 0.1), 0.2, 0.1, "Env_Bark", segs=4)
    b.blob((0.4, 9.8, -0.1), (3.6, 2.4, 3.4), "Env_LeafAcacia", seed=40, rough=0.3, subdiv=(2, 1))


def poplar_far(b):
    taper(b, (0, -0.3, 0), (0, 5.0, 0), 0.3, 0.22, "Env_BarkPale", segs=4)
    b.blob((0, 12.5, 0), (2.0, 9.5, 2.0), "Env_LeafPoplar", seed=60, rough=0.15, flat=0.1, subdiv=(2, 1))


def willow_far(b):
    taper(b, (0, -0.3, 0), (0.6, 3.0, 0.2), 0.42, 0.3, "Env_BarkPale", segs=4)
    b.blob((0.6, 5.2, 0.2), (4.8, 3.8, 4.6), "Env_LeafWillow", seed=5, rough=0.3, subdiv=(2, 1))


NAME = "Trees"
PARTS = {
    "Walnut": walnut, "Pear": pear, "Acacia": acacia, "Poplar": poplar, "Willow": willow,
    "WalnutFar": walnut_far, "PearFar": pear_far, "AcaciaFar": acacia_far, "PoplarFar": poplar_far, "WillowFar": willow_far,
}
AO_DISTANCE = 3.0
NOTES = ("Trees: walnut 12 m, wild pear 10 m, black locust 13 m, Lombardy poplar 22 m, white willow 9 m; near (Full / Light) "
         "and far variants. Drawn instanced by Trees.cs; trunk colliders made there (capsules).")
