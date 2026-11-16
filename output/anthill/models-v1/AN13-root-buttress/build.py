import sys, math, os
sys.path.insert(0, '/home/claude/ant/models')
from lib_organic import *

HERE = os.path.dirname(os.path.abspath(__file__))
RENDER = '--norender' not in sys.argv


def build():
    ak.reset()
    r = ak.Mesh('Root'); p = ak.Mesh('Props')
    # one leaning trunk from the split foot (left) arching over the passage to a thinner foot (right)
    path = [(-1.75, 0.0, -0.05), (-1.62, 0.02, 0.9), (-1.32, 0.06, 2.0), (-0.82, 0.12, 3.05), (-0.15, 0.16, 3.85),
            (0.65, 0.14, 4.25), (1.35, 0.06, 4.1), (1.88, -0.04, 3.55), (2.18, -0.12, 2.7), (2.28, -0.16, 1.75),
            (2.22, -0.16, 0.85), (2.3, -0.14, -0.05)]
    radii = [0.98, 0.86, 0.72, 0.62, 0.55, 0.5, 0.46, 0.42, 0.39, 0.37, 0.39, 0.5]
    tp, tr = densify(path, radii, 2, wob=0.05, seed=21)
    r.tube(tp, tr, sides=8, jitter=0.05, seed=21, caps=(False, False), rot=0.25,
           scale=[(1.0 + 0.12 * math.sin(i * 0.9), 1.05 - 0.1 * math.cos(i * 1.3)) for i in range(len(tp))],
           up_hint=(0, 1, 0))
    # split foot under the trunk: big front and back masses + side toes
    for i, (ang, L, h0, r0) in enumerate([(255, 1.45, 1.35, 0.7), (105, 1.35, 1.3, 0.66), (180, 1.35, 1.2, 0.6),
                                           (220, 1.25, 1.0, 0.5), (143, 1.15, 0.95, 0.48), (308, 1.0, 0.9, 0.42)]):
        toe(r, (-1.62, 0.0, 0), ang, L, h0, r0, seed=30 + i, sides=7)
    # small flare at the right foot
    for i, (ang, L, h0, r0) in enumerate([(280, 0.7, 0.7, 0.3), (60, 0.65, 0.65, 0.29), (5, 0.6, 0.6, 0.28), (150, 0.45, 0.5, 0.24)]):
        toe(r, (2.3, -0.14, 0), ang, L, h0, r0, seed=40 + i, sides=6)
    clamp(r, 2, lo=0.0)
    ro = r.to_object('AN13a_Root')
    objs = [ro]
    ak.ground(objs)
    return objs


def clearance(objs):
    bvh = bvh_of(objs)
    lo, hi = ak.bounds(objs)
    res = []
    for x in [i * 0.1 for i in range(-30, 31)]:
        h = bvh.ray_cast(Vector((x, 0.0, 0.01)), Vector((0, 0, 1)))
        res.append((round(x, 2), round(h[0].z, 2) if h[0] else None))
    ok = [x for x, z in res if z is not None and z >= 2.4]
    return res, (min(ok), max(ok)) if ok else None


if __name__ == '__main__':
    objs = build()
    res, span = clearance(objs)
    print('clear>=2.4 span x', span, [z for x, z in res])
    rec = outputs(objs, HERE, 'SM_AN13_RootButtress.fbx', 'AN13a', 'RootButtress', SHEETS + '13-root-buttress.png',
                  manifest_extra=dict(solidity='passable', collider='mesh collider or 4-6 convex pieces along the arch; the space under the arch stays open',
                                      emissive=False, pivot='bottom centre (bounding box)',
                                      clear_under_arch_m={'x_span_with_height_ge_2.4': span},
                                      notes='Asymmetric arch: split foot (6 toes) on -X, one leaning trunk arching to a thinner foot on +X; pair two copies (mirror in Unity)'),
                  views=RENDER)
    write_manifest(HERE, [rec])
    print(rec)
