"""MG01 layout - single source of truth for the blocking.

Points are authored as (px, py, y): a pixel in 00-canonical.png plus a chosen depth y (Blender Y,
+ = away from the canonical viewer). P() un-projects them through CANON camera, so the front view
matches the canon by construction while depth is a working interpretation (side views, spec).
Units: Blender metres at working scale 1:100 (model ~18 m tall = ~1800 m in-game proposal).
"""
from mathutils import Vector as V

# Camera fitted to the canon: 3 support contacts on one ground plane + figure height.
F_PX = 1333.0
CAM = V((1.1, -20.4, 8.4))
CX0, CY0 = 599.5, 656.0
LENS_MM = F_PX * 36.0 / 1312.0  # vertical sensor 36 mm, 1312 px tall canvas


def P(px, py, y):
    d = y - CAM.y
    return V((CAM.x + (px - CX0) * d / F_PX, y, CAM.z - (py - CY0) * d / F_PX))


def G(px, py, z):
    """Ray from the canon camera through pixel, hit the horizontal plane at height z."""
    dx, dz = (px - CX0) / F_PX, -(py - CY0) / F_PX
    t = (z - CAM.z) / dz
    return V((CAM.x + dx * t, CAM.y + t, z))


def pts(seq):
    return [P(*p) for p in seq]


# ------------------------------------------------------------------ body core
TORSO = dict(
    id='CORE', pts=pts([(565, 600, 0.45), (580, 515, 0.2), (585, 445, 0.2), (612, 335, 0.25), (640, 262, 0.35), (650, 212, 0.4)]),
    radii=[(1.3, 1.15), (1.55, 1.15), (1.2, 0.95), (1.6, 1.08), (1.6, 0.95), (0.5, 0.5)],
    roll=[0, 0, 0, 0, 0, 0], sides=8, samples=2, up_hint=(1, 0, 0))

HEAD = dict(id='HEAD', centre=P(640, 168, 0.4), size=(1.35, 1.15, 1.75), tilt_deg=(0, -8, -6),
            chin=P(650, 212, -0.1), hat_brim=P(615, 145, 0.35), hat_tip=P(735, 100, 0.6))

# Shoulder girdle of the old man (left end sinks into the shoulder mass, A02 hangs off the right end)
SHOULDER_BAR = dict(id='CORE', pts=pts([(560, 262, 0.45), (640, 250, 0.4), (712, 268, 0.35)]), radii=[(0.7, 0.62), (0.75, 0.62), (0.62, 0.55)])

# Big shoulder mass on the canon-left between C01 root and the torso: carries E06 (top) and face F01.
SHOULDER = dict(id='CORE', centre=P(492, 315, -0.15), radii=(1.25, 1.0, 1.75), tilt_deg=(0, 18, 0))

# Rear-lower pelvis mass where the three supports converge (hidden behind the cradle hands).
HUB = dict(id='CORE', pts=pts([(565, 590, 0.75), (552, 650, 1.5), (540, 700, 2.0)]),
           radii=[(1.15, 1.0), (1.4, 1.25), (1.6, 1.4)], sides=8, samples=2)
# the rounded rear lower mass itself: each support leaves it from its own side
HUB_MASS = dict(centre=P(540, 705, 2.0), radii=(2.05, 1.85, 1.65))

