"""Step 4: one closed smooth surface through the opened skin (screened Poisson), decimated."""
import os
WORK = os.environ.get("REBUILD_WORK", "/tmp/god/rb")
import numpy as np, time, pymeshlab, sys
src, out = sys.argv[1], sys.argv[2]
depth = int(sys.argv[3]) if len(sys.argv) > 3 else 10
M = int(sys.argv[4]) if len(sys.argv) > 4 else 1_500_000
target = int(sys.argv[5]) if len(sys.argv) > 5 else 240000
z = np.load(src); P, T = z["X"], z["T"]
t0 = time.time()
fn = np.cross(P[T[:,1]]-P[T[:,0]], P[T[:,2]]-P[T[:,0]]); area = np.linalg.norm(fn, axis=1) / 2
N = np.zeros_like(P)
for k in range(3): np.add.at(N, T[:,k], fn)
N /= np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-12)
rng = np.random.default_rng(1)
fi = rng.choice(len(T), M, p=area/area.sum()); r1, r2 = rng.random(M), rng.random(M)
s = np.sqrt(r1); a, b, c = 1-s, s*(1-r2), s*r2
pts = a[:,None]*P[T[fi,0]] + b[:,None]*P[T[fi,1]] + c[:,None]*P[T[fi,2]]
nrm = a[:,None]*N[T[fi,0]] + b[:,None]*N[T[fi,1]] + c[:,None]*N[T[fi,2]]
nrm /= np.maximum(np.linalg.norm(nrm, axis=1, keepdims=True), 1e-12)
ms = pymeshlab.MeshSet(); ms.add_mesh(pymeshlab.Mesh(vertex_matrix=pts, v_normals_matrix=nrm))
ms.generate_surface_reconstruction_screened_poisson(depth=depth, samplespernode=1.5, pointweight=4, preclean=False)
print("poisson", round(time.time()-t0), ms.current_mesh().face_number())
ms.meshing_remove_connected_component_by_diameter(mincomponentdiag=pymeshlab.PercentageValue(5))
ms.meshing_decimation_quadric_edge_collapse(targetfacenum=target, qualitythr=.5, preservenormal=True, optimalplacement=True, planarquadric=True)
ms.meshing_remove_unreferenced_vertices()
m = ms.current_mesh()
print("decimated", round(time.time()-t0), m.vertex_number(), m.face_number())
np.savez(out, RP=m.vertex_matrix(), RF=m.face_matrix())
