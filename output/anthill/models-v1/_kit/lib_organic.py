"""Shared helpers for the organic anthill assets (AN09, AN11-13, AN37-41). Uses the kit (ak.py) unchanged."""
import sys, math, random, os, json
sys.path.insert(0, '/home/claude/ant/kit')
import ak
import bpy, bmesh
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

SHEETS = '/home/claude/ant/concepts/environment-pack-v3/asset-concepts-v1/sheets/'
S = 512.0


# ---------------------------------------------------------------- atlas uv helpers
def ruv(region, fx, fy):
    """uv inside an atlas region; fx 0..1 left->right, fy 0..1 image TOP->BOTTOM of the region."""
    u0, v0, u1, v1 = ak.Mesh.region(region)
    return (u0 + fx * (u1 - u0), v1 - fy * (v1 - v0))


def sub(region, px):
    """sub-rect of a region in region-pixel coords (x0, y0, x1, y1), image orientation -> (region, px) spec"""
    return (region, px)


def suv(spec, fx, fy):
    region, px = spec
    x0, y0, x1, y1 = px
    return ruv_px(region, x0 + fx * (x1 - x0), y0 + fy * (y1 - y0))


def ruv_px(region, x, y):
    rx, ry, rw, rh = ak.REG['T_Ant_Props.png']['regions'][region]
    e = 0.6
    x = min(max(x, e), rw - e); y = min(max(y, e), rh - e)
    return ((rx + x) / S, 1 - (ry + y) / S)


SUB_CLEAN = ('substrate', (50, 16, 126, 70))      # substrate without mycelium veins
SUB_VEINS = ('substrate', (6, 20, 126, 126))       # the big vein figure


def frame(n):
    n = Vector(n).normalized()
    a = Vector((0, 0, 1)) if abs(n.z) < 0.9 else Vector((1, 0, 0))
    x = (a - n * a.dot(n)).normalized()
    return x, n.cross(x)


def fit_face(mesh, pts, spec, metres=0.8, rnd=None, wind=None):
    """Planar-project a polygon and place it inside an atlas sub-rect (random offset/rotation). metres = world size
    covered by the whole sub-rect (shrinks if the face is bigger)."""
    rnd = rnd or random.Random(0)
    P = [Vector(p) for p in pts]
    c = sum(P, Vector()) / len(P)
    n = Vector((0, 0, 0))
    for i in range(len(P)):
        n += (P[i] - c).cross(P[(i + 1) % len(P)] - c)
    if n.length < 1e-9:
        n = Vector((0, 0, 1))
    x, y = frame(n)
    ang = rnd.uniform(0, 2 * math.pi)
    ca, sa = math.cos(ang), math.sin(ang)
    loc = []
    for p in P:
        d = p - c
        a, b = d.dot(x), d.dot(y)
        loc.append((a * ca - b * sa, a * sa + b * ca))
    ext = max(max(abs(a) for a, b in loc), max(abs(b) for a, b in loc), 1e-6)
    sc = min(1.0 / metres, 0.48 / ext)
    ox = rnd.uniform(-(0.5 - ext * sc), (0.5 - ext * sc)) if ext * sc < 0.5 else 0
    oy = rnd.uniform(-(0.5 - ext * sc), (0.5 - ext * sc)) if ext * sc < 0.5 else 0
    uvs = [suv(spec, 0.5 + ox + a * sc, 0.5 + oy + b * sc) for a, b in loc]
    return mesh.face(P, uvs, wind)


# ---------------------------------------------------------------- geometry helpers
def rings_frame(path, radii, sides, rot=0.0, jitter=0.0, seed=0, scale=None, up_hint=None):
    rnd = random.Random(seed)
    P = [Vector(p) for p in path]
    n = len(P)
    if not isinstance(radii, (list, tuple)):
        radii = [radii] * n
    rings = []
    prev = None
    for i in range(n):
        t = (P[min(i + 1, n - 1)] - P[max(i - 1, 0)]).normalized()
        if prev is None:
            x = Vector(up_hint) if up_hint else (Vector((0, 0, 1)) if abs(t.z) < .9 else Vector((1, 0, 0)))
        else:
            x = prev
        x = (x - t * x.dot(t)).normalized(); prev = x
        y = t.cross(x)
        sx, sy = scale[i] if scale else (1, 1)
        ring = []
        for k in range(sides):
            a = rot + 2 * math.pi * k / sides
            p = P[i] + x * math.cos(a) * radii[i] * sx + y * math.sin(a) * radii[i] * sy
            p += Vector((rnd.uniform(-jitter, jitter), rnd.uniform(-jitter, jitter), rnd.uniform(-jitter, jitter)))
            ring.append(p)
        rings.append(ring)
    return rings


def atlas_tube(mesh, path, radii, sides, region, along='v', fy_range=(0.0, 1.0), wraps=1.0, jitter=0.0, seed=0,
               scale=None, caps=(False, False), cap_region=None, rot=0.0, fy_fn=None):
    """Tube on the Props atlas. along='v': length runs along image y of region (fy_range from base to tip),
    circumference along x. along='u': length along image x, circumference along y."""
    rings = rings_frame(path, radii, sides, rot, jitter, seed, scale)
    P = [Vector(p) for p in path]
    L = [0.0]
    for i in range(1, len(P)):
        L.append(L[-1] + (P[i] - P[i - 1]).length)
    T = [l / L[-1] for l in L]
    for i in range(len(rings) - 1):
        for k in range(sides):
            k2 = (k + 1) % sides
            a0 = (k / sides * wraps) % 1.0
            a1 = a0 + wraps / sides
            if a1 > 1.0001:
                a0, a1 = 0.0, wraps / sides
            f0 = fy_range[0] + T[i] * (fy_range[1] - fy_range[0])
            f1 = fy_range[0] + T[i + 1] * (fy_range[1] - fy_range[0])
            if fy_fn:   # per-vertex mapping (t along, k around)
                uv = [ruv(region, a0, fy_fn(T[i], k)), ruv(region, a1, fy_fn(T[i], k2)),
                      ruv(region, a1, fy_fn(T[i + 1], k2)), ruv(region, a0, fy_fn(T[i + 1], k))]
                mesh.quad(rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k], *uv)
                continue
            if along == 'v':
                uv = [ruv(region, a0, f0), ruv(region, a1, f0), ruv(region, a1, f1), ruv(region, a0, f1)]
            else:
                uv = [ruv(region, f0, a0), ruv(region, f0, a1), ruv(region, f1, a1), ruv(region, f1, a0)]
            mesh.quad(rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k], *uv)
    if caps[0]:
        mesh.cap(rings[0], flip=True, region=cap_region)
    if caps[1]:
        mesh.cap(rings[-1], region=cap_region)
    return rings


def clamp(mesh, axis=2, lo=None, hi=None):
    for v in mesh.bm.verts:
        c = v.co[axis]
        if lo is not None and c < lo:
            v.co[axis] = lo
        if hi is not None and c > hi:
            v.co[axis] = hi


