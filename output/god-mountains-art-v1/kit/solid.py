# solid.py src dst voxel target_tris [render.png]
import bpy, bmesh, sys, math
from mathutils import Vector
args=sys.argv[1:]
src,dst,voxel,target=args[0],args[1],float(args[2]),int(args[3]); png=args[4] if len(args)>4 else None
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src, axis_forward="-Z", axis_up="Y", bake_space_transform=True)
root=[o for o in bpy.context.scene.objects if o.type=='EMPTY'][0]
parts=[o for o in bpy.context.scene.objects if o.type=='MESH']
# texel density of the original: UV units per metre (median over faces)
import statistics
dens=[]
for o in parts[:20]:
    me=o.data; uv=me.uv_layers.active
    for p in me.polygons[:50]:
        if len(p.loop_indices)<3: continue
        a,b=p.loop_indices[0],p.loop_indices[1]
        va=o.matrix_world@me.vertices[me.loops[a].vertex_index].co; vb=o.matrix_world@me.vertices[me.loops[b].vertex_index].co
        d=(va-vb).length
        if d>0.01: dens.append((uv.data[a].uv-uv.data[b].uv).length/d)
uvpm=statistics.median(dens) if dens else 0.25
mats=[m for m in parts[0].data.materials]
names={m.name:m for o in parts for m in o.data.materials if m}
top=names.get('GM_FarTop'); side=names.get('GM_FarSide')
# join everything into one object
for o in parts: o.parent=None
bpy.ops.object.select_all(action='DESELECT')
for o in parts:
    o.select_set(True)
    with bpy.context.temp_override(object=o, active_object=o, selected_editable_objects=[o]): bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bpy.context.view_layer.objects.active=parts[0]
with bpy.context.temp_override(active_object=parts[0], selected_editable_objects=parts, selected_objects=parts): bpy.ops.object.join()
m=parts[0]
t0=len(m.data.polygons)
# one solid body: voxel remesh closes the gaps between the stones
r=m.modifiers.new('r','REMESH'); r.mode='VOXEL'; r.voxel_size=voxel; r.adaptivity=0
with bpy.context.temp_override(object=m, active_object=m): bpy.ops.object.modifier_apply(modifier='r')
# drop loose islands that are tiny next to the mountain
bm=bmesh.new(); bm.from_mesh(m.data)
islands=[]; seen=set()
for f in bm.faces:
    if f.index in seen: continue
    stack=[f]; isl=[]; seen.add(f.index)
    while stack:
        g=stack.pop(); isl.append(g)
        for e in g.edges:
            for h in e.link_faces:
                if h.index not in seen: seen.add(h.index); stack.append(h)
    islands.append(isl)
areas=[sum(f.calc_area() for f in i) for i in islands]
big=max(areas)
kill=[f for i,a in zip(islands,areas) if a<big*0.02 for f in i]
print('ISLANDS',len(islands),'dropped',sum(1 for a in areas if a<big*0.02))
bmesh.ops.delete(bm, geom=kill, context='FACES')
bm.to_mesh(m.data); bm.free()
d=m.modifiers.new('d','DECIMATE'); d.ratio=min(1.0, target/max(1,sum(len(p.vertices)-2 for p in m.data.polygons))); d.use_collapse_triangulate=True
with bpy.context.temp_override(object=m, active_object=m): bpy.ops.object.modifier_apply(modifier='d')
# materials: up-facing faces are the top, the rest the side; box-projected UVs at the original density
me=m.data; me.materials.clear(); me.materials.append(top); me.materials.append(side)
bm=bmesh.new(); bm.from_mesh(me)
bmesh.ops.triangulate(bm, faces=bm.faces[:])
uvl=bm.loops.layers.uv.verify()
for f in bm.faces:
    n=f.normal; f.material_index=0 if n.z>0.55 else 1
    ax=max(range(3), key=lambda i: abs(n[i]))
    for l in f.loops:
        c=l.vert.co
        u,v=((c.y,c.z) if ax==0 else (c.x,c.z) if ax==1 else (c.x,c.y))
        l[uvl].uv=(u*uvpm, v*uvpm)
bm.to_mesh(me); bm.free()
for p in me.polygons: p.use_smooth=False
m.name=root.name.replace(' ','_')+'_solid'; me.name=m.name
m.parent=root
t1=len(me.polygons)
print('SOLID',src.split('/')[-1],'parts',len(parts),'tris',t0,'->',t1,'uv/m',round(uvpm,3))
bpy.ops.object.select_all(action='DESELECT'); root.select_set(True); m.select_set(True)
bpy.ops.export_scene.fbx(filepath=dst, use_selection=True, object_types={"EMPTY","MESH"}, apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
    mesh_smooth_type="FACE", add_leaf_bones=False, use_triangles=True, path_mode="STRIP")
