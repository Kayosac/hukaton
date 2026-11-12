"""03 Poop (Govеshka) — collectible shelf item. Run: python3 build.py [--opt] [--align-only]

Model: a squat flat-bottomed base mass whose top is a real helical ramp (the first coil), one tube wound ~1.8 turns
along a rising, tightening spiral on top of it (the 2nd and 3rd tiers, real grooves where the turns rest on each
other), continuing into a tapering curled tip that hooks to the left. A small core dome fills the eye of the spiral.
The pixel-noise brown texture and the light/shadow in the grooves are projected from the concept.
"""
import sys, os, json, math
sys.path.insert(0, '/home/claude/kit')
import numpy as np
import bpy
from mathutils import Vector
from geo import Builder, clear_scene, check_fit
import bake as BK
import render as R

HERE = os.path.dirname(os.path.abspath(__file__))
CONCEPT = '/home/claude/concepts/collectible-concepts/03-poop-v1.png'
ID = 'CI03_Poop'
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)

# ------------------------------------------------------------------ shape parameters (metres)
TH0 = 95.0            # deg, where the coil starts (back, +Y side); the spiral turns counter-clockwise seen from top
TURNS = 1.75          # spiral length before the tip
PITCH = 0.046         # rise per turn
ZB0 = 0.041           # bottom of the tube at the start
SEG_PER_TURN = 14
TUBE_SIDES = 9
# keyframes over t (turns): outer extent (R + a), radial half-width a, vertical half-height b
KT = [0.00, 0.25, 0.50, 0.75, 1.00, 1.25, 1.50, 1.75]
OUTER = [0.084, 0.090, 0.083, 0.076, 0.052, 0.051, 0.049, 0.044]
ZBK = [0.038, 0.042, 0.061, 0.0755, 0.079, 0.090, 0.112, 0.1225]
A = [0.026, 0.026, 0.026, 0.025, 0.023, 0.023, 0.023, 0.021]
B = [0.024, 0.026, 0.028, 0.026, 0.023, 0.024, 0.024, 0.023]
# base mass profile (r, z) bottom -> top; rings marked with a weight follow the helical ramp of the coil
BASE = [(0.097, 0.000, 0.0), (0.107, 0.011, 0.0), (0.106, 0.028, 0.1), (0.101, 0.042, 0.5),
        (0.088, 0.055, 0.9), (0.060, 0.062, 1.0), (0.025, 0.064, 1.0), (0.0, 0.064, 1.0)]
BASE_REF = 0.064      # z of the base top at the ramp's middle
SEED = 3
TILT = float(os.environ.get('TILT', 4.0))          # deg forward lean of the coils
TILT_Z0 = 0.035


def interp(t, ys):
    return float(np.interp(t, KT, ys))


def zb(t):
    return interp(t, ZBK)


