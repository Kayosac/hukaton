"""Plays the MG01 clips in story order (with short crossfades like the game's Animator), runs the hat/beard springs,
renders three views: wide (everything), player at the lip (until the hand waits), rider on the finger (lift, talk).
usage: python3 seq.py <frames dir> [every n-th frame] [max seconds]"""
import sys, os, math
sys.path.insert(0, "/tmp/god/prev"); sys.path.insert(0, "/home/claude/gm-art/god/anim")
import bpy
from mathutils import Vector, Matrix, Quaternion
import env, springs

OUT = sys.argv[1]
EVERY = int(sys.argv[2]) if len(sys.argv) > 2 else 2
MAXS = float(sys.argv[3]) if len(sys.argv) > 3 else 1e9
os.makedirs(OUT, exist_ok=True)
arm = env.open_anim()
env.textures()
env.rocks()
sc, cam = env.scene((640, 360))
PB = arm.pose.bones
W = arm.matrix_world
FPS = 30
SEQ = [("Idle", 2.5), ("Notice", 3.6), ("AlertIdle", 1.5), ("Reach", 11.0), ("ReachHold", 2.5), ("Lift", 12.0), ("Talk", 7.0)]
FADE = .4
acts = {n: bpy.data.actions["MG01_" + n] for n, _ in SEQ}


def sample(name, t):
    act = acts[name]
    n = int(act.frame_range[1])
    f = t * FPS
    f = f % n if name in ("Idle", "AlertIdle", "ReachHold", "Talk") else min(f, n)
    arm.animation_data.action = act
    sc.frame_set(int(f), subframe=f - int(f))
    return {pb.name: pb.matrix_basis.copy() for pb in PB}


def blend(a, b, u):
    out = {}
    for k in a:
        la, qa, sa = a[k].decompose()
        lb, qb, sb = b[k].decompose()
        if qa.dot(qb) < 0:
            qb = -qb
        out[k] = Matrix.LocRotScale(la.lerp(lb, u), qa.slerp(qb, u), sa.lerp(sb, u))
    return out


timeline = []
t0 = 0.0
for n, d in SEQ:
    timeline.append((n, t0, d))
    t0 += d
TOTAL = min(t0, MAXS)
sp = springs.Springs(arm)
HEAD = Vector((1.387, 2.578, 16.057))
look_c = None
frame = 0
prev_last = None
for i in range(int(TOTAL * FPS)):
    t = i / FPS
    k = max(j for j, (n, s, d) in enumerate(timeline) if s <= t + 1e-6)
    n, s, d = timeline[k]
    pose = sample(n, t - s)
    if k > 0 and t - s < FADE:
        pn, ps, pd = timeline[k - 1]
        prev = sample(pn, t - ps)
        u = (t - s) / FADE
        pose = blend(prev, pose, u * u * (3 - 2 * u))
    arm.animation_data.action = None
    for pb in PB:
        pb.matrix_basis = pose[pb.name]
    bpy.context.view_layer.update()
    sp.step(1 / FPS)
    if i % EVERY:
        continue
    views = [("wide", (Vector((-24, -31, 13)), Vector((0, -5, 7)), 48))]
    if n in ("Idle", "Notice", "AlertIdle", "Reach", "ReachHold"):
        u = 0.0 if n in ("Idle", "Notice", "AlertIdle") else 1.0 if n == "ReachHold" else min(1.0, max(0.0, (t - s - 3) / 5))
        u = u * u * (3 - 2 * u)
        views.append(("player", (env.LIP + Vector((0, -.03, .017)), Vector((.6, 2, 10.5)).lerp(Vector((.3, 0, 6.5)), u), 70)))
    if n in ("Idle", "Notice", "AlertIdle", "Reach", "ReachHold"):
        views.append(("body", (Vector((3, -20, 5.5)), Vector((1.3, 1, 7.6)), 50)))
    if n in ("Lift", "Talk"):
        tip = PB["C01_f0_02"]
        tw, hw = W @ tip.tail, W @ tip.head
        fd = (tw - hw).normalized()
        spot = tw - fd * .08 + Vector((0, 0, .2))
        tgt = HEAD + Vector((0, 0, -.4))
        look_c = tgt if look_c is None else look_c.lerp(tgt, .15)
        views.append(("rider", (spot + Vector((0, 0, .017)), look_c, 65)))
    for v, (eye, tgt, fov) in views:
        env.look(cam, eye, tgt, fov)
        sc.render.filepath = os.path.join(OUT, f"{v}_{frame:04d}.png")
        bpy.ops.render.render(write_still=True)
    frame += 1
print("frames", frame)
