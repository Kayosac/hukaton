# measure cross-limb bleed: rotate the first bone of each chain, count verts of OTHER logical groups moving > 0.15 m
import bpy, json, math
from mathutils import Vector as V
bpy.ops.wm.open_mainfile(filepath='/home/claude/work/out/final5/rigtest/rigtest.blend')
body=bpy.data.objects['MG01_Body']; arm=bpy.data.objects['MG01_TestRig']
G=json.load(open('/home/claude/work/out/final5/MG01_rig_guides.json'))
logical=[vg.name for vg in body.vertex_groups if vg.name not in arm.data.bones]
lidx={body.vertex_groups[n].index:n for n in logical}
own=[]
for v in body.data.vertices:
    g=[lidx[x.group] for x in v.groups if x.group in lidx and x.weight>0.5]; own.append(g[0] if g else None)
for pb in arm.pose.bones: pb.rotation_mode='XYZ'; pb.rotation_euler=(0,0,0)
dg=bpy.context.evaluated_depsgraph_get()
def coords():
    dg.update(); e=body.evaluated_get(dg); m=e.to_mesh(); c=[v.co.copy() for v in m.vertices]; e.to_mesh_clear(); return c
rest=coords(); res={}
for key in ['C01','C02','L01','L02','A01','A02','S01','S02','S03','L03','L04','A03','A04']:
    first=G['chains'][key][1]['name'] if len(G['chains'][key])>2 else G['chains'][key][0]['name']
    b=arm.pose.bones.get(G['chains'][key][0]['name'])
    if b is None: continue
    b.rotation_euler=(math.radians(35),0,0)
    c=coords()
    moved={}
    for i,(a,bb) in enumerate(zip(rest,c)):
        if (a-bb).length>0.15 and own[i]!=key: moved[own[i]]=moved.get(own[i],0)+1
    res[key]=moved; b.rotation_euler=(0,0,0)
print(json.dumps(res))
