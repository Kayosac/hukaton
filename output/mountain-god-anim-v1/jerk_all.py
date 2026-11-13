"""Smoothness of the clips: the largest turn of any C01 bone (and the head and chest) between two frames, and how far
the fingertip bone's up (the player's up) tilts on the lip, in the pull and in the talk. CLIPS env picks the clips."""
import sys, os, math
sys.argv = ["clips.py", "/tmp/mg01_clips"]
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from mathutils import Vector
import clips as C
C.start_reach(); C.fit_dock(); C.start_reach()
CH = C.ARM + ["C01_wrist_00", "C01_f0_00", "C01_f0_02", "head_00", "Chest"]
def frame_axes():
    return {b: (C.arm.matrix_world @ C.PB[b].matrix).to_3x3() for b in CH}
C.apply(C.reach_hold(0))
Rtip = (C.arm.matrix_world @ C.PB["C01_f0_02"].matrix).to_3x3()
U = Rtip.inverted() @ Vector((0, 0, 1))
for name in os.environ.get("CLIPS", "Reach,ReachHold,Lift,Talk").split(","):
    fn, L = {n: (f, l) for n, l, f, lp in C.CLIPS}[name]
    prev = None; worst = []; upw = 0
    for f in range(int(round(L * 30)) + 1):
        t = f / 30
        C.apply(fn(t))
        ax = frame_axes()
        if name in ("Lift", "Talk", "ReachHold"):
            up = ax["C01_f0_02"] @ U
            upw = max(upw, math.degrees(up.angle(Vector((0, 0, 1)))))
        if prev:
            for b in CH:
                a = math.degrees(max(ax[b].col[i].angle(prev[b].col[i]) for i in range(3)))
                worst.append((a, t, b))
        prev = ax
    worst.sort(reverse=True)
    print(name, "worst per-frame turn (deg):", [(round(a, 2), round(t, 2), b) for a, t, b in worst[:6]], "finger up tilt max", round(upw, 2))
