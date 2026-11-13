"""Shared construction helpers for the log huts and the well sweep (SW16, SW17, SW18).

Everything is built upright in local metres (Z up, front = -Y), then bent with deform() (lean, sag, tilt),
then grounded with sk.ground().  One sk.Mesh per material is collected in a Kit.
"""
import sys, math, random
sys.path.insert(0, '/home/claude/swamp/kit')
import sk, bpy, bmesh
from mathutils import Vector, Matrix
from shapely.geometry import Polygon, box as sbox, MultiPolygon
from shapely.geometry.polygon import orient
from shapely import affinity

# LogWall texture: 5 log bands (pixel rows in the 256 px texture), dark seams between them
BANDS = [(5, 49), (55, 99), (105, 149), (155, 199), (205, 253)]
ORDER = ['LogWall', 'Bark', 'BarkMossy', 'Planks', 'Roof', 'Stone', 'Props', 'Moss', 'Plants']
Z = Vector((0, 0, 1))


class Kit:
    def __init__(self, vid, seed=0):
        self.vid = vid
        self.m = {}
        self.rnd = random.Random(seed)

    def __getitem__(self, mat):
        if mat not in self.m:
            self.m[mat] = sk.Mesh(mat)
        return self.m[mat]

    def drape(self, src, filt=None, keep=0.5, min_nz=0.6, off=0.014, seed=0):
        """Moss skin copied from upward faces of the material mesh `src` (before build)."""
        rnd = random.Random(seed)
        bm = self[src].bm
        bm.normal_update()
        mm = self['Moss']
        t = sk.MATS['Moss'][2]
        for f in list(bm.faces):
            n = f.normal
            if n.z < min_nz:
                continue
            c = f.calc_center_median()
            if filt and not filt(c):
                continue
            if rnd.random() > keep:
                continue
            pts = [v.co + n * off for v in f.verts]
            mm.face(pts, [(p.x / t + p.z * .3, p.y / t) for p in pts])

    def build(self):
        objs = []
        for mat in ORDER:
            if mat in self.m and len(self.m[mat].bm.faces):
                o = self.m[mat].to_object('%s_%s' % (self.vid, mat))
                triangulate_ngons(o)
                objs.append(o)
        return objs


def triangulate_ngons(o):
    bm = bmesh.new()
    bm.from_mesh(o.data)
    ng = [f for f in bm.faces if len(f.verts) > 4]
    if ng:
        bmesh.ops.triangulate(bm, faces=ng, quad_method='BEAUTY', ngon_method='BEAUTY')
    bm.to_mesh(o.data)
    bm.free()
    for p in o.data.polygons:
        p.use_smooth = False


def deform(objs, fn):
    for o in objs:
        for v in o.data.vertices:
            v.co = fn(Vector(v.co))
        o.data.update()


def smooth(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


# --------------------------------------------------------------------------- logs / beams
def log(K, p0, p1, r, sides=6, kinks=1, seed=0, caps=(True, True), sq=(1.0, 1.0), wob=0.03, rjit=0.07,
        mat='LogWall', band=None, cap_region=None, rot=None, r1=None, uoff=None, band_mode=None, up=None):
    """Crooked faceted log/beam from p0 to p1.
    LogWall: one log band of the texture wraps around the log (grain along the log).
    Other materials: tiling, grain (V) along the log.  Caps: Props end grain (or cap_region)."""
    rnd = random.Random(seed)
    P0, P1 = Vector(p0), Vector(p1)
    D = P1 - P0
    L = D.length
    t = D / L
    if up is None:
        up = Z.copy() if abs(t.z) < 0.9 else Vector((1, 0, 0))
    up = Vector(up)
    up = (up - t * up.dot(t)).normalized()
    sd = t.cross(up)
    n = kinks + 2
    if rot is None:
        rot = math.pi / sides
    if r1 is None:
        r1 = r
    M = K[mat]
    if band_mode is None:
        band_mode = (mat == 'LogWall')
    if band is None:
        band = rnd.randrange(5)
    y0, y1 = BANDS[band % 5]
    v0, v1 = 1 - y1 / 256.0, 1 - y0 / 256.0
    tile = sk.MATS[mat][2] or 1.0
    if uoff is None:
        uoff = rnd.random()
    rings, fs = [], []
    for i in range(n):
        f = i / (n - 1)
        if 0 < i < n - 1:
            f += rnd.uniform(-0.12, 0.12) / (n - 1)
        c = P0 + D * f
        if 0 < i < n - 1:
            c = c + up * rnd.uniform(-wob, wob) + sd * rnd.uniform(-wob, wob)
        rr = r + (r1 - r) * f
        ring = []
        for k in range(sides):
            a = rot + 2 * math.pi * k / sides
            j = 1 + rnd.uniform(-rjit, rjit)
            ring.append(c + up * (math.cos(a) * rr * sq[0] * j) + sd * (math.sin(a) * rr * sq[1] * j))
        rings.append(ring)
        fs.append(f)
    circ = 2 * math.pi * max(r, r1) * max(sq)
    for i in range(n - 1):
        for k in range(sides):
            k2 = (k + 1) % sides
            a = rot + 2 * math.pi * k / sides
            a2 = rot + 2 * math.pi * (k + 1) / sides
            if band_mode:
                ua, ub = fs[i] * L / tile + uoff, fs[i + 1] * L / tile + uoff
                va = v0 + (v1 - v0) * (0.5 + 0.5 * math.cos(a))
                vb = v0 + (v1 - v0) * (0.5 + 0.5 * math.cos(a2))
                uvs = [(ua, va), (ua, vb), (ub, vb), (ub, va)]
            else:
                ua = k / sides * circ / tile + uoff
                ub = (k + 1) / sides * circ / tile + uoff
                va, vb = fs[i] * L / tile, fs[i + 1] * L / tile
                uvs = [(ua, va), (ub, va), (ub, vb), (ua, vb)]
            M.quad(rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k], *uvs)
    creg = cap_region or ('end_grain_%d' % rnd.randint(0, 1))
    if caps[0]:
        K['Props'].cap(list(reversed(rings[0])), region=creg)
    if caps[1]:
        K['Props'].cap(rings[-1], region=creg)
    return rings


