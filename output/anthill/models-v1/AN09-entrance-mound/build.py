import sys, math, os
sys.path.insert(0, '/home/claude/ant/models')
from lib_organic import *
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
RENDER = '--norender' not in sys.argv

PEAK = (-0.35, 0.9)
H = 3.75
SKIRT = -0.8
# tunnel cross-section (x, z), floor edge = last -> first
ARCH = [(-1.3, 0.0), (-1.4, 0.9), (-1.32, 1.75), (-0.98, 2.45), (-0.3, 2.86), (0.38, 2.92), (1.0, 2.55),
        (1.36, 1.8), (1.38, 0.8), (1.3, 0.0)]
# stations: (y, floor z, scale, x shift)
STATIONS = [(-5.0, 0.0, 1.3, 0.0), (-3.3, 0.0, 1.2, 0.0), (-2.3, 0.0, 1.0, 0.0), (-0.8, -0.12, 0.98, 0.05),
            (0.4, -0.42, 0.95, 0.12), (1.4, -0.7, 0.93, 0.2)]


def edge(a):
    rx = 5.2 if math.cos(a) > 0 else 4.6          # long low shoulder to the right (+X)
    r = 1.0 + 0.05 * math.sin(3 * a + 0.7) + 0.04 * math.sin(7 * a)
    return Vector((math.cos(a) * rx * r, math.sin(a) * 3.2 * r, 0))


def prof(a):
    w = max(0.0, -math.sin(a)) ** 2                  # front (-Y) = steep hood over the mouth
    back_s, back_g = [1.0, 0.9, 0.78, 0.64, 0.5, 0.36, 0.22, 0.1], [0.0, 0.11, 0.25, 0.42, 0.6, 0.78, 0.91, 0.98]
    fr_s, fr_g = [1.0, 0.97, 0.92, 0.84, 0.7, 0.52, 0.34, 0.16], [0.0, 0.36, 0.7, 0.87, 0.92, 0.95, 0.975, 0.99]
    s = [b + (f - b) * w for b, f in zip(back_s, fr_s)]
    g = [b + (f - b) * w for b, f in zip(back_g, fr_g)]
    rr = max(0.0, math.cos(a)) * (1 - w)             # right shoulder: lower
    g = [x * (1 - 0.18 * rr) for x in g]
    return s, g


def cutter():
    rnd = random.Random(9)
    bm = bmesh.new()
    rings = []
    for (y, fz, sc, xs) in STATIONS:
        ring = []
        for (x, z) in ARCH:
            j = 0.05 if 0.0 < z else 0.0
            ring.append(bm.verts.new((x * sc + xs + rnd.uniform(-j, j), y + rnd.uniform(-0.05, 0.05),
                                      fz + z * sc + rnd.uniform(-j, j))))
        rings.append(ring)
    n = len(ARCH)
    for i in range(len(rings) - 1):
        for k in range(n):
            k2 = (k + 1) % n
            f = bm.faces.new((rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k]))
            if k == n - 1:
                f.material_index = 0                  # floor
            elif i == len(rings) - 2:
                f.material_index = 2                  # deepest segment: dark throat
            else:
                f.material_index = 1                  # earth walls / ceiling
    f0 = bm.faces.new(list(reversed(rings[0]))); f0.material_index = 1
    f1 = bm.faces.new(rings[-1]); f1.material_index = 2
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new('cutter'); bm.to_mesh(me); bm.free()
    for nm in ('Floor', 'Earth', 'Props'):
        me.materials.append(ak.material(nm))
    o = bpy.data.objects.new('cutter', me); bpy.context.scene.collection.objects.link(o)
    return o


