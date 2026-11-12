"""17 Baby bottle — collectible shelf item. Run: python3 build.py [--opt] [--align-only] [--rows]

Masses (from the concept, metres): 8-sided bottle body (the concept shows 4 front facets) (circumradius ~0.055) with a chamfered foot and a sloped
shoulder into a short neck, an 8-sided blue screw ring (wider than the neck, flat top), and a 12-sided rubber teat:
round flange seated in the ring, a cone, a nipple cylinder with a chamfered flat top. Small stuff (measuring scale,
milk level, chipped paint on the ring, mottling) is painted from the concept projection.
"""
import sys, os, json, math
sys.path.insert(0, '/home/claude/kit')
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), 'tools'))
import numpy as np
import cv2
from scipy import ndimage
import bpy
from mathutils import Vector
from geo import Builder, clear_scene, check_fit
import bake as BK
import render as R

HERE = os.path.dirname(os.path.abspath(__file__))
CONCEPT_SRC = '/home/claude/concepts/collectible-concepts/17-baby-bottle-v1.png'
# concept with a clean alpha (tools/make_alpha.py, threshold 9): the pale glass on the right matches the grey bg
CONCEPT = os.path.join(HERE, 'concept_alpha.png')
ID = 'CI17_BabyBottle'
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)
CW, CH = 1312, 1199

d2r = math.radians
ROT = d2r(8)                  # octagons: a vertex near the front, turned ~10 deg to the right (as drawn)

# ---------------------------------------------------------------- shape (metres)
RB = 0.0560                   # body circumradius
BODY = [(RB * 0.92, 0.000), (RB * 0.99, 0.0050), (RB, 0.0062), (RB * 1.005, 0.060), (RB * 1.01, 0.1075),
        (RB * 0.975, 0.1115), (0.0475, 0.1225), (0.0435, 0.1250), (0.0430, 0.1310)]
RR = 0.0450                   # ring circumradius
RING = [(0.0400, 0.1300), (RR * 0.90, 0.1255), (RR * 0.975, 0.1290), (RR, 0.1310), (RR, 0.1525), (RR * 0.965, 0.1552),
        (0.0372, 0.1560), (0.0300, 0.1545), (0.0290, 0.1500)]
TEAT = [(0.0312, 0.1500), (0.0316, 0.1565), (0.0306, 0.1605), (0.0300, 0.1643), (0.0242, 0.1702), (0.0170, 0.1770),
        (0.0130, 0.1815), (0.0123, 0.1880), (0.0123, 0.1960), (0.0115, 0.1997), (0.0095, 0.2026), (0.0055, 0.2048),
        (0.0, 0.2056)]
RING_DX = 0.0001              # ring sits a little right of the body axis (screwed on crooked)


def build():
    b = Builder(ID, seed=17)
    # hidden ends are left open (body top inside the ring, ring hole under the teat, teat bottom): no wasted texels
    rings = b.lathe('body', BODY, sides=8, rot=ROT, jitter=0.0006, cap_top=False, cap_bottom=False)
    b.cap(rings[0], b.part('base'), flip=True)           # bottom face: own part, painted from the palette
    b.lathe('ring', RING, sides=8, rot=ROT - d2r(5), jitter=0.0004, cap_top=False, cap_bottom=False,
            offset=(RING_DX, 0, 0))
    b.lathe('teat', TEAT, sides=12, rot=ROT, jitter=0.0003, cap_top=False, cap_bottom=False)
    # PSX crookedness: upper part leans a little to the right (the concept's teat axis sits right of the body's)
    for v in b.bm.verts:
        z = v.co.z
        v.co.x += 0.015 * z               # whole bottle: slight lean to the right
        if z > 0.10:
            v.co.x += 0.006 * (z - 0.10)
        if z > 0.15:                     # teat a touch further right and back
            v.co.x += 0.020 * (z - 0.15)
            v.co.y += 0.020 * (z - 0.15)
    return b


clear_scene()
b = build()
obj = b.finish()
fit = check_fit(obj)
print('fit', fit)



