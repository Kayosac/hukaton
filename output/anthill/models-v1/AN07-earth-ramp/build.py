import sys, os, random
sys.path.insert(0, '/home/claude/ant/models')
from shell_lib import *
from detail_lib import *

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
ak.reset()
TOE, TOP = 0.03, 1.0
TX = [-1.5, -1.36, -0.8, -0.25, 0.3, 0.85, 1.36, 1.5]     # top points across (lips at +/-1.5 rounded down)


def ring(h, rnd, end, sidew=1.0):
    J = (lambda a: 0.0) if end else (lambda a: rnd.uniform(-a, a))
    R = []
    for k, x in enumerate(TX):
        lip = k in (0, len(TX) - 1)
        z = h - (min(0.07, h * 0.5) if lip else 0) + (0 if lip else J(0.008))
        R.append((x + (J(0.03) if 0 < k < len(TX) - 1 else 0), z))
    # right side down, bottom, left side up (sides bulge and lean a little = crooked earth)
    R += [(1.5 + 0.06 * sidew + J(0.04), h * 0.55), (1.5 + J(0.03), 0.0),
          (-1.5 + J(0.03), 0.0), (-1.5 - 0.06 * sidew + J(0.04), h * 0.5)]
    return R


def hy(y):
    return TOE + (TOP - TOE) * (max(-2, min(2, y)) + 2) / 4


def forbid(lo, hi):
    if lo.y < -1.97 or hi.y > 1.97 or abs(lo.x) > 1.75 or abs(hi.x) > 1.75 or lo.z < -0.002:
        return True
    return hi.z > hy(hi.y) + 0.055


SIDES = lambda p, n: abs(n.x) > 0.5 and abs(p.y) < 1.9 and p.z > 0.04


def build(vid, seed):
    rnd = random.Random(seed)
    s = Solid()
    secs = []
    ys = [-2, -1.6, -1.1, -0.55, 0.0, 0.5, 1.05, 1.55, 2]
    for y in ys:
        end = abs(y) == 2
        h = TOE + (TOP - TOE) * (y + 2) / 4
        yy = y + (0 if end else rnd.uniform(-0.05, 0.05))
        secs.append([Vector((x, yy, z)) for x, z in ring(h, rnd, end, sidew=0 if y == -2 else 1)])
    n = len(secs[0])
    mats = ['Floor' if 0 < k < len(TX) - 2 else 'Earth' for k in range(n)]
    s.loft(secs, mats)
    rays = [((sx * 2.4, y, 0.05), (-sx, 0, 0)) for sx in (1, -1) for y in (-0.5, 0.5, 1.5)]
    dress_shell(s, rnd, forbid, SIDES, None, None, None, None, k=0.4, rays=rays, rubble=(14, 30, 24, 4))
    dress_walk(s, rnd, forbid, filt=lambda p, n: n.z > 0.8 and abs(p.x) < 1.4, k=0.75, along=Vector((0, 1, 0)), ruts=1)
    return s, s.finish(vid)


s, objs = build('AN07a', 71)
stats('AN07a', objs, s)
slope = max_slope(objs)
print('max walk slope', slope)
fbx = os.path.join(OUT, 'SM_AN07_EarthRamp.fbx')
ak.export_fbx(objs, fbx)
JOINS = {
    'grid': 'pivot bottom centre; 3 m wide (x +/-1.5), 4 m long (y -2..+2), rises toward +Y',
    'low_end': 'toe at y = -2.0, top edge z = 0.03 (sinks under AN06 floor tiles whose top is z = 0.15: no lip to step over)',
    'high_end': 'flat vertical face y = +2.0, x -1.5..+1.5, top edge z = 1.00 (flat across between the rounded lips)',
    'chain': 'continue at the high end with a floor/landing at z = 1.0, another AN07 placed at (0, +4, +0.97) on an earth base, or AN08 low end (top 0.5) placed at base z = +0.5 with its low-end centre (x=1.5 of AN08) on x = 0',
}
recs = [ak.variant_record('AN07a', 'Straight earth ramp 3 x 4 m, rise 1 m', objs, fbx, solidity='walk-on-top',
                          collider='mesh or convex hull (wedge)', joins=JOINS, emissive=False,
                          slope_deg=round(math.degrees(math.atan2(TOP - TOE, 4)), 1), max_face_slope_deg=round(slope, 1),
                          notes='wedge with a trodden Floor top (faceted +/-2 cm, no snags), rounded Earth lips, slightly bulging crooked Earth sides, closed bottom; thin toe vs 1 m high end. Details: top only flat (<= 5.5 cm: crumbs, flat pebbles, husks, needles, cracks, one rut track); sides with pebbles, clods, ledges, root tips, burrows, cracks, ridges, husks; rubble along the side foot.')]
write_manifest(OUT, recs)
ak.save_blend(os.path.join(HERE, 'AN07-earth-ramp.blend'))
if wants_render():
    hm = human(0.3, 0.6, 0.75)
    card = renders(objs, OUT, os.path.join(SHEETS, '07-earth-ramp.png'), az_off=90, el=22,
                   extra=[('SCALE 1.8 m man', objs + hm, dict(az=-60, el=15))], title='AN07 earth ramp (views turned +90: concept shows the ramp side-on)')
    print(card)
