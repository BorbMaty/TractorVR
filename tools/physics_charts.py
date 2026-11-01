"""Charts for docs/PHYSICS.md: the vehicle model's curves (from the same numbers as the code) and measured runs (Logs/PhysicsTest.txt).

    python tools/physics_charts.py        -> docs/images/physics_<name>_light.png / _dark.png

Style: the README benchmark charts' themes (tools/benchmark_charts.py); categorical colours = slots 1-4 of the documented palette
in fixed order, every multi-series chart also labelled directly (colour is never the only cue); one y-axis per panel (two measures
of different scale -> two panels); thin lines, recessive grid.
"""
import math
import os
import re

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402

OUT = "docs/images"
LOG = "Logs/PhysicsTest.txt"

THEMES = {
    "light": dict(surface="#fcfcfb", ink="#0b0b0b", ink2="#52514e", muted="#898781", grid="#e1e0d9", axis="#c3c2b7",
                  s=["#2a78d6", "#eb6834", "#1baf7a", "#eda100"], context="#c3c2b7"),
    "dark": dict(surface="#1a1a19", ink="#f0efec", ink2="#c3c2b7", muted="#898781", grid="#2c2c2a", axis="#383835",
                 s=["#3987e5", "#d95926", "#199e70", "#c98500"], context="#4a4a46"),
}

# ---- numbers from the code (keep in sync) ---------------------------------------------------------------------------
CURVE_RPM = [800, 1000, 1200, 1400, 1500, 1600, 1800, 1900, 2100, 2250]          # Drivetrain.curveRpm
CURVE_NM = [520, 640, 760, 850, 880, 880, 800, 745, 659, 0]                       # Drivetrain.curveNm
FIRST_GEAR, TOP_GEAR, GEARS, RATED = 2.4, 50.0, 20, 2100                         # Drivetrain
UPSHIFT, DOWNSHIFT = 1950, 1300
SURFACES = [("Asphalt / concrete", 35, 0.90), ("Gravel", 22, 0.65), ("Grass", 14, 0.70), ("Mud", 6, 0.35)]   # GroundMap: slipK, grip
MASS, COM_FRONT = 7500.0, 0.40                                                    # TractorController
K_FRONT, K_REAR, ZETA = 300000.0, 500000.0, 0.45
WHEELBASE, MAX_STEER, MAX_YAW, MAX_LAT = 2.8, 40.0, 25.0, 2.5
DRAFT_KN = 30.0                                                                   # plough
SOILS = [("Grass sod", 1.25), ("Soil (stubble)", 1.0), ("Mud", 0.8), ("Ploughed", 0.6)]


def torque(rpm):
    if rpm <= CURVE_RPM[0]:
        return CURVE_NM[0]
    for i in range(1, len(CURVE_RPM)):
        if rpm <= CURVE_RPM[i]:
            t = (rpm - CURVE_RPM[i - 1]) / (CURVE_RPM[i] - CURVE_RPM[i - 1])
            return CURVE_NM[i - 1] + t * (CURVE_NM[i] - CURVE_NM[i - 1])
    return 0.0


def gear_kmh():
    step = (TOP_GEAR / FIRST_GEAR) ** (1 / (GEARS - 1))
    return [FIRST_GEAR * step ** i for i in range(GEARS)]


