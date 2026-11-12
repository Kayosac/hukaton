"""08 Living bonsai with eyes — collectible shelf item.  Run: python3 build.py [--align-only]

Geometry is placed from the concept itself: every trunk / branch / pad / eye point is a concept pixel cast as a camera
ray onto a depth plane (y) or onto the soil plane (z), so the model lines up with the concept at the fixed concept
camera by construction; the rest of the 3D shape (depths, thicknesses, hidden roots/branches) is designed by hand.

Parts: pot (12-sided oval lathe with lip and foot), soil (disc), trunk (twisted elliptic sweep, 7 sides), branch
(tapered 5-sided tubes), root (tubes lying on the soil), pad (4 foliage pads: faceted flattened domes with ragged
outline), leaf (small flat diamonds poking out of the pads + the sprout), eye (bulging eyeballs), socket (bark rings
round the eyes). Texture = concept projection; hidden sides from pot rotation/mirror views and part palettes.
"""
import sys, os, json, math
sys.path.insert(0, '/home/claude/kit')
import numpy as np
import cv2
from scipy import ndimage as ndi
import bpy, bmesh
from mathutils import Vector, Matrix
from geo import Builder, clear_scene, check_fit
import bake as BK
import render as R
import cam as C

HERE = os.path.dirname(os.path.abspath(__file__))
CONCEPT = '/home/claude/concepts/collectible-concepts/08-living-bonsai-v2.png'
ID = 'CI08_LivingBonsai'
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)
CW, CH = 1536, 1024
BIG = max(CW, CH)

# ------------------------------------------------------------------ concept camera (fixed; geometry is built from it)
DIST = float(os.environ.get('BONSAI_DIST', 1.05))
CAM = {'az': 0.0, 'el': 20.0, 'dist': DIST, 'fov': 18.0, 'target': [0.0, 0.0, 0.10 * DIST / 1.05], 'shift': [0, 0],
       'roll': 0.0}
ORIGIN_PX = (737.0, 948.0)          # where the pot's bottom centre sits in the concept


def _fix_shift(c):
    u, v, _ = C.project(np.zeros((1, 3)), c, CW, CH)
    c['shift'] = [float((ORIGIN_PX[0] - u[0]) / BIG), float((v[0] - ORIGIN_PX[1]) / BIG)]
    return c


CAM = _fix_shift(CAM)
POS, RIGHT, UPC, FWD = [np.array(a) for a in C.basis(CAM)]
FPX = (BIG / 2) / math.tan(math.radians(CAM['fov']) / 2)


def ray(u, v):
    sx, sy = CAM['shift']
    xc = (u - CW / 2 - sx * BIG) / FPX
    yc = (CH / 2 - v - sy * BIG) / FPX
    d = FWD + RIGHT * xc + UPC * yc
    return POS, d / np.linalg.norm(d)


def PY(u, v, y=0.0):
    """Concept pixel -> 3D point on the plane Y = y."""
    o, d = ray(u, v)
    t = (y - o[1]) / d[1]
    return Vector(o + d * t)


def PZ(u, v, z):
    """Concept pixel -> 3D point on the plane Z = z."""
    o, d = ray(u, v)
    t = (z - o[2]) / d[2]
    return Vector(o + d * t)


def mpp(p):
    """Metres per concept pixel at 3D point p."""
    return float(np.dot(np.array(p) - POS, FWD)) / FPX


TO_CAM = Vector(-FWD)

# ------------------------------------------------------------------ pot (metres; oval: y = 0.62 x)
POT_SCALE = (1.0, 0.642)
POT_R, POT_Z = 1.035, 1.005
Z_SOIL = 0.0468 * POT_Z
POT_OUT = [(0.0585, 0.000), (0.0615, 0.0020), (0.0620, 0.0075), (0.0675, 0.0090), (0.0705, 0.0200),
           (0.0745, 0.0335), (0.0764, 0.0425), (0.0792, 0.0437), (0.0798, 0.0487), (0.0786, 0.0515)]
