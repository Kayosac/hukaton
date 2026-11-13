"""MG01 clips: Idle, Notice, AlertIdle, Reach, ReachHold, Lift, Talk — procedural keys over IK (support hands pinned to
their rocks; C01 and C02 driven through world targets that start on the thighs), baked frame by frame into plain bone
keys.

The story: he sits deep in his own hands (C01 under thigh L01 is the cradle, C02 hugs L02's knee); seeing the player he
stands up on his support arms; to reach he first lets go of himself — both hands slide down off the thighs — his two
ordinary legs drop and straighten under him, the torso sags with the weight and bounces; then C01 swings forward,
clear of the legs, and lays its index finger under the lip.
usage: python3 clips.py <out dir>"""
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
IK = {b: next(c for c in PB[b].constraints if c.type == 'IK') for b in ("C01_wrist_00", "C02_wrist_00")}


def wtail(b): return arm.matrix_world @ PB[b].tail
def whead(b): return arm.matrix_world @ PB[b].head


def turn_to(b, vec_fn, along):
    """Local rotation that turns the world vector vec_fn() (measured on bone b's posed chain) to `along`."""
    vl.update()
    q = vec_fn().normalized().rotation_difference(along.normalized())
    mw = (arm.matrix_world @ PB[b].matrix).to_quaternion()
    return mw.inverted() @ q @ mw


def qpow(q, t):
    """q to the power t (t > 1 overshoots)."""
    axis, ang = q.to_axis_angle()
    return Quaternion(axis, ang * t)


# ------------------------------------------------------------------ finger poses (computed once in rest)
FINGERS = {f: [f"C01_{f}_0{i}" for i in range(3)] for f in ("f0", "f1", "f2", "f3", "thumb")}
STRAIGHT = {}
d0 = (wtail("C01_f0_00") - whead("C01_f0_00")).normalized()
for b in FINGERS["f0"][1:]:
    STRAIGHT[b] = turn_to(b, lambda b=b: wtail(b) - whead(b), d0)
    PB[b].rotation_quaternion = STRAIGHT[b]
vl.update()
CURL = {}
for f in ("f1", "f2", "f3", "thumb"):
    base = (wtail(FINGERS[f][0]) - whead(FINGERS[f][0])).normalized()
    for b in FINGERS[f][1:]:
        CURL[b] = turn_to(b, lambda b=b: wtail(b) - whead(b), base).inverted()
tip_rest = wtail("C01_f0_02")
wrist_rest = G.tail_matrix(arm, "C01_wrist_00")
f_rest = (tip_rest - whead("C01_f0_00")).normalized()
FINGER_LEN = (tip_rest - whead("C01_f0_00")).length
w_rest = (whead("C01_wrist_00") - wtail("C01_wrist_00")).normalized()
for b in STRAIGHT:
    PB[b].rotation_quaternion = Quaternion()
vl.update()

# ------------------------------------------------------------------ legs L01, L02 dropping straight (computed in rest)
# Straight down under the hip, a little forward and out; L02 is long, so it keeps a soft knee and swings out more
# (its foot stays well above the rock under S03).
LEG_DIR = {"L01": Vector((.3, -.32, -1)), "L02": Vector((.42, -.22, -1))}
KNEE_BEND = {"L01": 4.0, "L02": 14.0}
LEGS = {}
for L, d in LEG_DIR.items():
    hip, knee, foot = L + "_hip", L + "_knee", L + "_foot_00"
    LEGS[hip] = turn_to(hip, lambda: whead(knee) - whead(hip), d)
    PB[hip].rotation_quaternion = LEGS[hip]
    vl.update()
    side = d.cross(Vector((1, 0, 0))).normalized()
    shin = Quaternion(side, math.radians(-KNEE_BEND[L])) @ d.normalized()
    LEGS[knee] = turn_to(knee, lambda: whead(foot) - whead(knee), shin)
    PB[knee].rotation_quaternion = LEGS[knee]
    vl.update()
    print("leg", L, "foot at", [round(c, 2) for c in wtail(L + "_foot_01")])
for b in LEGS:
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


