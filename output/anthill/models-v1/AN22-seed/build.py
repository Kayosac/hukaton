import sys, os, math, random
sys.path.insert(0, '/home/claude/ant/models'); from lib_sup import *
from mathutils import Vector, Matrix
D = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(D, 'out'); os.makedirs(OUT, exist_ok=True)
REND = '--norender' not in sys.argv

ak.reset()
rnd = random.Random(4)
L = 0.55
ts = [0.035, .1, .2, .33, .47, .61, .75, .87, .955]
path = [(-L / 2 + t * L, 0.006 * math.sin(t * 5), 0.018 * math.sin(math.pi * t) - 0.006 * t) for t in ts]
prof = [math.sin(math.pi * t) ** 0.62 for t in ts]
SIDES = 12
# radial shape per side index: keel on top (k=0), ribs at +-2, grooves at +-1, seam grooves low on the flanks,
# flattened belly
base = {0: 1.13, 1: .95, 2: 1.05, 3: 1.0, 4: .88, 5: .97, 6: .93, 7: .97, 8: .88, 9: 1.0, 10: 1.05, 11: .95}
J = [[rnd.uniform(-.03, .03) for k in range(SIDES)] for t in ts]
def rf(i, k, a):
    m = base[k] + J[i][k]
    if k == 6:
        m *= .9
    return m
radii = [p for p in prof]
scale = [(0.088, 0.122)] * len(ts)
m = ak.Mesh('Props')
rings = rings_along(path, radii, SIDES, scale=scale, rfun=rf, up_hint=(0, 0, 1))
skin(m, rings, lambda i, k: R('seed', k / SIDES, ts[i], fold=False))
tipA = Vector((-L / 2 - 0.004, 0.004, 0.004)); tipB = Vector((L / 2, -0.004, 0.012))
# tip fans (pointed ends)
first, last = rings[0], rings[-1]
for k in range(SIDES):
    k2 = (k + 1) % SIDES
    m.tri(first[k2], first[k], tipA, R('seed', (k + 1) / SIDES, ts[0], 0), R('seed', k / SIDES, ts[0], 0), R('seed', (k + .5) / SIDES, 0, 0))
    m.tri(last[k], last[k2], tipB, R('seed', k / SIDES, ts[-1], 0), R('seed', (k + 1) / SIDES, ts[-1], 0), R('seed', (k + .5) / SIDES, 1, 0))
o = m.to_object('AN22a_Props')
ak.ground([o])
check_normals([o])
f = os.path.join(D, 'SM_AN22_Seed.fbx'); ak.export_fbx([o], f)
rec = ak.variant_record('AN22a', 'Large seed', [o], f)
print(rec)
write_manifest(OUT, 'AN22 seed', [rec], solidity='solid (small prop; scatter/fill mesh)', collider='capsule or convex',
               emissive=False, notes='Pivot bottom centre, long axis X. Keel ridge, two flank ribs, seam grooves and '
               'flattened belly modelled; ochre mottling and the dark seam colour painted (Props seed).')
if REND:
    t, l = renders([o], OUT, 'a', el=22)
    card('22-seed', OUT, t, l, 'AN22 seed')
ak.save_blend(os.path.join(D, 'AN22_Seed.blend'))
