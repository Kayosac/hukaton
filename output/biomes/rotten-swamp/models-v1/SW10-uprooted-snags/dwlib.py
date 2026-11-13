"""Deadwood helpers shared by SW10/11/12/15/27 (built on the kit sk.py; the kit itself is not modified).

ptube(): a crooked tube whose cross-section is an arbitrary (also non-convex) 2D profile per ring, so stumps can have
root buttresses (star profiles), logs can have V cracks/splits, hollow shells are C-shaped profiles, and the last ring
can be pushed into a jagged broken crown. Profiles are CCW in the ring frame (x, y) -> faces point outward.
"""
import sys, math, random
sys.path.insert(0, '/home/claude/swamp/kit')
import sk
import bpy
from mathutils import Vector, Matrix, geometry

V = Vector


def frames(P, up_hint=None):
    n = len(P)
    out = []
    prev_x = None
    for i in range(n):
        t = (P[min(i + 1, n - 1)] - P[max(i - 1, 0)]).normalized()
        if prev_x is None:
            x = V(up_hint) if up_hint else (V((0, 0, 1)) if abs(t.z) < .9 else V((1, 0, 0)))
        else:
            x = prev_x
        x = (x - t * x.dot(t)).normalized()
        prev_x = x
        y = t.cross(x)
        out.append((x, y, t))
    return out


def prof(n, rx, ry=None, jit=0.0, seed=0, rot=0.0, ajit=0.0):
    """Irregular convex-ish polygon, CCW."""
    rnd = random.Random(seed)
    ry = rx if ry is None else ry
    pts = []
    for k in range(n):
        a = rot + 2 * math.pi * k / n + rnd.uniform(-ajit, ajit) * 2 * math.pi / n
        f = 1 + rnd.uniform(-jit, jit)
        pts.append((math.cos(a) * rx * f, math.sin(a) * ry * f))
    return pts


def scale_prof(p, s, sy=None):
    sy = s if sy is None else sy
    return [(x * s, y * sy) for x, y in p]


def ptube(m, path, profiles, offs=None, region=None, uv_swap=False, wind=None, v0=0.0, u0=0.0, up_hint=None,
          uv_scale=1.0, alt=None):
    """m: sk.Mesh. path: points. profiles: list of 2D CCW lists (same count) or one list for all rings.
    offs(i, k, x, y, t) -> Vector offset added to ring vertex (crowns, flares). region: map into an atlas region
    (u = around 0..1, v = along 0..1; uv_swap: length -> region u). Returns rings (lists of Vectors), frames."""
    P = [V(p) for p in path]
    n = len(P)
    if profiles and isinstance(profiles[0][0], (int, float)):
        profiles = [profiles] * n
    F = frames(P, up_hint)
    rings = []
    for i in range(n):
        x, y, t = F[i]
        r = []
        for k, (a, b) in enumerate(profiles[i]):
            p = P[i] + x * a + y * b
            if offs:
                p = p + offs(i, k, x, y, t)
            r.append(p)
        rings.append(r)
    _skin(m, rings, region=region, uv_swap=uv_swap, wind=wind, v0=v0, u0=u0, uv_scale=uv_scale, alt=alt)
    return rings, F