def recalc(mesh):
    """Weld + make normals consistent/outward (use on closed volumes)."""
    bmesh.ops.remove_doubles(mesh.bm, verts=mesh.bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(mesh.bm, faces=mesh.bm.faces)


def drop_bottom(mesh, z=0.0, eps=1e-4):
    bad = [f for f in mesh.bm.faces if all(v.co.z < z + eps for v in f.verts)]
    bmesh.ops.delete(mesh.bm, geom=bad, context='FACES')


def drop_degenerate(mesh, eps=1e-7):
    bad = [f for f in mesh.bm.faces if f.calc_area() < eps]
    bmesh.ops.delete(mesh.bm, geom=bad, context='FACES')


def densify(path, radii, k=2, wob=0.04, seed=0):
    """Insert k-1 points per segment (Catmull-Rom-ish) with small random lateral wobble; radii interpolated."""
    rnd = random.Random(seed)
    P = [Vector(p) for p in path]
    n = len(P)
    out, rr = [], []
    for i in range(n - 1):
        p0, p1, p2, p3 = P[max(i - 1, 0)], P[i], P[i + 1], P[min(i + 2, n - 1)]
        for j in range(k):
            t = j / k
            t2, t3 = t * t, t * t * t
            q = 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3)
            if j > 0:
                q += Vector((rnd.uniform(-wob, wob), rnd.uniform(-wob, wob), rnd.uniform(-wob, wob) * 0.5))
            out.append(q)
            rr.append(radii[i] + (radii[i + 1] - radii[i]) * t)
    out.append(P[-1]); rr.append(radii[-1])
    return out, rr


# ---------------------------------------------------------------- roots
def slant(ring, center, t, n):
    t = Vector(t).normalized(); n = Vector(n).normalized(); c = Vector(center)
    return [p + t * ((c - p).dot(n) / t.dot(n)) for p in ring]


def root(mesh, props, path, radii, sides=7, seed=0, jitter=0.03, scale=None, cut=None, rot=0.0, up_hint=None):
    """Root tube; cut = tilt vector for an end-grain cap at the tip (None = closed bark cap / buried)."""
    if scale is None and len(path) >= 3:
        path, radii = densify(path, radii, 2, wob=0.03, seed=seed)
    rings = mesh.tube(path, radii, sides=sides, jitter=jitter, seed=seed, caps=(True, cut is None), scale=scale,
                      rot=rot, up_hint=up_hint)
    if cut is not None:
        P = [Vector(p) for p in path]
        t = (P[-1] - P[-2]).normalized()
        n = (t + Vector(cut)).normalized()
        last = slant(rings[-1], P[-1], t, n)
        # rebuild last segment with slanted ring: move verts of the existing mesh
        for v in mesh.bm.verts:
            for i, p in enumerate(rings[-1]):
                if (v.co - p).length < 1e-6:
                    v.co = last[i]
        props.cap(last, region='root_end')
    return rings


def toe(mesh, base, ang, length, h0=1.0, r0=0.36, seed=0, sides=6, drop=0.0):
    a = math.radians(ang)
    d = Vector((math.cos(a), math.sin(a), 0))
    B = Vector(base)
    side = Vector((-d.y, d.x, 0)) * (0.08 * math.sin(seed * 1.7))
    path = [B + d * 0.05 + Vector((0, 0, h0)), B + d * (length * 0.35) + side + Vector((0, 0, h0 * 0.55)),
            B + d * (length * 0.62) - side + Vector((0, 0, h0 * 0.25)), B + d * (length * 0.85) + Vector((0, 0, 0.08)),
            B + d * length + Vector((0, 0, -0.08 - drop))]
    mesh.tube(path, [r0 * 0.72, r0, r0 * 0.8, r0 * 0.6, r0 * 0.45], sides=sides, jitter=0.03, seed=seed,
              caps=(True, True), scale=[(1.2, 1.0), (1.3, 0.95), (1.3, 0.9), (1.2, 0.9), (1.05, 0.95)],
              up_hint=(0, 0, 1), rot=math.pi / sides)



