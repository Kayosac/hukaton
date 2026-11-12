"""Step 3: open the skin so the rebuild cannot fuse what moves apart. The split skin (subdivided twice) is posed as in the
clips (FRAME, default TalkL01: hands off the thighs and the chest, legs down), the beard and arm A03 (which hardly move
there) slid off the chest and the pelvis; then every two skins facing each other closer than G, which are not one skin
along the surface (geodesic test) — or which cross, from parts that move apart — are pushed off each other by a smooth
field. Saves the subdivided rest skin X0, the opened skin X, triangles, weights, the push D and the pose S."""
import os
WORK = os.environ.get("REBUILD_WORK", "/tmp/god/rb")

import json, time, numpy as np
from scipy.spatial import cKDTree
import scipy.sparse as sp
t0 = time.time()
G = .10
d = np.load(WORK + "/split.npz")
V, T, W = d["V"], d["T"], d["W"]; bones = [str(b) for b in d["bones"]]
vside, vsdom, joint = d["vside"], d["vsdom"], d["joint"]
par = json.load(open(WORK + "/parents.json"))
FREE = ("C01", "C02", "A01", "A02", "A03", "A04", "L01", "L02")
# group of each vertex: free limbs and the beard on their own (except at a limb's root joint), the rest is one body
grp = np.array(["beard" if vd == "beard_00" else (vs if vs in FREE and not jt else "body") for vs, vd, jt in zip(vside, vsdom, joint)])
HEADISH = {"head_00", "neck_00", "Neck", "hat_00", "hat_01"}
def chain(b):
    out = []
    while b: out.append(b); b = par[b]
    return out
def hops(a, b):
    if not a or not b: return 0
    ca, cb = chain(a), chain(b); ib = {n: i for i, n in enumerate(cb)}
    return next((i + ib[n] for i, n in enumerate(ca) if n in ib), 99)

# two levels of midpoint subdivision (flat): contacts are finer than the low-poly faces
def subdivide(X, T, W, lab):  # lab: per-vertex arrays carried along (first endpoint's)
    E = np.sort(np.concatenate([T[:, [0, 1]], T[:, [1, 2]], T[:, [2, 0]]]), 1)
    Eu, inv = np.unique(E, axis=0, return_inverse=True)
    n = len(X); m = n + np.arange(len(Eu))
    X2 = np.vstack([X, (X[Eu[:, 0]] + X[Eu[:, 1]]) / 2])
    W2 = np.vstack([W, (W[Eu[:, 0]] + W[Eu[:, 1]]) / 2])
    lab2 = [np.concatenate([l, l[Eu[:, 0]]]) for l in lab]
    nt = len(T); a, b, c = m[inv[:nt]], m[inv[nt:2*nt]], m[inv[2*nt:]]
    T2 = np.concatenate([np.c_[T[:, 0], a, c], np.c_[a, T[:, 1], b], np.c_[c, b, T[:, 2]], np.c_[a, b, c]])
    return X2, T2, W2, lab2
X, TT, WW, (G_, DOM, ORIG) = V, T, W, (grp, vsdom, np.arange(len(V)))
for _ in range(2):
    X, TT, WW, (G_, DOM, ORIG) = subdivide(X, TT, WW, (G_, DOM, ORIG))
X0 = X.copy(); n = len(X)
print("subdivided", n, len(TT), time.time() - t0)
gnames = sorted(set(G_)); gi = np.array([gnames.index(g) for g in G_])
DOMn = sorted(set(DOM)); di = np.array([DOMn.index(x) for x in DOM])
# compatibility of (group, bone) pairs
HOP = np.array([[hops(a, b) for b in DOMn] for a in DOMn])
HEADV = np.array([x in HEADISH for x in DOMn])
def compat_pairs(i, j):
    gi_, gj_ = gi[i], gi[j]
    ok = gi_ == gj_
    # beard sits on the head and neck: allowed there
    bi = gi_ == gnames.index("beard") if "beard" in gnames else np.zeros_like(ok)
    bj = gj_ == gnames.index("beard") if "beard" in gnames else np.zeros_like(ok)
    headish = HEADV
    ok |= (bi & headish[di[j]]) | (bj & headish[di[i]])
    # inside one free limb, bones far apart in the chain must not fuse (a thumb on its own palm)
    same = ok & (gi_ == gj_) & (gi_ != gnames.index("body"))
    if same.any():
        far = HOP[di[i][same], di[j][same]] > 5
        idx = np.where(same)[0]; ok[idx[far]] = False
    return ok
E = np.concatenate([TT[:, [0, 1]], TT[:, [1, 2]], TT[:, [2, 0]]])
A = sp.coo_matrix((np.ones(len(E)), (E[:, 0], E[:, 1])), shape=(n, n)).tocsr(); A = ((A + A.T) > 0).astype(float)
deg = np.asarray(A.sum(1)).ravel(); Avg = sp.diags(1 / np.maximum(deg, 1)) @ A
def normals(X):
    fn = np.cross(X[TT[:, 1]] - X[TT[:, 0]], X[TT[:, 2]] - X[TT[:, 0]]); N = np.zeros_like(X)
    for k in range(3): np.add.at(N, TT[:, k], fn)
    return N / np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-12)
