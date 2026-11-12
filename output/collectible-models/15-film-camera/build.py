"""15 Old film camera - collectible shelf item. Run: python3 build.py [--opt] [--align-only] [--quick]

Masses (concept: front three-quarter from the front-left and a little above, fov ~15):
- worn metal top plate: crooked chamfered slab, taller at the right end (the concept draws it so), overhangs the
  leather a little; worn metal bottom plate with a dented (big chamfer) front-right corner
- dark leather body sleeve between the plates, inset ~2.5 mm, front-right bottom corner crumpled in
- lens: 12-sided stepped barrel lathed along -Y (big base ring, step, front bezel with a chamfer) and a recessed,
  slightly domed glass
- viewfinder: protruding rectangular frame on the top plate front (right) with a recessed window
- top: shutter collar (8 sides) + red button (10), advance lever (pivot + flat arm), big and small dials (10 sides)
Painted (concept projection): scuffs, worn orange edges, leather mottling, dial ticks, red lens marks, glass glint.
Painted after the bake: back film door (seams, hinge, latch), kept in the item's own colours.
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
from camgeo import CAM0, CW, CH, bp

CONCEPT = '/home/claude/concepts/collectible-concepts/15-film-camera-v2.png'
CONCEPT_A = os.path.join(HERE, 'concept_rgba.png')   # concept + clean alpha (tools/make_mask.py): the kit's
                                                   # colour-distance mask loses the grey metal against the grey bg
ID = 'CI15_FilmCamera'
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)
WORK = os.path.join(HERE, 'tmp'); os.makedirs(WORK, exist_ok=True)

# ------------------------------------------------------------------ dimensions (metres, pre-centering frame)
X0, X1 = -0.1000, 0.1045          # plate ends
Y0, Y1 = -0.0261, 0.0315          # plate front / back
HB = 0.0175                       # top of the bottom plate
HT = 0.0938                       # bottom of the top plate
ZTL, ZTR = 0.1185, 0.1280         # top plate top at the left / right end (the right end is taller)
INSET = 0.0026                    # leather inset from the plates

def ztop(x, y=0.0):
    return ZTL + (x - X0) / (X1 - X0) * (ZTR - ZTL) + 0.0012 * (y / Y1)   # a hair higher at the back


clear_scene()
b = Builder(ID, seed=15)


def foot(x0, x1, y0, y1, c):
    """CCW (from above) footprint with chamfers c = (FL, FR, BR, BL)."""
    fl, fr, br, bl = [max(v, 0.0012) for v in c]
    return [(x0 + fl, y0), (x1 - fr, y0), (x1, y0 + fr), (x1, y1 - br), (x1 - br, y1), (x0 + bl, y1), (x0, y1 - bl),
            (x0, y0 + fl)]


# ------------------------------------------------------------------ body: ONE closed shell of stacked 8-point rings
# bottom plate -> ledge in -> leather sleeve (inset) -> ledge out -> top plate. No hidden interior faces.
def ring_at(ft, zf):
    return [(x, y, zf(x, y)) for x, y in ft]

top_ft = foot(X0, X1, Y0, Y1, (0.0022, 0.0068, 0.0060, 0.0030))
bot_ft = foot(X0 + 0.0004, X1 - 0.0005, Y0 - 0.0002, Y1, (0.0022, 0.0125, 0.0050, 0.0050))
zhb = lambda x, y: HB + 0.0006 * (x - X0) / (X1 - X0)

def sleeve_ring(z, c_fr, push=0.0):
    ft = foot(X0 + 0.0012, X1 - INSET, Y0 + INSET - push, Y1 - INSET, (0.0018, c_fr, 0.0045, 0.0022))
    return [(x, y, z(x, y) if callable(z) else z) for x, y in ft]

L0, L1, L2 = sleeve_ring(zhb, 0.0110), sleeve_ring(0.036, 0.0060, 0.0004), sleeve_ring(HT, 0.0040)
# crumple: the front-right lower corner column pushed in, its crease a little higher
L0[1] = (L0[1][0] - 0.0012, L0[1][1] + 0.0006, L0[1][2])
L1[2] = (L1[2][0] - 0.0010, L1[2][1], L1[2][2] + 0.002)
RINGS = [ring_at(bot_ft, lambda x, y: 0.0), ring_at(bot_ft, zhb), L0, L1, L2,
         ring_at(top_ft, lambda x, y: HT), ring_at(top_ft, ztop)]
BAND_PART = ['plate_bot', 'plate_bot', 'leather', 'leather', 'plate_top', 'plate_top']
_rv = [b.ring_verts(r) for r in RINGS]
for (r0, r1), part in zip(zip(_rv, _rv[1:]), BAND_PART):
    b.bridge(r0, r1, b.part(part))
b.cap(_rv[0], b.part('plate_bot'), flip=True)
b.cap(_rv[-1], b.part('plate_top'))

# ------------------------------------------------------------------ lens (lathe along -Y)
LC = np.array([0.0012, Y0 + INSET, 0.0498])          # centre of the lens base on the leather front
# base barrel + flat front ring (red marks), dark groove, protruding front bezel with a chamfer, recessed glass
lens_prof = [(0.0425, -0.004), (0.0425, 0.0105), (0.0350, 0.0110), (0.0330, 0.0125), (0.0330, 0.0160),
             (0.0364, 0.0165), (0.0364, 0.0385), (0.0336, 0.0410), (0.0318, 0.0410), (0.0308, 0.0365)]
glass_prof = [(0.0308, 0.0365), (0.0200, 0.0382), (0.0, 0.0388)]
LENS_ROT = math.radians(15)
Mlens = Matrix.Translation(Vector(LC)) @ Matrix.Rotation(math.radians(90), 4, 'X')   # local +Z -> world -Y
for part, prof, cb in (('lens', lens_prof, False), ('glass', glass_prof, False)):
    rings = b.lathe(part, prof, sides=12, rot=LENS_ROT, cap_top=False, cap_bottom=cb, jitter=0.0002)
    for rg in rings:
        for v in rg:
            v.co = Mlens @ v.co
LENS_AX = np.array([0.0, -1.0, 0.0])

# ------------------------------------------------------------------ viewfinder frame + recessed window
VC = (0.0660, Y0, 0.1020)
def vrect(hw, hh, t, dz=0.0):
    x, y, z = VC
    return [(x - hw, y - t, z - hh + dz), (x + hw, y - t, z - hh + dz), (x + hw, y - t, z + hh + dz), (x - hw, y - t, z + hh + dz)]
vring = [vrect(0.0255, 0.0195, -0.0045), vrect(0.0255, 0.0195, 0.0052), vrect(0.0205, 0.0140, 0.0052, 0.0015),
         vrect(0.0198, 0.0133, 0.0012, 0.0015)]
b.loft('vframe', vring, closed_ends=(False, False))
b.add_bmesh_geometry('vglass', vring[-1], [(0, 1, 2, 3)])

# ------------------------------------------------------------------ top controls
def knob(part, cx, cy, prof, sides, rot):
    z0 = ztop(cx, cy)
    b.lathe(part, [(r, z0 + z) for r, z in prof], sides=sides, rot=rot, offset=(cx, cy, 0), jitter=0.00015, cap_bottom=False)
    return (cx, cy)

KNOBS = {}
KNOBS['collar'] = knob('collar', -0.0690, -0.0035, [(0.0205, -0.003), (0.0205, 0.0072), (0.0178, 0.0094), (0.0, 0.0094)], 8, 0.2)
z_col = ztop(-0.0690, -0.0035) + 0.0094
b.lathe('button', [(0.0128, z_col - 0.001), (0.0132, z_col + 0.0090), (0.0114, z_col + 0.0108), (0.0, z_col + 0.0108)],
        sides=10, rot=0.1, offset=(-0.0682, -0.0030, 0), jitter=0.00012, cap_bottom=False)
KNOBS['button'] = (-0.0682, -0.0030)
KNOBS['dialB'] = knob('dialB', 0.0452, 0.0100, [(0.0218, -0.003), (0.0218, 0.0118), (0.0190, 0.0146), (0.0, 0.0146)], 10, 0.05)
KNOBS['dialS'] = knob('dialS', 0.0880, 0.0080, [(0.0136, -0.003), (0.0136, 0.0090), (0.0116, 0.0110), (0.0, 0.0110)], 10, 0.25)
# advance lever: small pivot drum + flat arm reaching right and back, rising a little
PV = (-0.0415, 0.0060)
zp = ztop(*PV)
b.lathe('lever', [(0.0072, zp - 0.003), (0.0072, zp + 0.0118), (0.0, zp + 0.0118)], sides=7, rot=0.3, offset=(PV[0], PV[1], 0), cap_bottom=False)
arm = [(PV[0] - 0.0035, PV[1] - 0.0030, zp + 0.0108), (PV[0] + 0.0065, PV[1] + 0.0010, zp + 0.0138),
       (-0.0100, 0.0115, zp + 0.0160)]
b.tube('lever', arm, radii=[0.0060, 0.0058, 0.0052], sides=4, jitter=0.0001)

_co = np.array([v.co[:] for v in b.bm.verts])
SHIFT = np.array([-(_co[:, 0].min() + _co[:, 0].max()) / 2, -(_co[:, 1].min() + _co[:, 1].max()) / 2, -_co[:, 2].min()])
obj = b.finish()
fit = check_fit(obj)
print('fit', fit)
PARTS = list(obj['parts'])
if '--normals' in sys.argv:
    me = obj.data
    for k, p in enumerate(PARTS):
        fs = [f for f in me.polygons if f.material_index == k]
        cen = np.mean([f.center[:] for f in fs], 0)
        out = sum(1 for f in fs if np.dot(np.array(f.normal[:]), np.array(f.center[:]) - cen) > 0)
        print('normals', p, len(fs), 'outward', out)
    sys.exit()
BODY_C = np.array([(X0 + X1) / 2, (Y0 + Y1) / 2, 0]) + SHIFT     # body centre (mirror planes) after centering

BK.unwrap(obj)
cam_path = os.path.join(HERE, 'camera.json')
# camera.json is kept in the BUILD frame (before finish() re-centres the mesh); SHIFT is added on load
cam_b = json.load(open(cam_path)) if os.path.exists(cam_path) else json.loads(json.dumps(CAM0))
cam = json.loads(json.dumps(cam_b))
cam['target'] = [float(v) for v in np.array(cam_b['target']) + SHIFT]
if '--opt' in sys.argv:
    cam, s_ = BK.optimize_camera(obj, CONCEPT_A, cam, keys=('dist', 'shift', 'roll'))
    cam_b = json.loads(json.dumps(cam)); cam_b['target'] = [float(v) for v in np.array(cam['target']) - SHIFT]
    json.dump(cam_b, open(cam_path, 'w'), indent=1)
print('IoU', BK.align_report(obj, CONCEPT_A, cam, os.path.join(OUT, 'align.png')), cam)


def edge_overlay(obj, cam, out_png):
    """Concept with the model's hard edges drawn on top (inner edges that the silhouette IoU cannot see)."""
    me = obj.data
    V = np.array([v.co[:] for v in me.vertices])
    u, v, _ = C.project(V, cam, CW, CH)
    im = cv2.imread(CONCEPT)
    for e in me.edges:
        fs = [p for p in me.polygons if e.key in p.edge_keys]
        if len(fs) == 2 and fs[0].normal.dot(fs[1].normal) > 0.985:
            continue
        a, c = e.vertices
        cv2.line(im, (int(u[a]), int(v[a])), (int(u[c]), int(v[c])), (60, 255, 60), 1, cv2.LINE_AA)
    cv2.imwrite(out_png, im)