# ---------------------------------------------------------------- mushroom caps
def cap(mesh, attach, R, H, sides=10, tilt=(0.0, 0.0), spin=0.0, seed=0, top='cap_mint', under='gill_glow',
        stem_r=0.1, droop=0.18, lump=0.08, rim=0.12, sxy=(1.0, 1.0), under_depth=0.0, flat_top=0.0,
        bumps=None, glow_rim=False, chips=0, spots=0, spot_uv=None):
    """Faceted mushroom cap. attach = point where the stem enters the underside. tilt = (rx, ry) radians.
    Returns list of world points of the outer rim (for checks)."""
    rnd = random.Random(seed)
    M = Matrix.Translation(Vector(attach)) @ Matrix.Rotation(spin, 4, 'Z') @ Matrix.Rotation(tilt[1], 4, 'Y') @ Matrix.Rotation(tilt[0], 4, 'X')
    # profile: (radius fraction, z in units of R / H)
    prof_under = [(stem_r / R, 0.0 + under_depth), (0.55, -0.06 * R / max(R, 1e-6)), (0.93, -droop)]  # z * R
    prof_top = [(1.0, -droop - 0.01), (1.04, -droop + rim), (0.86, 0.38), (0.55, 0.78), (0.24, 0.96)]  # z: rim in R, rest in H
    lumpk = [1 + rnd.uniform(-lump, lump) for _ in range(sides)]
    zk = [rnd.uniform(-0.06, 0.06) for _ in range(sides)]
    bumps = bumps or []
    chipd = {}
    for c in range(chips):
        chipd[rnd.randrange(sides)] = rnd.uniform(0.12, 0.3) if c else rnd.uniform(0.3, 0.45)   # first = crack

    def P(k, rf, z):
        a = 2 * math.pi * k / sides
        r = R * rf * (lumpk[k] if rf > 0.3 else 1.0)
        if k in chipd and rf > 0.85:
            r *= 1 - chipd[k]
            z += chipd[k] * R * 0.25
        bz = 0.0
        for (ba, bh) in bumps:
            d = math.cos(a - ba)
            if d > 0.5 and rf < 0.9:
                bz += bh * (d - 0.5) * 2 * (1 - rf)
        return M @ Vector((math.cos(a) * r * sxy[0], math.sin(a) * r * sxy[1], z + bz))

    ring_u = []
    for j, (rf, z) in enumerate(prof_under):
        zz = z * R if j > 0 else z
        ring_u.append([P(k, rf, zz + (zk[k] * R if j == 2 else 0)) for k in range(sides)])
    ring_t = []
    for j, (rf, z) in enumerate(prof_top):
        if j < 2:
            ring_t.append([P(k, rf, z * R + zk[k] * R) for k in range(sides)])
        else:
            ring_t.append([P(k, rf, (z * (1 - flat_top) + flat_top * 0.85) * H + (zk[k] * R * 0.5 if j == 2 else 0)) for k in range(sides)])
    apex = M @ Vector((rnd.uniform(-.05, .05) * R, rnd.uniform(-.05, .05) * R, H))
    # underside (gills) -- radial stripes: u across sector, v along radius
    for j in range(len(ring_u) - 1):
        a, b = ring_u[j], ring_u[j + 1]
        for k in range(sides):
            k2 = (k + 1) % sides
            off = (k % 4) * 0.25
            f0, f1 = j / 2.0, (j + 1) / 2.0
            mesh.quad(a[k], a[k2], b[k2], b[k],
                      ruv(under, off, f0), ruv(under, off + .25, f0), ruv(under, off + .25, f1), ruv(under, off, f1))
    # inner disc under the stem
    mesh.cap(ring_u[0], flip=True, region=under, center=M @ Vector((0, 0, -0.04 * R)))
    # rim band: from under ring outer to top ring 0..1
    allr = [ring_u[-1]] + ring_t
    proj = lambda p: ((M.inverted() @ p).x / (2.1 * R * sxy[0]) + .5, (M.inverted() @ p).y / (2.1 * R * sxy[1]) + .5)
    for j in range(len(allr) - 1):
        a, b = allr[j], allr[j + 1]
        for k in range(sides):
            k2 = (k + 1) % sides
            if j < 2:   # rim band: lower part of the cap texture, sectors
                fx0, fx1 = (k % 4) * 0.25, (k % 4) * 0.25 + 0.25
                fa, fb = (0.92, 0.80) if j == 0 else (0.80, 0.68)
                uv = [ruv(top, fx0, fa), ruv(top, fx1, fa), ruv(top, fx1, fb), ruv(top, fx0, fb)]
                if j == 1 and glow_rim:
                    uv = [ruv(under, fx0, 0.1), ruv(under, fx1, 0.1), ruv(under, fx1, 0.0), ruv(under, fx0, 0.0)]
                if j == 0:
                    uv = [ruv(under, fx0, 0.0), ruv(under, fx1, 0.0), ruv(under, fx1, 0.12), ruv(under, fx0, 0.12)]
                    mesh.quad(a[k], a[k2], b[k2], b[k], *uv)
                    continue
            else:
                uv = [ruv(top, *proj(p)) for p in (a[k], a[k2], b[k2], b[k])]
            mesh.quad(a[k], a[k2], b[k2], b[k], *uv)
    last = ring_t[-1]
    for k in range(sides):
        k2 = (k + 1) % sides
        mesh.tri(last[k], last[k2], apex, ruv(top, *proj(last[k])), ruv(top, *proj(last[k2])), ruv(top, *proj(apex)))
    # raised spots: small flat 5-6 sided studs on the upper cap
    for sidx in range(spots):
        j = rnd.choice((2, 3, 3, 4)) if len(ring_t) > 4 else 2
        k = rnd.randrange(sides); k2 = (k + 1) % sides
        a, b = ring_t[j], ring_t[min(j + 1, len(ring_t) - 1)]
        u, v = rnd.uniform(.25, .75), rnd.uniform(.25, .75)
        c0 = a[k].lerp(a[k2], u); c1 = b[k].lerp(b[k2], u); C = c0.lerp(c1, v)
        nn = ((a[k2] - a[k]).cross(b[k] - a[k])).normalized()
        if nn.z < 0:
            nn = -nn
        rr = R * rnd.uniform(0.06, 0.11)
        xx, yy = frame(nn)
        m = rnd.choice((5, 6))
        topr = [C + nn * R * 0.03 + (xx * math.cos(2 * math.pi * i / m) + yy * math.sin(2 * math.pi * i / m)) * rr * rnd.uniform(.8, 1.2) for i in range(m)]
        botr = [q - nn * R * 0.05 for q in topr]
        su = spot_uv or ruv(top, .5, .5)
        tp = list(topr)
        if ((tp[1] - tp[0]).cross(tp[2] - tp[0])).dot(nn) < 0:
            tp.reverse(); botr.reverse()
        mesh.face(tp, [su] * m)
        for i in range(m):
            i2 = (i + 1) % m
            mesh.face([botr[i], botr[i2], tp[i2], tp[i]], [su] * 4)
    return ring_u[-1]


def bracket(mesh, attach, W, D, H, T, n=7, seed=0, top='cap_yellow', under='gill_glow', tilt=0.0, droop=0.04,
            lump=0.07, rim=0.05, wall_y=None):
    """Shelf fungus attached to a wall at +Y (front = -Y). attach = centre of the attachment line on the wall.
    W = width (x), D = protrusion (-y), H = dome height above rim, T = depth of the gill cone under the rim."""
    rnd = random.Random(seed)
    A = Vector(attach)
    Mt = Matrix.Translation(A) @ Matrix.Rotation(tilt, 4, 'X')
    angs = [math.pi + math.pi * i / n for i in range(n + 1)]   # pi..2pi  -> -y half
    lk = [1 + rnd.uniform(-lump, lump) for _ in angs]
    lk[0] = lk[-1] = 1.0
    zj = [rnd.uniform(-droop, droop) for _ in angs]

    def P(i, rf, z):
        a = angs[i]
        f = lk[i] if 0.2 < rf else 1.0
        return Mt @ Vector((math.cos(a) * W / 2 * rf * f, math.sin(a) * D * rf * f, z))
    # rings (rf, z)
    prof = [(0.0, -T), (0.5, -T * 0.45), (0.94, -0.02 + 0), (1.0, rim * 0.4), (0.9, rim + H * 0.35), (0.6, H * 0.8), (0.25, H)]
    R = []
    for j, (rf, z) in enumerate(prof):
        R.append([P(i, rf, z + (zj[i] if 1 < j < 5 else 0)) for i in range(len(angs))])
    proj = lambda p: (((Mt.inverted() @ p).x / W) + .5, ((Mt.inverted() @ p).y / (1.05 * D)) * -1.0)
    for j in range(len(R) - 1):
        a, b = R[j], R[j + 1]
        for i in range(len(angs) - 1):
            if j < 2:     # gills: radial stripes
                fx0 = (i % 4) * 0.25; fx1 = fx0 + 0.25
                f0, f1 = (1.0 - j * 0.5), (1.0 - (j + 1) * 0.5)
                uv = [ruv(under, fx0, f0), ruv(under, fx1, f0), ruv(under, fx1, f1), ruv(under, fx0, f1)]
                if j == 0:
                    mesh.tri(a[i], b[i], b[i + 1], ruv(under, fx0 + .125, f0), uv[3], uv[2])
                else:
                    mesh.quad(a[i], b[i], b[i + 1], a[i + 1], uv[0], uv[3], uv[2], uv[1])
            elif j == 2:   # glowing rim lip
                uv = [ruv(under, (i % 4) * .25, 0.0), ruv(under, (i % 4) * .25 + .25, 0.0),
                      ruv(under, (i % 4) * .25 + .25, 0.15), ruv(under, (i % 4) * .25, 0.15)]
                mesh.quad(a[i], b[i], b[i + 1], a[i + 1], uv[0], uv[3], uv[2], uv[1])
            else:
                pts = [a[i], b[i], b[i + 1], a[i + 1]]
                mesh.quad(*pts, *[ruv(top, *proj(p)) for p in pts])
    # back face against the wall (planar polygon in the wall plane)
    left = [R[j][0] for j in range(len(R))]
    right = [R[j][-1] for j in range(len(R))]
    poly = left + list(reversed(right))
    # dedupe consecutive duplicates
    clean = []
    for p in poly:
        if not clean or (p - clean[-1]).length > 1e-6:
            clean.append(p)
    if (clean[0] - clean[-1]).length < 1e-6:
        clean.pop()
    mesh.face(clean, [ruv(top, .5, .5)] * len(clean))
    return R


