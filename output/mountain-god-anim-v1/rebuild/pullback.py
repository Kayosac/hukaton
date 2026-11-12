"""Rebuilt skin (pushed-apart space) -> rest: each vertex takes the push and the weights of the old skin nearest to it;
where the skin was healed (away from the old skin), both are relaxed harmonically from around."""
import os
WORK = os.environ.get("REBUILD_WORK", "/tmp/god/rb")

import sys, time, numpy as np
import scipy.sparse as sp, scipy.sparse.linalg as spl
from scipy.spatial import cKDTree
t0 = time.time()
rel, src, out = sys.argv[1], sys.argv[2], sys.argv[3]
z = np.load(rel); X, T, W, D = z["X"], z["T"], z["W"], z["D"]
r = np.load(src); RP, RF = r["RP"], r["RF"]
rng = np.random.default_rng(2)
fn = np.cross(X[T[:,1]]-X[T[:,0]], X[T[:,2]]-X[T[:,0]]); area = np.linalg.norm(fn, axis=1)/2
M = 4_000_000
fi = rng.choice(len(T), M, p=area/area.sum()); r1, r2 = rng.random(M), rng.random(M)
s = np.sqrt(r1); B = np.stack([1-s, s*(1-r2), s*r2], 1)
S = (B[:,:,None]*X[T[fi]]).sum(1)
dist, idx = cKDTree(S).query(RP, workers=-1)
f, b = fi[idx], B[idx]
WR = (b[:,:,None]*W[T[f]]).sum(1); DR = (b[:,:,None]*D[T[f]]).sum(1)
print("nearest", round(time.time()-t0,1), "dist 50/90/99", np.percentile(dist,[50,90,99]).round(4), "max", dist.max().round(3))
far = dist > .03
n = len(RP)
E = np.concatenate([RF[:,[0,1]], RF[:,[1,2]], RF[:,[2,0]]])
A = sp.coo_matrix((np.ones(len(E)), (E[:,0], E[:,1])), shape=(n,n)).tocsr(); A = ((A + A.T) > 0).astype(float)
L = sp.diags(np.asarray(A.sum(1)).ravel()) - A
fi_, kn = np.where(far)[0], np.where(~far)[0]
if len(fi_):
    Lff = L[fi_][:,fi_].tocsc(); Lfk = L[fi_][:,kn]; solve = spl.factorized(Lff)
    for c in np.where(WR[far].max(0) > 1e-4)[0]:
        WR[fi_, c] = solve(-(Lfk @ WR[kn, c]))
    for c in range(3):
        DR[fi_, c] = solve(-(Lfk @ DR[kn, c]))
    WR = np.clip(WR, 0, None); WR /= WR.sum(1, keepdims=True)
if "S" in z.files:
    S = z["S"]
    A = np.einsum("nb,bij->nij", WR, S[:, :3, :3]); bb = np.einsum("nb,bi->ni", WR, S[:, :3, 3])
    RV = np.linalg.solve(A, (RP - DR - bb)[..., None])[..., 0]
else:
    RV = RP - DR
lp = np.linalg.norm(RP[E[:,0]]-RP[E[:,1]], axis=1); lr = np.linalg.norm(RV[E[:,0]]-RV[E[:,1]], axis=1)
print("far", int(far.sum()), "stretched edges x2/x3:", int((lr > 2*np.maximum(lp,.005)).sum()), int((lr > 3*np.maximum(lp,.005)).sum()))
np.savez(out, RV=RV, RP=RP, RF=RF, WR=WR.astype(np.float32), dist=dist, DR=DR, **({'S': z['S']} if 'S' in z.files else {}))
print("done", round(time.time()-t0,1))
