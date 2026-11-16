"""Earth-shell helpers for the anthill kit (AN01-AN08, AN10): closed faceted solids built from lofted rings,
normals fixed on the whole closed solid, then split into one object per material (Earth / Floor), box-UV'd.
"""
import sys, os, math, json, random
sys.path.insert(0, '/home/claude/ant/kit')
import ak
import bpy, bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree

SHEETS = '/home/claude/ant/concepts/environment-pack-v3/asset-concepts-v1/sheets'
V = lambda *a: Vector(a if len(a) == 3 else a[0])


def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


class Solid:
    """Collects polygons (points, material, optional uvs, optional normal hint); finish() merges, fixes normals
    (recalc on closed shells, hint flips open details), box-UVs faces without explicit UVs, triangulates crooked
    faces and splits into ak-style objects (one per material)."""

    def __init__(self):
        self.faces = []

    def face(self, pts, mat='Earth', uvs=None, nrm=None):
        P, U = [], []
        for i, p in enumerate(pts):
            p = Vector(p)
            if not P or (P[-1] - p).length > 1e-6:
                P.append(p); U.append(uvs[i] if uvs else None)
        if len(P) > 2 and (P[0] - P[-1]).length < 1e-6:
            P.pop(); U.pop()
        if len(P) >= 3:
            self.faces.append((P, mat, U if uvs else None, Vector(nrm) if nrm is not None else None))

    def quad(self, a, b, c, d, mat='Earth', uvs=None, nrm=None):
        self.face([a, b, c, d], mat, uvs, nrm)

    def mark(self):
        return len(self.faces)

    def aabb(self, m):
        lo = Vector((1e9, 1e9, 1e9)); hi = -lo
        for pts, *_ in self.faces[m:]:
            for p in pts:
                lo = Vector(map(min, lo, p)); hi = Vector(map(max, hi, p))
        return lo, hi

    def rollback(self, m):
        del self.faces[m:]

    def loft(self, secs, mats='Earth', closed=True, cap0=True, cap1=True, capmat='Earth', wrap=False):
        """secs: list of rings (same count). mats: str or list per ring segment k (k -> k+1)."""
        n = len(secs[0])
        nseg = n if closed else n - 1
        S = len(secs)
        for i in range(S - 1 + (1 if wrap else 0)):
            A, B = secs[i], secs[(i + 1) % S]
            for k in range(nseg):
                k2 = (k + 1) % n
                m = mats if isinstance(mats, str) else mats[k]
                self.quad(A[k], A[k2], B[k2], B[k], m)
        if cap0 and not wrap:
            self.face(list(reversed(secs[0])), capmat)
        if cap1 and not wrap:
            self.face(list(secs[-1]), capmat)

    def _bm(self, upto=None):
        bm = bmesh.new()
        ml = bm.faces.layers.int.new('mat')
        hl = bm.faces.layers.int.new('hint')
        ul = bm.faces.layers.int.new('hasuv')
        uvl = bm.loops.layers.uv.new('UVMap')
        names = sorted(set(m for _, m, _, _ in self.faces))
        hints = []
        for pts, m, U, nh in (self.faces if upto is None else self.faces[:upto]):
            vs = [bm.verts.new(p) for p in pts]
            try:
                f = bm.faces.new(vs)
            except ValueError:
                continue
            f[ml] = names.index(m)
            if nh is not None:
                hints.append(nh); f[hl] = len(hints)
            if U:
                for l, uv in zip(f.loops, U):
                    l[uvl].uv = uv
                f[ul] = 1                          # has explicit UVs
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        bad = [f for f in bm.faces if f.calc_area() < 1e-9]
        if bad:
            bmesh.ops.delete(bm, geom=bad, context='FACES')
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        for f in bm.faces:
            if f[hl] and f.normal.dot(hints[f[hl] - 1]) < 0:
                f.normal_flip()
        return bm, names

    def surface(self):
        """Closed-shell triangles with fixed outward normals: list of (a, b, c, normal, mat) + BVH + open edges."""
        bm, names = self._bm()
        ml = bm.faces.layers.int['mat']
        self.surf_open_edges = sum(1 for e in bm.edges if not e.is_manifold)
        tris = []
        for f in bm.faces:
            vs = [v.co.copy() for v in f.verts]
            for i in range(1, len(vs) - 1):
                tris.append((vs[0], vs[i], vs[i + 1], f.normal.copy(), names[f[ml]]))
        verts = [v.co.copy() for v in bm.verts]
        bm.verts.index_update()
        tree = BVHTree.FromBMesh(bm)
        bm.free()
        return tris, tree

    def finish(self, vid, check=True):
        bm, names = self._bm()
        ml = bm.faces.layers.int['mat']
        uvl = bm.loops.layers.uv['UVMap']
        ul = bm.faces.layers.int['hasuv']
        for f in bm.faces:                         # world-planar box UVs (same as ak.auto_uv) for tiling mats
            if f[ul]:
                continue
            t = ak.MATS[names[f[ml]]][2] or 1.0
            n = f.normal
            for l in f.loops:
                co = l.vert.co
                if abs(n.z) > 0.7:
                    u, v = co.x, co.y
                elif abs(n.x) > abs(n.y):
                    u, v = co.y * (1 if n.x > 0 else -1), co.z
                else:
                    u, v = co.x * (-1 if n.y > 0 else 1), co.z
                l[uvl].uv = (u / t, v / t)
        tri = []
        for f in bm.faces:
            if len(f.verts) > 4:
                tri.append(f)
            elif len(f.verts) == 4:
                f.normal_update()
                c = f.calc_center_median()
                dev = max(abs((v.co - c).dot(f.normal)) for v in f.verts)
                if dev > 0.004:
                    tri.append(f)
        if tri:
            bmesh.ops.triangulate(bm, faces=tri, quad_method='SHORT_EDGE', ngon_method='BEAUTY')
        self.all_open_edges = sum(1 for e in bm.edges if not e.is_manifold)
        if not hasattr(self, 'open_edges'):
            self.open_edges = self.all_open_edges
        if check and self.open_edges:
            print('WARNING %s: %d non-manifold edges in the shell' % (vid, self.open_edges))
        objs = []
        for mi, m in enumerate(names):
            b2 = bm.copy()
            l2 = b2.faces.layers.int['mat']
            bmesh.ops.delete(b2, geom=[f for f in b2.faces if f[l2] != mi], context='FACES')
            loose = [v for v in b2.verts if not v.link_faces]
            if loose:
                bmesh.ops.delete(b2, geom=loose, context='VERTS')
            name = '%s_%s' % (vid, m)
            me = bpy.data.meshes.new(name)
            b2.to_mesh(me)
            b2.free()
            for p in me.polygons:
                p.use_smooth = False
            for an in ('mat', 'hint', 'hasuv'):
                if an in me.attributes:
                    me.attributes.remove(me.attributes[an])
            o = bpy.data.objects.new(name, me)
            bpy.context.scene.collection.objects.link(o)
            me.materials.append(ak.material(m))
            o['mat'] = m
            w = me.uv_layers.new(name='Wind')
            for d in w.data:
                d.uv = (1000.0, 1000.0)
            me.uv_layers.active_index = 0
            objs.append(o)
        bm.free()
        order = {'Earth': 0, 'Floor': 1, 'Root': 2, 'Props': 3}
        objs.sort(key=lambda o: order.get(o['mat'], 9))
        return objs


