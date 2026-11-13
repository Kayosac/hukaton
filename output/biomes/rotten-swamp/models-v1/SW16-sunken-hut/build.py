"""SW16 Затонувшая изба (sunken hut) - 1 variant.  python3 build.py [norender]"""
import sys, os, math, json, random
sys.path.insert(0, '/home/claude/swamp/kit')
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sk, hutlib as H
from mathutils import Vector

D = os.path.dirname(os.path.abspath(__file__))
OUT = D + '/out'
SHEET = '/home/claude/ref/swamp/sheets/16-sunken-hut.png'


def build_sunken(vid='SW16a'):
    K = H.Kit(vid, seed=16)
    L, W, z0, rows, d, r = 5.0, 3.3, 0.0, 9, 0.26, 0.155
    win_c, win_w, win_zb, win_h = -1.3, 0.72, 1.28, 0.68
    door_c, door_w, door_zb, door_h = 0.95, 0.88, 0.32, 1.72
    bwin_c = 0.9
    openings = {
        'front': [(win_c - win_w / 2 - 0.03, win_c + win_w / 2 + 0.03, win_zb, win_zb + win_h),
                  (door_c - door_w / 2 - 0.02, door_c + door_w / 2 + 0.02, door_zb, door_zb + door_h)],
        'back': [(bwin_c - 0.3, bwin_c + 0.3, 1.35, 1.9)],
    }
    broken = {'back': {2: (-1.6, -0.9)}, 'left': {1: (-0.4, 0.3)}}
    H.cabin(K, L, W, z0, rows, d, r, ext=(0.2, 0.34), openings=openings, sides=7, seed=161, broken=broken)
    front, back = H.Wall('front', L, W), H.Wall('back', L, W)
    H.window(K, front, win_c, win_zb, win_w, win_h, r, seed=3)
    H.window(K, back, bwin_c, 1.38, 0.5, 0.48, r, seed=4)
    H.door(K, front, door_c, door_zb, door_w, door_h, r, seed=5, leaf=False)
    # a fallen door leaf leaning in the opening (half under water)
    H.beam(K, (door_c - 0.35, -W / 2 - 0.35, 0.05), (door_c - 0.2, -W / 2 - 0.18, 1.35), 0.42, 0.05, seed=7,
           mat='Planks', kinks=1, up=(0, -1, 0.2))
    # roof
    k = math.tan(math.radians(34))
    top = z0 + d * (rows - 0.5) + r
    ov = 0.5
    ye = W / 2 + ov
    ze, zr = top - ov * k, top + W / 2 * k
    xa, xb = -2.95, 2.95
    cuts = {-1: [[(4.45, -0.2), (6.2, -0.2), (6.2, 1.25), (5.75, 1.0), (5.5, 0.72), (5.15, 0.55), (4.85, 0.25)]]}
    H.roof(K, xa, xb, ye, ze, zr, thick=0.1, seed=162, cuts=cuts, moss=0.55, drip=0.8, bias=0.6, barge_cut={(-1, 1): 0.45},
           slab_w=(1.5, 2.3), t_split=0.55)
    # gables (vertical boards) + beam at the gable foot
    gz = z0 + d * (rows - 1) + r
    for out in (-1, 1):
        x = out * (L / 2 + r * 0.85)
        H.gable(K, x, out, W / 2 + r, gz, zr, ze + (ye - (W / 2 + r)) * 0 , ye, seed=170 + out,
                missing=(3,) if out > 0 else (), roof_thick=0.0, beam_z=gz + 0.02, beam_len=W / 2 + 0.35)
    # chimney
    H.chimney(K, -1.35, 0.42, zr - 1.0, zr + 0.8, s=0.52, seed=9)
    # moss on log ends / near the water line
    def filt(c):
        return 0.5 < c.z < 1.5 or abs(c.x) > L / 2 + 0.12 or abs(c.y) > W / 2 + 0.12
    K.drape('LogWall', filt=filt, keep=0.3, min_nz=0.55, seed=11)
    K.drape('Planks', filt=lambda c: c.z < 2.0, keep=0.5, min_nz=0.6, seed=12)
    # moss drips from the gable beams / log ends at a few spots
    rnd = random.Random(5)
    for i in range(10):
        sx = rnd.choice((-1, 1)); sy = rnd.choice((-1, 1))
        zz = rnd.uniform(0.9, 2.4)
        p = Vector((sx * (L / 2 + 0.22), sy * (W / 2 + rnd.uniform(-0.05, 0.05)), zz + r))
        H.drip_card(K, p + Vector((0, sy * 0.2, 0)), Vector((0.12, 0, 0)), rnd.uniform(0.15, 0.3), rnd)
    objs = K.build()

    water_local = 1.1
    tx, ty = math.radians(3.5), math.radians(5.0)

    def fn(p):
        x, y, z = p.x, p.y, p.z
        # ridge/roof sag in the middle + broken front-right corner droops
        z -= 0.17 * max(0.0, 1 - (x / 3.1) ** 2) * H.smooth(2.2, 3.2, z)
        z -= 0.32 * H.smooth(0.8, 3.0, x) * H.smooth(-0.6, -2.3, y) * H.smooth(1.6, 2.5, z)
        # whole front-right corner sinks deeper, walls skew a bit
        z -= 0.18 * H.smooth(-2.0, 2.6, x) * H.smooth(1.2, -1.8, y)
        x += 0.05 * z * H.smooth(-1, 1, y)
        # tilt: front sinks (roll about X), right end sinks (about Y)
        y1 = y * math.cos(tx) - z * math.sin(tx)
        z1 = y * math.sin(tx) + z * math.cos(tx)
        x2 = x * math.cos(ty) + z1 * math.sin(ty)
        z2 = -x * math.sin(ty) + z1 * math.cos(ty)
        return Vector((x2, y1, z2))

    H.deform(objs, fn)
    wl = fn(Vector((0, 0, water_local))).z
    dz = sk.ground(objs)
    return objs, wl + dz.z