def _skin(m, rings, region=None, uv_swap=False, wind=None, v0=0.0, u0=0.0, uv_scale=1.0, closed=True, alt=None):
    n = len(rings)
    N = len(rings[0])
    tile = (m.tile() or 1.0) / uv_scale
    # along-length distance per vertex column (average)
    L = [0.0]
    for i in range(1, n):
        d = sum((rings[i][k] - rings[i - 1][k]).length for k in range(N)) / N
        L.append(L[-1] + d)
    # around distance per ring
    U = []
    for i in range(n):
        acc = [0.0]
        for k in range(N):
            acc.append(acc[-1] + (rings[i][(k + 1) % N] - rings[i][k]).length)
        U.append(acc)
    if region:
        ru0, rv0, ru1, rv1 = m.region(region, m.mat)
    kmax = N if closed else N - 1
    for i in range(n - 1):
        for k in range(kmax):
            k2 = (k + 1) % N
            def uv(ii, kk, kidx):
                uu = U[ii][kidx] / (U[ii][-1] if region else tile)
                vv = L[ii] / (L[-1] if region else tile)
                if region:
                    if uv_swap:
                        uu, vv = vv, uu
                    return (ru0 + uu * (ru1 - ru0), rv0 + vv * (rv1 - rv0))
                return (uu + u0, vv + v0)
            tgt = m
            if alt is not None and k in alt[1] and (len(alt) < 4 or alt[3][0] <= i < alt[3][1]):
                tgt = alt[0]
                a0_, b0_, a1_, b1_ = tgt.region(alt[2], tgt.mat)
                ks = sorted(alt[1]); kk_ = ks.index(k)
                fu0, fu1 = kk_ / len(ks), (kk_ + 1) / len(ks)
                fv0, fv1 = L[i] / L[-1], L[i + 1] / L[-1]
                tgt.quad(rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k],
                         (a0_ + fu0 * (a1_ - a0_), b0_ + fv0 * (b1_ - b0_)), (a0_ + fu1 * (a1_ - a0_), b0_ + fv0 * (b1_ - b0_)),
                         (a0_ + fu1 * (a1_ - a0_), b0_ + fv1 * (b1_ - b0_)), (a0_ + fu0 * (a1_ - a0_), b0_ + fv1 * (b1_ - b0_)))
                continue
            wv = None
            if wind:
                wv = [wind(rings[i][k]), wind(rings[i][k2]), wind(rings[i + 1][k2]), wind(rings[i + 1][k])]
            m.quad(rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k],
                   uv(i, k, k), uv(i, k2, k + 1), uv(i + 1, k2, k + 1), uv(i + 1, k, k), wv)


def poly_cap(m, ring, normal, region=None, flip=False, center=None):
    """Cap any (non-convex) planar-ish ring. normal: desired facing. region: atlas region (Props) or tiling.
    center: optional apex (fan) -> jagged / sunken caps."""
    ring = [V(p) for p in ring]
    nrm = V(normal).normalized()
    c = sum(ring, V()) / len(ring)
    ax = (ring[0] - c)
    ax = (ax - nrm * ax.dot(nrm)).normalized()
    ay = nrm.cross(ax)
    rad = max(((p - c) - nrm * (p - c).dot(nrm)).length for p in ring) or 1.0
    if region:
        u0, v0, u1, v1 = m.region(region, m.mat)
    t = m.tile() or 1.0
    def uvof(p):
        d = p - c
        if region:
            return (u0 + (d.dot(ax) / rad * .5 + .5) * (u1 - u0), v0 + (d.dot(ay) / rad * .5 + .5) * (v1 - v0))
        return (d.dot(ax) / t, d.dot(ay) / t)
    if center is not None:
        C = V(center)
        for k in range(len(ring)):
            a, b = ring[k], ring[(k + 1) % len(ring)]
            fn = (b - a).cross(C - a)
            if fn.dot(nrm) < 0:
                a, b = b, a
            m.tri(a, b, C, uvof(a), uvof(b), uvof(C))
        return
    p2 = [V(((p - c).dot(ax), (p - c).dot(ay), 0)) for p in ring]
    tris = geometry.tessellate_polygon([p2])
    for tr in tris:
        a, b, cc = [ring[j] for j in tr]
        fn = (b - a).cross(cc - a)
        if fn.length < 1e-9:
            continue
        if fn.dot(nrm) < 0:
            b, cc = cc, b
        m.tri(a, b, cc, uvof(a), uvof(b), uvof(cc))


def crown_offsets(N, heights, last_index, inward=0.0, centre_pull=None):
    """offs() factory: on the ring `last_index` push vertex k by heights[k] along t (jagged broken top)."""
    def offs(i, k, x, y, t):
        if i != last_index:
            return V()
        return t * heights[k % len(heights)]
    return offs


def broken_cap(props, ring, t, depth=0.05, region='end_grain_0'):
    """Fan cap of a jagged ring towards a sunken centre (end grain on the Props atlas)."""
    c = sum(ring, V()) / len(ring)
    lo = min((p - c).dot(t) for p in ring)
    C = c + t * (lo - depth)
    poly_cap(props, ring, t, region=region, center=C)
    return C


def jag(N, lo, hi, seed, spikes=2, spike_h=None):
    """Heights for a broken crown: mostly low, `spikes` tall splinters."""
    rnd = random.Random(seed)
    h = [rnd.uniform(lo, (lo + hi) * .5) for _ in range(N)]
    idx = rnd.sample(range(N), min(spikes, N))
    for j in idx:
        h[j] = rnd.uniform(hi * .7, hi) if spike_h is None else spike_h
    return h


