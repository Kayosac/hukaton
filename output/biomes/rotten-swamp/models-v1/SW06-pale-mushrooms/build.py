"""SW06 Болотные грибы — pale long-stemmed mushroom clusters: a three tall, b low cluster.
All on Props: 4-sided slightly bent stems flaring at the foot (fungus_stem), 6-sided flat conical caps (fungus_cap)
with a thin rim and a gill underside (fungus_gill). The stump/log on the sheet only shows how they attach - not part
of the mesh. Pivot at the cluster base (stems start just below z=0 so they can be pushed into wood)."""
import sys, os, json, math, random
sys.path.insert(0, '/home/claude/swamp/items/_plants_lib')
import plib, sk
from plib import V

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)
SHEET = '/home/claude/ref/swamp/sheets/06-pale-mushrooms.png'

def cluster(vid, specs, seed):
    """specs: (base_xy, top_xyz, cap_r, stem_r, bend, sides)"""
    rnd = random.Random(seed); pr = sk.Mesh('Props')
    for (bx, by), top, cap_r, stem_r, bend, ns in specs:
        plib.mushroom(pr, V((bx, by, -0.012)), V(top), cap_r, cap_r * rnd.uniform(.42, .55), stem_r * 1.4, rnd,
                      sides_cap=ns, bend=bend, droop=rnd.uniform(.1, .35))
    return plib.finish([pr], vid)

def mush_a(vid):
    return cluster(vid, [
        ((0.0, 0.01), (0.005, 0.0, 0.25), 0.062, 0.011, 0.012, 6),
        ((-0.02, -0.005), (-0.075, -0.01, 0.15), 0.046, 0.009, -0.01, 6),
        ((0.02, 0.0), (0.075, 0.015, 0.18), 0.052, 0.0095, 0.01, 5),
    ], 61)

def mush_b(vid):
    return cluster(vid, [
        ((0.0, 0.02), (0.01, 0.03, 0.22), 0.07, 0.012, 0.012, 6),
        ((-0.04, 0.0), (-0.12, -0.01, 0.16), 0.06, 0.011, -0.012, 6),
        ((0.04, 0.0), (0.12, 0.0, 0.17), 0.058, 0.011, 0.012, 6),
        ((-0.02, -0.03), (-0.06, -0.07, 0.1), 0.045, 0.009, -0.006, 5),
        ((0.02, -0.03), (0.04, -0.075, 0.07), 0.03, 0.008, 0.004, 5),
        ((-0.04, 0.03), (-0.09, 0.06, 0.09), 0.034, 0.008, -0.004, 5),
        ((0.05, 0.03), (0.11, 0.06, 0.085), 0.027, 0.007, 0.004, 5),
    ], 62)

VARIANTS = [('SW06a', 'PaleMushroomsTall', mush_a), ('SW06b', 'PaleMushroomsCluster', mush_b)]

def main(render=True):
    sk.reset()
    recs, allv, tiles, labels = [], [], [], []
    for vid, name, fn in VARIANTS:
        objs = fn(vid)
        plib.centre_xy(objs)   # keep z: pivot at the cluster base, stem feet 12 mm below for embedding
        fbx = os.path.join(OUT, 'SM_%s_%s.fbx' % (vid[:4], name))
        sk.export_fbx(objs, fbx)
        recs.append(sk.variant_record(vid, name, objs, fbx, water_line_z=None, solidity='soft-passable', burns=False,
                                      collider='none', moving_parts=None,
                                      notes='pivot at the cluster base (stem feet reach 12 mm below to sink into '
                                            'stumps/logs); no wind'))
        allv.append(objs)
        if render:
            for j, (az, el) in enumerate([(-20, 15), (30, 12), (80, 12)]):
                f = os.path.join(OUT, '%s_%d.png' % (vid, j))
                sk.render(objs, f, az=az, el=el, size=(300, 260), samples=8)
                tiles.append(f); labels.append('%s %s az%d' % (vid, name, az))
    if render:
        sk.sheet(tiles, os.path.join(OUT, 'card.jpg'), cols=3, labels=labels, concept=SHEET,
                 title='SW06 Pale mushrooms — concept (top) vs model')
    json.dump(recs, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)
    plib.side_by_side(allv, gap=0.15)
    sk.save_blend(os.path.join(OUT, 'SW06_pale_mushrooms.blend'))
    for r in recs:
        print(r['id'], r['tris'], r['size_xyz_m'], r['min_z'])

if __name__ == '__main__':
    main('--norender' not in sys.argv)
