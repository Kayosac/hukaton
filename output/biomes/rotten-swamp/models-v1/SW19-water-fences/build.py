"""SW19 Забор из воды (fences standing in water) - 3 variants.
Pivot: LEFT joint at the base = bottom of the left post (0,0,0); the fence runs along +X, front faces -Y.
Posts are full length (lower ~0.4 m under the water line), water line z=0.42."""
import sys, math, random, os, json
sys.path.insert(0, '/home/claude/swamp/kit'); sys.path.insert(0, '/home/claude/swamp/items/_pathsfences')
import sk, pflib as pf
from mathutils import Vector as V
import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
SHEET = '/home/claude/ref/swamp/sheets/19-water-fences.png'
WATER = 0.42


def M():
    return {'Planks': sk.Mesh('Planks'), 'Bark': sk.Mesh('Bark'), 'Props': sk.Mesh('Props'), 'Moss': sk.Mesh('Moss'),
            'Plants': sk.Mesh('Plants')}


def post(ms, base, top, hw, hd, seed, kink=(0, 0), top_end=None, mat='Planks', sides=4, moss=True):
    """Hewn post from base to top (crooked). hw/hd half width/depth."""
    rnd = random.Random(seed)
    b, t = V(base), V(top)
    pts = []
    S = (0, 0.15, 0.4, 0.7, 1.0)
    for i, s in enumerate(S):
        p = b.lerp(t, s)
        if 0 < i < len(S) - 1:
            p += V((kink[0] * math.sin(math.pi * s) + rnd.uniform(-.012, .012), kink[1] * math.sin(math.pi * s) + rnd.uniform(-.012, .012), 0))
        pts.append(p)
    r = hw * 1.414
    radii = [r * 1.08, r * 1.04, r, r * .97, r * .93]
    sc = [(hd / hw, 1.0)] * len(pts)
    te = top_end or rnd.choice(('cut', 'broken'))
    rings = pf.beam(ms[mat], pts, radii, sides=sides, rot=math.pi / sides + rnd.uniform(-.12, .12), scale=sc, ends=('cut', te),
                    props=ms['Props'], cap_region='end_grain_1', seed=seed, broken_len=0.08, up_hint=(0, -1, 0))
    if moss:
        k = max(i for i, p in enumerate(pts) if p.z < WATER + 0.12) + 1
        pf.sleeve(ms['Moss'], rings, min(k + 1, len(pts) - 1), grow=0.011, seed=seed + 1, plants=ms['Plants'], cover=0.45, drips=1)
    return pts


def rail(ms, a, b, seed, h=0.13, th=0.055, ends=('cut', 'cut'), sag=0.02, segs=4, jag=0.15, front=True):
    """Broad hewn rail nailed on the posts (vertical board). a, b: end centre points."""
    a, b = V(a), V(b)
    c = (a + b) / 2
    d = b - a
    # across = up (rail board stands on edge); normal then points to -Y (front)
    rnd = random.Random(seed)
    return pf.plank(ms['Planks'], c, d, (0, 0, 1), d.length, h, th, ends=ends, sag=rnd.uniform(-0.02, 0.02),
                    twist=rnd.uniform(-0.12, 0.12), crook=-sag + rnd.uniform(-0.01, 0.01),
                    segs=segs, seed=seed, jag_len=jag, wvar=0.1)


def rail_moss(ms, a, b, seed, front_y=-0.072, n=3):
    """Moss clumps on top of a rail + drips hanging from it."""
    rnd = random.Random(seed)
    A, B = V(a), V(b)
    for k in range(n):
        if rnd.random() < 0.3:
            continue
        t0 = (k + rnd.uniform(0, .4)) / n; t1 = (k + 1 - rnd.uniform(0, .3)) / n
        p0, p1 = A.lerp(B, t0), A.lerp(B, t1)
        pf.drips_along(ms['Plants'], p0 + V((0, front_y, 0)), p1 + V((0, front_y, 0)), seed + k, n=1, hmin=0.12, hmax=0.26)


