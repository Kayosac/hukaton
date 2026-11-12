"""10 School trophy - collectible shelf item. Run: python3 build.py [--opt] [--align-only] [--rows]

Masses (traced from the concept on a fixed design camera, so pixel measurements become metres):
- crooked, slightly tapered wooden plinth (box, a little yawed against the cup), ~0.113 x 0.070 x 0.055 m
- brass plate on the plinth front, raised 2.5 mm (the '1', laurels, rivets, cut corners are painted)
- 8-sided brass foot + 8-sided stem, 12-sided bowl with a thick rolled rim, a real hollow and a dip in the rim
- two angular loop handles (chamfered strap sweeps, real holes) from the rim lip down to the lower bowl
Small stuff (scratches, tarnish, wood grain, plate engraving) comes from the concept projection.
"""
import sys, os, json, math
sys.path.insert(0, '/home/claude/kit')
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import numpy as np
import cv2
from scipy import ndimage
import bpy
from mathutils import Vector
from geo import Builder, clear_scene, check_fit
import bake_local as BK          # true z-buffer rasteriser (the hollow cup and handles overlap themselves)
import render as R
import cam as C

CONCEPT = '/home/claude/concepts/collectible-concepts/10-school-trophy-v1.png'
ID = 'CI10_SchoolTrophy'
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)
WORK = os.path.join(HERE, 'work'); os.makedirs(WORK, exist_ok=True)
CW, CH = 1536, 1024

# ------------------------------------------------------------------ design camera (pixel -> metre mapping)
DCAM = {'az': 17.0, 'el': 16.0, 'dist': 1.15, 'fov': 18, 'target': [0, 0, 0.1], 'shift': [0, 0], 'roll': 0}
ORIGIN_PX = (745, 904)           # where the plinth's bottom centre (model origin) sits in the concept
u0_, v0_, _ = C.project(np.array([[0, 0, 0.0]]), DCAM, CW, CH)
DCAM['shift'] = [(ORIGIN_PX[0] - u0_[0]) / CW, -(ORIGIN_PX[1] - v0_[0]) / CW]
FPX = (max(CW, CH) / 2) / math.tan(math.radians(DCAM['fov']) / 2)
AXIS_COL = ORIGIN_PX[0]

def _ray(u, v):
    pos, right, upc, f = C.basis(DCAM)
    sx, sy = DCAM['shift']
    xr = (u - CW / 2 - sx * CW) / FPX
    yr = (CH / 2 - sy * CW - v) / FPX
    return pos, f + right * xr + upc * yr

def on_z(u, v, z):
    p, d = _ray(u, v); t = (z - p[2]) / d[2]; return p + t * d

def on_y(u, v, y=0.0):
    p, d = _ray(u, v); t = (y - p[1]) / d[1]; return p + t * d

def axis_z(row):
    zs = np.linspace(-0.02, 0.26, 1200)
    _, v, _ = C.project(np.c_[np.zeros_like(zs), np.zeros_like(zs), zs], DCAM, CW, CH)
    return float(np.interp(-row, -v, zs))

def px_r(r_px, z):
    _, _, d = C.project(np.array([[0, 0, z]]), DCAM, CW, CH)
    return r_px * d[0] / FPX

clear_scene()
b = Builder(ID, seed=10)
rng = np.random.default_rng(1010)
d2r = math.radians

# ------------------------------------------------------------------ plinth (crooked tapered box)
# bottom corners on the ground from the concept corners; top corners on the plane of their own height
bFL, bFR = on_z(455, 937, 0), on_z(925, 970, 0)
H_FL, H_FR, H_BR, H_BL = 0.0556, 0.0549, 0.0532, 0.0540
tFL, tFR = on_z(473, 712, H_FL), on_z(910, 745, H_FR)
# depth direction: mean of the concept's bottom and top right edges, so the box does not lean back
Dv = 0.3 * (on_z(1002, 868, 0) - bFR) + 0.7 * (on_z(996, 654, H_BR) - tFR)
Dv[2] = 0.0
Dv *= max(1.0, 0.073 / np.linalg.norm(Dv))
bBR, bBL = bFR + Dv, bFL + Dv
tBR, tBL = tFR + Dv * 1.0, tFL + Dv * 1.0 + np.array([0.002, 0.0, 0.0])
tBR[2], tBL[2] = H_BR, H_BL
PL = [bFL, bFR, bBR, bBL, tFL, tFR, tBR, tBL]
b.add_bmesh_geometry('plinth', [tuple(p) for p in PL],
                     [(3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)])
