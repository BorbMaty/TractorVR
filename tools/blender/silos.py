"""Two grain silos in the south-east corner of the yard, east of the machine shed (WorldLayout.Silos): corrugated
galvanised bins (4.5 m across, 7.8 m to the eaves) on concrete footings, cone roofs, a ladder on the yard side
of each and a filling auger rising from a hopper in the yard to a spout head between the roofs.

Unity coordinates, origin = midpoint between the two silos on the ground, silos along Z, the yard to -X.
"""
import math

R = 2.25                  # bin radius
EAVE, APEX = 7.8, 9.65
GAP = 6.5                 # centre to centre
SILOS = (-GAP * 0.5, GAP * 0.5)


def describe(b):
    for z in SILOS:
        b.revolve((0, 0, z), [(R + 0.25, -0.3), (R + 0.25, 0.3)], "Env_Concrete", segs=20)
        b.revolve((0, 0, z), [(R, 0.3), (R, EAVE)], "Env_SiloSteel", segs=24)
        b.revolve((0, 0, z), [(R + 0.15, EAVE - 0.05), (R + 0.15, EAVE + 0.06), (0.4, APEX - 0.25), (0.4, APEX)],
                  "Env_Steel", segs=24)
        for y in (2.3, 4.3, 6.3):   # stiffening rings (detail)
            b.revolve((0, 0, z), [(R + 0.03, y), (R + 0.03, y + 0.08)], "Env_Steel", segs=24, detail=True)
        # Ladder on the yard side: two rails, rungs every 0.3 m (detail), a hoop cage from 2.5 m up (detail).
        lx = -R - 0.32
        for dz in (-0.22, 0.22):
            b.box((lx, (EAVE + 0.6) * 0.5, z + dz), (0.05, EAVE + 0.6, 0.05), "Env_Steel")
            b.box((-R - 0.16, EAVE * 0.5, z + dz), (0.32, 0.04, 0.04), "Env_Steel", detail=True)   # wall bracket
        for k in range(1, int(EAVE / 0.3) + 1):
            b.box((lx, k * 0.3, z), (0.03, 0.03, 0.44), "Env_Steel", detail=True)
        for y in (2.5, 3.5, 4.5, 5.5, 6.5, 7.5):
            b.box((lx - 0.7, y, z), (0.03, 0.04, 0.9), "Env_Steel", detail=True)
            for dz in (-0.45, 0.45):
                b.box((lx - 0.35, y, z + dz), (0.7, 0.04, 0.03), "Env_Steel", detail=True)
        b.collider((0, EAVE * 0.5, z), (R * 1.8, EAVE, R * 1.8))

    # Filling auger: a hopper in the yard, a tube up to a spout head over the gap, spouts down into both roofs.
    hop, head = (-7.2, 0.6, 0.0), (-0.3, APEX + 0.7, 0.0)
    b.box((hop[0] - 0.2, 0.35, 0), (1.0, 0.7, 1.0), "Env_Steel")
    b.tube(hop, head, 0.16, "Env_Steel", segs=12)
    b.box(head, (0.5, 0.5, 0.5), "Env_Steel")
    for z in SILOS:
        b.tube(head, (0.0, APEX - 0.05, z * 0.75), 0.1, "Env_Steel", segs=8)
    for t in (0.35, 0.65):   # A-frame legs under the tube (detail)
        p = [hop[k] + (head[k] - hop[k]) * t for k in range(3)]
        for dz in (-0.6, 0.6):
            b.tube((p[0], 0.0, dz), (p[0], p[1] - 0.15, 0.0), 0.05, "Env_Steel", segs=6, detail=True)
    b.collider((hop[0] - 0.2, 0.35, 0), (1.0, 0.7, 1.0))


NOTES = ("Two grain silos, %.1f m across, eaves %.1f m, apex %.2f m, %.1f m apart (centres), filling auger from the yard side. "
         "Colliders: one box per silo, the hopper (from the JSON)." % (2 * R, EAVE, APEX, GAP))
