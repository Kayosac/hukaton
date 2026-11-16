"""AN17 Rope railing section (Секция верёвочного ограждения): two unequal square posts with carved notches,
one thick sagging rope wrapped around both posts, a knot with two hanging tails on the big post.
Pivot bottom centre, front -Y, section runs along X (3 m)."""
import sys, os, math
sys.path.insert(0, '/home/claude/ant/models')
from an_woodlib import *
import ak

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
SHEET = '/home/claude/ant/concepts/environment-pack-v3/asset-concepts-v1/sheets/17-rope-railing.png'
RENDER = '--norender' not in sys.argv
R = 0.042  # rope radius

ak.reset()
w = ak.Mesh('Wood')
p = ak.Mesh('Props')

# post A (left, big): wider at the foot, leans a little, notches on front and side
AX = -1.30
post_prism(w, (AX, 0, 0), (AX + 0.03, 0.01, 1.0), 0.32, 0.27, 0.27, 0.235, seed=1, jitter=0.012, top_tilt=(-0.02, 0.012),
           bevel=0.025, endm=p,
           notches=[('ny', 0.2, 0.62, 0.86, 0.92, 0.04), ('ny', 0.4, 0.82, 0.14, 0.21, 0.04),
                    ('px', 0.3, 0.75, 0.87, 0.92, 0.03), ('nx', 0.25, 0.6, 0.22, 0.28, 0.025),
                    ('py', 0.3, 0.7, 0.4, 0.46, 0.025)])
# post B (right, smaller and shorter), leaning the other way
BX = 1.33
post_prism(w, (BX, 0, 0), (BX - 0.035, -0.01, 0.9), 0.24, 0.22, 0.2, 0.185, seed=2, jitter=0.01, top_tilt=(0.015, -0.01),
           bevel=0.02, endm=p,
           notches=[('px', 0.2, 0.62, 0.86, 0.92, 0.03), ('ny', 0.38, 0.8, 0.2, 0.27, 0.035), ('ny', 0.15, 0.55, 0.88, 0.93, 0.03), ('nx', 0.35, 0.72, 0.12, 0.17, 0.02),
                    ('py', 0.2, 0.6, 0.78, 0.83, 0.02)])

def post_half(x0, x1, wb, wt, h, z, tilt_x):
    f = z / h
    return (wb + (wt - wb) * f) / 2, x0 + tilt_x * f

# wraps around post A (3 turns, slightly tilted) and B (2 turns)
for i, z in enumerate((0.555, 0.63, 0.705, 0.78)):
    hw, cx = post_half(AX, AX, 0.32, 0.27, 1.0, z, 0.03)
    hd = (0.27 + (0.235 - 0.27) * z) / 2
    e1 = Vector((1, 0, 0.05 * (1 if i % 2 else -1)))
    loop = loop_rect((cx, 0.0, z), e1, (0, 1, 0.03), hw + R * .85, hd + R * .85, ch=0.25, jit=0.005, seed=10 + i)
    rope(p, loop, R, up=e1.cross(Vector((0, 1, 0.03))), sides=6, closed=True, step=0.11, k=5, seed=20 + i)
for i, z in enumerate((0.6, 0.675)):
    hw, cx = post_half(BX, BX, 0.24, 0.2, 0.9, z, -0.035)
    hd = (0.22 + (0.185 - 0.22) * z / 0.9) / 2
    e1 = Vector((1, 0, 0.06 * (1 if i % 2 else -1)))
    loop = loop_rect((cx, 0.0, z), e1, (0, 1, -0.02), hw + R * .85, hd + R * .85, ch=0.25, jit=0.005, seed=30 + i)
    rope(p, loop, R, up=e1.cross(Vector((0, 1, -0.02))), sides=6, closed=True, step=0.11, k=5, seed=40 + i)

# main rope: leaves A's wraps on its right face, sags, enters B's wraps
x0, z0 = AX + 0.13, 0.70
x1, z1 = BX - 0.10, 0.64
pts = []
N = 16
for i in range(N + 1):
    t = i / N
    x = x0 + (x1 - x0) * t
    z = z0 + (z1 - z0) * t - 0.19 * 4 * t * (1 - t) * (1 + 0.15 * math.sin(t * 7))
    y = -0.03 * math.sin(t * math.pi) + 0.012 * math.sin(t * 11)
    pts.append((x, y, z))
rope(p, pts, R, up=(0, 1, 0), sides=6, step=0.1, k=5, seed=50)

