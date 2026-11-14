"""Rotten Swamp modelling kit (bpy 5.3 as a Python module). See README.md.

Every asset variant = several Blender objects, ONE per material, named '<VID>_<Mat>' (like ForestArt 'DW08_Bark'),
parented to nothing, all sharing the same origin = the asset pivot (base centre, Z up, front faces -Y).
"""
import math, os, json, random
import numpy as np
import bpy, bmesh
from mathutils import Vector, Matrix

KIT = os.path.dirname(os.path.abspath(__file__))
TEX = os.path.join(KIT, 'tex')
REG = json.load(open(os.path.join(TEX, 'regions.json')))

# material -> (texture, kind, metres per texture repeat for tiling materials)
MATS = {
    'Bark':       ('T_Swamp_Bark.png', 'tile', 1.6),
    'BarkMossy':  ('T_Swamp_BarkMossy.png', 'tile', 1.6),
    'Birch':      ('T_Swamp_Birch.png', 'tile', 1.2),
    'Planks':     ('T_Swamp_Planks.png', 'tile', 1.6),
    'LogWall':    ('T_Swamp_LogWall.png', 'tile', 1.6),   # 5 logs per repeat -> logs ~0.32 m thick
    'Roof':       ('T_Swamp_Roof.png', 'tile', 2.0),
    'Mud':        ('T_Swamp_Mud.png', 'tile', 1.6),
    'Peat':       ('T_Swamp_Peat.png', 'tile', 1.2),
    'Stone':      ('T_Swamp_Stone.png', 'tile', 1.6),   # foundation stones, boulders   # hummock / mound walls (purple-brown peat)
    'Moss':       ('T_Swamp_Moss.png', 'tile', 0.8),
    'Props':      ('T_Swamp_Props.png', 'atlas', None),
    'Plants':     ('T_Swamp_Plants.png', 'cutout', None),   # alpha cutout + wind in Unity
}

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    random.seed(1)

