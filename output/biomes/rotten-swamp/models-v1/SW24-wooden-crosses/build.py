"""SW24 Кривой деревянный крест - 3 variants. Two crooked 4-sided beams + real rope lashing (Props rope).
Pivot: bottom end of the upright (0,0,0); front faces -Y; water line z=0.35 (lower part stands in the bog)."""
import sys, math, random, os, json
sys.path.insert(0, '/home/claude/swamp/kit'); sys.path.insert(0, '/home/claude/swamp/items/_pathsfences')
import sk, pflib as pf
from mathutils import Vector as V, Matrix
import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
SHEET = '/home/claude/ref/swamp/sheets/24-wooden-crosses.png'
WATER = 0.35


def M():
    return {'Planks': sk.Mesh('Planks'), 'Props': sk.Mesh('Props'), 'Moss': sk.Mesh('Moss'), 'Plants': sk.Mesh('Plants')}


def upright_path(H, lean, kinks, seed):
    """Points of the crooked upright: lean = (dx, dy) of the top; kinks: list of (s, dx, dy) local offsets."""
    rnd = random.Random(seed)
    S = [0, 0.12, 0.3, 0.5, 0.7, 0.86, 1.0]
    pts = []
    for s in S:
        x = lean[0] * s; y = lean[1] * s
        for (sk_, kx, ky) in kinks:
            w = max(0.0, 1 - abs(s - sk_) / 0.35)
            x += kx * w; y += ky * w
        if 0 < s < 1:
            x += rnd.uniform(-.006, .006); y += rnd.uniform(-.006, .006)
        pts.append(V((x, y, H * s)))
    return pts


def at(pts, z):
    """Point and tangent on a polyline at height z."""
    for i in range(len(pts) - 1):
        if pts[i].z <= z <= pts[i + 1].z:
            t = (z - pts[i].z) / (pts[i + 1].z - pts[i].z)
            return pts[i].lerp(pts[i + 1], t), (pts[i + 1] - pts[i]).normalized()
    return pts[-1], (pts[-1] - pts[-2]).normalized()


def cross(vid, H, hw, hd, lean, kinks, bar_z, bar_L, bar_left, bar_tilt, bw, bd, seed, top_end='cut',
          left_end='cut', right_end='cut', rope_r=0.012, turns=2, moss_h=0.22):
    ms = M(); rnd = random.Random(seed)
    up = upright_path(H, lean, kinks, seed)
    r = hw * 1.414
    radii = [r * 1.06, r * 1.03, r, r, r * .98, r * .96, r * .95]
    sc = [(hd / hw, 1.0)] * len(up)
    rings = pf.beam(ms['Planks'], up, radii, sides=4, rot=math.pi / 4 + rnd.uniform(-.1, .1), scale=sc, ends=('cut', top_end),
                    props=ms['Props'], cap_region='end_grain_grey', seed=seed, broken_len=0.07, up_hint=(0, -1, 0))
    # moss sleeve at the water line
    k = max(i for i, p in enumerate(up) if p.z < WATER + moss_h) + 1
    pf.sleeve(ms['Moss'], rings, min(k, len(up) - 2), grow=0.01, seed=seed + 1, plants=ms['Plants'], cover=0.5, drips=1)
    # crossbar: nailed in front of the upright at bar_z, tilted by bar_tilt (rad, + = right end lower)
    J, T = at(up, bar_z)
    Bdir = V((math.cos(bar_tilt), 0, -math.sin(bar_tilt)))
    Bdir = (Bdir - T * Bdir.dot(T)).normalized()
    N = Bdir.cross(T)                       # front normal (-Y-ish)
    C = J + N * (hd + bd)
    a = C - Bdir * bar_left
    b = C + Bdir * (bar_L - bar_left)
    bpts = []
    for i in range(5):
        s = i / 4
        p = a.lerp(b, s)
        if 0 < i < 4:
            p += T * rnd.uniform(-.008, .008) + N * rnd.uniform(-.004, .004)
        bpts.append(p)
    rb = bw * 1.414
    pf.beam(ms['Planks'], bpts, [rb * .97, rb, rb, rb, rb * .96], sides=4, rot=math.pi / 4 + rnd.uniform(-.08, .08),
            scale=[(1.0, bd / bw)] * 5, ends=(left_end, right_end), props=ms['Props'], cap_region='end_grain_grey',
            seed=seed + 5, broken_len=0.08, up_hint=tuple(T))
    # rope lashing (real geometry)
    pf.lash(ms['Props'], J, T, Bdir, hw * 1.02, hd * 1.02, bw * 1.02, bd * 1.02, r=rope_r, seed=seed + 9, turns=turns, wraps=1)
    objs = pf.finish(vid, ms, [('Planks', 0.75, 0.6, seed + 3)])
    lo, hi = sk.bounds(objs)
    for o in objs:
        o.data.transform(Matrix.Translation(V((0, 0, -lo[2]))))
    return objs


