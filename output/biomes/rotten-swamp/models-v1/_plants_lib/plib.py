"""Shared helpers for the 'Plants' group (SW01-SW06): bent single-blade strips, sedge tufts, reed stalks, cattail
cobs, mud mounds, lily pads/petals, mushrooms. Built on the kit (sk.Mesh)."""
import sys, math, random, os
sys.path.insert(0, '/home/claude/swamp/kit')
import sk
from mathutils import Vector, Matrix

V = Vector
UP = V((0, 0, 1))

def hdir(az):
    return V((math.cos(az), math.sin(az), 0))

# ------------------------------------------------------------------ curves
def arc_path(base, az, theta0, theta1, length, segs=4, p=1.6, wobble=0.0, rnd=None):
    """Curve from base: direction tilted from vertical by theta (rad) toward azimuth az; theta goes theta0->theta1
    with exponent p along the length (theta1 > 90deg = drooping tip). Returns list of Vectors (segs+1)."""
    pts = [V(base)]
    dl = length / segs
    for i in range(segs):
        t = (i + 0.5) / segs
        th = theta0 + (theta1 - theta0) * t ** p
        a = az + (rnd.uniform(-wobble, wobble) if rnd else 0)
        d = V((math.sin(th) * math.cos(a), math.sin(th) * math.sin(a), math.cos(th)))
        pts.append(pts[-1] + d * dl)
    return pts

# ------------------------------------------------------------------ blade strip (single alpha blade, V-folded)
def blade(pl, pts, width, region, fold=0.25, twist=0.0, sway=None, flutter=0.02, taper=0.0, side_hint=None, roll=0.0):
    """Strip along pts, width (full) constant (alpha does the taper), V-folded along the midrib by fold*width
    (gives the faceted light/dark halves and keeps the blade readable edge-on). UV: u across region, v base->tip.
    Wind: sway grows with t^2 (default 0.07 * length), flutter with t."""
    u0, v0, u1, v1 = pl.region(region, pl.mat)
    n = len(pts)
    L = [0.0]
    for i in range(1, n):
        L.append(L[-1] + (pts[i] - pts[i - 1]).length)
    tot = max(L[-1], 1e-6)
    if sway is None:
        sway = 0.07 * tot
    # width direction: perpendicular to the bend plane (blade faces the direction it arches to)
    d0 = (pts[-1] - pts[0])
    h = V((d0.x, d0.y, 0))
    if side_hint is not None:
        side0 = V(side_hint).normalized()
    elif h.length < 1e-4:
        side0 = V((1, 0, 0))
    else:
        side0 = UP.cross(h).normalized()
    if roll:
        side0 = Matrix.Rotation(roll, 3, h.normalized() if h.length > 1e-4 else UP) @ side0
    rows = []
    for i in range(n):
        t = L[i] / tot
        tg = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        s = (side0 - tg * side0.dot(tg)).normalized()
        if twist:
            s = (Matrix.Rotation(twist * t, 3, tg) @ s)
        nrm = s.cross(tg).normalized()
        w = width * 0.5 * (1 - taper * t)
        mid = pts[i] + nrm * (fold * width * 0.5)
        rows.append((pts[i] - s * w, mid, pts[i] + s * w, t))
    for i in range(n - 1):
        a0, m0, b0, t0 = rows[i]
        a1, m1, b1, t1 = rows[i + 1]
        va, vb = v0 + (v1 - v0) * t0, v0 + (v1 - v0) * t1
        um = (u0 + u1) / 2
        w0 = (sway * t0 * t0, flutter * t0); w1 = (sway * t1 * t1, flutter * t1)
        pl.quad(a0, m0, m1, a1, (u0, va), (um, va), (um, vb), (u0, vb), [w0, w0, w1, w1])
        pl.quad(m0, b0, b1, m1, (um, va), (u1, va), (u1, vb), (um, vb), [w0, w0, w1, w1])