def unwrap(o, shrink=0.35, margin=0.012):
    """Kit smart-project unwrap, then the bottom face (never seen on the shelf) is shrunk and everything repacked,
    so the visible surfaces get the texels."""
    BK.unwrap(o, margin=margin)
    me = o.data
    bi = list(o['parts']).index('base')
    uv = me.uv_layers.active.data
    loops = [l for p in me.polygons if p.material_index == bi for l in p.loop_indices]
    c = np.mean([uv[l].uv[:] for l in loops], axis=0)
    for l in loops:
        uv[l].uv = tuple(c + (np.array(uv[l].uv[:]) - c) * shrink)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(rotate=True, margin=margin)
    bpy.ops.object.mode_set(mode='OBJECT')


unwrap(obj)
cam_path = os.path.join(HERE, 'camera.json')
cam = json.load(open(cam_path)) if os.path.exists(cam_path) else \
    {'az': 0, 'el': 16, 'dist': 0.6, 'fov': 24, 'target': [0, 0, 0.1], 'shift': [0, 0], 'roll': 0}
if '--opt' in sys.argv:
    keys = (('az',) if '--az' in sys.argv else ()) + ('dist', 'shift') + (('el',) if '--el' in sys.argv else ())
    cam, s = BK.optimize_camera(obj, CONCEPT, cam, keys=keys)
    json.dump(cam, open(cam_path, 'w'), indent=1)
if '--rows' in sys.argv:
    import rowdiff; rowdiff.report(obj, CONCEPT, cam)
if '--edges' in sys.argv:
    import edges; edges.overlay(obj, CONCEPT, cam, os.path.join(OUT, 'edges.png'))
print('IoU', BK.align_report(obj, CONCEPT, cam, os.path.join(OUT, 'align.png')), cam)
if '--align-only' in sys.argv:
    sys.exit()

# ---------------------------------------------------------------- texture
MARK_BOX = (600, 970, 495, 700)          # y0, y1, x0, x1 of the measuring scale in the concept


def clean_and_delit(src_path, out_path, keep_marks, strength=0.7):
    """Concept prepared for projection.
    - delit: per part (ownership from the model at the concept camera), each row's large-scale left/right light
      falloff is divided out (blur along x only), so front, sides and the rotated copies used for the hidden back
      match; vertical structure (milk level, ring/teat bands) stays.
    - keep_marks=False: the measuring scale is inpainted away (this copy feeds the hidden sides, the scale is
      painted on the front only, as in the concept)."""
    im = cv2.imread(src_path, cv2.IMREAD_UNCHANGED)
    bgr, alpha = im[:, :, :3].copy(), im[:, :, 3]
    if not keep_marks:
        y0, y1, x0, x1 = MARK_BOX
        L = cv2.cvtColor(bgr, cv2.COLOR_BGR2LAB)[:, :, 0]
        m = np.zeros(L.shape, np.uint8)
        m[y0:y1, x0:x1] = (L[y0:y1, x0:x1] < 132).astype(np.uint8)
        m = cv2.dilate(m, np.ones((7, 7), np.uint8))
        bgr = cv2.inpaint(bgr, m, 9, cv2.INPAINT_TELEA)
    f = bgr.astype(np.float32)
    lum = f @ np.array([0.11, 0.59, 0.3], np.float32)
    P, UV, part, N = BK.mesh_arrays(obj)
    idb, _ = BK.rasterize(P, part, cam, CW, CH)
    own = np.where(idb >= 0, part[np.maximum(idb, 0)], -1)
    own[alpha < 128] = -1
    gain = np.ones_like(lum)
    for k in range(len(obj['parts'])):
        m = (own == k).astype(np.float32)
        if m.sum() < 100:
            continue
        sx = 70
        num = cv2.GaussianBlur(lum * m, (0, 0), sigmaX=sx, sigmaY=3)
        den = cv2.GaussianBlur(m, (0, 0), sigmaX=sx, sigmaY=3)
        loc = num / np.maximum(den, 1e-3)
        rown = (lum * m).sum(1) / np.maximum(m.sum(1), 1)
        rowm = np.repeat(rown[:, None], lum.shape[1], 1)
        g = (rowm / np.maximum(loc, 1.0)) ** strength
        gain[m > 0] = g[m > 0]
    out = np.clip(f * gain[..., None], 0, 255).astype(np.uint8)
    cv2.imwrite(out_path, np.dstack([out, alpha]))
    return out_path


