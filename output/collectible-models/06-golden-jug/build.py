"""06 Golden jug (v2) - collectible shelf item.
Run: python3 build.py [--opt] [--align-only]

Geometry is traced from the concept: lathe rings are given as (radius px, ring-centre row px) measured on the concept
silhouette, the handle centreline as concept pixels, and both are converted to metres with a fixed design camera.
Texture: concept projection (front) + scuff-free concept for rotated copies (sides/back) + painted rim top, inside
and new scuffs on the hidden side, so nothing is visibly mirrored.
"""
import sys, os, json, math
sys.path.insert(0, '/home/claude/kit')
import numpy as np
import cv2
from scipy import ndimage
import bpy
from mathutils import Vector
from geo import Builder, clear_scene, check_fit
import bake as BK
import render as R
import cam as C

HERE = os.path.dirname(os.path.abspath(__file__))
CONCEPT = '/home/claude/concepts/collectible-concepts/06-golden-jug-v1.png'
ID = 'CI06_GoldenJug'
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)
WORK = os.path.join(HERE, 'work'); os.makedirs(WORK, exist_ok=True)
CW, CH = 1536, 1024

# ------------------------------------------------------------------ design camera (pixel -> metre mapping)
DCAM = {'az': 8.0, 'el': 12.0, 'dist': 1.2276, 'fov': 18, 'target': [0, 0, 0.095], 'shift': [0, 0], 'roll': 0}
def _solve_shift(cam, uv=(711, 892)):
    u, v, _ = C.project(np.array([[0, 0, 0.0]]), cam, CW, CH)
    cam['shift'] = [(uv[0] - u[0]) / CW, -(uv[1] - v[0]) / CW]
    return cam
DCAM = _solve_shift(DCAM)
FPX = (max(CW, CH) / 2) / math.tan(math.radians(DCAM['fov']) / 2)

def axis_z(a):
    """Concept row of a ring centre -> z (m) on the design camera."""
    zs = np.linspace(-0.02, 0.26, 600)
    _, v, _ = C.project(np.c_[np.zeros_like(zs), np.zeros_like(zs), zs], DCAM, CW, CH)
    return float(np.interp(-a, -v, zs))

def px_r(r_px, z):
    _, _, d = C.project(np.array([[0, 0, z]]), DCAM, CW, CH)
    return r_px * d[0] / FPX

def unproject_y0(u, v):
    pos, right, upc, f = C.basis(DCAM)
    big = max(CW, CH); sx, sy = DCAM['shift']
    xr = (u - CW / 2 - sx * big) / FPX
    yr = (CH / 2 - sy * big - v) / FPX
    d = f + right * xr + upc * yr
    t = -pos[1] / d[1]
    return pos + t * d

# ------------------------------------------------------------------ geometry
clear_scene()
b = Builder(ID, seed=6)
SIDES = 12
# (part, radius px, ring-centre row px) bottom -> rim -> inside
RINGS = [   # (part, radius px, ring-centre row px, centre column px)
    ('body', 205, 892, 706),   # 0 foot bottom
    ('body', 214, 885, 706),   # 1 foot bevel
    ('body', 216, 846, 706),   # 2 foot top
    ('body', 207, 844, 706),   # 3 foot ledge
    ('body', 224, 839, 706),   # 4 underside start
    ('body', 303, 790, 708),   # 5 lower ring
    ('body', 360, 650, 721),   # 6 widest
    ('body', 333, 522, 728),   # 7 shoulder
    ('body', 280, 450, 728),   # 8 upper shoulder (the concept's shoulder is fuller on the handle side)
    ('body', 194, 405, 725),   # 9 shoulder top (tucked under the collar)
    ('body', 202, 403, 722),   # 10 collar bottom
    ('body', 200, 369, 722),   # 11 collar top
    ('body', 186, 364, 722),   # 12 collar bevel
    ('body', 179, 360, 721),   # 13 neck base
    ('neck', 177, 305, 721),   # 14 neck waist
    ('neck', 183, 268, 720),   # 15 flare
    ('neck', 191, 243, 720),   # 16 flare high
    ('lip', 203, 206, 719),    # 17 under the rolled lip
    ('lip', 213, 201, 719),    # 18 lip outer bottom
    ('lip', 214, 185, 719),    # 19 lip outer top
    ('lip', 207, 179, 719),    # 20 lip top (outer)
    ('lip', 190, 179, 719),    # 21 lip top (inner)
    ('lip', 183, 185, 719),    # 22 inner lip edge
    ('inside', 173, 202, 719), # 23
    ('inside', 165, 240, 720), # 24
    ('inside', 159, 290, 721), # 25
    ('inside', 0, 298, 721),   # 26 inner floor
]
prof = []
for _, rp, a, _c in RINGS:
    z = axis_z(a)
    prof.append((px_r(rp, z) if rp > 0 else 0.0, z))