def wob(path, amp, seed, keep_ends=True):
    rnd = random.Random(seed)
    out = []
    for i, p in enumerate(path):
        if keep_ends and (i == 0):
            out.append(V(p)); continue
        out.append(V(p) + V((rnd.uniform(-amp, amp), rnd.uniform(-amp, amp), rnd.uniform(-amp, amp) * .6)))
    return out


def bez(pts, n):
    """Quadratic/cubic-ish smooth chain through control points (Catmull-Rom), n samples."""
    P = [V(p) for p in pts]
    out = []
    segs = len(P) - 1
    for s in range(n):
        u = s / (n - 1) * segs
        i = min(int(u), segs - 1)
        f = u - i
        p0 = P[max(i - 1, 0)]; p1 = P[i]; p2 = P[i + 1]; p3 = P[min(i + 2, segs)]
        f2, f3 = f * f, f * f * f
        out.append(0.5 * ((2 * p1) + (-p0 + p2) * f + (2 * p0 - 5 * p1 + 4 * p2 - p3) * f2 + (-p0 + 3 * p1 - 3 * p2 + p3) * f3))
    return out


def root(m, start, ang, length, r0, r1, rise=0.1, end_z=-0.3, sides=5, seed=0, n=7, flat=0.8, wobble=0.05,
         twist=0.3, elbow=0.45, props=None, tip_up=False, shape='arch', water=0.0, blunt=False, taper_p=0.8):
    """Tentacle root: from `start` (inside the trunk) outward along horizontal angle `ang`, arching up by `rise`,
    then bending down into the water to end_z. Tapered crooked tube. Returns path."""
    rnd = random.Random(seed)
    S = V(start)
    d = V((math.cos(ang), math.sin(ang), 0))
    side = V((-d.y, d.x, 0))
    cz = S.z
    if shape == 'tentacle':
        sg = rnd.choice((-1, 1))     # arch up & out, then curl down into the water, sideways S
        ctrl = [S,
                S + d * length * .2 + side * sg * .03 * length + V((0, 0, rise)),
                S + d * length * .45 + side * sg * rnd.uniform(.08, .16) * length + V((0, 0, rise * 1.05)),
                S + d * length * .7 + side * sg * rnd.uniform(.02, .1) * length + V((0, 0, water + (cz + rise - water) * .45 - cz)),
                S + d * length * .88 - side * sg * rnd.uniform(.0, .08) * length + V((0, 0, water + .03 - cz)),
                S + d * length * .96 - side * sg * rnd.uniform(.02, .1) * length + V((0, 0, end_z - cz))]
    elif shape == 'ramp':
        sg = rnd.choice((-1, 1))     # S-curve sideways: tentacle-like, not a straight stick
        ctrl = [S,
                S + d * length * .2 + side * sg * rnd.uniform(.0, .06) * length + V((0, 0, rise)),
                S + d * length * .5 + side * sg * rnd.uniform(.12, .2) * length + V((0, 0, water + (cz - water) * .45 + rise * .3 - cz)),
                S + d * length * .8 - side * sg * rnd.uniform(.0, .1) * length + V((0, 0, water + .06 - cz)),
                S + d * length - side * sg * rnd.uniform(.05, .14) * length + V((0, 0, end_z - cz))]
    else:
        ctrl = [S,
                S + d * length * .22 + V((0, 0, rise)),
                S + d * length * elbow + V((0, 0, rise * .6)) + side * rnd.uniform(-.12, .12) * length,
                S + d * length * .78 + side * rnd.uniform(-.15, .15) * length + V((0, 0, (end_z - cz) * .45 + rise * .2)),
                S + d * length + side * rnd.uniform(-.1, .1) * length + V((0, 0, end_z - cz))]
    if tip_up:
        ctrl[-1] = S + d * length + V((0, 0, rise * .5))
    path = bez(ctrl, n)
    path = [path[0]] + [p + V((rnd.uniform(-wobble, wobble), rnd.uniform(-wobble, wobble), rnd.uniform(-wobble, wobble) * .5)) for p in path[1:]]
    profs = []
    base = prof(sides, 1, flat, jit=.18, seed=seed, rot=rnd.uniform(0, 1))
    for i in range(n):
        f = i / (n - 1)
        r = r0 + (r1 - r0) * (f ** taper_p)
        a = twist * f
        ca, sa = math.cos(a), math.sin(a)
        profs.append([((x * ca - y * sa) * r, (x * sa + y * ca) * r) for x, y in base])
    rings, F = ptube(m, path, profs, up_hint=(0, 0, 1))
    # tip cap (closed, below water usually)
    if blunt:
        poly_cap(m, rings[-1], F[-1][2], center=sum(rings[-1], V()) / len(rings[-1]) + F[-1][2] * r1 * .35)
    else:
        poly_cap(m, rings[-1], F[-1][2], center=sum(rings[-1], V()) / len(rings[-1]) + F[-1][2] * r1 * 1.5)
    if tip_up and props is not None:
        pass
    return path, rings, F


