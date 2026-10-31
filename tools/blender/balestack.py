"""Stack of round hay bales north of the barn (WorldLayout.BaleStack): bales lying on their sides, two rows deep,
three layers (6, 5, 4), older greyer bales at the bottom. Static (the loose bales in the yard are the ones to push).

Unity coordinates, origin = centre of the stack on the ground, rows along X, bale axes along Z.
"""
import math

RB, LB = 0.75, 1.2         # bale radius, length
LAYERS = (6, 5, 4)


def describe(b):
    step = 2 * RB
    rise = step * math.sin(math.radians(60))
    for k, n in enumerate(LAYERS):
        y = RB + k * rise
        for i in range(n):
            x = (i - (n - 1) * 0.5) * step
            for zr in (-0.62, 0.62):
                mat = "Env_HayOld" if k == 0 or (i + k) % 4 == 0 else "Env_Hay"
                b.revolve((x, y, zr), [(0.0, -LB * 0.5), (RB - 0.08, -LB * 0.5), (RB, -LB * 0.5 + 0.06),
                                       (RB, LB * 0.5 - 0.06), (RB - 0.08, LB * 0.5), (0.0, LB * 0.5)],
                          mat, segs=16, pitch=90)
        b.collider((0, y, 0), (n * step - 0.2, rise if k < len(LAYERS) - 1 else step, 2 * LB + 0.04))


NOTES = "Round bale stack, %s bales per row, two rows, Ø %.1f m x %.1f m. Colliders: one box per layer." % (
    "/".join(map(str, LAYERS)), 2 * RB, LB)

NAME = "BaleStack"   # asset / FBX name
