"""Blender-side helpers: shells -> objects, scene, canon-matched camera, review renders, overlays."""
import math, os
import bpy, bmesh
from mathutils import Vector, Matrix

CANON = '/home/claude/pack/mountain-god-art-pack-v1/01-concepts/00-canonical.png'
CANON_W, CANON_H = 1199, 1312

# Palette from the modelling guide (visual references, not an approved standard)
PAL = {
    'body_light': '#7B8DB7', 'body': '#5C70A4', 'body_shadow': '#405482', 'hat': '#6B4A86',
    'beard': '#A0AAC3', 'eye': '#C2B6CC', 'stroke': '#3A3546', 'cloth': '#826B64', 'leaf': '#718875',
    'branch': '#4A5E9A',
}


def hex_rgb(h, a=1.0):
    h = h.lstrip('#')
    srgb = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    lin = [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in srgb]
    return (*lin, a)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.unit_settings.system = 'METRIC'
    sc.unit_settings.scale_length = 1.0
    return sc


def material(name, color_hex):
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
        m.diffuse_color = hex_rgb(color_hex)
        m.roughness = 1.0
        m.metallic = 0.0
    return m


MAT_NAMES = {'body': ('M_MG01_Body', PAL['body']), 'cloth': ('M_MG01_Cloth', PAL['cloth']),
             'leaves': ('M_MG01_Leaves', PAL['leaf']), 'hat': ('M_MG01_Body', PAL['body']),
             'beard': ('M_MG01_Body', PAL['body']), 'branch': ('M_MG01_Body', PAL['body'])}


def shells_to_object(name, shells, collection=None, smooth=False, preview_colors=True):
    """Join shells into ONE mesh object. Each shell's .group becomes a vertex group (weight 1),
    each shell's .mat a material slot. Normals recalculated outward. Returns the object."""
    me = bpy.data.meshes.new(name)
    verts, faces, fmat, vgroups = [], [], [], {}
    mats = []
    for sh in shells:
        off = len(verts)
        verts += [tuple(p) for p in sh.verts]
        if sh.mat not in mats:
            mats.append(sh.mat)
        mi = mats.index(sh.mat)
        for fc in sh.faces:
            faces.append(tuple(i + off for i in fc))
            fmat.append(mi)
        g = sh.group or sh.name
        vgroups.setdefault(g, []).extend(range(off, off + len(sh.verts)))
    me.from_pydata(verts, [], faces)
    me.validate(clean_customdata=False)
    for i, p in enumerate(me.polygons):
        p.material_index = fmat[i]
        p.use_smooth = smooth
    ob = bpy.data.objects.new(name, me)
    (collection or bpy.context.scene.collection).objects.link(ob)
    for m in mats:
        ob.data.materials.append(preview_material(m) if preview_colors else material(*MAT_NAMES.get(m, MAT_NAMES['body'])))
    for g, ids in vgroups.items():
        vg = ob.vertex_groups.get(g) or ob.vertex_groups.new(name=g)
        vg.add(ids, 1.0, 'REPLACE')
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    return ob


def preview_material(key):
    col = {'body': PAL['body'], 'cloth': PAL['cloth'], 'leaves': PAL['leaf'], 'hat': PAL['hat'],
           'beard': PAL['beard'], 'branch': PAL['branch'], 'eye': PAL['eye']}.get(key, PAL['body'])
    return material('PRE_' + key, col)


# ---------------------------------------------------------------- cameras
# Camera that approximately reproduces the perspective of 00-canonical.png (fitted to the three
# support contacts and the figure height). Blender camera looks along +Y (front view).
CANON_CAM = dict(loc=(0.75, -30.0, 8.7), rot_deg=(90.0, 0.0, 0.0), lens=54.9, sensor_h=36.0)


