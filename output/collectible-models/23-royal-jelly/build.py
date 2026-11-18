"""23 Royal jelly — collectible shelf item (four-view concept). Run: python3 build.py [--opt] [--align-only]

Masses: squat 8-sided glass jar (chamfered-rectangle octagon: broad front/back faces, narrower side faces, a little
shallower than wide, as all 4 views show), broad chamfered foot, chamfered shoulder; two real stepped thread rings and
a wide flat pewter lid with a thick edge (12 sides); a crown folded from crumpled gold foil on the lid: thin-walled
zigzag band, five uneven points bent outward (front point tallest), open in the middle; a cream paper sealing strip
running from the lid top over the lid edge down the front to the foot; a round 10-sided dark-red wax seal on it.
Painted from the 4 views: jelly/glass (opaque), facet highlights, rust on the lid, foil creases, bee stamp.
"""
import sys, os, json, math
sys.path.insert(0, '/home/claude/kit')
import numpy as np
import cv2
from scipy import ndimage
import bpy
from geo import Builder, clear_scene, check_fit
import cam as C
import bake as BK
import bake4 as B4
import render as R
import render4 as R4

HERE = os.path.dirname(os.path.abspath(__file__))
PREFIX = '/home/claude/concepts/23-royal-jelly'
ID = 'CI23_RoyalJelly'
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)
d2r = math.radians

# ---------------------------------------------------------------- shape (metres)
W, D = 0.0875, 0.0795           # body half width (x) / half depth (y)
FX, FY = 0.60, 0.66            # front-face / side-face half length as a fraction of W / D (chamfered rectangle)
# body profile (scale, z)
BODY = [(0.73, 0.000), (0.83, 0.008), (0.91, 0.016), (0.96, 0.022), (1.0, 0.027), (1.0, 0.101), (0.975, 0.106), (0.90, 0.113), (0.80, 0.118), (0.66, 0.1195)]
SY = 0.93                      # lid / threads: a little shallower than wide (side views)
RT, RG = 0.0756, 0.0718        # thread ring / groove circumradius (12-gon)
THREADS = [(0.0660, 0.1170), (RG, 0.1190), (RT, 0.1210), (RT, 0.1260), (RG, 0.1285), (RG, 0.1300), (RT, 0.1320),
           (RT, 0.1370), (RG, 0.1395)]
RL = 0.0764                    # lid circumradius
ZL0, ZL1 = 0.1390, 0.1610      # lid bottom / top
LID = [(0.0660, ZL0), (RL - 0.0015, ZL0 + 0.0002), (RL, ZL0 + 0.0015), (RL, ZL1 - 0.0030), (RL - 0.0035, ZL1)]
# crown
CR = (0.0560, 0.0455)          # base half width x / y
# the 5 points (x, y, height above the lid), pre-scale metres: front, front-right, back-right, back-left, front-left.
# Measured once: x/height = front-camera ray through the tip pixel (337,62) (456,96) (410,112) (226,98) (186,120),
# hit on the plane y read from the side views (front tip leans in a little, the others flare out)
TIPS3 = [(0.0040, -0.046, 0.0690), (0.0594, 0.007, 0.0474), (0.0389, 0.046, 0.0338), (-0.0485, 0.042, 0.0411),
         (-0.0661, 0.005, 0.0400)]
CX = -0.001                    # crown centre x
VALLEY = [0.019, 0.022, 0.017, 0.021, 0.018]      # valley after each point
WALL = 0.0018
SCALE = 0.9475                 # whole item (height <= 0.22)


def octa(s, z):
    a, b = FX * W * s, FY * D * s
    w, d = W * s, D * s
    return [(a, -d, z), (w, -b, z), (w, b, z), (a, d, z), (-a, d, z), (-w, b, z), (-w, -b, z), (-a, -d, z)]


def poly12(r, z, sy=SY):
    rot = d2r(-90 + 15)        # a flat face at the front (the strip lies on it)
    return [(r * math.cos(rot + 2 * math.pi * i / 12), r * sy * math.sin(rot + 2 * math.pi * i / 12), z) for i in range(12)]


def front_y12(r):
    return -r * SY * math.cos(d2r(15))


def bridge_rings(b, rings, mi):
    for r0, r1 in zip(rings, rings[1:]):
        b.bridge(r0, r1, mi)


