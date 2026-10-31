"""3 m front disc mower for the front linkage (phase 5 step 2, remade from the primitives in ProjectSetup): red A-frame
headstock with the lower hooks and top bracket, gearbox and PTO shaft guard, arm out to the cutter bar, six discs (own part,
spinning), a sloping hood with side shields, a rubber curtain, skids, warning stripes. Same layout and points as before
(HitchPoint (0, 0.45, 0), disc hubs at (-1.25 + 0.5 i, 0.11, 1.15), skids at x ±1.45).

Implement space (front mount): +Z away from the tractor (forward), Y up, ground at y = 0 when parked.
Parts: Main (frame, hood), Disc (one disc in its hub's space, spinning around local Y; ProjectSetup puts it on all six hubs).
"""

RED, YELLOW, STEEL, RUBBER, CAST = "Impl_Red", "Impl_Yellow", "Impl_Steel", "Impl_Rubber", "Impl_Cast"
W = 3.0


def main(b):
    # Headstock: A-frame, lower bar with the hooks, top bracket, gearbox, PTO shaft guard.
    for s in (-1, 1):
        b.bar((s * 0.36, 0.45, 0.12), (s * 0.06, 1.0, 0.12), (0.08, 0.08), RED)
        b.box((s * 0.38, 0.45, 0.05), (0.05, 0.12, 0.1), YELLOW)
    b.box((0, 0.45, 0.12), (0.8, 0.09, 0.09), RED)
    b.box((0, 1.0, 0.12), (0.14, 0.12, 0.12), RED)
    b.tube((-0.09, 1.0, 0.12), (0.09, 1.0, 0.12), 0.016, YELLOW, segs=8, detail=True)
    b.box((0, 0.6, 0.38), (0.3, 0.26, 0.26), CAST)
    b.box((0, 0.75, 0.38), (0.2, 0.05, 0.2), CAST, detail=True)          # gearbox cover
    b.revolve((0, 0.62, 0.0), [(0.05, -0.05), (0.06, 0.0), (0.06, 0.14), (0.05, 0.2)], YELLOW, segs=12, pitch=90)
    b.bar((0, 0.55, 0.38), (0, 0.3, 0.9), (0.18, 0.14), RED)              # arm to the cutter bar
    # Hydraulic hose from the headstock to the lift cylinder (detail).
    b.tube((0.25, 0.75, 0.12), (0.3, 0.55, 0.5), 0.012, RUBBER, segs=6, detail=True)
    b.tube((0.3, 0.55, 0.5), (0.35, 0.4, 0.75), 0.012, RUBBER, segs=6, detail=True)
    # Cutter bar, nose, skids.
    b.box((0, 0.06, 1.15), (W, 0.06, 0.5), STEEL)
    b.box((0, 0.05, 1.42), (W, 0.04, 0.06), CAST)
    for x in (-1.45, 1.45):
        b.box((x, 0.02, 1.15), (0.08, 0.03, 0.55), STEEL)
    # Guard: frame tube, sloping hood, top panel, side shields, curtain.
    b.tube((-W * 0.5, 0.55, 1.0), (W * 0.5, 0.55, 1.0), 0.03, RED, segs=8)
    b.box((0, 0.48, 1.08), (W, 0.03, 0.76), RED, pitch=12.5)
    b.box((0, 0.57, 0.64), (W, 0.03, 0.2), RED)
    for x in (-1.52, 1.52):
        b.box((x, 0.3, 1.05), (0.03, 0.5, 0.85), RED)
        # Red / white warning stripes on the shield's outside (detail), a reflector.
        for k in range(4):
            b.box((x + (0.017 if x > 0 else -0.017), 0.3, 0.8 + k * 0.16), (0.005, 0.42, 0.07),
                  "Impl_White" if k % 2 else "Impl_Lamp", detail=True)
        b.box((x + (0.02 if x > 0 else -0.02), 0.45, 1.4), (0.01, 0.06, 0.06), "Impl_Amber", detail=True)
    b.box((0, 0.22, 1.48), (W, 0.36, 0.015), RUBBER)
    for k in range(1, 12):     # curtain slits (detail)
        b.box((-W * 0.5 + k * W / 12, 0.18, 1.49), (0.01, 0.28, 0.005), CAST, detail=True)
    # Hood ribs (detail).
    for x in (-1.0, 0.0, 1.0):
        b.box((x, 0.5, 1.08), (0.04, 0.04, 0.74), RED, pitch=12.5, detail=True)


def disc(b):
    """One disc in its hub's space: oval plate, two knives, cap."""
    b.revolve((0, 0, 0), [(0.0, -0.008), (0.17, -0.008), (0.18, 0.0), (0.17, 0.008), (0.0, 0.012)], RED, segs=16)
    for s in (-1, 1):
        b.box((s * 0.2, 0, 0), (0.1, 0.006, 0.045), STEEL, yaw=s * 8)
        b.tube((s * 0.17, -0.01, 0), (s * 0.17, 0.014, 0), 0.01, STEEL, segs=6, detail=True)
    b.revolve((0, 0.01, 0), [(0.05, 0.0), (0.05, 0.02), (0.03, 0.03)], CAST, segs=10)


PARTS = {"Main": main, "Disc": disc}
NO_GROUND = ("Disc",)
NAME = "ImplMower"
NOTES = "3 m front disc mower (remade): A-frame, gearbox, arm, cutter bar, hood, shields with warning stripes; discs (own part)."
