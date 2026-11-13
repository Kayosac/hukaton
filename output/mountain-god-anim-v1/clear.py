"""Closest approach of C01 (arm, hand, finger) to the rest of the body through Reach / Lift / Talk, on the split mesh."""
import sys, os, math
sys.argv = ["clips.py", "/tmp/mg01_clips"]
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
from scipy.spatial import cKDTree
import bpy
import clips as C
C.start_reach(); C.fit_dock(); C.start_reach()
d = np.load(os.environ.get("SPLIT", "/tmp/god/rb/split.npz"))  # rebuild/dump_split.py
V, W, bones = d["V"], d["W"], list(d["bones"])
ML = d["ML"]
side, dom, joint = d["vside"], d["vsdom"], d["joint"]
c01 = (side == "C01") & ~np.isin(dom, ["C01_seg00", "C01_shoulder"])
other = (side != "C01")
print("c01 verts", c01.sum(), "other", other.sum())
Vh = np.c_[V, np.ones(len(V))]
MLi = np.linalg.inv(ML)
step = float(os.environ.get("STEP", ".5"))
for name in os.environ.get("CLIPS", "Reach,Lift,Talk").split(","):
    fn, L = {n: (f, l) for n, l, f, lp in C.CLIPS}[name]
    if name == "Reach": C.start_reach()
    rows = []
    t = 0.0
    while t <= L + 1e-6:
        C.apply(fn(t))
        S = np.array([np.array(C.PB[b].matrix) for b in bones]) @ MLi
        P = np.einsum("nb,bij,nj->ni", W, S[:, :3, :], Vh)
        kd = cKDTree(P[other])
        dist, idx = kd.query(P[c01])
        i = int(np.argmin(dist))
        oi = np.nonzero(other)[0][idx[i]]
        rows.append((t, float(dist[i]), str(dom[np.nonzero(c01)[0][i]]), str(dom[oi]), tuple(np.round(P[np.nonzero(c01)[0][i]], 1))))
        t += step
    rows.sort(key=lambda r: r[1])
    print(name, "closest:", [(round(r[0], 1), round(r[1], 3), r[2], r[3], r[4]) for r in rows[:8]])
