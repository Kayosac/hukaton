import sys, math, os
sys.path.insert(0, '/home/claude/ant/models')
from lib_organic import *

HERE = os.path.dirname(os.path.abspath(__file__))
RENDER = '--norender' not in sys.argv


def build():
    ak.reset()
    r = ak.Mesh('Root'); p = ak.Mesh('Props')
    # trunk: three long bends, ~7 sides
    trunk = [(0, 0, -0.05), (0.04, 0.02, 0.5), (0.22, 0.04, 1.0), (0.45, 0.06, 1.5), (0.48, 0.1, 1.95),
             (0.28, 0.1, 2.4), (-0.05, 0.06, 2.8), (-0.3, 0.02, 3.15), (-0.28, -0.04, 3.55)]
    tp, tr = densify(trunk, [0.95, 0.74, 0.62, 0.58, 0.55, 0.52, 0.52, 0.56, 0.62], 2, wob=0.05, seed=1)
    r.tube(tp, tr, sides=8, jitter=0.06, seed=1, caps=(False, True),
           scale=[(1.1 + 0.08 * math.sin(i * 1.3), 0.95 - 0.06 * math.cos(i * 1.7)) for i in range(len(tp))], rot=0.3)
    # Y fork: left branch tall, right branch shorter and wider out
    root(r, p, [(-0.3, -0.02, 3.2), (-0.55, -0.04, 3.75), (-0.75, -0.08, 4.25), (-0.88, -0.1, 4.62), (-0.98, -0.12, 4.98)],
         [0.52, 0.47, 0.44, 0.42, 0.41], sides=7, seed=2, cut=(0.4, 0.1, 0.35), jitter=0.05)
    root(r, p, [(-0.1, 0.0, 3.3), (0.3, 0.06, 3.7), (0.7, 0.1, 4.05), (1.0, 0.14, 4.35), (1.18, 0.16, 4.6)],
         [0.46, 0.4, 0.36, 0.34, 0.33], sides=7, seed=3, cut=(-0.35, 0.0, 0.4), jitter=0.05)
    # short stub on the left flank (identity marker)
    root(r, p, [(0.0, -0.05, 2.05), (-0.5, -0.2, 2.25), (-0.72, -0.28, 2.38), (-0.86, -0.32, 2.48)],
         [0.27, 0.25, 0.23, 0.22], sides=6, seed=4, cut=(-0.2, -0.1, 0.25))
    # spreading foot: five flared toes
    for i, (ang, L, h0, r0) in enumerate([(212, 1.5, 1.05, 0.5), (272, 1.25, 0.95, 0.46), (338, 1.45, 1.0, 0.47),
                                           (52, 1.2, 0.9, 0.42), (138, 1.25, 0.95, 0.44)]):
        toe(r, (0.04, 0.02, 0), ang, L, h0, r0, seed=10 + i)
    clamp(r, 2, lo=0.0)
    ro = r.to_object('AN11a_Root'); po = p.to_object('AN11a_Props')
    root_details(ro, po, seed=11, n_rootlets=60, n_side=12, n_knots=7, n_chips=16, n_clods=16, n_pebbles=22,
                 n_leaves=4, base_r=(0.9, 1.9), zmax=4.7)
    objs = [ro, po]
    ak.ground(objs)
    return objs


if __name__ == '__main__':
    objs = build()
    rec = outputs(objs, HERE, 'SM_AN11_RootPillar.fbx', 'AN11a', 'RootPillar', SHEETS + '11-root-pillar.png',
                  manifest_extra=dict(solidity='solid', collider='convex: trunk+fork as 2-3 capsules/convex hulls; toes optional low boxes',
                                      emissive=False, pivot='bottom centre',
                                      notes='Y fork with slanted end-grain caps (Props root_end), stub on left flank, 5 flared toes'),
                  views=RENDER)
    write_manifest(HERE, [rec])
    print(rec)
