"""MG01 hands & feet builders (pure data: core.Shell lists, joint chains, eye anchors).

Every builder returns dict(shells=[core.Shell...], joints={chain: [Vector...]}, eye=None | dict(pos, normal, up, size),
fixes=[str...]) - `fixes` lists the layout targets the builder had to correct (anatomy / physics), for the report.

Conventions (see README_AGENTS.md): Blender metres, +X screen right, -Y front, +Z up, ground z=0.
Shells are closed, manifold, outward; parts of one hand overlap on purpose (finger roots sunk into the palm,
palm sunk into the forearm) - the lead unions everything with an exact boolean.

Shared idea for all digits: a digit is a chain of joints solved in its own bending plane (e1 = pointing
direction, e2 = dorsal side). Flexion angles grow with one 'curl' value that is solved so the chain ends at the
target tip, then the chain is rotated/scaled in that plane to hit the tip exactly. Rings sit on every joint
(MCP, PIP, DIP / CMC, MCP, IP) so the future rig gets clean loops.
"""
import math, random
from mathutils import Vector as V, Matrix
from mathutils.geometry import intersect_point_line
import core

UP = V((0, 0, 1))
FRONT = V((0, -1, 0))

# ---------------------------------------------------------------- cross-section profiles (CCW in (u, v); v = dorsal)
BOX4 = [(1, -1), (1, 1), (-1, 1), (-1, -1)]                       # flat top / bottom / sides
OCT8 = [(1, -0.42), (1, 0.42), (0.72, 1), (-0.72, 1), (-1, 0.42), (-1, -0.42), (-0.72, -1), (0.72, -1)]
RND8 = [(math.cos(math.radians(-22.5 + 45 * k)), math.sin(math.radians(-22.5 + 45 * k))) for k in range(8)]
HEX6 = [(1, 0), (0.68, 1), (-0.68, 1), (-1, 0), (-0.68, -1), (0.68, -1)]  # flat top + flat bottom
RND6 = [(math.cos(math.radians(60 * k)), math.sin(math.radians(60 * k))) for k in range(6)]


def _blend(pa, pb, w):
    return [(a[0] + (b[0] - a[0]) * w, a[1] + (b[1] - a[1]) * w) for a, b in zip(pa, pb)]


# ---------------------------------------------------------------- small helpers
def _rng(seed, key):
    return random.Random(f'{key}:{seed}')  # str seeds are hashed with sha512 -> deterministic across runs


def _r2(r):
    return (float(r[0]), float(r[1])) if isinstance(r, (tuple, list)) else (float(r), float(r))


def _ortho(v, n, fallback=None):
    w = V(v) - n * V(v).dot(n)
    if w.length < 1e-6:
        if fallback is None:
            return None
        w = V(fallback) - n * V(fallback).dot(n)
    return w.normalized()


def _hor(v):
    return V((v.x, v.y, 0.0))


def _lerp(a, b, t):
    return a + (b - a) * t


def _rot(v, axis, deg):
    return Matrix.Rotation(math.radians(deg), 3, axis) @ v


def _mean(pts):
    return sum(pts, V()) / len(pts)


def _ring(c, u, v, prof, sx, sy, rnd=None, jit=0.0, cup=0.0, keep_top=False, off=None):
    """Ring points c + u*x*sx + v*y*sy. cup bends the edges toward -v (cupped palm)."""
    out = []
    for i, (px, py) in enumerate(prof):
        j = 1.0
        if rnd is not None and jit and not (keep_top and py > 0.95):
            j += rnd.uniform(-jit, jit)
        p = c + u * (px * sx * j) + v * (py * sy * j - cup * sy * px * px)
        if off is not None:
            p = p + off(px, py)
        out.append(p)
    return out


def _loft(sh, rings, cap0=True, cap1=True):
    ids = [[sh.v(p) for p in r] for r in rings]
    n = len(rings[0])
    for a, b in zip(ids, ids[1:]):
        for k in range(n):
            k2 = (k + 1) % n
            sh.f(a[k], a[k2], b[k2], b[k])
    if cap0:
        sh.f(*reversed(ids[0]))
    if cap1:
        sh.f(*ids[-1])
    return ids


def _orient(sh):
    """Make faces outward (positive signed volume)."""
    c = _mean(sh.verts)
    vol = 0.0
    for f in sh.faces:
        p0 = sh.verts[f[0]] - c
        for i in range(1, len(f) - 1):
            vol += p0.dot((sh.verts[f[i]] - c).cross(sh.verts[f[i + 1]] - c))
    if vol < 0:
        sh.faces = [tuple(reversed(f)) for f in sh.faces]
    return sh


def _frames(pts, side_ref, dorsal_ref):
    """Per point (t, u, v): t tangent (bisector at inner points), u side, v dorsal; u x v = t."""
    n = len(pts)
    out = []
    for i in range(n):
        if i == 0:
            t = pts[1] - pts[0]
        elif i == n - 1:
            t = pts[-1] - pts[-2]
        else:
            t = (pts[i] - pts[i - 1]).normalized() + (pts[i + 1] - pts[i]).normalized()
            if t.length < 1e-6:
                t = pts[i + 1] - pts[i]
        t = t.normalized()
        u = _ortho(side_ref, t, fallback=t.orthogonal())
        v = t.cross(u)
        out.append((t, u, v))
    # make v agree with dorsal_ref at the root
    if out and out[0][2].dot(dorsal_ref) < 0:
        out = [(t, -u, -v) for t, u, v in out]
    return out


def _raycast(sh, origin, d):
    """Closest hit of ray origin + s*d (s>0) with the shell. Returns (point, face_normal) or (None, None)."""
    best = None
    for f in sh.faces:
        p0 = sh.verts[f[0]]
        for i in range(1, len(f) - 1):
            p1, p2 = sh.verts[f[i]], sh.verts[f[i + 1]]
            e1, e2 = p1 - p0, p2 - p0
            h = d.cross(e2)
            a = e1.dot(h)
            if abs(a) < 1e-12:
                continue
            fi = 1.0 / a
            s = origin - p0
            uu = fi * s.dot(h)
            if uu < 0 or uu > 1:
                continue
            q = s.cross(e1)
            vv = fi * d.dot(q)
            if vv < 0 or uu + vv > 1:
                continue
            tt = fi * e2.dot(q)
            if tt > 1e-6 and (best is None or tt < best[0]):
                best = (tt, e1.cross(e2).normalized())
    if best is None:
        return None, None
    return origin + d * best[0], best[1]


