"""05 Diamond apple — collectible shelf item. Run: python3 build.py [--opt] [--align-only]

Gem body: facet corners are picked on the concept (silhouette corners + the main painted facet corners), lifted to 3D
on a smooth apple surface fitted to the concept silhouette (apple_surface.py, tools/fit_surface.py), the unseen back
gets evenly spaced corners on the same surface, and everything is triangulated as one closed faceted shell (convex
hull of the corner directions around the apple centre). Flat shading, big facets, top dimple, bottom notch, flat base.
Stem = thin bent 5-sided prism, leaf = thin V-folded double-sided plate with a short petiole.
"""
import sys, os, json, math
sys.path.insert(0, '/home/claude/kit')
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import numpy as np
import cv2
from scipy.spatial import ConvexHull
from scipy import ndimage
import bpy
from mathutils import Vector
from geo import Builder, clear_scene, check_fit
import cam as C
import bake_local as BK
import render as R
import apple_surface as S

CONCEPT = '/home/claude/concepts/collectible-concepts/05-diamond-apple-v2.png'
ID = 'CI05_DiamondApple'
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)
CAM0 = json.load(open(os.path.join(HERE, 'camera_concept.json')))   # geometry is built in this camera's space
SP = S.load()
rgb, cmask = BK.load_concept(CONCEPT)
H, W = cmask.shape

# ------------------------------------------------------------------ camera helpers
def ray(u, v, cam=CAM0):
    pos, right, upc, f = C.basis(cam)
    big = max(W, H); fpx = (big / 2) / math.tan(math.radians(cam['fov']) / 2)
    sx, sy = cam.get('shift', [0, 0])
    a = (u - W / 2 - sx * big) / fpx
    b = (H / 2 - sy * big - v) / fpx
    return pos, right * a + upc * b + f          # point(zc) = pos + dir * zc  (zc = depth along f)

def unproject(u, v, zc):
    pos, d = ray(u, v)
    return pos + d * zc

def on_plane(u, v, n, p0):
    pos, d = ray(u, v)
    n = np.asarray(n, float); zc = (n @ (np.asarray(p0) - pos)) / (n @ d)
    return pos + d * zc

# depth buffer of the smooth surface at the concept camera (front-most hit per pixel)
V, TH, PH = S.grid(SP, nt=48, nphi=64)
tris = []
for i in range(V.shape[0] - 1):
    for j in range(V.shape[1]):
        k = (j + 1) % V.shape[1]
        tris += [[V[i, j], V[i, k], V[i + 1, k]], [V[i, j], V[i + 1, k], V[i + 1, j]]]
tris = np.array(tris)
idb, zb = BK.rasterize(tris, np.zeros(len(tris), int), CAM0, W, H)
cov = np.isfinite(zb)
CEN_PX = (744, 590)

def surf_depth(u, v, inset=0):
    """Depth of the smooth surface at pixel (u,v), optionally sampled `inset` px toward the body centre."""
    d = np.array(CEN_PX, float) - (u, v); d /= np.linalg.norm(d)
    for k in range(inset, inset + 80, 2):
        x, y = int(round(u + d[0] * k)), int(round(v + d[1] * k))
        if 0 <= x < W and 0 <= y < H and cov[y, x]:
            return float(zb[y, x])
    raise ValueError((u, v))

# ------------------------------------------------------------------ body corners
SIL = [(715, 239), (655, 221), (565, 215), (501, 234), (433, 279), (348, 412), (331, 550), (404, 755), (494, 875),
       (531, 913), (605, 946), (653, 948), (720, 930), (817, 950), (898, 939), (963, 901), (1020, 835), (1098, 710),
       (1153, 573), (1159, 479), (1155, 423), (1066, 286), (1018, 256), (974, 238), (931, 230), (882, 233),
       (804, 249), (760, 247)]
FRONT = [(498, 322), (575, 300), (600, 357), (735, 400), (403, 522), (885, 413), (995, 293), (800, 360),
         (1090, 410), (1050, 525), (945, 595), (820, 525), (650, 565), (745, 670), (500, 605), (400, 645),
         (450, 720), (510, 692), (660, 735), (545, 835), (800, 790), (900, 750), (1025, 700), (880, 875),
         (700, 860), (1060, 625), (980, 800), (640, 450), (930, 500), (560, 500)]

