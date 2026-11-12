"""11 Juice carton - collectible shelf item. Run: python3 build.py [--opt] [--align-only]

Masses (from the concept, front three-quarter view showing the front and the +X side):
- gable-top carton body: crooked square box 0.082 x 0.082, shoulder at ~0.163 m, 8-point rings (corners + mid-edge)
  so the front can bulge a little and the +X side has the horizontal crease of the concept
- front / back roof slopes up to a ridge at ~0.198 m; both gable ends are real folded-in triangles
  (upper triangles + a lit ledge, fold point pushed into the box)
- flat fin seam on the ridge (thin box, ~0.010 m tall), slightly wider than the body
- screw cap on the front roof slope: 12-sided lathe with a flange, axis tilted 22 deg toward the front
Painted (from the concept projection): JUICE lettering, orange band, orange + half orange + leaf, green bottom band,
side olive text bars, cap ridges, scuffs and crumple shading. Hidden back / left side get the de-lit +X side panel,
back roof / fin back get the front ones, cap back gets rotated copies around the cap axis.
"""
import sys, os, json, math
sys.path.insert(0, '/home/claude/kit')
HERE = os.path.dirname(os.path.abspath(__file__))
import numpy as np
import cv2
from scipy import ndimage
import bpy
from mathutils import Vector, Matrix
from geo import Builder, clear_scene, check_fit
sys.path.insert(0, HERE)
import bake_local as BK          # item 05's copy of the kit bake: true z-buffer (the painter's sort let the bottom
                                 # face overdraw the +X wall in the concept view)
import render as R
import cam as C

CONCEPT = '/home/claude/concepts/collectible-concepts/11-juice-carton-v2.png'
ID = 'CI11_JuiceCarton'
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)
WORK = os.path.join(HERE, 'tmp'); os.makedirs(WORK, exist_ok=True)
CW, CH = 1024, 1536

# ------------------------------------------------------------------ dimensions (metres)
W = 0.0772         # x (front width)
D = 0.090          # y (depth)
HB = 0.169         # shoulder (top of the walls)
ZR = 0.198         # ridge
FIN_H = 0.0105     # fin above ridge
FIN_T = 0.0062     # fin thickness
GABLE_IN = 0.012   # how far the gable fold point is pushed into the box
LEAN = (0.0008, 0.0)  # top ring shift (slight lean)

clear_scene()
b = Builder(ID, seed=11)


def ring8(z, hx, hy, off=(0.0, 0.0), push=None, rot=0.0):
    """8 points CCW from the top: FL, F-mid, FR, R-mid, BR, B-mid, BL, L-mid.
    push = (front, right, back, left) outward offsets of the mid-edge points."""
    p = [(-hx, -hy), (0, -hy), (hx, -hy), (hx, 0), (hx, hy), (0, hy), (-hx, hy), (-hx, 0)]
    push = push or (0, 0, 0, 0)
    q = []
    for i, (x, y) in enumerate(p):
        if i == 1: y -= push[0]
        if i == 3: x += push[1]
        if i == 5: y += push[2]
        if i == 7: x -= push[3]
        c, s = math.cos(rot), math.sin(rot)
        x, y = x * c - y * s, x * s + y * c
        q.append((x + off[0], y + off[1], z))
    return q


hx, hy = W / 2, D / 2
rings = [
    ring8(0.000, hx - 0.0006, hy - 0.0004, off=(0.0, 0.0), push=(0.0004, 0.0, 0.0002, 0.0)),
    ring8(0.018, hx + 0.0004, hy + 0.0003, off=(0.0002, 0.0), push=(0.0012, 0.0, 0.0003, 0.0), rot=0.004),
    ring8(0.113, hx + 0.0002, hy, off=(0.0007, 0.0), push=(0.0016, 0.0, 0.0004, 0.0), rot=-0.004),
    ring8(HB, hx, hy, off=LEAN, push=(0.0, 0.0, 0.0, 0.0), rot=0.010),
]
# the concept's front-right edge flares out toward the crumpled bottom: push that corner column out a little
for ri, dx in ((0, 0.0020), (1, 0.0016), (2, 0.0008)):
    x, y, z = rings[ri][2]
    rings[ri][2] = (x + dx, y - dx * 0.3, z)