SPOT_BACK = .08
HEAD = (whead("head_00") + wtail("head_00")) * .5


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
        self.breath_amp = 1.0
        self.look = None        # world point the head turns to
        self.look_w = 0.0
        self.c01 = 0.0          # 0: on the thigh, 1: on `hand`
        self.hand = None
        self.c01_orient = 1.0   # how strictly the wrist follows the target's rotation (low: the hand trails the arm)
        self.c02 = 0.0
        self.hand2 = None
        self.c02_orient = 1.0
        self.fingers = 0.0      # 0 rest, 1 pointing
        self.sway = 0.0         # time for the small arms' sway (s)
        self.sway_amp = 1.0
        self.nod = 0.0
        self.tilt = 0.0
        self.wiggle = 0.0
        self.leg_hip = 0.0      # 0 folded (rest), 1 hanging straight; > 1 swings past
        self.leg_knee = 0.0
        self.leg_swing = 0.0    # pendulum sway of the hanging legs (deg)
        self.point_dir = None   # world direction the index finger is bent toward at its knuckle (the wrist stays
        self.point_w = 0.0      # in line with the forearm; the finger does the aiming)


def apply(p):
    for pb in PB:
        pb.rotation_quaternion = Quaternion()
        pb.location = (0, 0, 0)
    b = p.breath * p.breath_amp
    G.move(arm, "Pelvis", (0, 0, p.rise + .03 * b))
    G.set_rot(arm, "Pelvis", (X, p.lean * .33), (Y, -p.roll * .4))
    G.set_rot(arm, "Spine", (X, p.lean * .42 - .8 * b), (Y, -p.roll * .35))
    G.set_rot(arm, "Chest", (X, p.lean * .25 - 1.4 * b), (Y, -p.roll * .25))
    G.set_rot(arm, "Neck", (X, .7 * b))
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
    for i, n in enumerate(("L03", "L04")):
        G.set_rot(arm, n + "_foot_00", (X, 2.5 * math.sin(w * s + i * 2.1)))
        G.set_rot(arm, n + "_knee", (X, 1.2 * math.sin(w * s + i * 1.3 + .8)))
    # the two ordinary legs: folded in the cradle, or dropped and hanging, swinging a little
    for i, L in enumerate(("L01", "L02")):
        hip, knee = L + "_hip", L + "_knee"
        sw = p.leg_swing * math.sin(w * 2 * s + i * 1.9)
        PB[hip].rotation_quaternion = G.rot(arm, hip, X, sw) @ qpow(LEGS[hip], p.leg_hip)
        PB[knee].rotation_quaternion = G.rot(arm, knee, X, .6 * sw) @ qpow(LEGS[knee], p.leg_knee)
    for bn, q in STRAIGHT.items():
        PB[bn].rotation_quaternion = Quaternion().slerp(q, p.fingers)
    for bn, q in CURL.items():
        extra = .12 * p.wiggle * math.sin(2 * w * s * 3 + hash(bn) % 7)
        PB[bn].rotation_quaternion = Quaternion().slerp(q, max(0.0, p.fingers * .45 + extra))
    vl.update()
    if p.look is not None and p.look_w > 0:
        for bn, share in (("Neck", .4), ("head_00", .6)):
            vl.update()
            cur = (arm.matrix_world @ PB[bn].matrix).to_quaternion()
            want = (p.look - whead(bn)).normalized()
            q = Quaternion().slerp(Vector((0, -1, 0)).rotation_difference(want), p.look_w * share)
            PB[bn].rotation_quaternion = cur.inverted() @ q @ cur @ PB[bn].rotation_quaternion
    if p.nod or p.tilt:
        vl.update()
        q = G.rot(arm, "head_00", X, p.nod) @ G.rot(arm, "head_00", Y, p.tilt)
        PB["head_00"].rotation_quaternion = q @ PB["head_00"].rotation_quaternion
    vl.update()
    # the hands: from the thigh helpers (riding the thighs) to their own targets
    for key, c, hand, orient, bone in (("C01", p.c01, p.hand, p.c01_orient, "C01_wrist_00"),
                                       ("C02", p.c02, p.hand2, p.c02_orient, "C02_wrist_00")):
        m_th = T[key + "_thigh"].matrix_world.copy()
        T[key].matrix_world = m_th if c <= 0 or hand is None else mlerp(m_th, hand, min(1.0, c))
        IK[bone].orient_weight = max(.02, min(1.0, orient))
    vl.update()
    if p.point_dir is not None and p.point_w > 0:
        q = turn_to("C01_f0_00", lambda: wtail("C01_f0_02") - whead("C01_f0_00"), p.point_dir)
        PB["C01_f0_00"].rotation_quaternion = Quaternion().slerp(q, min(1.0, p.point_w))
        vl.update()


