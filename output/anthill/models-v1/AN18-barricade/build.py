"""AN18 Plank barricade (Дощатое укрытие): cover for the machine-gun ant crew. Uneven vertical planks, a front top
rail with a short stub, two thick leaning end posts with protruding rail ends, two back rails, two back legs.
Cover height ~1.25-1.35 m (planks), posts ~1.45 m. Pivot bottom centre, front (the side facing the enemy) -Y."""
import sys, os, math, random
sys.path.insert(0, '/home/claude/ant/models')
from an_woodlib import *
import ak

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
SHEET = '/home/claude/ant/concepts/environment-pack-v3/asset-concepts-v1/sheets/18-barricade.png'
RENDER = '--norender' not in sys.argv

ak.reset()
w = ak.Mesh('Wood')
p = ak.Mesh('Props')
rnd = random.Random(5)

# front planks: (centre x, width, height, top style)
planks = [(-0.93, 0.27, 1.22, 'flat'), (-0.66, 0.25, 1.31, 'broken'), (-0.385, 0.28, 1.18, 'flat'),
          (-0.105, 0.26, 1.34, 'flat'), (0.165, 0.27, 1.24, 'broken'), (0.44, 0.26, 1.30, 'flat'),
          (0.71, 0.27, 1.20, 'flat'), (0.95, 0.2, 1.27, 'broken')]
PY = -0.03
for i, (x, wd, h, top) in enumerate(planks):
    lean = rnd.uniform(0.03, 0.07)      # tops lean back a bit
    dx = rnd.uniform(-0.035, 0.035)     # sideways tilt
    th = rnd.uniform(0.05, 0.065)
    pts = [(x, PY, 0.0), (x + dx * 0.45 + rnd.uniform(-.008, .008), PY + lean * 0.45, h * 0.45), (x + dx, PY + lean, h)]
    beam(w, pts, [wd, wd * rnd.uniform(.96, 1.02), wd * rnd.uniform(.93, 1.0)], th, up=(0, 1, 0), jitter=0.01, seed=100 + i,
         caps=(None, top), endm=p, broken=0.9)

# end posts: thick, leaning, different angles
beam(w, [(-1.15, -0.02, 0.0), (-1.13, 0.0, 0.7), (-1.08, 0.03, 1.43)], [0.21, 0.2, 0.19], [0.2, 0.2, 0.19], up=(0, 1, 0),
     jitter=0.012, seed=1, caps=(None, 'flat'), endm=p)
beam(w, [(1.12, -0.03, 0.0), (1.13, -0.005, 0.75), (1.16, 0.04, 1.42)], [0.21, 0.2, 0.2], [0.21, 0.2, 0.19], up=(0, 1, 0),
     jitter=0.012, seed=2, caps=(None, 'bevel'), endm=p, bevel=0.25)

# front top rail across the planks (rising slightly to the right), ends sticking out past the posts
beam(w, [(-1.22, -0.14, 0.98), (0.0, -0.15, 1.02), (1.25, -0.135, 1.07)], [0.16, 0.17, 0.16], 0.075, up=(0, 1, 0),
     jitter=0.01, seed=3, caps=('flat', 'flat'), endm=p)
# short stub plank in the middle, nailed over the rail
beam(w, [(0.0, -0.205, 0.86), (0.01, -0.2, 1.37)], 0.17, 0.06, up=(0, 1, 0), jitter=0.01, seed=4, caps=('flat', 'flat'), endm=p)
# chunky end blocks on the protruding rail ends (front rail left, back rail right)
beam(w, [(-1.3, -0.14, 0.98), (-1.17, -0.14, 0.985)], 0.15, 0.15, up=(0, 1, 0), jitter=0.01, seed=5, caps=('flat', 'flat'), endm=p)
beam(w, [(1.17, 0.1, 0.93), (1.3, 0.1, 0.935)], 0.15, 0.15, up=(0, 1, 0), jitter=0.01, seed=6, caps=('flat', 'flat'), endm=p)

# back rails (behind the planks), through the posts
for i, (z0, z1) in enumerate(((0.92, 0.95), (0.42, 0.40))):
    beam(w, [(-1.22, 0.1, z0), (0.0, 0.105, (z0 + z1) / 2 + 0.01), (1.22, 0.1, z1)], 0.15, 0.075, up=(0, 1, 0), jitter=0.01,
         seed=10 + i, caps=('flat', 'flat'), endm=p)
# back legs: from the upper back rail down to the ground behind
for i, (x, dx) in enumerate(((-0.72, -0.07), (0.74, 0.08))):
    beam(w, [(x, 0.17, 1.05), (x + dx * .5, 0.33, 0.55), (x + dx * 1.6, 0.50, 0.05)], [0.13, 0.13, 0.13], [0.11, 0.115, 0.11],
         up=(1, 0, 0), jitter=0.01, seed=20 + i, caps=('flat', 'flat'), endm=p)
# pegs on the front rail over the planks and on the back rails
for i, x in enumerate((-0.93, -0.385, 0.44, 0.95)):
    z = 0.98 + (x + 1.3) / 2.6 * 0.09
    peg(w, (x, -0.18, z), (0, -1, 0), size=0.055, length=0.035, endm=p, seed=40 + i)
for i, (x, z) in enumerate(((-0.66, 0.935), (0.71, 0.945), (-0.105, 0.41), (0.44, 0.41))):
    peg(w, (x, 0.14, z), (0, 1, 0), size=0.05, length=0.03, endm=p, seed=50 + i)

wo = w.to_object('AN18a_Wood')
po = p.to_object('AN18a_Props')
objs = [wo, po]
ak.ground(objs)
fbx = os.path.join(HERE, 'SM_AN18_Barricade.fbx')
ak.export_fbx(objs, fbx)
ak.save_blend(os.path.join(HERE, 'AN18_Barricade.blend'))
rec = ak.variant_record('AN18a', 'Barricade', objs, fbx,
                        solidity='solid (cover)', collider='box 2.4 x 0.25 x 1.3 for the plank wall (blocks shots/movement) + 2 boxes for the back legs',
                        cover_height_m='planks 1.18-1.37, posts ~1.5',
                        emissive=False,
                        notes='8 separate planks (3 with broken tops), front rail + stub, 2 back rails, 2 end posts, 2 back legs, '
                              'end blocks and 8 pegs are real geometry; olive wear comes from the Wood texture.')
save_manifest(os.path.join(OUT, 'manifest.json'), {'asset': 'AN18 barricade', 'variants': [rec]})
print('TRIS', rec['tris'], rec['tris_by_material'], 'SIZE', rec['size_xyz_m'])
if RENDER:
    render_set(objs, OUT, SHEET, 'AN18 Barricade  %d tris  %s m' % (rec['tris'], rec['size_xyz_m']), el=18)
