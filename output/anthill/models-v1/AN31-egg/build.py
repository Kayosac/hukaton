import sys, os, math, random
sys.path.insert(0, '/home/claude/ant/models'); from lib_sup import *
from mathutils import Vector, Matrix
D = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(D, 'out'); os.makedirs(OUT, exist_ok=True)
REND = '--norender' not in sys.argv

def egg(mesh, M, s=1.0, seed=0, H=0.65, Rm=0.2, sides=12):
    rnd = random.Random(seed)
    ts = [0, .05, .16, .30, .45, .60, .74, .86, .95]
    rings = []
    jit = [[1 + rnd.uniform(-.035, .035) for k in range(sides)] for t in ts]
    for i, t in enumerate(ts):
        if t <= .4:
            r = Rm * math.sqrt(max(1 - ((.4 - t) / .46) ** 2, 0))
        else:
            r = Rm * math.sqrt(max(1 - ((t - .4) / .615) ** 2, 0))
        z = t * H
        ring = []
        for k in range(sides):
            a = 2 * math.pi * (k + .5 * (i % 2)) / sides
            m = jit[i][k] if 0 < i else 1 + (jit[i][k] - 1) * .3
            # slight egg asymmetry: one flank fuller
            m *= 1 + .05 * math.cos(a - 1.1) * math.sin(math.pi * t)
            ring.append(M @ (Vector((math.cos(a) * r * m, math.sin(a) * r * m * .96, z)) * s))
        rings.append(ring)
    apex = M @ (Vector((.012, -.006, H)) * s)
    skin(mesh, rings, lambda i, k: R('egg', k / sides, ts[i], fold=False))
    cap(mesh, rings[0], 'egg', flip=True, rad_uv=.3)
    cu = R('egg', .5, 1.0, fold=False)
    last = rings[-1]
    for k in range(sides):
        mesh.tri(last[k], last[(k + 1) % sides], apex, R('egg', k / sides, ts[-1], fold=False),
                 R('egg', (k + 1) / sides, ts[-1], fold=False), R('egg', (k + .5) / sides, 1, fold=False))

def placed(x, y, rz, tilt, tilt_dir):
    """matrix: tilt (rad) toward direction tilt_dir about the base, then move to (x, y)."""
    ax = Vector((math.cos(tilt_dir + math.pi / 2), math.sin(tilt_dir + math.pi / 2), 0))
    return Matrix.Translation((x, y, 0)) @ Matrix.Rotation(tilt, 4, ax) @ Matrix.Rotation(rz, 4, 'Z')

def drop(mesh_obj):
    pass

ak.reset()
# --- a: single upright egg (slight lean like the concept)
m = ak.Mesh('Props')
egg(m, placed(0, 0, .3, math.radians(6), 0.0), seed=3)
a = m.to_object('AN31a_Props')
ak.ground([a])
# --- b: clutch of three
m = ak.Mesh('Props')
egg(m, placed(-0.03, 0.08, 0.0, math.radians(4), 2.0), s=1.0, seed=5)
egg(m, placed(0.25, -0.10, 1.0, math.radians(11), -0.5), s=0.86, seed=7)
egg(m, placed(-0.21, -0.17, 2.0, math.radians(15), 3.7), s=0.78, seed=9)
b = m.to_object('AN31b_Props')
ak.ground([b])
for o in (a, b):
    o.location.x = 0
check_normals([a, b])
fa = os.path.join(D, 'SM_AN31_Egg.fbx'); fb = os.path.join(D, 'SM_AN31_EggClutch.fbx')
ak.export_fbx([a], fa); ak.export_fbx([b], fb)
recs = [ak.variant_record('AN31a', 'Egg (single)', [a], fa),
        ak.variant_record('AN31b', 'Egg clutch (3)', [b], fb)]
for r in recs: print(r)
write_manifest(OUT, 'AN31 egg', recs, solidity='solid (small, can be pushed/ignored)',
               collider='a: capsule/convex; b: one convex per egg or a single box', emissive=False,
               notes='Pivot bottom centre. Facets modelled; colour mottling painted (Props egg region). '
                     'Eggs in b sit slightly tilted, bases sunk to z=0.')
b.location.x = 0
if REND:
    a.hide_render = False
    t1, l1 = renders([a], OUT, 'a', el=12)
    t2, l2 = renders([b], OUT, 'b', el=22)
    card('31-egg', OUT, t1 + t2, l1 + l2, 'AN31 egg  a=single  b=clutch')
ak.save_blend(os.path.join(D, 'AN31_Egg.blend'))