def beam(K, p0, p1, w, h, seed=0, mat='Planks', kinks=1, caps=(True, True), up=None, wob=0.02, cap_region='small_wood'):
    """Square-ish hewn beam / board: width w (side), height h (up)."""
    r = 0.7071
    return log(K, p0, p1, r, sides=4, rot=math.pi / 4, sq=(h, w), kinks=kinks, seed=seed, mat=mat, caps=caps,
               up=up, wob=wob, rjit=0.05, cap_region=cap_region)


# --------------------------------------------------------------------------- generic plates
def prism(K, poly, O, U, V, thick, mat_top, mat_bot=None, mat_side=None, uoff=(0, 0), grain='v', tile=None):
    """Extruded plate. poly: (u,v) metres in plane O + u*U + v*V; top face on the plane (normal U x V),
    bottom at -thick along the normal.  UV: planar metres / tile."""
    O, U, V = Vector(O), Vector(U).normalized(), Vector(V).normalized()
    N = U.cross(V).normalized()
    P = Polygon(poly)
    if not P.is_valid or P.area < 1e-4:
        return
    P = orient(P, 1.0)
    pts2 = list(P.exterior.coords)[:-1]
    mat_bot = mat_bot or mat_top
    mat_side = mat_side or mat_bot
    def uvf(mat, u, v):
        t = tile or sk.MATS[mat][2] or 1.0
        if grain == 'v':
            return (u / t + uoff[0], v / t + uoff[1])
        return (v / t + uoff[0], u / t + uoff[1])
    top = [O + U * u + V * v for (u, v) in pts2]
    bot = [p - N * thick for p in top]
    K[mat_top].face(top, [uvf(mat_top, u, v) for (u, v) in pts2])
    K[mat_bot].face(list(reversed(bot)), [uvf(mat_bot, u, v) for (u, v) in reversed(pts2)])
    ts = tile or sk.MATS[mat_side][2] or 1.0
    s = 0.0
    n = len(top)
    for i in range(n):
        j = (i + 1) % n
        e = (top[j] - top[i]).length
        K[mat_side].quad(top[i], bot[i], bot[j], top[j],
                         (0, s / ts), (thick / ts, s / ts), (thick / ts, (s + e) / ts), (0, (s + e) / ts))
        s += e
    return top


def polys_of(g):
    if g.is_empty:
        return []
    if isinstance(g, Polygon):
        return [g]
    if isinstance(g, MultiPolygon):
        return list(g.geoms)
    return [p for p in getattr(g, 'geoms', []) if isinstance(p, Polygon)]


