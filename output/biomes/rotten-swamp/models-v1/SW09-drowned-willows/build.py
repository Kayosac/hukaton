"""SW09 Ива-утопленница — drowned willows, 2 variants.
a: thick trunk bending into one heavy arching limb (left), long strand curtains to the water
b: squat low fork with two thick limbs and two hanging masses (big left, smaller right)
Run: python3 build.py [norender]"""
import sys, os, math, random, json
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, '/home/claude/swamp/kit')
import sk
import treelib as T
from mathutils import Vector, Matrix

OUT = os.path.join(HERE, 'out')
CONCEPT = '/home/claude/ref/swamp/sheets/09-drowned-willows.png'
WATER = 0.4   # water line above the pivot


def make_wind(H, trunk_amp, crown_amp):
    def Wb(p):
        h = max(0.0, p.z - 1.0) / max(H - 1.0, 1)
        return (trunk_amp * h * h, 0.0)

    def Wc(p):
        h = max(0.0, p.z - 1.0) / max(H - 1.0, 1)
        return (crown_amp * h * h, 0.0)
    return Wb, Wc


def knee_roots(bk, bm, base, trunk_r, n, seed, Wb, reach=(0.9, 2.4), z0=(1.0, 1.8)):
    """5-7 thick organic willow roots: they leave the trunk base high up, arch up/outwards, then plunge steeply
    into the water; irregular spacing and lengths, about half of them fork near the knee."""
    rnd = random.Random(seed)
    a = rnd.uniform(0, 6.28)
    base = Vector(base)
    for i in range(n):
        a += 2 * math.pi / n * rnd.uniform(.6, 1.4)
        d = Vector((math.cos(a), math.sin(a), 0))
        side = Vector((-d.y, d.x, 0))
        R = rnd.uniform(*reach)
        zs = rnd.uniform(*z0)
        arch = rnd.uniform(.1, .35)
        r = trunk_r * rnd.uniform(.4, .56)
        wob = side * rnd.uniform(-.25, .25)
        ctrl = [base + d * trunk_r * .2 + Vector((0, 0, zs + .45)),
                base + d * trunk_r * .85 + Vector((0, 0, zs)),
                base + d * (trunk_r + R * .38) + wob + Vector((0, 0, zs * .72 + arch)),
                base + d * (trunk_r + R * .72) + wob * 1.5 + Vector((0, 0, zs * .5)),
                base + d * (trunk_r + R * .92) + wob * 1.6 + Vector((0, 0, zs * .18)),
                base + d * (trunk_r + R) + wob * 1.6 + Vector((0, 0, -0.05))]
        rr = [r * 1.3, r * 1.1, r * .9, r * .78, r * .74, r * .85]
        path, rad = T.resample(ctrl, rr, 8, amp=0.06, seed=seed + i, keep_first=1)
        T.tube(lambda ii, k: bm if (ii < 5 and k in (0, 1, 4)) else bk, path, rad, sides=6 if r > .3 else 5,
               rot=rnd.uniform(0, 1), seed=seed + i, jitter=0.06, wind=Wb, caps=(False, True),
               lobes={len(path) - 1: [rnd.uniform(1.0, 1.35) for _ in range(6)]})
        if rnd.random() < 0.55:
            s0 = T.lerp_path(path, rnd.uniform(.45, .6))
            sd = (d * .6 + side * rnd.choice((-1, 1))).normalized()
            L = rnd.uniform(.5, 1.0)
            sp = [s0, s0 + sd * L * .45 + Vector((0, 0, .05)), s0 + sd * L * .85 + Vector((0, 0, -s0.z * .55)),
                  s0 + sd * L + Vector((0, 0, -s0.z - .05))]
            fr = r * rnd.uniform(.5, .65)
            T.tube(bk, sp, [fr, fr * .85, fr * .75, fr * .9], sides=5, seed=seed + 50 + i, jitter=0.05, wind=Wb,
                   caps=(False, True))


def limb(bk, pts, r0, r1, seed, Wb, sides=5, n=None, amp=0.05, mfn=None, tip=True):
    pts = [Vector(p) for p in pts]
    rr = [r0 + (r1 - r0) * i / (len(pts) - 1) for i in range(len(pts))]
    if n:
        pts, rr = T.resample(pts, rr, n, amp=amp, seed=seed, keep_first=1)
    T.tube(mfn or bk, pts, rr, sides=sides, seed=seed, jitter=0.04, wind=Wb, caps=(False, False),
           tip=(pts[-1] + (pts[-1] - pts[-2]).normalized() * r1 * 3) if tip else None)
    return pts