if '--align-only' in sys.argv:
    edge_overlay(obj, cam, os.path.join(WORK, 'edges.png'))
    sys.exit()

# ------------------------------------------------------------------ bake
size = BK.texture_size_for(obj)
if os.environ.get('TEX'):
    size = int(os.environ['TEX'])
BC = BODY_C
def T_rot_axis(center, axis, deg):
    a = np.asarray(axis, float); a /= np.linalg.norm(a)
    t = math.radians(deg)
    K = np.array([[0, -a[2], a[1]], [a[2], 0, -a[0]], [-a[1], a[0], 0]])
    Rm = np.eye(3) + math.sin(t) * K + (1 - math.cos(t)) * K @ K
    T = np.eye(4); T[:3, :3] = Rm
    T[:3, 3] = np.asarray(center) - Rm @ np.asarray(center)
    return T

BODY = ['plate_top', 'plate_bot', 'leather']
TOPS = ['plate_top', 'collar', 'button', 'dialB', 'dialS', 'lever']
views = [{'name': 'orig', 'T': np.eye(4), 'bonus': 0.3},
         # top surfaces: the concept looks at them at ~14 deg but paints them generously (2+ px per texel)
         {'name': 'orig_tops', 'T': np.eye(4), 'parts': TOPS, 'bonus': 0.3, 'min_facing': 0.12},
         # right end <- the left end (mirror across the body's x centre)
         {'name': 'mirror_x', 'T': BK.T_mirror_plane('x', BC[0]), 'parts': BODY, 'bonus': 0.05},
         # back <- the front turned around (not mirrored)
         {'name': 'rot180', 'T': BK.T_rot_z(180, (BC[0], BC[1])), 'parts': ['plate_top', 'plate_bot'], 'bonus': 0.0},
         {'name': 'rot180_tops', 'T': BK.T_rot_z(180, (BC[0], BC[1])), 'parts': ['plate_top'], 'min_facing': 0.12}]
