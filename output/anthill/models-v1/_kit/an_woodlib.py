"""Shared helpers for the wood / rope / iron anthill assets (AN14, AN16-AN19, AN30).
Built on top of the kit (ak.Mesh); does not modify the kit."""
import sys, math, random, os, json
sys.path.insert(0, '/home/claude/ant/kit')
import ak
from mathutils import Vector, Matrix


def vec(p):
    return Vector(p)


def _normal(pts):
    c = sum(pts, Vector()) / len(pts)
    n = Vector((0, 0, 0))
    for i in range(len(pts)):
        n += (pts[i] - c).cross(pts[(i + 1) % len(pts)] - c)
    return n


def oface(m, pts, uvs, outward):
    """Face with its winding chosen so the normal points along `outward`."""
    pts = [Vector(p) for p in pts]
    if _normal(pts).dot(Vector(outward)) < 0:
        pts = pts[::-1]; uvs = uvs[::-1]
    return m.face(pts, uvs)


def _frame(t, up):
    h = Vector(up) - t * Vector(up).dot(t)
    if h.length < 1e-5:
        alt = Vector((1, 0, 0)) if abs(t.x) < .9 else Vector((0, 1, 0))
        h = alt - t * alt.dot(t)
    h.normalize()
    w = t.cross(h).normalized()
    return w, h


def grain_cap(endm, ring, outward, region='end_grain', fill=0.36):
    """End-grain disc (Props atlas) on a beam end; ring corners are kept inside the painted disc."""
    u0, v0, u1, v1 = endm.region(region)
    c = sum(ring, Vector()) / len(ring)
    n = _normal(ring)
    if n.dot(outward) < 0:
        ring = ring[::-1]
    ax = (ring[0] - c).normalized()
    ay = _normal(ring).normalized().cross(ax)
    rad = max((p - c).length for p in ring) or 1
    uvs = [(u0 + (.5 + (p - c).dot(ax) / rad * fill) * (u1 - u0), v0 + (.5 + (p - c).dot(ay) / rad * fill) * (v1 - v0)) for p in ring]
    return endm.face(ring, uvs)


