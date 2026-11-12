"""16 Antique magnifying glass - collectible shelf item. Run: python3 build.py [--opt] [--align-only]

Resting pose: the glass LIES on the shelf, lens up, slightly tilted (the far rim of the frame and the brass end cap
touch the pad; the thick handle lifts the ring by ~2 deg). The concept is a high three-quarter view of exactly this.

Masses (local frame: u = along the handle from the lens centre, v = across, w = up from the base plane):
- brass frame: closed 12-sided ring with real depth (outer wall, flat top band, inner wall down to a recessed lens),
  outer 12-gon traced from the concept (crooked as drawn), inner opening a 24-gon
- smoky glass lens: slab with a faceted bevel ring and a low dome, recessed below the band top
- brass yoke: tapered hexagonal block from the ring to the neck (engraving painted)
- brass neck ring + collar disc, tapered 8-sided wooden handle, brass end cap with chamfered end face
Geometry positions come from concept pixels back-projected through the design camera CAM0 onto the part planes.
Painted (concept projection): brass mottling, leaf engraving on the yoke, wood grain, glass highlight and bevel.
"""
import sys, os, json, math
sys.path.insert(0, '/home/claude/kit')
HERE = os.path.dirname(os.path.abspath(__file__))
import numpy as np
import cv2
from scipy import ndimage
import bpy, bmesh
from mathutils import Vector, Matrix
from geo import Builder, clear_scene, check_fit
sys.path.insert(0, HERE)
import bake_local as BK          # kit copy with a true z-buffer (recessed lens inside the ring)
import render as R
import cam as C

CONCEPT_PNG = '/home/claude/concepts/collectible-concepts/16-antique-magnifying-glass-v1.png'
CONCEPT = os.path.join(HERE, 'concept_rgba.png')   # same pixels + alpha (lens hole filled: grey glass ~ background)
ID = 'CI16_AntiqueMagnifyingGlass'
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)
WORK = os.path.join(HERE, 'tmp'); os.makedirs(WORK, exist_ok=True)
CW, CH = 1536, 1024

CAM0 = {'az': 0.0, 'el': 62.0, 'dist': 0.745, 'fov': 18.0, 'target': [0.0, 0.0, 0.008], 'shift': [0.0, 0.0], 'roll': 0.0}

# ------------------------------------------------------------------ dimensions (metres, local frame)
H_RING = 0.0155          # frame depth
FLARE = 0.0004           # outer wall leans out toward the bottom
SHEAR_W = (-0.0016, 0.0007)   # world xy offset of the frame bottom vs its top (the concept's walls lean a bit left)
BAND = 0.0056            # top band width
LENS_TOP = H_RING - 0.0098
LENS_BOT = 0.0025
W_AXIS = H_RING * 0.5    # handle axis height above the base plane

# traced outer top-face vertices of the frame (concept px)
RING_PX = [(435, 19), (593, 56), (705, 162), (752, 281), (742, 398), None, (530, 592), (380, 617), (240, 567),
           (125, 442), (91, 317), (140, 177), (267, 67)]
RING_C_PX = (423, 318)          # top-face centre
CAP_END_PX = (1262, 900)        # centre of the end cap face (axis)
COLLAR_C_PX = (788, 594)        # centre of the collar disc (axis)

# handle stations along the axis are measured in the concept (see u_of_px below); radii in metres
YOKE_W = (0.048, 0.030)
FEAT_PX = {'neck0': [(700, 572)], 'neck1': [(737, 584)], 'coll1': [(803, 598)],
           'kink': [(1079, 638), (983, 860)], 'cap0': [(1263, 770), (1158, 950)], 'cap1': [CAP_END_PX]}


def frame_from(yaw, tilt, origin):
    """local (u, v, w) -> world. u axis = handle direction (yaw from +x), tilt raises +u side by `tilt` rad about the
    far rim (origin = world position of local (0,0,0))."""
    Rz = Matrix.Rotation(yaw, 4, 'Z')
    Ry = Matrix.Rotation(-tilt, 4, 'Y')      # +u goes up
    return Matrix.Translation(origin) @ Rz @ Ry


