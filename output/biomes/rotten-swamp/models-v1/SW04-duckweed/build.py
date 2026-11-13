"""SW04 Ряска — 4 duckweed patches floating on the water: a ragged patch, b crescent, c narrow band, d two joined
patches. Flat alpha cards (Plants duckweed_0..3), each split 2x2 with a few mm of z jitter (slightly uneven), layered
1-2 mm apart (no z-fighting), plus small loose bits around; open-water gaps come from the alpha.
Pivot = centre at the water surface (z = 0)."""
import sys, os, json, math, random
sys.path.insert(0, '/home/claude/swamp/items/_plants_lib')
import plib, sk
from plib import V

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)
SHEET = '/home/claude/ref/swamp/sheets/04-duckweed.png'

def patch(pl, rnd, c, size, region, rot, z, grid=2, zj=0.003, flutter=0.006):
    """Flat card centred at c (x,y) of size (sx,sy) rotated by rot, split grid x grid, z jittered."""
    u0, v0, u1, v1 = pl.region(region, 'Plants')
    ca, sa = math.cos(rot), math.sin(rot)
    P = {}
    for i in range(grid + 1):
        for j in range(grid + 1):
            x = (i / grid - .5) * size[0]; y = (j / grid - .5) * size[1]
            edge = i in (0, grid) or j in (0, grid)
            P[i, j] = (V((c[0] + x * ca - y * sa, c[1] + x * sa + y * ca, z + rnd.uniform(-zj, zj) * (1.0 if edge else .5))),
                       (u0 + (u1 - u0) * i / grid, v0 + (v1 - v0) * j / grid))
    w = (0.0, flutter)
    for i in range(grid):
        for j in range(grid):
            a, b, cc, d = P[i, j], P[i + 1, j], P[i + 1, j + 1], P[i, j + 1]
            pl.quad(a[0], b[0], cc[0], d[0], a[1], b[1], cc[1], d[1], [w] * 4)

def bits(pl, rnd, n, cx, cy, rx, ry, z=0.0035, inner=0.85):
    """Small loose clusters around the patch rim (single quads, small)."""
    for k in range(n):
        a = rnd.uniform(0, 2 * math.pi); f = rnd.uniform(inner, 1.25)
        s = rnd.uniform(.12, .26)
        patch(pl, rnd, (cx + math.cos(a) * rx * f, cy + math.sin(a) * ry * f), (s, s), rnd.choice(['duckweed_2', 'duckweed_3']),
              rnd.uniform(0, 6.3), z + 0.001 * (k % 3), grid=1, zj=0.001)

def dw_a(vid):
    rnd = random.Random(41); pl = sk.Mesh('Plants')
    patch(pl, rnd, (0, 0), (1.45, 1.35), 'duckweed_0', 0.2, 0.002)
    patch(pl, rnd, (0.42, -0.25), (0.7, 0.65), 'duckweed_3', 2.2, 0.0035)
    patch(pl, rnd, (-0.45, 0.2), (0.6, 0.6), 'duckweed_0', 4.0, 0.004)
    patch(pl, rnd, (0.1, 0.48), (0.55, 0.4), 'duckweed_2', 0.4, 0.0045, grid=1)
    bits(pl, rnd, 7, 0, 0, 0.75, 0.7)
    return plib.finish([pl], vid)

def dw_b(vid):
    rnd = random.Random(42); pl = sk.Mesh('Plants')
    patch(pl, rnd, (0, 0), (1.75, 1.75), 'duckweed_1', 0.0, 0.002)
    # thicken the arc: two band cards following the crescent (opening faces +X/+Y as on the sheet)
    patch(pl, rnd, (-0.55, -0.15), (0.9, 0.38), 'duckweed_2', 1.75, 0.0035, grid=1)
    patch(pl, rnd, (0.0, -0.6), (0.9, 0.38), 'duckweed_2', 0.25, 0.004, grid=1)
    bits(pl, rnd, 6, 0, 0, 0.9, 0.9)
    return plib.finish([pl], vid)

def dw_c(vid):
    rnd = random.Random(43); pl = sk.Mesh('Plants')
    for k, (x, y, r) in enumerate(((-0.9, -0.12, 0.25), (0.0, 0.05, -0.1), (0.9, 0.18, 0.3))):
        patch(pl, rnd, (x, y), (1.25, 0.62), 'duckweed_2', r + (math.pi if k == 1 else 0), 0.002 + 0.0015 * k)
    bits(pl, rnd, 7, 0, 0, 1.45, 0.4, inner=0.7)
    return plib.finish([pl], vid)

def dw_d(vid):
    rnd = random.Random(44); pl = sk.Mesh('Plants')
    patch(pl, rnd, (0.35, -0.1), (1.5, 1.3), 'duckweed_3', 0.1, 0.002)
    patch(pl, rnd, (-0.75, 0.25), (1.0, 0.95), 'duckweed_0', 1.3, 0.0035)
    patch(pl, rnd, (-0.2, 0.12), (0.9, 0.4), 'duckweed_2', 0.3, 0.0045, grid=1)
    bits(pl, rnd, 7, -0.1, 0.05, 1.2, 0.7)
    return plib.finish([pl], vid)

VARIANTS = [('SW04a', 'DuckweedPatch', dw_a), ('SW04b', 'DuckweedCrescent', dw_b), ('SW04c', 'DuckweedBand', dw_c),
            ('SW04d', 'DuckweedDouble', dw_d)]

def main(render=True):
    sk.reset()
    recs, allv, tiles, labels = [], [], [], []
    for vid, name, fn in VARIANTS:
        objs = fn(vid)
        plib.centre_xy(objs)   # keep z: pivot at the water surface
        fbx = os.path.join(OUT, 'SM_%s_%s.fbx' % (vid[:4], name))
        sk.export_fbx(objs, fbx)
        recs.append(sk.variant_record(vid, name, objs, fbx, water_line_z=0.0, solidity='surface', burns=False,
                                      collider='none', moving_parts=None,
                                      notes='pivot at the centre of the water surface; cards float 1-6 mm above it; '
                                            'gentle flutter in the wind UV2 (bob on water)'))
        allv.append(objs)
        if render:
            for j, (az, el, w) in enumerate([(-20, 50, -0.001), (25, 50, None), (0, 4, None)]):
                f = os.path.join(OUT, '%s_%d.png' % (vid, j))
                sk.render(objs, f, az=az, el=el, size=(300, 220), samples=8, water_z=w)
                tiles.append(f); labels.append('%s %s el%d%s' % (vid, name, el, ' water' if w is not None else ''))
    if render:
        sk.sheet(tiles, os.path.join(OUT, 'card.jpg'), cols=3, labels=labels, concept=SHEET,
                 title='SW04 Duckweed — concept (top) vs model')
    json.dump(recs, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)
    plib.side_by_side(allv)
    sk.save_blend(os.path.join(OUT, 'SW04_duckweed.blend'))
    for r in recs:
        print(r['id'], r['tris'], r['size_xyz_m'], r['min_z'])

if __name__ == '__main__':
    main('--norender' not in sys.argv)