def gear_name(i):
    return "ABCDE"[i // 4] + str(i % 4 + 1)


# ---- the measured runs --------------------------------------------------------------------------------------------
def section(letter):
    """Rows of a PhysicsTest section: (t, km/h, accel, yaw, lat, pitch, roll, power, height, gear, rpm, avail)."""
    rows, inside = [], False
    for line in open(LOG, encoding="utf-8"):
        if re.match(r"^[A-Z][0-9]?\. ", line):
            inside = line.startswith(letter + ". ")
            continue
        if inside:
            m = re.match(r"^\s+(-?[\d,]+)\s+(-?[\d,]+)\s+(-?[\d,]+)\s+(-?[\d,]+)\s+(-?[\d,]+)\s+(-?[\d,]+)\s+(-?[\d,]+)\s+(-?[\d,]+)"
                         r"\s+(-?[\d,]+)\s+([A-E]\d)\s+(\d+)\s+(\d+)", line)
            if m:
                g = m.groups()
                f = [float(x.replace(",", ".")) for x in g[:9]]
                rows.append(f + [g[9], int(g[10]), int(g[11])])
    return rows


def traction_summary():
    res = {}
    for line in open(LOG, encoding="utf-8"):
        m = re.match(r"^\s+(4WD|2WD|2WD \+ diff lock): slip ([\d,]+)%", line)
        if m:
            res[m.group(1)] = float(m.group(2).replace(",", "."))
    return res


# ---- drawing helpers ----------------------------------------------------------------------------------------------
def style(ax, th, xlabel=None, ylabel=None):
    ax.set_facecolor(th["surface"])
    for side in ("top", "right"):
        ax.spines[side].set_visible(False)
    for side in ("left", "bottom"):
        ax.spines[side].set_color(th["axis"])
    ax.tick_params(colors=th["ink2"], labelsize=9, length=0)
    ax.grid(True, color=th["grid"], linewidth=0.8)
    ax.set_axisbelow(True)
    if xlabel:
        ax.set_xlabel(xlabel, color=th["ink2"], fontsize=10)
    if ylabel:
        ax.set_ylabel(ylabel, color=th["ink2"], fontsize=10)


def figure(th, w=8.0, h=4.2, rows=1, cols=1, **kw):
    fig, axes = plt.subplots(rows, cols, figsize=(w, h), dpi=150, **kw)
    fig.patch.set_facecolor(th["surface"])
    return fig, axes


def title(fig, th, text, sub=None):
    fig.text(0.012, 0.975, text, color=th["ink"], fontsize=12.5, fontweight="bold", va="top")
    if sub:
        fig.text(0.012, 0.915, sub, color=th["ink2"], fontsize=9.5, va="top")


def save(fig, name, mode):
    os.makedirs(OUT, exist_ok=True)
    fig.savefig(f"{OUT}/physics_{name}_{mode}.png", facecolor=fig.get_facecolor())
    plt.close(fig)


def label(ax, th, x, y, text, colour=None, **kw):
    ax.text(x, y, text, color=th["ink"] if colour is None else colour, fontsize=9, **kw)


# ---- the charts ---------------------------------------------------------------------------------------------------
def chart_torque(th, mode):
    rpm = list(range(800, 2251, 10))
    nm = [torque(r) for r in rpm]
    kw = [torque(r) * r * 2 * math.pi / 60 / 1000 for r in rpm]
    fig, (a, b) = figure(th, 9, 4.0, 1, 2)
    style(a, th, "engine speed (rpm)", "torque (Nm)")
    style(b, th, "engine speed (rpm)", "power (kW)")
    a.plot(rpm, nm, color=th["s"][0], linewidth=2)
    b.plot(rpm, kw, color=th["s"][0], linewidth=2)
    a.plot([1550], [880], "o", color=th["s"][0], markersize=6, markeredgecolor=th["surface"], markeredgewidth=2)
    label(a, th, 1580, 893, "880 Nm, 1500-1600 rpm")
    pk = max(range(len(kw)), key=lambda i: kw[i])
    b.plot([rpm[pk]], [kw[pk]], "o", color=th["s"][0], markersize=6, markeredgecolor=th["surface"], markeredgewidth=2)
    label(b, th, rpm[pk] - 330, kw[pk] + 4, f"{kw[pk]:.0f} kW at {rpm[pk]} rpm")
    b.plot([2100], [torque(2100) * 2100 * 2 * math.pi / 60 / 1000], "o", color=th["ink2"], markersize=5)
    label(b, th, 1530, 100, "rated 145 kW\n(195 hp) at 2100", th["ink2"])
    a.set_ylim(0, 960); b.set_ylim(0, 170)
    title(fig, th, "Engine: torque and power", "Drivetrain.TorqueAt: piecewise linear, governor cut to 0 at high idle (2250 rpm); P = T * 2*pi*n / 60")
    fig.tight_layout(rect=(0, 0, 1, 0.86))
    save(fig, "torque", mode)


def chart_gears(th, mode):
    g = gear_kmh()
    run = section("B")
    fig, ax = figure(th, 9, 4.6)
    style(ax, th, "road speed (km/h)", "engine speed (rpm)")
    for i, v in enumerate(g):
        ax.plot([0, v * 2250 / RATED], [0, 2250], color=th["context"], linewidth=0.9)
        if i % 4 == 3 or i == 0:
            ax.text(v * 2250 / RATED, 2265, gear_name(i), color=th["muted"], fontsize=8, ha="center")
    ax.axhspan(DOWNSHIFT, UPSHIFT, color=th["s"][0], alpha=0.07, linewidth=0)
    ax.text(51, UPSHIFT, "upshift 1950", color=th["ink2"], fontsize=8.5, va="center")
    ax.text(51, DOWNSHIFT, "downshift 1300", color=th["ink2"], fontsize=8.5, va="center")
    xs = [r[1] for r in run if r[1] > 0.5]
    ys = [r[10] for r in run if r[1] > 0.5]
    ax.plot(xs, ys, "-o", color=th["s"][0], linewidth=2, markersize=5, markeredgecolor=th["surface"], markeredgewidth=1.5)
    for r in run:
        if r[1] > 0.5 and r[0] in (2.0, 4.0, 7.0, 13.0):
            ax.annotate(r[9], (r[1], r[10]), textcoords="offset points", xytext=(0, -14), ha="center", color=th["ink"], fontsize=8.5)
    ax.set_xlim(0, 58); ax.set_ylim(0, 2400)
    title(fig, th, "Gearbox: 20 gears, and a full-throttle run in AUTO",
          "Grey: engine speed of each gear (A1 2.4 ... E4 50 km/h at 2100 rpm). Blue: PhysicsTest B, one point per second, skip-shifts")
    fig.tight_layout(rect=(0, 0, 0.93, 0.87))
    save(fig, "gears", mode)


def chart_slip(th, mode):
    fig, ax = figure(th, 8, 4.4)
    style(ax, th, "tyre slip (%)", "share of the tyre's grip used")
    s = [i / 1000 for i in range(0, 601)]
    for k, (name, slip_k, grip) in enumerate(SURFACES):
        u = [1 - math.exp(-slip_k * x) for x in s]
        ax.plot([x * 100 for x in s], u, color=th["s"][k], linewidth=2, label=f"{name} (k = {slip_k})")
    leg = ax.legend(loc="lower right", frameon=False, fontsize=9.5)
    for t in leg.get_texts():
        t.set_color(th["ink"])
    ax.axhline(0.97, color=th["ink2"], linewidth=1, linestyle=(0, (4, 3)))
    label(ax, th, 24, 1.03, "97 %: beyond this the wheel spins (or locks when braking)", th["ink2"])
    ax.set_xlim(0, 60); ax.set_ylim(0, 1.12)
    ax.yaxis.set_major_formatter(matplotlib.ticker.PercentFormatter(1.0))
    title(fig, th, "Tyre slip: how much the wheel turns faster than the ground", "u = 1 - exp(-k s)  <=>  s = -ln(1 - u) / k   (WheelSlip; k = GroundMap slipK)")
    fig.tight_layout(rect=(0, 0, 1, 0.87))
    save(fig, "slip", mode)


def chart_friction(th, mode):
    fig, ax = figure(th, 5.6, 5.2)
    style(ax, th, "lateral force / (mu N)", "longitudinal force / (mu N)")
    t = [i * 2 * math.pi / 200 for i in range(201)]
    ax.plot([math.cos(a) for a in t], [math.sin(a) for a in t], color=th["ink2"], linewidth=1.5)
    lat = 0.6
    room = math.sqrt(1 - lat * lat)
    ax.annotate("", xy=(lat, 0), xytext=(0, 0), arrowprops=dict(arrowstyle="-|>", color=th["s"][1], lw=2))
    ax.annotate("", xy=(lat, room), xytext=(lat, 0), arrowprops=dict(arrowstyle="-|>", color=th["s"][0], lw=2))
    ax.plot([0, lat], [0, room], color=th["muted"], linewidth=1, linestyle=(0, (3, 3)))
    label(ax, th, -0.05, -0.32, "1. lateral first:\nF_lat (keeps the tractor on its line)", th["ink"])
    label(ax, th, lat + 0.05, room * 0.45, "2. what is left for\ndrive / brake:\nsqrt((mu N)^2 - F_lat^2)", th["ink"])
    label(ax, th, -0.95, 0.9, "friction circle\n|F| <= mu N", th["ink2"])
    ax.set_xlim(-1.15, 1.15); ax.set_ylim(-1.15, 1.15); ax.set_aspect("equal")
    title(fig, th, "Friction circle of one tyre")
    fig.tight_layout(rect=(0, 0, 1, 0.93))
    save(fig, "friction", mode)


def chart_suspension(th, mode):
    fig, ax = figure(th, 8, 4.2)
    style(ax, th, "time (s)", "body height change (cm)")
    g = 9.81
    for k_i, (name, k, share) in enumerate([("Front wheel (k 300 kN/m)", K_FRONT, COM_FRONT), ("Rear wheel (k 500 kN/m)", K_REAR, 1 - COM_FRONT)]):
        m = MASS * share / 2                           # the body mass this wheel carries
        c = 2 * ZETA * math.sqrt(k * m)
        dt, x, v, road = 1 / 900, 0.0, 0.0, 0.10        # the wheel meets a 10 cm step
        ts, xs = [], []
        for i in range(int(1.2 / dt)):
            f = -k * (x - road) - c * v
            v += f / m * dt
            x += v * dt
            ts.append(i * dt); xs.append(x * 100)
        fn = math.sqrt(k / m) / (2 * math.pi)
        ax.plot(ts, xs, color=th["s"][k_i], linewidth=2, label=f"{name}: natural frequency {fn:.1f} Hz")
    ax.axhline(10, color=th["ink2"], linewidth=1, linestyle=(0, (4, 3)))
    label(ax, th, 0.9, 10.4, "10 cm step", th["ink2"])
    ax.set_xlim(0, 1.2); ax.set_ylim(0, 14)
    leg = ax.legend(loc="lower right", frameon=False, fontsize=9)
    for t in leg.get_texts():
        t.set_color(th["ink"])
    title(fig, th, "Suspension: response of one corner to a 10 cm step",
          "F = F0 + k (x - x0) + c dx/dt,  c = 2 zeta sqrt(k m);  m = the share of the 7.5 t body on that wheel")
    fig.tight_layout(rect=(0, 0, 1, 0.87))
    save(fig, "suspension", mode)


def chart_steering(th, mode):
    fig, ax = figure(th, 8, 4.2)
    style(ax, th, "speed (km/h)", "steering lock (degrees)")
    kmh = [i / 10 for i in range(1, 501)]
    lock, yaw_part, lat_part = [], [], []
    for s in kmh:
        v = s / 3.6
        omega = min(math.radians(MAX_YAW), MAX_LAT / v)
        lock.append(min(MAX_STEER, math.degrees(math.atan(omega * WHEELBASE / v))))
    ax.plot(kmh, lock, color=th["s"][0], linewidth=2)
    v_switch = MAX_LAT / math.radians(MAX_YAW) * 3.6
    ax.axvline(v_switch, color=th["muted"], linewidth=1, linestyle=(0, (3, 3)))
    label(ax, th, 1.5, 41, "full lock 40 deg", th["ink2"])
    label(ax, th, 6, 22, "yaw rate limit 25 deg/s", th["ink2"])
    label(ax, th, v_switch + 1, 8, f"lateral limit 2.5 m/s^2\n(above {v_switch:.0f} km/h)", th["ink2"])
    ax.set_xlim(0, 50); ax.set_ylim(0, 45)
    title(fig, th, "Steering: the comfort lock", "delta_max = atan(omega_max L / v),  omega_max = min(25 deg/s, 2.5 m/s^2 / v),  L = 2.8 m")
    fig.tight_layout(rect=(0, 0, 1, 0.87))
    save(fig, "steering", mode)


def chart_accel(th, mode):
    run = section("B")
    t = [r[0] for r in run]
    fig, (a, b) = figure(th, 9, 4.8, 2, 1, sharex=True)
    style(a, th, None, "speed (km/h)")
    style(b, th, "time (s)", "power used")
    a.plot(t, [r[1] for r in run], color=th["s"][0], linewidth=2)
    b.plot(t, [r[7] for r in run], color=th["s"][0], linewidth=2)
    b.yaxis.set_major_formatter(matplotlib.ticker.PercentFormatter(1.0))
    label(a, th, 1, 38, "1.0 m/s^2: the comfort rate (VR), not the engine,\nlimits the acceleration up to ~45 km/h", th["ink2"])
    label(b, th, 9.3, 0.84, "full power at 46 km/h", th["ink2"])
    a.set_ylim(0, 56); b.set_ylim(0, 1.1)
    title(fig, th, "Measured: full throttle on the flat (PhysicsTest B)", "0 -> 50 km/h in 14 s, AUTO; power = share of the 145 kW the drive force uses")
    fig.tight_layout(rect=(0, 0, 1, 0.88))
    save(fig, "accel", mode)


def chart_climb(th, mode):
    run = section("F")
    t = [r[0] for r in run]
    fig, (a, b) = figure(th, 9, 4.8, 2, 1, sharex=True)
    style(a, th, None, "speed (km/h)")
    style(b, th, "time (s)", "pitch (degrees, nose up)")
    a.plot(t, [r[1] for r in run], color=th["s"][0], linewidth=2)
    b.plot(t, [-r[5] for r in run], color=th["s"][1], linewidth=2)
    label(a, th, 9, 22, "power-limited: F <= P / v slows it to ~14 km/h", th["ink2"])
    a.set_ylim(0, 48); b.set_ylim(-2, 32)
    title(fig, th, "Measured: full throttle up the steepest slope (PhysicsTest F)", "The test finds the steepest point of the terrain itself (22.8 deg) and starts 40 m below it")
    fig.tight_layout(rect=(0, 0, 1, 0.88))
    save(fig, "climb", mode)


def chart_draft(th, mode):
    fig, ax = figure(th, 8, 4.2)
    style(ax, th, "ploughing speed (km/h)", "draft force (kN)")
    kmh = [i / 10 for i in range(0, 151)]
    for k, (name, soil) in enumerate(SOILS):
        f = [DRAFT_KN * soil * (0.8 + 0.03 * s) for s in kmh]
        ax.plot(kmh, f, color=th["s"][k], linewidth=2)
        label(ax, th, 15.2, f[-1] - 0.6, name, th["ink"])
    ax.plot([7.1], [30.4], "o", color=th["ink"], markersize=6, markeredgecolor=th["surface"], markeredgewidth=2)
    ax.annotate("measured: 30.4 kN at 7.1 km/h\n(PhysicsTest J, stubble)", (7.1, 30.4), textcoords="offset points", xytext=(-150, 22),
                color=th["ink"], fontsize=8.5, arrowprops=dict(arrowstyle="-", color=th["muted"], lw=1))
    ax.set_xlim(0, 19.5); ax.set_ylim(0, 52)
    title(fig, th, "Plough draft at full working depth (25 cm)", "F = 30 kN * soil * min(1, depth / 0.25 m) * (0.8 + 0.03 v[km/h]);  soil factor from GroundMap")
    fig.tight_layout(rect=(0, 0, 1, 0.87))
    save(fig, "draft", mode)


def chart_traction(th, mode):
    res = traction_summary()
    names = [n for n in ("4WD", "2WD", "2WD + diff lock") if n in res]
    fig, ax = figure(th, 7, 3.4)
    style(ax, th, "average slip of the driven wheels (%)", None)
    ax.grid(axis="y", visible=False)
    ys = list(range(len(names)))[::-1]
    for y, n in zip(ys, names):
        ax.barh(y, res[n], height=0.55, color=th["s"][0])
        ax.text(res[n] + 0.3, y, f"{res[n]:.1f} %", va="center", color=th["ink"], fontsize=9.5)
    ax.set_yticks(ys)
    ax.set_yticklabels(names, color=th["ink"], fontsize=10)
    ax.set_xlim(0, max(res.values()) * 1.25 if res else 20)
    title(fig, th, "Measured: ploughing at 7 km/h, 30 kN draft (PhysicsTest U)", "4WD spreads the force over all four tyres: each uses less of its grip, so each slips less")
    fig.tight_layout(rect=(0, 0, 1, 0.82))
    save(fig, "traction", mode)


if __name__ == "__main__":
    for mode, th in THEMES.items():
        for fn in (chart_torque, chart_gears, chart_slip, chart_friction, chart_suspension, chart_steering,
                   chart_accel, chart_climb, chart_draft, chart_traction):
            fn(th, mode)
    print("charts written to", OUT)
