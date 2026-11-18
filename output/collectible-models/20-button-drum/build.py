"""20 Button drum - collectible shelf item (four-view concept). Run: python3 build.py [--opt] [--align-only]

Masses (metres): squat 12-sided drum of rolled birch bark (slightly barrel-shaped, a little crooked: lean + wobbled
radii), a darker wooden base ring with a chamfer at the bottom, and a big flat FOUR-HOLE BUTTON as the drumhead: thick
raised rim that sticks out past the bark wall, a recessed flat centre and four real shallow 8-sided holes.
Back: the overlapping bark seam = a thin flap shell standing proud of the wall with a step at its free edge.
Front: two pine needles (4-sided tapered prisms, tips up) crossed in an X, standing a little proud of the wall, held by
three twine wraps (closed 4-sided loops) at mid-height; they touch the wall at the twine and at their lower ends.
Painted: bark marks and tone from the 4 concept views; needles (olive with brown bases), twine (strands), hole insides.
"""
import sys, os, json, math
sys.path.insert(0, '/home/claude/kit')
import numpy as np
import cv2
from scipy import ndimage
import bpy
from mathutils import Vector, geometry as mgeo
from geo import Builder, clear_scene, check_fit
import bake as BK
import bake4 as B4
import render as R
import render4 as R4

HERE = os.path.dirname(os.path.abspath(__file__))
PREFIX = '/home/claude/concepts/20-button-drum'
ID = 'CI20_ButtonDrum'
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)

NS = 12
ROT = math.radians(15)            # a flat facet faces the front (-Y)
WOB = [0.012, -0.010, 0.006, 0.016, -0.012, 0.004, 0.010, -0.014, 0.008, -0.006, 0.014, -0.008]
LEAN = (0.022, -0.010)            # top drifts a little right and toward the front (per metre of height)
SCALE = (1.0, 0.985)

# drum body profile (r, z): chamfered base ring, step, slightly bulging bark wall up to the rim
BASE = [(0.0880, 0.0000), (0.0940, 0.0045), (0.0958, 0.0125), (0.0948, 0.0150)]
WALL = [(0.0948, 0.0150), (0.0952, 0.0420), (0.0960, 0.0720), (0.0950, 0.1000), (0.0935, 0.1110)]
# button: underside chamfer, side band, top chamfer, flat rim top, inner chamfer, recessed plate
RIM = [(0.0935, 0.1110), (0.0995, 0.1125), (0.1015, 0.1165), (0.1015, 0.1265), (0.0990, 0.1325),
       (0.0945, 0.1345), (0.0800, 0.1345), (0.0778, 0.1320), (0.0770, 0.1285)]
Z_PLATE = 0.1285
HOLE_R, HOLE_D, HOLE_OFF = 0.0098, 0.0050, 0.0225
HOLE_C = [(-HOLE_OFF, -HOLE_OFF), (HOLE_OFF, -HOLE_OFF), (HOLE_OFF, HOLE_OFF), (-HOLE_OFF, HOLE_OFF)]


def ring2d(r, z, wob=True):
    """The 12 xy points of a body ring of nominal radius r at height z (wobble, oval, lean)."""
    pts = []
    for i in range(NS):
        a = ROT + 2 * math.pi * i / NS
        rr = r * (1 + (WOB[i] if wob else 0.0))
        pts.append((math.cos(a) * rr * SCALE[0] + LEAN[0] * z, math.sin(a) * rr * SCALE[1] + LEAN[1] * z))
    return pts


def prof_r(prof, z):
    zs = [p[1] for p in prof]; rs = [p[0] for p in prof]
    return float(np.interp(z, zs, rs))


def surf_dist(pts, origin, d):
    """2D ray (origin, unit dir d) against the closed polygon pts: distance to the boundary."""
    best = None
    ox, oy = origin
    for i in range(len(pts)):
        ax, ay = pts[i]; bx, by = pts[(i + 1) % len(pts)]
        ex, ey = bx - ax, by - ay
        den = d[0] * ey - d[1] * ex
        if abs(den) < 1e-12:
            continue
        t = ((ax - ox) * ey - (ay - oy) * ex) / den
        s = ((ax - ox) * d[1] - (ay - oy) * d[0]) / den
        if t > 0 and -1e-9 <= s <= 1 + 1e-9:
            best = t if best is None else min(best, t)
    return best


