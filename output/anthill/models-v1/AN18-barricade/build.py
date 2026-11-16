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
PL = []
for i, (x, wd, h, top) in enumerate(planks):
    lean = rnd.uniform(0.03, 0.07)      # tops lean back a bit
    dx = rnd.uniform(-0.035, 0.035)     # sideways tilt
    th = rnd.uniform(0.05, 0.065)
    PL.append((x, dx, lean, th, h))
    pts = [(x, PY, 0.0), (x + dx * 0.45 + rnd.uniform(-.008, .008), PY + lean * 0.45, h * 0.45), (x + dx, PY + lean, h)]
    Wl = [wd, wd * rnd.uniform(.96, 1.02), wd * rnd.uniform(.93, 1.0)]
    cr = [(i % 4, rnd.uniform(0.1, 0.4), rnd.uniform(0.55, 0.9), 0.015), (0, rnd.uniform(0.6, 0.8), 1.0, 0.018)]
    ch = [(rnd.choice((0, 1)), rnd.uniform(0.3, 0.8), 0.08, 0.02), (rnd.choice((2, 3)), rnd.uniform(0.2, 0.7), 0.07, 0.018)]
    if i == 2:   # shot-through plank: broken in two, real hole between the pieces
        lo_pts = [(x, PY, 0.0), (x + dx * 0.2, PY + lean * 0.2, 0.25), (x + dx * 0.4, PY + lean * 0.4, 0.5)]
        beam(w, lo_pts, Wl[0], th, up=(0, 1, 0), jitter=0.01, seed=100 + i, caps=(None, 'broken'), broken=1.1, cracks=cr[:1])
        hi_pts = [(x + dx * 0.55, PY + lean * 0.55, 0.64), (x + dx * 0.8, PY + lean * 0.8, 0.9), (x + dx, PY + lean, h)]
        beam(w, hi_pts, Wl[2], th, up=(0, 1, 0), jitter=0.01, seed=150 + i, caps=('broken', top), endm=p, broken=1.1, chips=ch[:1])
        continue
    beam(w, pts, Wl, th, up=(0, 1, 0), jitter=0.01, seed=100 + i, caps=(None, top), endm=p, broken=0.9, cracks=cr, chips=ch)

# end posts: thick, leaning, different angles
beam(w, [(-1.15, -0.02, 0.0), (-1.13, 0.0, 0.7), (-1.08, 0.03, 1.43)], [0.21, 0.2, 0.19], [0.2, 0.2, 0.19], up=(0, 1, 0),
     jitter=0.012, seed=1, caps=(None, 'flat'), endm=p,
     cracks=[(0, 0.1, 0.5, 0.03), (3, 0.5, 1.0, 0.03), (2, 0.3, 0.8, 0.02)], chips=[(0, 0.65, 0.1, 0.035), (2, 0.25, 0.1, 0.03)])
beam(w, [(1.12, -0.03, 0.0), (1.13, -0.005, 0.75), (1.16, 0.04, 1.42)], [0.21, 0.2, 0.2], [0.21, 0.2, 0.19], up=(0, 1, 0),
     jitter=0.012, seed=2, caps=(None, 'bevel'), endm=p, bevel=0.25,
     cracks=[(0, 0.4, 0.85, 0.03), (1, 0.05, 0.4, 0.025)], chips=[(1, 0.5, 0.1, 0.035), (3, 0.8, 0.1, 0.03)])

# front top rail across the planks (rising slightly to the right), ends sticking out past the posts
beam(w, [(-1.22, -0.14, 0.98), (0.0, -0.15, 1.02), (1.25, -0.135, 1.07)], [0.16, 0.17, 0.16], 0.075, up=(0, 1, 0),
     jitter=0.01, seed=3, caps=('flat', 'flat'), endm=p,
     cracks=[(0, 0.1, 0.45, 0.02), (3, 0.6, 1.0, 0.02)], chips=[(0, 0.3, 0.1, 0.025), (1, 0.75, 0.1, 0.025), (3, 0.55, 0.08, 0.02)])
