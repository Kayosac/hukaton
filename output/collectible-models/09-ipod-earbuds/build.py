"""09 iPod wrapped in earbuds - collectible shelf item.
Run: python3 build.py [--opt] [--align-only] [--shade] [--no-render]

Geometry is traced from the concept with a design camera solved from the body's corners (work/solve_box.py):
a chamfered slab (octagon cross-section, bevelled top/bottom) sheared so it leans back ~7 deg and stands on its flat
bottom; recessed screen; a very shallow raised click-wheel disc; jack plug on the top; the cable as thin 6-sided tubes
(two closed two-turn wraps around the body, the plug cable arcing over the right side, two earbud cords running down
the front-right edge); two earbuds (low-poly heads + stems). Screen icons, wheel symbols, grilles, scuffs and the
rust/dirt blotches are painted (projected from the concept).
"""
import sys, os, json, math
sys.path.insert(0, '/home/claude/kit')
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import numpy as np
import cv2
from scipy import ndimage
import bpy, bmesh
from mathutils import Vector, Matrix
from geo import Builder, clear_scene, check_fit
import bake_local as BK
import render as R
import cam as C

CONCEPT = '/home/claude/concepts/collectible-concepts/09-ipod-earbuds-v1.png'
ID = 'CI09_IpodEarbuds'
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)
WORK = os.path.join(HERE, 'work'); os.makedirs(WORK, exist_ok=True)
CW, CH = 1536, 1024

# ------------------------------------------------------------------ body + design camera (solved from corners)
H = 0.1747                    # body height (unsheared)
W, D = 0.1100, 0.0357         # body width / depth
SX, SY = -0.0034, 0.0214      # top offset: slight lean to the left, lean back ~7 deg
CH_XY = 0.0042                # vertical edge chamfer (front-side bevel strip)
EX_T, EY_T, CZ_T = 0.0078, 0.0040, 0.0068   # top bevel: inset x, inset y, height
EX_B, EY_B, CZ_B = 0.0068, 0.0040, 0.0058   # bottom bevel
DCAM = {'az': 31.6178, 'el': 7.852, 'dist': 1.0412, 'fov': 18, 'target': [0, 0, 0.085],
        'shift': [-0.0563, -0.0358], 'roll': 0}
R_CAB = 0.0023                # cable radius
SIDES_CAB = 6


def shear(p):
    x, y, z = p
    return Vector((x + SX * z / H, y + SY * z / H, z))


def unshear(p):
    x, y, z = p
    return Vector((x - SX * z / H, y - SY * z / H, z))


def ray(u, v, cam=DCAM):
    """Camera ray through concept pixel (u, v) in UNSHEARED item space: (origin, direction)."""
    pos, right, upc, f = C.basis(cam)
    big = max(CW, CH)
    fpx = (big / 2) / math.tan(math.radians(cam['fov']) / 2)
    sx, sy = cam['shift']
    xr = (u - CW / 2 - sx * big) / fpx
    yr = (CH / 2 - sy * big - v) / fpx
    d = f + right * xr + upc * yr
    o = unshear(pos); o2 = unshear(pos + d)
    return o, (o2 - o)


def hit_plane(u, v, n, d):
    """Intersect the pixel ray with the unsheared plane n.X = d."""
    o, dr = ray(u, v)
    n = Vector(n)
    t = (d - n.dot(o)) / n.dot(dr)
    return o + dr * t


def front_pt(u, v, off):
    return hit_plane(u, v, (0, -1, 0), D / 2 + off)


def right_pt(u, v, off):
    return hit_plane(u, v, (1, 0, 0), W / 2 + off)


def octagon(w, d, c):
    """CCW from the top, starting at the front-left end of the front face. Edge i = (p_i, p_i+1):
    0 front, 1 front-right chamfer, 2 right, 3 back-right ch., 4 back, 5 back-left ch., 6 left, 7 front-left ch."""
    return [(-w / 2 + c, -d / 2), (w / 2 - c, -d / 2), (w / 2, -d / 2 + c), (w / 2, d / 2 - c),
            (w / 2 - c, d / 2), (-w / 2 + c, d / 2), (-w / 2, d / 2 - c), (-w / 2, -d / 2 + c)]


def offset_polygon(pts, r):
    """Offset a convex CCW polygon outward by r (intersection of offset edge lines)."""
    P = np.array(pts, float); n = len(P)
    lines = []
    for i in range(n):
        a, b_ = P[i], P[(i + 1) % n]
        t = (b_ - a) / np.linalg.norm(b_ - a)
        nrm = np.array([t[1], -t[0]])
        lines.append((a + nrm * r, t))
    out = []
    for i in range(n):
        (p0, t0), (p1, t1) = lines[i - 1], lines[i]
        A = np.array([t0, -t1]).T
        s = np.linalg.solve(A, p1 - p0)
        out.append(tuple(p0 + t0 * s[0]))
    return out


