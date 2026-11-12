"""19 Kerosene hurricane lamp - collectible shelf item. Run: python3 build.py [--opt] [--align-only]

Masses (all traced from the concept through the design camera CAM0, then the whole lamp is scaled to H_FINAL):
- squat 8-sided fuel tank: bevelled plinth, foot band, body, protruding lip, conical shoulder (lathe, face to front)
- burner: stepped collar disc + gallery ring under the globe (lathe)
- glass globe: 8-sided lathe, opaque, burner / wick / flame / soot / reflections painted (projected)
- hood: brim with a thick edge, cone, bead ring, vent drum (vents painted), separate lid with a low dome
- two ears (8-sided drums along X) on the brim edge, two bent support bars (rectangular section) ear -> tank lip
- arched wire bail (6-sided tube polyline) from ear to ear, small octagonal filler cap on the shoulder
"""
import sys, os, json, math
sys.path.insert(0, '/home/claude/kit')
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import numpy as np
import cv2
from scipy import ndimage
import bpy, bmesh
from mathutils import Vector, Matrix
from geo import Builder, clear_scene, check_fit
import bake_local as BK          # kit copy with a true z-buffer + per-view min_facing + post hook
import render as R
import cam as C

CONCEPT_PNG = '/home/claude/concepts/collectible-concepts/19-kerosene-lamp-v1.png'
CONCEPT = os.path.join(HERE, 'concept_rgba.png')   # same pixels + alpha (glass speckle holes filled)
ID = 'CI19_KeroseneLamp'
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)
WORK = os.path.join(HERE, 'work'); os.makedirs(WORK, exist_ok=True)
CW, CH = 1199, 1312
H_FINAL = 0.215

EL = float(os.environ.get('EL', 11.0))
FOV = float(os.environ.get('FOV', 14.0))
CAM0 = {'az': 0.0, 'el': EL, 'dist': 0.75 * math.tan(math.radians(9)) / math.tan(math.radians(FOV / 2)), 'fov': FOV, 'target': [0.0, 0.0, 0.11],
        'shift': [(588 - CW / 2) / CH, 0.0], 'roll': 0.0}
POS, RIGHT, UPC, FWD = C.basis(CAM0)
FPX = (max(CW, CH) / 2) / math.tan(math.radians(CAM0['fov']) / 2)


def bp(u, v, n, c):
    """Concept pixel -> 3D point on the plane n.p = c (ray through CAM0)."""
    big = max(CW, CH)
    sx, sy = CAM0['shift']
    a = (u - CW / 2 - sx * big) / FPX
    b_ = (CH / 2 - sy * big - v) / FPX
    d = FWD + RIGHT * a + UPC * b_
    n = np.asarray(n, float)
    t = (c - n @ POS) / (n @ d)
    return POS + d * t


def ngon(front_deg):
    """Vertex angles of a 'crooked' polygon given as angles from the front (-Y) toward +X, mirrored to both sides.
    Returns (angles in the geo ring convention, kx = side half-width / R, ky = front-face apothem / R)."""
    fs = sorted(set([a for f in front_deg for a in (f, -f)]))
    th = [math.radians(-90 + f) for f in fs]
    xs = [math.cos(t) for t in th]; ys = [math.sin(t) for t in th]
    kx = max(xs)
    f0 = min(f for f in front_deg if f > 0)
    ky = math.cos(math.radians(f0))
    return th, kx, ky


DEC = ngon([22, 56, 90, 124, 158])                 # tank: wide front face, vertex at the sides (as drawn)
DEC_H = ngon([18, 54, 90, 126, 162])               # hood / lid / burner: regular decagon, face to front
OCT = ngon([22.5, 67.5, 112.5, 157.5])             # glass: regular octagon, face to front


def ring_front(hw, v, uc, poly):
    """Ring whose front-face midpoint shows at (uc, v) and whose side extremes are uc +- hw px.
    Returns (cx, cy, z, R, poly)."""
    th, kx, ky = poly
    R = 0.04
    for _ in range(6):
        p = bp(uc, v, (0, 1, 0), -ky * R)
        depth_side = (p - POS) @ FWD + ky * R * FWD[1]
        R = hw * depth_side / FPX / kx
    return (p[0], 0.0, p[2], R, poly)


