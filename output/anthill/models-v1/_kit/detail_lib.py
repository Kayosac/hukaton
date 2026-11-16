"""Small modelled details for the earth shell pieces (pebbles, clods, crumbs, roots, rootlets, burrows, cracks,
drips, ridges, husks, needles, ruts). All write into a shell_lib.Solid. Every detail is placed tentatively and
rolled back if its bounding box breaks the piece's `forbid(lo, hi)` rule (join planes, clear passages, walk zones).
"""
import math, random
from mathutils import Vector
from shell_lib import Solid, ak

Z = Vector((0, 0, 1))


def basis(n, rnd=None, rot=None):
    n = Vector(n).normalized()
    h = Z if abs(n.z) < 0.9 else Vector((1, 0, 0))
    t1 = h.cross(n).normalized()
    t2 = n.cross(t1)
    a = rot if rot is not None else (rnd.uniform(0, 6.283) if rnd else 0.0)
    c, s = math.cos(a), math.sin(a)
    return t1 * c + t2 * s, -t1 * s + t2 * c, n


def region_uv(region):
    return ak.Mesh.region(region)


# ------------------------------------------------------------------ closed lumps
def ell(S, C, axes, radii, rnd, mat='Earth', region=None, sides=6, rings=3, jitter=0.2, clamp=None):
    """Oriented faceted ellipsoid. axes = (a1, a2, a3) unit vectors, radii along them. region -> Props atlas UVs.
    clamp: (point, normal) plane; vertices are pushed above it (flat bottom on the floor/face)."""
    C = Vector(C); a1, a2, a3 = axes; r1, r2, r3 = radii
    rings_pts = []
    for i in range(1, rings):
        lat = -math.pi / 2 + math.pi * i / rings
        ring = []
        for k in range(sides):
            a = 2 * math.pi * (k + 0.5 * (i % 2)) / sides
            f = 1 + rnd.uniform(-jitter, jitter)
            ring.append(C + (a1 * math.cos(a) * r1 + a2 * math.sin(a) * r2) * math.cos(lat) * f + a3 * math.sin(lat) * r3 * f)
        rings_pts.append(ring)
    top = C + a3 * r3 * rnd.uniform(0.8, 1.05) + a1 * r1 * rnd.uniform(-.2, .2)
    bot = C - a3 * r3 * rnd.uniform(0.8, 1.05)
    if clamp:
        P0, N0 = clamp
        def cl(p):
            d = (p - P0).dot(N0)
            return p - N0 * d if d < 0 else p
        rings_pts = [[cl(p) for p in r] for r in rings_pts]; top = cl(top); bot = cl(bot)
    if region:
        u0, v0, u1, v1 = region_uv(region)
        R = max(r1, r2, r3)
        def uv(p):
            d = p - C
            return (u0 + (0.5 + 0.5 * d.dot(a1) / R) * (u1 - u0), v0 + (0.5 + 0.5 * d.dot(a2 if abs(d.dot(a3)) < R * 0.7 else a3) / R) * (v1 - v0))
    m = 'Props' if region else mat
    def F(pts):
        S.face(pts, m, [uv(p) for p in pts] if region else None)
    for i in range(len(rings_pts) - 1):
        for k in range(sides):
            k2 = (k + 1) % sides
            F([rings_pts[i][k], rings_pts[i][k2], rings_pts[i + 1][k2], rings_pts[i + 1][k]])
    for k in range(sides):
        k2 = (k + 1) % sides
        F([rings_pts[-1][k], rings_pts[-1][k2], top])
        F([rings_pts[0][k2], rings_pts[0][k], bot])


def pebble(S, p, n, rnd, r=None, embed=0.45, region='pebble', flat=None):
    r = r or rnd.uniform(0.03, 0.09)
    a1, a2, a3 = basis(n, rnd)
    fl = flat if flat is not None else rnd.uniform(0.45, 0.75)
    rz = r * fl
    ell(S, Vector(p) + a3 * rz * (1 - 2 * embed), (a1, a2, a3), (r * rnd.uniform(0.8, 1.2), r * rnd.uniform(0.6, 1.0), rz),
        rnd, region=region, sides=rnd.choice((5, 6, 7)), jitter=0.18)