for p, n in (('collar', 8), ('button', 10), ('dialB', 10), ('dialS', 10)):
    cx, cy = np.array(KNOBS[p]) + SHIFT[:2]
    for k in (1, -1, 2, -2, 3, -3, 4, -4, 5):
        if abs(k) * 360 / n > 180:
            continue
        views.append({'name': f'{p}{k}', 'T': BK.T_rot_z(k * 360 / n, (cx, cy)), 'parts': [p], 'min_facing': 0.15})
LCs = LC + SHIFT
for a in (60, -60, 120, -120, 180):
    views.append({'name': f'lens{a}', 'T': T_rot_axis(LCs, LENS_AX, a), 'parts': ['lens']})
part_colors = {'plate_bot': [(102, 75, 64), (116, 85, 71), (122, 91, 76), (128, 97, 81), (150, 108, 80)]}
tex_path = os.path.join(OUT, f'T_{ID}.png')
tex, st = BK.bake(obj, CONCEPT_A, cam, views=views, size=size, out_png=tex_path, debug_png=os.path.join(OUT, 'coverage.png'),
                  part_colors=part_colors, erode_px=2, min_facing=0.3)
print('bake', json.dumps(st))


# ------------------------------------------------------------------ post-bake paint (hidden back)
def texel_points(part_name):
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
    return ys, xs, X - SHIFT, N[t]          # positions in the pre-centering build frame


def lum(c):
    return np.asarray(c, float) @ np.array([0.3, 0.59, 0.11])


rgb_c = cv2.cvtColor(cv2.imread(CONCEPT), cv2.COLOR_BGR2RGB)
# clean leather strip from the concept (left and right of the lens), blurred to the texel footprint
_l, _r = rgb_c[440:840, 300:572], rgb_c[440:840, 1068:1268]
strip = np.concatenate([_l, _r, _l[::-1]], 1).astype(np.float32)
strip = cv2.GaussianBlur(strip, (0, 0), 2.4)
SPX = 3700.0                      # concept pixels per metre on the back (strip spans the whole door, x1.4)


