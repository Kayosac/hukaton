import sys, os, random
sys.path.insert(0, '/home/claude/ant/models')
from shell_lib import *

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
ak.reset()
UX = [-1.22, -0.92, -0.58, -0.2, 0.2, 0.58, 0.92, 1.22]
TX = [2.25, 1.6, 0.8, 0.0, -0.8, -1.6, -2.25]


def ring(y, rnd, end):
    f = (1 - math.cos(math.pi * y / 2)) / 2          # 0 mid, 1 at the join ends
    J = (lambda a: 0.0) if end else (lambda a: rnd.uniform(-a, a))
    zf_in = -0.06 - 0.24 * f + J(0.04)
    zf_out = -0.02 - 0.12 * f + J(0.04)
    apex = 0.84 + 0.2 * (1 - f)
    R = [(-2.55 + J(0.08), zf_out), (-1.36 + J(0.05), zf_in)]
    for x in UX:
        t = min(1, abs(x) / 1.38)
        R.append((x + J(0.07), zf_in + (apex - zf_in) * (1 - t * t) ** 0.55 + J(0.06)))
    R += [(1.36 + J(0.05), zf_in + J(0.03)), (2.55 + J(0.08), zf_out), (2.64 + J(0.05), 0.36 + J(0.06))]
    for x in TX:
        t = abs(x) / 2.5
        R.append((x + J(0.08), 1.47 - 0.72 * t ** 2.2 + J(0.06) + (0.0 if end else 0.03 * (1 - f))))
    R.append((-2.64 + J(0.05), 0.36 + J(0.06)))
    return R


def build(vid, seed):
    rnd = random.Random(seed)
    s = Solid()
    secs = []
    ys = [-2, -1.55, -1.05, -0.5, 0.0, 0.5, 1.02, 1.52, 2]
    for y in ys:
        end = abs(y) == 2
        secs.append([Vector((x, y + (0 if end else rnd.uniform(-0.07, 0.07)), z)) for x, z in ring(y, rnd, end)])
    s.loft(secs, 'Earth')
    return s, s.finish(vid)


s, objs = build('AN03a', 31)
stats('AN03a', objs, s)
fbx = os.path.join(OUT, 'SM_AN03_CeilingVault.fbx')
ak.export_fbx(objs, fbx)
JOINS = {
    'grid': 'spans X between two AN01 walls whose centre lines are at x = -2 / +2 (4 m span); tiles along Y every 4.0 m',
    'pivot': 'origin (0,0,0) = seating plane on the wall tops (place at the wall top height z = 4.0), centred; the scalloped feet hang 0.06-0.30 m below it to overlap the crooked wall tops and hide the seam',
    'front_end': 'flat plane y = -2.0', 'back_end': 'flat plane y = +2.0',
    'end_profile_note': 'identical arch section at both ends: feet x = +/-1.36..2.55 at z = -0.30, underside apex z = 0.84, top z = 1.47',
    'scale': 'for wider spans scale X uniformly (span = 4 m x scale) and keep Y = 1 so the end profiles still tile',
}
recs = [ak.variant_record('AN03a', 'Ceiling vault 4 m span x 4 m', objs, fbx, solidity='solid',
                          collider='mesh', joins=JOINS, emissive=False,
                          notes='thick (0.45-0.6 m) faceted shell, concave underside that domes up toward the middle (ribbed when tiled), feet scalloped low at the 4 corners like the concept; top closed and convex.')]
write_manifest(OUT, recs)
ak.save_blend(os.path.join(HERE, 'AN03-ceiling-vault.blend'))
if wants_render():
    sys.path.insert(0, '/home/claude/ant/models')
    walls = []
    # in situ: vault on two walls
    from shell_lib import wall_ring, sweep, smooth
    for k, x0 in enumerate((-2, 2)):
        rr = random.Random(50 + k)
        ws = Solid()
        nodes = [(x0, y) for y in ((2, 1.8, 1.5, 1.0, 0.5, 0, -0.5, -1.0, -1.5, -1.8, -2) if x0 > 0 else (-2, -1.8, -1.5, -1, -0.5, 0, 0.5, 1, 1.5, 1.8, 2))]
        sweep(ws, nodes, lambda e, s_, i: wall_ring(smooth((e - 0.42) / 0.6), rr, depth=0.74, lift=0.26, brow=0.2, bulge=0.32, s=s_, ph=3.0 * k), rnd=rr)
        walls += ws.finish('W%d' % k)
    v2 = build('AN03b', 32)[1]
    for o in objs + v2:
        pass
    for o in v2:
        o.location = (0, 0, 4.0)
    tmp = []
    for o in objs:
        c = o.copy(); c.data = o.data; bpy.context.scene.collection.objects.link(c); c.location = (0, 0, 4.0); tmp.append(c)
    bpy.context.view_layer.update()
    hm = human(0.3, -1.0)
    card = renders(objs, OUT, os.path.join(SHEETS, '03-ceiling-vault.png'), el=12,
                   extra=[('IN SITU on AN01 walls', walls + tmp + hm, dict(az=-15, el=4, fov=40)),
                          ('UNDERSIDE', objs, dict(az=-30, el=-40))],
                   title='AN03 ceiling vault')
    print(card)
