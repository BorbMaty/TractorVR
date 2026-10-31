"""900 kg front weight for the front linkage (phase 5 step 2, remade from the primitives in ProjectSetup): a green carrier
frame with the two lower pins and a top bracket, a cast-iron block of stacked slabs with grooves and a sloping top, a tow
eye, a lifting eye on top, a badge. Same size and coupling point as before (HitchPoint at (0, 0.45, 0); the colliders stay
in ProjectSetup).

Implement space: +Z points to the tractor, Y up, ground at y = 0 when parked. One part: Main (static).
"""

GREEN, YELLOW, CAST, STEEL = "Impl_Green", "Impl_Yellow", "Impl_Cast", "Impl_Steel"


def main(b):
    # Carrier: pin bar with the lower pins, two side plates up to the top bracket with its pin hole bosses.
    b.box((0, 0.45, 0.04), (0.82, 0.08, 0.08), GREEN)
    for x in (-0.38, 0.38):
        b.tube((x - 0.06 * (1 if x > 0 else -1), 0.45, 0.04), (x + 0.04 * (1 if x > 0 else -1), 0.45, 0.04), 0.022, YELLOW, segs=8)
        b.tube((x + 0.045 * (1 if x > 0 else -1), 0.45, 0.04), (x + 0.055 * (1 if x > 0 else -1), 0.45, 0.04), 0.035, STEEL,
               segs=8, detail=True)   # linch pin washer
    for x in (-0.25, 0.25):
        b.box((x, 0.66, 0.1), (0.03, 0.5, 0.2), GREEN)
        b.bar((x, 0.48, 0.02), (x * 0.5, 0.88, 0.1), (0.03, 0.06), GREEN, detail=True)   # gusset
    b.box((0, 0.92, 0.1), (0.55, 0.06, 0.18), GREEN)
    for x in (-0.05, 0.05):
        b.box((x, 1.0, 0.06), (0.025, 0.14, 0.12), GREEN)
    b.tube((-0.08, 1.02, 0.06), (0.08, 1.02, 0.06), 0.015, YELLOW, segs=8, detail=True)
    # The cast block: five slabs, narrower towards the top, grooves between them; a sloping top; feet.
    for i in range(5):
        w = 0.98 - i * 0.03
        b.box((0, 0.22 + i * 0.14, 0.42), (w, 0.122, 0.5), CAST)
        b.box((0, 0.22 + i * 0.14 + 0.065, 0.42), (w - 0.04, 0.012, 0.47), CAST, detail=True)   # groove fill (shadowed)
    b.box((0, 0.9, 0.5), (0.82, 0.09, 0.36), CAST, pitch=14)
    b.box((0, 0.04, 0.42), (0.9, 0.08, 0.45), CAST)
    # Lifting eye on top, tow eye low at the front, badge with a yellow stripe and the mass.
    b.revolve((0, 0.98, 0.42), [(0.05, 0.0), (0.05, 0.03)], STEEL, segs=10)
    b.bar((0, 0.98, 0.42), (0, 1.07, 0.42), (0.03, 0.06), STEEL)
    b.revolve((0, 0.16, 0.71), [(0.06, -0.03), (0.06, 0.03)], STEEL, segs=10, pitch=90)
    b.box((0, 0.6, 0.675), (0.4, 0.16, 0.012), GREEN)
    b.box((0, 0.6, 0.682), (0.36, 0.04, 0.01), YELLOW)
    b.text("900 kg", (0, 0.43, 0.678), 0.07, "Impl_White", yaw=0, detail=True)


PARTS = {"Main": main}
NAME = "ImplFrontWeight"
NOTES = "Front weight 900 kg (remade): carrier, cast block, tow / lifting eyes, badge. Colliders and mass in ProjectSetup."