# ------------------------------------------------------------------ clips
BREATH = 6.0


def breath(t):
    return math.sin(2 * math.pi * t / BREATH)


SIT = dict(rise=-1.25, lean=8.0)    # deep down in the cradle, hunched over
STAND = dict(rise=.56, lean=-2.5)   # up on the support arms, chest out
SAG = .38                            # after letting go: the legs' weight pulls him down
REACH_END = dict(rise=.05, lean=13.0, roll=2.5)
HANG = Vector((0, 0, 0))


def idle(t):
    p = Pose(); p.breath = breath(t); p.breath_amp = 1.2; p.sway = t
    p.rise = SIT["rise"]; p.lean = SIT["lean"]
    return p


NOTICE_LEN = 4.4


def notice(t):
    p = Pose(); p.breath = breath(t); p.sway = t
    # a short dip to push off, then up with a heavy settle
    dip = .1 * (smooth(t / .4) - smooth((t - .4) / .45))
    u = settle((t - .35) / 3.3, overshoot=.06, wobbles=1.5)
    p.rise = SIT["rise"] + (STAND["rise"] - SIT["rise"]) * u - dip
    p.lean = SIT["lean"] + (STAND["lean"] - SIT["lean"]) * smoother((t - .25) / 3.0)
    p.look = DOCK_TIP; p.look_w = .55 * smooth((t - .5) / 2.0)
    p.sway_amp = 1 + .4 * smooth(t / 2.4)
    return p


def alert_idle(t):
    p = Pose(); p.breath = breath(t); p.sway = t + NOTICE_LEN
    p.rise = STAND["rise"]; p.lean = STAND["lean"]; p.look = DOCK_TIP; p.look_w = .55; p.sway_amp = 1.4
    return p


REACH_LEN = 11.0
_start = {}


def let_go_targets():
    """Release points (world), from the hands' grips at the start of the reach: C01 slides down and out to the left,
    clear of the leg that is about to drop; C02 slides down and out to the right."""
    m1 = T["C01_thigh"].matrix_world.copy()
    m2 = T["C02_thigh"].matrix_world.copy()
    r1 = Matrix.Translation(m1.translation + Vector((-2.4, -1.2, -1.2))) @ (Quaternion((0, 1, 0), math.radians(-14)) @ m1.to_quaternion()).to_matrix().to_4x4()
    r2 = Matrix.Translation(m2.translation + Vector((1.5, -.5, -1.9))) @ (Quaternion((0, 1, 0), math.radians(18)) @ m2.to_quaternion()).to_matrix().to_4x4()
    pel = arm.matrix_world @ PB["Pelvis"].matrix
    return m1, r1, pel.inverted() @ r2


def c02_hang(rel2, t, amp=1.0):
    """C02 after letting go: hangs from the body (moves with the pelvis), drifting a little."""
    pel = arm.matrix_world @ PB["Pelvis"].matrix
    m = pel @ rel2
    d = Vector((.08 * math.sin(t * .9), .06 * math.sin(t * .7 + 1), .05 * math.sin(t * 1.1 + 2))) * amp
    return Matrix.Translation(m.translation + d) @ m.to_quaternion().to_matrix().to_4x4()