# ================================================================ mesh writer
class Mesh:
    """Accumulates geometry for ONE material. All helpers set UVs immediately (uv layer 'UVMap')
    and per-vertex wind amplitudes (sway, flutter in metres) for the 'Wind' UV2 channel."""
    def __init__(self, mat):
        assert mat in MATS, mat
        self.mat = mat
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new('UVMap')
        self.wind = self.bm.loops.layers.uv.new('Wind')
        self.flat = True

    # -- low level
    def face(self, pts, uvs, wind=None):
        vs = [self.bm.verts.new(Vector(p)) for p in pts]
        try:
            f = self.bm.faces.new(vs)
        except ValueError:
            return None
        for i, l in enumerate(f.loops):
            l[self.uv].uv = uvs[i]
            w = wind[i] if wind else (0.0, 0.0)
            l[self.wind].uv = (1000.0 + w[0], 1000.0 + w[1])
        return f

    def quad(self, a, b, c, d, uva, uvb, uvc, uvd, wind=None):
        return self.face([a, b, c, d], [uva, uvb, uvc, uvd], wind)

    def tri(self, a, b, c, ua, ub, uc, wind=None):
        return self.face([a, b, c], [ua, ub, uc], wind)

    # -- region helpers (atlas materials)
    @staticmethod
    def region(name, mat='Props'):
        tex = MATS[mat][0]
        R = REG[tex]
        x, y, w, h = R['regions'][name]
        S = R['size']
        # return (u0, v0, u1, v1) in UV space (v up). small inset avoids bleeding with point sampling
        e = 0.5 / S
        return (x / S + e, 1 - (y + h) / S + e, (x + w) / S - e, 1 - y / S - e)

    def tile(self):
        return MATS[self.mat][2]

    # -- primitives with UVs ------------------------------------------------
    def tube(self, path, radii, sides=6, rot=0.0, caps=(True, True), jitter=0.0, seed=0, uv_u=None,
             cap_region=None, wind=None, twist=0.0, scale=None, up_hint=None):
        """Crooked log/branch/trunk. path: list of xyz. radii: float or per-point list.
        UV: tiling texture wraps around (u) and runs along the length (v) -> bark grain follows the branch.
        caps: end faces; cap_region (atlas region name on Props) is NOT possible on a tiling material, so caps
        use the same texture planar-mapped. Use end_cap() on a 'Props' Mesh to add an end-grain disc instead.
        wind: callable(point, t_along)->(sway, flutter) or None. scale: per-point (sx, sy) ellipse factors.
        Returns rings (list of lists of Vectors)."""
        rnd = random.Random(seed)
        P = [Vector(p) for p in path]
        n = len(P)
        if not isinstance(radii, (list, tuple)):
            radii = [radii] * n
        tile = self.tile() or 1.0
        rings = []
        prev_x = None
        L = [0.0]
        for i in range(1, n):
            L.append(L[-1] + (P[i] - P[i - 1]).length)
        for i in range(n):
            t = (P[min(i + 1, n - 1)] - P[max(i - 1, 0)]).normalized()
            if prev_x is None:
                hint = Vector(up_hint) if up_hint else (Vector((0, 0, 1)) if abs(t.z) < .9 else Vector((1, 0, 0)))
                x = hint
            else:
                x = prev_x
            x = (x - t * x.dot(t)).normalized()
            prev_x = x
            y = t.cross(x)
            r = radii[i]
            sx, sy = scale[i] if scale else (1, 1)
            ring = []
            for k in range(sides):
                a = rot + twist * i + 2 * math.pi * k / sides
                p = P[i] + x * math.cos(a) * r * sx + y * math.sin(a) * r * sy
                p += Vector((rnd.uniform(-jitter, jitter), rnd.uniform(-jitter, jitter), rnd.uniform(-jitter, jitter)))
                ring.append(p)
            rings.append(ring)
        circ = [2 * math.pi * radii[i] for i in range(n)]
        for i in range(n - 1):
            for k in range(sides):
                k2 = (k + 1) % sides
                u0 = (k / sides) * max(circ[i], circ[i + 1]) / tile
                u1 = ((k + 1) / sides) * max(circ[i], circ[i + 1]) / tile
                v0 = L[i] / tile; v1 = L[i + 1] / tile
                wv = None
                if wind:
                    wa = wind(rings[i][k], L[i] / max(L[-1], 1e-6)); wb = wind(rings[i][k2], L[i] / max(L[-1], 1e-6))
                    wc = wind(rings[i + 1][k2], L[i + 1] / max(L[-1], 1e-6)); wd = wind(rings[i + 1][k], L[i + 1] / max(L[-1], 1e-6))
                    wv = [wa, wb, wc, wd]
                self.quad(rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k],
                          (u0, v0), (u1, v0), (u1, v1), (u0, v1), wv)
        if caps[0]:
            self.cap(rings[0], flip=True)
        if caps[1]:
            self.cap(rings[-1])
        return rings

    def cap(self, ring, flip=False, region=None, center=None):
        """Planar cap of a ring. region: atlas region name (Props/Plants) to fit the cap into (e.g. 'end_grain_0').
        center: optional apex point (cone cap)."""
        ring = list(reversed(ring)) if flip else list(ring)
        c = sum((Vector(p) for p in ring), Vector()) / len(ring)
        nrm = Vector((0, 0, 0))
        for i in range(len(ring)):
            nrm += (Vector(ring[i]) - c).cross(Vector(ring[(i + 1) % len(ring)]) - c)
        nrm.normalize()
        ax = (Vector(ring[0]) - c).normalized()
        ay = nrm.cross(ax)
        rad = max((Vector(p) - c).length for p in ring) or 1.0
        def uvof(p):
            d = Vector(p) - c
            if region:
                u0, v0, u1, v1 = self.region(region, self.mat)
                return (u0 + (d.dot(ax) / rad * .5 + .5) * (u1 - u0), v0 + (d.dot(ay) / rad * .5 + .5) * (v1 - v0))
            t = self.tile() or 1.0
            return (d.dot(ax) / t, d.dot(ay) / t)
        if center is None:
            return self.face(ring, [uvof(p) for p in ring])
        for i in range(len(ring)):
            a, b = ring[i], ring[(i + 1) % len(ring)]
            self.tri(a, b, center, uvof(a), uvof(b), uvof(center) if region else (0.5, 0.5))

    def box(self, center, size, rot=(0, 0, 0), jitter=0.0, seed=0, taper=(1, 1), top_shift=(0, 0), grain='z',
            region=None, skip=()):
        """Crooked box (planks, beams, posts, walls). size=(sx,sy,sz). rot=euler radians (XYZ).
        grain: which local axis the texture V runs along ('x','y','z') -> plank/beam grain direction.
        region: map every face into one atlas region (Props) instead of tiling. skip: face names to omit
        ('top','bottom','px','nx','py','ny'). Returns dict of 8 corner points."""
        rnd = random.Random(seed)
        sx, sy, sz = [s / 2 for s in size]
        M = Matrix.Translation(Vector(center)) @ Matrix.Rotation(rot[2], 4, 'Z') @ Matrix.Rotation(rot[1], 4, 'Y') @ Matrix.Rotation(rot[0], 4, 'X')
        def P(x, y, z):
            if z > 0:
                x = x * taper[0] + top_shift[0]; y = y * taper[1] + top_shift[1]
            v = M @ Vector((x, y, z))
            return v + Vector((rnd.uniform(-jitter, jitter), rnd.uniform(-jitter, jitter), rnd.uniform(-jitter, jitter)))
        c = {}
        for ix in (-1, 1):
            for iy in (-1, 1):
                for iz in (-1, 1):
                    c[(ix, iy, iz)] = P(ix * sx, iy * sy, iz * sz)
        faces = {
            'top': [(-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)],
            'bottom': [(-1, 1, -1), (1, 1, -1), (1, -1, -1), (-1, -1, -1)],
            'px': [(1, -1, -1), (1, 1, -1), (1, 1, 1), (1, -1, 1)],
            'nx': [(-1, 1, -1), (-1, -1, -1), (-1, -1, 1), (-1, 1, 1)],
            'py': [(1, 1, -1), (-1, 1, -1), (-1, 1, 1), (1, 1, 1)],
            'ny': [(-1, -1, -1), (1, -1, -1), (1, -1, 1), (-1, -1, 1)],
        }
        gi = 'xyz'.index(grain)
        for name, idx in faces.items():
            if name in skip:
                continue
            pts = [c[i] for i in idx]
            loc = [Vector((i[0] * sx, i[1] * sy, i[2] * sz)) for i in idx]
            if region:
                u0, v0, u1, v1 = self.region(region, self.mat)
                # local planar coords of this face, normalised into region
                nrm_axis = {'top': 2, 'bottom': 2, 'px': 0, 'nx': 0, 'py': 1, 'ny': 1}[name]
                axes = [a for a in range(3) if a != nrm_axis]
                if gi in axes:
                    axes = [a for a in axes if a != gi] + [gi]
                uvs = []
                for l in loc:
                    a = (l[axes[0]] / (2 * [sx, sy, sz][axes[0]]) + .5)
                    b = (l[axes[1]] / (2 * [sx, sy, sz][axes[1]]) + .5)
                    uvs.append((u0 + a * (u1 - u0), v0 + b * (v1 - v0)))
            else:
                t = self.tile() or 1.0
                nrm_axis = {'top': 2, 'bottom': 2, 'px': 0, 'nx': 0, 'py': 1, 'ny': 1}[name]
                axes = [a for a in range(3) if a != nrm_axis]
                if gi in axes:
                    axes = [a for a in axes if a != gi] + [gi]
                uvs = [(l[axes[0]] / t + center[0] * .37, l[axes[1]] / t + center[1] * .29 + center[2] * .17) for l in loc]
            self.face(pts, uvs)
        return c

    def planar(self, pts, axis_u, axis_v, origin=None, region=None, wind=None):
        """Any polygon with planar UV along axis_u/axis_v (metres -> tiles), or fitted into an atlas region
        (region given: axis lengths are the region's metric size: uv = dot/len)."""
        o = Vector(origin) if origin else Vector(pts[0])
        au, av = Vector(axis_u), Vector(axis_v)
        uvs = []
        for p in pts:
            d = Vector(p) - o
            if region:
                u0, v0, u1, v1 = self.region(region, self.mat)
                uvs.append((u0 + d.dot(au.normalized()) / au.length * (u1 - u0), v0 + d.dot(av.normalized()) / av.length * (v1 - v0)))
            else:
                t = self.tile() or 1.0
                uvs.append((d.dot(au.normalized()) / t, d.dot(av.normalized()) / t))
        return self.face(pts, uvs, wind)

    def card(self, base, right, up, region, bend=0.0, segs=1, wind_sway=0.0, wind_flutter=0.0, hang=False,
             mat_check=True):
        """Alpha card (Plants atlas) fitted to a region. base = bottom-centre point (or TOP-centre if hang=True,
        then the card extends along -up). right = half-width vector, up = full height vector.
        bend: tip offset along the card normal (metres) applied quadratically; segs: vertical segments.
        Wind sway grows with height^2 from the base (or from the hang point), flutter on the free part."""
        u0, v0, u1, v1 = self.region(region, self.mat)
        B, R, U = Vector(base), Vector(right), Vector(up)
        N = R.normalized().cross(U.normalized())
        rows = []
        for s in range(segs + 1):
            t = s / segs
            if hang:
                p = B - U * t + N * bend * t * t
            else:
                p = B + U * t + N * bend * t * t
            rows.append((p - R, p + R, t))
        for s in range(segs):
            (a0, b0, t0), (a1, b1, t1) = rows[s], rows[s + 1]
            if hang:
                va0, va1 = v1 - (v1 - v0) * t0, v1 - (v1 - v0) * t1
            else:
                va0, va1 = v0 + (v1 - v0) * t0, v0 + (v1 - v0) * t1
            w0 = (wind_sway * t0 * t0, wind_flutter * t0)
            w1 = (wind_sway * t1 * t1, wind_flutter * t1)
            self.quad(a0, b0, b1, a1, (u0, va0), (u1, va0), (u1, va1), (u0, va1), [w0, w0, w1, w1])

    def cross_cards(self, base, width, height, region, n=3, rot=0.0, lean=(0, 0), bend=0.05, segs=2,
                    wind_sway=0.06, wind_flutter=0.02, seed=0):
        """n intersecting vertical cards around a point (grass/sedge/reed tufts)."""
        rnd = random.Random(seed)
        B = Vector(base)
        for i in range(n):
            a = rot + math.pi * i / n + rnd.uniform(-.2, .2)
            R = Vector((math.cos(a), math.sin(a), 0)) * width / 2
            U = Vector((lean[0], lean[1], 1)).normalized() * height
            self.card(B, R, U, region, bend=bend * rnd.uniform(.5, 1.2), segs=segs, wind_sway=wind_sway, wind_flutter=wind_flutter)

    def flat_card(self, center, size, region, rot=0.0, z_jitter=0.0, seed=0, wind_flutter=0.0):
        """Horizontal card on the water (duckweed, lily pads). size=(sx, sy)."""
        rnd = random.Random(seed)
        C = Vector(center)
        ca, sa = math.cos(rot), math.sin(rot)
        hx, hy = size[0] / 2, size[1] / 2
        pts = []
        for (x, y) in ((-hx, -hy), (hx, -hy), (hx, hy), (-hx, hy)):
            pts.append(C + Vector((x * ca - y * sa, x * sa + y * ca, rnd.uniform(-z_jitter, z_jitter))))
        u0, v0, u1, v1 = self.region(region, self.mat)
        w = [(0, wind_flutter)] * 4
        return self.face(pts, [(u0, v0), (u1, v0), (u1, v1), (u0, v1)], w)

    # -- finish
    def to_object(self, name, smooth=False):
        bm = self.bm
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        for p in me.polygons:
            p.use_smooth = smooth
        obj = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(obj)
        me.materials.append(material(self.mat))
        obj['mat'] = self.mat
        return obj