# short stub plank in the middle, nailed over the rail
beam(w, [(0.0, -0.205, 0.86), (0.01, -0.2, 1.37)], 0.17, 0.06, up=(0, 1, 0), jitter=0.01, seed=4, caps=('flat', 'flat'), endm=p)
# chunky end blocks on the protruding rail ends (front rail left, back rail right)
beam(w, [(-1.3, -0.14, 0.98), (-1.17, -0.14, 0.985)], 0.15, 0.15, up=(0, 1, 0), jitter=0.01, seed=5, caps=('flat', 'flat'), endm=p)
beam(w, [(1.17, 0.1, 0.93), (1.3, 0.1, 0.935)], 0.15, 0.15, up=(0, 1, 0), jitter=0.01, seed=6, caps=('flat', 'flat'), endm=p)

# back rails (behind the planks), through the posts
for i, (z0, z1) in enumerate(((0.92, 0.95), (0.42, 0.40))):
    beam(w, [(-1.22, 0.1, z0), (0.0, 0.105, (z0 + z1) / 2 + 0.01), (1.22, 0.1, z1)], 0.15, 0.075, up=(0, 1, 0), jitter=0.01,
         seed=10 + i, caps=('flat', 'flat'), endm=p, cracks=[(2, 0.2, 0.6, 0.02)], chips=[(1 + i, 0.4, 0.1, 0.025)])
# back legs: from the upper back rail down to the ground behind
for i, (x, dx) in enumerate(((-0.72, -0.07), (0.74, 0.08))):
    beam(w, [(x, 0.17, 1.05), (x + dx * .5, 0.33, 0.55), (x + dx * 1.6, 0.50, 0.05)], [0.13, 0.13, 0.13], [0.11, 0.115, 0.11],
         up=(1, 0, 0), jitter=0.01, seed=20 + i, caps=('flat', 'flat'), endm=p,
         cracks=[(i * 2, 0.2, 0.7, 0.02)], chips=[(1, 0.5, 0.08, 0.025)])
# pegs on the front rail over the planks and on the back rails
for i, x in enumerate((-0.93, -0.385, 0.44, 0.95)):
    z = 0.98 + (x + 1.3) / 2.6 * 0.09
    peg(w, (x, -0.18, z), (0, -1, 0), size=0.055, length=0.035, endm=p, seed=40 + i)
for i, (x, z) in enumerate(((-0.66, 0.935), (0.71, 0.945), (-0.105, 0.41), (0.44, 0.41))):
    peg(w, (x, 0.14, z), (0, 1, 0), size=0.05, length=0.03, endm=p, seed=50 + i)


# ---------------- detail pass
import random as _r
rr = _r.Random(18)
# nails: each plank to both back rails, a few on the front rail
for i, (x, dx, lean, th, h) in enumerate(PL):
    for z in (0.93, 0.41):
        if i == 2 and 0.5 < z < 0.64:
            continue
        for k in range(2):
            xx = x + dx * z / h + (k - 0.5) * 0.09 + rr.uniform(-.01, .01)
            nail(p, (xx, 0.14, z + rr.uniform(-.03, .03)), (0, 1, 0), size=0.018, out=0.006, seed=400 + i * 4 + k + int(z * 10),
                 bent=rr.random() < 0.15, region='iron' if rr.random() < .5 else 'dark')
for i, x in enumerate((-0.66, -0.105, 0.165, 0.71)):
    z = 0.98 + (x + 1.3) / 2.6 * 0.09
    nail(p, (x + 0.05, -0.18, z + 0.04), (0, -1, 0), size=0.02, out=0.007, seed=460 + i, region='dark')
