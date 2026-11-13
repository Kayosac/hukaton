"""MG01 clips: Idle, Notice, AlertIdle, Reach, ReachHold, Lift, Talk — procedural keys over IK (support hands pinned to
their rocks; C01 and C02 driven through world targets that start on the thighs), baked frame by frame into plain bone
keys.

The story: he sits deep in his own hands (C01 under thigh L01 is the cradle, C02 hugs L02's knee); seeing the player he
stands up on his support arms; to reach he first lets go of himself — both hands slide down off the thighs — his two
ordinary legs drop and straighten under him, the torso sags with the weight and bounces; then C01 swings forward,
clear of the legs, and lays its index finger under the lip.
usage: python3 clips.py <out dir>"""
import sys, os, math, json
import numpy as np
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
        self.fk = None          # C01 laid by hand instead of IK (see fk_c01)


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
    use_ik = not p.fk or p.fk.get("ik", 0.0) > 0
    IK["C01_wrist_00"].influence = 1.0 if use_ik else 0.0
    vl.update()
    if use_ik:
        spread_ik_twist()
        vl.update()
    if p.fk:
        fk_c01(p.fk)
        vl.update()
    if not p.fk and p.point_dir is not None and p.point_w > 0:
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
    if t >= REACH_FK[0] and "H" in RS:
        # the wrist head on a smooth arc from where the IK left it to the lip; the hand swings from gripping (turned in)
        # to leading the swing forward and down, then to its pose on the lip; the arm's roll goes over to the one it
        # has there as the hand gets there
        k = smoother(min(1.0, max(0.0, u)))
        w = vslerp(vslerp(RS["W"], REACH_SWING_W, smoother(u / .6)), DK["W"], smoother((u - .25) / .75))
        p.fk = dict(H=reach_wrist(u), W=w, tau=RS["tau"] + (DK["tau"] - RS["tau"]) * k,
                    point=p.point_w, ik=1 - smoother((t - REACH_FK[0]) / (REACH_FK[1] - REACH_FK[0])))
    p.look = DOCK_TIP + Vector((0, 2.0 * (1 - smooth((t - 3) / 5)), 1.5 * (1 - smooth((t - 3) / 5)))); p.look_w = .6
    return p


REACH_FK = (2.6, 4.4)   # the reach goes over from the IK to the hand-laid arm here
REACH_SWING_W = Vector((-.3, -.6, -.75)).normalized()
RS = {}


def reach_wrist(u):
    """C01's wrist head on its arc from the IK's last pose to the lip (u past 1: the overshoot)."""
    p0, p3 = RS["H"], DK["H"]
    p1 = p0 + Vector((-1.6, -4.2, 2.0))
    p2 = p3 + Vector((-1.4, 4.6, 2.8))
    if u <= 1:
        return bezier(p0, p1, p2, p3, max(u, 0.0))
    return p3 + (p3 - bezier(p0, p1, p2, p3, .985)) * (u - 1) * 30


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
    if "tau" in DK:
        p.fk = dict(tip=DOCK_TIP, yaw=0.0, pitch=0.0, tau_ref=DK["tau"])
    return p


LIFT_LEN = 28.0
LIFT_END = 26.6                   # the hand has arrived at the face; then it rests
LIFT_RISE, LIFT_LEAN = .3, 2.0
FOLD_FROM = 13.0                  # from here the arm goes over from bowing out to hanging in a U under the hand
FACE_PITCH = 75.0                 # the hand at the face, pitched up about the knuckle from its pose on the lip (deg)
# The pull, a new single stroke (no cross-over to his right, no spiral): the hand lifts off the lip, swings out to his
# left and down over the cloud sea (the player sees the clouds and the whole god from the side), runs low along his
# left side, rises there and glides in from the left to the front of his face. The finger stays level all the way and
# only turns about the vertical, once, through his left: from pointing away from him (the lip) to pointing at his face.
# Fingertip control points of a smooth (C2) curve; the speed eases in over 4.5 s and out over the last 7.5 s. The finger
# lies under the lip: it first draws back toward him, clear of the lip's rocks, and only then rises.
LIFT_CTRL = [None, (0.0, -15.45, 3.95), (-.6, -14.9, 6.2), (-3.0, -15.6, 6.6), (-7.0, -15.8, 4.6), (-11.8, -11.6, 1.6), (-12.2, -9.6, 2.1),
             (-9.2, -7.2, 6.0), (-5.0, -4.0, 10.0), (-1.5, -2.5, 13.2), "face-", None]
