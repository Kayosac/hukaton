"""Helpers for group 'Mill, boats, idol' (SW20/SW21/SW23). Built on top of the kit's sk.Mesh.
All solids are closed (front/back/edge faces), oriented by an explicit outward direction."""
import math, random, sys
sys.path.insert(0, '/home/claude/swamp/kit')
import sk
from mathutils import Vector, Matrix

V = Vector


def newell(pts):
    n = V((0, 0, 0))
    for i in range(len(pts)):
        a, b = pts[i], pts[(i + 1) % len(pts)]
        n.x += (a.y - b.y) * (a.z + b.z)
        n.y += (a.z - b.z) * (a.x + b.x)
        n.z += (a.x - b.x) * (a.y + b.y)
    return n


def oface(m, pts, uvs, want, wind=None):
    """Face whose normal points along `want` (reverses winding if needed). Drops duplicate points."""
    P = [V(p) for p in pts]
    keep = []
    for i, p in enumerate(P):
        if all((p - P[j]).length > 1e-6 for j in keep):
            keep.append(i)
    if len(keep) < 3:
        return None
    P = [P[i] for i in keep]; U = [uvs[i] for i in keep]
    W = [wind[i] for i in keep] if wind else None
    if newell(P).dot(V(want)) < 0:
        P.reverse(); U.reverse()
        if W: W.reverse()
    return m.face(P, U, W)


def strip(m, stations, P, N, thick, swap=False, uvoff=(0, 0), region=None, rsize=(1, 1), back=True,
          edges=True, caps=True, uvscale=1.0):
    """Generic thin slab over a parametric surface.
    stations: list of ((s_lo, h_lo), (s_hi, h_hi)) param points (metres), ordered along s.
    P(s,h)->Vector on the FRONT surface; N(s,h)->unit vector pointing to the BACK (thickness direction).
    Front faces point along -N. Tiling UV: grain (V) along s (swap=True: V along h).
    region: atlas region (Props) -> uv = param / rsize fitted into region."""
    t = (m.tile() or 1.0) / uvscale
    def uv(s, h, d=0.0):
        if region:
            u0, v0, u1, v1 = m.region(region, m.mat)
            return (u0 + (h / rsize[0]) * (u1 - u0), v0 + (s / rsize[1]) * (v1 - v0))
        a, b = (h + d) / t + uvoff[0], s / t + uvoff[1]
        return (b, a) if swap else (a, b)
    F = []; B = []
    for (lo, hi) in stations:
        f = (P(*lo), P(*hi)); n = (N(*lo), N(*hi))
        F.append(f); B.append((f[0] + n[0] * thick, f[1] + n[1] * thick))
    for i in range(len(stations) - 1):
        (l0, h0), (l1, h1) = stations[i], stations[i + 1]
        nn = -(N(*l0) + N(*h1)).normalized()
        oface(m, [F[i][0], F[i + 1][0], F[i + 1][1], F[i][1]], [uv(*l0), uv(*l1), uv(*h1), uv(*h0)], nn)
        if back:
            oface(m, [B[i][0], B[i + 1][0], B[i + 1][1], B[i][1]], [uv(*l0), uv(*l1), uv(*h1), uv(*h0)], -nn)
        if edges:
            out = F[i][0] - F[i][1]
            oface(m, [F[i][0], F[i + 1][0], B[i + 1][0], B[i][0]],
                  [uv(*l0), uv(*l1), uv(l1[0], l1[1], thick), uv(l0[0], l0[1], thick)], out)
            out = F[i][1] - F[i][0]
            oface(m, [F[i][1], F[i + 1][1], B[i + 1][1], B[i][1]],
                  [uv(*h0), uv(*h1), uv(h1[0], h1[1], thick), uv(h0[0], h0[1], thick)], out)
    if caps:
        for i, j in ((0, 1), (-1, -2)):
            (lo, hi) = stations[i]
            out = (F[i][0] + F[i][1]) - (F[j][0] + F[j][1])
            oface(m, [F[i][0], F[i][1], B[i][1], B[i][0]],
                  [uv(lo[0], 0), uv(lo[0], 0.2), uv(lo[0] + thick, 0.2), uv(lo[0] + thick, 0)], out)


def end_stations(L, W, kind, rnd, at_start=False):
    """Stations for one board end. kind: 'flat', 'broken' (splintered taper), 'slant'."""
    if kind == 'flat':
        st = [((L, 0), (L, W))]
    elif kind == 'slant':
        a = rnd.uniform(0.05, 0.15)
        st = [((L - a, 0), (L, W))] if rnd.random() < .5 else [((L, 0), (L - a, W))]
    else:
        r1, r2 = rnd.uniform(.04, .16), rnd.uniform(.0, .1)
        k = rnd.uniform(.3, .7)
        st = [((L - r1 - .1, 0), (L - r2 - .1, W)), ((L - r1, k * W * .5), (L - r2 * .3, W * (k * .5 + .45)))]
        if rnd.random() < .5:
            st = [((L - r2 - .1, 0), (L - r1 - .1, W)), ((L - r2 * .3, W * (.55 - k * .5)), (L - r1, W * (1 - k * .5)))]
    if at_start:
        st = [((L - a[0], a[1]), (L - b[0], b[1])) for (a, b) in st]
    return st


