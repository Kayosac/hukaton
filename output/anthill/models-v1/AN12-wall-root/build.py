import sys, math, os
sys.path.insert(0, '/home/claude/ant/models')
from lib_organic import *

HERE = os.path.dirname(os.path.abspath(__file__))
RENDER = '--norender' not in sys.argv


def build():
    ak.reset()
    r = ak.Mesh('Root'); p = ak.Mesh('Props')
    # S curve mostly in the Y-Z plane: foot off the wall, middle hugging the wall, top curling out into the room.
    path = [(0.0, -0.5, -0.05), (0.02, -0.5, 0.55), (-0.04, -0.36, 1.2), (-0.08, -0.24, 1.75), (-0.04, -0.34, 2.35),
            (0.04, -0.72, 2.9), (0.08, -1.0, 3.35), (0.04, -0.98, 3.72), (-0.02, -0.82, 4.02)]
    radii = [0.5, 0.44, 0.4, 0.38, 0.36, 0.33, 0.3, 0.28, 0.27]
    tp, tr = densify(path, radii, 2, wob=0.035, seed=5)
    rings = r.tube(tp, tr, sides=7, jitter=0.04, seed=5, caps=(False, False), rot=0.2,
                   scale=[(1.18, 0.95) if i < 9 else (1.05, 1.0) for i in range(len(tp))], up_hint=(1, 0, 0))
    # slanted cut tip with end grain
    t = (Vector(tp[-1]) - Vector(tp[-2])).normalized()
    last = slant(rings[-1], tp[-1], t, (t + Vector((0.1, -0.35, 0.15))).normalized())
    for v in r.bm.verts:
        for i, q in enumerate(rings[-1]):
            if (v.co - q).length < 1e-6:
                v.co = last[i]
    p.cap(last, region='root_end')
    # short side fork near the base: forward-right, cut end
    root(r, p, [(0.04, -0.55, 0.7), (0.1, -0.86, 0.98), (0.14, -1.06, 1.32), (0.16, -1.12, 1.52)],
         [0.24, 0.21, 0.19, 0.18], sides=6, seed=6, cut=(0.1, -0.2, 0.35), jitter=0.03)
    # small foot flare on both sides of the base
    toe(r, (0.0, -0.5, 0), 200, 0.7, 0.55, 0.3, seed=7, sides=6)
    toe(r, (0.0, -0.5, 0), 330, 0.75, 0.6, 0.3, seed=8, sides=6)
    toe(r, (0.0, -0.5, 0), 265, 0.55, 0.5, 0.28, seed=9, sides=6)
    clamp(r, 2, lo=0.0)
    clamp(r, 1, hi=0.0)           # flat mounting back against the wall (y = 0)
    clamp(p, 1, hi=0.0)
    drop_degenerate(r)
    ro = r.to_object('AN12a_Root'); po = p.to_object('AN12a_Props')
    root_details(ro, po, seed=12, n_rootlets=40, n_side=8, n_knots=5, n_chips=12, n_clods=10, n_pebbles=14,
                 n_leaves=3, base_r=(0.45, 1.0), base_c=(0.0, -0.5), zmax=3.9, clamp_y=0.0, ground_chips=4,
                 keep=lambda q: q.y < -0.04)
    objs = [ro, po]
    ak.ground(objs)
    lo, hi = ak.bounds(objs)
    for o in objs:
        o.data.transform(Matrix.Translation((0, -hi[1], 0)))
    return objs


if __name__ == '__main__':
    objs = build()
    rec = outputs(objs, HERE, 'SM_AN12_WallRoot.fbx', 'AN12a', 'WallRoot', SHEETS + '12-wall-root.png',
                  manifest_extra=dict(solidity='solid', collider='convex hull of lower trunk + capsule for the upper curl',
                                      emissive=False,
                                      attach='pivot = floor level, centre of the flat back; back plane is y = 0 (wall), root extends to -Y',
                                      notes='S curve in Y-Z (middle hugs the wall, top curls out), flat back, short forward fork near the base, slanted end-grain tip'),
                  views=RENDER)
    write_manifest(HERE, [rec])
    print(rec)
