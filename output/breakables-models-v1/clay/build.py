"""DB01 Clay shards + DB02 Glass shards (Hakaton breakables debris).
Usage: python3 db_shards.py clay|glass [norender]
Shards are cut out of a faceted (hex) revolved body: a jagged 2D outline in the body's (u = hex side, v = profile
arc length) parameter space is split along the facet creases, mapped onto the outer surface (flat facets), offset
inward for the inner surface (irregular thickness) and closed by slanted broken-edge faces."""
import sys, math, random, json, os, shutil
sys.path.insert(0, '/home/claude/brk/work')
import bpy, bmesh, numpy as np
from mathutils import Vector, Matrix
import bk
from bk import PROPS as P

M = 'M_Forest_Props'
SET = sys.argv[1] if len(sys.argv) > 1 else 'clay'
DO_RENDER = 'norender' not in sys.argv
bpy.ops.wm.read_factory_settings(use_empty=True)

# ------------------------------------------------------------------ atlas rects (pixel, y from top)
CLAY_OUT = (140, 0, 192, 41)        # terracotta with light patches (dark bottom band excluded)
CLAY_IN = (164, 41, 181, 52)        # darker clay band (solid #844930) -> inside of the pot
CLAY_EDGE = (153, 26, 175, 32)      # light raw clay patch (solid #ac724b) -> fresh broken edges
CLAY_LIP = (176, 10, 190, 20)       # plain terracotta (solid) -> the original rim lip
GL_OUT = (132, 78, 168, 120)        # green glass with highlight stripe + specks
GL_IN = (157, 68, 168, 78)          # darker top band of the glass region -> inner side
GL_EDGE = (139, 91, 142, 97)        # light highlight stripe -> sharp glass edges catch light
CORK = P['cork']
GLASS_SHIFT = {'Green': 0, 'Brown': 36, 'Blue': 72}   # px; redbrown / blue regions have the same layout


def fr(rect, px, py):
    """pixel offset inside rect (px from left, py from bottom) -> UV"""
    x0, y0, x1, y1 = rect
    return bk.uvr(M, rect, px / max(x1 - x0 - 1, 1e-6), py / max(y1 - y0 - 1, 1e-6))


def solid(rect, k):
    """4 fixed UVs inside a solid-colour rect"""
    a = [(0.15, 0.2), (0.85, 0.2), (0.85, 0.8), (0.15, 0.8)]
    return [bk.uvr(M, rect, *a[i % 4]) for i in range(k)]


# ------------------------------------------------------------------ faceted revolved body
class Body:
    def __init__(self, prof, n=6, seed=0, ajit=5.0, sjit=0.045, rref=0.1, corners=None, phase=0.0):
        rnd = random.Random(seed)
        self.n = n
        if corners is None:
            corners = []
            for i in range(n):
                a = math.radians(360.0 * i / n + phase + rnd.uniform(-ajit, ajit))
                s = 1.0 + rnd.uniform(-sjit, sjit)
                corners.append(Vector((math.cos(a) * s, math.sin(a) * s)))
        self.C = corners
        self.prof = [Vector(p) for p in prof]
        self.cum = [0.0]
        for i in range(1, len(self.prof)):
            self.cum.append(self.cum[-1] + (self.prof[i] - self.prof[i - 1]).length)
        self.L = self.cum[-1]
        self.vbreaks = self.cum[1:-1]
        self.rref = rref

    def hexpt(self, u):
        k = math.floor(u); f = u - k; i = k % self.n
        return self.C[i].lerp(self.C[(i + 1) % self.n], f)

    def rz(self, v):
        v = min(max(v, 0.0), self.L)
        for i in range(1, len(self.cum)):
            if v <= self.cum[i] + 1e-12:
                seg = self.cum[i] - self.cum[i - 1]
                f = (v - self.cum[i - 1]) / seg if seg > 0 else 0
                return self.prof[i - 1].lerp(self.prof[i], f)
        return self.prof[-1]

    def P(self, u, v):
        c = self.hexpt(u); rz = self.rz(v)
        return Vector((c.x * rz.x, c.y * rz.x, rz.y))

    def v_at_z(self, z):
        """arc parameter of the (last rising) profile segment at height z"""
        best = None
        for i in range(1, len(self.prof)):
            z0, z1 = self.prof[i - 1].y, self.prof[i].y
            if z1 > z0 and z0 - 1e-9 <= z <= z1 + 1e-9:
                f = (z - z0) / (z1 - z0)
                best = self.cum[i - 1] + f * (self.cum[i] - self.cum[i - 1])
        if best is None:
            best = self.L if z > self.prof[-1].y else 0.0
        return best

    def Pz(self, u, z):
        return self.P(u, self.v_at_z(z))