def ring_side(hw, v, uc, poly):
    """Ring whose silhouette at the sides (plane y=0) is uc +- hw at row v."""
    th, kx, ky = poly
    pl = bp(uc - hw, v, (0, 1, 0), 0.0); pr = bp(uc + hw, v, (0, 1, 0), 0.0)
    return ((pl[0] + pr[0]) / 2, 0.0, (pl[2] + pr[2]) / 2, (pr[0] - pl[0]) / 2 / kx, poly)


def pt(u, v, y=0.0):
    p = bp(u, v, (0, 1, 0), y)
    return (p[0], y, p[2])


clear_scene()
b = Builder(ID, seed=19)
ROT8 = math.pi / 8


def lathe_c(part, rings, cap_bottom=True, cap_top=True, top_center=None, bottom_center=None, jitter=0.0):
    """Lathe from explicit rings (cx, cy, z, R, poly) - per-ring centres give the slight lean of the concept."""
    mi = b.part(part)
    rv = []
    for (cx, cy, z, r, poly) in rings:
        if r <= 1e-6:
            rv.append([b.bm.verts.new((cx, cy, z))])
        else:
            rv.append(b.ring_verts([(cx + math.cos(t) * r + b.j(jitter), cy + math.sin(t) * r + b.j(jitter), z)
                                    for t in poly[0]]))
    for a, c in zip(rv, rv[1:]):
        if len(a) == 1:
            for i in range(len(c)):
                b._face([a[0], c[(i + 1) % len(c)], c[i]], mi)
        elif len(c) == 1:
            for i in range(len(a)):
                b._face([a[i], a[(i + 1) % len(a)], c[0]], mi)
        else:
            b.bridge(a, c, mi)
    # geo.cap winds a centred fan the other way round than a plain n-gon: flip accordingly (outward normals)
    if cap_bottom and len(rv[0]) > 1:
        b.cap(rv[0], mi, flip=bottom_center is None, center=bottom_center)
    if cap_top and len(rv[-1]) > 1:
        b.cap(rv[-1], mi, flip=top_center is not None, center=top_center)
    return rv


# ------------------------------------------------------------------ tank (front-face rows of the concept)
UT = 585
TANK = [(283, 1281), (296, 1268), (296, 1246), (288, 1231), (288, 1193), (286, 1179), (286, 1107),
        (296, 1101), (296, 1077), (289, 1063), (196, 984), (181, 980)]
tank_rings = [ring_front(hw, v, UT, DEC) for hw, v in TANK]
lathe_c('tank', tank_rings, cap_top=True)
SHOULDER = tank_rings[-3:-1]      # (outer, inner) rings of the conical shoulder, for the filler cap

# ------------------------------------------------------------------ burner: collar disc + gallery ring
UB = 590
BURN = [(178, 984), (178, 975), (150, 963), (134, 961), (134, 917), (110, 907)]
burn_rings = [ring_front(hw, v, UB, DEC_H) for hw, v in BURN]
lathe_c('burner', burn_rings, cap_bottom=True, cap_top=True)

# ------------------------------------------------------------------ glass globe (side silhouette rows)
UG = 590.5
GLASS = [(104, 899), (136, 872), (163, 836), (193, 795), (195, 735), (185, 690), (163, 650), (139, 615),
         (129, 596), (129, 560)]
glass_rings = [ring_side(hw, v, UG, OCT) for hw, v in GLASS]
lathe_c('glass', glass_rings, cap_bottom=True, cap_top=True)

# ------------------------------------------------------------------ hood: brim, cone, bead, drum
UH = 595
HOOD = [(132, 576, 595), (195, 563, 595), (198, 546, 595), (190, 533, 594), (141, 458, 590), (142, 455, 590),
        (142, 441, 590), (128, 439, 590), (128, 365, 590)]