def splinter(m, base, direction, length, width, seed=0, sides=4):
    """Thin tapered wooden shard (3-4 sided spike)."""
    rnd = random.Random(seed)
    B = V(base); D = V(direction).normalized()
    path = [B, B + D * length * .5 + V((rnd.uniform(-.02, .02), rnd.uniform(-.02, .02), 0)), B + D * length]
    profs = [prof(sides, width, width * .5, jit=.2, seed=seed), prof(sides, width * .6, width * .3, jit=.2, seed=seed + 1),
             [(0.0005 * math.cos(2 * math.pi * k / sides), 0.0005 * math.sin(2 * math.pi * k / sides)) for k in range(sides)]]
    ptube(m, path, profs)


def mushroom(props, base, up, h, cap_r, seed=0, sides=7, tilt=(0, 0)):
    """Pale swamp mushroom: 5-sided stem, conical cap with gill underside (Props fungus_*)."""
    rnd = random.Random(seed)
    B = V(base)
    U = (V(up).normalized() + V((tilt[0], tilt[1], 0))).normalized()
    top = B + U * h
    sr = cap_r * rnd.uniform(.18, .24)
    ptube(props, [B - U * .02, B + U * h * .5 + V((rnd.uniform(-.01, .01), rnd.uniform(-.01, .01), 0)), top],
          [prof(5, sr * 1.2, seed=seed), prof(5, sr, seed=seed + 1), prof(5, sr * .85, seed=seed + 2)],
          region='fungus_stem', uv_swap=True)
    # cap: rim ring slightly below top, apex above
    F = frames([B, top])
    x, y, t = F[-1]
    rim = []
    for k in range(sides):
        a = 2 * math.pi * k / sides + rnd.uniform(-.15, .15)
        rr = cap_r * rnd.uniform(.9, 1.08)
        rim.append(top + x * math.cos(a) * rr + y * math.sin(a) * rr - t * cap_r * .18)
    mid = []
    for k in range(sides):
        a = 2 * math.pi * (k + .5) / sides
        mid.append(top + x * math.cos(a) * cap_r * .62 + y * math.sin(a) * cap_r * .62 + t * cap_r * .28)
    apex = top + t * cap_r * .45
    u0, v0, u1, v1 = props.region('fungus_cap')
    def cuv(p):
        d = p - top
        return (u0 + (d.dot(x) / cap_r * .5 + .5) * (u1 - u0), v0 + (d.dot(y) / cap_r * .5 + .5) * (v1 - v0))
    for k in range(sides):
        k2 = (k + 1) % sides
        props.tri(rim[k], rim[k2], mid[k], cuv(rim[k]), cuv(rim[k2]), cuv(mid[k]))
        props.tri(mid[k], rim[k2], mid[k2], cuv(mid[k]), cuv(rim[k2]), cuv(mid[k2]))
        props.tri(mid[k], mid[k2], apex, cuv(mid[k]), cuv(mid[k2]), cuv(apex))
    # gills underneath (facing down)
    g0, gv0, g1, gv1 = props.region('fungus_gill')
    under = top - t * cap_r * .05
    for k in range(sides):
        k2 = (k + 1) % sides
        props.tri(rim[k2], rim[k], under, (g0 + (g1 - g0) * k2 / sides, gv1), (g0 + (g1 - g0) * k / sides, gv1),
                  ((g0 + g1) / 2, gv0))


def mushroom_cluster(props, base, up, seed=0, n=3, size=1.0):
    rnd = random.Random(seed)
    U = V(up).normalized()
    side = U.cross(V((0, 0, 1)) if abs(U.z) < .9 else V((1, 0, 0))).normalized()
    side2 = U.cross(side)
    for i in range(n):
        off = side * rnd.uniform(-.06, .06) * size + side2 * rnd.uniform(-.05, .05) * size
        s = size * (1.0 if i == 0 else rnd.uniform(.45, .75))
        mushroom(props, V(base) + off, U, .14 * s, .07 * s, seed=seed * 10 + i,
                 tilt=(rnd.uniform(-.25, .25), rnd.uniform(-.25, .25)))