# ------------------------------------------------------------------ traced concept pixels
SCREEN_PX = [(513, 210), (850, 229), (838, 462), (510, 429)]          # TL, TR, BR, BL of the dark screen
WHEEL_C, WHEEL_R = (652, 711), 151                                   # click wheel outer circle
PLUG_BASE = (824, 156)
# cable strands on the front face (left -> right) and their continuation on the right side face
STRAND = {
    'A': {'front': [(478, 458), (550, 485), (625, 510), (700, 531), (775, 556), (845, 582)],
          'side': [(915, 622), (950, 652)], 'left_dz': 0.006},
    'B': {'front': [(478, 553), (525, 535), (600, 520), (675, 512), (750, 507), (840, 502)],
          'side': [(915, 495), (950, 492)], 'left_dz': -0.008},
    'C': {'front': [(470, 812), (550, 845), (625, 861), (675, 867), (725, 861), (800, 846), (850, 831)],
          'side': [(905, 803), (940, 787)], 'left_dz': 0.009},
    'D': {'front': [(472, 830), (550, 861), (625, 881), (675, 888), (725, 883), (800, 866), (850, 851)],
          'side': [(905, 830), (940, 815)], 'left_dz': 0.006},
}
PLUG_ARC = [(828, 66), (842, 36), (870, 25), (913, 27), (950, 62), (978, 118), (989, 200), (987, 280),
            (975, 335), (962, 380)]
BUDS = [  # head centre px, depth of the head centre (y, m, unsheared), grille normal azimuth (deg), stem px
    {'c': (921, 402), 'y': -0.0185, 'az': 58, 'tilt': 12, 'stem': [(887, 452), (869, 500), (862, 527)]},
    {'c': (968, 503), 'y': -0.0120, 'az': 72, 'tilt': 8, 'stem': [(930, 548), (907, 584), (899, 598)]},
]
CORD = [  # earbud cords down the front-right edge (px), the end tucks under the lower wrap
    [(862, 527), (859, 565), (863, 625), (868, 690), (871, 745), (867, 800), (860, 840)],
    [(899, 598), (894, 640), (890, 700), (888, 760), (885, 805), (878, 845)],
]


# ------------------------------------------------------------------ geometry helpers
def recalc(bm, faces):
    bmesh.ops.recalc_face_normals(bm, faces=faces)


def new_faces_since(bm, n0):
    bm.faces.ensure_lookup_table()
    return [bm.faces[i] for i in range(n0, len(bm.faces))]


def cable(b, part, path, r=R_CAB, sides=SIDES_CAB, closed=False, radii=None, flat=False, rot0=0.0):
    """Sweep a polygon along a path with parallel-transport frames; closed loops get their twist distributed so the
    seam matches. Ends are capped with a short pointed cap."""
    mi = b.part(part, flat)
    P = [Vector(p) for p in path]
    n = len(P)
    T = []
    for i in range(n):
        if closed:
            t = P[(i + 1) % n] - P[i - 1]
        else:
            t = P[min(i + 1, n - 1)] - P[max(i - 1, 0)]
        T.append(t.normalized())
    x = Vector((0, 0, 1)) if abs(T[0].z) < 0.9 else Vector((1, 0, 0))
    x = (x - T[0] * x.dot(T[0])).normalized()
    X = [x]
    for i in range(1, n):
        q = T[i - 1].rotation_difference(T[i])
        xi = q @ X[-1]
        xi = (xi - T[i] * xi.dot(T[i])).normalized()
        X.append(xi)
    if closed:
        q = T[-1].rotation_difference(T[0])
        xe = q @ X[-1]
        xe = (xe - T[0] * xe.dot(T[0])).normalized()
        ang = math.atan2(T[0].dot(X[0].cross(xe)), X[0].dot(xe))   # twist of the transported frame
        for i in range(n):
            a = -ang * i / n
            X[i] = (Matrix.Rotation(a, 3, T[i]) @ X[i]).normalized()
    rings = []
    for i in range(n):
        y = T[i].cross(X[i])
        rr = radii[i] if radii else r
        pts = []
        for k in range(sides):
            a = rot0 + 2 * math.pi * k / sides
            pts.append(P[i] + X[i] * math.cos(a) * rr + y * math.sin(a) * rr)
        rings.append(b.ring_verts(pts))
    segs = n if closed else n - 1
    for i in range(segs):
        b.bridge(rings[i], rings[(i + 1) % n], mi)
    if not closed:
        rr0 = radii[0] if radii else r
        rr1 = radii[-1] if radii else r
        b.cap(rings[0], mi, flip=True, center=tuple(P[0] - T[0] * rr0 * 0.6))
        b.cap(rings[-1], mi, center=tuple(P[-1] + T[-1] * rr1 * 0.6))
    return rings