hood_rings = [ring_front(hw, v, uc, DEC_H) for hw, v, uc in HOOD]
lathe_c('hood', hood_rings, cap_bottom=True, cap_top=True)

# ------------------------------------------------------------------ lid with a low dome
UL = 591
LID = [(132, 370), (142, 367), (142, 347), (104, 327)]
lid_rings = [ring_front(hw, v, UL, DEC_H) for hw, v in LID]
LID_DOME = float(os.environ.get('LID_DOME', 0.0006))
lc = lid_rings[-1]
lathe_c('lid', lid_rings, top_center=(lc[0], 0.0, lc[2] + LID_DOME))

# ------------------------------------------------------------------ ears (8-sided drums along X)
def ear(u0, u1, v, rad_px):
    p0 = np.array(pt(u0, v)); p1 = np.array(pt(u1, v))
    depth = (p0 - POS) @ FWD
    r = rad_px * depth / FPX
    axis = (p1 - p0)
    L = np.linalg.norm(axis)
    axis = axis / L
    rings = []
    for s in (0.0, 0.12, 0.88, 1.0):
        rr = r * (0.86 if s in (0.0, 1.0) else 1.0)
        rings.append(b.ring(tuple(p0 + axis * L * s), rr, 8, z_axis=tuple(axis), rot=ROT8))
    b.loft('ears', rings)
    return r


R_EAR = ear(343, 412, 503, 46.5)
ear(765, 825, 492, 48)


# ------------------------------------------------------------------ support bars (rectangular section)
def bar(part, path_px, w_px, d_px, twist=0.0):
    """Rectangular-section bar along a polyline in the y=0 plane; w_px per point (front width), mitred bends."""
    P3 = [np.array(pt(u, v)) for u, v in path_px]
    depth = (P3[0] - POS) @ FWD
    d = d_px * depth / FPX
    n = len(P3)
    rings = []
    for i in range(n):
        w = w_px[i] * depth / FPX
        t0 = P3[min(i + 1, n - 1)] - P3[max(i - 1, 0)]
        miter = 1.0
        if 0 < i < n - 1:
            a = P3[i] - P3[i - 1]; c = P3[i + 1] - P3[i]
            a /= np.linalg.norm(a); c /= np.linalg.norm(c)
            t0 = a + c
            miter = 1.0 / max(math.cos(math.acos(np.clip(a @ c, -1, 1)) / 2), 0.5)
        t0 = t0 / np.linalg.norm(t0)
        xa = np.cross(t0, [0, 1.0, 0]); xa /= np.linalg.norm(xa)
        ya = np.array([0, 1.0, 0])
        ca, sa = math.cos(twist), math.sin(twist)
        xa, ya = xa * ca + ya * sa, -xa * sa + ya * ca
        pts = [P3[i] + xa * (sx * w / 2 * miter) + ya * (sy * d / 2) for sx, sy in ((1, -1), (1, 1), (-1, 1), (-1, -1))]
        rings.append([tuple(p) for p in pts])
    b.loft(part, rings)


BAR_D = 52
bar('tubes', [(366, 535), (366, 570), (289, 815), (360, 1012)], [66, 66, 68, 78], BAR_D, twist=math.radians(12))
bar('tubes', [(813, 525), (815, 570), (874, 768), (815, 1005)], [66, 66, 64, 76], BAR_D, twist=math.radians(-12))

# ------------------------------------------------------------------ bail (wire tube)
BAIL = [(356, 521), (338, 533), (330, 508), (328, 481), (332, 452), (372, 270), (412, 152), (510, 64), (646, 45),
        (750, 92), (792, 180), (815, 262), (826, 395), (834, 452), (844, 480), (845, 509), (835, 531), (815, 519)]
bail_pts = [pt(u, v) for u, v in BAIL]
_d = (np.array(bail_pts[0]) - POS) @ FWD
R_BAIL = 15.5 * _d / FPX
b.tube('bail', bail_pts, radius=R_BAIL, sides=6)

