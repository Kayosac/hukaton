"""18 Old battered briefcase - collectible shelf item. Run: python3 build.py [--opt] [--align-only] [--quick]

Masses (concept: front three-quarter from the front-right and above, near-orthographic; camera + proportions from
tools/calib.py, least squares on 7 hand-measured concept corners):
- case = ONE closed shell lofted along Y from 8-point chamfered rings: front half (lid, worn chamfered front edge),
  a real seam groove, a slightly larger back half, chamfered back edge. Crooked: right end a bit taller, top leaning.
- 8 brass corner caps (raised, tip-cut tetrahedra wrapping three faces), 4 on the front and 4 on the back corners
- handle: real loop with a hole (chamfered-rectangle section swept along an arch) on two brass mounting blocks
- two latches: a back block on the back half + a lower front clasp with a sloped face on the front half
Painted (concept projection): leather mottling, scuffs, worn orange edges, torn patch, stitched slash, brass grime.
Hidden sides: back face = the front turned 180 deg in its plane (not mirrored) with the stitched slash removed,
left side = mirror of the right side, back caps / latch backs = mirrors of the front ones, bottom = leather palette.
"""
import sys, os, json, math
sys.path.insert(0, '/home/claude/kit')
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import numpy as np
import cv2
from scipy import ndimage
import bpy
from mathutils import Vector, Matrix
from geo import Builder, clear_scene, check_fit
import bake_local as BK          # z-buffer rasterizer, per-view parts occlusion / min_facing
import render as R
import cam as C

CONCEPT = '/home/claude/concepts/collectible-concepts/18-battered-case-v1.png'
ID = 'CI18_BatteredCase'
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)
WORK = os.path.join(HERE, 'tmp'); os.makedirs(WORK, exist_ok=True)
CW, CH = 1536, 1024

# ------------------------------------------------------------------ dimensions (metres)
W1 = 0.200            # front half width (x)
H1 = 0.1185           # front half height at the left end
DR = 0.0016           # right end taller by this (crooked)
STEP = 0.0012         # back half larger than the front half on every side
Y0, YS, Y1 = -0.0215, -0.0010, 0.0215   # front face, seam, back face
CH_F = 0.0035         # worn front edge chamfer
CH_B = 0.0028         # back edge chamfer
GROOVE = 0.0018       # seam groove depth
K = (0.0035, 0.0100, 0.0035, 0.0035)   # corner chamfers (BL, BR, TR, TL) of the front-half rings; the concept's
                                        # bottom-right edge is worn off along the whole depth
BT = 0.0028           # back half reaches higher at the top (the concept's top step)
KB = (0.0095, 0.0120, 0.0095, 0.0095)          # corner chamfer of the back half (the concept shows its corners worn round)

clear_scene()
b = Builder(ID, seed=18)


def rect_ring(y, inset=0.0, grow=0.0, lean=0.0, k=K, top=0.0):
    """8-point chamfered rectangle in the XZ plane at depth y. Order from bottom-left, CCW seen from the front:
    0 B-left, 1 B-right, 2 R-bottom, 3 R-top, 4 T-right, 5 T-left, 6 L-top, 7 L-bottom."""
    x0, x1 = -W1 / 2 - grow + inset, W1 / 2 + grow - inset
    zb = 0.0 - grow + inset
    zl, zr = H1 + grow - inset + top, H1 + DR + grow - inset + top
    kbl, kbr, ktr, ktl = [kk + inset * 0.6 for kk in (k if isinstance(k, tuple) else (k, k, k, k))]
    pts = [(x0 + kbl, zb), (x1 - kbr, zb), (x1, zb + kbr), (x1, zr - ktr), (x1 - ktr, zr), (x0 + ktl, zl), (x0, zl - ktl),
           (x0, zb + kbl)]
    out = []
    for x, z in pts:
        out.append((x + lean * z / H1, y, z))
    return out