def sedge_tuft(pl, base, height, n=10, rnd=None, spread=1.0, lean_az=None, lean=0.0, width=0.06, regions=None,
               fans=2, fan_regions=('sedge_0', 'sedge_1', 'sedge_2'), droop=1.0, rot=0.0, fan_scale=0.9, segs=4,
               radius=0.03, tall_center=True, min_tilt=0.12, lean_len=0.3):
    """Layered sedge tuft: n bent single-blade strips (radiating, outer ones drooping) + `fans` crossing fan cards
    in the core for fullness. lean_az/lean: one-sided lean of the whole tuft."""
    rnd = rnd or random.Random(0)
    B = V(base)
    regions = regions or ['sedge_blade_0', 'sedge_blade_1', 'sedge_blade_2', 'sedge_blade_3']
    for i in range(n):
        az = rot + 2 * math.pi * i / n + rnd.uniform(-.35, .35)
        outer = rnd.random()
        th0 = (min_tilt + 0.55 * outer * spread)
        th1 = th0 + (0.25 + 1.0 * outer * droop) * rnd.uniform(.6, 1.2)
        L = height * (1.05 - 0.35 * outer if tall_center else rnd.uniform(.6, 1.05)) * rnd.uniform(.85, 1.15)
        if lean:
            # bend the tuft towards lean_az: blades on that side droop more, others lean over
            da = math.atan2(math.sin(lean_az - az), math.cos(lean_az - az))
            az = az + da * lean * 0.6
            th0 += lean * 0.35 * math.cos(da) * 0.5
            th1 += lean * 0.6
            L *= 1 + lean_len * lean * max(0, math.cos(da))
        p0 = B + hdir(az) * radius * rnd.uniform(.2, 1)
        # arc_path length is along the curve; height scale corrects for droop
        pts = arc_path(p0, az, th0, th1, L * (1 + 0.35 * outer * spread), segs=segs, p=1.5)
        blade(pl, pts, width * rnd.uniform(.85, 1.2), rnd.choice(regions), fold=rnd.uniform(.12, .25),
              twist=rnd.uniform(-.3, .3), roll=rnd.uniform(-.7, .7))
    for k in range(fans):
        a = rot + math.pi * k / max(fans, 1) + rnd.uniform(-.3, .3)
        reg = rnd.choice(fan_regions)
        rw, rh = sk.REG['T_Swamp_Plants.png']['regions'][reg][2:]
        R = hdir(a) * height * fan_scale * 0.5 * rw / rh
        la = (lean_az if lean else a + math.pi / 2)
        U = (V((0, 0, 1)) + hdir(la) * (lean * 0.5 if lean else 0)).normalized() * height * fan_scale
        pl.card(B, R, U, reg, bend=height * rnd.uniform(.05, .15), segs=2,
                wind_sway=0.05 * height, wind_flutter=0.02)

# ------------------------------------------------------------------ prisms with atlas UVs
def prism(m, pts, radii, region, sides=4, rot=0.0, caps=(False, True), wind=None, u_along=False, jitter=0.0,
          rnd=None, cap_center=None, up_hint=None):
    """Polygonal prism along pts with region-mapped UVs (v along the length, u around; or u along if u_along).
    wind: list of (sway, flutter) per point. cap_center: extra apex for a pointed end cap (last ring)."""
    u0, v0, u1, v1 = m.region(region, m.mat)
    n = len(pts)
    if not isinstance(radii, (list, tuple)):
        radii = [radii] * n
    L = [0.0]
    for i in range(1, n):
        L.append(L[-1] + (pts[i] - pts[i - 1]).length)
    tot = max(L[-1], 1e-6)
    rings = []
    prev = None
    for i in range(n):
        tg = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        x = prev or (V(up_hint) if up_hint else (V((1, 0, 0)) if abs(tg.x) < .9 else V((0, 1, 0))))
        x = (x - tg * x.dot(tg)).normalized(); prev = x
        y = tg.cross(x)
        ring = []
        for k in range(sides):
            a = rot + 2 * math.pi * k / sides
            p = pts[i] + (x * math.cos(a) + y * math.sin(a)) * radii[i]
            if jitter and rnd:
                p += V((rnd.uniform(-jitter, jitter), rnd.uniform(-jitter, jitter), rnd.uniform(-jitter, jitter)))
            ring.append(p)
        rings.append(ring)
    def uv(k, i):
        a = k / sides; b = L[i] / tot
        if u_along:
            return (u0 + (u1 - u0) * b, v0 + (v1 - v0) * a)
        return (u0 + (u1 - u0) * a, v0 + (v1 - v0) * b)
    for i in range(n - 1):
        for k in range(sides):
            k2 = k + 1
            w = None
            if wind:
                w = [wind[i], wind[i], wind[i + 1], wind[i + 1]]
            m.quad(rings[i][k % sides], rings[i][k2 % sides], rings[i + 1][k2 % sides], rings[i + 1][k % sides],
                   uv(k, i), uv(k2, i), uv(k2, i + 1), uv(k, i + 1), w)
    if caps[0]:
        r = rings[0]
        w = [wind[0]] * sides if wind else None
        m.face(list(reversed(r)), [uv(k, 0) for k in reversed(range(sides))], w)
    if caps[1]:
        r = rings[-1]
        if cap_center is not None:
            c = V(cap_center)
            for k in range(sides):
                w = [wind[-1]] * 3 if wind else None
                m.tri(r[k], r[(k + 1) % sides], c, uv(k, n - 1), uv(k + 1, n - 1),
                      ((u0 + u1) / 2, v1) if not u_along else (u1, (v0 + v1) / 2), w)
        else:
            w = [wind[-1]] * sides if wind else None
            m.face(r, [uv(k, n - 1) for k in range(sides)], w)
    return rings

