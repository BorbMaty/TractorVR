"""Yard gate and fences, in WORLD coordinates (the asset is placed at the origin):
- a Székely-style carved gate (székelykapu) where the yard road leaves for the main road (x = GATE_X): two big posts
  around the 5.1 m opening (4.5 m clear height: tractor with beacons 3.3 m), a pedestrian door on the north side,
  an inscription board, a pigeon-hole frieze and a small tiled roof; the big leaves stand open against the yard side;
- a picket fence around the farmhouse garden (WorldLayout.Farmhouse inside it), with an opening to the porch;
- a post-and-rail fence from the gate southwards along the east edge of the yard.

The tractor passes through: PhysicsTest I (x 30, z 0, facing north) and the gravel-track tests (x 32, z 0, east) stay clear.
Light variant: the pickets become one board panel per section, no carving, no frieze holes.
Colliders: posts, leaves, one thin box per fence run (from the JSON).
"""
import math

GATE_X = 28.0
OPEN = 2.55              # half the big opening (clear)
POST = 0.4
POST_H = 5.0
DOOR_Z = (2.95, 4.05)    # pedestrian door, clear width
LINTEL = 4.5             # underside of the beam over the big opening

GARDEN = (GATE_X, 4.2, 44.0, 25.0)        # x0, z0 (south side, along the road), x1, z1
PORCH_GAP = (12.4, 14.6)                   # opening in the west side, in front of the porch steps (z 13.5)
WING = (-2.95 - POST * 0.5, -22.0)         # rail fence south of the gate: z from, z to


def gate(b):
    x = GATE_X
    zs = (-OPEN - POST * 0.5, OPEN + POST * 0.5, DOOR_Z[1] + POST * 0.5)
    for z in zs:
        b.box((x, POST_H * 0.5 - 0.15, z), (POST, POST_H + 0.3, POST), "Env_Timber")
        b.collider((x, 2.0, z), (POST, 4.0, POST))
        # Carved band on each post (detail): three thin rings of boards around the post at 1.2 m.
        for k in range(3):
            b.box((x, 1.1 + k * 0.12, z), (POST + 0.04, 0.05, POST + 0.04), "Env_BoardsDark", detail=True)
    z0, z1 = zs[0] - POST * 0.5, zs[-1] + POST * 0.5
    zc, span = (z0 + z1) * 0.5, z1 - z0
    # Beam over the big opening, curved-looking braces in its corners (two straight pieces each, detail: one).
    b.box((x, LINTEL + 0.12, 0), (0.3, 0.24, 2 * OPEN), "Env_Timber")
    for s in (-1, 1):
        b.box((x, LINTEL - 0.35, s * (OPEN - 0.45)), (0.22, 0.12, 1.15), "Env_Timber", pitch=s * 40)
        b.box((x, LINTEL - 0.12, s * (OPEN - 0.18)), (0.22, 0.12, 0.5), "Env_Timber", pitch=s * 15, detail=True)
    # Inscription board between the beam and the top beam, readable from the road (+X): "welcome".
    b.box((x, (LINTEL + 0.24 + POST_H) * 0.5, 0), (0.08, POST_H - LINTEL - 0.24, 2 * OPEN), "Env_Boards")
    b.text("ISTEN HOZOTT", (x + 0.05, (LINTEL + 0.24 + POST_H) * 0.5, 0), 0.16, "Env_PaintWhite", yaw=90, detail=True)
    b.text("ISTEN HOZOTT", (x - 0.05, (LINTEL + 0.24 + POST_H) * 0.5, 0), 0.16, "Env_PaintWhite", yaw=-90, detail=True)
    # Pedestrian door: lintel, board panel above it, a closed board leaf.
    dz = (DOOR_Z[0] + DOOR_Z[1]) * 0.5
    dw = DOOR_Z[1] - DOOR_Z[0]
    b.box((x, 2.3, dz), (0.26, 0.2, dw), "Env_Timber")
    b.box((x, (2.4 + POST_H) * 0.5, dz), (0.08, POST_H - 2.4, dw), "Env_Boards")
    b.box((x, 1.15, dz), (0.06, 2.0, dw - 0.04), "Env_BoardsDark")
    b.collider((x, 1.15, dz), (0.1, 2.3, dw))
    # Top beam over all three posts; on each post a cross beam with two braces down to the post carries the wide top
    # (without them the frieze and roof sat on a 0.4 m post like a hat on a stick: "floating" from the side).
    b.box((x, POST_H + 0.12, zc), (0.5, 0.24, span + 0.3), "Env_Timber")
    fw = 1.3                                  # frieze width (across the gate)
    for z in zs:
        b.box((x, POST_H + 0.34, z), (fw + 0.2, 0.2, POST * 0.7), "Env_Timber")
        for sx in (-1, 1):
            b.box((x + sx * 0.45, POST_H - 0.155, z), (0.93, 0.12, 0.14), "Env_Timber", roll=sx * 57.7)
    # The frieze stands directly on the top beam (the cross beams only on the posts left a slot you could see through
    # from the yard); the cross beam ends stick out of its sides as brackets.
    top = POST_H + 0.24
    fh = 0.72
    b.box((x, top + fh * 0.5, zc), (fw, fh, span + 0.3), "Env_Boards")
    n = 14
    for k in range(n):
        z = z0 + (k + 0.5) * (span + 0.3) / n - 0.15
        for sx in (-1, 1):
            b.box((x + sx * (fw * 0.5 + 0.005), top + 0.46, z), (0.02, 0.2, 0.16), "Env_Glass", detail=True)
    pitch, half = 40.0, fw * 0.5
    rise = half * math.tan(math.radians(pitch))
    ridge = top + fh + rise
    length = math.hypot(half, rise) + 0.55
    c, s = math.cos(math.radians(pitch)), math.sin(math.radians(pitch))
    for side in (-1, 1):
        cx = side * c * length * 0.5 + side * s * 0.06
        cy = ridge - s * length * 0.5 + c * 0.06
        b.box((x + cx, cy, zc), (length, 0.12, span + 1.0), "Env_RoofTiles", roll=-side * pitch)
    b.gable(x - half, x + half, top + fh, rise, zc - span * 0.5 - 0.15, zc + span * 0.5 + 0.15, "Env_Boards")
    b.box((x, ridge + 0.06, zc), (0.2, 0.2, span + 1.05), "Env_RoofTiles", roll=45)
    # The big leaves, open: swung in against the yard side of the opening.
    for sz in (-1, 1):
        lz = sz * (OPEN - 0.06)
        b.box((x - 1.32, 1.3, lz), (2.45, 2.3, 0.07), "Env_BoardsDark")
        for y in (0.45, 2.15):
            b.box((x - 1.32, y, lz - sz * 0.06), (2.4, 0.14, 0.05), "Env_Timber", detail=True)
        b.box((x - 1.32, 1.3, lz - sz * 0.06), (2.6, 0.14, 0.05), "Env_Timber", roll=34, detail=True)
        b.collider((x - 1.32, 1.3, lz), (2.45, 2.3, 0.12))