def build():
    ak.reset()
    m = ak.Mesh('Earth')
    heap(m, PEAK, edge, H, None, None, n=30, jitter=(0.03, 0.04), seed=11, skirt=SKIRT, profile_fn=prof)
    recalc(m)
    mound = m.to_object('mound')
    cut = cutter()
    mod = mound.modifiers.new('cut', 'BOOLEAN')
    mod.operation = 'DIFFERENCE'; mod.solver = 'EXACT'; mod.object = cut
    mod.material_mode = 'TRANSFER'
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mound.evaluated_get(dg)
    me = ev.to_mesh()
    mats = [mm.name if mm else '' for mm in me.materials]
    print('boolean mats', mats, len(me.polygons))
    E = ak.Mesh('Earth'); F = ak.Mesh('Floor'); D = ak.Mesh('Props')
    du = ruv('dark', 0.5, 0.5)
    for poly in me.polygons:
        pts = [me.vertices[i].co.copy() for i in poly.vertices]
        name = mats[poly.material_index] if poly.material_index < len(mats) else 'M_Ant_Earth'
        if all(p.z < 0.001 for p in pts) and poly.normal.z < -0.9:
            continue                                   # buried bottom, nobody sees it
        if name.endswith('Floor'):
            F.face(pts, [(0, 0)] * len(pts))
        elif name.endswith('Props'):
            D.face(pts, [du] * len(pts))
        else:
            E.face(pts, [(0, 0)] * len(pts))
    ev.to_mesh_clear()
    bpy.data.objects.remove(mound); bpy.data.objects.remove(cut)
    bpy.context.view_layer.update()
    # thick crooked lip around the mouth (the hood rim of the concept)
    bmesh.ops.remove_doubles(E.bm, verts=E.bm.verts, dist=1e-5)
    E.bm.faces.ensure_lookup_table()
    bv = BVHTree.FromBMesh(E.bm)
    y0, fz0, sc0, xs0 = STATIONS[2]
    lip = []
    for (x, z) in [(-1.3, -0.25)] + ARCH[:] + [(1.3, -0.25)]:
        if z == 0.0:
            continue
        cx, cz = x * 1.17, z * 1.11
        h = bv.ray_cast(Vector((cx, -8.0, cz)), Vector((0, 1, 0)))
        if h[0] is not None:
            lip.append(h[0] + Vector((0, -0.12, 0)))
    lip = [Vector((-1.78, lip[0].y - 0.1, -0.3))] + lip + [Vector((1.78, lip[-1].y - 0.1, -0.3))]
    lp, lr = densify(lip, [0.42] + [0.36] * (len(lip) - 2) + [0.44], 2, wob=0.06, seed=4)
    E.tube(lp, lr, sides=6, jitter=0.06, seed=4, caps=(True, True), rot=0.5)
    # a few extra clay lumps at the foot (left and back right), away from the mouth
    ak.blob(E, (-3.3, -0.9, 0.0), (1.1, 0.9, 0.75), rings=3, sides=8, jitter=0.15, seed=3)
    ak.blob(E, (2.6, 1.6, 0.0), (1.3, 1.0, 0.8), rings=3, sides=8, jitter=0.15, seed=5)
    ak.blob(E, (-2.2, 2.3, 0.0), (1.2, 0.8, 0.9), rings=3, sides=8, jitter=0.15, seed=6)
    clamp(E, 2, lo=SKIRT)
    eo = E.to_object('AN09a_Earth'); fo = F.to_object('AN09a_Floor'); do = D.to_object('AN09a_Props')
    ak.auto_uv(eo); ak.auto_uv(fo)
    # dry leaves stuck on the heap
    bvh = bvh_of([eo])
    L = ak.Mesh('Props')
    leaves = [((-1.7, -0.1), 1.0, 'leaf_dry_0', 0.5), ((-0.6, 1.4), 0.84, 'leaf_dry_2', 2.0), ((-3.0, -1.4), 0.77, 'leaf_dry_0', 1.2),
              ((-2.6, -1.9), 0.70, 'leaf_dry_1', 4.0), ((-3.4, -0.6), 0.84, 'leaf_dry_2', 5.5), ((3.4, -1.0), 0.77, 'leaf_dry_0', 0.3),
              ((3.9, -0.4), 0.70, 'leaf_dry_2', 2.5), ((2.9, -1.4), 0.63, 'leaf_dry_1', 3.3), ((1.8, 2.0), 0.84, 'leaf_dry_0', 1.9),
              ((-2.2, 2.0), 0.77, 'leaf_dry_2', 0.9), ((0.6, -0.2), 0.84, 'leaf_dry_0', 2.2)]
    for i, (xy, size, reg, rot) in enumerate(leaves):
        place_leaf(L, bvh, (xy[0], xy[1], 8.0), (0, 0, -1), size, reg, seed=90 + i, rot=rot, lift=0.03, curl=0.05)
    # ======== v2 detail pass ========
    dr = random.Random(909)
    # keep the mouth and throat clear (pre-centring coordinates)
    keep = lambda q: not (abs(q.x) < 1.8 and q.y < 1.7 and -1.0 < q.z < 3.1)
    surf = lambda q, n: keep(q) and q.z > 0.05
    # roots crossing the lip of the opening (+ two down the flanks), lying on the surface
    R = ak.Mesh('Root')
    def on_surface(x, z, off):
        h = bv.ray_cast(Vector((x, -9.0, z)), Vector((0, 1, 0)))
        return None if h[0] is None else h[0] + Vector((0, -off, 0))
    for k, (xz, rad) in enumerate([([(-2.9, 2.2), (-2.3, 3.0), (-1.2, 3.42), (0.0, 3.5), (1.1, 3.38), (1.9, 2.9), (2.3, 1.8),
                                     (2.45, 0.7), (2.6, -0.2)], 0.13),
                                    ([(2.9, 2.5), (2.0, 3.25), (0.8, 3.62), (-0.4, 3.7), (-1.5, 3.45), (-2.1, 2.6)], 0.09),
                                    ([(-2.15, 2.5), (-2.0, 1.6), (-2.1, 0.8), (-2.0, -0.2)], 0.11),
                                    ([(2.05, 2.2), (2.25, 1.1), (2.15, -0.2)], 0.08)]):
        pts = [on_surface(x, z, rad * 0.55) for x, z in xz]
        pts = [q for q in pts if q is not None]
        if len(pts) < 3:
            continue
        rr = [rad * (1.0 - 0.35 * i / (len(pts) - 1)) for i in range(len(pts))]
        pp, rp = densify(pts, rr, 2, wob=0.05, seed=950 + k)
        R.tube(pp, rp, sides=6, jitter=0.02, seed=950 + k, caps=(True, True), rot=dr.uniform(0, 3))
        for i in range(5):   # hair rootlets hanging off the lip roots
            q = Vector(pp[dr.randrange(1, len(pp) - 1)])
            rootlet(R, q, Vector((0, -0.6, -1)), dr.uniform(0.2, 0.45), dr.uniform(0.015, 0.03), seed=960 + k * 10 + i,
                    droop=0.9, keep=keep)
    bv2 = bvh_of([eo])
    # sticks / twigs and pine-needle bundles poking out of the heap
    T = ak.Mesh('Props')
    S = samples([eo], 400, 970, lambda q, n: surf(q, n) and n.z > 0.15)
    for i, (q, n) in enumerate(S[:48]):
        d = (n * 0.7 + Vector((dr.uniform(-1, 1), dr.uniform(-1, 1), dr.uniform(0, 0.6)))).normalized()
        twig(T, q, d, dr.uniform(0.35, 1.2), dr.uniform(0.025, 0.055), seed=1000 + i, embed=0.25, bend=0.2,
             region='wood_props' if i % 3 else 'root_props', sides=dr.choice((4, 5)))
    for i, (q, n) in enumerate(S[48:68]):
        d = (n + Vector((dr.uniform(-.6, .6), dr.uniform(-.6, .6), 0.2))).normalized()
        needles(T, q, d, dr.uniform(0.45, 0.8), dr.randint(5, 8), 0.55, seed=1100 + i, r=0.016, embed=0.12)
    # pebbles and clods stuck in the slopes
    for i, (q, n) in enumerate(S[68:118]):
        sp = full('pebble') if i % 2 else full('earth_props')
        lump(T, q, dr.uniform(0.07, 0.18), n, sides=dr.choice((5, 6, 7)), squash=dr.uniform(.5, .8), sink=0.4,
             seed=1200 + i, spec=sp, metres=0.5)
    # many more dry leaves
    for i, (q, n) in enumerate(S[118:150]):
        place_leaf(T, bv2, (q.x, q.y, q.z + 3.0), (0, 0, -1), dr.uniform(0.45, 0.85),
                   dr.choice(('leaf_dry_0', 'leaf_dry_1', 'leaf_dry_2')), seed=1300 + i, lift=0.03, curl=0.05)
    # small ant holes
    for i, (q, n) in enumerate([s_ for s_ in S[150:260] if s_[1].z > 0.35][:10]):
        burrow(T, q, n, dr.uniform(0.13, 0.22), seed=1400 + i, depth=0.35)
    # clods and pebbles rolled down to the foot
    gb = ground_bvh()
    for i, (q, n) in enumerate(samples([eo], 60, 980, lambda q, n: keep(q) and 0.0 < q.z < 0.4 and n.z < 0.85)[:44]):
        h = Vector((n.x, n.y, 0)).normalized() if Vector((n.x, n.y, 0)).length > 1e-3 else Vector((1, 0, 0))
        g = Vector((q.x, q.y, 0)) + h * dr.uniform(0.05, 0.9)
        if not keep(g + Vector((0, 0, 0.2))):
            continue
        if i % 4 == 3:
            place_leaf(T, gb, (g.x, g.y, 1.0), (0, 0, -1), dr.uniform(0.4, 0.7), dr.choice(('leaf_dry_0', 'leaf_dry_2')),
                       seed=1500 + i, lift=0.01, curl=0.05)
        else:
            lump(T, g, dr.uniform(0.08, 0.28), (0, 0, 1), sides=dr.choice((6, 7)), squash=dr.uniform(.5, .85), sink=0.25,
                 seed=1600 + i, spec=full('earth_props') if i % 3 else full('pebble'), metres=0.6)
    # crumbs on the throat floor (flat, walkable)
    for i, (q, n) in enumerate(samples([fo], 40, 990, lambda q, n: n.z > 0.7)):
        lump(T, q, dr.uniform(0.03, 0.08), n, sides=5, squash=0.35, sink=0.45, seed=1700 + i,
             spec=full('earth_props') if i % 3 else full('pebble'), metres=0.3)
    clamp(T, 2, lo=SKIRT)
    join_into(do, T)
    join_into(do, L)
    ro = R.to_object('AN09a_Root')
    objs = [eo, fo, do, ro]
    # pivot: ground level (z = 0 = outside terrain), XY centred on the bounds; skirt/throat go below 0
    lo, hi = ak.bounds(objs)
    d = Vector((-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, 0))
    for o in objs:
        o.data.transform(Matrix.Translation(d))
    return objs, d


