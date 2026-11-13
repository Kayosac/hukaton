"""Shared helpers for the 'Paths & fences' group (SW13, SW14, SW19, SW22, SW24, SW25).
Built on top of the kit (sk.Mesh) - only adds geometry builders, never edits the kit.

beam()   crooked n-sided prism along a path (logs, posts, poles, rails, pickets, rope) with cut / broken /
         pointed / ragged ends; tiling UVs (grain along the piece) or an atlas region (iron, rope).
plank()  flat board with crooked outline, sag, twist and cut or splintered (jagged) ends; grain along the board.
sleeve() moss skin around the lower part of a post (ragged top edge), from the rings returned by beam().
grid_card() alpha card made of a deformable grid (nets, rags), with hang-point wind.
"""
import math, random, sys
sys.path.insert(0, '/home/claude/swamp/kit')
import sk
from mathutils import Vector, Matrix

V = Vector


def _frames(P, up_hint):
    n = len(P)
    T = []
    for i in range(n):
        if i == 0:
            t = P[1] - P[0]
        elif i == n - 1:
            t = P[-1] - P[-2]
        else:
            t = (P[i + 1] - P[i]).normalized() + (P[i] - P[i - 1]).normalized()
        T.append(t.normalized())
    X = []
    prev = None
    for i in range(n):
        t = T[i]
        h = prev if prev is not None else V(up_hint)
        if prev is None and abs(h.normalized().dot(t)) > 0.95:
            h = V((1, 0, 0)) if abs(t.x) < 0.9 else V((0, 1, 0))
        x = (h - t * h.dot(t)).normalized()
        prev = x
        X.append(x)
    return T, X