def wrap_loop(names, roff):
    """Closed path of several turns around the body. Each turn: back-left corner (z known: front-left z + dz),
    front-left chamfer, traced front points, front-right chamfer, traced right-side points, back-right chamfer,
    back. Unknown z are interpolated along the perimeter."""
    O = offset_polygon(octagon(W, D, CH_XY), roff)
    st = []   # (x, y, z or None)
    xlim = W / 2 - CH_XY - 0.0015
    for nm in names:
        s = STRAND[nm]
        fr = [front_pt(u, v, roff) for u, v in s['front']]
        fr = [p for p in fr if abs(p.x) <= xlim]
        sd = [right_pt(u, v, roff) for u, v in s['side']]
        sd = [p for p in sd if -D / 2 + CH_XY + 0.001 < p.y < D / 2 - CH_XY - 0.001]
        z0 = fr[0].z
        st.append((O[6][0], O[6][1], z0 + s['left_dz']))
        st.append((O[7][0], O[7][1], None))
        st.append((O[0][0], O[0][1], None))
        st += [(p.x, -D / 2 - roff, p.z) for p in fr]
        st.append((O[1][0], O[1][1], None))
        st.append((O[2][0], O[2][1], None))
        st += [(W / 2 + roff, p.y, p.z) for p in sd]
        st.append((O[3][0], O[3][1], None))
        st.append((O[4][0], O[4][1], None))
        st.append((O[4][0] * 0.33 + O[5][0] * 0.67, D / 2 + roff, None))
        st.append((O[5][0], O[5][1], None))
    # fill unknown z: linear along cumulative perimeter, cyclic
    n = len(st)
    xy = np.array([(a, b_) for a, b_, _ in st])
    seg = np.linalg.norm(np.roll(xy, -1, 0) - xy, axis=1)
    cum = np.concatenate([[0], np.cumsum(seg)])
    L = cum[-1]
    known = [i for i in range(n) if st[i][2] is not None]
    zs = [None] * n
    for i in known:
        zs[i] = st[i][2]
    for i in range(n):
        if zs[i] is not None:
            continue
        prv = max([k for k in known if k < i], default=known[-1])
        nxt = min([k for k in known if k > i], default=known[0])
        d0 = (cum[i] - cum[prv]) % L
        d1 = (cum[nxt] - cum[i]) % L
        zs[i] = st[prv][2] + (st[nxt][2] - st[prv][2]) * d0 / max(d0 + d1, 1e-9)
    return [Vector((st[i][0], st[i][1], zs[i])) for i in range(n)]


def lift_crossing(path, other, amount, sigma=0.010):
    """Push points of `path` outward (away from the body axis, in XY) near where it passes `other`."""
    out = []
    O = np.array([[p.x, p.y, p.z] for p in other])
    for p in path:
        d = np.min(np.linalg.norm(O - np.array([p.x, p.y, p.z]), axis=1))
        w = math.exp(-(d / sigma) ** 2) if d < 3 * sigma else 0.0
        q = p.copy()
        # outward normal of the nearest body face (front/back: y, sides: x)
        if abs(p.y) > D / 2 - 1e-4 and abs(p.x) < W / 2 - CH_XY:
            q.y += math.copysign(amount * w, p.y)
        elif abs(p.x) > W / 2 - 1e-4:
            q.x += math.copysign(amount * w, p.x)
        out.append(q)
    return out


# ------------------------------------------------------------------ build
BUD_FRAMES = []     # (centre, grille normal, x, y, RH, TH) in unsheared space, for painting the grilles