LEAN = (0.0, 0.0)
ROT = math.radians(15)                     # vertices at 15+30k deg: the spout gets a blunt two-vertex tip at 180
rings = b.lathe('body', prof, sides=SIDES, jitter=0.0, rot=ROT, cap_top=False, lean=LEAN)
# part per band (band i..i+1 -> part of ring i+1)
ring_of = {}
for ri, ring in enumerate(rings):
    for v in ring:
        ring_of[v] = ri
for f in b.bm.faces:
    ri = max(ring_of[v] for v in f.verts)
    if ri == 0:
        continue
    f.material_index = b.part(RINGS[ri][0])

# per-ring centre offsets (slight lean + fuller shoulder on the handle side, traced from the concept)
for ri, ring in enumerate(rings):
    dx = px_r(RINGS[ri][3] - 711, prof[ri][1])
    for v in ring:
        v.co.x += dx
# rim: rises toward the back (+Y), dips a little at the front - as painted in the concept
G_RIM = {15: 0.15, 16: 0.4, 17: 0.8, 18: 1, 19: 1, 20: 1, 21: 1, 22: 1, 23: 0.8, 24: 0.35, 25: 0.1}
# spout: a soft pulled-out lip toward -X, pinched at the sides, the neck below it bulges with it
G_SP = {14: 0.05, 15: 0.18, 16: 0.5, 17: 0.85, 18: 1, 19: 1, 20: 1, 21: 1, 22: 1, 23: 0.8, 24: 0.3, 25: 0.05}
SP_LEN, SP_SIG = 0.0155, math.radians(38)
for ri, ring in enumerate(rings):
    if len(ring) == 1:
        continue
    for k, v in enumerate(ring):
        phi = ROT + 2 * math.pi * k / SIDES
        s = math.sin(phi)
        c_ = math.cos(phi)
        v.co.z += G_RIM.get(ri, 0) * ((0.0095 * s if s > 0 else 0.0045 * s) + 0.0045 * max(0.0, math.cos(phi - math.radians(70))) ** 2
                                   + 0.004 * max(0.0, s) * max(0.0, -c_))
        d = (phi - math.pi + math.pi) % (2 * math.pi) - math.pi
        w = math.exp(-(d / SP_SIG) ** 2) * G_SP.get(ri, 0)
        if w > 1e-3:
            v.co.x -= SP_LEN * w
            v.co.y *= (1 - 0.35 * w)
            v.co.z -= 0.008 * w
# hand-made crookedness: small per-vertex jitter (not on the foot bottom, so it stands flat)
rng = np.random.default_rng(66)
for ri, ring in enumerate(rings):
    amp = 0.0 if ri == 0 else (0.0005 if 17 <= ri <= 22 else 0.0011)
    for v in ring:
        v.co.x += rng.uniform(-amp, amp); v.co.y += rng.uniform(-amp, amp); v.co.z += rng.uniform(-amp, amp) * 0.6

# handle: angular strap in the y=0 plane, traced from the concept (centreline pixels)
H_PX = [(872, 312), (903, 290), (958, 255), (1004, 231), (1050, 210), (1099, 214), (1140, 249), (1153, 300),
        (1152, 362), (1139, 404), (1103, 451), (1071, 498), (1038, 548), (1020, 572)]
path = [Vector(unproject_y0(u, v)) for u, v in H_PX]
for p in path:
    p.y = 0.0