def checks(objs, d):
    bvh = bvh_of(objs)
    out = {}
    # clear opening at the mouth and along the throat (cast sideways / up from the walking line)
    for (y, fz, sc, xs) in [(-3.1, 0, 1, 0), (-2.8, 0, 1, 0)] + STATIONS[2:-1]:
        p = Vector((xs + d.x, y + d.y, fz + 0.05))
        up = bvh.ray_cast(p, Vector((0, 0, 1)))
        l = bvh.ray_cast(p + Vector((0, 0, 1.0)), Vector((-1, 0, 0)))
        r = bvh.ray_cast(p + Vector((0, 0, 1.0)), Vector((1, 0, 0)))
        out['y=%.1f' % y] = dict(height=round(up[3], 2) if up[0] else None,
                                  width_at_1m=round(l[3] + r[3], 2) if l[0] and r[0] else None)
    return out


if __name__ == '__main__':
    objs, d = build()
    ck = checks(objs, d)
    print('clearance', ck)
    rec = outputs(objs, HERE, 'SM_AN09_EntranceMound.fbx', 'AN09a', 'EntranceMound', SHEETS + '09-entrance-mound.png',
                  manifest_extra=dict(solidity='solid shell + walk-in throat', collider='mesh collider (Earth + Floor); Root/Props details: no collider',
                                      emissive=False,
                                      pivot='z = 0 is the outside ground level, XY = bounds centre. Base skirt and throat floor go below 0 (sink into terrain).',
                                      mouth='opening faces -Y; mouth centre at x = %.2f' % d.x,
                                      clearance=ck,
                                      notes='v2 details: roots across the lip + rootlets, ~48 sticks, 20 needle bundles, pebbles/clods in slopes, ~45 leaves, 10 ant holes, clods rolled to the foot, flat crumbs on the throat floor. Heap with steep hood over the mouth, low right shoulder, 3 foot lumps; boolean-cut throat descending ~0.7 m (<= 16 deg) to a dark end (Props dark); 11 lobed dry leaves conformed to the surface'),
                  views=RENDER, extra_views=[(-8, 6, 'front low')] if RENDER else None)
    write_manifest(HERE, [rec])
    print(rec)