def wall_front_y(x, z, prof=WALL):
    """y of the drum surface seen from the front (-Y) at (x, z)."""
    pts = ring2d(prof_r(prof, z), z)
    t = surf_dist(pts, (x, -1.0), (0.0, 1.0))
    return -1.0 + t


def wall_point(ang, z, off, prof=WALL):
    """Point on the wall at polar angle ang (about the lean axis) pushed `off` outward."""
    pts = ring2d(prof_r(prof, z), z)
    c = (LEAN[0] * z, LEAN[1] * z)
    d = (math.cos(ang), math.sin(ang))
    t = surf_dist(pts, c, d)
    return (c[0] + d[0] * (t + off), c[1] + d[1] * (t + off), z)


def build():
    b = Builder(ID, seed=20)
    j = lambda: (b.j(0.0006), b.j(0.0006), b.j(0.0004))

    def rings(prof, part, start=None):
        mi = b.part(part)
        rs = [start] if start else []
        for r, z in (prof[1:] if start else prof):
            rs.append([b.bm.verts.new((x + jx, y + jy, z + jz)) for (x, y), (jx, jy, jz) in
                       zip(ring2d(r, z), [j() for _ in range(NS)])])
        for a, c in zip(rs, rs[1:]):
            b.bridge(a, c, mi)
        return rs

    rb = rings(BASE, 'base')
    # the bottom chamfer is only ever seen at grazing angles (concept outline specks): own part, painted
    bc = b.part('basech')
    for f in b.bm.faces:
        if max(v.co.z for v in f.verts) < 0.006:
            f.material_index = bc
    b.cap(rb[0], b.part('bottom'), flip=True)
    rw = rings(WALL, 'bark', start=rb[-1])            # the bark starts on the base ring's bevelled top edge
    rr = rings(RIM, 'rim', start=rw[-1])
    # the plate is flush with the last rim ring: 12-gon with 4 holes (scanfill), then the hole dimples
    top = rr[-1]
    zc = Z_PLATE
    cx, cy = LEAN[0] * zc, LEAN[1] * zc
    holes = []
    for hx, hy in HOLE_C:
        hp = []
        for k in range(8):
            a = math.radians(22.5) + 2 * math.pi * k / 8
            hp.append(Vector((cx + hx + math.cos(a) * HOLE_R, cy + hy + math.sin(a) * HOLE_R, zc)))
        holes.append(hp)
    outer = [v.co.copy() for v in top]
    polys = [outer] + holes
    flat = [p for poly in polys for p in poly]
    tris = mgeo.tessellate_polygon([[tuple(p) for p in poly] for poly in polys])
    verts = list(top) + [b.bm.verts.new(p) for poly in holes for p in poly]
    mp = b.part('plate')
    for t in tris:
        a, c, d = (flat[i] for i in t)
        if (c - a).cross(d - a).z < 0:
            t = (t[0], t[2], t[1])
        b._face([verts[i] for i in t], mp)
    mh = b.part('hole')
    for hi in range(4):
        ring_top = verts[NS + hi * 8: NS + hi * 8 + 8]
        hx, hy = HOLE_C[hi]
        bot = []
        for k in range(8):
            a = math.radians(22.5) + 2 * math.pi * k / 8
            bot.append(b.bm.verts.new((cx + hx + math.cos(a) * HOLE_R * 0.8, cy + hy + math.sin(a) * HOLE_R * 0.8,
                                       zc - HOLE_D)))
        # walls face inward (toward the hole axis): bridge top->bottom reversed
        for k in range(8):
            k2 = (k + 1) % 8
            b._face([ring_top[k], ring_top[k2], bot[k2], bot[k]], mh)
        b._face(list(bot), mh)

    # ---- back seam flap: thin shell over the wall around +Y, free (proud) edge on the -X side
    mf = b.part('flap')
    angs = [math.radians(a) for a in (67, 79, 91, 103)]
    offs = [0.0012, 0.0022, 0.0030, 0.0040]
    zs = [0.0175, 0.042, 0.072, 0.1000, 0.1085]
    grid = [[b.bm.verts.new(wall_point(a, z, o)) for a, o in zip(angs, offs)] for z in zs]
    for r0, r1 in zip(grid, grid[1:]):
        for i in range(len(angs) - 1):
            b._face([r0[i], r0[i + 1], r1[i + 1], r1[i]], mf)
    # free edge: a side face stepping back down to the wall
    edge_in = [b.bm.verts.new(wall_point(angs[-1], z, -0.0015)) for z in zs]
    for k in range(len(zs) - 1):
        b._face([grid[k][-1], edge_in[k], edge_in[k + 1], grid[k + 1][-1]], mf)
    # bottom/top lips of the flap (small faces back to the wall)
    for row, sgn in ((0, -1), (len(zs) - 1, 1)):
        z = zs[row]
        inner = [b.bm.verts.new(wall_point(a, z, -0.0015)) for a in angs]
        for i in range(len(angs) - 1):
            q = [grid[row][i], grid[row][i + 1], inner[i + 1], inner[i]]
            b._face(q if sgn > 0 else list(reversed(q)), mf)

    # ---- pine needles: 4-sided tapered prisms, base (brown) at the bottom, tip up, crossed at the twine.
    # Each needle follows the drum a little (pine needles bend): it lies on the wall at its lower end and under the
    # twine, and its upper part drifts outward so the tip stands proud in front of the button rim.
    ZX = 0.052                      # crossing / twine height
    NEEDLES = [((-0.029, 0.009), (0.047, 0.127)), ((0.033, 0.008), (-0.049, 0.125))]
    SS = [0.0, 0.16, None, 0.70, 0.87, 1.0]
    RAD = [0.0042, 0.0045, 0.0040, 0.0031, 0.0020, 0.0005]

    def surf_y(x, z):
        if z <= 0.0145:
            return min(wall_front_y(x, z, BASE), wall_front_y(x, 0.0145, BASE))
        if z <= 0.111:
            return wall_front_y(x, z) if z > 0.02 else min(wall_front_y(x, z), wall_front_y(x, 0.0145, BASE))
        return wall_front_y(x, min(z, 0.1325), RIM[:5])

    needle_data = []
    for k, ((x0, z0), (x1, z1)) in enumerate(NEEDLES):
        lift = 0.0034 * k            # the second needle lies over the first at the crossing
        sc = (ZX - z0) / (z1 - z0)
        path = []
        for s_, r_ in zip(SS, RAD):
            s_ = sc if s_ is None else s_
            x, z = x0 + (x1 - x0) * s_, z0 + (z1 - z0) * s_
            extra = lift * min(1.0, s_ / sc) * (1.0 if s_ <= sc else max(0.4, 1 - (s_ - sc)))
            extra += 0.0045 * max(0.0, (s_ - sc) / (1 - sc)) ** 1.3
            # clearance against the surface a little around the point (the prism's corners)
            ys_ = min(surf_y(x + dx, z + dz) for dx in (-0.004, 0, 0.004) for dz in (-0.004, 0, 0.004))
            y = ys_ - r_ * 1.05 - extra
            if s_ > sc and path:
                y = min(y, path[-1].y - 0.0006)      # above the crossing: only ever further out (no kink back)
            path.append(Vector((x, y, z)))
        print('needle', k, [tuple(round(c, 4) for c in p) for p in path])
        b.tube('needle', [tuple(p) for p in path], radii=RAD, sides=4)
        needle_data.append((path[0], path[-1], path[2]))
    # ---- twine: three closed 4-sided loops around the crossing, back half sunk into the wall
    ymid = sum(pc.y for _, _, pc in needle_data) / 2
    xm = sum(pc.x for _, _, pc in needle_data) / 2
    for k, dz in enumerate((-0.0046, 0.0046)):
        hw = 0.0225 - 0.0008 * k
        pts = []
        for i in range(8):
            a = 2 * math.pi * i / 8 + 0.2
            pts.append((xm + math.cos(a) * hw, ymid + math.sin(a) * 0.0092 - 0.0008, ZX + dz + 0.0008 * math.sin(a + k)))
        b.tube('twine', pts, radius=0.0042, sides=4, closed=True)
    b.needles = needle_data
    return b


