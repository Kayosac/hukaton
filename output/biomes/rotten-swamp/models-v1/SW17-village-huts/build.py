"""SW17 Избы деревни (village huts) - 3 variants, full bodies.  python3 build.py [norender] [a|b|c]"""
import sys, os, math, json, random
sys.path.insert(0, '/home/claude/swamp/kit')
sys.path.insert(0, '/home/claude/swamp/items/SW16-sunken-hut')
import sk, hutlib as H
from mathutils import Vector

D = os.path.dirname(os.path.abspath(__file__))
OUT = D + '/out'
SHEET = '/home/claude/ref/swamp/sheets/17-village-huts-full-bodies.png'


def rot_z90(p):
    return Vector((-p.y, p.x, p.z))


# ------------------------------------------------------------------ A: main hut with porch
def hut_a(vid='SW17a'):
    K = H.Kit(vid, seed=171)
    L, W = 4.4, 3.6
    fz = 0.42
    H.stone_plinth(K, L, W, 0.0, h=0.21, courses=2, seed=1)
    d, r, rows = 0.25, 0.135, 9
    sq = (1.0, 0.72)
    wins = [-1.5, -0.55, 1.2]
    ww, wh, wzb = 0.56, 0.6, fz + 0.95
    openings = {'front': [(c - ww / 2 - 0.03, c + ww / 2 + 0.03, wzb, wzb + wh) for c in wins],
                'back': [(0.9 - 0.27, 0.9 + 0.27, wzb + 0.05, wzb + 0.55)]}
    tops = H.cabin(K, L, W, fz, rows, d, r, openings=openings, sides=6, rot=0.0, sq=sq, seed=172, flush=True,
                   skip_walls=('right',))
    top = fz + d * (rows - 0.5) + r
    front, back, right = H.Wall('front', L, W), H.Wall('back', L, W), H.Wall('right', L, W)
    for i, c in enumerate(wins):
        H.window(K, front, c, wzb, ww, wh, r, seed=10 + i)
    H.window(K, back, 0.9, wzb + 0.05, 0.48, 0.5, r, seed=14)
    # corner + mid posts
    rnd = random.Random(5)
    for (x, y) in ((-L / 2, -W / 2), (L / 2, -W / 2), (-L / 2, W / 2), (L / 2, W / 2), (0.32, -W / 2), (-0.4, W / 2)):
        H.post(K, x + (0.0 if abs(x) > 1 else 0), y + (-0.04 if y < 0 else 0.04), fz - 0.06, top + 0.06,
               w=0.26, seed=rnd.randint(0, 999), lean=(rnd.uniform(-.05, .05), 0))
    # roof
    k = math.tan(math.radians(44))
    ov = 0.45
    ye = W / 2 + ov
    ze, zr = top - ov * k, top + W / 2 * k
    porch = 1.3
    xa, xb = -L / 2 - 0.42, L / 2 + porch + 0.3
    H.roof(K, xa, xb, ye, ze, zr, thick=0.09, seed=173, moss=0.36, drip=0.7, bias=0.5, slab_w=(0.9, 1.5))
    gz = top - 0.1
    H.gable(K, -L / 2 - r * 0.8, -1, W / 2 + 0.05, gz, zr, ze, ye, seed=33, beam_z=gz, beam_len=W / 2 + 0.3)
    # plank end wall (porch side), full height up to the roof, with a door
    H.gable(K, L / 2 + 0.03, 1, W / 2 + 0.02, fz, zr, ze, ye, seed=34, bw=(0.17, 0.26), beam_z=gz, beam_len=W / 2 + 0.2)
    H.door(K, right, 0.0, fz + 0.18, 0.86, 1.75, 0.0, seed=35, dark=False, leaf_d=0.06, frame_d=0.12, threshold=False)
    H.chimney(K, -1.05, -0.4, zr - 1.2, zr + 1.05, s=0.55, seed=36)
    H.floor(K, L - 0.1, W - 0.15, fz + 0.03, seed=37)
    # porch: joists, deck, posts, railing, steps
    x0, x1 = L / 2 + 0.05, L / 2 + porch
    dz = fz + 0.12
    for y in (-W / 2 + 0.1, 0.0, W / 2 - 0.1):
        H.beam(K, (x0, y, dz - 0.12), (x1 + 0.05, y, dz - 0.14), 0.14, 0.14, seed=rnd.randint(0, 999), mat='Bark')
    y = -W / 2 - 0.05
    while y < W / 2 + 0.05:
        bw = rnd.uniform(0.2, 0.28)
        H.beam(K, (x0, y + bw / 2, dz), (x1 + 0.08, y + bw / 2 + rnd.uniform(-.03, .03), dz + rnd.uniform(-.03, .01)),
               bw - 0.02, 0.05, seed=rnd.randint(0, 999), kinks=1)
        y += bw
    for y in (-W / 2 + 0.05, 0.05, W / 2 - 0.05):
        H.post(K, x1 - 0.08, y, 0.0, top + 0.02 + (x1 - L / 2) * 0, w=0.17, seed=rnd.randint(0, 999),
               lean=(rnd.uniform(-.06, .06), rnd.uniform(-.04, .04)))
    # eave beam over porch posts
    H.beam(K, (x1 - 0.08, -W / 2 - 0.2, top + 0.08), (x1 - 0.08, W / 2 + 0.2, top + 0.1), 0.16, 0.18, seed=7, mat='Bark')
    # railing: +X side (back half) and back side
    rz = dz + 0.85
    H.beam(K, (x1 - 0.08, 0.05, rz), (x1 - 0.08, W / 2 - 0.05, rz + 0.04), 0.09, 0.08, seed=8)
    H.beam(K, (x1 - 0.08, 0.05, dz + 0.45), (x1 - 0.08, W / 2 - 0.05, dz + 0.42), 0.07, 0.07, seed=9)
    H.beam(K, (x0 + 0.1, W / 2 - 0.05, rz), (x1 - 0.08, W / 2 - 0.05, rz - 0.03), 0.09, 0.08, seed=10)
    H.beam(K, (x0 + 0.1, W / 2 - 0.05, dz + 0.45), (x1 - 0.08, W / 2 - 0.05, dz + 0.47), 0.07, 0.07, seed=11)
    for y in (0.6, 1.15):
        H.beam(K, (x1 - 0.08, y, dz), (x1 - 0.08, y + 0.03, rz), 0.06, 0.06, seed=rnd.randint(0, 999))
    # steps down to the front (-Y)
    for i in range(3):
        sz = dz - 0.17 * (i + 1)
        sy = -W / 2 - 0.15 - 0.3 * i
        H.beam(K, (x0 + 0.1, sy, sz + 0.03), (x1 - 0.1, sy + rnd.uniform(-.03, .03), sz), 0.3, 0.1,
               seed=rnd.randint(0, 999), kinks=1)
    for x in (x0 + 0.12, x1 - 0.12):
        H.beam(K, (x, -W / 2, dz - 0.05), (x, -W / 2 - 1.0, -0.05), 0.08, 0.2, seed=rnd.randint(0, 999), kinks=0,
               up=(0, 0.5, 1))
    K.drape('Planks', filt=lambda c: c.z < 1.0, keep=0.35, seed=4)
    objs = K.build()
    # cut the full-height plank end wall boards: gable() with z=99 made boards up to 99 m -> clamp to the roof

    def fn(p):
        x, y, z = p.x, p.y, p.z
        z -= 0.1 * max(0.0, 1 - (x / 3.2) ** 2) * H.smooth(2.6, 4.2, z)       # ridge sag
        x += 0.03 * z + 0.04 * z * H.smooth(-1.5, 1.5, y)                    # slight lean / twist
        y += 0.025 * z * H.smooth(-2, 2, x)
        z -= 0.04 * x
        return Vector((x, y, z))
    H.deform(objs, fn)
    sk.ground(objs)
    levels = {'threshold': round(fz + 0.2, 2), 'windows': round(wzb + 0.1, 2), 'roof_only': round(top + 0.3, 2)}
    return objs, levels


