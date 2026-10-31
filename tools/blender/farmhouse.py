"""Farmhouse on the east side of the yard (WorldLayout.Farmhouse: 9 x 15 m), Transylvanian village style:
long and narrow, gable end to the road (south), a covered porch ("tornác") along the long side facing the yard (west)
under the same steep clay-tile roof, plastered walls in a warm ochre on a grey-blue plinth, a boarded gable,
white window frames with green shutters, a chimney.

Unity coordinates, origin = centre of the footprint (porch included) on the ground, +Z = north, -X = the yard.
detail=True parts are in the Full (PC) variant only.
"""
import math

W, L = 9.0, 15.0           # footprint with the porch (x, z)
PORCH = 2.0                # porch depth along the west side
PLINTH, EAVE = 0.6, 3.4    # top of the plinth, top of the walls
PITCH = 45.0
RISE = W * 0.5 * math.tan(math.radians(PITCH))
EAVE_OVER, GABLE_OVER = 0.6, 0.4
T_ROOF = 0.16
DECK = 0.45                # porch floor height
WIN_W, WIN_H, SILL = 1.0, 1.3, 1.1

HX, HZ = W * 0.5, L * 0.5
WX0 = -HX + PORCH          # west face of the walls (behind the porch)
WXC = (WX0 + HX) * 0.5     # centre of the wall block
WALL_W = HX - WX0


def place(b, base, yaw, local, size, mat, detail=False):
    """Box in a wall's own frame: x along the wall, y up, z out of the wall; `base` = point on the wall face (Unity)."""
    c, s = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    x, y, z = local
    b.box((base[0] + x * c + z * s, y, base[1] - x * s + z * c), size, mat, yaw=yaw, detail=detail)


def window(b, base, yaw):
    """Window with its frame, sill and two open shutters, on the wall face at `base` facing `yaw`."""
    yc = SILL + WIN_H * 0.5
    place(b, base, yaw, (0, yc, 0.02), (WIN_W, WIN_H, 0.04), "Env_Glass")
    f = 0.08
    for sx in (-1, 1):
        place(b, base, yaw, (sx * (WIN_W + f) * 0.5, yc, 0.05), (f, WIN_H + 2 * f, 0.08), "Env_PaintWhite")
    for sy in (-1, 1):
        place(b, base, yaw, (0, yc + sy * (WIN_H + f) * 0.5, 0.05), (WIN_W, f, 0.08), "Env_PaintWhite")
    place(b, base, yaw, (0, yc, 0.05), (0.05, WIN_H, 0.06), "Env_PaintWhite", detail=True)
    place(b, base, yaw, (0, SILL + WIN_H * 0.68, 0.05), (WIN_W, 0.05, 0.06), "Env_PaintWhite", detail=True)
    place(b, base, yaw, (0, SILL - 0.06, 0.08), (WIN_W + 0.3, 0.07, 0.16), "Env_PlasterBase")
    for sx in (-1, 1):   # shutter leaves folded back against the wall
        place(b, base, yaw, (sx * (WIN_W * 0.75 + 0.12), yc, 0.04), (WIN_W * 0.5, WIN_H + 0.1, 0.04), "Env_PaintGreen")
        for sy in (-1, 1):
            place(b, base, yaw, (sx * (WIN_W * 0.75 + 0.12), yc + sy * WIN_H * 0.3, 0.07), (WIN_W * 0.45, 0.06, 0.02),
                  "Env_PaintGreen", detail=True)