def make_camera(sc, loc, look_at=None, rot_deg=None, lens=50.0, sensor_h=36.0, ortho=None, name='cam'):
    cam = bpy.data.objects.get(name)
    if cam is None:
        cam = bpy.data.objects.new(name, bpy.data.cameras.new(name))
        sc.collection.objects.link(cam)
    cam.location = Vector(loc)
    if rot_deg is not None:
        cam.rotation_euler = [math.radians(a) for a in rot_deg]
    else:
        d = Vector(look_at) - cam.location
        cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    cd = cam.data
    cd.sensor_fit = 'VERTICAL'
    cd.sensor_height = sensor_h
    cd.lens = lens
    cd.clip_end = 2000
    if ortho:
        cd.type = 'ORTHO'
        cd.ortho_scale = ortho
    else:
        cd.type = 'PERSP'
    sc.camera = cam
    return cam


def setup_workbench(sc, color_type='MATERIAL', w=CANON_W, h=CANON_H, transparent=True, outline=True):
    sc.render.engine = 'BLENDER_WORKBENCH'
    sh = sc.display.shading
    sh.light = 'STUDIO'
    sh.color_type = color_type
    sh.show_object_outline = outline
    sh.object_outline_color = (0.05, 0.05, 0.1)
    sh.show_cavity = False
    sc.render.resolution_x, sc.render.resolution_y = w, h
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = transparent
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    sc.display_settings.display_device = 'sRGB'
    sc.view_settings.view_transform = 'Standard'


def render(sc, path):
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


def canon_render(sc, path, color_type='MATERIAL'):
    setup_workbench(sc, color_type)
    make_camera(sc, CANON_CAM['loc'], rot_deg=CANON_CAM['rot_deg'], lens=CANON_CAM['lens'], sensor_h=CANON_CAM['sensor_h'])
    return render(sc, path)


def turntable(sc, out_dir, prefix, target=(0.5, 0.0, 9.0), dist=42.0, height=9.0, angles=(0, 90, 180, 270),
              w=900, h=980, lens=50.0, color_type='MATERIAL'):
    """Views around the model. angle 0 = front (camera on -Y), 90 = from +X (canonical right side)."""
    setup_workbench(sc, color_type, w, h)
    paths = []
    for a in angles:
        r = math.radians(a)
        loc = (target[0] + math.sin(r) * dist, target[1] - math.cos(r) * dist, height)
        make_camera(sc, loc, look_at=target, lens=lens)
        p = os.path.join(out_dir, f'{prefix}_{a:03d}.png')
        render(sc, p)
        paths.append(p)
    return paths


def overlay(render_path, out_path, canon=CANON, alpha=0.55):
    """Canon in grey + render tinted on top, plus side-by-side strip. Uses PIL."""
    from PIL import Image, ImageOps, ImageChops
    c = Image.open(canon).convert('RGB')
    r = Image.open(render_path).convert('RGBA').resize(c.size)
    grey = ImageOps.grayscale(c).convert('RGB')
    tint = Image.new('RGBA', c.size, (255, 60, 40, 0))
    a = r.split()[3].point(lambda v: int(v * alpha))
    tint.putalpha(a)
    ov = grey.copy()
    ov.paste(Image.alpha_composite(Image.new('RGBA', c.size, (0, 0, 0, 0)), tint), (0, 0), tint)
    # render on canon background colour for side by side
    bg = Image.new('RGBA', c.size, (200, 200, 205, 255))
    rr = Image.alpha_composite(bg, r).convert('RGB')
    strip = Image.new('RGB', (c.width * 3, c.height))
    strip.paste(c, (0, 0)); strip.paste(rr, (c.width, 0)); strip.paste(ov, (c.width * 2, 0))
    strip = strip.resize((strip.width // 2, strip.height // 2))
    strip.save(out_path)
    return out_path


def contact_sheet(paths, out_path, cols=4, scale=0.5, bg=(200, 200, 205)):
    from PIL import Image
    ims = [Image.open(p).convert('RGBA') for p in paths]
    w, h = int(ims[0].width * scale), int(ims[0].height * scale)
    rows = (len(ims) + cols - 1) // cols
    sheet = Image.new('RGB', (w * cols, h * rows), bg)
    for i, im in enumerate(ims):
        b = Image.new('RGBA', im.size, bg + (255,))
        b = Image.alpha_composite(b, im).convert('RGB').resize((w, h))
        sheet.paste(b, ((i % cols) * w, (i // cols) * h))
    sheet.save(out_path)
    return out_path


def tri_count(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)
