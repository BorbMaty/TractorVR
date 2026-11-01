"""Roadside details (phase 5 step 4): one asset, one part per kind, each at its own origin (on the ground, facing +Z):

- Pole: precast concrete power pole as on the village lines around Reghin, 9 m, square and tapering, a steel crossarm at 8.3 m with
  two insulators (+-0.95 m) and a third on the pole top (9.15 m). The wires between poles are generated in Unity (ProjectSetup.PowerLine)
  from these insulator positions: keep WIRE_POINTS in sync with ProjectSetup.
- Post: white roadside delineator post, 1.05 m, black band and an amber reflector.
- SignReghin: blue direction sign "REGHIN 6 km" on a steel post (2.2 m to its lower edge).
- Sign50: speed limit, white disc with a red ring.
- SignWarn: warning triangle (red border, white, "!"): agricultural traffic.
Unity coordinates. Placed by ProjectSetup from WorldLayout.PowerPoles / RoadPosts / RoadSigns.
"""
import math

WIRE_POINTS = [(-0.95, 8.47, 0.0), (0.95, 8.47, 0.0), (0.0, 9.27, 0.0)]


def insulator(b, x, y, z):
    b.revolve((x, y, z), [(0.035, 0.0), (0.07, 0.03), (0.07, 0.05), (0.045, 0.08), (0.06, 0.1), (0.04, 0.13), (0.0, 0.14)],
              "Env_PaintWhite", segs=10)


def pole(b):
    # Square tapering shaft: a 4-sided revolve turned 45 degrees (flat faces to the road).
    b.revolve((0, -0.4, 0), [(0.21, 0.0), (0.12, 9.55)], "Env_Concrete", segs=4, yaw=45, cell=3.0)
    b.box((0, 8.3, 0), (2.3, 0.1, 0.1), "Env_Steel")
    for s in (-1, 1):
        b.box((s * 0.45, 7.95, 0), (0.06, 0.75, 0.05), "Env_Steel", roll=s * 50, detail=True)   # braces
        insulator(b, s * 0.95, 8.35, 0)
    insulator(b, 0, 9.15, 0)
    b.collider((0, 2.0, 0), (0.4, 4.0, 0.4))


def post(b):
    b.box((0, 0.45, 0), (0.12, 1.2, 0.12), "Env_PaintWhite")
    b.box((0, 0.88, 0), (0.125, 0.16, 0.125), "Env_RubberBlack")
    b.box((0, 0.88, 0.064), (0.05, 0.1, 0.006), "Impl_Amber", detail=True)
    b.collider((0, 0.5, 0), (0.12, 1.0, 0.12))


def sign_post(b, top):
    b.tube((0, -0.4, 0), (0, top, 0), 0.04, "Env_Steel", segs=8)
    b.collider((0, 1.2, 0), (0.12, 2.4, 0.12))


def sign_reghin(b):
    sign_post(b, 3.0)
    b.box((0, 2.6, 0.06), (1.9, 0.75, 0.04), "Env_PaintWhite")
    b.box((0, 2.6, 0.065), (1.82, 0.67, 0.04), "Env_SignBlue")
    b.text("REGHIN 6 km", (0, 2.6, 0.095), 0.15, "Env_PaintWhite")


def sign_50(b):
    sign_post(b, 2.6)
    b.revolve((0, 2.3, 0.04), [(0.0, 0.0), (0.33, 0.0), (0.33, 0.03), (0.0, 0.03)], "Env_PaintRed", segs=24, pitch=90)
    b.revolve((0, 2.3, 0.045), [(0.0, 0.0), (0.26, 0.0), (0.26, 0.03), (0.0, 0.03)], "Env_PaintWhite", segs=24, pitch=90)
    b.text("50", (0, 2.3, 0.085), 0.17, "Env_RubberBlack")


def sign_warn(b):
    sign_post(b, 2.7)
    tri = [(-0.4, 0.0), (0.4, 0.0), (0.0, 0.69)]
    inner = [(-0.29, 0.06), (0.29, 0.06), (0.0, 0.565)]
    y0 = 2.0
    b.prism([(x, y0 + y) for x, y in tri], 0.03, 0.06, "Env_PaintRed")
    b.prism([(x, y0 + y) for x, y in inner], 0.06, 0.065, "Env_PaintWhite")
    b.text("!", (0, y0 + 0.24, 0.08), 0.2, "Env_RubberBlack")


NAME = "Roadside"
PARTS = {"Pole": pole, "Post": post, "SignReghin": sign_reghin, "Sign50": sign_50, "SignWarn": sign_warn}
NOTES = "Roadside: concrete power pole (wires generated in Unity), delineator post, REGHIN direction sign, 50 limit, warning triangle."