LEAN = 0.0012   # top shifted to +x (slight lean)
RINGS = [
    rect_ring(Y0, inset=CH_F, lean=LEAN, k=(0.0035, 0.0060, 0.0035, 0.0035)),                 # front face
    rect_ring(Y0 + CH_F * 0.9, lean=LEAN),
    rect_ring(YS - 0.0012, lean=LEAN),
    rect_ring(YS - 0.0004, inset=GROOVE, lean=LEAN),      # groove
    rect_ring(YS + 0.0010, inset=GROOVE - STEP, grow=0, lean=LEAN, k=KB, top=BT),
    rect_ring(YS + 0.0026, grow=STEP, lean=LEAN, k=KB, top=BT),   # back half: worn, strongly chamfered corners
    rect_ring(Y1 - CH_B, grow=STEP, lean=LEAN, k=KB, top=BT),
    rect_ring(Y1, inset=CH_B - STEP, lean=LEAN, k=KB, top=BT),    # back face
]
# crooked: per-ring tiny jitter of the corner points (a worn, not machined case)
rng = np.random.default_rng(18)
for ri in range(len(RINGS)):
    RINGS[ri] = [(x + rng.uniform(-4e-4, 4e-4), y, z + rng.uniform(-4e-4, 4e-4)) for x, y, z in RINGS[ri]]
# the front face bulges a hair at its centre: front ring pushed forward by the n-gon? keep flat (PSX)

RV = [b.ring_verts(r) for r in RINGS]
SEG_PART = {0: 'bottom'}
for a, c in zip(RV, RV[1:]):
    for i in range(8):
        k = (i + 1) % 8
        part = SEG_PART.get(i, 'shell')
        mi = b.part(part)
        b._face([a[i], a[k], c[k], c[i]], mi)
b._face(list(reversed(RV[0])), b.part('face'))
b._face(list(RV[-1]), b.part('face'))


# ------------------------------------------------------------------ brass corner caps (tip-cut tetrahedra)
def corner_cap(corner, u1, u2, u3, a, d, cut, t=0.0010, tip=(0.004, 0.004, 0.004)):
    """Raised brass corner cap as one convex solid. Local coords (q1, q2, q3) along u1, u2, u3 (unit vectors along
    the box edges pointing INTO the box, u1/u2 span the front face, u3 goes back along the depth), origin at the
    box corner. Plate: -t <= q1, q2 <= a, -t <= q3 <= d; the inner corner of the front plate is cut (q1 + q2 <= cut,
    the concept's pentagon caps) and the outer tip is chamfered (plane through the points tip_i along each axis)."""
    from scipy.spatial import HalfspaceIntersection, ConvexHull
    t1, t2, t3 = tip
    # halfspaces A q + b <= 0
    H = [[-1, 0, 0, -t], [0, -1, 0, -t], [0, 0, -1, -t], [1, 0, 0, -a], [0, 1, 0, -a], [0, 0, 1, -d],
         [1, 1, 0, -cut],
         [-1 / t1, -1 / t2, -1 / t3, 1 - t * (1 / t1 + 1 / t2 + 1 / t3)]]
    hs = HalfspaceIntersection(np.array(H, float), np.array([a * 0.3, a * 0.3, d * 0.3]))
    Q = hs.intersections
    Q = Q[np.unique(np.round(Q, 7), axis=0, return_index=True)[1]]
    hull = ConvexHull(Q)
    cen = Q.mean(0)
    U = np.array([u1, u2, u3], float)
    Pw = np.array(corner, float) + Q @ U
    faces = []
    for simp, eq in zip(hull.simplices, hull.equations):
        tri = list(simp)
        n = np.cross(Q[tri[1]] - Q[tri[0]], Q[tri[2]] - Q[tri[0]])
        if np.dot(n, Q[tri[0]] - cen) < 0:
            tri = tri[::-1]
        faces.append(tuple(tri))
    # local->world may flip handedness: fix winding by world-space test
    cw = Pw.mean(0)
    fixed = []
    for f in faces:
        n = np.cross(Pw[f[1]] - Pw[f[0]], Pw[f[2]] - Pw[f[0]])
        fixed.append(f if np.dot(n, Pw[f[0]] - cw) > 0 else f[::-1])
    b.add_bmesh_geometry('brass', [tuple(p) for p in Pw], fixed)