def offset_profile(prof, t):
    """inner profile: move every point by t along the inward normal (mitred)"""
    pr = [Vector(p) for p in prof]
    out = []
    for i, p in enumerate(pr):
        ns = []
        if i > 0:
            d = (p - pr[i - 1]).normalized(); ns.append(Vector((d.y, -d.x)))
        if i < len(pr) - 1:
            d = (pr[i + 1] - p).normalized(); ns.append(Vector((d.y, -d.x)))
        n = sum(ns, Vector((0, 0))).normalized()
        c = max(0.5, n.dot(ns[0]))
        q = p - n * (t / c)
        out.append((max(q.x, 0.0), q.y))
    return out


# ------------------------------------------------------------------ generic shard (disc topology patch)
def tri_area2(a, b, c):
    return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])


def patch(b, body, outline, rnd, thick, out_rect, in_rect, edge_rect, lip_rect=None, D=250.0,
          slant=(-0.004, 0.002), uv_pick=(0.5, 0.5)):
    """outline: CCW list of (u, v). Returns stats."""
    R = body.rref
    # snap near-crease points onto the crease (avoid slivers)
    pts = []
    for u, v in outline:
        r = max(body.rz(v).x, 0.02)
        k = round(u)
        if abs(u - k) * r < 0.003: u = float(k)
        for vb in body.vbreaks:
            if abs(v - vb) < 0.003: v = vb
        pts.append((u, v))
    if sum(tri_area2(pts[0], pts[i], pts[i + 1]) for i in range(1, len(pts) - 1)) < 0:
        pts = pts[::-1]
    bm = bmesh.new()
    vs = [bm.verts.new((u * R, v, 0.0)) for u, v in pts]
    bm.faces.new(vs)
    us = [p[0] for p in pts]; vv = [p[1] for p in pts]
    for k in range(math.floor(min(us)) + 1, math.ceil(max(us))):
        if min(us) < k < max(us):
            bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], dist=1e-7,
                                   plane_co=(k * R, 0, 0), plane_no=(1, 0, 0))
    for vb in body.vbreaks:
        if min(vv) < vb < max(vv):
            bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], dist=1e-7,
                                   plane_co=(0, vb, 0), plane_no=(0, 1, 0))
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-7)
    bmesh.ops.triangulate(bm, faces=bm.faces, quad_method='BEAUTY', ngon_method='BEAUTY')
    bm.verts.index_update()
    for f in bm.faces:
        f.normal_update()
    X = [(v.co.x, v.co.y) for v in bm.verts]
    UV2 = [(x / R, y) for x, y in X]
    tris = []
    for f in bm.faces:
        ids = [v.index for v in f.verts]
        if f.normal.z < 0: ids = ids[::-1]
        tris.append(ids)
    # directed boundary edges (face on the left)
    bedges = []
    for e in bm.edges:
        if len(e.link_faces) == 1:
            f = e.link_faces[0]
            for l in f.loops:
                if l.edge == e:
                    a, c = l.vert.index, l.link_loop_next.vert.index
                    if f.normal.z < 0: a, c = c, a
                    bedges.append((a, c))
    bm.free()
    nxt = {a: c for a, c in bedges}; prv = {c: a for a, c in bedges}
    O = [body.P(u, v) for u, v in UV2]
    # vertex normals (area weighted) of the outer surface
    N = [Vector((0, 0, 0)) for _ in O]
    for t in tris:
        n = (O[t[1]] - O[t[0]]).cross(O[t[2]] - O[t[0]])
        for i in t: N[i] += n
    N = [n.normalized() for n in N]
    lipv = lambda i: lip_rect is not None and UV2[i][1] >= body.L - 1e-6
    # slanted break: shift inner boundary verts in 2D along the outline normal
    shift = {}
    for i in nxt:
        if lipv(i): continue
        p, q = Vector(X[prv[i]]), Vector(X[nxt[i]])
        tv = (q - p)
        if tv.length < 1e-9: continue
        tv.normalize()
        shift[i] = Vector((tv.y, -tv.x)) * rnd.uniform(*slant)
    for _ in range(6):
        XI = [Vector(X[i]) + shift.get(i, Vector((0, 0))) for i in range(len(X))]
        bad = [t for t in tris if tri_area2(XI[t[0]], XI[t[1]], XI[t[2]]) <= 1e-10]
        if not bad: break
        for t in bad:
            for i in t:
                if i in shift: shift[i] = shift[i] * 0.4
    I = []
    for i in range(len(X)):
        xi = XI[i]
        p = body.P(xi.x / R, min(max(xi.y, 0.0), body.L))
        I.append(p - N[i] * thick(O[i], UV2[i]))
    # outer UVs: unrolled metric coordinates, placed inside the rect
    u0 = sum(p[0] for p in UV2) / len(UV2)
    XM = [((u - u0) * body.rz(v).x, v) for u, v in UV2]
    xs = [p[0] for p in XM]; ys = [p[1] for p in XM]
    w, h = out_rect[2] - out_rect[0] - 1, out_rect[3] - out_rect[1] - 1
    ew, eh = (max(xs) - min(xs)) * D, (max(ys) - min(ys)) * D
    sc = min(1.0, (w - 0.5) / max(ew, 1e-6), (h - 0.5) / max(eh, 1e-6))   # only shrinks if it would not fit
    ox = (w - ew * sc) * uv_pick[0]; oy = (h - eh * sc) * uv_pick[1]
    UVo = [fr(out_rect, ox + (x - min(xs)) * D * sc, oy + (y - min(ys)) * D * sc) for x, y in XM]
    iw, ih = in_rect[2] - in_rect[0] - 1, in_rect[3] - in_rect[1] - 1
    UVi = [fr(in_rect, (x - min(xs)) / max(max(xs) - min(xs), 1e-6) * iw,
              (y - min(ys)) / max(max(ys) - min(ys), 1e-6) * ih) for x, y in XM]
    for t in tris:
        b.face([O[i] for i in t], [UVo[i] for i in t])
        r = t[::-1]
        b.face([I[i] for i in r], [UVi[i] for i in r])
    for a, c in bedges:
        rect = lip_rect if (lipv(a) and lipv(c)) else edge_rect
        b.face([O[c], O[a], I[a], I[c]], solid(rect, 4))
    return len(tris), len(bedges)


