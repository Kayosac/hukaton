"""13 Strange teddy bear — collectible shelf item. Run: python3 build.py [--opt] [--align-only] [--shade]

Model (all big masses real, PSX-faceted, few sides):
  head   faceted 10-sided blob, yawed toward the bear's left (+X) like the concept, flattened crown
  snout  squashed blob protruding from the face, nose = small raised wedge
  eyes   two raised button discs (big dark one, small yellow one) sitting on the head surface
  ears   left: thick round disc turned to the front-left; right: folded-over thick flap leaning forward
  body   pear-shaped blob with a flat seat, arms = fat tapered sausages with rounded paws,
  legs   8-sided prisms pointing forward and slightly toe-in, flat foot pads (own part) with a bevel ring
Geometry is placed in concept-camera space: each mass is anchored at the concept pixel it occupies (ray from the
design camera intersected with a depth plane), so the silhouette matches by construction and the geometry, not the
camera optimizer, does the fitting. Painted (projected from the delit concept): fur noise, patches, stitches, button
holes, nose, mouth; painted after the bake: the back seam of head and body (hidden side).
"""
import sys, os, json, math
sys.path.insert(0, '/home/claude/kit')
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import numpy as np
import bpy, bmesh
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
from geo import Builder, clear_scene, check_fit
import bake_local as BK
import render as R
import cam as C

CONCEPT = '/home/claude/concepts/collectible-concepts/13-strange-teddy-v2.png'
ID = 'CI13_StrangeTeddy'
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)
W, H = 1536, 1024
# design camera: geometry is anchored to concept pixels through this camera (build coordinates, before centering)
CAM0 = {'az': 0.0, 'el': 12.0, 'dist': 1.06, 'fov': 18.0, 'target': [0.0, 0.0, 0.105], 'shift': [0.0, 0.0], 'roll': 0.0}
SEED = 13


# ------------------------------------------------------------------ helpers
def ray(px, py, c=CAM0):
    pos, right, upc, f = C.basis(c)
    big = max(W, H)
    fpx = (big / 2) / math.tan(math.radians(c['fov']) / 2)
    sx, sy = c.get('shift', [0, 0])
    xc = (px - W / 2 - sx * big) / fpx
    yc = -(py - H / 2 + sy * big) / fpx
    d = f + right * xc + upc * yc
    return Vector(pos), Vector(d).normalized()


def at(px, py, Y):
    """Point on the concept-camera ray through pixel (px, py) at world depth Y."""
    o, d = ray(px, py)
    t = (Y - o.y) / d.y
    return o + d * t


def rot_verts(verts, center, axis, deg):
    M = Matrix.Rotation(math.radians(deg), 3, axis)
    c = Vector(center)
    for v in verts:
        v.co = c + M @ (v.co - c)


def flat(rings):
    out = []
    for r in rings:
        out += r
    return out


def frame(axis):
    z = Vector(axis).normalized()
    x = Vector((0, 0, 1)) if abs(z.z) < 0.9 else Vector((1, 0, 0))
    x = (x - z * x.dot(z)).normalized()
    return x, z.cross(x), z


def sweep(b, rings_spec, start='flat', end='flat', start_part=None, end_part=None, start_tip=None, end_tip=None):
    """rings_spec: list of (center, axis, radius, sides, rot, scale(x,y), part-of-the-band-after-this-ring).
    Rings are built in the plane normal to axis. start/end: 'flat' polygon cap, or 'tip' fan to start_tip/end_tip."""
    rings = []
    for (c, ax, r, n, rot, sc, _p) in rings_spec:
        x, y, z = frame(ax)
        pts = []
        for i in range(n):
            a = rot + 2 * math.pi * i / n
            pts.append(Vector(c) + x * (math.cos(a) * r * sc[0]) + y * (math.sin(a) * r * sc[1]) +
                       Vector((b.j(0.0007), b.j(0.0007), b.j(0.0007))))
        rings.append(b.ring_verts(pts))
    for k in range(len(rings) - 1):
        mi = b.part(rings_spec[k][6])
        b.bridge(rings[k], rings[k + 1], mi)
    # bridge face [a_i, a_i+1, b_i+1, b_i] -> first ring edges run a_i -> a_i+1, so its cap must run backwards
    mi0 = b.part(start_part or rings_spec[0][6])
    r0 = rings[0]; n = len(r0)
    if start == 'flat':
        b._face(list(reversed(r0)), mi0)
    else:
        cv = b.bm.verts.new(start_tip)
        for i in range(n):
            b._face([r0[(i + 1) % n], r0[i], cv], mi0)
    mi1 = b.part(end_part or rings_spec[-1][6])
    r1 = rings[-1]; n = len(r1)
    if end == 'flat':
        b._face(list(r1), mi1)
    else:
        cv = b.bm.verts.new(end_tip)
        for i in range(n):
            b._face([r1[i], r1[(i + 1) % n], cv], mi1)
    return rings


