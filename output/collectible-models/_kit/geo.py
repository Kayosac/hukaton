"""Mesh building helpers for Hakaton collectible items (bpy/bmesh).

Conventions
- Blender metres, Z up. The item's FRONT faces -Y (Blender front view). Pivot = bottom centre at the origin, min Z = 0.
- A mesh is built from named PARTS. Every face carries the part as its material slot; the bake uses parts to keep
  colours from bleeding (handle pixels never land on the body) and to fill hidden areas with that part's palette.
- Keep geometry simple and crooked (PSX): few sides (5-10), few rings, meaningful asymmetry. Paint small details.

Usage
    from geo import Builder
    b = Builder('jug')
    b.lathe('body', [(0.00,0.0),(0.07,0.0),(0.09,0.05),...], sides=8, jitter=0.003)
    b.tube('handle', [(x,y,z),...], radius=0.01, sides=5)
    obj = b.finish()           # one object, parts = material slots, smooth/flat flags, pivot at bottom centre
"""
import math, random
import bpy, bmesh
from mathutils import Vector, Matrix

def _rng(seed):
    r = random.Random(seed)
    return r

class Builder:
    def __init__(self, name, seed=1):
        self.name = name
        self.bm = bmesh.new()
        self.parts = []          # part names in material-slot order
        self.flat_parts = set()  # parts drawn with flat shading (hard faceted look)
        self.rng = _rng(seed)

    # ---------- bookkeeping ----------
    def part(self, name, flat=True):
        if name not in self.parts:
            self.parts.append(name)
        if flat:
            self.flat_parts.add(name)
        else:
            self.flat_parts.discard(name)
        return self.parts.index(name)

    def j(self, a):
        return self.rng.uniform(-a, a)

    def _face(self, verts, mi):
        try:
            f = self.bm.faces.new(verts)
        except ValueError:
            return None
        f.material_index = mi
        return f

    def ring_verts(self, pts):
        return [self.bm.verts.new(p) for p in pts]

    def bridge(self, r0, r1, mi, closed=True):
        n = len(r0)
        rng = range(n if closed else n - 1)
        for i in rng:
            k = (i + 1) % n
            self._face([r0[i], r0[k], r1[k], r1[i]], mi)

    def cap(self, ring, mi, flip=False, center=None):
        """Fan-cap a ring. With center=(x,y,z) a centre vertex is added (cone/pointed cap)."""
        if center is None:
            vs = list(reversed(ring)) if flip else list(ring)
            return self._face(vs, mi)
        c = self.bm.verts.new(center)
        n = len(ring)
        for i in range(n):
            a, b_ = ring[i], ring[(i + 1) % n]
            self._face([a, c, b_] if not flip else [b_, c, a], mi)

    # ---------- primitives ----------
    def ring(self, center, radius, sides, z_axis=(0, 0, 1), rot=0.0, jitter=0.0, scale=(1, 1), wobble=None):
        """Points of a polygon ring around `center` in the plane normal to z_axis."""
        zc = Vector(z_axis).normalized()
        xc = Vector((1, 0, 0)) if abs(zc.x) < .9 else Vector((0, 1, 0))
        xc = (xc - zc * xc.dot(zc)).normalized()
        yc = zc.cross(xc)
        pts = []
        for i in range(sides):
            a = rot + 2 * math.pi * i / sides
            r = radius * (1 + (wobble[i % len(wobble)] if wobble else 0))
            p = Vector(center) + xc * (math.cos(a) * r * scale[0]) + yc * (math.sin(a) * r * scale[1])
            p += Vector((self.j(jitter), self.j(jitter), self.j(jitter)))
            pts.append(p)
        return pts

    def lathe(self, part, profile, sides=8, jitter=0.0, rot=None, cap_bottom=True, cap_top=True, flat=True,
              offset=(0, 0, 0), lean=(0, 0), scale=(1, 1), top_center=None, bottom_center=None):
        """Revolve a profile [(r, z), ...] bottom->top around Z. lean=(dx,dy) per metre of height tilts the axis.
        Zero radius at an end becomes a point. Returns list of rings (vert lists)."""
        mi = self.part(part, flat)
        rot = self.rng.uniform(0, math.pi) if rot is None else rot
        rings = []
        for (r, z) in profile:
            c = (offset[0] + lean[0] * z, offset[1] + lean[1] * z, offset[2] + z)
            if r <= 1e-6:
                rings.append([self.bm.verts.new(c)])
            else:
                rings.append(self.ring_verts(self.ring(c, r, sides, rot=rot, jitter=jitter, scale=scale)))
        for a, b_ in zip(rings, rings[1:]):
            if len(a) == 1 and len(b_) == 1:
                continue
            if len(a) == 1:
                for i in range(len(b_)):
                    self._face([a[0], b_[(i + 1) % len(b_)], b_[i]], mi)
            elif len(b_) == 1:
                for i in range(len(a)):
                    self._face([a[i], a[(i + 1) % len(a)], b_[0]], mi)
            else:
                self.bridge(a, b_, mi)
        if cap_bottom and len(rings[0]) > 1:
            self.cap(rings[0], mi, flip=True, center=bottom_center)
        if cap_top and len(rings[-1]) > 1:
            self.cap(rings[-1], mi, center=top_center)
        return rings

    def loft(self, part, rings_pts, closed_ends=(True, True), flat=True):
        """Bridge consecutive point rings (equal counts)."""
        mi = self.part(part, flat)
        rings = [self.ring_verts(r) for r in rings_pts]
        for a, b_ in zip(rings, rings[1:]):
            self.bridge(a, b_, mi)
        if closed_ends[0]:
            self.cap(rings[0], mi, flip=True)
        if closed_ends[1]:
            self.cap(rings[-1], mi)
        return rings

    def tube(self, part, path, radius=0.01, sides=6, caps=True, flat=True, radii=None, jitter=0.0, closed=False):
        """Sweep a polygon along a polyline (list of xyz). radii: per-point radius. closed: ring path (torus)."""
        mi = self.part(part, flat)
        P = [Vector(p) for p in path]
        n = len(P)
        rings = []
        prev_x = None
        for i in range(n):
            if closed:
                t = (P[(i + 1) % n] - P[i - 1]).normalized()
            else:
                t = (P[min(i + 1, n - 1)] - P[max(i - 1, 0)]).normalized()
            if prev_x is None:
                x = Vector((0, 0, 1)) if abs(t.z) < .9 else Vector((1, 0, 0))
            else:
                x = prev_x
            x = (x - t * x.dot(t)).normalized()
            prev_x = x
            y = t.cross(x)
            r = radii[i] if radii else radius
            pts = []
            for k in range(sides):
                a = 2 * math.pi * k / sides
                p = P[i] + x * math.cos(a) * r + y * math.sin(a) * r
                p += Vector((self.j(jitter), self.j(jitter), self.j(jitter)))
                pts.append(p)
            rings.append(self.ring_verts(pts))
        segs = n if closed else n - 1
        for i in range(segs):
            self.bridge(rings[i], rings[(i + 1) % n], mi)
        if caps and not closed:
            self.cap(rings[0], mi, flip=True)
            self.cap(rings[-1], mi)
        return rings

    def box(self, part, center, size, jitter=0.0, flat=True, taper=(1, 1), shear=(0, 0), rot_z=0.0, top_offset=(0, 0)):
        """Crooked box: size=(sx,sy,sz) about center; taper scales the top face; shear/top_offset shifts the top."""
        mi = self.part(part, flat)
        cx, cy, cz = center
        sx, sy, sz = [s / 2 for s in size]
        cr, sr = math.cos(rot_z), math.sin(rot_z)
        def P(x, y, z, top):
            if top:
                x, y = x * taper[0] + top_offset[0] + shear[0], y * taper[1] + top_offset[1] + shear[1]
            x, y = x * cr - y * sr, x * sr + y * cr
            return Vector((cx + x + self.j(jitter), cy + y + self.j(jitter), cz + z + self.j(jitter)))
        b = [self.bm.verts.new(P(x, y, -sz, False)) for x, y in ((-sx, -sy), (sx, -sy), (sx, sy), (-sx, sy))]
        t = [self.bm.verts.new(P(x, y, sz, True)) for x, y in ((-sx, -sy), (sx, -sy), (sx, sy), (-sx, sy))]
        self._face(list(reversed(b)), mi)
        self._face(t, mi)
        self.bridge(b, t, mi)
        return b, t

    def blob(self, part, center, radii, rings=4, sides=8, jitter=0.0, flat=True, squash_top=1.0):
        """Low-poly ellipsoid (UV sphere with few rings)."""
        cx, cy, cz = center
        rx, ry, rz = radii
        prof = []
        for i in range(rings + 1):
            th = math.pi * i / rings
            prof.append((math.sin(th), -math.cos(th)))
        mi = self.part(part, flat)
        rot = self.rng.uniform(0, math.pi)
        rs = []
        for s, c in prof:
            z = cz + c * rz * (squash_top if c > 0 else 1)
            if s < 1e-6:
                rs.append([self.bm.verts.new((cx, cy, z))])
            else:
                rs.append(self.ring_verts(self.ring((cx, cy, z), 1.0, sides, rot=rot, jitter=jitter, scale=(rx * s, ry * s))))
        for a, b_ in zip(rs, rs[1:]):
            if len(a) == 1:
                for i in range(len(b_)):
                    self._face([a[0], b_[(i + 1) % len(b_)], b_[i]], mi)
            elif len(b_) == 1:
                for i in range(len(a)):
                    self._face([a[i], a[(i + 1) % len(a)], b_[0]], mi)
            else:
                self.bridge(a, b_, mi)
        return rs

    def add_bmesh_geometry(self, part, verts, faces, flat=True):
        """Raw geometry: verts list of xyz, faces as index lists."""
        mi = self.part(part, flat)
        vs = [self.bm.verts.new(v) for v in verts]
        for f in faces:
            self._face([vs[i] for i in f], mi)

    # ---------- finish ----------
    def finish(self, orient_shells=True, ground=True, center_xy=True):
        """Primitives are wound outward by construction. orient_shells flips any closed shell whose signed volume is
        negative (safety net for raw add_bmesh_geometry input). bmesh recalc_face_normals is NOT used: it flips
        correct lathe/tube shells when they intersect other parts."""
        bm = self.bm
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
        if orient_shells:
            bm.faces.ensure_lookup_table()
            seen = set()
            for f0 in bm.faces:
                if f0.index in seen:
                    continue
                shell, stack = [], [f0]
                seen.add(f0.index)
                while stack:
                    f = stack.pop(); shell.append(f)
                    for e in f.edges:
                        for g in e.link_faces:
                            if g.index not in seen:
                                seen.add(g.index); stack.append(g)
                closed = all(len(e.link_faces) == 2 for f in shell for e in f.edges)
                if not closed:
                    continue
                vol = 0.0
                for f in shell:
                    vs = [v.co for v in f.verts]
                    for i in range(1, len(vs) - 1):
                        vol += vs[0].dot(vs[i].cross(vs[i + 1]))
                if vol < 0:
                    bmesh.ops.reverse_faces(bm, faces=shell)
        me = bpy.data.meshes.new(self.name)
        bm.to_mesh(me)
        bm.free()
        obj = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(obj)
        for p in self.parts:
            m = bpy.data.materials.get('PART_' + p) or bpy.data.materials.new('PART_' + p)
            me.materials.append(m)
        # shading: flat parts faceted, others smooth
        for poly in me.polygons:
            poly.use_smooth = self.parts[poly.material_index] not in self.flat_parts
        obj['parts'] = list(self.parts)
        if ground or center_xy:
            xs = [v.co.x for v in me.vertices]; ys = [v.co.y for v in me.vertices]; zs = [v.co.z for v in me.vertices]
            dx = -(min(xs) + max(xs)) / 2 if center_xy else 0
            dy = -(min(ys) + max(ys)) / 2 if center_xy else 0
            dz = -min(zs) if ground else 0
            me.transform(Matrix.Translation((dx, dy, dz)))
        return obj


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def stats(obj):
    me = obj.data
    me.calc_loop_triangles()
    tris = len(me.loop_triangles)
    xs = [v.co.x for v in me.vertices]; ys = [v.co.y for v in me.vertices]; zs = [v.co.z for v in me.vertices]
    size = (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))
    return {'tris': tris, 'verts': len(me.vertices), 'size_xyz_m': [round(s, 4) for s in size],
            'min_z': round(min(zs), 5)}


# Shelf slot (ShopArt README: recommended goods volume X<=0.28 Y<=0.23 Z<=0.39; top row clearance 0.24).
# For a free fit in any orientation: height <= 0.22 and the horizontal footprint inside 0.26 x 0.26.
FIT = {'max_height': 0.22, 'max_footprint': 0.26}

def check_fit(obj):
    s = stats(obj)
    sx, sy, sz = s['size_xyz_m']
    ok = sz <= FIT['max_height'] and sx <= FIT['max_footprint'] and sy <= FIT['max_footprint'] and abs(s['min_z']) < 1e-4
    s['fits_shelf'] = ok
    return s
