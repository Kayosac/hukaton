import sys, math, os
sys.path.insert(0, '/home/claude/ant/models')
from lib_organic import *

HERE = os.path.dirname(os.path.abspath(__file__))
RENDER = '--norender' not in sys.argv


def slab(e, outline, t_back=0.0, t_front=-0.2, seed=0):
    rnd = random.Random(seed)
    back = [Vector((x, t_back, z)) for x, z in outline]
    front = [Vector((x * 0.94 + rnd.uniform(-.02, .02), t_front + rnd.uniform(-0.03, 0.03), z * 0.96 + 0.02 + rnd.uniform(-.02, .02)))
             for x, z in outline]
    n = len(outline)
    for k in range(n):
        k2 = (k + 1) % n
        e.quad(back[k], back[k2], front[k2], front[k], (0, 0), (1, 0), (1, 1), (0, 1))
    e.face(front, [(0, 0)] * n)            # outline is clockwise seen from the front -> normal -Y
    e.face(list(reversed(back)), [(0, 0)] * n)
    return front


def build():
    ak.reset()
    e = ak.Mesh('Earth'); p = ak.Mesh('Props')
    # clay chunk (mounting plate): irregular octagon, flat back at y = 0
    outline = [(-0.36, 0.0), (-0.44, 0.22), (-0.42, 0.55), (-0.3, 0.8), (-0.08, 0.9), (0.18, 0.86), (0.36, 0.7),
               (0.42, 0.42), (0.33, 0.12), (0.12, 0.0)]
    # make it clockwise seen from -Y (x right, z up): reverse if needed
    area = sum(outline[i][0] * outline[(i + 1) % len(outline)][1] - outline[(i + 1) % len(outline)][0] * outline[i][1]
               for i in range(len(outline)))
    if area > 0:
        outline = list(reversed(outline))
    slab(e, outline, 0.0, -0.2, seed=3)
    # three overlapping shelf fungi, staggered: big top-left, middle right, small low left
    bracket(p, (-0.06, -0.14, 0.66), W=0.86, D=0.5, H=0.15, T=0.15, n=9, seed=1, top='cap_yellow', under='gill_glow_yellow',
            droop=0.025, lump=0.08, rim=0.05)
    bracket(p, (0.16, -0.14, 0.44), W=0.66, D=0.42, H=0.13, T=0.13, n=8, seed=2, top='cap_yellow', under='gill_glow_yellow',
            droop=0.02, lump=0.08, rim=0.045)
    bracket(p, (-0.12, -0.14, 0.24), W=0.5, D=0.34, H=0.1, T=0.1, n=8, seed=3, top='cap_yellow', under='gill_glow_yellow',
            droop=0.02, lump=0.08, rim=0.04)
    # extra small shelves in the gaps
    for i, (ax, az, w, d, h, t) in enumerate([(-0.34, 0.42, 0.22, 0.16, 0.045, 0.05), (0.33, 0.17, 0.26, 0.18, 0.05, 0.06),
                                              (0.03, 0.07, 0.2, 0.14, 0.04, 0.045), (-0.3, 0.12, 0.16, 0.12, 0.035, 0.04),
                                              (0.3, 0.74, 0.16, 0.12, 0.035, 0.04)]):
        bracket(p, (ax, -0.14, az), W=w, D=d, H=h, T=t, n=6, seed=20 + i, top='cap_yellow', under='gill_glow_yellow',
                droop=0.01, lump=0.1, rim=0.02)
    recalc(e); recalc(p)
    plate = e.to_object('plate')
    # cracks cut into the clay plate (exact boolean): two long front cracks + an edge chip
    cb = bmesh.new()
    wedge(cb, (-0.3, -0.2, 0.05), (-0.12, -0.21, 0.3), (0, -1, 0), 0.035, 0.07)
    wedge(cb, (0.22, -0.2, 0.85), (0.38, -0.2, 0.6), (0, -1, 0), 0.03, 0.06)
    wedge(cb, (0.33, -0.22, 0.32), (0.5, -0.12, 0.38), (0.6, -0.8, 0), 0.08, 0.1)
    polys = boolean_cut(plate, cb); cb.free()
    me = plate.data; bpy.data.objects.remove(plate); bpy.data.meshes.remove(me)
    bpy.context.view_layer.update()
    e = ak.Mesh('Earth')
    for q in polys:
        e.face(q, [(0, 0)] * len(q))
    # clay crumbs stuck on the plate, button mushrooms, hanging root hairs
    rr = random.Random(39)
    for i in range(9):
        q = Vector((rr.uniform(-0.38, 0.38), -0.19, rr.uniform(0.05, 0.85)))
        lump(p, q, rr.uniform(0.025, 0.05), (rr.uniform(-.3, .3), -1, rr.uniform(-.2, .4)), sides=5, squash=0.6, sink=0.3,
             seed=390 + i, spec=full('earth_props'), metres=0.3)
    for i, (x, z) in enumerate([(0.38, 0.05), (-0.4, 0.3), (0.12, 0.84), (-0.2, 0.86)]):
        baby_mushroom(p, (x, -0.18, z), (0, -1, 0.35), rr.uniform(0.05, 0.09), rr.uniform(0.025, 0.045), seed=395 + i,
                      top='cap_yellow', under='gill_glow_yellow', lean=0.2)
    rl = ak.Mesh('Root')
    for i in range(10):
        x = rr.uniform(-0.35, 0.3)
        rootlet(rl, Vector((x, -0.08, 0.03)), Vector((rr.uniform(-.3, .3), -0.4, -1)), rr.uniform(0.1, 0.25),
                rr.uniform(0.008, 0.014), seed=399 + i, droop=0.9)
    for i in range(5):
        a = rr.uniform(0.5, 2.6)
        q = Vector((math.cos(a) * 0.4, -0.1, 0.45 + math.sin(a) * 0.42))
        rootlet(rl, q, Vector((math.cos(a), -0.3, math.sin(a))), rr.uniform(0.08, 0.18), rr.uniform(0.007, 0.012),
                seed=410 + i, droop=0.7)
    clamp(rl, 1, hi=0.0)
    eo = e.to_object('AN39a_Earth'); po = p.to_object('AN39a_Props'); rlo = rl.to_object('AN39a_Root')
    ak.auto_uv(eo)
    objs = [eo, po, rlo]
    ak.ground(objs)
    lo, hi = ak.bounds(objs)
    for o in objs:
        o.data.transform(Matrix.Translation((0, -hi[1], 0)))
    return objs


if __name__ == '__main__':
    objs = build()
    rec = outputs(objs, HERE, 'SM_AN39_WallFungi.fbx', 'AN39a', 'WallFungi', SHEETS + '39-wall-fungi.png',
                  manifest_extra=dict(solidity='surface (decor on walls/roots)', collider='none (or thin box on the plate)',
                                      emissive=True, emissive_parts='gill_glow under the three shelf caps and their rim lips',
                                      attach='pivot = bottom of the clay plate, on its flat back plane (y = 0 = wall surface); fungi point to -Y',
                                      notes='clay mounting plate (Earth) + three bracket fungi (Props cap_yellow top, gill_glow underside)'),
                  views=RENDER)
    write_manifest(HERE, [rec])
    print(rec)
