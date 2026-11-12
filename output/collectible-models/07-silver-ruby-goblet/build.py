"""07 Silver goblet with rubies — collectible shelf item. Run: python3 build.py [--opt] [--align-only]

Masses (from the concept, metres): 12-sided faceted cup with a real hollow and a thick flat rim (r 0.089, top z 0.205),
a raised strap band around the cup (z 0.147-0.163), six octagonal settings on the band (big ones at 0/120/240 deg,
smaller ones at 60/180/300 deg) holding protruding step-cut rubies, an 8-sided stem with a knot, a hexagonal foot
(r 0.071) with a sloped top. Small stuff (scratches, tarnish, facet sparkle) is painted from the concept projection.
"""
import sys, os, json, math
sys.path.insert(0, '/home/claude/kit')
import numpy as np
import bpy
from mathutils import Vector
from geo import Builder, clear_scene, check_fit
import bake as BK
import render as R

HERE = os.path.dirname(os.path.abspath(__file__))
# the original concept PNG with a clean alpha (tools/make_alpha.py): the kit's colour-distance mask punches holes
# where the grey silver matches the grey background
CONCEPT_SRC = '/home/claude/concepts/collectible-concepts/07-silver-ruby-goblet-v1.png'
CONCEPT = os.path.join(HERE, 'concept_alpha.png')
ID = 'CI07_SilverRubyGoblet'
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)

clear_scene()
b = Builder(ID, seed=7)
d2r = math.radians

# ---------------- foot: hexagon, flat face to the front, sloped top -------------------------------------------
foot_prof = [(0.0745, 0.000), (0.0762, 0.004), (0.0752, 0.0125), (0.0695, 0.0152), (0.0505, 0.0250),
             (0.0310, 0.0335), (0.0200, 0.0355)]
b.lathe('foot', foot_prof, sides=6, rot=0.0, jitter=0.0006)

# ---------------- stem + knot: 8 sides, flat to the front -------------------------------------------------------
stem_prof = [(0.0215, 0.028), (0.0260, 0.0330), (0.0232, 0.0380), (0.0222, 0.0405),
             (0.0255, 0.0412), (0.0285, 0.0430), (0.0287, 0.0550), (0.0257, 0.0570),
             (0.0224, 0.0578), (0.0226, 0.0640), (0.0245, 0.0700), (0.0180, 0.0740)]
b.lathe('stem', stem_prof, sides=8, rot=math.pi / 8, jitter=0.0005)

# ---------------- cup: 12 facets, a facet centred on every gem; outer wall -> rim -> inner wall -> floor ---------
cup_out = [(0.0, 0.0695), (0.0225, 0.0712), (0.0310, 0.0742), (0.0380, 0.0772), (0.0462, 0.0820),
           (0.0548, 0.0870), (0.0635, 0.0925), (0.0700, 0.0975), (0.0760, 0.1080), (0.0810, 0.1200),
           (0.0845, 0.1335), (0.0860, 0.1460),
           # band (raised strap)
           (0.0908, 0.1470), (0.0916, 0.1484), (0.0916, 0.1608), (0.0908, 0.1622),
           # upper wall, slight flare to the rim
           (0.0868, 0.1630), (0.0884, 0.1820), (0.0900, 0.2050)]
cup_in = [(0.0830, 0.2050), (0.0814, 0.1900), (0.0800, 0.1650), (0.0750, 0.1350), (0.0640, 0.1120),
          (0.0450, 0.0960), (0.0, 0.0900)]
CUPR = 1.04                      # upper cup a little wider than the first measurement (fades in from z 0.09)
cup_out = [(r * (1 + (CUPR - 1) * min(1, max(0, (z - 0.09) / 0.03))), z) for r, z in cup_out]
cup_in = [(r * CUPR, z) for r, z in cup_in]
rings = b.lathe('cup', cup_out + cup_in, sides=12, rot=math.pi / 12, jitter=0.0007, cap_bottom=False, cap_top=False)
n_out = len(cup_out)
b.part('inside')
inner_rings = rings[n_out:]          # rings from the inner rim edge down to the floor pole
inner_set = set(v for r in inner_rings for v in r)
mi_inside = b.parts.index('inside')
for f in b.bm.faces:
    if f.material_index == b.parts.index('cup') and all(v in inner_set for v in f.verts):
        f.material_index = mi_inside

