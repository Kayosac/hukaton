"""Low-poly pumpkin for the farm patch (rat pumpkin's disguise). Run: python3 build.py"""
import math, os, random
import bpy, bmesh
import numpy as np
from PIL import Image

OUT = '/tmp/pumpkin/out'; os.makedirs(OUT, exist_ok=True)
RIBS, PER_RIB = 8, 3
SEG = RIBS * PER_RIB
# body lathe profile (radius, height) in metres, bottom -> top
PROFILE = [(0.10, 0.00), (0.30, 0.025), (0.44, 0.09), (0.51, 0.19), (0.53, 0.29), (0.49, 0.39), (0.40, 0.47), (0.25, 0.54), (0.09, 0.565)]
TOP_DIP = 0.535   # the top centre sits in a dimple
H = 0.6

# ---------------- texture: 128 x 128 pixel art like the forest props: muted, dithered, a dirty bottom, a little moss
W = 128
rng = random.Random(7)
np_rng = np.random.default_rng(7)
hi, mid, lo, groove = np.array([206, 128, 66]), np.array([186, 106, 52]), np.array([152, 82, 42]), np.array([104, 58, 34])
dirt = np.array([96, 70, 48])
tex = np.zeros((W, W, 3), float)
for y in range(112):
    t = y / 111   # 0 bottom, 1 top
    for x in range(W):
        f = (x % 16) / 15.0
        d = abs(f - .5) * 2
        c = groove if d > .82 else lo if d > .55 else hi if (d < .22 and .3 < t < .75) else mid
        c = c * (0.82 + 0.18 * math.sin(min(1, t * 1.15) * math.pi))
        # dirt creeping up from the ground
        dirt_line = .16 + .05 * math.sin(x * .41) + .03 * math.sin(x * 1.3)
        if t < dirt_line: c = c * .45 + dirt * .55
        tex[W - 1 - y, x] = c
# pixel noise like the forest textures
tex[:112] *= 1 + np_rng.normal(0, .045, (112, W, 1))
# faded yellowish patches and darker blotches
for _ in range(14):
    cx, cy, r = rng.randrange(W), rng.randrange(24, 100), rng.randrange(2, 5)
    k = rng.choice([1.12, .84])
    for yy in range(cy - r, cy + r + 1):
        for xx in range(cx - r, cx + r + 1):
            if (xx - cx) ** 2 + (yy - cy) ** 2 <= r * r and 0 <= yy < 112:
                tex[W - 1 - yy, xx % W] *= k
# a few moss / lichen specks low on the body
moss = np.array([112, 122, 62])
for _ in range(10):
    cx, cy = rng.randrange(W), rng.randrange(14, 34)
    for dx, dy in [(0, 0), (1, 0), (0, 1), (-1, 0), (1, 1)][:rng.randrange(2, 6)]:
        tex[W - 1 - (cy + dy), (cx + dx) % W] = moss * (0.85 + .3 * rng.random())
# a few lichen clumps on the lower ribs, like the moss on the forest's crates and stones
lichen = [np.array([118, 128, 70]), np.array([96, 108, 58]), np.array([138, 140, 84])]
for _ in range(9):
    cx, cy = rng.randrange(W), rng.randrange(12, 46)
    for yy in range(cy - 3, cy + 4):
        for xx in range(cx - 4, cx + 5):
            if ((xx - cx) / 4.2) ** 2 + ((yy - cy) / 2.6) ** 2 <= 1 and rng.random() < .8:
                tex[W - 1 - yy, xx % W] = rng.choice(lichen) * (0.9 + .2 * rng.random())
