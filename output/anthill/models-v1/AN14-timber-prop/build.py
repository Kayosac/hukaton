"""AN14 Timber prop (Деревянная подпорка): crooked leaning post on a plinth, crossbeam, diagonal brace,
rope X-lashing at the post/beam joint. Pivot bottom centre, front -Y."""
import sys, os, math
sys.path.insert(0, '/home/claude/ant/models')
from an_woodlib import *
import ak

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
SHEET = '/home/claude/ant/concepts/environment-pack-v3/asset-concepts-v1/sheets/14-timber-prop.png'
RENDER = '--norender' not in sys.argv

ak.reset()
w = ak.Mesh('Wood')
p = ak.Mesh('Props')

# plinth (wide foot block with sloped top)
beam(w, [(0.0, 0.0, 0.0), (0.01, 0.0, 0.13), (0.015, 0.005, 0.22)], [0.50, 0.49, 0.36], [0.44, 0.43, 0.32],
     up=(0, 1, 0), jitter=0.012, seed=11, caps=(None, 'flat'),
     chips=[(0, 0.6, 0.1, 0.045), (2, 0.5, 0.1, 0.04), (3, 0.75, 0.08, 0.035)], cracks=[(1, 0.0, 0.8, 0.025)])
# post: kinked, leaning right going up, tapering
post = [(0.0, 0.0, 0.15), (0.012, 0.0, 0.5), (0.03, 0.01, 0.95), (0.035, -0.01, 1.22), (0.06, -0.015, 1.45), (0.12, 0.0, 1.72), (0.17, 0.0, 1.95), (0.30, 0.015, 2.5), (0.36, 0.0, 2.93)]
beam(w, post, [0.31, 0.315, 0.30, 0.28, 0.285, 0.265, 0.27, 0.25, 0.24], [0.29, 0.285, 0.28, 0.27, 0.275, 0.255, 0.26, 0.24, 0.23], up=(0, 1, 0),
     jitter=0.018, seed=12, caps=(None, 'bevel'), endm=p, bevel=0.35,
     cracks=[(0, 0.08, 0.42, 0.035), (3, 0.33, 0.72, 0.03), (1, 0.52, 0.9, 0.025), (2, 0.86, 1.0, 0.035), (0, 0.6, 0.78, 0.02)],
     chips=[(0, 0.3, 0.12, 0.035), (2, 0.62, 0.1, 0.04), (1, 0.18, 0.15, 0.035), (3, 0.5, 0.1, 0.03)])
# crossbeam: rests across the post below its top, runs out to -X, slightly crooked, left end cut
cb = [(0.58, -0.07, 2.66), (0.2, -0.065, 2.68), (-0.35, -0.06, 2.71), (-1.02, -0.05, 2.77)]
beam(w, cb, [0.25, 0.25, 0.24, 0.23], [0.25, 0.24, 0.24, 0.25], up=(0, 1, 0), jitter=0.016, seed=13,
     caps=('flat', 'flat'), endm=p,
     cracks=[(3, 0.55, 1.0, 0.04), (0, 0.2, 0.55, 0.03), (2, 0.4, 0.9, 0.025), (1, 0.75, 1.0, 0.025)],
     chips=[(1, 0.7, 0.12, 0.04), (0, 0.96, 0.07, 0.035), (2, 0.5, 0.1, 0.035), (3, 0.3, 0.12, 0.03)])
# diagonal brace from the post knee to the beam underside
beam(w, [(0.08, -0.06, 1.55), (-0.25, -0.065, 2.08), (-0.62, -0.06, 2.62)], [0.17, 0.165, 0.16], [0.15, 0.15, 0.15],
     up=(0, 1, 0), jitter=0.012, seed=14, caps=('flat', 'flat'),
     cracks=[(0, 0.15, 0.6, 0.025), (2, 0.5, 0.95, 0.02)], chips=[(3, 0.4, 0.1, 0.03), (1, 0.7, 0.1, 0.025)])

# wedges driven between post foot and plinth
for i, (x, y, r) in enumerate(((0.17, -0.02, 0.0), (-0.02, -0.165, 1.5708), (-0.165, 0.03, 3.1416), (0.02, 0.155, -1.5708))):
    d = Vector((math.cos(r), math.sin(r), 0))
    c = Vector((x, y, 0.27))
    beam(w, [c - d * 0.06 + Vector((0, 0, -0.05)), c + d * 0.06 + Vector((0, 0, 0.04))], [0.13, 0.12], [0.07, 0.015], up=(0, 0, 1) if False else d.cross(Vector((0, 0, 1))),
         jitter=0.005, seed=70 + i, caps=('flat', 'flat'))