# per-corner height jitter of the shoulder (crooked carton)
top = rings[-1]
dz = [0.0006, 0.0, -0.0008, 0.0, 0.0004, 0.0, -0.0002, 0.0]
rings[-1] = [(x, y, z + d) for (x, y, z), d in zip(top, dz)]

FRONT_SEG = (0, 1)   # ring segments on the front face
for a, c in zip(rings, rings[1:]):
    for i in range(8):
        k = (i + 1) % 8
        part = 'front' if i in FRONT_SEG else 'panel'
        b.add_bmesh_geometry(part, [a[i], a[k], c[k], c[i]], [(0, 1, 2, 3)])
# bottom
b.add_bmesh_geometry('bottom', rings[0], [tuple(reversed(range(8)))])

# ------------------------------------------------------------------ roof, gables, ridge strip
T = rings[-1]
FL, FM, FR, RM, BR, BM, BL, LM = T
lx, ly = LEAN
ridge_z_l, ridge_z_r = ZR + 0.0004, ZR - 0.0006
Rf_r = (hx + lx - 0.0004, -FIN_T / 2, ridge_z_r)
Rb_r = (hx + lx - 0.0004, FIN_T / 2, ridge_z_r)
Rf_l = (-hx + lx + 0.0004, -FIN_T / 2, ridge_z_l)
Rb_l = (-hx + lx + 0.0004, FIN_T / 2, ridge_z_l)
# front roof (pentagon with the front-mid shoulder point) and back roof
b.add_bmesh_geometry('roof', [FL, FM, FR, Rf_r, Rf_l], [(0, 1, 2, 3, 4)])
b.add_bmesh_geometry('roof', [BR, BM, BL, Rb_l, Rb_r], [(0, 1, 2, 3, 4)])
# ridge strip under the fin (closes the shell)
b.add_bmesh_geometry('roof', [Rf_l, Rf_r, Rb_r, Rb_l], [(0, 1, 2, 3)])
# gables: fold point pushed in
Mr = (hx + lx - GABLE_IN, 0.0, HB + 0.006)
Ml = (-hx + lx + GABLE_IN, 0.0, HB + 0.006)
# right (+X): A=FR, mid=RM, B=BR
b.add_bmesh_geometry('gable', [FR, Mr, Rf_r], [(0, 1, 2)])
b.add_bmesh_geometry('gable', [Mr, BR, Rb_r], [(0, 1, 2)])
b.add_bmesh_geometry('gable', [Mr, Rb_r, Rf_r], [(0, 1, 2)])
b.add_bmesh_geometry('gable', [FR, RM, BR, Mr], [(0, 1, 2, 3)])
# left (-X): A=FL, mid=LM, B=BL (mirrored winding)
b.add_bmesh_geometry('gable', [Ml, FL, Rf_l], [(0, 1, 2)])
b.add_bmesh_geometry('gable', [BL, Ml, Rb_l], [(0, 1, 2)])
b.add_bmesh_geometry('gable', [Rb_l, Ml, Rf_l], [(0, 1, 2)])
b.add_bmesh_geometry('gable', [BL, LM, FL, Ml], [(0, 1, 2, 3)])

# ------------------------------------------------------------------ fin seam (thin crooked box, a bit wider)
fz0, fz1 = ZR - 0.003, ZR + FIN_H
ft = FIN_T / 2
fx0, fx1 = -hx + lx + 0.0018, hx + lx + 0.0008
fin_v = [(fx0, -ft, fz0), (fx1, -ft, fz0 - 0.001), (fx1, ft, fz0 - 0.001), (fx0, ft, fz0),
         (fx0 + 0.0005, -ft - 0.0003, fz1 + 0.0004), (fx1 - 0.0003, -ft - 0.0002, fz1 - 0.0006),
         (fx1 - 0.0003, ft + 0.0002, fz1 - 0.0006), (fx0 + 0.0005, ft + 0.0003, fz1 + 0.0004)]
