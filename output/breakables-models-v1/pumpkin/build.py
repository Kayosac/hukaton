"""DB05 Pumpkin debris: 4 skin slices + the stem cap of the big forest pumpkin (SM_Pumpkin_A/B/C).
Skin = M_Pumpkin (same U 0..4 around / V band layout as the source), flesh (inner face, broken edges) =
M_Forest_Props 'amber' ."""
import sys, math, random, json, os
sys.path.insert(0, '/home/claude/brk/work')
import bpy, bmesh, numpy as np
from mathutils import Vector, Matrix
import bk
from bk import PROPS as P, sub

bpy.ops.wm.read_factory_settings(use_empty=True)
OUT = '/home/claude/brk/out/pumpkin/'
os.makedirs(OUT, exist_ok=True)
SKIN, FLESH = 'M_Pumpkin', 'M_Forest_Props'
AMBER, CREAM = P['amber'], P['cream']

# ---------------------------------------------------------------- source pumpkin (A) surface, studied from the FBX
# profile rings (peak radius r, z, skin V) from the bottom centre up over the shoulder to the stem dimple
PROF = [(0.00, 0.020, 0.005), (0.21, 0.000, 0.010), (0.29, 0.030, 0.041), (0.38, 0.090, 0.115), (0.46, 0.170, 0.221),
        (0.51, 0.270, 0.344), (0.51, 0.370, 0.476), (0.47, 0.460, 0.590), (0.40, 0.540, 0.689), (0.32, 0.600, 0.763),
        (0.07, 0.540, 0.820)]
GROOVE = 0.13      # 9 ribs (40 deg), groove radius = 87 % of the rib crest (source: 0.43 vs 0.51)

def prof(s):
    s = min(max(s, 0.0), len(PROF) - 1.0001)
    i = int(s); f = s - i
    a, b = PROF[i], PROF[i + 1]
    return tuple(a[k] + (b[k] - a[k]) * f for k in range(3))

def rib(th):   # groove at multiples of 40 deg, crest between
    x = (th % 40.0) / 20.0          # 0 groove .. 1 crest .. 2 groove
    return 1.0 - GROOVE * (1.0 - min(x, 2.0 - x))

def surf(th, s):
    r, z, v = prof(s)
    a = math.radians(th)
    rr = r * rib(th)
    return Vector((rr * math.cos(a), rr * math.sin(a), z)), v

def normal(th, s):
    e = 0.05
    r0, z0, _ = prof(s - e); r1, z1, _ = prof(s + e)
    dr, dz = r1 - r0, z1 - z0
    n = Vector((dz, -dr)).normalized()
    a = math.radians(th)
    return Vector((n.x * math.cos(a), n.x * math.sin(a), n.y))

def skin_uv(th, v, uoff):
    return (th / 90.0 + uoff, v)

