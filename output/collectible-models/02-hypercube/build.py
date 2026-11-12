"""02 Hypercube (tesseract) — collectible shelf item.
Run: python3 build.py [--opt] [--align-only] [--dbg]

Real frame of square stone bars: outer cube (12 bars + 8 chunky corner blocks), inner cube (12 bars + 8 blocks)
and the 8 diagonal connector bars, open holes between them. Slightly crooked, stands on one face.
Glowing cracks, stone pixel texture and chips are painted from the concept projection.
"""
import sys, os, json, math
sys.path.insert(0, '/home/claude/kit')
import numpy as np
import bpy
from mathutils import Vector, Matrix
from geo import Builder, clear_scene, check_fit
import bake as BK
import render as R

HERE = os.path.dirname(os.path.abspath(__file__))
CONCEPT_ORIG = '/home/claude/concepts/collectible-concepts/02-hypercube-v2.png'
# RGBA copy of the concept with a tight GrabCut mask in alpha (tools/mask.py): the kit's colour mask is fattened
# by the concept's glow halo and the blue-lit background seen through the frame.
CONCEPT = os.path.join(HERE, 'concept_rgba.png')
ID = 'CI02_Hypercube'
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)
os.makedirs(os.path.join(HERE, 'tmp'), exist_ok=True)

if not os.path.exists(CONCEPT):
    import subprocess
    subprocess.check_call([sys.executable, '-I', os.path.join(HERE, 'tools', 'mask.py'), CONCEPT_ORIG, CONCEPT,
                           os.path.join(HERE, 'tmp', 'maskvis.png')])

# ------------------------------------------------------------------ dimensions (metres)
E = 0.172        # outer cube edge (corner block centre to centre)
T = 0.0186       # outer bar thickness
BLK = 1.12       # outer corner block size relative to T
EI = 0.061       # inner cube edge
TI = 0.0130      # inner bar thickness (chunky like the concept; not fitted)
TD = 0.0135      # diagonal connector thickness
INNER_OFF = Vector((-0.008, -0.0035, 0.0015))   # inner cube offset from the outer centre
# The concept is painted with a strong horizontal 'vanishing' but almost no vertical one; tools/fitcam.py fits
# these crooked proportions to the concept corners: back face narrower in x, frame a bit taller than wide,
# back-right top corner raised, inner cube narrower at the back.
BX = 0.923       # outer back face x scale
BZ = 0.97        # outer back top (z scale of the upper half at the back)
ZS = 1.055       # the frame is a little taller than wide
RR = 0.011       # back-right top corner raised (crooked top face)
IBX = 0.78       # inner back face x scale
# per-corner nudges (dx, dz) in metres found by tools/fitgeo.py (silhouette IoU against the concept), clamped small
FIT_PATH = os.path.join(HERE, 'geo_fit.json')
KEYS = [(sx, sy, sz) for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]
AX = [Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))]
EDGES = []
for _k in KEYS:
    for _ax in range(3):
        if _k[_ax] < 0:
            _k2 = list(_k); _k2[_ax] = 1
            EDGES.append((_k, tuple(_k2), _ax))


def kstr(k):
    return ','.join(str(v) for v in k)


def load_fit():
    return json.load(open(FIT_PATH)) if os.path.exists(FIT_PATH) else {'o': {}, 'i': {}}