# ---------------------------------------------------------------- heaps
def heap(mesh, peak, edge, H, prof_s, prof_g, n=14, jitter=(0.05, 0.08), seed=0, skirt=None, profile_fn=None,
         zjit_fn=None):
    """Polar height-field heap. edge(a) -> Vector xy at ground. prof_s/prof_g: radial samples (s from 1 at edge to 0
    at peak) and height fractions; profile_fn(a) -> (s_list, g_list) overrides per angle.
    Returns rings (outer first) + apex."""
    rnd = random.Random(seed)
    Pk = Vector(peak)
    rings = []
    m = len(prof_s) if prof_s else len(profile_fn(0.0)[0])
    angs = [2 * math.pi * k / n + rnd.uniform(-0.12, 0.12) * 2 * math.pi / n for k in range(n)]
    for j in range(m - 1):
        ring = []
        for k, a in enumerate(angs):
            ss, gg = profile_fn(a) if profile_fn else (prof_s, prof_g)
            s, g = ss[j], gg[j]
            E = Vector(edge(a))
            xy = Pk.xy + (E.xy - Pk.xy) * s
            if j > 0:
                xy += (E.xy - Pk.xy).normalized() * rnd.uniform(-1, 1) * jitter[0] * (E.xy - Pk.xy).length
                g = g + rnd.uniform(-1, 1) * jitter[1]
            if zjit_fn:
                g += zjit_fn(a, s)
            ring.append(Vector((xy.x, xy.y, max(0.0, g) * H)))
        rings.append(ring)
    apex = Vector((Pk.x, Pk.y, H))
    for j in range(len(rings) - 1):
        a, b = rings[j], rings[j + 1]
        for k in range(n):
            k2 = (k + 1) % n
            mesh.quad(a[k], a[k2], b[k2], b[k], (0, 0), (1, 0), (1, 1), (0, 1))
    last = rings[-1]
    for k in range(n):
        mesh.tri(last[k], last[(k + 1) % n], apex, (0, 0), (1, 0), (.5, 1))
    if skirt is not None:
        low = [Vector((Pk.x + (p.x - Pk.x) * 1.05, Pk.y + (p.y - Pk.y) * 1.05, skirt)) for p in rings[0]]
        for k in range(n):
            k2 = (k + 1) % n
            mesh.quad(low[k], low[k2], rings[0][k2], rings[0][k], (0, 0), (1, 0), (1, 1), (0, 1))
        mesh.face(list(reversed(low)), [(0, 0)] * n)
    else:
        mesh.face(list(reversed(rings[0])), [(0, 0)] * n)
    return rings, apex


# ---------------------------------------------------------------- leaves
LEAF = [(0, 0), (0.14, 0.05), (0.32, 0.1), (0.44, 0.26), (0.38, 0.4), (0.47, 0.52), (0.4, 0.68), (0.26, 0.84),
        (0.12, 0.93), (0.0, 1.0)]


def leaf_outline(seed):
    rnd = random.Random(seed)
    right = [(x * rnd.uniform(.85, 1.15), y + rnd.uniform(-.03, .03)) for x, y in LEAF[1:-1]]
    pts = [LEAF[0]] + right + [LEAF[-1]] + [(-x * rnd.uniform(.85, 1.15), y + rnd.uniform(-.03, .03)) for x, y in reversed(LEAF[1:-1])]
    return pts


def place_leaf(mesh, bvh, origin, direction, length, region, seed=0, rot=None, lift=0.02, curl=0.06):
    """Raycast `origin` along `direction` onto bvh; lay a lobed leaf there, conforming to the surface."""
    rnd = random.Random(seed)
    hit = bvh.ray_cast(Vector(origin), Vector(direction).normalized())
    if hit[0] is None:
        return False
    p0, n0 = hit[0], hit[1]
    x, y = frame(n0)
    ang = rot if rot is not None else rnd.uniform(0, 2 * math.pi)
    ax = x * math.cos(ang) + y * math.sin(ang)       # leaf axis (length)
    bx = n0.cross(ax)                                 # leaf width
    out = leaf_outline(seed)
    ctr = (0.0, 0.5)
    W = length * 0.9

    def put(lx, ly):
        q = p0 + ax * ((ly - 0.5) * length) + bx * (lx * W)
        h = bvh.ray_cast(q + n0 * 0.6, -n0)
        base = h[0] if h[0] is not None and (h[0] - q).length < 0.8 else q
        up = lift + curl * (abs(lx) * 2) ** 2 + 0.015 * (1 - abs(lx) * 2)
        return base + n0 * up

    C = put(*ctr)
    cu = ruv(region, 0.5, 0.5)
    pts = [put(lx, ly) for lx, ly in out]
    uvs = [ruv(region, lx + 0.5, 1 - ly) for lx, ly in out]
    m = len(pts)
    flip = ((pts[1] - pts[0]).cross(C - pts[0])).dot(n0) < 0
    back = [q - n0 * 0.006 for q in pts]; Cb = C - n0 * 0.006
    for i in range(m):
        j = (i + 1) % m
        if flip:
            mesh.tri(pts[j], pts[i], C, uvs[j], uvs[i], cu)
            mesh.tri(back[i], back[j], Cb, uvs[i], uvs[j], cu)      # underside (curled edges stay solid)
        else:
            mesh.tri(pts[i], pts[j], C, uvs[i], uvs[j], cu)
            mesh.tri(back[j], back[i], Cb, uvs[j], uvs[i], cu)
    # tiny stalk
    s0 = put(0, -0.12)
    s1 = put(0.03, 0.0)
    if ((s0 - pts[0]).cross(s1 - pts[0])).dot(n0) < 0:
        mesh.tri(pts[0], s1, s0, uvs[0], uvs[1], ruv(region, .5, .99))
    else:
        mesh.tri(pts[0], s0, s1, uvs[0], ruv(region, .5, .99), uvs[1])
    return True


def join_into(obj, mesh):
    """Append a kit Mesh (same material) into an existing object."""
    tmp = mesh.to_object('_join_tmp')
    bm = bmesh.new(); bm.from_mesh(obj.data); bm.from_mesh(tmp.data); bm.to_mesh(obj.data); bm.free()
    for poly in obj.data.polygons:
        poly.use_smooth = False
    me = tmp.data
    bpy.data.objects.remove(tmp); bpy.data.meshes.remove(me)
    bpy.context.view_layer.update()


def bvh_of(objs):
    verts, polys = [], []
    for o in objs:
        me = o.data
        off = len(verts)
        verts += [o.matrix_world @ v.co for v in me.vertices]
        polys += [[off + i for i in p.vertices] for p in me.polygons]
    return BVHTree.FromPolygons(verts, polys)