def build():
    b = Builder(ID, seed=9)
    bm = b.bm
    rng = np.random.default_rng(9)

    # ---------------- body
    f_part, s_part, t_part = b.part('front', True), b.part('shell', True), b.part('top', True)
    sc_part = b.part('screen', True)
    scr = [front_pt(u, v, 0.0) for u, v in SCREEN_PX]
    zs_bot = min(scr[2].z, scr[3].z); zs_top = max(scr[0].z, scr[1].z)
    m = 0.006
    zl = [0.0, CZ_B, zs_bot - m, zs_top + m, H - CZ_T, H]
    rings = []
    for i, z in enumerate(zl):
        if i == 0:
            pts = octagon(W - 2 * EX_B, D - 2 * EY_B, CH_XY * 0.7)
        elif i == len(zl) - 1:
            pts = octagon(W - 2 * EX_T, D - 2 * EY_T, CH_XY * 0.7)
        else:
            pts = octagon(W, D, CH_XY)
        jit = 0.0 if i == 0 else 0.0006
        rings.append([bm.verts.new((x + rng.uniform(-jit, jit), y + rng.uniform(-jit, jit) * 0.5, z + (rng.uniform(-jit, jit) if 0 < i < len(zl) - 1 else 0)))
                      for x, y in pts])
    n0 = len(bm.faces)
    front_sides = {0, 1, 7}
    for bi in range(len(zl) - 1):
        r0, r1 = rings[bi], rings[bi + 1]
        for k in range(8):
            if bi == 2 and k == 0:
                continue          # screen band front: built below with the recess
            mi = f_part if k in front_sides else s_part
            b._face([r0[k], r0[(k + 1) % 8], r1[(k + 1) % 8], r1[k]], mi)
    b._face(list(reversed(rings[0])), s_part)
    b._face(list(rings[-1]), t_part)
    # screen frame + recess
    A_, B_ = rings[2][0], rings[2][1]
    C_, D_ = rings[3][1], rings[3][0]
    yf = -D / 2
    inner = [bm.verts.new((p.x, yf, p.z)) for p in (scr[3], scr[2], scr[1], scr[0])]   # BL, BR, TR, TL
    a, bb, c, d = inner
    for quad in ([A_, B_, bb, a], [B_, C_, c, bb], [C_, D_, d, c], [D_, A_, a, d]):
        b._face(quad, f_part)
    DEPTH = 0.0016
    deep = [bm.verts.new((v.co.x + (0.0006 if i in (0, 3) else -0.0006), yf + DEPTH, v.co.z + (0.0006 if i < 2 else -0.0006)))
            for i, v in enumerate(inner)]
    for i in range(4):
        k = (i + 1) % 4
        b._face([inner[i], deep[i], deep[k], inner[k]], f_part)
    b._face(list(deep), sc_part)
    recalc(bm, new_faces_since(bm, n0))

    # ---------------- click wheel: very shallow raised 14-gon disc
    n0 = len(bm.faces)
    wc = front_pt(*WHEEL_C, 0.0)
    we = front_pt(WHEEL_C[0] + WHEEL_R, WHEEL_C[1], 0.0)
    we2 = front_pt(WHEEL_C[0], WHEEL_C[1] - WHEEL_R * 1.03, 0.0)
    rx, rz = abs(we.x - wc.x), abs(we2.z - wc.z)
    mi = b.part('wheel', True)
    ring_back = b.ring_verts(b.ring((wc.x, yf + 0.0006, wc.z), 1.0, 14, z_axis=(0, -1, 0), rot=0.1, scale=(rx, rz)))
    ring_front = b.ring_verts(b.ring((wc.x, yf - 0.0011, wc.z), 1.0, 14, z_axis=(0, -1, 0), rot=0.1, scale=(rx * 0.985, rz * 0.985)))
    b.bridge(ring_back, ring_front, mi)
    fc = b.cap(ring_front, mi)       # no back cap: it sits sunk in the front face and would only waste texels
    fs = new_faces_since(bm, n0)
    recalc(bm, fs)
    fc.normal_update()
    if fc.normal.y > 0:
        bmesh.ops.reverse_faces(bm, faces=fs)

    # ---------------- jack plug on the top face
    n0 = len(bm.faces)
    py = 0.0015
    px = hit_plane(*PLUG_BASE, (0, 1, 0), py).x
    # plug heights from the concept: base collar top ~ row 97, narrow sleeve top ~ row 63
    zc1 = hit_plane(824, 97, (0, 1, 0), py).z
    zc2 = hit_plane(828, 66, (0, 1, 0), py).z
    b.lathe('plug', [(0.0064, H - 0.002), (0.0066, zc1 - 0.0008), (0.0058, zc1), (0.0034, zc1 + 0.0004),
                     (0.0033, zc2 - 0.001), (0.0026, zc2)], sides=8, rot=0.2, offset=(px, py, 0), flat=False)
    recalc(bm, new_faces_since(bm, n0))
    plug_top = Vector((px, py, zc2))

    # ---------------- cables
    roff = R_CAB * 0.85
    up = wrap_loop(['A', 'B'], roff)
    lo = wrap_loop(['C', 'D'], roff)
    # strand A lies over strand B where they cross on the front (and the reverse on the back)
    na = len(up) // 2
    up_a, up_b = up[:na], up[na:]
    up_a = lift_crossing(up_a, up_b, 1.7 * R_CAB)
    up = up_a + up_b
    for path in (up, lo):
        n0 = len(bm.faces)
        cable(b, 'cable', path, closed=True)
        recalc(bm, new_faces_since(bm, n0))
    # plug cable: arc in the plug's vertical plane, then down the right side to the upper wrap
    arc = [hit_plane(u, v, (0, 1, 0), py) for u, v in PLUG_ARC]
    arc = [plug_top + Vector((0, 0, -0.002))] + arc[1:]
    for p in arc:
        if p.z < H + 0.01:
            p.x = max(p.x, W / 2 + R_CAB * 1.2)
    tail = [Vector((W / 2 + roff + 0.0015, py + 0.004, arc[-1].z - 0.012)), Vector((W / 2 + roff + 0.001, py + 0.007, arc[-1].z - 0.024)),
            Vector((W / 2 + roff * 0.5, py + 0.010, arc[-1].z - 0.034))]
    path = arc + tail
    n0 = len(bm.faces)
    cable(b, 'cable', path)
    recalc(bm, new_faces_since(bm, n0))

    # ---------------- earbuds
    bud_ends = []
    for bi, bd in enumerate(BUDS):
        c = hit_plane(*bd['c'], (0, 1, 0), bd['y'])
        az, tl = math.radians(bd['az']), math.radians(bd['tilt'])
        nrm = Vector((math.sin(az) * math.cos(tl), -math.cos(az) * math.cos(tl), math.sin(tl)))
        # head: low-poly squashed ball (axis = grille normal), grille side a little flatter
        n0 = len(bm.faces)
        mi = b.part('bud', False)
        RH, TH = 0.0124, 0.0090
        prof = [(-1.0, 0.0), (-0.85, 0.5), (-0.45, 0.88), (0.05, 1.0), (0.5, 0.92), (0.82, 0.7), (0.92, 0.42), (0.95, 0.0)]
        zax = nrm
        xax = Vector((0, 0, 1)).cross(zax).normalized()
        yax = zax.cross(xax)
        ringsb = []
        for t, s in prof:
            ctr = c + zax * (t * TH * (0.85 if t > 0 else 1.0))
            if s < 1e-6:
                ringsb.append([bm.verts.new(ctr)])
                continue
            pts = []
            for k in range(10):
                a = 2 * math.pi * k / 10 + 0.3
                pts.append(ctr + xax * math.cos(a) * RH * s + yax * math.sin(a) * RH * s)
            ringsb.append(b.ring_verts(pts))
        for r0, r1 in zip(ringsb, ringsb[1:]):
            if len(r0) == 1:
                for i in range(len(r1)):
                    b._face([r0[0], r1[(i + 1) % len(r1)], r1[i]], mi)
            elif len(r1) == 1:
                for i in range(len(r0)):
                    b._face([r0[i], r0[(i + 1) % len(r0)], r1[0]], mi)
            else:
                b.bridge(r0, r1, mi)
        recalc(bm, new_faces_since(bm, n0))
        BUD_FRAMES.append((c.copy(), zax.copy(), xax.copy(), yax.copy(), RH, TH))
        # stem: from inside the head down to the traced end (on the plane through the head centre, parallel to front)
        ys = min(c.y, -D / 2 - 0.0036)
        sp = [c + (-zax) * 0.003]
        for u, v in bd['stem']:
            sp.append(hit_plane(u, v, (0, 1, 0), ys))
        n0 = len(bm.faces)
        cable(b, 'bud', sp, radii=[0.0060, 0.0040, 0.0031, 0.0028][:len(sp)], sides=7)
        recalc(bm, new_faces_since(bm, n0))
        bud_ends.append(sp[-1])

    # ---------------- earbud cords down the front-right edge, ending under the lower wrap
    for ci, cpx in enumerate(CORD):
        pts = []
        start = bud_ends[ci]
        for k, (u, v) in enumerate(cpx):
            if k == 0:
                pts.append(start.copy())
                continue
            # cord rests on the front-right chamfer region: front plane in front of the wraps
            p = front_pt(u, v, R_CAB * (2.6 if ci == 0 else 2.2) + 0.0005 * k)
            if ci == 1:
                q = right_pt(u, v, R_CAB * 1.2)
                p = q if q.y > -D / 2 - R_CAB * 2.5 else p
                p.x = max(p.x, W / 2 - CH_XY + 0.001)
            pts.append(p)
        # last point tucks back toward the body under the lower wrap
        pts[-1] = pts[-1] + Vector((0, 0.003, 0))
        n0 = len(bm.faces)
        cable(b, 'cable', pts)
        recalc(bm, new_faces_since(bm, n0))

    # ---------------- shear: lean back + slight lean left (bottom stays flat)
    for v in bm.verts:
        v.co = shear(v.co)
    return b


