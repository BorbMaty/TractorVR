"""Single-axle tipping trailer for the drawbar (phase 5 step 2, remade from the primitives in ProjectSetup). Same layout and
points as before: drawbar eye at (0, 0.43, 0), wheels at (±0.95, 0.5, -4.2), body hinge at (0, 1.0, -6.05), tailgate hinge,
ram base at (0, 0.86, -1.48) with seven 0.7 m stages; the 3 t load heap stays generated at runtime (Tipper.Mound).

Implement space: +Z points to the tractor, Y up, ground at y = 0 when parked.
Parts, each in the space of the pivot it hangs on in Unity:
  Main     trailer frame: drawbar, jack, chassis, axle, springs, mudguards, rear lights, hoses
  Wheel    one wheel in its Spin pivot's space (spins around local X), used for both sides
  Body     the tipping body in its hinge's space (z forward from the hinge at the rear of the chassis)
  Gate     the tailgate in its own hinge's space (hangs down from y = 0)
  Stage0-6 the telescopic ram's stages, each a tube from z = 0 to 0.7 m in its own space
"""
import math

GREEN, STEEL, RUBBER, CHROME, CAST = "Impl_Green", "Impl_Steel", "Impl_Rubber", "Impl_Chrome", "Impl_Cast"
EYE_Y = 0.43                         # Hitch.drawbarMount height (ProjectSetup reads it from the tractor)
WHEEL_X, WHEEL_Y, WHEEL_Z, WHEEL_R, WHEEL_W = 0.95, 0.5, -4.2, 0.5, 0.38
HINGE = (0.0, 1.0, -6.05)
STAGE_LEN = 0.7


def B(x, y, z):
    """Trailer space -> body hinge space."""
    return (x, y - HINGE[1], z - HINGE[2])


