# cull.py src dst ground_z  — removes faces no outside ray can reach (inside other stones / under the ground)
import bpy, bmesh, sys, math
from mathutils import Vector
from mathutils.bvhtree import BVHTree
src,dst,ground=sys.argv[1],sys.argv[2],float(sys.argv[3])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src, axis_forward="-Z", axis_up="Y", bake_space_transform=True)
parts=[o for o in bpy.context.scene.objects if o.type=='MESH']
verts=[];polys=[]
for o in parts:
    base=len(verts); verts+= [o.matrix_world@v.co for v in o.data.vertices]
    polys+=[[base+i for i in p.vertices] for p in o.data.polygons]
tree=BVHTree.FromPolygons(verts,polys)
N=320; dirs=[]
ga=math.pi*(3-math.sqrt(5))
for i in range(N*2):
    z=1-(i+.5)/(N*2)*2; r=math.sqrt(1-z*z); dirs.append(Vector((math.cos(ga*i)*r, math.sin(ga*i)*r, z)))
t0=t1=0; emptied=0
for o in parts:
    me=o.data; mw=o.matrix_world; nm=mw.to_3x3().inverted().transposed()
    bm=bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    hidden=[]
    for f in bm.faces:
        n=(nm@f.normal).normalized(); c=mw@f.calc_center_median()
        # a few sample points per face: centre and towards the corners
        import random as _r
        rr=_r.Random(f.index)
        wv=[mw@v.co for v in f.verts]
        area=f.calc_area()*abs(mw.determinant())**(2/3)
        k=int(min(60,max(8,area/0.03)))
        pts=[c]+[c.lerp(w,.9) for w in wv]
        for _ in range(k):
            a_,b_=rr.random(),rr.random()
            if a_+b_>1: a_,b_=1-a_,1-b_
            i=rr.randrange(1,len(wv)-1)
            pts.append(wv[0]+(wv[i]-wv[0])*a_+(wv[i+1]-wv[0])*b_)
        seen=False
        for p in pts:
            o0=p+n*0.015
            for d in dirs:
                if d.dot(n)<=0.02: continue
                if tree.ray_cast(o0,d,1e5)[0] is None: seen=True; break
            if seen: break
        if not seen: hidden.append(f)
    t0+=len(bm.faces)
    if len(hidden)==len(bm.faces):
        emptied+=1; hidden=[]   # a stone with nothing visible keeps its shape (its collider needs it)
    bmesh.ops.delete(bm, geom=hidden, context='FACES_ONLY')
    t1+=len(bm.faces)
    bm.to_mesh(me); bm.free()
print('CULL',src.split('/')[-1],t0,'->',t1,'fully hidden stones kept:',emptied)
for o in bpy.context.scene.objects: o.select_set(True)
# geometry names as the kit writes them (exporter adds .001)
for o in parts: o.data.name=o.name
bpy.ops.export_scene.fbx(filepath=dst, use_selection=True, object_types={"EMPTY","MESH"}, apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
    mesh_smooth_type="FACE", add_leaf_bones=False, use_triangles=True, path_mode="STRIP")