def load_cams():
    p = os.path.join(HERE, 'cameras.json')
    return json.load(open(p)) if os.path.exists(p) else None


def unproject_y(u, v, cam, y0, W_=627, H_=627):
    """Pixel of a view -> point on the plane y=y0."""
    pos, right, upc, f = C.basis(cam)
    big = max(W_, H_)
    fpx = (big / 2) / math.tan(math.radians(cam['fov']) / 2)
    sx, sy = cam.get('shift', [0, 0])
    a = (u - W_ / 2 - sx * big) / fpx
    bb = (H_ / 2 - sy * big - v) / fpx
    d = f + right * a + upc * bb
    t = (y0 - pos[1]) / d[1]
    return pos + d * t


def build(cams):
    b = Builder(ID, seed=23)
    rng = np.random.default_rng(23)
    # --- glass body (opaque-painted), bottom face own part
    mi = b.part('body')
    rings = [b.ring_verts(octa(s, z)) for s, z in BODY]
    bridge_rings(b, rings, mi)
    b.cap(rings[0], b.part('base'), flip=True)
    # --- thread rings + lid (12 sides)
    mi = b.part('thread')
    rings = [b.ring_verts(poly12(r, z)) for r, z in THREADS]
    bridge_rings(b, rings, mi)
    mi = b.part('lid')
    rings = [b.ring_verts(poly12(r, z)) for r, z in LID]
    bridge_rings(b, rings, mi)
    b.cap(rings[-1], b.part('lidtop'))
    b.cap(rings[0], b.part('lidtop'), flip=True)      # hidden underside (inside the threads), tiny in UV anyway

    # --- paper strip: front centre line from the foot bottom over the lid edge onto the lid top
    yt, yl = front_y12(RT), front_y12(RL)
    path = [(BODY[0][1], -D * BODY[0][0])] + [(z, -D * s) for s, z in BODY[1:-1]] + \
           [(0.1205, yt), (ZL0 + 0.0005, yt), (ZL0 + 0.0012, yl), (ZL1 - 0.0030, yl), (ZL1, yl + 0.0035), (ZL1, yl + 0.0150)]
    x0, x1 = -0.0150, 0.0170
    if cams:   # strip edges from the front view (x at the body's front face)
        c = cams['front']
        x0 = unproject_y(295, 420, c, -D * SCALE)[0] / SCALE; x1 = unproject_y(364, 420, c, -D * SCALE)[0] / SCALE
    T = 0.0016
    mi = b.part('strip')
    secs = []
    for i, (z, y) in enumerate(path):
        za, ya = path[max(i - 1, 0)]; zb, yb = path[min(i + 1, len(path) - 1)]
        tz, ty = zb - za, yb - ya
        n = math.hypot(tz, ty)
        tz, ty = tz / n, ty / n
        nz, ny = -ty, tz            # outward normal in the (z, y) plane: path runs up the front (y<0)
        if ny > 0:
            nz, ny = -nz, -ny
        if i == len(path) - 1 or i == len(path) - 2:
            nz, ny = 1.0, 0.0
        if i >= len(path) - 2:
            off = 0.0008
        else:
            off = 0.0004
        pin = (y + ny * off, z + nz * off)
        pout = (y + ny * (off + T), z + nz * (off + T))
        jx = rng.uniform(-0.0006, 0.0006)
        secs.append([b.bm.verts.new((x0 + jx, pin[0], pin[1])), b.bm.verts.new((x0 + jx, pout[0], pout[1])),
                     b.bm.verts.new((x1 + jx, pout[0], pout[1])), b.bm.verts.new((x1 + jx, pin[0], pin[1]))])
    for s0, s1 in zip(secs, secs[1:]):
        b.bridge(s0, s1, mi, closed=False)
    b._face(list(reversed(secs[0])), mi)
    b._face(secs[-1], mi)
    # --- wax seal: 10-sided short thick disc on the strip
    sx, sz, sr = 0.001, 0.061, 0.0315
    if cams:
        c = cams['front']
        p = unproject_y(330, 410, c, -D * SCALE) / SCALE; sx, sz = float(p[0]), float(p[2])
        pe = unproject_y(330 + 72, 410, c, -D * SCALE) / SCALE; sr = float(pe[0] - p[0])
    y0 = -D - 0.0004 - T + 0.0008
    mi = b.part('seal')
    seal = []
    for dy, k in ((0.0, 1.0), (-0.0045, 1.0), (-0.0068, 0.84)):
        pts = []
        for i in range(10):
            a = d2r(9) + 2 * math.pi * i / 10
            r = sr * k * (1 + rng.uniform(-0.04, 0.04))
            pts.append((sx + r * math.cos(a), y0 + dy, sz + r * math.sin(a)))
        seal.append(pts)
    sv = [b.ring_verts(p) for p in seal]
    # rings run along -y: bridge in reverse ring order keeps faces outward (finish() also orients closed shells)
    for r0, r1 in zip(sv, sv[1:]):
        b.bridge(r1, r0, mi)
    b.cap(sv[-1], mi, flip=True, center=(sx, y0 - 0.0075, sz))     # back left open: it sits on the strip

    # --- crown: crumpled foil zigzag band, 5 points, thin wall (outer part 'crown', inner 'crown_in')
    # tips: front-view pixel (u, v) + depth y read from the side views (pre-scale metres); the 3D tip is the
    # front-camera ray through (u, v) hit on the plane y
    z0 = ZL1 - 0.0008
    tips = TIPS3
    print('crown tips', [tuple(round(c, 4) for c in t) for t in tips])
    tang = [math.atan2(t[1] / CR[1], t[0] / CR[0]) for t in tips]
    N = 20
    cols = []
    for k in range(N):
        i, ph = divmod(k, 4)
        a0, a1 = tang[i], tang[(i + 1) % 5]
        if a1 <= a0:
            a1 += 2 * math.pi
        if ph == 0:
            x, y, h = tips[i]
            ang = tang[i]
            rt = math.hypot(x / CR[0], y / CR[1])          # tip flare (radius over the base ellipse)
        else:
            f = {1: 0.24 if i == 0 else 0.2, 2: 0.5, 3: 0.76 if i == 4 else 0.8}[ph] + rng.uniform(-0.02, 0.02)
            ang = a0 + (a1 - a0) * f
            hv = VALLEY[i]
            ht0, ht1 = tips[i][2], tips[(i + 1) % 5][2]
            h = {1: hv + 0.45 * (ht0 - hv), 2: hv, 3: hv + 0.45 * (ht1 - hv)}[ph] + rng.uniform(-0.003, 0.003)
            if (i, ph) in ((0, 1), (4, 3)):     # flanks of the tall front point: high, so it reads broad from the side
                h = tips[0][2] - 0.022
            # a folded point: the crease runs down the middle of the point, its flanks sit a little further in
            tf = lambda j: math.hypot(tips[j][0] / CR[0], tips[j][1] / CR[1])
            fold = lambda j: 0.62 if j == 0 else 0.86          # the tall front point is folded deepest
            rt = {1: fold(i) * tf(i), 2: 1.02, 3: fold((i + 1) % 5) * tf((i + 1) % 5)}[ph]
        rows = []
        for r, zz in enumerate((0.0, 0.010, 0.010 + 0.5 * (h - 0.010), h)):
            flare = (1.0, 1.015, 0.5 * (1.015 + rt) + rng.uniform(-0.03, 0.03), rt)[r]
            crump = 1 + (rng.uniform(-0.035, 0.035) if 0 < r < 3 else (0 if r == 3 else rng.uniform(-0.01, 0.01)))
            rx, ry = CR[0] * flare * crump, CR[1] * flare * crump
            rows.append((CX + rx * math.cos(ang), ry * math.sin(ang), z0 + zz + (rng.uniform(-0.0015, 0.0015) if 0 < r < 3 else 0)))
        cols.append(rows)
        if "--dbgcrown" in sys.argv and k in (18, 19, 0, 1, 2): print("col", k, round(ang, 3), [tuple(round(c, 4) for c in r_) for r_ in rows])
    outer = [[b.bm.verts.new(cols[k][r]) for k in range(N)] for r in range(4)]
    inner = []
    for r in range(4):
        ring = []
        for k in range(N):
            x, y, z = cols[k][r]
            rr = math.hypot(x - CX, y)
            f = (rr - WALL) / rr
            ring.append(b.bm.verts.new((CX + (x - CX) * f, y * f, z - (0.0006 if r == 3 else 0))))
        inner.append(ring)
    mo, mi_ = b.part('crown'), b.part('crown_in')
    for r in range(3):
        b.bridge(outer[r], outer[r + 1], mo)
        b.bridge(inner[r + 1], inner[r], mi_)
    b.bridge(outer[3], inner[3], mo)        # top rim (thin)
    b.bridge(inner[0], outer[0], mi_)       # bottom rim (on the lid)
    return b