# ------------------------------------------------------------------ filler cap on the shoulder (front right)
CAP_Y = float(os.environ.get('CAP_Y', -0.026))
cp = np.array(pt(762, 953, CAP_Y))                  # centre of the cap's top face
r_out, r_in = SHOULDER[0], SHOULDER[1]


def shoulder_z(r):
    t = (r - r_in[3]) / (r_out[3] - r_in[3])
    return r_in[2] + (r_out[2] - r_in[2]) * t


rc = math.hypot(cp[0] - tank_rings[0][0], cp[1])
z0 = shoulder_z(rc) - 0.0015
OCT_C = ([t + 0.25 for t in OCT[0]], OCT[1], OCT[2])
cap_r = 51 * ((cp - POS) @ FWD) / FPX / OCT_C[1]
ct = cp[2]
CAP = [(cp[0], CAP_Y, z0, cap_r * 0.74, OCT_C), (cp[0], CAP_Y, ct - 0.0044, cap_r * 0.74, OCT_C),
       (cp[0], CAP_Y, ct - 0.0040, cap_r * 0.97, OCT_C), (cp[0], CAP_Y, ct - 0.0012, cap_r, OCT_C),
       (cp[0], CAP_Y, ct, cap_r * 0.84, OCT_C)]
lathe_c('cap', CAP)
print('cap: base z %.4f top z %.4f r %.4f at (%.4f, %.4f)' % (z0, ct, cap_r, cp[0], CAP_Y))

# ------------------------------------------------------------------ hood assembly tilt (right side up, as drawn)
TILT = math.radians(float(os.environ.get('TILT', 0.6)))
tilt_parts = {b.parts.index(p) for p in ('hood', 'lid', 'ears', 'bail')}
tv = set()
for f in b.bm.faces:
    if f.material_index in tilt_parts:
        tv.update(f.verts)
xc_t = hood_rings[0][0]
z_t = hood_rings[1][2]
for v in tv:
    v.co.z += (v.co.x - xc_t) * math.tan(TILT)

# ------------------------------------------------------------------ ground + scale to H_FINAL (camera follows)
bm = b.bm
co = np.array([v.co[:] for v in bm.verts])
zmin, zmax = co[:, 2].min(), co[:, 2].max()
S = H_FINAL / (zmax - zmin)
M_FIN = Matrix.Scale(S, 4) @ Matrix.Translation((0, 0, -zmin))
for v in bm.verts:
    v.co = M_FIN @ v.co
CAM = json.loads(json.dumps(CAM0))
CAM['dist'] = CAM0['dist'] * S
CAM['target'] = [CAM0['target'][0] * S, CAM0['target'][1] * S, (CAM0['target'][2] - zmin) * S]
obj = b.finish(ground=False, center_xy=False)
fit = check_fit(obj)
print('scale', round(S, 4), 'fit', fit)
PARTS = list(obj['parts'])

BK.unwrap(obj)
cam_path = os.path.join(HERE, 'camera.json')
cam = json.load(open(cam_path)) if os.path.exists(cam_path) and '--cam0' not in sys.argv else CAM
if '--opt' in sys.argv:
    cam, s_ = BK.optimize_camera(obj, CONCEPT, CAM, keys=('dist', 'shift'))
    json.dump(cam, open(cam_path, 'w'), indent=1)
if '--cam0' in sys.argv:
    json.dump(cam, open(cam_path, 'w'), indent=1)
IOU = BK.align_report(obj, CONCEPT, cam, os.path.join(OUT, 'align.png'))
print('IoU', IOU, cam)


def edge_overlay(obj, cam, out_png):
    """Concept with the model's visible hard edges drawn on top."""
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


if '--dbg' in sys.argv:
    P, UV, part, N = BK.mesh_arrays(obj)
    li = PARTS.index('lid')
    sel = part == li
    cen = P[sel].mean(1)
    vd = C.view_dir(cen, cam)
    fc = np.einsum('ij,ij->i', N[sel], vd)
    print('lid tris', sel.sum(), 'facing', np.round(np.sort(fc), 2))
    idb, zb = BK.rasterize(P, part, cam, CW, CH)
    vis = np.isin(idb, np.nonzero(sel)[0])
    print('lid visible px', vis.sum())
    sys.exit()