# ================================================================ materials
_MAT_CACHE = {}
def material(key):
    name = 'M_Swamp_' + key
    m = bpy.data.materials.get(name)
    if m:
        return m
    tex, kind, _ = MATS[key]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes.get('Principled BSDF')
    t = nt.nodes.new('ShaderNodeTexImage')
    img = bpy.data.images.get(tex) or bpy.data.images.load(os.path.join(TEX, tex), check_existing=True)
    t.image = img
    t.interpolation = 'Closest'
    t.extension = 'REPEAT'
    nt.links.new(t.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = 1.0
    if 'Specular IOR Level' in bsdf.inputs:
        bsdf.inputs['Specular IOR Level'].default_value = 0.05
    if kind == 'cutout':
        # hard alpha clip like the Unity cutout material
        mth = nt.nodes.new('ShaderNodeMath'); mth.operation = 'GREATER_THAN'; mth.inputs[1].default_value = 0.5
        nt.links.new(t.outputs['Alpha'], mth.inputs[0])
        nt.links.new(mth.outputs[0], bsdf.inputs['Alpha'])
        try:
            m.surface_render_method = 'DITHERED'
        except Exception:
            pass
    return m

# ================================================================ moss drape
def moss_drape(src_obj, name, min_nz=0.55, offset=0.012, keep=0.75, seed=0, drips=None):
    """Copy the upward faces of src_obj (normal.z >= min_nz) as a thin moss skin (material Moss), randomly keeping
    `keep` of them. drips: a Plants Mesh -> hang 'moss_drip_*' cards from the outer edges of the skin.
    Returns the moss object (or None)."""
    rnd = random.Random(seed)
    me = src_obj.data
    mm = Mesh('Moss')
    tile = MATS['Moss'][2]
    edges = {}
    for p in me.polygons:
        n = p.normal
        if n.z < min_nz or rnd.random() > keep:
            continue
        pts = [me.vertices[i].co + n * offset for i in p.vertices]
        mm.face(pts, [(q.x / tile, q.y / tile) for q in pts])
        for k in range(len(pts)):
            e = (tuple(round(c, 4) for c in pts[k]), tuple(round(c, 4) for c in pts[(k + 1) % len(pts)]))
            key = tuple(sorted(e))
            edges[key] = edges.get(key, 0) + 1
    if not mm.bm.faces:
        mm.bm.free()
        return None
    if drips is not None:
        for (a, b), cnt in edges.items():
            if cnt != 1 or rnd.random() > 0.55:
                continue
            A, B = Vector(a), Vector(b)
            L = (B - A).length
            if L < 0.08:
                continue
            mid = (A + B) / 2
            R = (B - A) / 2
            h = min(0.35, L * rnd.uniform(0.35, 0.7))
            drips.card(mid + Vector((0, 0, 0.005)), R, Vector((0, 0, h)), 'moss_drip_%d' % rnd.randint(0, 1), hang=True,
                       segs=1, wind_sway=0.0, wind_flutter=0.01)
    return mm.to_object(name)

# ================================================================ stats / export
def tris(objs):
    t = 0
    for o in objs:
        o.data.calc_loop_triangles(); t += len(o.data.loop_triangles)
    return t

def bounds(objs):
    V = np.concatenate([np.array([(o.matrix_world @ v.co)[:] for v in o.data.vertices]) for o in objs])
    return V.min(0), V.max(0)

def ground(objs, z0=0.0, center_xy=True):
    """Move all objects' geometry so the lowest point is at z0 (default 0) and XY bounds are centred."""
    lo, hi = bounds(objs)
    d = Vector((-(lo[0] + hi[0]) / 2 if center_xy else 0, -(lo[1] + hi[1]) / 2 if center_xy else 0, z0 - lo[2]))
    for o in objs:
        o.data.transform(Matrix.Translation(d))
    return d

def export_fbx(objs, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, mesh_smooth_type='FACE', use_mesh_modifiers=True,
                             add_leaf_bones=False, path_mode='STRIP', embed_textures=False, use_custom_props=False)

def variant_record(vid, name, objs, fbx, **extra):
    lo, hi = bounds(objs)
    rec = {'id': vid, 'name': name, 'fbx': os.path.basename(fbx), 'tris': tris(objs),
           'tris_by_material': {o['mat']: tris([o]) for o in objs},
           'size_xyz_m': [round(float(hi[i] - lo[i]), 3) for i in range(3)],
           'min_z': round(float(lo[2]), 4), 'objects': [o.name for o in objs]}
    rec.update(extra)
    return rec

# ================================================================ rendering
def _lights_world():
    sc = bpy.context.scene
    w = bpy.data.worlds.get('SW') or bpy.data.worlds.new('SW')
    w.use_nodes = True
    w.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.53, 0.50, 1)
    w.node_tree.nodes['Background'].inputs[1].default_value = 0.65
    sc.world = w
    for o in [o for o in bpy.data.objects if o.type == 'LIGHT']:
        bpy.data.objects.remove(o)
    for rot, e, col in (((48, 0, -40), 1.7, (1, .95, .86)), ((65, 0, 140), 0.45, (.8, .88, 1))):
        l = bpy.data.lights.new('sun', 'SUN'); l.energy = e; l.color = col; l.angle = math.radians(12)
        o = bpy.data.objects.new('sun', l); sc.collection.objects.link(o)
        o.rotation_euler = [math.radians(a) for a in rot]

