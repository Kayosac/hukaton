"""SW22 Вешала с сетями (net-drying rack) - 1 variant.
Two crooked forked poles (BarkMossy), a sagging cross pole resting in the forks, rope lashing at the forks,
two torn net sections hanging in a sag (segmented Plants 'net' cards, front + folded-over back layers, extra torn
cells) and one rag over the pole (Plants 'rag_card'). Nets/rag carry wind (sway + flutter, 0 at the hang line).
Pivot: base centre (ground at z=0), front faces -Y. Stands on a bank (no water line; may also stand in shallows)."""
import sys, math, random, os, json
sys.path.insert(0, '/home/claude/swamp/kit'); sys.path.insert(0, '/home/claude/swamp/items/_pathsfences')
import sk, pflib as pf
from mathutils import Vector as V
import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
SHEET = '/home/claude/ref/swamp/sheets/22-net-rack.png'


def M():
    return {'BarkMossy': sk.Mesh('BarkMossy'), 'Bark': sk.Mesh('Bark'), 'Props': sk.Mesh('Props'), 'Moss': sk.Mesh('Moss'),
            'Plants': sk.Mesh('Plants')}


def pole(ms, pts, r0, r1, seed, mat='Bark', ends=('cut', 'cut'), sides=6):
    n = len(pts)
    radii = [r0 + (r1 - r0) * i / (n - 1) for i in range(n)]
    radii[0] *= 1.12
    return pf.beam(ms[mat], [V(p) for p in pts], radii, sides=sides, ends=ends, props=ms['Props'],
                   cap_region='end_grain_0', seed=seed, broken_len=0.06, jitter=0.004)


def forked(ms, base, crotch, prong_a, prong_b, r0, seed):
    rnd = random.Random(seed)
    B, C = V(base), V(crotch)
    stem = [B.lerp(C, s) + (V((rnd.uniform(-.03, .03), rnd.uniform(-.02, .02), 0)) if 0 < s < 1 else V()) for s in (0, .12, .3, .5, .7, .88, 1.0)]
    rings = pole(ms, stem + [C + (V(prong_a) - C) * 0.5, V(prong_a)], r0, r0 * 0.6, seed, ends=('cut', 'cut'))
    pf.sleeve(ms['Moss'], rings, 3, grow=0.012, seed=seed + 5, plants=ms['Plants'], cover=0.4, drips=1)
    pole(ms, [C - (C - stem[-2]) * 0.6, C + (V(prong_b) - C) * 0.5, V(prong_b)], r0 * 0.68, r0 * 0.5, seed + 1, ends=('open', 'cut'),
         sides=5)
    return stem


def net(ms, a, b, pole_z, sag, drop, seed, y0=-0.02, bulge=-0.07, nu=7, nv=5, cut=(), back=False):
    """Net section hanging from the cross pole between x=a..b. The top edge follows the pole at both ends and
    sags `sag` below it in the middle (festoon); the net falls `drop` metres below its top edge."""
    rnd = random.Random(seed)
    H = {}

    def P(i, j):
        s = i / nu; t = j / nv
        x = a + (b - a) * s
        ztop = pole_z(x) - 0.04 - sag * math.sin(math.pi * s) ** 1.3
        dl = drop * (0.75 + 0.35 * math.sin(math.pi * s)) + rnd.uniform(-0.03, 0.03) * t
        y = y0 + bulge * math.sin(math.pi * s) * (0.3 + t) + (0.06 * t if back else 0)
        return (x + rnd.uniform(-0.015, 0.015) * t, y, ztop - dl * t)

    def wind(i, j):
        s = i / nu; t = j / nv
        hang = t + 0.4 * math.sin(math.pi * s) * (sag / 0.35)       # the sagging top edge also moves a bit
        return (0.10 * hang * hang, 0.04 * hang)
    sub = (0.0, 0.0, 0.62, 1.0) if not back else (0.3, 0.35, 0.92, 1.0)
    pf.grid_card(ms['Plants'], 'net', P, nu, nv, wind=wind, cut=lambda i, j: (i, j) in cut, sub=sub)


def rag(ms, x0, x1, pole_z, seed):
    """Rag folded over the pole: front flap (longer) and back flap, each its own card, bent slightly."""
    for k, (yoff, L, bend) in enumerate(((-0.105, 0.88, -0.07), (0.105, 0.58, 0.06))):
        def P(i, j, yoff=yoff, L=L, bend=bend):
            s = i / 2; t = j / 4
            x = x0 + (x1 - x0) * s + 0.02 * t * (s - .5)
            z = pole_z(x) + 0.055 - L * t
            return (x, yoff + bend * t * t, z)

        def W(i, j):
            t = j / 4
            return (0.08 * t * t, 0.05 * t)
        pf.grid_card(ms['Plants'], 'rag_card', P, 2, 4, wind=W)