# The hand: the finger turns once, through his left, from pointing away from him on the lip to pointing at his face: with
# the arm while it reaches out to his left and down (the arm almost straight there, the hand sloping down along it),
# then as the hand comes back up it tips up (the finger stays level, bent at the knuckle) and the arm goes over to
# hanging from the shoulder in a U that comes up into the hand from below (the elbow is never out to the side and never
# in against his body). Degrees; the slope is counted from the hand's pose on the lip.
YAW_KEYS = [(0.0, 0.0), (3.5, -8.0), (7.0, -28.0), (10.5, -40.0), (13.0, -22.0), (16.0, 30.0), (19.5, 100.0), (23.0, 155.0),
            (LIFT_END, 180.0)]
PITCH_KEYS = [(0.0, 0.0), (5.0, -2.0), (10.0, -5.0), (14.0, 25.0), (17.5, 75.0), (21.0, 88.0), (24.0, 82.0), (LIFT_END, FACE_PITCH)]
LEAN_KEYS = [(0.0, 13.0), (6.0, 18.0), (11.5, 20.0), (16.5, 13.0), (21.5, 5.5), (LIFT_END, LIFT_LEAN)]
RISE_KEYS = [(0.0, .05), (7.0, -.18), (12.0, -.24), (18.0, .05), (LIFT_END, LIFT_RISE)]
ROLL_KEYS = [(0.0, 2.5), (8.0, 6.0), (13.0, 7.0), (18.0, 4.5), (23.0, 1.0), (LIFT_END, 0.0)]
_path = {}


def curve(keys, t):
    """Smooth monotone curve through (time, value) keys, flat at both ends (Fritsch-Carlson)."""
    ts = [k for k, _ in keys]; vs = [v for _, v in keys]
    if t <= ts[0]:
        return vs[0]
    if t >= ts[-1]:
        return vs[-1]
    n = len(ts)
    sec = [(vs[i + 1] - vs[i]) / (ts[i + 1] - ts[i]) for i in range(n - 1)]
    m = [0.0] * n
    for i in range(1, n - 1):
        if sec[i - 1] * sec[i] > 0:
            m[i] = 3 * (ts[i + 1] - ts[i - 1]) / ((2 * ts[i + 1] - ts[i] - ts[i - 1]) / sec[i - 1] + (ts[i + 1] + ts[i] - 2 * ts[i - 1]) / sec[i])
    i = max(j for j in range(n - 1) if ts[j] <= t)
    h = ts[i + 1] - ts[i]; u = (t - ts[i]) / h
    return (vs[i] * (2 * u ** 3 - 3 * u ** 2 + 1) + m[i] * h * (u ** 3 - 2 * u ** 2 + u)
            + vs[i + 1] * (-2 * u ** 3 + 3 * u ** 2) + m[i + 1] * h * (u ** 3 - u ** 2))


def build_path():
    from scipy.interpolate import BSpline
    face_in = FACE_TIP + Vector((-.9, -.3, 0))
    ctrl = [DOCK_TIP if c is None and i == 0 else FACE_TIP if c is None else face_in if c == "face-" else Vector(c)
            for i, c in enumerate(LIFT_CTRL)]
    n = len(ctrl); deg = 3
    knots = [0.0] * deg + list(range(n - deg + 1)) + [float(n - deg)] * deg
    spl = BSpline(np.array(knots, float), np.array([list(c) for c in ctrl]), deg)
    us = np.linspace(0, n - deg, 8001)
    pts = spl(us)
    s = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(pts, axis=0), axis=1))])
    ts = np.linspace(0, LIFT_END, 5001)
    ramp_in = np.array([smoother(t / 4.5) for t in ts])
    ramp_out = np.array([1 - smoother((t - (LIFT_END - 7.5)) / 7.5) for t in ts])
    v = ramp_in * ramp_out
    st = np.concatenate([[0.0], np.cumsum((v[1:] + v[:-1]) * .5 * np.diff(ts))])
    _path.update(spl=spl, us=us, s=s * 1.0, ts=ts, st=st / st[-1] * s[-1], length=float(s[-1]),
                 vmax=float(s[-1] / st[-1]))


