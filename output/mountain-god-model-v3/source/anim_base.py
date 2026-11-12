"""Build the animation preview base: test rig (from rigtest.blend) + cloth/leaves skinned by nearest-body weights
+ a bone-visualisation mesh deformed by the same armature. Saves anim_base.blend."""
import sys, os, math
sys.path.insert(0, '/home/claude/work/mg')
import bpy, bmesh
from mathutils import Vector as V, kdtree

SRC = '/home/claude/work/out/v3c/rigtest/rigtest.blend'
OUT = '/home/claude/work/anim/anim_base.blend'
bpy.ops.wm.open_mainfile(filepath=SRC)
sc = bpy.context.scene
arm = bpy.data.objects['MG01_TestRig']
body = bpy.data.objects['MG01_Body']
bones = arm.data.bones
for pb in arm.pose.bones:
    pb.rotation_mode = 'QUATERNION'
    pb.rotation_quaternion = (1, 0, 0, 0)
    pb.location = (0, 0, 0)
    pb.scale = (1, 1, 1)

# --- body weights per vertex (bone groups only)
bone_vg = {vg.index: vg.name for vg in body.vertex_groups if vg.name in bones}
bw = []
for v in body.data.vertices:
    ws = [(bone_vg[g.group], g.weight) for g in v.groups if g.group in bone_vg and g.weight > 1e-4]
    bw.append(ws)
kd = kdtree.KDTree(len(body.data.vertices))
for v in body.data.vertices:
    kd.insert(v.co, v.index)
kd.balance()

def skin(ob, per_island):
    mw = ob.matrix_world
    me = ob.data
    for vg in list(ob.vertex_groups):
        ob.vertex_groups.remove(vg)
    groups = {}
    def add(vi, ws):
        for n, w in ws:
            g = groups.get(n) or ob.vertex_groups.new(name=n)
            groups[n] = g
            g.add([vi], w, 'REPLACE')
    if per_island:
        bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
        seen = set()
        for v0 in bm.verts:
            if v0.index in seen:
                continue
            isl, stack = [], [v0]
            seen.add(v0.index)
            while stack:
                v = stack.pop(); isl.append(v.index)
                for e in v.link_edges:
                    o = e.other_vert(v)
                    if o.index not in seen:
                        seen.add(o.index); stack.append(o)
            best = min(isl, key=lambda i: kd.find(mw @ me.vertices[i].co)[2])
            _, bi, _ = kd.find(mw @ me.vertices[best].co)
            for i in isl:
                add(i, bw[bi])
        bm.free()
    else:
        for v in me.vertices:
            # average of 6 nearest body verts -> smooth, no tearing between thighs
            acc = {}
            for co, bi, d in kd.find_n(mw @ v.co, 6):
                for n, w in bw[bi]:
                    acc[n] = acc.get(n, 0) + w
            tot = sum(acc.values()) or 1
            add(v.index, [(n, w / tot) for n, w in acc.items()])
    mod = ob.modifiers.new('Armature', 'ARMATURE')
    mod.object = arm
    print('skinned', ob.name, len(groups), 'groups')

skin(bpy.data.objects['MG01_Cloth'], per_island=False)
skin(bpy.data.objects['MG01_Leaves'], per_island=True)

# --- bone visualisation: octahedral bones, colour by limb, rigidly bound to their bone
COL = {'spine': '#FFD34D', 'head': '#FF7AB8', 'S': '#4DE3FF', 'C': '#7DFF8A', 'A': '#B98CFF', 'L': '#FF9A4D'}
def cat(name):
    if name in ('Root', 'Pelvis', 'Spine', 'Chest', 'Neck') or name.startswith('PelvisLow'):
        return 'spine'
    if name.split('_')[0] in ('hat', 'beard', 'head', 'neck'):
        return 'head'
    return name[0] if name[0] in 'SCAL' else 'spine'
mats = {}
for k, h in COL.items():
    m = bpy.data.materials.new('bone_' + k)
    h = h.lstrip('#'); m.diffuse_color = tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)) + (1,)
    mats[k] = m
me = bpy.data.meshes.new('MG01_BoneViz')
bv = bpy.data.objects.new('MG01_BoneViz', me)
sc.collection.objects.link(bv)
for k in COL:
    me.materials.append(mats[k])
bm = bmesh.new()
assign = []
for b in bones:
    if b.name == 'Root':
        continue
    h, t = b.head_local, b.tail_local
    d = t - h; L = d.length
    if L < 1e-4:
        continue
    y = d.normalized()
    x = y.orthogonal().normalized(); z = y.cross(x)
    is_f = '_f' in b.name or 'thumb' in b.name or 'toe' in b.name
    w = min(max(0.09 * L, 0.035 if is_f else 0.07), 0.32)
    m = h + d * 0.18
    pts = [h, m + x * w, m + z * w, m - x * w, m - z * w, t]
    vs = [bm.verts.new(p) for p in pts]
    mi = list(COL).index(cat(b.name))
    for f in [(0, 1, 2), (0, 2, 3), (0, 3, 4), (0, 4, 1), (5, 2, 1), (5, 3, 2), (5, 4, 3), (5, 1, 4)]:
        fc = bm.faces.new([vs[i] for i in f]); fc.material_index = mi; fc.smooth = False
    assign.append((b.name, [v for v in vs]))
    # joint ball at the head
    r = w * 0.55
    ico = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=r)
    for v in ico['verts']:
        v.co += h
    for f in {f for v in ico['verts'] for f in v.link_faces}:
        f.material_index = mi
    assign[-1][1].extend(ico['verts'])
bm.verts.index_update()
idx = [(n, [v.index for v in vs]) for n, vs in assign]
bm.to_mesh(me); bm.free()
for n, ids in idx:
    g = bv.vertex_groups.get(n) or bv.vertex_groups.new(name=n)
    g.add(ids, 1.0, 'REPLACE')
mod = bv.modifiers.new('Armature', 'ARMATURE'); mod.object = arm
print('boneviz', len(idx), 'bones', len(me.polygons), 'faces')
bpy.ops.wm.save_as_mainfile(filepath=OUT)