PLINTH_TOP = (H_FL + H_FR + H_BR + H_BL) / 4

# ------------------------------------------------------------------ plaque: thin plate raised off the front face
fn = Vector(np.cross(bFR - bFL, tFL - bFL)).normalized()
if fn.y > 0:
    fn = -fn
def on_front(u, v):
    p, d = _ray(u, v)
    n = np.array(fn); t = n.dot(bFL - p) / n.dot(d)
    return Vector(p + t * d)
PQ = [on_front(537, 898), on_front(853, 906), on_front(856, 762), on_front(540, 745)]   # BL BR TR TL
PQ_T = 0.0026
back = [p - fn * 0.002 for p in PQ]
front = [p + fn * PQ_T for p in PQ]
b.add_bmesh_geometry('plaque', [tuple(p) for p in back + front],
                     [(3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)])

# ------------------------------------------------------------------ cup: (part, radius px, ring-centre row px, centre col px)
FOOT = [('foot', 146, 678, 746), ('foot', 151, 671, 746), ('foot', 150, 650, 746), ('foot', 116, 630, 747),
        ('foot', 108, 625, 747), ('foot', 106, 617, 747), ('foot', 82, 614, 748), ('foot', 0, 613, 748)]
STEM = [('stem', 84, 616, 748), ('stem', 77, 600, 749), ('stem', 72, 577, 749), ('stem', 70, 552, 750),
        ('stem', 72, 528, 750), ('stem', 75, 515, 751), ('stem', 0, 505, 751)]
BOWL = [('cup', 0, 505, 751), ('cup', 84, 504, 751), ('cup', 108, 497, 751), ('cup', 116, 486, 751),
        ('cup', 124, 474, 752), ('cup', 140, 462, 752), ('cup', 160, 446, 753), ('cup', 205, 425, 754), ('cup', 246, 402, 755),
        ('cup', 263, 388, 756), ('cup', 264, 368, 755), ('cup', 276, 300, 757), ('cup', 294, 230, 760),
        ('cup', 300, 198, 761),
        ('rim', 302, 178, 762), ('rim', 317, 173, 763), ('rim', 318, 142, 764), ('rim', 308, 132, 764),
        ('rim', 290, 130, 764), ('rim', 283, 140, 764),
        ('inside', 276, 175, 763), ('inside', 262, 250, 761), ('inside', 228, 330, 758), ('inside', 170, 392, 755),
        ('inside', 90, 420, 753), ('inside', 0, 426, 752)]

def lathe_px(rings_px, sides, rot, jit=0.0006):
    prof = []
    for _, rp, row, col in rings_px:
        z = axis_z(row)
        prof.append((px_r(rp, z) if rp > 0 else 0.0, z))
    rings = b.lathe(rings_px[0][0], prof, sides=sides, rot=rot, cap_bottom=False, cap_top=False)
    ring_of = {v: ri for ri, r in enumerate(rings) for v in r}
    for f in b.bm.faces:
        if all(v in ring_of for v in f.verts):
            ri = max(ring_of[v] for v in f.verts)
            f.material_index = b.part(rings_px[ri][0])
    for ri, ring in enumerate(rings):
        dx = px_r(rings_px[ri][3] - AXIS_COL, prof[ri][1])
        for v in ring:
            v.co.x += dx
            if len(ring) > 1:
                v.co += Vector(rng.uniform(-jit, jit, 3))
    return rings, prof

# foot: flat bottom ring sits exactly on the plinth top (embedded 1 mm)
foot_rings, foot_prof = lathe_px(FOOT, 8, d2r(22.5 + 17))
z0 = foot_prof[0][1]
for ri, ring in enumerate(foot_rings):
    for v in ring:
        if ri == 0:
            v.co.z = PLINTH_TOP - 0.001
