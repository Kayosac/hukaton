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
    join_into(do, L)
    objs = [eo, fo, do]
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
                  manifest_extra=dict(solidity='solid shell + walk-in throat', collider='mesh collider (Earth + Floor); Props (dark plug + leaves) no collider',
                                      emissive=False,
                                      pivot='z = 0 is the outside ground level, XY = bounds centre. Base skirt and throat floor go below 0 (sink into terrain).',
                                      mouth='opening faces -Y; mouth centre at x = %.2f' % d.x,
                                      clearance=ck,
                                      notes='Heap with steep hood over the mouth, low right shoulder, 3 foot lumps; boolean-cut throat descending ~0.7 m (<= 16 deg) to a dark end (Props dark); 11 lobed dry leaves conformed to the surface'),
                  views=RENDER, extra_views=[(-8, 6, 'front low')] if RENDER else None)
    write_manifest(HERE, [rec])
    print(rec)