# ---------------- settings + rubies ------------------------------------------------------------------------------
def local_lathe(parts_per_seg, base, axis, prof, sides=8, rot=math.pi / 8, cap_part=None):
    """Lathe around an arbitrary axis: prof = [(circumradius, distance along axis from base)]."""
    ax = Vector(axis).normalized()
    rs = []
    for (r, h) in prof:
        pts = b.ring(Vector(base) + ax * h, r, sides, z_axis=ax, rot=rot, jitter=0.0002)
        pts = [Vector((p.x, p.y, base[2] + (p.z - base[2]) * XYS / ZS)) for p in pts]   # stays square after ZS
        rs.append(b.ring_verts(pts))
    for i, (r0, r1) in enumerate(zip(rs, rs[1:])):
        b.bridge(r0, r1, b.part(parts_per_seg[i]))
    if cap_part:
        b.cap(rs[-1], b.part(cap_part))
    return rs

ZG = 0.1545
XYS, ZS = 0.985, 1.055            # final scale: the concept reads a little taller than the first measurement
# five gems: big one in front, smaller ones at +-60 deg (seen at the concept's sides), two big ones at the back
# (+-140 deg: further round they would poke out of the side silhouette, which the concept does not show)
gems = [(-90, 0.95), (-30, 0.80), (-150, 0.80), (50, 0.96), (130, 0.96)]
for k, (ang, sc) in enumerate(gems):
    a = d2r(ang + b.j(1.5))
    axis = (math.cos(a), math.sin(a), 0.0)
    Rc = 0.0322 * sc * (1 + b.j(0.03))         # octagon circumradius (across flats = 1.848 Rc)
    zc = ZG + b.j(0.0012)
    # back ring sits inside the cup wall (between the inner and outer wall), never pokes into the hollow
    prof = [(Rc * f, h * CUPR) for f, h in ((0.86, 0.0830), (1.00, 0.0918), (0.92, 0.0952), (0.80, 0.0952),
                                             (0.78, 0.0934), (0.64, 0.0990), (0.45, 0.1012))]
    local_lathe(['setting', 'setting', 'setting', 'setting', 'gem', 'gem'], (0, 0, zc), axis, prof, cap_part='gem')

# ---------------- PSX crookedness: slight lean, rim a little lower on one side --------------------------------------
for v in b.bm.verts:
    z = v.co.z
    v.co.x *= XYS; v.co.y *= XYS; v.co.z *= ZS
    v.co.x += 0.006 * z
    v.co.y += -0.020 * z
    if z > 0.17:                 # rim plane tilted: higher at the back right, lower at the front left
        t = (z - 0.17) / 0.035
        v.co.z += t * 0.0028 * math.cos(math.atan2(v.co.y, v.co.x) - d2r(70))
    if z < 0.016:                # foot hexagon a little deeper front-to-back
        v.co.y *= 1.06; v.co.x *= 1.02
        if abs(v.co.x) > 0.05:   # side corners pulled forward: irregular hexagon, as drawn
            v.co.y -= 0.011 * min(1, (abs(v.co.x) - 0.05) / 0.025)

obj = b.finish()
fit = check_fit(obj)
print('fit', fit)

BK.unwrap(obj)
cam_path = os.path.join(HERE, 'camera.json')
cam = json.load(open(cam_path)) if os.path.exists(cam_path) else \
    {'az': 0, 'el': 16, 'dist': 0.95, 'fov': 20, 'target': [0, 0, 0.1], 'shift': [0, 0], 'roll': 0}
if '--opt' in sys.argv:
    cam, s = BK.optimize_camera(obj, CONCEPT, cam, keys=(() if '--noaz' in sys.argv else ('az',)) + ('dist', 'shift') + (('el',) if '--el' in sys.argv else ()))
    json.dump(cam, open(cam_path, 'w'), indent=1)
