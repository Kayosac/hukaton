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


def grain_cap(endm, ring, outward, region='end_grain', fill=0.36, center=None):
    """End-grain disc (Props atlas) on a beam end, fan-triangulated from the centre (safe for notched rings)."""
    u0, v0, u1, v1 = endm.region(region)
    ring = [Vector(p) for p in ring]
    c = Vector(center) if center is not None else sum(ring, Vector()) / len(ring)
    if _normal(ring).dot(outward) < 0:
        ring = ring[::-1]
    ax = (ring[0] - c).normalized()
    ay = Vector(outward).normalized().cross(ax)
    rad = max((p - c).length for p in ring) or 1
    uvf = lambda p: (u0 + (.5 + (p - c).dot(ax) / rad * fill) * (u1 - u0), v0 + (.5 + (p - c).dot(ay) / rad * fill) * (v1 - v0))
    if len(ring) <= 4:
        return endm.face(ring, [uvf(p) for p in ring])
    for i in range(len(ring)):
        a_, b_ = ring[i], ring[(i + 1) % len(ring)]
        oface(endm, [a_, b_, c], [uvf(a_), uvf(b_), uvf(c)], outward)


def _interp_path(P, L, d):
    """point + segment index at arc length d"""
    for i in range(len(P) - 1):
        if d <= L[i + 1] + 1e-9 or i == len(P) - 2:
            f = (d - L[i]) / max(L[i + 1] - L[i], 1e-9)
            return P[i].lerp(P[i + 1], min(max(f, 0), 1)), i, f
    return P[-1], len(P) - 2, 1.0