def sedge(pl, base, h=0.6, w=0.5, seed=0, n=3, kind=None):
    rnd = random.Random(seed)
    reg = kind or 'sedge_%d' % rnd.randint(0, 2)
    pl.cross_cards(base, w, h, reg, n=n, rot=rnd.uniform(0, 3), lean=(rnd.uniform(-.15, .15), rnd.uniform(-.15, .15)),
                   seed=seed, wind_sway=.05 * h, wind_flutter=.02)


# ---------------------------------------------------------------- checks / output
def signed_volume(o):
    me = o.data
    me.calc_loop_triangles()
    vol = 0.0
    for t in me.loop_triangles:
        a, b, c = [me.vertices[i].co for i in t.vertices]
        vol += a.dot(b.cross(c)) / 6
    return vol


def finish(vid, meshes, moss_from=None, moss_kw=None, drips=True, extra=None, drip_min_z=-99.0):
    """meshes: dict mat->sk.Mesh (Plants optional). Returns objects (Moss draped over moss_from mats)."""
    objs = []
    pl = meshes.get('Plants')
    mobjs = {}
    for mat, m in meshes.items():
        if mat in ('Plants', 'Moss') or m is None:
            continue
        if not m.bm.faces:
            m.bm.free(); continue
        name = '%s_%s' % (vid, mat) if not mat.startswith('Props_') else '%s_%s' % (vid, mat.split('_', 1)[1])
        o = m.to_object(name)
        mobjs[mat] = o
        objs.append(o)
    if moss_from:
        kw = dict(min_nz=0.55, keep=0.8, seed=3)
        kw.update(moss_kw or {})
        mm = meshes.get('Moss') or sk.Mesh('Moss')
        for mat in moss_from:
            o = mobjs.get(mat)
            if not o:
                continue
            moss_ragged(o, mm, pl if drips else None, drip_min_z=drip_min_z, **kw)
        if mm.bm.faces:
            objs.append(mm.to_object(vid + '_Moss'))
        else:
            mm.bm.free()
    elif meshes.get('Moss') is not None and meshes['Moss'].bm.faces:
        objs.append(meshes['Moss'].to_object(vid + '_Moss'))
    if extra:
        objs.extend(extra)
    if pl is not None and pl.bm.faces:
        objs.append(pl.to_object(vid + '_Plants'))
    return objs


def offset_objs(objs, d):
    for o in objs:
        o.data.transform(Matrix.Translation(d))


def place_side_by_side(groups, gap=1.0):
    x = 0.0
    for objs in groups:
        lo, hi = sk.bounds(objs)
        offset_objs(objs, V((x - lo[0], 0, 0)))
        x += (hi[0] - lo[0]) + gap


def backface_render(objs, out, az=-35, el=20, size=(320, 320)):
    """Debug: render with backfaces red (to find inside-out faces)."""
    mat = bpy.data.materials.get('DBG') or bpy.data.materials.new('DBG')
    mat.use_nodes = True
    nt = mat.node_tree
    for nd in list(nt.nodes):
        nt.nodes.remove(nd)
    geo = nt.nodes.new('ShaderNodeNewGeometry'); mix = nt.nodes.new('ShaderNodeMixRGB')
    mix.inputs[1].default_value = (.7, .7, .7, 1); mix.inputs[2].default_value = (1, 0, 0, 1)
    bs = nt.nodes.new('ShaderNodeBsdfDiffuse'); out_n = nt.nodes.new('ShaderNodeOutputMaterial')
    nt.links.new(geo.outputs['Backfacing'], mix.inputs[0]); nt.links.new(mix.outputs[0], bs.inputs[0])
    nt.links.new(bs.outputs[0], out_n.inputs[0])
    saved = []
    for o in objs:
        saved.append(list(o.data.materials))
        o.data.materials.clear(); o.data.materials.append(mat)
    sk.render(objs, out, az=az, el=el, size=size, samples=4)
    for o, ms in zip(objs, saved):
        o.data.materials.clear()
        for mm in ms:
            o.data.materials.append(mm)