def zt(x):
    return H1 + DR * (x + W1 / 2) / W1


CAP_A, CAP_D, CAP_CUT = 0.0190, 0.0120, 0.0275
for sx in (-1, 1):
    for top in (0, 1):
        x = sx * W1 / 2 + LEAN * (top) + (0.0010 if sx < 0 else 0.0)   # left caps sit a hair in (concept outline)
        z = zt(x) if top else 0.0
        tip = {(-1, 0): (0.0075, 0.0075, 0.0045), (1, 0): (0.0100, 0.0100, 0.0050)}.get((sx, top), (0.0055, 0.0055, 0.004))
        corner_cap((x, Y0, z), (-sx, 0, 0), (0, 0, -1 if top else 1), (0, 1, 0), CAP_A, CAP_D, CAP_CUT, tip=tip)

# ------------------------------------------------------------------ latches and handle mounts (brass)
for lx in (-0.059, 0.059):
    xx = lx + LEAN + 0.0015
    zf, zb_ = zt(xx), zt(xx) + STEP + BT
    # back block on the back half, spanning the seam a little
    b.box('brass', (xx + 0.0012, YS + 0.0050, zb_ + 0.0035), (0.0225, 0.0110, 0.0074), jitter=0.0002, taper=(0.96, 0.92))
    # front clasp on the lid top, sloped front face, slightly over the front edge
    b.box('brass', (xx, Y0 + 0.0072, zf + 0.0024), (0.0215, 0.0140, 0.0058), jitter=0.0002,
          taper=(0.97, 0.80), top_offset=(0, 0.0012))
HX = 0.0265            # handle post centre |x|
for mx in (-HX, HX):
    xx = mx + LEAN
    b.box('brass', (xx, 0.0025, zt(xx) + STEP + BT + 0.0030), (0.0190, 0.0150, 0.0065), jitter=0.0002, taper=(0.94, 0.9))


# ------------------------------------------------------------------ handle: chamfered-rect section swept on an arch
def sweep_xz(part, path, wn, wy, ch, y0):
    """Planar path in XZ (list of (x, z)); section = rectangle wn (in-plane) x wy (along Y) with chamfer ch.
    Mitred corners (offset scaled by 1/cos of half the turn angle)."""
    P = [np.array(p, float) for p in path]
    n = len(P)
    sec = [(wn / 2 - ch, -wy / 2), (wn / 2, -wy / 2 + ch), (wn / 2, wy / 2 - ch), (wn / 2 - ch, wy / 2),
           (-wn / 2 + ch, wy / 2), (-wn / 2, wy / 2 - ch), (-wn / 2, -wy / 2 + ch), (-wn / 2 + ch, -wy / 2)]
    rings = []
    for i in range(n):
        tin = P[i] - P[i - 1] if i > 0 else P[1] - P[0]
        tout = P[i + 1] - P[i] if i < n - 1 else P[-1] - P[-2]
        tin /= np.linalg.norm(tin); tout /= np.linalg.norm(tout)
        t = tin + tout; t /= np.linalg.norm(t)
        nrm = np.array([-t[1], t[0]])                 # in-plane normal (x, z)
        sc = 1.0 / max(np.dot(nrm, np.array([-tin[1], tin[0]])), 0.5)
        pts = []
        for a_, yy in sec:
            q = P[i] + nrm * a_ * sc
            pts.append((q[0], y0 + yy, q[1]))
        rings.append(b.ring_verts(pts))
    mi = b.part(part)
    for r0, r1 in zip(rings, rings[1:]):
        b.bridge(r0, r1, mi)
    b.cap(rings[0], mi, flip=True)
    b.cap(rings[-1], mi)


