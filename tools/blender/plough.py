"""3-furrow mounted plough for the rear linkage (phase 5 step 2, remade from the primitives in ProjectSetup): blue frame,
headstock with the lower pins and the top pin, a diagonal main beam, three bodies (legs, frogs, polished twisted
mouldboards, shares and points, landsides), disc coulters ahead of them, a depth wheel on an arm, a parking stand,
a red / white rear warning panel. Same layout and points as before (HitchPoint (0, 0.75, 0), TopPin (0, 1.36, -0.12),
share tips, depth wheel at (1.15, 0.55, -2.85)).

Implement space: +Z points to the tractor, Y up, ground at y = 0 when parked.
Parts: Main (implement frame), Wheel (depth wheel, in its Spin pivot's space: spins around local X).
"""
import math

BLUE, YELLOW, STEEL, POLISH, RUBBER = "Impl_Blue", "Impl_Yellow", "Impl_Steel", "Impl_Polished", "Impl_Rubber"
BODIES = [(-0.95 - i * 0.9, 0.25 + i * 0.36) for i in range(3)]      # (z, x) of each body, as in ProjectSetup
WHEEL = (1.15, 0.55, -2.85)
BEAM0, BEAM1 = (0.207, 0.72, -0.17), (0.693, 0.72, -3.23)


def mouldboard(b, x, z):
    """The twisted mouldboard surface of ProjectSetup.MouldboardMesh, as a plate, at (x - 0.14, 0.04, z + 0.3)."""
    length, height = 0.75, 0.38
    ox, oy, oz = x - 0.14, 0.04, z + 0.3
    nu, nv = (7, 9) if b.detail else (3, 4)

    def S(u, w):
        return (ox + w * (0.30 + 0.22 * u) + 0.14 * u * u * w * w, oy + height * u * (1 - 0.12 * w),
                oz - length * w - 0.1 * u * (1 - w))
    rows = [[S(i / (nu - 1), j / (nv - 1)) for j in range(nv)] for i in range(nu)]
    b.sheet(rows, POLISH, thick=0.012)