def lift_tip(t):
    if not _path:
        build_path()
    P = _path
    s = float(np.interp(min(max(t, 0.0), LIFT_END), P["ts"], P["st"]))
    u = float(np.interp(s, P["s"], P["us"]))
    return Vector(P["spl"](u))


def talk_nod(t):
    w = 2 * math.pi / 12.0
    return (3.2 * math.sin(3 * w * t) * (.6 + .4 * math.sin(w * t)) + 1.5 * math.sin(5 * w * t + 1),
            2.5 * math.sin(w * t + .5) + 1.2 * math.sin(4 * w * t))


_TAU = {}


def tau_table():
    """The arm's roll through the pull, followed frame by frame from the lip so that it never jumps to another answer;
    lift() and talk() read it."""
    if "v" in _TAU or _TAU.get("building"):
        return
    _TAU["building"] = True
    ts, vs, prev = [], [], None
    for f in range(int(round(LIFT_LEN * FPS)) + 1):
        p = lift(f / FPS)
        if prev is not None:
            p.fk["tau_ref"] = prev
        apply(p)
        prev = FK_LAST["tau"]
        ts.append(f / FPS); vs.append(prev)
    _TAU.update(t=np.array(ts), v=np.array(vs), building=False)


def tau_at(t):
    tau_table()
    return float(np.interp(t, _TAU["t"], _TAU["v"])) if "v" in _TAU else None


def lift(t):
    L = LIFT_LEN
    p = Pose(); p.breath = breath(t - L) * (.7 + .3 * smooth((t - (L - 5.0)) / 5.0))
    p.sway = t + 55.0 - L; p.sway_amp = 1.1 + .5 * smooth((t - (L - 7.0)) / 7.0)
    hold_base(p)
    p.lean, p.rise, p.roll = curve(LEAN_KEYS, t), curve(RISE_KEYS, t), curve(ROLL_KEYS, t)
    tip = lift_tip(t)
    yaw = math.radians(curve(YAW_KEYS, t))
    p.c01 = 1; p.fingers = 1
    p.fk = dict(tip=tip, yaw=yaw, pitch=math.radians(curve(PITCH_KEYS, t)),
                fold=smoother((t - FOLD_FROM) / (LIFT_END - 1.0 - FOLD_FROM)))
    ref = tau_at(t)
    if ref is not None:
        p.fk["tau_ref"] = ref
    fd = Quaternion(UP, yaw) @ DOCK_DIR
    p.look = spot_of(tip, fd) + (DOCK_TIP - spot_of(DOCK_TIP, DOCK_DIR)) * (1 - smooth(t / 2.5))
    p.look_w = .6 + .15 * smooth(t / 4.0)
    k = smoother((t - (L - 3.5)) / 3.5)
    nod, tilt = talk_nod(t - L)
    p.nod, p.tilt = nod * k, tilt * k
    return p


def talk(t):
    p = Pose(); p.breath = breath(t); p.sway = t + 55.0; p.sway_amp = 1.6
    hold_base(p)
    p.rise = LIFT_RISE; p.lean = LIFT_LEAN; p.c01 = 1; p.fingers = 1
    bob = .003 * breath(t)
    p.fk = dict(tip=FACE_TIP + Vector((0, 0, bob)), yaw=math.radians(YAW_KEYS[-1][1]), pitch=math.radians(FACE_PITCH),
                fold=1.0, tau_ref=tau_at(LIFT_LEN))
    p.look = spot_of(FACE_TIP, FACE_DIR); p.look_w = .75
    p.nod, p.tilt = talk_nod(t)
    return p


