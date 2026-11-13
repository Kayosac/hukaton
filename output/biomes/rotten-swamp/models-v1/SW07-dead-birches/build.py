"""SW07 Сухостой-берёзка — dead birch snags, 3 variants.
a: long snapped trunk (leans to the front, two branch stubs on the right)
b: short forked broken trunk
c: long diagonal trunk with sparse dead twigs
Run: python3 build.py [render]"""
import sys, os, math, random, json
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, '/home/claude/swamp/kit')
import sk
import treelib as T
from mathutils import Vector

OUT = os.path.join(HERE, 'out')
CONCEPT = '/home/claude/ref/swamp/sheets/07-dead-birches.png'


def wind_fn(H, amp=0.02):
    def W(p):
        h = max(0.0, p.z) / H
        return (amp * h * h, 0.0)
    return W


def flare_lobes(sides, seed, lo, hi):
    r = random.Random(seed)
    return [r.uniform(lo, hi) if k % 2 == 0 else r.uniform(lo * .8, lo) for k in range(sides)]


def jag_shift(sides, seed, amp):
    r = random.Random(seed)
    return [r.uniform(-amp, amp) for _ in range(sides)]


def surface_roots(bark, base_r, n, seed, W, z=0.02, lens=(0.22, 0.42)):
    rnd = random.Random(seed)
    a0 = rnd.uniform(0, 6.28)
    for i in range(n):
        a = a0 + i * 2 * math.pi / n + rnd.uniform(-.4, .4)
        d = Vector((math.cos(a), math.sin(a), 0))
        L = rnd.uniform(*lens)
        p0 = d * base_r * .3 + Vector((0, 0, z + 0.4))
        p1 = d * (base_r * 1.0) + Vector((0, 0, z + 0.16))
        p2 = d * (base_r * 1.1 + L * .55) + Vector((0, 0, z + 0.0))
        p3 = d * (base_r * 1.1 + L) + Vector((0, 0, z - 0.16))
        rr = base_r * rnd.uniform(.4, .55)
        T.tube(bark, [p0, p1, p2, p3], [rr, rr * .85, rr * .6, rr * .4], sides=5, rot=.6, seed=seed + i,
               tip=p3 + d * 0.06 + Vector((0, 0, -0.12)), wind=W)


def stub(birch, props, start, d, L, r0, seed, W, broken=True, twig=None):
    """Dead branch stub: short tapering tube, ends broken (end grain) or pointed."""
    d = Vector(d).normalized()
    pts = T.crook([start, start + d * L * .5, start + d * L], 0.08, seed, sub=1)
    rings, Lc = T.tube(birch, pts, [r0, r0 * .85, r0 * .75, r0 * .62, r0 * .55], sides=5, seed=seed, wind=W,
                       tip=None if broken else pts[-1] + d * r0 * 2.5)
    if broken:
        ax = (pts[-1] - pts[-2]).normalized()
        T.broken_top(birch, props, rings[-1], ax, Lc[-1], r0 * .55, seed, spikes=(r0 * 1.4,), low=(0.0, r0 * .4),
                     sink=r0 * .2, region='end_grain_grey', wind=W, slope=r0 * .6)
    return pts


def twig(birch, start, d, L, r0, seed, W, forks=2, depth=0):
    rnd = random.Random(seed)
    d = Vector(d).normalized()
    end = start + d * L
    pts = T.crook([start, end], 0.12, seed, sub=2)
    T.tube(birch, pts, [r0 * (1 - .55 * i / 3) for i in range(4)], sides=4 if r0 > 0.025 else 3, rot=.4, seed=seed,
           wind=W, tip=end + d * L * .12)
    if depth < 2:
        for f in range(forks):
            t = rnd.uniform(.35, .8)
            s = T.lerp_path(pts, t)
            side = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(.2, 1)))
            nd = (d + side.normalized() * rnd.uniform(.7, 1.2)).normalized()
            twig(birch, s, nd, L * rnd.uniform(.3, .5), r0 * .55, seed * 7 + f + 1, W, forks=1, depth=depth + 1)


