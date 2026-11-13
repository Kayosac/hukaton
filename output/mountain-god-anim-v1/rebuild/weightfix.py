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
WR_in = WR.copy()
V0, W0 = d["V"], d["W"]
Xs = np.load(rel)["X"]
_, ov = cKDTree(Xs[:len(V0)]).query(RP)
n = 0
for b in SPRINGS:
    j = bones.index(b)
    bad = (WR[:, j] > 0) & (W0[ov, j] < 1e-3)
    WR[bad, j] = 0
    n += int(bad.sum())
# and no vertex follows a bone it lies far from: each bone reaches only as far from itself (in rest) as the old skin
# it carried (+0.3); a weight beyond that is a stray from the rebuild (a beard vertex following a finger)
H = d["BH"]; Tl = d["BT"]
RVo = z["RV"]
def seg_dist(X, a, t):
    ab = t - a
    s_ = np.clip(((X - a) @ ab) / max(ab @ ab, 1e-9), 0, 1)
    return np.linalg.norm(X - (a + s_[:, None] * ab), axis=1)
stray = 0
for j in range(len(bones)):
    old = W0[:, j] > .05
    if not old.any():
        continue
    reach = seg_dist(V0[old], H[j], Tl[j]).max() + .3
    cand = WR[:, j] > 0
    if not cand.any():
        continue
    far = cand.copy(); far[cand] = seg_dist(RVo[cand], H[j], Tl[j]) > reach
    stray += int(far.sum())
    WR[far, j] = 0
print("stray weights removed", stray)
s = WR.sum(1)
empty = s < 1e-6
WR[empty] = WR_in[empty]  # nothing left: the vertex keeps what it had (it is the spring's own skin, built anew)
WR /= WR.sum(1, keepdims=True)
print("spring weights removed on", n, "vertices; kept as they were (nothing else to follow):", int(empty.sum()))
z["WR"] = WR.astype(np.float32)
np.savez(out, **z)