# bullet holes on the front (dark holes with splinters around)
u0, v0, u1, v1 = p.region('dark')
for i, (x, z) in enumerate(((-0.86, 0.72), (-0.12, 0.58), (0.5, 0.78), (0.7, 0.4), (0.2, 0.3), (-0.62, 0.35))):
    ix = min(range(len(PL)), key=lambda j: abs(PL[j][0] - x))
    X0, dx, lean, th, h = PL[ix]
    yf = PY + lean * z / h - th / 2 - 0.004
    xx = X0 + dx * z / h + (x - PL[ix][0]) * 0.3
    hole = [Vector((xx + math.cos(a) * 0.022 * rr.uniform(.7, 1.3), yf, z + math.sin(a) * 0.022 * rr.uniform(.7, 1.3))) for a in [k * 6.283 / 6 for k in range(6)]]
    oface(p, hole, [(u0 + (k / 6) * (u1 - u0), v0 + .5 * (v1 - v0)) for k in range(6)], (0, -1, 0))
    for k in range(3):
        a = rr.uniform(0, 6.28)
        splinter(p, (xx + math.cos(a) * 0.025, yf, z + math.sin(a) * 0.025), (math.cos(a), -0.8, math.sin(a)), length=0.04, r=0.007, seed=470 + i * 3 + k)
# patch board nailed over a hole on plank 6 (front)
X0, dx, lean, th, h = PL[5]
patch(w, p, (X0 + dx * 0.5, PY + lean * 0.55 / h - th / 2 - 0.004, 0.55), (0, -1, 0), size=(0.2, 0.16), seed=500)
# splinters on the broken plank tops and the hole
for i, (x, dx, lean, th, h) in enumerate(PL):
    if planks[i][3] == 'broken' or i == 2:
        for k in range(2):
            zz = (h - 0.06) if i != 2 else (0.5 if k == 0 else 0.64)
            splinter(p, (x + dx * zz / h + rr.uniform(-.08, .08), PY + lean * zz / h, zz), (rr.uniform(-.4, .4), rr.uniform(-.4, .4), 1 if k == 0 or i != 2 else -1),
                     length=rr.uniform(0.05, 0.09), r=0.01, seed=520 + i * 2 + k)
# iron straps binding the front rail to the end posts
strap(p, Vector((-1.12, -0.06, 0.985)), (0.06, 0.03, 1), (1, 0, 0), 0.105, 0.15, width=0.06, thick=0.01, seed=540, nails=3)
strap(p, Vector((1.15, -0.04, 1.06)), (0.04, 0.05, 1), (1, 0, 0), 0.105, 0.16, width=0.06, thick=0.01, seed=544, nails=3)
# rope lashing tying each back leg to the upper back rail, loose ends
for i, (x, dx) in enumerate(((-0.72, -0.07), (0.74, 0.08))):
    for k in range(2):
        c = Vector((x + (k - 0.5) * 0.05, 0.14, 0.97))
        E1 = Vector((0, 1, 0.5 * (1 if k else -1))).normalized()
        rope(p, loop_rect(c, (1, 0, 0) if False else E1, (1, 0, 0), 0.11, 0.1, ch=0.3, jit=0.004, seed=560 + i * 2 + k), 0.02,
             up=E1.cross(Vector((1, 0, 0))), sides=6, closed=True, k=4, seed=564 + i * 2 + k)
    t0 = Vector((x + 0.06, 0.2, 0.88))
    rope(p, [t0, t0 + Vector((0.01, 0.02, -0.1)), t0 + Vector((0.03, 0.01, -0.22))], 0.018, up=(1, 0, 0), sides=6, step=0.05, k=4,
         seed=570 + i, flare_end=1.4)
# extra small boards: a diagonal batten on the back, a short board leaning on the back
beam(w, [(-0.3, 0.12, 0.47), (0.3, 0.12, 0.89)], 0.1, 0.035, up=(0, 1, 0), jitter=0.006, seed=580, caps=('flat', 'broken'), broken=0.8,
     cracks=[(2, 0.2, 0.7, 0.01)])
for k, (x, z) in enumerate(((-0.27, 0.5), (0.26, 0.86))):
    nail(p, (x, 0.14, z), (0, 1, 0), size=0.018, out=0.006, seed=585 + k, region='dark')
beam(w, [(0.25, 0.42, 0.0), (0.27, 0.2, 0.62)], 0.16, 0.03, up=(0, 1, -0.35), jitter=0.006, seed=590, caps=('flat', 'broken'), broken=0.7,
     cracks=[(2, 0.3, 0.9, 0.01)])
