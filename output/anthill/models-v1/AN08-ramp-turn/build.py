import sys, os, random
sys.path.insert(0, '/home/claude/ant/models')
from shell_lib import *

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
ak.reset()
H0, H1 = 0.5, 1.0
TT = [0.0, 0.05, 0.3, 0.55, 0.8, 0.95, 1.0]             # top points inner -> outer (lips at 0 and 1)


def sections():
    """(inner, outer) 2D pairs along the path. Arm A: x 0..3 going +Y from y=-3; 45-deg chamfered inner corner;
    arm B: y 0..3 going -X to x=-3."""
    S = []
    for y in (-3, -2.3, -1.5, -0.6):
        S.append(((0, y), (3, y)))
    I0, I1 = Vector((0, -0.6)), Vector((-0.6, 0))
    outer = [(3, -0.6), (3, 0.6), (3, 1.8), (3, 3), (1.8, 3), (0.6, 3), (-0.6, 3)]
    for k in range(1, 6):
        I = I0.lerp(I1, k / 6)
        S.append(((I.x, I.y), outer[k]))
    for x in (-0.6, -1.5, -2.3, -3):
        S.append(((x, 0), (x, 3)))
    return S


def build(vid, seed):
    rnd = random.Random(seed)
    s = Solid()
    S = sections()
    mids = [(Vector(a) + Vector(b)) / 2 for a, b in S]
    L = [0.0]
    for i in range(1, len(S)):
        L.append(L[-1] + (mids[i] - mids[i - 1]).length)
    secs = []
    for i, (a, b) in enumerate(S):
        A, B = Vector((a[0], a[1], 0)), Vector((b[0], b[1], 0))
        end = i in (0, len(S) - 1)
        J = (lambda q: 0.0) if end else (lambda q: rnd.uniform(-q, q))
        h = H0 + (H1 - H0) * L[i] / L[-1]
        out = (B - A).normalized()
        sec = []
        for k, t in enumerate(TT):
            lip = k in (0, len(TT) - 1)
            p = A.lerp(B, t)
            if 0 < k < len(TT) - 1:
                p += out * J(0.04)
            sec.append(p + Vector((0, 0, h - (0.07 if lip else 0) + (0 if lip else J(0.008)))))
        sb = 0 if end else 1
        sec += [B + out * (0.07 * sb + J(0.04)) + Vector((0, 0, h * 0.5 + J(0.05))), B + out * J(0.03),
                A - out * J(0.03), A - out * (0.06 * sb + J(0.04)) + Vector((0, 0, h * 0.45 + J(0.05)))]
        secs.append(sec)
    n = len(secs[0])
    mats = ['Floor' if 0 < k < len(TT) - 2 else 'Earth' for k in range(n)]
    s.loft(secs, mats)
    return s, s.finish(vid), L[-1]


s, objs, plen = build('AN08a', 81)
stats('AN08a', objs, s)
slope = max_slope(objs)
print('max walk slope', slope, 'path', plen)
fbx = os.path.join(OUT, 'SM_AN08_RampTurn.fbx')
ak.export_fbx(objs, fbx)
JOINS = {
    'grid': 'pivot bottom centre of the 6 x 6 m footprint; L-shaped 3 m wide path, turns 90 deg left (from +Y to -X); bottom flat at z=0',
    'low_end': 'flat face y = -3.0, x 0..+3 (centre x = +1.5), walk top z = 0.50',
    'high_end': 'flat face x = -3.0, y 0..+3 (centre y = +1.5), walk top z = 1.00',
    'chain': 'AN07 straight ramp (3 m wide) meets the low end when placed at (+1.5, -5, -0.5) [its 1.0 m high end -> 0.5]; at the high end continue with AN07 rotated 90 deg (rising toward -X) placed at (-5, +1.5, +0.97) on an earth base, or a landing at z = 1.0. Mirror in X for a right turn.',
}
recs = [ak.variant_record('AN08a', 'Ramp turn 90 deg, 3 m path, 0.5 -> 1.0 m', objs, fbx, solidity='walk-on-top',
                          collider='mesh', joins=JOINS, emissive=False, path_length_m=round(plen, 2),
                          slope_deg=round(math.degrees(math.atan2(H1 - H0, plen)), 1), max_face_slope_deg=round(slope, 1),
                          notes='L-shaped earth block with a 45-deg chamfer on the inside corner and a square outer corner; Floor top rises evenly along the path, rounded Earth lips, crooked bulging sides, no steps.')]
write_manifest(OUT, recs)
ak.save_blend(os.path.join(HERE, 'AN08-ramp-turn.blend'))
if wants_render():
    hm = human(-1.5, 1.5, 0.85)
    card = renders(objs, OUT, os.path.join(SHEETS, '08-ramp-turn.png'), az_off=180, el=28,
                   extra=[('SCALE 1.8 m man', objs + hm, dict(az=150, el=25))], title='AN08 ramp turn')
    print(card)
