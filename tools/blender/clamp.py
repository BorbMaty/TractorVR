"""Silage clamp behind the barn (WorldLayout.SilageClamp): two concrete walls 32 m long, 8 m apart, and a silage heap between
them under a black sheet weighted with old tyres, ramped at 1:3 (18°) at both ends: the tractor drives up and over it like
when rolling silage. Steeper than any hill (~11°).

Unity coordinates, origin = middle of the clamp on the ground, the clamp along local X (placed with yaw 90: along the world Z).
"""
import math

L, WIDTH = 32.0, 8.0          # wall length, clear width between the walls
WALL_H, WALL_T = 2.2, 0.3
HEAP_H = 2.4
RAMP = HEAP_H * 3.0           # 1:3
X0, XA = -L * 0.5, -L * 0.5 + RAMP


def describe(b):
    hz = WIDTH * 0.5
    # Concrete slab and the walls (0.3 m into the ground), steel edge on top (detail).
    b.box((0, -0.1, 0), (L, 0.3, WIDTH + 2 * WALL_T), "Env_Concrete")
    for sz in (-1, 1):
        z = sz * (hz + WALL_T * 0.5)
        b.box((0, (WALL_H - 0.3) * 0.5, z), (L, WALL_H + 0.3, WALL_T), "Env_Concrete")
        b.box((0, WALL_H + 0.02, z), (L + 0.04, 0.04, WALL_T + 0.04), "Env_Steel", detail=True)
        b.collider((0, WALL_H * 0.5, z), (L, WALL_H, WALL_T))
    # The heap under its sheet, ramps at both ends, the sheet tucked down the inside of the walls.
    prof = [(X0, 0.05), (-X0, 0.05), (-X0 + 0.0, 0.08), (-XA, HEAP_H), (XA, HEAP_H), (X0, 0.08)]
    b.prism(prof, -hz + 0.02, hz - 0.02, "Env_SilageSheet")
    # Tyres on the sheet (detail): rows across the top and down the ramps.
    for x in [XA + 0.8 + k * 1.6 for k in range(int((-2 * XA - 1.6) / 1.6) + 1)]:
        for z in (-3.0, -1.5, 0.0, 1.5, 3.0):
            b.revolve((x, HEAP_H + 0.01, z), [(0.42, 0.0), (0.42, 0.2)], "Env_RubberBlack", segs=10, detail=True)
    a = math.atan2(HEAP_H, RAMP)
    for side in (-1, 1):
        for k in range(1, 4):
            d = k * RAMP / 4.0
            x = side * (L * 0.5 - d)
            y = d * math.tan(a)
            for z in (-2.25, 0.0, 2.25):
                b.revolve((x, y + 0.01, z), [(0.42, 0.0), (0.42, 0.2)], "Env_RubberBlack", segs=10, roll=-side * math.degrees(a),
                          detail=True)
    # Wheel-ray colliders: ramp up, top, ramp down.
    b.slope_collider((X0, 0.08), (XA, HEAP_H), -hz, hz)
    b.slope_collider((XA, HEAP_H), (-XA, HEAP_H), -hz, hz)
    b.slope_collider((-XA, HEAP_H), (-X0, 0.08), -hz, hz)


NOTES = ("Silage clamp, walls %.0f m x %.1f m, %.0f m apart, heap %.1f m high with 1:3 ramps (%.1f°) at both ends. "
         "Colliders: walls, ramps, top." % (L, WALL_H, WIDTH, HEAP_H, math.degrees(math.atan2(HEAP_H, RAMP))))
NAME = "SilageClamp"