# --------------------------------------------------------------------------- walls
class Wall:
    """Wall frame: name in front/back/left/right of a L x W box. a = coordinate along the wall (world x for
    front/back, world y for left/right). point(a, z, d): d = outward offset from the wall centre plane."""
    def __init__(self, name, L, W):
        self.name = name
        if name == 'front':
            self.O, self.U, self.N, self.rz, self.ax = Vector((0, -W / 2, 0)), Vector((1, 0, 0)), Vector((0, -1, 0)), 0.0, 0
        elif name == 'back':
            self.O, self.U, self.N, self.rz, self.ax = Vector((0, W / 2, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)), math.pi, 0
        elif name == 'left':
            self.O, self.U, self.N, self.rz, self.ax = Vector((-L / 2, 0, 0)), Vector((0, -1, 0)), Vector((-1, 0, 0)), -math.pi / 2, 1
        else:
            self.O, self.U, self.N, self.rz, self.ax = Vector((L / 2, 0, 0)), Vector((0, 1, 0)), Vector((1, 0, 0)), math.pi / 2, 1

    def point(self, c, z, d=0.0):
        """c: WORLD coordinate along the wall (x or y)."""
        p = self.O.copy()
        p[self.ax] = c
        p.z = z
        return p + self.N * d

    def box(self, K, mat, c, z, d, size, seed=0, tilt=(0, 0), grain='z', jitter=0.006, region=None):
        """size = (along wall, out of wall, height)."""
        K[mat].box(self.point(c, z, d), size, rot=(tilt[0], tilt[1], self.rz), seed=seed, grain=grain,
                   jitter=jitter, region=region)

    def quad(self, K, mat, c0, c1, z0, z1, d, region, inward=False):
        """Flat atlas quad facing outward (or inward)."""
        a, b = self.point(c0, z0, d), self.point(c1, z0, d)
        c, e = self.point(c1, z1, d), self.point(c0, z1, d)
        # make it face outward
        if ((b - a).cross(e - a).dot(self.N) < 0) != inward:
            a, b, c, e = b, a, e, c
        u0, v0, u1, v1 = sk.Mesh.region(region)
        K['Props'].quad(a, b, c, e, (u0, v0), (u1, v0), (u1, v1), (u0, v1))


def cabin(K, L, W, z0, rows, d, r, ext=(0.2, 0.32), openings=None, sides=6, rot=None, sq=(1.0, 1.0),
          seed=0, flush=False, skip_walls=(), mid_ends=None, kinks_long=2, broken=None, extra_lr=0):
    """Stacked log walls with corner notches. front/back logs along X at y=-+W/2 (rows at z0+d*(i+.5)),
    left/right logs along Y at x=-+L/2 (rows at z0+d*i, half offset) - the crossing log ends make the
    Russian corner.  flush=True: no protruding ends (corner posts cover the corners).
    openings: {'front': [(c0, c1, zb, zt), ...]} world coords along the wall.
    mid_ends: {'front': [x, ...]} columns of stub log ends of an inner wall.
    broken: {'front': {row: (c_from, c_to)}} -> logs missing there (rotten gaps)."""
    rnd = random.Random(seed)
    openings = openings or {}
    mid_ends = mid_ends or {}
    broken = broken or {}
    tops = {}
    for wname in ('front', 'back', 'left', 'right'):
        if wname in skip_walls:
            continue
        along_x = wname in ('front', 'back')
        half = L / 2 if along_x else W / 2
        pos = (-W / 2 if wname == 'front' else W / 2) if along_x else (-L / 2 if wname == 'left' else L / 2)
        nrows = rows if along_x else rows + extra_lr
        for i in range(nrows):
            z = z0 + d * (i + 0.5) if along_x else z0 + d * i
            e0 = 0.08 if flush else rnd.uniform(*ext)
            e1 = 0.08 if flush else rnd.uniform(*ext)
            segs = [(-half - e0, half + e1)]
            for (c0, c1, zb, zt) in openings.get(wname, []):
                if z + r * 0.6 > zb and z - r * 0.6 < zt:
                    new = []
                    for (s0, s1) in segs:
                        if c1 <= s0 or c0 >= s1:
                            new.append((s0, s1)); continue
                        if c0 > s0 + 0.05:
                            new.append((s0, c0))
                        if c1 < s1 - 0.05:
                            new.append((c1, s1))
                    segs = new
            br = broken.get(wname, {}).get(i)
            if br:
                Wall(wname, L, W).quad(K, 'Props', br[0] - 0.05, br[1] + 0.05, z - r, z + r, -r * 0.75, 'dark')
                new = []
                for (s0, s1) in segs:
                    if br[1] <= s0 or br[0] >= s1:
                        new.append((s0, s1)); continue
                    if br[0] > s0 + 0.05:
                        new.append((s0, br[0]))
                    if br[1] < s1 - 0.05:
                        new.append((br[1], s1))
                segs = new
            for (s0, s1) in segs:
                if s1 - s0 < 0.12:
                    continue
                if along_x:
                    p0, p1 = (s0, pos, z), (s1, pos, z)
                else:
                    p0, p1 = (pos, s0, z), (pos, s1, z)
                kk = kinks_long if s1 - s0 > 3.0 else 1
                log(K, p0, p1, r * rnd.uniform(0.94, 1.06), sides=sides, kinks=kk, seed=rnd.randint(0, 99999),
                    rot=rot, sq=sq, band=rnd.randrange(5))
            tops[wname] = z + r * sq[0]
        # inner wall stub ends
        for c in mid_ends.get(wname, []):
            for i in range(rows):
                z = z0 + d * i + d * 0.0
                if along_x:
                    sgn = -1 if wname == 'front' else 1
                    p0 = (c, pos + sgn * rnd.uniform(*ext), z + d * 0.0)
                    p1 = (c, pos - sgn * 0.45, z)
                else:
                    sgn = -1 if wname == 'left' else 1
                    p0 = (pos + sgn * rnd.uniform(*ext), c, z)
                    p1 = (pos - sgn * 0.45, c, z)
                log(K, p0, p1, r * rnd.uniform(0.94, 1.04), sides=sides, kinks=0, seed=rnd.randint(0, 99999),
                    rot=rot, sq=sq, caps=(True, False))
    return tops


