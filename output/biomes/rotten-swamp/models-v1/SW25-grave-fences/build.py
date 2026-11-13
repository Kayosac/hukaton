"""SW25 Кладбищенская ограда (grave fence) - 2 variants. Old wrought-iron enclosure section (concept + brief:
rusted iron, does not burn): square posts with flat caps, two rails, simple uneven pickets, no ornaments.
All bars use the Props 'iron' atlas region (rust painted). Moss clumps + drips on the rails.
Pivot: LEFT joint at the base = bottom of the left post (0,0,0); section runs along +X; front -Y.
Water line z=0.42 (about half the height in water when placed)."""
import sys, math, random, os, json
sys.path.insert(0, '/home/claude/swamp/kit'); sys.path.insert(0, '/home/claude/swamp/items/_pathsfences')
import sk, pflib as pf
from mathutils import Vector as V
import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
SHEET = '/home/claude/ref/swamp/sheets/25-grave-fences.png'
WATER = 0.42


def M():
    return {'Props': sk.Mesh('Props'), 'Moss': sk.Mesh('Moss'), 'Plants': sk.Mesh('Plants')}


class Lean:
    """Tilts the whole section: every point is sheared by a lean that varies along x (posts lean differently)."""
    def __init__(self, L, left=(-0.04, 0.06), right=(0.05, 0.12)):
        self.L, self.a, self.b = L, left, right

    def __call__(self, p):
        p = V(p)
        t = min(1, max(0, p.x / self.L))
        lx = self.a[0] + (self.b[0] - self.a[0]) * t
        ly = self.a[1] + (self.b[1] - self.a[1]) * t
        return V((p.x + lx * p.z, p.y + ly * p.z, p.z))


def bar(ms, pts, hw, seed, lean, ends=('cut', 'cut'), hd=None, blen=0.06):
    P = [lean(p) for p in pts]
    hd = hd or hw
    vert = abs(P[-1].z - P[0].z) > 0.3
    return pf.beam(ms['Props'], P, hw * 1.414, sides=4, rot=math.pi / 4, scale=[(hd / hw, 1.0) if vert else (1.0, hd / hw)] * len(P),
                   ends=ends, region='iron', region_len=0.35, seed=seed, broken_len=blen, up_hint=(0, -1, 0) if vert else (0, 0, 1))


def post(ms, x, H, seed, lean, hw=0.068):
    rnd = random.Random(seed)
    pts = [(x, 0, 0), (x + rnd.uniform(-.01, .01), 0, H * 0.3), (x + rnd.uniform(-.01, .01), 0, H * 0.55), (x + rnd.uniform(-.015, .015), 0, H * 0.78), (x, 0, H)]
    rings = bar(ms, pts, hw, seed, lean)
    pf.sleeve(ms['Moss'], rings, 3, grow=0.008, seed=seed + 7, plants=ms['Plants'], cover=0.5, drips=1)
    # flat cap: a plate + low pyramid on top of the post, following its lean
    top = lean(V((x, 0, H)))
    below = lean(V((x, 0, H - 0.1)))
    T = (top - below).normalized()
    c0 = top + T * 0.02
    pf.beam(ms['Props'], [c0 - T * 0.02, c0 + T * 0.035], (hw + 0.035) * 1.414, sides=4, rot=math.pi / 4 + rnd.uniform(-.1, .1),
            ends=('cut', 'open'), region='iron', region_len=0.2, seed=seed + 1, up_hint=(0, -1, 0))
    ring_top = c0 + T * 0.035
    # pyramid roof of the cap
    rr = (hw + 0.035) * 1.414
    pf.beam(ms['Props'], [ring_top, ring_top + T * 0.035], [rr, rr * 0.5], sides=4, rot=math.pi / 4, ends=('open', 'cut'),
            region='iron', region_len=0.2, seed=seed + 2, up_hint=(0, -1, 0))


def rail(ms, xs, zs, seed, lean, ends=('cut', 'cut'), hw=0.03, hd=0.02):
    pts = [(x, random.Random(seed + i).uniform(-.008, .008), z) for i, (x, z) in enumerate(zip(xs, zs))]
    bar(ms, pts, hw, seed, lean, ends=ends, hd=hd)
    return [lean(V(p)) for p in pts]


def picket(ms, x, z0, z1, seed, lean, bend=0.0, ends=('cut', 'cut'), hw=0.018):
    rnd = random.Random(seed)
    pts = [(x, 0, z0), (x + bend * 0.4 + rnd.uniform(-.008, .008), rnd.uniform(-.01, .01), z0 + (z1 - z0) * 0.45),
           (x + bend + rnd.uniform(-.008, .008), rnd.uniform(-.012, .012), z1)]
    bar(ms, pts, hw, seed, lean, ends=ends)


def rail_drips(ms, rp, seed, n=2):
    rnd = random.Random(seed)
    for i in range(len(rp) - 1):
        if rnd.random() < 0.3:
            continue
        a, b = rp[i] + V((0, -0.024, 0.026)), rp[i + 1] + V((0, -0.024, 0.026))
        pf.drips_along(ms['Plants'], a, b, seed + i, n=1, hmin=0.14, hmax=0.3)