# ---------------------------------------------------------------- digit chain solver
def _curl_chain(K, T, e1, e2, fracs, phi0, gains, kmax=160.0, k_fixed=None, min_chord=0.55):
    """Joint chain from K to T bending in plane (e1, e2); flexion bends toward -e2.
    fracs: phalanx length fractions; angles (deg, measured from e1 toward -e2):
    Phi_j = phi0 + sum_{i<=j} gains[i]*k. Solves k so the chain direction matches T-K, then rotates/scales
    the chain in its plane so it ends exactly at T. Returns list of points [K, J1, ..., T]."""
    d = T - K
    e1 = V(e1).normalized()
    lat = e1.cross(V(e2)).normalized()           # lateral axis (normal of the bending plane)
    e2 = lat.cross(e1).normalized()
    dl = d.dot(lat)
    dp = d - lat * dl                          # in-plane component
    tx, ty = dp.dot(e1), dp.dot(e2)
    tang = math.degrees(math.atan2(-ty, tx))   # target angle below e1

    def shape(k):
        x = y = 0.0
        ang = phi0
        for f, g in zip(fracs, gains):
            ang += g * k
            x += f * math.cos(math.radians(ang))
            y -= f * math.sin(math.radians(ang))
        return x, y

    def angle(k):
        x, y = shape(k)
        return math.degrees(math.atan2(-y, x))

    # never fold the chain into a loop: limit k so the chord stays >= min_chord of the chain length
    kcap = kmax
    for step in range(1, 81):
        kk = kmax * step / 80.0
        if math.hypot(*shape(kk)) < min_chord * sum(fracs):
            kcap = kmax * (step - 1) / 80.0
            break
    if k_fixed is not None:
        k = min(k_fixed, kcap)
    else:
        lo, hi = 0.0, kcap
        if angle(lo) >= tang:
            k = lo
        elif angle(hi) <= tang:
            k = hi
        else:
            for _ in range(40):
                mid = 0.5 * (lo + hi)
                if angle(mid) < tang:
                    lo = mid
                else:
                    hi = mid
            k = 0.5 * (lo + hi)
    sx, sy = shape(k)
    slen = math.hypot(sx, sy)
    tlen = math.hypot(tx, ty)
    scale = tlen / slen if slen > 1e-9 else 0.0
    rot = math.radians(tang - math.degrees(math.atan2(-sy, sx)))
    cr, sr = math.cos(rot), math.sin(rot)
    pts = [V(K)]
    x = y = 0.0
    ang = phi0
    acc = 0.0
    for f, g in zip(fracs, gains):
        ang += g * k
        x += f * math.cos(math.radians(ang))
        y -= f * math.sin(math.radians(ang))
        acc += f
        # rotate (x, y) by -rot in the 'below e1' sense: angle measured downward -> y -> -y
        xr = x * cr + y * sr
        yr = -x * sr + y * cr
        p = K + e1 * (xr * scale) + e2 * (yr * scale) + lat * (dl * acc / sum(fracs))
        pts.append(p)
    pts[-1] = V(T)
    return pts


def _digit(name, group, joints, widths, heights, side_ref, dorsal_ref, rnd, jit=0.05, base_sink=1.3,
           tip_chamfer=0.45, tip_scale=0.7, extra_mid=(0,), sides='box', mat='body', ground=None, pretip=True,
           root_dir=None):
    """Boxy digit through joints [J0(root), J1, ..., Jn(tip)]; widths/heights per joint (half sizes).
    Adds a sunk base ring before J0, optional mid rings in segments listed in extra_mid, and a chamfered
    blunt tip (flat cap). ground: if set, shift the distal part so the lowest vertex sits at z=ground."""
    J = [V(p) for p in joints]
    W_ = list(widths)
    H_ = list(heights)
    t0 = (J[1] - J[0]).normalized() if root_dir is None else V(root_dir).normalized()
    if base_sink > 0:
        pts = [J[0] - t0 * (base_sink * W_[0])]
        ws, hs = [W_[0] * 0.92], [H_[0] * 0.92]
        tags = ['base']
    else:
        pts, ws, hs, tags = [], [], [], []
    for i in range(len(J)):
        if i == len(J) - 1:
            tl = (J[i] - J[i - 1]).normalized()
            if pretip:
                pts.append(J[i] - tl * (tip_chamfer * W_[i]))
                ws.append(W_[i]); hs.append(H_[i]); tags.append('pretip')
            pts.append(J[i]); ws.append(W_[i] * tip_scale); hs.append(H_[i] * tip_scale); tags.append('tip')
        else:
            pts.append(J[i]); ws.append(W_[i]); hs.append(H_[i]); tags.append(f'j{i}')
            if i in extra_mid:
                pts.append(_lerp(J[i], J[i + 1], 0.5))
                ws.append((W_[i] + W_[i + 1]) * 0.5 * 0.94); hs.append((H_[i] + H_[i + 1]) * 0.5 * 0.94)
                tags.append('mid')
    fr = _frames(pts, side_ref, dorsal_ref)
    prof = BOX4 if sides == 'box' else (HEX6 if sides == 'hex' else BOX4)
    rings = []
    for p, (t, u, v), w, h in zip(pts, fr, ws, hs):
        rings.append(_ring(p, u, v, prof, w, h, rnd, jit))
    if ground is not None:
        n = len(rings)
        mz = min(q.z for q in rings[-1] + rings[-2])
        dz = ground - mz
        if dz > 0 or dz < -0.002:
            # weight: 0 up to the PIP ring, ramps to 1 on the tip rings
            nrm = max(n - 4, 1)
            for ri, r in enumerate(rings):
                wgt = 0.0 if ri < n - 4 else min(1.0, (ri - (n - 4) + 1) / 3.0)
                for k in range(len(r)):
                    r[k] = r[k] + V((0, 0, dz * wgt))
    sh = core.Shell(name, group, mat)
    _loft(sh, rings)
    _orient(sh)
    return sh