# knot on A's front-outer corner: two crossing small loops + a lump, two tails hanging down
kc = Vector((AX - 0.12, -0.15, 0.70))
for i, (e1, e2) in enumerate((((0.4, -1, 0.9), (1, 0.4, 0)), ((-0.5, -1, -0.8), (1, -0.3, 0.2)))):
    E1 = Vector(e1).normalized(); E2 = Vector(e2).normalized()
    loop = loop_rect(kc, E1, E2, 0.07, 0.05, ch=0.4, jit=0.004, seed=60 + i)
    rope(p, loop, R * 0.92, up=E1.cross(E2), sides=6, closed=True, step=None, k=4, seed=62 + i)
for i, (dx, dy, ln, sw) in enumerate(((0.03, -0.02, 0.33, 0.05), (-0.05, 0.015, 0.27, -0.04))):
    tail = [kc + Vector((dx * .4, dy, -0.02)), kc + Vector((dx, dy - 0.02, -ln * 0.4)),
            kc + Vector((dx + sw, dy - 0.03, -ln * 0.75)), kc + Vector((dx + sw * 1.4, dy - 0.02, -ln))]
    rope(p, tail, R * 0.85, up=(1, 0, 0), sides=6, step=0.07, k=4, seed=70 + i, flare_end=1.35)
# short tail end on B (rope end tucked under the wraps)
tb = Vector((BX + 0.14, -0.05, 0.64))
rope(p, [tb, tb + Vector((0.035, -0.03, -0.08)), tb + Vector((0.05, -0.05, -0.17))], R * 0.85, up=(0, 1, 0), sides=6,
     step=0.06, k=4, seed=80, flare_end=1.3)


# ---------------- detail pass
import random as _r
rr = _r.Random(17)
# frayed fibres at the tail ends and along the rope
def fray(pt, d, n, seed):
    r2 = _r.Random(seed)
    for k in range(n):
        dd = Vector(d) + Vector((r2.uniform(-.6, .6), r2.uniform(-.6, .6), r2.uniform(-.3, .3)))
        P0 = Vector(pt) + Vector((r2.uniform(-.015, .015), r2.uniform(-.015, .015), 0))
        rope(p, [P0, P0 + dd.normalized() * r2.uniform(.03, .06), P0 + dd.normalized() * r2.uniform(.06, .1) + Vector((0, 0, -.02))],
             0.006, up=(1, 0, 0) if abs(dd.normalized().x) < .8 else (0, 1, 0), sides=3, k=2, seed=seed + k, radii=[.006, .004, .002],
             caps=False)
for i, (dx, dy, ln, sw) in enumerate(((0.03, -0.02, 0.33, 0.05), (-0.05, 0.015, 0.27, -0.04))):
    fray(kc + Vector((dx + sw * 1.4, dy - 0.02, -ln)), (0, 0, -1), 4, 200 + i * 10)
fray(tb + Vector((0.05, -0.05, -0.17)), (0.2, -0.3, -1), 3, 230)
for i in range(7):
    t = (i + 0.5) / 7
    q = pts[int(t * (len(pts) - 1))]
    fray(Vector(q) + Vector((0, 0, -0.035)), (rr.uniform(-1, 1), rr.uniform(-1, 1), -0.8), 1, 240 + i)
# iron staples pinning the rope to the posts (front faces)
for i, (x, z, y) in enumerate(((AX + 0.06, 0.70, -0.165), (BX - 0.04, 0.64, -0.14))):
    U = [Vector((x - 0.05, y + 0.03, z - 0.045)), Vector((x - 0.05, y - 0.02, z - 0.05)), Vector((x - 0.035, y - 0.04, z)),
         Vector((x + 0.035, y - 0.04, z)), Vector((x + 0.05, y - 0.02, z + 0.05)), Vector((x + 0.05, y + 0.03, z + 0.045))]
    rope(p, U, 0.009, up=(0, 1, 0), sides=4, region='iron', fit=True, rot=0.78)
# iron band round post B's foot, nails, patch on post A's back
strap(p, Vector((BX - 0.004, 0.0, 0.12)), (-0.04, -0.01, 0.9), (1, 0, 0), 0.118, 0.108, width=0.055, thick=0.009, seed=250, nails=3)
patch(w, p, (AX + 0.01, 0.124, 0.32), (0, 1, 0), size=(0.16, 0.12), seed=260)
for i, (pp, nn) in enumerate((((AX - 0.08, -0.137, 0.45), (0, -1, 0)), ((AX + 0.09, -0.13, 0.9), (0, -1, 0)), ((AX + 0.155, 0.05, 0.4), (1, 0, 0)),
                              ((BX + 0.05, -0.105, 0.42), (0, -1, 0)), ((BX - 0.112, 0.02, 0.3), (-1, 0, 0)), ((BX + 0.11, 0.03, 0.75), (1, 0, 0)),
                              ((AX - 0.15, 0.06, 0.2), (-1, 0, 0)))):
    nail(p, pp, nn, size=0.02, out=0.007, seed=270 + i, bent=(i in (1, 5)), region='dark' if i % 2 else 'iron')
