import bpy, bmesh, random, math
from mathutils import Vector
random.seed(23)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

def hull_object(name, pts, twist=0.0, lean=Vector((0,0,0))):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    for p in pts: bm.verts.new(p)
    res = bmesh.ops.convex_hull(bm, input=bm.verts)
    # drop interior verts not used by hull
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    # crooked: twist around Z and lean with height, so it is not a clean convex crystal
    for v in bm.verts:
        h = v.co.z
        a = twist * h
        x, y = v.co.x, v.co.y
        v.co.x = x*math.cos(a) - y*math.sin(a) + lean.x*h*h
        v.co.y = x*math.sin(a) + y*math.cos(a) + lean.y*h*h
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.normal_update()
    bm.to_mesh(me); bm.free()
    for poly in me.polygons: poly.use_smooth = False
    ob = bpy.data.objects.new(name, me)
    scene.collection.objects.link(ob)
    return ob

def normalize_shell(ob):
    # base on z=0, height 1, footprint ~1 x 1, centred
    me = ob.data
    xs=[v.co.x for v in me.vertices]; ys=[v.co.y for v in me.vertices]; zs=[v.co.z for v in me.vertices]
    cx=(min(xs)+max(xs))/2; cy=(min(ys)+max(ys))/2
    w=max(max(xs)-min(xs), max(ys)-min(ys)); h=max(zs)-min(zs); z0=min(zs)
    for v in me.vertices:
        v.co.x=(v.co.x-cx)/w; v.co.y=(v.co.y-cy)/w; v.co.z=(v.co.z-z0)/h

def shell_points(seed, n_body=26, n_spikes=6, squat=1.0):
    r = random.Random(seed)
    pts=[]
    # irregular body: rings of jittered points from base to top
    rings=5
    for i in range(rings):
        t=i/(rings-1)
        z=t
        rad=0.5*(1.0-0.35*t**1.4)*(0.85+0.3*r.random())
        k=r.randint(5,7)
        off=r.random()*math.tau
        for j in range(k):
            a=off+j/k*math.tau+r.uniform(-.35,.35)
            rr=rad*r.uniform(.78,1.12)
            pts.append(Vector((math.cos(a)*rr, math.sin(a)*rr, z+r.uniform(-.06,.06))))
    # a crooked crown
    pts.append(Vector((r.uniform(-.12,.12), r.uniform(-.12,.12), 1.12)))
    # spikes: crystal points jutting out of the sides and shoulders
    for s in range(n_spikes):
        a=r.random()*math.tau
        z=r.uniform(.25,.95)
        rad=0.5*(1.0-0.5*z)+r.uniform(.18,.38)
        pts.append(Vector((math.cos(a)*rad, math.sin(a)*rad, z+r.uniform(.05,.25))))
    for p in pts: p.z*=squat
    return pts


def crystal(name, base, direction, length, radius, r):
    # hexagonal prism with a pointed, slightly off-centre tip: a crystal growing out of the body
    me=bpy.data.meshes.new(name); bm=bmesh.new()
    sides=r.choice([5,6,6])
    d=direction.normalized()
    up=Vector((0,0,1)) if abs(d.z)<.95 else Vector((1,0,0))
    a=d.cross(up).normalized(); b=d.cross(a).normalized()
    bot=[];top=[]
    shoulder=length*r.uniform(.62,.78)
    for k in range(sides):
        t=k/sides*math.tau+r.uniform(-.15,.15)
        off=(a*math.cos(t)+b*math.sin(t))*radius*r.uniform(.85,1.1)
        bot.append(bm.verts.new(base-d*.15+off))
        top.append(bm.verts.new(base+d*shoulder+off*r.uniform(.85,1.05)))
    tip=bm.verts.new(base+d*length+(a*r.uniform(-1,1)+b*r.uniform(-1,1))*radius*.25)
    bm.faces.new(list(reversed(bot)))
    for k in range(sides):
        n=(k+1)%sides
        bm.faces.new([bot[k],bot[n],top[n],top[k]])
        bm.faces.new([top[k],top[n],tip])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    ob=bpy.data.objects.new(name,me); scene.collection.objects.link(ob); return ob