def trunk(birch, bark, props, ctrl, cradii, sides, seed, W, nrings=10, spikes=(0.6, 0.3), rot=0.0,
          kink=0.05, slope=0.25, patch=(0.45, 0.06), shards=(), lobes=5, flare_scale=1.0):
    """Trunk resampled into nrings crooked rings. Faces are Birch or dark peeled Bark patches (probability
    falling from patch[0] at the base to patch[1] at the top -> blocky peeled bark like the concept);
    the base segments are dark Bark and sit inside a soft rounded root-flare collar (treelib.flare) that melts
    into the ground. Splintered broken top + extra tall shards standing on the break rim."""
    path, radii = T.resample(ctrl, cradii, nrings, amp=kink, seed=seed, keep_first=2)
    n = len(path)
    rnd = random.Random(seed + 99)
    pick = {}
    for i in range(n - 1):
        f = i / (n - 2)
        p = patch[0] + (patch[1] - patch[0]) * f
        for k in range(sides):
            pick[(i, k)] = bark if (i < 2 or rnd.random() < p) else birch
    for i in range(2, n - 1):
        for k in range(sides):
            if rnd.random() < 0.25:
                pick[(i, k)] = pick[(i - 1, k)]
    rings, Lc = T.tube(lambda i, k: pick[(i, k)], path, radii, sides=sides, rot=rot, seed=seed,
                       jitter=0.05, caps=(True, False), wind=W)
    sc = flare_scale
    T.flare(bark, (path[0].x, path[0].y, 0.0), radii[1], sides=15, lobes=lobes, seed=seed + 7, wind=W,
            scales=(1.45 * sc, 1.62 * sc, 1.36 * sc, 1.08, 0.84), heights=(-0.24, 0.0, 0.22, 0.48, 0.8),
            amp=(0.2, 0.26, 0.2, 0.08, 0.02),
            tilt=((path[3].x - path[0].x) / max(path[3].z, .1), (path[3].y - path[0].y) / max(path[3].z, .1)))
    ax = (path[-1] - path[-2]).normalized()
    J = T.broken_top(birch, props, rings[-1], ax, Lc[-1], radii[-1], seed + 5, spikes=spikes, low=(0.04, 0.2),
                     sink=0.12, region='grey_wood', wind=W, slope=slope, pinch=0.1)
    ring = rings[-1]
    c = sum(ring, Vector()) / len(ring)
    for j, (L, w) in enumerate(shards):
        q = ring[(j * 2 + 1) % len(ring)]
        q = q + (c - q) * 0.15
        out = (q - c).normalized()
        T.shard(birch, q - ax * 0.05, ax + out * rnd.uniform(.1, .3), L, w, seed + 40 + j, wind=W)
    return path


def finish(vid, birch, bark, props, plants=None, moss_keep=0.3, W=None):
    ob = birch.to_object(vid + '_Birch')
    ok = bark.to_object(vid + '_Bark')
    op = props.to_object(vid + '_Props')
    om = sk.moss_drape(ok, vid + '_Moss', min_nz=0.5, keep=moss_keep, seed=3, offset=0.015)
    if W:
        T.apply_wind(om, W)
    objs = [o for o in (ob, ok, op, om) if o]
    sk.ground(objs, center_xy=False)   # pivot = trunk base centre (catalog: pivot at the base)
    return objs