def _water(z, size=60):
    if z is None:
        return None
    me = bpy.data.meshes.new('water')
    s = size / 2
    me.from_pydata([(-s, -s, z), (s, -s, z), (s, s, z), (-s, s, z)], [], [(0, 1, 2, 3)])
    o = bpy.data.objects.new('water', me); bpy.context.scene.collection.objects.link(o)
    m = bpy.data.materials.new('water'); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (0.05, 0.055, 0.035, 1)
    b.inputs['Roughness'].default_value = 0.8
    b.inputs['Alpha'].default_value = 0.8
    try:
        m.surface_render_method = 'BLENDED'
    except Exception:
        pass
    me.materials.append(m)
    return o

def render(objs, out_png, az=-35, el=18, size=(520, 520), water_z=None, fov=24, margin=1.12, samples=12,
           target=None, dist=None, bg=(0.78, 0.76, 0.72)):
    """Perspective render of objs from azimuth az (deg, 0 = front, i.e. camera on -Y looking +Y; positive = from
    the right side), elevation el. Camera framed to the bounding sphere. water_z: draw a murky water plane."""
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'; sc.cycles.samples = samples; sc.cycles.use_denoising = True
    sc.cycles.max_bounces = 2
    sc.render.resolution_x, sc.render.resolution_y = size
    sc.render.resolution_percentage = 100
    sc.view_settings.view_transform = 'Standard'
    sc.render.film_transparent = True
    _lights_world()
    hidden = []
    for o in bpy.data.objects:
        if o.type == 'MESH' and o not in objs and o.name != 'water':
            if not o.hide_render:
                o.hide_render = True; hidden.append(o)
    wo = bpy.data.objects.get('water')
    if wo:
        bpy.data.objects.remove(wo)
    if water_z is not None:
        _water(water_z)
    lo, hi = bounds(objs)
    c = Vector(((lo + hi) / 2).tolist()) if target is None else Vector(target)
    rad = float(np.linalg.norm(hi - lo)) / 2
    cam_d = bpy.data.cameras.get('cam') or bpy.data.cameras.new('cam')
    cam_d.lens_unit = 'FOV'; cam_d.angle = math.radians(fov); cam_d.clip_end = 1000
    cam = bpy.data.objects.get('cam') or bpy.data.objects.new('cam', cam_d)
    if cam.name not in sc.collection.objects:
        sc.collection.objects.link(cam)
    d = dist or rad * margin / math.sin(math.radians(fov) / 2)
    a, e = math.radians(az), math.radians(el)
    pos = c + Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e))) * d
    cam.location = pos
    cam.rotation_euler = (c - pos).to_track_quat('-Z', 'Y').to_euler()
    sc.camera = cam
    sc.render.filepath = out_png
    bpy.ops.render.render(write_still=True)
    from PIL import Image
    im = Image.open(out_png).convert('RGBA')
    base = Image.new('RGBA', im.size, tuple(int(c * 255) for c in bg) + (255,))
    base.alpha_composite(im)
    base.convert('RGB').save(out_png)
    for o in hidden:
        o.hide_render = False
    return out_png