def bp_plane(px, n, c, cam=CAM0):
    """Concept pixel -> 3D point on the plane n.p = c (ray through the design camera)."""
    pos, right, upc, f = C.basis(cam)
    big = max(CW, CH)
    fpx = (big / 2) / math.tan(math.radians(cam['fov']) / 2)
    sx, sy = cam.get('shift', [0, 0])
    u, v = px
    a = (u - CW / 2 - sx * big) / fpx
    b_ = (CH / 2 - sy * big - v) / fpx
    d = f + right * a + upc * b_
    n = np.asarray(n, float)
    t = (c - n @ pos) / (n @ d)
    return pos + d * t


def solve_frame(tilt):
    """Local frame from the concept: u axis = handle axis through the collar and cap centres (on the axis plane),
    origin = lens centre projected onto that axis (the lens centre itself may sit slightly off the axis)."""
    yaw, o = 0.0, bp_plane(RING_C_PX, (0, 0, 1), H_RING) - np.array([0, 0, H_RING])
    for _ in range(6):
        M = frame_from(yaw, tilt, Vector(o))
        n = np.array((M.to_3x3() @ Vector((0, 0, 1))).normalized())
        ev = np.array((M.to_3x3() @ Vector((0, 1, 0))).normalized())
        ctop = bp_plane(RING_C_PX, n, float(n @ o) + H_RING)
        Mi = M.inverted()
        c_loc = np.array(Mi @ Vector(ctop))
        a = np.array(Mi @ Vector(bp_plane(COLLAR_C_PX, n, float(n @ o) + W_AXIS)))
        e = np.array(Mi @ Vector(bp_plane(CAP_END_PX, n, float(n @ o) + W_AXIS)))
        yaw += math.atan2(e[1] - a[1], e[0] - a[0])
        # move the origin to the foot of the lens centre on the axis line, keep it on the base plane
        t = (c_loc[0] - a[0])
        v_axis = a[1] + (e[1] - a[1]) * t / (e[0] - a[0])
        o = np.array(M @ Vector((c_loc[0], v_axis, 0.0)))
    M = frame_from(yaw, tilt, Vector(o))
    Mi = M.inverted()
    n = np.array((M.to_3x3() @ Vector((0, 0, 1))).normalized())
    e = np.array(Mi @ Vector(bp_plane(CAP_END_PX, n, float(n @ o) + W_AXIS)))
    a = np.array(Mi @ Vector(bp_plane(COLLAR_C_PX, n, float(n @ o) + W_AXIS)))
    c = np.array(Mi @ Vector(bp_plane(RING_C_PX, n, float(n @ o) + H_RING)))
    return M, n, o, (a, e, c)


# tilt so that the far rim and the handle's lowest flat both touch the pad
TILT = math.atan2(0.0162 * math.cos(math.pi / 8) - W_AXIS, 0.125 + 0.052)   # handle's widest flat vs far rim
M, NRM, ORG, (A_LOC, E_LOC, C_LOC) = solve_frame(TILT)
Minv = M.inverted()
print('frame: yaw %.1f deg  tilt %.2f deg' % (math.degrees(math.atan2(M[1][0], M[0][0])), math.degrees(TILT)),
      'collar', A_LOC.round(4), 'cap end', E_LOC.round(4), 'lens centre', C_LOC.round(4))


def to_local(p):
    return np.array(Minv @ Vector(p))


def u_of_px(pxs):
    """Mean local u of concept pixels back-projected onto the handle-axis plane (edge pairs cancel the offset)."""
    return float(np.mean([to_local(bp_plane(p, NRM, float(NRM @ ORG) + W_AXIS))[0] for p in pxs]))