c3 = np.array([SP['cx'], 0.0, SP['zc']])
pts = []
def limb_point(u, v):
    """Silhouette corner: the point of the pixel ray that lies deepest inside (or closest to) the smooth surface,
    i.e. where the ray grazes the surface -> the vertex sits on the limb, not on the front or back."""
    pos, d = ray(u, v)
    zc0 = float(np.linalg.norm(c3 - pos))
    zs = np.linspace(zc0 - 0.15, zc0 + 0.15, 601)
    Q = pos[None] + d[None] * zs[:, None]
    rel = Q - c3
    rel_s = rel.copy(); rel_s[:, 1] /= SP['ky']
    th = np.arccos(np.clip(-rel_s[:, 2] / np.linalg.norm(rel_s, axis=1), -1, 1)); ph = np.arctan2(rel_s[:, 1], rel_s[:, 0])
    Sp = S.point(SP, th, ph, flatten=False)
    ratio = np.linalg.norm(rel, axis=1) / np.linalg.norm(Sp - c3, axis=1)
    return Q[int(np.argmin(ratio))]

for (u, v) in SIL:
    pts.append(limb_point(u, v))
for (u, v) in FRONT:
    pts.append(unproject(u, v, surf_depth(u, v)))
n_front = len(pts)
# top dimple: pole (stem socket) + small ring; base: flat ring at z=0 + raised notch pole
top_pole = S.point(SP, math.pi, 0.0, flatten=False)
pts.append(top_pole)
for k in range(5):
    pts.append(S.point(SP, math.radians(160), math.radians(-90 + 36 + 72 * k + 9 * math.sin(k * 2.3)), flatten=False))
base_r = 0.031
for k in range(5):
    a = math.radians(90 + 72 * k + 7 * math.cos(k * 1.7))
    pts.append(np.array([SP['cx'] + base_r * math.cos(a) * (1 + 0.08 * math.sin(3 * k)), 0.014 + base_r * math.sin(a), 0.0]))
pts.append(np.array([SP['cx'] - 0.004, 0.012, 0.011]))
# back/hidden corners: evenly spaced directions on the smooth surface, wherever the concept corners leave a gap
dirs_have = [(p - c3) / np.linalg.norm(p - c3) for p in pts]
rng = np.random.default_rng(5)
N = 3000
i = np.arange(N) + 0.5
phi_f = np.arccos(1 - 2 * i / N); th_f = math.pi * (1 + 5 ** 0.5) * i
cand = np.stack([np.cos(th_f) * np.sin(phi_f), np.sin(th_f) * np.sin(phi_f), np.cos(phi_f)], 1)
cand = cand[rng.permutation(N)]
min_ang = math.radians(27)
for d in cand:
    if min(math.acos(np.clip(d @ e, -1, 1)) for e in dirs_have) < min_ang:
        continue
    theta = math.acos(np.clip(-d[2], -1, 1)); phi = math.atan2(d[1], d[0])
    if theta < math.radians(32):      # bottom area is the base ring / notch
        continue
    p = S.point(SP, theta, phi)
    pts.append(p); dirs_have.append((p - c3) / np.linalg.norm(p - c3))
pts = np.array(pts)
D = (pts - c3) / np.linalg.norm(pts - c3, axis=1, keepdims=True)
hull = ConvexHull(D)
faces = []
for s_ in hull.simplices:
    a, b_, c_ = pts[s_]
    n = np.cross(b_ - a, c_ - a)
    if n @ ((a + b_ + c_) / 3 - c3) < 0:
        s_ = s_[::-1]
    faces.append(list(s_))