# ---------------------------------------------------------------- shell slice builder
def slice_piece(name, ths, ss, seed, mask=None, t=(0.04, 0.06), jth=6.0, js=0.32, uoff=0.0, seeds=0, zig=1.0, teeth_p=0.7):
    """Grid of cells over rib angles `ths` x profile params `ss`; mask[i][j]=0 drops a cell (notches).
    Outer = skin, inner offset 4-6 cm along the surface normal = flesh, boundary edges get broken flesh walls."""
    rnd = random.Random(seed)
    ni, nj = len(ths), len(ss)
    act = lambda i, j: 0 <= i < ni - 1 and 0 <= j < nj - 1 and (mask is None or mask[j][i])
    cells = [(i, j) for i in range(ni - 1) for j in range(nj - 1) if act(i, j)]
    # boundary edges
    bedges = []
    for i, j in cells:
        for (a, b, ni_, nj_) in (((i, j), (i + 1, j), i, j - 1), ((i + 1, j), (i + 1, j + 1), i + 1, j),
                                 ((i + 1, j + 1), (i, j + 1), i, j + 1), ((i, j + 1), (i, j), i - 1, j)):
            if not act(ni_, nj_):
                bedges.append((a, b))
    bverts = {v for e in bedges for v in e}
    used = {v for i, j in cells for v in ((i, j), (i + 1, j), (i, j + 1), (i + 1, j + 1))}
    O, I, UV, TH = {}, {}, {}, {}
    for (i, j) in used:
        th, s = ths[i], ss[j]
        if (i, j) in bverts:
            sg = 1 if (i + j) % 2 == 0 else -1
            th += rnd.uniform(-jth, jth) * 0.6 + sg * jth * 0.4 * zig
            s += sg * js * rnd.uniform(0.5, 1.0) * zig
        p, v = surf(th, s)
        n = normal(th, s)
        p = p + Vector([rnd.uniform(-0.004, 0.004) for _ in range(3)])
        tt = rnd.uniform(*t)
        if (i, j) in bverts:      # slanted break: inner rim shifted against the outer one
            th2 = th + rnd.uniform(-2.5, 2.5); s2 = s + rnd.uniform(-0.12, 0.12)
            q, _ = surf(th2, s2); q = q - normal(th2, s2) * tt
        else:
            q = p - n * tt
        O[(i, j)] = p; I[(i, j)] = q; UV[(i, j)] = skin_uv(th, v, uoff); TH[(i, j)] = (th, s)
    b = bk.B([SKIN, FLESH])
    # flesh UV: unwrap by (arc, z) over the piece extent into the amber rect
    allp = list(O.values()) + list(I.values())
    def fl_uv(p):
        a = math.atan2(p.y, p.x) * 0.45
        return a, p.z
    A = [fl_uv(p) for p in allp]
    a0, a1 = min(x[0] for x in A), max(x[0] for x in A); z0, z1 = min(x[1] for x in A), max(x[1] for x in A)
    def fuv(p, rect=AMBER):
        a, z = fl_uv(p)
        return bk.uvr(FLESH, rect, (a - a0) / max(a1 - a0, 1e-3), (z - z0) / max(z1 - z0, 1e-3))
    seed_cells = set(rnd.sample(cells, min(seeds, len(cells)))) if seeds else set()
    # extra break teeth in the middle of the top/bottom boundary edges (along the ribs' cross direction)
    teeth = {}
    for a, c in bedges:
        if a[1] == c[1] and rnd.random() < teeth_p:
            tha, sa = TH[a]; thc, sc = TH[c]
            th = (tha + thc) / 2 + rnd.uniform(-2, 2)
            outward = -1 if a[0] < c[0] else 1        # bottom edge of a cell -> tooth points down (-s)
            sgn = outward * rnd.choice((1, 1, -1))
            sm = max((sa + sc) / 2 + sgn * rnd.uniform(0.25, 0.42), 0.4)
            p, v = surf(th, sm); n = normal(th, sm); tt = rnd.uniform(*t)
            q, _ = surf(th + rnd.uniform(-2, 2), sm - sgn * rnd.uniform(0.05, 0.2)); q = q - normal(th, sm) * tt
            teeth[frozenset((a, c))] = (p, q, skin_uv(th, v, uoff))
    def ring(k, inner=False):
        pts, uvs = [], []
        for x, y in zip(k, k[1:] + k[:1]):
            pts.append(I[x] if inner else O[x]); uvs.append(fuv(I[x]) if inner else UV[x])
            tk = teeth.get(frozenset((x, y)))
            if tk:
                pts.append(tk[1] if inner else tk[0]); uvs.append(fuv(tk[1]) if inner else tk[2])
        return pts, uvs
    for i, j in cells:
        k = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
        pts, uvs = ring(k); b.face(pts, uvs, 0)
        kk = k[::-1]
        pts, uvs = ring(kk, True)
        if (i, j) in seed_cells and len(pts) == 4:   # half of this inner quad shows pale seeds (cream)
            b.face(pts[:3], uvs[:3], 1)
            b.face([pts[0], pts[2], pts[3]], [bk.uvr(FLESH, CREAM, 0.1, 0.15), bk.uvr(FLESH, CREAM, 0.3, 0.2),
                                              bk.uvr(FLESH, CREAM, 0.2, 0.35)], 1)
        else:
            b.face(pts, uvs, 1)
    for a, c in bedges:   # broken walls: outer a->c, inner c->a
        L = (O[c] - O[a]).length
        u0 = rnd.uniform(0, 0.4)
        U = lambda f, w: bk.uvr(FLESH, AMBER, u0 + f * L / 0.5, w)
        tk = teeth.get(frozenset((a, c)))
        if tk:
            b.face([O[c], tk[0], O[a], I[a], tk[1], I[c]], [U(1, .75), U(.5, .8), U(0, .75), U(0, .45), U(.5, .4), U(1, .45)], 1)
        else:
            b.face([O[c], O[a], I[a], I[c]], [U(1, .75), U(0, .75), U(0, .45), U(1, .45)], 1)
    # turn the slice so its skin faces the front (-Y)
    mid = (ths[0] + ths[-1]) / 2.0
    bmesh.ops.rotate(b.bm, verts=b.bm.verts[:], cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(-90 - mid), 3, 'Z'))
    o = b.obj(name)
    return o