def main(b):
    # Drawbar: A-frame from the eye back to the chassis, eye ring and block, ram mount, jack with crank and foot.
    for s in (-1, 1):
        b.bar((s * 0.04, EYE_Y + 0.02, -0.12), (s * 0.55, 0.85, -1.8), (0.1, 0.12), GREEN)
    b.revolve((0, EYE_Y, 0), [(0.0, -0.0125), (0.07, -0.0125), (0.07, 0.0125), (0.0, 0.0125)], STEEL, segs=14)
    b.box((0, EYE_Y + 0.01, -0.14), (0.12, 0.1, 0.18), GREEN)
    b.box((0, 0.8, -1.48), (0.62, 0.1, 0.16), GREEN)
    for x in (-0.12, 0.12):
        b.box((x, 0.88, -1.48), (0.03, 0.12, 0.12), GREEN)
    jx, jz = 0.3, -0.62
    b.tube((jx, 0.06, jz), (jx, 0.66, jz), 0.035, CAST, segs=10)
    b.tube((jx, 0.4, jz), (jx, 0.64, jz), 0.045, GREEN, segs=10)
    b.bar((jx, 0.7, jz), (jx + 0.16, 0.7, jz), (0.02, 0.02), CAST)
    b.bar((jx + 0.16, 0.7, jz), (jx + 0.16, 0.78, jz), (0.025, 0.025), RUBBER, detail=True)
    b.box((jx, 0.03, jz), (0.18, 0.03, 0.18), CAST)
    b.bar((jx, 0.55, jz), (0.2, 0.64, -0.78), (0.04, 0.04), GREEN, detail=True)    # jack bracket to the drawbar
    # Chassis: rails, cross members, axle, leaf springs with hangers.
    for x in (-0.55, 0.55):
        b.box((x, 0.88, -3.9), (0.12, 0.18, 4.4), GREEN)
    for i in range(4):
        b.box((0, 0.88, -2.0 - i * 1.35), (1.1, 0.12, 0.08), GREEN)
    b.box((0, WHEEL_Y, WHEEL_Z), (1.9, 0.1, 0.1), CAST)
    for x in (-0.55, 0.55):
        for k in range(3):     # leaf pack: shorter leaves below
            b.box((x, 0.71 - k * 0.025, WHEEL_Z), (0.08, 0.022, 1.0 - k * 0.25), CAST)
        for dz in (-0.5, 0.5):
            b.box((x, 0.76, WHEEL_Z + dz), (0.06, 0.1, 0.05), GREEN, detail=True)
        b.box((x, 0.6, WHEEL_Z), (0.1, 0.08, 0.14), CAST, detail=True)   # U-bolt plate
    # Mudguards: curved plates over the wheels on stays.
    for s in (-1, 1):
        x = s * WHEEL_X
        rows = []
        for i in range(9 if b.detail else 4):
            a = math.radians(-60 + 120 * i / ((9 if b.detail else 4) - 1))
            r = WHEEL_R + 0.12
            rows.append([(x + dx, WHEEL_Y + r * math.cos(a), WHEEL_Z + r * math.sin(a)) for dx in (-0.23, 0.23)])
        b.sheet(rows, GREEN, thick=0.01)
        b.bar((s * 0.6, 0.88, WHEEL_Z), (x, WHEEL_Y + WHEEL_R + 0.12, WHEEL_Z), (0.04, 0.03), GREEN)
    # Rear light bar: lamps, amber side reflectors, number plate with a light.
    b.box((0, 0.72, -6.12), (2.1, 0.06, 0.06), CAST)
    b.bar((-0.5, 0.86, -6.0), (-0.5, 0.75, -6.1), (0.04, 0.04), CAST)
    b.bar((0.5, 0.86, -6.0), (0.5, 0.75, -6.1), (0.04, 0.04), CAST)
    for x in (-0.95, 0.95):
        b.box((x, 0.72, -6.16), (0.2, 0.11, 0.05), CAST)
        b.box((x - 0.04, 0.72, -6.19), (0.09, 0.08, 0.01), "Impl_Lamp")
        b.box((x + 0.05, 0.72, -6.19), (0.06, 0.08, 0.01), "Impl_Amber")
    b.box((0.45, 0.72, -6.15), (0.34, 0.1, 0.012), "Impl_White")
    b.text("TR-24-VR", (0.45, 0.72, -6.158), 0.05, CAST, yaw=180, detail=True)
    for x in (-1.1, 1.1):
        b.box((x, 0.86, -5.6), (0.01, 0.06, 0.12), "Impl_Amber", detail=True)
    # Hydraulic hose (tipping) from the ram base along the drawbar to the coupler at the eye; air brake lines (detail).
    pts = [(0.1, 0.84, -1.4), (0.2, 0.8, -1.0), (0.12, 0.62, -0.5), (0.1, 0.52, -0.15)]
    for p0, p1 in zip(pts, pts[1:]):
        b.tube(p0, p1, 0.014, RUBBER, segs=6)
    b.tube((0.1, 0.52, -0.15), (0.1, 0.52, -0.05), 0.022, STEEL, segs=8)
    for x, col in ((-0.14, "Impl_Lamp"), (-0.2, "Impl_Yellow")):
        b.tube((x, 0.85, -1.6), (x, 0.55, -0.35), 0.01, RUBBER, segs=6, detail=True)
        b.tube((x, 0.55, -0.35), (x, 0.52, -0.2), 0.02, col, segs=6, detail=True)


def wheel(b):
    b.tyre((0, 0, 0), WHEEL_R, WHEEL_W, RUBBER, lugs=20, lug_h=0.035, segs=24)
    b.revolve((0, 0, 0), [(0.0, -0.16), (0.3, -0.16), (0.3, 0.14), (0.24, 0.17), (0.0, 0.17)], GREEN, segs=20, roll=90)
    b.revolve((0, 0, 0), [(0.0, -0.2), (0.08, -0.2), (0.08, 0.2), (0.0, 0.2)], STEEL, segs=10, roll=90)
    for k in range(8):       # wheel nuts on both faces (detail)
        a = 2 * math.pi * k / 8
        for x in (-0.2, 0.2):
            b.tube((x, 0.12 * math.cos(a), 0.12 * math.sin(a)), (x * 1.08, 0.12 * math.cos(a), 0.12 * math.sin(a)),
                   0.014, STEEL, segs=6, detail=True)