if '--align-only' in sys.argv:
    np.save(os.path.join(WORK, 'mm.npy'), BK.model_mask(obj, cam, CW, CH))
    edge_overlay(obj, cam, os.path.join(WORK, 'edges.png'))
    sys.exit()

# ------------------------------------------------------------------ bake
CLEAN = os.path.join(WORK, 'concept_glass_clean.png')    # globe without burner/flame (work/make_clean.py)
CLEAN_T = os.path.join(WORK, 'concept_tank_clean.png')   # tank without the filler cap and its shadow
if not os.path.exists(CLEAN) or not os.path.exists(CLEAN_T):
    import subprocess
    subprocess.run([sys.executable, '-I', os.path.join(WORK, 'make_clean.py')], check=True)


def fin(p):
    """Design-space point -> final (scaled, grounded) coordinates."""
    return np.array(M_FIN @ Vector(p))


def T_rz(deg, c):
    return BK.T_rot_z(deg, center=(c[0], c[1]))


AX_T = fin((tank_rings[3][0], 0, 0)); AX_H = fin((hood_rings[5][0], 0, 0)); AX_G = fin((glass_rings[4][0], 0, 0))
AX_B = fin((burn_rings[3][0], 0, 0)); AX_L = fin((lid_rings[1][0], 0, 0)); AX_C = fin((cp[0], CAP_Y, 0))
views = [{'name': 'orig', 'T': np.eye(4), 'bonus': 0.3}]
# tank (mirror-symmetric irregular decagon): back = mirror, sides = quarter turns
views += [{'name': 'tank_mirror', 'T': BK.T_mirror_plane('y'), 'parts': ['tank'], 'src': CLEAN_T, 'bonus': 0.1}]
views += [{'name': f'tank{a}', 'T': T_rz(a, AX_T), 'parts': ['tank'], 'src': CLEAN_T, 'bonus': -0.05 * abs(a) / 180}
          for a in (68, -68, 112, -112, 180)]
# regular decagons: hood, lid, burner - turn by whole faces so painted facets stay on facets
for nm, ax, prts in (('hood', AX_H, ['hood']), ('lid', AX_L, ['lid']), ('burner', AX_B, ['burner'])):
    views += [{'name': f'{nm}{a}', 'T': T_rz(a, ax), 'parts': prts, 'bonus': -0.05 * abs(a) / 180}
              for a in (36, -36, 72, -72, 108, -108, 144, -144, 180)]
# globe: the hidden sides sample a copy of the concept without burner and flame (no doubled flames)
views += [{'name': f'glass{a}', 'T': T_rz(a, AX_G), 'parts': ['glass'], 'src': CLEAN, 'bonus': -0.05 * abs(a) / 180}
          for a in (45, -45, 90, -90, 135, -135, 180)]
views += [{'name': f'cap{a}', 'T': T_rz(a, AX_C), 'parts': ['cap'], 'bonus': -0.05 * abs(a) / 180}
          for a in (45, -45, 90, -90, 135, -135, 180)]
# wire parts in the y=0 plane: back = mirror through that plane
views += [{'name': 'lid_grazing', 'T': np.eye(4), 'parts': ['lid'], 'min_facing': 0.1, 'bonus': 0.0}]
views += [{'name': 'wire_mirror', 'T': BK.T_mirror_plane('y'), 'parts': ['ears', 'tubes', 'bail'], 'bonus': 0.0}]
views += [{'name': 'swap_lr', 'T': BK.T_mirror_plane('x', float(AX_H[0])), 'parts': ['ears', 'tubes'], 'bonus': -0.1}]