def clamp_boards(objs, vid, x_plane, ze, zr, ye, zmax_bottom):
    """Plank end walls made with gable(z0..99): squash vertices above the roof underside down to it."""
    for o in objs:
        if not o.name.endswith('_Planks'):
            continue
        for v in o.data.vertices:
            if abs(v.co.x - x_plane) < 0.09 and v.co.z > 50:
                y = v.co.y
                v.co.z = zr - (zr - ze) * abs(y) / ye - 0.02
        o.data.update()


# ------------------------------------------------------------------ B: narrow leaning hut (gable to the front)
def hut_b(vid='SW17b'):
    # built with the ridge along X, then turned 90 deg so the windowed gable faces -Y
    K = H.Kit(vid, seed=181)
    L, W = 3.2, 3.6          # L -> depth (Y), W -> width (X) after the turn
    fz = 0.4
    H.stone_plinth(K, L, W, 0.0, h=0.2, courses=2, seed=2, depth=0.34)
    d, r, rows = 0.25, 0.135, 8
    sq = (1.0, 0.72)
    ww, wh, wzb = 0.6, 0.62, fz + 0.85
    wins = [-0.8, 0.85]       # along local y on the local LEFT wall (final front)
    side_win = -0.3
    openings = {'left': [(c - ww / 2 - 0.03, c + ww / 2 + 0.03, wzb, wzb + wh) for c in wins],
                'front': [(side_win - 0.25, side_win + 0.25, wzb + 0.1, wzb + 0.55)]}
    H.cabin(K, L, W, fz, rows, d, r, openings=openings, sides=6, rot=0.0, sq=sq, seed=182, flush=True,
            skip_walls=('right',), broken={'back': {3: (-0.6, 0.2)}})
    top = fz + d * (rows - 0.5) + r
    left, right, front = H.Wall('left', L, W), H.Wall('right', L, W), H.Wall('front', L, W)
    for i, c in enumerate(wins):
        H.window(K, left, c, wzb, ww, wh, r, seed=20 + i)
    H.window(K, front, side_win, wzb + 0.12, 0.42, 0.42, r, seed=23)
    rnd = random.Random(7)
    for (x, y) in ((-L / 2, -W / 2), (L / 2, -W / 2), (-L / 2, W / 2), (L / 2, W / 2), (-L / 2, 0.02), (0.1, W / 2)):
        H.post(K, x + (-0.04 if x < 0 else 0.04) * (abs(y) < 0.1), y, fz - 0.06, top + 0.05, w=0.26,
               seed=rnd.randint(0, 999))
    # plank back wall with boarded door
    H.door(K, right, -0.35, fz + 0.12, 0.8, 1.7, 0.0, seed=42, dark=False, leaf_d=0.06, frame_d=0.12,
           threshold=False, boarded=True)
    # mid-height cross beam on the plank wall (as on the sheet)
    H.beam(K, (L / 2 + 0.1, -W / 2 - 0.1, fz + 1.95), (L / 2 + 0.1, W / 2 + 0.1, fz + 2.0), 0.1, 0.18, seed=43)
    k = math.tan(math.radians(52))
    ov = 0.45
    ye = W / 2 + ov
    ze, zr = top - ov * k, top + W / 2 * k
    xa, xb = -L / 2 - 0.4, L / 2 + 0.35
    holes = {-1: [[(1.9, 1.25), (2.45, 1.2), (2.5, 1.75), (2.15, 1.85), (1.95, 1.6)]],
             1: [[(1.0, 1.15), (1.45, 1.1), (1.55, 1.6), (1.2, 1.7)]]}
    H.roof(K, xa, xb, ye, ze, zr, thick=0.09, seed=183, holes=holes, moss=0.48, drip=0.65, bias=0.5,
           slab_w=(0.8, 1.3), t_split=0.5)
    gz = top - 0.1
    H.gable(K, -L / 2 - r * 0.8, -1, W / 2 + 0.05, gz, zr, ze, ye, seed=44, beam_z=gz, beam_len=W / 2 + 0.3,
            missing=(5,))
    H.gable(K, L / 2 + 0.03, 1, W / 2 + 0.02, fz, zr, ze, ye, seed=45, bw=(0.18, 0.3))
    # gable framing: tie beam + diagonal rafters-posts on both gables (A-frame look)
    for xg, sg in ((-L / 2 - r * 0.8 - 0.06, -1), (L / 2 + 0.1, 1)):
        if sg > 0:
            H.beam(K, (xg, -W / 2 - 0.15, gz + 0.02), (xg, W / 2 + 0.15, gz + 0.04), 0.1, 0.2, seed=60)
        for sy in (-1, 1):
            H.beam(K, (xg, sy * (W / 2 + 0.02), fz + 0.1 if sg < 0 else gz), (xg, sy * 0.3, zr - 0.45), 0.1, 0.18,
                   seed=61 + sy + sg * 3, kinks=1)
        H.beam(K, (xg, 0.0, gz + 0.1), (xg, 0.02, zr - 0.25), 0.09, 0.16, seed=66 + sg, kinks=0)
    H.chimney(K, 0.35, 0.45, zr - 1.0, zr + 0.6, s=0.5, seed=46)
    H.floor(K, L - 0.1, W - 0.15, fz + 0.03, seed=47)
    objs = K.build()

    def fn(p):
        x, y, z = p.x, p.y, p.z
        zt = min(z, top) / top
        y *= 1 - 0.3 * zt           # walls taper inward (trapezoid front)
        x *= 1 - 0.14 * zt
        z -= 0.12 * max(0.0, 1 - (x / 2.2) ** 2) * H.smooth(top, zr, z)
        q = rot_z90(Vector((x, y, z)))
        q.x += 0.15 * q.z            # the whole hut leans to +X
        q.y += 0.03 * q.z
        return q
    H.deform(objs, fn)
    sk.ground(objs)
    levels = {'threshold': round(fz + 0.2, 2), 'windows': round(wzb + 0.1, 2), 'roof_only': round(top + 0.2, 2)}
    return objs, levels


