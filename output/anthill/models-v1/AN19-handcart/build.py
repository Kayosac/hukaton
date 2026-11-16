"""AN19 Two-wheel handcart (Двухколёсная тележка): low plank body with corner posts and pegs, broken top plank on
the front wall, low back wall, two faceted solid wheels on one axle, two long shafts resting on the ground.
Shafts point to -Y (front). Pivot bottom centre."""
import sys, os, math, random
sys.path.insert(0, '/home/claude/ant/models')
from an_woodlib import *
import ak

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
SHEET = '/home/claude/ant/concepts/environment-pack-v3/asset-concepts-v1/sheets/19-handcart.png'
RENDER = '--norender' not in sys.argv

ak.reset()
w = ak.Mesh('Wood')
p = ak.Mesh('Props')
rnd = random.Random(7)

BX = 0.62            # body half width (outer face of side walls)
Y0, Y1 = -0.66, 0.70  # body front / back
FZ = 0.53            # floor bottom
AXZ, WR = 0.41, 0.41  # axle height, wheel radius
AY = 0.12            # axle y

# axle + two sills (runners) on it
beam(w, [(-0.86, AY, AXZ), (0.0, AY + 0.005, AXZ + 0.005), (0.86, AY, AXZ)], 0.09, 0.095, up=(0, 0, 1), jitter=0.006, seed=1,
     caps=('flat', 'flat'), endm=p)
for i, sx in enumerate((-1, 1)):
    beam(w, [(sx * 0.42, Y1 - 0.02, FZ - 0.045), (sx * 0.425, 0.0, FZ - 0.05), (sx * 0.42, Y0 + 0.02, FZ - 0.045)], 0.09, 0.08,
         up=(1, 0, 0), jitter=0.006, seed=2 + i, caps=('flat', 'flat'))
# shafts: start under the body, run out forward and down to the ground
for i, sx in enumerate((-1, 1)):
    A_ = Vector((sx * 0.33, 0.35, FZ - 0.10)); B_ = Vector((sx * 0.37, -1.62, 0.07))
    beam(w, [A_, A_.lerp(B_, 0.35) + Vector((sx * 0.006, 0, 0.01)), A_.lerp(B_, 0.7) + Vector((0, 0, -0.008)), B_],
         [0.11, 0.115, 0.11, 0.105], 0.105, up=(1, 0, 0), jitter=0.008, seed=5 + i, caps=('flat', 'flat'), endm=p)
# floor: 5 planks along Y with small gaps
for i in range(5):
    x = -0.46 + i * 0.23 + rnd.uniform(-0.01, 0.01)
    beam(w, [(x, Y0 + 0.04, FZ + 0.025), (x + rnd.uniform(-.01, .01), Y1 - 0.04, FZ + 0.025 + rnd.uniform(-.008, .008))],
         0.215, 0.045, up=(0, 0, 1), jitter=0.006, seed=10 + i, caps=('flat', 'flat'))
# side walls: two planks each, along Y
for s, sx in enumerate((-1, 1)):
    for j, (z, hh) in enumerate(((FZ + 0.155, 0.2), (FZ + 0.36, 0.19))):
        x = sx * (BX - 0.03)
        beam(w, [(x, Y0 + 0.01, z + rnd.uniform(-.01, .01)), (x + sx * 0.01, Y1 - 0.01, z + rnd.uniform(-.015, .015))],
             hh, 0.045, up=(1, 0, 0), jitter=0.006, seed=20 + s * 2 + j, caps=('flat', 'flat'))
# front wall (shaft end): lower plank whole, upper plank broken into two jagged pieces
fy = Y0 + 0.025
beam(w, [(-BX + 0.04, fy, FZ + 0.155), (BX - 0.04, fy, FZ + 0.15)], 0.2, 0.045, up=(0, 1, 0), jitter=0.006, seed=30,
     caps=('flat', 'flat'))
beam(w, [(BX - 0.04, fy, FZ + 0.36), (0.12, fy, FZ + 0.355), (-0.1, fy, FZ + 0.355)], 0.19, 0.045, up=(0, 1, 0),
     jitter=0.006, seed=31, caps=("flat", "broken"), broken=0.9)
beam(w, [(-BX + 0.04, fy, FZ + 0.31), (-0.3, fy, FZ + 0.31)], 0.1, 0.045, up=(0, 1, 0), jitter=0.006, seed=32,
     caps=('flat', 'broken'), broken=1.2)
# back wall: one low plank
beam(w, [(-BX + 0.04, Y1 - 0.025, FZ + 0.12), (BX - 0.04, Y1 - 0.025, FZ + 0.13)], 0.17, 0.045, up=(0, 1, 0),
     jitter=0.006, seed=33, caps=('flat', 'flat'))