POT_IN = [(0.0690, 0.0525), (0.0668, 0.0498), (0.0660, 0.0468)]
POT_OUT = [(r * POT_R, z * POT_Z) for r, z in POT_OUT]
POT_IN = [(r * POT_R, z * POT_Z) for r, z in POT_IN]

# ------------------------------------------------------------------ tree, concept pixels
# trunk: (u, v, half width px, y depth)
TRUNK = [(700, 770, 84, 0.006), (692, 700, 86, 0.004), (688, 640, 98, -0.006), (694, 580, 100, -0.009),
         (728, 524, 108, -0.009), (790, 466, 102, -0.006), (797, 420, 104, -0.002), (760, 375, 82, 0.002),
         (735, 340, 60, 0.005), (712, 318, 38, 0.007), (697, 296, 30, 0.008), (690, 270, 25, 0.009),
         (697, 245, 22, 0.010), (704, 215, 20, 0.010), (706, 190, 18, 0.010)]
TRUNK_RY = 0.80          # depth / width of the trunk section
# branches: list of (u, v, radius px, y)
BRANCHES = [
    [(700, 300, 19, 0.004), (645, 308, 16, 0.005), (595, 304, 13, 0.006), (562, 282, 10, 0.008)],        # top-left
    [(712, 246, 12, 0.008), (748, 228, 9, 0.009), (778, 212, 7, 0.010)],                                  # top-right
    [(842, 398, 20, 0.002), (900, 362, 16, 0.010), (945, 345, 13, 0.020), (978, 318, 10, 0.026)],        # right-up
    [(902, 362, 9, 0.010), (918, 334, 7, 0.016)],                                                         # fork
    [(668, 500, 18, 0.000), (605, 512, 15, -0.004), (550, 520, 12, -0.008), (505, 503, 9, -0.012)],      # left-mid
    [(560, 516, 7, -0.007), (522, 534, 5, -0.010)],                                                       # twig
    [(790, 618, 20, 0.000), (858, 598, 16, -0.002), (922, 578, 13, -0.004), (990, 562, 10, -0.006),
     (1030, 545, 8, -0.006)],                                                                             # right-low
    [(930, 576, 8, -0.004), (944, 550, 6, -0.005)],                                                      # fork
]
# roots: (u, v) on the soil, start raised; radius px start->end
ROOTS = [
    dict(path=[(668, 680), (604, 722), (544, 753)], r=(22, 7), lift=(0.014, 0.0)),
    dict(path=[(700, 690), (672, 736), (640, 774)], r=(24, 8), lift=(0.014, 0.0)),
    dict(path=[(760, 684), (816, 712), (866, 727)], r=(20, 7), lift=(0.013, 0.0)),
    dict(path=[(792, 682), (848, 686), (902, 690)], r=(13, 5), lift=(0.008, 0.0)),
]
# hidden back roots (3D, metres): start, mid, end on soil
BACK_ROOTS = [((-0.012, 0.010), (-0.030, 0.022), (-0.048, 0.030)), ((0.014, 0.010), (0.030, 0.020), (0.046, 0.026)),
              ((0.000, 0.012), (0.004, 0.026), (0.008, 0.036))]
