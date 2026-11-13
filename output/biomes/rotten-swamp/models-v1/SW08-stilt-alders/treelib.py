"""Tree helpers for the swamp tree items (SW07-SW09), built on the shared kit sk.py (kit is not modified).
Crooked low-sided tubes with per-vertex lobes (root flare), joined branches, splintered broken tops,
layered crown clump cards and hanging strand cards, all with the kit 'Wind' UV2 (1000 + metres)."""
import math, random, sys
sys.path.insert(0, '/home/claude/swamp/kit')
import sk
from mathutils import Vector


def V(p):
    return Vector(p)


def lerp_path(P, t):
    """Point at fraction t (0..1) of the arc length of polyline P."""
    P = [V(p) for p in P]
    L = [0.0]
    for i in range(1, len(P)):
        L.append(L[-1] + (P[i] - P[i - 1]).length)
    s = t * L[-1]
    for i in range(1, len(P)):
        if L[i] >= s:
            f = (s - L[i - 1]) / max(L[i] - L[i - 1], 1e-9)
            return P[i - 1].lerp(P[i], f)
    return P[-1].copy()


def dir_at(P, t):
    a = lerp_path(P, max(0, t - .03)); b = lerp_path(P, min(1, t + .03))
    return (b - a).normalized()


def crook(points, amp, seed, sub=1):
    """Insert `sub` midpoints between control points with random lateral offsets (crooked look)."""
    rnd = random.Random(seed)
    P = [V(p) for p in points]
    out = [P[0]]
    for i in range(1, len(P)):
        a, b = P[i - 1], P[i]
        d = (b - a)
        for s in range(1, sub + 1):
            f = s / (sub + 1)
            q = a.lerp(b, f)
            off = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-.4, .4))) * amp * d.length
            off -= d.normalized() * off.dot(d.normalized())
            out.append(q + off)
        out.append(b)
    return out


def _frames(P, up_hint=None):
    n = len(P)
    X, T = [], []
    prev = None
    for i in range(n):
        t = (P[min(i + 1, n - 1)] - P[max(i - 1, 0)]).normalized()
        if prev is None:
            h = V(up_hint).normalized() if up_hint else None
            if h is None or abs(h.dot(t)) > .9:
                h = Vector((0, 0, 1)) if abs(t.z) < .9 else Vector((1, 0, 0))
            x = h
        else:
            x = prev
        x = (x - t * x.dot(t)).normalized()
        prev = x
        X.append(x); T.append(t)
    return T, X