clear_scene()
b = build()
obj = b.finish(center_xy=False)
print('fit', check_fit(obj))


def unwrap(o, shrink=0.3, margin=0.012):
    """Kit unwrap, then the bottom face (never seen on the shelf) is shrunk and everything repacked, so the visible
    surfaces get the texels."""
    BK.unwrap(o, margin=margin)
    me = o.data
    bi = list(o['parts']).index('bottom')
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

cam_path = os.path.join(HERE, 'cameras.json')
if os.path.exists(cam_path):
    cams = json.load(open(cam_path))
else:
    cams = B4.default_cams({'az': 0, 'el': 22, 'dist': 1.6, 'fov': 9.5, 'target': [0, 0, 0.065], 'shift': [0, 0], 'roll': 0})
views = B4.four_views(PREFIX, cams)
if '--opt' in sys.argv:
    keys = ('dist', 'shift') + (('el',) if '--el' in sys.argv else ()) + (('az',) if '--az' in sys.argv else ())
    print('opt', B4.optimize_views(obj, views, keys=keys))
    json.dump({v['name']: v['cam'] for v in views}, open(cam_path, 'w'), indent=1)
iou = B4.align_all(obj, views, os.path.join(OUT, 'align.png'))
print('IoU', iou)
if '--align-only' in sys.argv:
    sys.exit()

