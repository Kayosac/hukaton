"""SW03 Осока и болотная трава — 3 sedge tufts (alpha cards): a vertical fan, b low tangled rosette, c one-sided lean.
Each tuft = bent V-folded single-blade strips (Plants sedge_blade_*) radiating from the root + crossing fan cards
(sedge_0..2) in the core + a small low moss/mud clump at the root (Moss)."""
import sys, os, json, math, random
sys.path.insert(0, '/home/claude/swamp/items/_plants_lib')
import plib, sk
from plib import V

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)
SHEET = '/home/claude/ref/swamp/sheets/03-sedge.png'

def root_clump(seed, rx, ry, h=0.05):
    """Low lumpy moss clump at the root (sinks into the ground/water when placed)."""
    rnd = random.Random(seed)
    m = sk.Mesh('Mud')
    ph = [rnd.uniform(0, 6.3) for _ in range(3)]; am = [0.12, 0.08, 0.05]
    r0 = plib.blob_ring((0, 0), rx, ry, -0.02, 9, rnd, 0.15, phase=ph, amps=am)
    r1 = plib.blob_ring((0, 0), rx * .55, ry * .55, h * .7, 9, rnd, 0.2, phase=ph, amps=am)
    plib.loft(m, [r0, r1], top_center=(0, 0, h))
    return m

def tuft_a(vid):
    rnd = random.Random(31)
    pl = sk.Mesh('Plants')
    plib.sedge_tuft(pl, (0, 0, 0), 0.56, n=10, rnd=rnd, spread=1.25, droop=1.0, width=0.095, fans=2,
                    fan_regions=('sedge_0',), fan_scale=0.85, radius=0.035)
    plib.sedge_tuft(pl, (0, 0, 0), 0.64, n=4, rnd=rnd, spread=0.3, droop=0.7, width=0.09, fans=0, radius=0.02)
    plib.sedge_tuft(pl, (0, 0, 0), 0.22, n=4, rnd=rnd, spread=1.2, droop=1.0, width=0.07, fans=1,
                    fan_regions=('grass_low',), fan_scale=0.8, radius=0.06)
    return plib.finish([pl, root_clump(1, 0.11, 0.1, 0.04)], vid)

def tuft_b(vid):
    rnd = random.Random(32)
    pl = sk.Mesh('Plants')
    plib.sedge_tuft(pl, (0, 0, 0), 0.24, n=15, rnd=rnd, spread=1.2, droop=0.9, width=0.09, fans=2,
                    fan_regions=('sedge_1',), fan_scale=0.75, radius=0.07, tall_center=False, segs=5, min_tilt=0.75)
    plib.sedge_tuft(pl, (0, 0, 0), 0.26, n=5, rnd=rnd, spread=0.9, droop=1.0, width=0.08, fans=0, radius=0.03,
                    min_tilt=0.35)
    objs = plib.finish([pl, root_clump(2, 0.15, 0.12, 0.035)], vid)
    for o in objs:   # squash slightly to the 0.7 x 0.6 footprint
        o.data.transform(__import__('mathutils').Matrix.Diagonal((1.2, 1.05, 1.0, 1)))
    return objs

def tuft_c(vid):
    rnd = random.Random(33)
    pl = sk.Mesh('Plants')
    lean_az = 0.0   # leans to +X (right in the front view)
    plib.sedge_tuft(pl, (0, 0, 0), 0.42, n=11, rnd=rnd, spread=0.7, droop=0.5, width=0.095, fans=2,
                    fan_regions=('sedge_2',), fan_scale=0.9, radius=0.03, lean_az=lean_az, lean=1.1, lean_len=0.6)
    plib.sedge_tuft(pl, (-0.03, 0, 0), 0.25, n=4, rnd=rnd, spread=1.0, droop=0.8, width=0.075, fans=0,
                    radius=0.04, lean_az=lean_az, lean=0.4)
    return plib.finish([pl, root_clump(3, 0.1, 0.09, 0.04)], vid)

VARIANTS = [('SW03a', 'SedgeFan', tuft_a), ('SW03b', 'SedgeRosette', tuft_b), ('SW03c', 'SedgeLean', tuft_c)]

def main(render=True):
    sk.reset()
    recs, allv, tiles, labels = [], [], [], []
    for vid, name, fn in VARIANTS:
        objs = fn(vid)
        sk.ground(objs)
        # ground() puts the lowest point (moss clump bottom, 2 cm under the root) at 0 -> root at ~0.02
        fbx = os.path.join(OUT, 'SM_%s_%s.fbx' % (vid[:4], name))
        sk.export_fbx(objs, fbx)
        recs.append(sk.variant_record(vid, name, objs, fbx, water_line_z=None, solidity='soft-passable',
                                      burns=True, collider='none (soft, passable)', moving_parts=None,
                                      notes='pivot at the root (bottom of the 2 cm moss clump); cards have wind UV2'))
        allv.append(objs)
        if render:
            for j, (az, el) in enumerate([(-25, 14), (30, 14), (90, 8)]):
                f = os.path.join(OUT, '%s_%d.png' % (vid, j))
                sk.render(objs, f, az=az, el=el, size=(300, 260), samples=8)
                tiles.append(f); labels.append('%s %s az%d' % (vid, name, az))
    if render:
        sk.sheet(tiles, os.path.join(OUT, 'card.jpg'), cols=3, labels=labels, concept=SHEET,
                 title='SW03 Sedge — concept (top) vs model')
    json.dump(recs, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)
    plib.side_by_side(allv)
    sk.save_blend(os.path.join(OUT, 'SW03_sedge.blend'))
    for r in recs:
        print(r['id'], r['tris'], r['size_xyz_m'])

if __name__ == '__main__':
    main('--norender' not in sys.argv)
