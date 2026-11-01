"""Haystack (boglya / claie) as made in the Mezőség: hay stacked around a tall pole into a rounded cone ~3 m high, the top
weathered grey, the pole sticking out above it; a few forked branches propping the base (detail). WorldLayout.Haystacks.

Unity coordinates, origin = centre on the ground.
"""
import math


def describe(b):
    # Pointed like the Mezőség stacks (wide belly, long cone to the pole), olive-brown hay, the top weathered grey.
    prof = [(0.0, -0.15), (1.15, -0.15), (1.3, 0.4), (1.28, 0.95), (1.05, 1.55), (0.72, 2.1), (0.38, 2.6), (0.1, 3.05), (0.0, 3.15)]
    b.revolve((0, 0, 0), prof, "Env_HayStack", segs=14)
    b.revolve((0, 0, 0), [(0.0, 1.95), (0.83, 1.95), (0.65, 2.2), (0.36, 2.66), (0.1, 3.08), (0.0, 3.18)], "Env_HayGrey", segs=14)
    b.tube((0, -0.2, 0), (0.04, 3.75, 0.03), 0.05, "Env_Timber", segs=6)
    for k in range(3):   # props
        a = 2 * math.pi * (k + 0.2) / 3
        b.tube((1.8 * math.cos(a), -0.05, 1.8 * math.sin(a)), (1.05 * math.cos(a), 1.3, 1.05 * math.sin(a)), 0.035, "Env_Timber",
               segs=5, detail=True)
    b.collider((0, 1.2, 0), (2.0, 2.4, 2.0))


NAME = "Haystack"
NOTES = "Haystack (boglya), 2.6 m across, 3 m high, pole to 3.75 m. Collider: one box."
