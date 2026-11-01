"""Traffic cone for the practice area: 75 cm, square rubber foot, orange with two white reflective bands. Pushable (Rigidbody, made in
ProjectSetup). Unity coordinates, origin = centre of the foot on the ground."""


def describe(b):
    b.box((0, 0.015, 0), (0.42, 0.03, 0.42), "Env_RubberBlack")
    b.revolve((0, 0.03, 0), [(0.17, 0.0), (0.15, 0.12)], "Env_ConeOrange", segs=16)
    b.revolve((0, 0.15, 0), [(0.15, 0.0), (0.12, 0.17)], "Env_PaintWhite", segs=16)
    b.revolve((0, 0.32, 0), [(0.12, 0.0), (0.10, 0.12)], "Env_ConeOrange", segs=16)
    b.revolve((0, 0.44, 0), [(0.10, 0.0), (0.08, 0.10)], "Env_PaintWhite", segs=16)
    b.revolve((0, 0.54, 0), [(0.08, 0.0), (0.035, 0.21), (0.0, 0.21)], "Env_ConeOrange", segs=16)


NAME = "Cone"
NOTES = "Traffic cone 0.75 m (practice area). Collider and Rigidbody added in ProjectSetup."