def beam(m, path, W, H, up=(0, 0, 1), jitter=0.0, seed=0, caps=('flat', 'flat'), endm=None, uvoff=None,
         bevel=0.3, broken=0.6, cracks=(), chips=(), region=None, wob=0.25):
    """Crooked squared timber along `path`. Rings have 8 vertices (4 corners + 4 edge mids) so faces can carry
    real cracks (V-grooves along a face) and chips (corners knocked in).
    W = width (along w = t x up), H = height (along `up`); per-point lists allowed.
    caps per end: None | 'flat' | 'bevel' | 'broken'. endm: Props Mesh -> flat/bevel ends get the end_grain disc.
    cracks: (side, s0, s1, depth) side 0..3 = -up, +w, +up, -w faces; s in 0..1 along the length
            (s1 >= 1 or s0 <= 0 -> the crack runs out through that end and notches it).
    chips: (corner, s, length_m, depth) corner 0..3 = (-w,-up), (+w,-up), (+w,+up), (-w,+up).
    region: map into a Props atlas region instead of tiling (iron straps etc.). wob: mid-vertex wobble factor."""
    rnd = random.Random(seed)
    P = [Vector(p) for p in path]
    n = len(P)
    Ws = list(W) if isinstance(W, (list, tuple)) else [W] * n
    Hs = list(H) if isinstance(H, (list, tuple)) else [H] * n
    tile = m.tile() or 1.0
    uo = uvoff if uvoff else (rnd.uniform(0, 1), rnd.uniform(0, 1))
    L = [0.0]
    for i in range(1, n):
        L.append(L[-1] + (P[i] - P[i - 1]).length)
    LT = L[-1]
    size = max(max(Ws), max(Hs))
    # stations (arc length)
    st = set(L)
    for (side, s0, s1, dep) in cracks:
        a0, a1 = max(s0, 0) * LT, min(s1, 1) * LT
        for f in (0, .3, .55, .8, 1):
            st.add(a0 + (a1 - a0) * f)
    for (cn, s, ln, dep) in chips:
        for f in (-.5, 0, .5):
            st.add(min(max(s * LT + f * ln, 0), LT))
    st = sorted(st)
    S = [st[0]]
    for x in st[1:]:
        if x - S[-1] > 0.008:
            S.append(x)
    if LT - S[-1] > 1e-6:
        S[-1] = LT
    # per-path-point tangents
    T = []
    for i in range(n):
        if i == 0:
            t = (P[1] - P[0]).normalized()
        elif i == n - 1:
            t = (P[-1] - P[-2]).normalized()
        else:
            t = ((P[i + 1] - P[i]).normalized() + (P[i] - P[i - 1]).normalized()).normalized()
        T.append(t)
    outs = []
    rings = []
    cents = []
    tans = []
    for d in S:
        pos, i, f = _interp_path(P, L, d)
        t = T[i].lerp(T[i + 1], f).normalized() if 0 < f < 1 else (T[i] if f <= 0 else T[i + 1])
        wv, hv = _frame(t, up)
        Wd = Ws[i] + (Ws[i + 1] - Ws[i]) * f
        Hd = Hs[i] + (Hs[i + 1] - Hs[i]) * f
        cs = []
        for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            j = Vector((rnd.uniform(-jitter, jitter), rnd.uniform(-jitter, jitter), rnd.uniform(-jitter, jitter)))
            cs.append(pos + wv * sx * Wd / 2 + hv * sy * Hd / 2 + j)
        sideout = [-hv, wv, hv, -wv]
        ring = []
        for k in range(4):
            c0, c1 = cs[k], cs[(k + 1) % 4]
            mid = (c0 + c1) / 2 + sideout[k] * rnd.uniform(-1, 1) * jitter * wob
            ring += [c0, mid]
        sfrac = d / LT
        for (side, s0, s1, dep) in cracks:
            if s0 - 1e-6 <= sfrac <= s1 + 1e-6:
                span = max(s1 - s0, 1e-6)
                if s1 >= 0.999:
                    fz = (sfrac - s0) / span
                elif s0 <= 0.001:
                    fz = 1 - (sfrac - s0) / span
                else:
                    fz = math.sin(math.pi * (sfrac - s0) / span)
                fz = max(fz, 0)
                lat = (cs[(side + 1) % 4] - cs[side]).normalized()
                ring[2 * side + 1] = ring[2 * side + 1] - sideout[side] * dep * fz + lat * rnd.uniform(-.25, .25) * dep * fz
        for (cn, s, ln, dep) in chips:
            if abs(d - s * LT) < 0.004:
                a_, b_ = sideout[(cn - 1) % 4], sideout[cn]
                ring[2 * cn] = ring[2 * cn] - (a_ + b_) * dep * 0.7
        rings.append(ring)
        cents.append(pos)
        tans.append(t)
        outs.append(sideout)
    NS = len(S)

    # broken ends: stagger the whole end ring
    tips = {}
    for end, idx in ((0, 0), (1, NS - 1)):
        if caps[end] != 'broken':
            continue
        tout = -tans[0] if end == 0 else tans[-1]
        ring = rings[idx]
        for j in range(8):
            if j % 2 == 0:
                ring[j] = ring[j] + tout * rnd.uniform(-broken, 0.12) * size
            else:
                ring[j] = ring[j] + tout * rnd.uniform(-broken * .8, 0.3) * size
        tips[end] = cents[idx] + tout * rnd.uniform(-0.1, 0.35) * size + Vector([rnd.uniform(-.2, .2) * size for _ in range(3)])

    def uv_of(p, side, lf, ring_i):
        if region:
            ru0, rv0, ru1, rv1 = m.region(region)
            return (ru0 + ((side + lf) / 4) * (ru1 - ru0), rv0 + (S[ring_i] / max(LT, 1e-6)) * (rv1 - rv0))
        c0 = rings[0][2 * side]; c1 = rings[0][(2 * side + 2) % 8]
        wk = (c0 - c1).length
        u = uo[0] + side * 0.31 + lf * wk / tile
        v = uo[1] + (S[ring_i] + (p - cents[ring_i]).dot(tans[ring_i])) / tile
        return (u, v)

    for i in range(NS - 1):
        for j in range(8):
            j2 = (j + 1) % 8
            side = j // 2
            la, lb = (0.0, 0.5) if j % 2 == 0 else (0.5, 1.0)
            a, b, c, d = rings[i][j], rings[i][j2], rings[i + 1][j2], rings[i + 1][j]
            uvs = [uv_of(a, side, la, i), uv_of(b, side, lb, i), uv_of(c, side, lb, i + 1), uv_of(d, side, la, i + 1)]
            outward = outs[i][side] + outs[i + 1][side]
            oface(m, [a, b, c, d], uvs, outward)

    for end, idx in ((0, 0), (1, NS - 1)):
        stl = caps[end]
        if not stl:
            continue
        tout = -tans[0] if end == 0 else tans[-1]
        ring = rings[idx]
        c = sum(ring, Vector()) / 8
        if stl == 'flat':
            if endm is not None:
                grain_cap(endm, ring, tout, center=c)
            else:
                for j in range(8):
                    a_, b_ = ring[j], ring[(j + 1) % 8]
                    oface(m, [a_, b_, c], [(a_.x / tile, a_.y / tile + a_.z / tile), (b_.x / tile, b_.y / tile + b_.z / tile),
                                          (c.x / tile, c.y / tile + c.z / tile)], tout)
        elif stl == 'bevel':
            bsz = bevel * min(Ws[0 if end == 0 else -1], Hs[0 if end == 0 else -1])
            inner = [c + (p - c) * 0.6 + tout * bsz for p in ring]
            for j in range(8):
                j2 = (j + 1) % 8
                q = [ring[j], ring[j2], inner[j2], inner[j]]
                wk = (ring[j] - ring[j2]).length / tile
                oface(m, q, [(uo[0], 0), (uo[0] + wk, 0), (uo[0] + wk * .8, bsz / tile), (uo[0] + wk * .2, bsz / tile)],
                      tout + ((ring[j] + ring[j2]) / 2 - c).normalized())
            ci = c + tout * bsz
            if endm is not None:
                grain_cap(endm, inner, tout, center=ci)
            else:
                for j in range(8):
                    a_, b_ = inner[j], inner[(j + 1) % 8]
                    oface(m, [a_, b_, ci], [(a_.x / tile, a_.y / tile), (b_.x / tile, b_.y / tile), (ci.x / tile, ci.y / tile)], tout)
        elif stl == 'broken':
            tip = tips[end]
            ax = (ring[0] - c).normalized(); ay = tout.cross(ax)
            def uvp(p):
                dd = p - c
                return (uo[0] + dd.dot(ax) / tile, uo[1] + dd.dot(ay) / tile + 0.5)
            for j in range(8):
                a_, b_ = ring[j], ring[(j + 1) % 8]
                oface(m, [a_, b_, tip], [uvp(a_), uvp(b_), uvp(tip)], tout)
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
         rot=0.0, jitter=0.0, seed=0, flare_end=1.0, radii=None, scale=(1, 1)):
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
            p = P[i] + h * math.cos(a) * rr * scale[0] + w * math.sin(a) * rr * scale[1]
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



