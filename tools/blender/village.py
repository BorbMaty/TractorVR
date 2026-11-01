"""Village on the horizon (phase 5 step 4): a Mezőség village seen from 600-900 m, so only silhouettes and colours count: ~30
houses with their gable ends to a winding street, white walls, red tile roofs, and the Reformed church on a rise, its tower
with a tall slate spire and the four small corner turrets (fiatornyos) of the Transylvanian churches. Few triangles, no detail.

Unity coordinates, origin = the church; the street runs roughly along X.
"""
import math
import random


def house(b, x, z, w=6.5, l=11.0, h=3.2):
    """House (or barn) with its ridge along Z: walls, a solid gable / attic under the roof (the first version had only the two roof
    slabs, so the gable ends were open and the roofs looked see-through), two 45 degree roof slabs with eaves."""
    b.box((x, h / 2 - 0.3, z), (w, h + 0.6, l), "Env_PlasterVillage", cell=8.0)
    b.gable(x - w / 2, x + w / 2, h, w / 2, z - l / 2, z + l / 2, "Env_PlasterVillage", cell=8.0)
    half = w / 2 + 0.4
    for s in (-1, 1):
        b.box((x + s * half / 2, h + half / 2 + 0.05, z), (half * 1.42, 0.15, l + 0.6), "Env_RoofVillage", roll=-s * 45.0, cell=8.0)


def describe(b):
    rnd = random.Random(7)
    # Church: nave 11 x 22 m along Z with a solid gable roof, the tower at its south end, on the origin.
    house(b, 0, 9, w=11, l=22, h=8.5)
    b.box((0, 10, -5), (6, 21, 6), "Env_PlasterVillage", cell=8.0)
    b.revolve((0, 20.5, -5), [(4.6, 0.0), (0.0, 22.0)], "Env_RoofSlate", segs=8, yaw=22.5)           # spire, 8 sides
    for dx in (-1, 1):
        for dz in (-1, 1):                                                                           # corner turrets
            b.revolve((dx * 3.0, 20.5, -5 + dz * 3.0), [(0.9, 0.0), (0.0, 7.0)], "Env_RoofSlate", segs=6)
    b.box((0, 43.3, -5), (0.15, 1.6, 0.15), "Env_Steel")
    # Houses along a winding street (z = 30 sin(x / 70)), both sides, gable ends to it; half of them with a barn behind.
    for k in range(32):
        side = 1 if k % 2 else -1
        x = -180 + k * 11.5 + rnd.uniform(-2, 2)
        if -20 < x < 30:
            continue
        zs = 30 * math.sin(x / 70.0)
        house(b, x, zs + side * (12 + rnd.uniform(0, 4)), w=rnd.uniform(6, 7.5), l=rnd.uniform(10, 14), h=rnd.uniform(2.9, 3.5))
        if rnd.random() < 0.5:
            house(b, x + rnd.uniform(-2, 2), zs + side * 32, w=9, l=8, h=4.0)


NAME = "Village"
NOTES = "Village silhouette for the horizon (church with a spire and four corner turrets, ~30 houses, barns). No colliders."