# rope lashing: two diagonal pairs of loops around the joint -> X on the front and back
jc = Vector((0.36, -0.035, 2.68))
for i, (a, off) in enumerate(((0.78, 0.0), (-0.78, 0.0))):
    e2 = Vector((math.sin(a), 0, math.cos(a)))
    c = jc + e2.cross(Vector((0, 1, 0))).normalized() * off
    loop = loop_rect(c, (0, 1, 0), e2, 0.2, 0.225, ch=0.22, jit=0.004, seed=40 + i)
    rope(p, loop, 0.036, up=e2.cross(Vector((0, 1, 0))), sides=6, closed=True, step=0.1, k=5, seed=50 + i)
# a horizontal binding band just under the beam
loop = loop_rect(Vector((0.32, -0.02, 2.47)), (1, 0, 0), (0, 1, 0), 0.165, 0.175, ch=0.3, jit=0.005, seed=60)
rope(p, loop, 0.028, up=(0, 0, 1), sides=6, closed=True, step=0.1, k=5, seed=61)


# ---------------- detail pass
# third lashing band above the beam around the post top
loop = loop_rect(Vector((0.355, 0.0, 2.84)), (1, 0, 0.04), (0, 1, 0), 0.14, 0.135, ch=0.3, jit=0.004, seed=62)
rope(p, loop, 0.026, up=(0.04, 0, 1), sides=6, closed=True, step=0.09, k=5, seed=63)
# knot + loose rope end hanging from the main lashing
kc = Vector((0.22, -0.235, 2.56))
for i, (e1, e2) in enumerate((((1, 0, 0.6), (0, -1, 0.3)), ((0.3, -1, -0.5), (1, 0.2, 0.2)))):
    E1 = Vector(e1).normalized(); E2 = Vector(e2).normalized()
    rope(p, loop_rect(kc, E1, E2, 0.05, 0.035, ch=0.4, jit=0.003, seed=64 + i), 0.026, up=E1.cross(E2), sides=6, closed=True, k=4, seed=66 + i)
rope(p, [kc, kc + Vector((0.02, -0.02, -0.12)), kc + Vector((0.05, -0.03, -0.27)), kc + Vector((0.04, -0.02, -0.4))], 0.025,
     up=(1, 0, 0), sides=6, step=0.06, k=4, seed=68, flare_end=1.4)
rope(p, [kc, kc + Vector((-0.04, -0.015, -0.08)), kc + Vector((-0.06, -0.02, -0.17))], 0.023, up=(1, 0, 0), sides=6, step=0.05, k=4,
     seed=69, flare_end=1.35)
# lashing at the brace head: two turns around beam + brace, short tail
for i, tl in enumerate((-0.12, 0.12)):
    c = Vector((-0.56 + i * 0.07, -0.06, 2.655))
    E2 = Vector((tl, 0, 1)).normalized()
    rope(p, loop_rect(c, (0, 1, 0), E2, 0.16, 0.2, ch=0.25, jit=0.004, seed=70 + i), 0.026, up=Vector((0, 1, 0)).cross(E2), sides=6,
         closed=True, step=0.09, k=5, seed=72 + i)
t0 = Vector((-0.45, -0.22, 2.5))
rope(p, [t0, t0 + Vector((0.03, -0.01, -0.1)), t0 + Vector((0.02, -0.02, -0.22))], 0.024, up=(1, 0, 0), sides=6, step=0.05, k=4, seed=74,
     flare_end=1.35)
# iron straps: post above the plinth, beam near its left end
strap(p, Vector((0.012, 0.0, 0.44)), (0.03, 0, 1), (1, 0, 0), 0.158, 0.143, width=0.065, thick=0.01, seed=80, nails=4)
strap(p, Vector((-0.82, -0.052, 2.755)), (-0.99, 0.015, 0.09), (0, 1, 0), 0.125, 0.122, width=0.06, thick=0.01, seed=84, nails=3)
# iron plate over the brace foot / post joint
pl_c = Vector((0.03, -0.142, 1.62))
bd_ = Vector((-0.55, 0, 0.83)).normalized()
obox(p, pl_c, bd_, (0, 0, 1) if False else Vector((0, -1, 0)).cross(bd_), (0, -1, 0), (0.26, 0.09, 0.01), region='iron', jitter=0.002)
for i, f in enumerate((-0.1, -0.03, 0.05, 0.11)):
    nail(p, pl_c + bd_ * f + Vector((0, -0.006, 0)), (0, -1, 0), size=0.02, out=0.007, seed=90 + i, region='dark')
