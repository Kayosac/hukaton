import sys; sys.path.insert(0, "/tmp/god/prev")
import bpy, json
from mathutils import Vector
import env
arm = env.open_anim()
for m in bpy.data.materials:
    print("mat", m.name, m.use_nodes, [n.type for n in m.node_tree.nodes] if m.node_tree else None)
arm.animation_data.action = bpy.data.actions["MG01_Idle"]
bpy.context.scene.frame_set(0)
PB = arm.pose.bones
W = arm.matrix_world
out = {}
for s in ("S01", "S02", "S03"):
    tips = {}
    for b in PB:
        if b.name.startswith(s + "_") and (b.name.endswith("_02") and ("_f" in b.name or "thumb" in b.name)):
            tips[b.name] = list(W @ b.tail)
    knuck = {b.name: list(W @ b.head) for b in PB if b.name.startswith(s + "_") and b.name.endswith("_00") and ("_f" in b.name or "thumb" in b.name)}
    wr = PB[s + "_wrist_00"]
    out[s] = dict(tips=tips, knuckles=knuck, wrist_head=list(W @ wr.head), wrist_tail=list(W @ wr.tail))
    # palm underside: lowest body vertex near the wrist tail (within the knuckle ring), excluding fingers
body = bpy.data.objects["MG01_Body"]
dg = bpy.context.evaluated_depsgraph_get()
ev = body.evaluated_get(dg)
me = ev.to_mesh()
vg = {g.index: g.name for g in body.vertex_groups}
for s in out:
    wt = Vector(out[s]["wrist_tail"])
    kn = [Vector(v) for v in out[s]["knuckles"].values()]
    c = sum(kn, Vector()) / len(kn)
    r = max((k - c).length for k in kn)
    under = []
    for v in me.vertices:
        p = body.matrix_world @ v.co
        if (Vector((p.x, p.y, 0)) - Vector((c.x, c.y, 0))).length < r * .8 and p.z < c.z + .5:
            under.append(p.z)
    out[s]["knuckle_center"] = list(c); out[s]["knuckle_r"] = r
    out[s]["palm_under_z"] = min(under) if under else None
    print(s, "tips z", [round(t[2], 2) for t in out[s]["tips"].values()], "knuckles z", [round(k.z, 2) for k in kn], "center", [round(x, 2) for x in c], "r", round(r, 2), "palm under", round(min(under), 2) if under else None)
json.dump(out, open("/tmp/god/prev/shands.json", "w"), indent=1)
