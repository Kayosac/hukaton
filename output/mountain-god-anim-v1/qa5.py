"""Close-ups of the healed places through the clips (Unity-like: back faces culled, smooth skin, textured)."""
import sys, os
sys.argv = ["clips.py", "/tmp/god/prev/tf_out"]
sys.path.insert(0, "/tmp/god/prev"); sys.path.insert(0, "/home/claude/gm-art/god/anim")
import bpy
from mathutils import Vector
import clips as C
import env
OUT = os.environ.get("QA5", "/tmp/god/prev/qa5")
os.makedirs(OUT, exist_ok=True)
for f in os.listdir(OUT): os.remove(os.path.join(OUT, f))
C.start_reach(); C.fit_dock()
env.textures()
sc, cam = env.scene(tuple(int(x) for x in os.environ.get("RES", "480x360").split("x")))
sc.display.shading.show_backface_culling = True
arm = C.arm
fns = {n: (fn, l) for n, l, fn, lp in C.CLIPS}
def head(b): return arm.matrix_world @ arm.pose.bones[b].head
def tail(b): return arm.matrix_world @ arm.pose.bones[b].tail
# (name, target bone/point fn, camera offsets)
SPOTS = [
    ("chest", lambda: (head("Spine") + tail("Spine")) / 2 + Vector((1.6, -.6, 0)), [(2.5, -4, 1), (5, -1.5, 1.5), (0, -5, -1)]),
    ("chestc", lambda: (head("Spine") + tail("Spine")) / 2 + Vector((.9, -.9, -.3)), [(1.2, -2.2, .4), (2.2, -1.2, .2), (0, -2.4, -.8)]),
    ("palm", lambda: head("C01_wrist_00"), [(0, 0, -3), (2.5, -1, -1.5), (-2.5, -1, 1), (0, 2.5, 1)]),
    ("thighL01", lambda: tail("L01_seg01"), [(0, -4, 1), (2.5, -3, 2), (-2.5, -3, 1)]),
    ("beard", lambda: (head("beard_00") + tail("beard_00")) / 2, [(1, -4, 0), (3, -2.5, 0), (-2, -3, -1)]),
    ("c02hand", lambda: head("C02_wrist_00"), [(1.5, -3, 1), (3, -1, 1), (0, -3, -1.5)]),
    ("a03", lambda: head("A03_elbow"), [(-2, -2.5, 0), (-3, 0, 1), (0, -3, -1)]),
    ("hipL02", lambda: head("L02_seg01"), [(1.5, -3, .5), (3, -1, 0), (0, -3, -1.5)]),
    ("hipL01", lambda: head("L01_seg01"), [(-1.5, -3, .5), (0, -3, -1.5)]),
    ("c01shoulder", lambda: head("C01_shoulder"), [(-2, -2.5, 0), (-3, 0, -1), (-1, -3, -2)]),
]
MOMENTS = [(m.split(":")[0], float(m.split(":")[1])) for m in os.environ.get("MOMENTS", "Idle:0,ReachHold:0,Lift:8,Talk:0").split(",")]
for mn, t in MOMENTS:
    if mn in ("Reach", "ReachHold"):
        C.start_reach()
    C.apply(fns[mn][0](t))
    ONLY = os.environ.get("SPOTS")
    for sn, fn, offs in SPOTS:
        if ONLY and sn not in ONLY.split(","): continue
        c = fn()
        for k, o in enumerate(offs):
            env.look(cam, c + Vector(o), c, 50)
            cam.data.clip_start = .05
            sc.render.filepath = f"{OUT}/{sn}-{mn}_{k}.png"
            bpy.ops.render.render(write_still=True)
    print("QA5", mn, flush=True)