zb_l, zb_r = zt(-HX) + STEP + BT + 0.0060, zt(HX) + STEP + BT + 0.0060
HT = 0.0165            # handle centre line top above the mounts
handle_path = [(-HX + LEAN, zb_l - 0.002), (-HX + LEAN + 0.0004, zb_l + HT - 0.0085), (-HX + LEAN + 0.0085, zb_l + HT),
               (HX + LEAN - 0.0085, zb_r + HT + 0.0004), (HX + LEAN, zb_r + HT - 0.0085), (HX + LEAN, zb_r - 0.002)]
sweep_xz("handle", handle_path, 0.0150, 0.0170, 0.0030, 0.0025)

_co = np.array([v.co[:] for v in b.bm.verts])
SHIFT = np.array([-(_co[:, 0].min() + _co[:, 0].max()) / 2, -(_co[:, 1].min() + _co[:, 1].max()) / 2, -_co[:, 2].min()])
obj = b.finish()
fit = check_fit(obj)
print('fit', fit)
PARTS = list(obj['parts'])
print('parts', PARTS)

BK.unwrap(obj)
cam_path = os.path.join(HERE, 'camera.json')
# design camera from tools/calib.py (fov 11 fixed, rms 5.7 px on 7 corners)
cam = json.load(open(cam_path)) if os.path.exists(cam_path) else \
    {'az': 17.2, 'el': 15.8, 'dist': 1.51, 'fov': 11.0, 'target': [0, 0, 0.07], 'shift': [-0.0166, -0.0094], 'roll': 0.0}
if '--opt' in sys.argv:
    cam, s = BK.optimize_camera(obj, CONCEPT, cam, keys=('az', 'dist', 'shift'))
    json.dump(cam, open(cam_path, 'w'), indent=1)
print('IoU', BK.align_report(obj, CONCEPT, cam, os.path.join(OUT, 'align.png')), cam)


def edge_overlay(obj, cam, out_png):
    """Concept with the model's hard edges drawn on top (checks inner edges the silhouette IoU cannot see)."""
    me = obj.data
    V = np.array([v.co[:] for v in me.vertices])
    u, v, _ = C.project(V, cam, CW, CH)
    im = cv2.imread(CONCEPT)
    ekeys = {}
    for p in me.polygons:
        for ek in p.edge_keys:
            ekeys.setdefault(ek, []).append(p.index)
    for ek, fs in ekeys.items():
        if len(fs) == 2 and me.polygons[fs[0]].normal.dot(me.polygons[fs[1]].normal) > 0.985:
            continue
        a, c = ek
        cv2.line(im, (int(u[a]), int(v[a])), (int(u[c]), int(v[c])), (60, 255, 60), 1, cv2.LINE_AA)
    cv2.imwrite(out_png, im)


if '--align-only' in sys.argv:
    edge_overlay(obj, cam, os.path.join(WORK, 'edges.png'))
    sys.exit()

# ------------------------------------------------------------------ helpers
def part_mask_concept(names):
    P, UV, part, N = BK.mesh_arrays(obj)
    idb, _ = BK.rasterize(P, part, cam, CW, CH)
    ids = [PARTS.index(n) for n in names]
    return (idb >= 0) & np.isin(part[np.maximum(idb, 0)], ids), idb, N, part