def strap(part, path, a, bd, cham=0.55, caps=True):
    """Sweep a chamfered rectangle (8 sides) along a path in the XZ plane: a = in-plane half thickness,
    bd = half depth along Y. Mitred so the strap keeps its thickness at the bends."""
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
        pts = [q + Vector((rng.uniform(-4e-4, 4e-4), rng.uniform(-3e-4, 3e-4), rng.uniform(-4e-4, 4e-4))) for q in pts]
        rs.append(b.ring_verts(pts))
    for r0, r1 in zip(rs, rs[1:]):
        b.bridge(r0, r1, mi)
    if caps:
        b.cap(rs[0], mi, flip=True); b.cap(rs[-1], mi)
    return rs
strap('handle', path, a=0.0121, bd=0.0088)

obj = b.finish(center_xy=False)
fit = check_fit(obj)
print('fit', fit)

BK.unwrap(obj, angle=60)
cam_path = os.path.join(HERE, 'camera.json')
cam = json.load(open(cam_path)) if os.path.exists(cam_path) and '--reset-cam' not in sys.argv else json.loads(json.dumps(DCAM))
if '--opt' in sys.argv:
    cam, s = BK.optimize_camera(obj, CONCEPT, cam, keys=tuple(os.environ.get('OPTKEYS', 'az,dist,shift').split(',')))
    json.dump(cam, open(cam_path, 'w'), indent=1)
score = BK.align_report(obj, CONCEPT, cam, os.path.join(OUT, 'align.png'))
print('IoU', score, cam)
if '--align-only' in sys.argv:
    sys.exit()

# ------------------------------------------------------------------ texture
size = BK.texture_size_for(obj)
rgb, cmask = BK.load_concept(CONCEPT)
hsv = cv2.cvtColor(cv2.cvtColor(rgb, cv2.COLOR_RGB2BGR), cv2.COLOR_BGR2HSV).astype(int)

# scuffs = desaturated grey-beige blotches on the body; a scuff-free copy feeds the rotated (hidden side) views
inner = ndimage.binary_erosion(cmask, iterations=6)
sc = inner & (hsv[..., 1] < 150) & (hsv[..., 2] < 215) & (hsv[..., 2] > 90)
sc = ndimage.binary_opening(sc, iterations=1)
lab, n = ndimage.label(sc)
sizes = ndimage.sum(sc, lab, range(1, n + 1))
scuff_ids = []
for i, s in enumerate(sizes):
    ys_, xs_ = np.nonzero(lab == i + 1)
    if s > 150 and ys_.min() > 300:        # body scuffs only (not the rim)
        scuff_ids.append(i + 1)
scuffs = np.isin(lab, scuff_ids)
clean = cv2.inpaint(cv2.cvtColor(rgb, cv2.COLOR_RGB2BGR), ndimage.binary_dilation(scuffs, iterations=5).astype(np.uint8) * 255,
                    9, cv2.INPAINT_TELEA)
clean_png = os.path.join(WORK, 'concept_clean.png')
cv2.imwrite(clean_png, clean)

# A: the concept as seen (front)
texA, stA = BK.bake(obj, CONCEPT, cam, views=[{'name': 'orig', 'T': np.eye(4), 'bonus': 0.3}], size=size,
                    out_png=os.path.join(WORK, 'texA.png'), debug_png=os.path.join(WORK, 'covA.png'), min_facing=0.45)
covA = np.all(cv2.imread(os.path.join(WORK, 'covA.png'))[:, :, ::-1] == np.array([60, 200, 80]), axis=2)
# texels that fall between two triangles in the rasteriser are not 'covered' in the debug map: close those 1-texel
# gaps so the front never shows thin lines from the hidden-side bake
covA = ndimage.binary_closing(covA, structure=np.ones((3, 3)), iterations=1)
# B: hidden sides from the scuff-free concept
INSIDE_COLS = [(52, 30, 14), (74, 44, 20), (98, 60, 28), (124, 80, 38), (150, 100, 46)]
viewsB = [{'name': 'orig', 'T': np.eye(4), 'bonus': 0.3}]
viewsB += [{'name': f'rot{a}', 'T': BK.T_rot_z(a), 'parts': ['body']} for a in (60, -60, 120, -120, 180)]
# neck and lip are not rotationally symmetric (raised back rim, spout): their hidden texels take the palette fill
viewsB += [{'name': 'mirror_y', 'T': BK.T_mirror_plane('y'), 'parts': ['handle']}]
texB, stB = BK.bake(obj, clean_png, cam, views=viewsB, size=size, out_png=os.path.join(WORK, 'texB.png'),
                    debug_png=os.path.join(OUT, 'coverage.png'), min_facing=0.35, part_colors={'inside': INSIDE_COLS})
