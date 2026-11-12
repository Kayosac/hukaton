"""The spring bones (beard, hat) carry only skin that was theirs: a rebuilt vertex keeps a spring bone's weight only if
the old vertex nearest to it (in the opened pose) had it. Otherwise a healed place on the chest next to the beard
swings with the beard's spring and tears open."""
import sys, os, numpy as np
from scipy.spatial import cKDTree
WORK = os.environ.get("REBUILD_WORK", "/tmp/god/rb")
SPRINGS = ("beard_00", "hat_00", "hat_01")
src, rel, out = sys.argv[1], sys.argv[2], sys.argv[3]
z = dict(np.load(src)); d = np.load(WORK + "/split.npz")
bones = [str(b) for b in d["bones"]]
WR = z["WR"].astype(float); RP = z["RP"]
V0, W0 = d["V"], d["W"]
Xs = np.load(rel)["X"]
_, ov = cKDTree(Xs[:len(V0)]).query(RP)
n = 0
for b in SPRINGS:
    j = bones.index(b)
    bad = (WR[:, j] > 0) & (W0[ov, j] < 1e-3)
    WR[bad, j] = 0
    n += int(bad.sum())
s = WR.sum(1)
empty = s < 1e-6
WR[empty] = W0[ov[empty]]
WR /= WR.sum(1, keepdims=True)
print("spring weights removed on", n, "vertices; took the old vertex's weights:", int(empty.sum()))
z["WR"] = WR.astype(np.float32)
np.savez(out, **z)