def _sunk(ch, depth):
    """Copy of a joint chain whose root is moved back (into the palm) by `depth` along the first segment."""
    out = [V(p) for p in ch]
    out[0] = ch[0] - (ch[1] - ch[0]).normalized() * depth
    return out


def _arm_dir_default(hid, fallback):
    """Forearm direction at the wrist from layout.LIMBS (same id), else `fallback`."""
    try:
        import layout as L
        lb = next(l for l in L.LIMBS if l['id'] == hid)
        d = V(lb['pts'][-1]) - V(lb['pts'][-2])
        if d.length > 1e-6:
            return d.normalized()
    except Exception:
        pass
    return V(fallback).normalized()


# ---------------------------------------------------------------- support hand
def support_hand(hid, wrist, knuckles, tips, thumb_index, arm_radius, seed=0, arm_dir=None, finger_radius=None):
    """Giant hand standing on the ground like a claw (canon S01-S03).
    wrist: forearm end; knuckles/tips: 5 targets each (layout fan); thumb_index: which one is the thumb.
    arm_radius: float or (rx, ry). arm_dir: optional forearm direction at the wrist (into the hand).
    Joints: 'wrist': [wrist, palm_centre], 'f<i>': [mcp, pip, dip, tip] (thumb: [cmc, mcp, ip, tip])."""
    rnd = _rng(seed, hid)
    fixes = []
    W = V(wrist)
    K = [V(k) for k in knuckles]
    T = [V(t) for t in tips]
    ti = int(thumb_index)
    F = [i for i in range(5) if i != ti]
    ra = max(_r2(arm_radius))
    Kc = _mean([K[i] for i in F])
    Tc = _mean([T[i] for i in F])
    fwd = _hor(Tc - W)
    if fwd.length < 0.3:
        fwd = _hor(Tc - Kc)
    fwd.normalize()
    # anatomy fix: the knuckle line must lie in front of the wrist (fingers fan forward from the wrist)
    reach = _hor(Kc - W).dot(fwd)
    min_reach = 0.9 * ra
    if reach < min_reach:
        sh_ = fwd * (min_reach - reach)
        K = [k + sh_ for k in K]
        Kc = Kc + sh_
        fixes.append(f'{hid}: knuckles moved {sh_.length:.2f} m forward (were behind/at the wrist)')
    # tips must be in front of their knuckles and on the ground
    for i in range(5):
        if _hor(T[i] - K[i]).dot(fwd) < 0.3 * ra and i != ti:
            T[i] = T[i] + fwd * (0.3 * ra - _hor(T[i] - K[i]).dot(fwd))
            fixes.append(f'{hid}: tip {i} pushed forward of its knuckle')
    s = UP.cross(fwd).normalized()               # side axis (screen right for a hand pointing at the viewer)
    if s.dot(K[F[-1]] - K[F[0]]) < 0:
        s = -s
    a = (Kc - W).normalized()                    # palm axis
    n = a.cross(s).normalized()                  # dorsal normal
    if n.z < 0:
        n = -n
    adir = V(arm_dir).normalized() if arm_dir is not None else _arm_dir_default(hid, a)
    # finger size from knuckle spacing
    spacing = sum((K[F[i + 1]] - K[F[i]]).length for i in range(3)) / 3.0
    rf = finger_radius or max(0.22, min(0.4, 0.34 * spacing))
    sig = [(K[i] - Kc).dot(s) for i in F]
    half_span = max(abs(min(sig)), abs(max(sig)))
    hwK, hhK = half_span + rf * 1.05, rf * 1.45

    # knuckle arc as a function of the side coordinate (piecewise linear between finger knuckles)
    order = sorted(F, key=lambda i: (K[i] - Kc).dot(s))
    ks = [((K[i] - Kc).dot(s), (K[i] - Kc).dot(a), (K[i] - Kc).dot(n)) for i in order]

    def arc(sv):
        if sv <= ks[0][0]:
            return ks[0][1], ks[0][2]
        if sv >= ks[-1][0]:
            return ks[-1][1], ks[-1][2]
        for (s0, a0, n0), (s1, a1, n1) in zip(ks, ks[1:]):
            if s0 <= sv <= s1:
                w = (sv - s0) / max(s1 - s0, 1e-9)
                return a0 + (a1 - a0) * w, n0 + (n1 - n0) * w
        return 0.0, 0.0

    # ---- palm: wrist ring (inside forearm) -> wrist -> mid -> knuckle arc -> front lip
    sh = core.Shell(hid + '_palm', hid, 'body')
    rings = []
    P0 = W - adir * (0.55 * ra)
    rings.append(_ring(P0, *_uv(adir, s, n), RND8, ra * 0.93, ra * 0.93, rnd, 0.03))
    t1 = (adir + a).normalized()
    rings.append(_ring(W, *_uv(t1, s, n), _blend(RND8, OCT8, 0.35), ra * 1.04, ra * 0.98, rnd, 0.04))
    Pm = _lerp(W, Kc, 0.5) + n * (0.1 * rf)
    hwm, hhm = ra + (hwK - ra) * 0.86, ra * 0.92 + (hhK - ra * 0.92) * 0.5
    rings.append(_ring(Pm, *_uv(a, s, n), _blend(RND8, OCT8, 0.85), hwm, hhm, rnd, 0.04, keep_top=True))
    u3, v3 = _uv(a, s, n)

    su = 1.0 if u3.dot(s) > 0 else -1.0

    def arc_off(scale_u):
        def f(px, py):
            ao, no = arc(px * scale_u * su)
            return a * ao + n * no
        return f
    rings.append(_ring(Kc, u3, v3, OCT8, hwK, hhK, rnd, 0.04, keep_top=True, off=arc_off(hwK)))
    lip = Kc + a * (0.75 * rf) - n * (0.25 * rf)
    rings.append(_ring(lip, u3, v3, OCT8, hwK - rf * 0.35, hhK * 0.62, rnd, 0.03, off=arc_off(hwK - rf * 0.35)))
    _loft(sh, rings)
    _orient(sh)
    shells = [sh]

    # ---- eye anchor on the flat dorsum, ~half way wrist -> knuckles
    ep = _lerp(W, Kc, 0.52)
    hit, hn = _raycast(sh, ep, n)
    eye = None
    if hit is not None:
        if hn.dot(n) < 0:
            hn = -hn
        upv = _ortho(W - hit, hn, fallback=-a)
        eye = dict(pos=hit, normal=hn, up=upv, size=0.4 * 2 * hwK)

    joints = {'wrist': [V(W), Pm.copy()]}
    # ---- fingers
    lengths_scale = {}
    for i in F:
        e1 = _hor(T[i] - K[i]).normalized()
        ch = _curl_chain(K[i], T[i], e1, UP, (0.45, 0.30, 0.25), -8.0, (0.25, 0.6, 0.6))
        jw = [rf * 1.08, rf * 1.04, rf * 0.98, rf * 0.92]
        jh = [w * 0.9 for w in jw]
        side_ref = UP.cross(e1)
        f_sh = _digit(f'{hid}_f{i}', hid, ch, jw, jh, side_ref, UP, rnd, jit=0.05, base_sink=1.25,
                      extra_mid=(0,), ground=0.012, root_dir=(2 * a + (ch[1] - ch[0]).normalized()).normalized())
        shells.append(f_sh)
        joints[f'f{i}'] = ch
    # ---- thumb: CMC on the palm side near the wrist, 2 phalanges, splayed, standing on its tip
    sg = 1.0 if (K[ti] - Kc).dot(s) > 0 else -1.0
    tc = 0.38
    cmc = _lerp(W, Kc, tc) + s * (sg * (ra + (hwK - ra) * tc) * 0.8) + n * (ra * 0.05)
    mcp = K[ti]
    # keep the thumb MCP clear of the neighbouring finger root (splay it sideways if needed)
    nb = min(F, key=lambda i: (K[i] - mcp).length)
    gap = (mcp - K[nb]).dot(s) * sg
    if gap < 4.0 * rf:
        mcp = mcp + s * (sg * (4.0 * rf - gap))
        fixes.append(f'{hid}: thumb knuckle splayed {4.0 * rf - gap:.2f} m away from the index root (they touched)')
    e1t = _hor(T[ti] - mcp)
    if e1t.length < 1e-3:
        e1t = fwd + s * sg
    e1t.normalize()
    chain = _curl_chain(mcp, T[ti], e1t, UP, (0.55, 0.45), -5.0, (0.45, 0.8))
    th_j = [cmc] + chain
    rt = rf * 1.12
    tw = [rt * 1.0, rt * 1.08, rt * 1.0, rt * 0.9]
    th = [w * 0.85 for w in tw]
    th_sh = _digit(f'{hid}_f{ti}', hid, th_j, tw, th, UP.cross(e1t), UP, rnd, jit=0.05, base_sink=0.6,
                   extra_mid=(), ground=0.012)
    shells.append(th_sh)
    joints[f'f{ti}'] = th_j
    return dict(shells=shells, joints=joints, eye=eye, fixes=fixes, finger_radius=rf)