# foliage pads: centre px (u, v), half width px, half height px (silhouette), y depth, outline wobble, tilt (deg x, y)
# foliage pads: silhouette target (u centre, v top, v bottom, half width px) of the main mass (hanging leaves apart)
PADS = [
    dict(n='top', u=641, vt=32, vb=280, hw=279, y=0.010, sides=10, tilt=(3, -5), seed=1, peak=(-0.014, 0.004)),
    dict(n='right_up', u=974, vt=218, vb=346, hw=189, y=0.026, sides=9, tilt=(3, 0), seed=2),
    dict(n='left', u=491, vt=372, vb=516, hw=176, y=-0.012, sides=9, tilt=(3, -4), seed=3),
    dict(n='right_low', u=1051, vt=458, vb=596, hw=165, y=-0.006, sides=9, tilt=(3, 3), seed=4),
]
PAD_RY = 0.78
# leaves (base px, tip px, width px, y)
LEAVES = [
    ((398, 214), (322, 243), 30, 0.000), ((420, 226), (372, 276), 28, 0.000),          # top pad, left
    ((388, 470), (342, 516), 30, -0.022), ((418, 496), (402, 552), 30, -0.026),         # left pad, bottom
    ((452, 482), (462, 524), 26, -0.026),
    ((1062, 312), (1056, 372), 30, 0.010), ((1086, 316), (1118, 356), 28, 0.012),       # right-up pad, under
    ((1052, 562), (1042, 606), 30, -0.020), ((1150, 548), (1186, 596), 28, -0.012),     # right-low pad, under
    ((906, 530), (884, 552), 22, -0.014),
    ((1046, 196), (1006, 178), 22, 0.030), ((1048, 202), (1102, 206), 22, 0.030),       # sprout
    ((1050, 190), (1092, 160), 22, 0.030),
]
SPROUT = [(1032, 248, 6, 0.030), (1040, 222, 5, 0.030), (1048, 194, 4, 0.030)]
# eyes: socket centre px, eyeball radius px, socket outer radius px, protrusion out of the bark (x radius), turn
EYES = [dict(c=(735, 430), r=33, ro=51, pro=0.75, turn=-14, up=4),
        dict(c=(831, 404), r=34, ro=49, pro=0.75, turn=14, up=6)]


def rot_to(axis_z):
    """Rotation matrix taking +Z to axis_z."""
    z = Vector(axis_z).normalized()
    x = Vector((0, 0, 1)).cross(z) if abs(z.z) < 0.95 else Vector((1, 0, 0))
    x = (x - z * x.dot(z)).normalized()
    y = z.cross(x)
    return Matrix((x, y, z)).transposed()


def sweep(b, part, pts, rads, sides=7, twist=0.0, jitter=0.0, caps=(True, True), up_hint=(0, 1, 0), level=0.0):
    """Elliptic sweep: pts list of Vector, rads list of (rx, ry) (rx across the view, ry in depth)."""
    mi = b.part(part, True)
    n = len(pts)
    rings = []
    prev = None
    for i in range(n):
        t = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        t = (t * (1 - level) + Vector((0, 0, 1)) * level).normalized()
        yv = Vector(up_hint) if prev is None else prev
        yv = (yv - t * yv.dot(t)).normalized()
        prev = yv
        xv = yv.cross(t).normalized()
        rx, ry = rads[i]
        ring = []
        for k in range(sides):
            a = 2 * math.pi * k / sides + twist * i
            p = pts[i] + xv * math.cos(a) * rx + yv * math.sin(a) * ry
            p += Vector((b.j(jitter), b.j(jitter), b.j(jitter)))
            ring.append(p)
        rings.append(b.ring_verts(ring))
    for a_, c_ in zip(rings, rings[1:]):
        b.bridge(a_, c_, mi)
    if caps[0]:
        b.cap(rings[0], mi, flip=True)
    if caps[1]:
        b.cap(rings[-1], mi)
    return rings


def build_pot(b):
    prof = POT_OUT + POT_IN
    rings = b.lathe('pot', prof, sides=12, jitter=0.0006, rot=math.pi / 12, cap_bottom=True, cap_top=False,
                    scale=POT_SCALE)
    # crooked: tilt the rim a hair and push the lip a little out of round
    for ring in rings:
        for v in ring:
            v.co.y *= 0.72 + 0.28 * min(v.co.z / 0.025, 1.0)
            v.co.x -= 0.0018 * (1 - min(v.co.z / 0.045, 1.0))
            v.co.z += 0.0012 * v.co.x / 0.08 * (v.co.z / 0.05)
    soil = [(POT_IN[-1][0], Z_SOIL), (0.050, Z_SOIL + 0.0008), (0.034, Z_SOIL + 0.0016), (0.018, Z_SOIL + 0.0022),
            (0.0, Z_SOIL + 0.0025)]
    b.lathe('soil', soil, sides=12, jitter=0.0004, rot=math.pi / 12, cap_bottom=False, cap_top=False,
            scale=POT_SCALE)


