"""SW14 Гнилые мостки (rotten boardwalk) - 2 variants, 3 m modules.
Pivot: centre of the ENTRY edge (y=0) at the post bottoms (z=0); the walkway runs along +Y to y=3.
Deck top z~0.86, water line z=0.38. Planks across the walk direction on two longitudinal beams, four posts."""
import sys, math, random, os, json
sys.path.insert(0, '/home/claude/swamp/kit'); sys.path.insert(0, '/home/claude/swamp/items/_pathsfences')
import sk, pflib as pf
from mathutils import Vector as V
import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
SHEET = '/home/claude/ref/swamp/sheets/14-rotten-boardwalks.png'
LEN = 3.0
DECK = 0.86
WATER = 0.38
BX = 0.56          # beam offset
PX = 0.79          # post offset
PT = 0.075         # plank thickness
BH, BW = 0.21, 0.16


def M():
    return {'Planks': sk.Mesh('Planks'), 'Props': sk.Mesh('Props'), 'Moss': sk.Mesh('Moss'), 'Plants': sk.Mesh('Plants')}


def post(ms, base, top, hw, seed, flare=1.22, kink=(0, 0)):
    rnd = random.Random(seed)
    b, t = V(base), V(top)
    pts = []
    for i, s in enumerate((0, 0.18, 0.45, 0.75, 1.0)):
        p = b.lerp(t, s)
        if 0 < i < 4:
            p += V((kink[0] * math.sin(math.pi * s) + rnd.uniform(-.015, .015), kink[1] * math.sin(math.pi * s) + rnd.uniform(-.015, .015), 0))
        pts.append(p)
    r = hw * 1.414
    radii = [r * flare, r * (1 + (flare - 1) * .4), r, r * .97, r * .95]
    sc = [(1, rnd.uniform(.85, 1.0))] * 5
    rings = pf.beam(ms['Planks'], pts, radii, sides=4, rot=math.pi / 4 + rnd.uniform(-.15, .15), scale=sc,
                    ends=('cut', 'broken' if rnd.random() < .5 else 'cut'), props=ms['Props'], cap_region='end_grain_1',
                    seed=seed, broken_len=0.07)
    # moss sleeve from the bottom to just above the water
    k_top = 2 if WATER + 0.25 > pts[2].z else 1
    pf.sleeve(ms['Moss'], rings, 3, grow=0.012, seed=seed, plants=ms['Plants'], cover=0.4, drips=1)
    return pts


def beam(ms, x, y0, y1, z0, z1, seed, zmid=None, ends=('cut', 'cut')):
    rnd = random.Random(seed)
    pts = []
    for i in range(5):
        s = i / 4
        z = z0 + (z1 - z0) * s
        if zmid is not None:
            z -= zmid * math.sin(math.pi * s)
        pts.append(V((x + (rnd.uniform(-.015, .015) if 0 < i < 4 else 0), y0 + (y1 - y0) * s, z - BH / 2)))
    sc = [(BH / BW, 1.0)] * 5
    pf.beam(ms['Planks'], pts, BW / 2 * 1.414, sides=4, rot=math.pi / 4, scale=sc, ends=ends, props=ms['Props'],
            cap_region='end_grain_0', seed=seed, up_hint=(0, 0, 1))
    return pts