b.add_bmesh_geometry('fin', fin_v, [(3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)])

# ------------------------------------------------------------------ screw cap on the front slope
CAP_TILT = math.radians(22)
CAP_X, CAP_T = -0.0045, 0.44           # x and position along the slope (0 = shoulder, 1 = ridge)
yc = -hy + CAP_T * (hy - ft)
zc = HB + CAP_T * (ZR - HB)
xc = CAP_X + lx * CAP_T
# recessed spout collar (dark ring in the concept), ridged cap overhanging it, chamfered top
cap_prof = [(0.0128, -0.013), (0.0134, 0.0), (0.0134, 0.0028), (0.0149, 0.0030), (0.0151, 0.0068),
            (0.0149, 0.0108), (0.0131, 0.0123), (0.0, 0.0125)]
CAP_H = [p[1] for p in cap_prof]
cap_rings = b.lathe('cap', cap_prof, sides=14, rot=0.13, cap_top=False, cap_bottom=True, jitter=0.00015)
Mcap = Matrix.Translation((xc, yc, zc)) @ Matrix.Rotation(CAP_TILT, 4, 'X')   # +angle tips the top toward -Y (front)
for rg in cap_rings:
    for v in rg:
        v.co = Mcap @ v.co
CAP_C = np.array([xc, yc, zc])
CAP_AX = np.array([0, -math.sin(CAP_TILT), math.cos(CAP_TILT)])

_co = np.array([v.co[:] for v in b.bm.verts])
SHIFT = np.array([-(_co[:, 0].min() + _co[:, 0].max()) / 2, -(_co[:, 1].min() + _co[:, 1].max()) / 2, -_co[:, 2].min()])
CAP_C = CAP_C + SHIFT     # finish() re-centres XY and grounds Z
obj = b.finish()
fit = check_fit(obj)
print('fit', fit)

BK.unwrap(obj)
cam_path = os.path.join(HERE, 'camera.json')
# design camera from tools/calib.py (least squares on 8 hand-measured concept corners, rms 3.4 px)
cam = json.load(open(cam_path)) if os.path.exists(cam_path) else \
    {'az': 21.2, 'el': 7.45, 'dist': 1.084, 'fov': 13.88, 'target': [0, 0, 0.104], 'shift': [-0.002, 0.0179], 'roll': 0}
if '--opt' in sys.argv:
    if os.environ.get('AZ'):
        cam['az'] = float(os.environ['AZ'])
    keys = ('dist', 'shift') if os.environ.get('AZ') else ('az', 'dist', 'shift')
    cam, s = BK.optimize_camera(obj, CONCEPT, cam, keys=keys)
    json.dump(cam, open(cam_path, 'w'), indent=1)
print('IoU', BK.align_report(obj, CONCEPT, cam, os.path.join(OUT, 'align.png')), cam)


def edge_overlay(obj, cam, out_png):
    """Concept with the model's hard edges drawn on top (checks inner edges the silhouette IoU cannot see)."""
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
    # body corner columns (bottom ring) and shoulder corners: print their pixel x for comparison
    pts = np.array([FL, FR, BR, rings[0][0], rings[0][2], rings[0][4], Rf_l, Rf_r]) + SHIFT
    uu, vv, _ = C.project(pts, cam, CW, CH)
    print('px shoulder FL FR BR | bottom FL FR BR | ridge L R', [(int(a), int(c)) for a, c in zip(uu, vv)])
    cv2.imwrite(out_png, im)


if '--align-only' in sys.argv:
    edge_overlay(obj, cam, os.path.join(WORK, 'edges.png'))
    sys.exit()

# ------------------------------------------------------------------ de-lit concept (per planar face group gains)
PARTS = list(obj['parts'])