# ---------------------------------------------------------------- texture
def _shift_fill(bgr, m, ok, dxs):
    """Fill masked pixels with pixels of the same row shifted by the first dx (in order) that lands on usable
    pixels: keeps the horizontal birch-bark streaks instead of an inpaint smear."""
    out = bgr.copy()
    H, W = m.shape
    ys, xs = np.nonzero(m)
    cx = np.nonzero(ok)[1].mean()
    for dx in dxs:
        if not len(ys):
            break
        sx = xs + np.where(xs > cx, -abs(dx), abs(dx)) * np.sign(dx)
        good = (sx >= 0) & (sx < W)
        good[good] &= ok[ys[good], sx[good]]
        out[ys[good], xs[good]] = bgr[ys[good], sx[good]]
        ys, xs = ys[~good], xs[~good]
    if len(ys):
        mm = np.zeros(m.shape, np.uint8); mm[ys, xs] = 255
        out = cv2.inpaint(out, mm, 5, cv2.INPAINT_TELEA)
    return out


def clean_concepts(obj, views):
    """Concept copies for the bake:
    - the pine needles and the twine are replaced by bark shifted in from the same rows (they are painted onto
      their own geometry procedurally; their pixels and shadows must not land on the bark behind them);
    - the four button holes are inpainted with the button's own colour (the real hole dimples are painted dark
      procedurally, so the concept's holes never draw ghost holes on the plate when views disagree a little)."""
    lines = {'front': [((215, 285), (385, 515)), ((413, 275), (265, 512))],
             'left': [((464, 448), (528, 158)), ((510, 437), (462, 212))]}
    boxes = {'front': [(262, 398, 372, 452)], 'left': [(460, 316, 524, 362)]}
    dxs = {'front': [80, 110, 140, 55, -80, -110], 'left': [70, 95, 120, 150, 180]}
    P, UV, part, N = BK.mesh_arrays(obj)
    pl = list(obj['parts']).index('plate')
    out = {}
    for v in views:
        name = v['name']
        im = cv2.imread(v['concept'], cv2.IMREAD_UNCHANGED)
        bgr, a = im[:, :, :3].copy(), im[:, :, 3]
        inside = a > 128
        _, ind = ndimage.distance_transform_edt(~inside, return_indices=True)
        bgr = bgr[ind[0], ind[1]]
        H, W = a.shape
        # holes: dark blobs inside the projected plate
        idb, _ = BK.rasterize(P, part, v['cam'], W, H)
        plate = (idb >= 0) & (part[np.maximum(idb, 0)] == pl)
        plate = ndimage.binary_dilation(plate, iterations=6)
        lum = bgr.astype(np.float32) @ np.array([0.11, 0.59, 0.3], np.float32)
        ref = np.median(lum[plate & inside])
        hm = plate & (lum < ref - 28)
        hm = ndimage.binary_opening(hm, iterations=1)
        hm = ndimage.binary_dilation(hm, iterations=4)
        bgr = cv2.inpaint(bgr, hm.astype(np.uint8) * 255, 9, cv2.INPAINT_TELEA)
        if name in lines:
            m = np.zeros(a.shape, np.uint8)
            for p, q in lines[name]:
                cv2.line(m, p, q, 255, 29)
            for x0, y0, x1, y1 in boxes[name]:
                cv2.rectangle(m, (x0, y0), (x1, y1), 255, -1)
            m = m > 0
            filled = _shift_fill(bgr, m, ndimage.binary_erosion(inside, iterations=14) & ~m, dxs[name])
            # feathered edge: no hard seam where the fill meets the untouched concept
            w = cv2.GaussianBlur(m.astype(np.float32), (0, 0), 2.5)[..., None]
            w = np.clip(w * 1.6, 0, 1)
            bgr = (filled.astype(np.float32) * w + bgr.astype(np.float32) * (1 - w)).astype(np.uint8)
        p = os.path.join(HERE, f'concept_clean_{name}.png')
        cv2.imwrite(p, np.dstack([bgr, a]))
        out[name] = p
    return out