def foundation(K, L, W, z0, h=0.22, courses=2, seed=0, out=0.06):
    """Rows of short hewn blocks (sill blocks) under the walls."""
    rnd = random.Random(seed)
    for c in range(courses):
        z = z0 + h * (c + 0.5)
        for wname in ('front', 'back', 'left', 'right'):
            along_x = wname in ('front', 'back')
            half = (L / 2 + 0.15) if along_x else (W / 2 - 0.12)
            pos = (-W / 2 if wname == 'front' else W / 2) if along_x else (-L / 2 if wname == 'left' else L / 2)
            pos += (-out if pos < 0 else out)
            s = -half + (0.25 if c % 2 else 0) * rnd.random()
            while s < half - 0.1:
                l = min(rnd.uniform(0.45, 0.85), half - s)
                a, b = s + 0.015, s + l - 0.015
                p0, p1 = ((a, pos, z), (b, pos, z)) if along_x else ((pos, a, z), (pos, b, z))
                log(K, p0, p1, h * 0.62, sides=6, rot=0.0, sq=(0.85, 1.25), kinks=0, seed=rnd.randint(0, 9999),
                    mat='LogWall', rjit=0.1)
                s += l


def stone_plinth(K, L, W, z0, h=0.22, courses=2, seed=0, out=0.08, depth=0.36, missing=0.06, displaced=0.1):
    """Low plinth of separate crooked Stone blocks under the bottom logs (irregular heights, a few displaced or
    missing blocks)."""
    rnd = random.Random(seed)
    for c in range(courses):
        for wname in ('front', 'back', 'left', 'right'):
            along_x = wname in ('front', 'back')
            half = (L / 2 + 0.2) if along_x else (W / 2 - 0.15)
            pos = (-W / 2 if wname == 'front' else W / 2) if along_x else (-L / 2 if wname == 'left' else L / 2)
            sg = -1 if pos < 0 else 1
            s = -half - (rnd.uniform(0.0, 0.25) if c % 2 else 0)
            while s < half - 0.08:
                l = min(rnd.uniform(0.32, 0.7), half - s)
                if l < 0.12:
                    break
                hh = h * rnd.uniform(0.8, 1.2)
                if rnd.random() < missing and c == courses - 1:
                    s += l
                    continue
                m = (s + l / 2)
                o = sg * (out + rnd.uniform(-0.03, 0.03))
                rot = (rnd.uniform(-.06, .06), rnd.uniform(-.06, .06), rnd.uniform(-.06, .06))
                if rnd.random() < displaced:
                    o += sg * rnd.uniform(0.08, 0.18)
                    rot = (rnd.uniform(-.25, .25), rnd.uniform(-.25, .25), rnd.uniform(-.3, .3))
                zc = z0 + h * c + hh / 2 - (0.02 if c == 0 else 0)
                ctr = (m, pos + o, zc) if along_x else (pos + o, m, zc)
                size = (l - 0.03, depth * rnd.uniform(0.85, 1.1), hh) if along_x else (depth * rnd.uniform(0.85, 1.1), l - 0.03, hh)
                K['Stone'].box(ctr, size, rot=rot, jitter=0.03, seed=rnd.randint(0, 9999), taper=(rnd.uniform(.85, .97), rnd.uniform(.85, .97)),
                               top_shift=(rnd.uniform(-.02, .02), rnd.uniform(-.02, .02)), grain='x')
                s += l


# --------------------------------------------------------------------------- roof
def noise2(x, y, seed):
    r = random.Random(seed)
    v = 0.0
    for k in range(4):
        fx, fy = r.uniform(1.2, 3.2), r.uniform(1.2, 3.2)
        ph1, ph2 = r.uniform(0, 6.3), r.uniform(0, 6.3)
        v += math.sin(x * fx + ph1 + math.sin(y * 1.7 + ph2)) * math.sin(y * fy + ph2)
    return v / 2.0