# ================================================================ small details
def obox(m, c, ex, ey, ez, size, region=None, seed=0, jitter=0.0):
    """Oriented box (12 tris). ex/ey/ez axes (normalised here), size=(sx,sy,sz). region -> Props atlas fit,
    else metric tiling UVs."""
    rnd = random.Random(seed)
    C = Vector(c)
    ax = [Vector(ex).normalized(), Vector(ey).normalized(), Vector(ez).normalized()]
    hs = [s_ / 2 for s_ in size]
    corner = {}
    for ix in (-1, 1):
        for iy in (-1, 1):
            for iz in (-1, 1):
                corner[(ix, iy, iz)] = C + ax[0] * ix * hs[0] + ax[1] * iy * hs[1] + ax[2] * iz * hs[2] + \
                    Vector((rnd.uniform(-jitter, jitter), rnd.uniform(-jitter, jitter), rnd.uniform(-jitter, jitter)))
    faces = [((0, 1), [(-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)], ax[2]),
             ((0, 1), [(-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1)], -ax[2]),
             ((1, 2), [(1, -1, -1), (1, 1, -1), (1, 1, 1), (1, -1, 1)], ax[0]),
             ((1, 2), [(-1, -1, -1), (-1, 1, -1), (-1, 1, 1), (-1, -1, 1)], -ax[0]),
             ((0, 2), [(-1, 1, -1), (1, 1, -1), (1, 1, 1), (-1, 1, 1)], ax[1]),
             ((0, 2), [(-1, -1, -1), (1, -1, -1), (1, -1, 1), (-1, -1, 1)], -ax[1])]
    tile = m.tile() or 1.0
    for (a, b), idx, nrm in faces:
        pts = [corner[i] for i in idx]
        if region:
            u0, v0, u1, v1 = m.region(region)
            uvs = [(u0 + (i[a] * .5 + .5) * (u1 - u0), v0 + (i[b] * .5 + .5) * (v1 - v0)) for i in idx]
        else:
            uvs = [((i[a] * hs[a]) / tile + C.x * .7, (i[b] * hs[b]) / tile + C.y * .5 + C.z * .3) for i in idx]
        oface(m, pts, uvs, nrm)