# ------------------------------------------------------------------ variant a: long snapped trunk
def build_a():
    vid = 'SW07a'
    H = 6.0
    W = wind_fn(H, 0.025)
    birch, bark, props = sk.Mesh('Birch'), sk.Mesh('Bark'), sk.Mesh('Props')
    ctrl = [(0, 0, -0.2), (0, 0, 0.05), (0.03, -0.06, 0.6), (0.12, -0.3, 1.6), (0.26, -0.66, 2.8),
            (0.42, -1.02, 3.95), (0.55, -1.32, 4.9), (0.62, -1.48, 5.35)]
    cr = [0.5, 0.5, 0.45, 0.41, 0.38, 0.35, 0.32, 0.3]
    path = trunk(birch, bark, props, ctrl, cr, 6, 11, W, nrings=11, spikes=(1.15, 0.85, 0.6, 0.4), rot=0.3,
                 slope=0.35, shards=((1.35, 0.07), (0.9, 0.06)))
    # two stubs on the right (+X), the upper one with a broken fork
    stub(birch, props, T.lerp_path(path, .58), (1, 0.05, 0.3), 0.75, 0.12, 21, W)
    p = stub(birch, props, T.lerp_path(path, .77), (1, 0.15, 0.55), 0.95, 0.11, 22, W)
    twig(bark, T.lerp_path(p, .6), (0.5, -0.3, 1.0), 0.35, 0.04, 23, W, forks=0, depth=2)
    # small stubs on the back-left
    stub(birch, props, T.lerp_path(path, .68), (-0.5, 1, 0.35), 0.35, 0.08, 24, W)
    stub(birch, props, T.lerp_path(path, .42), (-0.9, -0.5, 0.2), 0.25, 0.07, 25, W)
    return vid, 'LongSnag', finish(vid, birch, bark, props, W=W)


# ------------------------------------------------------------------ variant b: short forked broken trunk
def build_b():
    vid = 'SW07b'
    H = 3.0
    W = wind_fn(H, 0.012)
    birch, bark, props = sk.Mesh('Birch'), sk.Mesh('Bark'), sk.Mesh('Props')
    # main (tall) stem to front-right, broken high
    ctrl = [(0, 0, -0.2), (0, 0, 0.05), (0.02, 0.0, 0.5), (0.08, -0.05, 1.1), (0.16, -0.1, 1.7),
            (0.24, -0.18, 2.3), (0.28, -0.22, 2.65)]
    cr = [0.6, 0.6, 0.53, 0.46, 0.39, 0.35, 0.33]
    path = trunk(birch, bark, props, ctrl, cr, 7, 31, W, nrings=8, spikes=(0.85, 0.6, 0.4), rot=0.2,
                 slope=0.25, patch=(0.55, 0.12), shards=((0.95, 0.07), (0.6, 0.06)), lobes=6)
    # short fork to back-left, also snapped
    f0 = T.lerp_path(path, .36)
    fctrl = [f0, f0 + Vector((-0.18, 0.1, 0.32)), f0 + Vector((-0.42, 0.2, 0.72)), f0 + Vector((-0.6, 0.28, 1.05)),
             f0 + Vector((-0.68, 0.32, 1.22))]
    fpath, fr = T.resample(fctrl, [0.33, 0.3, 0.28, 0.26, 0.25], 6, amp=0.03, seed=33, keep_first=1)
    rnd = random.Random(36)
    rings, Lc = T.tube(lambda i, k: bark if rnd.random() < 0.2 else birch, fpath, fr, sides=6, seed=33,
                       jitter=0.05, wind=W, rot=.5)
    ax = (fpath[-1] - fpath[-2]).normalized()
    T.broken_top(birch, props, rings[-1], ax, Lc[-1], fr[-1], 34, spikes=(0.6, 0.38, 0.22), low=(0.03, 0.14),
                 sink=0.08, region='grey_wood', wind=W, slope=0.2, pinch=0.1)
    q = rings[-1][1]
    T.shard(birch, q + (fpath[-1] - q) * 0.15 - ax * 0.05, ax, 0.7, 0.06, 37, wind=W)
    stub(birch, props, T.lerp_path(path, .72), (0.9, 0.5, 0.3), 0.3, 0.1, 35, W)
    return vid, 'ForkSnag', finish(vid, birch, bark, props, W=W)


