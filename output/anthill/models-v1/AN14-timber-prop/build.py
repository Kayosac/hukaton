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
     up=(0, 1, 0), jitter=0.012, seed=11, caps=(None, 'flat'))
# post: kinked, leaning right going up, tapering
post = [(0.0, 0.0, 0.15), (0.012, 0.0, 0.5), (0.03, 0.01, 0.95), (0.035, -0.01, 1.22), (0.06, -0.015, 1.45), (0.12, 0.0, 1.72), (0.17, 0.0, 1.95), (0.30, 0.015, 2.5), (0.36, 0.0, 2.93)]
beam(w, post, [0.31, 0.315, 0.30, 0.28, 0.285, 0.265, 0.27, 0.25, 0.24], [0.29, 0.285, 0.28, 0.27, 0.275, 0.255, 0.26, 0.24, 0.23], up=(0, 1, 0),
     jitter=0.018, seed=12, caps=(None, 'bevel'), endm=p, bevel=0.35)
# crossbeam: rests across the post below its top, runs out to -X, slightly crooked, left end cut
cb = [(0.58, -0.07, 2.66), (0.2, -0.065, 2.68), (-0.35, -0.06, 2.71), (-1.02, -0.05, 2.77)]
beam(w, cb, [0.25, 0.25, 0.24, 0.23], [0.25, 0.24, 0.24, 0.25], up=(0, 1, 0), jitter=0.016, seed=13,
     caps=('flat', 'flat'), endm=p)
# diagonal brace from the post knee to the beam underside
beam(w, [(0.08, -0.06, 1.55), (-0.25, -0.065, 2.08), (-0.62, -0.06, 2.62)], [0.17, 0.165, 0.16], [0.15, 0.15, 0.15],
     up=(0, 1, 0), jitter=0.012, seed=14, caps=('flat', 'flat'))

# wedges driven between post foot and plinth
for i, (x, y, r) in enumerate(((0.17, -0.02, 0.0), (-0.02, -0.165, 1.5708))):
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

wo = w.to_object('AN14a_Wood')
po = p.to_object('AN14a_Props')
objs = [wo, po]
ak.ground(objs)
fbx = os.path.join(HERE, 'SM_AN14_TimberProp.fbx')
ak.export_fbx(objs, fbx)
ak.save_blend(os.path.join(HERE, 'AN14_TimberProp.blend'))
rec = ak.variant_record('AN14a', 'TimberProp', objs, fbx,
                        solidity='solid', collider='3 box colliders: post (tilted, follow the kink roughly), crossbeam, brace; or one convex per part',
                        emissive=False,
                        notes='Post kink/lean, plinth, crossbeam, brace and 4+1 rope loops are real geometry; grain/rope fibres painted. Beam top ~2.9 m.')
save_manifest(os.path.join(OUT, 'manifest.json'), {'asset': 'AN14 timber-prop', 'variants': [rec]})
print('TRIS', rec['tris'], rec['tris_by_material'], 'SIZE', rec['size_xyz_m'])
if RENDER:
    render_set(objs, OUT, SHEET, 'AN14 Timber prop  %d tris  %s m' % (rec['tris'], rec['size_xyz_m']))