# ------------------------------------------------------------------ cattail
def cattail_stem(pl, props, pts, r=0.011, cob_from=0.72, cob_to=0.93, cob_r=0.035, sides_cob=6, sway=None,
                 rnd=None, broken=False, cob=True):
    """Thin 4-sided stalk (Plants reed_stem) along pts; a brown cob prism (Props cattail_cob) around the part
    cob_from..cob_to of the length, spike above. broken: no cob, jagged snapped top."""
    rnd = rnd or random.Random(0)
    n = len(pts)
    L = [0.0]
    for i in range(1, n):
        L.append(L[-1] + (pts[i] - pts[i - 1]).length)
    tot = L[-1]
    if sway is None:
        sway = 0.06 * tot
    wind = [(sway * (l / tot) ** 2, 0.004 * l / tot) for l in L]
    radii = [r * (1.0 - 0.45 * l / tot) for l in L]
    prism(pl, pts, radii, 'reed_stem', sides=4, rot=rnd.uniform(0, 1), caps=(False, not broken), wind=wind,
          cap_center=(pts[-1] + (pts[-1] - pts[-2]).normalized() * r * 1.5) if not broken else None)
    if broken:
        # jagged snapped end: a few splinter tris (Plants reed_stem) sticking up
        tip = pts[-1]; tg = (pts[-1] - pts[-2]).normalized()
        x = (V((1, 0, 0)) - tg * tg.x).normalized(); y = tg.cross(x)
        w = wind[-1]
        for k in range(3):
            a = 2 * math.pi * k / 3 + rnd.uniform(-.4, .4)
            b = tip + (x * math.cos(a) + y * math.sin(a)) * radii[-1]
            c = tip + (x * math.cos(a + 1.2) + y * math.sin(a + 1.2)) * radii[-1]
            s = tip + tg * rnd.uniform(.02, .06) + (x * math.cos(a + .6) + y * math.sin(a + .6)) * radii[-1] * .6
            u0, v0, u1, v1 = pl.region('reed_stem', 'Plants')
            pl.tri(b, c, s, (u0, v1 - .01), (u1, v1 - .01), ((u0 + u1) / 2, v1), [w, w, w])
            pl.tri(c, b, s, (u1, v1 - .01), (u0, v1 - .01), ((u0 + u1) / 2, v1), [w, w, w])
        return
    if not cob:
        return
    # resample the cob segment of the path
    def at(f):
        d = f * tot
        for i in range(1, n):
            if L[i] >= d:
                t = (d - L[i - 1]) / max(L[i] - L[i - 1], 1e-6)
                return pts[i - 1].lerp(pts[i], t), L[i - 1] + t * (L[i] - L[i - 1])
        return pts[-1], tot
    fs = [cob_from, cob_from + 0.02, (cob_from + cob_to) / 2, cob_to - 0.02, cob_to]
    cp = [at(f) for f in fs]
    cr = [cob_r * .55, cob_r * .95, cob_r * rnd.uniform(1.0, 1.1), cob_r * .9, cob_r * .5]
    cw = [(sway * (l / tot) ** 2, 0.004 * l / tot) for _, l in cp]
    prism(props, [p for p, _ in cp], cr, 'cattail_cob', sides=sides_cob, rot=rnd.uniform(0, 1), caps=(True, True),
          wind=cw, u_along=True, jitter=cob_r * 0.06, rnd=rnd)