U = {k: u_of_px(v) for k, v in FEAT_PX.items()}
print('stations', {k: round(v, 4) for k, v in U.items()})
U_YOKE0, U_YOKE1 = 0.040, U['neck0'] + 0.0016
YOKE_H = ((0.0, H_RING), (W_AXIS - 0.0085, W_AXIS + 0.0085))
NECK = [(U['neck0'], 0.0080), (U['neck1'], 0.0080)]
COLLAR = [(U['neck1'] - 0.0004, 0.0098), (U['neck1'] + 0.0010, 0.0121), (U['coll1'] - 0.0010, 0.0123), (U['coll1'], 0.0104)]
HANDLE = [(U['coll1'] - 0.0010, 0.0104), (U['kink'], 0.0161), (U['cap0'] + 0.0012, 0.0162)]
CAP = [(U['cap0'], 0.0164), (U['cap0'] + 0.0012, 0.0168), (U['cap1'] - 0.0016, 0.0168), (U['cap1'], 0.0152), (U['cap1'] + 0.0002, 0.0)]


# ------------------------------------------------------------------ outer ring polygon (local), around the lens centre
CU, CV = float(C_LOC[0]), float(C_LOC[1])
ring_loc = []
for px in RING_PX:
    if px is None:
        ring_loc.append(None)
        continue
    q = to_local(bp_plane(px, NRM, float(NRM @ ORG) + H_RING))
    ring_loc.append((math.atan2(q[1] - CV, q[0] - CU), math.hypot(q[0] - CU, q[1] - CV)))
# fill hidden vertices: mean angle of neighbours, mean radius
for i, r in enumerate(ring_loc):
    if r is None:
        a0, r0 = ring_loc[i - 1]; a1, r1 = ring_loc[(i + 1) % len(ring_loc)]
        da = (a1 - a0 + math.pi) % (2 * math.pi) - math.pi
        ring_loc[i] = (a0 + da / 2, (r0 + r1) / 2)
ring_loc.sort(key=lambda t: t[0])          # counter-clockwise seen from above
print('ring radii', [round(r, 4) for a, r in ring_loc], 'angles', [round(math.degrees(a)) for a, r in ring_loc])
R_MEAN = float(np.mean([r for a, r in ring_loc]))
NC = len(ring_loc)
NR = 2 * NC

clear_scene()
b = Builder(ID, seed=16)
bm = b.bm
LOCAL_VERTS = []   # verts built in local coords, transformed by M at the end


def V(p):
    v = bm.verts.new(p)
    LOCAL_VERTS.append(v)
    return v


def poly_ring(inset, w):
    """NR points: the traced corners + edge midpoints, offset inward by `inset` (parallel-ish offset)."""
    pts = []
    k_corner = 1.0 / math.cos(math.pi / NC)
    for i in range(NC):
        a0, r0 = ring_loc[i]; a1, r1 = ring_loc[(i + 1) % NC]
        p0 = np.array([math.cos(a0) * r0, math.sin(a0) * r0])
        p1 = np.array([math.cos(a1) * r1, math.sin(a1) * r1])
        for p, kk in ((p0, k_corner), ((p0 + p1) / 2, 1.0)):
            r = float(np.hypot(*p))
            s = (r - inset * kk) / r
            pts.append((CU + p[0] * s, CV + p[1] * s, w))
    return pts


def circle_ring(radius, w):
    pts = []
    for i in range(NC):
        a0, _ = ring_loc[i]; a1, _ = ring_loc[(i + 1) % NC]
        da = (a1 - a0) % (2 * math.pi)
        for ang in (a0, a0 + da / 2):
            pts.append((CU + math.cos(ang) * radius, CV + math.sin(ang) * radius, w))
    return pts


def loop_loft(part, rings):
    """Closed loop of rings (cross-section loop) -> torus-like closed shell."""
    mi = b.part(part)
    rv = [[V(p) for p in r] for r in rings]
    for k in range(len(rv)):
        b.bridge(rv[k], rv[(k + 1) % len(rv)], mi)
    return rv


# --- brass frame: outer bottom -> outer top -> inner top -> inner wall foot (lens) -> under the lens -> inner bottom
frame_rings = [poly_ring(-FLARE, 0.0), poly_ring(0.0, H_RING), poly_ring(BAND, H_RING),
               poly_ring(BAND, LENS_TOP), poly_ring(BAND, LENS_BOT), poly_ring(BAND - 0.0008, 0.0)]