def beam(m, path, radii, sides=5, rot=None, scale=None, ends=('cut', 'cut'), props=None, cap_region='end_grain_0',
         seed=0, jitter=0.0, region=None, region_len=0.3, up_hint=(0, 0, 1), broken_len=0.12, ragged=0.1,
         uv_off=None, closed=False, wind=None):
    """Crooked prism. path: points; radii: float or list; scale: list of (s_up, s_side) per point.
    rot default = pi/sides -> a flat face points to up_hint (walkable log tops, square posts).
    ends: 'cut' (flat; end-grain disc on `props` if given, else planar cap), 'broken' (jagged splintered end),
    'point' (sharpened), 'ragged' (open with uneven edge, for moss sleeves), 'open'.
    region: atlas region on m (e.g. 'iron', 'rope') instead of tiling UV; region_len metres per region repeat.
    Returns rings."""
    rnd = random.Random(seed)
    P = [V(p) for p in path]
    n = len(P)
    if not isinstance(radii, (list, tuple)):
        radii = [radii] * n
    if rot is None:
        rot = math.pi / sides
    T, X = _frames(P, up_hint)
    rings = []
    for i in range(n):
        t, x = T[i], X[i]
        y = t.cross(x)
        su, ss = scale[i] if scale else (1, 1)
        ring = []
        for k in range(sides):
            a = rot + 2 * math.pi * k / sides
            p = P[i] + x * math.cos(a) * radii[i] * su + y * math.sin(a) * radii[i] * ss
            if jitter:
                p = p + V((rnd.uniform(-jitter, jitter), rnd.uniform(-jitter, jitter), rnd.uniform(-jitter, jitter)))
            ring.append(p)
        rings.append(ring)
    # end shaping
    for e, idx in ((ends[0], 0), (ends[1], n - 1)):
        sgn = -1 if idx == 0 else 1
        t = T[idx] * sgn
        if e == 'broken':
            ring = rings[idx]
            spl = rnd.randrange(sides)
            for k in range(sides):
                d = rnd.uniform(0.0, broken_len) * (1.7 if k == spl else 1.0)
                ring[k] = ring[k] + t * d
        elif e == 'ragged':
            ring = rings[idx]
            L = (P[idx] - P[idx - sgn]).length if n > 1 else 0.1
            for k in range(sides):
                ring[k] = ring[k] - t * rnd.uniform(0, min(ragged, L * 0.9))
    # side faces
    Lc = [0.0]
    for i in range(1, n):
        Lc.append(Lc[-1] + (P[i] - P[i - 1]).length)
    tile = m.tile() or 1.0
    if uv_off is None:
        uv_off = (rnd.random(), rnd.random())
    if region:
        u0, v0, u1, v1 = m.region(region, m.mat)
    for i in range(n - 1):
        circ = 2 * math.pi * max(radii[i], radii[i + 1])
        for k in range(sides):
            k2 = (k + 1) % sides
            if region:
                s0 = Lc[i] / region_len
                f = s0 - math.floor(s0)
                s1 = min(1.0, f + (Lc[i + 1] - Lc[i]) / region_len)
                a0, a1 = u0 + f * (u1 - u0), u0 + s1 * (u1 - u0)
                b0, b1 = v0 + k / sides * (v1 - v0), v0 + (k + 1) / sides * (v1 - v0)
                uvs = [(a0, b0), (a0, b1), (a1, b1), (a1, b0)]
            else:
                ua = uv_off[0] + k / sides * circ / tile
                ub = uv_off[0] + (k + 1) / sides * circ / tile
                va = uv_off[1] + Lc[i] / tile
                vb = uv_off[1] + Lc[i + 1] / tile
                uvs = [(ua, va), (ub, va), (ub, vb), (ua, vb)]
            wv = None
            if wind:
                wv = [wind(rings[i][k]), wind(rings[i][k2]), wind(rings[i + 1][k2]), wind(rings[i + 1][k])]
            m.quad(rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k], *uvs, wind=wv)
    if closed:
        return rings
    # caps
    for e, idx in ((ends[0], 0), (ends[1], n - 1)):
        flip = idx == 0
        ring = rings[idx]
        sgn = -1 if idx == 0 else 1
        t = T[idx] * sgn
        c = sum(ring, V()) / len(ring)
        if e == 'cut':
            if props is not None:
                props.cap(ring, flip=flip, region=cap_region)
            elif region:
                m.cap(ring, flip=flip, region=region)
            else:
                m.cap(ring, flip=flip)
        elif e in ('broken', 'point'):
            r = radii[idx]
            ap = c + t * (rnd.uniform(0.2, 0.7) * broken_len if e == 'broken' else r * 2.2)
            ap += V((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))) * r * (0.35 if e == 'broken' else 0.1)
            rr = list(reversed(ring)) if flip else ring
            for k in range(sides):
                a, b = rr[k], rr[(k + 1) % sides]
                if region:
                    uu0, vv0, uu1, vv1 = m.region(region, m.mat)
                    m.tri(a, b, ap, (uu0, vv0), (uu1, vv0), ((uu0 + uu1) / 2, vv1))
                else:
                    m.tri(a, b, ap, (k * .07, 0), ((k + 1) * .07, 0), (k * .07 + .03, (ap - a).length / tile))
    return rings