def shell(name, seed, twist, lean, n_spikes):
    r=random.Random(seed)
    body=hull_object(name, shell_points(seed,n_spikes=0), twist=0)
    for v in body.data.vertices: v.co.x*=1.0; v.co.z*=0.95
    for s in range(n_spikes):
        az=r.random()*math.tau
        z=r.uniform(.15,.85)
        out=Vector((math.cos(az),math.sin(az),r.uniform(.35,1.6) if s else 2.5)).normalized()
        base=Vector((math.cos(az)*.28*(1-.4*z), math.sin(az)*.28*(1-.4*z), z))
        if s==0: base=Vector((r.uniform(-.08,.08),r.uniform(-.08,.08),.75))
        c=crystal(name+"_c%d"%s, base, out, r.uniform(.35,.62) if s else .6, r.uniform(.07,.12) if s else .13, r)
        mod=body.modifiers.new("u%d"%s,'BOOLEAN'); mod.operation='UNION'; mod.solver='EXACT'; mod.object=c
        bpy.context.view_layer.objects.active=body
        bpy.ops.object.modifier_apply(modifier=mod.name)
        bpy.data.objects.remove(c)
    me=body.data; bm=bmesh.new(); bm.from_mesh(me)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=1e-4)
    for v in bm.verts:
        h=v.co.z; ang=twist*h; x,y=v.co.x,v.co.y
        v.co.x=x*math.cos(ang)-y*math.sin(ang)+lean.x*h*h
        v.co.y=x*math.sin(ang)+y*math.cos(ang)+lean.y*h*h
    bmesh.ops.triangulate(bm, faces=bm.faces); bm.normal_update(); bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth=False
    normalize_shell(body)
    return body

shells=[]
for i,(seed,tw,lean,sp) in enumerate([(1,.35,Vector((.08,0,0)),7),(7,-.5,Vector((0,-.1,0)),9),(13,.2,Vector((-.06,.07,0)),6)]):
    shells.append(shell("IceShell_%s"%"ABC"[i], seed, tw, lean, sp))

shards=[]
for i in range(6):
    r=random.Random(100+i)
    n=r.randint(6,9)
    L=r.uniform(.9,1.6)
    pts=[Vector((r.uniform(-.35,.35),r.uniform(-.3,.3),r.uniform(-.5,.5)*L)) for _ in range(n)]
    pts.append(Vector((0,0,.6*L))); 
    ob=hull_object("IceShard_%d"%i, pts, twist=r.uniform(-.3,.3))
    # unit-ish size ~0.2 m, centred on origin (for rigidbody)
    me=ob.data
    c=sum((v.co for v in me.vertices),Vector())/len(me.vertices)
    m=max((v.co-c).length for v in me.vertices)
    for v in me.vertices: v.co=(v.co-c)*(0.12/m)
    shards.append(ob)

import os
out="/mnt/user-data/outputs/ice"
os.makedirs(out,exist_ok=True)
def export(objs, path):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active=objs[0]
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'}, apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False)
export(shells, out+"/IceShells.fbx")
export(shards, out+"/IceShards.fbx")
bpy.ops.wm.save_as_mainfile(filepath=out+"/IceShells.blend")

# preview: lay out shells and shards, workbench render
for k,o in enumerate(shells): o.location=(k*1.4-1.4,0,0)
for k,o in enumerate(shards): o.location=(k*0.35-0.9,-1.2,0.15); o.scale=(1.6,1.6,1.6)
mat=bpy.data.materials.new("ice"); mat.diffuse_color=(.55,.85,1,1)
for o in shells+shards: o.data.materials.append(mat)
cam=bpy.data.objects.new("cam",bpy.data.cameras.new("cam")); scene.collection.objects.link(cam)
cam.location=(0,-5.2,2.6); cam.rotation_euler=(math.radians(68),0,0); scene.camera=cam
scene.render.engine='BLENDER_WORKBENCH'
scene.display.shading.light='STUDIO'; scene.display.shading.color_type='MATERIAL'
scene.display.shading.show_cavity=True; scene.display.shading.show_object_outline=True
scene.render.resolution_x=1200; scene.render.resolution_y=700
scene.render.filepath=out+"/ice_meshes_preview.png"
bpy.ops.render.render(write_still=True)
for o in shells+shards: print(o.name, len(o.data.vertices), len(o.data.polygons), [round(x,2) for x in o.dimensions])