def main(b):
    # Headstock: cross shaft with the lower pins, two plates up to the top pin, braces back to the beam.
    b.tube((-0.48, 0.75, 0), (0.48, 0.75, 0), 0.035, STEEL, segs=10)
    for s in (-1, 1):
        b.tube((s * 0.38, 0.75, 0), (s * 0.47, 0.75, 0), 0.03, YELLOW, segs=8)
        b.revolve((s * 0.47, 0.75, 0), [(0.045, -0.006), (0.045, 0.006)], STEEL, segs=10, roll=90, detail=True)
        b.bar((s * 0.32, 0.72, -0.02), (s * 0.06, 1.36, -0.12), (0.03, 0.1), BLUE)
        b.bar((s * 0.32, 0.75, -0.02), (s * 0.12, 0.78, -0.4), (0.04, 0.08), BLUE)
    for x in (-0.05, 0.05):
        b.box((x, 1.36, -0.12), (0.02, 0.18, 0.18), BLUE)
    b.tube((-0.09, 1.36, -0.12), (0.09, 1.36, -0.12), 0.016, YELLOW, segs=8)
    b.bar((0.0, 1.3, -0.16), (0.25, 0.8, -0.85), (0.07, 0.07), BLUE)
    b.box((0.12, 0.72, -0.38), (0.36, 0.22, 0.36), BLUE)
    b.box((0.12, 0.84, -0.38), (0.3, 0.02, 0.3), BLUE, detail=True)   # cover plate
    # Main beam (rectangular tube), end caps.
    b.bar(BEAM0, BEAM1, (0.16, 0.16), BLUE)
    for i, (z, x) in enumerate(BODIES):
        # Clamp on the beam, leg (standard) down to the frog, the body.
        b.box((x + 0.02, 0.72, z), (0.2, 0.2, 0.2), BLUE)
        for dy in (-0.07, 0.07):     # clamp bolts (detail)
            b.tube((x - 0.1, 0.72 + dy, z + 0.06), (x + 0.14, 0.72 + dy, z + 0.06), 0.012, STEEL, segs=6, detail=True)
        b.bar((x + 0.02, 0.66, z), (x - 0.03, 0.18, z + 0.04), (0.06, 0.14), BLUE)
        b.box((x - 0.05, 0.14, z + 0.02), (0.14, 0.2, 0.3), BLUE)
        b.box((x + 0.04, 0.035, z + 0.18), (0.38, 0.02, 0.16), POLISH, yaw=-38, pitch=-8)          # share
        b.box((x - 0.12, 0.03, z + 0.27), (0.05, 0.03, 0.14), POLISH, yaw=-20, pitch=-12)         # point
        mouldboard(b, x, z)
        b.box((x - 0.14, 0.08, z - 0.05), (0.02, 0.12, 0.6), POLISH)                              # landside
        b.bar((x + 0.2, 0.25, z - 0.35), (x + 0.35, 0.3, z - 0.55), (0.02, 0.05), BLUE, detail=True)   # mouldboard stay
        # Disc coulter ahead of the body on an arm from the beam.
        cx, cy, cz = x - 0.13, 0.2, z + 0.62
        b.revolve((cx, cy, cz), [(0.0, -0.004), (0.2, -0.003), (0.21, 0.0), (0.2, 0.003), (0.0, 0.004)], STEEL, segs=20, roll=90)
        b.revolve((cx, cy, cz), [(0.05, -0.04), (0.05, 0.04)], STEEL, segs=10, roll=90)
        b.bar((cx + 0.05, cy, cz), (cx + 0.05, 0.72, z + 0.52), (0.04, 0.05), BLUE)
    # Depth wheel arm and fork (the wheel itself is the Wheel part), depth spindle with a crank (detail).
    wx, wy, wz = WHEEL
    b.bar((0.69, 0.72, -3.1), (wx - 0.12, wy + 0.08, wz + 0.25), (0.06, 0.06), BLUE)
    for s in (-1, 1):
        b.bar((wx + s * 0.12, wy + 0.25, wz + 0.2), (wx + s * 0.12, wy, wz), (0.02, 0.05), BLUE)
    b.box((wx, wy + 0.27, wz + 0.2), (0.28, 0.05, 0.08), BLUE)
    b.tube((wx - 0.12, wy, wz), (wx + 0.12, wy, wz), 0.02, STEEL, segs=8)
    b.tube((wx, wy + 0.3, wz + 0.2), (wx, wy + 0.65, wz + 0.25), 0.018, STEEL, segs=8, detail=True)
    b.bar((wx, wy + 0.65, wz + 0.25), (wx + 0.15, wy + 0.65, wz + 0.25), (0.015, 0.015), STEEL, detail=True)
    # Parking stand under the headstock, rear warning panel (road use) with red / white stripes.
    b.box((0.12, 0.33, -0.38), (0.06, 0.62, 0.06), BLUE)
    b.box((0.12, 0.02, -0.38), (0.2, 0.03, 0.2), BLUE)
    px, py, pz = 0.69, 0.98, -3.25
    b.bar((px, 0.8, -3.2), (px, py - 0.12, pz), (0.04, 0.04), BLUE)
    b.box((px, py, pz), (0.42, 0.42, 0.02), "Impl_White")
    for k in range(-2, 3):
        b.box((px + k * 0.085, py, pz - 0.012), (0.04, 0.5, 0.005), "Impl_Lamp", roll=45, detail=True)


def wheel(b):
    b.tyre((0, 0, 0), 0.3, 0.16, RUBBER, lugs=14, lug_h=0.02, segs=18)
    b.revolve((0, 0, 0), [(0.0, -0.06), (0.185, -0.06), (0.185, 0.06), (0.0, 0.06)], YELLOW, segs=16, roll=90)
    b.revolve((0, 0, 0), [(0.0, -0.09), (0.05, -0.09), (0.05, 0.09), (0.0, 0.09)], STEEL, segs=10, roll=90)


PARTS = {"Main": main, "Wheel": wheel}
NO_GROUND = ("Wheel",)
NAME = "ImplPlough"
NOTES = "3-furrow plough (remade): frame, 3 bodies with twisted mouldboards, coulters, depth wheel (own part), warning panel."