def _uv(t, side, dorsal=None):
    """(u, v) for a ring with tangent t, u ~ side, v = t x u (dorsal); flipped so v agrees with `dorsal`."""
    t = t.normalized()
    u = _ortho(side, t, fallback=t.orthogonal())
    v = t.cross(u)
    if dorsal is not None and v.dot(dorsal) < 0:
        u, v = -u, -v
    return u, v


# ---------------------------------------------------------------- body proxy (for cradle hands)
def _default_body():
    """Tapered capsules of the parts the cradle hands hold: pelvis, hub and the four thighs (from layout)."""
    import layout as L

    def rr(x):
        return (x[0] + x[1]) * 0.5 if isinstance(x, (tuple, list)) else float(x)
    caps = []
    T = L.TORSO
    caps.append((V(T['pts'][0]), V(T['pts'][1]), rr(T['radii'][0]), rr(T['radii'][1])))
    H = L.HUB
    for i in range(len(H['pts']) - 1):
        caps.append((V(H['pts'][i]), V(H['pts'][i + 1]), rr(H['radii'][i]), rr(H['radii'][i + 1])))
    for lb in L.LIMBS:
        if lb['kind'] != 'leg':
            continue
        kn = lb['joints'].get('knee', 2)
        for i in range(kn):
            caps.append((V(lb['pts'][i]), V(lb['pts'][i + 1]), rr(lb['radii'][i]), rr(lb['radii'][i + 1])))
    return caps


def _body_sdf(caps):
    def sdf(p):
        best, bg = 1e9, V((0, 0, 1))
        for a, b, r0, r1 in caps:
            q, t = intersect_point_line(p, a, b)
            t = max(0.0, min(1.0, t))
            q = a + (b - a) * t
            dvec = p - q
            d = dvec.length - (r0 + (r1 - r0) * t)
            if d < best:
                best = d
                bg = dvec.normalized() if dvec.length > 1e-9 else V((0, 0, 1))
        return best, bg
    return sdf


# ---------------------------------------------------------------- cradle hand
def _find_contact(caps, sdf, P, W, Tc, maxlen):
    """Surface point of the body where a cradle palm should rest: near the layout palm / finger tips,
    on an underside (outward normal pointing down), reachable from the wrist, palm axis across the part."""
    best = None
    for a_, b_, r0, r1 in caps:
        ax = b_ - a_
        if ax.length < 1e-6:
            continue
        ad = ax.normalized()
        e1 = ad.orthogonal().normalized()
        e2 = ad.cross(e1)
        for ti in range(9):
            t = ti / 8.0
            c = a_ + ax * t
            r = r0 + (r1 - r0) * t
            for k in range(20):
                ang = 2 * math.pi * k / 20
                g = e1 * math.cos(ang) + e2 * math.sin(ang)
                q = c + g * r
                if sdf(q)[0] < -0.02:              # inside another part
                    continue
                cost = (q - P).length + 1.0 * (q - Tc).length
                cost += 2.5 * max(0.0, g.z + 0.25)  # prefer undersides (palm facing up into the body)
                over = (q - W).length - maxlen
                if over > 0:
                    cost += 2.0 * over
                pa = _ortho(q - W, g)
                if pa is not None:
                    cost += 1.2 * abs(pa.dot(ad))   # palm axis across the held part -> fingers wrap it
                if best is None or cost < best[0]:
                    best = (cost, q, g, (a_, b_, r0, r1), t)
    return best[1:]