def tube(meshes, path, radii, sides=6, rot=0.0, lobes=None, jitter=0.0, seed=0, caps=(False, False),
         wind=None, up_hint=None, shift=None, tip=None, cap_apex=None, u_off=0.0, twist=0.0):
    """Crooked tube. meshes: one sk.Mesh or a list (one per segment -> material per segment).
    radii: float or per point. lobes: dict ring_index -> list of per-vertex radius multipliers.
    shift: dict ring_index -> list of per-vertex offsets ALONG the tube (jagged material boundary).
    wind: callable(Vector)->(sway, flutter). tip: Vector -> close the end with a cone to that point.
    cap_apex: (start_apex, end_apex) Vectors for cone caps instead of flat ones.
    Returns (rings, Lcum)."""
    rnd = random.Random(seed)
    P = [V(p) for p in path]
    n = len(P)
    if not isinstance(radii, (list, tuple)):
        radii = [radii] * n
    if callable(meshes):
        mfn = meshes
    elif isinstance(meshes, (list, tuple)):
        mfn = lambda i, k, _m=meshes: _m[i]
    else:
        mfn = lambda i, k, _m=meshes: _m
    meshes = [mfn(i, 0) for i in range(n - 1)]
    T, X = _frames(P, up_hint)
    L = [0.0]
    for i in range(1, n):
        L.append(L[-1] + (P[i] - P[i - 1]).length)
    rings = []
    for i in range(n):
        t, x = T[i], X[i]
        y = t.cross(x)
        ring = []
        lb = lobes.get(i) if lobes else None
        sh = shift.get(i) if shift else None
        for k in range(sides):
            a = rot + twist * i + 2 * math.pi * k / sides
            r = radii[i] * (lb[k % len(lb)] if lb else 1.0)
            p = P[i] + x * math.cos(a) * r + y * math.sin(a) * r
            if sh:
                p += t * sh[k % len(sh)]
            if jitter and 0 < i:
                p += Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))) * jitter * radii[i]
            ring.append(p)
        rings.append(ring)
    W = (lambda p: wind(p)) if wind else (lambda p: (0.0, 0.0))
    for i in range(n - 1):
        circ = 2 * math.pi * max(radii[i], radii[i + 1])
        for k in range(sides):
            m = mfn(i, k)
            tile = m.tile() or 1.0
            k2 = (k + 1) % sides
            u0 = u_off + (k / sides) * circ / tile
            u1 = u_off + ((k + 1) / sides) * circ / tile
            sa = (shift.get(i) if shift else None)
            sb = (shift.get(i + 1) if shift else None)
            v00 = (L[i] + (sa[k % len(sa)] if sa else 0)) / tile
            v01 = (L[i] + (sa[k2 % len(sa)] if sa else 0)) / tile
            v10 = (L[i + 1] + (sb[k % len(sb)] if sb else 0)) / tile
            v11 = (L[i + 1] + (sb[k2 % len(sb)] if sb else 0)) / tile
            a, b, c, d = rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k]
            m.quad(a, b, c, d, (u0, v00), (u1, v01), (u1, v11), (u0, v10), [W(a), W(b), W(c), W(d)])
    if caps[0]:
        m = meshes[0]
        if cap_apex and cap_apex[0] is not None:
            _cone(m, list(reversed(rings[0])), V(cap_apex[0]), W)
        else:
            m.cap(rings[0], flip=True)
    if caps[1] or tip is not None:
        m = meshes[-1]
        if tip is not None:
            _cone(m, rings[-1], V(tip), W)
        elif cap_apex and cap_apex[1] is not None:
            _cone(m, rings[-1], V(cap_apex[1]), W)
        else:
            m.cap(rings[-1])
    return rings, L


def _cone(m, ring, apex, W):
    tile = m.tile() or 1.0
    n = len(ring)
    c = sum(ring, Vector()) / n
    for k in range(n):
        a, b = ring[k], ring[(k + 1) % n]
        h = (apex - c).length
        m.tri(a, b, apex, (k / n * .6 / tile, 0), ((k + 1) / n * .6 / tile, 0), ((k + .5) / n * .6 / tile, h / tile),
              [W(a), W(b), W(apex)])


