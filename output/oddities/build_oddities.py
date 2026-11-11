"""Oddities for outings, built in Blender for Unity (same pipeline and look as the chests and the brazier):
OD01 street payphone (after the GTA III ones: a box on a post under a little hood, a sign on top, the handset on the
left on its own pivot so it can swing off the hook) and OD02 a pot-bellied old CRT television on splayed legs with
rabbit ears (the glass is its own object with 0..1 UVs for the CRT screen shader).
Very simple crooked PSX geometry, flat shading, painted point-filtered atlases (textures.py).
Unity axes: +Y up, +Z front; pivot at the ground. Blender = (x, -z, y) (FBX Forward -Z, Up Y, bake space transform).
Run: python build_oddities.py (bpy module) -> out/*.fbx, *.blend, previews.
"""
import math, random, os, json
import bpy, bmesh
from mathutils import Vector, Matrix

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'out')
os.makedirs(OUT, exist_ok=True)

PHONE_REG = {'blue': (0, 0, 256, 256), 'metal': (256, 0, 384, 256), 'concrete': (384, 0, 512, 128), 'dark': (384, 128, 448, 192),
             'black': (448, 128, 512, 192), 'lamp': (384, 192, 512, 256), 'faceplate': (0, 256, 256, 512), 'sign': (256, 256, 512, 384),
             'rust': (256, 384, 512, 512)}
TV_REG = {'veneer': (0, 0, 256, 256), 'bezel': (256, 0, 512, 128), 'plastic': (256, 128, 512, 256), 'panel': (0, 256, 128, 384),
          'knob': (128, 256, 192, 320), 'chrome': (192, 256, 256, 320), 'hole': (128, 320, 256, 384), 'legs': (0, 384, 256, 512),
          'screen': (256, 256, 512, 512)}
FRONT_MAPPED = {'faceplate', 'sign', 'panel'}   # painted for the front view: planar on x/y (reads right in Unity)

def B(p):
    x, y, z = p
    return Vector((x, -z, y))

class Part:
    def __init__(self):
        self.verts, self.faces = [], []
    def v(self, p):
        self.verts.append(tuple(p)); return len(self.verts) - 1
    def face(self, ids, region):
        self.faces.append((list(ids), region))

def J(p, a):
    return (p[0] + random.uniform(-a, a), p[1] + random.uniform(-a, a), p[2] + random.uniform(-a, a))

def loft(part, rings, region, cap_start=False, cap_end=False, cap_region=None, jitter=0.0):
    ids = [[part.v(J(p, jitter)) for p in r] for r in rings]
    n = len(rings[0])
    for a, b in zip(ids, ids[1:]):
        for i in range(n):
            j = (i + 1) % n
            part.face([a[i], a[j], b[j], b[i]], region)
    if cap_start: part.face(list(reversed(ids[0])), cap_region or region)
    if cap_end: part.face(ids[-1], cap_region or region)
    return ids

def box(part, c, size, region, rot_y=0.0, tilt=(0, 0), jitter=.006, front_region=None):
    """Axis box (crooked: tilt shears x/z with height, jitter moves the corners)."""
    cx, cy, cz = c
    sx, sy, sz = size[0] / 2, size[1] / 2, size[2] / 2
    cos, sin = math.cos(rot_y), math.sin(rot_y)
    def P(x, y, z):
        x, z = x + tilt[0] * y, z + tilt[1] * y
        return J((cx + x * cos - z * sin, cy + y, cz + x * sin + z * cos), jitter)
    b = [P(-sx, -sy, -sz), P(sx, -sy, -sz), P(sx, -sy, sz), P(-sx, -sy, sz)]
    t = [P(-sx, sy, -sz), P(sx, sy, -sz), P(sx, sy, sz), P(-sx, sy, sz)]
    ib = [part.v(p) for p in b]; it = [part.v(p) for p in t]
    part.face([ib[3], ib[2], ib[1], ib[0]], region)                      # bottom
    part.face([it[0], it[1], it[2], it[3]], region)                      # top
    part.face([ib[0], ib[1], it[1], it[0]], region)                      # back (-z)
    part.face([ib[2], ib[3], it[3], it[2]], front_region or region)      # front (+z)
    part.face([ib[1], ib[2], it[2], it[1]], region)                      # right (+x)
    part.face([ib[3], ib[0], it[0], it[3]], region)                      # left (-x)