loop_loft('frame', frame_rings)

# --- lens: rim (just inside the wall) + faceted bevel ring + low dome on top; flat bottom fan
mi_g = b.part('glass')
R_BEV = (R_MEAN - BAND) * 0.84
rim_t = [V(p) for p in poly_ring(BAND + 0.0002, LENS_TOP - 0.0002)]
bev_t = [V(p) for p in circle_ring(R_BEV, LENS_TOP + 0.0010)]
rim_b = [V(p) for p in poly_ring(BAND + 0.0002, LENS_BOT + 0.0002)]
b.bridge(rim_b, rim_t, mi_g)
b.bridge(rim_t, bev_t, mi_g)
ct = V((CU, CV, LENS_TOP + 0.0019))
for i in range(NR):
    b._face([bev_t[i], bev_t[(i + 1) % NR], ct], mi_g)
cb = V((CU, CV, LENS_BOT - 0.0003))
for i in range(NR):
    b._face([rim_b[(i + 1) % NR], rim_b[i], cb], mi_g)


FRAME_SET = set(LOCAL_VERTS)     # frame + lens verts (sheared below)

# --- along-axis parts: lathe in local coords around the u axis at height W_AXIS
def axis_lathe(part, prof, sides, rot=0.0, cap0=True, cap1=True, scale=(1, 1)):
    mi = b.part(part)
    rings = []
    for u, r in prof:
        if r <= 1e-6:
            rings.append([V((u, 0.0, W_AXIS))])
            continue
        pts = []
        for k in range(sides):
            a = rot + 2 * math.pi * k / sides
            pts.append(V((u, math.cos(a) * r * scale[0], W_AXIS + math.sin(a) * r * scale[1])))
        rings.append(pts)
    for r0, r1 in zip(rings, rings[1:]):
        if len(r1) == 1:
            for i in range(len(r0)):
                b._face([r0[i], r0[(i + 1) % len(r0)], r1[0]], mi)
        else:
            b.bridge(r0, r1, mi)
    if cap0 and len(rings[0]) > 1:
        b._face(list(reversed(rings[0])), mi)
    if cap1 and len(rings[-1]) > 1:
        b._face(rings[-1], mi)
    return rings


# yoke: hexagonal cross-section (flat top + bottom, pointed sides) tapering toward the neck
def yoke_sec(u, wdt, w0, w1, pinch=0.62):
    wm = (w0 + w1) / 2
    hw = wdt / 2
    return [(u, -hw, wm), (u, -hw * pinch, w0), (u, hw * pinch, w0), (u, hw, wm), (u, hw * pinch, w1), (u, -hw * pinch, w1)]


mi_y = b.part('yoke')
# start section sits inside the frame wall, following its curvature (never pokes into the lens opening)
R_YK = R_MEAN - BAND * 0.45
sec0 = [(CU + math.sqrt(max(R_YK ** 2 - (v - CV) ** 2, 0.0)), v, w) for _, v, w in yoke_sec(0.0, YOKE_W[0], *YOKE_H[0])]
secs = [sec0, yoke_sec(U_YOKE1, YOKE_W[1], *YOKE_H[1])]
yr = [[V(p) for p in s] for s in secs]
b.bridge(yr[0], yr[1], mi_y)
b._face(list(reversed(yr[0])), mi_y)
b._face(yr[1], mi_y)

axis_lathe('neck', NECK, 8, rot=math.pi / 8)
axis_lathe('collar', COLLAR, 10, rot=math.pi / 10)
axis_lathe('handle', HANDLE, 8, rot=math.pi / 8)
axis_lathe('cap', CAP, 8, rot=math.pi / 8, cap1=False)

# ------------------------------------------------------------------ local -> world, ground
for v in LOCAL_VERTS:
    w = v.co.z
    v.co = M @ v.co
    if v in FRAME_SET:
        k = 1.0 - min(max(w / H_RING, 0.0), 1.0)
        v.co.x += SHEAR_W[0] * k; v.co.y += SHEAR_W[1] * k
bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4], quad_method='BEAUTY', ngon_method='BEAUTY')


