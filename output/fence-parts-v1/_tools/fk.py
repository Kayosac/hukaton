"""Fence splitter: a fence section FBX -> one object per part (post / rail / picket / rod / stake), moss and caps
attached to their part, grass kept as one static object; every part gets a convex _COL; pivot = centre of the part."""
import os, sys, math, json, numpy as np, bpy, bmesh
from mathutils import Vector, Matrix
sys.path.insert(0, '/home/claude/fence/work')
import sk
R = '/home/claude/fence/ref/'
ST = '/home/claude/fence/ref/swamptex/'
TEX = {'M_Forest_Structures_Wood': '/home/claude/brk/ref/T_Forest_Structures_Wood.png'}
for k in ('Bark', 'BarkMossy', 'Birch', 'Planks', 'Moss', 'Props', 'Plants', 'Stone', 'Mud'):
    TEX['M_Swamp_' + k] = ST + f'T_Swamp_{k}.png'
STRUCT = ('M_Forest_Structures_Wood', 'M_Swamp_Planks', 'M_Swamp_Bark', 'M_Swamp_BarkMossy', 'M_Swamp_Birch', 'M_Swamp_Props')
SKIN = ('M_Swamp_Moss',)
GRASS = ('M_Swamp_Plants',)

def fix_mat(m):
    name = m.name.split('.')[0]
    if m.get('fk'): return
    m['fk'] = 1; m.use_nodes = True; nt = m.node_tree
    for n in list(nt.nodes):
        if n.type != 'OUTPUT_MATERIAL': nt.nodes.remove(n)
    b = nt.nodes.new('ShaderNodeBsdfPrincipled'); nt.links.new(b.outputs[0], nt.nodes['Material Output'].inputs[0])
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(TEX[name], check_existing=True)
    t.interpolation = 'Closest'; nt.links.new(t.outputs['Color'], b.inputs['Base Color'])
    b.inputs['Roughness'].default_value = 1; b.inputs['Specular IOR Level'].default_value = .05
    if name in GRASS:
        mth = nt.nodes.new('ShaderNodeMath'); mth.operation = 'GREATER_THAN'; mth.inputs[1].default_value = .5
        nt.links.new(t.outputs['Alpha'], mth.inputs[0]); nt.links.new(mth.outputs[0], b.inputs['Alpha'])
        try: m.surface_render_method = 'DITHERED'
        except Exception: pass

def load(fbx):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=R + fbx)
    for o in [o for o in bpy.data.objects if o.type != 'MESH']: bpy.data.objects.remove(o)
    objs = list(bpy.data.objects)
    for o in objs:
        o.data.transform(o.matrix_world); o.matrix_world = Matrix.Identity(4)
        for m in o.data.materials: fix_mat(m)
    return objs

def mat_of(o):
    return o.data.materials[0].name.split('.')[0]

def islands(objs):
    out = []
    for o in objs:
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.mesh.separate(type='LOOSE'); bpy.ops.object.mode_set(mode='OBJECT')
        out += list(bpy.context.selected_objects)
    return out

def V(o): return np.array([v.co[:] for v in o.data.vertices])

def hull_planes(P):
    bm = bmesh.new()
    for p in P: bm.verts.new(p)
    bmesh.ops.convex_hull(bm, input=bm.verts)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    bm.normal_update()
    N = np.array([f.normal[:] for f in bm.faces]); D = np.array([f.normal.dot(f.verts[0].co) for f in bm.faces])
    bm.free()
    return N, D

def classify(o):
    P = V(o); d = P.max(0) - P.min(0); dx, dy, dz = d; horiz = max(dx, dy); cross = min(dx, dy)
    m = mat_of(o)
    if dz > 0.45 and dz >= 0.6 * horiz:
        if cross >= 0.15: return 'Post'
        return 'Stake' if m in ('M_Swamp_Bark',) else 'Picket'
    if horiz >= 0.3:
        return 'Rod' if m == 'M_Swamp_Bark' else 'Rail'
    return None   # small bit: attach

def join(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1: bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = name; o.data.name = name
    return o

def split(fbx, vid, merge=None):
    """merge: optional list of (kind, [indices]) to glue pieces (e.g. rod fragments) - not used by default."""
    objs = load(fbx)
    isl = islands(objs)
    parts, small, grass = [], [], []
    for o in isl:
        m = mat_of(o)
        if m in GRASS: grass.append(o); continue
        k = classify(o) if m in STRUCT else None
        if k: parts.append([k, o, []])
        else: small.append(o)
    PV = [V(p[1]) for p in parts]
    planes = [hull_planes(pv) for pv in PV]
    for o in small:
        q = V(o)
        # distance of the bit (moss skin, cap) to each part's convex hull: it hangs on the closest surface
        dist = [np.clip((q @ n.T - d).max(1), 0, None).mean() for n, d in planes]
        parts[int(np.argmin(dist))][2].append(o)
    # names: per kind, ordered along the fence (x, then y)
    order = sorted(range(len(parts)), key=lambda i: (parts[i][0], round(float(PV[i].mean(0)[0]), 2), float(PV[i].mean(0)[1]), float(PV[i].mean(0)[2])))
    count = {}
    result = []
    for i in order:
        k, o, att = parts[i]
        count[k] = count.get(k, 0) + 1
        name = f'{vid}_{k}_{count[k]}'
        p = join([o] + att, name)
        result.append(p)
    for p in result:
        P = V(p); c = (P.min(0) + P.max(0)) / 2
        p.data.transform(Matrix.Translation(Vector((-c).tolist()))); p.location = Vector(c.tolist())
    cols = [col(p) for p in result]
    g = join(grass, f'{vid}_Grass') if grass else None
    return result, cols, g

def col(p, max_tris=255):
    me = p.data
    keep = set()
    for poly in me.polygons:
        if me.materials[poly.material_index].name.split('.')[0] not in SKIN + GRASS:
            keep.update(poly.vertices)
    bm = bmesh.new()
    for i in keep: bm.verts.new(me.vertices[i].co)
    bmesh.ops.convex_hull(bm, input=bm.verts)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    bmesh.ops.triangulate(bm, faces=bm.faces)
    if len(bm.faces) > max_tris:
        bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(8), verts=bm.verts, edges=bm.edges)
        bmesh.ops.triangulate(bm, faces=bm.faces)
    m2 = bpy.data.meshes.new(p.name + '_COL'); bm.to_mesh(m2); bm.free()
    c = bpy.data.objects.new(p.name + '_COL', m2); bpy.context.scene.collection.objects.link(c)
    c.location = p.location.copy(); c.display_type = 'WIRE'
    return c

def is_convex(o, eps=2e-4):
    me = o.data; P = V(o)
    for f in me.polygons:
        n = np.array(f.normal[:]); a = np.array(me.vertices[f.vertices[0]].co[:])
        if np.dot(P - a, n).max() > eps: return False
    return True

def tris(o):
    o.data.calc_loop_triangles(); return len(o.data.loop_triangles)

def export(objs, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'}, apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False, path_mode='STRIP',
        embed_textures=False, use_custom_props=False)
