import sys, os, random
sys.path.insert(0, '/home/claude/ant/models')
from shell_lib import *
from detail_lib import *

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
ak.reset()

# opening (inner) and outer outlines in XZ, same count, matched along the arch (u)
INNER = [(-1.32, 0), (-1.36, 0.6), (-1.34, 1.25), (-1.33, 1.85), (-1.30, 2.35), (-1.17, 2.74), (-0.9, 3.0),
         (-0.48, 3.16), (0.02, 3.2), (0.48, 3.13), (0.88, 2.97), (1.18, 2.72), (1.31, 2.36), (1.34, 1.8),
         (1.36, 1.2), (1.37, 0.6), (1.33, 0)]
OUTER = [(-2, 0), (-2, 0.55), (-2, 1.15), (-2, 1.7), (-2, 2.3), (-2, 3.0), (-2, 4.0), (-1.2, 4.18), (-0.35, 4.3),
         (0.45, 4.26), (1.25, 4.12), (2, 4.0), (2, 3.0), (2, 2.3), (2, 1.65), (2, 0.55), (2, 0)]


def dens(L):
    out = []
    for i in range(len(L) - 1):
        out += [L[i], ((L[i][0] + L[i + 1][0]) / 2, (L[i][1] + L[i + 1][1]) / 2)]
    return out + [L[-1]]


INNER, OUTER = dens(INNER), dens(OUTER)
TV = [0.0, 0.16, 0.38, 0.6, 0.8, 1.0]                     # layers from the opening edge to the outer edge
FY = [-0.5, -0.82, -0.94, -0.9, -0.74, -0.62]             # front y per layer (bulging frame), mirrored at the back


CLEAR = (Vector((-1.22, -1.3, 0.0)), Vector((1.22, 1.3, 2.62)))


def forbid(lo, hi):
    if lo.x < -1.97 or hi.x > 1.97 or lo.z < -0.002 or abs(lo.y) > 1.35 or abs(hi.y) > 1.35:
        return True
    a, b = CLEAR
    return lo.x < b.x and hi.x > a.x and lo.y < b.y and hi.y > a.y and lo.z < b.z and hi.z > a.z


FRONT = lambda p, n: abs(p.x) < 1.93 and p.z > 0.06 and n.z < 0.65
UNDER = lambda p, n: abs(p.x) < 1.9 and n.z < -0.3 and p.z > 1.0
TOP = lambda p, n: abs(p.x) < 1.9 and n.z > 0.6
HIGH = lambda p, n: abs(p.x) < 1.9 and abs(n.y) > 0.3 and p.z > 2.9


def build(vid, seed):
    rnd = random.Random(seed)
    s = Solid()
    secs = []
    n = len(INNER)
    for u in range(n):
        I = Vector((INNER[u][0], 0, INNER[u][1])); O = Vector((OUTER[u][0], 0, OUTER[u][1]))
        bottom = u in (0, n - 1)
        jm = 1.0 if u % 2 == 0 else 0.35
        front, back = [], []
        for v, t in enumerate(TV):
            p = I.lerp(O, t)
            if 0 < v < len(TV) - 1:
                p += Vector((jm * rnd.uniform(-0.1, 0.1), 0, 0 if bottom else jm * rnd.uniform(-0.13, 0.13)))
            on_join = v == len(TV) - 1 and abs(O.x) == 2
            fy = FY[v] if on_join else FY[v] + jm * rnd.uniform(-0.1, 0.1)
            by = -FY[v] if on_join else -FY[v] + jm * rnd.uniform(-0.1, 0.1)
            if bottom and 0 < v < len(TV) - 1:
                fy -= 0.08; by += 0.08                   # feet flare
            front.append(Vector((p.x, fy, p.z)))
            back.append(Vector((p.x + (0 if on_join or v == 0 else rnd.uniform(-0.05, 0.05)), by, p.z)))
        secs.append(front + list(reversed(back)))
    s.loft(secs, 'Earth')
    for c, r in (((-1.72, -0.98, 0.0), (0.3, 0.24, 0.2)), ((1.68, -0.95, 0.0), (0.22, 0.2, 0.15)), ((1.75, 0.98, 0.0), (0.28, 0.22, 0.18))):
        lump(s, c, r, rnd, rings=3, sides=7, jitter=0.2, flat_z=0.0, rot=rnd.uniform(0, 1))
    rays = [((x, sy * 1.8, 0.07), (0, -sy, 0)) for x in (-1.75, -1.55, 1.55, 1.75) for sy in (1, -1)] + \
           [((0, sy * 0.4, 0.07), (sx, 0, 0)) for sx in (1, -1) for sy in (1, -1)]
    extra = [(30, hang_root, UNDER, ('Earth',), lambda p, n: {'length': rnd.uniform(0.15, 0.6)})]
    dress_shell(s, rnd, forbid, FRONT, UNDER, TOP, None, HIGH, k=1.35, rays=rays, rubble=(14, 30, 24, 4), extra=extra)
    return s, s.finish(vid)


s, objs = build('AN04a', 41)
stats('AN04a', objs, s)
clear = box_clear(objs, (-1.2, -1.0, 0.0), (1.2, 1.0, 2.6))
print('clear 2.4 x 2.6 passage:', clear)
fbx = os.path.join(OUT, 'SM_AN04_TunnelCollar.fbx')
ak.export_fbx(objs, fbx)
JOINS = {
    'grid': 'replaces one 4 m AN01 wall module: pivot = bottom centre on the wall centre line (y=0), room side -Y, tunnel side +Y',
    'left_end': 'flat plane x = -2.0, covers y -0.62..+0.62, z 0..4.0 (AN01 end profile fits inside it)',
    'right_end': 'flat plane x = +2.0, same',
    'opening': 'clear passage >= 2.64 m wide (jambs x = +/-1.32) and 3.2 m high at the crown; verified box 2.4 x 2.6 m clear',
    'tunnel_mouth_xz_at_y+0.50': [list(p) for p in INNER],
    'tunnel_note': 'procedural tunnels start at the back face (y = +0.62) with the opening outline above (it is the soffit edge, set back 0.27 m from the frame face); frame front/back faces bulge to y = -/+0.94',
}
recs = [ak.variant_record('AN04a', 'Tunnel collar 4 x 4.3 x 1.7 m (opening 2.64 x 3.2)', objs, fbx, solidity='passable',
                          collider='mesh (or 2 leg boxes + arch convex pieces); walk-through opening must stay open', joins=JOINS,
                          emissive=False, clear_opening_2_4x2_6=clear,
                          notes='thick crooked arch frame: chamfered opening lip, bulging faceted frame faces front and back, flared feet with rubble outside the passage; covered in pebbles, clods, crumbling ledges, root tips, hair rootlets hanging from the soffit (all above 2.62 m), burrows, cracks, drips, ridges, husks; flat join sides for walls.')]
write_manifest(OUT, recs)
ak.save_blend(os.path.join(HERE, 'AN04-tunnel-collar.blend'))
if wants_render():
    hm = human(-0.4, -1.4)
    card = renders(objs, OUT, os.path.join(SHEETS, '04-tunnel-collar.png'),
                   extra=[('SCALE 1.8 m man', objs + hm, dict(az=-20, el=10))], title='AN04 tunnel collar')
    print(card)
