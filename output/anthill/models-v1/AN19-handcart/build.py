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
         [0.11, 0.115, 0.11, 0.105], 0.105, up=(1, 0, 0), jitter=0.008, seed=5 + i, caps=('flat', 'flat'), endm=p,
         cracks=[(3, 0.3, 0.7, 0.02), (0, 0.75, 1.0, 0.02)], chips=[(1, 0.55, 0.1, 0.025), (2, 0.85, 0.08, 0.02)])
# floor: 5 planks along Y with small gaps
for i in range(5):
    x = -0.46 + i * 0.23 + rnd.uniform(-0.01, 0.01)
    beam(w, [(x, Y0 + 0.04, FZ + 0.025), (x + rnd.uniform(-.01, .01), Y1 - 0.04, FZ + 0.025 + rnd.uniform(-.008, .008))],
         0.215, 0.045, up=(0, 0, 1), jitter=0.006, seed=10 + i, caps=('flat', 'flat'),
         cracks=[(2, 0.05, 0.75, 0.025)] if i == 1 else ([(2, 0.5, 1.0, 0.015)] if i == 3 else []),
         chips=[(2 + (i % 2), rnd.uniform(0.2, 0.8), 0.08, 0.015)])
# side walls: two planks each, along Y
for s, sx in enumerate((-1, 1)):
    for j, (z, hh) in enumerate(((FZ + 0.155, 0.2), (FZ + 0.36, 0.19))):
        x = sx * (BX - 0.03)
        beam(w, [(x, Y0 + 0.01, z + rnd.uniform(-.01, .01)), (x + sx * 0.01, Y1 - 0.01, z + rnd.uniform(-.015, .015))],
             hh, 0.045, up=(1, 0, 0), jitter=0.006, seed=20 + s * 2 + j, caps=('flat', 'flat'),
             cracks=[(0 if sx < 0 else 2, rnd.uniform(0.1, 0.4), rnd.uniform(0.5, 0.9), 0.015)],
             chips=[(3 - j * 2, rnd.uniform(0.2, 0.8), 0.1, 0.02)])
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
         0.12, 0.12, up=(0, 1, 0), jitter=0.006, seed=40 + i, caps=('flat', 'bevel'), endm=p, bevel=0.3,
         cracks=[(i % 4, 0.55, 1.0, 0.018)], chips=[((i + 2) % 4, 0.3, 0.06, 0.02)])
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
    # iron hub cap (octagonal) + linchpin
    cap_c = C + ax * (th / 2 + 0.07)
    ring_a = [cap_c + e1 * math.cos(2 * math.pi * k / 8 + .2) * 0.06 + e2 * math.sin(2 * math.pi * k / 8 + .2) * 0.06 for k in range(8)]
    rope(p, [cap_c - ax * 0.004, cap_c + ax * 0.03], 0.06, up=e1, sides=8, region='iron', fit=True, radii=[0.062, 0.05], rot=.2)
    pin = cap_c + ax * 0.015
    rope(p, [pin + e2 * 0.09 + e1 * 0.01, pin + e2 * 0.03, pin - e2 * 0.08, pin - e2 * 0.11 + e1 * 0.03], 0.009, up=ax, sides=4,
         region='iron', fit=True, rot=.78)
    # nails round the rim on the outer face and on the tread
    for k in range(12):
        a = 2 * math.pi * (k + .5) / 12 + 0.11
        rr_ = 0.345
        pos = C + ax * (th / 2 + 0.018 * (1 - rr_ / WR) + 0.001) + e1 * math.cos(a) * rr_ + e2 * math.sin(a) * rr_
        nail(p, pos, ax, size=0.02, out=0.006, seed=seed * 50 + k, region='dark' if k % 3 else 'iron')
    for k in range(7):
        a = 2 * math.pi * k / 7 + 0.3
        rad = e1 * math.cos(a) + e2 * math.sin(a)
        nail(p, C + rad * WR * 0.985 + ax * r.uniform(-.015, .015), rad, size=0.022, out=0.007, seed=seed * 50 + 20 + k, region='iron')

wheel(w, -(BX + 0.12), -1, 70)
wheel(w, (BX + 0.12), 1, 71)


