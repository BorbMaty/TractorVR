"""Open-front machine shed on the south side of the yard (WorldLayout.Shed: 36 x 10 m), open to the yard (+Z, north).
Board walls on a whitewashed plinth at the back and sides, timber posts and header along the open front, a lean-to roof
of weathered corrugated sheets (high at the front, so the rain runs off behind). Four parking bays, one per implement
(the tutorial's spots), with yellow lines between them, a white line along the entrance and a name board on the back wall.

Unity coordinates, origin = centre of the footprint on the ground, +Z = the open front.
Bay order and size must match WorldLayout.ShedBays (bay i centre x = -W/2 + (i + 0.5) * W/4).
"""
import math

W, D = 36.0, 10.0
FRONT_H, BACK_H = 5.4, 4.4       # eave heights (underside of the roof)
PLINTH = 0.5
T_WALL, T_ROOF = 0.16, 0.06
OVER_FRONT, OVER_BACK, OVER_SIDE = 0.8, 0.5, 0.4
BAYS = ["FRONT WEIGHT", "MOWER", "PLOUGH", "TRAILER"]   # west -> east
BAY_W = W / len(BAYS)
FLOOR = 0.03                      # the yard concrete is 3 cm above the terrain


def roof_y(z):
    """Underside of the roof at z (straight from the back eave to the front eave)."""
    return BACK_H + (FRONT_H - BACK_H) * (z + D * 0.5) / D


def describe(b):
    hx, hz = W * 0.5, D * 0.5
    zb = -hz + T_WALL * 0.5            # back wall centre line
    xs = hx - T_WALL * 0.5             # side walls centre lines
    pitch = math.degrees(math.atan2(FRONT_H - BACK_H, D))

    # Back and side walls: plaster plinth, boards above it up to the roof.
    b.wall((-hx, zb), (hx, zb), -0.3, PLINTH, PLINTH, T_WALL + 0.06, "Env_Plaster")
    b.wall((-hx, zb), (hx, zb), PLINTH, BACK_H, BACK_H, T_WALL, "Env_Boards")
    for sx in (-1, 1):
        b.wall((sx * xs, -hz), (sx * xs, hz - 0.2), -0.3, PLINTH, PLINTH, T_WALL + 0.06, "Env_Plaster")
        b.wall((sx * xs, -hz), (sx * xs, hz - 0.2), PLINTH, roof_y(-hz), roof_y(hz - 0.2), T_WALL, "Env_Boards")

    # Front: posts between the bays and at the corners, a header beam, knee braces (detail).
    zf = hz - 0.2
    for i in range(len(BAYS) + 1):
        x = -hx + 0.15 + i * (W - 0.3) / len(BAYS)
        b.box((x, FRONT_H * 0.5, zf), (0.26, FRONT_H, 0.26), "Env_Timber")
        for s in (-1, 1):
            if abs(x + s * 0.8) > hx:
                continue
            b.box((x + s * 0.45, FRONT_H - 0.75, zf), (0.12, 1.25, 0.12), "Env_Timber", roll=-s * 45, detail=True)
    b.box((0, FRONT_H - 0.15, zf), (W, 0.3, 0.3), "Env_Timber")
    # Back-wall posts inside (carry the back of the roof), a wall plate on top.
    for i in range(len(BAYS) + 1):
        x = -hx + 0.25 + i * (W - 0.5) / len(BAYS)
        b.box((x, BACK_H * 0.5, -hz + T_WALL + 0.1), (0.2, BACK_H, 0.2), "Env_Timber")
    b.box((0, BACK_H - 0.1, -hz + T_WALL + 0.1), (W - 0.2, 0.2, 0.2), "Env_Timber")

    # Rafters every ~3 m (detail) under a single slab of corrugated sheets.
    length = math.hypot(D + OVER_FRONT + OVER_BACK, FRONT_H - BACK_H)
    zc = (OVER_FRONT - OVER_BACK) * 0.5
    for k in range(13):
        x = -hx + 0.2 + k * (W - 0.4) / 12
        b.box((x, roof_y(zc) - 0.1, zc), (0.1, 0.18, length - 0.2), "Env_Timber", pitch=-pitch, detail=True)
    b.box((0, roof_y(zc) + T_ROOF * 0.5, zc), (W + 2 * OVER_SIDE, T_ROOF, length), "Env_Corrugated", pitch=-pitch)
    # Fascia board along the front edge (detail).
    zfront = hz + OVER_FRONT
    b.box((0, roof_y(zfront) - 0.06, zfront), (W + 2 * OVER_SIDE, 0.2, 0.04), "Env_Timber", detail=True)

    # Floor marks: yellow lines between the bays, a white line along the entrance.
    y = FLOOR + 0.006
    for i in range(1, len(BAYS)):
        b.box((-hx + i * BAY_W, y, -0.1), (0.12, 0.012, D - 0.8), "Env_PaintYellow", cell=3.0)
    b.box((0, y, hz - 0.55), (W - 0.6, 0.012, 0.12), "Env_PaintWhite", cell=3.0)

    # Name boards on the back wall, one per bay.
    for i, name in enumerate(BAYS):
        x = -hx + (i + 0.5) * BAY_W
        zw = -hz + T_WALL + 0.025
        b.box((x, 3.05, zw), (0.32 * len(name) + 0.6, 0.72, 0.05), "Env_BoardsDark", cell=3.0)
        b.text(name, (x, 3.05, zw + 0.035), 0.4, "Env_PaintWhite")


NOTES = ("Machine shed, 36 x 10 m, open to the north, lean-to roof %.1f -> %.1f m, bays %s (west -> east). "
         "Colliders: walls and front posts (ProjectSetup)." % (FRONT_H, BACK_H, ", ".join(BAYS)))