def beam(m, path, W, H, up=(0, 0, 1), jitter=0.0, seed=0, caps=('flat', 'flat'), endm=None, uvoff=None,
         bevel=0.3, broken=0.6):
    """Crooked squared timber along `path` (list of points). W = width (across, along w), H = height (along `up`).
    W/H may be per-point lists. caps per end: None | 'flat' | 'bevel' | 'broken'. endm: Props Mesh -> flat/bevel
    end faces get the end_grain disc. Tiling UVs in metres, grain (V) along the beam. Returns the rings."""
    rnd = random.Random(seed)
    P = [Vector(p) for p in path]
    n = len(P)
    Ws = list(W) if isinstance(W, (list, tuple)) else [W] * n
    Hs = list(H) if isinstance(H, (list, tuple)) else [H] * n
    tile = m.tile()
    uo = uvoff if uvoff else (rnd.uniform(0, 1), rnd.uniform(0, 1))
    T = []
    rings = []
    for i in range(n):
        if i == 0:
            t = (P[1] - P[0]).normalized()
        elif i == n - 1:
            t = (P[-1] - P[-2]).normalized()
        else:
            t = ((P[i + 1] - P[i]).normalized() + (P[i] - P[i - 1]).normalized()).normalized()
        T.append(t)
        w, h = _frame(t, up)
        ring = []
        for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            j = Vector((rnd.uniform(-jitter, jitter), rnd.uniform(-jitter, jitter), rnd.uniform(-jitter, jitter)))
            ring.append(P[i] + w * sx * Ws[i] / 2 + h * sy * Hs[i] / 2 + j)
        rings.append(ring)
    L = [0.0]
    for i in range(1, n):
        L.append(L[-1] + (P[i] - P[i - 1]).length)
    size = max(max(Ws), max(Hs))

    # broken ends: stagger the end ring along the axis, add jagged mid-edge teeth
    mids = {}
    for end, idx, tout in ((0, 0, -T[0]), (1, n - 1, T[-1])):
        if caps[end] != 'broken':
            continue
        ring = rings[idx]
        for k in range(4):
            ring[k] = ring[k] + tout * rnd.uniform(-broken, 0.15) * size
        mm = []
        for k in range(4):
            a, b = ring[k], ring[(k + 1) % 4]
            mm.append((a + b) / 2 + tout * rnd.uniform(-broken * .8, 0.35) * size)
        mids[end] = mm

    def vcoord(p):
        return (p - P[0]).dot(T[0])  # rough; refined below per segment

    for i in range(n - 1):
        cen = (P[i] + P[i + 1]) / 2
        for k in range(4):
            k2 = (k + 1) % 4
            a, b = rings[i][k], rings[i][k2]
            c, d = rings[i + 1][k2], rings[i + 1][k]
            wk = (a - b).length
            u0 = uo[0] + k * 0.31
            u1 = u0 + wk / tile
            t_seg = (P[i + 1] - P[i]).normalized()
            va = uo[1] + (L[i] + (a - P[i]).dot(t_seg)) / tile
            vb = uo[1] + (L[i] + (b - P[i]).dot(t_seg)) / tile
            vc = uo[1] + (L[i] + (c - P[i]).dot(t_seg)) / tile
            vd = uo[1] + (L[i] + (d - P[i]).dot(t_seg)) / tile
            outward = (a + b + c + d) / 4 - cen
            outward = outward - t_seg * outward.dot(t_seg)
            pts = [a, b, c, d]
            uvs = [(u0, va), (u1, vb), (u1, vc), (u0, vd)]
            if i == n - 2 and 1 in mids:
                mp = mids[1][k]
                pts = [a, b, c, mp, d]
                uvs = [(u0, va), (u1, vb), (u1, vc), ((u0 + u1) / 2, uo[1] + (L[i] + (mp - P[i]).dot(t_seg)) / tile), (u0, vd)]
            if i == 0 and 0 in mids:
                mp = mids[0][k]
                pts = pts[:1] + [mp] + pts[1:]
                uvs = uvs[:1] + [((u0 + u1) / 2, uo[1] + (L[i] + (mp - P[i]).dot(t_seg)) / tile)] + uvs[1:]
            oface(m, pts, uvs, outward)

    for end, idx, tout in ((0, 0, -T[0]), (1, n - 1, T[-1])):
        st = caps[end]
        if not st:
            continue
        ring = rings[idx]
        c = sum(ring, Vector()) / 4
        if st == 'flat':
            if endm is not None:
                grain_cap(endm, ring, tout)
            else:
                oface(m, ring, [(p.x / tile, p.y / tile + p.z / tile) for p in ring], tout)
        elif st == 'bevel':
            bsz = bevel * min(Ws[idx], Hs[idx])
            inner = [c + (p - c) * 0.6 + tout * bsz for p in ring]
            for k in range(4):
                k2 = (k + 1) % 4
                q = [ring[k], ring[k2], inner[k2], inner[k]]
                wk = (ring[k] - ring[k2]).length / tile
                oface(m, q, [(uo[0], 0), (uo[0] + wk, 0), (uo[0] + wk * .8, bsz / tile), (uo[0] + wk * .2, bsz / tile)],
                      tout + ((ring[k] + ring[k2]) / 2 - c).normalized())
            if endm is not None:
                grain_cap(endm, inner, tout)
            else:
                oface(m, inner, [(p.x / tile, p.y / tile + p.z / tile) for p in inner], tout)
        elif st == 'broken':
            mm = mids[end]
            tip = c + tout * rnd.uniform(-0.1, 0.4) * size + Vector((rnd.uniform(-.2, .2) * size, rnd.uniform(-.2, .2) * size, rnd.uniform(-.2, .2) * size))
            ax = (ring[0] - c).normalized(); ay = tout.cross(ax)
            def uvp(p):
                d = p - c
                return (uo[0] + d.dot(ax) / tile, uo[1] + d.dot(ay) / tile + 0.5)
            for k in range(4):
                k2 = (k + 1) % 4
                for a, b in ((ring[k], mm[k]), (mm[k], ring[k2])):
                    oface(m, [a, b, tip], [uvp(a), uvp(b), uvp(tip)], tout)
    return rings