def rack(vid='SW22a'):
    ms = M(); rnd = random.Random(22)
    # left pole: taller, leaning slightly right; right pole: shorter, leaning left
    Lc = (-1.12, 0.02, 2.12); Rc = (1.12, -0.02, 1.76)
    forked(ms, (-1.25, 0.0, 0.0), Lc, (-1.36, 0.02, 2.56), (-0.84, -0.03, 2.5), 0.15, 1)
    forked(ms, (1.3, 0.05, 0.0), Rc, (0.98, 0.04, 2.08), (1.46, -0.02, 2.08), 0.13, 3)
    # cross pole resting in both forks, sagging, ends sticking out
    rc = 0.095
    def pole_z(x):
        t = (x - Lc[0]) / (Rc[0] - Lc[0])
        return (Lc[2] + rc) + (Rc[2] - Lc[2]) * t - 0.08 * math.sin(math.pi * min(1, max(0, t)))
    xs = [-1.42, -1.12, -0.6, 0.0, 0.55, 1.12, 1.4]
    cp = [V((x, rnd.uniform(-.015, .015) if -1.1 < x < 1.1 else 0, pole_z(x) if -1.12 <= x <= 1.12 else pole_z(x) + (0.02 if x < 0 else -0.02))) for x in xs]
    pole(ms, cp, 0.1, 0.085, 5, mat='BarkMossy', ends=('cut', 'broken'), sides=6)
    # rope lashing at the two forks (cross pole in the crotch)
    for k, C in enumerate((Lc, Rc)):
        c = V((C[0], 0, pole_z(C[0])))
        for t in range(3):
            dz = -0.02 + 0.03 * t
            e = 0.11 + 0.012 * t
            pts = [c + V((-0.06 + 0.04 * t, -e, e + dz)), c + V((0.06 - 0.04 * t, -e, -e - 0.04 + dz)),
                   c + V((0.06 - 0.04 * t, e, -e - 0.04 + dz)), c + V((-0.06 + 0.04 * t, e, e + dz))]
            pf.rope_loop(ms['Props'], pts, r=0.011, seed=40 + k * 5 + t)
    # nets: two broad torn sections hanging in a sag + folded-over back layers
    net(ms, -1.02, -0.12, pole_z, 0.3, 1.05, 50, y0=-0.07, bulge=-0.06, cut={(2, 4), (5, 4)})
    net(ms, -0.95, -0.25, pole_z, 0.12, 0.45, 51, y0=0.07, bulge=0.04, nu=5, nv=3, back=True)
    net(ms, 0.28, 1.04, pole_z, 0.28, 0.95, 52, y0=-0.065, bulge=-0.05, cut={(4, 4), (1, 4)})
    net(ms, 0.35, 0.98, pole_z, 0.1, 0.4, 53, y0=0.07, bulge=0.05, nu=5, nv=3, back=True)
    rag(ms, -0.16, 0.3, pole_z, 60)
    # grass/sedge tufts at the pole bases
    for i, (x, y) in enumerate(((-1.45, -0.15), (-1.0, 0.18), (1.1, -0.18), (1.55, 0.12), (-1.25, 0.25))):
        h = rnd.uniform(0.35, 0.55)
        pf.tuft(ms['Plants'], (x, y, -0.02), h, h * 1.1, 70 + i, kind='grass_low' if i % 2 else 'sedge', n=3)
    objs = pf.finish(vid, ms, [('BarkMossy', 0.7, 0.45, 7), ('Bark', 0.5, 0.45, 8)])
    sk.ground(objs)
    return objs


VARIANTS = [('SW22a', 'NetRack', rack, 'two forked poles + cross pole; 2 torn net sections (4 cards) and a rag')]

if __name__ == '__main__':
    sk.reset(); os.makedirs(OUT, exist_ok=True)
    quick = 'quick' in sys.argv
    recs, tiles, labels = [], [], []
    for vid, name, fn, note in VARIANTS:
        objs = fn(vid)
        fbx = os.path.join(OUT, 'SM_SW22_%s.fbx' % name)
        sk.export_fbx(objs, fbx)
        recs.append(sk.variant_record(vid, name, objs, fbx, water_line_z=None,
                    pivot='base centre on the ground (z=0), front -Y', solidity='solid poles, passable nets', burns=True,
                    collider='two capsules on the forked poles + one capsule on the cross pole (SW22a_BarkMossy); '
                             'nets/rag (SW22a_Plants), rope (Props) and moss: no collider',
                    wind='Plants cards: Wind UV2 = (sway, flutter) in metres, 0 at the hang line on the pole',
                    moving_parts='none (nets/rag move by wind shader only)', notes=note))
        for tag, az, el in (('front', -25, 10), ('rear', 155, 10), ('side', 90, 6), ('top3q', -40, 35)):
            p = os.path.join(OUT, '%s_%s.png' % (vid, tag))
            sk.render(objs, p, az=az, el=el, size=(400, 320), samples=8 if quick else 12, fov=26, margin=0.85)
            tiles.append(p); labels.append('%s %s %s' % (vid, name, tag))
    sk.sheet(tiles, os.path.join(OUT, 'card.jpg'), cols=4, labels=labels, concept=SHEET,
             title='SW22 Veshala s setyami - concept on top; front 3/4, rear 3/4, side, high 3/4')
    json.dump(recs, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)
    sk.save_blend(os.path.join(OUT, 'SW22_net_rack.blend'))
    for r in recs:
        print(r['id'], r['tris'], r['size_xyz_m'], r['tris_by_material'])