def nail(pm, pos, nrm, size=0.022, out=0.008, bent=False, seed=0, region='iron'):
    """Iron nail head on a surface (square, slightly rotated); bent=True adds a bent-over shank stub."""
    rnd = random.Random(seed)
    N = Vector(nrm).normalized()
    a = Vector((0, 0, 1)) if abs(N.z) < .8 else Vector((1, 0, 0))
    e1 = (a - N * a.dot(N)).normalized()
    e1 = Matrix.Rotation(rnd.uniform(-.6, .6), 3, N) @ e1
    e2 = N.cross(e1)
    obox(pm, Vector(pos) + N * out / 2, e1, e2, N, (size, size, out), region=region)
    if bent:
        P0 = Vector(pos) + N * out
        d = (e1 * rnd.uniform(-1, 1) + e2 * rnd.uniform(-1, 1)).normalized()
        rope(pm, [P0, P0 + N * 0.03, P0 + N * 0.04 + d * 0.03], 0.004, up=d.cross(N) if d.cross(N).length > .1 else e1,
             sides=4, region=region, fit=True)


def lump(pm, c, radii, region, sides=6, rings=2, jitter=0.2, seed=0, flat_bottom=True, rot=0.0):
    """Faceted lump on the Props atlas (clod / pebble / seed / crumb). Bottom flat at c.z when flat_bottom."""
    rnd = random.Random(seed)
    C = Vector(c)
    rx, ry, rz = radii
    u0, v0, u1, v1 = pm.region(region)
    lat0 = 0.0 if flat_bottom else -math.pi / 2 + math.pi / (rings + 1)
    R = []
    for i in range(rings):
        lat = lat0 + (math.pi / 2 - lat0) * i / rings
        ring = []
        for k in range(sides):
            a = rot + 2 * math.pi * k / sides + (0.5 * (i % 2)) * 2 * math.pi / sides
            f = 1 + rnd.uniform(-jitter, jitter)
            z = math.sin(lat) * rz * f if (i or not flat_bottom) else 0.0
            ring.append(C + Vector((math.cos(a) * math.cos(lat) * rx * f, math.sin(a) * math.cos(lat) * ry * f, z)))
        R.append(ring)
    top = C + Vector((rnd.uniform(-.2, .2) * rx, rnd.uniform(-.2, .2) * ry, rz * rnd.uniform(.85, 1.1)))
    uv = lambda k, i: (u0 + (k / sides) * (u1 - u0), v0 + (i / (rings + 1)) * (v1 - v0))
    for i in range(rings - 1):
        for k in range(sides):
            k2 = (k + 1) % sides
            q = [R[i][k], R[i][k2], R[i + 1][k2], R[i + 1][k]]
            oface(pm, q, [uv(k, i), uv(k + 1, i), uv(k + 1, i + 1), uv(k, i + 1)], sum(q, Vector()) / 4 - C)
    for k in range(sides):
        k2 = (k + 1) % sides
        q = [R[-1][k], R[-1][k2], top]
        oface(pm, q, [uv(k, rings - 1), uv(k + 1, rings - 1), ((u0 + u1) / 2, v1)], sum(q, Vector()) / 3 - C)
    if flat_bottom:
        oface(pm, R[0], [uv(k, 0) for k in range(sides)], Vector((0, 0, -1)))
    else:
        bot = C - Vector((0, 0, rz))
        for k in range(sides):
            k2 = (k + 1) % sides
            q = [R[0][k], R[0][k2], bot]
            oface(pm, q, [uv(k, 0), uv(k + 1, 0), ((u0 + u1) / 2, v0)], sum(q, Vector()) / 3 - C)


def leaf(pm, base, direction, length, width, region='leaf_dry_0', fold=0.25, curl=0.3, normal=(0, 0, 1), seed=0):
    """Dry leaf: folded along the midrib, tip curled up. Single-sided, facing `normal`."""
    rnd = random.Random(seed)
    B = Vector(base); D = Vector(direction).normalized(); Nn = Vector(normal).normalized()
    D = (D - Nn * D.dot(Nn)).normalized()
    S = D.cross(Nn) * -1
    u0, v0, u1, v1 = pm.region(region)
    ts = [0, .3, .65, 1]
    ws = [0.12, .95, .8, 0.0]
    mids, lefts, rights = [], [], []
    for t, wf in zip(ts, ws):
        lift = curl * length * t * t
        m_ = B + D * length * t + Nn * (lift + 0.004)
        hw = width / 2 * wf * rnd.uniform(.85, 1.1)
        mids.append(m_)
        lefts.append(m_ + S * hw + Nn * (fold * hw))
        rights.append(m_ - S * hw + Nn * (fold * hw))
    for i in range(3):
        for side, arr, uside in ((1, lefts, 0.0), (-1, rights, 1.0)):
            q = [mids[i], arr[i], arr[i + 1], mids[i + 1]]
            uvs = [((u0 + u1) / 2, v0 + ts[i] * (v1 - v0)), (u0 + uside * (u1 - u0) * 1 + (1 - uside) * 0, v0 + ts[i] * (v1 - v0)),
                   (u0 + uside * (u1 - u0), v0 + ts[i + 1] * (v1 - v0)), ((u0 + u1) / 2, v0 + ts[i + 1] * (v1 - v0))]
            if (arr[i + 1] - arr[i]).length < 1e-5 or (arr[i] - mids[i]).length < 1e-5:
                q = [q[0], q[1], q[3]] if (arr[i + 1] - mids[i + 1]).length < 1e-5 else q
                uvs = [uvs[0], uvs[1], uvs[3]] if len(q) == 3 else uvs
            oface(pm, q, uvs, Nn)


