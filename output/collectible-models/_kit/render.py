"""Materials, preview renders, shelf-fit render and export for collectible items."""
import math, os, json
import numpy as np
import bpy
from mathutils import Vector, Matrix
import cam as C

SHOP = '/mnt/user-data/uploads/hukaton/hukaton/Assets/ShopArt'

def apply_texture(obj, tex_path, name):
    """One material for the whole item: the baked texture with point (closest) sampling, matte like ShopArt
    (URP/Lit Metallic 0 Smoothness 0). Part material slots are collapsed into this single material."""
    img = bpy.data.images.load(tex_path, check_existing=False)
    img.name = os.path.basename(tex_path)
    mat = bpy.data.materials.new('M_' + name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes.get('Principled BSDF')
    t = nt.nodes.new('ShaderNodeTexImage')
    t.image = img
    t.interpolation = 'Closest'
    nt.links.new(t.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = 1.0
    bsdf.inputs['Metallic'].default_value = 0.0
    if 'Specular IOR Level' in bsdf.inputs:
        bsdf.inputs['Specular IOR Level'].default_value = 0.0
    me = obj.data
    me.materials.clear()
    me.materials.append(mat)
    for p in me.polygons:
        p.material_index = 0
    return mat

def add_emission(obj, tex_path_mask=None):
    pass

def _world(color=(0.42, 0.40, 0.38), strength=1.0):
    w = bpy.data.worlds.new('W')
    w.use_nodes = True
    bg = w.node_tree.nodes['Background']
    bg.inputs[0].default_value = (*color, 1)
    bg.inputs[1].default_value = strength
    bpy.context.scene.world = w

def _setup_render(W, H, samples=24, transparent=False):
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    try:
        sc.cycles.denoiser = 'OPENIMAGEDENOISE'
    except Exception:
        pass
    sc.cycles.max_bounces = 3
    sc.render.resolution_x, sc.render.resolution_y = W, H
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = transparent
    sc.view_settings.view_transform = 'Standard'
    sc.render.image_settings.file_format = 'PNG'

def _lights():
    for o in [o for o in bpy.data.objects if o.type == 'LIGHT']:
        bpy.data.objects.remove(o)
    def sun(rot, e, col=(1, 0.95, 0.88)):
        l = bpy.data.lights.new('sun', 'SUN'); l.energy = e; l.color = col; l.angle = math.radians(10)
        o = bpy.data.objects.new('sun', l); bpy.context.scene.collection.objects.link(o)
        o.rotation_euler = [math.radians(a) for a in rot]
    sun((50, 0, -35), 1.6)
    sun((60, 0, 150), 0.5, (0.85, 0.9, 1.0))

def render_views(obj, out_png, concept_cam=None, concept_size=None, concept_path=None, size=640):
    """Preview sheet: [concept | model from the concept camera | front | back 3/4 | side | top] on warm gray."""
    import cv2
    _world((0.36, 0.34, 0.32), 0.9)
    _lights()
    tiles = []
    labels = []
    sz = obj.dimensions
    h = max(sz.z, 0.05)
    R = max(sz.x, sz.y, sz.z)
    tgt = [0, 0, sz.z * 0.5]
    views = []
    if concept_cam is not None:
        W, H = concept_size
        views.append(('concept camera', concept_cam, W, H))
    for name, az, el in (('front', 0, 12), ('back 3/4', 145, 18), ('left side', -90, 10), ('right 3/4', 50, 30)):
        views.append((name, {'az': az, 'el': el, 'dist': R * 4.2, 'fov': 18, 'target': tgt, 'shift': [0, 0]}, size, size))
    tmp = '/tmp/_kit_tile.png'
    for name, c, W, H in views:
        for o in [o for o in bpy.data.objects if o.type == 'CAMERA']:
            bpy.data.objects.remove(o)
        C.blender_camera(c, W, H)
        _setup_render(W, H)
        bpy.context.scene.render.filepath = tmp
        bpy.ops.render.render(write_still=True)
        im = cv2.imread(tmp)
        scale = size / max(im.shape[:2]) if name != 'concept camera' else size / im.shape[0]
        im = cv2.resize(im, (int(im.shape[1] * scale), int(im.shape[0] * scale)), interpolation=cv2.INTER_AREA)
        tiles.append(im); labels.append(name)
    if concept_path is not None and concept_cam is not None:
        cim = cv2.imread(concept_path, cv2.IMREAD_UNCHANGED)
        if cim.shape[2] == 4:
            a = cim[:, :, 3:4] / 255.0
            cim = (cim[:, :, :3] * a + np.array([82, 87, 92]) * (1 - a)).astype(np.uint8)
        s = size / cim.shape[0]
        cim = cv2.resize(cim, (int(cim.shape[1] * s), size), interpolation=cv2.INTER_AREA)
        tiles.insert(0, cim); labels.insert(0, 'concept')
    # layout: row 1 concept + model at concept cam, row 2 the 4 turnaround views
    def label(im, t):
        im = im.copy()
        cv2.rectangle(im, (0, 0), (im.shape[1], 34), (30, 30, 30), -1)
        cv2.putText(im, t, (10, 24), cv2.FONT_HERSHEY_SIMPLEX, 0.75, (235, 235, 235), 2)
        return im
    tiles = [label(t, l) for t, l in zip(tiles, labels)]
    n_top = 2 if concept_cam is not None and concept_path else (1 if concept_cam is not None else 0)
    top = tiles[:n_top]; bot = tiles[n_top:]
    row_b = np.concatenate(bot, 1)
    if top:
        row_t = np.concatenate(top, 1)
        Wt = max(row_t.shape[1], row_b.shape[1])
        pad = lambda r: np.pad(r, ((0, 0), (0, Wt - r.shape[1]), (0, 0)), constant_values=40)
        sheet = np.concatenate([pad(row_t), pad(row_b)], 0)
    else:
        sheet = row_b
    cv2.imwrite(out_png, sheet)
    return out_png

def shelf_fit_render(obj, out_png, slot=5, W=960, H=640):
    """Place a copy of the item on the real ShopArt display case pad and render from near camera B."""
    import cv2
    coll = bpy.data.collections.new('shelf'); bpy.context.scene.collection.children.link(coll)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(SHOP, 'Models/SM_DisplayCase.fbx'))
    case = [o for o in bpy.data.objects if o not in before]
    img = bpy.data.images.load(os.path.join(SHOP, 'Textures/T_DisplayCase.png'), check_existing=True)
    m = bpy.data.materials.new('M_DisplayCase_preview'); m.use_nodes = True
    t = m.node_tree.nodes.new('ShaderNodeTexImage'); t.image = img; t.interpolation = 'Closest'
    b = m.node_tree.nodes['Principled BSDF']; b.inputs['Roughness'].default_value = 1
    m.node_tree.links.new(t.outputs['Color'], b.inputs['Base Color'])
    for o in case:
        if o.type == 'MESH':
            o.data.materials.clear(); o.data.materials.append(m)
    pads = {o.name: o for o in case if o.name.startswith('Display_SlotPad_')}
    copies = []
    for i in (1, 2, 3, 4, 5, 6):
        pad = pads.get(f'Display_SlotPad_{i}')
        if not pad:
            continue
        bb = [pad.matrix_world @ Vector(c) for c in pad.bound_box]
        cx = sum(v.x for v in bb) / 8; cy = sum(v.y for v in bb) / 8; top = max(v.z for v in bb)
        if i == slot:
            o = obj.copy(); o.data = obj.data
            bpy.context.scene.collection.objects.link(o)
            # the item's front (-Y) faces the camera B side (+X in Blender here)
            o.matrix_world = Matrix.Translation((cx, cy, top)) @ Matrix.Rotation(math.radians(90), 4, 'Z')
            copies.append(o)
    # camera B: Unity (0.40,1.60,1.695) euler (23,90,0) FOV80 vertical -> Blender pos (-0.40,-1.695,1.60) looking -X
    cam_data = bpy.data.cameras.new('B'); cam_data.sensor_fit = 'VERTICAL'; cam_data.angle = math.radians(80)
    camo = bpy.data.objects.new('B', cam_data); bpy.context.scene.collection.objects.link(camo)
    camo.location = (-0.40, -1.695, 1.60)
    camo.rotation_euler = (math.radians(90 - 23), 0, math.radians(90))
    bpy.context.scene.camera = camo
    _world((0.25, 0.22, 0.19), 1.4)
    _lights()
    _setup_render(W, H, samples=24)
    tmp = '/tmp/_kit_shelf.png'
    bpy.context.scene.render.filepath = tmp
    bpy.ops.render.render(write_still=True)
    full = cv2.imread(tmp)
    # also a closer framing on the slot
    cam_data.angle = math.radians(30)
    pad = pads[f'Display_SlotPad_{slot}']
    bb = [pad.matrix_world @ Vector(c) for c in pad.bound_box]
    tgt = Vector((sum(v.x for v in bb) / 8, sum(v.y for v in bb) / 8, max(v.z for v in bb) + 0.11))
    d = (tgt - camo.location)
    camo.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    bpy.ops.render.render(write_still=True)
    close = cv2.imread(tmp)
    sheet = np.concatenate([full, close], 1)
    cv2.putText(sheet, 'ShopArt display case, camera B', (12, 28), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (240, 240, 240), 2)
    cv2.imwrite(out_png, sheet)
    for o in copies + case + [camo]:
        bpy.data.objects.remove(o)
    return out_png

def export(obj, out_dir, name, tex_path):
    """FBX like ShopArt: 1 unit = 1 m, Forward -Z, Up Y, apply transform, no anim/camera/light. Also .blend."""
    os.makedirs(out_dir, exist_ok=True)
    for o in bpy.context.view_layer.objects:
        o.select_set(o == obj)
    bpy.context.view_layer.objects.active = obj
    obj.name = name
    obj.data.name = name
    fbx = os.path.join(out_dir, name + '.fbx')
    bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, axis_forward='-Z', axis_up='Y',
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True,
                             object_types={'MESH'}, use_mesh_modifiers=True, mesh_smooth_type='FACE',
                             add_leaf_bones=False, bake_anim=False, path_mode='STRIP')
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, name + '.blend'), copy=True)
    return fbx