# harmonise: near the edge of the front (concept) region, scale the hidden-side bake's low-frequency colour to
# the front's, fading out over ~14 texels, so view seams do not show as light/dark facet columns
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
texB_h = texB_f * (1 + (ratio - 1) * fade[..., None])
tex = np.where(covA[..., None], texA_f, texB_h)

# texel -> 3D position / normal / part (same rasterisation as bake.py)
P, UV, part, N = BK.mesh_arrays(obj)
parts = list(obj['parts'])
tri_img = np.full((size, size), -1, np.int32)
uvp = UV * size; uvp[..., 1] = size - uvp[..., 1]
for t in range(len(P)):
    cv2.fillPoly(tri_img, [np.round(uvp[t] * 16).astype(np.int32)], int(t), lineType=cv2.LINE_8, shift=4)
tys, txs = np.nonzero(tri_img >= 0)
tt = tri_img[tys, txs]
cen = P[tt].mean(1)          # face-ish position is enough for painting decisions
tn = N[tt]
tp = part[tt]
# precise texel position (barycentric)
a0, a1, a2 = uvp[tt, 0], uvp[tt, 1], uvp[tt, 2]
px_, py_ = txs + 0.5, tys + 0.5
den = (a1[:, 1] - a2[:, 1]) * (a0[:, 0] - a2[:, 0]) + (a2[:, 0] - a1[:, 0]) * (a0[:, 1] - a2[:, 1])
den = np.where(np.abs(den) < 1e-12, 1e-12, den)
l0r = ((a1[:, 1] - a2[:, 1]) * (px_ - a2[:, 0]) + (a2[:, 0] - a1[:, 0]) * (py_ - a2[:, 1])) / den
l1r = ((a2[:, 1] - a0[:, 1]) * (px_ - a2[:, 0]) + (a0[:, 0] - a2[:, 0]) * (py_ - a2[:, 1])) / den
# texel centres just outside their triangle (rasteriser edge pixels): bake.py clips their barycentrics without
# renormalising, which samples the concept at a wrong spot -> dotted lines along triangle edges. Re-fill them.
edge_bad = (l0r < -0.01) | (l1r < -0.01) | (1 - l0r - l1r < -0.01)
l0, l1 = np.clip(l0r, 0, 1), np.clip(l1r, 0, 1)
l2 = np.clip(1 - l0 - l1, 0, 1)
ls = l0 + l1 + l2; l0, l1, l2 = l0 / ls, l1 / ls, l2 / ls
X = P[tt, 0] * l0[:, None] + P[tt, 1] * l1[:, None] + P[tt, 2] * l2[:, None]

