import sys
sys.argv = ["clips.py", "/tmp/god/prev/tf_out"]
sys.path.insert(0, "/tmp/god/prev"); sys.path.insert(0, "/home/claude/gm-art/god/anim")
import godrig as G
G.smooth_body = lambda body, levels=1: None
import bpy
from collections import Counter
import clips as C
C.start_reach(); C.fit_dock()
C.apply(C.reach_hold(0))
dg = bpy.context.evaluated_depsgraph_get()
ob = bpy.data.objects["MG01_Body"]; ev = ob.evaluated_get(dg); me = ev.to_mesh(); rest = ob.data
gi = {vg.index: vg.name for vg in ob.vertex_groups}
bones = set(C.arm.data.bones.keys())
def dom(i):
    return max(((g.weight, gi[g.group]) for g in rest.vertices[i].groups if gi[g.group] in bones), default=(0, "?"))[1]
pairs = Counter(); ex = []
for e in me.edges:
    a, b = e.vertices
    dl = (me.vertices[a].co - me.vertices[b].co).length
    rl = (rest.vertices[a].co - rest.vertices[b].co).length
    if dl > .3 and dl > rl * 2.5:
        k = tuple(sorted((dom(a), dom(b))))
        pairs[k] += 1
        if len(ex) < 10: ex.append((round(dl, 2), round(rl, 2), a, b, k, [round(c, 2) for c in rest.vertices[a].co]))
print("NBASE", len(rest.vertices), "stretched", sum(pairs.values()))
for k, n in pairs.most_common(15): print("PAIR", n, k)
for x in ex: print("EX", x)