# ------------------------------------------------------------------ resting pose: stand on a hull facet
def stable_facets(Vw, P_tris):
    """Convex-hull facets that the item can rest on (centre of mass projects inside), with margins."""
    from scipy.spatial import ConvexHull
    vol = np.einsum('ij,ij->i', P_tris[:, 0], np.cross(P_tris[:, 1], P_tris[:, 2])) / 6
    com = (vol[:, None] * (P_tris.sum(1) / 4)).sum(0) / vol.sum()
    h = ConvexHull(Vw)
    groups = {}
    for eq, sm in zip(h.equations, h.simplices):
        groups.setdefault(tuple(np.round(eq, 3)), []).append(sm)
    out = []
    for k, ss in groups.items():
        n = np.array(k[:3]); n /= np.linalg.norm(n)
        pts = Vw[np.unique(np.concatenate(ss))]
        a = np.cross(n, [0, 0, 1] if abs(n[2]) < .9 else [1, 0, 0]); a /= np.linalg.norm(a); b2 = np.cross(n, a)
        p2 = np.c_[pts @ a, pts @ b2]; c2 = np.array([com @ a, com @ b2])
        try:
            hh = ConvexHull(p2)
        except Exception:
            continue
        margin = -max(e[:2] @ c2 + e[2] for e in hh.equations)
        if margin > 0:
            out.append((margin, n))
    return out, com


bm.verts.ensure_lookup_table()
_Vw = np.array([v.co[:] for v in bm.verts])
_P = np.array([[v.co[:] for v in f.verts] for f in bm.faces if len(f.verts) == 3])
_quads = [f for f in bm.faces if len(f.verts) == 4]
for f in _quads:
    q = [v.co[:] for v in f.verts]
    _P = np.concatenate([_P, np.array([[q[0], q[1], q[2]], [q[0], q[2], q[3]]])])
facets, COM = stable_facets(_Vw, _P)
LENS_N = np.array(M.to_3x3() @ Vector((0, 0, 1)))
HANDLE_D = np.array(M.to_3x3() @ Vector((1, 0, 0)))
# standing on the rim + cap ("handle down, lens rim touching"): facet normal ~ in the lens plane, handle on the
# viewer's right when the painted lens side faces the viewer (as in the concept)
cands = [(m, n) for m, n in facets if abs(n @ LENS_N) < 0.2 and np.cross(LENS_N, n) @ HANDLE_D > 0.3]
print('stable facets', [(round(m * 1000, 1), n.round(3)) for m, n in facets], 'chosen', cands[0][1].round(3) if cands else None)
DOWN = max(cands, key=lambda t: t[0])[1]
POSE_MARGIN = max(cands, key=lambda t: t[0])[0]
q1 = Vector(DOWN).rotation_difference(Vector((0, 0, -1))).to_matrix().to_4x4()
l1 = np.array(q1.to_3x3() @ Vector(LENS_N))
yaw2 = math.atan2(-1.0, 0.0) - math.atan2(l1[1], l1[0])        # lens painted side -> -Y (shelf front)
Q3 = Matrix.Rotation(yaw2, 4, 'Z') @ q1
PIV = Vector(CAM0['target'])
QP = Matrix.Translation(PIV) @ Q3 @ Matrix.Translation(-PIV)
for v in bm.verts:
    v.co = QP @ v.co


def rotate_cam(c, Q4):
    """Camera dict moved rigidly with the object (Q4 4x4 world rotation about a point)."""
    pos, right, upc, f = C.basis(c)
    R3 = np.array(Q4.to_3x3())
    t = np.array(Q4 @ Vector(c['target']))
    p = np.array(Q4 @ Vector(pos)); rr = R3 @ right
    d = (p - t) / np.linalg.norm(p - t)
    c2 = dict(c); c2['target'] = [float(x) for x in t]; c2['el'] = math.degrees(math.asin(d[2])); c2['az'] = math.degrees(math.atan2(d[0], -d[1]))
    c2['roll'] = 0.0
    _, r0, u0, _ = C.basis(c2)
    c2['roll'] = math.degrees(math.atan2(rr @ u0, rr @ r0))
    return c2