def sleeve(moss, rings, k_top, grow=0.012, ragged=0.12, seed=0, k_bot=0, plants=None, cover=0.45, drips=2):
    """Irregular moss PATCHES climbing part-way up ONE side of a post/pole (never a full wrap).
    rings: from beam(); patches use the faces between rings[k_bot]..rings[k_top]; a contiguous run of columns
    (about `cover` of the sides) climbs to random heights with ragged tops and ragged side edges; plus one small
    stray patch on another side. plants: optional Plants Mesh -> a few moss-drip cards from the patch tops."""
    rnd = random.Random(seed)
    sides = len(rings[0])
    k_top = min(k_top, len(rings) - 1)
    nseg = max(1, k_top - k_bot)
    c0 = rnd.randrange(sides)
    ncol = max(1, min(sides - 1, int(round(sides * cover * rnd.uniform(0.8, 1.2)))))
    cols = [((c0 + j) % sides, j) for j in range(ncol)]
    stray = (c0 + ncol + 1 + rnd.randrange(max(1, sides - ncol - 1))) % sides
    if stray not in [c for c, _ in cols]:
        cols.append((stray, -1))
    tile = moss.tile()
    tops = []
    for k, j in cols:
        k2 = (k + 1) % sides
        edge = j in (0, ncol - 1) or j < 0
        if j < 0:
            climb = 1; frac = rnd.uniform(0.3, 0.6); start = k_bot + rnd.randrange(nseg)
        else:
            mid = 1 - abs((j + 0.5) / ncol - 0.5) * 1.4
            climb = max(1, min(nseg, int(round(nseg * rnd.uniform(0.5, 1.0) * mid + 0.5))))
            frac = rnd.uniform(0.35, 0.95)
            start = k_bot
        for i in range(start, min(start + climb, k_top)):
            a, b = rings[i][k], rings[i][k2]
            d, c = rings[i + 1][k], rings[i + 1][k2]
            n = (b - a).cross(c - a)
            if n.length < 1e-9:
                continue
            n.normalize()
            last = i == min(start + climb, k_top) - 1
            s0 = rnd.uniform(0.08, 0.35) if (edge and (j <= 0)) else 0.0
            s1 = 1 - rnd.uniform(0.08, 0.35) if (edge and (j == ncol - 1 or j < 0)) else 1.0
            t0 = rnd.uniform(0.0, 0.3) if j < 0 else 0.0

            def bil(s, t):
                return (a.lerp(b, s)).lerp(d.lerp(c, s), t) + n * grow
            if last:
                tt = [frac * rnd.uniform(0.6, 1.0) for _ in range(4)]
                topl = [(s1, tt[0]), (s0 + (s1 - s0) * 0.66, tt[1]), (s0 + (s1 - s0) * 0.33, tt[2]), (s0, tt[3])]
            else:
                topl = [(s1, 1.0), (s0, 1.0)]
            poly = [(s0, t0), (s0 + (s1 - s0) * 0.5, t0 + rnd.uniform(0, 0.12) * (1 if j < 0 else 0)), (s1, t0)] + topl
            P = [bil(*q) for q in poly]
            C = sum(P, V()) / len(P)
            for q in range(len(P)):
                p0, p1 = P[q], P[(q + 1) % len(P)]
                moss.tri(C, p0, p1, ((C.x + C.y) / tile, C.z / tile), ((p0.x + p0.y) / tile, p0.z / tile),
                         ((p1.x + p1.y) / tile, p1.z / tile))
            if last and j >= 0:
                tops.append((bil(s0 + (s1 - s0) * 0.5, max(tt) * 0.9), (b - a) * 0.5 * (s1 - s0), n))
    if plants is not None and tops:
        rnd.shuffle(tops)
        for (p, half, n) in tops[:drips]:
            plants.card(p + n * 0.012, half * 0.9, V((0, 0, rnd.uniform(0.12, 0.22))), 'moss_drip_%d' % rnd.randint(0, 1),
                        hang=True, segs=1, wind_sway=0.0, wind_flutter=0.01)