def delit_concept(out_png):
    """The concept paints the +X side and the gable in shadow and the roof in full light. The hidden back / left
    side are copies of the +X side, so every visible planar face group is brought to the front's paper brightness
    (one gain per group, measured on its bright paper texels). Cap and fin keep their painted light."""
    rgb, cm = BK.load_concept(CONCEPT)
    P, UV, part, N = BK.mesh_arrays(obj)
    idb, _ = BK.rasterize(P, part, cam, CW, CH)
    tri = np.maximum(idb, 0)
    pn = np.array(PARTS)[part[tri]]
    nz = N[tri][..., 2]
    nx = N[tri][..., 0]
    vis = (idb >= 0) & cm
    groups = {
        'front': vis & (pn == 'front'),
        'side': vis & (pn == 'panel') & (nx > 0.7),
        'roof': vis & (pn == 'roof'),
        'gable_up': vis & (pn == 'gable') & (nz > 0.6),
        'gable_tri': vis & (pn == 'gable') & (nz <= 0.6),
    }
    f = rgb.astype(np.float32)
    lum = f @ np.array([0.3, 0.59, 0.11], np.float32)
    ref = np.percentile(lum[groups['front']], 80)
    gain = np.ones(lum.shape, np.float32)
    info = {}
    for g, m in groups.items():
        if g == 'front' or m.sum() < 50:
            continue
        k = float(np.clip((ref / max(np.percentile(lum[m], 80), 1.0)) ** 0.9, 0.75, 2.4))
        gain[m] = k
        info[g] = round(k, 3)
    # soften the step a little at group borders (geometry edges) so a 1 px misfit does not ring
    gain = cv2.GaussianBlur(gain, (0, 0), 1.2)
    out = np.clip(f * gain[..., None], 0, 255).astype(np.uint8)
    out[~cm] = rgb[~cm]
    cv2.imwrite(out_png, np.dstack([cv2.cvtColor(out, cv2.COLOR_RGB2BGR), (cm * 255).astype(np.uint8)]))
    print('delit gains', info)
    return out_png


def T_rot_axis(center, axis, deg):
    """4x4: rotation by deg around an arbitrary axis through center."""
    a = np.asarray(axis, float); a /= np.linalg.norm(a)
    t = math.radians(deg)
    K = np.array([[0, -a[2], a[1]], [a[2], 0, -a[0]], [-a[1], a[0], 0]])
    Rm = np.eye(3) + math.sin(t) * K + (1 - math.cos(t)) * K @ K
    T = np.eye(4); T[:3, :3] = Rm
    T[:3, 3] = np.asarray(center) - Rm @ np.asarray(center)
    return T


def T_back_to_side():
    """Affine map of the back wall (y = +D/2, x in [-W/2, W/2]) exactly onto the +X side wall
    (x = +W/2, y in [-D/2, D/2]): a -90 deg turn, a slide out to the side plane and a stretch W -> D."""
    xs = np.array([v.co.x for v in obj.data.vertices]); ys = np.array([v.co.y for v in obj.data.vertices])
    zs = np.array([v.co.z for v in obj.data.vertices])
    body = zs < HB - 0.01
    x0, x1 = xs[body].min(), xs[body].max(); y0, y1 = ys[body].min(), ys[body].max()
    hw, hd = (x1 - x0) / 2, (y1 - y0) / 2
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    T = np.eye(4)
    T[:3, :3] = [[0, 1, 0], [-hd / hw, 0, 0], [0, 0, 1]]
    T[:3, 3] = [cx + hw - (cy + hd), cy + hd / hw * cx, 0]
    return T


BAKE_SRC = delit_concept(os.path.join(WORK, 'concept_delit.png'))
size = BK.texture_size_for(obj)
if os.environ.get('TEX'):
    size = int(os.environ['TEX'])
