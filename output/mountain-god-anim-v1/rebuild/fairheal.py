"""Healed sheets that came back crumpled (built where a pose stretched the skin, then shrunk back to rest) are faired
smooth in rest between the skin around them; then creases near such places are relaxed (Taubin)."""
import sys, numpy as np
import scipy.sparse as sp, scipy.sparse.linalg as spl
from scipy.sparse.csgraph import connected_components
src, out = sys.argv[1], sys.argv[2]
z = dict(np.load(src))
RV, RP, RF, dist = z["RV"].copy(), z["RP"], z["RF"], z["dist"]
n = len(RV)
E = np.concatenate([RF[:, [0, 1]], RF[:, [1, 2]], RF[:, [2, 0]]])
A = sp.coo_matrix((np.ones(len(E)), (E[:, 0], E[:, 1])), shape=(n, n)).tocsr(); A = ((A + A.T) > 0).astype(float)
deg = np.asarray(A.sum(1)).ravel(); Avg = sp.diags(1 / np.maximum(deg, 1)) @ A
lp = np.linalg.norm(RP[E[:, 0]] - RP[E[:, 1]], axis=1); lr = np.linalg.norm(RV[E[:, 0]] - RV[E[:, 1]], axis=1)
ratio = np.maximum(lr / np.maximum(lp, 1e-4), lp / np.maximum(lr, 1e-4))
bad_e = ratio > 1.8
far = dist > .03
# healed patches: far vertices grown by two rings
grow = far.astype(float)
for _ in range(2): grow = np.maximum(grow, (A @ grow > 0).astype(float))
H = grow > 0
Ah = A.multiply(H[:, None]).multiply(H[None, :]).tocsr()
nc, lab = connected_components(Ah, directed=False)
lab[~H] = -1
badv = np.zeros(n); np.add.at(badv, E[bad_e].ravel(), 1)
fair = np.zeros(n, bool)
for c in np.unique(lab[H]):
    idx = np.where(lab == c)[0]
    if len(idx) < 8: continue
    if (badv[idx] > 0).mean() > .08:
        fair[idx] = True
print("healed patches", len(np.unique(lab[H])), "faired vertices", int(fair.sum()))
Lp = sp.diags(np.ones(n)) - Avg
B = (Lp @ Lp).tocsr()
fi, kn = np.where(fair)[0], np.where(~fair)[0]
if len(fi):
    solve = spl.factorized(B[fi][:, fi].tocsc())
    rhs = -(B[fi][:, kn] @ RV[kn])
    for c in range(3):
        RV[fi, c] = solve(rhs[:, c])
# creases near odd places: relaxed
N_ = None
def normals(X):
    fn = np.cross(X[RF[:, 1]] - X[RF[:, 0]], X[RF[:, 2]] - X[RF[:, 0]]); N = np.zeros_like(X)
    for k in range(3): np.add.at(N, RF[:, k], fn)
    return N / np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-12)
N = normals(RV); M = Avg @ N
dev = 1 - np.einsum("ij,ij->i", N, M / np.maximum(np.linalg.norm(M, axis=1, keepdims=True), 1e-12))
odd = np.zeros(n, bool); np.logical_or.at(odd, E[bad_e].ravel(), True); odd |= far
near = odd.astype(float)
for _ in range(6): near = np.maximum(near, (A @ near > 0).astype(float))
reg = ((dev > .12) & (near > 0)).astype(float)
for _ in range(3): reg = np.maximum(reg, (A @ reg > 0).astype(float))
sel = reg > 0
X = RV.copy()
for it in range(15):
    for lam in (.6, -.62):
        X[sel] += lam * (Avg @ X - X)[sel]
print("relaxed", int(sel.sum()), "max move", np.linalg.norm(X - RV, axis=1).max().round(3))
z["RV"] = X
np.savez(out, **z)
