"""Pumpkins after the user's reference (three variants): rounder ribbed bodies, a thick bent stem with a square cut top,
a mottled rust-orange pixel texture with a dirty bottom. Run: python3 build2.py"""
import math, os, random
import bpy, bmesh
import numpy as np
from PIL import Image
from mathutils import Vector

OUT = '/tmp/pumpkin/out2'; os.makedirs(OUT, exist_ok=True)

# ---------------- shared texture 256 x 256: body area v 0..0.82, stem area v 0.84..1
W = 256
rng = np.random.default_rng(11)

def value_noise(shape, cell, seed):
    r = np.random.default_rng(seed)
    gh, gw = shape[0] // cell + 2, shape[1] // cell + 2
    g = r.random((gh, gw))
    ys = np.arange(shape[0]) / cell; xs = np.arange(shape[1]) / cell
    y0 = ys.astype(int); x0 = xs.astype(int); fy = (ys - y0)[:, None]; fx = (xs - x0)[None, :]
    fy = fy * fy * (3 - 2 * fy); fx = fx * fx * (3 - 2 * fx)
    a = g[y0][:, x0]; b = g[y0][:, (x0 + 1) % gw]; c = g[(y0 + 1)][:, x0]; d = g[(y0 + 1)][:, (x0 + 1) % gw]
    return a * (1 - fx) * (1 - fy) + b * fx * (1 - fy) + c * (1 - fx) * fy + d * fx * fy

H_BODY = int(W * .82)
tex = np.zeros((W, W, 3), float)
# palette from the reference: rust orange body, light sun patches, darker blotches, earthy dirt
mid = np.array([178, 90, 56]); light = np.array([198, 116, 70]); dark = np.array([158, 76, 50]); dirt = np.array([108, 74, 60])
n1 = value_noise((W, W), 24, 1); n2 = value_noise((W, W), 10, 2); n3 = value_noise((W, W), 3, 3)
mottle = .55 * n1 + .3 * n2 + .15 * n3
for y in range(H_BODY):
    t = y / (H_BODY - 1)           # 0 bottom of the body, 1 top
    row = W - 1 - y
    for x in range(W):
        m = mottle[row, x]
        # quantized painted tones
        c = dark if m < .3 else mid if m < .66 else light if m < .84 else mid * .4 + light * .6 * 1.04
        # top and bottom darker (the ribs turn away), a bit of sun on the upper shoulder
        shade = .8 + .2 * math.sin(min(1, t * 1.1) * math.pi) + (.05 if .55 < t < .8 else 0)
        c = c * shade
        # dirt creeping up from the ground in ragged pixels
        line = .14 + .06 * value_noise((1, W), 14, 5)[0, x] + .04 * n3[row, x]
        if t < line: c = c * .35 + dirt * .65 * (0.85 + .3 * n3[row, x])
        tex[row, x] = c
# speckle the lot like the forest textures
tex[:] *= 1 + rng.normal(0, .035, (W, W, 1))
# stem strip: olive-brown wood, lighter cut top (v 0.84 .. 1)
sn = value_noise((W, W), 6, 7)
stem_lo, stem_hi, cut = np.array([112, 92, 52]), np.array([150, 124, 72]), np.array([176, 150, 96])
for y in range(int(W * .84), W):
    row = W - 1 - y
    for x in range(W):
        c = stem_lo if sn[row, x] < .5 else stem_hi
        if y > W * .955: c = cut * (.9 + .2 * sn[row, x])
        tex[row, x] = c
Image.fromarray(np.clip(tex, 0, 255).astype(np.uint8)).save(os.path.join(OUT, 'T_Pumpkin.png'))

# ---------------- meshes
VARIANTS = {
    # name: ribs, height, width, top tuck, stem (bend x, bend z, height, radius), seed
    'A': dict(ribs=9, h=.64, w=.5, tuck=.1, stem=(.12, .18, .2, .085), seed=1),
    'B': dict(ribs=10, h=.58, w=.53, tuck=.09, stem=(.14, .14, .18, .08), seed=2),
    'C': dict(ribs=9, h=.72, w=.46, tuck=.09, stem=(-.08, .2, .19, .08), seed=3),
}
PER_RIB = 4
RINGS = [0, .05, .14, .27, .42, .58, .72, .84, .93, 1.0]   # heights as shares of the body

def body_radius(s):
    # side profile: a round squat ball, tucked at the bottom and the top
    return math.sin(math.pi * (0.05 + .9 * s)) ** .45

