"""MG01 clips: Idle, Notice, AlertIdle, Reach, ReachHold, Lift, Talk — procedural keys over IK (support hands pinned to
their rocks, C02 holding its thigh, C01 driven through T_C01), baked frame by frame into plain bone keys.
usage: python3 clips.py <out dir> [--preview]"""
import sys, os, math, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from mathutils import Vector, Matrix, Quaternion
import godrig as G
from godrig import smooth, smoother, settle

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
arm = G.setup()
T = G.rig(arm)
vl = bpy.context.view_layer
FPS = G.FPS
PB = arm.pose.bones


def wtail(b): return arm.matrix_world @ PB[b].tail
def whead(b): return arm.matrix_world @ PB[b].head


# ------------------------------------------------------------------ finger poses (computed once in rest)
def uncurl(b, along):
    """Local rotation that turns bone b to point along `along` (world), in the current pose."""
    vl.update()
    cur = (wtail(b) - whead(b)).normalized()
    q = cur.rotation_difference(along)
    mw = (arm.matrix_world @ PB[b].matrix).to_quaternion()
    return mw.inverted() @ q @ mw


FINGERS = {f: [f"C01_{f}_0{i}" for i in range(3)] for f in ("f0", "f1", "f2", "f3", "thumb")}
STRAIGHT = {}
d0 = (wtail("C01_f0_00") - whead("C01_f0_00")).normalized()
for b in FINGERS["f0"][1:]:
    STRAIGHT[b] = uncurl(b, d0)
    PB[b].rotation_quaternion = STRAIGHT[b]
vl.update()
# curl the others a bit more: the opposite of straightening each segment along its finger's base, 45 %
CURL = {}
for f in ("f1", "f2", "f3", "thumb"):
    base = (wtail(FINGERS[f][0]) - whead(FINGERS[f][0])).normalized()
    for b in FINGERS[f][1:]:
        CURL[b] = uncurl(b, base).inverted()
tip_rest = wtail("C01_f0_02")
wrist_rest = G.tail_matrix(arm, "C01_wrist_00")
f_rest = (tip_rest - whead("C01_f0_00")).normalized()
w_rest = (whead("C01_wrist_00") - wtail("C01_wrist_00")).normalized()
for b in STRAIGHT:
    PB[b].rotation_quaternion = Quaternion()
vl.update()


def frame(f, w):
    x = f.normalized(); y = (w - x * w.dot(x)).normalized(); z = x.cross(y)
    return Matrix((x, y, z)).transposed()


def hand_matrix(tip, fdir, wdir):
    """IK target for C01's wrist so that the straightened index fingertip lands on `tip`, pointing along fdir, with
    the forearm toward wdir."""
    R = (frame(fdir, wdir) @ frame(f_rest, w_rest).inverted()).to_quaternion()
    pos = tip - R @ (tip_rest - wrist_rest.translation)
    return Matrix.Translation(pos) @ (R @ wrist_rest.to_quaternion()).to_matrix().to_4x4()


# Dock: the fingertip's end a little under the platform's lip, the finger's top 1 m under it, pointing at the lip.
DOCK_TIP = G.LIP + Vector((0, -.05, -.01 - G.FINGER_R))
M_DOCK = hand_matrix(DOCK_TIP, Vector((0, -1, 0)), Vector((0, 1, .6)))
# Where the player stands on the finger: 8 m back from its end, on top.
SPOT_BACK = .08
HEAD = (whead("head_00") + wtail("head_00")) * .5
FACE_SPOT = Vector((HEAD.x - .1, HEAD.y - 3.2, HEAD.z - .2))  # in front of the face (tests will tune it)
FACE_TIP = FACE_SPOT + Vector((SPOT_BACK, 0, -G.FINGER_R))
M_FACE = hand_matrix(FACE_TIP, Vector((1, 0, 0)), Vector((-1, .5, .5)))


def spot(m_target):
    """The player's spot for a C01 target matrix (top of the finger, 8 m from its end)."""
    R = m_target.to_quaternion() @ wrist_rest.to_quaternion().inverted()
    tip = m_target.translation + R @ (tip_rest - wrist_rest.translation)
    fdir = R @ f_rest
    return tip - fdir * SPOT_BACK + Vector((0, 0, G.FINGER_R))


def mlerp(a, b, t):
    return Matrix.Translation(a.translation.lerp(b.translation, t)) @ a.to_quaternion().slerp(b.to_quaternion(), t).to_matrix().to_4x4()


