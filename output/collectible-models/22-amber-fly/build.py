"""22 Amber with a fly on a clay stand — collectible shelf item (four-view concept).
Run: python3 build.py [--opt] [--align-only] [--preview-only]

Masses: an upright faceted honey-ochre amber chunk = convex hull of hand-placed points (big flat front table with the
fly, big flat back table with the fly, bevel facets between them and a crooked ridge ring, top peak to the right
(+X, as all four drawings show) and to the back); its foot sits sunk in a low 9-sided oval terracotta saucer with a real
recessed cradle (sloped outer wall, flat rim, inner wall, floor) and one chip notched into the rim at the front-left.
Painted: amber mottling and the fly (projected from the front / back views onto the two tables), the clay mottling,
the lighter broken clay inside the chip.
"""
import sys, os, json, math
sys.path.insert(0, '/home/claude/kit')
import numpy as np
import cv2
import bpy, bmesh
from mathutils import Vector
from geo import Builder, clear_scene, check_fit
import bake as BK
import bake4 as B4
import render as R
import render4 as R4

HERE = os.path.dirname(os.path.abspath(__file__))
PREFIX = '/home/claude/concepts/22-amber-fly'
ID = 'CI22_AmberFly'
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)
d2r = math.radians

# ---------------------------------------------------------------- saucer (metres)
RIM_Z = 0.028          # outer top edge
RIM_IN_Z = 0.029       # inner top edge (flat rim)
FLOOR_Z = 0.012
OVAL = 0.84            # saucer depth / width
SAUCER = [  # (rx, z) per ring, bottom -> rim -> cradle floor edge
    (0.093, 0.000), (0.100, RIM_Z), (0.082, RIM_IN_Z), (0.082, FLOOR_Z)]
CHIP_C = 184.0         # chip centre, degrees (180 = -X, 270 = front -Y): front-left
CHIP_DROP = {-13: 0.0025, -7: 0.0125, 3: 0.0155, 12: 0.0045}

# ---------------------------------------------------------------- amber = intersection of flat facet planes
# The chunk is a convex polyhedron cut by planes, so every facet is one broad flat face:
# - a big front table and a big back table (the fly is painted on each),
# - for every edge of the front-view outline (picture -> metres, x right, z up) one front bevel and one back bevel,
#   meeting in a crooked ridge (y_ridge, leaning back toward the top) that forms the silhouette from the front,
# - a hidden bottom inside the saucer cradle.
OUTLINE = [(0.020, 0.205), (0.050, 0.197), (0.083, 0.127), (0.073, 0.055), (0.058, 0.014), (-0.060, 0.014),
           (-0.079, 0.047), (-0.088, 0.083), (-0.079, 0.127), (-0.041, 0.181)]      # clockwise from the peak
# bevel angles (front, back) per outline edge i -> i+1, degrees: larger = wider bevel seen from the front/back
BEVEL = [(14, 14), (28, 36), (24, 28), (12, 12), (14, 14), (12, 12), (24, 22), (26, 30), (30, 38), (14, 14)]
TWIST = [4, -3, 5, 0, 0, 0, -4, 3, -5, 2]       # small azimuth twist per edge (deg): front/back bevels not mirrored
Y_F0, S_F = -0.058, 0.07                         # front table: y = Y_F0 + S_F (z - 0.1)
Y_B0, S_B = 0.046, -0.10                         # back table:  y = Y_B0 + S_B (z - 0.1)
Z_BOT = 0.014
TOP_F = (-0.046, 0.165, 1.17)                    # front-top chamfer through (y, z), dz/dy
TOP_B = (0.041, 0.150, -0.36)                    # back-top chamfer: y = y0 + k (z - z0)


def y_ridge(z):
    return -0.006 + 0.12 * max(0.0, z - 0.13)


def amber_planes():
    """List of (n, d): n.p <= d."""
    P = []
    def add(n, d):
        n = np.asarray(n, float); l = np.linalg.norm(n)
        P.append((n / l, d / l))
    add((0, -1, S_F), -(Y_F0 - 0.1 * S_F))                   # front table
    add((0, 1, -S_B), Y_B0 - 0.1 * S_B)                      # back table
    add((0, 0, -1), -Z_BOT)                                  # bottom
    # top: the peak is a short ridge running front-back, cut by a front-top chamfer (falls to the front table) and a
    # back-top chamfer (the back table bends back more above z 0.13), as the side views show
    add((0, -1, 1 / TOP_F[2]), -TOP_F[0] + TOP_F[1] / TOP_F[2])
    add((0, 1, -TOP_B[2]), TOP_B[0] - TOP_B[2] * TOP_B[1])
    n = len(OUTLINE)
    for i in range(n):
        A, B = np.array(OUTLINE[i]), np.array(OUTLINE[(i + 1) % n])
        d = B - A
        m = np.array([-d[1], d[0]]); m /= np.linalg.norm(m)
        c = float(m @ A)
        yr = y_ridge(0.5 * (A[1] + B[1]))
        for side, beta in ((-1, BEVEL[i][0]), (1, BEVEL[i][1])):
            be = math.radians(beta)
            tw = math.radians(TWIST[i] * side)
            nn = np.array([m[0] * math.cos(be), side * math.sin(be), m[1] * math.cos(be)])
            # twist: rotate the normal a little about Z (irregular rock, front and back bevels not mirrored)
            nn = np.array([nn[0] * math.cos(tw) - nn[1] * math.sin(tw), nn[0] * math.sin(tw) + nn[1] * math.cos(tw), nn[2]])
            # plane through the outline edge midpoint on the ridge
            mid = np.array([0.5 * (A[0] + B[0]), yr, 0.5 * (A[1] + B[1])])
            add(nn, float(nn @ mid))
    return P