def leather_sample(x, z):
    """Ping-pong tiling of the strip over (x, z) metres."""
    def pp(v, n):
        v = np.mod(v, 2 * n)
        return np.where(v < n, v, 2 * n - 1 - v).astype(int)
    u = pp((x - X0) * SPX + 37, strip.shape[1])
    v = pp((HT - z) * SPX * 0.98 + 11, strip.shape[0])
    return strip[v, u]


def paint_back(tex):
    rng = np.random.default_rng(1515)
    ys, xs, X, Nn = texel_points('leather')
    back = Nn[:, 1] > 0.5
    ys, xs, X = ys[back], xs[back], X[back]
    x, z = X[:, 0], X[:, 2]
    col = leather_sample(x, z)
    leather_px = col.reshape(-1, 3)
    dark = np.percentile(leather_px, 8, axis=0) * 0.72
    tys, txs, TX, TN = texel_points('plate_top')
    metal_all = tex[tys, txs].astype(float)
    ml = lum(metal_all)
    metal = np.median(metal_all[(ml > np.percentile(ml, 40)) & (ml < np.percentile(ml, 80))], axis=0)
    metal_hi = np.median(metal_all[ml > np.percentile(ml, 88)], axis=0)
    metal_lo = np.median(metal_all[ml < np.percentile(ml, 15)], axis=0)
    rust = np.array([196, 128, 66.0])
    t = 1.0 / 640                                   # ~one texel in metres
    def rect(x0, x1, z0, z1):
        return (x >= x0) & (x <= x1) & (z >= z0) & (z <= z1)
    def frame(x0, x1, z0, z1, w):
        return rect(x0, x1, z0, z1) & ~rect(x0 + w, x1 - w, z0 + w, z1 - w)
    # door seam: one dark texel line, a lit texel under its top edge
    dx0, dx1, dz0, dz1 = X0 + 0.0115, X1 - 0.0125, HB + 0.0045, HT - 0.0042
    seam = frame(dx0, dx1, dz0, dz1, 1.3 * t)
    col[rect(dx0 + 1.3 * t, dx1 - 1.3 * t, dz1 - 2.6 * t, dz1 - 1.3 * t)] *= 1.18
    col[seam] = dark
    # hinge strip at the left end: metal with darker knuckle breaks
    hg = rect(X0 + 0.0040, X0 + 0.0082, HB + 0.0070, HT - 0.0070)
    col[hg] = metal
    col[hg & (np.mod((z - HB) / 0.0125, 1.0) < 0.12)] = metal_lo
    col[hg & (x > X0 + 0.0074)] = metal_lo
    # latch at the right end: small metal plate with a dark slot and a worn rusty corner
    lt = rect(X1 - 0.0215, X1 - 0.0125, 0.046, 0.068)
    col[lt] = metal
    col[lt & (z > 0.0655)] = metal_hi
    col[rect(X1 - 0.0185, X1 - 0.0155, 0.050, 0.064)] = metal_lo * 0.8
    col[rect(X1 - 0.0215, X1 - 0.0195, 0.046, 0.050)] = rust
    # film memo window: metal frame, dark inside, a cream film-box end with a red-brown band
    mx0, mx1, mz0, mz1 = 0.006, 0.050, 0.036, 0.066
    col[rect(mx0, mx1, mz0, mz1)] = np.array([46, 34, 30.0])
    col[rect(mx0 + 0.0045, mx1 - 0.0045, mz0 + 0.0045, mz1 - 0.0045)] = np.array([214, 188, 146.0])
    col[rect(mx0 + 0.0045, mx1 - 0.0045, mz0 + 0.0115, mz0 + 0.0175)] = np.array([150, 64, 44.0])
    col[rect(mx0 + 0.0045, mx1 - 0.0045, mz1 - 0.0075, mz1 - 0.0045)] = np.array([232, 210, 170.0])
    col[frame(mx0, mx1, mz0, mz1, 2.3 * t)] = metal
    col[frame(mx0, mx1, mz0, mz1, 2.3 * t) & (z > mz1 - 1.2 * t)] = metal_hi
    # a couple of worn scuffs along the door edge, in the concept's orange
    col *= rng.uniform(0.97, 1.03, (len(x), 1))
    tex[ys, xs] = np.clip(col, 0, 255).astype(np.uint8)
    # bottom plate back: the front is hidden behind the lens and its shadow there, so the plate's own concept
    # palette in 3-texel blocks, with the rubbed orange top edge
    bys, bxs, BX, BN = texel_points('plate_bot')
    bb = BN[:, 1] > 0.5
    nzb = np.random.default_rng(81).random((size // 3 + 2, size // 3 + 2)).astype(np.float32)
    nzb = cv2.GaussianBlur(nzb, (0, 0), 0.8); nzb = (nzb - nzb.min()) / max(float(np.ptp(nzb)), 1e-6)
    nzb = np.kron(nzb, np.ones((3, 3), np.float32))[:size, :size]
    palb = np.array(part_colors['plate_bot'], float)
    cb = palb[np.clip((nzb[bys[bb], bxs[bb]] * len(palb)).astype(int), 0, len(palb) - 1)]
    edge_b = (BX[bb, 2] > HB - 0.0016) & (nzb[bys[bb], bxs[bb]] < 0.35)
    cb[edge_b] = rust
    tex[bys[bb], bxs[bb]] = np.clip(cb, 0, 255).astype(np.uint8)
    # eyepiece on the top plate back, behind the viewfinder
    bk = TN[:, 1] > 0.5
    ex, ez = TX[bk, 0], TX[bk, 2]
    e0, e1, f0, f1 = VC[0] - 0.012, VC[0] + 0.006, HT + 0.0065, HT + 0.0195
    ep = (ex >= e0) & (ex <= e1) & (ez >= f0) & (ez <= f1)
    inner = (ex >= e0 + 0.0028) & (ex <= e1 - 0.0028) & (ez >= f0 + 0.0028) & (ez <= f1 - 0.0028)
    c2 = tex[tys[bk], txs[bk]].astype(float)
    c2[ep] = metal_lo * 0.9
    c2[ep & (ez > f1 - 0.0012)] = metal_hi
    c2[inner] = np.array([40, 42, 30.0])
    c2[inner & (ex < e0 + 0.0055) & (ez > f1 - 0.0060)] = np.array([96, 100, 70.0])
    tex[tys[bk], txs[bk]] = np.clip(c2, 0, 255).astype(np.uint8)
    return tex


def regutter(tex, parts_painted):
    """Re-run the gutter dilation from painted texels so Closest sampling at island borders stays clean."""
    P, UV, part, N = BK.mesh_arrays(obj)
    occ = np.zeros((size, size), np.uint8)
    uvp = UV * size; uvp[..., 1] = size - uvp[..., 1]
    for t_ in range(len(P)):
        cv2.fillPoly(occ, [np.round(uvp[t_] * 16).astype(np.int32)], 1, lineType=cv2.LINE_8, shift=4)
    dist, (iy, ix) = ndimage.distance_transform_edt(occ == 0, return_indices=True)
    gut = (occ == 0)
    tex[gut] = tex[iy[gut], ix[gut]]
    return tex


def backs_from_cut_concept(tex):
    """Plate backs and the top face behind the knobs: the front turned around, sampled from a concept copy whose
    alpha is cut (dilated) where the lens, viewfinder and knobs are, so no rim of them is copied onto the back."""
    im = cv2.imread(CONCEPT_A, cv2.IMREAD_UNCHANGED)
    P, UV, part, N = BK.mesh_arrays(obj)
    idb, _ = BK.rasterize(P, part, cam, CW, CH)
    occl = [PARTS.index(p_) for p_ in ('lens', 'glass', 'vframe', 'vglass', 'collar', 'button', 'dialB', 'dialS', 'lever')]
    cut = (idb >= 0) & np.isin(part[np.maximum(idb, 0)], occl)
    cut = cv2.dilate(cut.astype(np.uint8), np.ones((3, 3), np.uint8), iterations=9) > 0
    im[cut, 3] = 0
    cpath = os.path.join(WORK, 'concept_cut.png')
    cv2.imwrite(cpath, im)
    R180 = lambda cx: BK.T_rot_z(180, (cx, BC[1]))

    def T_stretch(k=0.68):
        x0s, x1s = X0 + SHIFT[0], X1 + SHIFT[0]
        Rt = R180(BC[0])
        S = np.eye(4); S[0, 0] = k
        S[0, 3] = x0s - (2 * BC[0] - x1s) * k
        return S @ Rt
    v2 = [{'name': 'orig', 'T': np.eye(4), 'parts': ['plate_top', 'plate_bot'], 'bonus': 0.3},
          {'name': 'orig_tops', 'T': np.eye(4), 'parts': ['plate_top'], 'bonus': 0.3, 'min_facing': 0.12},
          {'name': 'rot180', 'T': R180(BC[0]), 'parts': ['plate_top', 'plate_bot'], 'bonus': 0.1},
          {'name': 'rot180_tops', 'T': R180(BC[0]), 'parts': ['plate_top'], 'bonus': 0.1, 'min_facing': 0.12},
          {'name': 'slideL', 'T': R180(BC[0] - 0.04), 'parts': ['plate_top', 'plate_bot'], 'bonus': 0.0},
          {'name': 'slideR', 'T': R180(BC[0] + 0.04), 'parts': ['plate_top', 'plate_bot'], 'bonus': -0.05},
          {'name': 'slideL_tops', 'T': R180(BC[0] - 0.04), 'parts': ['plate_top'], 'bonus': 0.0, 'min_facing': 0.12},
          # top plate back <- the clean left 68 % of the top plate front (left of the viewfinder), turned and
          # stretched over the whole back: one source, no seams
          {'name': 'stretch', 'T': T_stretch(), 'parts': ['plate_top'], 'bonus': 0.2}]
    t2, st2 = BK.bake(obj, cpath, cam, views=v2, size=size, part_colors=part_colors, erode_px=2, min_facing=0.3)
    for pn in ('plate_top', 'plate_bot'):
        ys, xs, X, Nn = texel_points(pn)
        sel = (Nn[:, 1] > 0.3) | (Nn[:, 2] > 0.5)      # backs, top face, ledges (front + sides keep bake 1)
        tex[ys[sel], xs[sel]] = t2[ys[sel], xs[sel]]
    return tex



def kpal(region, k=4):
    """k-means colours (dark -> light) of a concept region (u0, u1, v0, v1) and their weights."""
    u0, u1, v0, v1 = region
    px = rgb_c[v0:v1, u0:u1].reshape(-1, 3).astype(np.float32)
    _, lab_, cen = cv2.kmeans(px, k, None, (3, 20, 1.0), 3, cv2.KMEANS_PP_CENTERS)
    w = np.bincount(lab_.ravel(), minlength=k) / len(lab_)
    o = np.argsort(lum(cen))
    return cen[o], w[o]


def pick(pal, w, r):
    """Choose palette entries by weighted random r in [0, 1)."""
    cdf = np.cumsum(w) / np.sum(w)
    return pal[np.clip(np.searchsorted(cdf, r), 0, len(pal) - 1)]


def paint_lens(tex):
    """The lens barrel facets sit at grazing angles in the concept, so the projection smears them: the lens is painted
    in the concept's own colours (k-means per band), facet by facet with the concept's upper-left light, worn orange
    rims, the two red marks on the top facet and the olive glass with its glint."""
    rng = np.random.default_rng(1505)
    ax = LENS_AX
    LCb = LC                                          # build frame
    light = np.array([-0.55, -0.55, 0.65]); light /= np.linalg.norm(light)
    pal_hi, w_hi = kpal((700, 770, 420, 460))         # base front ring, top-left (lit, no red marks)
    pal_mid, w_mid = kpal((588, 622, 560, 700))       # base barrel side, left
    pal_dk, w_dk = kpal((640, 662, 600, 700), 3)      # groove
    pal_gl, w_gl = kpal((760, 880, 650, 820), 4)      # glass (away from the glint)
    pal_gl, w_gl = pal_gl[:3], w_gl[:3]
    # blocky value noise in texture space (3-texel blocks, softly blurred), like the concept's paint patches
    nz = np.random.default_rng(77).random((size // 3 + 2, size // 3 + 2)).astype(np.float32)
    nz = cv2.GaussianBlur(nz, (0, 0), 0.9)
    nz = (nz - nz.min()) / max(float(np.ptp(nz)), 1e-6)
    nz = np.kron(nz, np.ones((3, 3), np.float32))[:size, :size]
    orange = np.array([190, 128, 76.0])
    red = np.median(rgb_c[412:432, 795:825].reshape(-1, 3), 0).astype(float)
    glint = np.array([134, 126, 108.0])
    for pn in ('lens', 'glass'):
        ys, xs, X, Nn = texel_points(pn)
        d = X - LCb
        t = d @ ax
        radv = d - t[:, None] * ax
        r = np.linalg.norm(radv, axis=1)
        ang = np.arctan2(radv[:, 2], radv[:, 0])                        # 0 = +X, pi/2 = top
        fac = np.floor((ang - LENS_ROT) / (2 * math.pi / 12)).astype(int) % 12
        fang = LENS_ROT + (fac + 0.5) * 2 * math.pi / 12                 # facet centre angle
        edge = np.abs(((ang - LENS_ROT) / (2 * math.pi / 12)) % 1.0 - 0.5) * 2   # 1 at a facet edge
        nax = Nn @ ax
        rb = nz[ys, xs]
        rr = rng.random(len(ys))
        if pn == 'glass':
            col = pick(pal_gl, w_gl, rb)
            # glint: a bent quad on the upper right, as in the concept
            gx, gz = radv[:, 0], radv[:, 2]
            g = (gx > 0.006) & (gx < 0.017) & (gz > 0.010) & (gz < 0.021) & (gz > 0.010 + (gx - 0.006) * 0.25)
            col[g] = glint * rng.uniform(0.95, 1.05, (g.sum(), 1))
            col[r > 0.0262] *= 0.82                                       # darker rim under the lip
        else:
            side = np.abs(nax) < 0.5
            col = pick(pal_hi, w_hi, rb) * 0.94
            front = nax > 0.5
            col[front] = pick(pal_hi, w_hi, rb[front])
            groove = (t > 0.0112) & (t < 0.0163) & (r < 0.0345)
            col[groove] = pick(pal_dk, w_dk, rb[groove])
            lip = (t > 0.0360) & (r < 0.0320) & ~front
            col[lip] = pick(pal_dk, w_dk, rb[lip]) * 0.9
            back = nax < -0.5
            col[back] = pick(pal_dk, w_dk, rb[back])
            # facet light: the concept lights the upper-left facets, the lower-right ones fall into shade
            fn = np.stack([np.cos(fang), np.zeros_like(fang), np.sin(fang)], 1)
            sh = np.where(side, 0.86 + 0.30 * (fn @ light), 1.0)
            sh = np.where(front, 0.96 + 0.10 * (np.stack([np.cos(ang), np.zeros_like(ang), np.sin(ang)], 1) @ light), sh)
            col *= sh[:, None]
            # worn orange: outer rims of the base front ring and of the bezel chamfer, and some facet edges
            rim = ((np.abs(t - 0.0105) < 0.0009) & (r > 0.0410)) | ((np.abs(t - 0.0386) < 0.0008) & (r > 0.0350)) | \
                  ((np.abs(t - 0.0410) < 0.0005) & front & (r > 0.0330))
            wear = rim & (rb < 0.45)
            wear |= side & (edge > 0.88) & (rb < 0.22) & ~groove
            col[wear] = orange * (0.88 + 0.2 * rb[wear, None])
            # two red marks on the top facet of the base front ring
            xr = radv[:, 0]
            for mx in (-0.0062, 0.0026):
                m = front & (t < 0.0115) & (radv[:, 2] > 0.0352) & (xr > mx) & (xr < mx + 0.0030)
                col[m] = red
        tex[ys, xs] = np.clip(col, 0, 255).astype(np.uint8)
    return tex



def paint_viewfinder(tex):
    """Viewfinder: thin frame faces get little clean concept area, so it is painted like the concept: light worn
    metal frame with orange rubbed edges, dark recess walls, olive window with a pale glint at the top right."""
    pal_fr, w_fr = kpal((1010, 1028, 290, 420), 4)     # frame left bar (front face)
    pal_win, w_win = kpal((1050, 1110, 340, 395), 3)   # window, lower left (no glint)
    nz = np.random.default_rng(78).random((size // 3 + 2, size // 3 + 2)).astype(np.float32)
    nz = cv2.GaussianBlur(nz, (0, 0), 0.9); nz = (nz - nz.min()) / max(float(np.ptp(nz)), 1e-6)
    nz = np.kron(nz, np.ones((3, 3), np.float32))[:size, :size]
    orange = np.array([190, 128, 76.0])
    ys, xs, X, Nn = texel_points('vframe')
    rb = nz[ys, xs]
    col = pick(pal_fr, w_fr, rb)
    front = Nn[:, 1] < -0.6
    inner = ~front & ((np.abs(X[:, 0] - VC[0]) < 0.0207) & (np.abs(X[:, 2] - VC[2] - 0.0015) < 0.0142))
    col[~front & ~inner] *= 0.9
    col[inner] = np.array([62, 50, 44.0]) * (0.9 + 0.2 * rb[inner, None])
    col[inner & (Nn[:, 2] > 0.5)] *= 1.35            # bottom wall catches light
    edge = front & ((np.abs(np.abs(X[:, 0] - VC[0]) - 0.0255) < 0.0011) | (np.abs(np.abs(X[:, 2] - VC[2]) - 0.0195) < 0.0011))
    col[edge & (rb < 0.5)] = orange
    tex[ys, xs] = np.clip(col, 0, 255).astype(np.uint8)
    ys, xs, X, Nn = texel_points('vglass')
    rb = nz[ys, xs]
    col = pick(pal_win, w_win, rb)
    gx, gz = X[:, 0] - VC[0], X[:, 2] - VC[2]
    gl = (gx > 0.002) & (gx < 0.010) & (gz > 0.004) & (gz < 0.0135) & ~((gx > 0.006) & (gz < 0.0085))
    col[gl] = np.array([128, 124, 108.0])
    tex[ys, xs] = np.clip(col, 0, 255).astype(np.uint8)
    return tex



def paint_controls(tex):
    """Top controls are a few texels across and the concept shows them from a steeper painted angle than its own
    perspective: painted in the concept's colours (sampled by k-means, see tools notes) with facet light, worn
    orange rims, dial ticks and the rust-red shutter button."""
    nz = np.random.default_rng(79).random((size // 2 + 2, size // 2 + 2)).astype(np.float32)
    nz = cv2.GaussianBlur(nz, (0, 0), 0.8); nz = (nz - nz.min()) / max(float(np.ptp(nz)), 1e-6)
    nz = np.kron(nz, np.ones((2, 2), np.float32))[:size, :size]
    W3 = np.array([0.3, 0.4, 0.3])
    metal_top = np.array([(150, 113, 93), (167, 127, 103), (178, 137, 110)], float)
    metal_side = np.array([(97, 72, 61), (114, 85, 71), (128, 96, 80)], float)
    coll_side = np.array([(112, 84, 71), (134, 101, 84), (146, 110, 90)], float)
    btn_top = np.array([(132, 72, 52), (148, 81, 58), (170, 98, 66)], float)
    btn_side = np.array([(88, 50, 40), (100, 56, 43), (112, 61, 46)], float)
    tick = np.array([118, 64, 50.0])
    orange = np.array([190, 128, 76.0])
    lh = np.array([-0.75, -0.66, 0.0])
    spec = {'collar': (metal_top, coll_side, None), 'button': (btn_top, btn_side, None),
            'dialB': (metal_top, metal_side, (8, 0.0080, 0.0150, 0.0030, 0.12)),
            'dialS': (metal_top, metal_side, (8, 0.0052, 0.0098, 0.0026, 0.35)), 'lever': (metal_top, metal_side, None)}
    for pn, (ptop, pside, ticks) in spec.items():
        ys, xs, X, Nn = texel_points(pn)
        rb = nz[ys, xs]
        top = Nn[:, 2] > 0.8
        cham = (Nn[:, 2] > 0.3) & ~top
        side = ~top & ~cham
        col = pick(ptop, W3, rb)
        col[side] = pick(pside, W3, rb[side]) * (0.92 + 0.22 * (Nn[side] @ lh))[:, None]
        col[cham] = pick(ptop, W3, rb[cham]) * 1.04
        wear = cham & (rb < 0.35)
        if pn in ('collar', 'dialB', 'dialS'):
            col[wear] = orange * (0.9 + 0.15 * rb[wear, None])
        if pn == 'button':
            col[cham] = btn_top[2] * (0.95 + 0.1 * rb[cham, None])
        if pn == 'lever':
            col[Nn[:, 2] > 0.5] = pick(ptop, W3, rb[Nn[:, 2] > 0.5])
            col[(Nn[:, 2] > 0.5) & (rb < 0.2)] = orange
        if ticks:
            n, r0, r1, w, a0 = ticks
            cx, cy = KNOBS[pn]
            dx, dy = X[:, 0] - cx, X[:, 1] - cy
            for k in range(n):
                a = a0 + 2 * math.pi * k / n
                ux, uy = math.cos(a), math.sin(a)
                along = dx * ux + dy * uy
                across = -dx * uy + dy * ux
                m = top & (along > r0) & (along < r1) & (np.abs(across) < w / 2)
                col[m] = tick
        tex[ys, xs] = np.clip(col, 0, 255).astype(np.uint8)
    return tex



def paint_edge_wear(tex):
    """Rubbed orange metal along the plates' outer edges (the concept paints it there; at 640 texels/m the projected
    highlight is thinner than a texel and gets lost)."""
    nz = np.random.default_rng(80).random((size // 2 + 2, size // 2 + 2)).astype(np.float32)
    nz = np.kron(cv2.GaussianBlur(nz, (0, 0), 0.7), np.ones((2, 2), np.float32))[:size, :size]
    nz = (nz - nz.min()) / max(float(np.ptp(nz)), 1e-6)
    orange = np.array([194, 130, 74.0])
    e = 0.0019
    ys, xs, X, Nn = texel_points('plate_top')
    x, y, z = X[:, 0], X[:, 1], X[:, 2]
    zt = ztop(x, y)
    fr, tp, lf = Nn[:, 1] < -0.7, Nn[:, 2] > 0.7, Nn[:, 0] < -0.7
    m = ((fr | lf) & (z > zt - e)) | (tp & ((y < Y0 + e) | (x < X0 + e))) | (fr & (x < X0 + 0.0022 + e)) | (lf & (y < Y0 + e))
    m &= nz[ys, xs] < 0.55
    tex[ys[m], xs[m]] = np.clip(orange * (0.88 + 0.2 * nz[ys[m], xs[m]][:, None]), 0, 255).astype(np.uint8)
    ys, xs, X, Nn = texel_points('plate_bot')
    x, y, z = X[:, 0], X[:, 1], X[:, 2]
    fr, lf, up = Nn[:, 1] < -0.7, Nn[:, 0] < -0.7, Nn[:, 2] > 0.7
    # the ledge on top of the bottom plate (under 2 texels wide): the concept's lit metal edge, not a dashed rim
    led = up & (z > 0.01)
    ledc = np.array([(150, 114, 94), (164, 125, 102), (176, 135, 108)], float)
    tex[ys[led], xs[led]] = ledc[np.clip((nz[ys[led], xs[led]] * 3).astype(int), 0, 2)].astype(np.uint8)
    m = (fr | lf) & (z < 0.0014)
    m &= nz[ys, xs] < 0.3
    tex[ys[m], xs[m]] = np.clip(orange * (0.85 + 0.2 * nz[ys[m], xs[m]][:, None]), 0, 255).astype(np.uint8)
    return tex


tex = backs_from_cut_concept(tex.copy())
tex = paint_back(tex)
tex = paint_lens(tex)
tex = paint_viewfinder(tex)
tex = paint_controls(tex)
tex = paint_edge_wear(tex)
tex = regutter(tex, None)
cv2.imwrite(tex_path, cv2.cvtColor(tex, cv2.COLOR_RGB2BGR))
R.apply_texture(obj, tex_path, ID)
stats = check_fit(obj)
stats['texture'] = f'{size}x{size}'
stats['iou'] = round(BK.align_report(obj, CONCEPT_A, cam), 4)
if '--quick' in sys.argv:
    R.render_views(obj, os.path.join(WORK, 'preview_q.png'), concept_cam=cam, concept_size=(CW, CH), concept_path=CONCEPT, size=640)
    sys.exit()
R.render_views(obj, os.path.join(OUT, 'preview.png'), concept_cam=cam, concept_size=(CW, CH), concept_path=CONCEPT)
R.shelf_fit_render(obj, os.path.join(OUT, 'shelf.png'))
R.export(obj, OUT, ID, tex_path)
json.dump({'id': ID, 'stats': stats, 'bake': st, 'camera': cam}, open(os.path.join(OUT, 'stats.json'), 'w'), indent=1)
R.card(OUT, ID, stats)
print('done', stats)