# ---------------- detail pass
rr = random.Random(19)
# iron corner brackets (L straps) on the body corners, with nails
for i, (sx, y) in enumerate(((-1, Y0), (1, Y0), (-1, Y1), (1, Y1))):
    sy = -1 if y < 0 else 1
    z = FZ + 0.27
    x0 = sx * (BX + 0.004)
    y0 = y + sy * 0.004 - sy * 0.0
    obox(p, (x0, y - sy * 0.12, z), (0, 1, 0), (0, 0, 1), (sx, 0, 0), (0.17, 0.06, 0.008), region='iron', jitter=0.002)
    obox(p, (sx * (BX - 0.12), y + sy * 0.004, z), (1, 0, 0), (0, 0, 1), (0, sy, 0), (0.17, 0.06, 0.008), region='iron', jitter=0.002)
    for k in range(2):
        nail(p, (x0 + sx * 0.004, y - sy * (0.09 + k * 0.08), z), (sx, 0, 0), size=0.016, out=0.006, seed=700 + i * 4 + k, region='dark')
        nail(p, (sx * (BX - 0.09 - k * 0.08), y + sy * 0.008, z), (0, sy, 0), size=0.016, out=0.006, seed=702 + i * 4 + k, region='dark')
# nails on the side planks over the sills/posts and on the floor ends
for s_, sx in enumerate((-1, 1)):
    for j, z in enumerate((FZ + 0.155, FZ + 0.36)):
        for y in (Y0 + 0.17, 0.0, Y1 - 0.17):
            nail(p, (sx * (BX + 0.005), y + rr.uniform(-.03, .03), z + rr.uniform(-.04, .04)), (sx, 0, 0), size=0.018, out=0.006,
                 seed=720 + s_ * 10 + j * 3 + int(y * 10), bent=rr.random() < .1, region='dark' if rr.random() < .6 else 'iron')
for i in range(5):
    x = -0.46 + i * 0.23
    for y in (Y0 + 0.09, Y1 - 0.09):
        nail(p, (x + rr.uniform(-.04, .04), y, FZ + 0.05), (0, 0, 1), size=0.018, out=0.005, seed=740 + i * 2 + int(y > 0), region='dark')
# iron straps round the shafts where they leave the body, rope grip wraps at the tips + loose end
for i, sx in enumerate((-1, 1)):
    A_ = Vector((sx * 0.33, 0.35, FZ - 0.10)); B_ = Vector((sx * 0.37, -1.62, 0.07))
    dv = (B_ - A_).normalized()
    strap(p, A_.lerp(B_, 0.48), dv, (1, 0, 0), 0.06, 0.06, width=0.05, thick=0.008, seed=760 + i * 4, nails=2)
    for k in range(3):
        c = A_.lerp(B_, 0.86 + k * 0.025)
        E1 = Vector((1, 0, 0)); E2 = dv.cross(E1).normalized()
        rope(p, loop_rect(c, E1, E2, 0.075, 0.075, ch=0.3, jit=0.003, seed=770 + i * 3 + k), 0.018,
             up=(dv + E2 * 0.15 * (k - 1)).normalized(), sides=6, closed=True, k=4, seed=775 + i * 3 + k)
    t0 = A_.lerp(B_, 0.83) + Vector((sx * 0.06, 0, -0.03))
    rope(p, [t0, t0 + Vector((sx * 0.04, -0.05, -0.05)), t0 + Vector((sx * 0.09, -0.08, -0.07)), Vector((t0.x + sx * 0.14, t0.y - 0.06, 0.02))],
         0.016, up=(0, 0, 1), sides=6, step=0.05, k=4, seed=780 + i, flare_end=1.4)
# patch nailed over a hole in the right side wall, splinters on the broken front plank
patch(w, p, (BX + 0.0, -0.32, FZ + 0.22), (1, 0, 0), size=(0.18, 0.13), seed=790)
for i, (pp, dd) in enumerate((((-0.1, fy, FZ + 0.43), (-0.6, -0.2, 1)), ((-0.12, fy - 0.02, FZ + 0.3), (-1, -0.3, 0.3)),
                              ((-0.3, fy, FZ + 0.35), (0.4, -0.2, 1)), ((-0.32, fy, FZ + 0.27), (1, -0.4, -0.2)),
                              ((BX - 0.02, Y1 - 0.2, FZ + 0.46), (0.2, 0.3, 1)), ((-BX - 0.02, 0.15, FZ + 0.06), (-1, 0, -0.3)))):
    splinter(p, pp, dd, length=0.05 + 0.02 * (i % 3), r=0.009, seed=800 + i)
# load leftovers in the bed: seeds, husks, a dry leaf, needles, a clod
for i in range(7):
    pp = (rr.uniform(-0.45, 0.45), rr.uniform(Y0 + 0.12, Y1 - 0.12), FZ + 0.05)
    a = rr.uniform(0, 6.28)
    lump(p, pp, (0.05, 0.03, 0.03), 'seed', sides=6, rings=2, jitter=0.1, seed=820 + i, rot=a)