def whole(vid='SW25a'):
    ms = M(); rnd = random.Random(251)
    L = 1.8
    lean = Lean(L, left=(-0.05, 0.07), right=(0.04, 0.13))
    post(ms, 0.0, 1.02, 1, lean)
    post(ms, L, 1.08, 2, lean)
    # top rail: wavy / bent; bottom rail lower and straighter
    xs = [0.0, 0.3, 0.62, 0.95, 1.25, 1.55, L]
    top = rail(ms, xs, [0.84, 0.82, 0.86, 0.9, 0.85, 0.83, 0.88], 10, lean)
    bot = rail(ms, xs, [0.42, 0.43, 0.41, 0.4, 0.42, 0.44, 0.43], 11, lean)
    px = [0.2, 0.42, 0.61, 0.86, 1.07, 1.3, 1.55]
    for i, x in enumerate(px):
        b = rnd.uniform(-0.04, 0.04) + (0.06 if i == 4 else 0)
        zt = 0.86 + rnd.uniform(-0.04, 0.05)
        picket(ms, x + rnd.uniform(-.03, .03), 0.02 + rnd.uniform(0, 0.05), zt, 20 + i, lean, bend=b)
    rail_drips(ms, top, 30); rail_drips(ms, bot, 31)
    return pf.finish(vid, ms, [('Props', 0.8, 0.6, 3)])


def broken(vid='SW25b'):
    ms = M(); rnd = random.Random(252)
    L = 1.5
    lean = Lean(L, left=(0.03, 0.05), right=(-0.06, 0.1))
    post(ms, 0.0, 1.0, 3, lean)
    post(ms, L, 1.06, 4, lean)
    # left part: top rail bent upward and torn, bottom rail broken
    t1 = rail(ms, [0.0, 0.25, 0.48, 0.62, 0.7], [0.83, 0.82, 0.86, 0.95, 1.04], 12, lean, ends=('cut', 'broken'))
    b1 = rail(ms, [0.0, 0.3, 0.58, 0.7], [0.41, 0.42, 0.4, 0.37], 13, lean, ends=('cut', 'broken'))
    # right part: rail fragment bent the other way, bottom rail
    t2 = rail(ms, [0.92, 1.0, 1.15, 1.32, L], [1.0, 0.92, 0.86, 0.84, 0.87], 14, lean, ends=('broken', 'cut'))
    b2 = rail(ms, [0.95, 1.2, L], [0.36, 0.41, 0.43], 15, lean, ends=('broken', 'cut'))
    for i, (x, zt, bnd, e) in enumerate(((0.2, 0.86, 0.02, 'cut'), (0.38, 0.88, -0.03, 'cut'), (0.55, 0.9, 0.05, 'cut'),
                                          (1.12, 0.88, -0.04, 'cut'), (1.3, 0.86, 0.02, 'cut'))):
        picket(ms, x, 0.02 + rnd.uniform(0, .05), zt, 40 + i, lean, bend=bnd, ends=('cut', e))
    # a broken picket stub in the gap (snapped off above the water)
    picket(ms, 0.8, 0.02, 0.55, 46, lean, bend=0.06, ends=('cut', 'broken'))
    rail_drips(ms, t1, 50); rail_drips(ms, b1, 51); rail_drips(ms, t2, 52)
    return pf.finish(vid, ms, [('Props', 0.8, 0.6, 4)])


VARIANTS = [('SW25a', 'GraveFenceTilted', whole, 'whole tilted section, wavy top rail, 7 uneven pickets'),
            ('SW25b', 'GraveFenceBroken', broken, 'broken section: torn and bent top rail, gap, snapped picket')]

if __name__ == '__main__':
    sk.reset(); os.makedirs(OUT, exist_ok=True)
    quick = 'quick' in sys.argv
    recs, tiles, labels, groups = [], [], [], {}
    for vid, name, fn, note in VARIANTS:
        objs = fn(vid); groups[vid] = objs
        fbx = os.path.join(OUT, 'SM_SW25_%s.fbx' % name)
        sk.export_fbx(objs, fbx)
        recs.append(sk.variant_record(vid, name, objs, fbx, water_line_z=WATER,
                    pivot='left joint at the base: bottom of the left post = (0,0,0); runs along +X, front -Y',
                    solidity='solid', burns=False,
                    collider='one thin box over the section (posts+rails); in b two boxes leaving the gap; Moss/Plants no collider',
                    moving_parts='none', notes=note + '; material: Props iron region (wrought iron, rust painted)'))
        for tag, az, el in (('front', -32, 12), ('rear', 148, 12), ('side', 75, 8), ('water', -25, 18)):
            p = os.path.join(OUT, '%s_%s.png' % (vid, tag))
            sk.render(objs, p, az=az, el=el, size=(360, 300), samples=8 if quick else 12, fov=26, margin=0.9,
                      water_z=WATER if tag == 'water' else None)
            tiles.append(p); labels.append('%s %s %s' % (vid, name, tag))
    sk.sheet(tiles, os.path.join(OUT, 'card.jpg'), cols=4, labels=labels, concept=SHEET,
             title='SW25 Kladbishchenskaya ograda - concept on top; front 3/4, rear 3/4, side, in water')
    for i, (vid, *_r) in enumerate(VARIANTS):
        pf.place(groups[vid], i * 2.5)
    json.dump(recs, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)
    sk.save_blend(os.path.join(OUT, 'SW25_grave_fences.blend'))
    for r in recs:
        print(r['id'], r['tris'], r['size_xyz_m'], r['tris_by_material'])
