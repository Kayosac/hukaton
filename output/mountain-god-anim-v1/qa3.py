import sys, os
sys.argv = ["clips.py", "/tmp/god/prev/tf_out"]
sys.path.insert(0, "/tmp/god/prev"); sys.path.insert(0, "/home/claude/gm-art/god/anim")
import bpy
from mathutils import Vector
import clips as C
import env
OUT = "/tmp/god/prev/qa3"; os.makedirs(OUT, exist_ok=True)
C.start_reach(); C.fit_dock()
env.textures(); env.rocks()
sc, cam = env.scene((800, 450))
sc.display.shading.show_backface_culling = True
fns = {n: fn for n, l, fn, lp in C.CLIPS}
C.start_reach()
for i, (n, t) in enumerate((("Reach", 6.0), ("Reach", 9.0), ("ReachHold", 0), ("Lift", 6.0), ("Lift", 11.0), ("Talk", 0))):
    C.apply(fns[n](t))
    # look at the middle of C01's forearm from its side
    a, b = C.whead("C01_elbow"), C.wtail("C01_wrist_00")
    mid = (a + b) / 2
    d = (b - a)
    side = d.cross(Vector((0, 0, 1))).normalized()
    for k, eye in (("s", mid + side * (d.length * .9) + Vector((0, 0, 2))), ("o", mid - side * (d.length * .9) + Vector((0, 0, 2)))):
        env.look(cam, eye, mid, 55)
        sc.render.filepath = f"{OUT}/{i}_{n}_{t}_{k}.png"
        bpy.ops.render.render(write_still=True)
    # the bend at each joint of C01's chain
    names = ["C01_shoulder", "C01_seg02", "C01_seg03", "C01_seg04", "C01_elbow", "C01_seg06", "C01_seg07", "C01_wrist_00"]
    bends = []
    for p, q in zip(names[:-1], names[1:]):
        u = (C.wtail(p) - C.whead(p)).normalized(); v = (C.wtail(q) - C.whead(q)).normalized()
        bends.append(round(__import__("math").degrees(u.angle(v))))
    print("QA3", n, t, "joint bends", bends, flush=True)