for i in range(4):
    a = rr.uniform(0, 6.28)
    leaf(p, (rr.uniform(-0.4, 0.4), rr.uniform(Y0 + 0.15, Y1 - 0.15), FZ + 0.048), (math.cos(a), math.sin(a), 0), 0.07, 0.05, region='seed',
         fold=0.6, curl=0.4, seed=830 + i)
leaf(p, (0.2, 0.25, FZ + 0.05), (-0.6, -1, 0), 0.17, 0.09, region='leaf_dry_1', seed=840)
leaf(p, (-0.32, -0.4, FZ + 0.05), (1, 0.3, 0), 0.13, 0.07, region='leaf_dry_2', seed=841)
for i in range(5):
    a = rr.uniform(0, 6.28); d_ = Vector((math.cos(a), math.sin(a), 0))
    obox(p, (rr.uniform(-0.45, 0.45), rr.uniform(Y0 + 0.1, Y1 - 0.1), FZ + 0.052), d_, Vector((0, 0, 1)).cross(d_), (0, 0, 1),
         (0.09, 0.006, 0.006), region='needles', seed=850 + i)
lump(p, (-0.1, 0.45, FZ + 0.048), (0.05, 0.04, 0.03), 'earth_props', sides=6, rings=2, jitter=0.25, seed=860)
# chock wedge under the left wheel, a flat stone at the right wheel
WX = BX + 0.12
beam(w, [(-WX, AY - 0.32, 0.0), (-WX, AY - 0.14, 0.0)], [0.12, 0.12], [0.12, 0.02], up=(0, 0, 1), jitter=0.004, seed=870, caps=('flat', 'flat'))
lump(p, (WX + 0.02, AY + 0.2, 0), (0.09, 0.07, 0.05), 'pebble', sides=6, rings=2, jitter=0.2, seed=871)
# ground litter under and around the cart
scatter_ground(p, (0, 0.1, 0), 0.95, squash=(1.0, 0.75), seed=88, clods=12, pebbles=10, chips=6, leaves=5, shavings=2, needles=5,
               avoid=[(-WX, AY, 0.12), (WX, AY, 0.12)])
for i, sx in enumerate((-1, 1)):
    scatter_ground(p, (sx * 0.37, -1.6, 0), 0.18, seed=90 + i, clods=4, pebbles=3, chips=0, leaves=1, shavings=0, needles=2, ring_min=0.4)
for i in range(2):
    lump(p, (rr.uniform(-0.6, 0.6), rr.uniform(-0.9, -0.7), 0), (0.05, 0.03, 0.03), 'seed', sides=6, rings=2, jitter=0.1, seed=880 + i,
         rot=rr.uniform(0, 6))

wo = w.to_object('AN19a_Wood')
po = p.to_object('AN19a_Props')
objs = [wo, po]
d0 = ak.ground(objs)
fbx = os.path.join(HERE, 'SM_AN19_Handcart.fbx')
ak.export_fbx(objs, fbx)
ak.save_blend(os.path.join(HERE, 'AN19_Handcart.blend'))
rec = ak.variant_record('AN19a', 'Handcart', objs, fbx,
                        solidity='solid (static prop; can be a physics prop)', collider='box for the body (1.3 x 1.4 x 0.6 at floor level), '
                        '2 thin boxes/cylinders for the wheels, 2 thin boxes for the shafts (or one convex hull overall)',
                        emissive=False,
                        notes='Modelled: floor 5 planks (one long-cracked), side walls 2+2, front wall with broken upper plank + splinters, low back plank, '
                              '4 corner posts, 16 pegs, 4 iron corner brackets, ~60 nails (body, wheel faces, wheel treads), axle, 2 sills, 2 shafts with iron straps '
                              'and rope grip wraps + loose ends, 2 faceted solid wheels with hex hubs, iron hub caps and linchpins, nailed patch, cracks/chips, '
                              'seeds/husks/leaves/needles/clod left in the bed, wheel chock + stone, ground litter. '
                              'Empty body; seeds/sacks are separate assets. Damaged variant = rotate/chip a wheel in the level.')
save_manifest(os.path.join(OUT, 'manifest.json'), {'asset': 'AN19 handcart', 'variants': [rec]})
print('TRIS', rec['tris'], rec['tris_by_material'], 'SIZE', rec['size_xyz_m'])
if RENDER:
    render_set(objs, OUT, SHEET, 'AN19 Handcart  %d tris  %s m' % (rec['tris'], rec['size_xyz_m']), el=24,
               extra=[(objs, 'detail', dict(az=-70, el=25, target=Vector((-0.6, -0.1, 0.45)) + d0, dist=2.6))])