def build_trunk(b):
    pts, rads = [], []
    for (u, v, hw, y) in TRUNK:
        p = PY(u, v, y)
        r = hw * mpp(p)
        pts.append(p)
        rads.append((r, r * TRUNK_RY))
    pts[0] = Vector((pts[1].x, pts[1].y, Z_SOIL + 0.0012))
    rads[0] = (rads[0][0] * 1.06, rads[0][1] * 1.06)
    sweep(b, 'trunk', pts, rads, sides=7, twist=0.07, jitter=0.0007, caps=(True, True), up_hint=(0, -1, 0), level=1.0)


def build_branches(b):
    for br in BRANCHES:
        pts, radii = [], []
        for (u, v, r, y) in br:
            p = PY(u, v, y)
            pts.append(p); radii.append(r * mpp(p))
        # grow the first point back into its parent so the joint closes
        d = (pts[1] - pts[0]).normalized()
        pts[0] = pts[0] - d * radii[0] * 1.2
        b.tube('branch', pts, radii=radii, sides=5, jitter=0.0004)


LOBE = [(752, 590, 30, 0.000), (800, 640, 26, -0.002), (836, 690, 18, -0.004)]


def build_roots(b):
    pts = [PY(u, v, y) for (u, v, r, y) in LOBE]
    rad = [r * mpp(p) for (u, v, r, y), p in zip(LOBE, pts)]
    pts.append(PZ(872, 724, Z_SOIL + 0.001)); rad.append(0.0016)
    b.tube('root', pts, radii=rad, sides=6, jitter=0.0004)
    from mathutils.bvhtree import BVHTree
    bvh = BVHTree.FromBMesh(b.bm)
    for rt in ROOTS:
        # every root point lies on what the concept camera sees there (trunk flare or soil), half sunk into it
        n = len(rt['path'])
        pts, radii = [], []
        for i, (u, v) in enumerate(rt['path']):
            f = i / (n - 1)
            o, d = ray(u, v)
            hit = bvh.ray_cast(Vector(o), Vector(d))[0]
            r0, r1 = rt['r']
            r = (r0 * (1 - f) + r1 * f) * mpp(hit)
            pts.append(hit + Vector(d) * r * 0.35)
            radii.append(r)
        b.tube('root', pts, radii=radii, sides=5, jitter=0.0004)
    for (a, m, e) in BACK_ROOTS:
        pts = [Vector((a[0], a[1], Z_SOIL + 0.012)), Vector((m[0], m[1], Z_SOIL + 0.005)),
               Vector((e[0], e[1], Z_SOIL + 0.0008))]
        b.tube('root', pts, radii=[0.0042, 0.0030, 0.0014], sides=5, jitter=0.0004)


PAD_PROF = [(0.00, -0.07), (0.60, 0.00), (0.95, 0.17), (1.00, 0.34), (0.80, 0.54), (0.56, 0.78), (0.28, 0.95), (0.0, 1.0)]


def pad_points(pd, cx, cz, rx, T):
    """Pad vertex rings (numpy) for centre (cx, pd.y, cz) of the base, x radius rx and thickness T."""
    rng = np.random.default_rng(pd['seed'])
    sides = pd['sides']
    wob = 1 + rng.uniform(-0.14, 0.09, sides)
    rot = rng.uniform(0, math.pi)
    ry = rx * PAD_RY
    Mt = np.array(Matrix.Rotation(math.radians(pd['tilt'][0]), 3, 'X') @
                  Matrix.Rotation(math.radians(pd['tilt'][1]), 3, 'Y'))
    pk = pd.get('peak', (0, 0))
    rings = []
    for ri, (r, z) in enumerate(PAD_PROF):
        if r <= 1e-6:
            pts = np.array([[pk[0], pk[1], z * T]])
        else:
            pts = []
            for k in range(sides):
                a = rot + 2 * math.pi * k / sides
                w = wob[k] * (1 + rng.uniform(-0.04, 0.04)) * (1.07 if (ri in (2, 3) and k % 2) else 1.0)
                lump = 0.13 if ri >= 4 else 0.06
                zz = z * T + (rng.uniform(-lump, lump) * T if 0 < ri < len(PAD_PROF) - 1 else 0)
                f = max(zz / T - 0.34, 0) / 0.66
                pts.append([math.cos(a) * r * rx * w + pk[0] * f, math.sin(a) * r * ry * w + pk[1] * f, zz])
            pts = np.array(pts)
        rings.append(pts @ Mt.T + np.array([cx, pd['y'], cz]))
    return rings


