import sys, os, math, random
sys.path.insert(0, '/home/claude/ant/models'); from lib_sup import *
from mathutils import Vector, Matrix
D = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(D, 'out'); os.makedirs(OUT, exist_ok=True)
REND = '--norender' not in sys.argv

ak.reset()
rnd = random.Random(22)
m = ak.Mesh('Props')
SIDES = 16
# seed shell: keel, ribs, seam grooves, belly, dented chips, and a split gash opening near the +x tip (side index 5
# = lower front flank) revealing the pale kernel
rings, ts = seed_body(m, Matrix.Identity(4), sides=SIDES, nring=15, seed=4, gash=(0.55, 0.95, 5), chips=7)
# kernel inside the split (ivory = egg region), slightly bulging out of the gash
for i, (x, y, z, s) in enumerate(((0.12, -0.05, 0.075, .035), (0.17, -0.05, 0.07, .03), (0.07, -0.045, 0.08, .025))):
    lump(m, (x, y, z), (s * 1.6, s * .8, s * .9), 'egg', sides=6, seed=30 + i, jit=.12)
# husk flake half peeled off the shell near the split
shard(m, (0.15, -0.075, 0.115), 0.045, 'seed', 'shell_in', seed=3, curv=.5, th=.006,
      M=Matrix.Rotation(math.radians(-60), 3, 'X') @ Matrix.Rotation(.4, 3, 'Z'))
# sprout from the split tip: curved pale stem + two tiny leaves
sp = [Vector((0.21, -0.05, 0.065)), Vector((0.25, -0.08, 0.09)), Vector((0.275, -0.09, 0.13)), Vector((0.28, -0.075, 0.165))]
atlas_tube(m, sp, [0.009, 0.008, 0.007, 0.005], 'leaf_green', sides=5, metres_u=.2, caps=(False, True))
small_leaf(m, (0.29, -0.07, 0.172), 0.05, 0.025, 'leaf_green', seed=5, rz=.3, ry=-.5, thick=.004)
small_leaf(m, (0.265, -0.085, 0.17), 0.045, 0.022, 'leaf_green', seed=6, rz=2.8, ry=.5, thick=.004)
# rootlet hair from the same tip
for i in range(3):
    fibres(m, (0.235, -0.05, 0.035), (0.6, -0.4, -0.6), n=1, length=0.05, r=0.003, seed=40 + i, region='paper', spread=.6)
# broken husk flakes and crumbs lying beside it
for i, (x, y) in enumerate(((0.2, -0.13), (0.27, 0.05), (-0.2, -0.12), (0.06, 0.15))):
    shard(m, (x, y, 0.006), rnd.uniform(.022, .035), 'seed', 'shell_in', seed=50 + i, curv=.35, th=.005)
scatter(m, 9, ring_sampler(0.0, 0.0, 0.17, 0.3, sy=.7), (.006, .013), 'seed', seed=7, sides=4)
o = m.to_object('AN22a_Props')
ak.ground([o])
check_normals([o])
f = os.path.join(D, 'SM_AN22_Seed.fbx'); ak.export_fbx([o], f)
rec = ak.variant_record('AN22a', 'Large seed', [o], f)
print(rec)
write_manifest(OUT, 'AN22 seed', [rec], solidity='solid (small prop; scatter/fill mesh)', collider='capsule or convex (seed body only)',
               emissive=False, notes='Pivot bottom centre, long axis X. Keel ridge, flank ribs, seam grooves, flattened belly, '
               '7 dented chips, a split gash at the +x end with the ivory kernel showing, a half-peeled husk flake, a small '
               'sprout (stem + 2 leaves + rootlet hairs), 4 loose husk flakes and crumbs beside it. Mottling painted.')
if REND:
    t, l = renders([o], OUT, 'a', el=22)
    card('22-seed', OUT, t, l, 'AN22 seed (v2 detail)')
ak.save_blend(os.path.join(D, 'AN22_Seed.blend'))