def bvh(objs):
    verts, polys = [], []
    for o in objs:
        off = len(verts)
        verts += [o.matrix_world @ v.co for v in o.data.vertices]
        polys += [[off + i for i in p.vertices] for p in o.data.polygons]
    return BVHTree.FromPolygons(verts, polys)


def box_clear(objs, lo, hi):
    """True if no geometry intersects the axis box lo..hi (passage test)."""
    lo, hi = Vector(lo), Vector(hi)
    c = [Vector((x, y, z)) for x in (lo.x, hi.x) for y in (lo.y, hi.y) for z in (lo.z, hi.z)]
    idx = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    tb = BVHTree.FromPolygons(c, idx)
    hits = bvh(objs).overlap(tb)
    # also any vertex inside the box
    inside = 0
    for o in objs:
        for v in o.data.vertices:
            p = o.matrix_world @ v.co
            if lo.x < p.x < hi.x and lo.y < p.y < hi.y and lo.z < p.z < hi.z:
                inside += 1
    return len(hits) == 0 and inside == 0


def max_slope(objs, mat='Floor'):
    m = 0.0
    for o in objs:
        if o['mat'] != mat:
            continue
        for p in o.data.polygons:
            if p.normal.z > 0.3 and p.area > 0.03:
                m = max(m, math.degrees(math.acos(min(1, p.normal.z))))
    return m