# corner posts with chamfered tops, square pegs
for i, (sx, y, top) in enumerate(((-1, Y0, 1.10), (1, Y0, 1.08), (-1, Y1, 1.06), (1, Y1, 1.09))):
    x = sx * (BX - 0.015)
    yy = y + (0.03 if y < 0 else -0.03)
    beam(w, [(x, yy, FZ - 0.08), (x + sx * 0.008, yy, (FZ + top) / 2), (x + rnd.uniform(-.01, .01), yy + rnd.uniform(-.01, .01), top)],
         0.12, 0.12, up=(0, 1, 0), jitter=0.006, seed=40 + i, caps=('flat', 'bevel'), endm=p, bevel=0.3)
    for j, z in enumerate((FZ + 0.12, FZ + 0.38)):
        peg(w, (x + sx * 0.06, yy, z), (sx, 0, 0), size=0.05, length=0.025, endm=p, seed=50 + i * 2 + j)
        peg(w, (x, yy - 0.06 if y < 0 else yy + 0.06, z + 0.04), (0, -1 if y < 0 else 1, 0), size=0.045, length=0.022, endm=p,
            seed=60 + i * 2 + j)

# wheels: solid faceted discs, slightly wobbly, hex hub
def wheel(m, cx, sx, seed):
    r = random.Random(seed)
    n = 14
    th = 0.085
    tilt = r.uniform(-0.05, 0.05)
    ax = Vector((sx, 0, 0))
    ax = Matrix.Rotation(tilt, 3, 'Z') @ ax
    e1 = Vector((0, 1, 0)); e1 = (e1 - ax * e1.dot(ax)).normalized(); e2 = ax.cross(e1).normalized()
    C = Vector((cx, AY, AXZ))
    tile = m.tile()
    rings = []
    for side in (-1, 1):
        ring = []
        for k in range(n):
            a = 2 * math.pi * k / n + 0.11
            rr = WR * r.uniform(0.975, 1.02)
            ring.append(C + ax * side * th / 2 + e1 * math.cos(a) * rr + e2 * math.sin(a) * rr)
        rings.append(ring)
    uvof = lambda q: ((q - C).dot(e1) / tile + seed * .1, (q - C).dot(e2) / tile)
    for k in range(n):
        k2 = (k + 1) % n
        q = [rings[0][k], rings[0][k2], rings[1][k2], rings[1][k]]
        L = (q[0] - q[1]).length / tile
        oface(m, q, [(0, 0), (L, 0), (L, th / tile), (0, th / tile)], (q[0] + q[1]) / 2 - C)
    for side, ring in ((-1, rings[0]), (1, rings[1])):
        cen = C + ax * side * (th / 2 + 0.018)
        for k in range(n):
            k2 = (k + 1) % n
            tri = [ring[k], ring[k2], cen]
            oface(m, tri, [uvof(t) for t in tri], ax * side)
    # hub (hexagonal) on the outer side, through-axle end
    hub = [C + ax * (th / 2), C + ax * (th / 2 + 0.07)]
    hr = []
    for z, pt in enumerate(hub):
        hr.append([pt + e1 * math.cos(2 * math.pi * k / 6) * 0.075 + e2 * math.sin(2 * math.pi * k / 6) * 0.075 for k in range(6)])
    for k in range(6):
        k2 = (k + 1) % 6
        q = [hr[0][k], hr[0][k2], hr[1][k2], hr[1][k]]
        oface(m, q, [(0, 0), (.05, 0), (.05, .05), (0, .05)], (q[0] + q[1]) / 2 - C - ax * th)
    grain_cap(p, hr[1], ax, fill=0.4)

wheel(w, -(BX + 0.12), -1, 70)
wheel(w, (BX + 0.12), 1, 71)

wo = w.to_object('AN19a_Wood')
po = p.to_object('AN19a_Props')
objs = [wo, po]
ak.ground(objs)
fbx = os.path.join(HERE, 'SM_AN19_Handcart.fbx')
ak.export_fbx(objs, fbx)
ak.save_blend(os.path.join(HERE, 'AN19_Handcart.blend'))
rec = ak.variant_record('AN19a', 'Handcart', objs, fbx,
                        solidity='solid (static prop; can be a physics prop)', collider='box for the body (1.3 x 1.4 x 0.6 at floor level), '
                        '2 thin boxes/cylinders for the wheels, 2 thin boxes for the shafts (or one convex hull overall)',
                        emissive=False,
                        notes='Floor 5 planks, side walls 2+2 planks, front wall with broken upper plank, low back plank, 4 corner posts, '
                              '16 pegs, axle, 2 sills, 2 shafts, 2 faceted 14-sided solid wheels with hex hubs: all separate geometry. '
                              'Empty body; seeds/sacks are separate assets. Damaged variant = rotate/chip a wheel in the level.')
save_manifest(os.path.join(OUT, 'manifest.json'), {'asset': 'AN19 handcart', 'variants': [rec]})
print('TRIS', rec['tris'], rec['tris_by_material'], 'SIZE', rec['size_xyz_m'])
if RENDER:
    render_set(objs, OUT, SHEET, 'AN19 Handcart  %d tris  %s m' % (rec['tris'], rec['size_xyz_m']), el=24)
