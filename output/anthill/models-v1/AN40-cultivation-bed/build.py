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
    rim = e.to_object('rim'); po = p.to_object('AN40a_Props')
    for o in (rim, po):   # concept: long axis runs along X
        o.data.transform(Matrix.Rotation(math.pi / 2, 4, 'Z'))
    # cracks cut into the rim (exact boolean): down the outer wall and across the top
    dr = random.Random(41)
    WG = []
    for (p0, n0) in samples([rim], 9, 400, lambda q, n: 0.1 < q.z < 0.3 and abs(n.z) < 0.5):
        t = Vector((0, 0, 1)).cross(n0).normalized()
        a = p0 + Vector((0, 0, 0.2)) + t * dr.uniform(-0.05, 0.05)
        b = p0 - Vector((0, 0, 0.12)) + t * dr.uniform(-0.12, 0.12)
        WG.append((a + n0 * 0.02, b + n0 * 0.02, n0, dr.uniform(0.025, 0.045), dr.uniform(0.06, 0.1)))
    for (p0, n0) in samples([rim], 6, 401, lambda q, n: n.z > 0.85 and q.z > 0.36):
        acr = Vector((0, 1 if p0.y < 0 else -1, 0)) if abs(p0.x) < 1.7 else Vector((1 if p0.x < 0 else -1, 0, 0))
        WG.append((p0 - acr * 0.12 + Vector((dr.uniform(-.05, .05), 0, 0)), p0 + acr * 0.12, (0, 0, 1),
                   dr.uniform(0.025, 0.04), dr.uniform(0.05, 0.08)))
    polys, ncut = safe_cuts(rim, WG)
    print('cracks applied', ncut, 'of', len(WG))
    me = rim.data; bpy.data.objects.remove(rim); bpy.data.meshes.remove(me); bpy.context.view_layer.update()
    E = ak.Mesh('Earth')
    for q in polys:
        if all(v.z < 1e-4 for v in q):
            continue
        E.face(q, [(0, 0)] * len(q))
    eo = E.to_object('AN40a_Earth')
    ak.auto_uv(eo)
    # details: clods + pebbles on the rim and at its foot, sprouting glow mushrooms, crumbs, leaves, twigs, needles,
    # two little burrows in the soil
    D = ak.Mesh('Props')
    soil_ok = lambda q, n: n.z > 0.7
    for j, (q, n) in enumerate(samples([eo], 26, 402, lambda q, n: n.z > 0.6 and q.z > 0.3)):
        lump(D, q, dr.uniform(0.04, 0.09), n, sides=6, squash=dr.uniform(.5, .8), sink=0.35, seed=4100 + j,
             spec=full('earth_props') if dr.random() < 0.7 else full('pebble'), metres=0.4)
    for j in range(34):
        a = dr.uniform(0, 6.28)
        q = Vector((math.cos(a) * dr.uniform(2.1, 2.5), math.sin(a) * dr.uniform(0.9, 1.25), 0.0))
        lump(D, q, dr.uniform(0.04, 0.12), (0, 0, 1), sides=dr.choice((5, 6, 7)), squash=dr.uniform(.45, .8), sink=0.3,
             seed=4200 + j, spec=full('earth_props') if j % 2 else full('pebble'), metres=0.35)
    sp = samples([po], 80, 403, soil_ok)
    for j, (q, n) in enumerate(sp[:14]):
        baby_mushroom(D, q, n, dr.uniform(0.06, 0.2), dr.uniform(0.035, 0.08), seed=4300 + j, lean=0.3)
    for j, (q, n) in enumerate(sp[14:50]):
        lump(D, q, dr.uniform(0.02, 0.045), n, sides=5, squash=0.55, sink=0.3, seed=4400 + j,
             spec=full('earth_props') if j % 3 else full('substrate'), metres=0.2)
    sb = bvh_of([po, eo])
    for j, (q, n) in enumerate(sp[50:57]):
        place_leaf(D, sb, (q.x, q.y, 2.0), (0, 0, -1), dr.uniform(0.2, 0.34), dr.choice(('leaf_dry_0', 'leaf_dry_1', 'leaf_dry_2')),
                   seed=4500 + j, lift=0.01, curl=0.03)
    for j, (q, n) in enumerate(sp[57:62]):
        a = dr.uniform(0, 6.28)
        twig(D, q + Vector((0, 0, 0.015)), (math.cos(a), math.sin(a), 0.05), dr.uniform(0.2, 0.4), 0.014, seed=4600 + j,
             embed=0.0, bend=0.1)
    for j, (q, n) in enumerate(sp[62:64]):
        a = dr.uniform(0, 6.28)
        needles(D, q, (math.cos(a), math.sin(a), 0.1), 0.25, 5, 0.5, seed=4700 + j, r=0.008, embed=0.0)
    for j, (q, n) in enumerate(sp[64:66]):
        burrow(D, q, n, 0.05, seed=4800 + j, depth=0.1)
    clamp(D, 2, lo=0.0); drop_bottom(D)
    join_into(po, D)
    objs = [eo, po]
    ak.ground(objs)
    return objs


if __name__ == '__main__':
    objs = build()
    rec = outputs(objs, HERE, 'SM_AN40_CultivationBed.fbx', 'AN40a', 'CultivationBed', SHEETS + '40-cultivation-bed.png',
                  manifest_extra=dict(solidity='solid (low obstacle) / walk-on-top possible', collider='box 4.1 x 1.6 x 0.42 (or mesh)',
                                      emissive=True, emissive_parts='gill_glow_yellow under 14 tiny sprouting mushrooms', pivot='bottom centre; long axis = X (as in the concept views; brief said 2.4 x 4)',
                                      notes='earth rim (Earth) with bevelled crooked top and notches; soil = Props substrate (vein-free part) ~0.12 m below the rim top; + cracks cut into the rim, clods/pebbles on the rim and around the foot, 14 tiny sprouting glow mushrooms, crumbs, leaves, twigs, needles, 2 burrows; big mushrooms placed separately'),
                  views=RENDER)
    write_manifest(HERE, [rec])
    print(rec)
