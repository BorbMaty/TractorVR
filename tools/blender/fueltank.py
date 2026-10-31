"""Farm diesel tank in the yard (WorldLayout.FuelTank): a red horizontal tank on two steel saddles over a concrete
bund, a pump with a hose and nozzle on top, DIESEL on the side facing the yard.

Unity coordinates, origin = centre of the bund on the ground, tank axis along Z, the yard to -X.
"""

R, HALF = 0.65, 1.3        # tank radius, half length (cylindrical part)
CY = 0.95 + R              # tank axis height


def describe(b):
    b.box((0, 0.05, 0), (2.0, 0.3, 3.6), "Env_Concrete")
    for z in (-0.85, 0.85):     # saddles
        b.box((0, (0.2 + CY - R * 0.6) * 0.5, z), (1.1, CY - R * 0.6 - 0.2, 0.16), "Env_Steel")
        b.box((0, 0.22, z), (1.3, 0.04, 0.3), "Env_Steel", detail=True)
    b.revolve((0, CY, 0), [(0.0, -HALF - 0.1), (R * 0.75, -HALF - 0.04), (R, -HALF), (R, HALF), (R * 0.75, HALF + 0.04),
                           (0.0, HALF + 0.1)], "Env_PaintRed", segs=20, pitch=90)
    b.box((0, CY + R + 0.06, -0.6), (0.3, 0.12, 0.3), "Env_Steel")                  # filler cap
    # Pump on the tank, hose down to the nozzle holder on the front saddle.
    px, py, pz = -0.15, CY + R + 0.25, 0.55
    b.box((px, py, pz), (0.36, 0.5, 0.3), "Env_Steel")
    b.box((px - 0.19, py + 0.05, pz), (0.02, 0.18, 0.14), "Env_PaintWhite", detail=True)   # meter face
    b.tube((px - 0.1, py - 0.1, pz + 0.15), (-0.75, CY, 0.95), 0.025, "Env_Glass", segs=6)
    b.tube((-0.75, CY, 0.95), (-0.6, 1.0, 0.95), 0.025, "Env_Glass", segs=6)
    b.box((-0.62, 0.95, 0.95), (0.18, 0.1, 0.06), "Env_Steel")                      # nozzle in its holder
    b.text("DIESEL", (-R - 0.015, CY, 0.0), 0.2, "Env_PaintWhite", yaw=-90, detail=True)
    b.collider((0, 1.2, 0), (1.5, 2.4, 3.2))


NOTES = "Diesel tank, Ø %.1f m x %.1f m on saddles, pump on top. Collider: one box (from the JSON)." % (2 * R, 2 * HALF)

NAME = "FuelTank"   # asset / FBX name