CAM0 = rotate_cam(CAM0, QP)
print('pose margin %.1f mm, concept camera after pose' % (POSE_MARGIN * 1000), {k: (round(v, 2) if isinstance(v, float) else v) for k, v in CAM0.items()})
_co = np.array([v.co[:] for v in bm.verts])
SHIFT = np.array([-(_co[:, 0].min() + _co[:, 0].max()) / 2, -(_co[:, 1].min() + _co[:, 1].max()) / 2, -_co[:, 2].min()])
obj = b.finish()
fit = check_fit(obj)
print('fit', fit)
PARTS = list(obj['parts'])

BK.unwrap(obj)
cam_path = os.path.join(HERE, 'camera.json')
cam0 = json.loads(json.dumps(CAM0)); cam0['target'] = [float(x) for x in np.array(CAM0['target']) + SHIFT]
cam = json.load(open(cam_path)) if os.path.exists(cam_path) else cam0
if '--cam0' in sys.argv:          # the design camera (geometry is back-projected through it) is the concept camera
    cam = cam0
    json.dump(cam, open(cam_path, 'w'), indent=1)
if '--opt' in sys.argv:
    cam, s_ = BK.optimize_camera(obj, CONCEPT, cam0, keys=('dist', 'shift'))
    json.dump(cam, open(cam_path, 'w'), indent=1)
print('IoU', BK.align_report(obj, CONCEPT, cam, os.path.join(OUT, 'align.png')), cam)


def edge_overlay(obj, cam, out_png):
    """Concept with the model's visible hard edges drawn on top (inner edges the silhouette IoU cannot see)."""
    me = obj.data
    Vv = np.array([v.co[:] for v in me.vertices])
    u, v, _ = C.project(Vv, cam, CW, CH)
    im = cv2.imread(CONCEPT_PNG)
    P, UV, part, N = BK.mesh_arrays(obj)
    idb, zb = BK.rasterize(P, part, cam, CW, CH)
    fn = {p.index: p.normal for p in me.polygons}
    ek = {}
    for p in me.polygons:
        for k in p.edge_keys:
            ek.setdefault(k, []).append(p.index)
    for k, fs in ek.items():
        if len(fs) == 2 and fn[fs[0]].dot(fn[fs[1]]) > 0.985:
            continue
        a, c = k
        mid = (Vv[a] + Vv[c]) / 2
        mu, mv, mz = C.project(mid[None], cam, CW, CH)
        iu, iv = int(np.clip(mu[0], 0, CW - 1)), int(np.clip(mv[0], 0, CH - 1))
        if mz[0] > zb[iv, iu] + 0.002:
            continue
        cv2.line(im, (int(u[a]), int(v[a])), (int(u[c]), int(v[c])), (60, 255, 60), 1, cv2.LINE_AA)
    cv2.imwrite(out_png, im)


if '--align-only' in sys.argv:
    edge_overlay(obj, cam, os.path.join(WORK, 'edges.png'))
    sys.exit()

# ------------------------------------------------------------------ bake
MW = Matrix.Translation(Vector(SHIFT)) @ QP @ M          # local -> final world (ignores the 1-2 mm frame shear)
MWi = MW.inverted()


def T_local(L):
    return np.array(MW @ L @ MWi)


def rot_lens(deg):
    return T_local(Matrix.Translation((CU, CV, 0)) @ Matrix.Rotation(math.radians(deg), 4, 'Z') @ Matrix.Translation((-CU, -CV, 0)))


def rot_axis(deg):
    return T_local(Matrix.Translation((0, 0, W_AXIS)) @ Matrix.Rotation(math.radians(deg), 4, 'X') @ Matrix.Translation((0, 0, -W_AXIS)))


def mirror_v():
    return T_local(Matrix.Diagonal((1, -1, 1, 1)))


def flip_w(h):
    return T_local(Matrix.Translation((0, 0, h)) @ Matrix.Diagonal((1, 1, -1, 1)))


