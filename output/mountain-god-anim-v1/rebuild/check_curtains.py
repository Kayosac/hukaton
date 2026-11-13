import sys, os, numpy as np
from collections import Counter
WORK = os.environ.get("REBUILD_WORK", "/tmp/god/rb")
z = np.load(sys.argv[1]); RP, RV, RF, WR = z["RP"], z["RV"], z["RF"], z["WR"].astype(float)
bones = [str(b) for b in np.load(WORK + "/split.npz")["bones"]]
FR = np.load(WORK + "/frames_S.npz")
top = np.argsort(-WR, axis=1)[:, :4]; w4 = np.take_along_axis(WR, top, 1); w4[w4 < .01] = 0; w4 /= w4.sum(1, keepdims=True)
W4 = np.zeros_like(WR); np.put_along_axis(W4, top, w4, 1)
Vh = np.c_[RV, np.ones(len(RV))]
E = np.unique(np.sort(np.concatenate([RF[:, [0, 1]], RF[:, [1, 2]], RF[:, [2, 0]]]), 1), axis=0)
lr = np.linalg.norm(RV[E[:, 0]] - RV[E[:, 1]], axis=1)
dom = np.argmax(W4, 1)
print("rest: edges > .35 ", int((lr > .35).sum()), "max", lr.max().round(2))
for name in ("Idle_0", "Notice_3", "Reach_6", "ReachHold_0", "Lift_4", "Lift_8", "Lift_12", "Talk_0"):
    Px = np.einsum("nb,bij,nj->ni", W4, FR[name][:, :3, :], Vh)
    lx = np.linalg.norm(Px[E[:, 0]] - Px[E[:, 1]], axis=1)
    bad = (lx > .35) & (lx > 6 * np.maximum(lr, .005))
    c = Counter(tuple(sorted((bones[dom[a]][:3], bones[dom[b]][:3]))) for a, b in E[bad])
    print(f"{name:12s} curtain edges {int(bad.sum()):5d} longest {lx[bad].max() if bad.any() else 0:6.2f}", c.most_common(4))