def build(name, v):
    random.seed(v['seed'])
    ribs, seg = v['ribs'], v['ribs'] * PER_RIB
    bulge = [1 + random.uniform(-.05, .05) for _ in range(ribs)]
    mesh = bpy.data.meshes.new('SM_Pumpkin_' + name)
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new('UVMap')
    H, Wd = v['h'], v['w']
    rings = []
    for k, s in enumerate(RINGS):
        ring = []
        z = s * H
        for i in range(seg):
            th = i / seg * 2 * math.pi
            pos = i / PER_RIB
            phase = pos % 1.0
            rib = int(pos) % ribs
            # each rib bulges round; the grooves cut in deeper near the top and the bottom
            groove_depth = .09 + .1 * (abs(s - .5) * 2) ** 2
            prof = math.sin(math.pi * phase) ** .8
            r = Wd * body_radius(s) * bulge[rib] * (1 - groove_depth * (1 - prof))
            # the top dips into the stem: the last ring sits lower in the middle
            zz = z - (v['tuck'] if s >= 1 else 0)
            if s >= 1: r = Wd * .14
            ring.append(bm.verts.new((r * math.cos(th), r * math.sin(th), zz)))
        rings.append(ring)
    bottom = bm.verts.new((0, 0, .02))
    vmax = .82
    for k in range(len(rings) - 1):
        for i in range(seg):
            i2 = (i + 1) % seg
            f = bm.faces.new((rings[k][i], rings[k][i2], rings[k + 1][i2], rings[k + 1][i]))
            for loop, (u, kk) in zip(f.loops, ((i, k), (i + 1, k), (i + 1, k + 1), (i, k + 1))):
                loop[uvl].uv = (u / seg * 4, RINGS[kk] * vmax)
    for i in range(seg):
        i2 = (i + 1) % seg
        f = bm.faces.new((rings[0][i2], rings[0][i], bottom))
        for loop, u in zip(f.loops, ((i + 1) / seg, i / seg, (i + .5) / seg)):
            loop[uvl].uv = (u * 4, .005)
    # close the top round the stem's foot
    top = bm.verts.new((0, 0, H - v['tuck'] - .01))
    for i in range(seg):
        i2 = (i + 1) % seg
        f = bm.faces.new((rings[-1][i], rings[-1][i2], top))
        for loop, u in zip(f.loops, (i / seg, (i + 1) / seg, (i + .5) / seg)):
            loop[uvl].uv = (u * 4, vmax * .99)
    # stem: thick, 6 sides, a kink, a square-ish cut top a little wider than the neck
    bx, bz, sh, sr = v['stem']
    base = Vector((0, 0, H - v['tuck'] - .02))
    pts = [(base, sr * 1.25), (base + Vector((bx * .1, 0, sh * .45)), sr), (base + Vector((bx * .45, 0, sh * .8)), sr * .9),
           (base + Vector((bx, 0, sh)), sr * 1.05), (base + Vector((bx * 1.25, 0, sh * 1.05)), sr * 1.3)]
    SIDES = 5
    srings = []
    for j, (p, r) in enumerate(pts):
        ring = []
        for i in range(SIDES):
            th = i / SIDES * 2 * math.pi + .2
            wob = 1 + .08 * math.sin(i * 2.3 + j)
            ring.append(bm.verts.new((p.x + r * wob * math.cos(th), p.y + r * wob * math.sin(th), p.z)))
        srings.append(ring)
    for j in range(len(srings) - 1):
        for i in range(SIDES):
            i2 = (i + 1) % SIDES
            f = bm.faces.new((srings[j][i], srings[j][i2], srings[j + 1][i2], srings[j + 1][i]))
            for loop, (u, vv) in zip(f.loops, ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1))):
                loop[uvl].uv = (u / SIDES, .845 + vv / (len(srings) - 1) * .105)
    capf = bm.faces.new(srings[-1])
    for loop, i in zip(capf.loops, range(SIDES)):
        a = i / SIDES * 2 * math.pi
        loop[uvl].uv = (.5 + .2 * math.cos(a), .975 + .02 * math.sin(a))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh); bm.free()
    for p in mesh.polygons: p.use_smooth = False
    obj = bpy.data.objects.new('SM_Pumpkin_' + name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.data.materials.append(mat)
    tris = sum(len(p.vertices) - 2 for p in mesh.polygons)
    return obj, tris

bpy.ops.wm.read_factory_settings(use_empty=True)
mat = bpy.data.materials.new('M_Pumpkin'); mat.use_nodes = True
img = bpy.data.images.load(os.path.join(OUT, 'T_Pumpkin.png'))
tn = mat.node_tree.nodes.new('ShaderNodeTexImage'); tn.image = img; tn.interpolation = 'Closest'
mat.node_tree.links.new(tn.outputs['Color'], mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
objs = []
for name, v in VARIANTS.items():
    obj, tris = build(name, v)
    print(name, 'tris', tris)
    objs.append(obj)
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, f'SM_Pumpkin_{name}.fbx'), use_selection=True, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                             path_mode='STRIP', mesh_smooth_type='FACE')
# preview: the three side by side
for k, o in enumerate(objs): o.location.x = (k - 1) * 1.3
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'TEXTURE'
scene.render.resolution_x, scene.render.resolution_y = 1200, 480
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); scene.collection.objects.link(cam)
cam.location = (0, -4.2, 1.3)
cam.rotation_euler = (Vector((0, 0, .35)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
cam.data.lens = 50
scene.camera = cam
scene.render.filepath = os.path.join(OUT, 'preview.png')
bpy.ops.render.render(write_still=True)