def body(b):
    # Floor, sub-frame with cross bearers, sides with top rails and stakes, headboard with a ladder, ram bracket.
    b.box(B(0, 1.04, -3.9), (2.3, 0.08, 4.6), GREEN)
    b.box(B(0, 0.97, -3.9), (1.2, 0.06, 4.5), CAST)
    for k in range(6):
        b.box(B(0, 0.975, -1.8 - k * 0.84), (2.2, 0.05, 0.06), GREEN, detail=True)
    for s in (-1, 1):
        x = s * 1.12
        b.box(B(x, 1.43, -3.9), (0.05, 0.7, 4.6), GREEN)
        b.tube(B(x, 1.79, -1.6), B(x, 1.79, -6.2), 0.035, GREEN, segs=8)
        for i in range(6):
            b.box(B(x * 1.03, 1.43, -1.75 - i * 0.86), (0.04, 0.72, 0.07), GREEN)
        b.box(B(x * 1.025, 1.18, -3.9), (0.03, 0.05, 4.6), GREEN, detail=True)     # rub rail
    b.box(B(0, 1.53, -1.63), (2.3, 0.9, 0.05), GREEN)
    for k in range(3):        # headboard ribs (detail)
        b.box(B(-0.7 + k * 0.7, 1.53, -1.6), (0.06, 0.88, 0.03), GREEN, detail=True)
    for i in range(4):
        b.box(B(0.7, 1.2 + i * 0.2, -1.58), (0.35, 0.025, 0.04), STEEL)
    for x in (0.53, 0.87):
        b.box(B(x, 1.5, -1.58), (0.025, 0.7, 0.03), STEEL)
    b.box(B(0, 1.56, -1.55), (0.2, 0.12, 0.08), GREEN)
    for x in (-0.07, 0.07):
        b.box(B(x, 1.56, -1.5), (0.02, 0.14, 0.1), GREEN, detail=True)


def gate(b):
    b.box((0, -0.35, 0), (2.3, 0.7, 0.05), GREEN)
    b.box((0, -0.6, -0.03), (2.2, 0.06, 0.03), CAST)
    for k in range(4):       # pressed ribs (detail)
        b.box((-0.9 + k * 0.6, -0.35, -0.03), (0.06, 0.64, 0.02), GREEN, detail=True)
    for x in (-1.0, 1.0):   # hinge eyes, lock hooks
        b.tube((x - 0.06, 0, 0), (x + 0.06, 0, 0), 0.025, STEEL, segs=8)
        b.box((x * 1.08, -0.62, 0.0), (0.04, 0.08, 0.08), STEEL, detail=True)


def stage(i):
    r = (0.13 - i * 0.012) * 0.5      # the old primitives were scaled by diameter

    def describe(b):
        mat = CAST if i == 0 else CHROME
        b.revolve((0, 0, 0), [(r, 0.0), (r, STAGE_LEN)], mat, segs=14, pitch=90)
        b.revolve((0, 0, 0), [(r + 0.008, STAGE_LEN - 0.04), (r + 0.008, STAGE_LEN)], CAST, segs=14, pitch=90, detail=True)
        if i == 0:   # base trunnion
            b.tube((-0.09, 0, 0.05), (0.09, 0, 0.05), 0.025, STEEL, segs=8)
    return describe


PARTS = {"Main": main, "Wheel": wheel, "Body": body, "Gate": gate, **{f"Stage{i}": stage(i) for i in range(7)}}
NO_GROUND = ("Wheel", "Body", "Gate", *[f"Stage{i}" for i in range(7)])
NAME = "ImplTrailer"
NOTES = "Tipping trailer (remade): frame, wheel, body, tailgate, 7 ram stages, each in its pivot's space."