def bezier(p0, p1, p2, p3, u):
    v = 1 - u
    return p0 * v ** 3 + p1 * 3 * v * v * u + p2 * 3 * v * u * u + p3 * u ** 3


# ------------------------------------------------------------------ pose layers
X, Y, Z = (1, 0, 0), (0, 1, 0), (0, 0, 1)


class Pose:
    """Everything a clip sets in one frame, as plain numbers (degrees, units)."""
    def __init__(self):
        self.rise = 0.0         # pelvis up (units)
        self.lean = 0.0         # whole torso forward (deg): pelvis 0.33, spine 0.42, chest 0.25
        self.roll = 0.0         # torso toward C01's side (deg)
        self.breath = 0.0       # -1..1
        self.look = None        # world point the head turns to
        self.look_w = 0.0
        self.c01 = 0.0          # 0: on the thigh, 1: on `hand`
        self.hand = None        # C01 target matrix when free
        self.fingers = 0.0      # 0 rest, 1 pointing
        self.sway = 0.0         # time for the small arms' sway (s)
        self.sway_amp = 1.0
        self.nod = 0.0          # head nod (deg)
        self.tilt = 0.0         # head tilt (deg)
        self.wiggle = 0.0       # other fingers of C01 moving (0..1)


def apply(p):
    for pb in PB:
        pb.rotation_quaternion = Quaternion()
        pb.location = (0, 0, 0)
    b = p.breath
    G.move(arm, "Pelvis", (0, 0, p.rise + .018 * b))
    G.set_rot(arm, "Pelvis", (X, p.lean * .33), (Y, -p.roll * .4))
    G.set_rot(arm, "Spine", (X, p.lean * .42 - .5 * b), (Y, -p.roll * .35))
    G.set_rot(arm, "Chest", (X, p.lean * .25 - .9 * b), (Y, -p.roll * .25))
    G.set_rot(arm, "Neck", (X, .45 * b))
    # small arms and legs: slow sway, each its own phase (loop-safe: whole cycles in 12 s)
    w = 2 * math.pi / 12.0
    s = p.sway
    a = p.sway_amp
    for i, n in enumerate(("A01", "A02", "A03", "A04")):
        ph = i * 1.7
        G.set_rot(arm, n + "_shoulder", (X, a * 2.2 * math.sin(w * s + ph)), (Z, a * 1.6 * math.sin(2 * w * s + ph * .7)))
        G.set_rot(arm, n + "_elbow", (X, a * 3.0 * math.sin(w * s + ph + 1.1)))
        for f in ("f0", "f1", "f2", "f3"):
            for k in range(3):
                bn = f"{n}_{f}_0{k}"
                if bn in PB:
                    G.set_rot(arm, bn, (X, a * 4 * math.sin(2 * w * s + ph + k * .6 + int(f[1]) * .4)))
    for i, n in enumerate(("L02", "L03", "L04")):
        G.set_rot(arm, n + "_foot_00", (X, 2.5 * math.sin(w * s + i * 2.1)))
    # C01 fingers: index straightens, the rest curl in; the free fingers wiggle a little while waiting
    for bn, q in STRAIGHT.items():
        PB[bn].rotation_quaternion = Quaternion().slerp(q, p.fingers)
    for bn, q in CURL.items():
        extra = .12 * p.wiggle * math.sin(2 * w * s * 3 + hash(bn) % 7)
        PB[bn].rotation_quaternion = Quaternion().slerp(q, max(0.0, p.fingers * .45 + extra))
    vl.update()
    # head: look and talk
    if p.look is not None and p.look_w > 0:
        for bn, share in (("Neck", .4), ("head_00", .6)):
            vl.update()
            cur = (arm.matrix_world @ PB[bn].matrix).to_quaternion()
            fwd = cur @ Vector((0, 0, 1))  # bone z: roughly the face's forward for this rig? use world -Y instead
            hpos = whead(bn)
            want = (p.look - hpos).normalized()
            q = Vector((0, -1, 0)).rotation_difference(want)
            q = Quaternion().slerp(q, p.look_w * share)
            PB[bn].rotation_quaternion = cur.inverted() @ q @ cur @ PB[bn].rotation_quaternion
    if p.nod or p.tilt:
        vl.update()
        q = G.rot(arm, "head_00", X, p.nod) @ G.rot(arm, "head_00", Y, p.tilt)
        PB["head_00"].rotation_quaternion = q @ PB["head_00"].rotation_quaternion
    vl.update()
    # C01: from the thigh (an empty riding it) to its own target
    m_th = T["C01_thigh"].matrix_world.copy()
    T["C01"].matrix_world = m_th if p.c01 <= 0 or p.hand is None else mlerp(m_th, p.hand, min(1.0, p.c01))
    vl.update()