def bottom_fn(trunk_pts, near=(1.0, 3.6), far=(3.2, 1.0)):
    """Lowest strand tip height by horizontal distance from the trunk: strands near the trunk stay high
    (trunk stays visible like on the concept), far ones reach down towards the water."""
    T2 = [Vector((p.x, p.y, 0)) for p in trunk_pts]

    def f(p):
        q = Vector((p.x, p.y, 0))
        d = min((q - t).length for t in T2)
        s = min(1.0, max(0.0, (d - near[0]) / (far[0] - near[0])))
        s = s * s * (3 - 2 * s)
        return near[1] + (far[1] - near[1]) * s
    return f


def drape_card(pl, top, d, R, drop, w, region, Wc, segs=3):
    """Umbrella card: from the dome top it runs outward first, then bends down (rounded willow head)."""
    u0, v0, u1, v1 = pl.region(region, pl.mat)
    top, d = Vector(top), Vector(d)
    side = Vector((-d.y, d.x, 0)) * w
    rows = []
    for s in range(segs + 1):
        t = s / segs
        rows.append((top + d * R * math.sin(t * math.pi / 2) - Vector((0, 0, drop * t * t)), t))
    s0 = Wc(top)[0]
    for s in range(segs):
        (pa, ta), (pb, tb) = rows[s], rows[s + 1]
        va, vb = v1 - (v1 - v0) * ta, v1 - (v1 - v0) * tb
        wa, wb = (s0 + 0.04 * ta * ta, 0.05 * ta), (s0 + 0.04 * tb * tb, 0.05 * tb)
        pl.quad(pa - side, pa + side, pb + side, pb - side, (u0, va), (u1, va), (u1, vb), (u0, vb), [wa, wa, wb, wb])


def mass(pl, c, R, top, clusters, seed, Wc, bot, flare=0.3, bias=None, inner=0.2, heads=11):
    """Big rounded strand mass made of chunky tassels (4-6 willow_strands cards each, packed tight) hung from a
    dome (centre c, radius R, dome height `top`). Tassels sit mostly on the dome rim (curtain shell, cards
    tangent so they show from every side), some inside; each tassel gets its own length so the bottom edge is
    ragged at different heights."""
    rnd = random.Random(seed)
    c = Vector(c)
    regs = ('willow_strands_0', 'willow_strands_1')
    a0 = rnd.uniform(0, 6.28)
    for i in range(clusters):
        a = a0 + i * 2 * math.pi / clusters + rnd.uniform(-.2, .2)
        if bias is not None and rnd.random() < bias[1]:
            a = math.atan2(bias[0].y, bias[0].x) + rnd.uniform(-1.1, 1.1)
        f = rnd.uniform(.3, .6) if rnd.random() < inner else rnd.uniform(.66, 1.0)
        lf = rnd.uniform(.5, 1.0)                     # tassel length factor -> ragged bottom edge
        ncard = rnd.choice((4, 5, 5, 6))
        for j in range(ncard):
            fj = min(1.02, f + rnd.uniform(-.06, .06))
            aj = a + rnd.uniform(-.09, .09)
            dj = Vector((math.cos(aj), math.sin(aj), 0))
            tp = c + dj * R * fj
            tp.z = c.z + top * math.sqrt(max(0.0, 1 - fj * fj * .8)) + rnd.uniform(-.1, .08) - j * 0.04
            bz = bot(tp) + (1 - fj) * 0.8
            L = max(0.7, (tp.z - bz) * lf * rnd.uniform(.88, 1.0))
            w = rnd.uniform(.38, .52)
            yaw = aj + math.pi / 2 + rnd.uniform(-.6, .6)
            right = Vector((math.cos(yaw), math.sin(yaw), 0)) * w
            T.hang_card(pl, tp, right, L, rnd.choice(regs), out=dj * L * flare * fj * rnd.uniform(.5, 1.0),
                        segs=3 if L > 1.6 else 2, sway0=Wc(tp)[0], sway=0.08 + 0.03 * L, flutter=0.05)
    # rounded head: umbrella cards from the dome top outwards and down over the rim
    for h in range(heads):
        a = a0 + 0.4 + h * 2 * math.pi / heads + rnd.uniform(-.3, .3)
        d = Vector((math.cos(a), math.sin(a), 0))
        drape_card(pl, c + Vector((0, 0, top + 0.18)) + d * R * .05, d, R * rnd.uniform(.85, 1.05),
                   top * rnd.uniform(1.4, 2.0), rnd.uniform(.42, .55), rnd.choice(regs), Wc)