# The +X wall is the only painted side; the concept shows it at facing ~0.29-0.33 (170 px for 0.09 m = ~3 concept px
# per texel, so no streaks), so the wall views use a lower facing limit than the default 0.3.
SIDE_FACING = 0.2
views = [{'name': 'orig', 'T': np.eye(4), 'bonus': 0.3},
         {'name': 'orig_side', 'T': np.eye(4), 'parts': ['panel'], 'bonus': 0.3, 'min_facing': SIDE_FACING},
         # left side / left gable / fin+roof edges: mirror of the +X side
         {'name': 'mirror_x', 'T': BK.T_mirror_plane('x'), 'parts': ['gable', 'fin', 'roof'], 'bonus': 0.1},
         {'name': 'mirror_side', 'T': BK.T_mirror_plane('x'), 'parts': ['panel'], 'bonus': 0.1, 'min_facing': SIDE_FACING},
         # back wall: the +X side panel turned to the back
         {'name': 'back_from_side', 'T': T_back_to_side(), 'parts': ['panel'], 'min_facing': SIDE_FACING},
         # back roof slope and fin back: the front ones turned around
         {'name': 'rot180', 'T': BK.T_rot_z(180), 'parts': ['roof', 'fin']}]
views += [{'name': f'cap{a}', 'T': T_rot_axis(CAP_C, CAP_AX, a), 'parts': ['cap']} for a in (40, -40, 80, -80, 120, -120, 160, -160)]
part_colors = {'bottom': [(112, 88, 60), (138, 112, 80), (160, 134, 98), (182, 156, 118)]}
tex_path = os.path.join(OUT, f'T_{ID}.png')
tex, st = BK.bake(obj, BAKE_SRC, cam, views=views, size=size, out_png=tex_path, debug_png=os.path.join(OUT, 'coverage.png'),
                  part_colors=part_colors, erode_px=2, min_facing=0.3)
print('bake', json.dumps(st))


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
    sm = np.maximum(l0 + l1 + l2, 1e-9)          # edge texels: renormalise the clipped barycentrics
    l0, l1, l2 = l0 / sm, l1 / sm, l2 / sm
    X = P[t, 0] * l0[:, None] + P[t, 1] * l1[:, None] + P[t, 2] * l2[:, None]
    return ys, xs, X, N[t]


def paint_cap(tex):
    """The cap is ~3 texels per ridge: the projection smears it, so it is painted in the concept's colours:
    cream top with a lighter chamfer ring, 14 light/dark vertical ridges, dark brown collar ring under it."""
    rng = np.random.default_rng(111)
    ys, xs, X, Nn = texel_points(obj, size, 'cap')
    d = X - CAP_C
    h = d @ CAP_AX
    e1 = np.cross(CAP_AX, [1.0, 0, 0]); e1 /= np.linalg.norm(e1)
    e2 = np.cross(CAP_AX, e1)
    ang = np.arctan2(d @ e2, d @ e1)
    rad = np.linalg.norm(d - h[:, None] * CAP_AX, axis=1)
    up = Nn @ CAP_AX
    top_c, top_rim = np.array([236, 196, 146]), np.array([246, 214, 168])
    ridge_l, ridge_d = np.array([226, 190, 142]), np.array([160, 128, 92])
    collar = np.array([118, 86, 56])
    col = np.zeros((len(ys), 3))
    ridge = (((ang + math.pi) / (2 * math.pi) * 14) % 1.0) > 0.38    # 14 ridges, ~4 texels each
    col[:] = np.where(ridge[:, None], ridge_l, ridge_d)
    col[h < CAP_H[3] + 0.0003] = collar     # recessed collar under the cap: dark ring
    top = up > 0.55
    col[top] = top_c
    col[top & (rad > 0.0105)] = top_rim
    # light texel noise in the concept's blocky manner
    col *= rng.uniform(0.96, 1.03, (len(ys), 1))
    tex[ys, xs] = np.clip(col, 0, 255).astype(np.uint8)
    # the cap's islands are only a few texels wide: re-run the gutter dilation from the painted texels, so
    # Closest sampling at island borders never picks up the old projected colours
    P, UV, part, N = BK.mesh_arrays(obj)
    occ = np.zeros((size, size), np.uint8)
    uvp = UV * size; uvp[..., 1] = size - uvp[..., 1]
    for t in np.nonzero(part != PARTS.index('cap'))[0]:
        cv2.fillPoly(occ, [np.round(uvp[t] * 16).astype(np.int32)], 1, lineType=cv2.LINE_8, shift=4)
    painted = np.zeros((size, size), bool); painted[ys, xs] = True
    dist, (iy, ix) = ndimage.distance_transform_edt(~painted, return_indices=True)
    gut = (~painted) & (occ == 0) & (dist <= 3)
    tex[gut] = tex[iy[gut], ix[gut]]
    return tex


