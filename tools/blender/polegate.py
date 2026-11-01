"""Field gate of the Transylvanian countryside: a forked oak post with a long swing pole balanced on it (a log tied to the short end
as a counterweight), closing the track onto a rest post on the other side. Shown open: the pole swung up at 65°.
WorldLayout.PoleGates, at the field entries where the tracks end.

Unity coordinates, origin = the fork post on the ground; the track is crossed along +X (the rest post stands 5 m away).
"""
import math

SPAN = 5.0
POST_H = 1.55
OPEN = 65.0          # pole angle up from the closed position


def describe(b):
    # Fork post: trunk and two prongs.
    b.tube((0, -0.3, 0), (0, POST_H - 0.15, 0), 0.11, "Env_Timber", segs=8)
    for s in (-1, 1):
        b.tube((0, POST_H - 0.2, 0), (0, POST_H + 0.12, s * 0.09), 0.045, "Env_Timber", segs=6)
    b.tube((0, POST_H, -0.12), (0, POST_H, 0.12), 0.025, "Env_Steel", segs=6)          # pin
    # The pole, swung up: from the counterweight end (1.6 m behind the pivot) out over the track.
    a = math.radians(OPEN)
    d = (math.cos(a), math.sin(a))
    p0 = (-1.6 * d[0], POST_H - 1.6 * d[1], 0.0)
    p1 = ((SPAN + 0.5) * d[0], POST_H + (SPAN + 0.5) * d[1], 0.0)
    b.tube(p0, p1, 0.065, "Env_Timber", segs=7)
    # Counterweight: a log lashed across the short end.
    b.tube((p0[0], p0[1] - 0.05, -0.35), (p0[0], p0[1] - 0.05, 0.35), 0.14, "Env_BoardsDark", segs=8)
    b.tube((p0[0] - 0.05, p0[1] + 0.1, 0), (p0[0] + 0.05, p0[1] - 0.2, 0), 0.02, "Env_Rope", segs=5, detail=True)
    # Rest post on the far side of the track, with a notch block.
    b.tube((SPAN, -0.3, 0), (SPAN, 1.15, 0), 0.08, "Env_Timber", segs=7)
    b.box((SPAN, 1.15, 0), (0.18, 0.08, 0.14), "Env_BoardsDark")
    b.collider((0, 0.8, 0), (0.24, 1.6, 0.24))
    b.collider((SPAN, 0.6, 0), (0.18, 1.2, 0.18))


NAME = "PoleGate"
NOTES = "Pole gate (open, 65°), fork post + swing pole + counterweight log, rest post 5 m across. Colliders: the two posts."