def finish(vid, bk, bm, pl, Wb=None):
    ob = bk.to_object(vid + '_Bark')
    om_ = bm.to_object(vid + '_BarkMossy')
    moss = sk.moss_drape(om_, vid + '_Moss', min_nz=0.4, keep=0.75, seed=4, offset=0.02, drips=pl)
    op = pl.to_object(vid + '_Plants')
    if Wb:
        T.apply_wind(moss, Wb)
        T.apply_wind(op, Wb, only_unset=True)   # moss drip cards move with the bark they hang from
    return [o for o in (ob, om_, moss, op) if o]


def ground_at(objs, base, yaw_deg=0.0):
    """Rotate the whole tree about the trunk base (so the heavy side faces the way the concept views show it),
    then put the pivot at the trunk base centre, lowest point z=0."""
    R = Matrix.Translation(Vector(base)) @ Matrix.Rotation(math.radians(yaw_deg), 4, 'Z') @ \
        Matrix.Translation(-Vector(base))
    for o in objs:
        o.data.transform(R)
    lo, hi = sk.bounds(objs)
    d = Vector((-base[0], -base[1], -lo[2]))
    for o in objs:
        o.data.transform(Matrix.Translation(d))


# ------------------------------------------------------------------ variant a: one heavy arch
def build_a():
    vid = 'SW09a'
    H = 7.0
    Wb, Wc = make_wind(H, 0.04, 0.09)
    bk, bm, pl = sk.Mesh('Bark'), sk.Mesh('BarkMossy'), sk.Mesh('Plants')
    rnd = random.Random(5)
    base = Vector((0, 0, 0))
    # trunk: rises, leans and bends into the heavy arch towards -X/-Y; mossy on its upper side
    ctrl = [(0, 0, -0.1), (0.0, 0.0, 0.5), (0.45, 0.22, 1.5), (0.42, 0.2, 2.5), (-0.35, -0.18, 3.45),
            (-1.2, -0.58, 4.05), (-1.9, -0.92, 4.45), (-2.6, -1.25, 4.6), (-3.15, -1.52, 4.45), (-3.5, -1.7, 4.15)]
    cr = [1.15, 1.05, 0.93, 0.83, 0.74, 0.64, 0.53, 0.41, 0.3, 0.19]
    path, rad = T.resample(ctrl, cr, 14, amp=0.06, seed=5, keep_first=2)
    lobes = {0: [1.35, 1.0, 1.4, 1.1, 1.3, 1.0], 1: [1.2, 1.05, 1.25, 1.0, 1.2, 1.05],
             2: [1.1, 1.0, 1.12, 1.0, 1.1, 1.0]}
    # BarkMossy on the upper side faces (k 0/1/5 ~ up for the leaning part) and the lower trunk
    tw = 0.1   # twist of the facets along the trunk
    up = lambda i, k: (k + int(round(i * tw / (2 * math.pi / 6)))) % 6 in (0, 1, 5)
    T.tube(lambda i, k: bm if (i < 3 or up(i, k) or rnd.random() < .2) else bk, path, rad, sides=6, twist=tw,
           rot=0.0, lobes=lobes, seed=5, jitter=0.05, wind=Wb, caps=(True, False),
           tip=path[-1] + (path[-1] - path[-2]).normalized() * 0.35, up_hint=(0, 0, 1))
    knee_roots(bk, bm, base, 1.05, 7, 6, Wb, reach=(1.0, 2.5), z0=(1.1, 1.9))
    # upward branch at the bend (to the top-right crown), with forks
    b1 = limb(bk, [T.lerp_path(path, .33), (-0.35, -0.05, 4.6), (-0.05, 0.15, 5.6), (0.35, 0.3, 6.3)],
              0.38, 0.14, 11, Wb, n=6)
    limb(bk, [T.lerp_path(b1, .7), (0.9, 0.2, 6.2), (1.5, 0.05, 6.05)], 0.12, 0.06, 12, Wb, n=4, sides=4)
    limb(bk, [T.lerp_path(b1, .8), (0.1, 0.7, 6.6), (-0.2, 1.0, 6.75)], 0.1, 0.05, 13, Wb, n=4, sides=4)
    # branch from the arch rising to the upper-left mass
    b2 = limb(bk, [T.lerp_path(path, .62), (-1.75, -0.6, 5.2), (-1.6, -0.3, 6.0), (-1.3, -0.15, 6.45)],
              0.28, 0.09, 14, Wb, n=5)
    # branch to the back (+Y) for the rear curtain
    b3 = limb(bk, [T.lerp_path(path, .4), (-0.7, 0.4, 4.6), (-0.55, 1.1, 5.3), (-0.45, 1.6, 5.6)],
              0.28, 0.09, 15, Wb, n=5)
    # small branch towards the front from the arch
    b4 = limb(bk, [T.lerp_path(path, .55), (-1.3, -1.1, 5.0), (-1.05, -1.45, 5.45)], 0.17, 0.07, 16, Wb, n=4,
              sides=4)
    # strand masses: (centre, R, dome height, clusters)
    bot = bottom_fn(path[:int(len(path) * .72)], near=(1.0, 4.9), far=(2.4, 1.0))
    mass(pl, (-3.05, -1.5, 4.95), 1.55, 0.85, 20, 101, Wc, bot, bias=(Vector((-1, -.5, 0)), .35))
    mass(pl, (-1.55, -0.4, 6.25), 1.3, 0.8, 15, 102, Wc, bot)
    mass(pl, (0.6, 0.25, 6.35), 1.3, 0.75, 15, 103, Wc, bot, bias=(Vector((1, 0, 0)), .3))
    mass(pl, (-0.5, 1.55, 5.5), 1.2, 0.7, 13, 105, Wc, bot)
    objs = finish(vid, bk, bm, pl, Wb)
    ground_at(objs, base, -61.0)   # heavy arch -> (-0.82, +0.57): left in the front 3/4, right in the rear 3/4
    return vid, 'DrownedWillowArch', objs


