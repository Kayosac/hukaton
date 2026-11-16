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
         n=8, serr=0.12, bend=None, tip_twist=0.0):
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