# ------------------------------------------------------------------ mound loft (hummock)
def loft(m, rings, top_center=None, close_bottom=False, tile=None):
    """Side faces between consecutive rings (same vertex count), UV u = perimeter metres, v = z (tiling).
    top_center: apex point -> top fan (faceted top)."""
    tile = tile or m.tile()
    nr = len(rings); ns = len(rings[0])
    for i in range(nr - 1):
        acc = 0.0
        for k in range(ns):
            a, b = rings[i][k], rings[i][(k + 1) % ns]
            c, d = rings[i + 1][(k + 1) % ns], rings[i + 1][k]
            seg = (b - a).length
            m.quad(a, b, c, d, (acc / tile, a.z / tile), ((acc + seg) / tile, b.z / tile),
                   ((acc + seg) / tile, c.z / tile), (acc / tile, d.z / tile))
            acc += seg
    if top_center is not None:
        r = rings[-1]; c = V(top_center)
        for k in range(ns):
            a, b = r[k], r[(k + 1) % ns]
            m.tri(a, b, c, (a.x / tile, a.y / tile), (b.x / tile, b.y / tile), (c.x / tile, c.y / tile))
    if close_bottom:
        r = list(reversed(rings[0]))
        m.face(r, [(p.x / tile, p.y / tile) for p in r])

def blob_ring(center, rx, ry, z, ns, rnd, jit=0.12, rot=0.0, shift=(0, 0), phase=None, amps=None):
    """Irregular ring (polygon) around center at height z; radial jitter fraction jit; consistent lumps via phase."""
    pts = []
    for k in range(ns):
        a = rot + 2 * math.pi * k / ns
        f = 1.0
        if amps:
            f += sum(am * math.cos(j * a + ph) for j, (am, ph) in enumerate(zip(amps, phase), start=2))
        f *= 1 + rnd.uniform(-jit, jit)
        pts.append(V((center[0] + shift[0] + math.cos(a) * rx * f, center[1] + shift[1] + math.sin(a) * ry * f, z)))
    return pts

# ------------------------------------------------------------------ water pads / petals
def disk(pl, center, radius, region, rot=0.0, sides=12, sag=0.004, rim_z=0.006, rnd=None, tilt=(0, 0), flutter=0.01,
         sx=1.0, sy=1.0):
    """Thin flat pad: fan polygon, centre slightly lower, rim jittered in z; UV planar into an atlas region."""
    rnd = rnd or random.Random(0)
    u0, v0, u1, v1 = pl.region(region, pl.mat)
    C = V(center)
    ca, sa = math.cos(rot), math.sin(rot)
    def uv(dx, dy):  # dx,dy in local unrotated pad coords [-1,1]
        return (u0 + (dx * .5 + .5) * (u1 - u0), v0 + (dy * .5 + .5) * (v1 - v0))
    def place(lx, ly, z):
        x = lx * radius * sx; y = ly * radius * sy
        return C + V((x * ca - y * sa, x * sa + y * ca, z + tilt[0] * x + tilt[1] * y))
    c = place(0, 0, -sag)
    ring = []
    for k in range(sides):
        a = 2 * math.pi * k / sides
        lx, ly = math.cos(a) * 1.04, math.sin(a) * 1.04   # alpha disk is ~0.93 of the region; overshoot a bit
        ring.append((place(lx, ly, rim_z * rnd.uniform(-.5, 1)), uv(lx / 1.04 * 1.0, ly / 1.04 * 1.0)))
    for k in range(sides):
        (a, ua), (b, ub) = ring[k], ring[(k + 1) % sides]
        w = [(0, flutter * .3), (0, flutter), (0, flutter)]
        pl.tri(c, a, b, uv(0, 0), ua, ub, w)