# ------------------------------------------------------------------ painted details at texel scale
CRGB = cv2.cvtColor(cv2.imread(CONCEPT_PNG), cv2.COLOR_BGR2RGB).astype(np.float32)
_cr, _cg, _cb = CRGB[..., 0], CRGB[..., 1], CRGB[..., 2]
_Y, _X = np.mgrid[0:CH, 0:CW]
FLAME_ZONE = (((_X - 592) / 40.0) ** 2 + ((_Y - 744) / 60.0) ** 2) < 1.0
FLAME_PX = FLAME_ZONE & (_cr > 175) & (_cr - _cb > 55)
FLAME_PX = ndimage.binary_opening(FLAME_PX, iterations=1)
FLAME_PAL = np.array([[176, 92, 38], [222, 138, 62], [246, 196, 120], [255, 236, 196]], np.float32)
PROTECT = {}


def fin_tilt_inv(Xf):
    """Final coords -> final coords with the hood tilt removed (z only)."""
    z = Xf[:, 2] - (Xf[:, 0] - xc_t * S) * math.tan(TILT)
    return z


def post(tex, have, ys, xs, t, src_view, tpart, X):
    # --- flame on the globe front: crisp texel flame from the concept, snapped to a 4-colour flame palette
    g = PARTS.index('glass')
    if '--dbgflame' in sys.argv:
        sg = np.nonzero(tpart == g)[0]
        uu, vv, _ = C.project(X[sg], cam, CW, CH)
        inz = FLAME_ZONE[np.clip(vv.astype(int), 0, CH - 1), np.clip(uu.astype(int), 0, CW - 1)]
        print('flame-zone glass texels', inz.sum(), 'views', np.unique(src_view[sg[inz]], return_counts=True))
        P_, UV_, part_, N_ = BK.mesh_arrays(obj)
        idb_, zb_ = BK.rasterize(P_, part_, cam, CW, CH)
        fr = sg[inz]
        fc = np.einsum('ij,ij->i', N_[t[fr]], C.view_dir(X[fr], cam))
        fr = fr[fc > 0]
        uu, vv, zz = C.project(X[fr], cam, CW, CH)
        ui_, vi_ = uu.astype(int), vv.astype(int)
        hit = idb_[vi_, ui_]
        for k_, i in enumerate(fr):
            print(ys[i], xs[i], 'sv', src_view[i], 'tri', t[i], 'hit', hit[k_], 'hitpart', part_[hit[k_]], 'dz %.4f' % (zb_[vi_[k_], ui_[k_]] - zz[k_]))
    sel = np.nonzero((tpart == g) & (src_view == 0))[0]
    u, v, _ = C.project(X[sel], cam, CW, CH)
    ui = np.clip(np.round(u).astype(int), 0, CW - 1); vi = np.clip(np.round(v).astype(int), 0, CH - 1)
    # flame membership: majority of a 7x7 concept neighbourhood (one texel covers ~9 concept px)
    frac = cv2.blur(FLAME_PX.astype(np.float32), (7, 7))[vi, ui]
    fl = frac > 0.4
    if fl.any():
        lum = cv2.blur(CRGB, (5, 5))[vi[fl], ui[fl]] @ np.array([0.3, 0.59, 0.11])
        k = np.digitize(lum, [150, 190, 228])
        tex[ys[sel[fl]], xs[sel[fl]]] = FLAME_PAL[k]
        PROTECT['flame'] = (ys[sel[fl]], xs[sel[fl]], FLAME_PAL[k])
    # --- burner / wick tube / knob seen through the globe: sharper sampling than the projection blur
    gl = np.nonzero(tpart == g)[0]
    tex[ys[gl], xs[gl]] = np.clip(tex[ys[gl], xs[gl]] * 1.08 + 4, 0, 255)     # globe a touch lighter (lit glass)
    sel2 = np.nonzero((tpart == g) & (src_view == 0))[0]
    u2, v2, _ = C.project(X[sel2], cam, CW, CH)
    bz = (u2 > 525) & (u2 < 715) & (v2 > 778) & (v2 < 912)
    if fl.any():
        bz &= ~np.isin(sel2, sel[fl])
    if bz.any():
        sharp = cv2.medianBlur(CRGB.astype(np.uint8), 5).astype(np.float32)
        ui2 = np.clip(np.round(u2[bz]).astype(int), 0, CW - 1); vi2 = np.clip(np.round(v2[bz]).astype(int), 0, CH - 1)
        cc = sharp[vi2, ui2]
        tex[ys[sel2[bz]], xs[sel2[bz]]] = cc
        PROTECT['burner'] = (ys[sel2[bz]], xs[sel2[bz]], cc)
    # --- vents on the hood drum: one dark slot per decagon face (the projection smears/doubles them)
    h = PARTS.index('hood')
    sel = np.nonzero(tpart == h)[0]
    Xh = X[sel]
    zu = fin_tilt_inv(Xh)
    z_lo, z_hi = hood_rings[7][2] * S - zmin * S, hood_rings[8][2] * S - zmin * S
    cx = hood_rings[8][0] * S
    zf = (zu - z_lo) / (z_hi - z_lo)
    r = np.hypot(Xh[:, 0] - cx, Xh[:, 1])
    drum = (zf > -0.02) & (zf < 1.02) & (r < hood_rings[8][3] * S * 1.05)
    phi = np.degrees(np.arctan2(Xh[:, 0] - cx, -Xh[:, 1]))
    f = ((phi + 18) % 36) / 36 - 0.5
    body = tex[ys[sel[drum]], xs[sel[drum]]]
    lumb = body @ np.array([0.3, 0.59, 0.11])
    base = np.median(body[lumb > np.percentile(lumb, 35)], 0)
    # the vent band of the drum: projected vents erased to the drum's olive (blocky palette noise), then slots
    band = drum & (zf > 0.04) & (zf < 0.86)
    okb = lumb > np.percentile(lumb, 40)
    pal = body[okb]
    rng_ = np.random.default_rng(7)
    pick = pal[rng_.integers(0, len(pal), band.sum())]
    tex[ys[sel[band]], xs[sel[band]]] = pick * 0.35 + base * 0.65
    return tex


