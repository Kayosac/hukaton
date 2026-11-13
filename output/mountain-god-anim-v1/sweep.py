"""C01's swept volume through Reach, Lift and the start of Talk, in Unity world metres: lines 'clip t x1 y1 z1 x2 y2 z2 r bone'."""
import sys, os, math
sys.argv = ["clips.py", "/tmp/mg01_clips"]
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from mathutils import Vector
import clips as C
C.start_reach(); C.fit_dock(); C.start_reach()
def U(v): return (-3957.6 + 100 * v.x, -400 + 100 * v.z, 1666.3 + 100 * v.y)
R = {b: .62 for b in C.ARM}
R.update({"C01_wrist_00": .85, "C01_f0_00": .24, "C01_f0_01": .20, "C01_f0_02": .165})
out = open(os.environ.get("OUT", "c01_sweep.txt"), "w")
for name, fn, L in (("Reach", C.reach, C.REACH_LEN), ("Lift", C.lift, C.LIFT_LEN)):
    if name == "Reach": C.start_reach()
    t = 0.0
    while t <= L + 1e-6:
        C.apply(fn(t))
        for b, r in R.items():
            a, e = U(C.whead(b)), U(C.wtail(b))
            out.write(f"{name} {t:.2f} {a[0]:.1f} {a[1]:.1f} {a[2]:.1f} {e[0]:.1f} {e[1]:.1f} {e[2]:.1f} {r * 100:.0f} {b}\n")
        if name == "Lift":
            tip = C.wtail("C01_f0_02") + Vector((0, 0, C.G.FINGER_R + .01))
            p = U(tip)
            out.write(f"Player {t:.2f} {p[0]:.1f} {p[1]:.1f} {p[2]:.1f} {p[0]:.1f} {p[1] + 2:.1f} {p[2]:.1f} 3 player\n")
        t += .2
out.close()
print("lines", "written")