def reach(t):
    p = Pose(); p.breath = breath(t) * .6; p.sway = t + 20.0; p.sway_amp = 1.2
    m1, r1, rel2 = _start["m1"], _start["r1"], _start["rel2"]
    # 1. letting go: a short squeeze-and-lift, then both hands slide down off the thighs
    lift = .05 * (smooth(t / .4) - smooth((t - .4) / .5))
    down = smoother((t - .3) / 1.1)
    # 2. the legs drop and straighten (hip first, the knee follows), swinging past and back
    p.leg_hip = settle((t - 1.0) / 1.5, overshoot=.16, wobbles=1.8)
    p.leg_knee = settle((t - 1.25) / 1.45, overshoot=.22, wobbles=1.9)
    p.leg_swing = 1.6 * smooth((t - 3) / 2)
    # 3. the torso sags under the weight and bounces, then leans and lowers into the reach
    sag = settle((t - 1.15) / 1.9, overshoot=.3, wobbles=1.7)
    lean_in = smoother((t - 3.0) / 6.2)
    p.rise = STAND["rise"] + lift + (SAG - STAND["rise"]) * sag + (REACH_END["rise"] - SAG) * lean_in
    p.lean = STAND["lean"] + (REACH_END["lean"] - STAND["lean"]) * lean_in - 1.5 * smooth(t / .4) + 1.5 * smooth((t - .4) / .6)
    p.roll = REACH_END["roll"] * lean_in
    # C02: let go, hang
    p.c02 = down
    p.hand2 = c02_hang(rel2, t, smooth((t - 1.5) / 2))
    p.c02_orient = 1.0 - .85 * smooth((t - 1.0) / 1.5)
    # C01: down off the thigh, then forward and up in a wide arc in front of the legs, landing under the lip
    u = settle((t - 2.6) / 7.2, overshoot=.012, wobbles=1.3)
    p.c01 = 1.0
    if t < 2.6:
        p.hand = mlerp(m1, r1, down)
    else:
        p.hand = reach_path(r1, u)
    p.c01_orient = 1.0 - (1.0 - TRAVEL_ORIENT) * smooth((t - 1.6) / .9) + (HAND_ORIENT - TRAVEL_ORIENT) * smooth((u - .6) / .4)
    p.fingers = smooth((t - 4.5) / 4.0)
    p.point_dir = DOCK_DIR; p.point_w = smooth((u - .55) / .45)
    p.look = DOCK_TIP + Vector((0, 2.0 * (1 - smooth((t - 3) / 5)), 1.5 * (1 - smooth((t - 3) / 5)))); p.look_w = .6
    return p


def reach_path(r1, u):
    p0 = r1.translation
    p3 = M_DOCK.translation
    p1 = p0 + Vector((-1.6, -4.2, 2.0))
    p2 = p3 + Vector((-1.4, 4.6, 2.8))
    uu = min(max(u, 0.0), 1.0)
    pos = bezier(p0, p1, p2, p3, uu)
    if u > 1:
        pos = p3 + (p3 - bezier(p0, p1, p2, p3, .985)) * (u - 1) * 30
    rot = r1.to_quaternion().slerp(M_DOCK.to_quaternion(), smoother(min(1.0, max(0.0, u * 1.1))))
    return Matrix.Translation(pos) @ rot.to_matrix().to_4x4()


def hold_base(p):
    p.leg_hip = 1; p.leg_knee = 1; p.leg_swing = 1.6
    p.c02 = 1; p.hand2 = c02_hang(_start["rel2"], p.sway); p.c02_orient = .15
    return p


def reach_hold(t):
    p = Pose(); p.breath = breath(t) * .7; p.sway = t + 31.0; p.sway_amp = 1.1
    hold_base(p)
    p.rise = REACH_END["rise"]; p.lean = REACH_END["lean"]; p.roll = REACH_END["roll"]
    p.c01 = 1; p.hand = M_DOCK; p.fingers = 1; p.wiggle = 1; p.c01_orient = HAND_ORIENT
    p.point_dir = DOCK_DIR; p.point_w = 1
    p.look = DOCK_TIP; p.look_w = .6
    return p


LIFT_LEN = 28.0
LIFT_RISE, LIFT_LEAN = .3, 2.0
# The pull: the hand lifts off the lip, dives in a wide arc to the cloud sea on his left, skims it in a long sweep
# around in front of him (the finger's underside on the clouds, the player 50 m up, the god's whole height above),
# swings out to his right and climbs in a wide spiral along his front to the face. Fingertip waypoints (time s,
# position); FACE_TIP closes the list.
LIFT_KEYS = [(0.0, None), (2.6, Vector((-.8, -15.2, 6.2))), (5.6, Vector((-4.6, -13.6, .1))), (8.8, Vector((-7.2, -9.4, -.05))),
             (12.2, Vector((-5.0, -6.2, -.1))), (15.4, Vector((-.6, -6.8, -.05))), (18.4, Vector((3.4, -6.6, .5))),
             (21.4, Vector((1.4, -7.0, 4.6))), (24.0, Vector((-1.6, -4.6, 9.8))), (26.6, None)]