def card(out_dir, name, stats):
    """One picture for the user: preview sheet on top, shelf close-up below, with key numbers."""
    import cv2
    pv = cv2.imread(os.path.join(out_dir, 'preview.png'))
    sh = cv2.imread(os.path.join(out_dir, 'shelf.png'))
    sh = sh[:, sh.shape[1] // 2:]
    s = pv.shape[1] / 2 / sh.shape[1]
    sh = cv2.resize(sh, (int(sh.shape[1] * s), int(sh.shape[0] * s)), interpolation=cv2.INTER_AREA)
    info = np.full((sh.shape[0], pv.shape[1] - sh.shape[1], 3), 36, np.uint8)
    x, y, z = stats['size_xyz_m']
    lines = [name, f"{stats['tris']} tris", f"{x:.3f} x {y:.3f} x {z:.3f} m (W x D x H)",
             f"texture {stats.get('texture', '')}", 'fits display slot: ' + ('yes' if stats.get('fits_shelf') else 'NO'),
             'right: on the ShopArt display case, camera B']
    for i, t in enumerate(lines):
        cv2.putText(info, t, (30, 70 + i * 52), cv2.FONT_HERSHEY_SIMPLEX, 1.1 if i == 0 else 0.9, (235, 235, 235), 2)
    out = np.concatenate([pv, np.concatenate([info, sh], 1)], 0)
    p = os.path.join(out_dir, 'card.jpg')
    cv2.imwrite(p, out, [cv2.IMWRITE_JPEG_QUALITY, 88])
    return p
