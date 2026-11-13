"""Step 1: the split skin (torn where limbs move apart, no fills) with its cleaned weights, as arrays."""
import os
WORK = os.environ.get("REBUILD_WORK", "/tmp/god/rb")
import sys, bpy, numpy as np
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import godrig
from collections import Counter
bpy.ops.wm.open_mainfile(filepath=godrig.SRC)
keep = {"MG01_TestRig", "MG01_Body", "MG01_Cloth", "MG01_Leaves"}
for o in list(bpy.data.objects):
    if o.name not in keep:
        bpy.data.objects.remove(o, do_unlink=True)
arm = bpy.data.objects["MG01_TestRig"]
for pb in arm.pose.bones:
    for c in list(pb.constraints): pb.constraints.remove(c)
    pb.rotation_mode = "QUATERNION"; pb.rotation_quaternion = (1,0,0,0); pb.location = (0,0,0)
body = bpy.data.objects["MG01_Body"]
print("arm mw", arm.matrix_world, "body mw", body.matrix_world, body.parent)
godrig.separate_limbs(body, arm, fill=False)
me = body.data
n = len(me.vertices)
V = np.array([v.co[:] for v in me.vertices])
me.calc_loop_triangles()
T = np.array([t.vertices[:] for t in me.loop_triangles])
TL = np.array([t.loops[:] for t in me.loop_triangles])
uv = np.array([d.uv[:] for d in me.uv_layers[0].data])
bones = [b.name for b in arm.data.bones]
bi = {b: i for i, b in enumerate(bones)}
W = np.zeros((n, len(bones)), np.float32)
gname = {vg.index: vg.name for vg in body.vertex_groups}
for v in me.vertices:
    for g in v.groups:
        nm = gname[g.group]
        if nm in bi: W[v.index, bi[nm]] += g.weight
print("weight sums", W.sum(1).min(), W.sum(1).max())
ML = np.array([np.array(arm.data.bones[b].matrix_local) for b in bones])
# per-vertex part code from attribute? recompute: dominant limb by weights of LIMB groups
vs, vd, jt = godrig.separate_limbs.sides
BH = np.array([arm.data.bones[b].head_local[:] for b in bones]); BT = np.array([arm.data.bones[b].tail_local[:] for b in bones])
np.savez(WORK + "/split.npz", V=V, T=T, TL=TL, uv=uv, W=W, ML=ML, bones=np.array(bones), vside=np.array(vs), vsdom=np.array(vd), joint=np.array(jt), BH=BH, BT=BT)
# components
import bmesh
bm = bmesh.new(); bm.from_mesh(me)
seen=set(); comps=[]
for v in bm.verts:
    if v in seen: continue
    st=[v]; seen.add(v); c=[]
    while st:
        x=st.pop(); c.append(x.index)
        for e in x.link_edges:
            y=e.other_vert(x)
            if y not in seen: seen.add(y); st.append(y)
    comps.append(c)
comps.sort(key=len, reverse=True)
for c in comps[:12]:
    dom = Counter(bones[int(np.argmax(W[i]))] for i in c).most_common(3)
    print(len(c), dom)
print("boundary edges", sum(1 for e in bm.edges if e.is_boundary), "components", len(comps))
bpy.ops.wm.save_as_mainfile(filepath=WORK + "/split.blend")

import json
json.dump({b.name: (b.parent.name if b.parent else None) for b in arm.data.bones}, open(WORK + "/parents.json", "w"))