def hermite_path(keys, t):
    """C1 path through (time, point) keys, still at both ends."""
    ts = [k for k, _ in keys]
    ps = [p for _, p in keys]
    t = min(max(t, ts[0]), ts[-1])
    i = max(j for j in range(len(ts) - 1) if ts[j] <= t)
    def vel(j):
        if j == 0 or j == len(ts) - 1:
            return Vector()
        return ((ps[j + 1] - ps[j]) / (ts[j + 1] - ts[j]) + (ps[j] - ps[j - 1]) / (ts[j] - ts[j - 1])) * .5
    h = ts[i + 1] - ts[i]
    u = (t - ts[i]) / h
    h00, h10, h01, h11 = 2 * u ** 3 - 3 * u ** 2 + 1, u ** 3 - 2 * u ** 2 + u, -2 * u ** 3 + 3 * u ** 2, u ** 3 - u ** 2
    return ps[i] * h00 + vel(i) * (h10 * h) + ps[i + 1] * h01 + vel(i + 1) * (h11 * h)


def lift_tip(t):
    keys = [(k, p if p is not None else (DOCK_TIP if k == 0 else FACE_TIP)) for k, p in LIFT_KEYS]
    return hermite_path(keys, t)


def lift(t):
    p = Pose(); p.breath = breath(t) * .7; p.sway = t + 43.0; p.sway_amp = 1.0
    hold_base(p)
    end = LIFT_KEYS[-1][0]
    f = min(1.0, t / end)
    tip = lift_tip(t)
    # he bends down after his hand to the clouds and turns with it, then straightens as it climbs to his face
    down, up = smooth(t / 5.5), smoother((t - 16.0) / (end - 16.0))
    p.lean = REACH_END["lean"] + (20.0 - REACH_END["lean"]) * down + (LIFT_LEAN - 20.0) * up
    p.rise = REACH_END["rise"] + (-.25 - REACH_END["rise"]) * down + (LIFT_RISE + .25) * up
    follow = max(-1.0, min(1.0, -tip.x / 6.0))
    p.roll = REACH_END["roll"] * (1 - up) + 4.0 * follow * smooth(t / 4.0) * (1 - up)
    p.c01 = 1; p.fingers = 1
    p.point_w = 1
    # the finger turns from the lip to his face the long way round on his left (where the hand sweeps), never
    # through a flip: one turn about the vertical, and at the very end the last roll into the face pose
    yaw = TURN * smooth((t - 1.5) / (end - 4.0))
    qz = Quaternion((0, 0, 1), yaw)
    p.point_dir = qz @ DOCK_DIR
    e_dock = M_DOCK.translation - (DOCK_TIP - DOCK_DIR * FINGER_LEN)
    e_face = M_FACE.translation - (FACE_TIP - FACE_DIR * FINGER_LEN)
    pos = tip - p.point_dir * FINGER_LEN + (qz @ e_dock).lerp(e_face, smoother(f))
    rot = (qz @ M_DOCK.to_quaternion()).slerp(M_FACE.to_quaternion(), smoother((t - (end - 6.0)) / 6.0))
    p.hand = Matrix.Translation(pos) @ rot.to_matrix().to_4x4()
    p.c01_orient = HAND_ORIENT - (HAND_ORIENT - TRAVEL_ORIENT) * (smooth(t / 1.8) - smooth((t - (end - 2.0)) / 2.0))
    p.look = tip + Vector((0, 0, G.FINGER_R)); p.look_w = .75
    return p


def talk(t):
    p = Pose(); p.breath = breath(t); p.sway = t + 55.0; p.sway_amp = 1.6
    hold_base(p)
    p.rise = LIFT_RISE; p.lean = LIFT_LEAN; p.c01 = 1; p.fingers = 1; p.c01_orient = HAND_ORIENT
    p.point_dir = FACE_DIR; p.point_w = 1
    bob = .003 * breath(t)
    p.hand = Matrix.Translation(M_FACE.translation + Vector((0, 0, bob))) @ M_FACE.to_quaternion().to_matrix().to_4x4()
    p.look = spot_of(FACE_TIP, FACE_DIR); p.look_w = .75
    w = 2 * math.pi / 12.0
    p.nod = 3.2 * math.sin(3 * w * t) * (.6 + .4 * math.sin(w * t)) + 1.5 * math.sin(5 * w * t + 1)
    p.tilt = 2.5 * math.sin(w * t + .5) + 1.2 * math.sin(4 * w * t)
    return p


