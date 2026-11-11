"""Oddities for outings, built in Blender for Unity (same pipeline and look as the chests and the brazier):
OD01 street payphone and OD02 old CRT television, v2 after the user's concepts (payphone: a flared open booth on a
bent olive post with a bolted base, rusty sloped roof with a handset sign, a grey phone unit with keypad and a chunky
handset whose cord loops below the shelf, a service door at the back; TV: rusty wooden case with a beige bezel, screen
on the left, dial knobs, grille and red button on the right, tapered back housing, wedge feet, jointed rabbit ears and
a power cord). The handset is its own object pivoting at the cord's anchor; the glass has 0..1 UVs for the CRT shader.
Very simple crooked PSX geometry, flat shading, painted point-filtered atlases (textures.py).
Unity axes: +Y up, +Z front; pivot at the ground. Blender = (x, -z, y) (FBX Forward -Z, Up Y, bake space transform).
Run: python build_oddities.py (bpy module) -> out/*.fbx, *.blend, previews.
"""
import math, random, os, json
import bpy, bmesh
from mathutils import Vector, Matrix

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'out')
os.makedirs(OUT, exist_ok=True)

PHONE_REG = {'beige': (0, 0, 256, 256), 'rust': (256, 0, 512, 128), 'olive': (256, 128, 384, 384), 'grey': (384, 128, 512, 256),
             'phoneface': (0, 256, 256, 512), 'sign': (384, 256, 512, 320), 'handset': (384, 320, 448, 384), 'door': (448, 320, 512, 384),
             'lamp': (384, 384, 448, 416), 'bolt': (448, 384, 512, 416), 'base': (256, 384, 384, 512), 'patch': (384, 416, 512, 512)}
TV_REG = {'casing': (0, 0, 256, 256), 'bezel': (256, 0, 512, 128), 'back': (256, 128, 512, 256), 'panel': (0, 256, 128, 512),
          'knob': (128, 256, 192, 320), 'olive': (192, 256, 256, 320), 'hole': (128, 320, 192, 384), 'rod': (192, 320, 256, 384),
          'feet': (128, 384, 256, 512), 'red': (256, 256, 288, 288), 'cord': (288, 256, 320, 288), 'dark': (320, 256, 352, 288)}
FRONT_MAPPED = {'phoneface', 'sign', 'panel', 'door'}   # painted for the front view: planar on x/y (reads right in Unity)

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

# ---------------- OD01 payphone (v2, after the concept) ----------------

def rect_ring(x, y, hw, z_front, z_back, xb=None):
    """Rectangle in x/z at height y (counter-clockwise from above)."""
    return [(x - hw, y, z_back), (x + hw, y, z_back), (x + hw, y, z_front), (x - hw, y, z_front)]

def u_ring(y, hw, zf, zb, t):
    """Open-front U of the booth walls (outline with thickness), as a closed ring of 8 points."""
    return [(-hw, y, zf), (-hw, y, zb), (hw, y, zb), (hw, y, zf),
            (hw - t, y, zf), (hw - t, y, zb + t), (-hw + t, y, zb + t), (-hw + t, y, zf)]

