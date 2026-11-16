import sys, os, random
sys.path.insert(0, '/home/claude/ant/models')
from shell_lib import *

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
ak.reset()


def build(vid, seed):
    rnd = random.Random(seed)
    s = Solid()
    PH = rnd.uniform(0, 6)
    # centre line: arm 1 along +X on y=+2 (join at x=-2), 45-degree chamfer, arm 2 along -Y on x=+2 (join at y=-2)
    nodes = [(-2, 2), (-1.78, 2), (-1.45, 2), (-0.95, 2), (-0.45, 2), (0, 2)]
    nodes += [(0.5, 1.5), (1.0, 1.0), (1.5, 0.5)]
    nodes += [(2, 0), (2, -0.45), (2, -0.95), (2, -1.45), (2, -1.78), (2, -2)]

    def ring(e, s_, i):
        w = smooth((e - 0.42) / 0.75)
        mid = smooth((e - 1.6) / 1.4)          # extra overhang / height toward the inner corner
        return wall_ring(w, rnd, depth=0.70 + 0.08 * mid, lift=0.22 + 0.28 * mid, brow=0.2 + 0.18 * mid,
                         bulge=0.32 + 0.45 * mid, s=s_, ph=PH)
    sweep(s, nodes, ring, rnd=rnd, tan_jitter=0.03)
    for c, r in (((0.75, 0.85, 0.0), (0.38, 0.3, 0.24)), ((-0.9, 1.55, 0.0), (0.26, 0.2, 0.16)), ((1.5, -0.7, 0.0), (0.22, 0.26, 0.15))):
        lump(s, c, r, rnd, rings=3, sides=7, jitter=0.2, flat_z=0.0, rot=rnd.uniform(0, 1))
    return s, s.finish(vid)


s, objs = build('AN02a', 21)
stats('AN02a', objs, s)
fbx = os.path.join(OUT, 'SM_AN02_EarthCorner.fbx')
ak.export_fbx(objs, fbx)
JOINS = {
    'grid': 'pivot (0,0,0) = centre of the 4x4 m corner cell; the two wall centre lines run on y = +2 (arm along X) and x = +2 (arm along Y); room side = toward the pivot (-X/-Y quadrant); inside corner is a concave 3-face bay (two arms + 45-degree face)',
    'arm_x_end': 'flat plane x = -2.0; profile (y,z): (2.40,0) (2.44,4.0) (1.44,4.0) (1.60,0)  = AN01 end profile shifted to y=+2',
    'arm_y_end': 'flat plane y = -2.0; profile (x,z): (2.40,0) (2.40,4.0) (1.44,4.0) (1.60,0) = AN01 end profile rotated 90 deg, centre line x=+2',
    'snap': 'AN01 wall at (x=-4, y=+2, rot 0) meets arm_x_end; AN01 wall at (x=+2, y=-4, rot Z -90 deg so its front faces -X) meets arm_y_end. Outside corner: use the same piece from the back (convex earth mass).',
}
recs = [ak.variant_record('AN02a', 'Earth corner 90 deg concave (3 faces)', objs, fbx, solidity='solid',
                          collider='mesh (or 2 boxes along the arms)', joins=JOINS, emissive=False,
                          notes='wall section swept along an L with a 45-degree chamfer: one big concave bay over three faces, brow higher and overhanging toward the corner; back = convex rounded earth mass; 3 rubble clods at the foot; bottom flat.')]
write_manifest(OUT, recs)
ak.save_blend(os.path.join(HERE, 'AN02-earth-corner.blend'))
if wants_render():
    sys.path.insert(0, '/home/claude/ant/models/AN01-earth-wall')
    card = renders(objs, OUT, os.path.join(SHEETS, '02-earth-corner.png'), title='AN02 earth corner (views turned -45 deg: concept front = corner bisector)', az_off=-45)
    print(card)