# ------------------------------------------------------------------ variant b: squat low fork, two masses
def build_b():
    vid = 'SW09b'
    H = 5.5
    Wb, Wc = make_wind(H, 0.035, 0.08)
    bk, bm, pl = sk.Mesh('Bark'), sk.Mesh('BarkMossy'), sk.Mesh('Plants')
    rnd = random.Random(8)
    base = Vector((0, 0, 0))
    # squat trunk to the fork
    ctrl = [(0, 0, -0.1), (0, 0, 0.5), (0.05, 0.0, 1.2), (0.0, -0.05, 1.8), (-0.1, -0.05, 2.2)]
    cr = [1.15, 1.02, 0.9, 0.84, 0.8]
    path, rad = T.resample(ctrl, cr, 6, amp=0.04, seed=8, keep_first=2)
    lobes = {0: [1.35, 1.0, 1.4, 1.05, 1.3, 1.0], 1: [1.2, 1.05, 1.25, 1.0, 1.2, 1.05],
             2: [1.08, 1.0, 1.1, 1.0, 1.08, 1.0]}
    T.tube(lambda i, k: bm if (i < 2 or k in (0, 1) or rnd.random() < .25) else bk, path, rad, sides=6, rot=0.3, twist=0.15,
           lobes=lobes, seed=8, jitter=0.05, wind=Wb, caps=(True, True))
    knee_roots(bk, bm, base, 1.0, 6, 9, Wb, reach=(1.0, 2.3), z0=(0.9, 1.6))
    # big left limb: arcs out to -X (slightly -Y), heavy
    fork = path[-2]
    L1 = [fork + Vector((-0.38, -0.1, -0.1)), (-0.55, -0.15, 2.75), (-1.25, -0.35, 3.45), (-2.0, -0.55, 3.95),
          (-2.7, -0.75, 4.15), (-3.25, -0.9, 4.0)]
    p1 = limb(bm, L1, 0.7, 0.2, 21, Wb, n=9, sides=6,
              mfn=lambda i, k: bm if (k in (0, 1, 5) or i < 2) else bk)
    # right limb: rises up-right (+X, +Y)
    L2 = [fork + Vector((0.38, 0.1, -0.1)), (0.45, 0.15, 2.85), (0.9, 0.3, 3.6), (1.35, 0.42, 4.4), (1.6, 0.5, 4.9)]
    p2 = limb(bm, L2, 0.6, 0.17, 22, Wb, n=8, sides=6,
              mfn=lambda i, k: bm if (k in (0, 1, 5) or i < 2) else bk)
    # side branches
    b = limb(bk, [T.lerp_path(p1, .45), (-1.3, -0.1, 4.3), (-1.0, 0.1, 4.9)], 0.17, 0.07, 23, Wb, n=4)
    limb(bk, [T.lerp_path(p1, .75), (-2.6, -1.3, 4.5), (-2.4, -1.6, 4.8)], 0.13, 0.06, 24, Wb, n=4, sides=4)
    limb(bk, [T.lerp_path(p2, .7), (1.9, 0.1, 4.7), (2.35, 0.0, 4.8)], 0.12, 0.05, 25, Wb, n=4, sides=4)
    limb(bk, [T.lerp_path(p2, .55), (0.8, 0.95, 4.4), (0.6, 1.3, 4.7)], 0.12, 0.05, 26, Wb, n=4, sides=4)
    # strand masses: big left curtain to the water, smaller right one; the fork stays visible between them
    bot = bottom_fn([p for p in path] + [Vector(q) for q in ((-0.6, -0.15, 2.7), (0.45, 0.15, 2.8))],
                    near=(1.1, 3.4), far=(2.6, 0.8))
    mass(pl, (-2.3, -0.55, 4.55), 1.75, 0.8, 24, 201, Wc, bot, bias=(Vector((-1, -.3, 0)), .3))
    mass(pl, (1.6, 0.4, 5.05), 1.4, 0.65, 17, 203, Wc, bot, bias=(Vector((1, .3, 0)), .3))
    objs = finish(vid, bk, bm, pl, Wb)
    ground_at(objs, base, -50.0)
    return vid, 'DrownedWillowFork', objs