def tube(part, points, radius, sides, region, cap=True, jitter=0.0):
    rings = []
    for k, p in enumerate(points):
        a = Vector(points[max(0, k - 1)]); b = Vector(points[min(len(points) - 1, k + 1)])
        d = (b - a).normalized()
        up = Vector((0, 1, 0)) if abs(d.y) < .9 else Vector((1, 0, 0))
        u = d.cross(up).normalized(); w = d.cross(u).normalized()
        r = radius[k] if isinstance(radius, (list, tuple)) else radius
        rings.append([tuple(Vector(p) + (u * math.cos(2 * math.pi * i / sides) + w * math.sin(2 * math.pi * i / sides)) * r) for i in range(sides)])
    loft(part, rings, region, cap, cap, jitter=jitter)

def rrect(cx, cy, z, w, h, corner, n=3):
    """Rounded rectangle ring in the x/y plane at depth z (4 corners x n points), counter-clockwise seen from +z."""
    pts = []
    for k, (sx, sy) in enumerate([(1, 1), (-1, 1), (-1, -1), (1, -1)]):
        ox, oy = cx + sx * (w / 2 - corner), cy + sy * (h / 2 - corner)
        a0 = k * math.pi / 2
        for i in range(n):
            a = a0 + (math.pi / 2) * i / (n - 1)
            pts.append((ox + corner * math.cos(a), oy + corner * math.sin(a), z))
    return pts

# ---------------- OD01 payphone ----------------

def build_phone():
    random.seed(31)
    body, handset, lamp = Part(), Part(), Part()
    box(body, (0, .045, 0), (.64, .09, .52), 'concrete', rot_y=.05, jitter=.012)                      # slab
    box(body, (.02, .99, -.06), (.1, 1.8, .1), 'metal', tilt=(.018, -.01), jitter=.004)                # post
    box(body, (0, 1.27, .04), (.38, .58, .2), 'blue', rot_y=-.03, tilt=(.012, 0), jitter=.008,
        front_region='blue')                                                                          # housing
    box(body, (.0, 1.28, .15), (.29, .44, .025), 'metal', rot_y=-.03, tilt=(.012, 0), jitter=.004,
        front_region='faceplate')                                                                     # faceplate
    box(body, (.0, 1.03, .16), (.3, .06, .06), 'dark', rot_y=-.03, jitter=.004)                       # coin return
    # little hood with side wings, leaning forward
    box(body, (.0, 1.66, .07), (.54, .04, .44), 'blue', tilt=(0, .0), jitter=.01)
    for s in (-1, 1):
        box(body, (s * .26, 1.43, .07), (.03, .46, .4), 'blue', tilt=(s * -.02, 0), jitter=.008)
    # sign on top, a bit askew
    box(body, (.01, 1.83, .08), (.48, .17, .05), 'blue', rot_y=.05, tilt=(.04, 0), jitter=.006, front_region='sign')
    box(body, (.0, 1.73, .06), (.06, .05, .04), 'metal', jitter=.003)
    # hook on the left side of the housing
    box(body, (-.215, 1.33, .1), (.05, .16, .07), 'dark', jitter=.004)
    # the lamp under the hood (own object: lit while it calls, dark after)
    box(lamp, (.0, 1.625, .15), (.3, .03, .07), 'lamp', jitter=.003)
    # the handset: pivot at the cord's anchor under the housing (the object origin); cord + receiver
    P = Vector((-.17, .99, .12))
    cord = [(-.17, .99, .12), (-.2, .96, .15), (-.24, .99, .16), (-.25, 1.06, .15), (-.25, 1.13, .14), (-.255, 1.19, .14)]
    tube(handset, [tuple(Vector(p) - P) for p in cord], .011, 5, 'black')
    hp = lambda x, y, z: tuple(Vector((x, y, z)) - P)
    box(handset, hp(-.255, 1.3, .14), (.045, .2, .05), 'black', jitter=.003)                         # handle
    box(handset, hp(-.255, 1.415, .155), (.06, .05, .08), 'black', jitter=.003)                      # earpiece
    box(handset, hp(-.255, 1.19, .155), (.06, .05, .08), 'black', jitter=.003)                       # mouthpiece
    return body, handset, lamp, P

# ---------------- OD02 old television ----------------