clear_scene()
cams = load_cams()
b = build(cams)
obj = b.finish(center_xy=False)
from mathutils import Matrix
obj.data.transform(Matrix.Scale(SCALE, 4))
print('fit', check_fit(obj))


def unwrap_parts(o, factors, margin=0.01):
    """Smart-project every part on its own (islands never cross parts), equalise texel density, then scale parts
    (hidden lid top / bottom smaller, the wax seal with its bee stamp larger) and pack."""
    bpy.context.view_layer.objects.active = o
    for ob in bpy.context.view_layer.objects:
        ob.select_set(ob == o)
    me = o.data
    if not me.uv_layers:
        me.uv_layers.new(name='UVMap')
    for k in range(len(o['parts'])):
        for p in me.polygons:
            p.select = p.material_index == k
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.uv.smart_project(angle_limit=math.radians(50), island_margin=margin, area_weight=0.0,
                                 correct_aspect=True, scale_to_bounds=False)
        bpy.ops.object.mode_set(mode='OBJECT')
    for p in me.polygons:
        p.select = True
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.average_islands_scale()
    bpy.ops.object.mode_set(mode='OBJECT')
    uv = me.uv_layers.active.data
    parts = list(o['parts'])
    for name, f in factors.items():
        k = parts.index(name)
        loops = [l for p in me.polygons if p.material_index == k for l in p.loop_indices]
        c = np.mean([uv[l].uv[:] for l in loops], axis=0)
        for l in loops:
            uv[l].uv = tuple(c + (np.array(uv[l].uv[:]) - c) * f)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(rotate=True, margin=margin)
    bpy.ops.object.mode_set(mode='OBJECT')