def roof(K, xa, xb, ye, ze, zr, thick=0.09, seed=0, holes=None, cuts=None, moss=0.0, drip=0.6,
         t_split=0.52, slab_w=(0.9, 1.5), bias=0.5, front_only=False, sides=(-1, 1), ridge=True,
         barge=True, purlins=True, cell=0.2, barge_cut=None, fringe=0.85, freq=2.6):
    """Gable roof of big crooked plank-shingle slabs in 2 courses per side.
    Underside plane runs from the eave line (y=-+ye, z=ze) to the ridge (y=0, z=zr), from x=xa to x=xb.
    holes/cuts: {side: [polygon in (s,t) metres]}: s along the eave from that side's start corner, t up-slope
    from the eave.  Side -1 starts at xa (front), side +1 starts at xb (back).
    moss: threshold-ish amount (0..1) of moss cells on top; drip: probability of drip cards on a mossy eave cell."""
    rnd = random.Random(seed)
    holes = holes or {}
    cuts = cuts or {}
    T = math.hypot(ye, zr - ze)
    info = {'T': T}
    for s in sides:
        U = Vector((1, 0, 0)) if s < 0 else Vector((-1, 0, 0))
        Vd = Vector((0, -s * ye, zr - ze)) / T
        N = U.cross(Vd).normalized()
        O0 = Vector((xa if s < 0 else xb, s * ye, ze))
        Lr = abs(xb - xa)
        tb = [0.0, T * t_split, T + (0.0 if s < 0 else 0.07)]
        cut_g = None
        for g in holes.get(s, []) + cuts.get(s, []):
            cut_g = Polygon(g) if cut_g is None else cut_g.union(Polygon(g))
        for c in range(2):
            t0 = tb[c] - (0.16 if c else 0.0)
            t1 = tb[c + 1]
            lift = c * thick * 0.85
            x = -rnd.uniform(0.1, 0.5) if c else 0.0
            while x < Lr - 0.02:
                w = rnd.uniform(*slab_w)
                if Lr - (x + w) < 0.45:
                    w = Lr - x
                a, b = max(x, 0.0), min(x + w, Lr)
                x += w
                if b - a < 0.1:
                    continue
                j = lambda: rnd.uniform(-0.035, 0.035)
                bottom = [(a + j(), t0 + j() * 0.6)]
                if rnd.random() < 0.55:  # ragged lower edge
                    nb = rnd.randint(2, 4)
                    for k in range(1, nb):
                        bottom.append((a + (b - a) * k / nb + j(), t0 + rnd.uniform(0.0, 0.11)))
                bottom.append((b + j(), t0 + j() * 0.6))
                top = [(b + j(), t1 + j() * 0.5), (a + j(), t1 + j() * 0.5)]
                P = Polygon(bottom + top).buffer(0)
                if cut_g is not None:
                    P = P.difference(cut_g)
                ang = rnd.uniform(-1.5, 1.5)
                P_list = [affinity.rotate(p, ang, origin='centroid') for p in polys_of(P)]
                for p in P_list:
                    if p.area < 0.02:
                        continue
                    if len(p.interiors):
                        p = Polygon(p.exterior)
                    lj = lift + rnd.uniform(-0.008, 0.018)
                    O = O0 + N * (thick + lj)
                    uo = (rnd.random(), 0.0)
                    prism(K, list(p.exterior.coords)[:-1], O, U, Vd, thick, 'Roof', 'Planks', 'Planks',
                          uoff=uo, tile=None)
                    if moss > 0:
                        roof_moss(K, p, O, U, Vd, N, s, rnd, moss, 0.0 if c == 0 else drip * 0.3, bias, T, cell, seed, hmax=0.16, freq=freq)
        # continuous fringe of moss drips along the eave of this side
        if fringe > 0:
            from shapely.geometry import Point
            sx = 0.0
            hoff = Vector((N.x, N.y, 0)).normalized() * 0.035
            while sx < Lr:
                w = rnd.uniform(0.2, 0.34)
                sm = sx + w / 2
                sx += w * rnd.uniform(0.55, 0.8)
                if rnd.random() > fringe or sm > Lr - 0.05:
                    continue
                if cut_g is not None and cut_g.contains(Point(sm, 0.06)):
                    continue
                base = O0 + U * sm + Vd * 0.04 + N * (thick + 0.012) + hoff
                h = rnd.choice((rnd.uniform(0.22, 0.4), rnd.uniform(0.35, 0.8)))
                drip_card(K, base + hoff * rnd.uniform(-0.3, 0.6), U * (w / 2), h, rnd)
        # purlins under the roof (visible at gables and through holes)
        if purlins:
            for tf in (0.38, 0.75):
                p = O0 + Vd * (T * tf) - N * 0.11
                log(K, p + U * 0.06, p + U * (Lr - 0.06), 0.1, sides=6, kinks=1, mat='Bark',
                    seed=rnd.randint(0, 9999), rjit=0.08)
        if barge:
            for end in (0, 1):
                xs = (xa - 0.04 if end == 0 else xb + 0.04)
                f0 = (barge_cut or {}).get((s, end), 0.0)   # fraction of the rake missing at the eave end
                yy = s * (ye + 0.06) * (1 - f0)
                p0 = Vector((xs, yy, ze + (zr - ze) * (1 - abs(yy) / ye) + thick * 0.4))
                p1 = Vector((xs, -s * 0.32, zr + 0.32 * (zr - ze) / ye + thick * 0.4))
                beam(K, p0, p1, 0.06, 0.2, seed=rnd.randint(0, 9999), up=(0, -s * (zr - ze), ye), kinks=1)
                if fringe > 0:   # drips hanging over the bargeboard
                    rk = (p1 - p0)
                    Lk = rk.length * 0.8
                    q = 0.1
                    while q < Lk:
                        w = rnd.uniform(0.2, 0.34)
                        if rnd.random() < fringe * 0.85:
                            c0 = p0 + rk.normalized() * (q + w / 2) + Vector((0, 0, 0.1)) + \
                                Vector((-0.035 if end == 0 else 0.035, 0, 0))
                            drip_card(K, c0, rk.normalized() * (w / 2), rnd.uniform(0.15, 0.45), rnd)
                        q += w * rnd.uniform(0.75, 1.0)
    if ridge:
        zt = zr + thick * 1.9
        log(K, (xa - 0.12, 0, zt), (xb + 0.12, 0, zt), 0.11, sides=6, kinks=2, mat='LogWall',
            seed=rnd.randint(0, 9999), wob=0.04)
    return info


