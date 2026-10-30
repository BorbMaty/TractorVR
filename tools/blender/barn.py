"""Barn on the west side of the yard (WorldLayout.Barn: 14 x 24 m), Transylvanian style:
whitewashed plinth, vertical weathered boards on a dark timber frame, steep clay-tile roof (40 deg),
big double doors on the long side facing the yard (+X).

Unity coordinates, origin = centre of the footprint on the ground, +X = towards the yard.
detail=True parts are in the Full (PC) variant only.
"""
import math

W, L = 14.0, 24.0          # footprint (x, z)
PLINTH, EAVE = 0.8, 6.0    # top of the plinth, top of the walls
PITCH = 40.0               # roof pitch (deg)
RISE = W * 0.5 * math.tan(math.radians(PITCH))
EAVE_OVER, GABLE_OVER = 0.7, 0.5
T_ROOF = 0.18
DOOR_W, DOOR_H = 5.0, 4.6


def describe(b):
    hx, hz = W * 0.5, L * 0.5

    # Plinth: reaches 0.3 m below the ground, so it stays buried on slightly uneven terrain.
    b.box((0, 0.25, 0), (W + 0.12, 1.1, L + 0.12), "Env_Plaster")
    # Walls (one closed block, the barn isn't entered) and the gable triangle on top of them.
    b.box((0, (PLINTH + EAVE) * 0.5, 0), (W, EAVE - PLINTH, L), "Env_Boards")
    b.gable(-hx, hx, EAVE, RISE, -hz, hz, "Env_Boards")

    # Timber frame: sill on the plinth, wall plate under the eaves, corner posts.
    for y, h in ((PLINTH + 0.1, 0.2), (EAVE - 0.12, 0.24)):
        b.box((0, y, -hz - 0.04), (W + 0.16, h, 0.12), "Env_Timber")
        b.box((0, y, hz + 0.04), (W + 0.16, h, 0.12), "Env_Timber")
        b.box((-hx - 0.04, y, 0), (0.12, h, L + 0.16), "Env_Timber")
        b.box((hx + 0.04, y, 0), (0.12, h, L + 0.16), "Env_Timber")
    for sx in (-1, 1):
        for sz in (-1, 1):
            b.box((sx * (hx + 0.02), (PLINTH + EAVE) * 0.5, sz * (hz + 0.02)), (0.28, EAVE - PLINTH, 0.28), "Env_Timber")

    # Studs every ~2 m on the long walls and 1.75 m on the gable walls (detail).
    for k in range(1, 12):
        z = -hz + k * L / 12
        if abs(z) < DOOR_W * 0.5 + 0.3:
            continue
        for sx in (-1, 1):
            b.box((sx * (hx + 0.03), (PLINTH + EAVE) * 0.5, z), (0.08, EAVE - PLINTH - 0.4, 0.14), "Env_Timber", detail=True)
    for k in range(1, 8):
        x = -hx + k * W / 8
        for sz in (-1, 1):
            b.box((x, (PLINTH + EAVE) * 0.5, sz * (hz + 0.03)), (0.14, EAVE - PLINTH - 0.4, 0.08), "Env_Timber", detail=True)

    # Roof: two slabs from the ridge down past the eaves, a ridge of tiles on top.
    c, s = math.cos(math.radians(PITCH)), math.sin(math.radians(PITCH))
    ridge_y = EAVE + RISE
    length = math.hypot(hx, RISE) + EAVE_OVER
    for side in (-1, 1):
        d = (side * c, -s)                       # down the slope
        n = (side * s, c)                        # out of the roof
        cx = d[0] * length * 0.5 + n[0] * T_ROOF * 0.5
        cy = ridge_y + d[1] * length * 0.5 + n[1] * T_ROOF * 0.5
        b.box((cx, cy, 0), (length, T_ROOF, L + 2 * GABLE_OVER), "Env_RoofTiles", roll=-side * PITCH)
        # Fascia board along the eave, barge boards up the gable ends (detail).
        ex = d[0] * length + n[0] * T_ROOF * 0.5
        ey = ridge_y + d[1] * length
        b.box((ex, ey, 0), (0.05, 0.25, L + 2 * GABLE_OVER), "Env_Timber", roll=-side * PITCH, detail=True)
        for sz in (-1, 1):
            b.box((cx, cy, sz * (hz + GABLE_OVER + 0.02)), (length, T_ROOF + 0.12, 0.05), "Env_Timber",
                  roll=-side * PITCH, detail=True)
    b.box((0, ridge_y + 0.1, 0), (0.34, 0.34, L + 2 * GABLE_OVER + 0.1), "Env_RoofTiles", roll=45)

    # Double doors on the yard side, header beam above, Z braces on each leaf (detail).
    dx = hx + 0.11
    for sz in (-1, 1):
        zc = sz * DOOR_W * 0.25
        b.box((dx, DOOR_H * 0.5, zc), (0.1, DOOR_H, DOOR_W * 0.5 - 0.04), "Env_BoardsDark")
        for y in (0.45, DOOR_H - 0.45):
            b.box((dx + 0.07, y, zc), (0.05, 0.18, DOOR_W * 0.5 - 0.2), "Env_Timber", detail=True)
        diag = math.hypot(DOOR_W * 0.5 - 0.3, DOOR_H - 0.9)
        ang = math.degrees(math.atan2(DOOR_H - 0.9, DOOR_W * 0.5 - 0.3))
        b.box((dx + 0.07, DOOR_H * 0.5, zc), (0.05, 0.16, diag), "Env_Timber", pitch=-sz * ang, detail=True)
    b.box((hx + 0.1, DOOR_H + 0.2, 0), (0.2, 0.3, DOOR_W + 0.6), "Env_Timber")

    # Louvred vents high in both gables (detail).
    for sz in (-1, 1):
        b.box((0, EAVE + RISE * 0.55, sz * (hz + 0.05)), (1.2, 0.9, 0.06), "Env_BoardsDark", detail=True)
        for k in range(4):
            b.box((0, EAVE + RISE * 0.55 - 0.32 + k * 0.21, sz * (hz + 0.09)), (1.3, 0.05, 0.05), "Env_Timber", detail=True)


NOTES = "Barn, 14 x 24 m, ridge %.1f m, roof %d deg. Collider: BoxCollider on the wall block (ProjectSetup)." % (EAVE + RISE, PITCH)