def prism(b, part, front, back_off):
    """Closed slab: polygon `front` (list of Vector) extruded by back_off."""
    mi = b.part(part)
    f = b.ring_verts([Vector(p) for p in front])
    bk = b.ring_verts([Vector(p) + Vector(back_off) for p in front])
    n = len(f)
    b._face(list(f), mi)
    b._face(list(reversed(bk)), mi)
    for i in range(n):
        k = (i + 1) % n
        b._face([f[k], f[i], bk[i], bk[k]], mi)
    return f, bk


def pick(b, px, py):
    """Ray-cast the current mesh through concept pixel (px, py): (location, outward normal)."""
    b.bm.normal_update()
    tree = BVHTree.FromBMesh(b.bm)
    o, d = ray(px, py)
    loc, nrm, idx, dist = tree.ray_cast(o, d)
    if loc is None:
        raise RuntimeError(f'pick miss at {px},{py}')
    if nrm.dot(d) > 0:
        nrm = -nrm
    return loc, nrm


def button(b, part, px, py, r, h, sides=9):
    loc, n = pick(b, px, py)
    x, y, z = frame(n)
    rot = b.rng.uniform(0, 1)
    def ring(c, rr):
        return [c + (x * math.cos(rot + 2 * math.pi * i / sides) + y * math.sin(rot + 2 * math.pi * i / sides)) * rr
                for i in range(sides)]
    spec = [(loc - n * 0.003, r * 0.95), (loc + n * h, r), (loc + n * (h + 0.0012), r * 0.8)]
    rings = [b.ring_verts(ring(c, rr)) for c, rr in spec]
    mi = b.part(part)
    for k in range(len(rings) - 1):
        b.bridge(rings[k], rings[k + 1], mi)
    b._face(list(reversed(rings[0])), mi)
    b._face(list(rings[-1]), mi)


# ------------------------------------------------------------------ the bear
CENTERS = {}
HEAD_YAW = 17.0
HEAD_ROLL = -4.0