# ---------------------------------------------------------------- the stem cap
def ring_zip(b, A, B, m=0):
    """Stitch two closed rings (lists of (pos, theta_deg, v)) by angle; skin u = theta/90 (4 tiles around)."""
    A = sorted([(p, th % 360.0, v) for p, th, v in A], key=lambda x: x[1])
    B = sorted([(p, th % 360.0, v) for p, th, v in B], key=lambda x: x[1])
    st = min(A[0][1], B[0][1])
    A2 = A + [(A[0][0], A[0][1] + 360, A[0][2])]
    B2 = B + [(B[0][0], B[0][1] + 360, B[0][2])]
    i = j = 0
    while i < len(A) or j < len(B):
        if j >= len(B) or (i < len(A) and A2[i + 1][1] < B2[j + 1][1]):
            tri = [A2[i], A2[i + 1], B2[j]]; i += 1
        else:
            tri = [A2[i], B2[j + 1], B2[j]]; j += 1
        b.face([x[0] for x in tri], [(x[1] / 90.0, x[2]) for x in tri], m)

def cap_piece(name, seed):
    rnd = random.Random(seed)
    b = bk.B([SKIN, FLESH])
    J = lambda a=0.004: Vector([rnd.uniform(-a, a) for _ in range(3)])
    # stem base pentagon in the dimple (source: r ~0.1 at z 0.52-0.54)
    base = []
    for k in range(5):
        th = 18 + k * 72 + rnd.uniform(-6, 6)
        a = math.radians(th); r = 0.085 + rnd.uniform(-0.01, 0.01)
        base.append((Vector((r * math.cos(a), r * math.sin(a), 0.548)) + J(0.003), th))
    # mid ring on the dish, at the 9 rib crests
    mid = []
    for k in range(9):
        th = 20 + k * 40 + rnd.uniform(-3, 3)
        p, v = surf(th, 9.55); mid.append((p + J(), th, 0.795))
    # jagged break ring around the crest: crests stick out further, grooves break inside
    edge, inner = [], []
    for k in range(18):
        th = k * 20 + rnd.uniform(-4, 4)
        crest = k % 2 == 1
        s = rnd.choice((8.55, 8.75, 8.95, 9.1)) if crest else rnd.uniform(8.85, 9.2)
        p, v = surf(th, s)
        n = normal(th, s)
        tt = rnd.uniform(0.04, 0.06)
        p = p + J()
        q = p - n * tt + Vector((0, 0, rnd.uniform(-0.01, 0.01)))
        q = q + Vector((math.cos(math.radians(th)), math.sin(math.radians(th)), 0)) * rnd.uniform(-0.03, 0.0)
        edge.append((p, th, v)); inner.append(q)
    # dish: stem base -> mid ring (just above the black line = shadow ring around the stem, like the source)
    ring_zip(b, [(p, th, 0.818) for p, th in base], mid, 0)
    ring_zip(b, mid, edge, 0)
    # underside flesh: fan to a centre under the stem
    allp = [e[0] for e in edge] + inner
    lo = min(p.x for p in allp), min(p.y for p in allp); hi = max(p.x for p in allp), max(p.y for p in allp)
    fu = lambda p: bk.uvr(FLESH, AMBER, (p.x - lo[0]) / (hi[0] - lo[0]), (p.y - lo[1]) / (hi[1] - lo[1]))
    cen = Vector((0.01, -0.01, 0.50))
    E = sorted(range(18), key=lambda k: edge[k][1] % 360)
    seedk = set()   # cream seed tris tried: read as grey patches, dropped
    for a in range(18):
        k0, k1 = E[a], E[(a + 1) % 18]
        if a in seedk:
            b.face([inner[k1], inner[k0], cen], [bk.uvr(FLESH, CREAM, .1, .1), bk.uvr(FLESH, CREAM, .3, .15),
                                                 bk.uvr(FLESH, CREAM, .2, .3)], 1)
        else:
            b.face([inner[k1], inner[k0], cen], [fu(inner[k1]), fu(inner[k0]), bk.uvr(FLESH, AMBER, .5, .5)], 1)
        # broken wall between skin edge and flesh
        L = (edge[k1][0] - edge[k0][0]).length; u0 = rnd.uniform(0, .4)
        b.face([edge[k0][0], edge[k1][0], inner[k1], inner[k0]],
               [bk.uvr(FLESH, AMBER, u0, .75), bk.uvr(FLESH, AMBER, u0 + L / .5, .75),
                bk.uvr(FLESH, AMBER, u0 + L / .5, .45), bk.uvr(FLESH, AMBER, u0, .45)], 1)
    # curly stem: pentagon tube base -> 2 rings -> tip point, bending over toward +X and curling to -Y
    path = [Vector((0, 0, 0.548)), Vector((0.015, -0.005, 0.64)), Vector((0.075, -0.03, 0.705)), Vector((0.17, -0.075, 0.69))]
    rads = [None, 0.062, 0.047]
    rings = [[p for p, th in base]]
    for k in (1, 2):
        d = (path[k + 1] - path[k - 1]).normalized()
        x = d.cross(Vector((0, 0, 1)))
        if x.length < 1e-3: x = Vector((1, 0, 0))
        x.normalize(); y = d.cross(x)
        R = []
        for q in range(5):
            a = math.radians(18 + q * 72 + k * 12)
            R.append(path[k] + (x * math.cos(a) + y * math.sin(a)) * rads[k] * rnd.uniform(0.85, 1.1))
        rings.append(R)
    # match ring vertex order by angle around the axis: reorder rings 1,2 so vertex q is closest to ring 0's q
    for k in (1, 2):
        prev = rings[k - 1]; cur = rings[k]
        best = min(range(5), key=lambda sh: sum((cur[(q + sh) % 5] - prev[q] - (path[k] - path[k - 1])).length for q in range(5)))
        rings[k] = [cur[(q + best) % 5] for q in range(5)]
    vs = [0.845, 0.884, 0.93]
    for k in range(2):
        for q in range(5):
            q1 = (q + 1) % 5
            u0, u1 = q * 0.2, (q + 1) * 0.2
            b.face([rings[k][q], rings[k][q1], rings[k + 1][q1], rings[k + 1][q]],
                   [(u0, vs[k]), (u1, vs[k]), (u1, vs[k + 1]), (u0, vs[k + 1])], 0)
    tip = path[3]
    for q in range(5):
        q1 = (q + 1) % 5
        b.face([rings[2][q], rings[2][q1], tip], [(q * .2, .93), ((q + 1) * .2, .93), (q * .2 + .1, .985)], 0)
    o = b.obj(name)
    return o