b.cap(foot_rings[0], b.parts.index('foot'), flip=True)
stem_rings, _ = lathe_px(STEM, 8, d2r(17))
ROT_BOWL = d2r(15)
bowl_rings, bowl_prof = lathe_px(BOWL, 12, ROT_BOWL, jit=0.0005)
# rim: a dip at the front right (as painted), the rim plane a touch higher at the back left
RIM_FROM = [i for i, r in enumerate(BOWL) if r[0] == 'rim'][0]
for ri, ring in enumerate(bowl_rings):
    if ri < RIM_FROM - 1 or len(ring) == 1:
        continue
    g = 1.0 if RIM_FROM <= ri <= RIM_FROM + 6 else 0.4
    for k, v in enumerate(ring):
        phi = ROT_BOWL + 2 * math.pi * k / 12
        dphi = (phi - d2r(-45) + math.pi) % (2 * math.pi) - math.pi
        v.co.z -= 0.0068 * g * math.exp(-(dphi / d2r(16)) ** 2)
        v.co.z += 0.0009 * g * math.sin(phi)
# the concept shows the rim ellipse more open than the camera explains: tilt the rim toward the viewer (front lower,
# back kept where the silhouette is)
TILT_W = {}
for ri, r_ in enumerate(BOWL):
    TILT_W[ri] = 1.0 if r_[0] in ('rim',) else 0.0
TILT_W[RIM_FROM - 1] = 0.75; TILT_W[RIM_FROM - 2] = 0.3
first_in = [i for i, r_ in enumerate(BOWL) if r_[0] == 'inside'][0]
TILT_W[first_in] = 0.8; TILT_W[first_in + 1] = 0.35
PHI_F = d2r(-73)
for ri, ring in enumerate(bowl_rings):
    w = TILT_W.get(ri, 0.0)
    if w == 0 or len(ring) == 1:
        continue
    for k, v in enumerate(ring):
        phi = ROT_BOWL + 2 * math.pi * k / 12
        v.co.z -= 0.0035 * w * ((1 + math.cos(phi - PHI_F)) / 2) ** 2

# ------------------------------------------------------------------ handles: chamfered straps in the y=0 plane
def strap(part, path, a, bd, cham=0.5):
    mi = b.part(part, True)
    Y = Vector((0, 1, 0))
    sec = [(1, cham), (cham, 1), (-cham, 1), (-1, cham), (-1, -cham), (-cham, -1), (cham, -1), (1, -cham)]
    rs = []
    n = len(path)
    for i, p in enumerate(path):
        if i == 0:
            t = (path[1] - path[0]).normalized(); sc = 1.0
        elif i == n - 1:
            t = (path[-1] - path[-2]).normalized(); sc = 1.0
        else:
            t0 = (path[i] - path[i - 1]).normalized(); t1 = (path[i + 1] - path[i]).normalized()
            t = (t0 + t1).normalized(); sc = 1.0 / max(0.6, t0.dot(t))
        e1 = Y.cross(t).normalized()
        pts = [p + e1 * (x * a * sc) + Y * (y * bd) for x, y in sec]
        pts = [q + Vector(rng.uniform(-3e-4, 3e-4, 3)) for q in pts]
        rs.append(b.ring_verts(pts))
    for r0, r1 in zip(rs, rs[1:]):
        b.bridge(r0, r1, mi)
    b.cap(rs[0], mi, flip=True); b.cap(rs[-1], mi)
    return rs

H_L = [(470, 178), (448, 134), (426, 104), (372, 101), (339, 134), (337, 195), (348, 248), (367, 299), (401, 341),
       (448, 371), (502, 388), (566, 410)]
H_R = [(1052, 178), (1082, 132), (1104, 103), (1156, 100), (1192, 132), (1189, 196), (1178, 246), (1158, 296),
       (1128, 334), (1086, 364), (1032, 384), (954, 410)]
for nm, pth in (('L', H_L), ('R', H_R)):
    path = [Vector(on_y(u, v, 0.0)) for u, v in pth]
    strap('handle', path, a=0.0066, bd=0.0072)

