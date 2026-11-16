"""Helpers for the supplies/brood assets (AN21-AN35). Builds on the kit (ak.py) without changing it.
Atlas (Props) parts get explicit UVs fitted into one region; tiling materials use ak.auto_uv or metric UVs."""
import sys, os, math, random, json
sys.path.insert(0, '/home/claude/ant/kit')
import ak
import bpy, bmesh
from mathutils import Vector, Matrix

SHEETS = '/home/claude/ant/concepts/environment-pack-v3/asset-concepts-v1/sheets'


def pp(x):
    """ping-pong fold into [0,1] (mirrored repeats keep atlas UVs inside a region)."""
    x = x % 2.0
    return 2.0 - x if x > 1.0 else x


def R(region, u, v, fold=True):
    u0, v0, u1, v1 = ak.Mesh.region(region)
    if fold:
        u, v = pp(u), pp(v)
    else:
        u, v = min(max(u, 0), 1), min(max(v, 0), 1)
    return (u0 + u * (u1 - u0), v0 + v * (v1 - v0))


# ------------------------------------------------------------------ frames / rings
def frames(path, up_hint=None):
    P = [Vector(p) for p in path]
    n = len(P)
    out = []
    prev_x = None
    for i in range(n):
        t = (P[min(i + 1, n - 1)] - P[max(i - 1, 0)]).normalized()
        if prev_x is None:
            x = Vector(up_hint) if up_hint else (Vector((0, 0, 1)) if abs(t.z) < .9 else Vector((1, 0, 0)))
        else:
            x = prev_x
        x = (x - t * x.dot(t)).normalized()
        prev_x = x
        out.append((P[i], x, t.cross(x), t))
    return out


def rings_along(path, radii, sides, rot=0.0, scale=None, rfun=None, up_hint=None, seed=0, jit=0.0):
    """Rings around a path. radii float or list. scale: per-ring (sx, sy). rfun(i, k, a) -> radial multiplier.
    jit: random xyz jitter (metres)."""
    rnd = random.Random(seed)
    F = frames(path, up_hint)
    n = len(F)
    if not isinstance(radii, (list, tuple)):
        radii = [radii] * n
    rings = []
    for i, (P, x, y, t) in enumerate(F):
        sx, sy = scale[i] if scale else (1, 1)
        ring = []
        for k in range(sides):
            a = rot + 2 * math.pi * k / sides
            m = rfun(i, k, a) if rfun else 1.0
            p = P + (x * math.cos(a) * sx + y * math.sin(a) * sy) * radii[i] * m
            if jit:
                p = p + Vector((rnd.uniform(-jit, jit), rnd.uniform(-jit, jit), rnd.uniform(-jit, jit)))
            ring.append(p)
        rings.append(ring)
    return rings


def skin(mesh, rings, uvf, flip=False):
    """Quad skin between consecutive rings (closed around). uvf(i, k) -> uv, k in 0..sides (seam column)."""
    n = len(rings); s = len(rings[0])
    for i in range(n - 1):
        for k in range(s):
            k2 = k + 1
            a, b, c, d = rings[i][k], rings[i][k2 % s], rings[i + 1][k2 % s], rings[i + 1][k]
            ua, ub, uc, ud = uvf(i, k), uvf(i, k2), uvf(i + 1, k2), uvf(i + 1, k)
            if flip:
                mesh.quad(d, c, b, a, ud, uc, ub, ua)
            else:
                mesh.quad(a, b, c, d, ua, ub, uc, ud)


def cap(mesh, ring, region, flip=False, apex=None, rad_uv=0.5):
    """Close a ring: planar n-gon (apex None) or a fan to apex. UVs = disc inside region."""
    s = len(ring)
    ring = list(ring)
    c = sum(ring, Vector()) / s
    uvs = []
    for k in range(s):
        a = 2 * math.pi * k / s
        uvs.append(R(region, .5 + math.cos(a) * rad_uv * .98, .5 + math.sin(a) * rad_uv * .98, fold=False))
    if apex is None:
        if flip:
            mesh.face(list(reversed(ring)), list(reversed(uvs)))
        else:
            mesh.face(ring, uvs)
        return
    cu = R(region, .5, .5, fold=False)
    for k in range(s):
        a, b = ring[k], ring[(k + 1) % s]
        if flip:
            mesh.tri(b, a, Vector(apex), uvs[(k + 1) % s], uvs[k], cu)
        else:
            mesh.tri(a, b, Vector(apex), uvs[k], uvs[(k + 1) % s], cu)


