"""MG01 details: blue leafy branches B01-B04 (+ leaf cards), the loincloth, and their BaseColor textures.

Pure-data builders (core.Shell / plain dicts). bpy is only *used* by branch_roots(body) (ray casts),
the *_object() helpers and textured_material(); PIL/numpy by the texture painters.

  branch(bid, root, normal, up_hint, length, host_group, seed=0, ...)
      -> dict(stem=[Shell...], leaves=[card...], joints={bid: [...], bid+'_f1': [...], ...})
  branch_roots(body=None) -> {bid: dict(root, normal, surface_normal, up_hint, length, host_group, seed, forks, leaves)}
  loincloth(pelvis_centre, axis_up, front, radii, top_z_offset, length, seed=0, ...)
      -> dict(shells=[Shell], joints={...}, uv_layout={...}, tri_count=int)
  cloth_fit_from_layout() -> kwargs for loincloth() fitted to layout.TORSO / HUB / LIMBS
  leaves_texture(path) / cloth_texture(path, uv_layout)
  leaves_object(cards, image) / cloth_object(cloth, image) / textured_material(name, image, alpha_clip)

Coordinates as in core.py: +X canonical screen right, -Y front, +Z up, Blender metres at 1:100.
"""
import math, random, os
try:
    import bpy  # noqa: F401  (the bpy wheel only exposes mathutils after `import bpy`)
except ImportError:  # pragma: no cover
    bpy = None
from mathutils import Vector as V, Quaternion
from mathutils.bvhtree import BVHTree
import core
import layout as L

OUT_DIR = '/home/claude/work/out/details'
LEAVES_PNG = os.path.join(OUT_DIR, 'T_MG01_Leaves_BaseColor.png')
CLOTH_PNG = os.path.join(OUT_DIR, 'T_MG01_Cloth_BaseColor.png')

UP = V((0, 0, 1))
Y = V((0, 1, 0))

# ======================================================================== small vector helpers


def _n(v):
    v = V(v)
    return v.normalized() if v.length > 1e-9 else V((0, 0, 1))


def _perp(v, hint=Y):
    v = _n(v)
    p = V(hint) - v * V(hint).dot(v)
    if p.length < 1e-4:
        p = V((1, 0, 0)) - v * v.x
    return p.normalized()


def _rot(v, axis, deg):
    return Quaternion(_n(axis), math.radians(deg)) @ V(v)


def _bend(a, b, w):
    return _n(V(a) * (1 - w) + V(b) * w)


def _kink(d, rnd, deg):
    """Rotate d by +-deg around a random axis perpendicular to it (low-poly crooked kink)."""
    ax = _rot(_perp(d), d, rnd.uniform(0, 360))
    return _n(_rot(d, ax, rnd.uniform(0.45, 1.0) * deg * rnd.choice((-1, 1))))


# ======================================================================== leaf texture layout
TEX_LEAVES = 256
# leaf cells in PNG pixels (x0, y0, x1, y1), y down; petiole at the bottom-centre, tip at the top.
LEAF_CELLS_PX = [(0, 0, 80, 128), (80, 0, 160, 128), (160, 0, 240, 128), (0, 128, 80, 240)]
STEM_STRIP_PX = (240, 0, 256, 128)    # solid blue (stem) strip, if the lead ever wants it
LEAF_SOLID_PX = (240, 128, 256, 256)  # solid leaf green strip


def _px_rect_to_uv(r, size):
    x0, y0, x1, y1 = r
    return (x0 / size, 1 - y1 / size, x1 / size, 1 - y0 / size)  # (u0, v0, u1, v1), v up


LEAF_RECTS = [_px_rect_to_uv(r, TEX_LEAVES) for r in LEAF_CELLS_PX]
LEAF_ASPECT = 80 / 128  # card width / card length, matches the cells -> no stretching
LEAF_LEN = 0.52          # m (working scale); canon leaves ~0.45 m long, a bit bigger reads better in-game


# ======================================================================== branches
def _twig(name, pts, radii, sides, rnd, group, mat='body', jitter=0.08):
    """Closed low-poly tapered twig: rings at pts[:-1], apex at pts[-1], start cap. Outward faces."""
    P = [V(p) for p in pts]
    frames = core.transport_frames(P, UP)
    sh = core.Shell(name, group, mat)
    ph = rnd.uniform(0, 2 * math.pi)
    rings = []
    for i in range(len(P) - 1):
        T, N, B = frames[i]
        ring = []
        for k in range(sides):
            a = ph + 2 * math.pi * k / sides
            j = 1 + rnd.uniform(-jitter, jitter)
            ring.append(sh.v(P[i] + (N * math.cos(a) + B * math.sin(a)) * radii[i] * j))
        rings.append(ring)
    for a, b in zip(rings, rings[1:]):
        for k in range(sides):
            k2 = (k + 1) % sides
            sh.f(a[k], a[k2], b[k2], b[k])
    sh.f(*reversed(rings[0]))
    apex = sh.v(P[-1])
    r = rings[-1]
    for k in range(sides):
        sh.f(r[k], r[(k + 1) % sides], apex)
    return sh


def _leaf_card(base, axis, face, size, rect, mirror, group, bid):
    """One quad: petiole at `base`, blade along `axis`, blade normal ~ `face` (made perpendicular)."""
    a = _n(axis)
    s = a.cross(_n(face))
    if s.length < 1e-4:
        s = _perp(a)
    s.normalize()
    h, w = size, size * LEAF_ASPECT
    co = [base - s * w * 0.5, base + s * w * 0.5, base + a * h + s * w * 0.5, base + a * h - s * w * 0.5]
    u0, v0, u1, v1 = rect
    if mirror:
        u0, u1 = u1, u0
    uv = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
    return dict(co=co, uv=uv, rect=rect, normal=_n((co[1] - co[0]).cross(co[3] - co[0])), group=group, branch=bid)


def _leaf_cluster(bid, tip, tdir, n, rnd, size, up, group, rects):
    """n leaf cards fanning out of a twig tip in varied directions (never all coplanar).
    ~2/3 of the blades face roughly front/back (+-Y, read in the front/back views), the rest face
    sideways/up so the cluster still reads from the side and from above."""
    cards = []
    t = _n(tdir)
    q0 = _perp(t, Y)
    az0 = rnd.uniform(0, 360)
    for k in range(n):
        az = az0 + 360.0 * k / n + rnd.uniform(-25, 25)
        q = _rot(q0, t, az)
        q = _n(q - Y * q.dot(Y) * 0.5)  # bias the fan towards the screen (X-Z) plane
        spread = 8 if (k == 0 and n >= 3) else rnd.uniform(28, 62)  # one terminal leaf ~ straight on
        a = _n(t * math.cos(math.radians(spread)) + q * math.sin(math.radians(spread)) + up * 0.35)
        if k % 3 != 2:
            face = _rot(Y * rnd.choice((-1, 1)), _perp(Y, a), rnd.uniform(-45, 45))
        else:
            face = _n(_rot(up, a, rnd.uniform(-70, 70)))
        base = tip - t * 0.04 + a * 0.02
        cards.append(_leaf_card(base, a, face, size * rnd.uniform(0.82, 1.12), rects[rnd.randrange(len(rects))],
                                rnd.random() < 0.5, group, bid))
    return cards