obj = b.finish(center_xy=False)
fit = check_fit(obj)
print('fit', fit)

BK.unwrap(obj, angle=55)
cam_path = os.path.join(HERE, 'camera.json')
cam = json.load(open(cam_path)) if os.path.exists(cam_path) and '--reset-cam' not in sys.argv else json.loads(json.dumps(DCAM))
if not os.path.exists(cam_path):
    json.dump(cam, open(cam_path, 'w'), indent=1)
if '--opt' in sys.argv:
    cam, s = BK.optimize_camera(obj, CONCEPT, cam, keys=tuple(os.environ.get('OPTKEYS', 'dist,shift').split(',')))
    json.dump(cam, open(cam_path, 'w'), indent=1)
if '--rows' in sys.argv:
    rgb_, cm_ = BK.load_concept(CONCEPT)
    mm_ = BK.model_mask(obj, cam, CW, CH)
    for y in range(60, 1000, 20):
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
if '--align-only' in sys.argv:
    sys.exit()

# ------------------------------------------------------------------ texture
size = BK.texture_size_for(obj)
parts = list(obj['parts'])
rgb, cmask = BK.load_concept(CONCEPT)
P, UV, part, N = BK.mesh_arrays(obj)

# ownership map at the concept camera (which part each concept pixel shows)
idb0, _ = BK.rasterize(P, part, cam, CW, CH)
own = np.where((idb0 >= 0) & cmask, part[np.maximum(idb0, 0)], -1)

# de-lit concept for the hidden-side views: remove the large-scale light gradient per material group, so rotated /
# mirrored copies do not repeat the front's highlight and shadow side
lab = cv2.cvtColor(rgb, cv2.COLOR_RGB2LAB).astype(np.float32)
L = lab[..., 0].copy()
groups = {'brass': [p for p in ('foot', 'stem', 'cup', 'rim', 'handle', 'plaque') if p in parts], 'wood': ['plinth'],
          'inside': ['inside']}
for g, plist in groups.items():
    m = np.isin(own, [parts.index(p) for p in plist]).astype(np.float32)
    if m.sum() < 100:
        continue
    num = cv2.GaussianBlur(L * m, (0, 0), 38); den = cv2.GaussianBlur(m, (0, 0), 38)
    low = num / np.maximum(den, 1e-4)
    mean = float((L * m).sum() / m.sum())
    L = np.where(m > 0, L - 0.7 * (low - mean), L)
lab[..., 0] = np.clip(L, 0, 255)
delit = cv2.cvtColor(lab.astype(np.uint8), cv2.COLOR_LAB2RGB)
alpha = (cmask * 255).astype(np.uint8)
delit_png = os.path.join(WORK, 'concept_delit.png')
cv2.imwrite(delit_png, np.dstack([cv2.cvtColor(delit, cv2.COLOR_RGB2BGR), alpha]))
concept_png = os.path.join(WORK, 'concept_alpha.png')
cv2.imwrite(concept_png, np.dstack([cv2.cvtColor(rgb, cv2.COLOR_RGB2BGR), alpha]))

def kpal(box, k=5, src=rgb):
    y0, y1, x0, x1 = box
    reg = src[y0:y1, x0:x1].reshape(-1, 3)
    msk = cmask[y0:y1, x0:x1].reshape(-1)
    pal, w = BK._kmeans_palette(reg[msk].astype(np.float32), k)
    pal = sorted([tuple(map(float, c)) for c in pal], key=lambda c: c[0] * .3 + c[1] * .59 + c[2] * .11)
    return pal

# A: the concept as seen (front)
texA, stA = BK.bake(obj, concept_png, cam, views=[{'name': 'orig', 'T': np.eye(4), 'bonus': 0.3}], size=size,
                    out_png=os.path.join(WORK, 'texA.png'), debug_png=os.path.join(WORK, 'covA.png'), min_facing=0.45)
covA = np.all(cv2.imread(os.path.join(WORK, 'covA.png'))[:, :, ::-1] == np.array([60, 200, 80]), axis=2)
covA = ndimage.binary_closing(covA, structure=np.ones((3, 3)), iterations=1)