def merge_flat_pairs(faces, P, max_deg=11.0):
    """Greedy: join two triangles sharing an edge into one quad facet when they are nearly coplanar and the quad is
    convex (bigger gem facets, like the concept)."""
    F = [list(f) for f in faces]
    nrm = []
    for f in F:
        n = np.cross(P[f[1]] - P[f[0]], P[f[2]] - P[f[0]]); nrm.append(n / np.linalg.norm(n))
    edges = {}
    for fi, f in enumerate(F):
        for k in range(3):
            e = tuple(sorted((f[k], f[(k + 1) % 3])))
            edges.setdefault(e, []).append(fi)
    cand = []
    for e, fs in edges.items():
        if len(fs) == 2:
            ang = math.degrees(math.acos(np.clip(nrm[fs[0]] @ nrm[fs[1]], -1, 1)))
            if ang < max_deg:
                cand.append((ang, e, fs))
    cand.sort()
    used = set(); out = []
    for ang, e, (fa, fb) in cand:
        if fa in used or fb in used:
            continue
        a = F[fa]; k = [x for x in a if x not in e][0]
        o = [x for x in F[fb] if x not in e][0]
        i = a.index(k)
        quad = [a[i], a[(i + 1) % 3], o, a[(i + 2) % 3]]
        Q = P[quad]; n = nrm[fa] + nrm[fb]
        conv = all(np.cross(Q[(j + 1) % 4] - Q[j], Q[(j + 2) % 4] - Q[(j + 1) % 4]) @ n > 0 for j in range(4))
        if not conv:
            continue
        used |= {fa, fb}; out.append(quad)
    out += [F[i] for i in range(len(F)) if i not in used]
    return out

faces = merge_flat_pairs(faces, pts)
print('body corners', len(pts), 'front', n_front, 'facets', len(faces))

clear_scene()
b = Builder(ID, seed=5)
b.add_bmesh_geometry('body', [tuple(p) for p in pts], faces)

# ------------------------------------------------------------------ stem (thin bent 5-sided prism)
y_st = top_pole[1]
plane_st = ((0, 1, 0), (0, y_st, 0))
stem_px = [(739, 318), (744, 262), (753, 205), (772, 152), (797, 112), (812, 90)]
stem = [top_pole + np.array([0.0, 0.0, -0.012])] + [on_plane(u, v, *plane_st) for u, v in stem_px]
b.tube('stem', [tuple(p) for p in stem], sides=5, radii=[0.0060, 0.0061, 0.0063, 0.0066, 0.0072, 0.0080, 0.0086])

# ------------------------------------------------------------------ leaf (V-folded double-sided plate) + petiole
B3 = on_plane(845, 180, *plane_st)
# leaf plane: blade faces front-up (readable from above too), tip goes back (dy/dx = 0.45)
n_leaf = np.array([0.405, -0.9, 0.55]); n_leaf /= np.linalg.norm(n_leaf)
pos_cam = C.basis(CAM0)[0]
if n_leaf @ (pos_cam - B3) < 0:
    n_leaf = -n_leaf                          # front side of the leaf faces the camera
up_px = [(845, 180), (868, 125), (905, 85), (950, 68), (1010, 64), (1068, 71)]
lo_px = [(845, 180), (890, 203), (940, 214), (992, 195), (1028, 165), (1048, 122), (1068, 71)]
UP = [on_plane(u, v, n_leaf, B3) for u, v in up_px]
LO = [on_plane(u, v, n_leaf, B3) for u, v in lo_px]
Bp, Tp = UP[0], UP[-1]
axis = (Tp - Bp); L = np.linalg.norm(axis); axis /= L
MID = [Bp + axis * L * s for s in (0.0, 0.3, 0.62, 1.0)]
fold = 0.0065
def s_of(p):
    return float((p - Bp) @ axis / L)
def lift(p, s):
    return p + n_leaf * fold * math.sin(math.pi * min(max(s, 0), 1)) ** 0.8
MIDf = [lift(m, s) for m, s in zip(MID, (0.0, 0.3, 0.62, 1.0))]
th_leaf = 0.0011

def zipper(chainA, chainB):
    """Triangulate between two polylines that share both end points (ordered base -> tip). Returns index triples
    into chainA + chainB (B offset by len(A))."""
    sa = [s_of(p) for p in chainA]; sb = [s_of(p) for p in chainB]
    nA = len(chainA); tri = []
    i, j = 0, 0
    while i < nA - 1 or j < len(chainB) - 1:
        if j >= len(chainB) - 1 or (i < nA - 1 and sa[i + 1] <= sb[j + 1]):
            tri.append((i, i + 1, nA + j)); i += 1
        else:
            tri.append((i, nA + j + 1, nA + j)); j += 1
    return tri