# ------------------------------------------------------------------ limbs
# joints: indices of control points that are anatomical joints (rings are guaranteed there)
LIMBS = [
    # cradle arms: giant loops from the shoulders, palms under the pelvis
    dict(id='C01', kind='cradle', pts=pts([(590, 290, 0.3), (470, 250, -0.35), (330, 272, 0.5), (185, 335, 1.5),
                                            (108, 438, 2.1), (95, 545, 1.8), (150, 632, 0.7), (290, 690, -0.3), (440, 640, -0.6)]),
         radii=[1.0, (1.25, 1.1), 1.12, 1.0, 1.05, 1.12, 1.06, 1.08, (0.78, 0.66)], sides=8, samples=2,
         joints={'shoulder': 1, 'elbow': 5, 'wrist': 8}),
    dict(id='C02', kind='cradle', pts=pts([(680, 300, 0.7), (800, 382, 1.5), (925, 420, 2.2), (1008, 470, 2.4),
                                            (1030, 545, 2.1), (990, 615, 1.5), (900, 640, 1.2), (780, 590, 0.9)]),
         radii=[0.95, 1.05, 1.0, 0.98, 1.05, 1.02, 0.98, (0.75, 0.64)], sides=8, samples=2,
         joints={'shoulder': 1, 'elbow': 4, 'wrist': 7}),
    # supports: hands on the ground, arms converge to the hub
    dict(id='S01', kind='support', pts=pts([(470, 735, 2.5), (320, 760, 2.5), (225, 812, 2.3), (160, 882, 2.3), (152, 962, 2.4)]),
         radii=[1.1, 1.0, 0.92, 1.0, (1.0, 0.85)], sides=8, samples=2,
         joints={'shoulder': 1, 'elbow': 2, 'wrist': 4}),
    dict(id='S02', kind='support', pts=pts([(530, 748, 1.4), (505, 800, 0.1), (482, 845, -1.3), (500, 905, -2.3), (515, 1000, -2.9)]),
         radii=[1.15, 1.05, 1.0, 1.12, (1.2, 1.0)], sides=8, samples=2,
         joints={'shoulder': 1, 'elbow': 2, 'wrist': 4}),
    dict(id='S03', kind='support', pts=pts([(610, 735, 2.5), (705, 712, 2.3), (790, 748, 2.0), (885, 805, 2.0), (942, 902, 2.2), (955, 978, 2.4)]),
         radii=[1.1, 1.02, 0.95, 0.98, 1.05, (1.02, 0.88)], sides=8, samples=2,
         joints={'shoulder': 1, 'elbow': 3, 'wrist': 5}),
    # small arms
    dict(id='A01', kind='small', pts=pts([(600, 245, 0.7), (565, 228, 1.45), (452, 208, 2.3), (305, 238, 2.35), (372, 172, 1.6)]),
         radii=[0.4, 0.37, 0.34, 0.29, 0.22], sides=6, samples=[1, 2, 3, 3], joints={'shoulder': 1, 'elbow': 3, 'wrist': 4}),
    dict(id='A02', kind='small', pts=pts([(722, 266, 0.3), (762, 284, -0.8), (805, 318, -2.7), (855, 290, -2.9)]),
         radii=[0.36, 0.32, 0.26, 0.2], sides=6, samples=[1, 3, 3], joints={'shoulder': 1, 'elbow': 2, 'wrist': 3}),
    dict(id='A03', kind='small', pts=pts([(560, 442, 0.1), (520, 444, 0.0), (430, 442, -0.5), (350, 460, -0.8)]),
         radii=[0.24, 0.22, 0.18, 0.14], sides=6, samples=[1, 3, 3], joints={'shoulder': 1, 'elbow': 2, 'wrist': 3}),
    dict(id='A04', kind='small', pts=pts([(668, 252, 0.8), (702, 230, 0.95), (752, 180, 0.9), (815, 115, 0.6)]),
         radii=[0.26, 0.24, 0.2, 0.15], sides=6, samples=[1, 3, 3], joints={'shoulder': 1, 'elbow': 2, 'wrist': 3}),
    # the old man's four legs
    dict(id='L01', kind='leg', pts=pts([(560, 525, -0.1), (598, 548, -1.0), (630, 572, -1.9), (645, 660, -2.75), (640, 800, -3.55)]),
         radii=[0.88, 0.74, 0.52, 0.58, 0.34], sides=6, samples=[2, 2, 2, 3], joints={'hip': 0, 'knee': 2, 'ankle': 4}),
    dict(id='L02', kind='leg', pts=pts([(610, 505, 0.0), (720, 418, -0.5), (825, 338, -1.0), (838, 440, -1.05), (835, 652, -1.1)]),
         radii=[0.88, 0.74, 0.52, 0.58, 0.34], sides=6, samples=[2, 2, 2, 3], joints={'hip': 0, 'knee': 2, 'ankle': 4}),
    dict(id='L03', kind='leg', pts=pts([(545, 515, 0.55), (330, 360, 2.9), (262, 152, 3.5), (205, 140, 3.7), (128, 142, 3.9)]),
         radii=[0.82, 0.7, 0.5, 0.48, 0.33], sides=6, samples=[2, 2, 2, 2], joints={'hip': 0, 'knee': 2, 'ankle': 4}),
    dict(id='L04', kind='leg', pts=pts([(625, 515, 0.6), (705, 465, 2.8), (850, 382, 4.3), (1062, 268, 4.5), (1085, 330, 4.55), (1108, 410, 4.6)]),
         radii=[0.82, 0.77, 0.7, 0.5, 0.48, 0.33], sides=6, samples=[2, 2, 2, 2, 2], joints={'hip': 0, 'knee': 3, 'ankle': 5}),
]

