import sys, os, random
sys.path.insert(0, '/home/claude/ant/models')
from shell_lib import *

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
ak.reset()
XS = [-1.5, -1.42, -1.22, -0.93, -0.6, -0.2, 0.22, 0.6, 0.94, 1.22, 1.42, 1.5]


def zr(x):
    return 0.78 + 1.27 * math.sqrt(max(0.0, 1 - (x / 1.12) ** 2))


def ring(x, rnd, end):
    r = 1.0 if abs(x) < 0.95 else 0.0
    top = 2.4 - 0.4 * (abs(x) / 1.5) ** 2 - 0.12 * x
    rim = [(-0.62, 0.78), (-0.66, 1.35), (-0.6, min(1.95, top - 0.45)), (-0.5, min(2.22, top - 0.16))]
    rec = [(0.22, 0.72), (0.32, 1.35), (0.18, zr(x) - 0.22), (-0.44, zr(x))]
    mid = [(a[0] * (1 - r) + b[0] * r, a[1] * (1 - r) + b[1] * r) for a, b in zip(rim, rec)]
    R = [(-0.55, 0.0), (-0.8, 0.32), (-0.92, 0.6), (-0.87, 0.74)] + mid + \
        [(-0.36, top), (0.12, top + 0.1), (0.58, top - 0.2), (0.75, 1.3), (0.75, 0.0)]
    J = (lambda a: rnd.uniform(-a, a))
    out = []
    for k, (y, z) in enumerate(R):
        if k in (3, 4):                      # keep the shelf top level (walkable / placeable)
            out.append((y + J(0.04), z + J(0.012)))
        elif y >= 0.75 - 1e-6:              # flat back plane
            out.append((y, z + (J(0.08) if 0 < z < 2 else 0)))
        else:
            out.append((y + J(0.06), z + (0 if z == 0 else J(0.07))))
    if end:                                   # rounded closed sides
        out = [(0.75 + (y - 0.75) * 0.7 if y < 0.75 else y, z * 0.82 if k > 3 else z) for k, (y, z) in enumerate(out)]
    return out


def build(vid, seed):
    rnd = random.Random(seed)
    s = Solid()
    secs = []
    for x in XS:
        end = abs(x) == 1.5
        xx = x + (0 if end else rnd.uniform(-0.05, 0.05))
        secs.append([Vector((xx + (0 if end else rnd.uniform(-0.04, 0.04)), y, z)) for y, z in ring(x, rnd, end)])
    n = len(secs[0])
    mats = ['Earth'] * n
    mats[3] = 'Floor'
    s.loft(secs, mats)
    return s, s.finish(vid)


s, objs = build('AN05a', 51)
stats('AN05a', objs, s)
fbx = os.path.join(OUT, 'SM_AN05_EarthNiche.fbx')
ak.export_fbx(objs, fbx)
JOINS = {
    'back': 'flat plane y = +0.75 (x -1.0..+1.0 fully flat, z 0..1.9): push it against / into an AN01 wall front (wall front is at y = -0.40 of the wall, so niche origin = wall origin + (0, -1.15..-0.9, 0)); sides are closed rounded earth, not join ends',
    'shelf': 'Floor top at z = 0.72-0.78 (level within 2 cm), recess 1.9 m wide x ~1.0 m deep, opening top 2.05 m',
}
recs = [ak.variant_record('AN05a', 'Earth niche with shelf 3 x 2.5 x 1.7 m', objs, fbx, solidity='solid (shelf walk-on-top / props on top)',
                          collider='mesh, or box under the shelf + convex hulls for the sides/top', joins=JOINS, emissive=False,
                          notes='closed-back shallow arched recess with a wide protruding shelf lip along the whole front; shelf top is Floor material; back closed and flat.')]
write_manifest(OUT, recs)
ak.save_blend(os.path.join(HERE, 'AN05-earth-niche.blend'))
if wants_render():
    card = renders(objs, OUT, os.path.join(SHEETS, '05-earth-niche.png'), title='AN05 earth niche')
    print(card)
