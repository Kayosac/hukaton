"""LODs + FBX export + hero renders for an assembled MG01.blend.
Run: python3 -I export.py ASM_DIR
Creates in ASM_DIR: MG01.blend (with LOD collection), MG01.fbx, renders/.
"""
import sys, os, json, math
sys.path.insert(0, '/home/claude/work/mg')
import bpy, bmesh
from mathutils import Vector as V
import bl

D = sys.argv[1]
bpy.ops.wm.open_mainfile(filepath=os.path.join(D, 'MG01.blend'))
sc = bpy.context.scene
LOG = json.load(open(os.path.join(D, 'assembly_log.json')))
body, cloth, leaves, root = (bpy.data.objects[n] for n in ('MG01_Body', 'MG01_Cloth', 'MG01_Leaves', 'MG01_Root'))


def tris(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    me = ob.evaluated_get(dg).to_mesh()
    n = sum(len(p.vertices) - 2 for p in me.polygons)
    ob.evaluated_get(dg).to_mesh_clear()
    return n


def lod_copy(ob, suffix, target_tris, col):
    me = ob.data.copy()
    c = bpy.data.objects.new(ob.name + suffix, me)
    col.objects.link(c)
    c.parent = ob.parent
    c.matrix_world = ob.matrix_world.copy()
    if len(c.vertex_groups) == 0 and len(ob.vertex_groups):  # older Blender: groups live on the object
        for vg in ob.vertex_groups:
            c.vertex_groups.new(name=vg.name)
        for v in ob.data.vertices:
            for g in v.groups:
                c.vertex_groups[g.group].add([v.index], g.weight, 'REPLACE')
    if target_tris:
        base = sum(len(p.vertices) - 2 for p in me.polygons)
        m = c.modifiers.new('Decimate', 'DECIMATE')
        m.decimate_type = 'COLLAPSE'
        m.use_collapse_triangulate = True
        m.ratio = min(1.0, target_tris / base)
        bpy.context.view_layer.objects.active = c
        for o in bpy.context.selected_objects:
            o.select_set(False)
        c.select_set(True)
        bpy.ops.object.modifier_apply(modifier=m.name)
        # keep the support contacts exactly on the ground plane (no popping between LODs)
        lo = min(v.co.z for v in ob.data.vertices)
        for v in c.data.vertices:
            if v.co.z < lo:
                v.co.z = lo
    return c


# v2: no LODs (user decision). Remove an old LOD collection if present.
col = bpy.data.collections.get('MG01_LOD')
if col:
    for o in list(col.objects):
        bpy.data.objects.remove(o)
    bpy.data.collections.remove(col)
lods = {}
LOG.pop('lod', None)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(D, 'MG01.blend'))

# ---------------- FBX: LOD0 objects renamed with _LOD0 suffix only inside the export (Unity LODGroup)
rename = {}
orig = {o: o.name for o in rename}
for o, n in rename.items():
    o.name = n
exp = [root, body, cloth, leaves] + list(lods.values())
tri_mods = []
for o in exp:
    if o.type == 'MESH':
        m = o.modifiers.new('ExportTriangulate', 'TRIANGULATE')
        m.quad_method = 'BEAUTY'; m.ngon_method = 'BEAUTY'
        tri_mods.append((o, m))
for o in bpy.context.selected_objects:
    o.select_set(False)
for o in exp:
    o.select_set(True)
bpy.context.view_layer.objects.active = root
fbx = os.path.join(D, 'MG01.fbx')
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={'EMPTY', 'MESH'}, apply_unit_scale=True,
                         apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                         use_mesh_modifiers=True, mesh_smooth_type='OFF', use_tspace=False, add_leaf_bones=False,
                         path_mode='STRIP', embed_textures=False, use_custom_props=False)
for o, m in tri_mods:
    o.modifiers.remove(m)
for o, n in orig.items():
    o.name = n
LOG['fbx'] = dict(path=fbx, bytes=os.path.getsize(fbx))
json.dump(LOG, open(os.path.join(D, 'assembly_log.json'), 'w'), indent=1, default=str)
print(LOG['fbx'])
