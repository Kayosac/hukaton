"""24 Petrified Queen egg — collectible shelf item (four-view concept). Run: python3 build.py [--opt] [--align-only]

Masses: one upright egg, 9 sides x 7 profile rings (low-poly, broad faceted bands like the concept), broad bottom with a
small flat seat so it stands, tapered top. Slight asymmetry: tip leans a touch to the back-left, wobbled ring radii.
Painted: stone mottling, olive patches, the gold vein network (front Y, continuing around) - all from the 4 views.
"""
import sys, os, json, math
sys.path.insert(0, '/home/claude/kit')
import numpy as np
import cv2
import bpy
from geo import Builder, clear_scene, check_fit
import bake as BK
import bake4 as B4
import render as R
import render4 as R4

HERE = os.path.dirname(os.path.abspath(__file__))
PREFIX = '/home/claude/concepts/24-petrified-queen-egg'
ID = 'CI24_PetrifiedQueenEgg'
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)

H = 0.205
# (radius, z): broad lower third, flattened seat, rounded taper to the top
PROFILE = [(0.046, 0.0), (0.066, 0.011), (0.078, 0.035), (0.081, 0.066), (0.078, 0.100), (0.069, 0.134),
           (0.055, 0.162), (0.036, 0.186), (0.014, 0.201), (0.0, H)]
NS = 11
WOB = [0.03, -0.02, 0.01, 0.035, -0.03, 0.0, 0.02, -0.025, 0.015, -0.01, 0.025]


def build():
    b = Builder(ID, seed=24)
    rings = b.lathe('shell', PROFILE[:-1], sides=NS, rot=0.2, jitter=0.0012, cap_top=False, cap_bottom=False,
                    scale=(1.0, 0.97))
    b.cap(rings[-1], b.part('tip'), center=(0.0, 0.0, H))
    b.cap(rings[0], b.part('base'), flip=True)
    for i, v in enumerate(b.bm.verts):
        z = v.co.z
        ang = math.atan2(v.co.y, v.co.x)
        k = int(((ang + math.pi) / (2 * math.pi)) * NS) % NS
        r = math.hypot(v.co.x, v.co.y)
        if r > 1e-6:
            f = 1 + WOB[k] * min(1.0, z / 0.05)
            v.co.x *= f; v.co.y *= f
        v.co.x -= 0.010 * (z / H) ** 2      # tip a little to the left
        v.co.y += 0.006 * (z / H) ** 2      # and back
    # the top cap and the ring under it are seen only at grazing angles (concept silhouette edge = dark outline):
    # they become the 'tip' part, filled with the concept's top-of-egg stone colours
    tip = b.part('tip')
    for f in b.bm.faces:
        if f.calc_center_median().z > 0.180:
            f.material_index = tip
    return b


clear_scene()
b = build()
obj = b.finish()
print('fit', check_fit(obj))
BK.unwrap(obj)

cam_path = os.path.join(HERE, 'cameras.json')
if os.path.exists(cam_path):
    cams = json.load(open(cam_path))
else:
    cams = B4.default_cams({'az': 0, 'el': 12, 'dist': 1.6, 'fov': 9.5, 'target': [0, 0, 0.1], 'shift': [0, 0], 'roll': 0})
views = B4.four_views(PREFIX, cams)
if '--opt' in sys.argv:
    print('opt', B4.optimize_views(obj, views))
    json.dump({v['name']: v['cam'] for v in views}, open(cam_path, 'w'), indent=1)
iou = B4.align_all(obj, views, os.path.join(OUT, 'align.png'))
print('IoU', iou)
if '--align-only' in sys.argv:
    sys.exit()

size = BK.texture_size_for(obj)
tex_path = os.path.join(OUT, f'T_{ID}.png')
def concept_palette(view, box, k=4):
    im = cv2.cvtColor(cv2.imread(f'{PREFIX}_{view}.png'), cv2.COLOR_BGR2RGB)
    y0, y1, x0, x1 = box
    pal, w = BK._kmeans_palette(im[y0:y1, x0:x1].reshape(-1, 3).astype(np.float32), k)
    return [tuple(map(float, c)) for c in pal]


# the tip is seen only at grazing angles: fill it with the concept's top-of-egg colours (olive-brown stone)
part_colors = {'tip': concept_palette('front', (55, 110, 250, 340)) + concept_palette('back', (60, 110, 290, 360), 3)}
tex, st = B4.bake_multi(obj, views, size=size, out_png=tex_path, debug_png=os.path.join(OUT, 'coverage.png'), part_colors=part_colors, erode_px=5, min_facing=0.28)
# despeckle: thin dark lines/dots (concept silhouette outline sampled at grazing angles, part seams) -> local median
img = cv2.imread(tex_path)
med = cv2.medianBlur(img, 5)
lum = lambda a: a.astype(np.float32) @ np.array([0.11, 0.59, 0.3], np.float32)
dark = (lum(med) - lum(img)) > 18
img[dark] = med[dark]
cv2.imwrite(tex_path, img)
print('bake', json.dumps(st), 'despeckled', int(dark.sum()))
R4.apply_texture(obj, tex_path, ID)
stats = check_fit(obj)
stats['texture'] = f'{size}x{size}'
stats['iou'] = iou
R4.preview4(obj, views, os.path.join(OUT, 'preview.png'))
if '--preview-only' in sys.argv:
    sys.exit()
R.shelf_fit_render(obj, os.path.join(OUT, 'shelf.png'))
R.export(obj, OUT, ID, tex_path)
json.dump({'id': ID, 'stats': stats, 'bake': st, 'cameras': {v['name']: v['cam'] for v in views}},
          open(os.path.join(OUT, 'stats.json'), 'w'), indent=1)
R4.card4(OUT, ID, stats)
print('done', stats)