def deck(ms, zL, zR, seed, missing=(), broken=(), chipped=()):
    """Planks across X from y=0 to y=3. zL(y)/zR(y): beam top heights at the left/right beam."""
    rnd = random.Random(seed)
    y = 0.0
    i = 0
    planks = []
    while True:
        w = rnd.uniform(0.2, 0.28)
        if y + w > LEN + 0.005:
            w = LEN - y - 0.005
            if w < 0.12:
                break
        yc = y + w / 2 + 0.003
        planks.append((i, yc, w))
        y += w + rnd.uniform(0.01, 0.035)
        i += 1
    tops = []
    for i, yc, w in planks:
        if i in missing:
            continue
        L = rnd.uniform(1.38, 1.55)
        xo = rnd.uniform(-0.06, 0.06)
        x0, x1 = -L / 2 + xo, L / 2 + xo
        zl, zr = zL(yc) + PT / 2, zR(yc) + PT / 2
        # z along the plank: line through the two beam supports
        def zat(x):
            return zl + (zr - zl) * (x + BX) / (2 * BX)
        ends = ['cut', 'cut']
        if i in broken:
            # broken plank: only a stub over the left beam, splintered end
            x1 = rnd.uniform(-0.15, 0.15)
            ends[1] = 'broken'
        if i in chipped:
            ends[0] = 'broken'
        a = V((x0, yc + rnd.uniform(-.02, .02), zat(x0) + rnd.uniform(-.01, .01)))
        b = V((x1, yc + rnd.uniform(-.02, .02), zat(x1) + rnd.uniform(-.01, .01)))
        c = (a + b) / 2
        pf.plank(ms['Planks'], c, b - a, (0, -1, 0), (b - a).length, w, PT * rnd.uniform(.9, 1.15), ends=tuple(ends),
                 sag=rnd.uniform(-0.008, 0.012), twist=rnd.uniform(-0.06, 0.06), crook=rnd.uniform(-0.015, 0.015),
                 segs=4, seed=seed * 100 + i, jag_len=0.14)
        tops.append((a, b, w))
    return tops


def deck_drips(ms, tops, seed):
    rnd = random.Random(seed)
    # drips hang from the plank ends along both sides of the deck and from the beams' outer bottom edge
    for k, (a, b, w) in enumerate(tops):
        if rnd.random() < 0.75:
            for p, sgn in ((a, -1), (b, 1)):
                if rnd.random() < 0.7:
                    q = p + V((0, 0, -PT * 0.5))
                    pf.drips_along(ms['Plants'], q + V((0, -w * .45, 0)), q + V((0, w * .45, 0)), seed + k * 3 + sgn, n=1,
                                   hmin=0.25, hmax=0.42)


def boardwalk(vid, sag=False):
    ms = M(); rnd = random.Random(140 + sag)
    btop = DECK - PT
    if not sag:
        zL = lambda y: btop + 0.012 * math.sin(y * 2.1)
        zR = lambda y: btop - 0.018 * math.sin(math.pi * y / LEN) + 0.01 * math.sin(y * 3.3)
        bl = beam(ms, -BX, -0.04, LEN + 0.05, zL(0), zL(LEN), 1)
        br = beam(ms, BX, -0.06, LEN + 0.03, zR(0), zR(LEN), 2, zmid=0.018)
        tops = deck(ms, zL, zR, 3, chipped=(8,), broken=())
        posts = [((-PX, 0.22), (-PX - 0.05, 0.2), 1.20, (-0.04, 0.0)), ((PX, 0.2), (PX + 0.03, 0.24), 1.12, (0.03, 0)),
                 ((-PX, LEN - 0.22), (-PX - 0.02, LEN - 0.2), 1.26, (0.0, .03)), ((PX, LEN - 0.2), (PX + 0.06, LEN - 0.25), 1.16, (.04, 0))]
    else:
        # right beam has sagged: its far half drops, deck tilts to the right, two planks gone, one broken stub
        zL = lambda y: btop + 0.01 * math.sin(y * 2.4) - 0.03 * (y / LEN)
        zR = lambda y: btop - 0.03 - 0.24 * math.sin(math.pi * min(1, y / LEN * 1.25) / 2) ** 2
        bl = beam(ms, -BX, -0.04, LEN + 0.06, zL(0), zL(LEN), 11)
        pts = []
        br = beam(ms, BX, -0.05, LEN + 0.05, zR(0), zR(LEN), 12, zmid=0.03)
        tops = deck(ms, zL, zR, 13, missing=(6, 7), broken=(8,), chipped=(2,))
        posts = [((-PX, 0.22), (-PX - 0.03, 0.2), 1.22, (-0.03, 0)), ((PX, 0.2), (PX + 0.05, 0.22), 1.14, (0.03, 0)),
                 ((-PX, LEN - 0.22), (-PX - 0.04, LEN - 0.18), 1.18, (0, .03)), ((PX - 0.04, LEN - 0.25), (PX + 0.24, LEN - 0.2), 1.0, (.03, 0))]
    for j, (b, t, h, kink) in enumerate(posts):
        pts = post(ms, (b[0], b[1], 0), (t[0], t[1], h), 0.13 + rnd.uniform(-.01, .012), 30 + j + 10 * sag, kink=kink)
    deck_drips(ms, tops, 70 + sag)
    # drips under the beams' outer bottom edges
    for k, (x, zf) in enumerate(((-BX - BW / 2 - 0.005, zL), (BX + BW / 2 + 0.005, zR))):
        for s0 in (0.3, 0.75, 1.2, 1.65, 2.1, 2.5):
            if rnd.random() < 0.15:
                continue
            y0 = s0 + rnd.uniform(-0.05, 0.05); y1 = y0 + rnd.uniform(0.38, 0.45)
            pf.drips_along(ms['Plants'], (x, y0, zf(y0) - BH * 0.4), (x, y1, zf(y1) - BH * 0.4), 90 + k * 7 + int(s0 * 10), n=1,
                           hmin=0.32, hmax=0.52)
    return pf.finish(vid, ms, [('Planks', 0.8, 0.22, 5 + sag)])