def build_pad(b, pd):
    """Faceted flattened dome fitted to the concept silhouette box of the pad."""
    c = PY(pd['u'], (pd['vt'] + pd['vb']) / 2, pd['y'])
    m = mpp(c)
    cx, cz, rx = c.x, c.z, pd['hw'] * m
    T = 0.4 * rx
    cosel = math.cos(math.radians(CAM['el']))
    for it in range(12):
        allp = np.concatenate(pad_points(pd, cx, cz - T / 2, rx, T))
        u, v, _ = C.project(allp, CAM, CW, CH)
        vt, vb, ul, ur = v.min(), v.max(), u.min(), u.max()
        dmid = (pd['vt'] + pd['vb']) / 2 - (vt + vb) / 2
        cz -= dmid * m / cosel
        T = max(T + ((pd['vb'] - pd['vt']) - (vb - vt)) * m / cosel, 0.004)
        rx *= (2 * pd['hw']) / (ur - ul)
        cx += (pd['u'] - (ul + ur) / 2) * m
    rings = pad_points(pd, cx, cz - T / 2, rx, T)
    vr = [b.ring_verts([Vector(p) for p in r]) for r in rings]
    for ri, (a_, c_) in enumerate(zip(vr, vr[1:])):
        mi = b.part('pad_under' if ri < 2 else 'pad', True)
        if len(a_) == 1:
            for i in range(len(c_)):
                b._face([a_[0], c_[(i + 1) % len(c_)], c_[i]], mi)
        elif len(c_) == 1:
            for i in range(len(a_)):
                b._face([a_[i], a_[(i + 1) % len(a_)], c_[0]], mi)
        else:
            b.bridge(a_, c_, mi)
    # leaves round the hidden back / sides of the rim (the concept only shows the front ones)
    rng = np.random.default_rng(pd['seed'] + 50)
    ring = rings[3]
    ctr = np.array([cx, pd['y'], cz - T / 2])
    for k, pt in enumerate(ring):
        out = pt - ctr; out[2] = 0
        dist = np.linalg.norm(out); out /= max(dist, 1e-9)
        if out[1] < 0.45 or k % 3 == 2:      # front: the concept's own leaves; skip some for irregularity
            continue
        p0 = Vector(pt - out * dist * 0.12)
        L = rng.uniform(0.011, 0.016)
        tip = p0 + Vector(out) * L * 1.15 + Vector((0, 0, -L * rng.uniform(0.25, 0.5)))
        tip += Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), 0)) * 0.002
        build_leaf3d(b, p0, tip, L * 0.55)
    print('pad', pd['n'], 'rx %.4f T %.4f' % (rx, T))


def build_leaf3d(b, p0, p1, w, part='leaf', th=0.0012):
    ax = p1 - p0
    L = ax.length
    ax.normalize()
    n = Vector((0, 0, 1))
    n = (n - ax * n.dot(ax)).normalized()
    side = ax.cross(n).normalized()
    mid = p0 + ax * L * 0.45
    vs = [p0, mid + side * w * 0.5, p1, mid - side * w * 0.5, mid + n * th, mid - n * th]
    faces = [[0, 1, 4], [1, 2, 4], [2, 3, 4], [3, 0, 4], [1, 0, 5], [2, 1, 5], [3, 2, 5], [0, 3, 5]]
    b.add_bmesh_geometry(part, vs, faces)


def build_leaf(b, base_px, tip_px, w_px, y, part='leaf'):
    p0, p1 = PY(base_px[0], base_px[1], y), PY(tip_px[0], tip_px[1], y)
    ax = p1 - p0
    L = ax.length
    ax.normalize()
    n = (TO_CAM * 0.55 + Vector((0, 0, 0.45)))
    n = (n - ax * n.dot(ax)).normalized()
    side = ax.cross(n).normalized()
    w = w_px * mpp(p0) * 0.5
    th = 0.0012
    mid = p0 + ax * L * 0.45
    vs = [p0, mid + side * w, p1, mid - side * w, mid + n * th, mid - n * th]
    faces = [[0, 1, 4], [1, 2, 4], [2, 3, 4], [3, 0, 4], [1, 0, 5], [2, 1, 5], [3, 2, 5], [0, 3, 5]]
    b.add_bmesh_geometry(part, vs, faces)