def branch(bid, root, normal, up_hint=(0, 0, 1), length=2.0, host_group=None, seed=0, *,
           surface_normal=None, forks=2, leaves=7, sink=0.3, leaf_rects=None, fork_axis=(0, 1, 0)):
    """A small blue sprout growing out of a limb surface.

    root           : point ON the host surface.
    normal         : main growth direction at the root (outward, already biased up/to the canon side).
    surface_normal : host face normal (used to sink the base); defaults to `normal`.
    up_hint        : the twig bends towards it ('towards the light').
    length         : ~1.5-2.4 m (whole twig incl. forks, leaves add ~0.4 m).
    forks          : 2 or 3 side forks (stem tris: 2 -> 66, 3 -> 80).
    leaves         : leaf cards (2 tris each), distributed over the main tip and the fork tips.
    fork_axis      : forks splay by rotating around this axis (default Y -> forks fan in the X-Z plane,
                     so they read sideways in the front/back views; the kinks add depth).
    Returns dict(stem=[Shell...] (group=host_group, mat='body', closed, base sunk `sink` m into the host),
                 leaves=[card dicts: co(4 Vectors CCW), uv(4 tuples), rect, normal, group, branch],
                 joints={bid: [root, exit, kink1, kink2, tip], bid+'_f1': [start, kink, tip], ...},
                 tris=dict(stem=int, leaves=int)).
    """
    rnd = random.Random(seed * 1013 + sum(map(ord, bid)))
    rects = leaf_rects or LEAF_RECTS
    g = _n(normal)
    ns = _n(surface_normal) if surface_normal is not None else g
    up = _n(up_hint)
    Lg = float(length)
    root = V(root)
    group = host_group or bid

    # ---- main stem: sunk ring, flare ring just outside the surface, 2 kinks, apex
    p0 = root - ns * sink
    p1 = root + g * 0.10 * Lg
    d1 = _kink(_bend(g, up, 0.22), rnd, 16)
    k1 = p1 + d1 * 0.30 * Lg
    d2 = _kink(_bend(d1, up, 0.30), rnd, 22)
    k2 = k1 + d2 * 0.26 * Lg
    d3 = _kink(_bend(d2, up, 0.25), rnd, 18)
    tip = k2 + d3 * 0.24 * Lg
    main_r = [0.13 * Lg, 0.11 * Lg, 0.054 * Lg, 0.038 * Lg]  # flared root, thin top
    stem = [_twig(f'{bid}_stem', [p0, p1, k1, k2, tip], main_r, 5, rnd, group)]
    joints = {bid: [root.copy(), p1, k1, k2, tip]}

    # ---- forks (closed 4-sided twigs starting inside the main stem)
    fax = _n(fork_axis)
    fork_specs = [(k1, d2, 0.46, 0.042), (k1.lerp(k2, 0.55), d3, 0.36, 0.035), (k2.lerp(tip, 0.45), d3, 0.27, 0.029)]
    side = rnd.choice((-1, 1))
    tips = [(tip, d3)]
    for fi in range(max(0, min(3, forks))):
        start, dmain, flen, fr = fork_specs[fi]
        ax = _perp(dmain, fax)
        ax = _rot(ax, dmain, rnd.uniform(-30, 30))  # a little depth so it is not perfectly planar
        df = _bend(_rot(dmain, ax, side * rnd.uniform(42, 60)), up, 0.12)
        side = -side
        fs = start - dmain * 0.03
        fm = start + df * flen * 0.55 * Lg
        df2 = _kink(_bend(df, up, 0.2), rnd, 16)
        ft = fm + df2 * flen * 0.45 * Lg
        stem.append(_twig(f'{bid}_f{fi + 1}', [fs, fm, ft], [fr * Lg, fr * 0.75 * Lg], 4, rnd, group))
        joints[f'{bid}_f{fi + 1}'] = [start.copy(), fm, ft]
        tips.append((ft, df2))

    # ---- leaves: distribute the cards over the tips (main tip gets the most)
    n_tips = len(tips)
    counts = [leaves // n_tips] * n_tips
    for i in range(leaves - sum(counts)):
        counts[i % n_tips] += 1
    size = LEAF_LEN * max(0.85, min(1.1, Lg / 1.8))
    cards = []
    for (tp, td), cnt in zip(tips, counts):
        if cnt:
            cards += _leaf_cluster(bid, tp, td, cnt, rnd, size, up, group, rects)
    return dict(stem=stem, leaves=cards, joints=joints,
                tris=dict(stem=sum(s.tri_count() for s in stem), leaves=2 * len(cards)))


# ======================================================================== branch roots
# Per-branch placement: host limb, outward side in 3D ('side' = where the twig physically sticks out,
# kept from every camera), length/forks/leaves. Canon pixels come from layout.BRANCHES.
BRANCH_SPEC = {
    'B01': dict(host='C01', side=(-0.55, 0.0, 1.0), length=1.65, forks=2, leaves=7, seed=1),   # top of the upper-left arc
    'B02': dict(host='C01', side=(-0.45, -0.35, 1.0), length=1.35, forks=2, leaves=6, seed=2),  # on top of the C01 hand, waist gap
    'B03': dict(host='C02', side=(1.0, 0.0, 0.35), length=1.95, forks=3, leaves=8, seed=3),    # outer (+X) edge of the right cradle arm
    'B04': dict(host='S01', side=(-0.75, 0.0, 1.0), length=1.8, forks=2, leaves=7, seed=4),   # outer/top of the lower-left support arm
}


def canon_ray(px, py):
    """Ray from the canon camera through a canon pixel (same math as layout.P / layout.G)."""
    d = V(((px - L.CX0) / L.F_PX, 1.0, -(py - L.CY0) / L.F_PX))
    return V(L.CAM), d.normalized()


class _BodyBVH:
    """World-space BVH of a mesh object + per-polygon dominant vertex group."""

    def __init__(self, ob):
        me = ob.data
        mw = ob.matrix_world
        self.verts = [mw @ v.co for v in me.vertices]
        self.polys = [tuple(p.vertices) for p in me.polygons]
        nm = mw.to_3x3().inverted().transposed()
        self.normals = [_n(nm @ p.normal) for p in me.polygons]
        self.centres = [mw @ p.center for p in me.polygons]
        names = {g.index: g.name for g in ob.vertex_groups}
        vgrp = []
        for v in me.vertices:
            best = max(v.groups, key=lambda e: e.weight, default=None)
            vgrp.append(names.get(best.group) if best is not None else None)
        self.pgroup = []
        for p in self.polys:
            cnt = {}
            for i in p:
                cnt[vgrp[i]] = cnt.get(vgrp[i], 0) + 1
            self.pgroup.append(max(cnt, key=cnt.get))
        self.all = BVHTree.FromPolygons(self.verts, self.polys)
        self._sub = {}

    def sub(self, group):
        """(bvh, index_map) restricted to polygons of `group` (None -> whole mesh)."""
        if group is None or group not in self.pgroup:
            return self.all, None
        if group not in self._sub:
            ids = [i for i, g in enumerate(self.pgroup) if g == group]
            self._sub[group] = (BVHTree.FromPolygons(self.verts, [self.polys[i] for i in ids]), ids)
        return self._sub[group]

    def blocked(self, a, b, eps=0.02):
        d = b - a
        if d.length < 1e-6:
            return False
        hit = self.all.ray_cast(a, d.normalized(), d.length)
        return hit[0] is not None and hit[3] > eps


def _root_on_body(bb, o, d, host, side, refine):
    bvh, ids = bb.sub(host)
    how = 'ray'
    loc, nor, idx, dist = bb.all.ray_cast(o, d)
    if idx is None or (host and bb.pgroup[idx] != host):
        loc, nor, idx, dist = bvh.ray_cast(o, d)
        if idx is not None and ids:
            idx = ids[idx]
        how = 'ray-host'
    if idx is None:  # ray misses the host: nearest host surface point to the ray (silhouette)
        best = None
        for k in range(0, 600):
            x = o + d * (5.0 + k * 0.08)
            r = bvh.find_nearest(x)
            if r[0] is not None and (best is None or r[3] < best[3]):
                best = r
        loc, nor, idx, dist = best
        if ids:
            idx = ids[idx]
        how = 'nearest'
    p = V(loc)
    # refine: within `refine` m of the canon hit, take the host face that faces the branch's physical side most
    sd = _n(side)
    cand = range(len(bb.polys)) if not ids else ids
    best_i, best_s = idx, -9
    for i in cand:
        dc = (bb.centres[i] - p).length
        if dc > refine:
            continue
        s = bb.normals[i].dot(sd) - 0.35 * dc / refine
        if s > best_s:
            best_i, best_s = i, s
    c, n = bb.centres[best_i], bb.normals[best_i]
    q = p - n * (p - c).dot(n)               # canon hit projected into the chosen face plane
    root = c.lerp(q, 0.4)
    r = bvh.find_nearest(root)
    if r[0] is not None:
        root = V(r[0])
    return root, n, bb.pgroup[best_i], how


def _layout_capsules(host):
    """Fallback host geometry from layout: list of (a, b, ra, rb)."""
    caps = []
    for lb in L.LIMBS:
        if lb['id'] != host:
            continue
        samp = core.catmull_rom(lb['pts'], 3)
        rad = [sum(r) / 2 if isinstance(r, (tuple, list)) else r for r in lb['radii']]
        pts = [(s[0], core.lerp_param(rad, s[1], s[2])) for s in samp]
        caps += [(a, b, ra, rb) for (a, ra), (b, rb) in zip(pts, pts[1:])]
    hd = L.CRADLE_HANDS.get(host) or L.SUPPORT_HANDS.get(host)
    if hd:
        kc = sum(hd['knuckles'], V()) / len(hd['knuckles'])
        caps.append((hd['wrist'], kc, 0.5, 0.45))
        caps += [(k, t, 0.24, 0.2) for k, t in zip(hd['knuckles'], hd['tips'])]
        if 'thumb' in hd:
            caps.append((hd['thumb'][0], hd['thumb'][1], 0.3, 0.2))
    return caps


def _seg_ray_closest(a, b, o, d):
    """Closest points between segment a-b and ray o+d*t. Returns (point_on_seg, s, point_on_ray, dist)."""
    best = None
    for k in range(41):
        s = k / 40
        x = a.lerp(b, s)
        t = max(0.0, (x - o).dot(d))
        y = o + d * t
        dd = (x - y).length
        if best is None or dd < best[3]:
            best = (x, s, y, dd)
    return best


def _root_on_layout(o, d, host, side):
    best = None
    for a, b, ra, rb in _layout_capsules(host):
        x, s, y, dd = _seg_ray_closest(V(a), V(b), o, d)
        r = ra + (rb - ra) * s
        if best is None or dd - r < best[0]:
            best = (dd - r, x, r, V(b) - V(a), y)
    _, x, r, ax, y = best
    axn = _n(ax)
    sd = _n(side)
    out = _n((sd - axn * sd.dot(axn)) * 0.7 + _n((y - x) - axn * (y - x).dot(axn)) * 0.3)
    return x + out * r * 0.92, out, host, 'layout'


def _candidate_dirs(g, ns):
    out = [g]
    for ang in (15, 30, 45, 60):
        for k in range(8):
            ax = _rot(_perp(g), g, k * 45)
            c = _rot(g, ax, ang)
            if c.dot(ns) > 0.3:
                out.append(c)
    out.sort(key=lambda c: -(c.dot(g) + 0.25 * c.dot(UP)))
    return out


def _branch_clear(bb, br, length):
    """True if no stem segment (outside the host) passes through the body and leaves are clear of it."""
    for key, ch in br['joints'].items():
        seq = ch[1:] if '_f' not in key else ch
        for a, b in zip(seq, seq[1:]):
            if bb.blocked(V(a) + (V(b) - V(a)) * 0.02, V(b)):
                return False
    for c in br['leaves']:
        m = (c['co'][0] + c['co'][1] + c['co'][2] + c['co'][3]) / 4
        r = bb.all.find_nearest(m)
        if r[0] is not None and r[3] < 0.12:
            return False
    return True


def branch_roots(body=None, spec=None, refine=0.9, up=(0, 0, 1)):
    """Find where B01-B04 grow (canon pixels in layout.BRANCHES).

    body given (bpy mesh object with vertex groups named by limb ID): cast a ray from the canon camera
    through the pixel; take the hit on the host limb (or, if the ray misses it, the host surface point
    nearest to the ray), then move to the nearby host face that faces the branch's physical side
    (BRANCH_SPEC 'side', world space -> the twig keeps its side from every camera).
    The growth direction blends that face normal with 'side' and 'up' (towards the light) and is then
    checked against the whole body: a candidate direction is rejected if any stem segment would run
    through the body or a leaf would touch it.
    body None: same logic on capsules built from layout limb/hand targets (no collision check).
    Returns {bid: dict(root, normal (growth dir), surface_normal, up_hint, length, host_group, seed,
                       forks, leaves, method)} -> feed to branch(**kw) via branch_kwargs().
    """
    spec = spec or BRANCH_SPEC
    upv = _n(up)
    bb = _BodyBVH(body) if body is not None else None
    out = {}
    for bid, (px, py) in L.BRANCHES.items():
        s = spec[bid]
        o, d = canon_ray(px, py)
        sd = _n(s['side'])
        if bb is not None:
            root, ns, grp, how = _root_on_body(bb, o, d, s['host'], sd, refine)
        else:
            root, ns, grp, how = _root_on_layout(o, d, s['host'], sd)
        g0 = _n(ns * 0.45 + sd * 0.35 + upv * 0.25)
        g = g0
        if bb is not None:
            for c in _candidate_dirs(g0, ns):
                br = branch(bid, root, c, upv, s['length'], grp, s['seed'], surface_normal=ns,
                            forks=s['forks'], leaves=s['leaves'])
                if _branch_clear(bb, br, s['length']):
                    g = c
                    break
            else:
                how += '+blocked'
        out[bid] = dict(bid=bid, root=root, normal=g, surface_normal=ns, up_hint=upv.copy(), length=s['length'],
                        host_group=grp, seed=s['seed'], forks=s['forks'], leaves=s['leaves'], method=how)
    return out


def branch_kwargs(r):
    """branch_roots() entry -> kwargs for branch()."""
    return dict(bid=r['bid'], root=r['root'], normal=r['normal'], up_hint=r['up_hint'], length=r['length'],
                host_group=r['host_group'], seed=r['seed'], surface_normal=r['surface_normal'],
                forks=r['forks'], leaves=r['leaves'])


def build_branches(body=None):
    """Convenience: roots + all four branches. Returns (stems [Shell], cards [dict], joints {}, roots {})."""
    roots = branch_roots(body)
    stems, cards, joints = [], [], {}
    for bid in sorted(roots):
        br = branch(**branch_kwargs(roots[bid]))
        stems += br['stem']
        cards += br['leaves']
        joints.update(br['joints'])
    return stems, cards, joints, roots


# ======================================================================== loincloth
def _ell(rs, rf, th):
    """Radius of an ellipse (side half-width rs, front half-depth rf) in direction th (0 = front, +90 = +X)."""
    return 1.0 / math.sqrt((math.cos(th) / rf) ** 2 + (math.sin(th) / rs) ** 2)


def _push_out(o, dr, r0, obstacles, clearance, r_max):
    """Smallest r in [r0, r_max] such that o + dr*r is outside every capsule (+clearance).
    If the ray runs along a limb (cannot escape) keep r0: the limb simply comes out through the cloth."""
    def inside(x):
        for a, b, ra, rb in obstacles:
            ab = b - a
            t = max(0.0, min(1.0, (x - a).dot(ab) / max(ab.length_squared, 1e-9)))
            if (x - (a + ab * t)).length < ra + (rb - ra) * t + clearance:
                return True
        return False
    r = r0
    while r <= r_max:
        if not inside(o + dr * r):
            return r
        r += 0.03
    return r0


def loincloth(pelvis_centre, axis_up, front, radii, top_z_offset, length, seed=0, *,
              profile=None, obstacles=(), n_cols=12, band_h=0.32, flare=1.36, thickness=0.035,
              knot_deg=55.0, back_flap=1.35, clearance=0.05, max_push=1.45):
    """Rough hide/cloth wrapped around the hips: a belt band, a skirt with 2-3 folds and a jagged hem
    (big physical triangular cut-outs), the longest tooth hanging at the back, a knot with two pointed
    flaps at the front on the +X side (the old man's left = canon screen right).

    pelvis_centre : body axis point the rows are measured from; axis_up: body up; front: towards the viewer.
    radii         : (r_side, r_front) of the body at the top ring (used when `profile` is None).
    top_z_offset  : height of the band top above pelvis_centre along axis_up.
    length        : band top -> typical tooth tip.
    profile(h)    : optional -> (r_side, r_front[, centre_offset Vector]) body section at height h.
    obstacles     : capsules (a, b, ra, rb) (thighs, hub) the skirt drapes over (radial push-out).
    Double-sided by construction: outer sheet (faces outward) + inner sheet `thickness` inside with
    reversed winding (an open two-layer sheet, NOT a closed shell -> separate object, never in the union),
    plus a small closed knot lump. Each Shell carries .uv (per face, per corner) for T_MG01_Cloth.
    Returns dict(shells=[Shell], joints={'cloth': [...], 'cloth_back': [...], 'cloth_knot': [...]},
                 uv_layout={...}, tri_count=int).
    """
    rnd = random.Random(seed * 7 + 3)
    c = V(pelvis_centre)
    U = _n(axis_up)
    F = _n(V(front) - U * V(front).dot(U))
    X = U.cross(F)  # +X when F = -Y, U = +Z

    def prof(h):
        if profile is None:
            return radii[0], radii[1], V()
        r = profile(h)
        return (r[0], r[1], V(r[2]) if len(r) > 2 else V())

    def dirv(th):
        return F * math.cos(th) + X * math.sin(th)

    N = n_cols
    th = [-math.pi / 2 + 2 * math.pi * i / N + rnd.uniform(-0.07, 0.07) for i in range(N)]
    skirt = length - band_h
    folds = [math.radians(a) for a in (-55, 150, 215)]  # front-left, back-left, back-right folds

    def fold(t):
        f = sum(max(0.0, math.cos(t - a)) ** 10 for a in folds)
        return 0.16 * f

    def tooth_factor(t):
        a = abs(math.atan2(math.sin(t - math.pi), math.cos(t - math.pi)))  # 0 at the back centre
        back = max(0.0, 1 - a / 0.7) * (back_flap - 1)
        fl = max(0.0, math.cos(t - math.radians(-60))) ** 6 * 0.15  # front-left tatters a bit longer
        fc = max(0.0, math.cos(t)) ** 8 * -0.15                      # front centre (under the thigh) shorter
        return 1 + back + fl + fc

    def ring_point(h, t, r_scale, add, push):
        rs, rf, off = prof(h)
        o = c + U * h + off
        dr = dirv(t)
        r0 = _ell(rs, rf, t) * r_scale + add
        if push and obstacles:
            r0 = _push_out(o, dr, r0, obstacles, clearance, r0 * max_push)
        return o + dr * r0

    top, band, notch, tipv, tipth = [], [], [], [], []
    for i, t in enumerate(th):
        ht = top_z_offset + 0.30 * math.sin(t) * max(math.cos(t), 0.0) + rnd.uniform(-0.03, 0.03)
        hb = ht - band_h * rnd.uniform(0.9, 1.1)
        hn = hb - skirt * rnd.uniform(0.28, 0.46)
        wob = 0.04 * (1 if i % 2 else -1)
        top.append(ring_point(ht, t, 0.95, 0.0, False))
        band.append(ring_point(hb, t, 1.04, 0.07, True))
        fl = 1 + (flare - 1) * 0.6
        notch.append(ring_point(hn, t, fl, clearance + fold(t) + wob, True))
    for i in range(N):
        t0, t1 = th[i], th[(i + 1) % N] + (2 * math.pi if i == N - 1 else 0)
        tm = (t0 + t1) / 2 + rnd.uniform(-0.08, 0.08)
        hb = (top_z_offset - band_h)
        htip = hb - skirt * tooth_factor(tm) * rnd.uniform(0.88, 1.1)
        tipv.append(ring_point(htip, tm, flare, clearance + fold(tm) + 0.03, True))
        tipth.append(tm)

    # ---- outer sheet faces (index rows: top 0..N-1, band N.., notch 2N.., tips 3N..)
    verts = top + band + notch + tipv
    T0, B0, N0, P0 = 0, N, 2 * N, 3 * N
    faces, fcols = [], []  # fcols: (kind, column) for UVs
    for i in range(N):
        j = (i + 1) % N
        faces.append((T0 + i, B0 + i, B0 + j, T0 + j)); fcols.append(('band', i))
        faces.append((B0 + i, N0 + i, N0 + j, B0 + j)); fcols.append(('skirt', i))
        faces.append((N0 + i, P0 + i, N0 + j)); fcols.append(('tooth', i))

    # ---- UVs: two strips (front half / back half), arc-length unwrapped, one uniform scale (no stretch)
    half = N // 2
    halves = [list(range(0, half + 1)), list(range(half, N + 1))]  # column indices (N == 0 wrap)

    def row_len(row, cols):
        return sum((row[cols[k + 1] % N] - row[cols[k] % N]).length for k in range(len(cols) - 1))
    max_w = max(row_len(r, cols) for r in (top, band, notch) for cols in halves)
    max_h = max(max((band[i] - top[i]).length + (notch[i] - band[i]).length for i in range(N)) +
                max((tipv[i] - (notch[i] + notch[(i + 1) % N]) / 2).length for i in range(N)), 0.1)
    strips = [(0.02, 0.84, 0.52, 0.98), (0.02, 0.84, 0.02, 0.48)]  # (u0, u1, v0, v1) front, back
    s = min((strips[0][1] - strips[0][0]) / max_w, (strips[0][3] - strips[0][2]) / max_h)
    uvv = {}  # (half, vertex index) -> uv
    for hi, cols in enumerate(halves):
        u0, u1, v0, v1 = strips[hi]
        uc = (u0 + u1) / 2
        vtop = {}
        for row, base in ((top, T0), (band, B0), (notch, N0)):
            cum = [0.0]
            for k in range(len(cols) - 1):
                cum.append(cum[-1] + (row[cols[k + 1] % N] - row[cols[k] % N]).length)
            for k, ci in enumerate(cols):
                i = ci % N
                if base == T0:
                    v = v1
                elif base == B0:
                    v = v1 - (band[i] - top[i]).length * s
                else:
                    v = v1 - ((band[i] - top[i]).length + (notch[i] - band[i]).length) * s
                uvv[(hi, base + i, k)] = (uc + (cum[k] - cum[-1] / 2) * s, v)
        for k in range(len(cols) - 1):
            i = cols[k] % N
            a, b = uvv[(hi, N0 + i, k)], uvv[(hi, N0 + cols[k + 1] % N, k + 1)]
            m = (notch[i] + notch[cols[k + 1] % N]) / 2
            w = (tipv[i] - m)
            side = w.dot(_n(notch[cols[k + 1] % N] - notch[i]))
            down = math.sqrt(max(w.length_squared - side * side, 0.0))
            uvv[(hi, P0 + i, k)] = ((a[0] + b[0]) / 2 + side * s, (a[1] + b[1]) / 2 - down * s)
    face_uv = []
    for (kind, i), f in zip(fcols, faces):
        hi = 0 if i < half else 1
        k = i - (0 if hi == 0 else half)
        if kind == 'band':
            face_uv.append([uvv[(hi, T0 + i, k)], uvv[(hi, B0 + i, k)], uvv[(hi, B0 + (i + 1) % N, k + 1)], uvv[(hi, T0 + (i + 1) % N, k + 1)]])
        elif kind == 'skirt':
            face_uv.append([uvv[(hi, B0 + i, k)], uvv[(hi, N0 + i, k)], uvv[(hi, N0 + (i + 1) % N, k + 1)], uvv[(hi, B0 + (i + 1) % N, k + 1)]])
        else:
            face_uv.append([uvv[(hi, N0 + i, k)], uvv[(hi, P0 + i, k)], uvv[(hi, N0 + (i + 1) % N, k + 1)]])

    # ---- vertex normals (outward, horizontal-ish) for the inner offset
    vn = [V() for _ in verts]
    for f in faces:
        fn = (verts[f[1]] - verts[f[0]]).cross(verts[f[-1]] - verts[f[0]])
        for i in f:
            vn[i] += fn
    vn = [_n(n) for n in vn]

    sheet = core.Shell('CLOTH', 'CLOTH', 'cloth')
    sheet.verts = [V(p) for p in verts] + [V(p) - n * thickness for p, n in zip(verts, vn)]
    off = len(verts)
    sheet.faces = list(faces) + [tuple(off + i for i in reversed(f)) for f in faces]
    sheet.uv = [list(u) for u in face_uv] + [list(reversed(u)) for u in face_uv]

    # ---- knot (closed lump) + two pointed flaps (double-sided) at the front, +X side
    tk = math.radians(knot_deg)
    hk = top_z_offset - band_h * 0.45 + 0.30 * math.sin(tk) * max(math.cos(tk), 0.0)
    kp = ring_point(hk, tk, 1.04, 0.10, False)
    rs_, rf_, off_ = prof(hk)
    out = _n(kp - (c + U * hk + off_))
    knot = core.blob('CLOTH_knot', kp, (0.2, 0.17, 0.15), rings=2, sides=4, jitter=0.12, seed=seed + 5,
                     rot=_frame3(out, U), group='CLOTH', mat='cloth')
    eu0, eu1, ev0, ev1 = 0.86, 0.98, 0.52, 0.98
    eu_c = (eu0 + eu1) / 2
    side_t = _n(U.cross(out))
    knot.uv = [[(eu_c + (knot.verts[i] - kp).dot(side_t) * s, ev0 + 0.07 + (knot.verts[i] - kp).dot(U) * s)
                for i in f] for f in knot.faces]
    flaps = core.Shell('CLOTH_flaps', 'CLOTH', 'cloth')
    flaps.uv = []
    flap_defs = [(_n(U * 0.85 + X * 0.55 + out * 0.25), 0.62, 0.16, ev0 + 0.16),
                 (_n(-U * 0.55 + X * 0.75 + out * 0.3), 0.48, 0.14, ev0 + 0.16 + 0.62 * s + 0.03)]
    for fd, flen, fw, va in flap_defs:
        sd = _n(side_t - fd * side_t.dot(fd))
        nrm = _n(fd.cross(sd))
        if nrm.dot(out) < 0:
            nrm, sd = -nrm, -sd
        b1, b2 = kp + sd * fw, kp - sd * fw
        mid = kp + fd * flen * 0.55 + sd * fw * 0.6 + nrm * 0.03
        jag = rnd.uniform(-0.3, 0.3)
        tipp = kp + fd * flen + sd * fw * jag
        quad = [b2, b1, mid, tipp]
        uvq = [(eu_c - fw * s, va), (eu_c + fw * s, va), (eu_c + fw * 0.6 * s, va + flen * 0.55 * s),
               (eu_c + fw * jag * s, va + flen * s)]
        base = len(flaps.verts)
        for p in quad:
            flaps.v(p + nrm * 0.0)
        for p in quad:
            flaps.v(p - nrm * thickness)
        for tri in ((0, 1, 2), (0, 2, 3)):
            flaps.f(*(base + i for i in tri))
            flaps.uv.append([uvq[i] for i in tri])
            flaps.f(*(base + 4 + i for i in reversed(tri)))
            flaps.uv.append([uvq[i] for i in reversed(tri)])
    shells = [sheet, knot, flaps]

    back_i = min(range(N), key=lambda i: abs(math.atan2(math.sin(tipth[i] - math.pi), math.cos(tipth[i] - math.pi))))
    joints = {
        'cloth': [c + U * top_z_offset + prof(top_z_offset)[2], c + U * (top_z_offset - length) + prof(top_z_offset - length)[2]],
        'cloth_back': [band[back_i].lerp(band[(back_i + 1) % N], 0.5), notch[back_i].lerp(notch[(back_i + 1) % N], 0.5), tipv[back_i]],
        'cloth_knot': [kp, kp + flap_defs[0][0] * flap_defs[0][1]],
    }
    uv_layout = dict(scale=s, strips=strips, band_v=[(v1 - band_h * s, v1) for (_, _, _, v1) in strips],
                     extra=(eu0, eu1, ev0, ev1), size=512)
    return dict(shells=shells, joints=joints, uv_layout=uv_layout, tri_count=sum(x.tri_count() for x in shells))


def _frame3(fwd, up):
    M = core.frame_matrix((0, 0, 0), fwd, up)
    return M.to_3x3()


def cloth_fit_from_layout(seed=0):
    """kwargs for loincloth() fitted to layout.TORSO (pelvis = 1, waist = 2), HUB and the leg hips.
    TORSO radii are (side half-width, front/back half-depth) (tube with up_hint (1,0,0))."""
    tp = L.TORSO['pts']
    rad = [r if isinstance(r, (tuple, list)) else (r, r) for r in L.TORSO['radii']]
    c = V(tp[1])
    U = _n(tp[2] - tp[0])
    samp = core.catmull_rom(tp[:4], 6)

    def profile(h):
        z = (c + U * h).z
        best = min(samp, key=lambda s: abs(s[0].z - z))
        rs, rf = core.lerp_param(rad, best[1], best[2])  # layout TORSO radii = (side half-width, front half-depth), tube up_hint (1,0,0)
        o = best[0] - (c + U * h)
        o -= U * o.dot(U)
        return rs, rf, o
    obstacles = []
    for lb in L.LIMBS:
        if lb['kind'] == 'leg':
            a, b = V(lb['pts'][0]), V(lb['pts'][1])
            r0 = lb['radii'][0]
            r1 = lb['radii'][1]
            obstacles.append((a, a.lerp(b, 0.5), r0, (r0 + r1) / 2))
    hub = L.HUB['pts']
    hr = [sum(r) / 2 if isinstance(r, (tuple, list)) else r for r in L.HUB['radii']]
    obstacles.append((V(hub[0]), V(hub[1]), hr[0] * 0.9, hr[1] * 0.9))
    rs, rf, _ = profile(0.75)
    return dict(pelvis_centre=c, axis_up=U, front=V((0, -1, 0)), radii=(rs, rf), top_z_offset=0.75, length=1.75,
                seed=seed, profile=profile, obstacles=obstacles)


# ======================================================================== textures
def _hex(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def leaves_texture(path=LEAVES_PNG, seed=7):
    """256x256 RGBA: 4 pixel-art leaves (grey-green #718875 family, darker midrib/outline), hard alpha,
    plus solid strips (blue stem, leaf green). Transparent texels carry the dark leaf colour so mips
    do not fringe. Returns dict(path, rects=LEAF_RECTS (uv), stem_rect, leaf_solid_rect)."""
    import numpy as np
    from PIL import Image, ImageDraw
    S = TEX_LEAVES // 2  # paint at half res, upscale x2 nearest (2x2 texel 'pixels')
    rnd = random.Random(seed)
    fill = np.array(_hex('#718875'), float)
    light = np.array(_hex('#82997f'), float)
    shade = np.array(_hex('#62786a'), float)
    edge = np.array(_hex('#4b5f52'), float)
    rib = np.array(_hex('#55695b'), float)
    img = np.zeros((S, S, 4), np.uint8)
    img[..., :3] = edge.astype(np.uint8)
    shapes = [dict(wid=0.46, bend=0.10, tipk=0.75), dict(wid=0.34, bend=-0.14, tipk=0.9),
              dict(wid=0.40, bend=0.22, tipk=0.65), dict(wid=0.44, bend=-0.06, tipk=0.8)]
    for (x0, y0, x1, y1), sp in zip(LEAF_CELLS_PX, shapes):
        x0, y0, x1, y1 = x0 // 2, y0 // 2, x1 // 2, y1 // 2
        w, h = x1 - x0, y1 - y0
        cx = x0 + w / 2
        base_y, tip_y = y1 - 2, y0 + 2
        n = 24
        mid, left, right = [], [], []
        for k in range(n + 1):
            t = k / n
            y = base_y + (tip_y - base_y) * t
            x = cx + sp['bend'] * w * (t ** 2)
            hw = 0 if t < 0.07 else sp['wid'] * w * (math.sin(math.pi * ((t - 0.07) / 0.93) ** sp['tipk'])) ** 0.85
            hw *= 1 + rnd.uniform(-0.05, 0.05)
            mid.append((x, y))
            left.append((x - hw, y))
            right.append((x + hw, y))
        m = Image.new('L', (S, S), 0)
        d = ImageDraw.Draw(m)
        d.polygon(left + right[::-1], fill=255)
        d.line([(cx - 0.5, base_y + 1), mid[3]], fill=255, width=2)  # petiole
        msk = np.array(m) > 0
        yy, xx = np.mgrid[0:S, 0:S]
        # side of the midline -> light/shade halves
        midx = np.interp(yy, [p[1] for p in mid[::-1]], [p[0] for p in mid[::-1]])
        col = np.where((xx < midx)[..., None], light, shade)
        # a few coarse painted blotches
        for _ in range(3):
            bx, by = rnd.uniform(x0 + w * 0.25, x1 - w * 0.25), rnd.uniform(y0 + h * 0.2, y1 - h * 0.25)
            br = rnd.uniform(2.5, 4.5)
            blot = (xx - bx) ** 2 + (yy - by) ** 2 < br * br
            col = np.where(blot[..., None], fill, col)
        er = msk.copy()
        er[1:, :] &= msk[:-1, :]; er[:-1, :] &= msk[1:, :]; er[:, 1:] &= msk[:, :-1]; er[:, :-1] &= msk[:, 1:]
        border = msk & ~er
        col = np.where(border[..., None], edge, col)
        ribm = np.zeros_like(msk)
        for (xa, ya), (xb, yb) in zip(mid[:-3], mid[1:-3]):
            for q in np.linspace(0, 1, 4):
                ribm[int(round(ya + (yb - ya) * q)), int(round(xa + (xb - xa) * q))] = True
        # side veins
        for k in range(5, n - 5, 4):
            xm, ym = mid[k]
            for sgn in (-1, 1):
                hw = abs(right[k][0] - mid[k][0]) * 0.7
                for q in np.linspace(0, 1, int(hw) + 2):
                    vx, vy = int(round(xm + sgn * hw * q)), int(round(ym - hw * 0.6 * q))
                    if 0 <= vx < S and 0 <= vy < S and er[vy, vx]:
                        col[vy, vx] = (col[vy, vx] * 0.6 + rib * 0.4)
        col = np.where((ribm & msk)[..., None], rib, col)
        sel = msk
        img[sel, :3] = col[sel].astype(np.uint8)
        img[sel, 3] = 255
    for rect, hx in ((STEM_STRIP_PX, '#4A5E9A'), (LEAF_SOLID_PX, '#718875')):
        x0, y0, x1, y1 = (v // 2 for v in rect)
        img[y0:y1, x0:x1, :3] = _hex(hx)
        img[y0:y1, x0:x1, 3] = 255
    im = Image.fromarray(img, 'RGBA').resize((TEX_LEAVES, TEX_LEAVES), Image.NEAREST)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    im.save(path)
    return dict(path=path, rects=LEAF_RECTS, stem_rect=_px_rect_to_uv(STEM_STRIP_PX, TEX_LEAVES),
                leaf_solid_rect=_px_rect_to_uv(LEAF_SOLID_PX, TEX_LEAVES))


def cloth_texture(path=CLOTH_PNG, uv_layout=None, seed=11):
    """512x512 RGB painted rough hide, #826B64 family, flat muted tones in crisp 2x2 texel 'pixels':
    big irregular patches, a darker rolled belt (matching uv_layout['band_v']) with a light lip and a
    dark crease, the hem darkening in 3 flat jagged steps (worn/dirty edge), a few stitches, scratches
    and worn spots. No ornaments, no gradients/noise."""
    import numpy as np
    from PIL import Image, ImageDraw
    lay = uv_layout or dict(strips=[(0.02, 0.84, 0.52, 0.98), (0.02, 0.84, 0.02, 0.48)],
                            band_v=[(0.90, 0.98), (0.40, 0.48)], extra=(0.86, 0.98, 0.52, 0.98), size=512)
    S = 256  # paint at half res -> x2 nearest
    rnd = random.Random(seed)
    base = _hex('#826B64')
    pal = [_hex(h) for h in ('#7b655e', '#8a7269', '#786159', '#8d756b', '#7f6861')]
    dark, darker, light = _hex('#64504a'), _hex('#4f3e3a'), _hex('#987e73')
    belt, belt_d, belt_l = _hex('#715b55'), _hex('#654f4a'), _hex('#7d6660')
    im = Image.new('RGB', (S, S), base)
    d = ImageDraw.Draw(im)

    def blob(cx, cy, rx, ry, col, k=None):
        k = k or rnd.randint(5, 7)
        d.polygon([(cx + math.cos(2 * math.pi * i / k + rnd.uniform(-0.35, 0.35)) * rx * rnd.uniform(0.7, 1.2),
                    cy + math.sin(2 * math.pi * i / k + rnd.uniform(-0.35, 0.35)) * ry * rnd.uniform(0.7, 1.2))
                   for i in range(k)], fill=col)
    for _ in range(70):  # coarse painted patches, wider than tall (hide grain runs around the hips)
        blob(rnd.uniform(0, S), rnd.uniform(0, S), rnd.uniform(9, 22), rnd.uniform(5, 11), rnd.choice(pal))

    def vy(v):  # uv v -> painted pixel row
        return (1 - v) * S
    arr = np.array(im).astype(float)
    for (u0, u1, v0, v1) in lay['strips']:
        xa, xb = max(0, int(u0 * S) - 3), min(S, int(u1 * S) + 3)
        # hem: 3 flat darker steps with jagged (4-texel) boundaries
        prev = 1.0
        for frac, f in ((0.52, 0.93), (0.70, 0.86), (0.86, 0.79)):  # cumulative brightness per step
            y0 = vy(v1 - (v1 - v0) * frac)
            jag = 0
            for xi, x in enumerate(range(xa, xb, 2)):  # random-walk boundary (torn, not comb-like)
                if xi % 2 == 0:
                    jag = max(-6, min(6, jag + rnd.choice((-2, 0, 2))))
                yj = int(y0 + jag)
                arr[max(0, yj):int(vy(v0)) + 2, x:x + 2] *= f / prev
            prev = f
    im = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8))
    d = ImageDraw.Draw(im)
    for (u0, u1, v0, v1), (b0, b1) in zip(lay['strips'], lay['band_v']):
        xa, xb = u0 * S - 3, u1 * S + 3
        ya, yb = vy(b1), vy(b0)
        # rolled belt: flat darker strip, irregular creases, light lip on top, dark crease below
        d.rectangle([xa, ya, xb, yb], fill=belt)
        for _ in range(26):
            x = rnd.uniform(xa, xb)
            y = rnd.uniform(ya + 3, yb - 3)
            d.line([(x, y), (x + rnd.uniform(5, 14), y + rnd.choice((-1, 0, 1)))], fill=rnd.choice((belt_d, belt_l)), width=1)
        x = xa
        while x < xb:
            w = rnd.uniform(6, 20)
            d.rectangle([x, ya, x + w, ya + rnd.choice((1, 2))], fill=light)
            x += w + rnd.uniform(1, 5)
        x = xa
        while x < xb:
            w = rnd.uniform(5, 16)
            yy = yb + rnd.choice((-1, 0, 1))
            d.rectangle([x, yy - 1, x + w, yy], fill=darker)
            x += w
        # (no stitches: the spec asks for plain hide without ornament)
        # scratches / wear marks and a few worn darker spots near the hem
        for _ in range(14):
            x = rnd.uniform(xa, xb)
            y0_ = rnd.uniform(yb + 6, vy(v0) - 10)
            d.line([(x, y0_), (x + rnd.uniform(-3, 3), y0_ + rnd.uniform(5, 12))], fill=rnd.choice((dark, light)), width=1)
        for _ in range(6):
            blob(rnd.uniform(xa + 10, xb - 10), rnd.uniform(vy(v0 + (v1 - v0) * 0.35), vy(v0) - 4), rnd.uniform(3, 6),
                 rnd.uniform(2, 4), dark, k=5)
    # knot / flaps area: darker folded hide
    eu0, eu1, ev0, ev1 = lay['extra']
    d.rectangle([eu0 * S, vy(ev1), eu1 * S, vy(ev0)], fill=_hex('#76605a'))
    for _ in range(10):
        blob(rnd.uniform(eu0 * S, eu1 * S), rnd.uniform(vy(ev1), vy(ev0)), rnd.uniform(4, 8), rnd.uniform(3, 6),
             rnd.choice((belt, belt_l, _hex('#6c5752'))))
    for _ in range(8):
        x = rnd.uniform(eu0 * S, eu1 * S)
        y = rnd.uniform(vy(ev1), vy(ev0))
        d.line([(x, y), (x + rnd.uniform(-3, 3), y + rnd.uniform(5, 10))], fill=rnd.choice((dark, light)), width=1)
    im = im.resize((lay.get('size', 512),) * 2, Image.NEAREST)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    im.save(path)
    return dict(path=path, uv_layout=lay)


# ======================================================================== bpy helpers (objects / materials)
def textured_material(name, image_path, alpha_clip=None, double_sided=False):
    """Principled material with an image texture (Closest = point filter) -> Base Color (+ Alpha).
    Workbench 'TEXTURE' colour mode shows the active image node; alpha_clip sets CLIP/threshold."""
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    try:
        m.use_nodes = True
    except Exception:
        pass
    nt = m.node_tree
    bsdf = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if bsdf is None:
        bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
        outn = next((n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL'), None) or nt.nodes.new('ShaderNodeOutputMaterial')
        nt.links.new(bsdf.outputs['BSDF'], outn.inputs['Surface'])
    img = bpy.data.images.load(image_path, check_existing=True)
    tex = nt.nodes.new('ShaderNodeTexImage')
    tex.image = img
    tex.interpolation = 'Closest'
    tex.extension = 'CLIP' if alpha_clip is not None else 'EXTEND'
    nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = 1.0
    bsdf.inputs['Metallic'].default_value = 0.0
    if alpha_clip is not None:
        nt.links.new(tex.outputs['Alpha'], bsdf.inputs['Alpha'])
        m.blend_method = 'CLIP'
        m.alpha_threshold = alpha_clip
        m.surface_render_method = 'DITHERED'
    m.use_backface_culling = not double_sided
    nt.nodes.active = tex
    return m


def uv_object(name, shells, material, collection=None, groups=None):
    """Mesh object from shells that carry .uv (per face list of (u, v)). Keeps the authored winding
    (no normal recalculation: the cloth is a deliberate two-layer sheet). Vertex group per shell.group."""
    verts, faces, uvs, vg = [], [], [], {}
    for sh in shells:
        off = len(verts)
        verts += [tuple(p) for p in sh.verts]
        faces += [tuple(i + off for i in f) for f in sh.faces]
        uvs += sh.uv
        vg.setdefault(sh.group or sh.name, []).extend(range(off, off + len(sh.verts)))
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    uvl = me.uv_layers.new(name='UVMap')
    for p in me.polygons:
        for k, li in enumerate(p.loop_indices):
            uvl.data[li].uv = uvs[p.index][k]
    for p in me.polygons:
        p.use_smooth = False
    me.update()
    ob = bpy.data.objects.new(name, me)
    (collection or bpy.context.scene.collection).objects.link(ob)
    ob.data.materials.append(material)
    for g, ids in vg.items():
        ob.vertex_groups.new(name=g).add(ids, 1.0, 'REPLACE')
    return ob


def cards_to_shells(cards):
    """Leaf cards -> one uv Shell per host group (vertex group = host limb, so leaves follow its bones)."""
    by = {}
    for c in cards:
        sh = by.get(c['group'])
        if sh is None:
            sh = by[c['group']] = core.Shell('LEAVES_' + c['group'], c['group'], 'leaves')
            sh.uv = []
        b = len(sh.verts)
        for p in c['co']:
            sh.v(p)
        sh.f(b, b + 1, b + 2, b + 3)
        sh.uv.append(list(c['uv']))
    return list(by.values())


def leaves_object(cards, image_path=LEAVES_PNG, name='MG01_Leaves', collection=None):
    mat = textured_material('M_MG01_Leaves', image_path, alpha_clip=0.5, double_sided=True)
    return uv_object(name, cards_to_shells(cards), mat, collection)


def cloth_object(cloth, image_path=CLOTH_PNG, name='MG01_Cloth', collection=None):
    mat = textured_material('M_MG01_Cloth', image_path)
    return uv_object(name, cloth['shells'], mat, collection)
