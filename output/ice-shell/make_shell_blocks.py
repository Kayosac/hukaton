import bpy, bmesh, random, math, os
from mathutils import Vector
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene
out="/mnt/user-data/outputs/ice"; os.makedirs(out,exist_ok=True)

def crystal(name, base, direction, length, radius, r):
    me=bpy.data.meshes.new(name); bm=bmesh.new()
    sides=r.choice([5,6])
    d=direction.normalized()
    up=Vector((0,0,1)) if abs(d.z)<.95 else Vector((1,0,0))
    a=d.cross(up).normalized(); b=d.cross(a).normalized()
    bot=[];top=[]
    shoulder=length*r.uniform(.6,.75)
    for k in range(sides):
        t=k/sides*math.tau+r.uniform(-.15,.15)
        off=(a*math.cos(t)+b*math.sin(t))*radius*r.uniform(.85,1.1)
        bot.append(bm.verts.new(base-d*.12+off))
        top.append(bm.verts.new(base+d*shoulder+off*r.uniform(.85,1.0)))
    tip=bm.verts.new(base+d*length+(a*r.uniform(-1,1)+b*r.uniform(-1,1))*radius*.25)
    bm.faces.new(list(reversed(bot)))
    for k in range(sides):
        n=(k+1)%sides
        bm.faces.new([bot[k],bot[n],top[n],top[k]]); bm.faces.new([top[k],top[n],tip])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    ob=bpy.data.objects.new(name,me); scene.collection.objects.link(ob); return ob

def block(name, seed, twist, crystals):
    """A crooked block of ice: never smaller than the unit box x,y in [-.5,.5], z in [0,1] (it must hide a
    camera-facing sprite of that size from any side); faces bulge outward, the top is uneven, edges chamfered."""
    r=random.Random(seed)
    me=bpy.data.meshes.new(name); bm=bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts: v.co.z+=.5
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=2, use_grid_fill=True)
    # outward only: every vertex moves away from the box centre line, never in
    for v in bm.verts:
        x,y,z=v.co
        sx=1+r.uniform(.0,.09); sy=1+r.uniform(.0,.09)
        v.co.x=x*sx+ (r.uniform(0,.03)*math.copysign(1,x) if abs(x)>.45 else 0)
        v.co.y=y*sy+ (r.uniform(0,.03)*math.copysign(1,y) if abs(y)>.45 else 0)
        if z>.99: v.co.z=1+r.uniform(.0,.12)
        elif z<.01: v.co.z=0
        else: v.co.z=z+r.uniform(-.03,.03)
    bmesh.ops.bevel(bm, geom=[e for e in bm.edges if e.is_boundary or len(e.link_faces)==2 and e.calc_face_angle(0)>.6],
                    offset=.045, segments=1, affect='EDGES', clamp_overlap=True)
    bm.to_mesh(me); bm.free()
    body=bpy.data.objects.new(name,me); scene.collection.objects.link(body)
    for s in range(crystals):
        az=r.random()*math.tau
        # on the upper rim and shoulders, leaning out and up
        base=Vector((math.cos(az)*.42, math.sin(az)*.42, r.uniform(.72,1.0)))
        out_=Vector((math.cos(az)*r.uniform(.4,1.0), math.sin(az)*r.uniform(.4,1.0), r.uniform(.8,1.6)))
        c=crystal(name+"_c%d"%s, base, out_, r.uniform(.22,.4), r.uniform(.05,.09), r)
        mod=body.modifiers.new("u%d"%s,'BOOLEAN'); mod.operation='UNION'; mod.solver='EXACT'; mod.object=c
        bpy.context.view_layer.objects.active=body
        bpy.ops.object.modifier_apply(modifier=mod.name)
        bpy.data.objects.remove(c)
    me=body.data; bm=bmesh.new(); bm.from_mesh(me)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=1e-4)
    # a little crooked: the block twists with height (a turned square still holds its inscribed circle)
    for v in bm.verts:
        a=twist*v.co.z; x,y=v.co.x,v.co.y
        v.co.x=x*math.cos(a)-y*math.sin(a); v.co.y=x*math.sin(a)+y*math.cos(a)
    bmesh.ops.triangulate(bm, faces=bm.faces); bm.normal_update(); bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth=False
    return body

shells=[block("IceShell_A",3,.10,3), block("IceShell_B",11,-.14,4), block("IceShell_C",29,.06,2)]

def export(objs, path):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active=objs[0]
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'}, apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False)
export(shells, out+"/IceShells.fbx")
bpy.ops.wm.save_as_mainfile(filepath=out+"/IceShells.blend")

# check: the unit box core is inside every shell (sample the inscribed circle at several heights)
for o in shells:
    xs=[v.co.x for v in o.data.vertices]; ys=[v.co.y for v in o.data.vertices]; zs=[v.co.z for v in o.data.vertices]
    print(o.name, len(o.data.vertices), "x",round(min(xs),3),round(max(xs),3),"y",round(min(ys),3),round(max(ys),3),"z",round(min(zs),3),round(max(zs),3))

# preview with a flat "sprite" card inside each block
for k,o in enumerate(shells): o.location=(k*1.6-1.6,0,0)
mat=bpy.data.materials.new("ice"); mat.diffuse_color=(.55,.85,1,.45)
for o in shells: o.data.materials.append(mat)
cam=bpy.data.objects.new("cam",bpy.data.cameras.new("cam")); scene.collection.objects.link(cam)
cam.location=(0,-5.6,2.4); cam.rotation_euler=(math.radians(72),0,0); scene.camera=cam
scene.render.engine='BLENDER_WORKBENCH'
scene.display.shading.light='STUDIO'; scene.display.shading.color_type='MATERIAL'
scene.display.shading.show_cavity=True; scene.display.shading.show_object_outline=True
scene.render.resolution_x=1200; scene.render.resolution_y=600
scene.render.filepath=out+"/ice_blocks_preview.png"
bpy.ops.render.render(write_still=True)
