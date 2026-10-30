"""Phase 4 benchmark charts for the README, in a light and a dark variant (GitHub picks one with <picture>).

  docs/images/bench_gpu_{light,dark}.png       GPU time per frame against each version's frame budget
  docs/images/bench_timeline_{light,dark}.png  frame time and GPU time over the scripted drive, one panel per version

Usage: python tools/benchmark_charts.py   (reads the runs listed in RUNS from docs/benchmark/<run>/frames.csv)
Colours: the dataviz reference palette (categorical slots 1-2, chart chrome); system sans.
"""
import csv
import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt

RUNS = [  # (label, run folder, refresh rate Hz)
    ("Quest standalone", "QuestStandalone_2026-10-31_00-43-24", 72),
    ("PC VR (Link)", "PCVR_2026-10-31_00-49-37", 90),
    ("PC desktop", "PCDesktop_2026-10-31_00-34-03", 144),
]
THEMES = {
    "light": dict(surface="#fcfcfb", ink="#0b0b0b", ink2="#52514e", muted="#898781", grid="#e1e0d9", axis="#c3c2b7",
                  s1="#2a78d6", s2="#eb6834"),
    "dark": dict(surface="#1a1a19", ink="#f0efec", ink2="#c3c2b7", muted="#898781", grid="#2c2c2a", axis="#383835",
                 s1="#3987e5", s2="#d95926"),
}
plt.rcParams["font.family"] = ["Segoe UI", "DejaVu Sans"]


def load(run):
    rows = list(csv.DictReader(open(f"docs/benchmark/{run}/frames.csv", encoding="utf-8")))
    rows = [r for r in rows if r["phase"] in ("cab", "third")]   # the drive (runs since phase 5 also stand at the barn)
    t = [float(r["t_s"]) for r in rows]
    frame = [float(r["frame_ms"]) for r in rows]
    gpu = [float(r["gpu_ms"]) for r in rows]
    # The Quest's FrameTimingManager repeats its previous reading (the still rendered just before the timed drive) for the
    # first ~0.5 s: drop a leading run of identical GPU values.
    n = 0
    while n < len(gpu) and gpu[n] == gpu[0]:
        n += 1
    if n > 5:
        gpu[:n] = [-1.0] * n
    return t, frame, gpu


def binned(t, ys, width=0.25):
    """Mean per time bin (the raw series is 4-9k frames)."""
    bins = {}
    for x, y in zip(t, ys):
        if y >= 0:
            bins.setdefault(int(x / width), []).append(y)
    keys = sorted(bins)
    return [(k + 0.5) * width for k in keys], [sum(bins[k]) / len(bins[k]) for k in keys]


def style(ax, th):
    ax.set_facecolor(th["surface"])
    for side in ("top", "right", "left"):
        ax.spines[side].set_visible(False)
    ax.spines["bottom"].set_color(th["axis"])
    ax.tick_params(colors=th["muted"], labelsize=10, length=0)
    ax.grid(axis="y", color=th["grid"], linewidth=0.8)
    ax.set_axisbelow(True)


data = {run: load(run) for _, run, _ in RUNS}

for mode, th in THEMES.items():
    # ---- 1. GPU time against the frame budget (one series: no legend, the title names it)
    fig, ax = plt.subplots(figsize=(9, 3.4), dpi=150)
    fig.patch.set_facecolor(th["surface"])
    style(ax, th)
    ax.grid(axis="y", visible=False)
    ax.grid(axis="x", color=th["grid"], linewidth=0.8)
    for i, (label, run, hz) in enumerate(RUNS):
        gpu = [g for g in data[run][2] if g >= 0]
        avg, budget = sum(gpu) / len(gpu), 1000 / hz
        y = len(RUNS) - 1 - i
        ax.barh(y, budget, height=0.42, color=th["grid"], zorder=1)                 # the budget, as a track
        ax.barh(y, avg, height=0.42, color=th["s1"], zorder=2)
        ax.plot([budget, budget], [y - 0.3, y + 0.3], color=th["ink2"], linewidth=2, zorder=3)
        ax.text(budget + 0.25, y, f"{avg:.1f} ms of {budget:.1f} ms  ({100 * avg / budget:.0f} %)  ·  {hz} Hz",
                va="center", fontsize=10, color=th["ink"])
    ax.set_yticks(range(len(RUNS)), [r[0] for r in reversed(RUNS)], fontsize=11, color=th["ink"])
    ax.set_xlim(0, 22)
    ax.set_xlabel("ms per frame", color=th["muted"], fontsize=10)
    fig.suptitle("GPU time per frame vs. the frame budget (bar end = budget)", x=0.02, ha="left", fontsize=13,
                 color=th["ink"], fontweight="bold")
    fig.tight_layout(rect=(0, 0, 1, 0.95))
    fig.savefig(f"docs/images/bench_gpu_{mode}.png", facecolor=th["surface"])
    plt.close(fig)

    # ---- 2. Frame time + GPU time over the drive, small multiples on one shared axis
    fig, axes = plt.subplots(len(RUNS), 1, figsize=(9, 7.2), dpi=150, sharex=True, sharey=True)
    fig.patch.set_facecolor(th["surface"])
    for ax, (label, run, hz) in zip(axes, RUNS):
        style(ax, th)
        t, frame, gpu = data[run]
        budget = 1000 / hz
        ax.axhline(budget, color=th["ink2"], linewidth=1, linestyle=(0, (4, 3)), zorder=1)
        ax.text(60.6, budget, f"budget {budget:.1f} ms", va="center", fontsize=9, color=th["ink2"])
        ax.axvline(30, color=th["axis"], linewidth=1, zorder=1)
        bt, bf = binned(t, frame)
        gt, gg = binned(t, gpu)
        ax.plot(bt, bf, color=th["s1"], linewidth=2, label="frame time", zorder=3)
        ax.plot(gt, gg, color=th["s2"], linewidth=2, label="GPU time", zorder=3)
        ax.text(0.5, 18.2, f"{label}  ·  {hz} Hz", fontsize=11, color=th["ink"], fontweight="bold")
        ax.set_ylim(0, 20)
        ax.set_yticks([0, 5, 10, 15, 20])
    axes[0].text(30.4, 15.6, "cab view  →  third person", fontsize=9, color=th["muted"])
    axes[-1].set_xlabel("seconds into the drive (0.25 s means)", color=th["muted"], fontsize=10)
    axes[1].set_ylabel("ms", color=th["muted"], fontsize=10)
    axes[-1].set_xlim(0, 60)
    leg = fig.legend(*axes[0].get_legend_handles_labels(), loc="upper right", ncol=2, frameon=False, fontsize=10,
                     bbox_to_anchor=(0.98, 0.985))
    for text in leg.get_texts():
        text.set_color(th["ink"])
    fig.suptitle("Frame time and GPU time over the scripted drive", x=0.02, ha="left", fontsize=13, color=th["ink"],
                 fontweight="bold")
    fig.tight_layout(rect=(0, 0, 0.93, 0.96))
    fig.savefig(f"docs/images/bench_timeline_{mode}.png", facecolor=th["surface"])
    plt.close(fig)
    print("written", mode)
