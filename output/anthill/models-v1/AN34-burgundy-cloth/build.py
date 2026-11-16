import sys, os, math, random
sys.path.insert(0, '/home/claude/ant/models'); from lib_sup import *
from mathutils import Vector, Matrix
D = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(D, 'out'); os.makedirs(OUT, exist_ok=True)
REND = '--norender' not in sys.argv

ak.reset()
rnd = random.Random(34)
BX, BY, TOP = 0.88, 0.63, 0.43       # the (invisible) low box the cloth is draped over
HANG = 0.31                           # flat-sheet overhang beyond the box edge
def axis(b, nt):
    out = [-b - HANG, -b - .25, -b - .19, -b - .13, -b - .07, -b - .03]
    out += [-b + 2 * b * i / nt for i in range(nt + 1)]
    out += [b + .03, b + .07, b + .13, b + .19, b + .25, b + HANG]
    return out
XS = axis(BX, 18); YS = axis(BY, 13)
NX, NY = len(XS), len(YS)
# ragged edge: per boundary vertex extra length (teeth)
rag = {}
for i in range(NX):
    for j in range(NY):
        if i in (0, NX - 1) or j in (0, NY - 1):
            tooth = .05 if (i + j) % 2 == 0 else -.015
            rag[(i, j)] = tooth + rnd.uniform(-.025, .03)
        elif i in (1, NX - 2) or j in (1, NY - 2):
            rag[(i, j)] = rnd.uniform(-.015, .025)

def P(i, j, inner=0.0):
    X, Y = XS[i], YS[j]
    dx = max(abs(X) - BX, 0.0); dy = max(abs(Y) - BY, 0.0)
    d = math.hypot(dx, dy) + rag.get((i, j), 0.0) * (1 if (dx > 0 or dy > 0) else 0)
    if dx == 0 and dy == 0:
        # top: shallow wavy folds, slight dip between raised ends
        z = TOP + .028 * math.sin(2.4 * X + .6) * math.cos(1.6 * Y + .3) + .03 * (X / BX) ** 2 - .015 \
            + .012 * math.sin(5 * Y + X)
        return Vector((X * (1 + .02 * math.sin(3 * Y)), Y, z - inner))
    sx, sy = math.copysign(1, X), math.copysign(1, Y)
    cx, cy = max(min(X, BX), -BX), max(min(Y, BY), -BY)
    o = Vector((sx * dx, sy * dy, 0))
    o = o.normalized() if o.length else Vector((sx, 0, 0))
    # round over the box edge (first few cm), then hang
    flare = .035 + .06 * min(d / HANG, 1.0)
    edge_s = (Y if dx > 0 and dy == 0 else X)   # coordinate along the edge -> vertical pleats
    pleat = (.025 + .05 * min(d / HANG, 1)) * math.sin(edge_s * 7.5 + (1.7 if dx > 0 else 0.4))
    if dx > 0 and dy > 0:
        pleat = .04 * math.sin(d * 6)
    z = TOP - d * .97 - .015 * min(d / .05, 1)
    z += .02 * math.sin(edge_s * 4 + 1)   # hem not level
    pos = Vector((cx, cy, 0)) + o * (flare + pleat - inner)
    if z < 0.01:
        pos += o * (0.01 - z) * .6; z = 0.01
    return Vector((pos.x, pos.y, z))

C = ak.Mesh('Cloth'); PR = ak.Mesh('Props')
T = 1.2
# holes / tears: removed grid cells (front skirt, right skirt, back skirt, a slit on the top near the right edge)
HOLES = {(12, 2), (13, 2), (12, 3), (NX - 4, 10), (NX - 4, 11), (NX - 3, 11), (8, NY - 4), (9, NY - 4),
         (21, 8), (22, 8), (4, 1), (3, 13)}
def N(i, j):
    a = P(min(i + 1, NX - 1), j) - P(max(i - 1, 0), j)
    b = P(i, min(j + 1, NY - 1)) - P(i, max(j - 1, 0))
    return a.cross(b).normalized()
for i in range(NX - 1):
    for j in range(NY - 1):
        if (i, j) in HOLES:
            continue
        idx = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
        uv = [(XS[a] / T, YS[b] / T) for a, b in idx]
        C.face([P(a, b) for a, b in idx], uv)
        C.face([P(a, b, .012) for a, b in reversed(idx)], list(reversed(uv)))
def fringe(p, tangent, down, L, seed, w=.012):
    r2 = random.Random(seed)
    t = tangent.normalized() * w
    sway = Vector((r2.uniform(-.02, .02), r2.uniform(-.02, .02), 0))
    p1 = p + down * L * .5 + sway * .5; p2 = p + down * L + sway
    for (a, b) in ((p, p1), (p1, p2)):
        q = [a - t, a + t, b + t * .7, b - t * .7]
        C.face(q, [(a.x / T, a.z / T), (a.x / T + .02, a.z / T), (b.x / T + .02, b.z / T), (b.x / T, b.z / T)])
        C.face(list(reversed(q)), [(b.x / T, b.z / T), (b.x / T + .02, b.z / T), (a.x / T + .02, a.z / T), (a.x / T, a.z / T)])