# B: everything from the de-lit concept with rotated / mirrored copies for the hidden sides
AXC = (px_r(751 - AXIS_COL, 0.1), 0.0)
LATHE = ['foot', 'stem', 'cup', 'rim']
viewsB = [{'name': 'orig', 'T': np.eye(4), 'bonus': 0.3},
          {'name': 'plinth_top', 'T': np.eye(4), 'parts': ['plinth'], 'min_facing': 0.2, 'bonus': 0.0}]
viewsB += [{'name': f'rot{a}', 'T': BK.T_rot_z(a, AXC), 'parts': LATHE} for a in (40, -40, 80, -80, 120, -120, 160, -160, 180)]
viewsB += [{'name': f'in{a}', 'T': BK.T_rot_z(a, AXC), 'parts': ['inside']} for a in (30, -30, 60, -60, 90, -90, 120, -120, 150, -150, 180)]
viewsB += [{'name': 'h_mirror_y', 'T': BK.T_mirror_plane('y'), 'parts': ['handle']}]
# plinth frame: back face -> visible right side face (rotate -90 and rescale), left face -> right face (rotate 180)
ex = (bFR - bFL); Wd = np.linalg.norm(ex); ex = ex / Wd
ez = np.array([0, 0, 1.0]); ey = np.cross(ez, ex)
Dd = abs(np.dot(bBR - bFR, ey))
cpl = (bFL + bFR + bBR + bBL) / 4
M = np.eye(4); M[:3, 0], M[:3, 1], M[:3, 2], M[:3, 3] = ex, ey, ez, cpl
Mi = np.linalg.inv(M)
Lb = np.eye(4); Lb[:2, :2] = [[0, Wd / Dd], [-Dd / Wd, 0]]
Ll = np.eye(4); Ll[:2, :2] = [[-1, 0], [0, -1]]
viewsB += [{'name': 'pl_back', 'T': M @ Lb @ Mi, 'parts': ['plinth'], 'bonus': -0.2},
           {'name': 'pl_left', 'T': M @ Ll @ Mi, 'parts': ['plinth'], 'bonus': -0.1}]
INSIDE_COLS = kpal((95, 175, 560, 960), 5)
BRASS = [(124, 85, 42), (146, 99, 47), (160, 109, 50), (172, 118, 52), (186, 131, 57)]
PART_COLS = {'inside': INSIDE_COLS, 'plinth': kpal((780, 920, 470, 530), 5), 'cup': BRASS, 'rim': BRASS, 'foot': BRASS,
             'stem': BRASS, 'handle': BRASS, 'plaque': BRASS}
WOOD_DARK = [tuple(np.array(c) * 0.62) for c in kpal((780, 920, 470, 530), 4)]
texB, stB = BK.bake(obj, delit_png, cam, views=viewsB, size=size, out_png=os.path.join(WORK, 'texB.png'),
                    debug_png=os.path.join(OUT, 'coverage.png'), min_facing=0.35,
                    part_colors=PART_COLS)
print('views B', stB['views'])

# harmonise the hidden-side bake to the front's low-frequency colour near the boundary
texA_f, texB_f = texA.astype(np.float32), texB.astype(np.float32)
def _lowpass(img, m, sig=7):
    num = cv2.GaussianBlur(img * m[..., None], (0, 0), sig)
    den = cv2.GaussianBlur(m.astype(np.float32), (0, 0), sig)[..., None]
    return num / np.maximum(den, 1e-4), den[..., 0]
mA, wA = _lowpass(texA_f, covA.astype(np.float32))
mB, wB = _lowpass(texB_f, (~covA).astype(np.float32))
ratio = np.clip(mA / np.maximum(mB, 1.0), 0.7, 1.4)
dist = ndimage.distance_transform_edt(~covA)
fade = np.exp(-dist / 14.0) * np.clip(wA / 0.08, 0, 1)
tex = np.where(covA[..., None], texA_f, texB_f * (1 + (ratio - 1) * fade[..., None]))