def build_eyes(b):
    from mathutils.bvhtree import BVHTree
    bvh = BVHTree.FromBMesh(b.bm)
    for e in EYES:
        o, d0 = ray(*e['c'])
        hit, nrm, _, _ = bvh.ray_cast(Vector(o), Vector(d0))
        m = mpp(hit)
        re, ro = e['r'] * m, e['ro'] * m
        d = Matrix.Rotation(math.radians(e['turn']), 3, 'Z') @ Matrix.Rotation(math.radians(e['up']), 3, 'X') @ TO_CAM
        d = Vector((d.x, d.y, d.z)).normalized()
        # eyeball centre: on the view ray, its front pole `pro` x radius in front of the bark surface
        cpt = hit - Vector(d0) * re * (e['pro'] - 1.0)
        M = rot_to(d)
        rs = b.blob('eye', (0, 0, 0), (re, re, re * 0.92), rings=4, sides=8, jitter=0.0)
        for ring in rs:
            for v in ring:
                v.co = M @ v.co + cpt
        # socket: bark ring (closed tube) round the eyeball, a bit behind its pole
        rc = (re + ro) * 0.5
        tr = (ro - re) * 0.5 * 1.35
        path = []
        for k in range(9):
            a = 2 * math.pi * k / 9 + 0.3
            p = Vector((math.cos(a) * rc, math.sin(a) * rc * 1.04, re * 0.05))
            path.append(M @ p + cpt)
        b.tube('socket', path, radius=tr, sides=5, closed=True, jitter=0.0003)


def build():
    clear_scene()
    b = Builder(ID, seed=8)
    build_pot(b)
    build_trunk(b)
    build_branches(b)
    build_roots(b)
    for pd in PADS:
        build_pad(b, pd)
    for (bp, tp, w, y) in LEAVES:
        build_leaf(b, bp, tp, w, y)
    pts = [PY(u, v, y) for (u, v, r, y) in SPROUT]
    b.tube('branch', pts, radii=[r * mpp(p) for (u, v, r, y), p in zip(SPROUT, pts)], sides=4)
    build_eyes(b)
    obj = b.finish(ground=False, center_xy=False)
    # ground: lift so min z = 0 and move the camera target with it (identical projection)
    dz = -min(v.co.z for v in obj.data.vertices)
    obj.data.transform(Matrix.Translation((0, 0, dz)))
    CAM['target'][2] += dz
    return obj


obj = build()
fit = check_fit(obj)
print('fit', fit)
BK.unwrap(obj)
# eyes carry the painted pupils: give them a much higher texel density, then repack
EYE_UV_BOOST = 3.2
parts_l = list(obj['parts'])
uvl = obj.data.uv_layers.active.data
for poly in obj.data.polygons:
    if parts_l[poly.material_index] == 'eye':
        for li in poly.loop_indices:
            uvl[li].uv = uvl[li].uv * EYE_UV_BOOST
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.select_all(action='SELECT')
bpy.ops.uv.pack_islands(rotate=True, margin=0.012)
bpy.ops.object.mode_set(mode='OBJECT')
cam = CAM
json.dump(cam, open(os.path.join(HERE, 'camera.json'), 'w'), indent=1)
print('IoU', round(BK.align_report(obj, CONCEPT, cam, os.path.join(OUT, 'align.png')), 4))
if '--parts' in sys.argv:
    P_, UV_, part_, N_ = BK.mesh_arrays(obj)
    idb, _ = BK.rasterize(P_, part_, cam, CW, CH)
    pal = np.array([[200, 90, 60], [70, 50, 40], [140, 90, 50], [220, 160, 60], [120, 70, 30], [60, 160, 60],
                    [160, 230, 90], [250, 250, 250], [250, 120, 200]], np.uint8)
    rgb_, cm_ = BK.load_concept(CONCEPT)
    vis = (rgb_ * 0.35).astype(np.uint8)
    m_ = idb >= 0
    vis[m_] = (pal[part_[idb[m_]] % len(pal)] * 0.7 + vis[m_] * 0.3).astype(np.uint8)
    edge = cm_ ^ ndi.binary_erosion(cm_, iterations=2)
    vis[edge] = (255, 255, 0)
    cv2.imwrite(os.path.join(HERE, 'work', 'parts.png'), cv2.cvtColor(vis, cv2.COLOR_RGB2BGR))
    print('parts', obj['parts'])