LATHE = ['neck', 'collar', 'handle', 'cap']
views = [{'name': 'orig', 'T': np.eye(4), 'bonus': 0.3}]
# end faces of the cap/collar and the yoke's near side are seen at a grazing angle but carry the painted detail
views += [{'name': 'orig_grazing', 'T': np.eye(4), 'parts': ['cap', 'collar', 'neck', 'yoke'], 'min_facing': 0.12, 'bonus': 0.2}]
views += [{'name': f'lens{a}', 'T': rot_lens(a), 'parts': ['frame'], 'bonus': -0.05 * abs(a) / 180}
          for a in (28, -28, 55, -55, 83, -83, 110, -110, 138, -138, 165, -165)]
views += [{'name': f'axis{a}', 'T': rot_axis(a), 'parts': LATHE, 'bonus': -0.05 * abs(a) / 180}
          for a in (45, -45, 90, -90, 135, -135, 180)]
views += [{'name': 'yoke_mirror', 'T': mirror_v(), 'parts': ['yoke'], 'bonus': 0.0}]
views += [{'name': 'frame_flip', 'T': flip_w(H_RING), 'parts': ['frame'], 'bonus': -0.1},
          {'name': 'glass_flip', 'T': flip_w(LENS_TOP + LENS_BOT), 'parts': ['glass'], 'bonus': -0.1}]

size = BK.texture_size_for(obj)
tex_path = os.path.join(OUT, f'T_{ID}.png')
tex, st = BK.bake(obj, CONCEPT, cam, views=views, size=size, out_png=tex_path,
                  debug_png=os.path.join(OUT, 'coverage.png'), min_facing=0.3)
print('bake', json.dumps(st))


def paint_engraving(path):
    """Leaf engraving on the yoke, drawn at texel scale (the projection smears it into mottling at item density).
    Top face: three-leaf fan pointing back to the ring (as in the concept); both side chamfers: a spray of leaves."""
    tex = cv2.cvtColor(cv2.imread(path), cv2.COLOR_BGR2RGB)
    S = tex.shape[0]
    P, UV, part, N = BK.mesh_arrays(obj)
    yk = PARTS.index('yoke')
    Ml = np.array(MWi)
    DARK = np.array([78, 55, 35]); DEEP = np.array([58, 40, 26])

    def leaf(x, y, bx, by, ang, L, W):
        a = (x - bx) * math.cos(ang) + (y - by) * math.sin(ang)
        b = -(x - bx) * math.sin(ang) + (y - by) * math.cos(ang)
        a_ = np.clip(a / L, 0, 1)
        prof = W * np.sin(np.pi * a_) ** 0.7 * (0.55 + 0.45 * a_)        # teardrop, fat toward the tip
        return (a > 0) & (a < L) & (np.abs(b) < prof)

    for ti in np.nonzero(part == yk)[0]:
        n_loc = Ml[:3, :3] @ N[ti]
        uvp = UV[ti] * S; uvp[:, 1] = S - uvp[:, 1]
        x0, y0 = np.floor(uvp.min(0)).astype(int); x1, y1 = np.ceil(uvp.max(0)).astype(int)
        X, Y = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        (ua, va), (ub, vb), (uc, vc) = uvp
        den = (vb - vc) * (ua - uc) + (uc - ub) * (va - vc)
        if abs(den) < 1e-12:
            continue
        l0 = ((vb - vc) * (X - uc) + (uc - ub) * (Y - vc)) / den
        l1 = ((vc - va) * (X - uc) + (ua - uc) * (Y - vc)) / den
        l2 = 1 - l0 - l1
        ins = (l0 > -0.02) & (l1 > -0.02) & (l2 > -0.02)
        if not ins.any():
            continue
        Pw = P[ti, 0] * l0[..., None] + P[ti, 1] * l1[..., None] + P[ti, 2] * l2[..., None]
        Lc = Pw @ Ml[:3, :3].T + Ml[:3, 3]
        u, v, w = Lc[..., 0], Lc[..., 1], Lc[..., 2]
        s_ = (u - 0.044) / (U_YOKE1 - 0.044)           # 0 at the ring, 1 at the neck
        if n_loc[2] > 0.8:                               # top face: three-leaf fan
            hw = (YOKE_W[0] + (YOKE_W[1] - YOKE_W[0]) * np.clip(s_, 0, 1)) * 0.31
            t = v / hw
            m = np.zeros_like(ins)
            for ang, Lf in ((math.pi, 0.62), (math.pi - 0.62, 0.50), (math.pi + 0.62, 0.50)):
                m |= leaf(s_, t * 0.55, 0.86, 0.0, ang, Lf, 0.13)
        elif abs(n_loc[1]) > 0.5 and n_loc[2] > -0.2:    # upper side chamfers: leaf spray toward the ring
            sg = np.sign(n_loc[1])
            hh = 0.0085
            t = (w - W_AXIS) / hh                       # -1..1 across the chamfer height
            m = np.zeros_like(ins)
            for ang, Lf, bt in ((math.pi - 0.15, 0.66, 0.15), (math.pi + 0.35, 0.52, 0.1), (math.pi - 0.6, 0.42, 0.2)):
                m |= leaf(s_, t * 0.5, 0.88, bt * 0.5, ang, Lf, 0.11)
        else:
            continue
        sel = ins & m
        ys_, xs_ = np.nonzero(sel)
        px, py = (xs_ + x0).clip(0, S - 1), (ys_ + y0).clip(0, S - 1)
        tex[py, px] = DARK
        # deeper core (1 texel inside the motif) for an incised look
        core = sel & ndimage.binary_erosion(sel)
        ys_, xs_ = np.nonzero(core)
        tex[(ys_ + y0).clip(0, S - 1), (xs_ + x0).clip(0, S - 1)] = DEEP
    cv2.imwrite(path, cv2.cvtColor(tex, cv2.COLOR_RGB2BGR))