def make_card(variants, outdir, concept, title, views=((35, 20), (145, 20), (90, 16)), size=(320, 320),
              samples=10, extra_view=(-35, 8), only=None, margin=0.72):
    """variants: list of (label, objs, water_z). Renders concept views (with water) + one dry view of the whole mesh."""
    import os
    os.makedirs(outdir, exist_ok=True)
    tiles, labels = [], []
    for vi, (lab, objs, wz) in enumerate(variants):
        for j, (az, el) in enumerate(views):
            p = os.path.join(outdir, 'r_%s_%d.png' % (lab.split()[0], j))
            if only is None or lab.split()[0] in only:
                sk.render(objs, p, az=az, el=el, size=size, samples=samples, water_z=wz, margin=margin)
            tiles.append(p); labels.append('%s az%d' % (lab, az))
        if extra_view:
            p = os.path.join(outdir, 'r_%s_dry.png' % lab.split()[0])
            if only is None or lab.split()[0] in only:
                sk.render(objs, p, az=extra_view[0], el=extra_view[1], size=size, samples=samples, margin=margin)
            tiles.append(p); labels.append('%s full mesh (no water)' % lab)
    cols = len(views) + (1 if extra_view else 0)
    return sk.sheet(tiles, os.path.join(outdir, 'card.jpg'), cols, labels=labels, concept=concept, title=title)


def _mface(mm, pts):
    """Moss polygon with dominant-axis planar UVs."""
    pts = [V(p) for p in pts]
    n = V()
    for j in range(1, len(pts) - 1):
        n += (pts[j] - pts[0]).cross(pts[j + 1] - pts[0])
    if n.length < 1e-10:
        return
    n.normalize()
    t = sk.MATS['Moss'][2]
    if abs(n.z) > .6:
        uv = [(p.x / t, p.y / t) for p in pts]
    else:
        h = V((-n.y, n.x, 0)).normalized()
        uv = [(p.dot(h) / t, p.z / t) for p in pts]
    mm.face(pts, uv)


def _drip(pl, A, B, n, h, rnd, off=.016):
    """Drip card hanging from edge A-B on a surface with normal n (faces outward)."""
    R = (B - A) / 2 * .95
    if V((0, 0, 1)).cross(R).dot(n) < 0:
        R = -R
    out = V((n.x, n.y, 0))
    out = out.normalized() if out.length > 1e-6 else V()
    pl.card((A + B) / 2 + out * off + V((0, 0, .015)), R, V((0, 0, h)), 'moss_drip_%d' % rnd.randint(0, 1),
            hang=True, segs=1, wind_flutter=.01)


def moss_ragged(o, mm, pl, min_nz=.55, keep=.75, seed=0, off=.012, drape=(.25, .7), drape_chance=.7,
                drip_chance=.55, drip_min_z=-99.0, shrink=(.08, .45), drip_len=(.1, .28), min_z=-99.0):
    """Ragged moss skin over the upward faces of o: each kept face is copied with jagged, randomly inset edges where
    it borders uncovered faces (subdivided edges, points pulled to the centroid); over edges that drop to steeper
    faces it hangs ragged tongues down that face with drip cards -- the concept's ochre moss drapes."""
    import bmesh
    rnd = random.Random(seed + 7)
    bm = bmesh.new(); bm.from_mesh(o.data); bm.faces.ensure_lookup_table(); bm.normal_update()
    sel = set(f.index for f in bm.faces if f.normal.z >= min_nz and f.calc_area() > 1e-5 and
              f.calc_center_median().z >= min_z and rnd.random() < keep)
    for f in bm.faces:
        if f.index not in sel:
            continue
        n = f.normal.copy()
        c = f.calc_center_median()
        loops = list(f.loops)
        pts = []
        for lp in loops:
            a = lp.vert.co; b = lp.link_loop_next.vert.co
            e = lp.edge
            others = [g for g in e.link_faces if g.index != f.index]
            g = others[0] if others else None
            covered = g is not None and g.index in sel
            for t in (0, 1 / 3, 2 / 3):
                p = a.lerp(b, t)
                if not covered:
                    p = p.lerp(c, rnd.uniform(*shrink) * (1.0 if t > 0 else .6))
                pts.append(p + n * off)
            # tongue draped over the edge onto a steeper neighbour
            if g is not None and not covered and g.normal.z < min_nz and rnd.random() < drape_chance:
                gc = g.calc_center_median()
                em = (a + b) / 2
                dvec = gc - em
                gn = g.normal
                dvec = dvec - gn * dvec.dot(gn)
                if dvec.z > -.01 or dvec.length < .03:
                    continue
                t0, t1 = sorted((rnd.uniform(.05, .45), rnd.uniform(.55, .95)))
                e0, e1 = a.lerp(b, t0), a.lerp(b, t1)
                dep = rnd.uniform(*drape)
                bots = []
                for tt in (t1, (t0 + t1) / 2 + rnd.uniform(-.1, .1) * (t1 - t0), t0):
                    bots.append(a.lerp(b, tt) + dvec * dep * rnd.uniform(.45, 1.0))
                poly = [e0, e1] + bots
                poly = [p + gn * off * 1.2 for p in poly]
                pn = (poly[1] - poly[0]).cross(poly[-1] - poly[0])
                if pn.dot(gn) < 0:
                    poly = list(reversed(poly))
                # triangulate as a fan from the tongue centre (non-convex safe enough for 5 points)
                tc = sum(poly, V()) / len(poly)
                for j in range(len(poly)):
                    _mface(mm, [tc, poly[j], poly[(j + 1) % len(poly)]])
                if pl is not None and rnd.random() < drip_chance:
                    lo = min(bots[0].z, bots[2].z)
                    h = rnd.uniform(*drip_len)
                    if lo - h > drip_min_z:
                        _drip(pl, bots[2] + gn * off, bots[0] + gn * off, gn, h, rnd)
        cc = c + n * off
        for j in range(len(pts)):
            _mface(mm, [cc, pts[j], pts[(j + 1) % len(pts)]])
    bm.free()