# texel -> 3D position / normal / part
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
edge_bad = (l0r < -0.01) | (l1r < -0.01) | (1 - l0r - l1r < -0.01)
l0, l1 = np.clip(l0r, 0, 1), np.clip(l1r, 0, 1)
l2 = np.clip(1 - l0 - l1, 0, 1)
ls = l0 + l1 + l2; l0, l1, l2 = l0 / ls, l1 / ls, l2 / ls
X = P[tt, 0] * l0[:, None] + P[tt, 1] * l1[:, None] + P[tt, 2] * l2[:, None]

def block_noise(n, block, seed):
    r = np.random.default_rng(seed)
    g = r.random((n // block + 2, n // block + 2))
    return np.kron(g, np.ones((block, block)))[:n, :n]
def pick(pal, t):
    pal = np.asarray(pal, np.float32)
    return pal[np.clip((t * len(pal)).astype(int), 0, len(pal) - 1)]
nz1 = block_noise(size, 2, 11); nz2 = block_noise(size, 5, 12); nz3 = block_noise(size, 8, 13)
tv = np.clip(0.55 * nz1[tys, txs] + 0.45 * nz2[tys, txs], 0, 0.999)

# rim top: clean light-gold band all round (grazing in the concept)
RIM_TOP = kpal((66, 84, 640, 880), 5)
m_top = (tp == parts.index('rim')) & (tn[:, 2] > 0.5)
tex[tys[m_top], txs[m_top]] = pick(RIM_TOP[1:4], tv[m_top])
# underside of the rolled lip: in shadow (the concept shows a dark line under the rim)
m_und = (tp == parts.index('rim')) & (tn[:, 2] < -0.25)
tex[tys[m_und], txs[m_und]] = pick([(70, 46, 24), (84, 56, 28), (98, 66, 32)], tv[m_und])
# inside: concept back wall where seen head-on, darker with depth, chunky blocks
m_in = tp == parts.index('inside')
ztop, zbot = X[m_in, 2].max(), X[m_in, 2].min()
depth = np.clip((ztop - X[:, 2]) / max(ztop - zbot, 1e-6), 0, 1)
shade = (1.0 - 0.55 * depth ** 1.2)[:, None]
fill_in = pick(INSIDE_COLS, np.clip(0.55 + 0.3 * (nz3[tys, txs] - 0.5) + 0.2 * (tv - 0.5), 0, 0.999))
cur = tex[tys, txs]
use_c = covA[tys, txs]
tex[tys[m_in], txs[m_in]] = (np.where(use_c[:, None], cur, 0.5 * cur + 0.5 * fill_in) * np.where(use_c[:, None], 1.0, shade))[m_in]
# plinth bottom: plain dark wood
m_bot = (tp == parts.index('plinth')) & (tn[:, 2] < -0.5)
tex[tys[m_bot], txs[m_bot]] = pick(WOOD_DARK, tv[m_bot])
# plaque edges: plain darker brass (thin grazing faces otherwise pick up dotted samples)
PLQ_EDGE = [tuple(np.array(c) * 0.8) for c in kpal((770, 890, 560, 840), 4)]
m_pe = (tp == parts.index('plaque')) & (tn @ np.array(fn) < 0.8)
tex[tys[m_pe], txs[m_pe]] = pick(PLQ_EDGE, tv[m_pe])

# slight lift of the brass so the matte render keeps the concept's warm gold
m_br = np.isin(tp, [parts.index(p) for p in ('foot', 'stem', 'cup', 'rim', 'handle', 'plaque')])
tex[tys[m_br], txs[m_br]] *= 1.07
have = np.zeros((size, size), bool); have[tys[~edge_bad], txs[~edge_bad]] = True
_, inds = ndimage.distance_transform_edt(~have, return_indices=True)
tex = tex[inds[0], inds[1]]
tex = np.clip(tex, 0, 255).astype(np.uint8)
tex = BK._quantize(tex, 56)
tex_path = os.path.join(OUT, f'T_{ID}.png')
cv2.imwrite(tex_path, cv2.cvtColor(tex, cv2.COLOR_RGB2BGR))

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