def peg(m, center, nrm, size=0.07, length=0.06, up=(0, 0, 1), endm=None, seed=0, sink=0.03):
    """Square wooden peg (nagel) head sticking out of a surface along nrm."""
    rnd = random.Random(seed)
    N = Vector(nrm).normalized()
    C = Vector(center)
    upv = Vector(up)
    # small random rotation of the peg head around its axis
    ang = rnd.uniform(-.25, .25)
    upv = Matrix.Rotation(ang, 3, N) @ upv
    beam(m, [C - N * sink, C + N * length], size * rnd.uniform(.9, 1.1), size * rnd.uniform(.9, 1.1), up=upv,
         jitter=size * 0.06, seed=seed, caps=(None, 'flat'), endm=endm)


def resample(P, step, closed=False):
    P = [Vector(p) for p in P]
    if closed:
        P = P + [P[0]]
    L = [0.0]
    for i in range(1, len(P)):
        L.append(L[-1] + (P[i] - P[i - 1]).length)
    tot = L[-1]
    N = max(2, int(round(tot / step)))
    out = []
    j = 0
    for s in range(N + (0 if closed else 1)):
        d = tot * s / N
        while j < len(P) - 2 and L[j + 1] < d:
            j += 1
        f = (d - L[j]) / max(L[j + 1] - L[j], 1e-9)
        out.append(P[j].lerp(P[j + 1], min(max(f, 0), 1)))
    return out


def rope(pm, pts, r, up=(0, 0, 1), sides=6, closed=False, region='rope', step=None, k=6, fit=False, caps=True,
         rot=0.0, jitter=0.0, seed=0, flare_end=1.0, radii=None):
    """Tube on the Props atlas region (rope / iron). up: vector perpendicular-ish to the path, fixes the ring
    orientation (use the loop plane normal for loops). step: resample spacing (None = keep points).
    UV: around the tube -> region V; along -> region U, one region width per k segments (or fit whole path)."""
    rnd = random.Random(seed)
    P = resample(pts, step, closed) if step else [Vector(p) for p in pts]
    n = len(P)
    u0, v0, u1, v1 = pm.region(region)
    rings = []
    for i in range(n):
        if closed:
            t = (P[(i + 1) % n] - P[i - 1]).normalized()
        else:
            t = (P[min(i + 1, n - 1)] - P[max(i - 1, 0)]).normalized()
        w, h = _frame(t, up)
        rr = radii[i] if radii else r
        if not closed and i == n - 1:
            rr *= flare_end
        ring = []
        for q in range(sides):
            a = rot + 2 * math.pi * q / sides
            p = P[i] + h * math.cos(a) * rr + w * math.sin(a) * rr
            p += Vector((rnd.uniform(-jitter, jitter), rnd.uniform(-jitter, jitter), rnd.uniform(-jitter, jitter)))
            ring.append(p)
        rings.append(ring)
    L = [0.0]
    for i in range(1, n):
        L.append(L[-1] + (P[i] - P[i - 1]).length)
    tot = L[-1] + ((P[0] - P[-1]).length if closed else 0)
    segs = n if closed else n - 1
    for i in range(segs):
        i2 = (i + 1) % n
        if fit:
            s0 = L[i] / tot; s1 = (L[i2] if i2 else tot) / tot
        else:
            s0 = (i % k) / k; s1 = s0 + 1 / k
        cen = (P[i] + P[i2]) / 2
        for q in range(sides):
            q2 = (q + 1) % sides
            a0, a1 = q / sides, (q + 1) / sides
            pts4 = [rings[i][q], rings[i][q2], rings[i2][q2], rings[i2][q]]
            uvs = [(u0 + s0 * (u1 - u0), v0 + a0 * (v1 - v0)), (u0 + s0 * (u1 - u0), v0 + a1 * (v1 - v0)),
                   (u0 + s1 * (u1 - u0), v0 + a1 * (v1 - v0)), (u0 + s1 * (u1 - u0), v0 + a0 * (v1 - v0))]
            oface(pm, pts4, uvs, sum(pts4, Vector()) / 4 - cen)
    if not closed and caps:
        for idx, tout in ((0, P[0] - P[1]), (n - 1, P[-1] - P[-2])):
            ring = rings[idx]
            c = sum(ring, Vector()) / sides
            uvs = [(u0 + (.5 + .4 * math.cos(2 * math.pi * q / sides)) * (u1 - u0) * .25,
                    v0 + (.5 + .4 * math.sin(2 * math.pi * q / sides)) * (v1 - v0)) for q in range(sides)]
            oface(pm, ring, uvs, tout)
    return rings