size = BK.texture_size_for(obj)
tex_path = os.path.join(OUT, f'T_{ID}.png')
tex, st = BK.bake(obj, CONCEPT, cam, views=views, size=size, out_png=None, quantize=0, post=post, blur_scale=0.55,
                  debug_png=os.path.join(OUT, 'coverage.png'), min_facing=0.3)
tex = BK._quantize(tex, 40)
for k_, (yy, xx, cc) in PROTECT.items():        # keep the painted flame out of the palette reduction
    tex[yy, xx] = cc.astype(np.uint8)


def paint_vents(tex):
    """One dark slot per drum face, drawn as the face's own sub-rectangle in UV space (crisp at texel scale)."""
    me = obj.data
    uvl = me.uv_layers.active.data
    h = PARTS.index('hood')
    z_lo, z_hi = (hood_rings[7][2] - zmin) * S, (hood_rings[8][2] - zmin) * S
    cx = hood_rings[8][0] * S
    Sz = tex.shape[0]
    n = 0
    for p in me.polygons:
        if p.material_index != h or len(p.vertices) != 4:
            continue
        co = np.array([me.vertices[i].co[:] for i in p.vertices])
        zu = fin_tilt_inv(co)
        r = np.hypot(co[:, 0] - cx, co[:, 1])
        if not (np.all(np.abs(r - hood_rings[8][3] * S) < 0.0008) and zu.min() > z_lo - 0.0006 and zu.max() < z_hi + 0.0006):
            continue
        uv = np.array([uvl[li].uv[:] for li in p.loop_indices])
        # order corners: bottom two then top two, each left->right as seen from outside
        zf = (zu - z_lo) / (z_hi - z_lo)
        ang = np.arctan2(co[:, 0] - cx, -co[:, 1])
        ang = np.unwrap(ang)
        bot = np.argsort(zf)[:2]; top = np.argsort(zf)[2:]
        bl, br = bot[np.argsort(ang[bot])]; tl, tr = top[np.argsort(ang[top])]
        def at(s_, t_):
            return (1 - s_) * (1 - t_) * uv[bl] + s_ * (1 - t_) * uv[br] + s_ * t_ * uv[tr] + (1 - s_) * t_ * uv[tl]
        quad = np.array([at(0.37, 0.17), at(0.63, 0.17), at(0.63, 0.77), at(0.37, 0.77)])
        pts = np.stack([quad[:, 0] * Sz, (1 - quad[:, 1]) * Sz], 1) - 0.5
        cv2.fillPoly(tex, [np.round(pts * 16).astype(np.int32)], (40, 31, 26), lineType=cv2.LINE_8, shift=4)
        n += 1
    print('vents painted', n)
    return tex