def blob(rnd, n, a, b, rot=0.0, lo=0.62, hi=1.12, alt=0.75, ajit=0.38):
    """jagged closed outline in local metres (CCW): alternating in/out radii for broken teeth"""
    pts = []
    for i in range(n):
        t = 2 * math.pi * (i + rnd.uniform(-ajit, ajit)) / n
        rr = rnd.uniform(lo, hi) * (rnd.uniform(alt, 0.95) if i % 2 else 1.0)
        x, y = a * math.cos(t) * rr, b * math.sin(t) * rr
        c, s = math.cos(rot), math.sin(rot)
        pts.append((x * c - y * s, x * s + y * c))
    return pts


def to_uv(body, u0, v0, local):
    out = []
    for x, y in local:
        v = min(max(v0 + y, 0.0), body.L)
        r = max(body.rz(v).x, 0.02)
        out.append((u0 + x / r, v))
    return out


# ------------------------------------------------------------------ ring helpers (full-circle pieces)
def zip_band(b, ringA, ringB, uvA, uvB):
    """ringA: list of (u, Vector) incl. all integer corners (any extra points between); ringB: corners only
    (u = 0..n-1). Triangulates side by side so every triangle stays in its facet. uvA/uvB: callables (k, u, p)->uv"""
    n = len(ringB)
    for k in range(n):
        A = [(u, p) for u, p in ringA if k - 1e-9 <= u <= k + 1 + 1e-9]
        if A[-1][0] < k + 1 - 1e-9:
            A.append((k + 1.0, ringA[0][1]))
        if abs(A[-1][0] - (k + 1)) < 1e-9 and k + 1 == n:
            A[-1] = (float(n), ringA[0][1])
        B0, B1 = ringB[k], ringB[(k + 1) % n]
        m = len(A) - 1; s = m // 2
        for j in range(m):
            Bp, ub = (B0, k) if j < s else (B1, k + 1)
            b.face([A[j][1], A[j + 1][1], Bp], [uvA(k, A[j][0], A[j][1]), uvA(k, A[j + 1][0], A[j + 1][1]), uvB(k, ub, Bp)])
        b.face([A[s][1], B1, B0], [uvA(k, A[s][0], A[s][1]), uvB(k, k + 1, B1), uvB(k, k, B0)])


def quad_band(b, r0, r1, uvf):
    n = len(r0)
    for k in range(n):
        a, c = r0[k], r0[(k + 1) % n]; d, e = r1[(k + 1) % n], r1[k]
        b.face([a, c, d, e], [uvf(k, k, a), uvf(k, k + 1, c), uvf(k, k + 1, d), uvf(k, k, e)])


def cap(b, ring, up, uvf):
    pts = list(ring) if up else list(ring)[::-1]
    b.face(pts, [uvf(p) for p in pts])