# one surface: midrib (with fold) + upper edge + lower edge; shared ends B and T
verts2d = [m for m in MIDf]                       # 0..3
up_inner = UP[1:-1]; lo_inner = LO[1:-1]
iu0 = len(verts2d); verts2d += up_inner
il0 = len(verts2d); verts2d += lo_inner
mid_idx = [0, 1, 2, 3]
up_chain = [0] + list(range(iu0, iu0 + len(up_inner))) + [3]
lo_chain = [0] + list(range(il0, il0 + len(lo_inner))) + [3]
tris_l = []
for chain, flip in ((up_chain, False), (lo_chain, True)):
    A = [verts2d[k] for k in mid_idx]; Bc = [verts2d[k] for k in chain]
    for (p, q, r) in zipper(A, Bc):
        ids = [(mid_idx + chain)[x] for x in (p, q, r)]
        if len(set(ids)) < 3:
            continue
        tris_l.append(ids)
# orient every triangle so it faces the leaf front (n_leaf)
fv = np.array(verts2d)
for t in tris_l:
    nn = np.cross(fv[t[1]] - fv[t[0]], fv[t[2]] - fv[t[0]])
    if nn @ n_leaf < 0:
        t[1], t[2] = t[2], t[1]
nv = len(verts2d)
top = [p + n_leaf * th_leaf for p in verts2d]
bot = [p - n_leaf * th_leaf for p in verts2d]
lv = top + bot
lf = [list(t) for t in tris_l] + [[t[0] + nv, t[2] + nv, t[1] + nv] for t in tris_l]
rim = up_chain + lo_chain[::-1][1:-1]             # closed outline loop
lc = np.mean(verts2d, axis=0)
for k in range(len(rim)):
    a_, b2 = rim[k], rim[(k + 1) % len(rim)]
    q = [a_, b2, b2 + nv, a_ + nv]
    Q = np.array([lv[x] for x in q])
    nq = np.cross(Q[1] - Q[0], Q[3] - Q[0])
    if nq @ (Q.mean(0) - lc) < 0:
        q = q[::-1]
    lf.append(q)
b.add_bmesh_geometry('leaf', [tuple(p) for p in lv], lf)
# petiole: from inside the stem to the leaf base
P0 = on_plane(788, 228, *plane_st)
P1 = on_plane(818, 203, *plane_st)
b.tube('leaf', [tuple(P0), tuple(P1), tuple(Bp + axis * 0.004)], radii=[0.0032, 0.0030, 0.0026], sides=4)

# ------------------------------------------------------------------ finish + camera in item space
xs = [v.co.x for v in b.bm.verts]; ys_ = [v.co.y for v in b.bm.verts]; zs = [v.co.z for v in b.bm.verts]
shift = np.array([-(min(xs) + max(xs)) / 2, -(min(ys_) + max(ys_)) / 2, -min(zs)])
obj = b.finish()
fit = check_fit(obj)
print('fit', fit)
cam0 = dict(CAM0); cam0['target'] = list(np.array(CAM0['target']) + shift)
cam = cam0                                     # the geometry is fitted in this camera's space: no optimizing needed
if '--opt' in sys.argv:
    cam, s = BK.optimize_camera(obj, CONCEPT, cam0, keys=('az', 'dist', 'shift'))
