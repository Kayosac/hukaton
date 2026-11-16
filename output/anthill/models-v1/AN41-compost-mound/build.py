import sys, math, os
sys.path.insert(0, '/home/claude/ant/models')
from lib_organic import *

HERE = os.path.dirname(os.path.abspath(__file__))
RENDER = '--norender' not in sys.argv


def build():
    ak.reset()
    rnd = random.Random(41)
    tmp = ak.Mesh('Earth')

    def edge(a):
        r = 1.0 + 0.08 * math.sin(3 * a + 0.5) + 0.05 * math.sin(5 * a + 1.3)
        return Vector((math.cos(a) * 0.76 * r, math.sin(a) * 0.58 * r, 0))

    def prof(a):
        steep = max(0.0, math.cos(a)) * 0.12           # right (+X) side steeper
        return ([1.0, 0.85, 0.68, 0.5, 0.32, 0.15], [0.0, 0.24 + steep, 0.5 + steep, 0.72, 0.88, 0.97])
    heap(tmp, (-0.14, 0.08), edge, 0.72, None, None, n=15, jitter=(0.05, 0.06), seed=4, profile_fn=prof)
    recalc(tmp)
    drop_bottom(tmp)
    p = ak.Mesh('Props')
    # mycelium patch on the front face (-Y): one shared projection so the vein figure stays continuous
    PC = Vector((0.12, 0.0, 0.34)); PS = 0.62
    sr = random.Random(7)
    for f in tmp.bm.faces:
        pts = [v.co.copy() for v in f.verts]
        c = f.calc_center_median(); n = f.normal
        if n.y < -0.35 and abs(c.x - PC.x) < PS * 0.5 and abs(c.z - PC.z) < PS * 0.5:
            uvs = [suv(SUB_VEINS, 0.5 + (q.x - PC.x) / PS, 0.5 - (q.z - PC.z) / PS) for q in pts]
            p.face(pts, uvs)
        else:
            fit_face(p, pts, SUB_CLEAN, metres=0.7, rnd=sr)
    tmp.bm.free()
    po = p.to_object('AN41a_Props')
    bvh = bvh_of([po])
    # broad shredded dry leaves: one draped over the top, the rest on the slopes
    lv = ak.Mesh('Props')
    leaves = [((-0.2, 0.12), 0.46, 'leaf_dry_0', 0.3), ((-0.45, -0.22), 0.4, 'leaf_dry_2', 2.1),
              ((0.52, -0.02), 0.38, 'leaf_dry_0', 4.0), ((-0.5, 0.2), 0.36, 'leaf_dry_1', 1.0),
              ((0.42, 0.24), 0.4, 'leaf_dry_0', 5.2), ((0.0, 0.4), 0.36, 'leaf_dry_2', 2.8),
              ((-0.12, -0.42), 0.3, 'leaf_dry_0', 0.6)]
    for i, (xy, size, reg, rot) in enumerate(leaves):
        place_leaf(lv, bvh, (xy[0], xy[1], 3.0), (0, 0, -1), size, reg, seed=60 + i, rot=rot, lift=0.015, curl=0.025)
    # detail pass: more leaves, twigs + needles poking out, clods/crumbs rolling down, pebbles and crumbs at the foot,
    # sprouting glow mushrooms, one burrow
    dr = random.Random(410)
    top = lambda q, n: n.z > 0.35 and q.z > 0.12 and not (n.y < -0.3 and abs(q.x - 0.12) < 0.32 and q.z < 0.62)
    S = samples([po], 90, 411, top)
    for i, (q, n) in enumerate(S[:9]):
        place_leaf(lv, bvh, (q.x, q.y, 3.0), (0, 0, -1), dr.uniform(0.18, 0.32), dr.choice(('leaf_dry_0', 'leaf_dry_1', 'leaf_dry_2')),
                   seed=700 + i, lift=0.012, curl=0.03)
    for i, (q, n) in enumerate(S[9:17]):
        d = (n + Vector((dr.uniform(-.8, .8), dr.uniform(-.8, .8), 0.1))).normalized()
        twig(lv, q, d, dr.uniform(0.2, 0.42), dr.uniform(0.012, 0.022), seed=720 + i, embed=0.08, bend=0.15)
    for i, (q, n) in enumerate(S[17:20]):
        needles(lv, q, (n + Vector((0, 0, 0.3))).normalized(), 0.28, 6, 0.6, seed=740 + i, r=0.008, embed=0.04)
    for i, (q, n) in enumerate(S[20:36]):
        lump(lv, q, dr.uniform(0.03, 0.07), n, sides=6, squash=0.6, sink=0.35, seed=750 + i,
             spec=SUB_CLEAN if i % 3 else full('earth_props'), metres=0.3)
    for i, (q, n) in enumerate(S[36:42]):
        baby_mushroom(lv, q, n, dr.uniform(0.05, 0.14), dr.uniform(0.03, 0.06), seed=770 + i, lean=0.3)
    for i, (q, n) in enumerate(S[42:43]):
        burrow(lv, q, n, 0.045, seed=790 + i, depth=0.1)
    foot_details(lv, (-0.05, 0.0), 0.7, 1.05, seed=41, n_clump=10, clump_r=(0.05, 0.11), n_babies=2, baby_h=(0.05, 0.1),
                 baby_R=(0.03, 0.05), n_pebbles=10, n_leaves=3, n_twigs=2, n_needles=1, n_crumbs=16)
    clamp(lv, 2, lo=0.0); drop_bottom(lv)
    join_into(po, lv)
    objs = [po]
    ak.ground(objs)
    return objs


if __name__ == '__main__':
    objs = build()
    rec = outputs(objs, HERE, 'SM_AN41_CompostMound.fbx', 'AN41a', 'CompostMound', SHEETS + '41-compost-mound.png',
                  manifest_extra=dict(solidity='solid (small)', collider='convex hull',
                                      emissive=True, emissive_parts='mycelium veins of Props substrate on the front (-Y) face only',
                                      pivot='bottom centre',
                                      notes='v2 detail pass: + leaves, twigs, needles, clods/crumbs, foot pebbles, 8 sprouting glow mushrooms (gill_glow_yellow), a burrow; faceted compost heap (Props substrate, vein-free part everywhere except one front patch with the vein figure) + 7 lobed dry leaves conformed to the surface'),
                  views=RENDER)
    write_manifest(HERE, [rec])
    print(rec)