# ------------------------------------------------------------------ C01 laid by hand (Lift, Talk)
# The IK solver has more than one answer for this long arm and jumps between them (the arm whips round and coils).
# From the reach on C01 is posed directly instead:
#  - the finger keeps exactly the pose it had under the lip, only turned about the vertical (the player rides it at a
#    fixed offset from the fingertip bone: up on the finger stays up, the player is never tipped or swung round);
#  - the hand hangs from the knuckle and pitches about it, the finger bending at the knuckle to stay level;
#  - the arm lies along one smooth curve from the shoulder to the wrist, bone after bone at its own length, the curve
#    leaving the shoulder toward the hand and running straight into it, the slack bowing it out behind the hand, to his
#    left and down (at the face it hangs in a U from the shoulder and comes up into the hand);
#  - every arm bone only swings from its parent; the roll the hand needs is spread along the arm.
# The reach goes over to it from the IK once the hand has slid off the thigh; on the lip, in the pull and at the face
# the arm is laid by hand all the time.
ARM = ["C01_shoulder", "C01_seg02", "C01_seg03", "C01_seg04", "C01_elbow", "C01_seg06", "C01_seg07"]
ARM_LEN = [arm.data.bones[b].length for b in ARM]
ARM_TOTAL = sum(ARM_LEN)
TWIST_W = [.2, .14, .13, .13, .13, .13, .14]
TS_OUT, TS_FWD = .5, .3           # the curve leaves the shoulder toward the hand, outward along the collar bone and
FWD = Vector((0, -1, 0))          # forward (never down along his body); it runs straight into the hand. The slack
HANDLE = .33                      # bows the arm toward a pole (the elbow side):
POLE_BEHIND, POLE_OUT, POLE_DOWN, POLE_FWD = 1.0, .6, .5, .8
HANDLE_H = .5                     # (the hand end's handle: the forearm runs into the hand along it)
TS_FOLD = Vector((-.45, -1.0, -1)).normalized()  # at the face the arm hangs from the shoulder forward, down and a little out
LEFT = Vector((-1, 0, 0))
E_MIN, E_MAX = -32.0, 58.0        # the hand's slope: from 32 deg down (the knuckle bent back) to 58 deg up
YAX = Vector((0, 1, 0))
UP = Vector((0, 0, 1))
REL = {}
for _b in ARM + ["C01_wrist_00", "C01_f0_00"]:
    _bb = arm.data.bones[_b]
    REL[_b] = (_bb.parent.matrix_local.inverted() @ _bb.matrix_local).to_quaternion()
DK = {}
FK_LAST = {}
NB = 480
_BERN = np.array([[(1 - u) ** 3, 3 * (1 - u) ** 2 * u, 3 * (1 - u) * u * u, u ** 3] for u in np.linspace(0, 1, NB + 1)])


def capture_dock():
    """The hand on the lip as the IK fitted it (ReachHold): the finger, the hand and the arm's roll the reach ends in
    and the pull starts from. From here on C01 is laid by hand there too."""
    DK.clear()
    apply(reach_hold(0))
    wq = lambda b: (arm.matrix_world @ PB[b].matrix).to_quaternion()
    DK["Q"], DK["F"] = wq("C01_wrist_00"), wq("C01_f0_00")
    H, K, tip = whead("C01_wrist_00"), whead("C01_f0_00"), wtail("C01_f0_02")
    DK["vf"] = DK["F"].inverted() @ (tip - K)
    DK["vh"] = DK["Q"].inverted() @ (K - H)
    DK["lat"] = (tip - K).normalized().cross(UP).normalized()
    DK["fd"] = (tip - K).normalized()
    DK["W"] = DK["Q"] @ YAX
    DK["e"] = math.asin(DK["W"].z)
    DK["f0"] = (DK["Q"] @ REL["C01_f0_00"]).inverted() @ DK["F"]
    ik_tau = FK_LAST["tau"]
    DK["tau"] = ik_tau
    apply(reach_hold(0))  # now laid by hand: the roll the hand needs along this arm
    DK["tau"] = FK_LAST["tau"]
    DK["H"] = whead("C01_wrist_00")
    print(f"dock: arm roll {ik_tau:.1f} deg (IK, spread) -> {DK['tau']:.1f} deg (laid by hand), tip miss "
          f"{(wtail('C01_f0_02') - DOCK_TIP).length:.4f}")


def hand_at(tip, yaw, pitch):
    """Finger (f0_00) and hand (wrist) world rotations and the wrist head for a fingertip at `tip`."""
    qz = Quaternion(UP, yaw)
    F = qz @ DK["F"]
    Q = Quaternion(qz @ DK["lat"], pitch) @ qz @ DK["Q"]
    K = tip - F @ DK["vf"]
    return F, Q, K - Q @ DK["vh"]


def vslerp(a, b, t):
    """Turn unit vector a toward b by the fraction t (t > 1 turns past b)."""
    q = a.rotation_difference(b)
    ax, ang = q.to_axis_angle()
    return Quaternion(ax, ang * t) @ a