def hull(pts):
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in pts]
    res = bmesh.ops.convex_hull(bm, input=vs)
    bmesh.ops.delete(bm, geom=res['geom_interior'] + res['geom_unused'], context='VERTS')
    bmesh.ops.dissolve_limit(bm, angle_limit=d2r(0.5), verts=bm.verts, edges=bm.edges)
    bm.verts.index_update()
    V = [v.co.copy() for v in bm.verts]
    F = [[v.index for v in f.verts] for f in bm.faces]
    bm.free()
    return V, F


def amber_mesh():
    from scipy.spatial import HalfspaceIntersection
    P = amber_planes()
    hs = np.array([np.r_[n, -d] for n, d in P])
    hi = HalfspaceIntersection(hs, np.array([0.0, 0.0, 0.1]))
    pts = hi.intersections
    uniq = []
    for p in pts:
        if all(np.linalg.norm(p - q) > 2e-4 for q in uniq):
            uniq.append(p)
    return hull([tuple(p) for p in uniq])


def saucer_angles():
    base = [i * 40.0 for i in range(9)]
    chip = [CHIP_C + k for k in CHIP_DROP]
    base = [a for a in base if min(abs((a - c + 180) % 360 - 180) for c in chip) > 3.5]
    return sorted([a % 360 for a in base + chip])


def build():
    b = Builder(ID, seed=22)
    # --- amber
    V, F = amber_mesh()
    b.add_bmesh_geometry('amber', V, F)
    # --- saucer
    mi = b.part('clay')
    angs = saucer_angles()
    rings = []
    for ri, (r, z) in enumerate(SAUCER):
        ring = []
        for a in angs:
            jit = 1 + 0.012 * math.sin(a * 0.37 + ri)
            x = math.cos(d2r(a)) * r * jit
            y = math.sin(d2r(a)) * r * (OVAL + {0: 0.04, 1: 0.0, 2: 0.11, 3: 0.11}[ri]) * jit
            zz = z
            for k, drop in CHIP_DROP.items():
                if abs(a - (CHIP_C + k)) < 1e-6 and ri in (1, 2):
                    zz = z - drop
                    if ri == 1 and abs(k) < 7:      # broken outer edge also bites a little into the wall
                        x *= 0.985; y *= 0.985
            ring.append(b.bm.verts.new((x, y, zz)))
        rings.append(ring)
    for r0, r1 in zip(rings, rings[1:]):
        b.bridge(r0, r1, mi)
    b.cap(rings[-1], mi)                                   # cradle floor
    b.cap(rings[0], b.part('base'), flip=True)             # bottom (never seen)
    return b


def check_cradle(obj, margin=0.0):
    """Amber vertices below the rim must lie inside the cradle: inside the inner-rim ring and the floor ring (both
    convex, CCW) by at least `margin`. Returns the offending vertices."""
    me = obj.data
    parts = list(obj['parts'])
    av = {v for p in me.polygons if p.material_index == parts.index('amber') for v in p.vertices}
    cv = {v for p in me.polygons if p.material_index == parts.index('clay') for v in p.vertices}
    n = len(saucer_angles())
    ids = sorted(cv)                       # saucer verts were created ring by ring after the amber
    rings = [np.array([me.vertices[i].co[:] for i in ids[k * n:(k + 1) * n]]) for k in range(len(SAUCER))]
    bad = []
    for ring in (rings[2], rings[3]):
        R2 = ring[:, :2]
        for i in av:
            p = np.array(me.vertices[i].co[:])
            if p[2] > RIM_IN_Z + 0.001:
                continue
            for k in range(n):
                e = R2[(k + 1) % n] - R2[k]
                cr = (e[0] * (p[1] - R2[k][1]) - e[1] * (p[0] - R2[k][0])) / np.linalg.norm(e)
                if cr < margin:
                    bad.append(tuple(float(c) for c in np.round(p, 3))); break
    return sorted(set(bad))