def tufts(ms, pts, seed, base_z=WATER - 0.08):
    for i, (x, y) in enumerate(pts):
        h = random.Random(seed + i).uniform(0.35, 0.55)
        pf.tuft(ms['Plants'], (x, y, base_z), h, h * 0.8, seed + i * 5)


def drape(seed):
    return [('Planks', 0.7, 0.55, seed), ('Bark', 0.8, 0.07, seed + 1)]


# ------------------------------------------------------------------------------------------------
def two_rails(vid='SW19a'):
    ms = M()
    # left post leans left/back, right post leans right; posts 1.5 m long
    P1 = post(ms, (0, 0, 0), (-0.16, 0.0, 1.48), 0.125, 0.11, 1, kink=(0.03, 0))
    P2 = post(ms, (2.3, 0.02, 0), (2.38, -0.03, 1.28), 0.12, 0.108, 2, kink=(-0.02, 0.02), top_end='cut')
    fy = -0.11 - 0.066       # rails nailed on the front faces of the posts
    # upper rail: overhangs both posts, drops to the right
    zl, zr = 1.22, 1.06
    a = V((-0.35, fy, zl + 0.03)); b = V((2.62, fy - 0.01, zr - 0.02))
    rail(ms, a, b, 10, h=0.22, th=0.13, sag=0.07, segs=6)
    # lower rail: from the left post, broken short of the right post end (splintered end overhangs right post)
    a2 = V((-0.08, fy, 0.72)); b2 = V((2.5, fy + 0.005, 0.66))
    rail(ms, a2, b2, 11, h=0.2, th=0.12, ends=('cut', 'broken'), sag=0.08, segs=6, jag=0.25)
    rail_moss(ms, a + V((0, 0, 0.11)), b + V((0, 0, 0.09)), 20, n=5)
    rail_moss(ms, a2 + V((0, 0, 0.1)), b2 + V((0, 0, 0.1)), 30, n=4)
    tufts(ms, [(-0.4, 0.05), (2.7, 0.12), (1.3, 0.3)], 40)
    return pf.finish(vid, ms, drape(5))