def clod(S, p, n, rnd, r=None, embed=0.4, mat='Earth', flat=None, sides=None):
    r = r or rnd.uniform(0.07, 0.2)
    a1, a2, a3 = basis(n, rnd)
    fl = flat if flat is not None else rnd.uniform(0.5, 0.85)
    rz = r * fl
    ell(S, Vector(p) + a3 * rz * (1 - 2 * embed), (a1, a2, a3), (r * rnd.uniform(0.85, 1.3), r * rnd.uniform(0.6, 1.0), rz),
        rnd, mat=mat, sides=sides or rnd.choice((5, 6, 7)), jitter=0.25)


def crumb_flat(S, p, n, rnd, r=None, h=None, mat='Earth', region=None):
    """Flat crumb lying on a walk surface (height <= h)."""
    r = r or rnd.uniform(0.03, 0.08)
    h = h or rnd.uniform(0.012, 0.03)
    a1, a2, a3 = basis(n, rnd)
    ell(S, Vector(p) + a3 * h * 0.35, (a1, a2, a3), (r * rnd.uniform(0.8, 1.3), r * rnd.uniform(0.5, 0.9), h * 0.7),
        rnd, mat=mat, region=region, sides=5, jitter=0.2, clamp=(Vector(p) - Vector(n) * 0.01, Vector(n).normalized()))


def ledge_slab(S, p, n, rnd, length=None):
    """Crumbling horizontal ledge: a flat slab sticking out of a face + 1-2 broken pieces under it."""
    L = length or rnd.uniform(0.3, 0.7)
    nh = Vector((n.x, n.y, 0))
    if nh.length < 0.3:
        return
    nh.normalize()
    side = Z.cross(nh).normalized()
    c = Vector(p) + nh * 0.04
    ell(S, c, (side, nh, Z), (L / 2, rnd.uniform(0.1, 0.17), rnd.uniform(0.04, 0.07)), rnd, sides=6, jitter=0.2)
    for _ in range(rnd.randint(1, 2)):
        q = c + side * rnd.uniform(-L / 3, L / 3) - Z * rnd.uniform(0.08, 0.16) + nh * 0.02
        ell(S, q, (side, nh, Z), (rnd.uniform(0.04, 0.09), rnd.uniform(0.04, 0.08), rnd.uniform(0.03, 0.06)), rnd, sides=5, jitter=0.25)


# ------------------------------------------------------------------ tubes (roots)
def tube(S, path, radii, sides, mat='Root', tile=1.6):
    P = [Vector(p) for p in path]
    n = len(P)
    L = [0.0]
    for i in range(1, n):
        L.append(L[-1] + (P[i] - P[i - 1]).length)
    rings = []
    prev = None
    for i in range(n):
        t = (P[min(i + 1, n - 1)] - P[max(i - 1, 0)]).normalized()
        x = prev if prev is not None else (Z if abs(t.z) < 0.9 else Vector((1, 0, 0)))
        x = (x - t * x.dot(t)).normalized(); prev = x
        y = t.cross(x)
        rings.append([P[i] + (x * math.cos(2 * math.pi * k / sides) + y * math.sin(2 * math.pi * k / sides)) * radii[i]
                      for k in range(sides)])
    circ = 2 * math.pi * max(radii)
    for i in range(n - 1):
        for k in range(sides):
            k2 = (k + 1) % sides
            u0, u1 = k / sides * circ / tile, (k + 1) / sides * circ / tile
            v0, v1 = L[i] / tile, L[i + 1] / tile
            S.quad(rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k], mat, [(u0, v0), (u1, v0), (u1, v1), (u0, v1)])
    for ring, rev in ((rings[0], True), (rings[-1], False)):
        r = list(reversed(ring)) if rev else ring
        S.face(r, mat, [(0.1 * math.cos(2 * math.pi * k / sides), 0.1 * math.sin(2 * math.pi * k / sides)) for k in range(sides)])


def root_tip(S, p, n, rnd, length=None, r0=None):
    """Root poking out of a face: starts buried, comes out along the normal and sags down, tapering to a tip."""
    L = length or rnd.uniform(0.25, 0.6)
    r0 = r0 or rnd.uniform(0.025, 0.05)
    n = Vector(n).normalized()
    a1, a2, _ = basis(n, rnd)
    pts = [Vector(p) - n * 0.08]
    d = (n + a1 * rnd.uniform(-0.4, 0.4)).normalized()
    q = Vector(p)
    pts.append(q.copy())
    for i in range(3):
        d = (d + Vector((rnd.uniform(-.3, .3), rnd.uniform(-.3, .3), -0.45 - 0.2 * i))).normalized()
        q = q + d * L / 3
        pts.append(q.copy())
    radii = [r0, r0 * 0.9, r0 * 0.65, r0 * 0.38, r0 * 0.1]
    tube(S, pts, radii, 5)
    # a hair rootlet or two off the root
    for _ in range(rnd.randint(0, 2)):
        i = rnd.randint(1, 3)
        rootlet(S, pts[i], rnd, length=rnd.uniform(0.08, 0.22), r=r0 * 0.25)