def build_phone():
    random.seed(31)
    body, handset, lamp = Part(), Part(), Part()
    # base plate with four bolts
    box(body, (0, .025, 0), (.56, .05, .46), 'base', rot_y=.03, jitter=.008)
    for sx in (-1, 1):
        for sz in (-1, 1):
            box(body, (sx * .22, .058, sz * .17), (.045, .02, .045), 'bolt', jitter=.003)
    # the post: a little wider at the top, bent
    loft(body, [rect_ring(0, .05, .085, .07, -.07), rect_ring(.018, .5, .088, .072, -.072), rect_ring(0, 1.0, .1, .08, -.08)],
         'olive', jitter=.006)
    # the booth: open-front U walls, straight at the bottom then flaring out
    t = .03
    levels = [(.98, .28, .17, -.17), (1.24, .28, .17, -.17), (1.31, .315, .2, -.18), (1.77, .335, .22, -.19)]
    loft(body, [u_ring(y, hw, zf, zb, t) for y, hw, zf, zb in levels], 'beige', jitter=.006)
    # the shelf (floor of the booth), sticking out at the front
    box(body, (0, .985, .015), (.6, .05, .42), 'beige', jitter=.006)
    # a riveted patch on the right wall, bolts at the top corners
    box(body, (.338, 1.56, .07), (.012, .13, .15), 'patch', jitter=.003)
    for sx in (-1, 1):
        for z in (.19, -.16):
            box(body, (sx * .33, 1.745, z), (.03, .03, .03), 'bolt', jitter=.002)
    # the roof: a skirt, then the front slope with the handset sign
    roof = [rect_ring(0, 1.77, .375, .27, -.24), rect_ring(0, 1.83, .375, .27, -.24), rect_ring(0, 1.98, .3, .14, -.19)]
    loft(body, roof, 'rust', cap_start=True, cap_end=True, jitter=.004)
    # sign on the front slope, a hair off the surface
    def slope(x, k):  # point on the front slope: k=0 bottom edge, 1 top edge
        y = 1.83 + .15 * k + .012; z = .27 - .13 * k + .017   # lifted off the slope along its normal
        return (x, y, z)
    ids = [body.v(p) for p in (slope(-.2, .15), slope(.2, .15), slope(.2, .85), slope(-.2, .85))]
    body.face(ids, 'sign')
    # the phone unit against the back wall
    box(body, (.035, 1.38, -.115), (.4, .52, .11), 'grey', jitter=.004, front_region='phoneface')
    box(body, (-.15, 1.5, -.05), (.04, .055, .05), 'patch', jitter=.002)           # hook
    # the back door with hinges
    box(body, (0, 1.43, -.2), (.3, .36, .02), 'door', jitter=.003, front_region='door')
    for y in (1.33, 1.52):
        box(body, (-.155, y, -.21), (.02, .05, .02), 'bolt', jitter=.001)
    # lamp strip under the roof (glows while it calls)
    box(lamp, (0, 1.755, .02), (.34, .02, .06), 'lamp', jitter=.002)
    # the handset: pivot at the cord's anchor under the phone unit; chunky receiver on the hook, cord looping below the shelf
    P = Vector((-.07, 1.13, -.08))
    hp = lambda x, y, z: tuple(Vector((x, y, z)) - P)
    box(handset, hp(-.2, 1.38, -.03), (.085, .24, .065), 'handset', jitter=.003)     # handle
    box(handset, hp(-.2, 1.52, -.01), (.1, .08, .1), 'handset', jitter=.003)         # earpiece
    box(handset, hp(-.2, 1.24, -.01), (.1, .08, .1), 'handset', jitter=.003)         # mouthpiece
    cord = [(-.2, 1.2, -.01), (-.205, 1.1, .02), (-.19, 1.03, .2), (-.18, .88, .25), (-.15, .77, .25), (-.11, .76, .25),
            (-.08, .86, .24), (-.07, 1.03, .2), (-.07, 1.09, .02), (-.07, 1.13, -.08)]
    tube(handset, [hp(*p) for p in cord], .013, 5, 'handset')
    return body, handset, lamp, P

# ---------------- OD02 old television (v2, after the concept) ----------------