def arm_samples(S, H, tS, tH, pole, h, hand=1.0, fold=0.0):
    """The arm curve: leaving the shoulder along tS, running into the hand along tH; the slack h bows it toward
    `pole`, or (fold 1) draws it out along both end directions (the arm hangs in a U from the shoulder to the hand)."""
    D = (H - S).length
    a, b = h * fold, h * (1 - fold)
    P = np.array([list(S), list(S + tS * (HANDLE * D * hand + a) + pole * b), list(H - tH * (HANDLE_H * D * hand + a) + pole * b),
                  list(H)])
    return _BERN @ P


def solve_arm(S, H, tS, tH, pole, fold=0.0):
    """Joint points from the shoulder S to the wrist head H: the curve bowed toward the pole until it is as long as
    the arm, the joints laid on it at the bones' lengths, then a few passes to make every bone exactly its length with
    both ends held."""
    D = (H - S).length
    c = (H - S).normalized()
    if D >= ARM_TOTAL * .999:  # out of reach: straight (FK_LAST tells)
        out, s = [S.copy()], 0.0
        for L in ARM_LEN:
            s += L; out.append(S + c * s)
        return out, 0.0
    pole = pole - c * pole.dot(c)
    pole = pole.normalized() if pole.length > 1e-6 else Vector((0, 0, -1))
    def length(h, hand=1.0):
        p = arm_samples(S, H, tS, tH, pole, h, hand, fold)
        return float(np.linalg.norm(np.diff(p, axis=0), axis=1).sum())
    hand = 1.0
    if length(0.0) >= ARM_TOTAL:  # the end directions alone make it too long: shorter handles, no bow
        lo, hi = 0.0, 1.0
        for _ in range(36):
            mid = (lo + hi) * .5
            lo, hi = (mid, hi) if length(0.0, mid) < ARM_TOTAL else (lo, mid)
        hand, hi = lo, 0.0
    else:
        lo, hi = 0.0, 1.0
        while length(hi) < ARM_TOTAL:
            lo, hi = hi, hi * 1.6
            if hi > 200:
                raise RuntimeError(f"C01: no arm curve from {S} to {H}")
        for _ in range(36):
            mid = (lo + hi) * .5
            lo, hi = (mid, hi) if length(mid) < ARM_TOTAL else (lo, mid)
    pts = arm_samples(S, H, tS, tH, pole, hi, hand, fold)
    s = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(pts, axis=0), axis=1))])
    want = np.cumsum([0.0] + ARM_LEN) * (s[-1] / ARM_TOTAL)
    J = [np.array([np.interp(w, s, pts[:, i]) for i in range(3)]) for w in want]
    Hn = np.array(list(H))
    for _ in range(80):  # FABRIK, both ends held
        J[-1] = Hn.copy()
        for i in range(len(ARM_LEN) - 1, -1, -1):
            d = J[i] - J[i + 1]; J[i] = J[i + 1] + d / np.linalg.norm(d) * ARM_LEN[i]
        J[0] = np.array(list(S))
        for i in range(len(ARM_LEN)):
            d = J[i + 1] - J[i]; J[i + 1] = J[i] + d / np.linalg.norm(d) * ARM_LEN[i]
        if np.linalg.norm(J[-1] - Hn) < 1e-6:
            break
    return [Vector(j) for j in J], hi


def twist_of(q):
    """Angle (rad, -pi..pi) of q's twist about its own Y."""
    a = 2 * math.atan2(q.y, q.w)
    return (a + math.pi) % (2 * math.pi) - math.pi