def rootlet(S, p, rnd, length=None, r=None, down=1.0):
    """Hair rootlet hanging from p (thin crooked 3-sided tube)."""
    L = length or rnd.uniform(0.15, 0.55)
    r = r or rnd.uniform(0.006, 0.014)
    q = Vector(p) + Z * 0.03
    pts = [q.copy()]
    segs = 4
    for i in range(segs):
        q = q + Vector((rnd.uniform(-.25, .25), rnd.uniform(-.25, .25), -down)).normalized() * (L / segs + (0.03 if i == 0 else 0))
        pts.append(q.copy())
    tube(S, pts, [r, r * 0.85, r * 0.65, r * 0.45, r * 0.15], 3)


# ------------------------------------------------------------------ holes / cracks / drips
def burrow(S, p, n, rnd, r=None):
    """Tiny ant burrow: dark funnel into the face + crumb rim."""
    r = r or rnd.uniform(0.045, 0.09)
    a1, a2, a3 = basis(n, rnd)
    c = Vector(p) + a3 * 0.012
    k = 7
    ring = [c + (a1 * math.cos(2 * math.pi * i / k) * rnd.uniform(0.85, 1.15) + a2 * math.sin(2 * math.pi * i / k) * rnd.uniform(0.75, 1.1)) * r for i in range(k)]
    apex = c - a3 * r * 2.2 + a1 * r * 0.3
    u0, v0, u1, v1 = region_uv('dark')
    for i in range(k):
        a, b = ring[i], ring[(i + 1) % k]
        S.face([a, b, apex], 'Props', [(u0, v0), (u1, v0), ((u0 + u1) / 2, v1)], nrm=a3)
    # rim lip: little crumbs around
    for i in range(k):
        if rnd.random() < 0.8:
            q = ring[i] + (ring[i] - c).normalized() * r * 0.25
            ell(S, q, (a1, a2, a3), (r * rnd.uniform(0.35, 0.6), r * rnd.uniform(0.3, 0.5), r * rnd.uniform(0.25, 0.45)), rnd, sides=5, jitter=0.2)


def crack(S, p, n, rnd, length=None, width=None, depth=None, along=None, lips=True):
    """V-groove crack (dark inside) along the face tangent, with clay lips."""
    L = length or rnd.uniform(0.3, 0.8)
    w = width or rnd.uniform(0.015, 0.035)
    dp = depth or rnd.uniform(0.04, 0.08)
    a1, a2, a3 = basis(n, rnd)
    if along is not None:
        a1 = (Vector(along) - a3 * Vector(along).dot(a3)).normalized(); a2 = a3.cross(a1)
    segs = 5
    q = Vector(p) - a1 * L / 2
    pts = []
    for i in range(segs + 1):
        pts.append(q.copy())
        q = q + (a1 + a2 * rnd.uniform(-0.5, 0.5)).normalized() * L / segs
    u0, v0, u1, v1 = region_uv('dark')
    for i in range(segs):
        wi = w * (math.sin(math.pi * (i + 0.5) / segs) * 0.8 + 0.2)
        wj = w * (math.sin(math.pi * (i + 1.5) / segs) * 0.8 + 0.2) if i < segs - 1 else 0.003
        wi0 = w * (math.sin(math.pi * (i - 0.5) / segs) * 0.8 + 0.2) if i > 0 else 0.003
        A, B = pts[i], pts[i + 1]
        o = a3 * 0.012
        La, Lb = A + a2 * wi0 + o, B + a2 * wi + o
        Ra, Rb = A - a2 * wi0 + o, B - a2 * wi + o
        Da, Db = A - a3 * dp, B - a3 * dp
        uv = [(u0, v1), (u1, v1), (u1, v0), (u0, v0)]
        S.face([La, Lb, Db, Da], 'Props', uv, nrm=a3 - a2 * 0.5)
        S.face([Rb, Ra, Da, Db], 'Props', uv, nrm=a3 + a2 * 0.5)
        if lips and rnd.random() < 0.85:
            for sgn in (1, -1):
                m = (A + B) / 2 + a2 * sgn * (wi + 0.018)
                ell(S, m, (a1, a2, a3), ((B - A).length * 0.55, rnd.uniform(0.015, 0.03), rnd.uniform(0.012, 0.022)), rnd, sides=4, jitter=0.15)