if '--mask' in sys.argv:
    cv2.imwrite(os.path.join(HERE, 'work', 'model_mask.png'), BK.model_mask(obj, cam, CW, CH).astype(np.uint8) * 255)
if '--align-only' in sys.argv:
    sys.exit()

# ------------------------------------------------------------------ bake
def texel_parts(obj, size):
    P, UV, part, N = BK.mesh_arrays(obj)
    img = np.full((size, size), -1, np.int32)
    uvp = UV * size
    uvp[..., 1] = size - uvp[..., 1]
    for t in range(len(P)):
        cv2.fillPoly(img, [np.round(uvp[t] * 16).astype(np.int32)], int(part[t]), lineType=cv2.LINE_8, shift=4)
    _, ind = ndi.distance_transform_edt(img < 0, return_indices=True)
    return img[ind[0], ind[1]]


PUPILS = [((739.2, 432.0), 15.5, (735, 430), 33), ((828.7, 405.4), 13.5, (831, 404), 34)]   # concept px


def eye_texels(obj, size):
    """Texel rows/cols, 3D points and normals of the eye part."""
    P, UV, part, N = BK.mesh_arrays(obj)
    sel = np.nonzero(part == parts_l.index('eye'))[0]
    out = []
    uvp = UV * size
    uvp[..., 1] = size - uvp[..., 1]
    img = np.full((size, size), -1, np.int32)
    for t in sel:
        cv2.fillPoly(img, [np.round(uvp[t] * 16).astype(np.int32)], int(t), lineType=cv2.LINE_8, shift=4)
    img = ndi.grey_dilation(img, size=3) if False else img
    ys, xs = np.nonzero(img >= 0)
    t = img[ys, xs]
    a0, a1, a2 = uvp[t, 0], uvp[t, 1], uvp[t, 2]
    px, py = xs + 0.5, ys + 0.5
    den = (a1[:, 1] - a2[:, 1]) * (a0[:, 0] - a2[:, 0]) + (a2[:, 0] - a1[:, 0]) * (a0[:, 1] - a2[:, 1])
    den = np.where(np.abs(den) < 1e-12, 1e-12, den)
    l0 = ((a1[:, 1] - a2[:, 1]) * (px - a2[:, 0]) + (a2[:, 0] - a1[:, 0]) * (py - a2[:, 1])) / den
    l1 = ((a2[:, 1] - a0[:, 1]) * (px - a2[:, 0]) + (a0[:, 0] - a2[:, 0]) * (py - a2[:, 1])) / den
    l0, l1 = np.clip(l0, 0, 1), np.clip(l1, 0, 1)
    l2 = np.clip(1 - l0 - l1, 0, 1)
    ls = l0 + l1 + l2
    X = (P[t, 0] * l0[:, None] + P[t, 1] * l1[:, None] + P[t, 2] * l2[:, None]) / ls[:, None]
    return ys, xs, X, N[t]


