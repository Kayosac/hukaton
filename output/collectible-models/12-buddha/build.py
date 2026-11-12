"""12 Buddha statuette - collectible shelf item. Run: python3 build.py [--opt] [--align-only] [--rows] [--no-render]

Masses (traced from the concept on a fixed design camera, so pixel measurements become metres):
- oval lotus pedestal: low petal band + slightly overhanging plate (12-sided lathe, oval). Petals are painted.
- crossed legs as one wide low loft along X with rounded knee ends, sitting on the plate
- torso loft (waist -> chest -> shoulders -> neck), two arms as tubes (the figure's left one flares into the robe
  sleeve), hands as one low blob in the lap
- head: 10-sided lathe with a pulled nose ridge and chin, hair cap (part split by a tilted plane), ushnisha bun,
  two flat elongated ears; the whole head is yawed toward the viewer's right like the concept
Painted (concept projection / procedural patina): eyes, mouth, curls, robe folds, crossed feet, lotus petals,
patina with rubbed bronze high points.
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
import bake_local as BK          # true z-buffer rasteriser (arms / hands / legs overlap themselves)
import render as R
import cam as C

CONCEPT = '/home/claude/concepts/collectible-concepts/12-buddha-v2.png'
ID = 'CI12_Buddha'
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)
WORK = os.path.join(HERE, 'work'); os.makedirs(WORK, exist_ok=True)
CW, CH = 1536, 1024

# ------------------------------------------------------------------ design camera (pixel -> metre mapping)
DCAM = {'az': -6.0, 'el': 9.0, 'dist': 1.05, 'fov': 18, 'target': [0, 0, 0.1], 'shift': [0, 0], 'roll': 0}
ORIGIN_PX = (751, 946)           # where the pedestal's bottom centre (model origin) sits in the concept
u0_, v0_, _ = C.project(np.array([[0, 0, 0.0]]), DCAM, CW, CH)
DCAM['shift'] = [(ORIGIN_PX[0] - u0_[0]) / CW, -(ORIGIN_PX[1] - v0_[0]) / CW]
FPX = (max(CW, CH) / 2) / math.tan(math.radians(DCAM['fov']) / 2)

def _ray(u, v):
    pos, right, upc, f = C.basis(DCAM)
    sx, sy = DCAM['shift']
    xr = (u - CW / 2 - sx * CW) / FPX
    yr = (CH / 2 - sy * CW - v) / FPX
    return pos, f + right * xr + upc * yr

def on_y(u, v, y=0.0):
    p, d = _ray(u, v); t = (y - p[1]) / d[1]; return p + t * d

def on_z(u, v, z):
    p, d = _ray(u, v); t = (z - p[2]) / d[2]; return p + t * d

def span(row, ul, ur, y):
    """Left/right silhouette pixels on one row, taken on the plane Y=y -> (centre x, z, half width)."""
    a, c = on_y(ul, row, y), on_y(ur, row, y)
    return (a[0] + c[0]) / 2, (a[2] + c[2]) / 2, abs(c[0] - a[0]) / 2

clear_scene()
b = Builder(ID, seed=12)
rng = np.random.default_rng(1212)
d2r = math.radians

def jig(vs, a=0.0006):
    for v in vs:
        v.co += Vector(rng.uniform(-a, a, 3))

# ------------------------------------------------------------------ pedestal (oval lotus base)
BX = span(850, 413, 1085, 0.0)
BX = (BX[0] + 0.0009, BX[1], BX[2])
RX = BX[2] * 1.012
z_plate_top = on_y(413, 826, 0.0)[2]                    # plate top seen at the side extremes
RY = -on_z(749, 862, z_plate_top)[1] / 1.0              # plate front edge at the centre column
z_band_top = on_y(749, 878, -RY)[2]
print('base RX %.4f RY %.4f band_top %.4f plate_top %.4f' % (RX, RY, z_band_top, z_plate_top))
SCL = (1.0, RY / RX)
base_prof = [(RX * 0.86, 0.0), (RX * 0.95, 0.003), (RX * 0.985, 0.009), (RX * 0.995, z_band_top - 0.001)]
plate_prof = [(RX * 0.99, z_band_top - 0.001), (RX * 1.012, z_band_top), (RX * 1.016, z_plate_top - 0.0025),
              (RX * 1.0, z_plate_top), (RX * 0.6, z_plate_top + 0.0008)]
ROT_BASE = d2r(7)
r_band = b.lathe('band', base_prof, sides=12, rot=ROT_BASE, scale=SCL, cap_top=False, offset=(BX[0], 0, 0))
r_plate = b.lathe('plate', plate_prof, sides=12, rot=ROT_BASE, scale=SCL, cap_bottom=False, offset=(BX[0], 0, 0),
                  top_center=(BX[0], 0, z_plate_top + 0.0012))
for ring in r_band[1:] + r_plate[1:]:
    jig(ring, 0.0005)
BASE_C = (BX[0], 0.0)
print('BX', BX)

# ------------------------------------------------------------------ crossed legs: loft along X
LY = -0.004                                   # legs centre plane
xl = on_y(437, 770, LY)[0]; xr = on_y(1074, 765, LY)[0]
z0 = z_plate_top - 0.003
# (t along the width 0..1, half height, centre z offset, centre y, half depth)
LEGS = [(0.012, 0.010, 0.016, -0.004, 0.012),
        (0.045, 0.0140, 0.0160, -0.006, 0.022),
        (0.12, 0.0205, 0.0210, -0.006, 0.032),
        (0.25, 0.0200, 0.0210, -0.004, 0.040),
        (0.40, 0.0165, 0.0180, -0.002, 0.044),
        (0.52, 0.0170, 0.0180, -0.003, 0.045),
        (0.64, 0.0175, 0.0185, -0.004, 0.044),
        (0.77, 0.0215, 0.0220, -0.006, 0.040),
        (0.89, 0.0210, 0.0215, -0.007, 0.032),
        (0.958, 0.0160, 0.0175, -0.007, 0.022),
        (0.99, 0.0100, 0.0160, -0.005, 0.012)]
legs_rings = []
for t, hz, zc, yc, hy in LEGS:
    x = xl + (xr - xl) * t
    pts = b.ring((x, LY + yc, z0 + zc), 1.0, 8, z_axis=(1, 0, 0), rot=d2r(22.5), scale=(hy, hz))
    # flatten the underside onto the plate, a softer top
    if t < 0.2 or t > 0.8:      # knees: tuck the lower outer corner in (rounded knee, not a box corner)
        sgn = 1 if t < 0.5 else -1
        w = (0.2 - min(t, 1 - t)) / 0.2
        pts = [Vector((p.x + sgn * max(0.0, (z0 + zc) - p.z) * 0.6 * w, p.y, p.z)) for p in pts]
    pts = [Vector((p.x, p.y, max(p.z, z0 + 0.0005))) for p in pts]
    legs_rings.append(b.ring_verts(pts))
mi = b.part('legs')
for a, c in zip(legs_rings, legs_rings[1:]):
    b.bridge(a, c, mi)
b.cap(legs_rings[0], mi, flip=True, center=(xl - 0.001, LY - 0.006, z0 + 0.014))
b.cap(legs_rings[-1], mi, center=(xr + 0.001, LY - 0.009, z0 + 0.0145))
for r_ in legs_rings:
    jig(r_, 0.0007)

# ------------------------------------------------------------------ torso: loft of oval rings, a little behind the lap
TY = 0.010
TORSO = [  # row, left px, right px, half depth, y offset
    (700, 625, 895, 0.025, 0.000),
    (640, 612, 905, 0.026, 0.000),
    (560, 606, 905, 0.027, -0.002),
    (480, 600, 904, 0.026, -0.002),
    (430, 572, 928, 0.025, 0.000),
    (404, 586, 918, 0.023, 0.002),
    (380, 620, 890, 0.019, 0.003),
    (362, 668, 855, 0.016, 0.003),
    (348, 700, 826, 0.0145, 0.003),
    (320, 712, 818, 0.0135, 0.004)]
tor_rings = []
for row, ul, ur, hd, dy in TORSO:
    cx, cz, hw = span(row, ul, ur, TY + dy)
    tor_rings.append(b.ring_verts(b.ring((cx, TY + dy, cz), 1.0, 10, rot=d2r(-90), scale=(hw, hd))))
mi = b.part('torso')
for a, c in zip(tor_rings, tor_rings[1:]):
    b.bridge(a, c, mi)
b.cap(tor_rings[0], mi, flip=True); b.cap(tor_rings[-1], mi)
for r_ in tor_rings[1:-1]:
    jig(r_, 0.0006)

# ------------------------------------------------------------------ arms (tubes): shoulder -> elbow -> forearm -> wrist
def arm_path(pts):
    return [tuple(on_y(u, v, y)) for u, v, y in pts]
ARM_L = arm_path([(646, 428, 0.010), (596, 470, 0.008), (570, 560, 0.004), (566, 625, -0.002),
                  (610, 668, -0.022), (690, 690, -0.036)])
ARM_R = arm_path([(850, 428, 0.010), (887, 470, 0.008), (910, 560, 0.003), (940, 638, -0.003),
                  (905, 668, -0.024), (840, 690, -0.036)])
b.tube('arms', ARM_L, sides=7, radii=[0.0112, 0.0108, 0.0110, 0.0115, 0.0098, 0.0082], jitter=0.0005)
b.tube('arms', ARM_R, sides=7, radii=[0.0112, 0.0122, 0.0138, 0.0160, 0.0112, 0.0085], jitter=0.0005)

# ------------------------------------------------------------------ hands in the lap
hc = on_y(782, 676, -0.040)
b.blob('hands', tuple(hc), (0.0185, 0.0125, 0.0072), rings=4, sides=8, jitter=0.0004)

# ------------------------------------------------------------------ head: lathe, nose ridge, hair split, bun, ears
HY = 0.006
HEAD_COL = 757
HEAD = [  # row, half width px
    (343, 0), (338, 45), (325, 86), (300, 103), (262, 113), (222, 120), (185, 125), (150, 121), (127, 106),
    (113, 80), (106, 46), (103, 0)]
YAW = d2r(17)
hprof = []
for row, hw in HEAD:
    p = on_y(HEAD_COL, row, HY)
    hprof.append((abs(on_y(HEAD_COL + hw, row, HY)[0] - p[0]), p[2]))
HX = on_y(HEAD_COL, 250, HY)[0]
HDEPTH = 1.12
n_before = len(b.bm.verts)
head_rings = b.lathe('head', hprof, sides=10, rot=d2r(-90), scale=(1.0, HDEPTH), offset=(HX, HY, 0))
# nose ridge and chin on the front vertex column (index 0 points to -Y)
def ring_at(row):
    zr = on_y(HEAD_COL, row, HY)[2]
    return min(range(len(head_rings)), key=lambda i: abs(head_rings[i][0].co.z - zr) if len(head_rings[i]) > 1 else 9)
NOSE = {300: 0.0035, 262: 0.0062, 222: 0.0030}
for row, d in NOSE.items():
    ring = head_rings[ring_at(row)]
    ring[0].co.y -= d
    ring[1].co.y -= d * 0.25; ring[-1].co.y -= d * 0.25
head_rings[ring_at(325)][0].co.y -= 0.002
for ring in head_rings[1:-1]:
    jig(ring, 0.0004)
# hair: faces above a plane tilted down toward the back (hairline at the front, nape behind the ears)
z_hair_front = on_y(HEAD_COL, 168, HY)[2]
z_hair_back = on_y(HEAD_COL, 262, HY)[2]
hair_mi = b.part('hair')
head_mi = b.parts.index('head')
head_vset = set(v for r_ in head_rings for v in r_)
head_faces = [f for f in b.bm.faces if f.material_index == head_mi and all(v in head_vset for v in f.verts)]
HDY = max(abs(v.co.y - HY) for v in head_vset)
for f in head_faces:
    c = f.calc_center_median()
    t = (c.y - HY) / HDY            # -1 front, +1 back
    zcut = z_hair_front + (z_hair_back - z_hair_front) * np.clip((t + 0.35) / 1.35, 0, 1)
    if c.z > zcut:
        f.material_index = hair_mi
# bun (ushnisha)
z_top = hprof[-1][1]
bun_c = on_y(783, 75, HY)
bun_prof = [(0.0108, z_top - 0.004), (0.0145, z_top + 0.001), (0.0150, z_top + 0.0055), (0.0122, z_top + 0.0100),
            (0.0062, z_top + 0.0128), (0.0, z_top + 0.0134)]
bun_prof = [(r * 0.9, z) for r, z in bun_prof]
bun_rings = b.lathe('bun', bun_prof, sides=9, rot=d2r(10), scale=(1, 1.05), offset=(HX + 0.0012, HY, 0))
# ears: flat elongated lofts on the head sides
EAR_FLARE = 35
def ear(side):
    rows = [(206, 0.0022, 0.0062, -0.0050), (232, 0.0026, 0.0080, -0.0038), (280, 0.0025, 0.0070, -0.0034),
            (318, 0.0024, 0.0058, -0.0026), (348, 0.0022, 0.0046, -0.0022), (358, 0.0, 0.0, -0.0030)]
    hw_ref = hprof[4][0]
    hw_at = lambda z: np.interp(z, [hp[1] for hp in hprof[1:7]], [hp[0] for hp in hprof[1:7]])
    rings = []
    for row, tx, ty, off in rows:
        z = on_y(HEAD_COL, row, HY)[2]
        x = HX + side * (hw_ref + off + (0.0035 if side > 0 and row > 300 else 0) + (0.0025 if side > 0 else 0) - (0.0015 if side < 0 and row > 300 else 0))
        if tx == 0:
            rings.append([b.bm.verts.new((x, HY + 0.006, z))])
        else:
            pts = b.ring((0, 0, 0), 1.0, 6, rot=d2r(30), scale=(tx, ty))
            th = d2r(EAR_FLARE) * (-side)
            ct_, st_ = math.cos(th), math.sin(th)
            pts = [Vector((x + p.x * ct_ - p.y * st_, HY + 0.005 + p.x * st_ + p.y * ct_, z)) for p in pts]
            rings.append(b.ring_verts(pts))
    mi_ = b.part('ears')
    for a, c in zip(rings, rings[1:]):
        if len(c) == 1:
            for i in range(len(a)):
                b._face([a[i], a[(i + 1) % len(a)], c[0]], mi_)
        else:
            b.bridge(a, c, mi_)
    b.cap(rings[0], mi_, flip=True)
    return rings
ear_rings = ear(-1) + ear(+1)
# yaw the whole head group around the head axis
head_verts = list(b.bm.verts)[n_before:]
ROLL = d2r(5.0)
z_neck = hprof[0][1]
Rh = Matrix.Rotation(ROLL, 3, 'Y') @ Matrix.Rotation(YAW, 3, 'Z')
for v in head_verts:
    p = v.co - Vector((HX, HY, z_neck))
    v.co = Rh @ p + Vector((HX, HY, z_neck))
HEAD_C = (HX, HY)
TORSO_TOP_Z = on_y(760, 404, TY)[2]

obj = b.finish(center_xy=False)
fit = check_fit(obj)
print('fit', fit)

BK.unwrap(obj, angle=55)
# give the face (the focal point) more texels: scale the head islands up and repack (same texture size)
HEAD_UV_BOOST = 1.6
_parts = list(obj['parts'])
_hid = [_parts.index(p) for p in ('head', 'hair', 'bun', 'ears')]
_uvl = obj.data.uv_layers.active.data
for poly in obj.data.polygons:
    if poly.material_index in _hid:
        for li in poly.loop_indices:
            u_, v_ = _uvl[li].uv
            _uvl[li].uv = (u_ * HEAD_UV_BOOST, v_ * HEAD_UV_BOOST)
bpy.context.view_layer.objects.active = obj
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.select_all(action='SELECT')
bpy.ops.uv.pack_islands(rotate=True, margin=0.012)
bpy.ops.object.mode_set(mode='OBJECT')
cam_path = os.path.join(HERE, 'camera.json')
cam = json.load(open(cam_path)) if os.path.exists(cam_path) and '--reset-cam' not in sys.argv else json.loads(json.dumps(DCAM))
if '--opt' in sys.argv:
    cam, s = BK.optimize_camera(obj, CONCEPT, cam, keys=tuple(os.environ.get('OPTKEYS', 'dist,shift').split(',')))
    json.dump(cam, open(cam_path, 'w'), indent=1)
if '--rows' in sys.argv:
    rgb_, cm_ = BK.load_concept(CONCEPT)
    mm_ = BK.model_mask(obj, cam, CW, CH)
    for y in range(40, 1000, 15):
        def runs(m):
            xs = np.nonzero(m)[0]
            if not len(xs):
                return []
            br = np.nonzero(np.diff(xs) > 1)[0]
            st = [xs[0]] + [xs[i + 1] for i in br]; en = [xs[i] for i in br] + [xs[-1]]
            return list(zip(st, en))
        print(y, 'C', [(int(a), int(c)) for a, c in runs(cm_[y])], ' M', [(int(a), int(c)) for a, c in runs(mm_[y])])
score = BK.align_report(obj, CONCEPT, cam, os.path.join(OUT, 'align.png'))
print('IoU', score, cam)
if '--shade' in sys.argv:       # cheap flat-shaded check renders (numpy rasteriser), no Cycles
    P_, UV_, part_, N_ = BK.mesh_arrays(obj)
    pal_ = np.array([[200, 120, 90], [120, 200, 90], [90, 120, 220], [220, 200, 80], [200, 90, 200], [90, 210, 210],
                     [240, 150, 60], [160, 160, 160], [120, 90, 60], [250, 250, 250]], float)
    def shade(c, W, H):
        idb, _ = BK.rasterize(P_, part_, c, W, H)
        L = np.array([-0.4, -0.7, 0.6]); L /= np.linalg.norm(L)
        lam = np.clip(N_ @ L, 0, 1) * 0.7 + 0.3
        img = np.full((H, W, 3), 60, np.uint8)
        m = idb >= 0
        img[m] = (pal_[part_[idb[m]] % len(pal_)] * lam[idb[m]][:, None]).astype(np.uint8)
        return img
    rgb_, _ = BK.load_concept(CONCEPT)
    im = shade(cam, CW, CH)
    cv2.imwrite(os.path.join(WORK, 'shade_concept.png'), cv2.cvtColor((0.55 * im + 0.45 * rgb_).astype(np.uint8), cv2.COLOR_RGB2BGR))
    tiles = []
    for az, el in ((0, 10), (90, 10), (180, 15), (-90, 10), (40, 35), (150, 35)):
        c = {'az': az, 'el': el, 'dist': 0.9, 'fov': 18, 'target': [0, 0, 0.1], 'shift': [0, 0]}
        tiles.append(shade(c, 400, 400))
    sheet = np.concatenate([np.concatenate(tiles[:3], 1), np.concatenate(tiles[3:], 1)], 0)
    cv2.imwrite(os.path.join(WORK, 'shade_turn.png'), cv2.cvtColor(sheet, cv2.COLOR_RGB2BGR))
if '--align-only' in sys.argv:
    sys.exit()

# ------------------------------------------------------------------ texture
size = BK.texture_size_for(obj)
parts = list(obj['parts'])
pid = {p: i for i, p in enumerate(parts)}
rgb, cmask = BK.load_concept(CONCEPT)
P, UV, part, N = BK.mesh_arrays(obj)
print('texture', size, 'parts', parts)

idb0, _ = BK.rasterize(P, part, cam, CW, CH)
own = np.where((idb0 >= 0) & cmask, part[np.maximum(idb0, 0)], -1)

# de-lit concept: remove the large-scale key light (lit left, shadowed right) so copies and fills match the front
lab = cv2.cvtColor(rgb, cv2.COLOR_RGB2LAB).astype(np.float32)
L = lab[..., 0].copy()
m = cmask.astype(np.float32)
num = cv2.GaussianBlur(L * m, (0, 0), 45); den = cv2.GaussianBlur(m, (0, 0), 45)
low = num / np.maximum(den, 1e-4)
LMEAN = float((L * m).sum() / m.sum())
L = np.where(cmask, L - 0.75 * (low - LMEAN), L)
lab[..., 0] = np.clip(L, 0, 255)
delit = cv2.cvtColor(lab.astype(np.uint8), cv2.COLOR_LAB2RGB)
alpha = (cmask * 255).astype(np.uint8)
delit_png = os.path.join(WORK, 'concept_delit.png')
cv2.imwrite(delit_png, np.dstack([cv2.cvtColor(delit, cv2.COLOR_RGB2BGR), alpha]))
concept_png = os.path.join(WORK, 'concept_alpha.png')
cv2.imwrite(concept_png, np.dstack([cv2.cvtColor(rgb, cv2.COLOR_RGB2BGR), alpha]))

# A: the concept as seen (front)
texA, stA = BK.bake(obj, concept_png, cam, views=[{'name': 'orig', 'T': np.eye(4), 'bonus': 0.3}], size=size,
                    out_png=os.path.join(WORK, 'texA.png'), debug_png=os.path.join(WORK, 'covA.png'), min_facing=0.42,
                    quantize=0)
covA = np.all(cv2.imread(os.path.join(WORK, 'covA.png'))[:, :, ::-1] == np.array([60, 200, 80]), axis=2)
covA = ndimage.binary_opening(covA, iterations=1)
# head / hair / bun meet on one continuous surface: the part-boundary erosion would leave a seam line across the
# forehead, so bake those parts again without erosion and take the extra texels
texA0, _ = BK.bake(obj, concept_png, cam, views=[{'name': 'orig', 'T': np.eye(4), 'bonus': 0.3}], size=size,
                   out_png=os.path.join(WORK, 'texA0.png'), debug_png=os.path.join(WORK, 'covA0.png'), min_facing=0.42,
                   quantize=0, erode_px=0, texel_blur=False)   # head islands are boosted: sharper source
covA0 = np.all(cv2.imread(os.path.join(WORK, 'covA0.png'))[:, :, ::-1] == np.array([60, 200, 80]), axis=2)
_P0, _UV0, _part0, _ = BK.mesh_arrays(obj)
_ti = np.full((size, size), -1, np.int32)
_uv = _UV0 * size; _uv[..., 1] = size - _uv[..., 1]
for t in range(len(_P0)):
    cv2.fillPoly(_ti, [np.round(_uv[t] * 16).astype(np.int32)], int(t), lineType=cv2.LINE_8, shift=4)
_hp = np.isin(np.where(_ti >= 0, _part0[np.maximum(_ti, 0)], -1), [list(obj['parts']).index(p) for p in ('head', 'hair', 'bun')])
add = covA0 & _hp
texA[add] = texA0[add]
covA = covA | add
print('head texels from the sharp/no-erosion pass', int(add.sum()))

# B: hidden sides of the radial / symmetric parts from the de-lit concept
def T_about(pivot, Rm):
    T1 = np.eye(4); T1[:3, 3] = -np.asarray(pivot)
    T2 = np.eye(4); T2[:3, 3] = np.asarray(pivot)
    Rm4 = np.eye(4); Rm4[:3, :3] = Rm
    return T2 @ Rm4 @ T1
RH = np.array(Matrix.Rotation(ROLL, 3, 'Y') @ Matrix.Rotation(YAW, 3, 'Z'))
RT = np.array(Matrix.Rotation(ROLL, 3, 'Y'))
HP = (HX, HY, z_neck)
def head_rot(a):        # rotation about the head's own (rolled) vertical axis
    return T_about(HP, RT @ np.array(Matrix.Rotation(d2r(a), 3, 'Z')) @ RT.T)
head_mirror = T_about(HP, RH @ np.diag([-1.0, 1, 1]) @ RH.T)
Sb = np.diag([1.0, RY / RX, 1.0])
def base_rot(a):
    return T_about((BASE_C[0], BASE_C[1], 0), Sb @ np.array(Matrix.Rotation(d2r(a), 3, 'Z')) @ np.linalg.inv(Sb))
viewsB = [{'name': 'orig', 'T': np.eye(4), 'bonus': 0.3}]
viewsB += [{'name': f'hair{a}', 'T': head_rot(a), 'parts': ['hair', 'bun']} for a in (35, -35, 70, -70, 105, -105, 140, -140, 180)]
viewsB += [{'name': 'head_mirror', 'T': head_mirror, 'parts': ['head', 'ears'], 'bonus': 0.1}]
viewsB += [{'name': f'base{a}', 'T': base_rot(a), 'parts': ['band']} for a in (30, -30, 60, -60, 90, -90, 120, -120, 150, -150, 180)]
texB, stB = BK.bake(obj, delit_png, cam, views=viewsB, size=size, out_png=os.path.join(WORK, 'texB.png'),
                    debug_png=os.path.join(OUT, 'coverage.png'), min_facing=0.42, quantize=0)
covB = np.zeros((size, size), bool)
dbg = cv2.imread(os.path.join(OUT, 'coverage.png'))[:, :, ::-1]
covB = ~np.all(dbg == np.array([255, 40, 40]), axis=2) & (dbg.sum(2) > 0)
print('views B', stB['views'])

# ------------------------------------------------------------------ texel -> 3D position / normal / part
tri_img = np.full((size, size), -1, np.int32)
uvp = UV * size; uvp[..., 1] = size - uvp[..., 1]
for t in range(len(P)):
    cv2.fillPoly(tri_img, [np.round(uvp[t] * 16).astype(np.int32)], int(t), lineType=cv2.LINE_8, shift=4)
tys, txs = np.nonzero(tri_img >= 0)
tt = tri_img[tys, txs]
tn = N[tt]; tp = part[tt]
a0, a1, a2 = uvp[tt, 0], uvp[tt, 1], uvp[tt, 2]
px_, py_ = txs + 0.5, tys + 0.5
den = (a1[:, 1] - a2[:, 1]) * (a0[:, 0] - a2[:, 0]) + (a2[:, 0] - a1[:, 0]) * (a0[:, 1] - a2[:, 1])
den = np.where(np.abs(den) < 1e-12, 1e-12, den)
l0r = ((a1[:, 1] - a2[:, 1]) * (px_ - a2[:, 0]) + (a2[:, 0] - a1[:, 0]) * (py_ - a2[:, 1])) / den
l1r = ((a2[:, 1] - a0[:, 1]) * (px_ - a2[:, 0]) + (a0[:, 0] - a2[:, 0]) * (py_ - a2[:, 1])) / den
edge_bad = (l0r < -0.02) | (l1r < -0.02) | (1 - l0r - l1r < -0.02)
l0, l1 = np.clip(l0r, 0, 1), np.clip(l1r, 0, 1)
l2 = np.clip(1 - l0 - l1, 0, 1)
ls = l0 + l1 + l2; l0, l1, l2 = l0 / ls, l1 / ls, l2 / ls
X = P[tt, 0] * l0[:, None] + P[tt, 1] * l1[:, None] + P[tt, 2] * l2[:, None]

# convexity per vertex (how far a vertex sticks out of its neighbours along its normal) -> rubbed high points
me = obj.data
V = np.array([v.co[:] for v in me.vertices]); VN = np.array([v.normal[:] for v in me.vertices])
E = np.array([e.vertices[:] for e in me.edges])
acc = np.zeros_like(V); cnt = np.zeros(len(V))
np.add.at(acc, E[:, 0], V[E[:, 1]]); np.add.at(acc, E[:, 1], V[E[:, 0]])
np.add.at(cnt, E[:, 0], 1); np.add.at(cnt, E[:, 1], 1)
lap = V - acc / np.maximum(cnt, 1)[:, None]
conv_v = np.einsum('ij,ij->i', lap, VN)
conv_v = np.clip(conv_v / (np.percentile(np.abs(conv_v), 90) + 1e-9), -1, 1)
tris_v = np.array([t_.vertices[:] for t_ in me.loop_triangles])
CONV = conv_v[tris_v[tt, 0]] * l0 + conv_v[tris_v[tt, 1]] * l1 + conv_v[tris_v[tt, 2]] * l2

# ------------------------------------------------------------------ procedural patina (hidden / grazing texels)
def vnoise3(Xp, cell, seed):
    """Trilinear value noise on 3D positions (coherent across UV seams)."""
    r = np.random.default_rng(seed)
    G = r.random((64, 64, 64)).astype(np.float32)
    q = Xp / cell + 32.0
    i0 = np.floor(q).astype(int); f = q - i0
    f = f * f * (3 - 2 * f)
    out = 0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (f[:, 0] if dx else 1 - f[:, 0]) * (f[:, 1] if dy else 1 - f[:, 1]) * (f[:, 2] if dz else 1 - f[:, 2])
                out = out + w * G[(i0[:, 0] + dx) % 64, (i0[:, 1] + dy) % 64, (i0[:, 2] + dz) % 64]
    return out

# palette from the de-lit concept: green patina vs rubbed bronze
pix = texA[covA].astype(np.float32)          # colours as they appear on the baked front
pal, w = BK._kmeans_palette(pix[::3], 12)
pal = np.asarray(pal, np.float32); w = np.asarray(w)
is_bronze = pal[:, 0] > pal[:, 1] * 1.02
GREEN = pal[~is_bronze]; GW = w[~is_bronze]
BRONZE = pal[is_bronze]; BW = w[is_bronze]
lum = lambda c: c @ np.array([0.3, 0.59, 0.11])
og = np.argsort(lum(GREEN)); GREEN, GW = GREEN[og], GW[og]
ob = np.argsort(lum(BRONZE)); BRONZE, BW = BRONZE[ob], BW[ob]
BRONZE_FRAC = float(BW.sum() / w.sum())
print('palette green', GREEN.astype(int).tolist(), 'bronze', BRONZE.astype(int).tolist(), 'bronze frac %.2f' % BRONZE_FRAC)

def pick_w(pal_, w_, t):
    cdf = np.cumsum(w_) / np.sum(w_)
    return pal_[np.clip(np.searchsorted(cdf, np.clip(t, 0, 0.999)), 0, len(pal_) - 1)]

def block_noise(n, block, seed):
    r = np.random.default_rng(seed)
    g = r.random((n // block + 2, n // block + 2))
    return np.kron(g, np.ones((block, block)))[:n, :n]
nz1 = block_noise(size, 1, 21); nz2 = block_noise(size, 2, 22)
n_big = vnoise3(X, 0.011, 31)                      # patina continents
n_mid = vnoise3(X, 0.0045, 32)
n_fine = 0.5 * nz1[tys, txs] + 0.5 * nz2[tys, txs]
# bronze where the surface is rubbed: high points (convex), tops, plus patches
up = np.clip(tn[:, 2], 0, 1)
pb = 0.50 * n_big + 0.26 * n_mid + 0.30 * np.clip(CONV, 0, 1) + 0.14 * up + 0.30 * (n_fine - 0.5)
thr = np.quantile(pb, 1 - max(0.14, BRONZE_FRAC - 0.06))
bronze_m = pb > thr
shade_t = np.clip(0.35 * n_mid + 0.15 * n_fine + 0.5 * vnoise3(X, 0.0032, 33), 0, 0.999)
shade_t = np.clip((shade_t - shade_t.mean()) * 1.8 + 0.5, 0, 0.999)
# underside / bottom faces darker (less rubbed, more oxidised)
proc = np.where(bronze_m[:, None], pick_w(BRONZE[1:], BW[1:], shade_t), pick_w(GREEN[1:], GW[1:], np.clip(shade_t + 0.05, 0, 0.999)))
# mottle inside each colour family: a few chunky flecks of the other family (patina is a fine mottle)
sp = block_noise(size, 2, 51)[tys, txs]
fl = vnoise3(X, 0.0028, 52)
spk_b = (~bronze_m) & (fl > 0.80) & (sp < 0.5)
proc[spk_b] = pick_w(BRONZE[:3], BW[:3], shade_t[spk_b])
spk_g = bronze_m & (fl < 0.25)
proc[spk_g] = pick_w(GREEN[2:], GW[2:], shade_t[spk_g])
dark = np.clip(-tn[:, 2], 0, 1)
proc = proc * (1 - 0.30 * dark)[:, None]

# painted robe folds on the hidden back: a few dark creases with a lit edge (torso back diagonal, legs back)
def creases(coord, spacing, width, phase=0.0):
    f = (coord / spacing + phase) % 1.0
    d = np.minimum(f, 1 - f) * spacing
    core = np.clip(1 - d / width, 0, 1)
    lit = np.clip(1 - np.abs(f * spacing - width * 2.2) / width, 0, 1)
    return core, lit
back = (X[:, 1] > TY + 0.002)
# the robe crosses the back from the figure's left shoulder (+X, high) to its right hip (-X, low), as on the front
SASH_A = d2r(40)
sash_c = -math.sin(SASH_A) * X[:, 0] + math.cos(SASH_A) * X[:, 2] + 0.003 * (vnoise3(X, 0.012, 41) - 0.5)
sash_c0 = -math.sin(SASH_A) * 0.030 + math.cos(SASH_A) * (TORSO_TOP_Z - 0.012)
dsh = sash_c - sash_c0                       # > 0: bare shoulder blade above the robe edge
sel = back & np.isin(tp, [pid['torso'], pid['arms']])
edge_core = np.clip(1 - np.abs(dsh + 0.0012) / 0.0016, 0, 1)
edge_lit = np.clip(1 - np.abs(dsh - 0.0012) / 0.0012, 0, 1)
fold_c, fold_l = creases(dsh, 0.017, 0.0016, 0.5)
below = np.clip(-dsh / 0.004, 0, 1) * np.clip((dsh + 0.05) / 0.01, 0, 1)
mult = 1 - 0.50 * edge_core + 0.22 * edge_lit - below * (0.26 * fold_c - 0.12 * fold_l)
proc[sel] = proc[sel] * mult[sel][:, None]
fm = np.clip((vnoise3(X, 0.02, 43) - 0.3) * 2.5, 0, 1)
cl, ll = creases(X[:, 0] + 0.005 * (vnoise3(X, 0.012, 42) - 0.5), 0.028, 0.0018, 0.3)
cl, ll = cl * fm, ll * fm
sel = (X[:, 1] > LY + 0.012) & (tp == pid['legs'])
proc[sel] = proc[sel] * (1 - 0.32 * cl[sel] + 0.15 * ll[sel])[:, None]

# colour transfer: match the front bake's mean / contrast per channel (front texels are the reference)
refA = texA[covA].reshape(-1, 3).astype(np.float32)
lit = tn[:, 2] > -0.3
mu_p, sd_p = proc[lit].mean(0), proc[lit].std(0)
proc = (proc - mu_p) / np.maximum(sd_p, 1) * (refA.std(0) * 0.9) + refA.mean(0) * 1.02
proc = np.clip(proc, 0, 255)
print('mean A', texA[covA].reshape(-1,3).mean(0).round(1), 'mean proc', proc.mean(0).round(1), 'std A', texA[covA].reshape(-1,3).std(0).round(1), 'std proc', proc.std(0).round(1))
# ------------------------------------------------------------------ combine: front (A) > radial copies (B) > patina
texA_f = texA.astype(np.float32); texB_f = texB.astype(np.float32)
tex = np.zeros((size, size, 3), np.float32)
cA = covA[tys, txs]; cB = covB[tys, txs] & ~cA
tex[tys, txs] = proc
tex[tys[cB], txs[cB]] = texB_f[tys[cB], txs[cB]]
# harmonise fills to the front's low-frequency colour near the boundary (no hard seam where the concept ends)
fill = np.zeros((size, size), bool); fill[tys[~cA], txs[~cA]] = True
def _lowpass(img, m_, sig=6):
    num_ = cv2.GaussianBlur(img * m_[..., None], (0, 0), sig)
    den_ = cv2.GaussianBlur(m_.astype(np.float32), (0, 0), sig)[..., None]
    return num_ / np.maximum(den_, 1e-4), den_[..., 0]
mA, wA = _lowpass(texA_f, covA.astype(np.float32))
mF, wF = _lowpass(tex, fill.astype(np.float32))
ratio = np.clip(mA / np.maximum(mF, 1.0), 0.75, 1.3)
dist = ndimage.distance_transform_edt(~covA)
fade = np.exp(-dist / 6.0) * np.clip(wA / 0.08, 0, 1)
tex = tex * (1 + (ratio - 1) * fade[..., None])
tex[covA] = texA_f[covA]
# soft 1-texel blend across the A boundary
edge = covA & ~ndimage.binary_erosion(covA, iterations=1)
tex[edge] = 0.5 * tex[edge] + 0.5 * cv2.GaussianBlur(tex, (0, 0), 0.8)[edge]

have = np.zeros((size, size), bool); have[tys[~edge_bad], txs[~edge_bad]] = True
_, inds = ndimage.distance_transform_edt(~have, return_indices=True)
tex = tex[inds[0], inds[1]]
tex = np.clip(tex, 0, 255).astype(np.uint8)
tex = BK._quantize(tex, 56)
tex_path = os.path.join(OUT, f'T_{ID}.png')
cv2.imwrite(tex_path, cv2.cvtColor(tex, cv2.COLOR_RGB2BGR))
def tex_view(c, W, H, light=True):
    idb, _ = BK.rasterize(P, part, c, W, H)
    u, v, z = C.project(P.reshape(-1, 3), c, W, H)
    u, v = u.reshape(-1, 3), v.reshape(-1, 3)
    ys_, xs_ = np.nonzero(idb >= 0); t_ = idb[ys_, xs_]
    px, py = xs_ + 0.5, ys_ + 0.5
    d_ = (v[t_, 1] - v[t_, 2]) * (u[t_, 0] - u[t_, 2]) + (u[t_, 2] - u[t_, 1]) * (v[t_, 0] - v[t_, 2])
    d_ = np.where(np.abs(d_) < 1e-9, 1e-9, d_)
    ba = ((v[t_, 1] - v[t_, 2]) * (px - u[t_, 2]) + (u[t_, 2] - u[t_, 1]) * (py - v[t_, 2])) / d_
    bb = ((v[t_, 2] - v[t_, 0]) * (px - u[t_, 2]) + (u[t_, 0] - u[t_, 2]) * (py - v[t_, 2])) / d_
    bc = 1 - ba - bb
    zz = z.reshape(-1, 3)[t_]
    wa, wb, wc = ba / zz[:, 0], bb / zz[:, 1], bc / zz[:, 2]; ws = wa + wb + wc
    uv = (UV[t_, 0] * wa[:, None] + UV[t_, 1] * wb[:, None] + UV[t_, 2] * wc[:, None]) / ws[:, None]
    tx = np.clip((uv[:, 0] * size).astype(int), 0, size - 1); ty = np.clip(((1 - uv[:, 1]) * size).astype(int), 0, size - 1)
    img = np.full((H, W, 3), 70, np.uint8)
    col = tex[ty, tx].astype(np.float32)
    if light:
        Ld = np.array([-0.4, -0.7, 0.6]); Ld /= np.linalg.norm(Ld)
        col *= (np.clip(N[t_] @ Ld, 0, 1) * 0.55 + 0.6)[:, None]
    img[ys_, xs_] = np.clip(col, 0, 255).astype(np.uint8)
    return img
if '--texshade' in sys.argv:
    tiles = [tex_view(cam, CW, CH, light=False)[0:1024:2, 256:1280:2]]
    for az, el in ((0, 10), (60, 15), (120, 15), (180, 15), (-120, 15), (-60, 15), (0, 55)):
        tiles.append(tex_view({'az': az, 'el': el, 'dist': 0.9, 'fov': 18, 'target': [0, 0, 0.1], 'shift': [0, 0]}, 512, 512))
    sheet = np.concatenate([np.concatenate(tiles[:4], 1), np.concatenate(tiles[4:], 1)], 0)
    cv2.imwrite(os.path.join(WORK, 'texshade.png'), cv2.cvtColor(sheet, cv2.COLOR_RGB2BGR))
if '--tex-only' in sys.argv:
    sys.exit()

R.apply_texture(obj, tex_path, ID)
stats = check_fit(obj)
stats['texture'] = f'{size}x{size}'
stats['iou'] = round(score, 3)
if '--no-render' not in sys.argv:
    R.render_views(obj, os.path.join(OUT, 'preview.png'), concept_cam=cam, concept_size=(CW, CH), concept_path=CONCEPT)
    R.shelf_fit_render(obj, os.path.join(OUT, 'shelf.png'))
R.export(obj, OUT, ID, tex_path)
json.dump({'id': ID, 'stats': stats, 'bake': {'A': stA, 'B': stB}, 'camera': cam}, open(os.path.join(OUT, 'stats.json'), 'w'), indent=1)
if '--no-render' not in sys.argv:
    R.card(OUT, ID, stats)
print('done', stats)