def petal(m, base, direction, length, width, region, cup=0.35, wind=(0, 0)):
    """Boat-shaped petal: base -> tip along direction, sides raised (cupped). 4 tris, region UV."""
    u0, v0, u1, v1 = m.region(region, m.mat)
    d = V(direction).normalized()
    s = UP.cross(d)
    if s.length < 1e-4:
        s = V((1, 0, 0))
    s.normalize()
    nrm = d.cross(s).normalized()
    if nrm.z < 0:
        nrm = -nrm
    B = V(base); T = B + d * length
    Mi = B + d * length * 0.45
    Lp = Mi - s * width * .5 + nrm * width * cup
    Rp = Mi + s * width * .5 + nrm * width * cup
    um = (u0 + u1) / 2
    uvB = (um, v0); uvT = (um, v1); uvM = (um, v0 + .45 * (v1 - v0))
    uvL = (u0, v0 + .45 * (v1 - v0)); uvR = (u1, v0 + .45 * (v1 - v0))
    w = [wind] * 3
    m.tri(B, Mi, Lp, uvB, uvM, uvL, w); m.tri(Lp, Mi, T, uvL, uvM, uvT, w)
    m.tri(B, Rp, Mi, uvB, uvR, uvM, w); m.tri(Mi, Rp, T, uvM, uvR, uvT, w)

# ------------------------------------------------------------------ mushroom
def mushroom(props, base, top, cap_r, cap_h, stem_r, rnd, sides_cap=6, tilt=None, bend=0.0, droop=0.15):
    """4-sided bent stem (fungus_stem) from base to top, 5-6 sided cap (fungus_cap upper, fungus_gill underside),
    cap rim with a little thickness. tilt: cap axis (defaults to the stem tip direction)."""
    B, T = V(base), V(top)
    ax = (T - B)
    side = V((-ax.y, ax.x, 0))
    if side.length < 1e-5:
        side = V((1, 0, 0))
    side.normalize()
    pts = []
    for i in range(5):
        t = i / 4
        p = B.lerp(T, t) + side * bend * math.sin(math.pi * t) + V((rnd.uniform(-1, 1), rnd.uniform(-1, 1), 0)) * stem_r * .25 * (0 < i < 4)
        pts.append(p)
    radii = [stem_r * 1.45, stem_r * 1.05, stem_r * .95, stem_r * .9, stem_r * .85]
    prism(props, pts, radii, 'fungus_stem', sides=4, rot=rnd.uniform(0, 1.5), caps=(True, False))
    a = (tilt or (pts[-1] - pts[-2])).normalized()
    x = (V((1, 0, 0)) - a * a.x).normalized(); y = a.cross(x)
    ns = sides_cap
    r0 = rnd.uniform(0, 1)
    rim, rim_lo, upper, under = [], [], [], []
    apex = T + a * cap_h * 0.75
    for k in range(ns):
        ang = r0 + 2 * math.pi * k / ns
        dr = (x * math.cos(ang) + y * math.sin(ang))
        rr = cap_r * rnd.uniform(.88, 1.1)
        dz = -droop * cap_h * rnd.uniform(.5, 1.5)
        rim.append(T + dr * rr + a * (cap_h * 0.12 + dz))
        rim_lo.append(T + dr * rr * 0.97 + a * (dz - cap_h * 0.05))
        upper.append(T + dr * rr * 0.62 + a * cap_h * 0.55)
        under.append(T + dr * stem_r * 1.3 - a * cap_h * 0.02)
    cu0, cv0, cu1, cv1 = props.region('fungus_cap', 'Props')
    def cuv(p):
        d = p - T
        return (cu0 + (d.dot(x) / cap_r * .5 + .5) * (cu1 - cu0), cv0 + (d.dot(y) / cap_r * .5 + .5) * (cv1 - cv0))
    for k in range(ns):
        k2 = (k + 1) % ns
        props.quad(rim[k], rim[k2], upper[k2], upper[k], cuv(rim[k]), cuv(rim[k2]), cuv(upper[k2]), cuv(upper[k]))
        props.tri(upper[k], upper[k2], apex, cuv(upper[k]), cuv(upper[k2]), cuv(apex))
        props.quad(rim_lo[k], rim_lo[k2], rim[k2], rim[k], cuv(rim_lo[k]), cuv(rim_lo[k2]), cuv(rim[k2]), cuv(rim[k]))
    gu0, gv0, gu1, gv1 = props.region('fungus_gill', 'Props')
    for k in range(ns):
        k2 = (k + 1) % ns
        ua, ub = gu0 + (gu1 - gu0) * k / ns, gu0 + (gu1 - gu0) * (k + 1) / ns
        props.quad(rim_lo[k2], rim_lo[k], under[k], under[k2], (ub, gv1), (ua, gv1), (ua, gv0), (ub, gv0))
    return apex