VARIANTS = [('SW14a', 'RottenBoardwalk', lambda v: boardwalk(v, False), 'whole uneven deck, one chipped board'),
            ('SW14b', 'RottenBoardwalkSagged', lambda v: boardwalk(v, True), 'right side sagged, 2 missing planks (real gap) + broken stub')]

if __name__ == '__main__':
    sk.reset(); os.makedirs(OUT, exist_ok=True)
    quick = 'quick' in sys.argv
    recs, tiles, labels, groups = [], [], [], {}
    for vid, name, fn, note in VARIANTS:
        objs = fn(vid); groups[vid] = objs
        fbx = os.path.join(OUT, 'SM_SW14_%s.fbx' % name)
        sk.export_fbx(objs, fbx)
        recs.append(sk.variant_record(vid, name, objs, fbx, water_line_z=WATER, deck_top_z=DECK,
                    pivot='centre of the entry edge (y=0), z=0 = post bottoms; walkway runs along +Y to y=3.0',
                    solidity='walk-on-top', burns=True,
                    collider='planks (box per plank or mesh collider of SW14x_Planks) + posts; the gap in b stays open; '
                             'Plants (moss drips) and Moss no collider', moving_parts='none', notes=note))
        for tag, az, el in (('front', -40, 22), ('rear', 140, 22), ('side', 90, 6), ('water', -30, 25)):
            p = os.path.join(OUT, '%s_%s.png' % (vid, tag))
            sk.render(objs, p, az=az, el=el, size=(360, 300), samples=8 if quick else 12, fov=26, margin=0.9,
                      water_z=WATER if tag == 'water' else None)
            tiles.append(p); labels.append('%s %s %s' % (vid, name, tag))
    sk.sheet(tiles, os.path.join(OUT, 'card.jpg'), cols=4, labels=labels, concept=SHEET,
             title='SW14 Gnilye mostki - concept on top; front 3/4, rear 3/4, side, in water')
    for i, (vid, *_r) in enumerate(VARIANTS):
        pf.place(groups[vid], i * 2.5)
    json.dump(recs, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)
    sk.save_blend(os.path.join(OUT, 'SW14_rotten_boardwalks.blend'))
    for r in recs:
        print(r['id'], r['tris'], r['size_xyz_m'], r['tris_by_material'])