def build():
    b = Builder(ID, seed=SEED)

    # ---------------- body: pear blob with a flat seat
    bc = at(735, 690, 0.020)
    rs = b.blob('body', tuple(bc), (0.050, 0.046, 0.060), rings=5, sides=10, jitter=0.0012)
    for v in flat(rs):
        t = (bc.z - v.co.z) / 0.06               # +1 at the bottom, -1 at the top
        s = 1 + 0.10 * t
        v.co.x = bc.x + (v.co.x - bc.x) * s
        v.co.y = bc.y + (v.co.y - bc.y) * s
        v.co.z = max(v.co.z, 0.0)
    rot_verts(flat(rs), bc, 'Y', -3)
    CENTERS['body'] = Vector(bc)

    # ---------------- legs: forward prisms, slightly toe-in, flat bevelled foot pads
    for side, (ppx, ppy), toe in ((-1, (507, 888), 6.0), (1, (897, 892), 5.0)):
        pad = at(ppx, ppy, -0.088)
        a = math.radians(toe)
        d = Vector((-side * math.sin(a), -math.cos(a), 0.06)).normalized()   # toward the toe
        rr = 0.028
        hip = pad - d * 0.075
        rot = math.pi / 8
        spec = [(hip, d, rr * 1.05, 8, rot, (1, 1), 'legs'),
                (pad - d * 0.036, d, rr * 1.04, 8, rot, (1, 1), 'legs'),
                (pad - d * 0.004, d, rr, 8, rot, (1, 1), 'legs'),
                (pad, d, rr * 0.80, 8, rot, (1, 1), 'pads')]
        rings = sweep(b, spec, end_part='pads')
        for v in flat(rings):
            v.co.z = max(v.co.z, 0.0)

    # ---------------- arms: fat sausages from the shoulders, rounded paws
    for path, rad in (([(575, 530, -0.004), (495, 620, -0.020), (418, 724, -0.030)], [0.019, 0.022, 0.023]),
                      ([(878, 530, -0.004), (950, 628, -0.020), (1052, 742, -0.030)], [0.017, 0.019, 0.023])):
        P = [at(px, py, Y) for px, py, Y in path]
        spec = []
        for i, p in enumerate(P):
            ax = (P[min(i + 1, 2)] - P[max(i - 1, 0)]).normalized()
            spec.append((p, ax, rad[i], 8, 0.3, (1, 0.92), 'arms'))
        a0 = (P[1] - P[0]).normalized(); a1 = (P[2] - P[1]).normalized()
        sweep(b, spec, start='tip', end='tip', start_tip=P[0] - a0 * 0.012, end_tip=P[2] + a1 * 0.011)

    # ---------------- head: 10-sided blob, flattened crown, yawed to +X, slight roll
    hc = at(727, 262, 0.006)
    rs = b.blob('head', tuple(hc), (0.067, 0.052, 0.055), rings=5, sides=10, jitter=0.0012)
    hv = flat(rs)
    for v in hv:
        dz = v.co.z - hc.z
        if dz > 0.025:
            v.co.z = hc.z + 0.025 + (dz - 0.025) * 0.7
        dx = v.co.x - hc.x
        if dx < 0 and dz < 0.01:            # fuller lower-left cheek, slimmer right side (crooked head)
            w = min(1.0, (0.01 - dz) / 0.04)
            v.co.x = hc.x + dx * (1 + 0.04 * w)
            v.co.z -= 0.008 * w * min(1.0, -dx / 0.04)
        elif dx > 0:
            v.co.x = hc.x + dx * 0.95
    CENTERS['head'] = Vector(hc)
    rot_verts(hv, hc, 'Z', HEAD_YAW)
    rot_verts(hv, hc, 'Y', HEAD_ROLL)

    # ---------------- snout (yawed with the head), nose wedge
    sc_ = at(788, 352, -0.044)
    rs = b.blob('snout', tuple(sc_), (0.031, 0.017, 0.021), rings=4, sides=8, jitter=0.0008)
    rot_verts(flat(rs), sc_, 'Z', HEAD_YAW)
    loc, n = pick(b, 776, 318)
    rs = b.blob('nose', tuple(loc + n * 0.001), (0.008, 0.005, 0.005), rings=3, sides=6)
    rot_verts(flat(rs), loc, 'Z', HEAD_YAW)

    # ---------------- button eyes (real raised discs)
    button(b, 'eyes', 614, 296, 0.0105, 0.0025)
    button(b, 'eyes', 846, 202, 0.0089, 0.0022)

    # ---------------- ears
    ec = at(468, 158, 0.014)
    n = Vector((-0.55, -0.80, 0.22)).normalized()
    spec = [(ec - n * 0.010, n, 0.025, 8, 0.2, (1, 1), 'ear_l'),
            (ec - n * 0.002, n, 0.029, 8, 0.2, (1, 1), 'ear_l'),
            (ec + n * 0.006, n, 0.027, 8, 0.2, (1, 1), 'ear_l')]
    sweep(b, spec)
    # right ear: folded flap leaning forward over the head's top-right corner
    poly = [(858, 74, 0.016), (926, 42, 0.022), (1008, 60, 0.016), (1048, 182, -0.012), (1014, 216, -0.016),
            (944, 166, -0.010)]
    front = [at(px, py, Y) for px, py, Y in poly]
    nrm = (front[1] - front[0]).cross(front[3] - front[0]).normalized()
    if nrm.y > 0:
        nrm = -nrm
    prism(b, 'ear_r', front, tuple(-nrm * 0.009))
    return b


def finish_centered(b):
    obj = b.finish(ground=False, center_xy=False)
    me = obj.data
    V = np.array([v.co[:] for v in me.vertices])
    off = np.array([-(V[:, 0].min() + V[:, 0].max()) / 2, -(V[:, 1].min() + V[:, 1].max()) / 2, -V[:, 2].min()])
    me.transform(Matrix.Translation(Vector(off)))
    return obj, off


def winding_errors(obj):
    from collections import Counter
    c = Counter()
    for p in obj.data.polygons:
        vs = list(p.vertices)
        for i in range(len(vs)):
            c[(vs[i], vs[(i + 1) % len(vs)])] += 1
    same = sum(1 for e, k in c.items() if k > 1)
    open_ = sum(1 for (a, b_) in c if (b_, a) not in c)
    return same, open_