def block_noise(shape, block, seed):
    r = np.random.default_rng(seed)
    g = r.random((shape // block + 2, shape // block + 2))
    return np.kron(g, np.ones((block, block)))[:shape, :shape]

def pick(pal, t):
    pal = np.asarray(pal, np.float32)
    return pal[np.clip((t * len(pal)).astype(int), 0, len(pal) - 1)]

nz1 = block_noise(size, 3, 11); nz2 = block_noise(size, 6, 12)
# rim top: clean light-gold painted band all around (no grazing-angle streaks)
LIP_TOP = [(150, 98, 40), (178, 122, 50), (204, 148, 62), (222, 170, 80), (236, 192, 104)]
m_top = (tp == parts.index('lip')) & (tn[:, 2] > 0.55)
t_ = np.clip(0.55 * nz1[tys, txs] + 0.45 * nz2[tys, txs], 0, 0.999)
tex[tys[m_top], txs[m_top]] = pick(LIP_TOP, t_[m_top])
# underside of the rolled lip: in shadow (the concept shows a dark line under the rim)
LIP_UNDER = [(92, 56, 24), (112, 70, 30), (132, 86, 36)]
m_und = (tp == parts.index('lip')) & (tn[:, 2] < -0.25)
tex[tys[m_und], txs[m_und]] = pick(LIP_UNDER, t_[m_und])
# inside: dark painted gold, darker with depth, chunky blocks; keep the projected back wall where the concept
# shows it head-on
if 'inside' in parts:
    m_in = tp == parts.index('inside')
    zr = X[:, 2]
    ztop, zbot = zr[m_in].max(), zr[m_in].min()
    depth = np.clip((ztop - zr) / max(ztop - zbot, 1e-6), 0, 1)
    nz3 = block_noise(size, 8, 13)
    tval = np.clip(0.6 - 0.62 * depth + 0.28 * (nz3[tys, txs] - 0.5) + 0.14 * (nz2[tys, txs] - 0.5), 0, 0.999)
    painted = pick(INSIDE_COLS, tval)
    facing = np.einsum('ij,ij->i', tn, C.view_dir(X, cam))
    mm = m_in
    tex[tys[mm], txs[mm]] = painted[mm]

# new scuffs on the hidden side: stamp the concept's own scuff blots (cut out) at other places
body_i = parts.index('body')
spr = []
for sid in scuff_ids:
    ys_, xs_ = np.nonzero(lab == sid)
    y0, y1, x0, x1 = ys_.min() - 4, ys_.max() + 5, xs_.min() - 4, xs_.max() + 5
    m = ndimage.binary_dilation(lab[y0:y1, x0:x1] == sid, iterations=2)
    spr.append((rgb[y0:y1, x0:x1].astype(np.float32), m))
PPM = FPX / C.project(np.array([[0, 0, 0.08]]), cam, CW, CH)[2][0]   # concept px per metre
# (angle deg from +X, z m, sprite, flip) - back and sides only, avoiding the spout/handle lines
STAMPS = [(100, 0.066, 1, True), (135, 0.034, 3, False), (160, 0.096, 0, False), (205, 0.052, 2, True),
          (40, 0.046, 2, False), (72, 0.100, 3, True), (185, 0.020, 0, True)]
phi_t = np.degrees(np.arctan2(X[:, 1], X[:, 0])) % 360
rad_t = np.hypot(X[:, 0], X[:, 1])
for ang, zc, si, flip in STAMPS:
    img, m = spr[si % len(spr)]
    hh, ww = m.shape
    dphi = ((phi_t - ang + 180) % 360) - 180
    su = np.radians(dphi) * rad_t * PPM * (-1 if flip else 1) + ww / 2
    sv = (zc - X[:, 2]) * PPM + hh / 2
    sel = (tp == body_i) & (su >= 0) & (su < ww) & (sv >= 0) & (sv < hh) & ~covA[tys, txs]
    ui, vi = su[sel].astype(int), sv[sel].astype(int)
    on = m[vi, ui]
    idx = np.nonzero(sel)[0][on]
    tex[tys[idx], txs[idx]] = img[vi[on], ui[on]]

# gutters and between-triangle gaps take the nearest texel of the final paint
have = np.zeros((size, size), bool); have[tys[~edge_bad], txs[~edge_bad]] = True
print('edge texels refilled', int(edge_bad.sum()), 'of', len(tys))
_, inds = ndimage.distance_transform_edt(~have, return_indices=True)
tex = tex[inds[0], inds[1]]
tex = np.clip(tex, 0, 255).astype(np.uint8)
tex = BK._quantize(tex, 48)
tex_path = os.path.join(OUT, f'T_{ID}.png')
cv2.imwrite(tex_path, cv2.cvtColor(tex, cv2.COLOR_RGB2BGR))
st = {'A': stA, 'B': stB}
print('bake', json.dumps({'A': stA['covered_from_concept'], 'B': stB['views']}))

R.apply_texture(obj, tex_path, ID)
stats = check_fit(obj)
stats['texture'] = f'{size}x{size}'
stats['iou'] = round(score, 3)
if '--no-render' not in sys.argv:
    R.render_views(obj, os.path.join(OUT, 'preview.png'), concept_cam=cam, concept_size=(CW, CH), concept_path=CONCEPT)
    R.shelf_fit_render(obj, os.path.join(OUT, 'shelf.png'))
R.export(obj, OUT, ID, tex_path)
json.dump({'id': ID, 'stats': stats, 'bake': st, 'camera': cam}, open(os.path.join(OUT, 'stats.json'), 'w'), indent=1)
if '--no-render' not in sys.argv:
    R.card(OUT, ID, stats)
print('done', stats)
