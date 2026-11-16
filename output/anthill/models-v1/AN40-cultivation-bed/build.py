import sys, math, os
sys.path.insert(0, '/home/claude/ant/models')
from lib_organic import *

HERE = os.path.dirname(os.path.abspath(__file__))
RENDER = '--norender' not in sys.argv

M = 11          # rows along the bed (Y)
W, L = 1.45, 4.0
rnd = random.Random(40)
YS = [-L / 2 + L * j / M + (rnd.uniform(-0.06, 0.06) if 0 < j < M else 0) for j in range(M + 1)]
XL = [-W / 2 + 0.05 * math.sin(j * 1.9) + rnd.uniform(-0.04, 0.04) for j in range(M + 1)]
XR = [W / 2 + 0.05 * math.sin(j * 1.3 + 1) + rnd.uniform(-0.04, 0.04) for j in range(M + 1)]
CH = 0.18       # corner chamfer


def ring(d, z, zj=0.0, xj=0.0, seed=0):
    """Closed outline inset by d (negative = outward). Order: left side front->back, right side back->front."""
    r = random.Random(seed)
    pts = []
    for side in (0, 1):
        js = range(M + 1) if side == 0 else range(M, -1, -1)
        for j in js:
            x = XL[j] + d if side == 0 else XR[j] - d
            y = YS[j]
            if j == 0:
                y += d; x += CH if side == 0 else -CH
            elif j == M:
                y -= d; x += CH if side == 0 else -CH
            elif j == 1:
                y = YS[0] + d + CH * 0.8
            elif j == M - 1:
                y = YS[M] - d - CH * 0.8
            x += r.uniform(-xj, xj)
            pts.append(Vector((x, y, z + r.uniform(-zj, zj))))
    return pts


def band(mesh, A, B):
    n = len(A)
    for k in range(n):
        k2 = (k + 1) % n
        mesh.quad(A[k], A[k2], B[k2], B[k], (0, 0), (1, 0), (1, 1), (0, 1))


def build():
    ak.reset()
    e = ak.Mesh('Earth'); p = ak.Mesh('Props')
    # earth rim: outer wall (slanted, with a bulge), bevelled top, inner wall down into the soil; closed for normals
    rings = [ring(-0.06, 0.0, seed=1),
             ring(-0.03, 0.18, zj=0.03, xj=0.03, seed=2),
             ring(0.04, 0.36, zj=0.025, xj=0.02, seed=3),
             ring(0.09, 0.41, zj=0.03, xj=0.02, seed=4),
             ring(0.22, 0.39, zj=0.025, xj=0.02, seed=5),
             ring(0.27, 0.33, zj=0.02, seed=6),
             ring(0.32, 0.0, seed=7)]
    # chip a few notches out of the rim top (lower some top verts)
    for k in rnd.sample(range(len(rings[3])), 6):
        for rr in (rings[3], rings[4]):
            rr[k].z -= rnd.uniform(0.03, 0.06)
    for i in range(len(rings) - 1):
        band(e, rings[i], rings[i + 1])
    band(e, rings[-1], rings[0])
    recalc(e)
    drop_bottom(e)
    # soil: substrate grid slightly below the rim top, edges tucked into the inner wall
    nx = 4
    soil = ring(0.24, 0.27)
    left = soil[:M + 1]; right = list(reversed(soil[M + 1:]))
    G = []
    for j in range(M + 1):
        row = []
        for i in range(nx + 1):
            q = left[j].lerp(right[j], i / nx)
            edge = i in (0, nx) or j in (0, M)
            q.z = 0.27 if edge else 0.29 + rnd.uniform(-0.015, 0.03)
            row.append(q)
        G.append(row)
    sr = random.Random(9)
    for j in range(M):
        for i in range(nx):
            pts = [G[j][i], G[j][i + 1], G[j + 1][i + 1], G[j + 1][i]]
            nrm = (pts[1] - pts[0]).cross(pts[3] - pts[0])
            if nrm.z < 0:
                pts = list(reversed(pts))
            fit_face(p, pts, SUB_CLEAN, metres=1.1, rnd=sr)
    eo = e.to_object('AN40a_Earth'); po = p.to_object('AN40a_Props')
    for o in (eo, po):   # concept: long axis runs along X
        o.data.transform(Matrix.Rotation(math.pi / 2, 4, 'Z'))
    ak.auto_uv(eo)
    objs = [eo, po]
    ak.ground(objs)
    return objs


if __name__ == '__main__':
    objs = build()
    rec = outputs(objs, HERE, 'SM_AN40_CultivationBed.fbx', 'AN40a', 'CultivationBed', SHEETS + '40-cultivation-bed.png',
                  manifest_extra=dict(solidity='solid (low obstacle) / walk-on-top possible', collider='box 4.1 x 1.6 x 0.42 (or mesh)',
                                      emissive=False, pivot='bottom centre; long axis = X (as in the concept views; brief said 2.4 x 4)',
                                      notes='earth rim (Earth) with bevelled crooked top and notches; soil = Props substrate (vein-free part) ~0.12 m below the rim top; mushrooms placed separately'),
                  views=RENDER)
    write_manifest(HERE, [rec])
    print(rec)