def cradle_hand(hid, wrist, palm, knuckles, tips, thumb, arm_radius, seed=0, body=None, arm_dir=None):
    """Huge cupped palm holding a thigh/pelvis from below (canon C01/C02).
    body = list of capsules (a, b, r0, r1) (default: pelvis + hub + thighs from layout). The builder finds the
    held part (near the layout palm/tips, an underside reachable from the wrist), lays the palm under it and
    wraps the 4 fingers (3 phalanges) around it in parallel planes; the thumb (2 phalanges) wraps the other way
    from the wrist side, opposing the fingers. Layout knuckles give hand width and finger order, layout tips the
    finger length. Joints: 'wrist': [wrist, palm_centre], 'f0'..'f3': [mcp, pip, dip, tip],
    'thumb': [cmc, mcp, ip, tip]."""
    rnd = _rng(seed, hid)
    fixes = []
    W = V(wrist)
    P0 = V(palm)
    K0 = [V(k) for k in knuckles]
    T0 = [V(t) for t in tips]
    TH = [V(p) for p in thumb]
    ra = max(_r2(arm_radius))
    caps = body if body is not None else _default_body()
    sdf = _body_sdf(caps)
    rf = max(0.16, min(0.3, 0.3 * ra))
    hp = 0.4 * ra                                # palm half-thickness
    maxlen = 3.6 * ra
    d0, _ = sdf(P0)
    Q, g, cap, tq = _find_contact(caps, sdf, P0, W, _mean(T0), maxlen)
    ca, cb, r0, r1 = cap
    ad = (cb - ca).normalized()
    Rq = r0 + (r1 - r0) * tq
    Cq = ca + (cb - ca) * tq
    rho0 = _ortho(Q - Cq, ad, fallback=g)
    tau = ad.cross(rho0).normalized()
    if tau.dot(Q - W) < 0:
        tau = -tau
    s = ad if ad.dot(K0[-1] - K0[0]) >= 0 else -ad   # knuckle line runs along the held part
    P = Cq + rho0 * (Rq + 0.85 * hp)
    if (P - W).length > maxlen * 1.15:
        fixes.append(f'{hid}: held part is {(P - W).length:.1f} m from the wrist (> {maxlen:.1f}); palm stretched')
    fixes.append(f'{hid}: palm moved {(P - P0).length:.2f} m to cup the body from below (layout palm floated '
                 f'{d0:.2f} m off it); fingers re-wrapped around the held part (layout tips used for length only)')
    if (TH[1] - TH[0]).dot(tau) < 0:
        fixes.append(f'{hid}: layout thumb pointed back toward the elbow -> thumb opposes the fingers instead')

    def on_cyl(x, theta, rad):
        c = Cq + ad * x
        return c + (rho0 * math.cos(math.radians(theta)) + tau * math.sin(math.radians(theta))) * rad

    sp = max(2.5 * rf, (K0[-1] - K0[0]).length / 3.0 * 0.9)   # finger spacing along the held part
    order = sorted(range(4), key=lambda i: (K0[i] - _mean(K0)).dot(s))
    xs = {}
    for r_i, i in enumerate(order):
        xs[i] = (r_i - 1.5) * sp * (1.0 if s.dot(ad) > 0 else -1.0)
    Lf0 = max(sum((t - k).length for k, t in zip(K0, T0)) / 4.0 * 1.15, 0.62 * (P - W).length)
    rank_len = (0.9, 1.0, 0.96, 0.82)
    th_k = 38.0
    Rk = Rq + max(hp, 1.15 * rf)
    # slide the finger block along the held part until no knuckle sits inside another body part
    x0 = 0.0
    for _ in range(16):
        bad = [xs[i] for i in range(4) if sdf(on_cyl(x0 + xs[i], th_k, Rk))[0] < 0.9 * rf]
        if not bad:
            break
        x0 += (-0.2 if sum(bad) > 0 else 0.2) * sp
    if abs(x0) > 1e-6:
        fixes.append(f'{hid}: finger block slid {x0:+.2f} m along the held part (knuckles were inside the body)')
    for i in xs:
        xs[i] += x0
    P = P + ad * x0
    Cq = Cq + ad * x0
    for i in xs:
        xs[i] -= x0
    K = {}
    joints = {}
    shells = []
    for r_i, i in enumerate(order):
        Lf = Lf0 * rank_len[r_i]
        dth = math.degrees(Lf / (Rq + rf))
        dth = max(70.0, min(185.0, dth))
        J = [on_cyl(xs[i], th_k, Rk)]
        for fr, rr_ in ((0.45, 0.98), (0.76, 0.92), (1.0, 0.86)):
            J.append(on_cyl(xs[i], th_k + dth * fr, Rq + rr_ * rf))
        # do not cut through other body parts: push joints radially out of the held axis
        for j in range(1, 4):
            moved = 0.0
            for _ in range(8):
                dj, _g = sdf(J[j])
                if dj >= 0.75 * rf or moved > 1.6 * rf:
                    break
                c = Cq + ad * xs[i]
                step = min(0.75 * rf - dj + 0.02, 1.6 * rf - moved + 0.01)
                J[j] = J[j] + _ortho(J[j] - c, ad, fallback=rho0) * step
                moved += step
        K[i] = J[0]
        joints[f'f{i}'] = J
    Kc = _mean([K[i] for i in range(4)])
    half_span = 1.5 * sp
    hwK, hhK = half_span + rf * 1.3, rf * 1.3
    a = (Kc - P).normalized()
    nrm = _ortho(rho0 * math.cos(math.radians(th_k * 0.5)) + tau * math.sin(math.radians(th_k * 0.5)), s, fallback=rho0)
    adir = V(arm_dir).normalized() if arm_dir is not None else _arm_dir_default(hid, P - W)
    # ---- palm: forearm -> wrist -> heel -> contact (under the held part) -> knuckle arc -> lip
    sh = core.Shell(hid + '_palm', hid, 'body')
    hwP = max(hwK, ra * 1.1)
    ax_wp = (P - W).normalized()
    heel = _lerp(W, P, 0.5) - rho0 * 0.0
    rings = []
    rings.append(_ring(W - adir * (0.5 * ra), *_uv(adir, s, rho0), RND8, ra * 0.92, ra * 0.92, rnd, 0.03))
    rings.append(_ring(W, *_uv((adir + ax_wp).normalized(), s, rho0), _blend(RND8, OCT8, 0.4), ra * 1.04, ra * 0.95, rnd, 0.04))
    rings.append(_ring(heel, *_uv(ax_wp, s, rho0), OCT8, ra + (hwP - ra) * 0.6, ra * 0.75, rnd, 0.04, cup=0.15))
    rings.append(_ring(P, *_uv(tau, s, rho0), OCT8, hwP, hp, rnd, 0.04, cup=0.25))
    Kring = Cq + (rho0 * math.cos(math.radians(th_k)) + tau * math.sin(math.radians(th_k))) * Rk
    tk = (-rho0 * math.sin(math.radians(th_k)) + tau * math.cos(math.radians(th_k))).normalized()
    rk = _ortho(Kring - Cq, ad, fallback=rho0)
    rings.append(_ring(Kring, *_uv(tk, s, rk), OCT8, hwK, hhK, rnd, 0.04, cup=0.1))
    th_l = th_k + 14.0
    Lring = Cq + (rho0 * math.cos(math.radians(th_l)) + tau * math.sin(math.radians(th_l))) * (Rq + 0.9 * rf)
    tl = (-rho0 * math.sin(math.radians(th_l)) + tau * math.cos(math.radians(th_l))).normalized()
    rl = _ortho(Lring - Cq, ad, fallback=rho0)
    rings.append(_ring(Lring, *_uv(tl, s, rl), OCT8, hwK - 0.3 * rf, hhK * 0.62, rnd, 0.03))
    _loft(sh, rings)
    _orient(sh)
    shells.append(sh)
    # ---- fingers (root sunk back into the palm along the wrap)
    for i in range(4):
        J = joints[f'f{i}']
        jw = [rf * 1.06, rf * 1.0, rf * 0.95, rf * 0.88]
        jh = [w * 0.85 for w in jw]
        shells.append(_digit(f'{hid}_f{i}', hid, J, jw, jh, s, _ortho(J[0] - Cq, ad, fallback=rho0), rnd, jit=0.05,
                             base_sink=1.6, extra_mid=(), root_dir=tk))
    # ---- thumb: from the wrist side of the contact, wraps the other way round (opposes the fingers)
    sg = 1.0 if (TH[0] - _mean(K0)).dot(s) > 0 else -1.0
    xt = sg * (1.5 * sp + 0.35 * sp)
    rt = rf * 1.15
    Lt = max(0.72 * Lf0, (TH[1] - TH[0]).length)
    cmc = _lerp(W, P, 0.62) + s * (sg * hwP * 0.55) + rho0 * (0.1 * hp)
    mcp = on_cyl(xt, -40.0, Rq + max(hp, 1.2 * rt))
    dth = max(50.0, min(120.0, math.degrees(Lt / (Rq + rt))))
    ip = on_cyl(xt, -40.0 - dth * 0.55, Rq + 0.98 * rt)
    tip = on_cyl(xt, -40.0 - dth, Rq + 0.85 * rt)
    TJ = [cmc, mcp, ip, tip]
    for j in (1, 2, 3):
        moved = 0.0
        for _ in range(8):
            dj, _g = sdf(TJ[j])
            if dj >= 0.75 * rt or moved > 1.6 * rt:
                break
            c = Cq + ad * xt
            step = min(0.75 * rt - dj + 0.02, 1.6 * rt - moved + 0.01)
            TJ[j] = TJ[j] + _ortho(TJ[j] - c, ad, fallback=rho0) * step
            moved += step
    tw = [rt * 1.35, rt * 1.12, rt * 1.0, rt * 0.9]
    shells.append(_digit(f'{hid}_thumb', hid, TJ, tw, [w * 0.85 for w in tw], s,
                         _ortho(TJ[1] - (Cq + ad * xt), ad, fallback=rho0), rnd, jit=0.05, base_sink=0.5, extra_mid=()))
    joints['thumb'] = TJ
    joints['wrist'] = [V(W), P.copy()]
    held = dict(centre=Cq, axis=ad, radius=Rq, contact=Q)
    return dict(shells=shells, joints=joints, eye=None, fixes=fixes, finger_radius=rf, held=held)