if '--rows' in sys.argv:
    sys.path.insert(0, os.path.join(HERE, 'tools')); import rowdiff; rowdiff.report(obj, CONCEPT, cam)
if '--edges' in sys.argv:
    sys.path.insert(0, os.path.join(HERE, 'tools')); import edges
    edges.overlay(obj, CONCEPT, cam, os.path.join(OUT, 'edges.png'))
print('IoU', BK.align_report(obj, CONCEPT, cam, os.path.join(OUT, 'align.png')), cam)
if '--align-only' in sys.argv:
    sys.exit()

size = BK.texture_size_for(obj)
ALL_BUT_IN = ['foot', 'stem', 'cup', 'setting', 'gem']
views = [{'name': 'orig', 'T': np.eye(4), 'bonus': 0.3, 'parts': ALL_BUT_IN},
         {'name': 'mirror_x', 'T': BK.T_mirror_x(), 'bonus': 0.1, 'parts': ALL_BUT_IN}]
# inside: painted from a palette picked from the concept's inner back wall (projection dragged the rim-top highlight
# into the hollow as light streaks), darkened with depth afterwards
views += [{'name': f'cup{a}', 'T': BK.T_rot_z(a), 'parts': ['cup']} for a in (60, -60, 120, -120, 180)]
# every setting+ruby is textured from the big front one: exact similarity transform (rotate + scale about its centre)
def gem_groups(o):
    me = o.data
    gi = list(o['parts']).index('gem')
    import collections
    parent = {}
    def find(a):
        while parent.setdefault(a, a) != a:
            a = parent[a]
        return a
    for p in me.polygons:
        if p.material_index == gi:
            vs = list(p.vertices)
            for v in vs[1:]:
                ra, rb = find(vs[0]), find(v)
                if ra != rb: parent[ra] = rb
    comps = collections.defaultdict(set)
    for v in parent: comps[find(v)].add(v)
    out = []
    for vs in comps.values():
        P = np.array([me.vertices[i].co[:] for i in vs])
        c = P.mean(0)
        out.append({'c': c, 'ang': math.atan2(c[1], c[0]), 'size': np.linalg.norm(P - c, axis=1).max()})
    return out
G = gem_groups(obj)
front = min(G, key=lambda g: abs(g['ang'] + math.pi / 2))
for k, g in enumerate(G):
    if g is front:
        continue
    da = front['ang'] - g['ang']
    sc_ = front['size'] / g['size']
    Rz = np.array([[math.cos(da), -math.sin(da), 0], [math.sin(da), math.cos(da), 0], [0, 0, 1]])
    T = np.eye(4); T[:3, :3] = sc_ * Rz; T[:3, 3] = front['c'] - sc_ * Rz @ g['c']
    views.append({'name': f'gem{k}', 'T': T, 'parts': ['setting', 'gem'], 'bonus': 0.5})
views += [{'name': f'foot{a}', 'T': BK.T_rot_z(a), 'parts': ['foot']} for a in (60, -60, 120, -120, 180)]
views += [{'name': f'stem{a}', 'T': BK.T_rot_z(a), 'parts': ['stem']} for a in (90, -90, 180, 45, -45, 135, -135)]
tex_path = os.path.join(OUT, f'T_{ID}.png')
def concept_palette(box, k=5):
    import cv2
    im = cv2.cvtColor(cv2.imread(CONCEPT_SRC), cv2.COLOR_BGR2RGB)
    y0, y1, x0, x1 = box
    pal, w = BK._kmeans_palette(im[y0:y1, x0:x1].reshape(-1, 3), k)
    return [tuple(map(float, c)) for c in pal]
part_colors = {'inside': concept_palette((75, 165, 560, 980))}
tex, st = BK.bake(obj, CONCEPT, cam, views=views, size=size, part_colors=part_colors, out_png=tex_path, debug_png=os.path.join(OUT, 'coverage.png'))
print('bake', json.dumps(st))