# ------------------------------------------------------------------ dock and face targets
# The wrist follows the forearm (low orientation weight on C01's IK: the hand trails the arm and never folds back);
# the index finger bends at its knuckle to point where it must. The target is placed so the knuckle sits one finger
# length behind the wanted fingertip.
DOCK_TIP = G.LIP + Vector((0, -.05, -.01 - G.FINGER_R))
DOCK_DIR = Vector((0, -1, 0))
# At the face the hand hides nothing: the player stands 320 m in front of him and 160 m below his eyes, the index
# finger pointing at his face; the hand turned so the arm hangs low from the shoulder and comes up from below and
# outside (searched by the share of the view the hand covers and by how high the arm loops).
FACE_AZ, FACE_DIST, FACE_DROP = math.radians(0), 3.2, 1.6
FACE_SPOT = HEAD + Vector((-math.sin(FACE_AZ) * FACE_DIST, -math.cos(FACE_AZ) * FACE_DIST, -FACE_DROP))
FACE_DIR = Vector((HEAD.x - FACE_SPOT.x, HEAD.y - FACE_SPOT.y, 0)).normalized()
FACE_TIP = FACE_SPOT + FACE_DIR * SPOT_BACK + Vector((0, 0, -G.FINGER_R))
HAND_ORIENT = 1.0  # at the dock and the face the wrist target is lined up with the forearm (see fit_target)
TRAVEL_ORIENT = .04  # on the way: the hand trails the arm
M_DOCK = hand_matrix(DOCK_TIP, DOCK_DIR, Vector((0, 1, .6)))
M_FACE = hand_matrix(FACE_TIP, FACE_DIR, -FACE_DIR + FACE_DIR.cross(Vector((0, 0, 1))).normalized())
# the turn of the finger in the pull, from DOCK_DIR to FACE_DIR about the vertical, through his left (-x)
TURN = -(math.pi - math.atan2(FACE_DIR.x, FACE_DIR.y)) if FACE_DIR.y > -1 else -math.pi


def fit_target(m, tip, fdir, pose_fn, rounds=6):
    """Target for C01 so that the knuckle lands one finger length behind `tip` and the wrist continues the forearm:
    solve, turn the target's rotation so the palm lines up with the forearm, move it so the knuckle lands, repeat.
    pose_fn(m) -> Pose (with c01_orient as the clip uses it)."""
    want = tip - fdir.normalized() * FINGER_LEN
    for _ in range(rounds):
        for _ in range(6):
            apply(pose_fn(m))
            err = want - whead("C01_f0_00")
            if err.length < 5e-4:
                break
            m = Matrix.Translation(m.translation + err) @ m.to_quaternion().to_matrix().to_4x4()
        fa = (wtail("C01_seg07") - whead("C01_seg07")).normalized()
        wd = (wtail("C01_wrist_00") - whead("C01_wrist_00")).normalized()
        qw = (arm.matrix_world @ PB["C01_wrist_00"].matrix).to_quaternion()
        q = Quaternion().slerp(wd.rotation_difference(fa), .85) @ qw
        m = Matrix.Translation(m.translation) @ q.to_matrix().to_4x4()
    for _ in range(8):
        apply(pose_fn(m))
        err = want - whead("C01_f0_00")
        if err.length < 5e-4:
            break
        m = Matrix.Translation(m.translation + err) @ m.to_quaternion().to_matrix().to_4x4()
    return m


def spot_of(tip, fdir):
    return tip - fdir.normalized() * SPOT_BACK + Vector((0, 0, G.FINGER_R))


def spot(m_target):
    """The player's spot: on the dock or in front of the face, by which target it is nearer."""
    return spot_of(DOCK_TIP, DOCK_DIR) if (m_target.translation - M_DOCK.translation).length < (m_target.translation - M_FACE.translation).length \
        else spot_of(FACE_TIP, FACE_DIR)