# ---------------------------------------------------------------- pieces
pieces = []
# tall: one whole rib lobe split off along two grooves, from just above the base over the shoulder
pieces.append(slice_piece('DB05_Pumpkin_SliceTall', [0, 20, 40], [1.9, 3.0, 4.2, 5.5, 6.7, 7.6, 8.4], seed=11,
                          mask=[[1, 0], [1, 1], [1, 1], [1, 1], [1, 1], [1, 1]], uoff=0.0, jth=2.0, js=0.26))
# wide: across the belly from a groove over a crest and the next groove, broken mid-rib on the right
pieces.append(slice_piece('DB05_Pumpkin_SliceWide', [120, 140, 160, 176], [4.4, 5.5, 6.6, 7.2], seed=23,
                          mask=[[1, 1, 1], [1, 1, 1], [1, 1, 0]], uoff=0.5, jth=2.5, js=0.24))
# small chip: half a lobe, groove to just past the crest
pieces.append(slice_piece('DB05_Pumpkin_SliceSmall', [240, 260, 270], [5.2, 6.0, 6.8], seed=37,
                          uoff=0.25, jth=2.0, js=0.2))
# bottom: one lobe that keeps a wedge of the flat base, narrowing towards the bottom centre
pieces.append(slice_piece('DB05_Pumpkin_SliceBottom', [280, 300, 320], [0.8, 1.35, 2.2, 3.1, 4.1, 4.9], seed=41,
                          mask=[[1, 1], [1, 1], [1, 1], [1, 1], [1, 0]], uoff=0.75, jth=2.0, js=0.24))