# ---------------------------------------------------------------- output
def outputs(objs, folder, fbx_name, vid, name, sheet_png, extra_render=None, manifest_extra=None, size=360,
            samples=8, views=True, extra_tiles=None, extra_views=None):
    out = os.path.join(folder, 'out')
    os.makedirs(out, exist_ok=True)
    fbx = os.path.join(folder, fbx_name)
    ak.export_fbx(objs, fbx)
    ak.save_blend(os.path.join(folder, vid.split('a')[0] + '.blend'))
    rec = ak.variant_record(vid, name, objs, fbx, **(manifest_extra or {}))
    tiles, labels = [], []
    if views:
        for lab, az in (('FL -45', -45), ('FR 45', 45), ('RR 135', 135), ('RL -135', -135)):
            p = os.path.join(out, 'v_%s.png' % lab.split()[0])
            ak.render(objs, p, az=az, el=20, size=(size, size), samples=samples)
            tiles.append(p); labels.append(lab)
    for i, (az, el, lab) in enumerate(extra_views or []):
        pth = os.path.join(out, 'x_%d.png' % i)
        ak.render(objs, pth, az=az, el=el, size=(size, size), samples=samples)
        tiles.append(pth); labels.append(lab)
    for t in (extra_tiles or []):
        tiles.append(t[0]); labels.append(t[1])
    if tiles:
        ak.sheet(tiles, os.path.join(out, 'peek.jpg'), cols=2 if len(tiles) == 4 else 3, labels=labels)
        ak.sheet(tiles, os.path.join(out, 'card.jpg'), cols=4, labels=labels, concept=sheet_png,
                 title='%s %s  tris %d  size %s' % (vid, name, rec['tris'], rec['size_xyz_m']))
    if '--bf' in sys.argv:
        backface_check(objs, os.path.join(out, 'backface.png'))
    return rec


def write_manifest(folder, recs, **extra):
    d = {'variants': recs}
    d.update(extra)
    json.dump(d, open(os.path.join(folder, 'out', 'manifest.json'), 'w'), indent=1, ensure_ascii=False)


def backface_check(objs, png, views=((-45, 25), (135, 25), (30, -30)), size=240):
    """QA: render with a debug material, front faces grey, BACK faces red (Unity culls them)."""
    m = bpy.data.materials.get('DBG_backface') or bpy.data.materials.new('DBG_backface')
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    geo = nt.nodes.new('ShaderNodeNewGeometry')
    mix = nt.nodes.new('ShaderNodeMixShader')
    a = nt.nodes.new('ShaderNodeBsdfDiffuse'); a.inputs['Color'].default_value = (0.6, 0.6, 0.6, 1)
    b = nt.nodes.new('ShaderNodeEmission'); b.inputs['Color'].default_value = (1, 0, 0, 1); b.inputs['Strength'].default_value = 2
    nt.links.new(geo.outputs['Backfacing'], mix.inputs[0])
    nt.links.new(a.outputs[0], mix.inputs[1]); nt.links.new(b.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs[0])
    saved = []
    for o in objs:
        saved.append(list(o.data.materials))
        o.data.materials.clear(); o.data.materials.append(m)
    tiles = []
    for i, (az, el) in enumerate(views):
        t = png.replace('.png', '_%d.png' % i)
        ak.render(objs, t, az=az, el=el, size=(size, size), samples=4)
        tiles.append(t)
    ak.sheet(tiles, png, cols=len(tiles), labels=['az%d el%d' % v for v in views])
    for o, ms in zip(objs, saved):
        o.data.materials.clear()
        for mm in ms:
            o.data.materials.append(mm)
    return png


# ================================================================ detail kit (v2: "as many details as possible")
def full(region, inset=1):
    rx, ry, rw, rh = ak.REG['T_Ant_Props.png']['regions'][region]
    return (region, (inset, inset, rw - inset, rh - inset))


def basis(n):
    n = Vector(n).normalized()
    x, y = frame(n)
    return x, y, n


def samples(objs, count, seed=0, filt=None, tries=40):
    """Random surface points (p, normal) on objects, area weighted; filt(p, n) -> bool."""
    rnd = random.Random(seed)
    T, A = [], []
    for o in objs:
        me = o.data
        me.calc_loop_triangles()
        mw = o.matrix_world
        for t in me.loop_triangles:
            a, b, c = [mw @ me.vertices[i].co for i in t.vertices]
            ar = ((b - a).cross(c - a)).length / 2
            if ar > 1e-8:
                T.append((a, b, c)); A.append(ar)
    if not T:
        return []
    cum = []
    s = 0
    for ar in A:
        s += ar; cum.append(s)
    import bisect
    out = []
    for _ in range(count * tries):
        if len(out) >= count:
            break
        i = bisect.bisect_left(cum, rnd.uniform(0, s))
        a, b, c = T[min(i, len(T) - 1)]
        u, v = rnd.random(), rnd.random()
        if u + v > 1:
            u, v = 1 - u, 1 - v
        p = a + (b - a) * u + (c - a) * v
        n = ((b - a).cross(c - a)).normalized()
        if filt is None or filt(p, n):
            out.append((p, n))
    return out


def ofit(mesh, pts, spec, metres, rs, center):
    """fit_face, oriented so the face normal points away from `center`."""
    P = [Vector(q) for q in pts]
    cc = sum(P, Vector()) / len(P)
    nn = (P[1] - P[0]).cross(P[2] - P[0])
    if nn.dot(cc - Vector(center)) < 0:
        P = list(reversed(P))
    return fit_face(mesh, P, spec, metres, rs)


def lump(mesh, c, r, normal=(0, 0, 1), sides=6, squash=0.6, sink=0.35, seed=0, spec=None, jitter=0.25,
         bottom=False, metres=0.4, stretch=1.0):
    """Crude faceted lump (pebble / clod / crumb) sitting on a surface. spec = (region, px) atlas sub-rect."""
    rnd = random.Random(seed)
    spec = spec or full('pebble')
    x, y, n = basis(normal)
    ang = rnd.uniform(0, 6.28)
    x, y = x * math.cos(ang) + y * math.sin(ang), -x * math.sin(ang) + y * math.cos(ang)
    h = r * squash
    C = Vector(c) - n * (h * sink)
    def P(rr, z, k, j=True):
        a = 2 * math.pi * k / sides + (rnd.uniform(-0.25, 0.25) if j else 0)
        f = 1 + (rnd.uniform(-jitter, jitter) if j else 0)
        return C + x * (math.cos(a) * rr * f * stretch) + y * (math.sin(a) * rr * f) + n * z
    r0 = [P(r, 0.0, k) for k in range(sides)]
    r1 = [P(r * 0.68, h * 0.72, k + 0.5) for k in range(sides)]
    top = C + n * h + x * rnd.uniform(-.15, .15) * r
    rs = random.Random(seed + 1)
    for k in range(sides):
        k2 = (k + 1) % sides
        ctr = C + n * h * 0.3
        ofit(mesh, [r0[k], r0[k2], r1[k]], spec, metres, rs, ctr)
        ofit(mesh, [r0[k2], r1[k2], r1[k]], spec, metres, rs, ctr)
        ofit(mesh, [r1[k], r1[k2], top], spec, metres, rs, ctr)
    if bottom:
        ofit(mesh, list(r0), spec, metres, rs, C + n * h)