# ------------------------------------------------------------------ variant c: diagonal trunk, sparse twigs
def build_c():
    vid = 'SW07c'
    H = 4.3
    W = wind_fn(H, 0.03)
    birch, bark, props = sk.Mesh('Birch'), sk.Mesh('Bark'), sk.Mesh('Props')
    ctrl = [(0, 0, -0.2), (0, 0, 0.05), (0.06, 0.0, 0.6), (0.3, 0.02, 1.3), (0.8, 0.04, 1.95),
            (1.4, 0.06, 2.5), (2.0, 0.1, 2.95), (2.6, 0.1, 3.3), (3.1, 0.13, 3.55)]
    cr = [0.47, 0.47, 0.41, 0.36, 0.32, 0.29, 0.27, 0.25, 0.24]
    path = trunk(birch, bark, props, ctrl, cr, 6, 41, W, nrings=12, spikes=(0.75, 0.5, 0.3), rot=0.1, slope=0.25,
                 shards=((0.85, 0.06),))
    # tall twig rising from the middle of the trunk (front view: up, slightly left)
    twig(bark, T.lerp_path(path, .48) + Vector((0, 0, 0.16)), (-0.05, -0.15, 1), 1.4, 0.07, 42, W, forks=3)
    # low twig to the left
    twig(bark, T.lerp_path(path, .27) + Vector((-0.1, 0, 0)), (-0.8, -0.2, 0.8), 0.85, 0.055, 43, W, forks=2)
    # twig near the top going up-right
    twig(bark, T.lerp_path(path, .86) + Vector((0, 0, -0.05)), (1, -0.2, 0.1), 0.85, 0.05, 44, W, forks=1)
    # twig to the side (+Y, seen right in the side view)
    twig(bark, T.lerp_path(path, .64), (0.2, 1, 0.6), 1.0, 0.055, 45, W, forks=2)
    stub(birch, props, T.lerp_path(path, .38), (0.1, -1, 0.3), 0.35, 0.1, 46, W)
    return vid, 'DiagonalSnag', finish(vid, birch, bark, props, W=W)


VARIANTS = [build_a, build_b, build_c]
VIEWS = [(-35, 10, 'front 3/4'), (-145, 10, 'rear 3/4'), (90, 8, 'side')]


def main(render=True):
    sk.reset()
    recs, tiles, labels = [], [], []
    built = []
    for f in VARIANTS:
        vid, name, objs = f()
        built.append((vid, name, objs))
    # lay variants side by side for the .blend (geometry is grounded at origin; offset via object location)
    for i, (vid, name, objs) in enumerate(built):
        fbx = os.path.join(OUT, 'SM_SW07_%s.fbx' % name)
        sk.export_fbx(objs, fbx)
        rec = sk.variant_record(vid, name, objs, fbx, water_line_z=None, ground_line_z=0.24,
                                solidity='solid', burns=True,
                                collider='capsule/simplified mesh around trunk (+fork for b); twigs and stubs no collider',
                                moving_parts=None,
                                notes='Pivot at the bottom of the root flare; the soft root-flare collar goes 0.24 m below '
                                      'the ground line (sink when placing). Leafless dead snag. Material per object: '
                                      'Birch (upper trunk, stubs, twigs), Bark (dark old base sleeve + surface roots), '
                                      'Props (splintered heartwood of broken top: grey_wood; stub ends: end_grain_grey), '
                                      'Moss (drape on the root flare). Tiny trunk sway in Wind UV2.')
        recs.append(rec)
        print(vid, name, rec['tris'], rec['tris_by_material'], rec['size_xyz_m'])
        if render:
            for az, el, lab in VIEWS:
                png = os.path.join(HERE, 'work', '%s_%d.png' % (vid, az))
                sk.render(objs, png, az=az, el=el, size=(360, 360), samples=10)
                tiles.append(png); labels.append('%s %s %s az%d' % (vid, name, lab, az))
    for i, (vid, name, objs) in enumerate(built):
        for o in objs:
            o.location.x = i * 3.5
    sk.save_blend(os.path.join(OUT, 'SW07_dead_birches.blend'))
    json.dump(recs, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)
    if render:
        sk.sheet(tiles, os.path.join(OUT, 'card.jpg'), 3, labels=labels, concept=CONCEPT,
                 title='SW07 dead birches (Suhostoi-berezka): concept (top) vs model (front 3/4, rear 3/4, side)')


if __name__ == '__main__':
    main(render='norender' not in sys.argv)