def side_uv(rect, D, body_r_of, offs, ybase=0.0):
    """per hex side unrolled UV: x = (u-k)*r, y = z"""
    w = rect[2] - rect[0] - 1
    def f(k, u, p):
        r = math.hypot(p.x, p.y)
        x = (u - k) * r * D + offs[k % len(offs)]
        return fr(rect, min(max(x, 0), w), (p.z - ybase) * D)
    return f


# ------------------------------------------------------------------ object finishing
def finish(b, name, face_front=True):
    o = b.obj(name)
    if face_front:
        V = np.array([v.co[:] for v in o.data.vertices]) + np.array(o.location[:])
        # outer side: direction from the body axis to the piece
        ang = math.atan2(V[:, 1].mean(), V[:, 0].mean())
        o.data.transform(Matrix.Translation(o.location))
        o.location = (0, 0, 0)
        o.data.transform(Matrix.Rotation(-math.pi / 2 - ang, 4, 'Z'))
        bk.set_origin(o)
    return o


def place(objs, gap=2.0, y=0.0):
    x = 0.0; prev = None
    for o in objs:
        lo, hi = bk.bounds([o]); sz = hi - lo
        w = float(sz[0])
        if prev is not None:
            x += max(prev, w) * gap * 0.5 + max(prev, w) * 0.5
        o.location = (x, y, float(sz[2]) / 2)
        prev = w
    bpy.context.view_layer.update()


def glass_variant(o, name, shift_px):
    o2 = o.copy(); o2.data = o.data.copy(); o2.name = name; o2.data.name = name
    bpy.context.scene.collection.objects.link(o2)
    uvl = o2.data.uv_layers[0].data
    gx0, gx1 = 132 / 256, 168 / 256; gv0, gv1 = 1 - 120 / 256, 1 - 68 / 256
    for l in uvl:
        u, v = l.uv
        if gx0 <= u <= gx1 and gv0 <= v <= gv1:
            l.uv = (u + shift_px / 256, v)
    return o2


OUTDIR = '/home/claude/brk/out/' + SET + '/'
os.makedirs(OUTDIR, exist_ok=True)