def sheet(tiles, out_png, cols, labels=None, concept=None, concept_h=360, title=None):
    """Combine rendered PNGs into one sheet; optional concept image strip on top (scaled to the sheet width)."""
    from PIL import Image, ImageDraw
    ims = [Image.open(t).convert('RGB') for t in tiles]
    tw, th = ims[0].size
    rows = (len(ims) + cols - 1) // cols
    W = tw * cols
    top = 0
    cim = None
    if concept:
        cim = Image.open(concept).convert('RGB')
        cim = cim.resize((W, int(cim.height * W / cim.width)))
        top = cim.height
    head = 28 if title else 0
    out = Image.new('RGB', (W, head + top + th * rows), (34, 32, 30))
    dr = ImageDraw.Draw(out)
    if title:
        dr.text((8, 8), title, fill=(235, 230, 220))
    if cim:
        out.paste(cim, (0, head))
    for i, im in enumerate(ims):
        x, y = (i % cols) * tw, head + top + (i // cols) * th
        out.paste(im, (x, y))
        if labels:
            dr.rectangle([x, y, x + 8 + 7 * len(labels[i]), y + 18], fill=(20, 20, 20))
            dr.text((x + 4, y + 4), labels[i], fill=(240, 235, 225))
    out.save(out_png, quality=88)
    return out_png

def save_blend(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=path, compress=True)
