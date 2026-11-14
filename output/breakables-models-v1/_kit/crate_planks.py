"""DB03 Crate debris: pieces of the iron-bound crate PR17 (also fits the forest crate PR13).
Planks are built as extruded 2D outlines (s = along grain, t = across, z = thickness) mapped through a warp
function, so jagged / splintered ends are real geometry. Atlas regions as crate.py: planksV strips (wall planks),
plankH (battens / posts), endgrain (clean saw cuts), darkpatch (iron band, nail)."""
import sys, os, math, random, json, shutil; sys.path.insert(0, '/home/claude/brk/work')
import bpy, bmesh, numpy as np
from mathutils import Vector, Matrix
import bk
from bk import PROPS as P, sub
bpy.ops.wm.read_factory_settings(use_empty=True)
M = 'M_Forest_Props'
OUT = '/home/claude/brk/out/crate_planks/'
os.makedirs(OUT, exist_ok=True)
STRIPS = [(3, 0, 18, 68), (19, 0, 33, 68), (34, 0, 47, 68), (48, 0, 63, 68)]   # the 4 painted planks of planksV
PLANK_FULL = 0.86      # PR17 wall plank length that maps the full planksV height
PLANK_W = 0.215        # PR17 wall plank width (~0.23) that maps one painted plank (inner part)
POST_FULL = 0.85       # PR17 post/batten length mapping the full plankH width
IRON = P['darkpatch']
RUSTY = sub(P['darkpatch'], 0.05, 0.5, 0.0, 0.4)   # corner of darkpatch with rust spots


def finish(b, name):
    """B.obj + triangulate (ear clip for the concave jagged outlines), flat shading."""
    o = b.obj(name)
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.triangulate(bm, faces=bm.faces, quad_method='BEAUTY', ngon_method='EAR_CLIP')
    bm.to_mesh(o.data); bm.free()
    for p in o.data.polygons: p.use_smooth = False
    return o


def split_poly(n, chords):
    """Partition polygon 0..n-1 by non-crossing chords (i, j) into index lists."""
    polys = [list(range(n))]
    for i, j in chords:
        for k, p in enumerate(polys):
            if i in p and j in p:
                a, c = sorted((p.index(i), p.index(j)))
                polys[k:k + 1] = [p[a:c + 1], p[c:] + p[:a + 1]]
                break
    return polys


def slab(b, outline, th, F, top_uv, bot_uv, side_uv, end_uv=None, clean=(), m=0, chords=()):
    """Extrude a closed 2D outline [(s,t)] between per-vertex thickness pairs th[i] = (z_bottom, z_top).
    F(s,t,z) -> world point. top_uv/bot_uv(s,t); side_uv(g, frac) with g = coordinate along the grain;
    end_uv(t, frac) used for edges in `clean` (index i = edge i -> i+1), i.e. clean saw-cut end grain."""
    n = len(outline)
    T = [tuple(F(s, t, th[i][1])) for i, (s, t) in enumerate(outline)]
    Bt = [tuple(F(s, t, th[i][0])) for i, (s, t) in enumerate(outline)]
    for poly in split_poly(n, chords):     # cross chords keep the facets of the warped faces tidy
        b.face([T[i] for i in poly], [top_uv(*outline[i]) for i in poly], m)
        b.face([Bt[i] for i in poly][::-1], [bot_uv(*outline[i]) for i in poly][::-1], m)
    for i in range(n):
        j = (i + 1) % n
        (si, ti), (sj, tj) = outline[i], outline[j]
        pts = [Bt[i], Bt[j], T[j], T[i]]
        if i in clean and end_uv:
            uvs = [end_uv(ti, 0), end_uv(tj, 0), end_uv(tj, 1), end_uv(ti, 1)]
        else:
            along = abs(sj - si) >= 0.5 * abs(tj - ti)
            gi, gj = (si, sj) if along else (si + ti, sj + tj)
            uvs = [side_uv(gi, 0), side_uv(gj, 0), side_uv(gj, 1), side_uv(gi, 1)]
        b.face(pts, uvs, m)


