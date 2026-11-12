"""Skinning matrices of MG01's bones at a few moments of the clips (for picking a pose that opens the fused places)."""
import os
WORK = os.environ.get("REBUILD_WORK", "/tmp/god/rb")

import sys, os, numpy as np
from scipy.spatial.transform import Rotation as Rr, Slerp
sys.argv = ["clips.py", "/tmp/god/prev/tf_out"]
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import bpy
import clips as C
arm = C.arm
fns = {n: fn for n, l, fn, lp in C.CLIPS}
d = np.load(WORK + "/split.npz"); bones = [str(b) for b in d["bones"]]
C.start_reach(); C.fit_dock()
out = {}
for n, t in [("Idle", 0.0), ("Notice", 3.0), ("Reach", 6.0), ("ReachHold", 0.0), ("Lift", 4.0), ("Lift", 8.0), ("Lift", 12.0), ("Talk", 0.0)]:
    if n in ("Reach", "ReachHold"):
        C.start_reach()
    C.apply(fns[n](t))
    bpy.context.view_layer.update()
    S = np.array([np.array(arm.matrix_world @ arm.pose.bones[b].matrix @ arm.data.bones[b].matrix_local.inverted() @ arm.matrix_world.inverted()) for b in bones])
    out[f"{n}_{t:g}"] = S
# TalkL01: Talk, with leg L01 only a third of the way down (all the way, the legs press into each other at the hips)
S = out["Talk_0"]; pi = bones.index("Pelvis"); H = S.copy()
for k, b in enumerate(bones):
    if b.startswith("L01"):
        M = np.linalg.inv(S[pi]) @ S[k]; r = Rr.from_matrix(M[:3, :3]); sl = Slerp([0, 1], Rr.concatenate([Rr.identity(), r]))
        Mi = np.eye(4); Mi[:3, :3] = sl(.35).as_matrix(); Mi[:3, 3] = .35 * M[:3, 3]; H[k] = S[pi] @ Mi
out["TalkL01"] = H
np.savez(WORK + "/frames_S.npz", **out)
print("saved", list(out))