def atlas_tube(mesh, path, radii, region, sides=5, metres_u=0.6, rep_v=1.0, rot=0.0, caps=(True, True),
               jit=0.0, seed=0, up_hint=None, scale=None, rfun=None):
    """Tube on an atlas region: u runs ALONG the path (metres_u metres per region width, ping-pong),
    v runs around (rep_v repeats). Good for rope (rope region stripes) and stems."""
    rings = rings_along(path, radii, sides, rot=rot, scale=scale, rfun=rfun, up_hint=up_hint, seed=seed, jit=jit)
    P = [Vector(p) for p in path]
    L = [0.0]
    for i in range(1, len(P)):
        L.append(L[-1] + (P[i] - P[i - 1]).length)
    skin(mesh, rings, lambda i, k: R(region, L[i] / metres_u, k / sides * rep_v))
    if caps[0]:
        cap(mesh, rings[0], region, flip=True, rad_uv=.2)
    if caps[1]:
        cap(mesh, rings[-1], region, rad_uv=.2)
    return rings


def closed_loop_tube(mesh, loop, radius, region, sides=5, metres_u=0.6, seed=0, jit=0.0, up_hint=None):
    """Tube following a CLOSED loop of points (rope band). Ends join seamlessly."""
    pts = list(loop) + [loop[0], loop[1]]
    rings = rings_along(pts, radius, sides, seed=seed, jit=jit, up_hint=up_hint)
    rings = rings[:-1]
    rings[-1] = rings[0]
    P = [Vector(p) for p in pts[:-1]]
    L = [0.0]
    for i in range(1, len(P)):
        L.append(L[-1] + (P[i] - P[i - 1]).length)
    skin(mesh, rings, lambda i, k: R(region, L[i] / metres_u, k / sides))
    return rings


def planar_uv_fit(mesh_pts_faces, region, axis_u, axis_v, lo_u, size_u, lo_v, size_v):
    pass


# ------------------------------------------------------------------ face list emitter
def emit(mesh, faces, uvf):
    """faces: list of point lists; uvf(points, normal) -> list of uvs"""
    for pts in faces:
        pts = [Vector(p) for p in pts]
        n = Vector()
        c = sum(pts, Vector()) / len(pts)
        for i in range(len(pts)):
            n += (pts[i] - c).cross(pts[(i + 1) % len(pts)] - c)
        if n.length > 0:
            n.normalize()
        mesh.face(pts, uvf(pts, n))


def box_uv_fit(region, lo, size):
    """uv function: per-face box projection, whole object fitted into the region (no repeats)."""
    lo = Vector(lo); size = Vector(size)
    def f(pts, n):
        out = []
        for p in pts:
            d = p - lo
            if abs(n.z) >= max(abs(n.x), abs(n.y)):
                u, v = d.x / size.x, d.y / size.y
            elif abs(n.x) > abs(n.y):
                u, v = d.y / size.y, d.z / size.z
            else:
                u, v = d.x / size.x, d.z / size.z
            out.append(R(region, u, v, fold=False))
        return out
    return f


# ------------------------------------------------------------------ objects
def finish(mesh, name, auto=False):
    o = mesh.to_object(name)
    if auto:
        ak.auto_uv(o)
    return o


def check_normals(objs):
    """Count faces whose normal points towards the object's bbox centre on closed parts (info only)."""
    bad = 0
    for o in objs:
        me = o.data
        bm = bmesh.new(); bm.from_mesh(me)
        bm.edges.ensure_lookup_table()
        nonman = sum(1 for e in bm.edges if not e.is_manifold)
        bm.free()
        print('  %s: %d faces, %d non-manifold edges' % (o.name, len(me.polygons), nonman))