def make(fit=None):
    """Build the item mesh. Returns (obj, outer corners, inner corners) — corners before finish() recentring."""
    fit = fit or {'o': {}, 'i': {}}
    g = fit.get('g', {})
    T_, TI_, TD_, EI_, IBX_ = g.get('T', T), g.get('TI', TI), g.get('TD', TD), g.get('EI', EI), g.get('IBX', IBX)
    b = Builder(ID, seed=2)
    rng = b.rng

    def frame_axes(d, hint):
        d = d.normalized()
        h = Vector(hint)
        a = (h - d * h.dot(d)).normalized()
        return a, d.cross(a)

    def box_from_corners(part, vs):
        """vs: 8 corners, 0-3 one end ring, 4-7 the other (same order). Closed shell; finish() fixes winding."""
        faces = [(3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
        b.add_bmesh_geometry(part, [tuple(v) for v in vs], faces)

    def beam(part, p0, p1, w, hint, twist=0.0, taper=1.0):
        """Square-section bar from p0 to p1 with side w; taper scales the p1 end; twist rolls it (deg)."""
        p0, p1 = Vector(p0), Vector(p1)
        d = (p1 - p0).normalized()
        a, c = frame_axes(d, hint)
        rot = Matrix.Rotation(math.radians(twist), 3, d)
        a, c = rot @ a, rot @ c
        vs = []
        for P, s in ((p0, 1.0), (p1, taper)):
            for sa, sc in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                vs.append(P + a * (sa * w * s / 2) + c * (sc * w * s / 2))
        box_from_corners(part, vs)

    def block(part, center, size, rot_deg=(0, 0, 0), flat_bottom=None):
        """Crooked cube block (corner joint). flat_bottom=z keeps the bottom face at that height (it stands)."""
        c = Vector(center)
        M = (Matrix.Rotation(math.radians(rot_deg[2]), 3, 'Z') @ Matrix.Rotation(math.radians(rot_deg[1]), 3, 'Y') @
             Matrix.Rotation(math.radians(rot_deg[0]), 3, 'X'))
        sx, sy, sz = [s / 2 for s in size]
        vs = []
        for z in (-sz, sz):
            for x, y in ((-sx, -sy), (sx, -sy), (sx, sy), (-sx, sy)):
                v = c + M @ Vector((x, y, z))
                if flat_bottom is not None and z < 0:
                    v.z = flat_bottom
                vs.append(v)
        box_from_corners(part, vs)

    # ---------------- outer cube
    h = E / 2
    lo = T_ * BLK / 2                    # bottom block half height -> bottom faces at z=0
    zc = lo + h * ZS                     # cube centre height
    lean = Vector((0.003, -0.002, 0.0))  # top leans a little
    oc = {}
    for k in KEYS:
        sx, sy, sz = k
        p = Vector((sx * h * (BX if sy > 0 else 1.0), sy * h, zc + sz * h * ZS * (BZ if (sy > 0 and sz > 0) else 1.0)))
        if sz > 0:
            p += lean
        p += Vector((b.j(0.002), b.j(0.002), 0 if sz < 0 else b.j(0.002)))
        dx, dz = fit['o'].get(kstr(k), (0, 0))
        p += Vector((dx, 0, dz if sz > 0 else 0))
        oc[k] = p
    oc[(1, 1, 1)] += Vector((0.0, 0.0, RR))

    for k0, k1, ax in EDGES:
        w = T_ * rng.uniform(0.93, 1.06)
        beam('outer', oc[k0], oc[k1], w, AX[(ax + 1) % 3], twist=rng.uniform(-4, 4), taper=rng.uniform(0.94, 1.06))
    for k, p in oc.items():
        s = T_ * BLK
        size = (s * rng.uniform(0.97, 1.08), s * rng.uniform(0.97, 1.08), s * rng.uniform(0.97, 1.06))
        rot = (rng.uniform(-4, 4), rng.uniform(-4, 4), rng.uniform(-7, 7))
        if k[2] < 0:
            block('outer', p, size, rot_deg=(0, 0, rot[2]), flat_bottom=p.z - lo)
        else:
            block('outer', p, size, rot_deg=rot)

    # ---------------- stone chips: a few wedge flakes on outer bars where the concept shows them in silhouette
    CHIPS = [(((1, -1, -1), (1, -1, 1)), 0.42, (1, 0, 0)), (((-1, -1, -1), (-1, -1, 1)), 0.30, (-1, 0, 0)),
             (((-1, -1, -1), (-1, -1, 1)), 0.64, (-1, 0, 0)), (((1, 1, -1), (1, 1, 1)), 0.74, (1, 0, 0)),
             (((1, 1, -1), (1, 1, 1)), 0.36, (1, 0, 0)), (((-1, -1, 1), (1, -1, 1)), 0.10, (0, 0, 1)),
             (((-1, -1, -1), (1, -1, -1)), 0.22, (0, -1, 0)), (((-1, 1, 1), (1, 1, 1)), 0.55, (0, 0, 1)),
             (((-1, 1, -1), (-1, 1, 1)), 0.55, (-1, 0, 0)), (((1, -1, 1), (1, 1, 1)), 0.6, (0, 0, 1))]
    for (k0, k1), t, n in CHIPS:
        a, c = oc[k0], oc[k1]
        d = (c - a).normalized()
        n = Vector(n)
        n = (n - d * n.dot(d)).normalized()
        p = a.lerp(c, t) + n * (T_ * 0.5 - 0.0022)
        L = rng.uniform(0.014, 0.020)
        if rng.random() < 0.5:
            d = -d
        beam('outer', p - d * L * 0.5, p + d * L * 0.5, rng.uniform(0.0075, 0.009), n, twist=45 + rng.uniform(-10, 10),
             taper=rng.uniform(0.3, 0.45))

    # ---------------- inner cube
    hi = EI_ / 2
    cin = Vector((0, 0, zc)) + INNER_OFF
    ic = {}
    for k in KEYS:
        p = cin + Vector((k[0] * hi * (IBX_ if k[1] > 0 else 1.0), k[1] * hi, k[2] * hi))
        p += Vector((b.j(0.001), b.j(0.001), b.j(0.001)))
        dx, dz = fit['i'].get(kstr(k), (0, 0))
        ic[k] = p + Vector((dx, 0, dz))
    for k0, k1, ax in EDGES:
        beam('inner', ic[k0], ic[k1], TI_ * rng.uniform(0.94, 1.06), AX[(ax + 1) % 3], twist=rng.uniform(-4, 4))
    for k, p in ic.items():
        s = TI_ * 1.2
        block('inner', p, (s, s, s), rot_deg=(rng.uniform(-5, 5), rng.uniform(-5, 5), rng.uniform(-6, 6)))

    # ---------------- 8 diagonal connectors (inner corner -> outer corner), a little thicker at the outer end
    for k in KEYS:
        beam('diag', ic[k], oc[k], TD_ * rng.uniform(0.95, 1.06), (0, 0, 1), twist=rng.uniform(-5, 5), taper=1.1)

    obj = b.finish()
    return obj, oc, ic


def cube_views(obj):
    """Symmetry views of the (nearly) cubic frame for the bake: every face family can borrow the concept's
    front/top/right faces. Rotations about the frame centre; mirrors across its mid-planes."""
    zs = [v.co.z for v in obj.data.vertices]
    c = np.array([0.0, 0.0, (min(zs) + max(zs)) / 2])
    def about(Rm):
        T = np.eye(4); T[:3, :3] = Rm; T[:3, 3] = c - Rm @ c
        return T
    def rx(d):
        a = math.radians(d); return np.array([[1, 0, 0], [0, math.cos(a), -math.sin(a)], [0, math.sin(a), math.cos(a)]])
    def ry(d):
        a = math.radians(d); return np.array([[math.cos(a), 0, math.sin(a)], [0, 1, 0], [-math.sin(a), 0, math.cos(a)]])
    def rz(d):
        a = math.radians(d); return np.array([[math.cos(a), -math.sin(a), 0], [math.sin(a), math.cos(a), 0], [0, 0, 1]])
    views = [{'name': 'orig', 'T': np.eye(4), 'bonus': 0.3}]
    for n, Rm in (('rz90', rz(90)), ('rz-90', rz(-90)), ('rz180', rz(180)), ('rx90', rx(90)), ('rx-90', rx(-90)),
                  ('rx180', rx(180)), ('ry90', ry(90)), ('ry-90', ry(-90)), ('mirx', np.diag([-1.0, 1, 1])),
                  ('miry', np.diag([1.0, -1, 1])), ('mirz', np.diag([1.0, 1, -1]))):
        views.append({'name': n, 'T': about(Rm), 'bonus': 0.0})
    return views


# stone fill palette picked from the concept's lit stone (dark -> light), no crack cyan
STONE = [(56, 64, 100), (68, 78, 118), (82, 93, 138), (98, 110, 156), (116, 128, 170)]


def grade(tex, partmap=None, parts=()):
    """Post-bake paint pass. The concept's cracks are thin near-white cyan cores in a saturated blue glow; at
    640 texels/m the projection softens them. Push the cores to bright cyan-white, keep the halo saturated,
    lift the stone a little (the concept has its light baked in, the game light darkens it again), and give the
    inner cube the concept's stronger blue glow wash."""
    t = tex.astype(np.float32)
    r, g, b_ = t[..., 0], t[..., 1], t[..., 2]
    lum = 0.3 * r + 0.59 * g + 0.11 * b_
    glow = (b_ - r > 95) & (g - r > 35)
    # drop isolated glow specks (projection noise) so the cracks read as lines, not camo
    from scipy import ndimage
    lab, n = ndimage.label(glow, structure=np.ones((3, 3)))
    if n:
        area = ndimage.sum(glow, lab, range(1, n + 1))
        speck = np.isin(lab, np.nonzero(area < 7)[0] + 1)
        pal = np.array(STONE, np.float32)
        pl = pal @ np.array([0.3, 0.59, 0.11])
        idx = np.clip(np.searchsorted(pl, lum[speck] * 0.8), 0, len(pal) - 1)
        t[speck] = pal[idx]
        glow &= ~speck
        r, g, b_ = t[..., 0], t[..., 1], t[..., 2]
        lum = 0.3 * r + 0.59 * g + 0.11 * b_
    core = glow & (lum > 135)
    halo = glow & ~core
    t[halo] = t[halo] * 0.85 + np.array([40, 125, 225], np.float32) * 0.15
    t[core] = t[core] * 0.2 + np.array([185, 250, 255], np.float32) * 0.8
    rest = ~glow
    t[rest] = 255.0 * (t[rest] / 255.0) ** 0.78
    if partmap is not None and 'inner' in parts:
        inn = (partmap == parts.index('inner')) & ~core
        t[inn] = t[inn] * 0.72 + np.array([60, 135, 225], np.float32) * 0.28
    return np.clip(t, 0, 255)


def bake_per_face(obj, cam, views, size, tex_path, debug_path):
    """Bake every view separately, then give each polygon (bar side) ONE view: the one that covers most of it
    (the original concept view preferred). Keeps every bar face a coherent piece of the concept's paint instead of
    a per-texel patchwork of several symmetric copies. Uncovered texels get the stone palette."""
    import cv2
    from scipy import ndimage
    parts = list(obj['parts'])
    me = obj.data
    P, UV, part, N = BK.mesh_arrays(obj)
    poly = np.array([t.polygon_index for t in me.loop_triangles])
    tri_img = np.full((size, size), -1, np.int32)
    uvp = UV * size; uvp[..., 1] = size - uvp[..., 1]
    for t in range(len(P)):
        cv2.fillPoly(tri_img, [np.round(uvp[t] * 16).astype(np.int32)], int(t), lineType=cv2.LINE_8, shift=4)
    inside = tri_img >= 0
    tpoly = np.where(inside, poly[np.maximum(tri_img, 0)], -1)
    npoly = len(me.polygons)
    texs, covs = [], []
    tmp = os.path.join(HERE, 'tmp', '_cov.png')
    pc = {p: STONE for p in parts}
    for v in views:
        tex, st = BK.bake(obj, CONCEPT, cam, views=[v], size=size, debug_png=tmp, part_colors=pc, quantize=0,
                          texel_blur=False, min_facing=0.3)
        dbg = cv2.cvtColor(cv2.imread(tmp), cv2.COLOR_BGR2RGB)
        cov = inside & (dbg[:, :, 1] > 150) & (dbg[:, :, 0] < 120)
        texs.append(tex.astype(np.float32)); covs.append(cov)
    area = np.bincount(tpoly[inside], minlength=npoly).astype(np.float32)
    score = []
    for vi, (v, cov) in enumerate(zip(views, covs)):
        c = np.bincount(tpoly[cov], minlength=npoly) / np.maximum(area, 1)
        score.append(c * (1.25 if v['name'] == 'orig' else 1.0))
    score = np.array(score)
    choice = np.argmax(score, 0)
    best = score.max(0)
    out = np.zeros((size, size, 3), np.float32)
    have = np.zeros((size, size), bool)
    src = np.full((size, size), -1, np.int32)
    ys, xs = np.nonzero(inside)
    pv = tpoly[ys, xs]
    ch = choice[pv]
    for vi in range(len(views)):
        sel = ch == vi
        out[ys[sel], xs[sel]] = texs[vi][ys[sel], xs[sel]]
        src[ys[sel], xs[sel]] = np.where(covs[vi][ys[sel], xs[sel]], vi, -1)
    have[ys, xs] = True
    # faces nobody covers well: palette noise (already in every view's texture as fill) - nothing more to do
    if (~have).any():
        _, inds = ndimage.distance_transform_edt(~have, return_indices=True)
        out = out[inds[0], inds[1]]
    pm = np.where(inside, part[np.maximum(tri_img, 0)], -1)
    if (~inside).any():
        _, ii = ndimage.distance_transform_edt(~inside, return_indices=True)
        pm = pm[ii[0], ii[1]]
    out = grade(out, pm, parts)
    out = BK._quantize(np.clip(out, 0, 255).astype(np.uint8), 40)
    cv2.imwrite(tex_path, cv2.cvtColor(out, cv2.COLOR_RGB2BGR))
    pal = np.array([[60, 200, 80], [60, 120, 255], [255, 200, 40], [220, 80, 220], [80, 220, 220], [255, 120, 60],
                    [200, 200, 200], [120, 80, 40]])
    dbg = np.zeros((size, size, 3), np.uint8); dbg[inside] = (255, 40, 40)
    for vi in range(len(views)):
        dbg[src == vi] = pal[vi % len(pal)]
    cv2.imwrite(debug_path, cv2.cvtColor(dbg, cv2.COLOR_RGB2BGR))
    stats = {'texture_size': size, 'covered_from_concept': round(float((src >= 0).sum() / inside.sum()), 3),
             'faces_per_view': {v['name']: int((choice == vi).sum()) for vi, v in enumerate(views)}}
    return out, stats


if __name__ == '__main__':
    clear_scene()
    obj, oc, ic = make(load_fit())
    fit = check_fit(obj)
    print('fit', fit)

    BK.unwrap(obj)
    cam_path = os.path.join(HERE, 'camera.json')
    cam = json.load(open(cam_path))
    if '--opt' in sys.argv:
        cam, s = BK.optimize_camera(obj, CONCEPT, cam, keys=('az', 'dist', 'shift'))
        json.dump(cam, open(cam_path, 'w'), indent=1)
    print('IoU', BK.align_report(obj, CONCEPT, cam, os.path.join(OUT, 'align.png')), cam)
    if '--align-only' in sys.argv:
        sys.exit()

    size = BK.texture_size_for(obj)
    tex_path = os.path.join(OUT, f'T_{ID}.png')
    tex, st = bake_per_face(obj, cam, cube_views(obj), size, tex_path, os.path.join(OUT, 'coverage.png'))
    print('bake', json.dumps(st))
    R.apply_texture(obj, tex_path, ID)
    stats = check_fit(obj)
    stats['texture'] = f'{size}x{size}'
    R.render_views(obj, os.path.join(OUT, 'preview.png'), concept_cam=cam, concept_size=(1536, 1024),
                   concept_path=CONCEPT_ORIG)
    R.shelf_fit_render(obj, os.path.join(OUT, 'shelf.png'))
    R.export(obj, OUT, ID, tex_path)
    iou = BK.align_report(obj, CONCEPT, cam)
    json.dump({'id': ID, 'stats': stats, 'iou': round(iou, 4), 'bake': st, 'camera': cam},
              open(os.path.join(OUT, 'stats.json'), 'w'), indent=1)
    R.card(OUT, ID, stats)
    print('done', stats)
