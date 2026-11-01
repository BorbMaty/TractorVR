"""Practice area (phase 5 step 5, the tutorial's driving school): markings and fittings on the concrete pad east of the yard
(WorldLayout.PracticeArea, x 34..76, z -78..-12). Modelled in **world coordinates** (placed at the origin, like fences.py);
the numbers must match WorldLayout (SlalomCones, ReversingGoal, CouplingSpot). The cones themselves are separate, pushable
objects (cone.py). The pad's surface is at y = 0.03.

- slalom: start and finish lines across x 40..52 at z -14 and -66 (the cones stand at x 46 every 8 m in between);
- reversing bay: yellow lines x 63.75 / 68.25 from z -76 to -64, a concrete kerb across its end (z -77, collider);
- coupling spot: a yellow 3 x 3 m box around (66, -26), the word COUPLING;
- a sign board at the pad's north-west corner: PRACTICE AREA.
"""
Y = 0.03 + 0.006   # paint just above the pad


def line(b, x0, z0, x1, z1, mat="Env_PaintYellow", w=0.15):
    if abs(x1 - x0) > abs(z1 - z0):
        b.box(((x0 + x1) / 2, Y, z0), (abs(x1 - x0) + w, 0.012, w), mat, cell=6.0)
    else:
        b.box((x0, Y, (z0 + z1) / 2), (w, 0.012, abs(z1 - z0) + w), mat, cell=6.0)


def describe(b):
    for z in (-14.0, -66.0):                         # slalom start / finish
        line(b, 40.0, z, 52.0, z, "Env_PaintWhite", w=0.3)
    line(b, 63.75, -76.0, 63.75, -64.0)              # reversing bay
    line(b, 68.25, -76.0, 68.25, -64.0)
    line(b, 63.75, -64.0, 64.75, -64.0)
    line(b, 67.25, -64.0, 68.25, -64.0)
    b.box((66.0, 0.18, -77.0), (5.5, 0.36, 0.5), "Env_Concrete")          # kerb at the bay's end
    b.collider((66.0, 0.18, -77.0), (5.5, 0.36, 0.5))
    cx, cz = 66.0, -26.0                              # coupling spot
    line(b, cx - 1.5, cz - 1.5, cx + 1.5, cz - 1.5)
    line(b, cx - 1.5, cz + 1.5, cx + 1.5, cz + 1.5)
    line(b, cx - 1.5, cz - 1.5, cx - 1.5, cz + 1.5)
    line(b, cx + 1.5, cz - 1.5, cx + 1.5, cz + 1.5)
    # Sign board on two posts, facing the yard (north-west): readable from +Z turned by yaw.
    # 4.6 x 1.3 m board on 3 m posts (the first one, 2.6 m wide, was narrower than its lettering: the letters stuck out into the air);
    # the letters sit on the board's faces (board 6 cm thick, letters 2 cm deep, centred 1 cm proud of each face).
    sx, sz = 36.0, -10.5
    for dx in (-2.0, 2.0):
        b.box((sx + dx, 1.35, sz), (0.14, 3.3, 0.14), "Env_Timber")
    b.box((sx, 2.3, sz), (4.6, 1.3, 0.06), "Env_PaintGreen")
    b.box((sx, 2.3, sz), (4.4, 1.1, 0.064), "Env_PaintWhite", detail=True)    # white border (the green panel inside it is drawn next)
    b.box((sx, 2.3, sz), (4.3, 1.0, 0.068), "Env_PaintGreen", detail=True)
    b.text("PRACTICE AREA", (sx, 2.3, sz + 0.044), 0.22, "Env_PaintWhite")
    b.text("PRACTICE AREA", (sx, 2.3, sz - 0.044), 0.22, "Env_PaintWhite", yaw=180)
    b.collider((sx, 1.5, sz), (4.4, 3.0, 0.2))
    b.text("COUPLING", (cx, Y + 0.01, cz + 2.4), 0.5, "Env_PaintYellow", depth=0.008)