def concept_palette(box, k=5):
    im = cv2.cvtColor(cv2.imread(CONCEPT_SRC), cv2.COLOR_BGR2RGB)
    y0, y1, x0, x1 = box
    pal, w = BK._kmeans_palette(im[y0:y1, x0:x1].reshape(-1, 3).astype(np.float32), k)
    return [tuple(map(float, c)) for c in pal]


def texel_points(o, size):
    """Per texel: (ys, xs, 3D position, face normal, part index) via UV-space rasterization."""
    P, UV, part, N = BK.mesh_arrays(o)
    tri = np.full((size, size), -1, np.int32)
    uvp = UV * size
    uvp[..., 1] = size - uvp[..., 1]
    for t in range(len(P)):
        cv2.fillPoly(tri, [np.round(uvp[t] * 16).astype(np.int32)], int(t), lineType=cv2.LINE_8, shift=4)
    ys, xs = np.nonzero(tri >= 0)
    t = tri[ys, xs]
    a0, a1, a2 = uvp[t, 0], uvp[t, 1], uvp[t, 2]
    px, py = xs + 0.5, ys + 0.5
    den = (a1[:, 1] - a2[:, 1]) * (a0[:, 0] - a2[:, 0]) + (a2[:, 0] - a1[:, 0]) * (a0[:, 1] - a2[:, 1])
    den = np.where(np.abs(den) < 1e-12, 1e-12, den)
    l0 = ((a1[:, 1] - a2[:, 1]) * (px - a2[:, 0]) + (a2[:, 0] - a1[:, 0]) * (py - a2[:, 1])) / den
    l1 = ((a2[:, 1] - a0[:, 1]) * (px - a2[:, 0]) + (a0[:, 0] - a2[:, 0]) * (py - a2[:, 1])) / den
    # texels fillPoly gave to a neighbouring triangle (edge texels): dropped, refilled from neighbours after painting
    ok = (l0 > -0.04) & (l1 > -0.04) & (1 - l0 - l1 > -0.04)
    ys, xs, t, l0, l1 = ys[ok], xs[ok], t[ok], np.clip(l0[ok], 0, 1), np.clip(l1[ok], 0, 1)
    l2 = np.clip(1 - l0 - l1, 0, 1)
    X = P[t, 0] * l0[:, None] + P[t, 1] * l1[:, None] + P[t, 2] * l2[:, None]
    return ys, xs, X, N[t], part[t]