_LONG = None


def _long_cols():
    """Columns (fractions of the region width) of the moss_drip regions whose painted strand is long."""
    global _LONG
    if _LONG is None:
        import numpy as np
        from PIL import Image
        im = np.asarray(Image.open(sk.os.path.join(sk.TEX, 'T_Swamp_Plants.png')).convert('RGBA'))
        _LONG = {}
        R = sk.REG['T_Swamp_Plants.png']['regions']
        for reg in ('moss_drip_0', 'moss_drip_1'):
            x, y, w, h = R[reg]
            a = im[y:y + h, x:x + w, 3] > 127
            cols = []
            for c in range(w):
                nz = np.nonzero(a[:, c])[0]
                if len(nz) and nz.max() + 1 >= 0.38 * h:
                    cols.append((c + 0.5) / w)
            _LONG[reg] = cols or [0.5]
    return _LONG


def drip_card(K, top_mid, R, h, rnd, flutter=0.015):
    """Moss drip card hanging from top_mid (half-width vector R, length h). Uses a narrow 12-20 % slice of a
    moss_drip region centred on a long painted strand, so the drips come out chunky like the concept."""
    reg = 'moss_drip_%d' % rnd.randint(0, 1)
    u0, v0, u1, v1 = sk.Mesh.region(reg, 'Plants')
    f = rnd.uniform(0.12, 0.2)
    cc = rnd.choice(_long_cols()[reg]) + rnd.uniform(-0.03, 0.03)
    a = min(max(cc - f / 2, 0.0), 1.0 - f)
    ua, ub = u0 + (u1 - u0) * a, u0 + (u1 - u0) * (a + f)
    T = Vector(top_mid)
    Rv = Vector(R)
    D = Vector((0, 0, -h * 1.6))       # painted strands fill ~40-60 % of the card height
    w0, w1 = (0.0, 0.0), (0.0, flutter)
    K['Plants'].quad(T - Rv, T + Rv, T + Rv + D, T - Rv + D, (ua, v1), (ub, v1), (ub, v0), (ua, v0), [w0, w0, w1, w1])


def roof_moss(K, P, O, U, V, N, side, rnd, amount, drip, bias, T, cell, seed, hmax=0.42, freq=1.3):
    mm = K['Moss']
    pl = K['Plants']
    minx, miny, maxx, maxy = P.bounds
    t = sk.MATS['Moss'][2]
    off = N * 0.016
    nx = int(math.ceil((maxx - minx) / cell))
    ny = int(math.ceil((maxy - miny) / cell))
    for i in range(nx):
        for j in range(ny):
            ca, cb = minx + i * cell, miny + j * cell
            cg = sbox(ca, cb, ca + cell, cb + cell)
            w = O + U * (ca + cell / 2) + V * (cb + cell / 2)
            val = noise2(w.x * freq, w.y * freq + side * 3, seed) + bias * max(0.0, 1 - 2.2 * cb / T) + rnd.uniform(-.25, .25)
            if val < 0.95 - amount * 0.9:
                continue
            g = cg.intersection(P)
            for q in polys_of(g):
                if q.area < 0.004:
                    continue
                pts2 = list(orient(q, 1.0).exterior.coords)[:-1]
                pts = [O + U * u + V * v + off for (u, v) in pts2]
                mm.face(pts, [(p.x / t, p.y / t + p.z / t) for p in pts])
                # drip cards from edges on the lower boundary of the slab
                if rnd.random() > drip:
                    continue
                n = len(pts2)
                best = None
                for k in range(n):
                    (u0, v0), (u1, v1) = pts2[k], pts2[(k + 1) % n]
                    if abs(v1 - v0) > 0.5 * abs(u1 - u0) or abs(u1 - u0) < 0.1:
                        continue
                    mu, mv = (u0 + u1) / 2, (v0 + v1) / 2
                    from shapely.geometry import Point
                    if P.contains(Point(mu, mv - 0.04)):
                        continue
                    if best is None or mv < best[1]:
                        best = (k, mv)
                if best is None:
                    continue
                k = best[0]
                A = pts[k]
                B = pts[(k + 1) % n]
                out = Vector((N.x, N.y, 0))
                out = out.normalized() * 0.025 if out.length > 1e-6 else Vector()
                mid = (A + B) / 2 + out - N * 0.03
                h = rnd.uniform(0.4, 1.0) * hmax
                R = (B - A) / 2 * rnd.uniform(0.9, 1.15)
                drip_card(K, mid, R, h, rnd)