def broken_top(mout, minner, ring, axis, v0, radius, seed, spikes=(0.55,), low=(0.04, 0.22), sink=0.06,
               region='grey_wood', wind=None, u_off=0.0, slope=0.0, pinch=0.35):
    """Splintered broken end on top of `ring` (outer bark on mout, broken wood inside on minner Props region).
    spikes: heights of the tall splinters (the rest are low notches). v0: v coordinate (metres) of the ring."""
    rnd = random.Random(seed)
    W = wind or (lambda p: (0.0, 0.0))
    ax = V(axis).normalized()
    n = len(ring)
    c = sum(ring, Vector()) / n
    base = []
    for i in range(2 * n):
        if i % 2 == 0:
            base.append(ring[i // 2].copy())
        else:
            base.append((ring[i // 2] + ring[(i // 2 + 1) % n]) / 2)
    sa = rnd.uniform(0, 6.283)
    H = [rnd.uniform(*low) + slope * (0.5 + 0.5 * math.cos(math.pi * i / n - sa)) for i in range(2 * n)]
    idx = list(range(0, 2 * n))
    rnd.shuffle(idx)
    for j, h in enumerate(spikes):
        H[idx[j]] = h
        # neighbours of a spike rise a bit (splinter shoulder)
        H[(idx[j] + 1) % (2 * n)] = max(H[(idx[j] + 1) % (2 * n)], h * rnd.uniform(.25, .5))
    J = []
    hmax = max(H)
    for i in range(2 * n):
        f = H[i] / hmax
        inward = 0.08 + pinch * f
        J.append(base[i] + ax * H[i] + (c - base[i]) * inward)
    tile = mout.tile() or 1.0
    circ = 2 * math.pi * radius
    for k in range(n):
        R0, R1 = ring[k], ring[(k + 1) % n]
        Ja, Jm, Jb = J[2 * k], J[2 * k + 1], J[(2 * k + 2) % (2 * n)]
        u0 = u_off + k / n * circ / tile; u1 = u_off + (k + 1) / n * circ / tile; um = (u0 + u1) / 2
        vr = v0 / tile
        ua = (u0, (v0 + H[2 * k]) / tile); umv = (um, (v0 + H[2 * k + 1]) / tile); ub = (u1, (v0 + H[(2 * k + 2) % (2 * n)]) / tile)
        mout.tri(R0, R1, Jm, (u0, vr), (u1, vr), umv, [W(R0), W(R1), W(Jm)])
        mout.tri(R0, Jm, Ja, (u0, vr), umv, ua, [W(R0), W(Jm), W(Ja)])
        mout.tri(R1, Jb, Jm, (u1, vr), ub, umv, [W(R1), W(Jb), W(Jm)])
    C = c + ax * (min(H) - sink)
    u0, vv0, u1, vv1 = minner.region(region, minner.mat)
    for i in range(2 * n):
        a, b = J[i], J[(i + 1) % (2 * n)]
        fa = i / (2 * n); fb = (i + 1) / (2 * n)
        minner.tri(a, b, C, (u0 + fa * (u1 - u0), vv1), (u0 + fb * (u1 - u0), vv1), ((u0 + u1) / 2, vv0),
                   [W(a), W(b), W(C)])
    return J


def clump(m, center, right, fwd, region, lift=0.12, droop=0.1, wind=None, flutter=0.03, seed=0):
    """Leaf clump card: 3x3-vertex grid (8 tris) across `right` (half) and `fwd` (half) with a raised centre
    and drooping corners (like the forest crown clumps)."""
    rnd = random.Random(seed)
    W = wind or (lambda p: (0.0, 0.0))
    C, R, F = V(center), V(right), V(fwd)
    N = R.normalized().cross(F.normalized())
    if N.z < 0:
        N = -N
    u0, v0, u1, v1 = m.region(region, m.mat)
    G = {}
    for i in (-1, 0, 1):
        for j in (-1, 0, 1):
            e = abs(i) + abs(j)
            h = lift if e == 0 else (-droop * (1.0 if e == 2 else .5)) * rnd.uniform(.6, 1.3)
            G[(i, j)] = C + R * i + F * j + N * h
    for i in (-1, 0):
        for j in (-1, 0):
            q = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
            pts = [G[a] for a in q]
            uvs = [(u0 + (a[0] + 1) / 2 * (u1 - u0), v0 + (a[1] + 1) / 2 * (v1 - v0)) for a in q]
            wv = []
            for a, p in zip(q, pts):
                s, _ = W(p)
                wv.append((s, flutter * (0.4 + 0.6 * (abs(a[0]) + abs(a[1])) / 2)))
            m.face(pts, uvs, wv)


def hang_card(m, top, right, length, region, out=Vector((0, 0, 0)), segs=3, sway0=0.0, sway=0.12, flutter=0.04,
              curl=0.0):
    """Hanging strand card from `top` (top centre) straight down `length` m, half-width vector `right`.
    out: horizontal push of the lower end (bend, quadratic). curl: first segment rises/arches outward."""
    u0, v0, u1, v1 = m.region(region, m.mat)
    T, R, O = V(top), V(right), V(out)
    rows = []
    for s in range(segs + 1):
        t = s / segs
        p = T + Vector((0, 0, -length * t)) + O * t * t
        if curl and s == 0:
            p = p + Vector((0, 0, curl))
        rows.append((p, t))
    for s in range(segs):
        (pa, ta), (pb, tb) = rows[s], rows[s + 1]
        va, vb = v1 - (v1 - v0) * ta, v1 - (v1 - v0) * tb
        wa = (sway0 + sway * ta * ta, flutter * ta); wb = (sway0 + sway * tb * tb, flutter * tb)
        m.quad(pa - R, pa + R, pb + R, pb - R, (u0, va), (u1, va), (u1, vb), (u0, vb), [wa, wa, wb, wb])




def resample(path, radii, n, amp=0.0, seed=0, keep_first=2):
    """Resample a control polyline to n points (arc-length), with lateral kinks of `amp` metres
    (first `keep_first` points kept exactly). Radii interpolated."""
    rnd = random.Random(seed)
    P = [V(p) for p in path]
    L = [0.0]
    for i in range(1, len(P)):
        L.append(L[-1] + (P[i] - P[i - 1]).length)
    out, rad = [], []
    for j in range(n):
        if j < keep_first:
            out.append(P[j].copy()); rad.append(radii[j]); continue
        s = L[keep_first - 1] + (L[-1] - L[keep_first - 1]) * (j - keep_first + 1) / (n - keep_first)
        for i in range(1, len(P)):
            if L[i] >= s - 1e-9:
                f = (s - L[i - 1]) / max(L[i] - L[i - 1], 1e-9)
                q = P[i - 1].lerp(P[i], f)
                r = radii[i - 1] + (radii[i] - radii[i - 1]) * f
                break
        if 0 < j < n - 1:
            d = dir_at(P, s / L[-1])
            off = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))) * amp
            off -= d * off.dot(d)
            q = q + off
            r *= rnd.uniform(.92, 1.08)
        out.append(q); rad.append(r)
    return out, rad


def flare(m, center, axis_r, sides=15, lobes=5, seed=0, heights=(-0.22, 0.04, 0.24, 0.5),
          scales=(1.95, 1.7, 1.3, 0.92), amp=(0.2, 0.17, 0.1, 0.03), wind=None, tilt=(0.0, 0.0)):
    """Soft root flare: a many-sided collar with `lobes` rounded cosine lobes (buttress roots) that melts into the
    ground (lowest ring below z=0). Its top ring sits just inside the trunk. tilt: xy offset per metre of height
    (follow a leaning trunk)."""
    rnd = random.Random(seed)
    ph = rnd.uniform(0, 6.28)
    lob_amp = [rnd.uniform(.7, 1.3) for _ in range(lobes)]
    C = V(center)
    rings = []
    for j, (h, sc, am) in enumerate(zip(heights, scales, amp)):
        ring = []
        for k in range(sides):
            a = 2 * math.pi * k / sides
            li = int(((a + ph) % (2 * math.pi)) / (2 * math.pi) * lobes) % lobes
            r = axis_r * sc * (1 + am * lob_amp[li] * math.cos(lobes * a + ph))
            ring.append(C + Vector((math.cos(a) * r + tilt[0] * h, math.sin(a) * r + tilt[1] * h, h)))
        rings.append(ring)
    W = wind or (lambda p: (0.0, 0.0))
    tile = m.tile() or 1.0
    L = [0.0]
    for j in range(1, len(rings)):
        L.append(L[-1] + (heights[j] - heights[j - 1]) * 1.3)
    for j in range(len(rings) - 1):
        circ = 2 * math.pi * axis_r * scales[j]
        for k in range(sides):
            k2 = (k + 1) % sides
            a, b, c, d = rings[j][k], rings[j][k2], rings[j + 1][k2], rings[j + 1][k]
            u0 = k / sides * circ / tile; u1 = (k + 1) / sides * circ / tile
            m.quad(a, b, c, d, (u0, L[j] / tile), (u1, L[j] / tile), (u1, L[j + 1] / tile), (u0, L[j + 1] / tile),
                   [W(a), W(b), W(c), W(d)])
    m.cap(rings[0], flip=True)
    return rings


def shard(mout, base, d, L, w, seed, wind=None):
    """Tall thin splinter (3-sided prism tapering to a point) standing on the rim of a break."""
    rnd = random.Random(seed)
    d = V(d).normalized()
    pts = [V(base), V(base) + d * L * .5 + Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0)) * w * .3]
    tube(mout, pts, [w, w * .6], sides=3, rot=rnd.uniform(0, 6), seed=seed, wind=wind, caps=(False, False),
         tip=V(base) + d * L)


def apply_wind(obj, fn, only_unset=False):
    """Add fn(vertex position)[0] to the sway channel of the 'Wind' UV2 of obj (e.g. moss skins draped on
    swaying bark, so they move with it). only_unset: touch only loops whose sway is still 0."""
    if obj is None:
        return
    me = obj.data
    wl = me.uv_layers['Wind']
    for poly in me.polygons:
        for li in poly.loop_indices:
            d = wl.data[li]
            if only_unset and abs(d.uv[0] - 1000.0) > 1e-6:
                continue
            p = me.vertices[me.loops[li].vertex_index].co
            d.uv = (d.uv[0] + fn(Vector(p))[0], d.uv[1])
