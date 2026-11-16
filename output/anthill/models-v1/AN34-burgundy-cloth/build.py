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
    out = [-b - HANG, -b - .22, -b - .13, -b - .04]
    out += [-b + 2 * b * i / nt for i in range(nt + 1)]
    out += [b + .04, b + .13, b + .22, b + HANG]
    return out
XS = axis(BX, 12); YS = axis(BY, 9)
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

C = ak.Mesh('Cloth')
T = 1.2
for i in range(NX - 1):
    for j in range(NY - 1):
        idx = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
        uv = [(XS[a] / T, YS[b] / T) for a, b in idx]
        C.face([P(a, b) for a, b in idx], uv)
        C.face([P(a, b, .012) for a, b in reversed(idx)], list(reversed(uv)))
# hem strip joining both sides along the ragged border (gives the edge a little thickness)
border = [(i, 0) for i in range(NX - 1)] + [(NX - 1, j) for j in range(NY - 1)] + \
         [(i, NY - 1) for i in range(NX - 1, 0, -1)] + [(0, j) for j in range(NY - 1, 0, -1)]
for k in range(len(border)):
    a, b = border[k], border[(k + 1) % len(border)]
    q = [P(*a), P(*b), P(*b, .012), P(*a, .012)]
    face_out(C, q, (q[0] + q[1]) / 2 - Vector((0, 0, q[0].z)) * 0, lambda pts, n: [(0, 0), (.1, 0), (.1, .01), (0, .01)])
co = C.to_object('AN34a_Cloth')
objs = [co]
ak.ground(objs)
check_normals(objs)
f = os.path.join(D, 'SM_AN34_BurgundyCloth.fbx'); ak.export_fbx(objs, f)
rec = ak.variant_record('AN34a', 'Burgundy bed cloth (draped)', objs, f)
print(rec)
write_manifest(OUT, 'AN34 burgundy-cloth', [rec], solidity='solid (low box-like heap) or decor over the bed rim',
               collider='box 1.8 x 1.3 x 0.43 (the draped volume)', emissive=False,
               notes='Pivot bottom centre. Cloth draped over an implied low box (no box mesh): wavy top with shallow folds, '
                     'hanging sides with vertical pleats, pointed corners hanging lowest, ragged toothed hem. Double-sided '
                     '(underside 12 mm inside) + hem strip, so no backface holes. Weave/seam painted (Cloth tiling 1.2 m, '
                     'UVs = flat sheet coordinates, no stretching).')
if REND:
    t, l = renders(objs, OUT, 'a', el=24)
    card('34-burgundy-cloth', OUT, t, l, 'AN34 burgundy cloth')
ak.save_blend(os.path.join(D, 'AN34_BurgundyCloth.blend'))