def build_tv():
    random.seed(57)
    body, screen = Part(), Part()
    cy = .62
    # pot belly: rounded rings from the front frame back to a small cap
    rings = [rrect(0, cy, .22, .68, .54, .07), rrect(0, cy + .005, .14, .72, .57, .09),
             rrect(0, cy + .01, -.08, .62, .5, .1), rrect(.01, cy + .01, -.28, .44, .36, .1), rrect(.01, cy + .015, -.36, .3, .24, .08)]
    ids = loft(body, rings[:3], 'veneer', jitter=.006)
    loft(body, rings[2:], 'plastic', cap_end=True, jitter=.006)
    # front frame: outer ring -> screen hole (left of centre, knobs on the right)
    outer = rings[0]
    hole = rrect(-.07, cy + .02, .225, .46, .38, .06)
    hole_in = rrect(-.07, cy + .02, .19, .44, .36, .055)
    io = [body.v(J(p, .002)) for p in outer]
    ih = [body.v(p) for p in hole]
    n = len(outer)
    for i in range(n):
        j = (i + 1) % n
        body.face([io[i], ih[i], ih[j], io[j]], 'bezel')
    loft(body, [hole, hole_in], 'hole', cap_end=True)
    # knob panel and two knobs on the right
    box(body, (.255, cy - .01, .228), (.13, .42, .012), 'bezel', jitter=.003, front_region='panel')
    for ky in (cy + .1, cy - .05):
        k = [(.255, ky, .23), (.255, ky, .26), (.255, ky, .27)]
        tube(body, k, [.032, .03, .024], 7, 'knob')
    # legs: four splayed tapered sticks
    for sx, sz in ((-1, 1), (1, 1), (-1, -1), (1, -1)):
        top = (sx * .24, cy - .24, sz * .12 - .02)
        foot = (sx * .33, 0, sz * .22 - .02 + random.uniform(-.02, .02))
        tube(body, [top, foot], [.025, .016], 5, 'legs', jitter=.002)
    # rabbit ears
    box(body, (.0, cy + .3, -.08), (.12, .04, .1), 'plastic', jitter=.004)
    for s, lean in ((-1, .27), (1, .33)):
        tip = (s * lean, cy + .82 + random.uniform(-.05, .05), -.14)
        tube(body, [(s * .03, cy + .31, -.08), tip], .008, 4, 'chrome')
        box(body, tip, (.025, .025, .025), 'chrome', jitter=.002)
    # the glass: a bulged grid with 0..1 UVs (own object for the CRT screen shader)
    gw, gh, cols, rows = .44, .36, 8, 6
    grid = []
    for r in range(rows + 1):
        row = []
        for c in range(cols + 1):
            u, v = c / cols, r / rows
            x, y = -.07 - gw / 2 + u * gw, cy + .02 - gh / 2 + v * gh
            bulge = .025 * (1 - (2 * u - 1) ** 2) * (1 - (2 * v - 1) ** 2)
            row.append(screen.v((x, y, .2 + bulge)))
        grid.append(row)
    for r in range(rows):
        for c in range(cols):
            screen.face([grid[r][c], grid[r][c + 1], grid[r + 1][c + 1], grid[r + 1][c]], 'glass')
    return body, screen

# ---------------- objects ----------------

def uv_rect(reg, name):
    x0, y0, x1, y1 = reg[name]
    pad = 1
    return ((x0 + pad) / 512, 1 - (y1 - pad) / 512, (x1 - pad) / 512, 1 - (y0 + pad) / 512)