def finish_obj():
    b = build()
    obj = b.finish(center_xy=False)
    return obj


def unwrap(o, shrink=0.3, margin=0.012):
    """Kit smart-project unwrap; the saucer bottom (never seen) is shrunk and everything repacked."""
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


def paint_fixes(o, tex, size, chip_pal):
    """Painted touches after the projection: the lighter broken clay inside the rim chip (a V-shaped bite into the
    rim and the outer wall, blocky texel noise from the concept's chip colours)."""
    from scipy import ndimage
    ys, xs, X, Nt, pt = B4.texel_points(o, size)
    parts = list(o['parts'])
    t = tex.astype(np.float32)
    clay, amber = parts.index('clay'), parts.index('amber')
    rng = np.random.default_rng(22)
    # concept colours that landed on the wrong material (the drawn amber/clay border differs a little from the
    # model's): amber-yellow texels on the clay, clay-red texels on the amber -> nearest good texel of the same part
    hsv = cv2.cvtColor(np.clip(t, 0, 255).astype(np.uint8)[None, :, :][0], cv2.COLOR_RGB2HSV)
    H, S = hsv[ys, xs, 0], hsv[ys, xs, 1]
    bad = ((pt == clay) & (H >= 12.5) & (S >= 120)) | ((pt == amber) & (H <= 10))
    for k in (clay, amber):
        good = np.zeros((size, size), bool)
        sel = (pt == k) & ~bad
        good[ys[sel], xs[sel]] = True
        _, inds = ndimage.distance_transform_edt(~good, return_indices=True)
        m = (pt == k) & bad
        t[ys[m], xs[m]] = t[inds[0][ys[m], xs[m]], inds[1][ys[m], xs[m]]]
    print('wrong-material texels fixed', int(bad.sum()))
    # the render light is dimmer than the painted concept: lift the albedo a little (hue kept)
    t[ys[pt == amber], xs[pt == amber]] *= 1.14
    t[ys[pt == clay], xs[pt == clay]] *= 1.14
    # cradle inner wall / floor: the concept's darkest gap colours -> a lighter shadowed clay (it is in shadow anyway)
    rad = np.stack([X[:, 0], X[:, 1] / OVAL], 1)
    rad /= np.maximum(np.linalg.norm(rad, axis=1, keepdims=True), 1e-9)
    inner = (pt == clay) & (X[:, 2] > FLOOR_Z - 1e-4) & (X[:, 2] < RIM_IN_Z - 0.001) & \
            ((np.einsum('ij,ij->i', Nt[:, :2], rad) < -0.5) | (Nt[:, 2] > 0.9))
    t[ys[inner], xs[inner]] = np.maximum(t[ys[inner], xs[inner]], 0) * 1.12 + 6
    blk = np.kron(rng.random((size // 3 + 2, size // 3 + 2)), np.ones((3, 3)))[:size, :size]
    ang = np.degrees(np.arctan2(X[:, 1] / OVAL, X[:, 0])) % 360
    da = np.abs((ang - CHIP_C + 180) % 360 - 180)
    r = np.hypot(X[:, 0], X[:, 1] / OVAL)
    edge = blk[ys, xs] * 0.004
    chip = (pt == clay) & (da < 14.5) & (X[:, 2] > RIM_Z - 0.0175 + 0.0009 * da - edge) & (r > 0.074)
    pal = np.array(sorted(chip_pal, key=lambda c: sum(c)), np.float32) * 1.08
    idx = np.clip((blk[ys[chip], xs[chip]] * len(pal)).astype(int), 0, len(pal) - 1)
    # deeper inside the break a touch darker (shadowed broken surface)
    shade = 1.0 - 0.10 * np.clip((RIM_Z - X[chip, 2]) / 0.012, 0, 1)
    t[ys[chip], xs[chip]] = pal[idx] * shade[:, None]
    print('chip texels', int(chip.sum()))
    # lower side bevels of the amber (just above the rim, tilted down): the side concepts put dark blots there
    # (rim shadow / outline); lift them to the amber's lower-quartile brightness
    lowb = (pt == amber) & (X[:, 2] < 0.066) & (Nt[:, 2] < 0.05) & (np.abs(Nt[:, 1]) < 0.9)
    L = t[ys, xs] @ np.array([0.3, 0.59, 0.11], np.float32)
    floor_l = np.percentile(L[pt == amber], 25)
    k = lowb & (L < floor_l)
    t[ys[k], xs[k]] *= (floor_l / np.maximum(L[k], 1))[:, None] ** 0.85
    print('lifted lower-bevel texels', int(k.sum()))
    valid = np.zeros((size, size), bool)
    valid[ys, xs] = True
    _, inds = ndimage.distance_transform_edt(~valid, return_indices=True)
    t = t[inds[0], inds[1]]
    return np.clip(t, 0, 255).astype(np.uint8)


if __name__ == '__main__':
    clear_scene()
    obj = finish_obj()
    print('fit', check_fit(obj))
    print('cradle collisions', check_cradle(obj))
    unwrap(obj)

    cam_path = os.path.join(HERE, 'cameras.json')
    if os.path.exists(cam_path):
        cams = json.load(open(cam_path))
    else:
        cams = B4.default_cams({'az': 0, 'el': 12, 'dist': 1.6, 'fov': 9.5, 'target': [0, 0, 0.1], 'shift': [0, 0], 'roll': 0})
    views = B4.four_views(PREFIX, cams)
    if '--opt' in sys.argv:
        keys = ('dist', 'shift') + (('az',) if '--az' in sys.argv else ())
        print('opt', B4.optimize_views(obj, views, keys=keys))
        json.dump({v['name']: v['cam'] for v in views}, open(cam_path, 'w'), indent=1)
    if '--rows' in sys.argv:
        sys.path.insert(0, HERE)
        import rowdiff4; rowdiff4.report(obj, views)
    iou = B4.align_all(obj, views, os.path.join(OUT, 'align.png'))
    print('IoU', iou)
    print('cams', {v['name']: (round(v['cam']['az'], 1), round(v['cam']['dist'], 3), v['cam']['shift']) for v in views})
    if '--align-only' in sys.argv:
        sys.exit()

    # ------------------------------------------------------------ texture
    size = BK.texture_size_for(obj)
    tex_path = os.path.join(OUT, f'T_{ID}.png')
    sys.path.insert(0, HERE)
    import clean_side
    # side views without the fly: the fly is painted once on the front table and once on the back table
    nofly = clean_side.clean_all(PREFIX, HERE)
    bviews = [dict(v) for v in views]
    for v in bviews:
        if v['name'] in nofly:
            v['concept'] = nofly[v['name']]

    def concept_palette(view, box, k=4, light=None):
        im = cv2.cvtColor(cv2.imread(f'{PREFIX}_{view}.png'), cv2.COLOR_BGR2RGB)
        y0, y1, x0, x1 = box
        px = im[y0:y1, x0:x1].reshape(-1, 3).astype(np.float32)
        if light is not None:
            lum = px @ np.array([0.3, 0.59, 0.11], np.float32)
            px = px[lum > np.percentile(lum, light)]
        pal, w = BK._kmeans_palette(px, k)
        return [tuple(map(float, c)) for c in pal]

    part_colors = {'base': concept_palette('front', (500, 540, 200, 420), 4)}
    tex, st = B4.bake_multi(obj, bviews, size=size, debug_png=os.path.join(OUT, 'coverage.png'),
                            part_colors=part_colors, erode_px=4, min_facing=0.25, quantize=0)
    tex = paint_fixes(obj, tex, size, concept_palette('front', (440, 510, 140, 215), 3, light=88) +
                      concept_palette('left', (440, 510, 235, 310), 3, light=88))
    tex = BK._quantize(tex, 40)
    img = cv2.cvtColor(tex, cv2.COLOR_RGB2BGR)
    # despeckle: thin dark lines/dots (concept silhouette outline sampled at grazing angles) -> local median
    med = cv2.medianBlur(img, 5)
    lum = lambda a: a.astype(np.float32) @ np.array([0.11, 0.59, 0.3], np.float32)
    dark = (lum(med) - lum(img)) > 18
    # ...but never on the two fly tables (legs and antennae are thin dark lines too)
    tys, txs, _, tN, tpart = B4.texel_points(obj, size)
    table = np.zeros((size, size), np.uint8)
    table[tys, txs] = (np.abs(tN[:, 1]) > 0.93).astype(np.uint8)
    table = cv2.dilate(table, np.ones((5, 5), np.uint8)) > 0
    dark &= ~table
    # larger dark blots on the amber away from the tables (stray concept marks, e.g. fly remnants at a facet border)
    amb = np.zeros((size, size), bool)
    amb[tys, txs] = tpart == list(obj['parts']).index('amber')
    med9 = cv2.medianBlur(img, 9)
    low = np.zeros((size, size), bool)
    low[tys, txs] = (_[:, 2] < 0.066) | (_[:, 2] > 0.150)      # table rims outside the fly's extent
    blot = ((lum(med9) - lum(img)) > 22) & amb & (~table | low)
    dark |= blot
    med = np.where(blot[..., None], med9, med)
    img[dark] = med[dark]
    cv2.imwrite(tex_path, img)
    print('bake', json.dumps(st), 'despeckled', int(dark.sum()))
    if '--tex-only' in sys.argv:
        sys.exit()
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