# skin that runs on without a tear from one group into another (a limb's root joint, the beard's top) is one surface:
# points near such a frontier never push each other
from scipy.sparse.csgraph import dijkstra
fr = np.unique(E[gi[E[:, 0]] != gi[E[:, 1]]].ravel())
L = np.linalg.norm(X[E[:, 0]] - X[E[:, 1]], axis=1)
GL = sp.coo_matrix((L, (E[:, 0], E[:, 1])), shape=(n, n)).tocsr()
band = np.isfinite(dijkstra(GL, directed=False, indices=fr, limit=3 * G, min_only=True)) if len(fr) else np.zeros(n, bool)
print("frontier", len(fr), "band", int(band.sum()), time.time() - t0)
# geodesic distances on the low-poly skin (up to R): two close points far apart along the skin are separate surfaces
from scipy.sparse.csgraph import dijkstra as _dj
R = .9
E0 = np.concatenate([T[:, [0, 1]], T[:, [1, 2]], T[:, [2, 0]]])
L0 = np.linalg.norm(V[E0[:, 0]] - V[E0[:, 1]], axis=1)
G0 = sp.coo_matrix((L0, (E0[:, 0], E0[:, 1])), shape=(len(V), len(V))).tocsr()
rows, cols, vals = [], [], []
for s0 in range(0, len(V), 2500):
    idx = np.arange(s0, min(len(V), s0 + 2500))
    dd = _dj(G0, directed=False, indices=idx, limit=R).astype(np.float32)
    r_, c_ = np.where(np.isfinite(dd))
    rows.append(idx[r_]); cols.append(c_); vals.append(dd[r_, c_] + 1e-4)
GEO = sp.coo_matrix((np.concatenate(vals), (np.concatenate(rows), np.concatenate(cols))), shape=(len(V), len(V))).tocsr()
print("geodesics", GEO.nnz, round(time.time() - t0, 1))
def separate(i, j):
    oi, oj = ORIG[i], ORIG[j]
    g = np.asarray(GEO[oi, oj]).ravel()
    g[g == 0] = 9.0  # not within R
    g[oi == oj] = 0.0
    return g > 3 * np.linalg.norm(X0[i] - X0[j], axis=1) + .3
# first the fused places are opened by posing the god as in his clips (FRAME: the hands off the thighs and the chest),
# with the beard and arm A03 (which hardly move there) slid off the chest and the pelvis
import os
FRAME = os.environ.get("FRAME", "TalkL01")
S = np.load(WORK + "/frames_S.npz")[FRAME].copy()
OFFS = {"beard_00": [0.0, -0.9, 0.2], "A03_shoulder": [-0.6, -0.35, 0.3]}
def cum(b):
    o = np.zeros(3)
    while b:
        if b in OFFS: o += OFFS[b]
        b = par[b]
    return o
for k, b in enumerate(bones):
    S[k, :3, 3] += cum(b)
def lbs(Xr, Wv):
    return np.einsum("nb,bij,nj->ni", Wv, S[:, :3, :], np.c_[Xr, np.ones(len(Xr))])
X = lbs(X0, WW)
D = np.zeros_like(X)
print("posed", FRAME, "max move", np.linalg.norm(X - X0, axis=1).max().round(2))
for it in range(80):
    N = normals(X)
    kd = cKDTree(X)
    pr = kd.query_pairs(2 * G, output_type="ndarray")
    i, j = pr[:, 0], pr[:, 1]
    # skin of one part pushed onto other skin of the same part (a thumb onto its palm) must not fuse either
    inc_ = ~compat_pairs(i, j) & ~(band[i] & band[j])
    bad = separate(i, j)
    i, j, inc_ = i[bad], j[bad], inc_[bad]
    # signed distance of each point from the other's tangent plane; closer than G (or behind it) -> push out along it
    sij = np.einsum("ij,ij->i", X[i] - X[j], N[j]); sji = np.einsum("ij,ij->i", X[j] - X[i], N[i])
    near = np.linalg.norm(X[i] - X[j], axis=1) < 2 * G
    push = np.zeros_like(X); cnt = np.zeros(n)
    opp = np.einsum('ij,ij->i', N[i], N[j]) < -.2
    both = near & opp & (((sij > 0) & (sji > 0) & (sij < G) & (sji < G)) | ((sij <= 0) & (sji <= 0) & inc_))
    m1 = m2 = both
    np.add.at(push, i[m1], ((G - sij[m1]) / 2)[:, None] * N[j[m1]]); np.add.at(cnt, i[m1], 1)
    np.add.at(push, j[m2], ((G - sji[m2]) / 2)[:, None] * N[i[m2]]); np.add.at(cnt, j[m2], 1)
    hit = cnt > 0
    print(f"it {it}: pairs {int(both.sum())}, pushed {int(hit.sum())}, worst {float(max((G - sij[m1]).max(initial=0), (G - sji[m2]).max(initial=0))):.3f}", round(time.time() - t0, 1))
    if it % 10 == 9 or it == 79:
        from collections import Counter
        print("   ", Counter((gnames[gi[a]], DOM[a], gnames[gi[b]], DOM[b], bool(nc)) for a, b, nc in zip(i[both], j[both], newc[bad][both] if False else [0]*int(both.sum()))).most_common(8))
    if not hit.any() or both.sum() < 4:
        break
    push[hit] /= cnt[hit][:, None]
    # spread the push smoothly over the skin around (keeps the shape, moves whole regions)
    mag = np.linalg.norm(push, axis=1).max()
    inc = push.copy()
    for _ in range(60):
        inc = .5 * inc + .5 * (Avg @ inc)
        inc[hit] = np.maximum(np.linalg.norm(inc[hit], axis=1), np.linalg.norm(push[hit], axis=1) * .7)[:, None] * \
            push[hit] / np.maximum(np.linalg.norm(push[hit], axis=1, keepdims=True), 1e-9)
    X = X + inc; D += inc
print("max displacement", np.linalg.norm(D, axis=1).max().round(3), "moved >.01:", int((np.linalg.norm(D, axis=1) > .01).sum()))
np.savez(WORK + "/relaxed.npz", X0=X0, X=X, T=TT, W=WW.astype(np.float32), D=D, grp=gi, S=S)