# ================================================================== DB01 CLAY
def build_clay():
    POT = [(0, 0), (0.093, 0), (0.13, 0.055), (0.14, 0.10), (0.11, 0.165), (0.093, 0.185), (0.108, 0.20), (0.113, 0.218)]
    PIT = [(0, 0), (0.091, 0), (0.116, 0.05), (0.125, 0.17), (0.095, 0.28), (0.065, 0.36), (0.09, 0.40), (0.10, 0.435)]
    pot = Body(POT, 6, seed=3, rref=0.12)
    pit = Body(PIT, 6, seed=8, rref=0.10)
    out = {}
    notes = {}

    def thick_fn(rnd, lo=0.007, hi=0.013, base_extra=0.0, body=None):
        ph = [rnd.uniform(0, 6.28) for _ in range(3)]
        def t(p, uv):
            n = 0.5 + 0.25 * math.sin(p.x * 90 + ph[0]) + 0.25 * math.sin(p.y * 70 + p.z * 60 + ph[1])
            e = base_extra if (body is not None and uv[1] < body.cum[1] + 1e-6) else 0.0
            return lo + (hi - lo) * n + e
        return t

    # --- Rim: shoulder + neck + flared lip band, straight lip on top, jagged lower break
    rnd = random.Random(101)
    b = bk.B([M]); L = pot.L; ua, ub = 1.25, 2.05
    J = lambda a: rnd.uniform(-a, a)
    outline = [(ua + 0.05, L - 0.052), (ua + 0.15, L - 0.070 + J(.003)), (ua + 0.22, L - 0.064), (ua + 0.30, L - 0.092),
               (ua + 0.37, L - 0.081), (ua + 0.47, L - 0.088 + J(.003)), (ua + 0.55, L - 0.060), (ua + 0.63, L - 0.068),
               (ub - 0.03, L - 0.050), (ub + 0.04, L - 0.030), (ub, L - 0.014), (ub + 0.015, L),
               (ua - 0.01, L), (ua + 0.03, L - 0.016), (ua - 0.03, L - 0.034)]
    patch(b, pot, outline, rnd, thick_fn(rnd, 0.008, 0.012), CLAY_OUT, CLAY_IN, CLAY_EDGE, lip_rect=CLAY_LIP,
          uv_pick=(0.95, 0.05))
    out['Rim'] = finish(b, 'DB01_Clay_Rim')
    notes['Rim'] = 'pot rim: shoulder + neck + flared lip band (clean lip on top, jagged lower break); from SM_PR09_PotLow profile'

    # --- Base: wedge of the flat bottom + wall rising ~3-4 cm
    rnd = random.Random(202)
    b = bk.B([M]); vb = pot.cum[1]
    outline = [(0.35, 0.040), (0.55, 0.030), (0.72, 0.046), (0.92, 0.036), (1.12, 0.052), (1.28, 0.064),
               (1.32, vb + 0.008), (1.25, vb + 0.026), (1.16, vb + 0.020), (1.06, vb + 0.042), (0.97, vb + 0.038),
               (0.90, vb + 0.047), (0.80, vb + 0.024), (0.70, vb + 0.030), (0.62, vb + 0.016), (0.52, vb + 0.036),
               (0.43, vb + 0.028), (0.36, vb + 0.008), (0.30, vb - 0.022),
               (0.36, 0.066)]
    patch(b, pot, outline, rnd, thick_fn(rnd, 0.008, 0.012, base_extra=0.004, body=pot), CLAY_OUT, CLAY_IN,
          CLAY_EDGE, uv_pick=(0.6, 0.3))
    out['Base'] = finish(b, 'DB01_Clay_Base')
    notes['Base'] = 'flat bottom wedge (thicker floor) with the lower wall rising ~4 cm, jagged break all round'

    # --- WallLong: ~12 cm sliver down the pitcher body across the belly crease
    rnd = random.Random(303)
    b = bk.B([M])
    loc = blob(rnd, 17, 0.017, 0.072, rot=math.radians(-14), lo=0.72, hi=1.1, alt=0.62)
    patch(b, pit, to_uv(pit, 1.12, 0.245, loc), rnd, thick_fn(rnd), CLAY_OUT, CLAY_IN, CLAY_EDGE, uv_pick=(0.75, 0.2))
    out['WallLong'] = finish(b, 'DB01_Clay_WallLong')
    notes['WallLong'] = 'long thin sliver of the pitcher belly, crosses a vertical hex crease and the belly bend'

    # --- WallMid: ~7 cm piece of the pot belly over the widest bend
    rnd = random.Random(404)
    b = bk.B([M])
    loc = blob(rnd, 15, 0.042, 0.033, rot=0.4, lo=0.7, hi=1.1, alt=0.55)
    patch(b, pot, to_uv(pot, 3.62, 0.198, loc), rnd, thick_fn(rnd), CLAY_OUT, CLAY_IN, CLAY_EDGE, uv_pick=(0.3, 0.5))
    out['WallMid'] = finish(b, 'DB01_Clay_WallMid')
    notes['WallMid'] = 'mid wall shard of the pot belly over the widest bend (2-3 facets)'

    # --- WallSmall: ~4 cm, strongly curved, from the pitcher neck bend
    rnd = random.Random(505)
    b = bk.B([M])
    loc = [(-0.021, -0.012), (-0.006, -0.016), (0.004, -0.021), (0.016, -0.009), (0.022, 0.006), (0.011, 0.010),
           (0.007, 0.021), (-0.005, 0.013), (-0.011, 0.016), (-0.014, 0.003)]
    loc = [(x + rnd.uniform(-.001, .001), y + rnd.uniform(-.001, .001)) for x, y in loc]
    patch(b, pit, to_uv(pit, 4.86, 0.385, loc), rnd, thick_fn(rnd, 0.007, 0.011), CLAY_OUT, CLAY_IN, CLAY_EDGE,
          uv_pick=(0.9, 0.7))
    out['WallSmall'] = finish(b, 'DB01_Clay_WallSmall')
    notes['WallSmall'] = 'small chip from the pitcher upper body (r 6-9 cm, tighter curvature), over the shoulder crease'

    order = ['Rim', 'Base', 'WallLong', 'WallMid', 'WallSmall']
    pieces = [out[k] for k in order]
    return pieces, {out[k].name: notes[k] for k in order}