unwrap_parts(obj, {'base': 0.3, 'lidtop': 0.45, 'crown_in': 0.75, 'thread': 0.85, 'seal': 1.7})

cam_path = os.path.join(HERE, 'cameras.json')
if cams is None:
    cams = B4.default_cams({'az': 0, 'el': 12, 'dist': 1.6, 'fov': 9.5, 'target': [0, 0, 0.1], 'shift': [0, 0], 'roll': 0})
views = B4.four_views(PREFIX, cams)
if '--opt' in sys.argv:
    res = {}
    for v in views:
        keys = ('az', 'dist', 'shift') if v['name'] in ('left', 'right') else ('dist', 'shift')
        v['cam'], res[v['name']] = BK.optimize_camera(obj, v['concept'], v['cam'], keys=keys, iters=7)
    print('opt', res, {v['name']: round(v['cam']['az'], 1) for v in views})
    json.dump({v['name']: v['cam'] for v in views}, open(cam_path, 'w'), indent=1)
iou = B4.align_all(obj, views, os.path.join(OUT, 'align.png'), tile=627)
_a = cv2.imread(os.path.join(OUT, 'align.png')); _w = _a.shape[1] // 4
def _contours(obj, views):
    P, UV, part, N = BK.mesh_arrays(obj)
    tiles = []
    for v in views:
        im = cv2.imread(v['concept'], cv2.IMREAD_UNCHANGED)
        bg = im[:, :, 3:4] / 255.0
        im = (im[:, :, :3] * bg + 60 * (1 - bg)).astype(np.uint8)
        idb, _ = BK.rasterize(P, part, v['cam'], 627, 627)
        own = np.where(idb >= 0, part[np.maximum(idb, 0)], -1)
        for k in range(len(obj['parts'])):
            m = (own == k).astype(np.uint8)
            cs, _ = cv2.findContours(m, cv2.RETR_LIST, cv2.CHAIN_APPROX_NONE)
            col = [(0, 255, 255), (255, 0, 255), (0, 255, 0), (255, 255, 0), (0, 128, 255), (255, 128, 0), (128, 255, 128), (255, 255, 255), (0, 0, 255), (128, 128, 255)][k % 10]
            cv2.drawContours(im, cs, -1, col, 1)
        tiles.append(im)
    cv2.imwrite('/tmp/b23_cont.png', np.concatenate([np.concatenate(tiles[:2], 1), np.concatenate(tiles[2:], 1)], 0))