def gable(K, x, out, yw, z0, zr, ze, ye, seed=0, bw=(0.16, 0.26), thick=0.05, missing=(), roof_thick=0.0,
          beam_z=None, beam_len=None):
    """Vertical board gable at plane x (outer face), out=-1 left / +1 right. Boards from y=-yw..yw,
    bottom z0, top under the roof underside line (ze at |y|=ye, zr at y=0)."""
    rnd = random.Random(seed)
    U = Vector((0, 1, 0)) if out > 0 else Vector((0, -1, 0))
    V = Z
    O = Vector((x, 0, 0))
    def zroof(y):
        return zr - (zr - ze) * abs(y) / ye - roof_thick
    a = -yw
    k = 0
    while a < yw - 0.03:
        w = min(rnd.uniform(*bw), yw - a)
        b = a + w
        k += 1
        if k in missing:
            a = b; continue
        u0, u1 = a + 0.012, b - 0.012
        y0, y1 = u0 * out, u1 * out
        zt0 = zroof(y0) - rnd.uniform(0.0, 0.05)
        zt1 = zroof(y1) - rnd.uniform(0.0, 0.05)
        zb = z0 - rnd.uniform(0.0, 0.08)
        if min(zt0, zt1) - zb > 0.08:
            poly = [(u0, zb), (u1, zb + rnd.uniform(-0.03, 0.03)), (u1, zt1), (u0, zt0)]
            if rnd.random() < 0.25:  # broken top corner
                poly = [(u0, zb), (u1, zb), (u1, zt1 - rnd.uniform(0.1, 0.3)), ((u0 + u1) / 2, (zt0 + zt1) / 2 - 0.05), (u0, zt0)]
            prism(K, poly, O + Vector((out * rnd.uniform(-0.01, 0.012), 0, 0)), U, V, thick, 'Planks',
                  uoff=(rnd.random(), rnd.random()))
        a = b
    if beam_z is not None:
        bl = beam_len or yw + 0.25
        beam(K, (x + out * 0.05, -bl, beam_z), (x + out * 0.05, bl, beam_z), 0.12, 0.2, seed=rnd.randint(0, 999), kinks=1)


# --------------------------------------------------------------------------- openings
def window(K, wall, c, zb, w, h, r, seed=0, glass=True, depth=None, head=True):
    """Window in a log wall: atlas window pane set into the wall, plank casing, sill and head board."""
    rnd = random.Random(seed)
    dout = r * 0.95 if depth is None else depth
    if glass:
        wall.quad(K, 'Props', c - w / 2, c + w / 2, zb, zb + h, -0.02, 'window')
        wall.quad(K, 'Props', c + w / 2, c - w / 2, zb, zb + h, -0.06, 'dark', inward=True)
    sgn = 1 if wall.U[wall.ax] > 0 else -1   # direction of +a along the wall in world coords
    cw = 0.1
    # jambs
    for side in (-1, 1):
        wall.box(K, 'Planks', c + side * sgn * (w / 2 + cw / 2), zb + h / 2, dout, (cw, 0.06, h + 0.1),
                 seed=rnd.randint(0, 999), tilt=(0, rnd.uniform(-.03, .03), 0), grain='z')
    # sill & head
    wall.box(K, 'Planks', c + rnd.uniform(-.03, .03), zb - 0.04, dout + 0.03, (w + 0.36, 0.12, 0.08),
             seed=rnd.randint(0, 999), tilt=(rnd.uniform(-.1, .05), rnd.uniform(-.05, .05), 0), grain='x')
    if head:
        wall.box(K, 'Planks', c, zb + h + 0.08, dout + 0.01, (w + 0.42, 0.08, 0.14),
                 seed=rnd.randint(0, 999), tilt=(0, rnd.uniform(-.04, .04), 0), grain='x')