paint_engraving(tex_path)
R.apply_texture(obj, tex_path, ID)
stats = check_fit(obj)
stats['texture'] = f'{size}x{size}'
if '--closeup' in sys.argv:          # debug: close views of the yoke / collar area
    yk = np.array(MW @ Vector((0.06, 0.0, W_AXIS)))
    tiles = []
    for nm, c in (('concept cam', dict(cam, fov=7, target=[float(x) for x in yk], shift=[0, 0])),
                  ('shop-ish', {'az': 10, 'el': 22, 'dist': 0.5, 'fov': 9, 'target': [float(x) for x in yk], 'shift': [0, 0]}),
                  ('back', {'az': 170, 'el': 20, 'dist': 0.5, 'fov': 9, 'target': [float(x) for x in yk], 'shift': [0, 0]})):
        for o in [o for o in bpy.data.objects if o.type == 'CAMERA']:
            bpy.data.objects.remove(o)
        R._world((0.36, 0.34, 0.32), 0.9); R._lights()
        C.blender_camera(c, 520, 520); R._setup_render(520, 520, samples=16)
        bpy.context.scene.render.filepath = '/tmp/_cu.png'
        bpy.ops.render.render(write_still=True)
        tiles.append(cv2.imread('/tmp/_cu.png'))
    cv2.imwrite(os.path.join(WORK, 'closeup.png'), np.concatenate(tiles, 1))
    sys.exit()
if '--quick' in sys.argv:
    R.render_views(obj, os.path.join(OUT, 'preview.png'), concept_cam=cam, concept_size=(CW, CH), concept_path=CONCEPT_PNG, size=420)
    sys.exit()
R.render_views(obj, os.path.join(OUT, 'preview.png'), concept_cam=cam, concept_size=(CW, CH), concept_path=CONCEPT_PNG)
R.shelf_fit_render(obj, os.path.join(OUT, 'shelf.png'))
R.export(obj, OUT, ID, tex_path)
json.dump({'id': ID, 'stats': stats, 'bake': st, 'camera': cam,
           'iou': BK.align_report(obj, CONCEPT, cam, os.path.join(OUT, 'align.png'))},
          open(os.path.join(OUT, 'stats.json'), 'w'), indent=1, default=float)
R.card(OUT, ID, stats)
print('done', stats)