# ------------------------------------------------------------------ clips
BREATH = 6.0  # s


def breath(t):
    return math.sin(2 * math.pi * t / BREATH)


ALERT = dict(rise=.22, lean=-1.5)
REACH_END = dict(rise=-.35, lean=12.0, roll=2.5)


def idle(t):
    p = Pose(); p.breath = breath(t); p.sway = t
    return p


def notice(t):
    p = Pose(); p.breath = breath(t); p.sway = t
    u = settle(t / 2.4, overshoot=.1)
    p.rise = ALERT["rise"] * u; p.lean = ALERT["lean"] * u
    p.look = DOCK_TIP; p.look_w = .55 * smooth(t / 1.6)
    p.sway_amp = 1 + .4 * smooth(t / 2)
    return p


def alert_idle(t):
    p = Pose(); p.breath = breath(t); p.sway = t + 3.0
    p.rise = ALERT["rise"]; p.lean = ALERT["lean"]; p.look = DOCK_TIP; p.look_w = .55; p.sway_amp = 1.4
    return p


REACH_LEN = 9.0


def reach(t):
    p = Pose(); p.breath = breath(t) * .6; p.sway = t + 6.0; p.sway_amp = 1.2
    # anticipation: the palm lifts off the thigh, the chest pulls back
    a = smooth(t / 1.2) - smooth((t - 1.2) / 1.0)
    # the arm: unwinds out to the left, comes in from above, settles at the lip with a small overshoot
    u = settle((t - 1.0) / 6.6, overshoot=.035, wobbles=1.3)
    # losing the cradle: pelvis and legs drop and bounce, the torso leans after the arm
    drop = settle((t - 1.6) / 2.6, overshoot=.18, wobbles=1.8)
    lean = smoother((t - 1.3) / 5.5)
    p.rise = ALERT["rise"] * (1 - smooth((t - 1.4) / 1.2)) + REACH_END["rise"] * drop
    p.lean = ALERT["lean"] * (1 - lean) + REACH_END["lean"] * lean - 2.0 * a
    p.roll = REACH_END["roll"] * lean
    p.c01 = min(1.0, smooth(t / .8) * 1.0)
    vl.update()
    p.hand = reach_path(u, a)
    p.fingers = smooth((t - 3.0) / 4.0)
    p.look = DOCK_TIP + Vector((0, 2.0 * (1 - smooth((t - 2) / 5)), 1.5 * (1 - smooth((t - 2) / 5)))); p.look_w = .6
    return p


_reach_start = {}


def reach_path(u, a):
    m0 = _reach_start["m"]
    p0 = m0.translation + Vector((0, 0, .5 * a))
    p3 = M_DOCK.translation
    p1 = p0 + Vector((-3.2, -3.5, 2.2))
    p2 = p3 + Vector((-.6, 3.8, 3.4))
    pos = bezier(p0, p1, p2, p3, min(max(u, 0), 1.05)) if u <= 1 else p3 + (p3 - bezier(p0, p1, p2, p3, .985)) * (u - 1) * 30
    rot = m0.to_quaternion().slerp(M_DOCK.to_quaternion(), smoother(min(1.0, max(0.0, u * 1.15))))
    return Matrix.Translation(pos) @ rot.to_matrix().to_4x4()


def reach_hold(t):
    p = Pose(); p.breath = breath(t) * .7; p.sway = t + 15.0; p.sway_amp = 1.1
    p.rise = REACH_END["rise"]; p.lean = REACH_END["lean"]; p.roll = REACH_END["roll"]
    p.c01 = 1; p.hand = M_DOCK; p.fingers = 1; p.wiggle = 1
    p.look = DOCK_TIP; p.look_w = .6
    return p


LIFT_LEN = 12.0