# ------------------------------------------------------------------ C: low sagging log hut
def hut_c(vid='SW17c'):
    K = H.Kit(vid, seed=191)
    L, W = 4.8, 2.9
    fz = 0.34
    H.stone_plinth(K, L, W, 0.0, h=0.17, courses=2, seed=3, out=0.1, depth=0.42)
    d, r, rows = 0.26, 0.155, 8
    win_c, ww, wh, wzb = -1.3, 0.6, 0.55, fz + 0.85
    door_c, dw, dh = 0.75, 0.8, 1.55
    rdoor_c = 0.0
    openings = {'front': [(win_c - ww / 2 - 0.03, win_c + ww / 2 + 0.03, wzb, wzb + wh),
                          (door_c - dw / 2 - 0.02, door_c + dw / 2 + 0.02, fz + 0.1, fz + 0.1 + dh)],
                'back': [(0.6, 1.1, wzb + 0.05, wzb + 0.5)],
                'right': [(rdoor_c - dw / 2 - 0.02, rdoor_c + dw / 2 + 0.02, fz + 0.1, fz + 0.1 + dh)]}
    H.cabin(K, L, W, fz, rows, d, r, ext=(0.2, 0.32), openings=openings, sides=7, seed=192,
            mid_ends={'front': [-0.3], 'back': [-0.3]})
    top = fz + d * (rows - 0.5) + r
    front, back, right = H.Wall('front', L, W), H.Wall('back', L, W), H.Wall('right', L, W)
    H.window(K, front, win_c, wzb, ww, wh, r, seed=50)
    H.window(K, back, 0.85, wzb + 0.05, 0.44, 0.42, r, seed=51)
    H.door(K, front, door_c, fz + 0.1, dw, dh, r, seed=52, leaf=True, leaf_d=-0.02)
    H.door(K, right, rdoor_c, fz + 0.1, dw, dh, r, seed=53, leaf=True, leaf_d=-0.02)
    k = math.tan(math.radians(40))
    ov = 0.38
    ye = W / 2 + ov
    ze, zr = top - ov * k, top + W / 2 * k
    xa, xb = -2.95, 2.95
    holes = {-1: [[(3.9, 0.95), (4.5, 0.9), (4.55, 1.3), (4.2, 1.42), (3.95, 1.25)]],
             1: [[(0.9, 0.85), (1.35, 0.8), (1.4, 1.2), (1.05, 1.3)]]}
    H.roof(K, xa, xb, ye, ze, zr, thick=0.09, seed=193, holes=holes, moss=0.5, drip=0.7, bias=0.55,
           slab_w=(1.0, 1.6))
    gz = fz + d * (rows - 1) + r
    for out in (-1, 1):
        H.gable(K, out * (L / 2 + r * 0.85), out, W / 2 + r, gz, zr, ze, ye, seed=55 + out, beam_z=gz + 0.02,
                beam_len=W / 2 + 0.3)
    H.chimney(K, -0.45, 0.35, zr - 1.0, zr + 1.2, s=0.55, seed=57)
    H.floor(K, L - 0.2, W - 0.2, fz + 0.02, seed=58)
    K.drape('LogWall', filt=lambda c: abs(c.x) > L / 2 + 0.12 or abs(c.y) > W / 2 + 0.12, keep=0.3,
            seed=6)
    objs = K.build()

    def fn(p):
        x, y, z = p.x, p.y, p.z
        bow = max(0.0, 1 - (x / 3.0) ** 2)
        z -= 0.1 * bow * H.smooth(0.3, top, z)              # walls settle in the middle
        z -= 0.2 * bow * H.smooth(top, zr, z)               # ridge sags
        x += 0.035 * z
        z -= 0.03 * x + 0.02 * y
        return Vector((x, y, z))
    H.deform(objs, fn)
    sk.ground(objs)
    levels = {'threshold': round(fz + 0.15, 2), 'windows': round(wzb + 0.1, 2), 'roof_only': round(top + 0.15, 2)}
    return objs, levels


