import sys, os, math, random
sys.path.insert(0, '/home/claude/ant/models'); from lib_sup import *
from mathutils import Vector, Matrix
D = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(D, 'out'); os.makedirs(OUT, exist_ok=True)
REND = '--norender' not in sys.argv

def egg(mesh, M, s=1.0, seed=0, H=0.65, Rm=0.2, sides=16):
    rnd = random.Random(seed)
    ts = [0, .035, .11, .21, .32, .44, .56, .67, .77, .86, .925, .965]
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

def base_bits(m, pts, rad, seed, n_clod=10, n_peb=4, n_leaf=3, n_crumb=16):
    """earth clods, pebbles, leaf bits and crumbs around egg bases. pts: list of (x, y) egg centres."""
    rnd = random.Random(seed)
    def near(r0, r1):
        def f(rr):
            cx, cy = rr.choice(pts)
            a = rr.uniform(0, 6.3); d = rr.uniform(r0, r1)
            return (cx + math.cos(a) * d, cy + math.sin(a) * d, 0.0)
        return f
    scatter(m, n_clod, near(rad * .85, rad * 1.5), (.03, .06), 'earth_props', seed=seed, sides=5, flat=.7, zsq=.7)
    scatter(m, n_peb, near(rad * 1.1, rad * 1.9), (.025, .045), 'pebble', seed=seed + 1, sides=5, flat=.5, zsq=.6)
    scatter(m, n_crumb, near(rad * .9, rad * 2.2), (.008, .018), 'earth_props', seed=seed + 2, sides=4)
    for i in range(n_leaf):
        cx, cy = rnd.choice(pts)
        a = rnd.uniform(0, 6.3); d = rad * rnd.uniform(.9, 1.4)
        small_leaf(m, (cx + math.cos(a) * d, cy + math.sin(a) * d, 0.012), rnd.uniform(.14, .22), rnd.uniform(.07, .1),
                   rnd.choice(('leaf_dry_0', 'leaf_dry_1', 'leaf_green', 'leaf_dry_2')), seed=seed + 10 + i,
                   rz=a + rnd.uniform(-.6, .6), rx=rnd.uniform(-.15, .15), droop=.008, curl=.012)
    # one tiny twig
    cx, cy = rnd.choice(pts)
    a = rnd.uniform(0, 6.3)
    twig(m, (cx + math.cos(a) * rad * 1.2, cy + math.sin(a) * rad * 1.2, 0.01), (math.cos(a + 1.5), math.sin(a + 1.5), 0),
         rad * 1.3, r=.008, seed=seed + 20, branches=1, segs=3, ground=True)

ak.reset()
# --- a: single upright egg (slight lean like the concept)
m = ak.Mesh('Props')
egg(m, placed(0, 0, .3, math.radians(6), 0.0), seed=3)
base_bits(m, [(0, 0)], 0.2, 11)
a = m.to_object('AN31a_Props')
ak.ground([a], center_xy=False)
# --- b: clutch of three
m = ak.Mesh('Props')
egg(m, placed(-0.03, 0.08, 0.0, math.radians(4), 2.0), s=1.0, seed=5)
egg(m, placed(0.25, -0.10, 1.0, math.radians(11), -0.5), s=0.86, seed=7)
egg(m, placed(-0.21, -0.17, 2.0, math.radians(15), 3.7), s=0.78, seed=9)
base_bits(m, [(-0.03, 0.08), (0.25, -0.10), (-0.21, -0.17)], 0.19, 21, n_clod=20, n_peb=7, n_leaf=6, n_crumb=30)
b = m.to_object('AN31b_Props')
ak.ground([b], center_xy=False)
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
               notes='Pivot bottom centre (of the egg / the clutch). Faceted eggs (16 sides); colour mottling painted (Props egg). '
                     'Eggs in b sit slightly tilted. Around the bases: earth clods (earth_props), pebbles, crumbs, dry leaf bits '
                     'and a small twig (a: 10/4/16/3, b: 20/7/30/6).')
b.location.x = 0
if REND:
    a.hide_render = False
    t1, l1 = renders([a], OUT, 'a', el=12)
    t2, l2 = renders([b], OUT, 'b', el=22)
    card('31-egg', OUT, t1 + t2, l1 + l2, 'AN31 egg v2  a=single  b=clutch')
ak.save_blend(os.path.join(D, 'AN31_Egg.blend'))