def wattle(vid='SW19b'):
    ms = M(); rnd = random.Random(19)
    P1 = post(ms, (0, 0, 0), (-0.06, 0.03, 1.34), 0.125, 0.115, 3, kink=(0.02, 0), top_end='cut')
    P2 = post(ms, (2.0, 0, 0), (2.07, 0.02, 1.38), 0.12, 0.112, 4, kink=(-0.02, 0.01), top_end='broken')
    # thin round stakes (zales) the withies are woven around
    stakes = [(0.0, 0.0), (0.52, 0.01), (1.02, -0.01), (1.5, 0.0), (2.0, 0.0)]
    sticks = []
    for j, (x, y) in enumerate(stakes[1:-1]):
        top = 1.18 + rnd.uniform(-0.06, 0.08)
        lean = rnd.uniform(-0.05, 0.05)
        pts = [V((x, y, 0)), V((x + lean * 0.4, y, 0.5)), V((x + lean, y + 0.01, top))]
        pf.beam(ms['Bark'], pts, [0.04, 0.036, 0.032], sides=5, ends=('cut', 'broken' if j != 1 else 'cut'),
                props=ms['Props'], seed=50 + j, broken_len=0.06)
        sticks.append(pts)
    # withies: horizontal rods woven in and out of the stakes; hole on the right half (rows 3..7 broken)
    rows = 11
    amps = [0.17, 0.09, 0.09, 0.09, 0.17]
    hole_x = (1.12, 1.82)
    for r in range(rows):
        z = 0.28 + r * 0.086 + rnd.uniform(-0.01, 0.01)
        phase = r % 2
        xs = [s[0] for s in stakes]
        pts = []
        x0 = -0.12 - rnd.uniform(0, 0.08); x1 = 2.1 + rnd.uniform(0, 0.1)
        # sample the weave: +-y at stakes alternating, 0 between
        samples = [x0] + [xs[0] + (xs[-1] - xs[0]) * q / 12 for q in range(13)] + [x1]
        for x in samples:
            # which stake interval
            ph = 0
            for k in range(len(xs) - 1):
                if xs[k] <= x <= xs[k + 1]:
                    t = (x - xs[k]) / (xs[k + 1] - xs[k])
                    s0 = 1 if (k + phase) % 2 == 0 else -1
                    amp = amps[k] * (1 - t) + amps[k + 1] * t
                    ph = s0 * math.cos(math.pi * t) * amp
                    break
            sag = 0.0
            if 0.95 < x < 1.95 and 2 <= r <= 7:
                sag = 0.03 * math.sin(math.pi * (x - 0.95))
            pts.append(V((x, ph, z - sag + rnd.uniform(-0.006, 0.006))))
        rad = 0.056 + rnd.uniform(-0.005, 0.006)
        if 3 <= r <= 7:
            # cut by the hole: a left piece ending in a splinter, a right stub
            hx0 = hole_x[0] + rnd.uniform(-0.12, 0.1) + (0.12 if r in (3, 7) else 0)
            hx1 = hole_x[1] + rnd.uniform(-0.1, 0.12) - (0.15 if r in (3, 7) else 0)
            left = [p for p in pts if p.x < hx0] + [V((hx0, 0.0, pts[0].z - 0.02 * (r % 3)))]
            right = [V((hx1, 0.0, pts[-1].z + 0.03 * ((r + 1) % 2)))] + [p for p in pts if p.x > hx1]
            if r in (4, 6):
                # dangling end bent down into the hole
                left[-1] = left[-1] + V((0.05, -0.04, -0.12))
            pf.beam(ms['Bark'], left, rad, sides=4, ends=('cut', 'broken'), props=ms['Props'], seed=70 + r, broken_len=0.07,
                    up_hint=(0, -1, 0), scale=[(0.6, 1.15)] * len(left))
            if len(right) >= 2:
                pf.beam(ms['Bark'], right, rad, sides=4, ends=('broken', 'cut'), props=ms['Props'], seed=90 + r,
                        broken_len=0.07, up_hint=(0, -1, 0), scale=[(0.6, 1.15)] * len(right))
        else:
            pf.beam(ms['Bark'], pts, rad, sides=4, ends=('cut', 'cut'), props=ms['Props'], seed=70 + r, up_hint=(0, -1, 0),
                    scale=[(0.6, 1.15)] * len(pts))
    # a couple of loose withy ends sticking out at the top
    for j, (x, dz, dx) in enumerate(((0.3, 0.12, 0.35), (1.55, 0.1, -0.3))):
        z = 0.28 + 10 * 0.086
        pf.beam(ms['Bark'], [V((x, 0.03, z)), V((x + dx * 0.5, 0.035, z + dz * 0.6)), V((x + dx, 0.05, z + dz))], 0.03,
                sides=4, ends=('cut', 'broken'), seed=120 + j, broken_len=0.04)
    # moss lumps on the top withies + drips on the wattle face
    for k, (xa, xb, z) in enumerate(((0.2, 0.75, 1.18), (0.85, 1.1, 1.18), (1.3, 1.9, 0.5))):
        pf.drips_along(ms['Plants'], (xa, -0.15, z), (xb, -0.15, z), 140 + k, n=2, hmin=0.12, hmax=0.3)
    tufts(ms, [(-0.35, 0.05), (2.35, 0.1), (0.9, 0.35)], 44)
    return pf.finish(vid, ms, drape(6))