def twig(mesh, base, direction, length, r=0.03, seed=0, region='wood_props', sides=4, fork=True, bend=0.15,
         embed=0.15):
    """Stick / twig poking out of a surface (embedded `embed` m), crooked, optional fork; end-grain tip."""
    rnd = random.Random(seed)
    d = Vector(direction).normalized()
    x, y = frame(d)
    B = Vector(base) - d * embed
    pts = [B]
    for i in range(1, 4):
        t = i / 3
        pts.append(B + d * (length + embed) * t + (x * rnd.uniform(-1, 1) + y * rnd.uniform(-1, 1)) * bend * length * t * 0.3
                   + Vector((0, 0, -bend * length * t * t * 0.3)))
    rad = [r, r * 0.9, r * 0.75, r * 0.55]
    fy1 = min(0.98, (length + embed) / 1.2)
    atlas_tube(mesh, pts, rad, sides, region, along='v', fy_range=(0.0, fy1), jitter=r * 0.15, seed=seed,
               caps=(embed < 0.03, True), cap_region='end_grain', rot=rnd.uniform(0, 3))
    if fork and length > 0.25:
        q = pts[2]
        fd = (d + (x * rnd.choice((-1, 1)) + y * rnd.uniform(-.5, .5)) * 0.9).normalized()
        L2 = length * rnd.uniform(0.3, 0.5)
        atlas_tube(mesh, [q - fd * r, q + fd * L2 * 0.5, q + fd * L2], [r * 0.6, r * 0.5, r * 0.35], sides, region,
                   along='v', fy_range=(0.2, 0.2 + min(0.7, L2)), seed=seed + 7, caps=(False, True), cap_region='end_grain')


def needles(mesh, base, direction, length=0.35, count=6, spread=0.45, seed=0, r=0.012, embed=0.05):
    """Bundle of pine needles (thin 3-sided strands, Props `needles`)."""
    rnd = random.Random(seed)
    d = Vector(direction).normalized()
    x, y = frame(d)
    for i in range(count):
        a = rnd.uniform(0, 6.28)
        dd = (d + (x * math.cos(a) + y * math.sin(a)) * rnd.uniform(0.1, spread)).normalized()
        L = length * rnd.uniform(0.75, 1.15)
        B = Vector(base) - dd * embed + (x * rnd.uniform(-1, 1) + y * rnd.uniform(-1, 1)) * r * 1.5
        mid = B + dd * L * 0.5 + Vector((0, 0, -0.03 * L))
        atlas_tube(mesh, [B, mid, B + dd * L + Vector((0, 0, -0.08 * L))], [r, r * 0.8, r * 0.35], 3, 'needles',
                   along='u', fy_range=(rnd.uniform(0, .3), rnd.uniform(.7, 1.0)), seed=seed + i, caps=(embed < 0.02, True),
                   rot=rnd.uniform(0, 3))


def burrow(mesh, p, normal, R=0.18, seed=0, sides=7, depth=0.25):
    """Tiny ant burrow: raised clay lip (earth_props) around a dark well (Props `dark`)."""
    rnd = random.Random(seed)
    x, y, n = basis(normal)
    P = Vector(p)
    def ring(rr, z, jr=0.15):
        out = []
        for k in range(sides):
            a = 2 * math.pi * k / sides
            f = 1 + rnd.uniform(-jr, jr)
            out.append(P + (x * math.cos(a) + y * math.sin(a)) * rr * f + n * z)
        return out
    o = ring(R * 1.6, -0.03)
    lip = ring(R, R * 0.35)
    w = ring(R * 0.55, -depth * 0.4, 0.05)
    bot = P - n * depth
    spec = full('earth_props'); rs = random.Random(seed + 3)
    du = ruv('dark', .5, .5)
    for k in range(sides):
        k2 = (k + 1) % sides
        ofit(mesh, [o[k], o[k2], lip[k2], lip[k]], spec, 0.5, rs, P - n * 1.0)
        q = [lip[k], lip[k2], w[k2], w[k]]
        if ((q[1] - q[0]).cross(q[3] - q[0])).dot(P - (q[0] + q[2]) / 2) < 0:
            q.reverse()
        mesh.face(q, [du] * 4)
        q = [w[k], w[k2], bot]
        if ((q[1] - q[0]).cross(q[2] - q[0])).dot(P + n * 0.5 - (q[0] + q[1] + q[2]) / 3) < 0:
            q.reverse()
        mesh.face(q, [du] * 3)


def rootlet(mesh, p, normal, length=0.4, r=0.03, seed=0, sides=3, droop=0.6, embed=0.04, keep=None):
    """Hair rootlet: thin crooked tapering strand growing from a surface and drooping."""
    rnd = random.Random(seed)
    n = Vector(normal).normalized()
    x, y = frame(n)
    side = (x * rnd.uniform(-1, 1) + y * rnd.uniform(-1, 1)) * 0.5
    d = (n + side).normalized()
    pts = [Vector(p) - n * embed]
    cur = Vector(p)
    for i in range(1, 5):
        step = length / 4
        d = (d + Vector((rnd.uniform(-.35, .35), rnd.uniform(-.35, .35), -droop * 0.5))).normalized()
        cur = cur + d * step
        pts.append(cur.copy())
    if keep and not all(keep(q) for q in pts):
        return False
    mesh.tube(pts, [r, r * 0.8, r * 0.6, r * 0.4, r * 0.15], sides=sides, jitter=0.0, seed=seed, caps=(False, False),
              rot=rnd.uniform(0, 3))


def side_root(mesh, props, p, normal, length=0.6, r=0.08, seed=0, sides=5, cut=True, droop=0.3, keep=None):
    """Small side root / broken stub growing out of a bigger root; cut end with end grain (Props root_end)."""
    rnd = random.Random(seed)
    n = Vector(normal).normalized()
    x, y = frame(n)
    d = (n + (x * rnd.uniform(-.6, .6) + y * rnd.uniform(-.6, .6))).normalized()
    pts = [Vector(p) - n * r * 1.2]
    cur = Vector(p)
    for i in range(1, 4):
        d = (d + Vector((rnd.uniform(-.25, .25), rnd.uniform(-.25, .25), -droop * 0.4))).normalized()
        cur = cur + d * (length / 3)
        pts.append(cur.copy())
    if keep and not all(keep(q) for q in pts):
        return None
    if cut:
        root(mesh, props, pts, [r * 1.1, r, r * 0.85, r * 0.75], sides=sides, seed=seed, jitter=r * 0.12,
             cut=(rnd.uniform(-.3, .3), rnd.uniform(-.3, .3), rnd.uniform(0, .4)))
    else:
        mesh.tube(pts, [r * 1.1, r * 0.8, r * 0.5, r * 0.15], sides=sides, seed=seed, jitter=r * 0.1, caps=(False, True))
    return pts