# stem strip (rows 112..127): dry woody green-brown, lighter cut on top
for y in range(112, 128):
    for x in range(W):
        base = np.array([104, 100, 52]) if (x // 3) % 2 == 0 else np.array([84, 80, 42])
        if y >= 124: base = np.array([168, 150, 98])
        tex[W - 1 - y, x] = base * (0.92 + .16 * rng.random())
Image.fromarray(np.clip(tex, 0, 255).astype(np.uint8)).save(os.path.join(OUT, 'T_Pumpkin.png'))

# ---------------- mesh
bpy.ops.wm.read_factory_settings(use_empty=True)
mesh = bpy.data.meshes.new('SM_Pumpkin')
bm = bmesh.new()
uv_layer = bm.loops.layers.uv.new('UVMap')

RIB_BULGE = [1.0, .94, 1.05, .97, 1.03, .92, 1.0, 1.06]

def rib_radius(theta):
    # grooves between ribs: the radius dips at each rib boundary; every rib a little fatter or thinner
    pos = theta / (2 * math.pi) * RIBS
    phase = pos % 1.0
    bulge = RIB_BULGE[int(pos) % RIBS]
    return bulge * (1 - 0.12 * (abs(phase - .5) * 2) ** 2.5)

rings = []
for (r, z) in PROFILE:
    ring = []
    for s in range(SEG):
        th = s / SEG * 2 * math.pi
        rr = r * rib_radius(th)
        # a squat, slightly lopsided body: wider one way, the top leaning
        lean = .035 * (z / H)
        sag = z * (1 + .05 * math.cos(th + 1.1)) if 0 < z < .5 else z
        ring.append(bm.verts.new((rr * 1.04 * math.cos(th) + lean, rr * .96 * math.sin(th), sag)))
    rings.append(ring)
top = bm.verts.new((0, 0, TOP_DIP))
bottom = bm.verts.new((0, 0, 0.01))
bm.verts.ensure_lookup_table()

def uv(s, k):
    return (s / SEG, PROFILE[k][1] / H * (111 / 128))

for k in range(len(rings) - 1):
    for s in range(SEG):
        s2 = (s + 1) % SEG
        f = bm.faces.new((rings[k][s], rings[k][s2], rings[k + 1][s2], rings[k + 1][s]))
        us = [(s / SEG, k), ((s + 1) / SEG, k), ((s + 1) / SEG, k + 1), (s / SEG, k + 1)]
        for loop, (u, kk) in zip(f.loops, us):
            loop[uv_layer].uv = (u, PROFILE[kk][1] / H * (111 / 128))
# top cap into the dimple, bottom cap
for s in range(SEG):
    s2 = (s + 1) % SEG
    f = bm.faces.new((rings[-1][s], rings[-1][s2], top))
    for loop, u in zip(f.loops, (s / SEG, (s + 1) / SEG, (s + .5) / SEG)):
        loop[uv_layer].uv = (u, 0.85)
    f = bm.faces.new((rings[0][s2], rings[0][s], bottom))
    for loop, u in zip(f.loops, ((s + 1) / SEG, s / SEG, (s + .5) / SEG)):
        loop[uv_layer].uv = (u, 0.01)

# stem: a bent 5-sided tapering stalk out of the dimple
SIDES = 5
stem_rings = []
pts = [(0.03, 0.0, TOP_DIP - .02, .06), (0.045, 0.01, .6, .05), (0.075, 0.02, .65, .043), (0.12, 0.0, .665, .036), (0.15, -0.02, .655, .03)]
for (x, y, z, r) in pts:
    ring = []
    for s in range(SIDES):
        th = s / SIDES * 2 * math.pi + .3
        ring.append(bm.verts.new((x + r * math.cos(th), y + r * math.sin(th), z)))
    stem_rings.append(ring)
for k in range(len(stem_rings) - 1):
    for s in range(SIDES):
        s2 = (s + 1) % SIDES
        f = bm.faces.new((stem_rings[k][s], stem_rings[k][s2], stem_rings[k + 1][s2], stem_rings[k + 1][s]))
        for loop, (u, v) in zip(f.loops, ((s / SIDES, 0), ((s + 1) / SIDES, 0), ((s + 1) / SIDES, 1), (s / SIDES, 1))):
            loop[uv_layer].uv = (u * .9, 0.885 + v * .07)
tip = bm.faces.new(list(reversed(stem_rings[-1])) if False else stem_rings[-1])
for loop in tip.loops: loop[uv_layer].uv = (0.5, 0.985)

bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bm.to_mesh(mesh)
bm.free()
for p in mesh.polygons: p.use_smooth = False
obj = bpy.data.objects.new('SM_Pumpkin', mesh)
bpy.context.scene.collection.objects.link(obj)

mat = bpy.data.materials.new('M_Pumpkin')
mat.use_nodes = True
img = bpy.data.images.load(os.path.join(OUT, 'T_Pumpkin.png'))
nodes = mat.node_tree.nodes
texnode = nodes.new('ShaderNodeTexImage'); texnode.image = img; texnode.interpolation = 'Closest'
bsdf = nodes.get('Principled BSDF')
mat.node_tree.links.new(texnode.outputs['Color'], bsdf.inputs['Base Color'])
obj.data.materials.append(mat)

bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); bpy.context.view_layer.objects.active = obj
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'SM_Pumpkin.fbx'), use_selection=True, apply_unit_scale=True,
                         apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                         path_mode='STRIP', mesh_smooth_type='FACE')
print('tris', sum(len(p.vertices) - 2 for p in mesh.polygons), 'verts', len(mesh.vertices))

# ---------------- preview render (Eevee/workbench)
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'TEXTURE'
scene.render.resolution_x = scene.render.resolution_y = 512
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
scene.collection.objects.link(cam)
cam.location = (1.6, -1.6, 1.0)
direction = -cam.location + __import__('mathutils').Vector((0, 0, .3))
cam.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
scene.camera = cam
scene.render.filepath = os.path.join(OUT, 'preview.png')
bpy.ops.render.render(write_still=True)