def describe(b):
    # Plinth under the walls (0.3 m below the ground), plastered wall block, boarded gable over the whole width.
    b.box((WXC, (PLINTH - 0.3) * 0.5, 0), (WALL_W + 0.1, PLINTH + 0.3, L + 0.1), "Env_PlasterBase")
    b.box((WXC, (PLINTH + EAVE) * 0.5, 0), (WALL_W, EAVE - PLINTH, L), "Env_PlasterHouse")
    b.gable(-HX, HX, EAVE, RISE, -HZ, HZ, "Env_Boards")
    # Eave cornice: a plaster band under the roof on the long walls.
    b.box((WXC, EAVE - 0.12, -HZ - 0.03), (WALL_W + 0.06, 0.24, 0.08), "Env_PlasterBase", detail=True)
    b.box((HX + 0.03, EAVE - 0.12, 0), (0.08, 0.24, L + 0.12), "Env_PlasterBase", detail=True)

    # Porch: plinth-high floor with a board deck, two steps at the door, posts carrying a beam, a board parapet.
    px = -HX + PORCH * 0.5
    b.box((px, (DECK - 0.3) * 0.5, 0), (PORCH, DECK + 0.3, L), "Env_PlasterBase")
    b.box((px + 0.02, DECK + 0.02, 0), (PORCH - 0.06, 0.04, L - 0.06), "Env_BoardsDark")
    for top, x0 in ((0.15, -HX - 0.6), (0.30, -HX - 0.3)):
        b.box(((x0 - HX) * 0.5, (top - 0.3) * 0.5, 0), (-HX - x0, top + 0.3, 1.4), "Env_PlasterBase")
    xp = -HX + 0.15
    posts = [-HZ + 0.2 + k * (L - 0.4) / 5 for k in range(6)]
    for z in posts:
        b.box((xp, (DECK + EAVE) * 0.5, z), (0.18, EAVE - DECK, 0.18), "Env_Timber")
        for s in (-1, 1):   # knee braces under the beam (detail)
            if abs(z + s * 0.6) < HZ:
                b.box((xp, EAVE - 0.55, z + s * 0.32), (0.09, 0.8, 0.09), "Env_Timber", pitch=s * 45, detail=True)
    b.box((xp, EAVE - 0.11, 0), (0.2, 0.22, L), "Env_Timber")
    rail_y0, rail_y1 = DECK + 0.04, DECK + 0.9
    ry = (rail_y0 + rail_y1) * 0.5
    for z0, z1 in zip(posts[:-1], posts[1:]):
        if z0 < 0 < z1:          # the opening to the steps
            continue
        b.box((xp, ry, (z0 + z1) * 0.5), (0.05, rail_y1 - rail_y0, z1 - z0 - 0.18), "Env_Boards")
        b.box((xp, rail_y1 + 0.03, (z0 + z1) * 0.5), (0.12, 0.06, z1 - z0 - 0.18), "Env_Timber", detail=True)
    for sz in (-1, 1):           # parapet across the porch ends
        b.box(((xp + WX0) * 0.5, ry, sz * (HZ - 0.15)), (WX0 - xp - 0.18, rail_y1 - rail_y0, 0.05), "Env_Boards")

    # Front door behind the porch (west wall, middle), windows all round.
    place(b, (WX0, 0), -90, (0, DECK + 1.07, 0.03), (1.0, 2.14, 0.06), "Env_BoardsDark")
    for sx in (-1, 1):
        place(b, (WX0, 0), -90, (sx * 0.56, DECK + 1.1, 0.05), (0.1, 2.2, 0.1), "Env_PaintWhite")
    place(b, (WX0, 0), -90, (0, DECK + 2.25, 0.05), (1.22, 0.1, 0.1), "Env_PaintWhite")
    for z in (-4.4, 4.4):
        window(b, (WX0, z), -90)
    for z in (-4.5, 0.0, 4.5):
        window(b, (HX, z), 90)
    for x in (WXC - 1.4, WXC + 1.4):
        window(b, (x, -HZ), 180)          # two windows to the road (south gable)
    window(b, (WXC + 1.0, HZ), 0)

    # Roof: two slabs from the ridge past the eaves, tile ridge, fascia and barge boards (detail).
    c, s = math.cos(math.radians(PITCH)), math.sin(math.radians(PITCH))
    ridge_y = EAVE + RISE
    length = math.hypot(HX, RISE) + EAVE_OVER
    for side in (-1, 1):
        d, n = (side * c, -s), (side * s, c)
        cx = d[0] * length * 0.5 + n[0] * T_ROOF * 0.5
        cy = ridge_y + d[1] * length * 0.5 + n[1] * T_ROOF * 0.5
        b.box((cx, cy, 0), (length, T_ROOF, L + 2 * GABLE_OVER), "Env_RoofTiles", roll=-side * PITCH)
        ex, ey = d[0] * length + n[0] * T_ROOF * 0.5, ridge_y + d[1] * length
        b.box((ex, ey, 0), (0.05, 0.22, L + 2 * GABLE_OVER), "Env_Timber", roll=-side * PITCH, detail=True)
        for sz in (-1, 1):
            b.box((cx, cy, sz * (HZ + GABLE_OVER + 0.02)), (length, T_ROOF + 0.1, 0.05), "Env_Timber",
                  roll=-side * PITCH, detail=True)
    b.box((0, ridge_y + 0.08, 0), (0.3, 0.3, L + 2 * GABLE_OVER + 0.1), "Env_RoofTiles", roll=45)

    # Gables: a small attic window to the road, a louvred vent to the north (detail).
    b.box((0, EAVE + RISE * 0.4, -HZ - 0.03), (0.86, 0.96, 0.04), "Env_PaintWhite", detail=True)   # frame behind the glass
    b.box((0, EAVE + RISE * 0.4, -HZ - 0.06), (0.7, 0.8, 0.04), "Env_Glass")
    b.box((0, EAVE + RISE * 0.45, HZ + 0.05), (0.9, 0.7, 0.06), "Env_BoardsDark", detail=True)

    # Chimney through the east slope near the ridge.
    chx, chz = 1.0, 2.5
    top = ridge_y + 0.9
    b.box((chx, (EAVE + top) * 0.5, chz), (0.55, top - EAVE, 0.55), "Env_PlasterHouse")
    b.box((chx, top + 0.05, chz), (0.7, 0.1, 0.7), "Env_PlasterBase")


NOTES = ("Farmhouse, 9 x 15 m (porch 2 m on the yard side), ridge %.1f m, roof %d deg. "
         "Collider: one box over the whole footprint up to the eaves (ProjectSetup)." % (EAVE + RISE, PITCH))