def knot(mesh, props, p, normal, r=0.12, seed=0):
    """Bark knot: short bulging stub with an end-grain face."""
    n = Vector(normal).normalized()
    P = Vector(p)
    rnd = random.Random(seed)
    t = (n + Vector((rnd.uniform(-.2, .2), rnd.uniform(-.2, .2), rnd.uniform(0, .25)))).normalized()
    root(mesh, props, [P - n * r * 0.9, P + t * r * 0.25, P + t * r * 0.55], [r * 1.25, r * 1.1, r * 0.8], sides=6,
         seed=seed, jitter=r * 0.06, cut=(rnd.uniform(-.2, .2), rnd.uniform(-.2, .2), 0.1))


def chip(mesh, p, normal, size=0.1, seed=0, region='root_props', lift=0.25):
    """Bark chip / flake: thin crooked plate on a surface, one edge lifted."""
    rnd = random.Random(seed)
    x, y, n = basis(normal)
    a = rnd.uniform(0, 6.28)
    x, y = x * math.cos(a) + y * math.sin(a), -x * math.sin(a) + y * math.cos(a)
    P = Vector(p) + n * 0.006
    pts2 = [(-1, -0.6), (0.2, -0.75), (1, -0.2), (0.8, 0.6), (-0.3, 0.7), (-0.9, 0.3)]
    top = [P + x * (u * size * rnd.uniform(.8, 1.2)) + y * (v * size * 0.6 * rnd.uniform(.8, 1.2)) + n * (max(0, u) * size * lift)
           for u, v in pts2]
    thick = size * 0.12
    bot = [q - n * thick for q in top]
    spec = full(region); rs = random.Random(seed + 2)
    ctr = sum(top, Vector()) / len(top) - n * thick * 0.5
    ofit(mesh, top, spec, 0.6, rs, ctr - n * 0.3)
    ofit(mesh, bot, spec, 0.6, rs, ctr + n * 0.3)
    m = len(top)
    for k in range(m):
        k2 = (k + 1) % m
        ofit(mesh, [bot[k], bot[k2], top[k2], top[k]], spec, 0.6, rs, ctr)


def baby_mushroom(mesh, p, normal=(0, 0, 1), h=0.15, R=0.06, seed=0, top='cap_yellow', under='gill_glow_yellow',
                  stem_region='stem', sides=7, lean=0.2):
    """Small mushroom (stem + domed cap) growing from a surface point."""
    rnd = random.Random(seed)
    n = Vector(normal).normalized()
    x, y = frame(n)
    lv = (x * rnd.uniform(-1, 1) + y * rnd.uniform(-1, 1)) * lean
    d = (n + lv + Vector((0, 0, 0.6))).normalized()
    B = Vector(p) - n * 0.02
    T = B + d * h
    mid = B.lerp(T, 0.5) + (x * rnd.uniform(-1, 1)) * h * 0.06
    rs = max(0.014, R * 0.36)
    atlas_tube(mesh, [B, mid, T], [rs * 1.3, rs, rs * 0.9], 5, stem_region, along='u', fy_range=(0.1, 0.9), seed=seed,
               caps=(False, True))
    tilt = (rnd.uniform(-.25, .25), rnd.uniform(-.25, .25))
    cap(mesh, T, R=R, H=R * rnd.uniform(0.55, 0.85), sides=sides, tilt=tilt, spin=rnd.uniform(0, 6), seed=seed + 5, top=top,
        under=under, stem_r=rs * 0.95, droop=0.12, lump=0.08, rim=0.12, under_depth=R * 0.15,
        glow_rim=(under != 'gill_glow'), chips=rnd.choice((0, 1)))


def boolean_cut(obj, cutters_bm):
    """Exact boolean difference of a closed bmesh (cutters) from obj; returns list of polygons (world pts)."""
    bmesh.ops.recalc_face_normals(cutters_bm, faces=cutters_bm.faces)
    me = bpy.data.meshes.new('cut_tmp'); cutters_bm.to_mesh(me)
    co = bpy.data.objects.new('cut_tmp', me); bpy.context.scene.collection.objects.link(co)
    mod = obj.modifiers.new('cut', 'BOOLEAN'); mod.operation = 'DIFFERENCE'; mod.solver = 'EXACT'; mod.object = co
    dg = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(dg)
    m2 = ev.to_mesh()
    polys = [[m2.vertices[i].co.copy() for i in p.vertices] for p in m2.polygons]
    ev.to_mesh_clear()
    obj.modifiers.remove(mod)
    bpy.data.objects.remove(co); bpy.data.meshes.remove(me)
    bpy.context.view_layer.update()
    return polys


def wedge(bm, a, b, normal, width=0.04, depth=0.08):
    """Thin V-shaped crack cutter from a to b on a surface with normal (closed prism into bm)."""
    a, b, n = Vector(a), Vector(b), Vector(normal).normalized()
    t = (b - a).normalized()
    s = t.cross(n).normalized()
    pts = []
    for P in (a, b):
        pts += [P + s * width / 2 + n * 0.05, P - s * width / 2 + n * 0.05, P - n * depth]
    v = [bm.verts.new(q) for q in pts]
    for f in ((0, 1, 2), (5, 4, 3), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)):
        bm.faces.new([v[i] for i in f])


def ground_bvh(size=12.0, z=0.0):
    return BVHTree.FromPolygons([(-size, -size, z), (size, -size, z), (size, size, z), (-size, size, z)], [(0, 1, 2, 3)])