cam['target'] = [float(x) for x in cam['target']]
json.dump(cam, open(os.path.join(HERE, 'camera.json'), 'w'), indent=1)
if '--dbg' in sys.argv:                         # visible back faces (folds / flipped faces) from 8 directions
    P, UV, part, N = BK.mesh_arrays(obj)
    import render_quick
    idb_ = render_quick.zraster(P, cam, W // 2, H // 2)
    pos = C.basis(cam)[0]
    ids, cnt = np.unique(idb_[idb_ >= 0], return_counts=True)
    for fi, n_ in zip(ids, cnt):
        fc = N[fi] @ (pos - P[fi].mean(0)) / np.linalg.norm(pos - P[fi].mean(0))
        if fc < 0.1:
            print('concept-cam low facing tri', fi, obj['parts'][part[fi]], n_, round(float(fc), 3), np.round(C.project(P[fi].mean(0)[None], cam, W, H)[0:2], 0).ravel())
    for az in range(0, 360, 45):
        for el in (15, 50):
            c = {'az': az, 'el': el, 'dist': 1.0, 'fov': 18, 'target': [0, 0, 0.1], 'shift': [0, 0]}
            import render_quick; idb_ = render_quick.zraster(P, c, 600, 600)
            pos = C.basis(c)[0]
            ids, cnt = np.unique(idb_[idb_ >= 0], return_counts=True)
            for fi, n_ in zip(ids, cnt):
                if N[fi] @ (pos - P[fi].mean(0)) < 0 and n_ > 8:
                    print('backface az', az, 'el', el, 'tri', fi, obj['parts'][part[fi]], n_, np.round(P[fi].mean(0), 3))
    sys.exit()
if '--quick' in sys.argv:                       # cheap untextured turnaround (workbench) for shaping
    import render_quick; render_quick.turnaround(obj, os.path.join(HERE, 'tools', 'quick.png'), cam)
    sys.exit()
BK.unwrap(obj)
iou = BK.align_report(obj, CONCEPT, cam, os.path.join(OUT, 'align.png'))
print('IoU', iou, cam)
if '--align-only' in sys.argv:
    sys.exit()

# ------------------------------------------------------------------ bake (concept projection + gem facets on hidden sides)
def T_reflect(n, p0):
    n = np.asarray(n, float); n = n / np.linalg.norm(n)
    T = np.eye(4); T[:3, :3] -= 2 * np.outer(n, n); T[:3, 3] = 2 * (n @ np.asarray(p0, float)) * n
    return T

size = BK.texture_size_for(obj)
# the concept view may reach facets down to facing 0.06 (the yellow-green left shoulder is a near-silhouette facet);
# texels of facets below facing 0.3 are not used as projected pixels but averaged to one facet tone (gem_facets)
views = [{'name': 'orig', 'T': np.eye(4), 'bonus': 2.0, 'min_facing': 0.06}]   # concept always wins where valid
views += [{'name': f'rot{a}', 'T': BK.T_rot_z(a), 'parts': ['body'], 'bonus': -0.05 * abs(a) / 180}
          for a in (55, -55, 110, -110, 160, -160)]
views.append({'name': 'stem_back', 'T': T_reflect((0, 1, 0), (0, y_st + shift[1], 0)), 'parts': ['stem']})
views.append({'name': 'leaf_under', 'T': T_reflect(n_leaf, B3 + shift), 'parts': ['leaf']})
me = obj.data; me.calc_loop_triangles()
tri2poly = np.array([t.polygon_index for t in me.loop_triangles])
PARTS = list(obj['parts'])

# per-facet facing toward the concept camera (polygon normal at polygon centre)
_pos = np.array(C.basis(cam)[0])
POLY_FACING = np.array([float(np.dot(p.normal[:], (_pos - np.array(p.center[:])) / np.linalg.norm(_pos - np.array(p.center[:]))))
                        for p in me.polygons])
_rng = np.random.default_rng(11)
_blk = np.kron(_rng.random((size // 3 + 2, size // 3 + 2)), np.ones((3, 3)))[:size, :size].astype(np.float32)

def gem_facets(tex, have, ys, xs, t, src_view, tpart):
    """Texels that are not a clean concept projection get one tone per facet (like the concept's cut facets):
    - facets grazing at the concept camera (facing < 0.3): the facet's mean CONCEPT colour (keeps the yellow-green
      shoulder) with a small blocky variation instead of the streaky projection;
    - hidden body facets (rotated views): facet tone + half of the projected brush texture."""
    global SRCMAP
    SRCMAP = np.zeros((tex.shape[0], tex.shape[1], 3), np.uint8)
    pal = np.array([[255, 255, 0], [0, 0, 255], [0, 128, 255], [0, 200, 0], [0, 120, 0], [200, 0, 200], [120, 0, 120], [255, 128, 0], [0, 255, 255]])
    SRCMAP[ys, xs] = np.where((src_view >= 0)[:, None], pal[np.maximum(src_view, 0) % len(pal)], [255, 0, 0])
    poly = tri2poly[t]
    kb = PARTS.index('body')
    orig = src_view == 0

    def view_detail(tex, ii):
        """Brush detail of texels ii relative to the mean of their own source view (no tone step where two rotated
        views meet inside one facet)."""
        vr = tex[ys[ii], xs[ii]].astype(np.float32)
        out = np.zeros_like(vr)
        for sv in np.unique(src_view[ii]):
            m = src_view[ii] == sv
            out[m] = vr[m] - vr[m].mean(0)
        return np.clip(out, -22, 22)          # brush grain only: no transplanted highlight shapes
    order = np.argsort(poly, kind='stable')
    bounds = np.searchsorted(poly[order], np.arange(len(me.polygons) + 1))
    for pid in range(len(me.polygons)):
        idx = order[bounds[pid]:bounds[pid + 1]]
        if len(idx) == 0:
            continue
        is_body = tpart[idx[0]] == kb
        io = idx[orig[idx]]; ir = idx[~orig[idx]]
        grazing = POLY_FACING[pid] < 0.3 and len(io) > 0.25 * len(idx)
        if grazing:
            base = tex[ys[io], xs[io]].astype(np.float32).mean(0)
            var = 1 + 0.16 * (_blk[ys[idx], xs[idx]] - 0.5)
            col = base[None] * var[:, None]
            if len(ir) and is_body:
                col[~orig[idx]] += 0.4 * view_detail(tex, ir)
            tex[ys[idx], xs[idx]] = col
        elif len(ir) and is_body:
            vr = tex[ys[ir], xs[ir]].astype(np.float32)
            base = tex[ys[io], xs[io]].mean(0) if len(io) > 0.35 * len(idx) else np.median(vr, 0)
            tex[ys[ir], xs[ir]] = base + 0.5 * view_detail(tex, ir)
    # per-part palette (PSX look) instead of one global palette that greys out the pinks / yellow-greens
    crit = (cv2.TERM_CRITERIA_EPS + cv2.TERM_CRITERIA_MAX_ITER, 30, 0.5)
    cv2.setRNGSeed(5)
    for k, pname in enumerate(PARTS):
        m = tpart == k
        if m.sum() < 64:
            continue
        cols = np.clip(tex[ys[m], xs[m]], 0, 255).astype(np.float32)
        kk = {'body': 48, 'stem': 14, 'leaf': 14}.get(pname, 12)
        _, lab, cen = cv2.kmeans(cols, kk, None, crit, 2, cv2.KMEANS_PP_CENTERS)
        tex[ys[m], xs[m]] = cen[lab.ravel()]
    return tex

tex_path = os.path.join(OUT, f'T_{ID}.png')
tex, st = BK.bake(obj, CONCEPT, cam, views=views, size=size, out_png=tex_path, debug_png=os.path.join(OUT, 'coverage.png'),
                  part_colors={'stem': [(96, 52, 22), (140, 78, 32), (176, 108, 48), (205, 140, 70)],
                               'leaf': [(88, 98, 30), (118, 128, 40), (150, 156, 58), (178, 180, 80)]},
                  post=gem_facets, quantize=0)
print('bake', json.dumps(st))
if '--texcheck' in sys.argv:
    import render_quick
    im = render_quick.textured(obj, tex, cam, W // 2, H // 2)
    im2 = render_quick.textured(obj, SRCMAP, cam, W // 2, H // 2)
    cv2.imwrite(os.path.join(HERE, 'tools', 'srccheck.png'), cv2.cvtColor(im2, cv2.COLOR_RGB2BGR))
    cv2.imwrite(os.path.join(HERE, 'tools', 'texcheck.png'), np.concatenate(
        [cv2.cvtColor(im, cv2.COLOR_RGB2BGR), cv2.resize(cv2.cvtColor(rgb, cv2.COLOR_RGB2BGR), (W // 2, H // 2))], 1))
    sys.exit()
R.apply_texture(obj, tex_path, ID)
stats = check_fit(obj)
stats['texture'] = f'{size}x{size}'
stats['iou'] = round(iou, 4)
R.render_views(obj, os.path.join(OUT, 'preview.png'), concept_cam=cam, concept_size=(1536, 1024), concept_path=CONCEPT)
R.shelf_fit_render(obj, os.path.join(OUT, 'shelf.png'))
R.export(obj, OUT, ID, tex_path)
json.dump({'id': ID, 'stats': stats, 'bake': st, 'camera': cam}, open(os.path.join(OUT, 'stats.json'), 'w'), indent=1)
R.card(OUT, ID, stats)
print('done', stats)