def build():
    b = Builder(ID, seed=SEED)
    rng = np.random.default_rng(SEED)

    # ---------------- base: lathe, top rings ride the helical ramp (step at the coil start)
    rings = b.lathe('base', [(r, z) for r, z, _ in BASE], sides=12, jitter=0.0015, rot=math.radians(TH0 + 15),
                    cap_bottom=True, cap_top=False, flat=False)
    for (r, z, w), ring in zip(BASE, rings):
        for v in ring:
            th = math.degrees(math.atan2(v.co.y, v.co.x)) if r > 0 else TH0 + 180
            t = ((th - TH0) % 360.0) / 360.0
            v.co.z += w * (zb(t) + 0.006 - BASE_REF)
    # base: squat oval foot (a bit shallower front-back), bulging a bit to the left (crooked)
    for ring in rings:
        for v in ring:
            w = max(0.0, (0.05 - v.co.z) / 0.05)
            v.co.y *= 1 - 0.10 * w
            v.co.x *= 1 + (0.03 if v.co.x < 0 else 0.0) * w
            if v.co.x < 0 and 0.025 < v.co.z < 0.07:          # fuller left shoulder
                v.co.x *= 1.05
    # ---------------- coil: elliptic tube along a rising spiral, then the tip
    n = int(TURNS * SEG_PER_TURN)
    path, rad = [], []
    for i in range(n + 1):
        t = TURNS * i / n
        th = math.radians(TH0 + 360.0 * t)
        a, bb = interp(t, A), interp(t, B)
        Rc = interp(t, OUTER) - a
        wob = 1 + rng.uniform(-0.05, 0.05)
        path.append(Vector((Rc * math.cos(th), Rc * math.sin(th), zb(t) + bb)))
        rad.append((a * wob, bb * (1 + rng.uniform(-0.04, 0.04)), min(a, max(Rc - 0.004, 0.004))))
    # tip: from the end of the spiral (+X side) up, leaning back to the centre, hooking to the left
    p = path[-1]
    tip = [((0.029, 0.012, 0.147), (0.0190, 0.0185)), ((0.029, 0.015, 0.159), (0.0150, 0.0145)),
           ((0.026, 0.015, 0.169), (0.0108, 0.0103)), ((0.021, 0.012, 0.1745), (0.0076, 0.0072)),
           ((0.0155, 0.009, 0.1765), (0.0052, 0.0050)), ((0.0105, 0.006, 0.1755), (0.0030, 0.0029))]
    for (x, y, z), (a, bb) in tip:
        path.append(Vector((x, y, z)))
        rad.append((a, bb, a))
    ring_pts = []
    prev_x = None
    for i, P in enumerate(path):
        T = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
        rho = Vector((P.x, P.y, 0))
        x = rho.normalized() if rho.length > 0.012 and i <= n else prev_x
        x = (x - T * x.dot(T)).normalized()
        prev_x = x
        y = x.cross(T).normalized()
        a, bb, ai = rad[i]
        pts = []
        for k in range(TUBE_SIDES):
            ph = 2 * math.pi * k / TUBE_SIDES + math.pi / TUBE_SIDES
            c, s = math.cos(ph), math.sin(ph)
            ext = a if c >= 0 else ai
            q = P + x * (c * ext) + y * (s * bb)
            q += Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * 0.0009
            pts.append(q)
        ring_pts.append(pts)
    mi = b.part('coil', False)
    vr = [b.ring_verts(pts) for pts in ring_pts]
    for r0, r1 in zip(vr, vr[1:]):
        for k in range(TUBE_SIDES):
            k2 = (k + 1) % TUBE_SIDES
            b._face([r0[k], r1[k], r1[k2], r0[k2]], mi)
    # rounded start of the coil (lies on the ramp step at the back)
    P0, T0 = path[0], (path[1] - path[0]).normalized()
    c0 = sum((v.co for v in vr[0]), Vector()) / TUBE_SIDES
    cap_r = [b.bm.verts.new(c0 + (v.co - c0) * 0.62 - T0 * 0.011) for v in vr[0]]
    for k in range(TUBE_SIDES):
        k2 = (k + 1) % TUBE_SIDES
        b._face([cap_r[k], vr[0][k], vr[0][k2], cap_r[k2]], mi)
    b.cap(cap_r, mi, flip=True, center=tuple(c0 - T0 * 0.015))
    # pointed end of the tip, hooking left and slightly down
    end = path[-1] + Vector((-0.0035, -0.001, -0.0012))
    b.cap(vr[-1], mi, flip=False, center=tuple(end))
    # ---------------- core: small dome filling the eye of the spiral
    b.lathe('coil', [(0.040, 0.080), (0.032, 0.118), (0.018, 0.142), (0.0, 0.150)], sides=8, jitter=0.001,
            cap_bottom=True, cap_top=False, offset=(0.004, 0.0, 0), flat=False)
    # ---------------- the whole stack leans forward (toward the viewer, -Y): front of each tier drops, the
    # upper tiers overhang the ledge below them like in the concept; the base top follows with a weight
    sa, ca = math.sin(math.radians(TILT)), math.cos(math.radians(TILT))
    for v in b.bm.verts:
        mats = {f.material_index for f in v.link_faces}
        w = 1.0 if b.parts.index('base') not in mats else min(1.0, max(0.0, (v.co.z - 0.02) / 0.04))
        if w <= 0:
            continue
        y, z = v.co.y, v.co.z - TILT_Z0
        y2, z2 = y * ca - z * sa, z * ca + y * sa
        v.co.y = y + (y2 - y) * w
        v.co.z = TILT_Z0 + z + (z2 - z) * w
    return b


def shell_volumes(obj):
    """Signed volume per part (all must be > 0: outward normals)."""
    me = obj.data
    vol = {}
    for p in me.polygons:
        vs = [me.vertices[i].co for i in p.vertices]
        s = 0.0
        for i in range(1, len(vs) - 1):
            s += vs[0].dot(vs[i].cross(vs[i + 1])) / 6
        vol[p.material_index] = vol.get(p.material_index, 0) + s
    return vol


def winding_errors(obj):
    """Edges used twice in the same direction (inconsistent normals) and open edges."""
    from collections import Counter
    me = obj.data
    c = Counter()
    for p in me.polygons:
        vs = list(p.vertices)
        for i in range(len(vs)):
            c[(vs[i], vs[(i + 1) % len(vs)])] += 1
    same = sum(1 for e, k in c.items() if k > 1)
    open_ = sum(1 for (a, b_) in c if (b_, a) not in c)
    return same, open_