def human(x, y, z=0.0):
    """1.8 m scale dummy (render only)."""
    s = Solid()
    w, d, h = 0.25, 0.18, 1.8
    c = [Vector((x + sx * w, y + sy * d, z + sz * h)) for sx in (-1, 1) for sy in (-1, 1) for sz in (0, 1)]
    s.face([c[0], c[2], c[6], c[4]], 'Wood'); s.face([c[1], c[5], c[7], c[3]], 'Wood')
    s.face([c[0], c[4], c[5], c[1]], 'Wood'); s.face([c[2], c[3], c[7], c[6]], 'Wood')
    s.face([c[4], c[6], c[7], c[5]], 'Wood'); s.face([c[0], c[1], c[3], c[2]], 'Wood')
    return s.finish('HUMAN', check=False)


VIEWS = [(-45, 'FRONT-LEFT'), (45, 'FRONT-RIGHT'), (135, 'REAR-RIGHT'), (-135, 'REAR-LEFT')]


def renders(objs, outdir, sheet_png, extra=(), el=20, size=(360, 360), samples=8, title=None, az_off=0):
    """extra: list of (label, objs, kwargs) additional renders."""
    os.makedirs(outdir, exist_ok=True)
    tiles, labels = [], []
    for az, lab in VIEWS:
        p = os.path.join(outdir, 'v_%s.png' % lab.lower())
        ak.render(objs, p, az=az + az_off, el=el, size=size, samples=samples)
        tiles.append(p); labels.append(lab)
    for lab, eo, kw in extra:
        p = os.path.join(outdir, 'x_%s.png' % lab.lower().replace(' ', '_'))
        k = dict(az=-30, el=20, size=size, samples=samples); k.update(kw)
        ak.render(eo, p, **k)
        tiles.append(p); labels.append(lab)
    card = os.path.join(outdir, 'card.jpg')
    ak.sheet(tiles, card, cols=4, labels=labels, concept=sheet_png, title=title)
    return card