def texel_points(obj, size, part_name):
    """(ys, xs, X world pos, N normal) for every texel of one part (UV-space raster, as in the bake)."""
    P, UV, part, N = BK.mesh_arrays(obj)
    k = PARTS.index(part_name)
    tri_img = np.full((size, size), -1, np.int32)
    uvp = UV * size
    uvp[..., 1] = size - uvp[..., 1]
    for t in np.nonzero(part == k)[0]:
        cv2.fillPoly(tri_img, [np.round(uvp[t] * 16).astype(np.int32)], int(t), lineType=cv2.LINE_8, shift=4)
    ys, xs = np.nonzero(tri_img >= 0)
    t = tri_img[ys, xs]
    a0, a1, a2 = uvp[t, 0], uvp[t, 1], uvp[t, 2]
    px, py = xs + 0.5, ys + 0.5
    den = (a1[:, 1] - a2[:, 1]) * (a0[:, 0] - a2[:, 0]) + (a2[:, 0] - a1[:, 0]) * (a0[:, 1] - a2[:, 1])
    den = np.where(np.abs(den) < 1e-12, 1e-12, den)
    l0 = ((a1[:, 1] - a2[:, 1]) * (px - a2[:, 0]) + (a2[:, 0] - a1[:, 0]) * (py - a2[:, 1])) / den
    l1 = ((a2[:, 1] - a0[:, 1]) * (px - a2[:, 0]) + (a0[:, 0] - a2[:, 0]) * (py - a2[:, 1])) / den
    l0, l1 = np.clip(l0, 0, 1), np.clip(l1, 0, 1)
    l2 = np.clip(1 - l0 - l1, 0, 1)
    sm = np.maximum(l0 + l1 + l2, 1e-9)
    l0, l1, l2 = l0 / sm, l1 / sm, l2 / sm
    X = P[t, 0] * l0[:, None] + P[t, 1] * l1[:, None] + P[t, 2] * l2[:, None]
    return ys, xs, X, N[t]


# ------------------------------------------------------------------ de-lit concept (per planar face group gains)
def delit_concept(out_png):
    """The concept lights the top strongly and keeps the +X side in shade. The left side is a mirror of the +X side
    and the back a copy of the front, so the top and the +X side are brought part of the way to the front's leather
    brightness (one gain per group, measured on the median leather), keeping some of the painted light."""
    rgb, cm = BK.load_concept(CONCEPT)
    vis, idb, N, part = part_mask_concept(['shell', 'face'])
    tri = np.maximum(idb, 0)
    nx, nz = N[tri][..., 0], N[tri][..., 2]
    pn = np.array(PARTS)[part[tri]]
    vis &= cm
    groups = {'front': vis & (pn == 'face'), 'side': vis & (pn == 'shell') & (nx > 0.7),
              'top': vis & (pn == 'shell') & (nz > 0.7)}
    f = rgb.astype(np.float32)
    lum = f @ np.array([0.3, 0.59, 0.11], np.float32)
    ref = np.median(lum[groups['front']])
    gain = np.ones(lum.shape, np.float32)
    info = {}
    for g, m in groups.items():
        if g == 'front' or m.sum() < 50:
            continue
        k = float(np.clip((ref / max(np.median(lum[m]), 1.0)) ** 0.6, 0.8, 1.6))
        gain[m] = k
        info[g] = round(k, 3)
    gain = cv2.GaussianBlur(gain, (0, 0), 1.2)
    out = np.clip(f * gain[..., None], 0, 255).astype(np.uint8)
    out[~cm] = rgb[~cm]
    cv2.imwrite(out_png, np.dstack([cv2.cvtColor(out, cv2.COLOR_RGB2BGR), (cm * 255).astype(np.uint8)]))
    print('delit gains', info, 'front median lum', round(float(ref), 1))
    return out_png


def T_back_to_front():
    """Affine map of the back face (y = Y1) onto the front face (y = Y0), turned 180 deg in its own plane (x kept,
    z flipped, y flipped) and stretched to the front face's size: the back shows the front's leather, not a mirror."""
    def bbox(r):
        a = np.array(r) + SHIFT
        return a[:, 0].min(), a[:, 0].max(), a[:, 2].min(), a[:, 2].max(), a[:, 1].mean()
    fx0, fx1, fz0, fz1, fy = bbox(RINGS[0])
    bx0, bx1, bz0, bz1, by = bbox(RINGS[-1])
    sx, sz = (fx1 - fx0) / (bx1 - bx0), (fz1 - fz0) / (bz1 - bz0)
    T = np.eye(4)
    T[:3, :3] = np.diag([sx, -1.0, -sz])
    T[:3, 3] = [fx0 - sx * bx0, fy + by, fz1 + sz * bz0]
    return T