# ---------------------------------------------------------------- small hands
def _small_frame(W, P, T, normal=None):
    """Palm axis a (wrist -> fingers), side s, palm-side normal N (for open hands) for the small hands."""
    Tc = _mean(T)
    if normal is not None:
        N = V(normal).normalized()
        a = _ortho(Tc - W, N, fallback=P - W)
    else:
        N = None
        a = (Tc - W).normalized()
    s = _ortho(T[-1] - T[0], a, fallback=a.orthogonal())
    if N is not None:
        s = _ortho(s, N, fallback=a.cross(N))
    return a, s, N


def open_hand(hid, wrist, palm, normal, tips, thumb, arm_radius, seed=0, arm_dir=None):
    """Small raised hand, palm facing `normal` (canon A01/A02), fingers splayed and slightly bent toward
    the palm side, painted eye in the palm. Joints: 'wrist': [wrist, palm], 'f0'..'f3': [mcp, pip, dip, tip],
    'thumb': [cmc, mcp, ip, tip]."""
    rnd = _rng(seed, hid)
    fixes = []
    W, P = V(wrist), V(palm)
    T = [V(t) for t in tips]
    TT = V(thumb)
    ra = max(_r2(arm_radius))
    a, s, N = _small_frame(W, P, T, normal)
    L = (_mean(T) - W).dot(a)
    # palm plane: through the palm point; the eye/palm face looks along N
    Kc = W + a * (0.6 * L) + N * ((P - W).dot(N))
    Pc = P
    if (P - W).dot(a) > 0.5 * L:
        Pc = W + a * (0.42 * L) + N * ((P - W).dot(N))
        fixes.append(f'{hid}: palm centre pulled toward the wrist (was at the knuckles)')
    hw = max(2.2 * ra, 0.3 * L)                 # palm half-width
    hh = max(0.55 * ra, 0.085 * L)              # palm half-thickness
    order = sorted(range(4), key=lambda i: (T[i] - Kc).dot(s))
    sp = 0.78 * hw
    sig = {}
    for r_i, i in enumerate(order):
        sig[i] = -sp + 2 * sp * r_i / 3.0
    K = {}
    for i in range(4):
        x = sig[i] / sp
        K[i] = Kc + s * sig[i] - a * (0.12 * hw * x * x) + N * (0.15 * hh)
    rf = max(0.05, min(0.16, 0.36 * (2 * sp / 3.0)))
    adir = V(arm_dir).normalized() if arm_dir is not None else _arm_dir_default(hid, P - W)
    sh = core.Shell(hid + '_palm', hid, 'body')
    rings = [
        _ring(W - adir * (0.6 * ra), *_uv(adir, s, N), RND6, ra * 0.93, ra * 0.93, rnd, 0.03),
        _ring(W, *_uv((adir + a).normalized(), s, N), _blend(RND6, HEX6, 0.3), ra * 1.06, ra * 0.95, rnd, 0.04),
        _ring(Pc, *_uv(a, s, N), HEX6, hw, hh, rnd, 0.04, keep_top=True),
        _ring(Kc + a * (0.25 * rf), *_uv(a, s, N), HEX6, hw * 0.92, hh * 0.85, rnd, 0.04, keep_top=True,
              off=lambda px, py: -a * (0.12 * hw * px * px)),
    ]
    _loft(sh, rings)
    _orient(sh)
    shells = [sh]
    hit, hn = _raycast(sh, Pc + a * (0.05 * L), N)
    eye = None
    if hit is not None:
        if hn.dot(N) < 0:
            hn = -hn
        eye = dict(pos=hit, normal=hn, up=_ortho(a, hn, fallback=a), size=0.55 * 2 * hw)
    joints = {'wrist': [V(W), Pc.copy()]}
    for i in range(4):
        tgt = T[i] + N * (0.16 * (T[i] - K[i]).length)       # fingertips curl a little toward the viewer
        e1 = _ortho(T[i] - K[i], N, fallback=a)
        ch = _curl_chain(K[i], tgt, e1, -N, (0.44, 0.31, 0.25), 0.0, (0.3, 0.6, 0.6))
        jw = [rf * 1.05, rf * 1.0, rf * 0.93, rf * 0.85]
        jh = [w * 0.85 for w in jw]
        shells.append(_digit(f'{hid}_f{i}', hid, _sunk(ch, 0.9 * rf), jw, jh, e1.cross(-N), -N, rnd, jit=0.05, base_sink=0.0,
                             extra_mid=(), tip_chamfer=0.0, tip_scale=0.78, pretip=False))
        joints[f'f{i}'] = ch
    # thumb: CMC on the palm side near the wrist, 2 phalanges
    sg = 1.0 if (TT - Pc).dot(s) > 0 else -1.0
    cmc = _lerp(W, Pc, 0.55) + s * (sg * hw * 0.45) + N * (0.2 * hh)
    tt = TT + N * (0.12 * (TT - cmc).length)
    e1t = _ortho(tt - cmc, N, fallback=s * sg)
    ch = _curl_chain(cmc, tt, e1t, -N, (0.4, 0.33, 0.27), 0.0, (0.25, 0.5, 0.5))
    rt = rf * 1.12
    tw = [rt * 1.3, rt * 1.08, rt * 0.98, rt * 0.86]
    shells.append(_digit(f'{hid}_thumb', hid, ch, tw, [w * 0.85 for w in tw], e1t.cross(-N), -N, rnd, jit=0.05,
                         base_sink=0.6, extra_mid=(), tip_chamfer=0.0, tip_scale=0.78, pretip=False))
    joints['thumb'] = ch
    return dict(shells=shells, joints=joints, eye=eye, fixes=fixes, finger_radius=rf)