def moss_cape(mm, pl, rings, i0, i1, ks, off=0.014, drip_chance=0.7, seed=0, drip_len=(.12, .3), cols=3,
              reach=(.2, .85)):
    """Ragged moss drape over trunk faces: hangs from ring i1 down towards ring i0; per face the lower edge is a
    zig-zag of random depths, the sides are pulled in at random; drip cards hang from the lowest tongues."""
    rnd = random.Random(seed)
    N = len(rings[0])
    span = i1 - i0
    def surf(k, f, d):
        """point on face k at across-fraction f, depth d (0 = ring i1, span = ring i0)."""
        k2 = (k + 1) % N
        i = i1 - d
        ia = max(i0, min(i1 - 1, int(math.floor(i))))
        t = i - ia
        lo = rings[ia][k].lerp(rings[ia][k2], f)
        hi = rings[ia + 1][k].lerp(rings[ia + 1][k2], f)
        return lo.lerp(hi, t)
    for k in ks:
        k2 = (k + 1) % N
        n = (rings[i1 - 1][k2] - rings[i1 - 1][k]).cross(rings[i1][k] - rings[i1 - 1][k])
        if n.length < 1e-9:
            continue
        n.normalize()
        m = cols * 2
        fs = [0.0] + sorted(rnd.uniform(.05, .95) for _ in range(m - 1)) + [1.0]
        fs[0] = rnd.uniform(0, .12); fs[-1] = rnd.uniform(.88, 1)
        deps = [span * rnd.uniform(*reach) * (.35 if j in (0, len(fs) - 1) else 1) for j in range(len(fs))]
        for j in range(len(fs) - 1):
            fa, fb = fs[j], fs[j + 1]
            da, db = deps[j], deps[j + 1]
            steps = max(1, int(math.ceil(max(da, db) * 2)))
            prev = None
            for s_ in range(steps + 1):
                u = s_ / steps
                pa = surf(k, fa, da * u) + n * off; pb = surf(k, fb, db * u) + n * off
                if prev is not None:
                    _mface(mm, [pa, pb, prev[1], prev[0]])
                prev = (pa, pb)
        if pl is not None and rnd.random() < drip_chance:
            j = max(range(len(fs)), key=lambda q: deps[q])
            j = min(j, len(fs) - 2)
            A = surf(k, fs[j], deps[j]); B = surf(k, fs[j + 1], deps[j + 1])
            if (B - A).length > .05:
                _drip(pl, A + n * off, B + n * off, n, rnd.uniform(*drip_len), rnd)


def flare_profiles(profs, ks, amts):
    """Push profile vertices ks radially outward on the first rows (root buttresses flowing into the trunk)."""
    for i, a in enumerate(amts):
        if i >= len(profs):
            break
        p = profs[i]
        for k in ks:
            x, y = p[k]
            p[k] = (x * (1 + a), y * (1 + a))


def ks_facing(rings, i, direction, thr=0.2):
    N = len(rings[i]); c = sum(rings[i], V()) / N
    d = V(direction).normalized()
    out = []
    for k in range(N):
        m = (rings[i][k] + rings[i][(k + 1) % N]) / 2 - c
        m.z = 0
        if m.length > 1e-6 and m.normalized().dot(d) > thr:
            out.append(k)
    return out