def plank_warp(L, bow=0.0, twist=0.0, edge=0.0, s0=None):
    """Warp along the plank: bow (z sag at the middle), twist (deg end to end), edge bend (t)."""
    s0 = L / 2 if s0 is None else s0
    def F(s, t, z):
        u = (s - s0) / L
        a = math.radians(twist) * u * 2
        z2 = z + bow * (1 - 4 * u * u)
        t2 = t + edge * (1 - 4 * u * u)
        return Vector((s, t2 * math.cos(a) - z2 * math.sin(a), t2 * math.sin(a) + z2 * math.cos(a)))
    return F


def plank_uvs(strip, W, v0, strip_bot, v0b, tw=0.0):
    """planksV strip, grain along v. Texel density as the PR17 wall planks."""
    fu = lambda t: 0.5 + t / PLANK_W + tw
    top = lambda s, t: bk.uvr(M, strip, fu(t), v0 + s / PLANK_FULL)
    bot = lambda s, t: bk.uvr(M, strip_bot, 1 - fu(t), v0b + s / PLANK_FULL)
    narrow = sub(strip, 0.0, 0.2)
    side = lambda g, f: bk.uvr(M, narrow, f, v0 + g / PLANK_FULL)
    end = lambda t, f: bk.uvr(M, P['endgrain'], 0.5 + t / W * 0.9, 0.3 + 0.4 * f)
    return top, bot, side, end


def sides_outline(stations, W, rnd, j=0.002):
    """-t side (s increasing) stations; returns list of points."""
    return [(s, -W / 2 + rnd.uniform(-j, j)) for s in stations]


def thick(outline, T, rnd, j=0.0015, special=None):
    th = []
    for i, (s, t) in enumerate(outline):
        if special and i in special:
            th.append(special[i]); continue
        th.append((-T / 2 + rnd.uniform(-j, j), T / 2 + rnd.uniform(-j, j)))
    return th


def bar(b, path, w, h, up, m=0, rect=IRON, per=1.2, tear=(0, 0)):
    """Iron strip along a 2D path given as 3D points with a per-point 'up' normal (list) — rectangle section
    w (across, along `side` axis) x h (along up). Closed with end caps. tear shifts the first cap corners."""
    n = len(path)
    rings = []
    for i, p in enumerate(path):
        p = Vector(p); u = Vector(up[i]).normalized()
        d = (Vector(path[min(i + 1, n - 1)]) - Vector(path[max(i - 1, 0)])).normalized()
        sdir = d.cross(u).normalized()
        a = [p - sdir * w / 2, p + sdir * w / 2, p + sdir * w / 2 + u * h, p - sdir * w / 2 + u * h]
        if i == 0:
            a[0] = a[0] + d * tear[0]; a[3] = a[3] + d * tear[0]
            a[1] = a[1] + d * tear[1]; a[2] = a[2] + d * tear[1]
        rings.append([tuple(x) for x in a])
    acc = [0.0]
    for i in range(1, n): acc.append(acc[-1] + (Vector(path[i]) - Vector(path[i - 1])).length)
    fv = [x / per for x in acc]
    uvA = lambda fu_, fv_: bk.uvr(M, rect, fu_, fv_)
    for i in range(n - 1):
        r0, r1 = rings[i], rings[i + 1]
        # bottom(0-1), side(1-2), top(2-3), side(3-0)
        for k, (fa, fb) in enumerate(((0, 1), (0.9, 1), (0, 1), (0, 0.1))):
            a, c = k, (k + 1) % 4
            b.face([r0[a], r0[c], r1[c], r1[a]], [uvA(fa, fv[i]), uvA(fb, fv[i]), uvA(fb, fv[i + 1]), uvA(fa, fv[i + 1])], m)
    b.face(rings[0][::-1], [uvA(0, 0), uvA(0.1, 0), uvA(0.1, 0.1), uvA(0, 0.1)], m)
    b.face(rings[-1], [uvA(0, fv[-1]), uvA(0.1, fv[-1]), uvA(0.1, fv[-1] + .1), uvA(0, fv[-1] + .1)], m)