def claw_hand(hid, wrist, palm, tips, thumb, arm_radius, seed=0, arm_dir=None):
    """Thin small hand with fingers curled like claws (canon A03/A04).
    Joints: 'wrist': [wrist, palm], 'f0'..'f3': [mcp, pip, dip, tip], 'thumb': [cmc, mcp, ip, tip]."""
    rnd = _rng(seed, hid)
    fixes = []
    W, P = V(wrist), V(palm)
    T = [V(t) for t in tips]
    TT = V(thumb)
    ra = max(_r2(arm_radius))
    a = (P - W).normalized()
    lp = (P - W).length
    if lp < 1.6 * ra:
        P = W + a * (1.6 * ra)
        fixes.append(f'{hid}: palm centre pushed out of the wrist ({lp:.2f} m -> {1.6 * ra:.2f} m)')
        lp = 1.6 * ra
    Kc = P + a * (0.65 * lp)
    Tc = _mean(T)
    pal = _ortho(Tc - Kc, a, fallback=a.orthogonal())       # palm side = where the claws curl to
    D = -pal                                                 # dorsal
    s = _ortho(T[-1] - T[0], a, fallback=a.cross(D))
    s = _ortho(s, D, fallback=a.cross(D))
    hw = max(1.7 * ra, 0.22 * (T[-1] - T[0]).length + 0.6 * ra)
    hh = 0.62 * ra
    order = sorted(range(4), key=lambda i: (T[i] - Kc).dot(s))
    sp = 0.75 * hw
    K = {}
    for r_i, i in enumerate(order):
        x = -1 + 2 * r_i / 3.0
        K[i] = Kc + s * (x * sp) - a * (0.15 * hw * x * x)
    rf = max(0.035, min(0.1, 0.34 * (2 * sp / 3.0)))
    adir = V(arm_dir).normalized() if arm_dir is not None else _arm_dir_default(hid, a)
    sh = core.Shell(hid + '_palm', hid, 'body')
    rings = [
        _ring(W - adir * (0.6 * ra), *_uv(adir, s, D), BOX4, ra * 0.68, ra * 0.68, rnd, 0.03),
        _ring(P, *_uv(a, s, D), BOX4, hw * 0.85, hh, rnd, 0.05),
        _ring(Kc + a * (0.3 * rf), *_uv(a, s, D), BOX4, hw * 0.8, hh * 0.8, rnd, 0.05),
    ]
    _loft(sh, rings)
    _orient(sh)
    shells = [sh]
    joints = {'wrist': [V(W), P.copy()]}
    for i in range(4):
        ch = _curl_chain(K[i], T[i], a, D, (0.4, 0.33, 0.27), 12.0, (0.5, 0.8, 0.8), kmax=150)
        jw = [rf * 1.05, rf * 1.0, rf * 0.9, rf * 0.75]
        shells.append(_digit(f'{hid}_f{i}', hid, _sunk(ch, 0.9 * rf), jw, [w * 0.9 for w in jw], a.cross(D), D, rnd,
                             jit=0.05, base_sink=0.0, extra_mid=(), tip_chamfer=0.0, tip_scale=0.6, pretip=False))
        joints[f'f{i}'] = ch
    sg = 1.0 if (TT - P).dot(s) > 0 else -1.0
    cmc = _lerp(W, P, 0.6) + s * (sg * hw * 0.5)
    e1t = (TT - cmc).normalized()
    e2t = _ortho(D, e1t, fallback=e1t.orthogonal())
    ch = _curl_chain(cmc, TT, e1t, e2t, (0.4, 0.33, 0.27), 8.0, (0.4, 0.7, 0.7), kmax=120)
    rt = rf * 1.1
    tw = [rt * 1.2, rt * 1.05, rt * 0.9, rt * 0.72]
    shells.append(_digit(f'{hid}_thumb', hid, ch, tw, [w * 0.9 for w in tw], e1t.cross(e2t), e2t, rnd, jit=0.05,
                         base_sink=0.0, extra_mid=(), tip_chamfer=0.0, tip_scale=0.6, pretip=False))
    joints['thumb'] = ch
    return dict(shells=shells, joints=joints, eye=None, fixes=fixes, finger_radius=rf)