# ================================================================== DB02 GLASS
def build_glass():
    notes = {}
    D = 300.0
    pieces = []

    # --- Neck: shoulder flare (jagged break) + neck + lip + cork, one closed mesh
    rnd = random.Random(11)
    PO = [(0.050, -0.012), (0.019, 0.026), (0.018, 0.060), (0.0215, 0.079)]
    bo = Body(PO, 6, seed=21)
    bi = Body(offset_profile(PO, 0.0038), 6, corners=bo.C)
    b = bk.B([M])
    ju = []
    for k in range(6):
        ju.append(float(k)); ju.append(k + 0.5 + rnd.uniform(-0.18, 0.18))
    jz = [-0.003, 0.012, 0.003, 0.006, -0.001, 0.015, 0.009, 0.001, 0.011, 0.005, -0.002, 0.007]   # uneven break
    jz = [z + rnd.uniform(-0.0015, 0.0015) for z in jz]
    J = [(u, bo.Pz(u, z)) for u, z in zip(ju, jz)]
    Jin = [(u, bi.Pz(u, z + rnd.uniform(-0.003, 0.002))) for u, z in zip(ju, jz)]
    N0 = [bo.Pz(k, 0.026) for k in range(6)]
    N1 = [bo.Pz(k, 0.060) for k in range(6)]
    T = [bo.Pz(k, 0.079) for k in range(6)]
    K0 = [Vector((c.x * 0.0145, c.y * 0.0145, 0.0795)) for c in bo.C]
    K1 = [Vector((c.x * 0.0158 + rnd.uniform(-.0008, .0008), c.y * 0.0158 + rnd.uniform(-.0008, .0008),
                  0.101 + rnd.uniform(-0.002, 0.002))) for c in bo.C]
    I0 = [bi.Pz(k, 0.024) for k in range(6)]
    offs = [rnd.uniform(0, 22) for _ in range(6)]
    gu = side_uv(GL_OUT, D, None, offs, ybase=-0.015)
    zip_band(b, J, N0, gu, gu)
    quad_band(b, N0, N1, gu)
    quad_band(b, N1, T, gu)
    quad_band(b, T, K0, lambda k, u, p: fr(GL_OUT, offs[k] * 0.5 + (u - k) * 12, 40))
    co = [rnd.uniform(0, 14) for _ in range(6)]
    CS = (127, 186, 144, 192)   # cork side: plain part (no dark marks that read as a face)
    quad_band(b, K0, K1, lambda k, u, p: fr(CS, co[k] * 0.8 + (u - k) * 5, min((p.z - 0.0795) * D * 0.8, 5)))
    # cork top: two triangles + quad -> 4 tris (top tilted slightly, keep planar-ish via fan)
    cuv = lambda p: fr(CORK, 10 + p.x * D, 10 + p.y * D)
    for t in ((0, 1, 2), (0, 2, 3), (0, 3, 4), (0, 4, 5)):
        b.face([K1[i] for i in t], [cuv(K1[i]) for i in t])
    # inner: jagged inner ring up to the cork bottom (seen from below)
    giu = lambda k, u, p: fr(GL_IN, 1 + (u - k) * 8, 1 + min(max((p.z + 0.012) * 200, 0), 8))
    nI = len(Jin)
    # inner band winding is reversed relative to outer: build then flip via recalc (B.obj recalcs normals)
    zip_band(b, Jin, I0, giu, giu)
    for t in ((0, 2, 1), (0, 3, 2), (0, 4, 3), (0, 5, 4)):
        b.face([I0[i] for i in t], [cuv(I0[i]) for i in t])
    # broken edge band J -> Jin
    n = len(J)
    for i in range(n):
        a, c = J[i][1], J[(i + 1) % n][1]; ai, ci = Jin[i][1], Jin[(i + 1) % n][1]
        b.face([a, ai, ci, c], solid(GL_EDGE, 4))
    o = finish(b, 'DB02_Glass_Neck', face_front=False)
    pieces.append(o)
    notes[o.name] = 'bottle neck snapped at the shoulder (jagged flare), lip ring, cork still in (cork bottom visible inside)'

    # --- Bottom: base disc + jagged wall ring
    rnd = random.Random(12)
    PO = [(0, 0), (0.050, 0), (0.056, 0.012), (0.057, 0.08)]
    bo = Body(PO, 6, seed=22, phase=12)
    bi = Body(offset_profile(PO, 0.0045), 6, corners=bo.C)
    b = bk.B([M])
    ju = []
    for k in range(6):
        ju.append(float(k))
        m = 2 if k in (1, 4) else 1
        cuts = sorted(rnd.uniform(0.15, 0.85) for _ in range(m))
        if m == 2 and cuts[1] - cuts[0] < 0.2: cuts = [cuts[0], min(cuts[0] + 0.25, 0.9)]
        for c in cuts:
            ju.append(k + c)
    jz = [0.016 + 0.034 * rnd.random() ** 1.4 for i in range(len(ju))]
    jz[3] = 0.064; jz[4] = 0.03; jz[9] = 0.052; jz[10] = 0.015   # one tall splinter, uneven rest
    J = [(u, bo.Pz(u, z)) for u, z in zip(ju, jz)]
    Jin = [(u, bi.Pz(u, z + rnd.uniform(-0.003, 0.002))) for u, z in zip(ju, jz)]
    R0 = [bo.P(k, bo.cum[1]) for k in range(6)]
    R1 = [bo.P(k, bo.cum[2]) for k in range(6)]
    Ri0 = [bi.P(k, bi.cum[1]) for k in range(6)]
    Ri1 = [bi.P(k, bi.cum[2]) for k in range(6)]
    offs = [rnd.uniform(0, 18) for _ in range(6)]
    gu = side_uv(GL_OUT, D, None, offs, ybase=-0.004)
    cuv = lambda p: fr(GL_OUT, 18 + p.x * D, 21 + p.y * D)
    cap(b, R0, up=False, uvf=cuv)
    quad_band(b, R0, R1, gu)
    zip_band(b, J, R1, gu, gu)
    ciu = lambda p: fr(GL_IN, 5 + p.x * 80, 5 + p.y * 80)
    cap(b, Ri0, up=True, uvf=ciu)
    giu = lambda k, u, p: fr(GL_IN, 1 + (u - k) * 8, 1 + min(p.z * 120, 8))
    quad_band(b, Ri0, Ri1, giu)
    zip_band(b, Jin, Ri1, giu, giu)
    n = len(J)
    for i in range(n):
        a, c = J[i][1], J[(i + 1) % n][1]; ai, ci = Jin[i][1], Jin[(i + 1) % n][1]
        b.face([a, ai, ci, c], solid(GL_EDGE, 4))
    o = finish(b, 'DB02_Glass_Bottom', face_front=False)
    pieces.append(o)
    notes[o.name] = 'bottle base disc (11 cm, hex) with a jagged ring of wall 2-6 cm high'

    # --- Wall shards: thin (4 mm), pointy, curved
    SQ = [(0, 0), (0.059, 0), (0.078, 0.03), (0.08, 0.09), (0.06, 0.135), (0.026, 0.162)]
    TL = [(0, 0), (0.047, 0), (0.05, 0.02), (0.05, 0.2), (0.04, 0.25), (0.02, 0.29)]
    gl_t = lambda rnd: (lambda p, uv: 0.0036 + rnd.uniform(0, 0.0009))
    rnd = random.Random(13)
    b = bk.B([M])
    sq = Body(SQ, 6, seed=23, rref=0.08)
    loc = [(-0.036, -0.020), (-0.012, -0.027), (0.004, -0.017), (0.018, -0.032), (0.024, -0.011), (0.038, 0.004),
           (0.015, 0.006), (0.007, 0.033), (-0.004, 0.012), (-0.017, 0.010), (-0.022, -0.004)]
    loc = [(x + rnd.uniform(-.0015, .0015), y + rnd.uniform(-.0015, .0015)) for x, y in loc]
    patch(b, sq, to_uv(sq, 0.92, 0.150, loc), rnd, gl_t(rnd), GL_OUT, GL_IN, GL_EDGE, D=D,
          slant=(-0.0025, 0.0015), uv_pick=(0.05, 0.6))
    o = finish(b, 'DB02_Glass_WallA')
    pieces.append(o)
    notes[o.name] = 'pointy curved shard over the squat bottle shoulder bend (2-3 facets)'

    rnd = random.Random(14)
    b = bk.B([M])
    tl = Body(TL, 6, seed=24, rref=0.05)
    loc = [(0.0, 0.040), (-0.006, 0.012), (-0.017, 0.004), (-0.012, -0.012), (-0.020, -0.030), (-0.004, -0.022),
           (0.004, -0.037), (0.011, -0.012), (0.019, -0.002), (0.009, 0.010)]
    loc = [(x * 1.05 + rnd.uniform(-.002, .002), y + rnd.uniform(-.002, .002)) for x, y in loc]
    loc = [(x * math.cos(0.25) - y * math.sin(0.25), x * math.sin(0.25) + y * math.cos(0.25)) for x, y in loc]
    patch(b, tl, to_uv(tl, 2.05, 0.205, loc), rnd, gl_t(rnd), GL_OUT, GL_IN, GL_EDGE, D=D,
          slant=(-0.0025, 0.0015), uv_pick=(0.0, 0.3))
    o = finish(b, 'DB02_Glass_WallB')
    pieces.append(o)
    notes[o.name] = 'crooked dagger-like shard of the tall bottle body across a hex crease'
    return pieces, notes


