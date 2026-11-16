import sys, os, random
sys.path.insert(0, '/home/claude/ant/models')
from shell_lib import *
from detail_lib import *

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
ak.reset()


def forbid(lo, hi):
    return lo.x < -1.97 or hi.x > 1.97 or lo.z < -0.002 or lo.y < -1.0 or hi.y > 0.95


FRONT = lambda p, n: abs(p.x) < 1.92 and n.y < -0.3 and p.z > 0.06
UNDER = lambda p, n: abs(p.x) < 1.8 and n.z < -0.25 and p.z > 0.3 and n.y < 0.3
TOP = lambda p, n: abs(p.x) < 1.9 and n.z > 0.6
BACK = lambda p, n: abs(p.x) < 1.8 and n.y > 0.4 and p.z > 0.05
SIDE_UP = lambda p, n: abs(p.x) < 1.8 and n.y < -0.3 and p.z > 2.6


def dress_wall(s, rnd):
    rays = [((x, -1.6, 0.07), (0, 1, 0)) for x in (-1.2, -0.6, 0.0, 0.6, 1.2)]
    dress_shell(s, rnd, forbid, FRONT, UNDER, TOP, BACK, SIDE_UP, k=1.0, rays=rays)


def build(vid, seed):
    rnd = random.Random(seed)
    s = Solid()
    PH = rnd.uniform(0, 6)
    xs = [-2, -1.8, -1.55, -1.3, -1.0, -0.7, -0.38, -0.05, 0.28, 0.6, 0.92, 1.24, 1.54, 1.8, 2]

    def ring(e, s_, i):
        w = smooth((e - 0.42) / 0.6)
        return wall_ring(w, rnd, depth=0.74, lift=0.26, brow=0.2, bulge=0.32, s=s_, ph=PH)
    sweep(s, [(x, 0) for x in xs], ring, rnd=rnd, tan_jitter=0.03)
    bay_y = lambda z: -(0.40 + 0.04 * z - 0.74 * (0.62 + 0.38 * min(1, z / 1.0)) * (1 if z < 3.0 else 0.6))
    # strata slabs sticking out of the bay at the ledge rows, rubble at the foot
    for x, z, r in ((rnd.uniform(-1.0, -0.4), 1.16, (0.5, 0.22, 0.09)), (rnd.uniform(0.3, 0.9), 2.66, (0.46, 0.2, 0.08)),
                    (rnd.uniform(0.7, 1.1), 1.14, (0.32, 0.17, 0.08))):
        lump(s, (x, bay_y(z) - 0.06, z + 0.02), r, rnd, rings=3, sides=6, jitter=0.12, rot=rnd.uniform(-0.3, 0.3))
    for x, r in ((rnd.uniform(-0.7, -0.1), (0.34, 0.26, 0.2)), (rnd.uniform(0.6, 1.1), (0.22, 0.18, 0.14))):
        lump(s, (x, bay_y(0) - 0.2, 0.0), r, rnd, rings=3, sides=7, jitter=0.25, flat_z=0.0)
    dress_wall(s, rnd)
    objs = s.finish(vid)
    return s, objs


s, objs = build('AN01a', 11)
stats('AN01a', objs, s)
fbx = os.path.join(OUT, 'SM_AN01_EarthWall.fbx')
ak.export_fbx(objs, fbx)
JOINS = {
    'grid': 'wall centre line on y = 0, room (front) side = -Y; 4.0 m module, pivot = bottom centre of the centre line',
    'left_end': 'flat plane x = -2.0', 'right_end': 'flat plane x = +2.0',
    'end_profile_yz': [[-0.40, 0.0], [-0.56, 4.0], [0.40, 4.0], [0.40, 0.0]],
    'end_profile_note': 'identical at both ends and shared with AN02 corner arms: 0.8 m thick at the floor, front leans 0.16 m into the room at the top (y=-0.56 at z=4), top z = 4.0 at the joins (crooked 4.0-4.25 between), back y=+0.40',
    'snap': 'next wall at x +/- 4.0 (same y); bend a run by rotating about a join end (gap on the outside is hidden in the earth); top joins AN03 vault feet at z = 4.0',
}
recs = [ak.variant_record('AN01a', 'Earth wall 4x4x0.8 (concave bay)', objs, fbx, solidity='solid',
                          collider='mesh (or box 4 x 0.8 x 4 m centred at y=0, z=2)', joins=JOINS, emissive=False,
                          notes='front: one big concave faceted bay with 2 strata ledges, 3 protruding strata slabs, ~150 embedded pebbles, clods, crumbling ledge slabs, erosion ribs, cracks (dark V-grooves), 5 burrow holes, root tips, hair rootlets hanging from the brow, clay drips, seed husks, and a rubble/crumb/pebble heap at the foot, thick rim pillars at the joins and a brow on top; back closed, slightly bulged; bottom flat at z=0.')]
write_manifest(OUT, recs)
ak.save_blend(os.path.join(HERE, 'AN01-earth-wall.blend'))
if wants_render():
    # in situ: three walls in a row + a scale man
    s2, w2 = build('AN01x', 12)
    s3, w3 = build('AN01y', 13)
    for o in w2:
        o.location.x = -4.0
    for o in w3:
        o.location.x = 4.0
    bpy.context.view_layer.update()
    hm = human(0.6, -1.5)
    card = renders(objs, OUT, os.path.join(SHEETS, '01-earth-wall.png'),
                   extra=[('IN SITU x3 + 1.8m', objs + w2 + w3 + hm, dict(az=-25, el=12))], title='AN01 earth wall')
    print(card)