_contours(obj, views)
if '--rows' in sys.argv:
    P_, UV_, part_, N_ = BK.mesh_arrays(obj)
    for v in views:
        rgb, cm = BK.load_concept(v['concept'])
        idb, _ = BK.rasterize(P_, part_, v['cam'], 627, 627)
        mm = idb >= 0
        print(v['name'])
        line = []
        for y in range(20, 570, 10):
            a = np.nonzero(cm[y])[0]; m = np.nonzero(mm[y])[0]
            fa = f'{a.min():3d}-{a.max():3d}' if len(a) else '   -   '
            fm = f'{m.min():3d}-{m.max():3d}' if len(m) else '   -   '
            dl = (m.min() - a.min()) if len(a) and len(m) else 0
            dr = (m.max() - a.max()) if len(a) and len(m) else 0
            line.append(f'{y:3d} c{fa} m{fm} L{-dl:+3d} R{dr:+3d}')
        for i in range(0, len(line), 4):
            print('   '.join(line[i:i + 4]))
cv2.imwrite('/tmp/b23_align2x2.png', np.concatenate([np.concatenate([_a[:, :2 * _w]], 1), _a[:, 2 * _w:]], 0))
print('IoU', iou)
if '--align-only' in sys.argv:
    sys.exit()

# ---------------------------------------------------------------- texture
size = BK.texture_size_for(obj)
if '--size' in sys.argv:
    size = int(sys.argv[sys.argv.index('--size') + 1])
tex_path = os.path.join(OUT, f'T_{ID}.png')


def concept_palette(view, box, k=4, gain=1.0):
    im = cv2.cvtColor(cv2.imread(f'{PREFIX}_{view}.png'), cv2.COLOR_BGR2RGB)
    y0, y1, x0, x1 = box
    pal, w = BK._kmeans_palette(im[y0:y1, x0:x1].reshape(-1, 3).astype(np.float32), k)
    return [tuple(float(c) * gain for c in p) for p in pal]


part_colors = {
    'lidtop': concept_palette('front', (186, 204, 168, 205)) + concept_palette('front', (186, 204, 452, 485), 3),
    'crown_in': concept_palette('front', (120, 160, 225, 270), 4, 0.85),
    'base': concept_palette('front', (535, 552, 200, 460), 4, 0.8),
}
print('palettes', {k: [tuple(int(c) for c in p) for p in v] for k, v in part_colors.items()})
tex, st = B4.bake_multi(obj, views, size=size, out_png=tex_path, debug_png=os.path.join(OUT, 'coverage.png'),
                        part_colors=part_colors, erode_px=4, min_facing=0.28)


def crown_palette(k=6):
    """Gold-foil colours of the crown in all 4 views (pixels the model's crown covers, eroded)."""
    P_, UV_, part_, N_ = BK.mesh_arrays(obj)
    ci = list(obj['parts']).index('crown')
    cols = []
    for v in views:
        rgb, cm = BK.load_concept(v['concept'])
        idb, _ = BK.rasterize(P_, part_, v['cam'], 627, 627)
        m = (idb >= 0) & (part_[np.maximum(idb, 0)] == ci) & cm
        m = ndimage.binary_erosion(m, iterations=3)
        hsv = cv2.cvtColor(rgb, cv2.COLOR_RGB2HSV)
        m &= (hsv[..., 1] > 90) & (hsv[..., 2] > 110)       # gold foil only (no grey lid, no deep inner shadow)
        cols.append(rgb[m])
    pal, w = BK._kmeans_palette(np.concatenate(cols).astype(np.float32), k)
    o = np.argsort(pal @ np.array([0.3, 0.59, 0.11]))
    return pal[o], np.asarray(w)[o]