def tall(vid='SW24a'):
    return cross(vid, 1.8, 0.08, 0.068, (0.3, 0.04), [(0.4, 0.025, 0.0)], 1.36, 0.82, 0.37, 0.2, 0.065, 0.052, 241,
                 top_end='cut', left_end='cut', right_end='cut', rope_r=0.017, turns=2)


def thin(vid='SW24b'):
    # S-shaped crooked thin upright, short broken left arm
    return cross(vid, 1.25, 0.06, 0.052, (0.04, 0.0), [(0.3, 0.08, 0.0), (0.72, -0.06, 0.01)], 0.97, 0.52, 0.16, 0.12,
                 0.05, 0.042, 242, top_end='broken', left_end='broken', right_end='cut', rope_r=0.014, turns=2)


def heavy(vid='SW24c'):
    # stout cross leaning diagonally, crossbar low on the shaft
    return cross(vid, 1.5, 0.11, 0.095, (0.5, -0.03), [(0.5, 0.02, 0.0)], 0.86, 0.95, 0.43, 0.26, 0.09, 0.072, 243,
                 top_end='cut', left_end='cut', right_end='cut', rope_r=0.021, turns=3, moss_h=0.3)


VARIANTS = [('SW24a', 'WoodenCrossTall', tall, 'tall cross, tilted crossbar, X lashing'),
            ('SW24b', 'WoodenCrossThinBroken', thin, 'thin S-crooked upright, broken left arm and splintered top'),
            ('SW24c', 'WoodenCrossHeavy', heavy, 'short heavy cross leaning diagonally, crossbar low')]

if __name__ == '__main__':
    sk.reset(); os.makedirs(OUT, exist_ok=True)
    quick = 'quick' in sys.argv
    recs, tiles, labels, groups = [], [], [], {}
    for vid, name, fn, note in VARIANTS:
        objs = fn(vid); groups[vid] = objs
        fbx = os.path.join(OUT, 'SM_SW24_%s.fbx' % name)
        sk.export_fbx(objs, fbx)
        recs.append(sk.variant_record(vid, name, objs, fbx, water_line_z=WATER,
                    pivot='bottom end of the upright = (0,0,0) (lower 0.35 m goes into the bog/water)',
                    solidity='solid', burns=True,
                    collider='two boxes (upright + crossbar) or a capsule on the upright; rope/moss no collider',
                    moving_parts='none', notes=note))
        for tag, az, el in (('front', -30, 8), ('rear', 150, 8), ('side', 90, 6), ('water', -25, 12)):
            p = os.path.join(OUT, '%s_%s.png' % (vid, tag))
            sk.render(objs, p, az=az, el=el, size=(300, 360), samples=8 if quick else 12, fov=26, margin=0.95,
                      water_z=WATER if tag == 'water' else None)
            tiles.append(p); labels.append('%s %s' % (vid, tag))
    sk.sheet(tiles, os.path.join(OUT, 'card.jpg'), cols=4, labels=labels, concept=SHEET,
             title='SW24 Krivoy derevyanny krest - concept on top; front 3/4, rear 3/4, side, in water')
    for i, (vid, *_r) in enumerate(VARIANTS):
        pf.place(groups[vid], i * 1.5)
    json.dump(recs, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)
    sk.save_blend(os.path.join(OUT, 'SW24_wooden_crosses.blend'))
    for r in recs:
        print(r['id'], r['tris'], r['size_xyz_m'], r['tris_by_material'])