def concept_strip(nn_slug, out):
    from PIL import Image
    im = Image.open(os.path.join(SHEETS, nn_slug + '.png')).convert('RGB')
    w, h = im.size
    q = [im.crop((0, 0, w // 2, h // 2)), im.crop((w // 2, 0, w, h // 2)),
         im.crop((0, h // 2, w // 2, h)), im.crop((w // 2, h // 2, w, h))]
    s = Image.new('RGB', (w * 2, h // 2))
    for i, c in enumerate(q):
        s.paste(c, (i * (w // 2), 0))
    s.save(out)
    return out


VIEWS = [(-45, 'FL'), (45, 'FR'), (135, 'RR'), (-135, 'RL')]


def renders(objs, outdir, tag, el=20, size=(360, 360), samples=8, views=VIEWS, **kw):
    tiles, labels = [], []
    for az, lab in views:
        p = os.path.join(outdir, '%s_%s.png' % (tag, lab))
        ak.render(objs, p, az=az, el=el, size=size, samples=samples, **kw)
        tiles.append(p); labels.append('%s %s az%d' % (tag, lab, az))
    return tiles, labels


def card(nn_slug, outdir, tiles, labels, title):
    strip = concept_strip(nn_slug, os.path.join(outdir, 'concept_strip.jpg'))
    ak.sheet(tiles, os.path.join(outdir, 'card.jpg'), cols=4, labels=labels, concept=strip, title=title)


def write_manifest(outdir, asset, records, **extra):
    d = {'asset': asset}
    d.update(extra)
    d['variants'] = records
    json.dump(d, open(os.path.join(outdir, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)


# ------------------------------------------------------------------ leaves
def leaf(mesh, M, L=1.0, W=0.6, thick=0.06, belly=0.35, seed=0, region='leaf_green', droop=0.0, curl=0.0,
         n=8, serr=0.12, bend=None, tip_twist=0.0, tears=0):
    """Solid lens-shaped leaf (closed, knife edge) along local X (length L), width W (local Y), domed top.
    droop: tips drop (m); curl: edges rise (m); bend(p, s, f) -> extra offset Vector (local);
    M: Matrix to place it. UV: midrib along region V (leaf atlas regions are vertical leaves)."""
    rnd = random.Random(seed)
    F = [-1, -.5, 0, .5, 1]
    T, B = [], []
    def P(s, f, z):
        x = (s - .5) * L
        hw = W / 2 * math.sin(math.pi * min(max(s, 0), 1) ** .85) ** .8
        y = f * hw
        zz = z + droop * -((2 * s - 1) ** 2) + curl * f * f + tip_twist * (2 * s - 1) * f
        p = Vector((x, y, zz))
        if bend:
            p = p + bend(p, s, f)
        return p
    ej = [[1 + rnd.uniform(-serr, serr * .6) for r in range(2)] for j in range(n)]
    for t_ in range(tears):                       # torn bites out of the edge
        ej[rnd.randint(2, n - 3)][rnd.randint(0, 1)] = rnd.uniform(.35, .6)
    for j in range(1, n - 1):
        s = j / (n - 1)
        th = thick * math.sin(math.pi * s) ** .6
        trow, brow = [], []
        for r, f in enumerate(F):
            fe = f
            if r in (0, 4):
                fe = f * ej[j][0 if r == 0 else 1]
            if r in (0, 4):
                pt = P(s + rnd.uniform(-.02, .02), fe, 0.0)
                trow.append(pt); brow.append(pt)
            else:
                trow.append(P(s, fe, th * (1 - .8 * f * f) - (0.006 if f == 0 else 0)))
                brow.append(P(s, fe, -th * belly * (1 - f * f)))
        T.append(trow); B.append(brow)
    tipA = P(0, 0, 0); tipB = P(1, 0, rnd.uniform(-.01, .01))
    T = [[M @ p for p in row] for row in T]; B = [[M @ p for p in row] for row in B]
    tipA, tipB = M @ tipA, M @ tipB
    def uv(j, r):           # j: 0..n-1 column (0, n-1 = tips)
        return R(region, (F[r] + 1) / 2 * .94 + .03, j / (n - 1), fold=False)
    m = len(T)
    for j in range(m - 1):
        for r in range(4):
            a, b, c, d = T[j][r], T[j + 1][r], T[j + 1][r + 1], T[j][r + 1]
            mesh.quad(a, b, c, d, uv(j + 1, r), uv(j + 2, r), uv(j + 2, r + 1), uv(j + 1, r + 1))
            a, b, c, d = B[j][r], B[j][r + 1], B[j + 1][r + 1], B[j + 1][r]
            mesh.quad(a, b, c, d, uv(j + 1, r), uv(j + 1, r + 1), uv(j + 2, r + 1), uv(j + 2, r))
    tu0 = R(region, .5, 0.0, fold=False); tu1 = R(region, .5, 1.0, fold=False)
    for r in range(4):
        mesh.tri(tipA, T[0][r], T[0][r + 1], tu0, uv(1, r), uv(1, r + 1))
        mesh.tri(tipA, B[0][r + 1], B[0][r], tu0, uv(1, r + 1), uv(1, r))
        mesh.tri(tipB, T[-1][r + 1], T[-1][r], tu1, uv(n - 2, r + 1), uv(n - 2, r))
        mesh.tri(tipB, B[-1][r], B[-1][r + 1], tu1, uv(n - 2, r), uv(n - 2, r + 1))
    return T, B


def place(x, y, z, rz=0.0, rx=0.0, ry=0.0):
    return (Matrix.Translation((x, y, z)) @ Matrix.Rotation(rz, 4, 'Z') @ Matrix.Rotation(ry, 4, 'Y')
            @ Matrix.Rotation(rx, 4, 'X'))


def rope_knot(mesh, c, r=0.022, size=0.05, seed=0, axis=(0, 0, 1), ends=()):
    """Lumpy knot: two small crossing rope loops around point c (+ optional loose ends: list of point lists)."""
    rnd = random.Random(seed)
    C = Vector(c); A = Vector(axis).normalized()
    t1 = A.orthogonal().normalized(); t2 = A.cross(t1)
    for q, (u, v) in enumerate(((t1, A), (t2, A + t1 * .4))):
        v = v.normalized()
        pts = []
        for i in range(7):
            a = 2 * math.pi * i / 7 + q
            s = size * (1 + rnd.uniform(-.15, .15))
            pts.append(C + u * math.cos(a) * s + v * math.sin(a) * s * .7 + A * size * .3)
        closed_loop_tube(mesh, pts, r, 'rope', sides=5, metres_u=.3, seed=seed + q)
    for e in ends:
        n = len(e)
        atlas_tube(mesh, e, [r * (1 - .3 * i / max(n - 1, 1)) for i in range(n)], 'rope', sides=5, metres_u=.3,
                   caps=(False, True))


def face_out(mesh, pts, out_dir, uvf):
    """Emit polygon, flipping it if its normal points against out_dir."""
    pts = [Vector(p) for p in pts]
    c = sum(pts, Vector()) / len(pts)
    n = Vector()
    for i in range(len(pts)):
        n += (pts[i] - c).cross(pts[(i + 1) % len(pts)] - c)
    if n.dot(Vector(out_dir)) < 0:
        pts = list(reversed(pts)); n = -n
    if n.length:
        n.normalize()
    return mesh.face(pts, uvf(pts, n))


def hjit(p, amp, seed=0):
    """deterministic jitter from a position (shared vertices move identically)."""
    k = (round(p[0], 4), round(p[1], 4), round(p[2], 4), seed)
    r = random.Random(hash(k))
    return Vector((r.uniform(-amp, amp), r.uniform(-amp, amp), r.uniform(-amp, amp)))


# ================================================================== detail kit (v2 detail pass)
def lump(mesh, c, r, region=None, sides=5, seed=0, rot=None, flat=0.0, jit=0.22, M=None):
    """Crude faceted lump (clod, pebble, crumb, kernel): 2 rings + poles = 4*sides tris.
    r=(rx,ry,rz) or float. region None -> tiling material (call auto_uv on the object later).
    flat: 0..1 flattens the bottom (sits on the ground). M: optional rotation matrix (3x3/4x4) applied about c."""
    rnd = random.Random(seed)
    C = Vector(c)
    if not isinstance(r, (list, tuple)):
        r = (r, r, r)
    rx, ry, rz = r
    rot = rnd.uniform(0, 6.3) if rot is None else rot
    def T(p):
        if M is not None:
            p = (M.to_3x3() if len(M) == 4 else M) @ p
        return C + p
    rings = []
    for ri, lat in enumerate((-50.0, 12.0)):
        la = math.radians(lat + rnd.uniform(-8, 8))
        ring = []
        for k in range(sides):
            a = rot + 2 * math.pi * (k + .5 * ri) / sides
            f = 1 + rnd.uniform(-jit, jit)
            z = math.sin(la) * rz * f
            if ri == 0 and flat:
                z = -rz * (1 - flat) * .55 + z * flat * 0.2
            ring.append(T(Vector((math.cos(a) * math.cos(la) * rx * f, math.sin(a) * math.cos(la) * ry * f, z))))
        rings.append(ring)
    bot = T(Vector((0, 0, -rz * (1 - flat) * (0.6 if flat else 1))))
    top = T(Vector((rnd.uniform(-.2, .2) * rx, rnd.uniform(-.2, .2) * ry, rz * (1 + rnd.uniform(-jit, jit) * .5))))
    def uv(k, v):
        return R(region, k / sides, v, fold=False) if region else (0.0, 0.0)
    r1, r2 = rings
    for k in range(sides):
        k2 = (k + 1) % sides
        mesh.tri(bot, r1[k2], r1[k], uv(k + .5, 0), uv(k + 1, .3), uv(k, .3))
        mesh.quad(r1[k], r1[k2], r2[k2], r2[k], uv(k, .3), uv(k + 1, .3), uv(k + 1, .75), uv(k, .75))
        mesh.tri(r2[k], r2[k2], top, uv(k, .75), uv(k + 1, .75), uv(k + .5, 1))
    return rings


def scatter(mesh, n, sampler, size, region=None, seed=0, sides=4, flat=0.6, zsq=0.6):
    """n lumps at sampler(rnd) -> (x, y, z) positions; size=(min, max) radius."""
    rnd = random.Random(seed)
    for i in range(n):
        p = sampler(rnd)
        if p is None:
            continue
        s = rnd.uniform(*size)
        lump(mesh, (p[0], p[1], p[2] + s * zsq * .35), (s * rnd.uniform(.8, 1.3), s * rnd.uniform(.7, 1.1), s * zsq),
             region, sides=sides, seed=seed * 1000 + i, flat=flat)


def ring_sampler(cx, cy, r0, r1, z=0.0, sy=1.0, a0=0.0, a1=2 * math.pi):
    def f(rnd):
        a = rnd.uniform(a0, a1); r = rnd.uniform(r0, r1)
        return (cx + math.cos(a) * r, cy + math.sin(a) * r * sy, z)
    return f


def twig(mesh, start, direction, length, r=0.012, seed=0, region='needles', branches=1, sides=4, segs=4, droop=0.0,
         ground=False):
    """Crooked twig (bark = needles region) with short side branches."""
    rnd = random.Random(seed)
    P = [Vector(start)]
    d = Vector(direction).normalized()
    for i in range(segs):
        d = (d + Vector((rnd.uniform(-.35, .35), rnd.uniform(-.35, .35), rnd.uniform(-.15, .15) - droop))).normalized()
        P.append(P[-1] + d * length / segs)
        if ground:
            P[-1].z = Vector(start).z + rnd.uniform(0, .02)
    radii = [r * (1 - .55 * i / segs) for i in range(segs + 1)]
    atlas_tube(mesh, P, radii, region, sides=sides, metres_u=.25, seed=seed, caps=(True, False))
    for b in range(branches):
        i = rnd.randint(1, max(1, segs - 1))
        bd = (d.cross(Vector((0, 0, 1))) * rnd.choice((-1, 1)) + d * .6).normalized()
        q = [P[i], P[i] + bd * length * .25, P[i] + bd * length * .42 + Vector((rnd.uniform(-.03, .03),) * 3)]
        if ground:
            for p in q[1:]:
                p.z = P[i].z + rnd.uniform(-.005, .01)
        atlas_tube(mesh, q, [radii[i] * .6, radii[i] * .45, radii[i] * .25], region, sides=3, metres_u=.25,
                   caps=(False, False))
    return P


def shard(mesh, c, size, region_out, region_in=None, seed=0, curv=0.25, th=0.01, M=None, n=5):
    """Curved shell fragment / flake: jagged n-gon plate, domed (curv * size), with thickness th."""
    rnd = random.Random(seed)
    region_in = region_in or region_out
    C = Vector(c)
    Mr = (M.to_3x3() if M is not None and len(M) == 4 else M) if M is not None else Matrix.Rotation(rnd.uniform(0, 6.3), 3, 'Z')
    out = []
    for k in range(n):
        a = 2 * math.pi * k / n + rnd.uniform(-.3, .3)
        rr = size * rnd.uniform(.55, 1.0)
        out.append(Vector((math.cos(a) * rr, math.sin(a) * rr * .8, 0)))
    for p in out:
        p.z = curv * size * (1 - (p.length / size) ** 2) * .4
    top_c = Vector((0, 0, curv * size * .4))
    T = [C + Mr @ p for p in out]; B = [C + Mr @ (p - Vector((0, 0, th))) for p in out]
    tc, bc = C + Mr @ top_c, C + Mr @ (top_c - Vector((0, 0, th)))
    for k in range(n):
        k2 = (k + 1) % n
        u0 = (.5 + .45 * math.cos(2 * math.pi * k / n), .5 + .45 * math.sin(2 * math.pi * k / n))
        u1 = (.5 + .45 * math.cos(2 * math.pi * k2 / n), .5 + .45 * math.sin(2 * math.pi * k2 / n))
        mesh.tri(T[k], T[k2], tc, R(region_out, *u0, fold=False), R(region_out, *u1, fold=False), R(region_out, .5, .5, fold=False))
        mesh.tri(B[k2], B[k], bc, R(region_in, *u1, fold=False), R(region_in, *u0, fold=False), R(region_in, .5, .5, fold=False))
        mesh.quad(B[k], B[k2], T[k2], T[k], R(region_out, u0[0], .02, 0), R(region_out, u1[0], .02, 0),
                  R(region_out, u1[0], .06, 0), R(region_out, u0[0], .06, 0))


def small_leaf(mesh, c, L, W, region='leaf_dry_0', seed=0, rz=None, rx=0.0, ry=0.0, droop=0.01, curl=0.01, thick=None):
    rnd = random.Random(seed)
    rz = rnd.uniform(0, 6.3) if rz is None else rz
    leaf(mesh, place(c[0], c[1], c[2], rz, rx=rx, ry=ry), L=L, W=W, thick=thick or max(W * .06, .006), belly=.5,
         seed=seed, region=region, droop=droop, curl=curl, serr=.18, n=6)


def fibres(mesh, start, direction, n=3, length=0.06, r=0.004, seed=0, region='rope', spread=0.5):
    """Frayed fibres splaying from a rope/cloth end: thin 3-sided tubes."""
    rnd = random.Random(seed)
    d0 = Vector(direction).normalized()
    for i in range(n):
        d = (d0 + Vector((rnd.uniform(-spread, spread), rnd.uniform(-spread, spread), rnd.uniform(-spread, spread) - .2))).normalized()
        L = length * rnd.uniform(.6, 1.2)
        p0 = Vector(start); p1 = p0 + d * L * .5; p2 = p1 + (d + Vector((0, 0, -.4))).normalized() * L * .5
        atlas_tube(mesh, [p0, p1, p2], [r, r * .8, r * .4], region, sides=3, metres_u=.2, caps=(False, False))


def burrow(props, c, normal, r=0.05, depth=0.12, sides=6, seed=0):
    """Tiny burrow hole: a short dark tube sunk into a surface (normals face into the hole), closed at the back."""
    rnd = random.Random(seed)
    N = Vector(normal).normalized(); C = Vector(c)
    path = [C + N * 0.015, C - N * depth * .5, C - N * depth]
    rings = rings_along(path, [r, r * .85, r * .6], sides, rot=rnd.uniform(0, 3), seed=seed, jit=r * .12)
    skin(props, rings, lambda i, k: R('dark', k / sides, i / 2, fold=False), flip=True)
    cap(props, rings[-1], 'dark', flip=True)


def mushroom(props, base, h=0.08, cap_r=0.035, seed=0, lean=(0, 0), cap_region='cap_mint', stem_region='stem'):
    """Tiny non-glowing baby mushroom (stem tube + cap cone)."""
    rnd = random.Random(seed)
    B = Vector(base); top = B + Vector((lean[0] * h, lean[1] * h, h))
    atlas_tube(props, [B - Vector((0, 0, .01)), B.lerp(top, .5) + Vector((rnd.uniform(-.01, .01), rnd.uniform(-.01, .01), 0)), top],
               [cap_r * .32, cap_r * .28, cap_r * .25], stem_region, sides=4, metres_u=.2, caps=(False, False))
    ring = []
    s = 6
    for k in range(s):
        a = 2 * math.pi * k / s
        ring.append(top + Vector((math.cos(a) * cap_r, math.sin(a) * cap_r, -cap_r * .25 + rnd.uniform(-.005, .005))))
    cap(props, ring, cap_region, apex=top + Vector((0, 0, cap_r * .7)), rad_uv=.45)
    cap(props, ring, cap_region, flip=True, rad_uv=.2)


def merge_objects(objs, name):
    """Join several objects of the SAME material into one (bmesh)."""
    bm = bmesh.new()
    for o in objs:
        bm.from_mesh(o.data)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for p in me.polygons:
        p.use_smooth = False
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    me.materials.append(ak.material(objs[0]['mat'])); ob['mat'] = objs[0]['mat']
    for o in objs:
        bpy.data.objects.remove(o)
    return ob


def seed_body(mesh, M, L=0.55, H=0.088, Wd=0.122, sides=12, nring=9, seed=4, gash=None, chips=0):
    """Ribbed pointed seed along local X (keel on top). gash=(t0, t1, k): open split along side index k between
    t0..t1 (deep groove). chips: number of random dented facets. Returns rings (local->M applied)."""
    rnd = random.Random(seed)
    ts = [0.035 + (0.955 - 0.035) * i / (nring - 1) for i in range(nring)]
    path = [(-L / 2 + t * L, 0.006 * math.sin(t * 5), 0.018 * math.sin(math.pi * t) - 0.006 * t) for t in ts]
    prof = [math.sin(math.pi * t) ** 0.62 for t in ts]
    base = [1.13, .95, 1.05, 1.0, .88, .97, .93, .97, .88, 1.0, 1.05, .95]
    if sides != 12:
        base = [base[int(k * 12 / sides)] for k in range(sides)]
    J = [[rnd.uniform(-.03, .03) for k in range(sides)] for t in ts]
    dents = set((rnd.randint(1, nring - 2), rnd.randint(0, sides - 1)) for c in range(chips))
    def rf(i, k, a):
        m = base[k] + J[i][k]
        if k == sides // 2:
            m *= .9
        if (i, k) in dents:
            m *= .8
        if gash and gash[0] <= ts[i] <= gash[1]:
            kk = gash[2]
            o = math.sin(math.pi * (ts[i] - gash[0]) / (gash[1] - gash[0]))
            if k == kk:
                m *= 1 - .5 * o
            elif k in ((kk - 1) % sides, (kk + 1) % sides):
                m *= 1 + .1 * o
        return m
    rings = rings_along(path, prof, sides, scale=[(H, Wd)] * nring, rfun=rf, up_hint=(0, 0, 1))
    rings = [[M @ p for p in r] for r in rings]
    skin(mesh, rings, lambda i, k: R('seed', k / sides, ts[i], fold=False))
    tipA = M @ Vector((-L / 2 - 0.004, 0.004, 0.004)); tipB = M @ Vector((L / 2, -0.004, 0.012))
    first, last = rings[0], rings[-1]
    for k in range(sides):
        k2 = (k + 1) % sides
        mesh.tri(first[k2], first[k], tipA, R('seed', (k + 1) / sides, ts[0], 0), R('seed', k / sides, ts[0], 0), R('seed', (k + .5) / sides, 0, 0))
        mesh.tri(last[k], last[k2], tipB, R('seed', k / sides, ts[-1], 0), R('seed', (k + 1) / sides, ts[-1], 0), R('seed', (k + .5) / sides, 1, 0))
    return rings, ts