def drip(S, p, rnd, length=None, r=None):
    """Clay drip / erosion icicle hanging under an overhang."""
    L = length or rnd.uniform(0.08, 0.25)
    r = r or rnd.uniform(0.025, 0.05)
    a1, a2, _ = basis(Z, rnd)
    c = Vector(p) + Z * 0.04
    k = 5
    ring = [c + (a1 * math.cos(2 * math.pi * i / k) + a2 * math.sin(2 * math.pi * i / k)) * r * rnd.uniform(0.8, 1.2) for i in range(k)]
    mid = [Vector(p) - Z * L * 0.45 + (q - c) * 0.55 + Vector((rnd.uniform(-.01, .01), rnd.uniform(-.01, .01), 0)) for q in ring]
    tip = Vector(p) - Z * L + a1 * rnd.uniform(-0.02, 0.02)
    for i in range(k):
        j = (i + 1) % k
        S.quad(ring[j], ring[i], mid[i], mid[j], 'Earth')
        S.face([mid[j], mid[i], tip], 'Earth')
    S.face(ring, 'Earth')


def ridge(S, p, n, rnd, length=None, along=Z):
    """Erosion rib / groove edge running down a face."""
    L = length or rnd.uniform(0.35, 0.9)
    _, _, a3 = basis(n)
    a1 = (Vector(along) - a3 * Vector(along).dot(a3)).normalized()
    a2 = a3.cross(a1)
    ell(S, Vector(p), (a1, a2, a3), (L / 2, rnd.uniform(0.03, 0.06), rnd.uniform(0.025, 0.05)), rnd, sides=5, jitter=0.15)


def husk(S, p, n, rnd, region=None):
    region = region or rnd.choice(('seed', 'shell_out', 'shell_out', 'seed'))
    r = rnd.uniform(0.035, 0.07)
    a1, a2, a3 = basis(n, rnd)
    a3t = (a3 + a1 * rnd.uniform(-0.6, 0.6)).normalized()
    a1 = (a1 - a3t * a1.dot(a3t)).normalized(); a2 = a3t.cross(a1)
    ell(S, Vector(p) + a3 * r * 0.1, (a1, a2, a3t), (r, r * 0.6, r * 0.3), rnd, region=region, sides=6, jitter=0.12)


def needle(S, p, n, rnd, length=None):
    """Dry pine needle / twig bit lying on a surface (thin crooked box, Props needles)."""
    L = length or rnd.uniform(0.08, 0.22)
    a1, a2, a3 = basis(n, rnd)
    w, h = rnd.uniform(0.006, 0.012), rnd.uniform(0.005, 0.009)
    c = Vector(p) + a3 * h * 0.5
    bend = a2 * rnd.uniform(-0.02, 0.02)
    A0, A1 = c - a1 * L / 2, c + a1 * L / 2 + bend
    u0, v0, u1, v1 = region_uv('needles')
    pts = {}
    for i, A in enumerate((A0, A1)):
        for j, (sx, sz) in enumerate(((-1, -1), (1, -1), (1, 1), (-1, 1))):
            pts[(i, j)] = A + a2 * sx * w / 2 + a3 * sz * h / 2
    uv = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
    for j in range(4):
        j2 = (j + 1) % 4
        S.quad(pts[(0, j)], pts[(0, j2)], pts[(1, j2)], pts[(1, j)], 'Props', uv)
    S.face([pts[(0, 3)], pts[(0, 2)], pts[(0, 1)], pts[(0, 0)]], 'Props', uv)
    S.face([pts[(1, 0)], pts[(1, 1)], pts[(1, 2)], pts[(1, 3)]], 'Props', uv)