def paint_crown(tex):
    """Crumpled foil: every triangle of the crown gets its own gold tone (concept palette, weighted by how often
    the colour appears), plus fine blocky mottling; the inside of the band is the same foil, darker."""
    P_, part_, N_, ys, xs, t, X = B4._texels(obj, size)
    parts = list(obj['parts'])
    pal, w = crown_palette()
    print('crown palette', pal.astype(int).tolist(), np.round(w, 2).tolist())
    rng = np.random.default_rng(7)
    cdf = np.cumsum(w) / np.sum(w)
    tone = pal[np.clip(np.searchsorted(cdf, rng.random(len(P_))), 0, len(pal) - 1)]
    tone = tone * 0.75 + (pal * np.asarray(w)[:, None]).sum(0) / np.sum(w) * 0.25   # facets a little less contrasty
    # light from above: upward-facing facets a touch lighter, the folds' undersides darker
    tone = tone * (0.92 + 0.16 * np.clip(N_[:, 2], -1, 1))[:, None]
    blk = np.kron(rng.random((size // 2 + 1, size // 2 + 1)), np.ones((2, 2)))[:size, :size]
    out = tex.astype(np.float32)
    for name, g in (('crown', 1.17), ('crown_in', 0.82)):
        m = part_[t] == parts.index(name)
        c = tone[t[m]] * g * (0.96 + 0.08 * blk[ys[m], xs[m]])[:, None]
        out[ys[m], xs[m]] = c
    # glass: the octagon's corner edges catch the light (thin pale lines, as drawn on every side)
    bi = parts.index('body')
    m = part_[t] == bi
    Xb = X[m] / SCALE
    zs, ss = np.array([z for s_, z in BODY]), np.array([s_ for s_, z in BODY])
    sc = np.interp(Xb[:, 2], zs, ss)
    best = np.full(len(Xb), 1e9)
    for cx_, cy_, _ in octa(1.0, 0):
        best = np.minimum(best, np.hypot(Xb[:, 0] - cx_ * sc, Xb[:, 1] - cy_ * sc))
    edge = (best < 0.0017) & (Xb[:, 2] > 0.003) & (Xb[:, 2] < 0.117)
    a = np.clip(1.0 - best[edge] / 0.0017, 0, 1)[:, None] * 0.45 + 0.15
    yy, xx = ys[m][edge], xs[m][edge]
    out[yy, xx] = out[yy, xx] * (1 - a) + np.array([214, 200, 178], np.float32) * a
    print('glass edge texels', int(edge.sum()))
    return np.clip(out, 0, 255).astype(np.uint8)


img = cv2.imread(tex_path)
med = cv2.medianBlur(img, 5)
lum = lambda a: a.astype(np.float32) @ np.array([0.11, 0.59, 0.3], np.float32)
dark = (lum(med) - lum(img)) > 18
img[dark] = med[dark]
cv2.imwrite(tex_path, img)
tex = paint_crown(cv2.cvtColor(cv2.imread(tex_path), cv2.COLOR_BGR2RGB))
cv2.imwrite(tex_path, cv2.cvtColor(tex, cv2.COLOR_RGB2BGR))
print('bake', json.dumps(st), 'despeckled', int(dark.sum()))
R4.apply_texture(obj, tex_path, ID)
stats = check_fit(obj)
stats['texture'] = f'{size}x{size}'
stats['iou'] = iou
if '--quick' in sys.argv:
    R._world((0.36, 0.34, 0.32), 0.9); R._lights()
    tiles = []
    for v in views:
        tiles.append(cv2.resize(R4._render(v['cam'], 627, 627, samples=6), (400, 400), interpolation=cv2.INTER_AREA))
    for az, el in ((-40, 22), (140, 22), (15, 60), (35, 8)):
        c = {'az': az, 'el': el, 'dist': 0.22 * 4.2, 'fov': 18, 'target': [0, 0, 0.11], 'shift': [0, 0]}
        tiles.append(cv2.resize(R4._render(c, 400, 400, samples=6), (400, 400)))
    cv2.imwrite('/tmp/b23_quick.png', np.concatenate([np.concatenate(tiles[:4], 1), np.concatenate(tiles[4:], 1)], 0))
    sys.exit()
R4.preview4(obj, views, os.path.join(OUT, 'preview.png'))
if '--preview-only' in sys.argv:
    sys.exit()
R.shelf_fit_render(obj, os.path.join(OUT, 'shelf.png'))
R.export(obj, OUT, ID, tex_path)
json.dump({'id': ID, 'stats': stats, 'bake': st, 'cameras': {v['name']: v['cam'] for v in views}},
          open(os.path.join(OUT, 'stats.json'), 'w'), indent=1)
R4.card4(OUT, ID, stats)
print('done', stats)