VARIANTS = [('a', 'SW17a', 'MainHutPorch', hut_a), ('b', 'SW17b', 'NarrowLeaningHut', hut_b),
            ('c', 'SW17c', 'LowSaggingHut', hut_c)]


def main(render=True, only=None):
    sk.reset()
    recs, allobjs = [], []
    off = 0.0
    tiles, labels = [], []
    for key, vid, name, fn in VARIANTS:
        objs, levels = fn(vid)
        fbx = OUT + '/SM_SW17_%s.fbx' % name
        sk.export_fbx(objs, fbx)
        rec = sk.variant_record(vid, name, objs, fbx, water_line_z=None, flood_levels_z=levels,
                                solidity='solid', burns=False,
                                collider='box/convex hull of the log walls (+ plank end wall); 2 box colliders along '
                                         'the roof planes; porch deck/steps boxes (A); log ends, casings, posts, '
                                         'drips: no separate colliders',
                                moving_parts='none',
                                notes='Full body (foundation blocks to chimney); flooding is done by placement. '
                                      'flood_levels_z = suggested water heights for threshold / windows / roof-only.')
        recs.append(rec)
        print(json.dumps({k: rec[k] for k in ('id', 'tris', 'tris_by_material', 'size_xyz_m')}))
        if render and (only is None or key in only):
            views = [('front34', -38, 12), ('back34', 148, 12), ('side', 92, 6)]
            if key == 'b':
                views = [('front34', -32, 10), ('back34', 140, 12), ('side', 92, 6)]
            t = H.render_views(objs, D + '/work', vid, views, size=(420, 420), samples=10)
            tiles += t
            labels += ['%s %s' % (vid, v[0]) for v in views]
        # place side by side in the .blend
        for o in objs:
            o.location.x = off
        off += 9.0
        allobjs += objs
    json.dump(recs, open(OUT + '/manifest.json', 'w'), indent=1, ensure_ascii=False)
    for o in allobjs:
        pass
    sk.save_blend(OUT + '/SW17_VillageHuts.blend')
    if tiles:
        for o in allobjs:
            o.location.x = 0
        sk.sheet(tiles, OUT + '/card.jpg', cols=3, labels=labels, concept=SHEET,
                 title='SW17 Village huts  ' + '  '.join('%s %dtris %s' % (r['id'], r['tris'], r['size_xyz_m']) for r in recs))


if __name__ == '__main__':
    only = [a for a in sys.argv[1:] if a in ('a', 'b', 'c')] or None
    main(render='norender' not in sys.argv, only=only)