def build_tv():
    random.seed(57)
    body, screen = Part(), Part()
    cy = .36
    # the wooden front case (rounded box), its rim, the beige bezel, the screen hole
    case = [rrect(0, cy, .25, .8, .58, .08), rrect(0, cy, .21, .84, .61, .09), rrect(0, cy, -.1, .82, .6, .09)]
    loft(body, case, 'casing', jitter=.006)
    bezel_out = rrect(0, cy, .25, .8, .58, .08)
    bezel_in = rrect(0, cy, .252, .73, .51, .065)
    io = [body.v(J(p, .002)) for p in bezel_out]; ii = [body.v(p) for p in bezel_in]
    n = len(bezel_out)
    for i in range(n):
        j = (i + 1) % n
        body.face([io[i], ii[i], ii[j], io[j]], 'casing')
    hole = rrect(-.085, cy + .01, .254, .5, .44, .06)
    hi_ = [body.v(p) for p in hole]
    for i in range(n):
        j = (i + 1) % n
        body.face([ii[i], hi_[i], hi_[j], ii[j]], 'bezel')
    loft(body, [hole, rrect(-.085, cy + .01, .22, .47, .41, .05)], 'hole', cap_end=True)
    # control panel on the right: dial ticks and grille painted, two knobs and the red button modelled
    box(body, (.265, cy, .256), (.13, .48, .008), 'bezel', jitter=.002, front_region='panel')
    for ky in (cy + .15, cy + .03):
        tube(body, [(.265, ky, .258), (.265, ky, .29), (.265, ky, .3)], [.048, .045, .036], 8, 'knob')
        box(body, (.265, ky, .302), (.012, .06, .01), 'dark', rot_y=0, jitter=.001)
    box(body, (.265, cy - .19, .262), (.04, .035, .02), 'red', jitter=.002)
    # the tapered back housing with its vents (painted on top), slightly lower
    back = [rrect(0, cy - .03, -.1, .7, .5, .04), rrect(.005, cy - .1, -.42, .46, .32, .04)]
    loft(body, back, 'back', cap_end=True, jitter=.006)
    # short wedge feet
    for sx, z, h, x in ((-1, .17, .08, .3), (1, .17, .08, .3), (-1, -.33, .1, .17), (1, -.33, .1, .17)):
        box(body, (sx * x, h / 2, z), (.08, h, .07), 'feet', tilt=(sx * .25, 0), jitter=.004)
    # antenna: an olive base block and two jointed rods
    box(body, (0, cy + .315, -.03), (.16, .05, .1), 'olive', jitter=.004)
    for s in (-1, 1):
        pts = [(s * .035, cy + .34, -.03), (s * .09, cy + .5, -.05), (s * .15, cy + .64, -.06), (s * .2, cy + .78, -.07)]
        tube(body, pts, .009, 4, 'rod', cap=True)
        for p in pts[1:3]: box(body, p, (.018, .018, .018), 'rod', jitter=.001)
        box(body, pts[-1], (.026, .026, .026), 'rod', jitter=.002)
    # the power cord curling on the ground behind
    cord = [(.05, .12, -.42), (.06, .05, -.47), (.1, .015, -.52), (.18, .015, -.53), (.22, .015, -.47), (.16, .015, -.43)]
    tube(body, cord, .011, 5, 'cord')
    # the glass: a bulged grid with 0..1 UVs (own object for the CRT screen shader)
    gw, gh, cols, rows = .47, .41, 8, 6
    grid = []
    for r in range(rows + 1):
        row = []
        for c in range(cols + 1):
            u, v = c / cols, r / rows
            x, y = -.085 - gw / 2 + u * gw, cy + .01 - gh / 2 + v * gh
            bulge = .03 * (1 - (2 * u - 1) ** 2) * (1 - (2 * v - 1) ** 2)
            row.append(screen.v((x, y, .225 + bulge)))
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

# ---------------- living curvature ----------------
# The concepts are hand-drawn: nothing is straight. Long edges are subdivided and every vertex goes through a smooth
# per-object warp (lean, bend, bulging sides, domed top, a little twist) in Unity space, so the flat boxes sag and swell.

def smooth(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)

def warp_phone(p):
    x, y, z = p
    # the post bends (a soft S), the whole thing leans a little
    bend = .03 * math.sin(math.pi * min(y, 1.0)) - .008 * math.sin(2 * math.pi * min(y, 1.0))
    x += bend * (1 - smooth(.95, 1.05, y)) + .035 * y
    # the booth: walls belly out, the box twists and leans forward, the roof sits askew
    k = smooth(.95, 1.8, y)
    if y > .95:
        belly = 1 + .07 * math.sin(math.pi * min(1, (y - .95) / .85))
        x *= belly; z *= 1 + .04 * math.sin(math.pi * min(1, (y - .95) / .85))
        a = .07 * k
        x, z = x * math.cos(a) - z * math.sin(a), x * math.sin(a) + z * math.cos(a)
        z += .03 * k
    if y > 1.76:
        y += .035 * x + .015 * math.sin(z * 9)
        x += .02
    # the base plate: one corner sunk
    if y < .07:
        y += .012 * (x + z)
    return (x, y, z)

