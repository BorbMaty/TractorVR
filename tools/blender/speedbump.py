"""Speed bump (WorldLayout.SpeedBumps, on the west track by the barn): a 10 cm triangular hump, 1.3 m long, across a 4 m track,
yellow and black rubber segments. Made to see the suspension work: the front axle rises and swings, the rods slide in the
front suspension cylinders, a raised implement bounces.

Unity coordinates, origin = centre on the ground; the track runs along local X (the bump is crossed in X), the hump is 4.4 m wide in Z.
The wheel rays (TractorController.GroundRay) hit the two sloped colliders, so the wheels ride up and over the hump.
"""
import math

H = 0.10          # height above the terrain (the gravel ribbon itself is 3-7 cm above it, so ~4-7 cm show)
HALF = 0.65       # half the length along the track
W = 4.4           # across
SEGS = 8          # stripes


def describe(b):
    seg = W / SEGS
    for k in range(SEGS):
        z = -W * 0.5 + (k + 0.5) * seg
        mat = "Env_PaintYellow" if k % 2 == 0 else "Env_RubberBlack"
        b.gable(-HALF, HALF, -0.04, H + 0.04, z - seg * 0.5, z + seg * 0.5, mat, cell=0.5)
    # Bolts along the crest (detail).
    for k in range(SEGS):
        z = -W * 0.5 + (k + 0.5) * seg
        for dx in (-0.3, 0.3):
            b.box((dx, H * (1 - abs(dx) / HALF) + 0.005, z), (0.05, 0.02, 0.05), "Env_Steel", detail=True)
    # Colliders: one box under each slope, its top face on the slope (roll = slope angle).
    a = math.atan2(H, HALF)
    L = math.hypot(HALF, H)
    t = 0.2
    for s in (-1, 1):
        mx, my = s * HALF * 0.5, H * 0.5
        nx, ny = s * math.sin(a), math.cos(a)           # slope normal (up, leaning away from the crest)
        b.collider((mx - nx * t * 0.5, my - ny * t * 0.5, 0.0), (L, t, W), roll=-s * math.degrees(a))


NOTES = "Speed bump, %.0f cm high, %.1f m long, %.1f m wide; two sloped colliders (from the JSON)." % (H * 100, 2 * HALF, W)
NAME = "SpeedBump"