def to_object(name, part, material, reg, origin=Vector((0, 0, 0))):
    mesh = bpy.data.meshes.new(name)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    bm = bmesh.new()
    bverts = [bm.verts.new(B(p)) for p in part.verts]
    uv = bm.loops.layers.uv.new('UVMap')
    for region in sorted({r for _, r in part.faces}):
        faces = [f for f in part.faces if f[1] == region]
        pts = [part.verts[i] for f, _ in faces for i in f]
        lo = Vector((min(p[0] for p in pts), min(p[1] for p in pts), min(p[2] for p in pts)))
        hi = Vector((max(p[0] for p in pts), max(p[1] for p in pts), max(p[2] for p in pts)))
        span = max((hi - lo).x, (hi - lo).y, (hi - lo).z, 1e-4)
        for ids, _ in faces:
            try:
                face = bm.faces.new([bverts[i] for i in reversed(ids)])
            except ValueError:
                continue
            n = (Vector(part.verts[ids[1]]) - Vector(part.verts[ids[0]])).cross(Vector(part.verts[ids[2]]) - Vector(part.verts[ids[0]]))
            ax = max(range(3), key=lambda k: abs(n[k]))
            for loop, i in zip(face.loops, list(reversed(ids))):
                p = Vector(part.verts[i]) - lo
                if region == 'glass':
                    # planar 0..1 over the glass (reads right in Unity after the FBX axis conversion)
                    loop[uv].uv = (p.x / max((hi - lo).x, 1e-4), p.y / max((hi - lo).y, 1e-4))
                    continue
                u0, v0, u1, v1 = uv_rect(reg, region)
                if region in FRONT_MAPPED and ax == 2:
                    a = p.x / max((hi - lo).x, 1e-4)
                    b = p.y / max((hi - lo).y, 1e-4)
                    loop[uv].uv = (u0 + (u1 - u0) * a, v0 + (v1 - v0) * b)
                    continue
                a, b = [(p.y, p.z), (p.x, p.z), (p.x, p.y)][ax]
                loop[uv].uv = (u0 + (u1 - u0) * min(1, a / span), v0 + (v1 - v0) * min(1, b / span))
    bm.normal_update(); bm.to_mesh(mesh); bm.free(); mesh.validate()
    mat = bpy.data.materials.get(material) or bpy.data.materials.new(material)
    mesh.materials.append(mat)
    for poly in mesh.polygons: poly.use_smooth = False
    obj.location = B(origin)
    return obj

def fix_normals(obj):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False); bpy.ops.object.mode_set(mode='OBJECT')
    obj.select_set(False)

def textured(material, image):
    mat = bpy.data.materials[material]
    mat.use_nodes = True
    nt = mat.node_tree
    tex = nt.nodes.new('ShaderNodeTexImage')
    tex.image = bpy.data.images.load(os.path.join(OUT, image))
    tex.interpolation = 'Closest'
    bsdf = nt.nodes.get('Principled BSDF')
    nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = 1

def export(name, objs, root_name):
    root = bpy.data.objects.new(root_name, None)
    bpy.context.collection.objects.link(root)
    for o in objs: o.parent = root
    bpy.ops.object.select_all(action='DESELECT')
    root.select_set(True)
    for o in objs: o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + '.fbx'), use_selection=True, apply_unit_scale=True,
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True, object_types={'MESH', 'EMPTY'},
                             mesh_smooth_type='FACE', use_triangles=True, add_leaf_bones=False, bake_anim=False)
    tris = 0
    for o in objs:
        o.data.calc_loop_triangles(); tris += len(o.data.loop_triangles)
    return tris

def main():
    stats = {}
    bpy.ops.wm.read_factory_settings(use_empty=True)
    body, handset, lamp, pivot = build_phone()
    ob = to_object('OD01_Body', body, 'M_OD01_Payphone', PHONE_REG)
    oh = to_object('OD01_Handset', handset, 'M_OD01_Payphone', PHONE_REG, origin=pivot)
    ol = to_object('OD01_Lamp', lamp, 'M_OD01_Lamp', PHONE_REG)
    for o in (ob, oh, ol): fix_normals(o)
    textured('M_OD01_Payphone', 'T_Payphone.png'); textured('M_OD01_Lamp', 'T_Payphone.png')
    stats['payphone'] = export('SM_OD01_Payphone', [ob, oh, ol], 'SM_OD01_Payphone')
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'OD01_Payphone.blend'))

    bpy.ops.wm.read_factory_settings(use_empty=True)
    body, screen = build_tv()
    tb = to_object('OD02_Body', body, 'M_OD02_OldTV', TV_REG)
    ts = to_object('OD02_Screen', screen, 'M_OD02_Screen', TV_REG)
    fix_normals(tb)
    textured('M_OD02_OldTV', 'T_OldTV.png'); textured('M_OD02_Screen', 'T_OldTV_Eye.png')
    stats['tv'] = export('SM_OD02_OldTV', [tb, ts], 'SM_OD02_OldTV')
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'OD02_OldTV.blend'))
    json.dump(stats, open(os.path.join(OUT, 'stats.json'), 'w'))
    print('triangles', stats)

main()