def write_manifest(outdir, recs):
    os.makedirs(outdir, exist_ok=True)
    json.dump(recs, open(os.path.join(outdir, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)


def wants_render():
    return '--norender' not in sys.argv


def stats(vid, objs, solid):
    lo, hi = ak.bounds(objs)
    print('%s tris=%d open_edges=%d bounds=%s..%s' % (vid, ak.tris(objs), solid.open_edges,
          [round(float(v), 3) for v in lo], [round(float(v), 3) for v in hi]))


# ================================================================ standard wall section (AN01 wall, AN02 corner)
# Section in (d, z): d = offset from the wall centre line, + toward the room (front), metres.
# JOIN PROFILE (exact at every join end):  front d = 0.40 + 0.04 z (leans 0.16 m into the room at the top),
# top z = 4.0, back d = -0.40.   i.e. polygon (d,z): (0.40,0) (0.56,4.0) (-0.40,4.0) (-0.40,0).
FZ = [0.0, 0.45, 1.05, 1.13, 1.85, 2.55, 2.62, 3.2, 3.65, 4.0]
FH = [0.62, 0.85, 1.0, 0.9, 1.0, 0.97, 0.85, 0.66, 0.22, 0.0]
FL = [0, 0, 0, 0.09, 0, 0, 0.08, 0, 0, 0]                           # strata ledges (protrusion)
BZ = [4.0, 3.0, 1.9, 0.8, 0.0]
WALL_PROFILE = [(0.40, 0.0), (0.56, 4.0), (-0.40, 4.0), (-0.40, 0.0)]


def sn(s, k, ph):
    """smooth pseudo-noise along a path (-1..1)"""
    return 0.6 * math.sin(1.9 * s + 2.7 * k + ph) + 0.4 * math.sin(4.3 * s + 1.3 * k + 2.1 * ph)


def wall_ring(w, rnd, depth=0.32, lift=0.18, brow=0.0, bulge=0.10, wb=None, s=0.0, ph=0.0):
    """w: recess weight 0 (join/rim) .. 1 (middle of the bay). wb: back bulge weight (default w).
    s: path distance (smooth noise), ph: per-piece phase."""
    wb = w if wb is None else wb
    R = []
    ztop = 4.0 + lift * w * (0.65 + 0.35 * sn(s, 9, ph))
    for i, z in enumerate(FZ):
        d = 0.40 + 0.04 * z
        if i == len(FZ) - 1:
            z = ztop
            d += brow * w * (0.8 + 0.3 * sn(s, 7, ph)) + 0.02 * w * rnd.uniform(-1, 1)
        else:
            d -= depth * w * FH[i] * (0.97 + 0.22 * sn(s, i, ph))
            d += FL[i] * w * (0.85 + 0.45 * sn(s * 0.8, 3, ph))
            if 0 < i:
                z += 0.08 * w * sn(s, i + 4, ph) + 0.025 * w * rnd.uniform(-1, 1)
                d += 0.02 * w * rnd.uniform(-1, 1)
        R.append((d, z))
    R.append((0.06 + 0.12 * wb * sn(s, 11, ph), ztop + 0.05 * w * rnd.uniform(0, 1)))
    for i, z in enumerate(BZ):
        d = -0.40
        if i == 0:
            z = 4.0 + lift * 0.5 * w * (0.5 + 0.5 * sn(s, 12, ph))
        if 0 < i < len(BZ) - 1:
            z += 0.1 * wb * sn(s, 13 + i, ph)
        if i > 0:
            d -= bulge * wb * math.sin(math.pi * min(z, 4.0) / 4.4 + 0.25) * (0.95 + 0.3 * sn(s, 17 + i, ph))
        R.append((d, z))
    return R


def sweep(solid, nodes, ring_fn, mats='Earth', tan_jitter=0.06, rnd=None, end_exact=True):
    """nodes: 2D path points (x, y). Front normal = tangent rotated -90 deg (path +x -> front -y).
    ring_fn(e, s, i) -> list of (d, z); e = path distance to the nearest end, s = distance from start."""
    rnd = rnd or random.Random(0)
    P = [Vector((x, y, 0)) for x, y in nodes]
    L = [0.0]
    for i in range(1, len(P)):
        L.append(L[-1] + (P[i] - P[i - 1]).length)
    secs = []
    for i, p in enumerate(P):
        a = P[max(i - 1, 0)]; b = P[min(i + 1, len(P) - 1)]
        if 0 < i < len(P) - 1:
            t1 = (p - a).normalized(); t2 = (b - p).normalized()
            t = (t1 + t2).normalized()
            miter = 1.0 / max(0.5, t.dot(t2))
        else:
            t = (b - a).normalized(); miter = 1.0
        n = Vector((t.y, -t.x, 0))
        e = min(L[i], L[-1] - L[i])
        ring = ring_fn(e, L[i], i)
        end = end_exact and (i == 0 or i == len(P) - 1)
        sec = []
        for d, z in ring:
            q = p + n * d * miter + Vector((0, 0, z))
            if not end:
                q += t * tan_jitter * rnd.uniform(-1, 1)
            sec.append(q)
        secs.append(sec)
    solid.loft(secs, mats)
    return secs


def lump(solid, center, radii, rnd, rings=3, sides=7, jitter=0.18, rot=0.0, flat_z=None, mat='Earth'):
    """Closed faceted clod (jittered low-poly ellipsoid). flat_z: clamp vertices below this z (sits on floor)."""
    C = Vector(center); rx, ry, rz = radii
    pts = []
    for i in range(1, rings):
        lat = -math.pi / 2 + math.pi * i / rings
        ring = []
        for k in range(sides):
            a = rot + 2 * math.pi * (k + 0.5 * (i % 2)) / sides
            f = 1 + rnd.uniform(-jitter, jitter)
            ring.append(C + Vector((math.cos(a) * math.cos(lat) * rx * f, math.sin(a) * math.cos(lat) * ry * f,
                                    math.sin(lat) * rz * f)))
        pts.append(ring)
    top = C + Vector((rnd.uniform(-.2, .2) * rx, rnd.uniform(-.2, .2) * ry, rz * rnd.uniform(0.85, 1.1)))
    bot = C + Vector((0, 0, -rz * rnd.uniform(0.85, 1.1)))
    if flat_z is not None:
        cl = lambda p: Vector((p.x, p.y, max(p.z, flat_z)))
        pts = [[cl(p) for p in r] for r in pts]; bot = cl(bot); top = cl(top)
    for i in range(len(pts) - 1):
        for k in range(sides):
            k2 = (k + 1) % sides
            solid.quad(pts[i][k], pts[i][k2], pts[i + 1][k2], pts[i + 1][k], mat)
    for k in range(sides):
        k2 = (k + 1) % sides
        solid.face([pts[-1][k], pts[-1][k2], top], mat)
        solid.face([pts[0][k2], pts[0][k], bot], mat)