def nail(b, pts, r, m=0, sides=3, rot=0.0, tip=True):
    """Bent nail: `sides`-gon prism through pts (last = tip), start capped."""
    rings = []
    n = len(pts)
    for i in range(n - 1 if tip else n):
        p = Vector(pts[i])
        d = (Vector(pts[min(i + 1, n - 1)]) - Vector(pts[max(i - 1, 0)])).normalized()
        if i == 0: a0 = d.orthogonal().normalized()
        a = (a0 - d * a0.dot(d)).normalized(); c = d.cross(a)
        rings.append([tuple(p + (a * math.cos(rot + 2 * math.pi * k / sides) + c * math.sin(rot + 2 * math.pi * k / sides)) * r)
                      for k in range(sides)])
    tipp = tuple(pts[-1])
    uv = lambda fu_, fv_: bk.uvr(M, RUSTY, fu_, fv_)
    for i in range(len(rings) - 1):
        for k in range(sides):
            k2 = (k + 1) % sides
            b.face([rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k]],
                   [uv(k / sides, i / n), uv((k + 1) / sides, i / n), uv((k + 1) / sides, (i + 1) / n), uv(k / sides, (i + 1) / n)], m)
    if tip:
        for k in range(sides):
            k2 = (k + 1) % sides
            b.face([rings[-1][k], rings[-1][k2], tipp], [uv(k / sides, .8), uv((k + 1) / sides, .8), uv(.5, 1)], m)
    else:
        b.face(rings[-1], [uv(0.2, 1), uv(0.5, 0.9), uv(0.8, 1)], m)
    b.face(rings[0][::-1], [uv(0.2, 0), uv(0.5, 0.1), uv(0.8, 0)], m)