BAKE_SRC = delit_concept(os.path.join(WORK, 'concept_delit.png'))
size = BK.texture_size_for(obj)
if os.environ.get('TEX'):
    size = int(os.environ['TEX'])
SIDE_FACING = 0.14          # top and +X side are seen at facing ~0.27 (2 concept px per texel: no streaks)
HC = np.array([HX * 0 + LEAN, 0.0025, 0.0]) + SHIFT          # handle centre (x, y)
views = [{'name': 'orig', 'T': np.eye(4), 'bonus': 0.3},
         {'name': 'orig_low', 'T': np.eye(4), 'parts': ['shell', 'handle', 'brass'], 'bonus': 0.3, 'min_facing': SIDE_FACING},
         {'name': 'mirror_x', 'T': BK.T_mirror_plane('x', at=float(SHIFT[0] * 0 + 0.0)), 'parts': ['shell', 'brass'],
          'bonus': 0.05, 'min_facing': SIDE_FACING},
         {'name': 'back_from_front', 'T': T_back_to_front(), 'parts': ['face']},
         {'name': 'handle_mx', 'T': BK.T_mirror_plane('x', at=float(HC[0])), 'parts': ['handle'], 'min_facing': SIDE_FACING},
         {'name': 'handle_my', 'T': BK.T_mirror_plane('y', at=float(HC[1])), 'parts': ['handle'], 'min_facing': SIDE_FACING}]
# bottom: the concept never shows it; dark worn leather picked from the concept by eye
part_colors = {'bottom': [(58, 26, 20), (74, 34, 25), (88, 42, 30), (104, 52, 36), (128, 70, 44)]}
tex_path = os.path.join(OUT, f'T_{ID}.png')
tex, st = BK.bake(obj, BAKE_SRC, cam, views=views, size=size, out_png=tex_path, debug_png=os.path.join(OUT, 'coverage.png'),
                  part_colors=part_colors, erode_px=2, min_facing=0.3)
print('bake', json.dumps(st))


def back_without_slash(tex):
    """The back face reuses the front leather turned 180 deg; the stitched slash (the concept's one signature mark)
    is inpainted out of that copy first, so the back is not a twin of the front."""
    im = cv2.imread(BAKE_SRC, cv2.IMREAD_UNCHANGED)
    src = np.ascontiguousarray(im[:, :, :3]).copy()
    m = np.zeros(src.shape[:2], np.uint8)
    cv2.fillPoly(m, [np.array([(250, 722), (330, 718), (400, 760), (478, 812), (470, 832), (395, 815), (300, 775), (250, 745)])], 1)
    m = cv2.dilate(m, np.ones((3, 3), np.uint8), iterations=4)
    rgb = cv2.inpaint(src, m, 9, cv2.INPAINT_TELEA)
    # give the filled area the leather's own blocky variation, copied from plain leather beside it
    hp = src.astype(np.float32) - cv2.blur(src, (31, 31)).astype(np.float32)
    ys_, xs_ = np.nonzero(m)
    rgb[ys_, xs_] = np.clip(rgb[ys_, xs_] + hp[ys_ - 130, xs_ + 160], 0, 255).astype(np.uint8)
    clean = os.path.join(WORK, 'concept_delit_noslash.png')
    cv2.imwrite(clean, np.dstack([rgb, im[:, :, 3]]))
    t2, _ = BK.bake(obj, clean, cam, views=[{'name': 'back_from_front', 'T': T_back_to_front(), 'parts': ['face']}],
                    size=size, erode_px=2, min_facing=0.3, part_colors=part_colors)
    ys, xs, X, Nn = texel_points(obj, size, 'face')
    back = Nn[:, 1] > 0.5
    tex[ys[back], xs[back]] = t2[ys[back], xs[back]]
    return tex




def seg_dist(P, A, B):
    A, B = np.asarray(A, float), np.asarray(B, float)
    d = B - A
    t = np.clip(((P - A) @ d) / max(d @ d, 1e-9), 0, 1)
    return np.linalg.norm(P - (A + t[:, None] * d), axis=1), t