def paint_eyes(obj, tex):
    """Crisp painted eyes: cream eyeball shaded toward its rim, near-black pupil where the concept has it,
    a 1-2 texel catch light. Same chunky texel manner as the rest."""
    size = tex.shape[0]
    ys, xs, X, Nn = eye_texels(obj, size)
    u, v, _ = C.project(X, CAM, CW, CH)
    col = np.zeros((len(X), 3))
    for (pc, pr, ec, er) in PUPILS:
        d_e = np.hypot(u - ec[0], v - ec[1])
        near = d_e < er * 1.6
        # radial shade on the eyeball (distance from its own centre in 3D, front hemisphere ~ concept disc)
        f = np.clip(d_e / er, 0, 1)
        white = np.array([226, 208, 174]) * (1 - 0.18 * f[:, None] ** 2)
        dp = np.hypot(u - pc[0], v - pc[1])
        pup = dp < pr
        hl = np.hypot(u - (pc[0] - 0.45 * pr), v - (pc[1] - 0.45 * pr)) < 0.32 * pr
        c = np.where(pup[:, None], np.array([38, 29, 22]), white)
        c = np.where((pup & hl)[:, None], np.array([232, 224, 206]), c)
        col[near] = c[near]
    tex = tex.copy()
    tex[ys, xs] = np.clip(col, 0, 255).astype(np.uint8)
    return tex


def postprocess(obj, tex):
    """Palette-quantize like the kit (40 colours) but keep the eyes out of the shared palette (their whites
    would collapse into the foliage greens): eyes get their own 6-colour palette."""
    from PIL import Image
    size = tex.shape[0]
    tp = texel_parts(obj, size)
    eye = tp == parts_l.index('eye')
    q = np.array(Image.fromarray(tex).quantize(colors=40, method=Image.Quantize.MEDIANCUT,
                                               dither=Image.Dither.NONE).convert('RGB'))
    ys, xs = np.nonzero(eye)
    if len(ys):
        strip = tex[ys, xs][None]
        qe = np.array(Image.fromarray(strip).quantize(colors=6, method=Image.Quantize.MEDIANCUT,
                                                      dither=Image.Dither.NONE).convert('RGB'))[0]
        q[ys, xs] = qe
    return paint_eyes(obj, q)


size = BK.texture_size_for(obj)
I4 = np.eye(4)
views = [{'name': 'orig', 'T': I4, 'bonus': 0.3},
         {'name': 'mirror_y', 'T': BK.T_mirror_plane('y'), 'bonus': 0.05,
          'parts': ['trunk', 'branch', 'root', 'pad', 'pad_under', 'leaf', 'pot', 'soil']},
         {'name': 'rot180', 'T': BK.T_rot_z(180), 'bonus': 0.1, 'parts': ['pot', 'soil']}]
views += [{'name': f'rot{a}', 'T': BK.T_rot_z(a), 'parts': ['pot']} for a in (40, -40, 140, -140)]
views += [{'name': f'trot{a}', 'T': BK.T_rot_z(a), 'parts': ['trunk', 'branch', 'root']} for a in (60, -60, 120, -120)]
part_colors = {
    'soil': [(62, 47, 35), (72, 55, 41), (84, 64, 48), (95, 72, 54)],
    'eye': [(196, 186, 166), (212, 202, 182), (226, 216, 196)],
    'pad_under': [(48, 46, 30), (58, 56, 34), (68, 66, 38), (80, 80, 44)],
}
tex_path = os.path.join(OUT, f'T_{ID}.png')
tex, st = BK.bake(obj, CONCEPT, cam, views=views, size=size, out_png=tex_path,
                  debug_png=os.path.join(OUT, 'coverage.png'), part_colors=part_colors, quantize=0)
tex = postprocess(obj, tex)
cv2.imwrite(tex_path, cv2.cvtColor(tex, cv2.COLOR_RGB2BGR))
print('bake', json.dumps(st))
R.apply_texture(obj, tex_path, ID)
stats = check_fit(obj)
stats['texture'] = f'{size}x{size}'
R.render_views(obj, os.path.join(OUT, 'preview.png'), concept_cam=cam, concept_size=(CW, CH), concept_path=CONCEPT)
R.shelf_fit_render(obj, os.path.join(OUT, 'shelf.png'))
R.export(obj, OUT, ID, tex_path)
iou = BK.align_report(obj, CONCEPT, cam, None)
json.dump({'id': ID, 'stats': stats, 'iou': round(iou, 4), 'bake': st, 'camera': cam},
          open(os.path.join(OUT, 'stats.json'), 'w'), indent=1)
R.card(OUT, ID, stats)
print('done', stats)
