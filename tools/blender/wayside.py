"""Wayside cross at a crossroads (as on the roads around Reghin): an oak cross 4 m high on a stone base, a small shingled gable
roof over the crossbeam, carved boards under the arms, a little ledge for flowers. WorldLayout.WaysideCross.

Unity coordinates, origin = centre of the base on the ground; it faces +Z.
"""
import math


def describe(b):
    b.box((0, 0.15, 0), (0.7, 0.4, 0.7), "Env_Concrete")
    b.box((0, 0.38, 0), (0.55, 0.08, 0.55), "Env_Concrete", detail=True)
    b.box((0, 2.2, 0), (0.18, 3.7, 0.16), "Env_Timber")                 # upright
    b.box((0, 3.05, 0.0), (1.6, 0.16, 0.14), "Env_Timber")              # arms
    for s in (-1, 1):                                                    # carved boards under the arms (detail)
        b.box((s * 0.45, 2.88, 0.07), (0.55, 0.18, 0.03), "Env_BoardsDark", detail=True)
        b.box((s * 0.82, 3.05, 0.0), (0.06, 0.24, 0.18), "Env_BoardsDark", detail=True)
    # Roof: two shingled slabs over the arms, ridge along X.
    pitch, half, length = 35.0, 0.42, 1.9
    rise = half * math.tan(math.radians(pitch))
    for s in (-1, 1):
        b.box((0, 3.32 + rise * 0.5, s * half * 0.5), (length, 0.04, half / math.cos(math.radians(pitch)) + 0.06),
              "Env_BoardsDark", pitch=s * pitch)
    b.box((0, 3.33 + rise, 0), (length + 0.04, 0.06, 0.06), "Env_Timber")
    b.box((0, 3.95, 0), (0.1, 0.4, 0.1), "Env_Timber")                   # top of the upright, above the roof
    b.box((0, 1.1, 0.14), (0.4, 0.04, 0.16), "Env_Timber", detail=True)  # flower ledge
    b.collider((0, 0.9, 0), (0.7, 1.8, 0.7))


NAME = "WaysideCross"
NOTES = "Wayside cross, 4 m, small shingled roof, stone base. Collider: the base and upright."