def shaving(pm, c, r=0.03, turns=1.3, width=0.012, seed=0, region='needles'):
    """Curled wood shaving (a flat ribbon curl) lying on the ground."""
    rnd = random.Random(seed)
    C = Vector(c)
    a0 = rnd.uniform(0, 6.28)
    ax = Vector((math.cos(a0), math.sin(a0), 0))
    pts = []
    n = 7
    for i in range(n):
        a = turns * 2 * math.pi * i / (n - 1)
        rr = r * (1 - 0.35 * i / n)
        pts.append(C + ax * (i / n) * 0.03 + Vector((0, 0, r)) + (ax.cross(Vector((0, 0, 1))) * math.cos(a) + Vector((0, 0, 1)) * math.sin(a)) * rr)
    rope(pm, pts, 1.0, up=ax, sides=4, rot=math.pi / 4, region=region, fit=True, caps=False,
         scale=(width / 1.414, 0.003 / 1.414))


def scatter_ground(pm, center, radius, seed=0, clods=6, pebbles=5, chips=5, leaves=3, shavings=2, needles=3,
                   avoid=(), ring_min=0.0, wm=None, squash=(1, 1)):
    """Small stuff around a base on z = center.z: earth clods, pebbles, wood chips, dry leaves, shavings, needle bits.
    avoid: list of (x, y, r) discs to keep free. ring_min: inner radius fraction to keep free."""
    rnd = random.Random(seed)
    C = Vector(center)
    placed = []
    def spot(rmin=ring_min):
        for _ in range(60):
            a = rnd.uniform(0, 6.283)
            rr = radius * math.sqrt(rnd.uniform(rmin * rmin, 1))
            p = C + Vector((math.cos(a) * rr * squash[0], math.sin(a) * rr * squash[1], 0))
            if all((p.x - x) ** 2 + (p.y - y) ** 2 > r_ * r_ for x, y, r_ in avoid):
                return p
        return C + Vector((radius, 0, 0))
    for i in range(clods):
        p = spot(); s_ = rnd.uniform(0.03, 0.09)
        lump(pm, p, (s_ * rnd.uniform(1, 1.5), s_, s_ * rnd.uniform(.5, .85)), 'earth_props', sides=rnd.choice((5, 6, 7)), rings=2,
             jitter=0.25, seed=seed * 100 + i, rot=rnd.uniform(0, 6))
    for i in range(pebbles):
        p = spot(); s_ = rnd.uniform(0.015, 0.045)
        lump(pm, p, (s_ * rnd.uniform(1, 1.6), s_, s_ * rnd.uniform(.5, .8)), 'pebble', sides=5, rings=2, jitter=0.2,
             seed=seed * 100 + 30 + i, rot=rnd.uniform(0, 6))
    for i in range(chips):
        p = spot(); a = rnd.uniform(0, 6.28)
        L_ = rnd.uniform(0.05, 0.14)
        ex = Vector((math.cos(a), math.sin(a), rnd.uniform(-.25, .25)))
        ez = Vector((rnd.uniform(-.25, .25), rnd.uniform(-.25, .25), 1))
        ey = ez.cross(ex)
        ez = ex.cross(ey)
        obox(pm, p + Vector((0, 0, 0.012)), ex, ey, ez, (L_, L_ * rnd.uniform(.25, .5), rnd.uniform(.012, .022)),
             region='wood_props', jitter=0.004, seed=seed * 100 + 60 + i)
    for i in range(leaves):
        p = spot(); a = rnd.uniform(0, 6.28)
        leaf(pm, p, (math.cos(a), math.sin(a), 0), rnd.uniform(0.09, 0.16), rnd.uniform(0.05, 0.08),
             region=rnd.choice(('leaf_dry_0', 'leaf_dry_1', 'leaf_dry_2')), seed=seed * 100 + 70 + i)
    for i in range(shavings):
        shaving(pm, spot(), r=rnd.uniform(0.018, 0.03), seed=seed * 100 + 80 + i)
    for i in range(needles):
        p = spot(); a = rnd.uniform(0, 6.28)
        d = Vector((math.cos(a), math.sin(a), 0))
        obox(pm, p + Vector((0, 0, 0.004)), d, Vector((0, 0, 1)).cross(d), (0, 0, 1),
             (rnd.uniform(0.06, 0.12), 0.006, 0.006), region='needles', seed=seed * 100 + 90 + i)