def lift(t):
    p = Pose(); p.breath = breath(t) * .7; p.sway = t + 23.0; p.sway_amp = 1.0
    u = settle(t / 10.5, overshoot=.02, wobbles=1.2)
    up = smooth(t / 3.0)
    p.rise = REACH_END["rise"] + (.05 - REACH_END["rise"]) * smoother(t / 9)
    p.lean = REACH_END["lean"] + (2.0 - REACH_END["lean"]) * smoother((t - 1) / 9)
    p.roll = REACH_END["roll"] * (1 - smoother(t / 9))
    p.c01 = 1; p.fingers = 1
    p0, p3 = M_DOCK.translation, M_FACE.translation
    p1 = p0 + Vector((0, 0, 4.0))
    p2 = p3 + Vector((-4.0, -3.0, -.5))
    uu = min(max(u, 0), 1)
    pos = bezier(p0, p1, p2, p3, uu) + Vector((0, 0, .05 * math.sin(math.pi * up)))
    rot = M_DOCK.to_quaternion().slerp(M_FACE.to_quaternion(), smoother(uu))
    p.hand = Matrix.Translation(pos) @ rot.to_matrix().to_4x4()
    p.look = spot(p.hand); p.look_w = .7
    return p


def talk(t):
    p = Pose(); p.breath = breath(t); p.sway = t + 35.0; p.sway_amp = 1.6
    p.rise = .05; p.lean = 2.0; p.c01 = 1; p.fingers = 1
    bob = .003 * breath(t)
    p.hand = Matrix.Translation(M_FACE.translation + Vector((0, 0, bob))) @ M_FACE.to_quaternion().to_matrix().to_4x4()
    p.look = spot(M_FACE); p.look_w = .75
    w = 2 * math.pi / 12.0
    p.nod = 3.2 * math.sin(3 * w * t) * (.6 + .4 * math.sin(w * t)) + 1.5 * math.sin(5 * w * t + 1)
    p.tilt = 2.5 * math.sin(w * t + .5) + 1.2 * math.sin(4 * w * t)
    return p


CLIPS = [("Idle", 12.0, idle, True), ("Notice", 3.0, notice, False), ("AlertIdle", 12.0, alert_idle, True),
         ("Reach", REACH_LEN, reach, False), ("ReachHold", 12.0, reach_hold, True), ("Lift", LIFT_LEN, lift, False),
         ("Talk", 12.0, talk, True)]


# ------------------------------------------------------------------ bake
def bake(name, length, fn):
    frames = int(round(length * FPS))
    keys = {pb.name: [] for pb in PB}
    for f in range(frames + 1):
        t = f / FPS
        if name == "Reach" and f == 0:
            apply(alert_idle(0.0))
            _reach_start["m"] = T["C01_thigh"].matrix_world.copy()
        apply(fn(t))
        for pb in PB:
            m = arm.convert_space(pose_bone=pb, matrix=pb.matrix, from_space="POSE", to_space="LOCAL")
            keys[pb.name].append(m.copy())
    act = bpy.data.actions.new("MG01_" + name)
    act.use_fake_user = True
    return act, keys, frames


def write_action(act, keys, frames):
    arm.animation_data_create()
    arm.animation_data.action = act
    for bn, ms in keys.items():
        pb = PB[bn]
        loc_needed = any(m.to_translation().length > 1e-5 for m in ms)
        prev = None
        for f, m in enumerate(ms):
            l, q, _ = m.decompose()
            if prev is not None and prev.dot(q) < 0:
                q = -q
            prev = q
            pb.rotation_quaternion = q
            pb.keyframe_insert("rotation_quaternion", frame=f)
            if loc_needed:
                pb.location = l
                pb.keyframe_insert("location", frame=f)
    arm.animation_data.action = None


if __name__ == "__main__":
    report = {}
    baked = {}
    for name, length, fn, loop in CLIPS:
        act, keys, frames = bake(name, length, fn)
        baked[name] = (act, keys, frames)
        tipd = None
        report[name] = dict(seconds=length, frames=frames + 1, loop=loop)
        print("baked", name, frames + 1, "frames")
    # positions to report
    apply(reach_hold(0)); report["dock_tip"] = list(wtail("C01_f0_02")); report["dock_spot"] = list(spot(M_DOCK))
    apply(talk(0)); report["face_spot"] = list(spot(M_FACE)); report["face_tip"] = list(wtail("C01_f0_02")); report["head"] = list(HEAD)
    report["dock_miss"] = (Vector(report["dock_tip"]) - DOCK_TIP).length
    json.dump(report, open(os.path.join(OUT, "clips.json"), "w"), indent=1)
    import pickle
    pickle.dump({n: ([[list(r) for r in m] for m in []], fr) for n, (a, k, fr) in baked.items()}, open(os.path.join(OUT, "frames.pkl"), "wb"))
    # mute IK, write the actions
    for pb in PB:
        for c in pb.constraints:
            c.mute = True
    for name, (act, keys, frames) in baked.items():
        write_action(act, keys, frames)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "MG01_anim.blend"))
    print(json.dumps(report, indent=1))