def winding_errors(obj):
    from collections import Counter
    me = obj.data
    c = Counter()
    for p in me.polygons:
        vs = list(p.vertices)
        for i in range(len(vs)):
            c[(vs[i], vs[(i + 1) % len(vs)])] += 1
    same = sum(1 for e, k in c.items() if k > 1)
    open_ = sum(1 for (a, b_) in c if (b_, a) not in c)
    return same, open_


clear_scene()
b = build()
obj = b.finish(center_xy=False)
print('parts', list(obj['parts']), 'winding(same,open)', winding_errors(obj))
fit = check_fit(obj)
print('fit', fit)

def concept_rgba():
    """The kit's background-distance mask leaves holes where cable shadows on the body are close to the mauve
    background: fill every enclosed hole except the real opening under the plug-cable arc (top right)."""
    p = os.path.join(WORK, 'concept_rgba.png')
    rgb, m = BK.load_concept(CONCEPT)
    # the grey top face / steel side are close to the background colour: add the traced body outline
    body = np.array([(474, 178), (500, 150), (527, 140), (550, 132), (800, 146), (940, 156), (965, 170), (975, 195),
                     (977, 220), (933, 920), (920, 953), (877, 973), (850, 983), (800, 975), (550, 952), (467, 932),
                     (443, 910), (441, 880)], np.int32)
    poly = np.zeros(m.shape, np.uint8)
    cv2.fillPoly(poly, [body], 1)
    m |= poly.astype(bool)
    holes = ndimage.binary_fill_holes(m) & ~m
    lab, n = ndimage.label(holes)
    for i in range(1, n + 1):
        ys, xs = np.nonzero(lab == i)
        if not (ys.mean() < 340 and xs.mean() > 840):
            m[lab == i] = True
    cv2.imwrite(p, cv2.cvtColor(np.dstack([rgb, (m * 255).astype(np.uint8)]), cv2.COLOR_RGBA2BGRA))
    return p