def strap(pm, center, axis, e1, h1, h2, width=0.05, thick=0.008, seed=0, nails=2):
    """Iron strap band wrapped around a beam (loop around the beam section, centred on `center`).
    axis: beam direction; e1: one section axis; h1/h2 half sizes of the beam section."""
    A = Vector(axis).normalized(); E1 = (Vector(e1) - A * Vector(e1).dot(A)).normalized(); E2 = A.cross(E1)
    loop = loop_rect(center, E1, E2, h1 + thick / 2, h2 + thick / 2, ch=0.08, jit=0.002, seed=seed)
    rope(pm, loop, 1.0, up=A, sides=4, rot=math.pi / 4, closed=True, region='iron', fit=True,
         scale=(width / 1.414, thick / 1.414))
    rnd = random.Random(seed)
    for i in range(nails):
        sgn = 1 if i % 2 == 0 else -1
        nrm = E1 * sgn if i < 2 else E2 * sgn
        hh = h1 if i < 2 else h2
        nail(pm, Vector(center) + nrm * (hh + thick) + E2 * rnd.uniform(-.3, .3) * (h2 if i < 2 else h1),
             nrm, size=0.018, out=0.006, seed=seed + i, region='dark')


def splinter(pm, p, d, length=0.08, r=0.01, seed=0, region='wood_props'):
    """Thin pointed sliver of wood sticking out of a broken spot."""
    rnd = random.Random(seed)
    P0 = Vector(p); D = Vector(d).normalized()
    bend = Vector((rnd.uniform(-.3, .3), rnd.uniform(-.3, .3), rnd.uniform(-.3, .3)))
    pts = [P0, P0 + D * length * .5 + bend * length * .1, P0 + D * length + bend * length * .25]
    upv = Vector((0, 0, 1)) if abs(D.z) < .8 else Vector((1, 0, 0))
    rope(pm, pts, r, up=upv, sides=3, region=region, fit=True, radii=[r, r * .6, r * .12], caps=False, rot=rnd.uniform(0, 1))


def patch(wm, pm, c, nrm, size=(0.2, 0.14), thick=0.022, up=(0, 0, 1), seed=0, nails_n=4):
    """Small board nailed over a hole: a dark hole edge peeks out, board + nails."""
    rnd = random.Random(seed)
    N = Vector(nrm).normalized()
    U = Vector(up); U = (U - N * U.dot(N)).normalized()
    U = Matrix.Rotation(rnd.uniform(-.25, .25), 3, N) @ U
    R = U.cross(N)
    C = Vector(c)
    # dark hole rim (slightly larger, irregular) just above the surface
    hole = []
    for k in range(7):
        a = 2 * math.pi * k / 7
        hole.append(C + N * 0.003 + R * math.cos(a) * size[0] * .62 * rnd.uniform(.8, 1.15) + U * math.sin(a) * size[1] * .7 * rnd.uniform(.8, 1.2) + U * 0.02)
    u0, v0, u1, v1 = pm.region('dark')
    oface(pm, hole, [(u0 + (.5 + .4 * math.cos(2 * math.pi * k / 7)) * (u1 - u0), v0 + (.5 + .4 * math.sin(2 * math.pi * k / 7)) * (v1 - v0)) for k in range(7)], N)
    obox(wm, C + N * (thick / 2 + 0.004), R, U, N, (size[0], size[1], thick), jitter=0.004, seed=seed)
    for i in range(nails_n):
        sx = (-1 if i % 2 else 1) * size[0] * .38
        sy = (-1 if i < 2 else 1) * size[1] * .33
        nail(pm, C + N * (thick + 0.004) + R * sx + U * sy, N, size=0.018, out=0.006, seed=seed + 10 + i, bent=(i == 3))