def broken_low(vid='SW19c'):
    ms = M()
    P1 = post(ms, (0, 0, 0), (-0.04, 0.02, 1.02), 0.12, 0.108, 5, kink=(0.02, 0), top_end='cut')
    P2 = post(ms, (1.3, 0.0, 0), (1.33, -0.02, 1.08), 0.115, 0.105, 6, kink=(0.02, 0.01), top_end='broken')
    # leaning pair at the right end: one post tilted right, a fallen post leaning against it
    P3 = post(ms, (2.2, 0.02, 0), (2.47, 0.0, 1.22), 0.125, 0.11, 7, kink=(0.02, 0.0), top_end='cut')
    P4 = post(ms, (3.05, 0.1, 0), (2.58, 0.16, 1.1), 0.115, 0.105, 8, kink=(0.03, 0.02), top_end='broken')
    fy = -0.175
    # the only rail level left (upper rail missing): left post -> middle post -> broken in front of the leaning pair
    a = V((-0.25, fy, 0.74)); b = V((1.45, fy, 0.7))
    rail(ms, a, b, 12, h=0.2, th=0.125, ends=('cut', 'cut'), sag=0.07, segs=6)
    a2 = V((1.22, fy - 0.11, 0.63)); b2 = V((2.42, fy - 0.08, 0.84))
    rail(ms, a2, b2, 13, h=0.19, th=0.12, ends=('broken', 'cut'), sag=0.04, segs=5, jag=0.25)
    # stub of the missing upper rail on the middle post
    rail_moss(ms, a + V((0, 0, .095)), b + V((0, 0, .095)), 31, n=4)
    rail_moss(ms, a2 + V((0, -0.02, .09)), b2 + V((0, -0.02, .09)), 32, n=2)
    tufts(ms, [(-0.35, 0.05), (3.2, 0.2), (2.75, 0.3)], 46)
    return pf.finish(vid, ms, drape(7))


VARIANTS = [('SW19a', 'WaterFenceRails', two_rails, 'two broad rails between two leaning posts; lower rail splintered'),
            ('SW19b', 'WaterFenceWattle', wattle, 'woven wattle (real rods) between two posts and 3 stakes, ragged hole'),
            ('SW19c', 'WaterFenceBroken', broken_low, 'low broken section, upper rail missing, leaning post pair')]

if __name__ == '__main__':
    sk.reset(); os.makedirs(OUT, exist_ok=True)
    quick = 'quick' in sys.argv
    recs, tiles, labels, groups = [], [], [], {}
    for vid, name, fn, note in VARIANTS:
        objs = fn(vid); groups[vid] = objs
        fbx = os.path.join(OUT, 'SM_SW19_%s.fbx' % name)
        sk.export_fbx(objs, fbx)
        recs.append(sk.variant_record(vid, name, objs, fbx, water_line_z=WATER,
                    pivot='left joint at the base: bottom of the left post = (0,0,0); fence runs along +X, front -Y',
                    solidity='solid', burns=True,
                    collider='posts + rails (+ wattle rods as one thin box per intact span) solid; the gaps/hole stay open; '
                             'Plants and Moss no collider', moving_parts='none', notes=note))
        lo, hi = sk.bounds(objs)
        cx = (lo[0] + hi[0]) / 2
        for tag, az, el in (('front', -32, 12), ('rear', 148, 12), ('side', 90, 6), ('water', -20, 16)):
            p = os.path.join(OUT, '%s_%s.png' % (vid, tag))
            sk.render(objs, p, az=az, el=el, size=(360, 300), samples=8 if quick else 12, fov=26, margin=0.9,
                      water_z=WATER if tag == 'water' else None)
            tiles.append(p); labels.append('%s %s %s' % (vid, name, tag))
    sk.sheet(tiles, os.path.join(OUT, 'card.jpg'), cols=4, labels=labels, concept=SHEET,
             title='SW19 Zabor iz vody - concept on top; front 3/4, rear 3/4, side, in water')
    for i, (vid, *_r) in enumerate(VARIANTS):
        pf.place(groups[vid], i * 4.0)
    json.dump(recs, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)
    sk.save_blend(os.path.join(OUT, 'SW19_water_fences.blend'))
    for r in recs:
        print(r['id'], r['tris'], r['size_xyz_m'], r['tris_by_material'])