# ================================================================== main
if SET == 'clay':
    pieces, notes = build_clay()
    place(pieces)
    FBX = OUTDIR + 'SM_DB01_ClayShards.fbx'
else:
    pieces, notes = build_glass()
    place(pieces, y=0.0)
    variants = []
    for vi, (vn, sh) in enumerate((('Brown', 36), ('Blue', 72))):
        row = []
        for o in pieces:
            o2 = glass_variant(o, o.name + '_' + vn, sh)
            o2.location = o.location + Vector((0, 0.3 * (vi + 1), 0))
            row.append(o2)
            notes[o2.name] = notes[o.name] + f' ({vn.lower()} glass UV variant)'
        variants += row
    green = pieces
    pieces = pieces + variants
    FBX = OUTDIR + 'SM_DB02_GlassShards.fbx'
bpy.context.view_layer.update()

cols = [bk.col(o) for o in pieces]
objs = []
for o, c in zip(pieces, cols):
    objs += [o, c]
rows = bk.report(objs)
for r in rows:
    r['note'] = notes.get(r['name'], 'convex hull collider of ' + r['name'][:-4])
print(json.dumps(rows, indent=1))
bk.export_fbx(objs, FBX)
json.dump({'set': SET, 'fbx': os.path.basename(FBX), 'material': M, 'objects': rows},
          open(OUTDIR + 'manifest.json', 'w'), indent=1)
