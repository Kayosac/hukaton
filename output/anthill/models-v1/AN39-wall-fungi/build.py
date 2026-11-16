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
    recalc(e); recalc(p)
    eo = e.to_object('AN39a_Earth'); po = p.to_object('AN39a_Props')
    ak.auto_uv(eo)
    objs = [eo, po]
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