VARIANTS = [build_a, build_b]
VIEWS = [(-35, 12, 'front 3/4', None), (145, 12, 'rear 3/4', None), (-90, 10, 'side', None),
         (-30, 18, 'in water', WATER)]


def main(render=True):
    sk.reset()
    recs, tiles, labels, built = [], [], [], []
    for f in VARIANTS:
        built.append(f())
    for vid, name, objs in built:
        fbx = os.path.join(OUT, 'SM_SW09_%s.fbx' % name)
        sk.export_fbx(objs, fbx)
        rec = sk.variant_record(vid, name, objs, fbx, water_line_z=WATER, solidity='solid (trunk+branches); '
                                'strands soft-passable', burns=True,
                                collider='trunk + big limbs + roots solid (capsules / simplified mesh of Bark and '
                                         'BarkMossy objects); Plants (strand curtains, moss drips) no collider',
                                moving_parts=None,
                                notes='Pivot = trunk base centre at the bottom of the roots (roots go %.2f m under the '
                                      'water line). Objects: Bark, BarkMossy (trunk/limb upper sides, root tops), '
                                      'Moss (drape on upward faces), Plants (willow_strands_0/1 hang cards in layered '
                                      'curtain shells + a few moss_drip cards). Wind UV2: bark sway up to ~0.04 m, '
                                      'strands sway from the hang point (= branch sway) growing to the tips + '
                                      'flutter 0.05 m.' % WATER)
        recs.append(rec)
        print(vid, name, rec['tris'], rec['tris_by_material'], rec['size_xyz_m'])
        if render:
            for az, el, lab, wz in VIEWS:
                png = os.path.join(HERE, 'work', '%s_%d_%s.png' % (vid, az, 'w' if wz else 'd'))
                sk.render(objs, png, az=az, el=el, size=(340, 340), samples=10, water_z=wz, margin=0.95)
                tiles.append(png); labels.append('%s %s %s az%d' % (vid, name, lab, az))
    for i, (vid, name, objs) in enumerate(built):
        for o in objs:
            o.location.x = i * 9.0
    sk.save_blend(os.path.join(OUT, 'SW09_drowned_willows.blend'))
    json.dump(recs, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)
    if render:
        sk.sheet(tiles, os.path.join(OUT, 'card.jpg'), 4, labels=labels, concept=CONCEPT,
                 title='SW09 drowned willows (Iva-utoplennica): concept (top) vs model (front 3/4, rear 3/4, side, in water)')


if __name__ == '__main__':
    main(render='norender' not in sys.argv)
