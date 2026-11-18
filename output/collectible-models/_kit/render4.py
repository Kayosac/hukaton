"""Card / material helpers for four-view items (20+). Uses render.py for lights, world, shelf render and export."""
import os, json
import numpy as np
import cv2
import bpy
import cam as C
import render as R


def apply_texture(obj, tex_path, name, glass_parts=(), glass_alpha=0.28):
    """One opaque material for everything (as render.apply_texture). Faces of `glass_parts` get a SECOND material
    M_<name>_Glass: same texture, alpha blended (Unity: URP/Lit Transparent, same texture, alpha ~glass_alpha).
    Call before collapsing parts (uses obj['parts'])."""
    parts = list(obj.get('parts', []))
    gidx = {parts.index(p) for p in glass_parts if p in parts}
    is_glass = [p.material_index in gidx for p in obj.data.polygons]
    mat = R.apply_texture(obj, tex_path, name)
    if not gidx:
        return mat, None
    g = bpy.data.materials.new('M_' + name + '_Glass')
    g.use_nodes = True
    nt = g.node_tree
    bsdf = nt.nodes.get('Principled BSDF')
    t = nt.nodes.new('ShaderNodeTexImage')
    t.image = bpy.data.images.get(os.path.basename(tex_path))
    t.interpolation = 'Closest'
    nt.links.new(t.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = 1.0
    bsdf.inputs['Metallic'].default_value = 0.0
    if 'Specular IOR Level' in bsdf.inputs:
        bsdf.inputs['Specular IOR Level'].default_value = 0.0
    bsdf.inputs['Alpha'].default_value = glass_alpha
    try:
        g.surface_render_method = 'BLENDED'
    except Exception:
        pass
    obj.data.materials.append(g)
    for p, gl in zip(obj.data.polygons, is_glass):
        if gl:
            p.material_index = 1
    return mat, g


def _render(c, W, H, samples=24):
    for o in [o for o in bpy.data.objects if o.type == 'CAMERA']:
        bpy.data.objects.remove(o)
    C.blender_camera(c, W, H)
    R._setup_render(W, H, samples=samples)
    tmp = '/tmp/_k4_tile.png'
    bpy.context.scene.render.filepath = tmp
    bpy.ops.render.render(write_still=True)
    return cv2.imread(tmp)


def _label(im, t):
    im = im.copy()
    cv2.rectangle(im, (0, 0), (im.shape[1], 30), (30, 30, 30), -1)
    cv2.putText(im, t, (8, 21), cv2.FONT_HERSHEY_SIMPLEX, 0.62, (235, 235, 235), 2)
    return im


def preview4(obj, views, out_png, tile=480):
    """Row 1: the 4 concept views. Row 2: the model from the fitted camera of each view. Row 3: free 3/4 views."""
    R._world((0.36, 0.34, 0.32), 0.9)
    R._lights()
    top, mid = [], []
    for v in views:
        cim = cv2.imread(v['concept'], cv2.IMREAD_UNCHANGED)[:, :, :3]
        H, W = cim.shape[:2]
        top.append(_label(cv2.resize(cim, (tile, tile), interpolation=cv2.INTER_AREA), 'concept ' + v['name']))
        im = _render(v['cam'], W, H)
        mid.append(_label(cv2.resize(im, (tile, tile), interpolation=cv2.INTER_AREA), 'model ' + v['name']))
    sz = obj.dimensions
    Rr = max(sz.x, sz.y, sz.z)
    tgt = [0, 0, sz.z * 0.5]
    bot = []
    for name, az, el in (('3/4 front-left', -40, 22), ('3/4 back-right', 140, 22), ('top', 15, 60), ('3/4 front-right', 35, 8)):
        c = {'az': az, 'el': el, 'dist': Rr * 4.2, 'fov': 18, 'target': tgt, 'shift': [0, 0]}
        bot.append(_label(cv2.resize(_render(c, 640, 640), (tile, tile), interpolation=cv2.INTER_AREA), name))
    sheet = np.concatenate([np.concatenate(r, 1) for r in (top, mid, bot)], 0)
    cv2.imwrite(out_png, sheet)
    return out_png


def card4(out_dir, name, stats, title_ru=''):
    pv = cv2.imread(os.path.join(out_dir, 'preview.png'))
    sh = cv2.imread(os.path.join(out_dir, 'shelf.png'))
    sh = sh[:, sh.shape[1] // 2:]
    s = pv.shape[1] / 2 / sh.shape[1]
    sh = cv2.resize(sh, (int(sh.shape[1] * s), int(sh.shape[0] * s)), interpolation=cv2.INTER_AREA)
    info = np.full((sh.shape[0], pv.shape[1] - sh.shape[1], 3), 36, np.uint8)
    x, y, z = stats['size_xyz_m']
    iou = stats.get('iou', {})
    lines = [name, f"{stats['tris']} tris", f"{x:.3f} x {y:.3f} x {z:.3f} m (W x D x H)",
             f"texture {stats.get('texture', '')}", 'fits display slot: ' + ('yes' if stats.get('fits_shelf') else 'NO'),
             'silhouette IoU: ' + ' '.join(f"{k} {v:.2f}" for k, v in iou.items() if k != 'mean'),
             'right: on the ShopArt display case, camera B']
    for i, t in enumerate(lines):
        cv2.putText(info, t, (30, 70 + i * 52), cv2.FONT_HERSHEY_SIMPLEX, 1.1 if i == 0 else 0.85, (235, 235, 235), 2)
    out = np.concatenate([pv, np.concatenate([info, sh], 1)], 0)
    p = os.path.join(out_dir, 'card.jpg')
    cv2.imwrite(p, out, [cv2.IMWRITE_JPEG_QUALITY, 88])
    return p
