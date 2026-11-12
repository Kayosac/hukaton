"""QA every second of every clip, as Unity draws it (back faces culled, smooth skin): three views per moment."""
import sys, os
sys.argv = ["clips.py", "/tmp/god/prev/tf_out"]
sys.path.insert(0, "/tmp/god/prev"); sys.path.insert(0, "/home/claude/gm-art/god/anim")
import bpy
from mathutils import Vector
import clips as C
import env
OUT = sys.argv[-1] if len(sys.argv) > 1 and sys.argv[-1].startswith("/") and "qa" in sys.argv[-1] else "/tmp/god/prev/qa2"
OUT = "/tmp/god/prev/qa2"
os.makedirs(OUT, exist_ok=True)
C.start_reach(); C.fit_dock()
env.textures(); env.rocks()
sc, cam = env.scene((640, 360))
sc.display.shading.show_backface_culling = True
fns = {n: (fn, l) for n, l, fn, lp in C.CLIPS}
times = [("Idle", t) for t in (0, 6)] + [("Notice", t) for t in (1, 2, 3)] + [("Reach", float(t)) for t in range(0, 12)] + \
        [("ReachHold", 0)] + [("Lift", float(t)) for t in range(0, 17, 2)] + [("Talk", t) for t in (0, 4)]
E = C.G.LIP + Vector((0, -.03, .017))
VIEWS = {"lip": (E, Vector((.6, 2.5, 11.0)), 70), "side": (Vector((-22, -12, 9)), Vector((0, -3, 8)), 50),
         "front": (Vector((4, -9, 10)), Vector((1, 2, 11)), 60)}
for i, (n, t) in enumerate(times):
    if n == "Reach" and t == 0:
        C.start_reach()
    C.apply(fns[n][0](t))
    for k, (eye, tgt, fov) in VIEWS.items():
        env.look(cam, eye, tgt, fov)
        sc.render.filepath = f"{OUT}/{k}_{i:02d}_{n}_{t:04.1f}.png"
        bpy.ops.render.render(write_still=True)
    print("QA", i, n, t, flush=True)
