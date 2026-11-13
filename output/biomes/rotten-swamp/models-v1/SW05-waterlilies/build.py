"""SW05 Кувшинки — a: three pads without a flower, b: two pads with one cream flower.
Pads = thin 12-sided fan cards (Plants lily_pad_0/1: notch, radial facets, rotten brown rim), centre sagging a few
mm, rim slightly uneven. Flower = real petals (Props lily_petal, boat-shaped, 3 rings 8+7+5) around a small
ochre stamen prism (Props vent_crust). Pivot at the water level (z = 0)."""
import sys, os, json, math, random
sys.path.insert(0, '/home/claude/swamp/items/_plants_lib')
import plib, sk
from plib import V

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)
SHEET = '/home/claude/ref/swamp/sheets/05-waterlilies.png'

def flower(pr, rnd, c, scale=1.0):
    C = V(c)
    rings = [(8, 0.06, 0.032, 0.42, 0.0), (7, 0.05, 0.028, 0.85, 0.008), (5, 0.036, 0.022, 1.2, 0.016)]
    for n, L, W, tilt, dz in rings:
        a0 = rnd.uniform(0, 1)
        for k in range(n):
            a = a0 + 2 * math.pi * k / n + rnd.uniform(-.08, .08)
            t = tilt + rnd.uniform(-.12, .12)
            d = V((math.cos(a) * math.cos(t), math.sin(a) * math.cos(t), math.sin(t)))
            base = C + V((math.cos(a), math.sin(a), 0)) * 0.008 * scale + V((0, 0, dz * scale))
            plib.petal(pr, base, d, L * scale * rnd.uniform(.9, 1.08), W * scale, 'lily_petal', cup=0.4,
                       wind=(0.0, 0.004))
    # stamen: small ochre 6-sided prism with a flat top
    pts = [C + V((0, 0, 0.004 * scale)), C + V((0, 0, 0.026 * scale))]
    plib.prism(pr, pts, [0.012 * scale, 0.015 * scale], 'vent_crust', sides=6, caps=(False, True))

def lily_a(vid):
    rnd = random.Random(51); pl = sk.Mesh('Plants')
    plib.disk(pl, (-0.04, -0.05, 0.005), 0.135, 'lily_pad_0', rot=0.3, rnd=rnd, tilt=(0.01, -0.015))
    plib.disk(pl, (0.1, 0.07, 0.0065), 0.1, 'lily_pad_1', rot=2.4, rnd=rnd, tilt=(-0.02, 0.0))
    plib.disk(pl, (-0.1, 0.13, 0.008), 0.075, 'lily_pad_0', rot=4.1, rnd=rnd, tilt=(0.0, 0.02))
    return plib.finish([pl], vid)

def lily_b(vid):
    rnd = random.Random(52); pl = sk.Mesh('Plants'); pr = sk.Mesh('Props')
    plib.disk(pl, (-0.05, -0.04, 0.005), 0.16, 'lily_pad_1', rot=5.2, rnd=rnd, tilt=(0.015, 0.01))
    plib.disk(pl, (0.15, 0.1, 0.0065), 0.11, 'lily_pad_0', rot=1.9, rnd=rnd, tilt=(-0.01, -0.02))
    flower(pr, rnd, (-0.08, 0.0, 0.006), scale=1.15)
    return plib.finish([pl, pr], vid)

VARIANTS = [('SW05a', 'LilyPads', lily_a), ('SW05b', 'LilyPadsFlower', lily_b)]

def main(render=True):
    sk.reset()
    recs, allv, tiles, labels = [], [], [], []
    for vid, name, fn in VARIANTS:
        objs = fn(vid)
        plib.centre_xy(objs)
        fbx = os.path.join(OUT, 'SM_%s_%s.fbx' % (vid[:4], name))
        sk.export_fbx(objs, fbx)
        recs.append(sk.variant_record(vid, name, objs, fbx, water_line_z=0.0, solidity='surface', burns=False,
                                      collider='none', moving_parts=None,
                                      notes='pivot at the water level; pads float 0-9 mm above it (thin cards, '
                                            'centre sags 4 mm); petals flutter slightly (wind UV2 on Props)'))
        allv.append(objs)
        if render:
            for j, (az, el, w) in enumerate([(-25, 38, None), (30, 38, None), (0, 6, None), (-25, 38, -0.001)]):
                f = os.path.join(OUT, '%s_%d.png' % (vid, j))
                sk.render(objs, f, az=az, el=el, size=(300, 220), samples=8, water_z=w)
                tiles.append(f); labels.append('%s %s az%d el%d%s' % (vid, name, az, el, ' water' if w is not None else ''))
    if render:
        sk.sheet(tiles, os.path.join(OUT, 'card.jpg'), cols=4, labels=labels, concept=SHEET,
                 title='SW05 Water lilies — concept (top) vs model')
    json.dump(recs, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)
    plib.side_by_side(allv, gap=0.2)
    sk.save_blend(os.path.join(OUT, 'SW05_waterlilies.blend'))
    for r in recs:
        print(r['id'], r['tris'], r['tris_by_material'], r['size_xyz_m'], r['min_z'])

if __name__ == '__main__':
    main('--norender' not in sys.argv)