clear_scene()
b = build()
obj = b.finish()
for poly in obj.data.polygons:          # flat foot: hard edge around the base
    if poly.normal.z < -0.9:
        poly.use_smooth = False
print('volumes', shell_volumes(obj), 'winding(same,open)', winding_errors(obj))
fit = check_fit(obj)
print('fit', fit)

BK.unwrap(obj)
cam_path = os.path.join(HERE, 'camera.json')
cam = json.load(open(cam_path)) if os.path.exists(cam_path) else \
    {'az': 0, 'el': 16, 'dist': 1.0, 'fov': 18, 'target': [0, 0, 0.085], 'shift': [0, 0], 'roll': 0}
if '--opt' in sys.argv:
    cam, s = BK.optimize_camera(obj, CONCEPT, cam, keys=('az', 'dist', 'shift'))
    json.dump(cam, open(cam_path, 'w'), indent=1)
print('IoU', BK.align_report(obj, CONCEPT, cam, os.path.join(OUT, 'align.png')), cam)
if '--shade' in sys.argv:   # quick flat-shaded model at the concept camera next to the concept (no Cycles)
    import cv2
    import cam as C
    rgb, cm = BK.load_concept(CONCEPT)
    H, W = cm.shape
    P, UV, part, N = BK.mesh_arrays(obj)
    idb, _ = BK.rasterize(P, part, cam, W, H)
    L = np.array([-0.4, -0.6, 0.7]); L /= np.linalg.norm(L)
    sh = np.clip(N @ L, 0, 1) * 0.75 + 0.2
    img = np.zeros((H, W, 3), np.float32)
    m = idb >= 0
    img[m] = (sh[idb[m]][:, None] * np.array([150, 95, 60]))
    both = np.concatenate([rgb[:, :, ::-1].astype(np.float32), img[:, :, ::-1]], 1)
    cv2.imwrite(os.path.join(OUT, '_shade.png'), cv2.resize(both, (W, H // 2)).astype(np.uint8))
if '--align-only' in sys.argv:
    sys.exit()

def delit_concept(path, out_path, sigma=30, strength=0.75):
    """Remove most of the concept's large-scale light/shadow (keeps the pixel-noise colour pattern), so texels
    projected from different views (front, rotated copies for the hidden back) match and the real game light
    does the shading. Alpha = concept silhouette mask."""
    import cv2
    rgb, cm = BK.load_concept(path)
    m = cm.astype(np.float32)
    f = rgb.astype(np.float32)
    lum = f @ np.array([0.3, 0.59, 0.11], np.float32)
    blur = cv2.GaussianBlur(lum * m, (0, 0), sigma) / np.maximum(cv2.GaussianBlur(m, (0, 0), sigma), 1e-3)
    target = 1.08 * float(np.median(lum[cm]))
    gain = (target / np.maximum(blur, 1.0)) ** strength
    out = np.clip(f * gain[..., None], 0, 255).astype(np.uint8)
    out[~cm] = rgb[~cm]
    rgba = np.dstack([out, (cm * 255).astype(np.uint8)])
    cv2.imwrite(out_path, cv2.cvtColor(rgba, cv2.COLOR_RGBA2BGRA))
    return out_path


BAKE_SRC = delit_concept(CONCEPT, os.path.join(HERE, 'concept_delit.png'))
size = BK.texture_size_for(obj)
views = [{'name': 'orig', 'T': np.eye(4), 'bonus': 0.3},
         {'name': 'mirror_x', 'T': BK.T_mirror_plane('x'), 'bonus': 0.1}]
views += [{'name': f'rot{a}', 'T': BK.T_rot_z(a)} for a in (60, -60, 120, -120, 180)]
tex_path = os.path.join(OUT, f'T_{ID}.png')
tex, st = BK.bake(obj, BAKE_SRC, cam, views=views, size=size, out_png=tex_path, debug_png=os.path.join(OUT, 'coverage.png'))
print('bake', json.dumps(st))
R.apply_texture(obj, tex_path, ID)
stats = check_fit(obj)
stats['texture'] = f'{size}x{size}'
R.render_views(obj, os.path.join(OUT, 'preview.png'), concept_cam=cam, concept_size=(1536, 1024), concept_path=CONCEPT)
R.shelf_fit_render(obj, os.path.join(OUT, 'shelf.png'))
R.export(obj, OUT, ID, tex_path)
json.dump({'id': ID, 'stats': stats, 'bake': st, 'camera': cam}, open(os.path.join(OUT, 'stats.json'), 'w'), indent=1)
R.card(OUT, ID, stats)
print('done', stats)