tex = paint_vents(tex)
cv2.imwrite(tex_path, cv2.cvtColor(tex, cv2.COLOR_RGB2BGR))
print('bake', json.dumps(st))


def reproject(obj, cam, tex_path, out_png):
    """Debug: the baked texture looked up through the concept camera (flat colour, no lighting), beside the concept."""
    tex = cv2.imread(tex_path)
    S_ = tex.shape[0]
    P, UV, part, N = BK.mesh_arrays(obj)
    idb, zb = BK.rasterize(P, part, cam, CW, CH)
    u, v, z = C.project(P.reshape(-1, 3), cam, CW, CH)
    u, v, z = u.reshape(-1, 3), v.reshape(-1, 3), z.reshape(-1, 3)
    ys, xs = np.nonzero(idb >= 0)
    t = idb[ys, xs]
    px, py = xs + 0.5, ys + 0.5
    (ua, ub, uc), (va, vb, vc) = u[t].T, v[t].T
    den = (vb - vc) * (ua - uc) + (uc - ub) * (va - vc)
    a = ((vb - vc) * (px - uc) + (uc - ub) * (py - vc)) / den
    b2 = ((vc - va) * (px - uc) + (ua - uc) * (py - vc)) / den
    c = 1 - a - b2
    w = np.stack([a / z[t, 0], b2 / z[t, 1], c / z[t, 2]], 1)
    w /= w.sum(1, keepdims=True)
    uv = (UV[t] * w[:, :, None]).sum(1)
    tx = np.clip((uv[:, 0] * S_).astype(int), 0, S_ - 1); ty = np.clip(((1 - uv[:, 1]) * S_).astype(int), 0, S_ - 1)
    im = np.full((CH, CW, 3), 90, np.uint8)
    im[ys, xs] = tex[ty, tx]
    cim = cv2.imread(CONCEPT_PNG)
    cv2.imwrite(out_png, np.concatenate([cim, im], 1))


reproject(obj, cam, tex_path, os.path.join(WORK, 'reproject.png'))
if '--checker' in sys.argv:
    ck = ((np.add.outer(np.arange(size), np.arange(size)) % 2) * 120 + 60).astype(np.uint8)
    ck = np.dstack([ck, ck, ck]); ck[..., 2] = np.where(np.arange(size)[:, None] % 2 == 0, 200, ck[..., 2])
    cv2.imwrite('/tmp/_ck.png', ck)
    reproject(obj, cam, '/tmp/_ck.png', os.path.join(WORK, 'checker.png'))
    sv = np.zeros((size, size, 3), np.uint8)
    sys.exit()
R.apply_texture(obj, tex_path, ID)
stats = check_fit(obj)
stats['texture'] = f'{size}x{size}'
if '--quick' in sys.argv:
    R.render_views(obj, os.path.join(OUT, 'preview.png'), concept_cam=cam, concept_size=(CW, CH), concept_path=CONCEPT_PNG, size=420)
    sys.exit()
R.render_views(obj, os.path.join(OUT, 'preview.png'), concept_cam=cam, concept_size=(CW, CH), concept_path=CONCEPT_PNG)
R.shelf_fit_render(obj, os.path.join(OUT, 'shelf.png'))
R.export(obj, OUT, ID, tex_path)
json.dump({'id': ID, 'stats': stats, 'bake': st, 'camera': cam, 'iou': IOU},
          open(os.path.join(OUT, 'stats.json'), 'w'), indent=1, default=float)
R.card(OUT, ID, stats)
print('done', stats)