def paint_slash(tex):
    """The stitched slash is ~25 texels long at the item's texel density: the projection blurs its three stitches
    into a smudge, so it is repainted crisp at texel scale in the concept's colours, placed by projecting every front
    texel into the concept (shapes measured on the concept in concept pixels)."""
    ys, xs, X, Nn = texel_points(obj, size, 'face')
    fr = Nn[:, 1] < -0.5
    ys, xs, X = ys[fr], xs[fr], X[fr]
    u, v, _ = C.project(X, cam, CW, CH)
    P = np.stack([u, v], 1)
    slash = [(262, 730), (300, 748), (345, 770), (390, 790), (430, 806), (470, 820)]
    ds = np.full(len(P), 1e9); tt = np.zeros(len(P)); L = 0.0; tot = sum(np.hypot(b[0] - a[0], b[1] - a[1]) for a, b in zip(slash, slash[1:]))
    for a, b in zip(slash, slash[1:]):
        seg = np.hypot(b[0] - a[0], b[1] - a[1])
        d, t = seg_dist(P, a, b)
        better = d < ds
        ds[better] = d[better]; tt[better] = (L + t[better] * seg) / tot
        L += seg
    width = 6.5 * np.sin(np.clip(tt, 0, 1) * math.pi) ** 0.5 + 1.0          # tapered ends
    dsh = np.full(len(P), 1e9)
    for a, b in zip(slash, slash[1:]):
        d, _ = seg_dist(P, (a[0] - 2, a[1] + 9), (b[0] - 2, b[1] + 9))
        dsh = np.minimum(dsh, d)
    col = tex[ys, xs].astype(np.float32)
    shadow = (dsh < 5.0) & (ds >= width)
    col[shadow] = col[shadow] * 0.55
    cut = ds < width
    col[cut] = np.array([178, 124, 86])
    core = ds < width * 0.35
    col[core & (tt > 0.15) & (tt < 0.85)] = np.array([120, 70, 48])          # dark split inside the slash
    for a, b in (((321, 735), (300, 773)), ((352, 752), (332, 789)), ((388, 770), (369, 807))):
        d, _ = seg_dist(P, a, b)
        col[d < 6.5] = np.array([212, 160, 112])
    tex[ys, xs] = np.clip(col, 0, 255).astype(np.uint8)
    return tex


tex = back_without_slash(tex.copy())
tex = paint_slash(tex)


def lift_brass(tex):
    """The concept's brass reads gold against the leather mostly by contrast with its strong key light; under the
    game's flatter light the projected brass sinks toward the leather, so it is lifted a little toward its own
    lit ochre (warmer, brighter, same pixel pattern)."""
    ys, xs, X, Nn = texel_points(obj, size, 'brass')
    c = tex[ys, xs].astype(np.float32)
    c = c * np.array([1.18, 1.16, 0.98]) + np.array([6, 6, 0])
    tex[ys, xs] = np.clip(c, 0, 255).astype(np.uint8)
    return tex


tex = lift_brass(tex)
cv2.imwrite(tex_path, cv2.cvtColor(tex, cv2.COLOR_RGB2BGR))
R.apply_texture(obj, tex_path, ID)
stats = check_fit(obj)
stats['texture'] = f'{size}x{size}'
stats['iou'] = round(BK.align_report(obj, CONCEPT, cam), 4)
if '--quick' in sys.argv:
    sys.exit()
R.render_views(obj, os.path.join(OUT, 'preview.png'), concept_cam=cam, concept_size=(CW, CH), concept_path=CONCEPT)
R.shelf_fit_render(obj, os.path.join(OUT, 'shelf.png'))
R.export(obj, OUT, ID, tex_path)
json.dump({'id': ID, 'stats': stats, 'bake': st, 'camera': cam}, open(os.path.join(OUT, 'stats.json'), 'w'), indent=1)
R.card(OUT, ID, stats)
print('done', stats)