CONCEPT_A = concept_rgba()
UV_WEIGHT = {'front': 1.45, 'screen': 1.6, 'wheel': 1.45, 'bud': 1.7, 'plug': 1.1, 'top': 0.8, 'shell': 0.72,
             'cable': 0.6}


def unwrap_weighted(obj, margin=0.012, angle=55):
    """Smart-project each part on its own (islands never mix parts), scale each part's islands by UV_WEIGHT
    (more texels where the painted detail is: screen, wheel, front, earbud grilles; fewer on the plain steel back
    and the repainted cables), then pack everything. Texture size stays the kit's texture_size_for."""
    me = obj.data
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    parts = list(obj['parts'])
    for pi, pname in enumerate(parts):
        bpy.ops.object.mode_set(mode='OBJECT')
        for p in me.polygons:
            p.select = p.material_index == pi
        bpy.ops.object.mode_set(mode='EDIT')
        ang = angle
        bpy.ops.uv.smart_project(angle_limit=math.radians(ang), island_margin=margin, area_weight=0.0,
                                 correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    # smart_project normalises each call to the unit square: rescale every part to true metric size x weight
    uv = me.uv_layers.active.data
    for pi, pname in enumerate(parts):
        polys = [p for p in me.polygons if p.material_index == pi]
        a3 = sum(p.area for p in polys)
        a2 = 0.0
        for p in polys:
            co = [uv[li].uv.copy() for li in p.loop_indices]
            for i in range(1, len(co) - 1):
                a2 += abs((co[i] - co[0]).cross(co[i + 1] - co[0])) / 2
        k = math.sqrt(a3 / max(a2, 1e-12)) * UV_WEIGHT.get(pname, 1.0)
        for p in polys:
            for li in p.loop_indices:
                uv[li].uv = uv[li].uv * k
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(rotate=True, margin_method='FRACTION', margin=0.0085, shape_method='CONCAVE')
    bpy.ops.object.mode_set(mode='OBJECT')


unwrap_weighted(obj)
if '--uvdump' in sys.argv:
    _P, _UV, _pt, _N = BK.mesh_arrays(obj)
    img = np.zeros((512, 512, 3), np.uint8)
    cols = [(220, 190, 150), (120, 120, 140), (200, 200, 90), (60, 160, 60), (230, 120, 120), (90, 200, 230), (240, 240, 240), (200, 90, 220)]
    for tri, pp in zip(_UV, _pt):
        q = np.round(np.c_[tri[:, 0] * 512, (1 - tri[:, 1]) * 512] * 16).astype(np.int32)
        cv2.fillPoly(img, [q], cols[pp % 8], shift=4)
    cv2.imwrite(os.path.join(WORK, 'uv.png'), img)
cam_path = os.path.join(HERE, 'camera.json')
cam = json.load(open(cam_path)) if os.path.exists(cam_path) and '--reset-cam' not in sys.argv else json.loads(json.dumps(DCAM))
if '--opt' in sys.argv:
    cam, s = BK.optimize_camera(obj, CONCEPT_A, cam, keys=('az', 'dist', 'shift'))
    json.dump(cam, open(cam_path, 'w'), indent=1)
score = BK.align_report(obj, CONCEPT_A, cam, os.path.join(OUT, 'align.png'))
print('IoU', round(score, 4), cam)


def shade_png(path, cam):
    """Quick flat-shaded model at the concept camera beside the concept (no Cycles)."""
    rgb, cm = BK.load_concept(CONCEPT)
    P, UV, part, N = BK.mesh_arrays(obj)
    idb, _ = BK.rasterize(P, part, cam, CW, CH)
    L = np.array([-0.5, -0.6, 0.65]); L /= np.linalg.norm(L)
    sh = np.clip(N @ L, 0, 1) * 0.7 + 0.25
    cols = np.array([[215, 190, 150], [150, 150, 150], [190, 180, 160], [70, 70, 50], [225, 205, 170],
                     [220, 200, 160], [235, 215, 175], [240, 225, 190]], np.float32)
    img = np.full((CH, CW, 3), 60, np.float32)
    mm = idb >= 0
    img[mm] = sh[idb[mm]][:, None] * cols[part[idb[mm]] % len(cols)]
    cv2.imwrite(path.replace('.png', '_full.png'), cv2.cvtColor(img.astype(np.uint8), cv2.COLOR_RGB2BGR))
    both = np.concatenate([rgb.astype(np.float32), img], 1)
    cv2.imwrite(path, cv2.cvtColor(cv2.resize(both, (CW, CH // 2)).astype(np.uint8), cv2.COLOR_RGB2BGR))


if '--shade' in sys.argv:
    shade_png(os.path.join(WORK, 'shade.png'), cam)
    # and from other angles
    for nm, az, el in (('shade_back.png', 160, 15), ('shade_side.png', 95, 8)):
        c2 = dict(cam); c2.update({'az': az, 'el': el, 'shift': [0, 0], 'target': [0, 0, 0.1], 'dist': 1.1})
        shade_png(os.path.join(WORK, nm), c2)
if '--diag' in sys.argv:
    _, cmk = BK.load_concept(CONCEPT_A)
    P, UV, part, N = BK.mesh_arrays(obj)
    body = np.isin(part, [list(obj['parts']).index(p) for p in ('front', 'shell', 'top', 'screen', 'wheel')])
    idb, _ = BK.rasterize(P[body], part[body], cam, CW, CH)
    mmk = idb >= 0
    def run(mask, r, c0, step):
        c = c0
        while 0 <= c < mask.shape[1] and mask[r, c]:
            c += step
        return c
    for r in (200, 300, 400, 650, 720, 880, 920):
        print('row', r, 'L concept/model', run(cmk, r, 650, -1), run(mmk, r, 650, -1), ' R', run(cmk, r, 650, 1), run(mmk, r, 650, 1))
    for c in (480, 520, 600, 700, 760, 830, 900):
        col = lambda m_: (np.nonzero(m_[:, c])[0].min() if m_[:, c].any() else -1, np.nonzero(m_[:, c])[0].max() if m_[:, c].any() else -1)
        print('col', c, 'top/bot concept', col(cmk), 'model', col(mmk))
if '--align-only' in sys.argv:
    sys.exit()

# ------------------------------------------------------------------ texture
size = BK.texture_size_for(obj)
parts = list(obj['parts'])
PLUG_C = None
for v in obj.data.vertices:
    pass
P_, UV_, part_, N_ = BK.mesh_arrays(obj)
pl = P_[part_ == parts.index('plug')].reshape(-1, 3)
PLUG_C = (float((pl[:, 0].min() + pl[:, 0].max()) / 2), float((pl[:, 1].min() + pl[:, 1].max()) / 2))
# no mirror view for the steel shell: the concept's right side is half covered by cables/buds, a mirrored copy
# put their beige shading on the left side. Hidden steel is painted in post_paint instead.
views = [{'name': 'orig', 'T': np.eye(4), 'bonus': 0.3}]
views += [{'name': f'plug{a}', 'T': BK.T_rot_z(a, PLUG_C), 'parts': ['plug']} for a in (60, -60, 120, -120, 180)]
PART_COLS = {
    'top': [(161, 135, 119), (172, 146, 129), (188, 160, 140), (206, 179, 157)],
    'shell': [(100, 86, 77), (114, 99, 89), (124, 107, 95), (136, 119, 106), (150, 133, 118)],
}


def block_noise(shape, block, seed):
    r = np.random.default_rng(seed)
    g = r.random((shape // block + 2, shape // block + 2))
    return np.kron(g, np.ones((block, block)))[:shape, :shape]


def pick(pal, t):
    pal = np.asarray(pal, np.float32)
    return pal[np.clip((t * len(pal)).astype(int), 0, len(pal) - 1)]


CAB_PAL = [(186, 152, 114), (199, 165, 126), (209, 176, 136), (218, 186, 146), (226, 195, 156)]
BUD_PAL = [(198, 166, 128), (209, 177, 138), (219, 188, 149), (228, 198, 160)]
STEEL_PAL = [(121, 109, 99), (127, 114, 104), (132, 119, 108), (138, 124, 112)]


def post_paint(tex, have, ys, xs, t, src_view, tpart):
    """Repaint materials that projection smears at this texel density: cable and earbuds (clean beige plastic,
    chunky blocks, sparse grime), earbud grilles (dark disc, rim, square holes), and the hidden steel back
    (quiet brushed grey with a few light scratches)."""
    S = tex.shape[0]
    uvp = UV_ * S
    uvp[..., 1] = S - uvp[..., 1]
    a0, a1, a2 = uvp[t, 0], uvp[t, 1], uvp[t, 2]
    px_, py_ = xs + 0.5, ys + 0.5
    den = (a1[:, 1] - a2[:, 1]) * (a0[:, 0] - a2[:, 0]) + (a2[:, 0] - a1[:, 0]) * (a0[:, 1] - a2[:, 1])
    den = np.where(np.abs(den) < 1e-12, 1e-12, den)
    l0 = np.clip(((a1[:, 1] - a2[:, 1]) * (px_ - a2[:, 0]) + (a2[:, 0] - a1[:, 0]) * (py_ - a2[:, 1])) / den, 0, 1)
    l1 = np.clip(((a2[:, 1] - a0[:, 1]) * (px_ - a2[:, 0]) + (a0[:, 0] - a2[:, 0]) * (py_ - a2[:, 1])) / den, 0, 1)
    l2 = np.clip(1 - l0 - l1, 0, 1)
    ls = l0 + l1 + l2
    X = (P_[t, 0] * l0[:, None] + P_[t, 1] * l1[:, None] + P_[t, 2] * l2[:, None]) / ls[:, None]
    Nn = N_[t]
    n1, n2, n3 = block_noise(S, 2, 21)[ys, xs], block_noise(S, 4, 22)[ys, xs], block_noise(S, 3, 23)[ys, xs]
    # cable
    m = tpart == parts.index('cable')
    tv = np.clip(0.5 + 0.55 * (n1 - 0.5) + 0.35 * (n2 - 0.5), 0, 0.999)
    col = pick(CAB_PAL, tv)
    grime = n3 > 0.93
    col[grime] = np.array([168, 134, 100], np.float32)
    tex[ys[m], xs[m]] = col[m]
    # earbuds: shell + grille
    m = tpart == parts.index('bud')
    col = pick(BUD_PAL, np.clip(0.5 + 0.5 * (n1 - 0.5) + 0.3 * (n2 - 0.5), 0, 0.999))
    col[n3 > 0.95] = np.array([178, 146, 112], np.float32)
    for c, z, xa, ya, RH, TH in BUD_FRAMES:
        cs = np.array(shear(c)); zs = np.array(shear(c + z)) - cs; zs /= np.linalg.norm(zs)
        xs3 = np.array(xa); ys3 = np.cross(zs, xs3); ys3 /= np.linalg.norm(ys3); xs3 = np.cross(ys3, zs)
        rel = X - cs
        h = rel @ zs
        r = np.linalg.norm(rel - h[:, None] * zs, axis=1)
        near = m & (np.linalg.norm(rel, axis=1) < RH * 1.3) & (h > TH * 0.35)
        rim = near & (r < 0.74 * RH) & (r >= 0.60 * RH)
        gr = near & (r < 0.60 * RH)
        col[rim] = np.array([150, 132, 114], np.float32)
        col[gr] = np.array([98, 90, 84], np.float32)
        gx, gy = rel @ xs3, rel @ ys3
        sp = 0.0029
        fx, fy = np.abs(gx / sp - np.round(gx / sp)), np.abs(gy / sp - np.round(gy / sp))
        hole = gr & (fx < 0.26) & (fy < 0.26) & (r < 0.50 * RH)
        col[hole] = np.array([46, 40, 37], np.float32)
    tex[ys[m], xs[m]] = col[m]
    # hidden steel shell: quiet grey + light scratches on the back
    m = (tpart == parts.index('shell')) & (src_view < 0)
    col = pick(STEEL_PAL, np.clip(0.5 + 0.3 * (n2 - 0.5) + 0.45 * (n1 - 0.5), 0, 0.999))
    rng = np.random.default_rng(91)
    back = Nn[:, 1] > 0.6
    for _ in range(9):
        p0 = np.array([rng.uniform(-0.045, 0.045), rng.uniform(0.02, 0.16)])
        ang = rng.uniform(-0.6, 0.6) + (math.pi / 2 if rng.random() < 0.3 else 0)
        L = rng.uniform(0.012, 0.035)
        dvec = np.array([math.cos(ang), math.sin(ang)])
        q = np.c_[X[:, 0], X[:, 2]] - p0
        s_ = np.clip(q @ dvec, 0, L)
        dist = np.linalg.norm(q - s_[:, None] * dvec, axis=1)
        col[back & (dist < 0.0008)] = np.array([152, 138, 124], np.float32)
    tex[ys[m], xs[m]] = col[m]
    # screen: the texel prefilter flattens the pixel icons; restore contrast around the dark LCD colour
    m = (tpart == parts.index('screen')) & (src_view >= 0)
    sc = tex[ys[m], xs[m]].astype(np.float32)
    base = np.median(sc, axis=0)
    tex[ys[m], xs[m]] = np.clip(base + (sc - base) * 1.35, 0, 255)
    return tex


tex_path = os.path.join(OUT, f'T_{ID}.png')
tex, st = BK.bake(obj, CONCEPT_A, cam, views=views, size=size, out_png=tex_path,
                  debug_png=os.path.join(OUT, 'coverage.png'), part_colors=PART_COLS, min_facing=0.3, post=post_paint,
                  quantize=64)
print('bake', json.dumps({k: st[k] for k in ('texture_size', 'covered_from_concept', 'views')}), json.dumps(st['parts']))

R.apply_texture(obj, tex_path, ID)
stats = check_fit(obj)
stats['texture'] = f'{size}x{size}'
stats['iou'] = round(score, 3)
if '--no-render' not in sys.argv:
    R.render_views(obj, os.path.join(OUT, 'preview.png'), concept_cam=cam, concept_size=(CW, CH), concept_path=CONCEPT)
    # kit/render.shelf_fit_render turns the item's front (-Y) away from camera B (checked: jug handle/spout swap
    # sides there too). Turn the mesh 180 deg for that render only so the shelf shows the front, then turn back.
    obj.data.transform(Matrix.Rotation(math.pi, 4, 'Z'))
    R.shelf_fit_render(obj, os.path.join(OUT, 'shelf.png'))
    obj.data.transform(Matrix.Rotation(math.pi, 4, 'Z'))
R.export(obj, OUT, ID, tex_path)
json.dump({'id': ID, 'stats': stats, 'bake': st, 'camera': cam}, open(os.path.join(OUT, 'stats.json'), 'w'), indent=1)
if '--no-render' not in sys.argv:
    R.card(OUT, ID, stats)
print('done', stats)