# ------------------------------------------------------------------ hands / feet (targets from the canon)
# Support hands: built on the flat ground z=0 (the canon puts the side hands on higher hills, so their
# canon pixels are not on one ground plane). Wrist from the canon, fingers fan out towards the viewer.
import math as _m


def fan_hand(wrist, heading_deg, knuckle_dist, knuckle_z, finger_len, tip_z, span, spread_deg, thumb_index,
             thumb_extra_deg=22.0):
    """5 knuckles/tips fanned around a heading (0 deg = towards the viewer, -Y; + = towards +X)."""
    h = _m.radians(heading_deg)
    fwd = V((_m.sin(h), -_m.cos(h), 0))
    side = V((_m.cos(h), _m.sin(h), 0))
    kc = V((wrist.x, wrist.y, 0)) + fwd * knuckle_dist
    kn, tp = [], []
    for i in range(5):
        u = (i - 2) / 2.0
        k = kc + side * (u * span / 2) - fwd * (abs(u) ** 1.6 * 0.35)
        a = _m.radians(u * spread_deg)
        flen = finger_len * (1.0 - 0.12 * abs(u))
        if i == thumb_index:
            a += _m.radians(thumb_extra_deg) * (1 if u > 0 else -1)
            flen *= 0.72
            k = k - fwd * 0.55
        d = fwd * _m.cos(a) + side * _m.sin(a)
        kn.append(V((k.x, k.y, knuckle_z)))
        t = k + d * flen
        tp.append(V((t.x, t.y, tip_z)))
    return kn, tp


def _support(wrist, eye, **kw):
    kn, tp = fan_hand(wrist, **kw)
    return dict(wrist=wrist, knuckles=kn, tips=tp, eye=eye, thumb_index=kw['thumb_index'])