def board(m, a, b, wdir, width, thick, ends=('flat', 'flat'), seed=0, sag=0.0, sagdir=(0, 0, -1), segs=1,
          twist=0.0, uvoff=None, swap=False, region=None, rsize=(1, 1), center=True):
    """Straight (optionally sagging/twisting) board/beam from a to b (centre line), width along wdir, thickness
    along d x w. ends: 'flat' | 'broken' | 'slant'. Returns (d, w, n) unit axes."""
    rnd = random.Random(seed)
    a, b = V(a), V(b)
    d = (b - a); L = d.length; d.normalize()
    w = V(wdir); w = (w - d * w.dot(d)).normalized()
    n = d.cross(w)
    sd = V(sagdir)
    def P(s, h):
        ang = twist * s / max(L, 1e-6)
        ww = w * math.cos(ang) + n * math.sin(ang)
        nn = n * math.cos(ang) - w * math.sin(ang)
        p = a + d * s + ww * (h - width / 2) - nn * (thick / 2 if center else 0)
        return p + sd * sag * math.sin(math.pi * max(0, min(1, s / L)))
    def N(s, h):
        ang = twist * s / max(L, 1e-6)
        return n * math.cos(ang) - w * math.sin(ang)
    # start end: mirror of an end at s=0
    e0 = end_stations(0.0, width, ends[0], rnd)
    e0 = [((-p[0], p[1]), (-q[0], q[1])) for (p, q) in e0]
    e0 = list(reversed(e0))
    e1 = end_stations(L, width, ends[1], rnd)
    mids = []
    s0 = max(p[0] for st in e0 for p in st) if ends[0] != 'flat' else 0.0
    s1 = min(p[0] for st in e1 for p in st) if ends[1] != 'flat' else L
    for k in range(1, segs):
        s = s0 + (s1 - s0) * k / segs
        mids.append(((s, 0), (s, width)))
    st = e0 + mids + e1
    if uvoff is None:
        uvoff = (rnd.random(), rnd.random())
    strip(m, st, P, N, thick, uvoff=uvoff, swap=swap, region=region, rsize=rsize)
    return d, w, n


def wbox(m, lo, hi, grain='z', jitter=0.0, seed=0, skip=(), region=None, lean=(0, 0)):
    """World-aligned box lo..hi with world-planar tiling UVs (continuous across neighbouring pieces).
    grain: world axis the texture V runs along. lean: top xy shift."""
    rnd = random.Random(seed)
    lo, hi = V(lo), V(hi)
    t = m.tile() or 1.0
    gi = 'xyz'.index(grain)
    C = {}
    for ix in (0, 1):
        for iy in (0, 1):
            for iz in (0, 1):
                p = V((hi.x if ix else lo.x, hi.y if iy else lo.y, hi.z if iz else lo.z))
                C[(ix, iy, iz)] = p
    J = {k: v + V((rnd.uniform(-jitter, jitter), rnd.uniform(-jitter, jitter), rnd.uniform(-jitter, jitter)))
         + (V((lean[0], lean[1], 0)) if k[2] else V()) for k, v in C.items()}
    faces = {'nx': (0, [(0, 0, 0), (0, 1, 0), (0, 1, 1), (0, 0, 1)], (-1, 0, 0)),
             'px': (0, [(1, 0, 0), (1, 1, 0), (1, 1, 1), (1, 0, 1)], (1, 0, 0)),
             'ny': (1, [(0, 0, 0), (1, 0, 0), (1, 0, 1), (0, 0, 1)], (0, -1, 0)),
             'py': (1, [(0, 1, 0), (1, 1, 0), (1, 1, 1), (0, 1, 1)], (0, 1, 0)),
             'bottom': (2, [(0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0)], (0, 0, -1)),
             'top': (2, [(0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1)], (0, 0, 1))}
    for name, (ax, idx, want) in faces.items():
        if name in skip:
            continue
        axes = [a for a in range(3) if a != ax]
        if gi in axes:
            axes = [a for a in axes if a != gi] + [gi]
        pts = [J[i] for i in idx]
        if region:
            u0, v0, u1, v1 = m.region(region, m.mat)
            uvs = [(u0 + i[axes[0]] * (u1 - u0), v0 + i[axes[1]] * (v1 - v0)) for i in idx]
        else:
            uvs = [(C[i][axes[0]] / t, C[i][axes[1]] / t) for i in idx]
        oface(m, pts, uvs, want)