def start_reach():
    """Set the release targets from the AlertIdle pose (the reach starts there)."""
    apply(alert_idle(0.0))
    _start["m1"], _start["r1"], _start["rel2"] = let_go_targets()


def wrist_bend():
    """Angle between C01's forearm (seg07) and palm (wrist) bones, degrees."""
    a = (wtail("C01_seg07") - whead("C01_seg07")).normalized()
    b = (wtail("C01_wrist_00") - whead("C01_wrist_00")).normalized()
    return math.degrees(a.angle(b))


def fit_dock():
    global M_DOCK, M_FACE
    def hold(m):
        global M_DOCK
        M_DOCK = m
        return reach_hold(0)
    M_DOCK = fit_target(M_DOCK, DOCK_TIP, DOCK_DIR, hold)
    apply(reach_hold(0))
    print(f"dock: tip miss {(wtail('C01_f0_02') - DOCK_TIP).length:.4f}, wrist bend {wrist_bend():.1f} deg")
    def face(m):
        global M_FACE
        M_FACE = m
        return talk(0)
    # four rounds: the arm stays on its low branch (hanging from the shoulder and up to the face); more rounds walk the
    # target onto the branch where the arm loops over his head
    M_FACE = fit_target(M_FACE, FACE_TIP, FACE_DIR, face, rounds=4)
    apply(talk(0))
    top = max(whead(b).z for b in ("C01_shoulder", "C01_seg02", "C01_seg03", "C01_seg04", "C01_elbow", "C01_seg06", "C01_seg07"))
    print(f"face: arm top {top:.2f} (head {HEAD.z:.2f})")
    print(f"face: tip miss {(wtail('C01_f0_02') - FACE_TIP).length:.4f}, wrist bend {wrist_bend():.1f} deg")


CLIPS = [("Idle", 12.0, idle, True), ("Notice", NOTICE_LEN, notice, False), ("AlertIdle", 12.0, alert_idle, True),
         ("Reach", REACH_LEN, reach, False), ("ReachHold", 12.0, reach_hold, True), ("Lift", LIFT_LEN, lift, False),
         ("Talk", 12.0, talk, True)]


# ------------------------------------------------------------------ bake
def bake(name, length, fn):
    frames = int(round(length * FPS))
    keys = {pb.name: [] for pb in PB}
    for f in range(frames + 1):
        apply(fn(f / FPS))
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


def check(name, fn, length):
    """Worst IK misses over a clip (S hands on their rocks, C hands on their targets), units."""
    worst = {}
    for f in range(0, int(length * FPS) + 1, 5):
        apply(fn(f / FPS))
        for s in ("S01", "S02", "S03", "C01", "C02"):
            d = (wtail(s + "_wrist_00") - T[s].matrix_world.translation).length
            worst[s] = max(worst.get(s, 0), d)
    return {k: round(v, 3) for k, v in worst.items()}


if __name__ == "__main__":
    start_reach()
    fit_dock()
    report = {}
    baked = {}
    for name, length, fn, loop in CLIPS:
        if name == "Reach":
            start_reach()
        report[name] = dict(seconds=length, frames=int(round(length * FPS)) + 1, loop=loop, worst_ik_miss=check(name, fn, length))
        baked[name] = bake(name, length, fn)
        print("baked", name, report[name])
    apply(reach_hold(0)); report["dock_tip"] = list(wtail("C01_f0_02")); report["dock_spot"] = list(spot_of(DOCK_TIP, DOCK_DIR))
    report["dock_wrist_bend_deg"] = wrist_bend()
    apply(talk(0)); report["face_spot"] = list(spot_of(FACE_TIP, FACE_DIR)); report["head"] = list(HEAD)
    report["face_wrist_bend_deg"] = wrist_bend()
    report["dock_miss"] = (Vector(report["dock_tip"]) - DOCK_TIP).length
    json.dump(report, open(os.path.join(OUT, "clips.json"), "w"), indent=1)
    for pb in PB:
        for c in pb.constraints:
            c.mute = True
    for name, (act, keys, frames) in baked.items():
        write_action(act, keys, frames)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "MG01_anim.blend"))
    print(json.dumps(report, indent=1))