def concept_palette(view, box, k=4):
    im = cv2.cvtColor(cv2.imread(f'{PREFIX}_{view}.png'), cv2.COLOR_BGR2RGB)
    y0, y1, x0, x1 = box
    pal, w = BK._kmeans_palette(im[y0:y1, x0:x1].reshape(-1, 3).astype(np.float32), k)
    return [tuple(map(float, c)) for c in pal]


def paint(o, tex, size, needles):
    """Procedural touches at texel scale, colours from the concept:
    needles olive with a brown base (gradient), lighter tips; twine tan with strand stripes and dark grooves;
    hole dimples dark brown, the rim's inner step a bit shaded."""
    ys, xs, X, Nt, pt = B4.texel_points(o, size)
    parts = list(o['parts'])
    t = tex.astype(np.float32)
    rng = np.random.default_rng(7)
    blk = np.kron(rng.random((size // 2 + 2, size // 2 + 2)), np.ones((2, 2)))[:size, :size]
    n_i, tw_i, h_i = parts.index('needle'), parts.index('twine'), parts.index('hole')
    # needles
    sel = pt == n_i
    Xs = X[sel]
    best_s = np.zeros(len(Xs)); best_d = np.full(len(Xs), 9.0)
    for p0, p1, _ in needles:
        p0 = np.array(p0[:]); p1 = np.array(p1[:]); d = p1 - p0
        s_ = np.clip(((Xs - p0) @ d) / (d @ d), 0, 1)
        dist = np.linalg.norm(Xs - (p0 + s_[:, None] * d), axis=1)
        upd = dist < best_d
        best_s[upd], best_d[upd] = s_[upd], dist[upd]
    olive = np.array([[92, 80, 36], [116, 100, 46], [136, 118, 56], [154, 134, 66]], np.float32)
    brown = np.array([[96, 56, 31], [118, 70, 40], [140, 86, 50]], np.float32)
    nb = blk[ys[sel], xs[sel]]
    c_ol = olive[np.clip((0.75 + nb * 0.6 + best_s * 1.9).astype(int), 0, 3)]
    c_br = brown[np.clip((nb * 2.99).astype(int), 0, 2)]
    w = np.clip((best_s - 0.15) / 0.10, 0, 1)[:, None]
    t[ys[sel], xs[sel]] = c_br * (1 - w) + c_ol * w
    # twine
    sel = pt == tw_i
    tw = np.array([[160, 118, 80], [184, 140, 96], [204, 160, 114], [220, 178, 130]], np.float32)
    nz = Nt[sel, 2]
    ang = np.arctan2(X[sel, 1] - X[sel, 1].mean(), X[sel, 0] - X[sel, 0].mean())
    stripe = (np.floor(ang * 7 + X[sel, 2] * 900) % 2)
    k = 2 - stripe + (blk[ys[sel], xs[sel]] > 0.6)
    k = np.where(nz < -0.3, k - 1, k)
    t[ys[sel], xs[sel]] = tw[np.clip(k.astype(int), 0, 3)]
    # holes: dark brown dimples (floor slightly lighter than the walls)
    sel = pt == h_i
    hole = np.array([[70, 44, 28], [86, 56, 36], [104, 70, 48]], np.float32)
    k = (blk[ys[sel], xs[sel]] * 1.99).astype(int) + (Nt[sel, 2] > 0.7)
    t[ys[sel], xs[sel]] = hole[np.clip(k, 0, 2)]
    # base ring: bark-coloured specks (part border mismatch between concept and model) -> base colours
    bp = np.array([[86, 52, 34], [101, 65, 44], [116, 76, 52]], np.float32)
    sel = np.nonzero(pt == parts.index('base'))[0]
    lum_b = t[ys[sel], xs[sel]] @ np.array([0.3, 0.59, 0.11], np.float32)
    sel = sel[lum_b > 105]
    t[ys[sel], xs[sel]] = bp[np.clip((blk[ys[sel], xs[sel]] * 2.99).astype(int), 0, 2)]
    # texels on the UV edge the base band shares with the bark: point sampling on the base side reads them, so
    # light (bark) ones there become base colours too (no light dotted line along the top of the base ring)
    P_, UV_, part_, _ = BK.mesh_arrays(o)
    fm = np.zeros((size, size), np.uint8)
    for tt in np.nonzero(part_ == parts.index('base'))[0]:
        q = UV_[tt] * size; q[:, 1] = size - q[:, 1]
        cv2.fillPoly(fm, [np.round(q * 16).astype(np.int32)], 255, lineType=cv2.LINE_8, shift=4)
    fm = cv2.dilate(fm, np.ones((3, 3), np.uint8)) > 0
    L = t @ np.array([0.3, 0.59, 0.11], np.float32)
    fy, fx = np.nonzero(fm & (L > 105))
    t[fy, fx] = bp[np.clip((blk[fy, fx] * 2.99).astype(int), 0, 2)]
    # base chamfer: blocky noise from the concept's base-ring colours
    sel = pt == parts.index('basech')
    bp = np.array([[86, 52, 34], [101, 65, 44], [116, 76, 52]], np.float32)
    t[ys[sel], xs[sel]] = bp[np.clip((blk[ys[sel], xs[sel]] * 2.99).astype(int), 0, 2)]
    valid = np.zeros((size, size), bool); valid[ys, xs] = True
    valid |= fm
    _, inds = ndimage.distance_transform_edt(~valid, return_indices=True)
    t = t[inds[0], inds[1]]
    return np.clip(t, 0, 255).astype(np.uint8)


size = BK.texture_size_for(obj)
tex_path = os.path.join(OUT, f'T_{ID}.png')
clean = clean_concepts(obj, views)
# the concepts are lit paintings; the model is lit again in game: a mild gain keeps the drum as light as drawn
bviews = [dict(v, concept=clean[v['name']], gain=[1.07, 1.07, 1.07]) for v in views]
part_colors = {'bottom': concept_palette('front', (505, 525, 200, 440), 3),
               'hole': [(70, 44, 28), (86, 56, 36), (104, 70, 48)],
               'needle': [(112, 100, 44)], 'twine': [(156, 112, 74)], 'basech': [(101, 65, 44)]}
tex, st = B4.bake_multi(obj, bviews, size=size, part_colors=part_colors, erode_px=4, min_facing=0.25, quantize=0,
                         debug_png=os.path.join(OUT, 'coverage.png'))
# despeckle: thin dark lines/dots (concept outline sampled at grazing angles, part seams) -> local median
img = cv2.cvtColor(tex, cv2.COLOR_RGB2BGR)
med = cv2.medianBlur(img, 5)
lum = lambda a: a.astype(np.float32) @ np.array([0.11, 0.59, 0.3], np.float32)
dark = (lum(med) - lum(img)) > 22
img[dark] = med[dark]
# palette reduction of the projected colours first, then the procedural parts (their own small palettes, so the
# olive of the needles never merges with the charcoal bark marks)
tex = BK._quantize(cv2.cvtColor(img, cv2.COLOR_BGR2RGB), 40)
tex = paint(obj, tex, size, b.needles)
img = cv2.cvtColor(tex, cv2.COLOR_RGB2BGR)
cv2.imwrite(tex_path, img)
print('bake', json.dumps(st), 'despeckled', int(dark.sum()))
R4.apply_texture(obj, tex_path, ID)
stats = check_fit(obj)
stats['texture'] = f'{size}x{size}'
stats['iou'] = iou
R4.preview4(obj, views, os.path.join(OUT, 'preview.png'))
if '--preview-only' in sys.argv:
    sys.exit()
R.shelf_fit_render(obj, os.path.join(OUT, 'shelf.png'))
R.export(obj, OUT, ID, tex_path)
json.dump({'id': ID, 'stats': stats, 'bake': st, 'cameras': {v['name']: v['cam'] for v in views}},
          open(os.path.join(OUT, 'stats.json'), 'w'), indent=1)
R4.card4(OUT, ID, stats)
print('done', stats)