def picket_run(b, p0, p1, gaps=()):
    """Picket fence along a straight line p0 -> p1 (world XZ), 1.3 m, posts every ~2.5 m. `gaps` = (from, to) distances
    along the run left open."""
    (x0, z0), (x1, z1) = p0, p1
    L = math.hypot(x1 - x0, z1 - z0)
    ux, uz = (x1 - x0) / L, (z1 - z0) / L
    yaw = math.degrees(math.atan2(ux, uz)) - 90.0     # box local X along the run
    pieces, t = [], 0.0
    for g0, g1 in sorted(gaps) + [(L, L)]:
        if g0 > t:
            pieces.append((t, g0))
        t = g1
    for a, e in pieces:
        n = max(1, math.ceil((e - a) / 2.5 - 0.01))
        for k in range(n + 1):
            d = a + (e - a) * k / n
            b.box((x0 + ux * d, 0.65, z0 + uz * d), (0.12, 1.6, 0.12), "Env_Timber", yaw=yaw)
        mid = (a + e) * 0.5
        cx, cz = x0 + ux * mid, z0 + uz * mid
        for y in (0.35, 1.05):
            b.box((cx, y, cz), (e - a, 0.09, 0.04), "Env_Timber", yaw=yaw)
        if b.detail:
            m = int((e - a) / 0.16)
            for k in range(m):
                d = a + (k + 0.5) * (e - a) / m
                b.box((x0 + ux * d, 0.72, z0 + uz * d), (0.09, 1.25, 0.022), "Env_Boards", yaw=yaw, cell=3.0)
        else:
            b.box((cx, 0.72, cz), (e - a, 1.25, 0.03), "Env_Boards", yaw=yaw, cell=3.0)
        b.collider((cx, 0.7, cz), (e - a, 1.4, 0.15), yaw=yaw)


def rail_run(b, p0, p1):
    """Post-and-rail fence (three rails, 1.25 m), posts every ~3 m."""
    (x0, z0), (x1, z1) = p0, p1
    L = math.hypot(x1 - x0, z1 - z0)
    ux, uz = (x1 - x0) / L, (z1 - z0) / L
    yaw = math.degrees(math.atan2(ux, uz)) - 90.0
    n = max(1, math.ceil(L / 3.0 - 0.01))
    for k in range(n + 1):
        d = L * k / n
        b.box((x0 + ux * d, 0.55, z0 + uz * d), (0.14, 1.4, 0.14), "Env_Timber", yaw=yaw)
    cx, cz = (x0 + x1) * 0.5, (z0 + z1) * 0.5
    for y in (0.4, 0.8, 1.15):
        b.box((cx, y, cz), (L, 0.1, 0.05), "Env_Boards", yaw=yaw)
    b.collider((cx, 0.65, cz), (L, 1.3, 0.15), yaw=yaw)


def describe(b):
    gate(b)
    gx0, gz0, gx1, gz1 = GARDEN
    picket_run(b, (gx0, gz0), (gx0, gz1), gaps=[(PORCH_GAP[0] - gz0, PORCH_GAP[1] - gz0)])   # west, yard side
    picket_run(b, (gx0, gz1), (gx1, gz1))       # north
    picket_run(b, (gx1, gz1), (gx1, gz0))       # east
    picket_run(b, (gx1, gz0), (gx0 + 0.2, gz0))  # south, along the road, to the door post
    rail_run(b, (GATE_X, WING[0]), (GATE_X, WING[1]))


NOTES = ("Székely-style yard gate at x %.0f (opening %.1f m, %.1f m clear), garden picket fence %s, rail fence along x %.0f "
         "from z %.1f to %.0f. World coordinates: placed at the origin." % (GATE_X, 2 * OPEN, LINTEL, GARDEN, GATE_X, *WING))