# wedges at the post feet, flat stones propping the leg feet
for i, (x, y, r) in enumerate(((-1.0, -0.03, 0.0), (-1.29, 0.0, 3.1416), (1.0, -0.04, 3.1416), (1.27, 0.02, 0.0))):
    dv = Vector((math.cos(r), math.sin(r), 0))
    c = Vector((x, y, 0.06))
    beam(w, [c + dv * 0.05 + Vector((0, 0, -0.06)), c + Vector((0, 0, 0.07))], [0.1, 0.09], [0.045, 0.01], up=dv.cross(Vector((0, 0, 1))),
         jitter=0.004, seed=600 + i, caps=(None, 'flat'))
for i, (x, dx) in enumerate(((-0.72, -0.07), (0.74, 0.08))):
    fx = x + dx * 1.6
    for k in range(2):
        lump(p, (fx + (k - .5) * 0.16, 0.56 + rr.uniform(-.05, .05), 0), (0.07, 0.055, 0.04), 'pebble', sides=6, rings=2, jitter=0.2,
             seed=610 + i * 2 + k, rot=rr.uniform(0, 6))
# soil heaped along the front foot (dug-in cover) and litter
for i in range(18):
    x = -1.15 + i * 2.3 / 17 + rr.uniform(-.04, .04)
    s_ = rr.uniform(0.05, 0.1)
    lump(p, (x, PY - 0.06 + rr.uniform(-.03, .04), 0), (s_ * 1.4, s_, s_ * 0.8), 'earth_props', sides=6, rings=2, jitter=0.25,
         seed=620 + i, rot=rr.uniform(0, 6))
scatter_ground(p, (0.0, -0.26, 0), 1.2, squash=(1.0, 0.17), seed=31, clods=10, pebbles=10, chips=8, leaves=5, shavings=3, needles=6)
scatter_ground(p, (0.0, 0.32, 0), 1.1, squash=(1.0, 0.2), seed=32, clods=7, pebbles=6, chips=8, leaves=4, shavings=4, needles=5,
               avoid=[(-0.79, 0.4, 0.12), (0.82, 0.4, 0.12)])

wo = w.to_object('AN18a_Wood')
po = p.to_object('AN18a_Props')
objs = [wo, po]
d0 = ak.ground(objs)
fbx = os.path.join(HERE, 'SM_AN18_Barricade.fbx')
ak.export_fbx(objs, fbx)
ak.save_blend(os.path.join(HERE, 'AN18_Barricade.blend'))
rec = ak.variant_record('AN18a', 'Barricade', objs, fbx,
                        solidity='solid (cover)', collider='box 2.4 x 0.25 x 1.3 for the plank wall (blocks shots/movement) + 2 boxes for the back legs',
                        cover_height_m='planks 1.18-1.37, posts ~1.5',
                        emissive=False,
                        notes='Modelled: 8 planks (3 broken tops, 1 shot through into two pieces with a real gap), cracks/chips on every timber, front rail + stub, '
                              '2 back rails, diagonal back batten, board leaning on the back, 2 end posts with iron straps, 2 back legs lashed with rope '
                              '(loose ends) and propped on flat stones, 6 bullet holes with splinters, nailed patch, ~40 nails, 8 pegs, 4 foot wedges, '
                              'soil heaped along the front foot, clods/pebbles/chips/shavings/needles/leaves. Olive wear from the Wood texture.')
save_manifest(os.path.join(OUT, 'manifest.json'), {'asset': 'AN18 barricade', 'variants': [rec]})
print('TRIS', rec['tris'], rec['tris_by_material'], 'SIZE', rec['size_xyz_m'])
if RENDER:
    render_set(objs, OUT, SHEET, 'AN18 Barricade  %d tris  %s m' % (rec['tris'], rec['size_xyz_m']), el=18,
               extra=[(objs, 'detail-front', dict(az=-25, el=15, target=Vector((-0.4, 0, 0.7)) + d0, dist=3.2)),
                      (objs, 'detail-back', dict(az=150, el=25, target=Vector((0.4, 0.2, 0.6)) + d0, dist=3.2))])