def rut(S, p, n, rnd, length=None, along=None, gauge=0.32):
    """Shallow rut track: two low trodden berms (Floor) with a groove between, <= 3 cm high."""
    L = length or rnd.uniform(1.0, 2.0)
    a1, a2, a3 = basis(n, rnd)
    if along is not None:
        a1 = (Vector(along) - a3 * Vector(along).dot(a3)).normalized(); a2 = a3.cross(a1)
    for sgn in (1, -1):
        for k in range(3):
            c = Vector(p) + a2 * sgn * gauge / 2 + a1 * (k - 1) * L / 3 + a2 * rnd.uniform(-0.03, 0.03)
            ell(S, c, (a1, a2, a3), (L / 6 * 1.15, rnd.uniform(0.04, 0.06), rnd.uniform(0.018, 0.028)), rnd, mat='Floor',
                sides=6, jitter=0.12, clamp=(Vector(p) - a3 * 0.004, a3))


# ------------------------------------------------------------------ sampling + scattering
class Surf:
    def __init__(self, solid, rnd):
        self.tris, self.tree = solid.surface()
        self.open_edges = solid.surf_open_edges
        self.rnd = rnd

    def sample(self, n, filt=None, mats=('Earth',)):
        cand = []
        for a, b, c, nr, m in self.tris:
            if m not in mats:
                continue
            area = (b - a).cross(c - a).length / 2
            if area < 1e-6:
                continue
            cand.append((area, a, b, c, nr))
        out = []
        if not cand:
            return out
        tot = sum(x[0] for x in cand)
        cum = []
        acc = 0
        for x in cand:
            acc += x[0]; cum.append(acc)
        import bisect
        tries = 0
        while len(out) < n and tries < n * 40:
            tries += 1
            i = bisect.bisect(cum, self.rnd.uniform(0, tot))
            _, a, b, c, nr = cand[min(i, len(cand) - 1)]
            u, v = self.rnd.random(), self.rnd.random()
            if u + v > 1:
                u, v = 1 - u, 1 - v
            p = a + (b - a) * u + (c - a) * v
            if filt is None or filt(p, nr):
                out.append((p, nr))
        return out

    def ray(self, origin, d, dist=10):
        hit = self.tree.ray_cast(Vector(origin), Vector(d).normalized(), dist)
        return hit[0], hit[1]


def place(S, forbid, fn, *a, **k):
    m = S.mark()
    fn(S, *a, **k)
    if forbid is not None and S.mark() > m:
        lo, hi = S.aabb(m)
        if forbid(lo, hi):
            S.rollback(m)
            return False
    return True


def scatter(S, surf, forbid, plan, rnd):
    """plan: list of (count, fn, filt, mats, kwargs_fn or None). fn(S, p, n, rnd, **kw)."""
    placed = {}
    for count, fn, filt, mats, kw in plan:
        pts = surf.sample(count, filt, mats)
        ok = 0
        for p, n in pts:
            kws = kw(p, n) if kw else {}
            if place(S, forbid, fn, p, n, rnd, **kws):
                ok += 1
        placed[fn.__name__] = placed.get(fn.__name__, 0) + ok
    return placed


def hang_root(S, p, n, rnd, **k):
    rootlet(S, p, rnd, **k)


def hang_drip(S, p, n, rnd, **k):
    drip(S, p, rnd, **k)


def foot_rubble(S, surf, forbid, rnd, rays, n_clods=14, n_crumbs=30, n_peb=24, n_husk=4):
    """Rubble/crumbs/pebbles heaped on z=0 at the foot of a face. rays: list of (origin, direction) horizontal."""
    floor = (Vector((0, 0, 0)), Z)
    def spot(spread):
        o, d = rnd.choice(rays)
        o = Vector(o); d = Vector(d).normalized()
        side = Z.cross(d)
        o = o + side * rnd.uniform(-spread, spread)
        hit, nr = surf.ray(o, d)
        if hit is None:
            return None, None
        return hit, d
    kinds = [('clod', n_clods), ('crumb', n_crumbs), ('peb', n_peb), ('husk', n_husk)]
    for kind, cnt in kinds:
        for _ in range(cnt):
            hit, d = spot(0.5)
            if hit is None:
                continue
            back = rnd.uniform(0.0, 0.35) if kind != 'clod' else rnd.uniform(0.02, 0.2)
            p = Vector((hit.x, hit.y, 0)) - d * back
            if kind == 'clod':
                r = rnd.uniform(0.08, 0.22)
                place(S, forbid, lambda S_, : ell(S_, p + Z * r * 0.25, basis(Z, rnd), (r * 1.2, r, r * rnd.uniform(0.5, 0.8)), rnd,
                                                  sides=rnd.choice((6, 7)), jitter=0.25, clamp=floor))
            elif kind == 'crumb':
                place(S, forbid, crumb_flat, p, Z, rnd, r=rnd.uniform(0.03, 0.07), h=rnd.uniform(0.02, 0.045))
            elif kind == 'peb':
                place(S, forbid, pebble, p, Z, rnd, r=rnd.uniform(0.03, 0.08), embed=0.3)
            else:
                place(S, forbid, husk, p, Z, rnd)