def door(K, wall, c, zb, w, h, r, seed=0, leaf=True, ajar=0.0, lintel=True, dark=True, leaf_d=-0.05,
         frame_d=None, boarded=False, threshold=True):
    rnd = random.Random(seed)
    sgn = 1 if wall.U[wall.ax] > 0 else -1
    dout = r * 0.9 if frame_d is None else frame_d
    if dark:
        wall.quad(K, 'Props', c + w / 2, c - w / 2, zb, zb + h, -r * 0.6, 'dark')
    for side in (-1, 1):
        wall.box(K, 'Planks', c + side * sgn * (w / 2 + 0.07), zb + h / 2, dout - 0.06, (0.13, 0.16, h + 0.08),
                 seed=rnd.randint(0, 999), tilt=(0, rnd.uniform(-.03, .03), 0), grain='z')
    if lintel:
        wall.box(K, 'Planks', c, zb + h + 0.1, dout - 0.04, (w + 0.6, 0.2, 0.2), seed=rnd.randint(0, 999),
                 tilt=(0, rnd.uniform(-.03, .03), 0), grain='x')
    if threshold:
        wall.box(K, 'Planks', c, zb - 0.05, dout - 0.08, (w + 0.3, 0.3, 0.1), seed=rnd.randint(0, 999), grain='x')
    if leaf:
        n = 4
        bw = w / n
        for i in range(n):
            a = c + sgn * (-w / 2 + bw * (i + 0.5))
            hh = h - 0.04 - (rnd.uniform(0.0, 0.12) if i in (0, n - 1) else rnd.uniform(0, 0.04))
            wall.box(K, 'Planks', a, zb + hh / 2 + 0.01, leaf_d + ajar, (bw - 0.012, 0.05, hh), seed=rnd.randint(0, 999),
                     grain='z', jitter=0.01)
        for zz in (0.25, h - 0.3):
            wall.box(K, 'Planks', c, zb + zz, leaf_d + 0.045 + ajar, (w - 0.08, 0.04, 0.12), seed=rnd.randint(0, 999),
                     grain='x', tilt=(0, rnd.uniform(-0.04, 0.04), 0))
        wall.box(K, 'Planks', c, zb + h / 2, leaf_d + 0.045 + ajar, (0.11, 0.035, math.hypot(w - 0.2, h - 0.6)),
                 seed=rnd.randint(0, 999), grain='z', tilt=(0, sgn * math.atan2(w - 0.2, h - 0.6), 0))
    if boarded:
        for k, (zz, ang) in enumerate(((h * 0.3, 0.5), (h * 0.62, -0.42), (h * 0.85, 0.08))):
            wall.box(K, 'Planks', c + rnd.uniform(-.05, .05), zb + zz, leaf_d + 0.1 + 0.03 * k,
                     (w + 0.5, 0.04, 0.17), seed=rnd.randint(0, 999), grain='x', tilt=(0, ang, 0))


def floor(K, L, W, z, seed=0, bw=0.5):
    rnd = random.Random(seed)
    y = -W / 2
    while y < W / 2 - 0.05:
        w = min(bw * rnd.uniform(0.8, 1.2), W / 2 - y)
        beam(K, (-L / 2, y + w / 2, z), (L / 2, y + w / 2, z + rnd.uniform(-.02, .02)), w - 0.02, 0.06,
             seed=rnd.randint(0, 999), kinks=1, caps=(False, False))
        y += w


def post(K, x, y, z0, z1, w=0.22, seed=0, lean=(0, 0)):
    beam(K, (x, y, z0), (x + lean[0], y + lean[1], z1), w, w * 1.1, seed=seed, kinks=1, mat='Planks')


def chimney(K, x, y, zb, zt, s=0.5, seed=0):
    rnd = random.Random(seed)
    n = max(2, int(round((zt - zb) / 0.55)))
    hh = (zt - zb) / n
    ox = oy = 0.0
    for i in range(n):
        ox += rnd.uniform(-0.025, 0.025); oy += rnd.uniform(-0.025, 0.025)
        K['Props'].box((x + ox, y + oy, zb + hh * (i + 0.5)), (s, s, hh + 0.01), rot=(0, 0, rnd.uniform(-.05, .05)),
                       seed=rnd.randint(0, 999), jitter=0.012, region='chimney')
    K['Props'].box((x + ox, y + oy, zt + 0.06), (s + 0.12, s + 0.12, 0.12), rot=(0, 0, rnd.uniform(-.08, .08)),
                   seed=rnd.randint(0, 999), jitter=0.015, region='chimney')


def grass(K, base, n=3, h=(0.5, 0.9), regions=('sedge_0', 'sedge_1', 'sedge_2', 'grass_low'), seed=0, spread=0.25):
    rnd = random.Random(seed)
    for i in range(n):
        b = Vector(base) + Vector((rnd.uniform(-spread, spread), rnd.uniform(-spread, spread), -0.03))
        reg = rnd.choice(regions)
        hh = rnd.uniform(*h) * (0.5 if reg == 'grass_low' else 1)
        K['Plants'].cross_cards(b, hh * 1.1, hh, reg, n=2 if reg != 'grass_low' else 3, rot=rnd.uniform(0, 3),
                                seed=rnd.randint(0, 999), wind_sway=0.05, wind_flutter=0.02)


# --------------------------------------------------------------------------- rendering helpers
def render_views(objs, outdir, prefix, views, water_z=None, size=(400, 400), samples=10):
    paths = []
    for (lab, az, el) in views:
        p = '%s/%s_%s.png' % (outdir, prefix, lab)
        sk.render(objs, p, az=az, el=el, size=size, samples=samples, water_z=water_z if lab.startswith('w') else None, margin=0.9)
        paths.append(p)
    return paths