def rtube(m, path, radii, sides, region, caps=(True, True), rot=0.0, jitter=0.0, seed=0, cap_region=None):
    """Tube with UVs fitted into one atlas region (u around, v along)."""
    rnd = random.Random(seed)
    Pt = [V(p) for p in path]; n = len(Pt)
    if not isinstance(radii, (list, tuple)):
        radii = [radii] * n
    u0, v0, u1, v1 = m.region(region, m.mat)
    rings = []; prev = None
    for i in range(n):
        t = (Pt[min(i + 1, n - 1)] - Pt[max(i - 1, 0)]).normalized()
        x = prev if prev is not None else (V((0, 0, 1)) if abs(t.z) < .9 else V((1, 0, 0)))
        x = (x - t * x.dot(t)).normalized(); prev = x
        y = t.cross(x)
        ring = []
        for k in range(sides):
            a = rot + 2 * math.pi * k / sides
            ring.append(Pt[i] + x * math.cos(a) * radii[i] + y * math.sin(a) * radii[i]
                        + V((rnd.uniform(-jitter, jitter), rnd.uniform(-jitter, jitter), rnd.uniform(-jitter, jitter))))
        rings.append(ring)
    for i in range(n - 1):
        for k in range(sides):
            k2 = (k + 1) % sides
            pts = [rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k]]
            c = (Pt[i] + Pt[i + 1]) / 2
            want = sum(pts, V()) / 4 - c
            ua, ub = u0 + (u1 - u0) * k / sides, u0 + (u1 - u0) * (k + 1) / sides
            va, vb = v0 + (v1 - v0) * i / (n - 1), v0 + (v1 - v0) * (i + 1) / (n - 1)
            oface(m, pts, [(ua, va), (ub, va), (ub, vb), (ua, vb)], want)
    for ci, (i, j) in enumerate(((0, 1), (-1, -2))):
        if caps[ci]:
            reg = cap_region or region
            cu0, cv0, cu1, cv1 = m.region(reg, m.mat)
            uvs = [(cu0 + (cu1 - cu0) * (.5 + .5 * math.cos(2 * math.pi * k / sides)),
                    cv0 + (cv1 - cv0) * (.5 + .5 * math.sin(2 * math.pi * k / sides))) for k in range(sides)]
            oface(m, rings[i], uvs, Pt[i] - Pt[j])
    return rings


def otube(m, path, radii, sides=6, want_out=True, **kw):
    """sk tube, then nothing else (sk.tube winding is consistent: outward). Wrapper for symmetry."""
    return m.tube(path, radii, sides=sides, **kw)


def crooked(a, b, n, amp, seed, lean=(0, 0, 0)):
    """Polyline from a to b with n points, random lateral wobble amp (not at ends)."""
    rnd = random.Random(seed)
    a, b = V(a), V(b)
    pts = []
    for i in range(n):
        t = i / (n - 1)
        p = a.lerp(b, t) + V(lean) * t * t
        if 0 < i < n - 1:
            p += V((rnd.uniform(-amp, amp), rnd.uniform(-amp, amp), rnd.uniform(-amp, amp) * .3))
        pts.append(p)
    return pts


def skin(src_obj, name, pred, offset=0.012, keep=0.6, seed=0):
    """Moss skin copy of faces where pred(center, normal) is True."""
    rnd = random.Random(seed)
    me = src_obj.data
    mm = sk.Mesh('Moss'); tile = sk.MATS['Moss'][2]
    for p in me.polygons:
        if not pred(p.center, p.normal) or rnd.random() > keep:
            continue
        pts = [me.vertices[i].co + p.normal * offset for i in p.vertices]
        nn = p.normal
        ax = V((0, 0, 1)) if abs(nn.z) < .9 else V((1, 0, 0))
        u = nn.cross(ax).normalized(); v = nn.cross(u)
        mm.face(pts, [(q.dot(u) / tile, q.dot(v) / tile) for q in pts])
    if not mm.bm.faces:
        mm.bm.free(); return None
    return mm.to_object(name)


def merge(objs, name):
    """Join objects of the same material into one object named name."""
    import bpy
    objs = [o for o in objs if o]
    if len(objs) == 1:
        objs[0].name = name; objs[0].data.name = name; return objs[0]
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = name; o.data.name = name
    return o


def check_orientation(objs, verbose=True):
    """For every connected component of closed solids: signed volume must be > 0 (normals outward).
    Returns list of (object, n_components, n_negative, n_open)."""
    import bmesh
    res = []
    for o in objs:
        if o.get('mat') in ('Plants', 'Moss'):
            continue
        bm = bmesh.new(); bm.from_mesh(o.data)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        seen = set(); comps = []
        for f in bm.faces:
            if f.index in seen:
                continue
            stack = [f]; comp = []
            seen.add(f.index)
            while stack:
                g = stack.pop(); comp.append(g)
                for e in g.edges:
                    for h in e.link_faces:
                        if h.index not in seen:
                            seen.add(h.index); stack.append(h)
            comps.append(comp)
        neg = 0; opn = 0; bad = []
        for comp in comps:
            vol = 0.0
            for g in comp:
                a, b, c = [l.vert.co for l in g.loops]
                vol += a.dot(b.cross(c)) / 6
            edges = set(e for g in comp for e in g.edges)
            if any(len(e.link_faces) != 2 for e in edges):
                opn += 1
            if vol < -1e-7:
                neg += 1; bad.append(tuple(round(x, 2) for x in comp[0].calc_center_median()))
        bm.free()
        res.append((o.name, len(comps), neg, opn))
        if verbose:
            print('ORIENT', o.name, 'components', len(comps), 'inside-out', neg, 'open', opn, bad[:5])
    return res