def paint_fixes(o, tex, size):
    """Painted touches after the projection (chunky, at texel scale, colours from the concept):
    - the neck under the ring is in the ring's shadow (concept: dark grey band), the ring's underside is dark blue;
    - milk vs empty glass contrast a little stronger all around, so the milk level reads on every side."""
    import cam as C
    ys, xs, X, Nt, pt = texel_points(o, size)
    parts = list(o['parts'])
    t = tex.astype(np.float32)
    body, ring = parts.index('body'), parts.index('ring')
    rng = np.random.default_rng(5)
    blk = np.kron(rng.random((size // 3 + 2, size // 3 + 2)), np.ones((3, 3)))[:size, :size]
    # milk level: z of front texels that project onto the concept's milk line (row ~738 at the front)
    u, v, _ = C.project(X, cam, CW, CH)
    sel = (pt == body) & (np.abs(v - 738) < 4) & (np.abs(u - 600) < 60) & (Nt[:, 1] < -0.8)
    zm = float(np.median(X[sel, 2])) if sel.sum() > 3 else 0.075
    print('milk level z', round(zm, 4), int(sel.sum()))
    b = pt == body
    milk = b & (X[:, 2] < zm) & (X[:, 2] > 0.004)
    glass = b & (X[:, 2] >= zm)
    t[ys[milk], xs[milk]] *= np.array([1.06, 1.05, 1.02])
    t[ys[glass], xs[glass]] = t[ys[glass], xs[glass]] * np.array([0.98, 0.99, 1.0]) + np.array([-2, 0, 4])
    neck = b & (X[:, 2] > 0.1215)
    w = np.clip((X[neck, 2] - 0.1215) / 0.0025, 0, 1)[:, None] * 0.8
    nb = blk[ys[neck], xs[neck]]
    dark = np.array([[98, 102, 100], [112, 114, 108], [84, 90, 92]], np.float32)
    t[ys[neck], xs[neck]] = t[ys[neck], xs[neck]] * (1 - w) + dark[(nb * 2.999).astype(int)] * w
    under = (pt == ring) & (Nt[:, 2] < -0.25)
    t[ys[under], xs[under]] *= 0.62
    # texels not painted above (triangle-edge texels): take the nearest painted texel, no dotted lines along edges
    valid = np.zeros((size, size), bool)
    valid[ys, xs] = True
    _, inds = ndimage.distance_transform_edt(~valid, return_indices=True)
    t = t[inds[0], inds[1]]
    return np.clip(t, 0, 255).astype(np.uint8)


SRC_CLEAN = clean_and_delit(CONCEPT, os.path.join(HERE, 'concept_clean.png'), keep_marks=False)
SRC_MARKS = clean_and_delit(CONCEPT, os.path.join(HERE, 'concept_marks.png'), keep_marks=True)
# texture: the kit density gives 256, where the measuring-scale digits (the item's defining painted detail) are
# ~4 texels tall and unreadable; 512 keeps them legible (README: size override allowed for unreadable text)
size = max(512, BK.texture_size_for(obj))
LATHE = ['body', 'ring', 'teat']
views = [{'name': 'orig', 'T': np.eye(4), 'bonus': 0.3, 'parts': LATHE}]
views += [{'name': f'body{a}', 'T': BK.T_rot_z(a), 'parts': ['body', 'ring']} for a in (45, -45, 90, -90, 135, -135, 180)]
views += [{'name': f'teat{a}', 'T': BK.T_rot_z(a), 'parts': ['teat']} for a in (30, -30, 60, -60, 90, -90, 120, -120, 150, -150, 180)]
part_colors = {'base': concept_palette((1040, 1110, 470, 800), 4)}
tex_path = os.path.join(OUT, f'T_{ID}.png')
cov_a = os.path.join(OUT, 'coverage.png')
cov_b = '/tmp/_b17_cov_marks.png'
texA, st = BK.bake(obj, SRC_CLEAN, cam, views=views, size=size, part_colors=part_colors, quantize=0, debug_png=cov_a)
texB, _ = BK.bake(obj, SRC_MARKS, cam, views=views[:1], size=size, part_colors=part_colors, quantize=0, debug_png=cov_b)
covB = cv2.cvtColor(cv2.imread(cov_b), cv2.COLOR_BGR2RGB)
front = np.all(covB == np.array([60, 200, 80]), axis=2)
tex = texA.copy()
tex[front] = texB[front]
tex = paint_fixes(obj, tex, size)
tex = BK._quantize(tex, 40)
cv2.imwrite(tex_path, cv2.cvtColor(tex, cv2.COLOR_RGB2BGR))
print('bake', json.dumps(st))
R.apply_texture(obj, tex_path, ID)
stats = check_fit(obj)
stats['texture'] = f'{size}x{size}'
stats['iou'] = round(BK.align_report(obj, CONCEPT, cam), 4)
R.render_views(obj, os.path.join(OUT, 'preview.png'), concept_cam=cam, concept_size=(CW, CH), concept_path=CONCEPT_SRC)
if '--preview-only' in sys.argv:
    sys.exit()
R.shelf_fit_render(obj, os.path.join(OUT, 'shelf.png'))
R.export(obj, OUT, ID, tex_path)
json.dump({'id': ID, 'stats': stats, 'bake': st, 'camera': cam}, open(os.path.join(OUT, 'stats.json'), 'w'), indent=1)
R.card(OUT, ID, stats)
print('done', stats)