# ------------------------------------------------------------------ utils
def centre_xy(objs, z_keep=True):
    lo, hi = sk.bounds(objs)
    d = V((-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, 0))
    for o in objs:
        o.data.transform(Matrix.Translation(d))

def offset(objs, d):
    for o in objs:
        o.data.transform(Matrix.Translation(V(d)))

def finish(meshes, vid):
    """Mesh objects for the non-empty meshes named '<vid>_<Mat>'."""
    out = []
    for m in meshes:
        if m is None:
            continue
        if not m.bm.faces:
            m.bm.free(); continue
        out.append(m.to_object('%s_%s' % (vid, m.mat)))
    return out

def render_views(objs, outdir, tag, views, size=(300, 300), water_z=None, samples=8, **kw):
    files = []
    for j, (az, el) in enumerate(views):
        f = os.path.join(outdir, '%s_%d.png' % (tag, j))
        sk.render(objs, f, az=az, el=el, size=size, water_z=water_z, samples=samples, **kw)
        files.append(f)
    return files

def side_by_side(variants, gap=0.6):
    """Move each variant's objects along +X so they sit side by side in the .blend."""
    x = 0.0
    for objs in variants:
        lo, hi = sk.bounds(objs)
        w = hi[0] - lo[0]
        for o in objs:
            o.location.x = x + w / 2
        x += w + gap

# ------------------------------------------------------------------ cattail leaves (shared with SW01)
def leaf(pl, rnd, base, az, length, width, tilt0=0.15, tilt1=0.9, kink=None, segs=5):
    """Strap leaf. kink=(f, drop_angle): snapped at fraction f, the rest hangs down/outwards."""
    if kink is None:
        pts = arc_path(base, az, tilt0, tilt1, length, segs=segs, p=1.7)
    else:
        f, dang = kink
        n1 = max(2, int(segs * f))
        pts = arc_path(base, az, tilt0, tilt0 + 0.25, length * f, segs=n1, p=1.5)
        d = V((math.sin(dang) * math.cos(az), math.sin(dang) * math.sin(az), math.cos(dang)))
        rest = length * (1 - f)
        for i in range(1, 3):
            pts.append(pts[-1] + d * rest / 2 + V((0, 0, -0.02 * i)))
    for p in pts[1:]:   # drooping / snapped leaves never dip below the root
        p.z = max(p.z, 0.015)
    blade(pl, pts, width, 'cattail_leaf_%d' % (1 if kink or rnd.random() < .3 else 0),
               fold=rnd.uniform(.15, .3), twist=rnd.uniform(-.6, .6), roll=rnd.uniform(-.5, .5),
               sway=0.06 * length)

def leaves_ring(pl, rnd, n, Lmin, Lmax, width, kinks=0.3, spread=(0.15, 0.6), droop=(0.5, 1.3), az0=0.0,
                pref_az=None, pref=0.0):
    for i in range(n):
        az = az0 + 2 * math.pi * i / n + rnd.uniform(-.4, .4)
        if pref_az is not None:
            da = math.atan2(math.sin(pref_az - az), math.cos(pref_az - az)); az += da * pref
        L = rnd.uniform(Lmin, Lmax)
        t0 = rnd.uniform(*spread)
        base = V((0, 0, 0)) + hdir(az) * rnd.uniform(0.01, 0.05)
        if rnd.random() < kinks:
            leaf(pl, rnd, base, az, L, width * rnd.uniform(.9, 1.15), t0, kink=(rnd.uniform(.45, .7), rnd.uniform(1.9, 2.6)))
        else:
            leaf(pl, rnd, base, az, L, width * rnd.uniform(.9, 1.15), t0, t0 + rnd.uniform(*droop))