def plank(m, c, along, across, length, width, thick, ends=('cut', 'cut'), sag=0.0, twist=0.0, crook=0.0, segs=3,
          seed=0, jag_len=0.12, wvar=0.08, uv_off=None):
    """Flat board centred at c. along/across: unit-ish vectors (normal = across x along points 'up').
    sag: downward bow at the middle (m, along -normal); twist: radians over the length; crook: sideways bow (m).
    ends: 'cut' (slightly skewed straight cut) or 'broken' (jagged splinters). Returns dict(top=[...] outline pts)."""
    rnd = random.Random(seed)
    A = V(along).normalized(); C = V(across)
    C = (C - A * C.dot(A)).normalized()
    N = C.cross(A)
    Cw = V(c)
    L, W, Th = length, width, thick
    vs = [-L / 2 + L * i / segs for i in range(segs + 1)]
    wl = [W / 2 * (1 + rnd.uniform(-wvar, wvar)) for _ in vs]
    wr = [W / 2 * (1 + rnd.uniform(-wvar, wvar)) for _ in vs]

    def F(u, v, w):
        s = v / (L / 2)
        w2 = w - sag * (1 - s * s)
        u2 = u + crook * (1 - s * s)
        a = twist * s / 2
        uu = u2 * math.cos(a) - w2 * math.sin(a)
        ww = u2 * math.sin(a) + w2 * math.cos(a)
        return Cw + C * uu + A * v + N * ww

    # outline in (u, v): start seq L0..R0 (u increasing), end seq Rn..Ln (u decreasing)
    def end_seq(v_edge, sgn, kind, uL, uR, skew):
        pts = []
        if kind == 'broken':
            k = rnd.randint(3, 5)
            for j in range(1, k):
                u = uL + (uR - uL) * j / k + rnd.uniform(-.2, .2) * (uR - uL) / k
                pts.append((u, v_edge + sgn * rnd.uniform(0.0, jag_len) * (1.8 if j == k // 2 else 1)))
            return pts
        return []
    sk0 = rnd.uniform(-.03, .03); sk1 = rnd.uniform(-.03, .03)
    L0 = (-wl[0], vs[0] - sk0); R0 = (wr[0], vs[0] + sk0)
    Ln = (-wl[-1], vs[-1] + sk1); Rn = (wr[-1], vs[-1] - sk1)
    if ends[0] == 'broken':
        L0 = (L0[0], L0[1] + rnd.uniform(0, jag_len * .6)); R0 = (R0[0], R0[1] + rnd.uniform(0, jag_len * .6))
    if ends[1] == 'broken':
        Ln = (Ln[0], Ln[1] - rnd.uniform(0, jag_len * .6)); Rn = (Rn[0], Rn[1] - rnd.uniform(0, jag_len * .6))
    s_mid = end_seq(vs[0], -1, ends[0], L0[0], R0[0], sk0)            # u increasing
    e_mid = list(reversed(end_seq(vs[-1], 1, ends[1], Ln[0], Rn[0], sk1)))  # u decreasing
    left = [L0] + [(-wl[i], vs[i]) for i in range(1, segs)] + [Ln]
    right = [R0] + [(wr[i], vs[i]) for i in range(1, segs)] + [Rn]
    tile = m.tile() or 1.0
    if uv_off is None:
        uv_off = (rnd.random(), rnd.random())

    def uv(p):
        return (uv_off[0] + p[0] / tile, uv_off[1] + p[1] / tile)

    h = Th / 2
    for w, flip in ((h, False), (-h, True)):
        polys = []
        for i in range(segs):
            polys.append([left[i], right[i], right[i + 1], left[i + 1]])
        if s_mid:
            M = ((left[0][0] + right[0][0]) / 2, (left[0][1] + right[0][1]) / 2)
            seq = [left[0]] + s_mid + [right[0]]
            for j in range(len(seq) - 1):
                polys.append([M, seq[j], seq[j + 1]])
        if e_mid:
            M = ((left[-1][0] + right[-1][0]) / 2, (left[-1][1] + right[-1][1]) / 2)
            seq = [right[-1]] + e_mid + [left[-1]]
            for j in range(len(seq) - 1):
                polys.append([M, seq[j], seq[j + 1]])
        for pg in polys:
            if flip:
                pg = list(reversed(pg))
            m.face([F(p[0], p[1], w) for p in pg], [uv(p) for p in pg])
    # sides: closed CCW perimeter
    per = [left[0]] + s_mid + right + e_mid + list(reversed(left[1:]))
    s = 0.0
    for j in range(len(per)):
        p, q = per[j], per[(j + 1) % len(per)]
        dl = math.hypot(q[0] - p[0], q[1] - p[1])
        if dl < 1e-6:
            continue
        pb, qb, qt, pt = F(p[0], p[1], -h), F(q[0], q[1], -h), F(q[0], q[1], h), F(p[0], p[1], h)
        m.quad(pb, qb, qt, pt, (uv_off[0], uv_off[1] + s / tile), (uv_off[0], uv_off[1] + (s + dl) / tile),
               (uv_off[0] + Th / tile, uv_off[1] + (s + dl) / tile), (uv_off[0] + Th / tile, uv_off[1] + s / tile))
        s += dl
    return {'F': F, 'N': N, 'A': A, 'C': C}


def grid_card(m, region, P, nu, nv, wind=None, flip_v=False, cut=None, sub=(0, 0, 1, 1)):
    """Deformable alpha card. P(i, j) -> world point for grid indices i in 0..nu, j in 0..nv (j=0 = top/hang edge).
    Region u spans i, region v spans j (top of region at j=0). wind(i, j) -> (sway, flutter).
    cut(i, j) -> True to omit a cell (extra tear)."""
    U0, V0, U1, V1 = m.region(region, m.mat)
    # sub-rectangle of the region (fractions, v measured from the bottom of the region)
    u0, u1 = U0 + (U1 - U0) * sub[0], U0 + (U1 - U0) * sub[2]
    v0, v1 = V0 + (V1 - V0) * sub[1], V0 + (V1 - V0) * sub[3]
    G = [[V(P(i, j)) for j in range(nv + 1)] for i in range(nu + 1)]

    def uv(i, j):
        jj = (nv - j) / nv if not flip_v else j / nv
        return (u0 + (u1 - u0) * i / nu, v0 + (v1 - v0) * jj)
    for i in range(nu):
        for j in range(nv):
            if cut and cut(i, j):
                continue
            idx = [(i, j + 1), (i + 1, j + 1), (i + 1, j), (i, j)]
            pts = [G[a][b] for a, b in idx]
            uvs = [uv(a, b) for a, b in idx]
            w = [wind(a, b) for a, b in idx] if wind else None
            m.face(pts, uvs, w)
    return G


def tuft(pl, base, h, w, seed, kind='sedge', n=3, lean=(0, 0)):
    rnd = random.Random(seed)
    if kind == 'sedge':
        reg = 'sedge_%d' % rnd.randint(0, 2)
    elif kind == 'cattail':
        reg = 'cattail_leaves_%d' % rnd.randint(0, 1)
    else:
        reg = kind
    pl.cross_cards(base, w, h, reg, n=n, rot=rnd.uniform(0, 3), lean=lean, seed=seed,
                   wind_sway=0.05 * h, wind_flutter=0.02)


def drips_along(pl, a, b, seed, n=None, hmin=0.12, hmax=0.3, out=(0, 0, 0)):
    """Hang moss-drip cards under the edge a->b (cards span pieces of the edge)."""
    rnd = random.Random(seed)
    A, B = V(a), V(b)
    L = (B - A).length
    n = n or max(1, int(L / 0.35))
    for k in range(n):
        if rnd.random() < 0.3:
            continue
        t0 = (k + rnd.uniform(0, .3)) / n
        t1 = (k + 1 - rnd.uniform(0, .3)) / n
        p0, p1 = A.lerp(B, t0), A.lerp(B, t1)
        mid = (p0 + p1) / 2 + V(out)
        pl.card(mid, (p1 - p0) / 2, V((0, 0, rnd.uniform(hmin, hmax))), 'moss_drip_%d' % rnd.randint(0, 1),
                hang=True, segs=1, wind_sway=0.0, wind_flutter=0.012)


def drape_into(moss, obj, min_nz=0.75, keep=0.5, seed=0, offset=0.012, region_fn=None, patch=True):
    """Moss on upward faces of obj, written into an existing Moss Mesh. patch=True: an irregular blob (fan of
    triangles) inside each chosen face instead of the whole face (reads like painted moss clumps).
    region_fn(center)->bool limits where moss grows."""
    rnd = random.Random(seed)
    me = obj.data
    tile = sk.MATS['Moss'][2]
    for p in me.polygons:
        n = p.normal
        if n.z < min_nz or rnd.random() > keep:
            continue
        if region_fn and not region_fn(p.center):
            continue
        pts = [me.vertices[i].co + n * offset for i in p.vertices]
        if not patch or len(pts) != 4:
            moss.face(pts, [(q.x / tile, q.y / tile) for q in pts])
            continue
        a, b, c, d = pts

        def bil(s, t):
            return (a.lerp(b, s)).lerp(d.lerp(c, s), t)
        L1 = (b - a).length; L2 = (d - a).length
        sc, tc = rnd.uniform(.3, .7), rnd.uniform(.3, .7)
        rs = rnd.uniform(.25, .5) * (1 if L1 < 0.6 else 0.6 / L1 * 1.4)
        rt = rnd.uniform(.25, .5) * (1 if L2 < 0.6 else 0.6 / L2 * 1.4)
        k = rnd.randint(7, 10)
        ring = []
        for j in range(k):
            ang = 2 * math.pi * j / k + rnd.uniform(-.2, .2)
            f = rnd.uniform(.45, 1.0)
            s = min(.99, max(.01, sc + math.cos(ang) * rs * f))
            t = min(.99, max(.01, tc + math.sin(ang) * rt * f))
            ring.append(bil(s, t))
        C = bil(sc, tc)
        for j in range(k):
            q0, q1 = ring[j], ring[(j + 1) % k]
            moss.tri(C, q0, q1, (C.x / tile, C.y / tile), (q0.x / tile, q0.y / tile), (q1.x / tile, q1.y / tile))


def finish(vid, meshes, drape=()):
    """meshes: dict mat->Mesh (may include 'Moss' for sleeves). drape: list of (mat, min_nz, keep, seed[, region_fn]).
    Returns objects (one per material, named <vid>_<Mat>), Moss last-but-plants."""
    objs = {}
    for mat, mm in meshes.items():
        if mat in ('Moss',):
            continue
        if not mm.bm.faces:
            mm.bm.free(); continue
        objs[mat] = mm.to_object('%s_%s' % (vid, mat))
    if 'Moss' in meshes:
        mo = meshes['Moss']
        for d in drape:
            if d[0] in objs:
                drape_into(mo, objs[d[0]], d[1], d[2], d[3], region_fn=d[4] if len(d) > 4 else None)
        if mo.bm.faces:
            objs['Moss'] = mo.to_object('%s_Moss' % vid)
        else:
            mo.bm.free()
    return list(objs.values())


def views(objs, outdir, tag, angles, water_z=None, size=(360, 300), samples=10, fov=24):
    import os
    os.makedirs(outdir, exist_ok=True)
    out = []
    for name, az, el in angles:
        p = os.path.join(outdir, '%s_%s.png' % (tag, name))
        sk.render(objs, p, az=az, el=el, size=size, water_z=water_z, samples=samples, fov=fov)
        out.append(p)
    return out


def place(objs, dx, dy=0.0):
    for o in objs:
        o.location.x += dx
        o.location.y += dy


def rope_loop(props, pts, r=0.014, seed=0, round_k=0.1):
    """Closed rope loop through corner points (list of Vectors), corners softened, Props 'rope' region."""
    P = [V(p) for p in pts]
    path = []
    n = len(P)
    for i in range(n):
        a, b = P[i], P[(i + 1) % n]
        path.append(a.lerp(b, round_k)); path.append(a.lerp(b, 1 - round_k))
    path.append(path[0])
    beam(props, path, r, sides=4, ends=('open', 'open'), region='rope', region_len=0.16, seed=seed, closed=True,
         up_hint=(0, 0, 1))


def lash(props, J, U, B, wu, du, wc, dc, r=0.014, seed=0, turns=2, wraps=1):
    """Rope lashing of a crossbar (axis B, half-height wc, half-depth dc) nailed IN FRONT of an upright
    (axis U, half-width wu, half-depth du) at joint centre J (on the upright axis). Front normal N = B x U.
    Two diagonal loops (X on the front and on the back) + wraps around the upright above/below the bar."""
    rnd = random.Random(seed)
    J, U, B = V(J), V(U).normalized(), V(B).normalized()
    B = (B - U * B.dot(U)).normalized()
    N = B.cross(U)
    f = du + 2 * dc            # crossbar front face depth (from the upright centre plane)
    def W(b, u, n):
        return J + B * b + U * u + N * n
    for t in range(turns):
        e = r * 0.8 + t * r * 1.5
        sh = (t - (turns - 1) / 2) * r * 2.2
        for sgn in (1, -1):
            pts = [W(-sgn * (wu + e) + sh, wc + e, f + e), W(sgn * (wu + e) + sh, -wc - e, f + e),
                   W(sgn * (wu + e) + sh, -wc - e, -du - e), W(-sgn * (wu + e) + sh, wc + e, -du - e)]
            rope_loop(props, pts, r, seed=seed + t * 3 + sgn)
    for k in range(wraps):
        for side in (1, -1):
            u = side * (wc + r * (1.3 + 2.2 * k)) + rnd.uniform(-.004, .004)
            e = r + 0.003
            pts = [W(-wu - e, u, -du - e), W(wu + e, u, -du - e), W(wu + e, u, du + e), W(-wu - e, u, du + e)]
            if side > 0 or True:
                rope_loop(props, pts, r * 0.9, seed=seed + 20 + k * 2 + side)