# ---------------------------------------------------------------- foot
def foot(lid, ankle, toe, sole_normal, ankle_radius, seed=0, shin_dir=None, big_toe_side=None):
    """Simple human foot: flat sole, heel bump behind the ankle, arch, wide ball, a separate protruding big toe
    and one block for the small toes. toe = tip of the toes; sole_normal = outward normal of the sole
    (re-orthogonalised to the foot axis). big_toe_side: optional vector toward the medial side
    (default: toward the body's vertical axis x=y=0). Joints: 'foot': [ankle, ball, toe_tip],
    'toe': [big_toe_base, big_toe_tip]."""
    rnd = _rng(seed, lid)
    fixes = []
    A, Tp = V(ankle), V(toe)
    r = max(_r2(ankle_radius))
    f = (Tp - A)
    D = f.length
    f.normalize()
    sn = V(sole_normal).normalized()
    if abs(sn.dot(f)) > 0.35:
        fixes.append(f'{lid}: sole_normal was {90 - math.degrees(math.acos(min(1.0, abs(sn.dot(f))))):.0f} deg '
                     f'off perpendicular to the foot axis -> made perpendicular (sole faces back/down)')
    sn = _ortho(sn, f, fallback=f.orthogonal())
    w = f.cross(sn).normalized()                 # width axis
    up = -sn                                     # instep side
    if big_toe_side is not None:
        med = 1.0 if V(big_toe_side).dot(w) > 0 else -1.0
    else:
        med = 1.0 if (-_hor(A)).dot(w) > 0 else -1.0
    if D < 4.4 * r:
        fixes.append(f'{lid}: foot lengthened {D:.2f} -> {4.4 * r:.2f} m (ankle->toe was shorter than a human foot)')
        D = 4.4 * r
    ha = 1.1 * r                                 # ankle centre height above the sole

    def P(x, y, z=0.0):
        return A + f * x + up * y + w * z
    # (x, y_bottom, y_top, half width, lateral shift)
    spec = [
        (-0.95 * r, -ha + 0.1 * r, -ha + 0.95 * r, 0.62 * r, 0.0),    # heel back (rounded)
        (-0.35 * r, -ha, 1.0 * r, 0.82 * r, 0.0),                      # heel / ankle (covers the shin end)
        (0.42 * D, -ha + 0.1 * r, -ha + 1.1 * r, 1.1 * r, 0.05 * r),   # instep, arch slightly raised
        (0.70 * D, -ha, -ha + 0.66 * r, 1.42 * r, 0.1 * r),            # ball (widest)
    ]
    sh = core.Shell(lid + '_foot', lid, 'body')
    rings = []
    for x, yb, yt, hw, lat in spec:
        c = P(x, (yb + yt) * 0.5, lat * med)
        u, v = _uv(f, w, up)
        rings.append(_ring(c, u, v, HEX6, hw, (yt - yb) * 0.5, rnd, 0.04))
    _loft(sh, rings)
    _orient(sh)
    shells = [sh]
    # big toe (medial) and small-toes block (lateral), separated by a gap
    b = [P(0.6 * D, -ha + 0.3 * r, med * 0.8 * r), P(0.86 * D, -ha + 0.28 * r, med * 0.84 * r),
         P(1.02 * D, -ha + 0.26 * r, med * 0.86 * r)]
    tw = [0.4 * r, 0.38 * r, 0.33 * r]
    shells.append(_digit(lid + '_bigtoe', lid, b, tw, [x * 0.82 for x in tw], w, up, rnd, jit=0.04,
                         base_sink=0.0, extra_mid=(), tip_chamfer=0.0, tip_scale=0.82, pretip=False))
    c_ = [P(0.6 * D, -ha + 0.26 * r, -med * 0.42 * r), P(0.8 * D, -ha + 0.24 * r, -med * 0.48 * r),
          P(0.93 * D, -ha + 0.2 * r, -med * 0.52 * r)]
    cw = [0.56 * r, 0.5 * r, 0.42 * r]
    shells.append(_digit(lid + '_toes', lid, c_, cw, [x * 0.5 for x in cw], w, up, rnd, jit=0.04,
                         base_sink=0.0, extra_mid=(), tip_chamfer=0.0, tip_scale=0.85, pretip=False))
    ball = P(0.70 * D, -ha + 0.34 * r, 0.1 * r * med)
    joints = {'foot': [V(A), ball, P(1.0 * D, -ha + 0.25 * r)], 'toe': [b[0], b[2]]}
    return dict(shells=shells, joints=joints, eye=None, fixes=fixes)