# split tops: splinters and a split-off sliver on each post top
for i, (pp, dd) in enumerate((((AX - 0.1, -0.1, 0.99), (-0.4, -0.5, 1)), ((AX + 0.12, 0.08, 0.98), (0.5, 0.3, 1)), ((BX + 0.08, -0.07, 0.9), (0.4, -0.3, 1)),
                              ((BX - 0.09, 0.08, 0.89), (-0.5, 0.3, 1)), ((AX + 0.16, -0.1, 0.05), (1, -0.4, 0.3)), ((BX - 0.12, -0.1, 0.06), (-1, -0.3, 0.3)))):
    splinter(p, pp, dd, length=0.05 + 0.02 * (i % 2), r=0.009, seed=290 + i)
# wedges driven at the post feet
for i, (x, y, r) in enumerate(((AX + 0.17, 0.0, 0.0), (AX - 0.03, -0.145, -1.5708), (BX - 0.13, 0.02, 3.1416), (BX + 0.02, 0.12, 1.5708))):
    dv = Vector((math.cos(r), math.sin(r), 0))
    c = Vector((x, y, 0.06))
    beam(w, [c + dv * 0.05 + Vector((0, 0, -0.06)), c + Vector((0, 0, 0.07))], [0.1, 0.09], [0.045, 0.01], up=dv.cross(Vector((0, 0, 1))),
         jitter=0.004, seed=300 + i, caps=(None, 'flat'))
# a broken short board lying in the litter near post A
beam(w, [(AX + 0.42, -0.22, 0.02), (AX + 0.75, -0.3, 0.035)], 0.12, 0.03, up=(0, 0, 1), jitter=0.005, seed=310, caps=('flat', 'broken'), broken=1.0,
     cracks=[(2, 0.2, 0.8, 0.008)])
nail(p, (AX + 0.5, -0.24, 0.05), (0, 0, 1), size=0.02, out=0.006, bent=True, seed=311)
# earth heaped at the feet + litter along the section
for j, (X, rad) in enumerate(((AX, 0.2), (BX, 0.16))):
    for i in range(7):
        a = i / 7 * 6.283 + rr.uniform(-.2, .2)
        s_ = rr.uniform(0.045, 0.085)
        lump(p, (X + math.cos(a) * rad, math.sin(a) * rad * 0.9, 0), (s_ * 1.3, s_, s_ * 0.75), 'earth_props', sides=6, rings=2,
             jitter=0.25, seed=320 + j * 10 + i, rot=a)
    scatter_ground(p, (X, 0, 0), 0.34, squash=(1.0, 0.8), seed=40 + j, clods=7, pebbles=6, chips=4, leaves=3, shavings=2, needles=3,
                   ring_min=0.5)
scatter_ground(p, (0.0, -0.05, 0), 1.0, squash=(0.9, 0.18), seed=50, clods=5, pebbles=7, chips=3, leaves=4, shavings=1, needles=5)

wo = w.to_object('AN17a_Wood')
po = p.to_object('AN17a_Props')
objs = [wo, po]
d0 = ak.ground(objs)
fbx = os.path.join(HERE, 'SM_AN17_RopeRailing.fbx')
ak.export_fbx(objs, fbx)
ak.save_blend(os.path.join(HERE, 'AN17_RopeRailing.blend'))
rec = ak.variant_record('AN17a', 'RopeRailing', objs, fbx,
                        solidity='solid', collider='2 boxes for the posts + 1 thin box (or capsule) along the rope at 0.55-0.8 m; '
                                                  'rope collider blocks walking off the edge',
                        emissive=False,
                        joins='post A centre x=%.3f, post B centre x=%.3f (pivot = bounds centre); neighbouring sections overlap one post or leave ~0.1 m; corners by rotating a section' % (AX + d0.x, BX + d0.x),
                        notes='Modelled: posts with carved notches, 6 rope wraps, sagging rope, knot (2 loops) with tails, frayed fibres, iron staples, '
                              'iron foot band, nailed patch, loose/bent nails, splinters at split tops, 4 foot wedges, broken board on the ground, '
                              'earth heaped at the feet, clods/pebbles/chips/shavings/needles/leaves. Rope = Props `rope`, post tops end_grain.')
save_manifest(os.path.join(OUT, 'manifest.json'), {'asset': 'AN17 rope-railing', 'variants': [rec]})
print('TRIS', rec['tris'], rec['tris_by_material'], 'SIZE', rec['size_xyz_m'])
if RENDER:
    render_set(objs, OUT, SHEET, 'AN17 Rope railing  %d tris  %s m' % (rec['tris'], rec['size_xyz_m']), el=16,
               extra=[(objs, 'detail', dict(az=-35, el=20, target=Vector((AX + 0.1, 0, 0.5)) + d0, dist=2.4))])
