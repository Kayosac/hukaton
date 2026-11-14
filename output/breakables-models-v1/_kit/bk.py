"""Breakables kit: forest-style debris for Hakaton. Blender Z up, front -Y (exports to Unity +Z forward, Y up)."""
import os, math, random, numpy as np, bpy, bmesh
from mathutils import Vector, Matrix
import sk
from sk import render, sheet, tris, bounds, save_blend
REF = '/home/claude/brk/ref/'
TEXMAP = {
 'M_Forest_Props': 'T_Forest_Props.png', 'M_Forest_PropsCutout': 'T_Forest_Props.png',
 'M_Forest_PropsGlow': 'T_Forest_Props.png', 'M_Forest_Special': 'T_Forest_Special.png',
 'M_Forest_Structures_Wood': 'T_Forest_Structures_Wood.png', 'M_Pumpkin': 'T_Pumpkin.png',
}
def mat(name):
    m = bpy.data.materials.get(name)
    if m and m.get('bk'): return m
    if m is None: m = bpy.data.materials.new(name)
    m['bk'] = 1
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        if n.type not in ('OUTPUT_MATERIAL',): nt.nodes.remove(n)
    bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
    nt.links.new(bsdf.outputs[0], nt.nodes['Material Output'].inputs[0])
    t = nt.nodes.new('ShaderNodeTexImage')
    t.image = bpy.data.images.load(REF + TEXMAP[name], check_existing=True)
    t.interpolation = 'Closest'; t.extension = 'REPEAT'
    nt.links.new(t.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = 1.0
    bsdf.inputs['Specular IOR Level'].default_value = 0.05
    if name == 'M_Forest_PropsCutout':
        mth = nt.nodes.new('ShaderNodeMath'); mth.operation = 'GREATER_THAN'; mth.inputs[1].default_value = 0.5
        nt.links.new(t.outputs['Alpha'], mth.inputs[0]); nt.links.new(mth.outputs[0], bsdf.inputs['Alpha'])
        try: m.surface_render_method = 'DITHERED'
        except Exception: pass
    if name == 'M_Forest_PropsGlow':
        nt.links.new(t.outputs['Color'], bsdf.inputs['Emission Color']); bsdf.inputs['Emission Strength'].default_value = 1.5
    return m

def import_src(fbx):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=REF + fbx)
    objs = [o for o in bpy.data.objects if o not in before and o.type == 'MESH']
    for o in [o for o in bpy.data.objects if o not in before and o.type != 'MESH']:
        bpy.data.objects.remove(o)
    for o in objs:
        for i, s in enumerate(o.material_slots):
            nm = s.material.name.split('.')[0]
            o.material_slots[i].material = mat(nm)
        o.data.transform(o.matrix_world); o.matrix_world = Matrix.Identity(4)
    return objs