def loop_rect(c, e1, e2, h1, h2, ch=0.35, jit=0.0, seed=0):
    """Closed 8-point loop (rectangle with cut corners) centred at c, half-extents h1 along e1, h2 along e2."""
    rnd = random.Random(seed)
    c, e1, e2 = Vector(c), Vector(e1).normalized(), Vector(e2).normalized()
    q = 1 - ch
    uv = [(1, -q), (1, q), (q, 1), (-q, 1), (-1, q), (-1, -q), (-q, -1), (q, -1)]
    return [c + e1 * a * h1 + e2 * b * h2 + Vector((rnd.uniform(-jit, jit), rnd.uniform(-jit, jit), rnd.uniform(-jit, jit)))
            for a, b in uv]


def save_manifest(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    json.dump(data, open(path, 'w'), indent=1, ensure_ascii=False)


def render_set(objs, out, sheet_png, title, extra=None, size=(400, 400), samples=10, el=20):
    os.makedirs(out, exist_ok=True)
    tiles = []
    labels = []
    for az, lab in ((-45, 'front-left'), (45, 'front-right'), (135, 'rear-right'), (-135, 'rear-left')):
        p = os.path.join(out, 'r_%s.png' % lab)
        ak.render(objs, p, az=az, el=el, size=size, samples=samples)
        tiles.append(p); labels.append(lab)
    if extra:
        for (objs2, lab, kw) in extra:
            p = os.path.join(out, 'r_%s.png' % lab)
            ak.render(objs2, p, size=size, samples=samples, **kw)
            tiles.append(p); labels.append(lab)
    cols = 4 if len(tiles) <= 4 else len(tiles)
    ak.sheet(tiles, os.path.join(out, 'card.jpg'), cols=cols, labels=labels, concept=sheet_png, title=title)


def post_prism(m, bot, top, wb, db, wt, dt, notches=(), seed=0, jitter=0.0, top_tilt=(0, 0), top_cap='bevel',
               bevel=0.04, endm=None, bottom=False):
    """Square post as a tapered prism with real carved notches (recesses).
    bot/top: centre points of the bottom/top. wb/db, wt/dt: width (X) / depth (Y) at bottom/top.
    notches: list of (side, u0, u1, v0, v1, depth) with side in 'ny','px','py','nx' (front, right, back, left),
    u/v in 0..1 across the face (u left->right seen from outside, v bottom->top).
    top_tilt: (dz per +x, dz per +y) metres of slant of the top. Returns the 8 corners."""
    rnd = random.Random(seed)
    B, T = Vector(bot), Vector(top)
    def J():
        return Vector((rnd.uniform(-jitter, jitter), rnd.uniform(-jitter, jitter), rnd.uniform(-jitter, jitter) * .3))
    # corners ccw seen from above: (-x,-y), (+x,-y), (+x,+y), (-x,+y)
    sg = ((-1, -1), (1, -1), (1, 1), (-1, 1))
    bc = [B + Vector((sx * wb / 2, sy * db / 2, 0)) + J() for sx, sy in sg]
    tc = [T + Vector((sx * wt / 2, sy * dt / 2, sx * top_tilt[0] + sy * top_tilt[1])) + J() for sx, sy in sg]
    # side faces as (bottom-left, bottom-right) corner indices seen from outside
    sides = {'ny': (0, 1), 'px': (1, 2), 'py': (2, 3), 'nx': (3, 0)}
    tile = m.tile()
    vb = sorted(set([0.0, 1.0] + [v for nt in notches for v in (nt[3], nt[4])]))
    cen_axis = lambda v: B.lerp(T, v)
    for si, (side, (i0, i1)) in enumerate(sides.items()):
        A_, B_, C_, D_ = bc[i0], bc[i1], tc[i1], tc[i0]
        def at(u, v):
            return (A_.lerp(B_, u)).lerp(D_.lerp(C_, u), v)
        my = [nt for nt in notches if nt[0] == side]
        ub = sorted(set([0.0, 1.0] + [u for nt in my for u in (nt[1], nt[2])]))
        wlen = (A_ - B_).length
        hlen = (A_ - D_).length
        nrm = (B_ - A_).cross(D_ - A_).normalized()
        uo = si * 0.27 + rnd.random()
        def uv(u, v):
            return (uo + u * wlen / tile, v * hlen / tile + B.x * .3)
        for a in range(len(ub) - 1):
            for b in range(len(vb) - 1):
                u0, u1, v0, v1 = ub[a], ub[a + 1], vb[b], vb[b + 1]
                hole = None
                for nt in my:
                    if nt[1] <= u0 + 1e-6 and u1 <= nt[2] + 1e-6 and nt[3] <= v0 + 1e-6 and v1 <= nt[4] + 1e-6:
                        hole = nt
                q = [at(u0, v0), at(u1, v0), at(u1, v1), at(u0, v1)]
                quv = [uv(u0, v0), uv(u1, v0), uv(u1, v1), uv(u0, v1)]
                if hole is None:
                    oface(m, q, quv, nrm)
                    continue
                dep = hole[5]
                # recess: back wall + walls only on the borders of the notch
                qi = [x - nrm * dep for x in q]
                oface(m, qi, quv, nrm)
                inward_edges = []
                if abs(u0 - hole[1]) < 1e-6: inward_edges.append((3, 0, (B_ - A_).normalized()))
                if abs(u1 - hole[2]) < 1e-6: inward_edges.append((1, 2, -(B_ - A_).normalized()))
                if abs(v0 - hole[3]) < 1e-6: inward_edges.append((0, 1, (D_ - A_).normalized()))
                if abs(v1 - hole[4]) < 1e-6: inward_edges.append((2, 3, -(D_ - A_).normalized()))
                for e0, e1, inn in inward_edges:
                    wq = [q[e0], q[e1], qi[e1], qi[e0]]
                    L = (q[e0] - q[e1]).length / tile
                    oface(m, wq, [(uo, 0), (uo + L, 0), (uo + L, dep / tile), (uo, dep / tile)], inn)
    # top
    up = Vector((0, 0, 1))
    if top_cap == 'bevel':
        c = sum(tc, Vector()) / 4
        inner = [c + (p - c) * 0.72 + Vector((0, 0, bevel)) + J() * .5 for p in tc]
        for k in range(4):
            k2 = (k + 1) % 4
            q = [tc[k], tc[k2], inner[k2], inner[k]]
            L = (tc[k] - tc[k2]).length / tile
            oface(m, q, [(0, 0), (L, 0), (L * .8, .03), (L * .2, .03)], up + ((tc[k] + tc[k2]) / 2 - c).normalized())
        if endm is not None:
            grain_cap(endm, inner, up)
        else:
            oface(m, inner, [(p.x / tile, p.y / tile) for p in inner], up)
    else:
        if endm is not None:
            grain_cap(endm, tc, up)
        else:
            oface(m, tc, [(p.x / tile, p.y / tile) for p in tc], up)
    if bottom:
        oface(m, bc, [(p.x / tile, p.y / tile) for p in bc], -up)
    return bc + tc
