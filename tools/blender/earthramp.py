"""Earth practice ramp on the pasture north-west of the yard (WorldLayout.EarthRamp): a 3 m high mound of dry, rutted soil
with a 6 m wide driving track over it, a 15° side and a steep 22° side (the hills are at most ~11°), banks falling away at
35° on both sides of the track, and a sign at the foot of each side. For practising climbing and descending: low gear,
4WD, the hand throttle, the brakes, and the view down the bonnet from the cab.

Unity coordinates, origin = middle of the mound on the ground, the track over it along local X: 15° side at -X, 22° side at +X.
"""
import math

H, W = 3.0, 6.0
GENTLE, STEEP, BANK = 15.0, 22.0, 35.0
TOP = 4.0
RUN_G = H / math.tan(math.radians(GENTLE))
RUN_S = H / math.tan(math.radians(STEEP))
X0 = -(RUN_G + TOP + RUN_S) * 0.5
XA, XB, X1 = X0 + RUN_G, X0 + RUN_G + TOP, X0 + RUN_G + TOP + RUN_S
SINK = 0.08                    # edges this far below the ground


def ramp(x):
    """Height of the driving track along X."""
    if x <= X0 or x >= X1:
        return 0.0
    if x < XA:
        return (x - X0) / RUN_G * H
    if x <= XB:
        return H
    return (X1 - x) / RUN_S * H


def describe(b):
    hz = W * 0.5
    bank_run = H / math.tan(math.radians(BANK))
    zo = hz + bank_run

    def y(x, z):
        across = max(0.0, abs(z) - hz) * math.tan(math.radians(BANK))   # the bank falls away beside the track
        # No clamp at the ground: the crease where a bank meets the ground runs diagonally across the grid cells, and a clamp
        # there stair-steps it. The surface runs on below the ground instead (the terrain hides it), clamped only deep down.
        # The track itself matches its colliders exactly; beyond its ends the surface lies SINK under the ground (no z-fighting).
        r = ramp(x) if X0 < x < X1 else -SINK
        return max(-1.5, r - across)

    cell = b.cell
    xs = b.lines([X0 - 0.5, X0, XA, XB, X1, X1 + 0.5], cell)
    zs = b.lines([-zo - 0.3, -zo, -hz, hz, zo, zo + 0.3], cell)
    b.heightfield(xs, zs, y, "Env_Earth")

    # Signs at the foot of each side, beside the track, facing the approaching driver.
    for x, deg, yaw in ((X0 - 2.0, GENTLE, -90), (X1 + 2.0, STEEP, 90)):
        z = hz + 1.0
        b.box((x, 0.75, z), (0.1, 1.5, 0.1), "Env_Timber")
        b.box((x, 1.55, z), (0.06, 0.5, 0.9), "Env_PaintYellow")
        b.text("%d°" % deg, (x + (0.04 if yaw == 90 else -0.04), 1.55, z), 0.3, "Env_RubberBlack", yaw=yaw)
        b.collider((x, 0.75, z), (0.1, 1.5, 0.1))
    # Wheel-ray colliders on the track: the two slopes and the top (the banks are left to the eye: steer straight).
    b.slope_collider((X0, 0.0), (XA, H), -hz, hz)
    b.slope_collider((XA, H), (XB, H), -hz, hz)
    b.slope_collider((XB, H), (X1, 0.0), -hz, hz)


NOTES = ("Earth practice ramp, %.0f m high, %.0f m wide track, %d° side (%.1f m), top %.0f m, %d° side (%.1f m), %d° banks. "
         "Colliders: the two slopes and the top, the sign posts." % (H, W, GENTLE, RUN_G, TOP, STEEP, RUN_S, BANK))
NAME = "EarthRamp"
