"""Village on the horizon (phase 5 step 4): a Mezőség village seen from 600-900 m, so only silhouettes and colours count: ~30
houses with their gable ends to a winding street, white walls, red tile roofs, and the Reformed church on a rise, its tower
with a tall slate spire and the four small corner turrets (fiatornyos) of the Transylvanian churches. Few triangles, no detail.

Unity coordinates, origin = the church; the street runs roughly along X.
"""
import math
import random


def house(b, x, z, yaw, w=6.5, l=11.0, h=3.2):
    """House with its gable to the street: walls and a steep roof turned by yaw (only 0 / 90 used: boxes are axis-aligned)."""
    if yaw:
        w, l = l, w
    b.box((x, h / 2 - 0.3, z), (w, h + 0.6, l), "Env_PlasterVillage", cell=8.0)
    pitch = 45.0
    if yaw:   # ridge along X
        half = l / 2 + 0.4
        rise = half
        for s in (-1, 1):
            b.box((x, h + rise / 2, z + s * half / 2), (w + 0.6, 0.15, half * 1.42), "Env_RoofVillage", pitch=s * pitch, cell=8.0)
    else:     # ridge along Z
        half = w / 2 + 0.4
        rise = half
        for s in (-1, 1):
            b.box((x + s * half / 2, h + rise / 2, z), (half * 1.42, 0.15, l + 0.6), "Env_RoofVillage", roll=-s * pitch, cell=8.0)


def describe(b):
    rnd = random.Random(7)
    # Church: nave 22 x 11 m along X, tower at its west end, on the origin.
    b.box((8, 4, 0), (22, 9, 11), "Env_PlasterVillage", cell=8.0)
    for s in (-1, 1):
        b.box((8, 8.5 + 3.0, s * 3.0), (23, 0.2, 8.4), "Env_RoofVillage", roll=0, pitch=s * 45, cell=8.0)
    b.box((-5, 10, 0), (6, 21, 6), "Env_PlasterVillage", cell=8.0)
    b.revolve((-5, 20.5, 0), [(4.6, 0.0), (0.0, 22.0)], "Env_RoofSlate", segs=8, yaw=22.5)           # spire, 8 sides
    for dx in (-1, 1):
        for dz in (-1, 1):                                                                           # corner turrets
            b.revolve((-5 + dx * 3.0, 20.5, dz * 3.0), [(0.9, 0.0), (0.0, 7.0)], "Env_RoofSlate", segs=6)
    b.box((-5, 43.3, 0), (0.15, 1.6, 0.15), "Env_Steel")
    # Houses along a winding street (z = 30 sin(x / 70)), both sides, gable ends to it.
    for k in range(32):
        side = 1 if k % 2 else -1
        x = -180 + k * 11.5 + rnd.uniform(-2, 2)
        if -20 < x < 30:
            continue
        zs = 30 * math.sin(x / 70.0)
        house(b, x, zs + side * (12 + rnd.uniform(0, 4)), 0, w=rnd.uniform(6, 7.5), l=rnd.uniform(10, 14), h=rnd.uniform(2.9, 3.5))
        if rnd.random() < 0.5:   # barn behind
            house(b, x + rnd.uniform(-2, 2), zs + side * 30, 90, w=7, l=14, h=4.0)


NAME = "Village"
NOTES = "Village silhouette for the horizon (church with a spire and four corner turrets, ~30 houses, barns). No colliders."