def head(b, c, size, h, up, m=0, rot=0.0):
    """Small square nail head (closed box)."""
    c = Vector(c); u = Vector(up).normalized(); a = u.orthogonal().normalized()
    a = Matrix.Rotation(rot, 3, u) @ a; d = u.cross(a)
    q = [c + (a * x + d * y) * size / 2 for x, y in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    lo = [tuple(p) for p in q]; hi = [tuple(p + u * h) for p in q]
    uv = lambda fu_, fv_: bk.uvr(M, RUSTY, fu_, fv_)
    sq = [uv(0, 0), uv(.3, 0), uv(.3, .3), uv(0, .3)]
    b.face(hi, sq, m); b.face(lo[::-1], sq, m)
    for k in range(4):
        k2 = (k + 1) % 4
        b.face([lo[k], lo[k2], hi[k2], hi[k]], [uv(0, 0), uv(.3, 0), uv(.3, .05), uv(0, .05)], m)


pieces = []
# ------------------------------------------------------------------ 1) short whole plank (warped/twisted)
rnd = random.Random(31)
L, W, T = 0.40, 0.196, 0.025
st = [0.0, 0.08, 0.16, 0.24, 0.32]
ol = [(0.0 + 0.012, -W / 2 + 0.001)]                       # chipped corner at s=0,-t
ol += [(s, -W / 2 + rnd.uniform(-.002, .002)) for s in st[1:]]
ol += [(L + 0.004, -W / 2)]                                # slightly skew saw cut at the far end
ol += [(L, 0.022), (L - 0.055, 0.026), (L - 0.002, 0.031)]  # a short split running in from the end
ol += [(L - 0.008, W / 2)]
ol += [(s, W / 2 + rnd.uniform(-.002, .002)) for s in st[::-1][:-1]]
ol += [(0.0, W / 2 - 0.004), (-0.002, -W / 2 + 0.016)]
th = thick(ol, T, rnd)
F = plank_warp(L, bow=0.011, twist=7.0, edge=0.004)
top, bot, side, end = plank_uvs(STRIPS[1], W, 0.42, STRIPS[3], 0.1)
cl = {i for i in range(len(ol)) if abs(ol[i][0] - ol[(i + 1) % len(ol)][0]) < 0.01 and abs(ol[i][1] - ol[(i + 1) % len(ol)][1]) > 0.02}
ch = [(k, len(ol) - 2 - k) for k in range(1, 5)] 
b = bk.B([M]); slab(b, ol, th, F, top, bot, side, end, clean=cl, chords=ch)
pieces.append((finish(b, 'DB03_Crate_PlankShort'),
               'Short whole wall plank 0.40 x 0.196 x 0.025 m (PR17 plank section), bowed ~1 cm and twisted 7 deg; '
               'one chipped corner, short split at the far end; planksV strip at PR17 texel density, endgrain saw cuts.'))

# ------------------------------------------------------------------ 2) splintered plank
rnd = random.Random(57)
L, W, T = 0.36, 0.198, 0.025
ol = [(s, -W / 2 + rnd.uniform(-.002, .002)) for s in (0.0, 0.09, 0.18, 0.27)] + [(0.335, -W / 2)]
jag = [((0.415, -0.086), 'tip'), ((0.352, -0.070), 'v'), ((0.468, -0.047), 'tip'), ((0.375, -0.026), 'v'),
       ((0.535, -0.004), 'tip'), ((0.392, 0.014), 'v'), ((0.452, 0.040), 'tip'), ((0.366, 0.056), 'v'),
       ((0.428, 0.080), 'tip')]
k0 = len(ol)
ol += [p for p, _ in jag] + [(0.318, W / 2)]
ol += [(s, W / 2 + rnd.uniform(-.002, .002)) for s in (0.27, 0.18, 0.09, 0.0)]
special = {}
lift = [0.006, 0, -0.008, 0, 0.016, 0, -0.004, 0, 0.010]   # splinters curl up/down a little
layer = [1, 0, -1, 0, 1, 0, 0, 0, -1]                       # which thickness layer the splinter comes from
for i, (p, kind) in enumerate(jag):
    if kind == 'tip':
        tt = rnd.uniform(0.004, 0.007); zc = layer[i] * (T / 2 - tt / 2) * 0.8 + lift[i]
        special[k0 + i] = (zc - tt / 2, zc + tt / 2)
    else:
        special[k0 + i] = (-T / 2 + rnd.uniform(0, .006), T / 2 - rnd.uniform(0, .006))
th = thick(ol, T, rnd, special=special)
F = plank_warp(0.5, bow=-0.006, twist=-4.0, edge=-0.003, s0=0.25)
top, bot, side, end = plank_uvs(STRIPS[2], W, 0.05, STRIPS[0], 0.3)
ch = [(k, len(ol) - 1 - k) for k in range(1, 4)]
b = bk.B([M]); slab(b, ol, th, F, top, bot, side, end, clean={len(ol) - 1}, chords=ch)
pieces.append((finish(b, 'DB03_Crate_PlankSplintered'),
               'Broken wall plank ~0.53 m: clean saw-cut end, the other end torn into 5 long pointy splinters '
               '(real geometry, splinter tips thinned to 4-7 mm and curled up/down from top/bottom layers).'))

# ------------------------------------------------------------------ 3) plank with bent rusty nail + iron band scrap
rnd = random.Random(83)
L, W, T = 0.34, 0.194, 0.025
ol = [(s, -W / 2 + rnd.uniform(-.002, .002)) for s in (0.0, 0.1, 0.2)] + [(0.285, -W / 2)]
jag = [(0.335, -0.074), (0.300, -0.050), (0.362, -0.016), (0.312, 0.012), (0.345, 0.046), (0.296, 0.070)]
k0 = len(ol)
ol += jag + [(0.302, W / 2)]
ol += [(s, W / 2 + rnd.uniform(-.002, .002)) for s in (0.2, 0.1, 0.0)]
special = {k0 + i: ((-0.006, 0.010) if i % 2 == 0 else (-T / 2 + .003, T / 2 - .002)) for i in range(len(jag))}
th = thick(ol, T, rnd, j=0.0008, special=special)
F = plank_warp(L, bow=0.007, twist=-5.0, edge=0.003)
top, bot, side, end = plank_uvs(STRIPS[0], W, 0.0, STRIPS[2], 0.55)
ch = [(k, len(ol) - 1 - k) for k in range(1, 3)]
b = bk.B([M]); slab(b, ol, th, F, top, bot, side, end, clean={len(ol) - 1}, chords=ch)
# iron band scrap lying along the grain on the outer face, torn end bent up off the clean end
tb = 0.040; zt = T / 2 + 0.0016
path_l = [(0.205, tb, zt), (0.06, tb, zt), (0.0, tb, zt), (-0.024, tb + 0.004, zt + 0.040)]
path = [tuple(F(*p)) for p in path_l]
ups = []
for i, p in enumerate(path_l):
    q0 = F(p[0], p[1], p[2]); q1 = F(p[0], p[1], p[2] + 0.01)
    ups.append(tuple((q1 - q0).normalized()))
ups[-1] = tuple((Vector(ups[-1]) + Vector((1.4, 0, 0))).normalized())   # bent segment tilts with the bend
bar(b, path, 0.055, 0.0065, ups, rect=IRON, per=1.2, tear=(0.0, 0.018))
# nail: half pulled out of the plank near the broken end, sticking up and bent over, head at its end
ns, nt = 0.245, -0.048
nl = [(ns, nt, 0.002), (ns - 0.003, nt, T / 2 + 0.036), (ns + 0.012, nt + 0.004, T / 2 + 0.058),
      (ns + 0.040, nt + 0.010, T / 2 + 0.066)]
npts = [F(*p) for p in nl]
dlast = (npts[-1] - npts[-2]).normalized()
npts[-1] = npts[-1] + dlast * 0.0015
nail(b, [tuple(p) for p in npts], 0.0034, sides=3, tip=False)
head(b, npts[-1] - dlast * 0.0015, 0.015, 0.004, dlast, rot=0.3)
pieces.append((finish(b, 'DB03_Crate_PlankNail'),
               'Wall plank piece ~0.36 m, short broken end; a scrap of the iron band (55 mm, darkpatch) still on it, '
               'torn end bent up; a half-pulled rusty nail (3-sided bent prism + head, darkpatch) sticking up near the break.'))

# ------------------------------------------------------------------ 4) corner block: post + two batten stubs at 90 deg
rnd = random.Random(101)
PS, PH = 0.068, 0.30
hP = P['plankH']
post_u0 = 0.0
def post_uv(z, a):       # plankH, grain along u (z), a = 0..1 across the face
    return bk.uvr(M, hP, post_u0 + z / POST_FULL, a)
b = bk.B([M])
J = lambda: rnd.uniform(-0.002, 0.002)
base = [(0 + J(), 0 + J()), (PS + J(), 0 + J()), (PS + J(), PS + J()), (0 + J(), PS + J())]
base = [(x, y, 0.0 + J()) for x, y in base]
# jagged crown: slanted break (front higher), corner splinter at the outer corner
ring_h = [0.352, 0.284, 0.306, 0.262, 0.236, 0.251, 0.226, 0.272]
ring_xy = [(0, 0), (PS * 0.42, -0.001), (PS, 0), (PS + 0.001, PS * 0.45), (PS, PS), (PS * 0.55, PS + 0.001), (0, PS), (-0.001, PS * 0.38)]
ring = [(x + J(), y + J(), h) for (x, y), h in zip(ring_xy, ring_h)]
cen = (PS * 0.5, PS * 0.42, 0.268)
pf = lambda p: (p[0], p[1], p[2])
# bottom (clean saw cut -> endgrain)
b.face([base[3], base[2], base[1], base[0]],
       [bk.uvr(M, P['endgrain'], u, v) for u, v in ((0.1, 0.9), (0.9, 0.9), (0.9, 0.1), (0.1, 0.1))])
for k in range(4):
    b0, b1 = base[k], base[(k + 1) % 4]
    r0, rm, r1 = ring[2 * k], ring[2 * k + 1], ring[(2 * k + 2) % 8]
    pts = [b0, b1, r1, rm, r0]
    uvs = [post_uv(b0[2], 0.05), post_uv(b1[2], 0.95), post_uv(r1[2], 0.95), post_uv(rm[2], 0.5), post_uv(r0[2], 0.05)]
    b.face(pts, uvs)
for k in range(8):
    a, c = ring[k], ring[(k + 1) % 8]
    b.face([a, c, cen], [post_uv(a[2], .2 + .1 * (k % 2)), post_uv(c[2], .5), post_uv(cen[2] + 0.02, .8)])

# batten stubs (plankH, grain along u). A on the front face (y<0) running +x, B on the left face (x<0) running +y.
BH, BT = 0.084, 0.023
z0 = 0.028
def batten_uv(u0):
    top = lambda s, t: bk.uvr(M, hP, u0 + s / POST_FULL, (t - z0) / BH)
    bot = lambda s, t: bk.uvr(M, hP, u0 + 0.3 + s / POST_FULL, 1 - (t - z0) / BH)
    narrow = sub(hP, 0.0, 1.0, 0.0, 0.25)
    side = lambda g, f: bk.uvr(M, narrow, u0 + g / POST_FULL, f)
    end = lambda t, f: bk.uvr(M, P['endgrain'], 0.2 + 0.6 * (t - z0) / BH, 0.3 + 0.4 * f)
    return top, bot, side, end
olA = [(-BT - 0.0007, z0), (0.145, z0 + 0.001), (0.205, z0 + 0.012), (0.160, z0 + 0.026), (0.228, z0 + 0.043),
       (0.166, z0 + 0.055), (0.190, z0 + 0.070), (0.150, BH + z0 - 0.002), (-BT - 0.0007, BH + z0)]
thA = [(-0.0012, BT)] * len(olA)
thA = [(a, c if i not in (2, 4, 6) else c - 0.010) for i, (a, c) in enumerate(thA)]
thA = [(a + (0.006 if i in (4,) else 0), c) for i, (a, c) in enumerate(thA)]
# A: s -> x, t -> z, thickness -> -y (outer face at y = -BT)
FA = lambda s, t, z: Vector((s, -z + 0.0004 * s, t + 0.02 * s * s))
top, bot, side, end = batten_uv(0.0)
slab(b, olA, thA, FA, top, bot, side, end, clean={len(olA) - 1})
olB = [(0.0006, z0 + 0.002), (0.112, z0 + 0.001), (0.168, z0 + 0.018), (0.124, z0 + 0.034), (0.146, z0 + 0.052),
       (0.106, z0 + 0.064), (0.130, BH + z0 - 0.004), (0.0006, BH + z0 - 0.001)]
thB = [(-0.0012, BT)] * len(olB)
thB = [(a + (0.009 if i in (2, 4) else 0), c) for i, (a, c) in enumerate(thB)]
ang = math.radians(7)   # B pried a little off the post
FB = lambda s, t, z: Vector((-z - s * math.sin(ang), s, t - 0.003))
top, bot, side, end = batten_uv(0.62)
slab(b, olB, thB, FB, top, bot, side, end)
o = finish(b, 'DB03_Crate_Corner')
pieces.append((o, 'Corner post piece 0.30 m, 68 mm square (PR17 post, plankH grain along the post), saw-cut bottom, '
                  'jagged broken top with a tall splinter; two 84x23 mm batten stubs nailed on at 90 deg (one pried '
                  'out 7 deg), both with jagged broken ends.'))

# ------------------------------------------------------------------ colliders, layout, export
objs = []
x = 0.0
for i, (o, note) in enumerate(pieces):
    lo, hi = bk.bounds([o])
    w = float(hi[0] - lo[0])
    if i: x += w
    o.location = Vector((x, 0, float(hi[2] - lo[2]) / 2))
    x += w + 0.1
bpy.context.view_layer.update()
for o, note in pieces:
    c = bk.col(o)
    objs += [o, c]
notes = {o.name: n for o, n in pieces}
rep = bk.report(objs)
for r in rep:
    if r['name'] in notes: r['note'] = notes[r['name']]
    if r['name'].endswith('_COL'): r['note'] = 'convex hull of the piece'
print(json.dumps(rep, indent=1))
bk.export_fbx(objs, OUT + 'SM_DB03_CratePlanks.fbx')
json.dump({'set': 'DB03 Crate', 'source_props': ['SM_PR17_CrateBound', 'SM_PR13_CrateClosed'],
           'fbx': 'SM_DB03_CratePlanks.fbx', 'pieces': rep}, open(OUT + 'manifest.json', 'w'), indent=1)
shutil.copy(__file__, OUT + 'build.py')
bk.save_blend(OUT + 'crate_planks.blend')

# ------------------------------------------------------------------ card
if '--norender' not in sys.argv:
    tiles, labels = [], []
    for o, _ in pieces:
        for az, el in ((-35, 35), (145, 40)):
            tiles.append(bk.render_objs([o], OUT + f'_t_{o.name}_{az}.png', az=az, el=el, size=(360, 360), samples=8))
            labels.append(f'{o.name} {az}/{el}')
    # scale shot: pieces lying next to PR17 (new crate) and PR13 (forest crate)
    def imp(path):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=path)
        new = [o for o in bpy.data.objects if o not in before]
        ms = [o for o in new if o.type == 'MESH']
        for o in new:
            if o.type != 'MESH': bpy.data.objects.remove(o)
        for o in ms:
            for i, s in enumerate(o.material_slots): o.material_slots[i].material = bk.mat(s.material.name.split('.')[0])
            o.data.transform(o.matrix_world); o.matrix_world = Matrix.Identity(4)
        return [o for o in ms if not o.name.endswith('_COL')]
    crA = imp('/home/claude/brk/out/crate/SM_PR17_CrateBound.fbx')
    crB = imp('/home/claude/brk/ref/SM_PR13_CrateClosed.fbx')
    for o in crB: o.location = (-0.85, 0.25, 0)
    saved = {o.name: (o.location.copy(), o.rotation_euler.copy()) for o, _ in pieces}
    lay = [((0.75, -0.35), 25), ((0.85, 0.15), -60), ((0.55, -0.75), 80), ((1.15, -0.55), 10)]
    for (o, _), ((px, py), rz) in zip(pieces, lay):
        lo, hi = bk.bounds([o])
        o.rotation_euler = (0, 0, math.radians(rz))
        o.location = (px, py, float(hi[2] - lo[2]) / 2)
    bpy.context.view_layer.update()
    grp = [o for o, _ in pieces] + crA + crB
    tiles.append(bk.render(grp, OUT + '_t_g1.png', az=-20, el=30, size=(360, 360), samples=8))
    tiles.append(bk.render([o for o, _ in pieces] + crA, OUT + '_t_g2.png', az=40, el=38, size=(360, 360), samples=8))
    labels += ['scale: PR13 | PR17 + pieces', 'pieces next to PR17']
    for o, _ in pieces:
        o.location, o.rotation_euler = saved[o.name]
    for o in crA + crB: bpy.data.objects.remove(o)
    bk.sheet(tiles, OUT + 'card.jpg', cols=4, labels=labels, title='DB03 Crate debris  (PR17 iron-bound crate / PR13)')
    for f in os.listdir(OUT):
        if f.startswith('_t_'): os.remove(OUT + f)