def main(render=True):
    sk.reset()
    objs, wz = build_sunken()
    fbx = OUT + '/SM_SW16_SunkenHut.fbx'
    sk.export_fbx(objs, fbx)
    rec = sk.variant_record('SW16a', 'SunkenHut', objs, fbx,
                            water_line_z=round(wz, 3), solidity='solid; roof = walk-on-top (reward)', burns=False,
                            collider='walls: one convex/box hull of the log box (NOT the protruding log ends, '
                                     'casings or drips, so the corners do not become a ladder); roof: 2 tilted box '
                                     'colliders along the two roof planes (back slope ~30 deg after the tilt is the '
                                     'standable one, front ~38 deg); chimney box; Plants/Moss no collider',
                            moving_parts='none',
                            notes='Upright log cabin modelled whole (lower logs kept), then roll-tilted 3.5 deg toward '
                                  'the front, 5 deg toward +X, sagged ridge and drooping broken front-right eave. '
                                  'Door opening (front, under water) is a dark atlas quad; window pane atlas quad.')
    json.dump([rec], open(OUT + '/manifest.json', 'w'), indent=1, ensure_ascii=False)
    print(json.dumps(rec, ensure_ascii=False))
    sk.save_blend(OUT + '/SW16_SunkenHut.blend')
    if render:
        views = [('front34', -40, 12), ('gable', 95, 10), ('back', 200, 12), ('top', -20, 55),
                 ('w_front34', -40, 14), ('w_back', 160, 22)]
        tiles = H.render_views(objs, D + '/work', 'a', views, water_z=wz)
        sk.sheet(tiles, OUT + '/card.jpg', cols=3, labels=['SW16a ' + v[0] for v in views], concept=SHEET,
                 title='SW16 Sunken hut  tris=%d  size=%s  water_line_z=%.2f' % (rec['tris'], rec['size_xyz_m'], wz))


if __name__ == '__main__':
    main(render='norender' not in sys.argv)
