"""Stick views of the Lift: front (x-z), side (y-z), top (x-y), C01 arm thick, the rest of the skeleton thin."""
import sys, os, math
sys.argv = ["clips.py", "/tmp/mg01_clips"]
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from mathutils import Vector
import clips as C
import matplotlib; matplotlib.use("Agg")
import matplotlib.pyplot as plt
C.start_reach(); C.fit_dock(); C.start_reach()
C.build_path()
out = os.environ.get("OUT", "stick.png")
clip = os.environ.get("CLIP", "Lift")
fns = {n: (fn, l) for n, l, fn, lp in C.CLIPS}
fn, L = fns[clip]
times = [float(x) for x in os.environ.get("TIMES", "0,3,6,9,12,15,18,21,24,28").split(",")]
ARMB = C.ARM + ["C01_wrist_00", "C01_f0_00", "C01_f0_01", "C01_f0_02"]
others = [pb.name for pb in C.PB if not pb.name.startswith("C01") and not pb.name.startswith("A0") or pb.name.endswith(("shoulder", "elbow", "wrist_00"))]
fig, axs = plt.subplots(3, len(times), figsize=(2.2 * len(times), 7.2))
tipcol = []
for j, t in enumerate(times):
    C.apply(fn(t))
    for i, (a, b, lab) in enumerate(((0, 2, "front x-z"), (1, 2, "side y-z"), (0, 1, "top x-y"))):
        ax = axs[i][j]
        for bn in others:
            h, tl = C.whead(bn), C.wtail(bn)
            ax.plot([h[a], tl[a]], [h[b], tl[b]], color="#aaa", lw=.6)
        for k, bn in enumerate(ARMB):
            h, tl = C.whead(bn), C.wtail(bn)
            ax.plot([h[a], tl[a]], [h[b], tl[b]], color=("#c33" if k < 7 else "#36c"), lw=2.2 if k < 7 else 1.6)
            ax.plot([h[a]], [h[b]], "k.", ms=2)
        hd = C.HEAD
        ax.plot([hd[a]], [hd[b]], "go", ms=4)
        ax.plot([C.FACE_TIP[a]], [C.FACE_TIP[b]], "m+", ms=6)
        ax.set_aspect("equal"); ax.set_xlim(-14, 8) if a == 0 else ax.set_xlim(-18, 8)
        ax.set_ylim(-2, 20) if b == 2 else ax.set_ylim(-18, 8)
        ax.tick_params(labelsize=5)
        if i == 0:
            ax.set_title(f"t={t:.1f} tw={C.FK_LAST.get('tau', 0):.0f} wb={C.FK_LAST.get('wrist', 0):.0f} p={C.FK_LAST.get('pitch', 0):.0f}", fontsize=7)
        if j == 0:
            ax.set_ylabel(lab, fontsize=7)
plt.tight_layout(); plt.savefig(out, dpi=110)
print("saved", out)
