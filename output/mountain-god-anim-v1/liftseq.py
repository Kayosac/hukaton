"""Contact sheet of the Lift: a wide view from the lip side and the view from the finger, every 2 s."""
import sys, os, math
sys.argv = ["clips.py", "/tmp/god/prev/tf_out"]
sys.path.insert(0, "/tmp/god/prev"); sys.path.insert(0, "/home/claude/gm-art/god/anim")
import bpy
from mathutils import Vector
import clips as C
import env
C.start_reach(); C.fit_dock()
env.textures()
sc, cam = env.scene((480, 300))
sc.display.shading.show_backface_culling = True
out = sys.argv[-1] if len(sys.argv) > 1 and sys.argv[-1].startswith("/") else "/tmp/god/rb/liftseq"
out = os.environ.get("OUT", "/tmp/god/rb/liftseq")
os.makedirs(out, exist_ok=True)
for f in os.listdir(out): os.remove(os.path.join(out, f))
clip = os.environ.get("CLIP", "Lift"); step = float(os.environ.get("STEP", "2"))
fns = {n: (fn, l) for n, l, fn, lp in C.CLIPS}
fn, L = fns[clip]
t = 0.0; k = 0
while t <= L + 1e-6:
    if clip in ("Reach",): C.start_reach()
    C.apply(fn(t))
    tip = C.wtail("C01_f0_02")
    env.look(cam, Vector((30, -40, 16)), Vector((-2, -6, 6)), 62); cam.data.clip_start = .1
    sc.render.filepath = f"{out}/{k:02d}_a.png"; bpy.ops.render.render(write_still=True)
    fd = (C.wtail("C01_f0_02") - C.whead("C01_f0_02")).normalized()
    eye = tip - fd * C.SPOT_BACK + Vector((0, 0, C.G.FINGER_R + .02))
    head = (C.whead("head_00") + C.wtail("head_00")) / 2
    env.look(cam, eye, head, 35)
    cam.data.sensor_fit = 'VERTICAL'; cam.data.angle = math.radians(70); cam.data.clip_start = .003
    sc.render.filepath = f"{out}/{k:02d}_b.png"; bpy.ops.render.render(write_still=True)
    cam.data.sensor_fit = 'AUTO'
    t += step; k += 1
print("frames", k)