rnd2 = random.Random(341)
# thickness strips round the hole edges + frayed threads hanging into the holes
for (i, j) in HOLES:
    for (a, b, nb) in (((i, j), (i + 1, j), (i, j - 1)), ((i + 1, j), (i + 1, j + 1), (i + 1, j)),
                       ((i + 1, j + 1), (i, j + 1), (i, j + 1)), ((i, j + 1), (i, j), (i - 1, j))):
        if nb in HOLES or not (0 <= nb[0] < NX - 1 and 0 <= nb[1] < NY - 1):
            continue
        q = [P(*a), P(*b), P(*b, .012), P(*a, .012)]
        C.face(q, [(0, 0), (.1, 0), (.1, .01), (0, .01)])
        C.face(list(reversed(q)), [(0, .01), (.1, .01), (.1, 0), (0, 0)])
        if rnd2.random() < .6:
            pa = P(*a).lerp(P(*b), .5)
            fringe(pa, P(*b) - P(*a), Vector((0, 0, -1)), rnd2.uniform(.04, .09), rnd2.randint(0, 9999), w=.008)
# hem strip joining both sides along the ragged border + frayed fringe strips hanging off the hem
border = [(i, 0) for i in range(NX - 1)] + [(NX - 1, j) for j in range(NY - 1)] + \
         [(i, NY - 1) for i in range(NX - 1, 0, -1)] + [(0, j) for j in range(NY - 1, 0, -1)]
for k in range(len(border)):
    a, b = border[k], border[(k + 1) % len(border)]
    q = [P(*a), P(*b), P(*b, .012), P(*a, .012)]
    face_out(C, q, (q[0] + q[1]) / 2, lambda pts, n: [(0, 0), (.1, 0), (.1, .01), (0, .01)])
    for f in (.25, .7):
        if rnd2.random() < .8:
            p = P(*a).lerp(P(*b), f)
            out = Vector((p.x, p.y, 0)).normalized() * .25
            fringe(p, P(*b) - P(*a), (Vector((0, 0, -1)) + out).normalized(), rnd2.uniform(.03, .1), rnd2.randint(0, 99999))
# patches (Props cloth region, darker) sewn on with stitches
def patch(i0, j0, ni, nj, seed):
    r2 = random.Random(seed)
    G = [[P(i0 + a, j0 + b) + N(i0 + a, j0 + b) * .007 + Vector((r2.uniform(-.006, .006),) * 3) for b in range(nj + 1)]
         for a in range(ni + 1)]
    uv = lambda a, b: R('cloth', .05 + .9 * a / ni, .05 + .9 * b / nj, fold=False)
    for a in range(ni):
        for b in range(nj):
            PR.quad(G[a][b], G[a + 1][b], G[a + 1][b + 1], G[a][b + 1], uv(a, b), uv(a + 1, b), uv(a + 1, b + 1), uv(a, b + 1))
    ring = [(a, 0) for a in range(ni)] + [(ni, b) for b in range(nj)] + [(a, nj) for a in range(ni, 0, -1)] + \
           [(0, b) for b in range(nj, 0, -1)]
    for k in range(len(ring)):
        (a1, b1), (a2, b2) = ring[k], ring[(k + 1) % len(ring)]
        A, B = G[a1][b1], G[a2][b2]
        nA = N(i0 + a1, j0 + b1) * .008; nB = N(i0 + a2, j0 + b2) * .008
        PR.quad(A - nA, B - nB, B, A, uv(a1, b1), uv(a2, b2), uv(a2, b2), uv(a1, b1))
        cen = G[ni // 2][nj // 2]
        m_ = A.lerp(B, .5); inw = (cen - m_).normalized() * .014
        atlas_tube(PR, [m_ - inw + nA * .3, m_ + nA * .6, m_ + inw + nA * .3], .004, 'rope', sides=3, metres_u=.1,
                   caps=(False, False))
patch(9, 14, 3, 3, 1)     # on the top
patch(21, 1, 3, 3, 2)     # front skirt
patch(1, 15, 3, 3, 3)     # left skirt
patch(16, NY - 5, 2, 3, 4)   # back skirt
# raised seams: thin cords following the cloth across the top + down the skirts (Props cloth region)
for path in ([P(i, 10) + N(i, 10) * .006 for i in range(NX)], [P(24, j) + N(24, j) * .006 for j in range(NY)]):
    atlas_tube(PR, path, .007, 'cloth', sides=3, metres_u=.4, caps=(False, False))
co = C.to_object('AN34a_Cloth')
po = PR.to_object('AN34a_Props')
objs = [co, po]
ak.ground(objs, center_xy=False)
check_normals(objs)
f = os.path.join(D, 'SM_AN34_BurgundyCloth.fbx'); ak.export_fbx(objs, f)
rec = ak.variant_record('AN34a', 'Burgundy bed cloth (draped)', objs, f)
print(rec)
write_manifest(OUT, 'AN34 burgundy-cloth', [rec], solidity='solid (low box-like heap) or decor over the bed rim',
               collider='box 1.8 x 1.3 x 0.43 (the draped volume)', emissive=False,
               notes='Pivot bottom centre. Cloth draped over an implied low box (no box mesh): wavy top with shallow folds, '
                     'hanging sides with vertical pleats, pointed corners hanging lowest, ragged toothed hem. Double-sided '
                     '(underside 12 mm inside) + hem strip. v2: 12 cut holes/tears with edge strips and threads, ~190 frayed fringe '
                     'strips off the hem, 4 sewn patches with stitches (Props cloth), 2 raised seam cords (Props cloth). Weave painted (Cloth tiling 1.2 m, '
                     'UVs = flat sheet coordinates, no stretching).')
if REND:
    t, l = renders(objs, OUT, 'a', el=24)
    card('34-burgundy-cloth', OUT, t, l, 'AN34 burgundy cloth (v2 detail)')
ak.save_blend(os.path.join(D, 'AN34_BurgundyCloth.blend'))