def root_details(ro, po, seed=0, n_rootlets=50, n_side=10, n_knots=6, n_chips=14, n_clods=14, n_pebbles=18,
                 n_leaves=3, base_r=(1.0, 1.9), base_c=(0, 0), zmax=99.0, keep=None, ground_chips=6,
                 rootlet_len=(0.2, 0.6), side_r=(0.05, 0.1), clamp_y=None):
    """Detail pass for a root asset: hair rootlets, side roots / broken stubs, knots, bark chips, earth clods stuck
    at the toes, pebbles + a few dry leaves on the ground around the base. Appends into ro (Root) / po (Props)."""
    rnd = random.Random(seed)
    R = ak.Mesh('Root'); P = ak.Mesh('Props')
    kp = keep or (lambda q: True)
    surf = lambda p, n: 0.12 < p.z < zmax
    i = 0
    for (p, n) in samples([ro], n_rootlets * 2, seed + 1, surf):
        if i >= n_rootlets:
            break
        if rootlet(R, p, n, rnd.uniform(*rootlet_len), rnd.uniform(0.015, 0.035), seed=seed * 100 + i,
                   droop=rnd.uniform(0.3, 0.9), keep=kp) is not False:
            i += 1
    i = 0
    for (p, n) in samples([ro], n_side * 3, seed + 2, lambda p, n: 0.4 < p.z < zmax - 0.3 and abs(n.z) < 0.6):
        if i >= n_side:
            break
        cut = rnd.random() < 0.6
        if side_root(R, P, p, n, rnd.uniform(0.25, 0.7), rnd.uniform(*side_r), seed=seed * 100 + 50 + i, cut=cut,
                     keep=kp) is not None:
            i += 1
    for j, (p, n) in enumerate(samples([ro], n_knots, seed + 3, lambda p, n: 0.5 < p.z < zmax and abs(n.z) < 0.5 and kp(p + n * 0.15))):
        knot(R, P, p, n, rnd.uniform(0.07, 0.13), seed=seed * 100 + 80 + j)
    for j, (p, n) in enumerate(samples([ro], n_chips, seed + 4, lambda p, n: 0.2 < p.z < zmax and n.z > -0.3 and kp(p + n * 0.1))):
        chip(P, p, n, rnd.uniform(0.06, 0.12), seed=seed * 100 + 90 + j)
    for j, (p, n) in enumerate(samples([ro], n_clods, seed + 5, lambda p, n: p.z < 0.45 and n.z > -0.2 and kp(p + n * 0.15 + Vector((0, 0, 0.1))))):
        lump(P, p, rnd.uniform(0.06, 0.15), n.lerp(Vector((0, 0, 1)), 0.5), sides=6, squash=rnd.uniform(.5, .8),
             sink=0.45, seed=seed * 100 + 120 + j, spec=full('earth_props'), metres=0.5)
    gb = ground_bvh()
    for j in range(n_pebbles + ground_chips + n_leaves):
        a = rnd.uniform(0, 6.28); d = rnd.uniform(*base_r)
        q = Vector((base_c[0] + math.cos(a) * d, base_c[1] + math.sin(a) * d, 0.0))
        if clamp_y is not None and q.y > clamp_y - 0.1:
            q.y = clamp_y - rnd.uniform(0.1, 0.5)
        if not kp(q + Vector((0, 0, 0.1))):
            continue
        if j < n_pebbles:
            sp = full('pebble') if rnd.random() < 0.7 else full('earth_props')
            lump(P, q, rnd.uniform(0.04, 0.12), (0, 0, 1), sides=rnd.choice((5, 6, 7)), squash=rnd.uniform(.4, .7),
                 sink=0.3, seed=seed * 100 + 150 + j, spec=sp, metres=0.3)
        elif j < n_pebbles + ground_chips:
            chip(P, q, (0, 0, 1), rnd.uniform(0.08, 0.14), seed=seed * 100 + 170 + j, lift=0.15)
        else:
            place_leaf(P, gb, (q.x, q.y, 1.0), (0, 0, -1), rnd.uniform(0.25, 0.4), rnd.choice(('leaf_dry_0', 'leaf_dry_1', 'leaf_dry_2')),
                       seed=seed * 100 + 190 + j, lift=0.01, curl=0.04)
    if clamp_y is not None:
        clamp(R, 1, hi=clamp_y); clamp(P, 1, hi=clamp_y)
    clamp(R, 2, lo=0.0); clamp(P, 2, lo=0.0)
    join_into(ro, R); join_into(po, P)


def foot_details(P, center, r_in, r_out, seed=0, n_clump=7, clump_r=(0.08, 0.16), n_babies=4, baby_h=(0.12, 0.3),
                 baby_R=(0.05, 0.1), baby_top='cap_yellow', baby_under='gill_glow_yellow', n_pebbles=10, n_leaves=2,
                 n_twigs=1, n_needles=0, n_crumbs=0, keep=None, z=0.0):
    """Ground details around a foot: soil clump (earth_props lumps), baby mushrooms, pebbles, leaves, twigs, crumbs."""
    rnd = random.Random(seed)
    C = Vector((center[0], center[1], z))
    kp = keep or (lambda q: True)
    gb = ground_bvh(z=z)

    def pt(a0, a1):
        for _ in range(30):
            a = rnd.uniform(0, 6.28); d = rnd.uniform(a0, a1)
            q = C + Vector((math.cos(a) * d, math.sin(a) * d, 0))
            if kp(q):
                return q
        return None
    for j in range(n_clump):
        q = pt(r_in * 0.7, r_in * 1.15)
        if q:
            lump(P, q, rnd.uniform(*clump_r), (0, 0, 1), sides=rnd.choice((6, 7)), squash=rnd.uniform(.6, .9), sink=0.15,
                 seed=seed * 50 + j, spec=full('earth_props'), metres=0.5)
    for j in range(n_babies):
        q = pt(r_in * 0.9, r_out * 0.8)
        if q:
            baby_mushroom(P, q, (0, 0, 1), rnd.uniform(*baby_h), rnd.uniform(*baby_R), seed=seed * 50 + 10 + j,
                          top=baby_top, under=baby_under, lean=0.35)
    for j in range(n_pebbles):
        q = pt(r_in * 0.8, r_out)
        if q:
            lump(P, q, rnd.uniform(0.03, 0.09), (0, 0, 1), sides=rnd.choice((5, 6)), squash=rnd.uniform(.4, .7), sink=0.3,
                 seed=seed * 50 + 20 + j, spec=full('pebble') if rnd.random() < .7 else full('earth_props'), metres=0.3)
    for j in range(n_crumbs):
        q = pt(r_in * 0.6, r_out)
        if q:
            lump(P, q, rnd.uniform(0.02, 0.04), (0, 0, 1), sides=5, squash=0.5, sink=0.3, seed=seed * 50 + 30 + j,
                 spec=full('earth_props'), metres=0.2)
    for j in range(n_leaves):
        q = pt(r_in, r_out)
        if q:
            place_leaf(P, gb, (q.x, q.y, z + 1.0), (0, 0, -1), rnd.uniform(0.18, 0.3), rnd.choice(('leaf_dry_0', 'leaf_dry_1', 'leaf_dry_2')),
                       seed=seed * 50 + 40 + j, lift=0.008, curl=0.03)
    for j in range(n_twigs):
        q = pt(r_in, r_out)
        if q:
            a = rnd.uniform(0, 6.28)
            twig(P, q + Vector((0, 0, 0.02)), (math.cos(a), math.sin(a), 0.05), rnd.uniform(0.25, 0.45), 0.015,
                 seed=seed * 50 + 45 + j, embed=0.0, fork=True, bend=0.1)
    for j in range(n_needles):
        q = pt(r_in, r_out)
        if q:
            a = rnd.uniform(0, 6.28)
            needles(P, q + Vector((0, 0, 0.01)), (math.cos(a), math.sin(a), 0.08), 0.25, 5, 0.5, seed=seed * 50 + 48 + j, r=0.008, embed=0.0)


def safe_cuts(obj, wedges):
    """Apply crack wedges one by one (exact boolean); a wedge that breaks the result is skipped.
    Returns (polygons, number_applied)."""
    polys = [[obj.matrix_world @ obj.data.vertices[i].co for i in p.vertices] for p in obj.data.polygons]
    ok = 0
    for w in wedges:
        cb = bmesh.new(); wedge(cb, *w)
        res = boolean_cut(obj, cb); cb.free()
        if len(res) >= len(polys):
            polys = res; ok += 1
            bm = bmesh.new()
            for q in res:
                vs = [bm.verts.new(v) for v in q]
                try:
                    bm.faces.new(vs)
                except ValueError:
                    pass
            bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
            bm.to_mesh(obj.data); bm.free()
            obj.data.update()
    return polys, ok