clear_scene()
b = build()
obj, OFF = finish_centered(b)
print('winding(same,open)', winding_errors(obj), 'offset', OFF)
fit = check_fit(obj)
print('fit', fit)

BK.unwrap(obj)
cam_path = os.path.join(HERE, 'camera.json')
cam = dict(CAM0)
if os.path.exists(cam_path):
    cam.update(json.load(open(cam_path)))
cam['target'] = list(np.array(cam['target']) + OFF)          # follow the centering offset
if '--opt' in sys.argv:
    cam, s = BK.optimize_camera(obj, CONCEPT, cam, keys=('shift', 'dist'))
    save = dict(cam); save['target'] = list(np.array(cam['target']) - OFF)
    json.dump(save, open(cam_path, 'w'), indent=1)
IOU = round(BK.align_report(obj, CONCEPT, cam, os.path.join(OUT, 'align.png')), 4)
print('IoU', IOU, cam)


def shade_sheet(path):
    """Quick flat-shaded numpy renders (no Cycles): concept | model at concept cam, then 4 turnaround views."""
    import cv2
    rgb, cm = BK.load_concept(CONCEPT)
    P, UV, part, N = BK.mesh_arrays(obj)
    pal = np.array([[150, 95, 60], [120, 140, 90], [170, 120, 80], [90, 60, 50], [200, 160, 60], [160, 90, 120],
                    [110, 110, 160], [200, 200, 120], [90, 160, 160]], np.float32)
    def shade(c, w, h):
        idb, _ = BK.rasterize(P, part, c, w, h)
        L = np.array([-0.4, -0.6, 0.7]); L /= np.linalg.norm(L)
        sh = np.clip(N @ L, 0, 1) * 0.7 + 0.25
        img = np.full((h, w, 3), 60, np.float32)
        m = idb >= 0
        img[m] = sh[idb[m]][:, None] * pal[part[idb[m]] % len(pal)]
        return img.astype(np.uint8)
    top = np.concatenate([rgb, shade(cam, W, H)], 1)
    top = cv2.resize(top, (top.shape[1] // 2, top.shape[0] // 2))
    tiles = []
    tgt = [0, 0, 0.1]
    for az, el in ((0, 10), (90, 10), (160, 15), (-60, 45)):
        tiles.append(shade({'az': az, 'el': el, 'dist': 1.0, 'fov': 18, 'target': tgt, 'shift': [0, 0], 'roll': 0}, 384, 384))
    bot = np.concatenate(tiles, 1)
    sheet = np.concatenate([top, cv2.resize(bot, (top.shape[1], top.shape[1] * 384 // (384 * 4)))], 0)
    cv2.imwrite(path, cv2.cvtColor(sheet, cv2.COLOR_RGB2BGR))


if '--shade' in sys.argv:
    shade_sheet(os.path.join(OUT, '_shade.png'))
if '--align-only' in sys.argv:
    sys.exit()


# ------------------------------------------------------------------ bake
def delit_concept(path, out_path, sigma=45, strength=0.45):
    """Remove most of the concept's large-scale light/shadow (keeps the pixel-noise fur pattern), so projected front
    texels and palette-filled hidden texels match and the game light does the shading. Alpha = concept mask."""
    import cv2
    rgb, cm = BK.load_concept(path)
    m = cm.astype(np.float32)
    f = rgb.astype(np.float32)
    lum = f @ np.array([0.3, 0.59, 0.11], np.float32)
    blur = cv2.GaussianBlur(lum * m, (0, 0), sigma) / np.maximum(cv2.GaussianBlur(m, (0, 0), sigma), 1e-3)
    target = 1.10 * float(np.median(lum[cm]))
    gain = (target / np.maximum(blur, 1.0)) ** strength
    out = np.clip(f * gain[..., None], 0, 255).astype(np.uint8)
    out[~cm] = rgb[~cm]
    cv2.imwrite(out_path, cv2.cvtColor(np.dstack([out, (cm * 255).astype(np.uint8)]), cv2.COLOR_RGBA2BGRA))
    return out_path, out


def fur_palette(img, boxes, k=5, n=12):
    """k-means fur colours from plain-fur boxes of the delit concept, returned as a list of n entries (repeats
    give the cluster weights, so light blotches stay as rare as in the concept)."""
    import cv2
    px = np.concatenate([img[y0:y1, x0:x1].reshape(-1, 3) for x0, y0, x1, y1 in boxes]).astype(np.float32)
    crit = (cv2.TERM_CRITERIA_EPS + cv2.TERM_CRITERIA_MAX_ITER, 30, 1.0)
    _, lab, cen = cv2.kmeans(px, k, None, crit, 3, cv2.KMEANS_PP_CENTERS)
    w = np.bincount(lab.ravel(), minlength=k) / len(lab)
    cnt = np.maximum(1, np.round(w * n)).astype(int)
    out = []
    for c, m in zip(cen, cnt):
        out += [tuple(float(v) for v in c)] * int(m)
    return out


STITCH = np.array([74, 43, 35], np.float32)


def paint_back_seams(tex, have, ys, xs, t, src_view, tpart, X, N):
    """Hidden-side seams, drawn at texel scale: a dashed seam over the crown and down the back of the head
    (continuing the concept's forehead seam) and a cross-stitched seam down the middle of the back."""
    parts = list(obj['parts'])
    hidden = src_view < 0
    texel = 1.0 / TPM                        # metres per texel
    # head: plane through the head centre containing the yawed forward axis
    if 'head' in parts:
        hc = np.array(CENTERS['head']) + OFF
        a = math.radians(HEAD_YAW)
        xaxis = np.array([math.cos(a), math.sin(a), 0.0])
        fwd = np.array([math.sin(a), -math.cos(a), 0.0])
        d = X - hc
        lat = d @ xaxis
        fw = d @ fwd
        ang = np.arctan2(d[:, 2], -fw)       # 0 = straight back, + up over the crown
        s_ = ang * 0.055
        sel = (tpart == parts.index('head')) & hidden & (np.abs(lat) < 0.8 * texel) & (fw < 0.012)
        sel &= (np.mod(s_, 0.0085) < 0.0058) & (d[:, 2] > -0.040)
        tex[ys[sel], xs[sel]] = STITCH
    if 'body' in parts:
        bc = np.array(CENTERS['body']) + OFF
        d = X - bc
        back = d[:, 1] > 0.012
        z = X[:, 2]
        body = (tpart == parts.index('body')) & hidden & back & (z > 0.012) & (z < bc[2] + 0.048)
        line = body & (np.abs(d[:, 0]) < 0.75 * texel)
        zz = np.mod(z, 0.0075)
        cross = body & (np.abs(zz - 0.0037) < 0.55 * texel) & (np.abs(d[:, 0]) < 0.0042)
        tex[ys[line], xs[line]] = STITCH
        tex[ys[cross], xs[cross]] = STITCH * 0.92
    return tex


import cv2
BAKE_SRC, DELIT = delit_concept(CONCEPT, os.path.join(HERE, 'concept_delit.png'))
FUR = fur_palette(DELIT, [(560, 650, 700, 760), (470, 160, 560, 250), (940, 520, 1000, 600), (600, 520, 700, 600)])
print('fur palette', [tuple(int(v) for v in c) for c in FUR])
size = BK.texture_size_for(obj)
TPM = size / math.sqrt(BK.surface_area(obj) / 0.62)
views = [{'name': 'orig', 'T': np.eye(4), 'bonus': 0.3}]
part_colors = {p: FUR for p in ('head', 'body', 'arms', 'legs', 'ear_l', 'ear_r')}
tex_path = os.path.join(OUT, f'T_{ID}.png')
tex, st = BK.bake(obj, BAKE_SRC, cam, views=views, size=size, out_png=tex_path, debug_png=os.path.join(OUT, 'coverage.png'),
                  part_colors=part_colors, post=paint_back_seams, texel_blur=False, quantize=64)
print('bake', json.dumps(st))
R.apply_texture(obj, tex_path, ID)
stats = check_fit(obj)
stats['texture'] = f'{size}x{size}'
R.render_views(obj, os.path.join(OUT, 'preview.png'), concept_cam=cam, concept_size=(W, H), concept_path=CONCEPT)
if '--preview-only' in sys.argv:
    sys.exit()
R.shelf_fit_render(obj, os.path.join(OUT, 'shelf.png'))
R.export(obj, OUT, ID, tex_path)
json.dump({'id': ID, 'stats': stats, 'iou': IOU, 'bake': st, 'camera': cam}, open(os.path.join(OUT, 'stats.json'), 'w'), indent=1)
R.card(OUT, ID, stats)
print('done', stats)