pieces.append(cap_piece('DB05_Pumpkin_Top', seed=5))

# triangulate (deterministic export) + check
for o in pieces:
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method='BEAUTY', ngon_method='EAR_CLIP')
    bm.to_mesh(o.data); bm.free()
    for p in o.data.polygons: p.use_smooth = False

# layout: side by side along X, ~2x their size apart, resting on the ground
x = 0.0
cols = []
bpy.context.view_layer.update()
for o in pieces:
    lo, hi = bk.bounds([o]); w = float(hi[0] - lo[0])
    x += w
    o.location = Vector((x, 0, o.location.z - float(lo[2])))
    x += w + 0.1
bpy.context.view_layer.update()
for o in pieces:
    cols.append(bk.col(o))
objs = []
for o, c in zip(pieces, cols): objs += [o, c]
rows = bk.report(objs)
NOTES = {
 'DB05_Pumpkin_SliceTall': 'tall wall slice: 1.5 rib lobes, from above the base over the shoulder; skin M_Pumpkin skin band, inner face + broken walls M_Forest_Props amber',
 'DB05_Pumpkin_SliceWide': 'wide low slice of the belly across two rib lobes, jagged top/bottom breaks',
 'DB05_Pumpkin_SliceSmall': 'small chip across one rib groove',
 'DB05_Pumpkin_SliceBottom': 'wall slice that keeps a wedge of the flat base (darker bottom band of T_Pumpkin)',
 'DB05_Pumpkin_Top': 'stem cap: the dish around the stem with the curly pentagon stem (stem band), broken off in a jagged ring at the rib crests; flesh underside',
}
for r in rows:
    r['note'] = NOTES.get(r['name'], 'convex hull collider (same origin as its piece)')
print(json.dumps(rows, indent=1))
json.dump(rows, open(OUT + 'manifest.json', 'w'), indent=1)
bk.export_fbx(objs, OUT + 'SM_DB05_PumpkinChunks.fbx')

# ---------------------------------------------------------------- card
if '--nocard' not in sys.argv:
    tiles, labels = [], []
    ANG = {'DB05_Pumpkin_SliceBottom': ((-30, -18), (160, 45)), 'DB05_Pumpkin_Top': ((-35, 30), (160, -35))}
    for o in pieces:
        for az, el in ANG.get(o.name, ((-35, 28), (150, 40))):
            tiles.append(bk.render_objs([o], OUT + f'_t_{o.name}_{az}.png', az=az, el=el, size=(420, 420), samples=9))
            labels.append(f'{o.name[5:]} az{az} el{el}')
    # scale shot: pieces lying around the source pumpkin
    src = bk.import_src('SM_Pumpkin_A.fbx')
    for s_ in src: s_.location = Vector((0, 0.0, 0))
    save = [o.location.copy() for o in pieces]
    spots = [(-0.95, -0.35), (0.85, -0.55), (-0.45, -0.85), (0.95, 0.35), (0.15, -1.05)]
    rots = [(0, 0, 30), (90, 0, 20), (80, 0, -40), (0, 0, 0), (0, 0, 60)]
    for o, (x_, y_), rr in zip(pieces, spots, rots):
        o.rotation_euler = [math.radians(a) for a in rr]
        bpy.context.view_layer.update()
        lo, hi = bk.bounds([o])
        o.location = Vector((x_, y_, o.location.z - float(lo[2])))
        bpy.context.view_layer.update()
    tiles.append(bk.render_objs(pieces + src, OUT + '_t_scale.png', az=-20, el=35, size=(420, 420), samples=9))
    labels.append('with SM_Pumpkin_A')
    for o, l in zip(pieces, save):
        o.location = l; o.rotation_euler = (0, 0, 0)
    for s_ in src: bpy.data.objects.remove(s_)
    bk.sheet(tiles, OUT + 'card.jpg', cols=4, labels=labels, title='DB05 Pumpkin debris')
    for t in tiles: os.remove(t)
bk.save_blend(OUT + 'pumpkin.blend')
import shutil; shutil.copy(__file__, OUT + 'build.py')