def warp_tv(p):
    x, y, z = p
    cy = .36
    h = (y - .06) / .62
    # sides bulge in the middle, the case leans and twists, the top domes, the face swells
    if y > .05:
        x *= (1 + .1 * math.sin(math.pi * min(1, max(0, h)))) * (1 - .14 * (y - cy))
        x += .09 * (y - cy) + .02 * math.sin(z * 5)
        a = .16 * (y - cy) + .04
        z += .03 * math.sin(x * 4) * smooth(.1, .3, y)
        x, z = x * math.cos(a) - z * math.sin(a), x * math.sin(a) + z * math.cos(a)
        y += .045 * math.cos(x * 3.4) * smooth(.45, .66, y) - .03 * smooth(.45, .66, y) * (x + .4)
        y += .012 * math.sin(z * 6) * smooth(.4, .66, y)
        if z > .15:
            z += .02 * max(0, 1 - (x / .45) ** 2) * max(0, 1 - ((y - cy) / .32) ** 2)
    return (x, y, z)

def subdivide_long(bm, limit):
    """Cuts long edges so the warp can curve them."""
    for _ in range(2):
        long = [e for e in bm.edges if e.calc_length() > limit]
        if not long: break
        bmesh.ops.subdivide_edges(bm, edges=long, cuts=1, use_grid_fill=True)

def to_object(name, part, material, reg, origin=Vector((0, 0, 0)), warp=None, limit=.14):
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
    if warp:
        subdivide_long(bm, limit)
        o = Vector(origin)
        wo = Vector(warp(tuple(o)))
        for v in bm.verts:
            u = Vector((v.co.x, v.co.z, -v.co.y)) + o          # back to Unity space, absolute
            w = Vector(warp(tuple(u))) - wo
            v.co = B(tuple(w))
    bm.normal_update(); bm.to_mesh(mesh); bm.free(); mesh.validate()
    mat = bpy.data.materials.get(material) or bpy.data.materials.new(material)
    mesh.materials.append(mat)
    for poly in mesh.polygons: poly.use_smooth = False
    obj.location = B(warp(tuple(origin)) if warp else origin)
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
    ob = to_object('OD01_Body', body, 'M_OD01_Payphone', PHONE_REG, warp=warp_phone)
    oh = to_object('OD01_Handset', handset, 'M_OD01_Payphone', PHONE_REG, origin=pivot, warp=warp_phone)
    ol = to_object('OD01_Lamp', lamp, 'M_OD01_Lamp', PHONE_REG, warp=warp_phone)
    for o in (ob, oh, ol): fix_normals(o)
    textured('M_OD01_Payphone', 'T_Payphone.png'); textured('M_OD01_Lamp', 'T_Payphone.png')
    stats['payphone'] = export('SM_OD01_Payphone', [ob, oh, ol], 'SM_OD01_Payphone')
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'OD01_Payphone.blend'))

    bpy.ops.wm.read_factory_settings(use_empty=True)
    body, screen = build_tv()
    tb = to_object('OD02_Body', body, 'M_OD02_OldTV', TV_REG, warp=warp_tv, limit=.1)
    ts = to_object('OD02_Screen', screen, 'M_OD02_Screen', TV_REG, warp=warp_tv, limit=.1)
    fix_normals(tb)
    textured('M_OD02_OldTV', 'T_OldTV.png'); textured('M_OD02_Screen', 'T_OldTV_Static.png')
    stats['tv'] = export('SM_OD02_OldTV', [tb, ts], 'SM_OD02_OldTV')
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'OD02_OldTV.blend'))
    json.dump(stats, open(os.path.join(OUT, 'stats.json'), 'w'))
    print('triangles', stats)

main()