def back_roof_from_clean_concept(tex):
    """The back roof slope is the front slope turned around; the cap and its cast shadow are inpainted out of the
    de-lit concept first, so the back slope gets plain carton paper instead of a cap-shaped smudge."""
    im = cv2.imread(BAKE_SRC, cv2.IMREAD_UNCHANGED)
    P, UV, part, N = BK.mesh_arrays(obj)
    idb, _ = BK.rasterize(P, part, cam, CW, CH)
    capm = ((idb >= 0) & (part[np.maximum(idb, 0)] == PARTS.index('cap'))).astype(np.uint8)
    capm = cv2.dilate(capm, np.ones((3, 3), np.uint8), iterations=14)
    # its shadow falls to the lower right
    capm = np.maximum(capm, np.roll(np.roll(capm, 14, 1), 8, 0))
    roofm = ((idb >= 0) & (part[np.maximum(idb, 0)] == PARTS.index('roof'))).astype(np.uint8)
    capraw = ((idb >= 0) & (part[np.maximum(idb, 0)] == PARTS.index('cap'))).astype(np.uint8)
    capm &= (roofm | capraw)
    src = np.ascontiguousarray(im[:, :, :3]).copy()
    paper = (roofm > 0) & (capm == 0)
    src[~paper & (capm == 0)] = np.median(src[paper], axis=0).astype(np.uint8)   # no fin orange leaking in
    rgb = cv2.inpaint(src, capm, 9, cv2.INPAINT_TELEA)
    # give the filled area the roof's own blocky paper variation, copied from plain roof beside the cap
    hp = src.astype(np.float32) - cv2.blur(src, (31, 31)).astype(np.float32)
    paper_e = cv2.erode(paper.astype(np.uint8), np.ones((3, 3), np.uint8), iterations=16) > 0
    ys_, xs_ = np.nonzero(capm)
    var = np.zeros((len(ys_), 3), np.float32); done = np.zeros(len(ys_), bool)
    for dx, dy in ((-150, 25), (-190, 0), (150, -25), (190, 0), (-110, 40), (110, -40), (-60, 60), (60, -60), (0, 70)):
        yy, xx = np.clip(ys_ + dy, 0, CH - 1), np.clip(xs_ + dx, 0, CW - 1)
        ok = ~done & paper_e[yy, xx]
        var[ok] = hp[yy[ok], xx[ok]]; done |= ok
    rgb[ys_, xs_] = np.clip(rgb[ys_, xs_] + var, 0, 255).astype(np.uint8)
    keep = ~((roofm > 0) | (capm > 0))
    rgb[keep] = im[:, :, :3][keep]
    clean = os.path.join(WORK, 'concept_delit_nocap.png')
    cv2.imwrite(clean, np.dstack([rgb, im[:, :, 3]]))
    t2, _ = BK.bake(obj, clean, cam, views=[{'name': 'rot180', 'T': BK.T_rot_z(180), 'parts': ['roof']}], size=size,
                    erode_px=2, min_facing=0.3)
    ys, xs, X, Nn = texel_points(obj, size, 'roof')
    back = Nn[:, 1] > 0.3
    tex[ys[back], xs[back]] = t2[ys[back], xs[back]]
    return tex


tex = back_roof_from_clean_concept(tex.copy())
tex = paint_cap(tex)
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