def export_fbx(objs, path):
    """1 unit = 1 m; Blender -Y front -> Unity +Z front (axis_forward='-Z' w/ our -Y front convention)."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    for o in bpy.context.view_layer.objects: o.select_set(False)
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'}, apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False, path_mode='STRIP',
        embed_textures=False, use_custom_props=False)

# ======================================================= atlas regions (pixel rects x0,y0,x1,y1; y from top)
TEXSIZE = {'M_Forest_Props': (256, 256), 'M_Forest_PropsCutout': (256, 256), 'M_Forest_PropsGlow': (256, 256),
           'M_Forest_Special': (256, 1024), 'M_Forest_Structures_Wood': (None, None), 'M_Pumpkin': (256, 256)}
PROPS = {  # T_Forest_Props.png
 'planksV': (0, 0, 68, 68),        # 4 vertical crate planks w/ nails (17 px each), grain vertical
 'clay': (140, 0, 192, 52),        # pot / pitcher terracotta (red-orange, light patches)
 'lightdiag': (192, 0, 244, 52), 'darkcorner': (244, 0, 256, 12),   # pot inside = darkcorner
 'diag': (0, 68, 52, 120), 'frame': (53, 70, 95, 118),             # lantern window frame (cutout)
 'cream': (96, 68, 132, 120),       # candle wax
 'green': (132, 68, 168, 120), 'redbrown': (168, 68, 204, 120), 'blue': (204, 68, 240, 120),  # bottle glass
 'greyV': (0, 120, 20, 172), 'amber': (20, 120, 56, 164),           # lantern light / honey / flesh
 'darkpatch': (56, 120, 92, 156),   # dark metal w/ rust spots (lantern body, iron)
 'darkwoodV': (92, 120, 128, 156), 'rust': (128, 120, 155, 156), 'label': (164, 120, 192, 148),
 'leather': (195, 122, 217, 145), 'flame': (224, 122, 236, 145),
 'plankH': (0, 172, 68, 192),       # horizontal batten w/ nails at ends, grain horizontal
 'camo': (68, 172, 104, 192), 'creamdot': (104, 172, 124, 192), 'cork': (124, 172, 144, 192),
 'rope': (144, 172, 164, 192), 'endgrain': (164, 172, 184, 192),
}
REG = {'M_Forest_Props': PROPS, 'M_Forest_PropsCutout': PROPS, 'M_Forest_PropsGlow': PROPS}

def tex_size(matname):
    w, h = TEXSIZE.get(matname, (None, None))
    if w is None:
        img = mat(matname).node_tree.nodes['Image Texture'].image
        w, h = img.size
    return w, h

def uvr(matname, rect, fu, fv):
    """Point inside pixel rect -> UV. fu 0..1 left->right, fv 0..1 bottom->top of the rect."""
    W, H = tex_size(matname)
    x0, y0, x1, y1 = rect
    x = x0 + 0.5 + (x1 - x0 - 1.0) * min(max(fu, 0), 1)
    y = y1 - 0.5 - (y1 - y0 - 1.0) * min(max(fv, 0), 1)
    return (x / W, 1 - y / H)

def sub(rect, fu0, fu1, fv0=0.0, fv1=1.0):
    """Sub-rect (fractions, v from bottom)."""
    x0, y0, x1, y1 = rect
    w, h = x1 - x0, y1 - y0
    return (x0 + w * fu0, y1 - h * fv1, x0 + w * fu1, y1 - h * fv0)

# ======================================================= mesh builder (multi-material)
class B:
    def __init__(self, mats):
        self.mats = list(mats)
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new('UVMap')

    def face(self, pts, uvs, m=0):
        vs = [self.bm.verts.new(Vector(p)) for p in pts]
        try:
            f = self.bm.faces.new(vs)
        except ValueError:
            return None
        f.material_index = m
        for l, uv in zip(f.loops, uvs):
            l[self.uv].uv = uv
        return f

    def proj_face(self, pts, origin, ax_u, ax_v, size_u, size_v, rect, m=0, off=(0, 0)):
        """Planar UV: project pts on ax_u/ax_v (metres) and fit size_u x size_v metres into rect."""
        mn = self.mats[m]
        o, au, av = Vector(origin), Vector(ax_u).normalized(), Vector(ax_v).normalized()
        uvs = [uvr(mn, rect, (Vector(p) - o).dot(au) / size_u + off[0], (Vector(p) - o).dot(av) / size_v + off[1]) for p in pts]
        return self.face(pts, uvs, m)

    def box(self, c, size, rot=(0, 0, 0), grain=2, side=None, end=None, m=0, jitter=0.0, seed=0, taper=(1, 1),
            shift=(0, 0), side_narrow=None, grain_u=False):
        """Crooked box. grain = local axis (0/1/2) of the wood grain; side rect for faces along the grain
        (v of the rect follows the grain), end rect for the two end faces. side_narrow: rect for the
        narrow long faces (defaults to a thin slice of side). taper scales the +grain end's cross-section,
        shift moves it (metres, in the two other local axes)."""
        rnd = random.Random(seed)
        R = (Matrix.Rotation(math.radians(rot[2]), 3, 'Z') @ Matrix.Rotation(math.radians(rot[1]), 3, 'Y') @
             Matrix.Rotation(math.radians(rot[0]), 3, 'X'))
        h = [s / 2 for s in size]
        oth = [a for a in range(3) if a != grain]
        P = {}
        for sx in (-1, 1):
            for sy in (-1, 1):
                for sz in (-1, 1):
                    s = [sx, sy, sz]
                    loc = [s[i] * h[i] for i in range(3)]
                    if s[grain] > 0:
                        loc[oth[0]] = loc[oth[0]] * taper[0] + shift[0]
                        loc[oth[1]] = loc[oth[1]] * taper[1] + shift[1]
                    v = Vector(loc) + Vector([rnd.uniform(-jitter, jitter) for _ in range(3)])
                    P[(sx, sy, sz)] = Vector(c) + R @ v
        side = side or PROPS['planksV']
        end = end or PROPS['endgrain']
        narrow = side_narrow or (sub(side, 0.0, 1.0, 0.0, 0.25) if grain_u else sub(side, 0.0, 0.2))
        # faces: for axis a, sign s
        for a in range(3):
            for s in (-1, 1):
                keys = []
                # build CCW quad looking from outside
                b1, b2 = [x for x in range(3) if x != a]
                for (p, q) in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                    k = [0, 0, 0]; k[a] = s; k[b1] = p; k[b2] = q
                    keys.append(tuple(k))
                if s < 0:
                    keys = keys[::-1]
                pts = [P[k] for k in keys]
                if a == grain:
                    o = [x for x in range(3) if x != a]
                    uvs = [uvr(self.mats[m], end, (k[o[0]] + 1) / 2, (k[o[1]] + 1) / 2) for k in keys]
                else:
                    w = [x for x in range(3) if x not in (a, grain)][0]
                    wide = size[w] >= size[a]   # the face is a wide face of the plank
                    rect = side if wide else narrow
                    uvs = [((k[w] + 1) / 2 if s > 0 else (1 - k[w]) / 2, (k[grain] + 1) / 2) for k in keys]
                    if grain_u: uvs = [(b, a) for a, b in uvs]
                    uvs = [uvr(self.mats[m], rect, a, b) for a, b in uvs]
                self.face(pts, uvs, m)
        return P

    def obj(self, name, center_origin=True, origin=None):
        bm = self.bm
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.normal_update()
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me); bm.free()
        for p in me.polygons: p.use_smooth = False
        o = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(o)
        for mn in self.mats: me.materials.append(mat(mn))
        if center_origin or origin is not None:
            set_origin(o, origin)
        return o

def set_origin(o, at=None):
    """Move the object origin to `at` (world) or to the centre of its bounds, keeping geometry in place."""
    V = np.array([v.co[:] for v in o.data.vertices])
    c = Vector(((V.min(0) + V.max(0)) / 2).tolist()) if at is None else Vector(at) - o.location
    o.data.transform(Matrix.Translation(-c))
    o.location = o.location + c

def col(o, max_tris=255, name=None):
    """Convex collision hull object '<name>_COL' with the same origin as o (no material)."""
    bm = bmesh.new()
    for v in o.data.vertices: bm.verts.new(v.co)
    bmesh.ops.convex_hull(bm, input=bm.verts)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    me = bpy.data.meshes.new((name or o.name) + '_COL')
    bm.to_mesh(me); bm.free()
    c = bpy.data.objects.new((name or o.name) + '_COL', me)
    bpy.context.scene.collection.objects.link(c)
    c.location = o.location.copy()
    n = len(me.polygons)
    if n > max_tris:
        md = c.modifiers.new('dec', 'DECIMATE'); md.ratio = (max_tris - 8) / n
        bpy.context.view_layer.objects.active = c
        dg = bpy.context.evaluated_depsgraph_get()
        me2 = bpy.data.meshes.new_from_object(c.evaluated_get(dg))
        c.modifiers.clear(); c.data = me2
        bm = bmesh.new(); bm.from_mesh(me2); bmesh.ops.convex_hull(bm, input=bm.verts)
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
        bmesh.ops.triangulate(bm, faces=bm.faces); bm.to_mesh(me2); bm.free()
    c.display_type = 'WIRE'
    return c

def box_col(o, name=None):
    """Oriented-to-world bounding box collider (12 tris)."""
    V = np.array([v.co[:] for v in o.data.vertices]); lo, hi = V.min(0), V.max(0)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector([(lo[i] + hi[i]) / 2 + v.co[i] * (hi[i] - lo[i]) for i in range(3)])
    bmesh.ops.triangulate(bm, faces=bm.faces)
    me = bpy.data.meshes.new((name or o.name) + '_COL'); bm.to_mesh(me); bm.free()
    c = bpy.data.objects.new((name or o.name) + '_COL', me); bpy.context.scene.collection.objects.link(c)
    c.location = o.location.copy(); c.display_type = 'WIRE'
    return c

def is_convex(o, eps=1e-4):
    me = o.data
    V = np.array([v.co[:] for v in me.vertices])
    for p in me.polygons:
        n = np.array(p.normal[:]); d = np.dot(V - np.array(me.vertices[p.vertices[0]].co[:]), n)
        if d.max() > eps: return False
    return True

def check_manifold(o):
    bm = bmesh.new(); bm.from_mesh(o.data)
    bad = sum(1 for e in bm.edges if not e.is_manifold); bm.free()
    return bad

def report(objs):
    rows = []
    for o in objs:
        lo, hi = bounds([o]); t = tris([o])
        rows.append({'name': o.name, 'tris': t, 'size_m': [round(float(hi[i] - lo[i]), 3) for i in range(3)],
                     'pivot': [round(x, 3) for x in o.location],
                     'materials': [m.name for m in o.data.materials],
                     **({'convex': is_convex(o)} if o.name.endswith('_COL') else {'open_edges': check_manifold(o)})})
    return rows

def render_objs(objs, path, az=-30, el=25, size=(420, 420), samples=10, hide_col=True):
    vis = [o for o in objs if not (hide_col and o.name.endswith('_COL'))]
    for o in objs:
        if o.name.endswith('_COL'): o.hide_render = hide_col
    return render(vis, path, az=az, el=el, size=size, samples=samples)