def fk_c01(fk):
    """C01 laid by hand. Either the fingertip is given (tip, yaw, pitch: the hand held, as on the lip, in the pull and
    at the face; the roll it needs is spread along the arm, tau_ref picking the answer) or the wrist head, the hand's
    direction and the arm's roll (H, W, tau, point: the reach, where the hand just follows the arm). ik > 0 mixes in
    the IK pose already set (the reach starts from it)."""
    S = whead("C01_shoulder")
    d00 = (wtail("C01_seg00") - whead("C01_seg00")).normalized()
    if "tip" in fk:
        F, Q, H = hand_at(fk["tip"], fk["yaw"], fk["pitch"])
        W = Q @ YAX
    else:
        H, W, F, Q = fk["H"], fk["W"].normalized(), None, None
    c = (H - S).normalized()
    fold = fk.get("fold", 0.0)
    tS = (c + d00 * TS_OUT + FWD * TS_FWD).normalized().lerp(TS_FOLD, fold).normalized()
    # the arm bows behind the hand (the forearm runs straight into it), and to his left and down: the elbow never
    # comes in toward his body
    pole = (-W * POLE_BEHIND + LEFT * POLE_OUT - UP * POLE_DOWN + FWD * POLE_FWD).normalized()
    joints, k = solve_arm(S, H, tS, W, pole, fold)
    dirs = [(joints[i + 1] - joints[i]).normalized() for i in range(len(ARM))]
    if Q is not None:
        locs, lw, tau, res = lay_arm(dirs, Q, fk.get("tau_ref", DK.get("tau", 0.0)))
    else:
        locs, lw = lay_arm_roll(dirs, W, fk["tau"])
        tau, res = fk["tau"], 0.0
    ik = fk.get("ik", 0.0)
    if ik > 0:
        locs = [PB[b].rotation_quaternion.slerp(q, 1 - ik) for b, q in zip(ARM, locs)]
        lw = PB["C01_wrist_00"].rotation_quaternion.slerp(lw, 1 - ik)
        IK["C01_wrist_00"].influence = 0.0
    for b, q in zip(ARM, locs):
        PB[b].rotation_quaternion = q
    PB["C01_wrist_00"].rotation_quaternion = lw
    if F is not None:
        PB["C01_f0_00"].rotation_quaternion = (Q @ REL["C01_f0_00"]).inverted() @ F
    else:
        PB["C01_f0_00"].rotation_quaternion = Quaternion().slerp(DK["f0"], min(1.0, fk.get("point", 0.0)))
    FK_LAST.update(yaw=math.degrees(fk.get("yaw", 0.0)), pitch=math.degrees(fk.get("pitch", 0.0)), pole=pole, H=H, S=S, k=k,
                   tau=tau, res=res, reach=(H - S).length / ARM_TOTAL, wrist=math.degrees(dirs[-1].angle(W)))


def lay_arm_roll(dirs, W, tau):
    """Arm bone rotations along `dirs` with the roll tau (deg) spread along the arm; the hand only swings to W."""
    R = (arm.matrix_world @ PB["C01_seg00"].matrix).to_quaternion()
    locs = []
    for i, b in enumerate(ARM):
        base = R @ REL[b]
        q = YAX.rotation_difference(base.inverted() @ dirs[i]) @ Quaternion(YAX, math.radians(tau) * TWIST_W[i])
        locs.append(q); R = base @ q
    bw = R @ REL["C01_wrist_00"]
    return locs, YAX.rotation_difference(bw.inverted() @ W)


def lay_arm(dirs, Q, tau_ref):
    """Arm bone rotations along the world directions `dirs`, each bone only swinging from its parent, and the roll the
    hand (world rotation Q) needs spread along the arm by TWIST_W. tau_ref (deg) picks which spread: the one nearest it
    (one more full turn spread along the arm is another answer). Returns arm locals, wrist local, the roll and the
    wrist's leftover roll (deg)."""
    R0 = (arm.matrix_world @ PB["C01_seg00"].matrix).to_quaternion()
    W = Q @ YAX
    tau = math.radians(tau_ref)
    for it in range(16):
        R = R0; locs = []
        for i, b in enumerate(ARM):
            base = R @ REL[b]
            q = YAX.rotation_difference(base.inverted() @ dirs[i]) @ Quaternion(YAX, tau * TWIST_W[i])
            locs.append(q); R = base @ q
        bw = R @ REL["C01_wrist_00"]
        lw = bw.inverted() @ Q
        sw = YAX.rotation_difference(bw.inverted() @ W)
        res = twist_of(sw.inverted() @ lw)
        if abs(res) < 1e-6:
            break
        tau += res
    return locs, lw, math.degrees(tau), math.degrees(res)


def spread_ik_twist():
    """C01 under IK (the reach, on the lip): the solver puts the whole roll of the hand into the shoulder joint; keep
    its bone directions and hand, spread the roll along the arm instead."""
    dirs = [(wtail(b) - whead(b)).normalized() for b in ARM]
    Q = (arm.matrix_world @ PB["C01_wrist_00"].matrix).to_quaternion()
    q0 = arm.convert_space(pose_bone=PB[ARM[0]], matrix=PB[ARM[0]].matrix, from_space="POSE", to_space="LOCAL").to_quaternion()
    locs, lw, tau, res = lay_arm(dirs, Q, math.degrees(twist_of(q0)))
    IK["C01_wrist_00"].influence = 0.0
    for b, q in zip(ARM, locs):
        PB[b].rotation_quaternion = q
    PB["C01_wrist_00"].rotation_quaternion = lw
    FK_LAST.update(tau=tau, res=res)