SUPPORT_HANDS = {
    'S01': _support(P(152, 962, 2.4), 'E01', heading_deg=-8, knuckle_dist=1.7, knuckle_z=1.05, finger_len=2.0, tip_z=0.2,
                    span=3.2, spread_deg=26, thumb_index=4),
    'S02': _support(P(515, 1000, -2.9), 'E02', heading_deg=0, knuckle_dist=1.6, knuckle_z=1.25, finger_len=2.3, tip_z=0.26,
                    span=4.4, spread_deg=34, thumb_index=0),
    'S03': _support(P(955, 978, 2.4), 'E03', heading_deg=12, knuckle_dist=1.8, knuckle_z=1.1, finger_len=2.2, tip_z=0.2,
                    span=3.5, spread_deg=28, thumb_index=0),
}
# Cradle hands: palms cup the pelvis/thighs; fingers wrap towards the body.
CRADLE_HANDS = {
    # palms under the buttocks / thigh roots (the god sits IN his hands), wrists come from the loops
    'C01': dict(wrist=P(440, 640, -0.6), palm=P(545, 600, -0.2),
                knuckles=[P(546, 548, 0.9), P(564, 580, 0.1), P(582, 612, -0.7), P(596, 642, -1.5)],
                tips=[P(640, 500, 0.6), P(660, 535, -0.2), P(676, 570, -1.0), P(684, 600, -1.8)],
                thumb=[P(470, 600, -0.6), P(470, 540, -0.9)]),
    'C02': dict(wrist=P(780, 590, 0.9), palm=P(700, 525, 0.5),
                knuckles=[P(645, 500, 0.45), P(675, 472, 0.1), P(705, 446, -0.25), P(735, 420, -0.6)],
                tips=[P(624, 462, 0.25), P(654, 436, -0.1), P(684, 410, -0.45), P(716, 388, -0.75)],
                thumb=[P(740, 545, 0.4), P(735, 495, 0.0)]),
}
# Small hands: A01/A02 open palms facing the viewer (eyes E04/E05 in the palm), A03/A04 claws.
SMALL_HANDS = {
    'A01': dict(style='open', wrist=P(372, 172, 1.6), palm=P(388, 118, 1.55), normal=(0, -1, 0.15),
                tips=[P(335, 48, 1.55), P(368, 42, 1.55), P(410, 32, 1.55), P(452, 72, 1.55)], thumb=P(462, 130, 1.45), eye='E04'),
    'A02': dict(style='open', wrist=P(855, 290, -2.9), palm=P(882, 258, -2.95), normal=(0.1, -1, 0.1),
                tips=[P(858, 198, -2.9), P(885, 198, -2.9), P(915, 212, -2.9), P(936, 258, -2.9)], thumb=P(828, 228, -2.9), eye='E05'),
    'A03': dict(style='claw', wrist=P(350, 460, -0.8), palm=P(322, 465, -0.85), normal=(0, 0, 1),
                tips=[P(290, 488, -1.05), P(300, 494, -0.9), P(316, 494, -0.72), P(332, 488, -0.6)], thumb=P(338, 445, -1.0)),
    'A04': dict(style='claw', wrist=P(815, 115, 0.6), palm=P(845, 112, 0.6), normal=(0, 0, 1),
                tips=[P(856, 140, 0.4), P(870, 138, 0.55), P(884, 128, 0.7), P(892, 112, 0.8)], thumb=P(840, 92, 0.5)),
}
FEET = {
    'L01': dict(ankle=P(640, 800, -3.55), toe=P(646, 862, -3.85), sole_normal=(0, 0.2, -1)),
    'L02': dict(ankle=P(835, 652, -1.1), toe=P(846, 738, -1.4), sole_normal=(0, 0.2, -1)),
    'L03': dict(ankle=P(128, 142, 3.9), toe=P(150, 42, 3.7), sole_normal=(-0.6, -0.75, 0.25)),
    'L04': dict(ankle=P(1108, 410, 4.6), toe=P(1178, 392, 4.4), sole_normal=(0.2, 0.2, -1)),
}

# ------------------------------------------------------------------ painted / attached details (canon px)
EYES = {  # open eyes painted into BaseColor (no eyeballs)
    'E01': (116, 990), 'E02': (511, 1088), 'E03': (996, 1018), 'E04': (386, 111), 'E05': (876, 263),
    'E06': (454, 256), 'E07': (548, 276), 'E08': (595, 316), 'E09': (571, 358), 'E10': (575, 410)}
FACES = {'F01': (498, 353), 'F02': (157, 515), 'F03': (350, 624), 'F04': (988, 500), 'F05': (944, 913), 'F06': (536, 981)}
BRANCHES = {'B01': (149, 320), 'B02': (410, 489), 'B03': (1050, 578), 'B04': (151, 816)}