shutil.copy(__file__, OUTDIR + 'build.py')
bk.save_blend(OUTDIR + ('clay.blend' if SET == 'clay' else 'glass.blend'))

if DO_RENDER:
    tiles, labels = [], []
    show = pieces if SET == 'clay' else green
    for o in show:
        for az, el, lab in ((-28, 22, 'outer'), (152, 38, 'inner/edge')):
            p = OUTDIR + f'_t_{o.name}_{az}.png'
            bk.render([o], p, az=az, el=el, size=(360, 360), samples=8)
            tiles.append(p); labels.append(f'{o.name} {lab}')
    # scale shot: duplicates lying on the ground next to the source prop(s)
    srcs = ['SM_PR09_PotLow.fbx', 'SM_PR10_Pitcher.fbx'] if SET == 'clay' else \
        ['SM_PR06_BottleSquat.fbx', 'SM_PR07_BottleTall.fbx', 'SM_PR08_BottleFlat.fbx']
    src_objs = []
    xs = 0.0
    for f in srcs:
        so = bk.import_src(f)
        lo, hi = bk.bounds(so)
        for s in so:
            s.data.transform(Matrix.Translation((xs - (lo[0] + hi[0]) / 2, 0, 0)))
        xs += float(hi[0] - lo[0]) + 0.08
        src_objs += so
    lo, hi = bk.bounds(src_objs)
    dups = []
    xx = float(lo[0])
    rr = random.Random(5)
    for i, o in enumerate(show):
        d = o.copy(); bpy.context.scene.collection.objects.link(d)
        if 'Neck' in o.name or 'Bottom' in o.name:
            d.rotation_euler = (0, 0, rr.uniform(0, 6))
        else:
            d.rotation_euler = (math.radians(-90 + rr.uniform(-15, 15)), 0, math.radians(rr.uniform(-40, 40)))
        d.location = (0, 0, 0)
        bpy.context.view_layer.update()
        l2, h2 = bk.bounds([d])
        d.location = (xx - float(l2[0]), -0.22 - float(l2[1] + h2[1]) / 2 + (0.06 if i % 2 else 0), -float(l2[2]))
        bpy.context.view_layer.update()
        xx += float(h2[0] - l2[0]) + 0.03
        dups.append(d)
    p = OUTDIR + '_t_scale.png'
    bk.render(src_objs + dups, p, az=-18, el=32, size=(360, 360), samples=8)
    tiles.append(p); labels.append('scale vs ' + ', '.join(s[3:-4] for s in srcs))
    p = OUTDIR + '_t_scale2.png'
    bk.render(src_objs + dups, p, az=200, el=40, size=(360, 360), samples=8)
    tiles.append(p); labels.append('scale, back')
    if SET == 'glass':
        for o in src_objs + dups:
            o.hide_render = True
        p = OUTDIR + '_t_variants.png'
        bk.render(pieces, p, az=-15, el=58, size=(360, 360), samples=8, margin=0.95)
        tiles.append(p); labels.append('green / brown / blue')
    bk.sheet(tiles, OUTDIR + 'card.jpg', cols=4 if SET == 'clay' else 5, labels=labels,
             title=f'{os.path.basename(FBX)}  ' + '  '.join(f"{r['name']}:{r['tris']}t" for r in rows if not r['name'].endswith('_COL') and r['name'] in [x.name for x in show]))
    for t in tiles:
        os.remove(t)