# pegs: brace head into the beam, brace foot into the post (+ through-end on the back)
peg(w, (-0.57, -0.19, 2.69), (0, -1, 0), size=0.06, length=0.04, endm=p, seed=101)
peg(w, (0.14, -0.14, 1.52), (0, -1, 0), size=0.06, length=0.04, endm=p, seed=102)
peg(w, (0.1, 0.14, 1.6), (0, 1, 0), size=0.055, length=0.03, endm=p, seed=103)
peg(w, (-0.15, -0.19, 2.69), (0, -1, 0), size=0.055, length=0.035, endm=p, seed=104)
# patch over a hole on the post front + loose nails
patch(w, p, (0.035, -0.143, 1.1), (0, -1, 0), size=(0.17, 0.13), seed=110)
for i, (pp, nn) in enumerate((((0.02, -0.148, 0.75), (0, -1, 0)), ((0.07, -0.147, 0.82), (0, -1, 0)), ((0.24, -0.135, 2.2), (0, -1, 0)),
                              ((-0.3, -0.07, 2.84), (0, 0, 1)), ((-0.62, -0.06, 2.88), (0, 0, 1)), ((0.19, 0.0, 1.0), (1, 0, 0)),
                              ((-0.13, 0.0, 1.3), (-1, 0, 0)), ((0.45, -0.01, 2.2), (1, 0, 0)))):
    nail(p, pp, nn, size=0.02, out=0.007, seed=120 + i, bent=(i in (1, 4, 6)), region='dark' if i % 3 == 0 else 'iron')
# splinters at the cracked beam end, the chips and the plinth
for i, (pp, dd) in enumerate((((-1.0, -0.06, 2.9), (-1, 0, 0.4)), ((-1.02, -0.15, 2.84), (-1, -0.3, 0.2)), ((-0.98, 0.05, 2.7), (-1, 0.2, -0.3)),
                              ((0.07, -0.15, 0.6), (0.3, -1, 0.5)), ((-0.15, 0.1, 1.25), (-1, 0.2, 0.4)), ((0.2, -0.2, 0.17), (0.4, -1, 0.3)),
                              ((-0.24, 0.12, 0.1), (-1, 0.5, 0.2)), ((0.25, 0.05, 2.0), (1, 0, 0.6)))):
    splinter(p, pp, dd, length=0.06 + 0.02 * (i % 3), r=0.009, seed=130 + i)
# earth heaped against the plinth, then the ground litter
import random as _r
rr = _r.Random(9)
for i in range(9):
    a = i / 9 * 6.283 + rr.uniform(-.2, .2)
    pp = Vector((math.cos(a) * 0.29, math.sin(a) * 0.26, 0))
    s_ = rr.uniform(0.06, 0.11)
    lump(p, pp, (s_ * 1.3, s_, s_ * 0.8), 'earth_props', sides=6, rings=2, jitter=0.25, seed=140 + i, rot=a)
scatter_ground(p, (0.01, 0, 0), 0.58, squash=(1.25, 0.85), seed=7, clods=14, pebbles=10, chips=10, leaves=6, shavings=4, needles=6, ring_min=0.5)

wo = w.to_object('AN14a_Wood')
po = p.to_object('AN14a_Props')
objs = [wo, po]
d0 = ak.ground(objs)
fbx = os.path.join(HERE, 'SM_AN14_TimberProp.fbx')
ak.export_fbx(objs, fbx)
ak.save_blend(os.path.join(HERE, 'AN14_TimberProp.blend'))
rec = ak.variant_record('AN14a', 'TimberProp', objs, fbx,
                        solidity='solid', collider='3 box colliders: post (tilted, follow the kink roughly), crossbeam, brace; or one convex per part',
                        emissive=False,
                        notes='Modelled: kinked post, plinth, crossbeam, brace, 4 wedges, cracks (V-grooves) and chipped edges on all beams, X lashing + 2 bands, knot with 2 loose ends, brace-head lashing with tail, 2 iron straps + iron plate with nails, 4 pegs, nailed patch over a hole, loose/bent nails, splinters, earth heaped on the plinth, clods/pebbles/chips/shavings/needles/dry leaves around the base. Grain/rope fibres painted. Beam top ~2.9 m.')
save_manifest(os.path.join(OUT, 'manifest.json'), {'asset': 'AN14 timber-prop', 'variants': [rec]})
print('TRIS', rec['tris'], rec['tris_by_material'], 'SIZE', rec['size_xyz_m'])
if RENDER:
    render_set(objs, OUT, SHEET, 'AN14 Timber prop  %d tris  %s m' % (rec['tris'], rec['size_xyz_m']),
               extra=[(objs, 'joint', dict(az=-35, el=18, target=Vector((-0.1, 0, 2.6)) + d0, dist=2.6)),
                      (objs, 'base', dict(az=30, el=30, target=Vector((0.0, 0, 0.3)) + d0, dist=2.6))])
