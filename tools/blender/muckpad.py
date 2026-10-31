"""Muck heap / tipping pad in the north-west of the yard (WorldLayout.TipPad: 10 x 9 m): a concrete slab with
walls on three sides, open to the south so the trailer reverses in (tractor facing south) and tips against the
back wall. A muck heap fills the back half; a TIPPING PAD board on the back wall (the tutorial's spot).

Unity coordinates, origin = centre of the slab on the ground, open side -Z (south).
"""

W, D = 10.0, 9.0
WALL_H, WALL_T = 1.8, 0.3
SLAB = 0.06              # slab top (the yard concrete is at 0.03)


def describe(b):
    hx, hz = W * 0.5, D * 0.5
    b.box((0, SLAB - 0.12, 0), (W, 0.24, D), "Env_Concrete")
    # Walls (from 0.3 m below the ground), a cap on each (detail).
    walls = [((-hx + WALL_T * 0.5, 0), (WALL_T, D)), ((hx - WALL_T * 0.5, 0), (WALL_T, D)),
             ((0, hz - WALL_T * 0.5), (W - 2 * WALL_T, WALL_T))]
    for (x, z), (sx, sz) in walls:
        b.box((x, (WALL_H - 0.3) * 0.5, z), (sx, WALL_H + 0.3, sz), "Env_Concrete")
        b.box((x, WALL_H + 0.02, z), (sx + 0.06, 0.04, sz + 0.06), "Env_Steel", detail=True)
        b.collider((x, WALL_H * 0.5, z), (sx, WALL_H, sz))
    # Wall ends: steel corner guards where the trailer passes (detail).
    for sx in (-1, 1):
        b.box((sx * (hx - WALL_T * 0.5), WALL_H * 0.5, -hz + 0.04), (WALL_T + 0.04, WALL_H, 0.08), "Env_PaintYellow")
    # The heap against the back wall, a smaller one in the corner.
    b.heap((0.4, SLAB, hz - 2.2), 8.6, 4.2, 1.5, "Env_Muck", seed=2)
    b.heap((-3.2, SLAB, 0.2), 2.6, 2.4, 0.6, "Env_Muck", seed=5, detail=True)
    # Sign on the back wall, readable from the yard (south).
    b.box((0, WALL_H + 0.55, hz - 0.12), (3.4, 0.6, 0.06), "Env_BoardsDark", cell=3.0)
    for x in (-1.4, 1.4):
        b.box((x, WALL_H + 0.2, hz - 0.05), (0.08, 0.9, 0.08), "Env_Timber")   # behind the board
    b.text("TIPPING PAD", (0, WALL_H + 0.55, hz - 0.16), 0.32, "Env_PaintWhite", yaw=180)


NOTES = ("Tipping pad / muck heap, %.0f x %.0f m, walls %.1f m on three sides, open to the south. "
         "Colliders: the three walls (from the JSON); the heap has none." % (W, D, WALL_H))

NAME = "MuckPad"   # asset / FBX name