def c_profile(a0, a1, ro, ri, n, seed, jit=.08):
    """C-shaped (shell) profile: outer arc a0->a1 CCW, inner arc back. Non-convex, CCW."""
    rnd = random.Random(seed)
    outer = []
    for k in range(n):
        a = a0 + (a1 - a0) * k / (n - 1)
        f = 1 + rnd.uniform(-jit, jit)
        outer.append((math.cos(a) * ro * f, math.sin(a) * ro * f))
    inner = []
    for k in range(n - 1, -1, -1):
        a = a0 + (a1 - a0) * k / (n - 1)
        f = 1 + rnd.uniform(-jit, jit)
        inner.append((math.cos(a) * ri * f, math.sin(a) * ri * f))
    return outer + inner


def drips_from(mo, pl, seed=0, min_z=-99.0, chance=.55, out_k=.55, hmax=.3):
    """Moss drip cards along the outer boundary edges of a moss skin, hanging down AND outward (following the
    surface below the edge instead of cutting into the wood). Drips never reach below min_z."""
    rnd = random.Random(seed + 101)
    me = mo.data
    edges = {}
    for p in me.polygons:
        vs = list(p.vertices)
        for j in range(len(vs)):
            a, b = vs[j], vs[(j + 1) % len(vs)]
            key = (min(a, b), max(a, b))
            edges.setdefault(key, []).append(p.index)
    for (a, b), fl in edges.items():
        if len(fl) != 1 or rnd.random() > chance:
            continue
        A, B = me.vertices[a].co.copy(), me.vertices[b].co.copy()
        L = (B - A).length
        if L < .08:
            continue
        pc = me.polygons[fl[0]].center
        mid = (A + B) / 2
        out = mid - pc
        e = (B - A).normalized()
        out = out - e * out.dot(e)
        out.z = 0
        if out.length < 1e-6:
            continue
        out.normalize()
        hang = (V((0, 0, -1)) + out * out_k).normalized()
        h = min(hmax, L * rnd.uniform(.35, .7))
        if mid.z + hang.z * h < min_z:
            h = (mid.z - min_z) / max(-hang.z, .1)
            if h < .05:
                continue
        R = (B - A) / 2
        if (-hang).cross(R).dot(out) < 0:
            R = -R
        pl.card(mid + out * .012, R, -hang * h, 'moss_drip_%d' % rnd.randint(0, 1), hang=True,
                segs=1, wind_flutter=.01)


D = math.radians


def trunk(bark, props, base, top, r_bot, r_top, sides=7, seed=0, n=6, crown=(0.03, 0.35, 2), flare=1.35,
          lean_mid=(0, 0), cap_bottom=True, chamfer=True, flat_top=False, grain='end_grain_0', v0=0.0,
          buttress=None):
    """Upright crooked stump trunk with flared foot and broken (or flat) top. Returns (rings, frames, top centre)."""
    rnd = random.Random(seed)
    B, T = V(base), V(top)
    path = []
    for i in range(n):
        f = i / (n - 1)
        p = B.lerp(T, f)
        p += V((lean_mid[0], lean_mid[1], 0)) * math.sin(f * math.pi)
        if 0 < i < n - 1:
            p += V((rnd.uniform(-.04, .04), rnd.uniform(-.04, .04), 0))
        path.append(p)
    base_p = prof(sides, 1, 1, jit=.14, seed=seed, rot=rnd.uniform(0, 1), ajit=.25)
    profs = []
    for i in range(n):
        f = i / (n - 1)
        r = r_bot + (r_top - r_bot) * f
        if i == 0:
            r *= flare
        jp = prof(sides, 1, 1, jit=.06, seed=seed + 7 * i)
        profs.append([((x + jx * .3) * r, (y + jy * .3) * r) for (x, y), (jx, jy) in zip(base_p, jp)])
    if buttress:
        flare_profiles(profs, buttress[0], buttress[1])
    if chamfer:
        # inset ring on top -> slanted rim faces get moss
        path.append(path[-1] + (path[-1] - path[-2]).normalized() * r_top * .25)
        profs.append(scale_prof(profs[-1], .78))
    N = sides
    last = len(path) - 1
    hts = [0.0] * N if flat_top else jag(N, crown[0], crown[1], seed + 3, spikes=crown[2])
    rings, F = ptube(bark, path, profs, offs=crown_offsets(N, hts, last), v0=v0)
    if flat_top:
        poly_cap(props, rings[-1], F[-1][2], region=grain)
        C = sum(rings[-1], V()) / N
    else:
        C = broken_cap(props, rings[-1], F[-1][2], depth=0.04, region=grain)
    if cap_bottom:
        poly_cap(bark, rings[0], -F[0][2])
    return rings, F, C