# ------------------------------------------------------------------ dock and face targets
# The wrist follows the forearm (low orientation weight on C01's IK: the hand trails the arm and never folds back);
# the index finger bends at its knuckle to point where it must. The target is placed so the knuckle sits one finger
# length behind the wanted fingertip.
DOCK_TIP = G.LIP + Vector((0, -.05, -.01 - G.FINGER_R))
DOCK_DIR = Vector((0, -1, 0))
# At the face the hand hides nothing: the player stands 320 m in front of him and 160 m below his eyes on the level
# finger pointing at his face; the hand behind the player is pitched up about the knuckle and the arm hangs low from the
# shoulder and runs up into it from in front and below (see fk_c01).
FACE_AZ, FACE_DIST, FACE_DROP = math.radians(0), 3.2, 1.6
FACE_SPOT = HEAD + Vector((-math.sin(FACE_AZ) * FACE_DIST, -math.cos(FACE_AZ) * FACE_DIST, -FACE_DROP))
FACE_DIR = Vector((HEAD.x - FACE_SPOT.x, HEAD.y - FACE_SPOT.y, 0)).normalized()
FACE_TIP = FACE_SPOT + FACE_DIR * SPOT_BACK + Vector((0, 0, -G.FINGER_R))
HAND_ORIENT = 1.0  # on the lip the wrist target is lined up with the forearm (see fit_target)
TRAVEL_ORIENT = .04  # on the way: the hand trails the arm
M_DOCK = hand_matrix(DOCK_TIP, DOCK_DIR, Vector((0, 1, .6)))


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


def start_reach():
    """Set the release targets from the AlertIdle pose (the reach starts there), and, once the dock is known, where the
    IK leaves C01 when the reach goes over to the hand-laid arm."""
    apply(alert_idle(0.0))
    _start["m1"], _start["r1"], _start["rel2"] = let_go_targets()
    RS.clear()
    if "tau" in DK:
        p = reach(REACH_FK[0]); p.fk = None
        apply(p)
        RS.update(H=whead("C01_wrist_00"), W=(wtail("C01_wrist_00") - whead("C01_wrist_00")).normalized(), tau=FK_LAST["tau"])


def wrist_bend():
    """Angle between C01's forearm (seg07) and palm (wrist) bones, degrees."""
    a = (wtail("C01_seg07") - whead("C01_seg07")).normalized()
    b = (wtail("C01_wrist_00") - whead("C01_wrist_00")).normalized()
    return math.degrees(a.angle(b))


def fit_dock():
    global M_DOCK
    def hold(m):
        global M_DOCK
        M_DOCK = m
        return reach_hold(0)
    M_DOCK = fit_target(M_DOCK, DOCK_TIP, DOCK_DIR, hold)
    apply(reach_hold(0))
    print(f"dock: tip miss {(wtail('C01_f0_02') - DOCK_TIP).length:.4f}, wrist bend {wrist_bend():.1f} deg")
    capture_dock()
    apply(talk(0))
    top = max(whead(b).z for b in ARM)
    print(f"face: arm top {top:.2f} (head {HEAD.z:.2f}), tip miss {(wtail('C01_f0_02') - FACE_TIP).length:.4f}, "
          f"wrist bend {wrist_bend():.1f} deg, twist {FK_LAST['tau']:.1f} deg, reach {FK_LAST['reach']:.2f}")


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
        p = fn(f / FPS)
        apply(p)
        for s in ("S01", "S02", "S03", "C01", "C02"):
            if s == "C01" and p.fk and "tip" in p.fk:
                d = (wtail("C01_f0_02") - p.fk["tip"]).length
            elif s == "C01" and p.fk:
                d = (whead("C01_wrist_00") - p.fk["H"]).length
            else:
                d = (wtail(s + "_wrist_00") - T[s].matrix_world.translation).length
            worst[s] = max(worst.get(s, 0), d)
    return {k: round(v, 3) for k, v in worst.items()}


if __name__ == "__main__":
    start_reach()
    fit_dock()
    start_reach()
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