def dress_shell(s, rnd, forbid, FRONT, UNDER=None, TOP=None, BACK=None, HIGH=None, k=1.0, rays=None,
                rubble=(14, 30, 24, 4), extra=()):
    """Generic earth-face dressing. Filters are f(p, n) -> bool on the closed shell surface. k scales counts."""
    surf = Surf(s, rnd)
    if not hasattr(s, "open_edges"):
        s.open_edges = surf.open_edges
    c = lambda n: max(0, int(round(n * k)))
    big = lambda p, n: {'r': rnd.uniform(0.09, 0.2)}
    plan = [
        (c(130), pebble, FRONT, ('Earth',), None),
        (c(30), clod, FRONT, ('Earth',), big),
        (c(7), ledge_slab, lambda p, n: FRONT(p, n) and 0.6 < p.z < 3.4 and abs(n.z) < 0.5, ('Earth',), None),
        (c(10), root_tip, FRONT, ('Earth',), None),
        (c(5), burrow, lambda p, n: FRONT(p, n) and p.z > 0.4, ('Earth',), None),
        (c(6), crack, FRONT, ('Earth',), lambda p, n: {'along': Vector((rnd.uniform(-0.5, 0.5), rnd.uniform(-0.5, 0.5), 1))}),
        (c(10), ridge, lambda p, n: FRONT(p, n) and 0.5 < p.z < 3.3 and abs(n.z) < 0.6, ('Earth',), None),
        (c(12), husk, FRONT, ('Earth',), None),
    ]
    if TOP:
        plan += [(c(22), pebble, TOP, ('Earth',), None), (c(8), clod, TOP, ('Earth',), big),
                 (c(3), root_tip, TOP, ('Earth',), lambda p, n: {'length': rnd.uniform(0.2, 0.4)}),
                 (c(2), crack, TOP, ('Earth',), None), (c(6), needle, TOP, ('Earth',), None)]
    if BACK:
        plan += [(c(25), pebble, BACK, ('Earth',), None), (c(4), clod, BACK, ('Earth',), big)]
    if UNDER:
        plan += [(c(30), hang_root, UNDER, ('Earth',), None), (c(16), hang_drip, UNDER, ('Earth',), None),
                 (c(20), pebble, UNDER, ('Earth',), None)]
    if HIGH:
        plan += [(c(14), hang_root, HIGH, ('Earth',), lambda p, n: {'length': rnd.uniform(0.2, 0.7)})]
    plan += list(extra)
    res = scatter(s, surf, forbid, plan, rnd)
    if rays:
        a, b, cc, d = rubble
        foot_rubble(s, surf, forbid, rnd, rays, c(a), c(b), c(cc), c(d))
    return res


def dress_walk(s, rnd, forbid, filt=None, k=1.0, along=None, ruts=2, mats=('Floor',)):
    """Walkable top dressing: everything flat (<= ~5 cm): crumbs, flat pebbles, husks, needles, cracks, ruts."""
    surf = Surf(s, rnd)
    if not hasattr(s, "open_edges"):
        s.open_edges = surf.open_edges
    f = filt or (lambda p, n: n.z > 0.8)
    c = lambda n: max(0, int(round(n * k)))
    plan = [
        (c(90), crumb_flat, f, mats, None),
        (c(60), crumb_flat, f, mats, lambda p, n: {'region': 'pebble', 'r': rnd.uniform(0.025, 0.07), 'h': rnd.uniform(0.015, 0.04)}),
        (c(14), husk, f, mats, None),
        (c(35), needle, f, mats, None),
        (c(8), crack, f, mats, lambda p, n: {'depth': 0.025, 'width': rnd.uniform(0.012, 0.025)}),
        (ruts, rut, f, mats, lambda p, n: {'along': along, 'length': rnd.uniform(1.2, 2.2)}),
    ]
    return scatter(s, surf, forbid, plan, rnd)