def poly_masks(o, size, part):
    import cv2
    me = o.data
    uv = me.uv_layers.active.data
    pi = list(o['parts']).index(part)
    for p in me.polygons:
        if p.material_index != pi:
            continue
        pts = np.array([[uv[l].uv[0] * size, (1 - uv[l].uv[1]) * size] for l in p.loop_indices])
        m = np.zeros((size, size), np.uint8)
        cv2.fillPoly(m, [np.round(pts * 16).astype(np.int32)], 1, lineType=cv2.LINE_8, shift=4)
        yield p, m

def shade_inside(o, tex, size):
    """Painted depth darkening of the hollow: rim level full brightness, floor ~55%."""
    t = tex.astype(np.float32)
    zr = max(v.co.z for v in o.data.vertices)
    for p, m in poly_masks(o, size, 'inside'):
        z = p.center.z
        f = 0.55 + 0.45 * min(1, max(0, (z - 0.09) / (zr - 0.09))) ** 0.8
        t[m.astype(bool)] *= f
    return np.clip(t, 0, 255).astype(np.uint8)

def flatten_gem_facets(o, tex, size, boost=1.12):
    """Rubies: one flat colour per facet (median of the projected texels) so the cut reads crisply like the
    concept's step-cut instead of posterized blotches; slightly richer red."""
    import cv2
    t = tex.copy()
    allg = np.concatenate([tex[m.astype(bool)] for _, m in poly_masks(o, size, 'gem')]).astype(np.float32)
    red = allg[allg[:, 0] > allg[:, 1] * 1.5]
    fallback = np.median(red, axis=0)
    gmean = fallback
    for p, m in poly_masks(o, size, 'gem'):
        sel = m.astype(bool)
        if sel.sum() == 0:
            continue
        inner = cv2.erode(m, np.ones((3, 3), np.uint8)).astype(bool)
        src = (tex[inner] if inner.sum() >= 3 else tex[sel]).reshape(-1, 3).astype(np.float32)
        src = src[src[:, 0] > src[:, 1] * 1.5]          # only ruby texels (setting silver bleeds in at the edges)
        c = (np.median(src, axis=0) if len(src) >= 2 else fallback)
        c = gmean + (c - gmean) * 1.6                   # keep the concept's facet-to-facet tones, a bit stronger
        # painted step-cut light, in each gem's own frame (same on every gem): bright upper right, dark lower left
        n = np.array(p.normal[:])
        cen = np.array(p.center[:])
        a = math.atan2(cen[1], cen[0])
        d = np.array([math.cos(a), math.sin(a), 0.0]); tng = np.array([-math.sin(a), math.cos(a), 0.0])
        L = 0.55 * tng + 0.65 * np.array([0, 0, 1.0]) + 0.55 * d; L /= np.linalg.norm(L)
        k = float(n @ L)
        c = c * boost * (0.84 + 0.5 * max(0.0, k) ** 1.5)
        if k > 0.70:
            c = c * 0.55 + np.array([235, 120, 135]) * 0.45
        t[sel] = np.clip(c, 0, 255).astype(np.uint8)
    return t
import cv2
tex = shade_inside(obj, flatten_gem_facets(obj, tex, size), size)
cv2.imwrite(tex_path, cv2.cvtColor(tex, cv2.COLOR_RGB2BGR))
R.apply_texture(obj, tex_path, ID)
stats = check_fit(obj)
stats['texture'] = f'{size}x{size}'
R.render_views(obj, os.path.join(OUT, 'preview.png'), concept_cam=cam, concept_size=(1536, 1024), concept_path=CONCEPT)
R.shelf_fit_render(obj, os.path.join(OUT, 'shelf.png'))
R.export(obj, OUT, ID, tex_path)
json.dump({'id': ID, 'stats': stats, 'bake': st, 'camera': cam}, open(os.path.join(OUT, 'stats.json'), 'w'), indent=1)
R.card(OUT, ID, stats)
print('done', stats)
