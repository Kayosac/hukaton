"""21 Inverted farm — collectible shelf item (four-view concept). Run: python3 build.py [--opt] [--align-only]

An ant-farm style glass case with a tiny human room inside.
Masses: dark worn wood tray (low, slightly flared box with a ring top around the plank floor), a glass box whose walls
taper inward toward the top (front / left / right panes = part 'glass', transparent 2nd material), thin light
grey-green corner posts ('glass_edge'), the clay back wall (= back of the case) with a plank door and a 4-pane window
going through it (proud on both faces), a thin dark-wood top frame with a flat murky glass plate.
Room: bed along the left (-X) side (legs, frame, mattress, striped blanket slab, pillow, headboard with posts),
table at the right (+X) with a mug and a book, a 3-legged stool, a rug in front of the door.
Painted: wood grain/wear, clay mottling, door planks + iron bands + ring handle, window panes, blanket stripes,
rug border, floor planks (projected from the 4 views; the glass itself is painted, never projected).
"""
import sys, os, json, math
sys.path.insert(0, '/home/claude/kit')
import numpy as np
import cv2
from scipy import ndimage
import bpy, bmesh
from geo import Builder, clear_scene, check_fit
import bake as BK
import bake4 as B4
import render as R
import render4 as R4
import cam as C


def _rasterize_z(P, part, cam, W, H):
    """Drop-in for bake.rasterize with a real per-pixel z-test (the kit's painter's sort by mean triangle depth
    draws the big back-wall triangles over the proud door/window boxes)."""
    u, v, z = C.project(P.reshape(-1, 3), cam, W, H)
    u, v, z = u.reshape(-1, 3), v.reshape(-1, 3), z.reshape(-1, 3)
    idb = np.full((H, W), -1, np.int32)
    zb = np.full((H, W), np.inf, np.float32)
    for t in range(len(P)):
        if (z[t] <= 0).any():
            continue
        x0 = max(int(np.floor(u[t].min())), 0); x1 = min(int(np.ceil(u[t].max())), W - 1)
        y0 = max(int(np.floor(v[t].min())), 0); y1 = min(int(np.ceil(v[t].max())), H - 1)
        if x1 < x0 or y1 < y0:
            continue
        gx_, gy_ = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        u0, v0, u1, v1, u2, v2 = u[t, 0], v[t, 0], u[t, 1], v[t, 1], u[t, 2], v[t, 2]
        den = (v1 - v2) * (u0 - u2) + (u2 - u1) * (v0 - v2)
        if abs(den) < 1e-9:
            continue
        a = ((v1 - v2) * (gx_ - u2) + (u2 - u1) * (gy_ - v2)) / den
        b_ = ((v2 - v0) * (gx_ - u2) + (u0 - u2) * (gy_ - v2)) / den
        c = 1 - a - b_
        ins = (a >= -1e-6) & (b_ >= -1e-6) & (c >= -1e-6)
        if not ins.any():
            continue
        zt = 1 / np.maximum(a / z[t, 0] + b_ / z[t, 1] + c / z[t, 2], 1e-9)
        sub_z = zb[y0:y1 + 1, x0:x1 + 1]
        m = ins & (zt < sub_z)
        sub_z[m] = zt[m]
        idb[y0:y1 + 1, x0:x1 + 1][m] = t
    return idb, zb


BK.rasterize = _rasterize_z

HERE = os.path.dirname(os.path.abspath(__file__))
PREFIX = '/home/claude/concepts/21-inverted-farm'
ID = 'CI21_InvertedFarm'
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)

# ---------------------------------------------------------------- dimensions (metres)
TW, TD = 0.250, 0.165          # tray top outer
TWB, TDB = 0.243, 0.158        # tray bottom (sides flare out a touch)
TZ = 0.044                     # tray top / glass bottom
GX0, GY0 = 0.1167, 0.0728      # glass half extents at the bottom
GX1, GY1 = 0.1112, 0.0668      # glass half extents at the top
GZ1 = 0.177                    # glass top = frame bottom
FW, FD, FZ1 = 0.231, 0.143, 0.195   # top frame
FLOOR = TZ + 0.0015            # floor plate top (planks)
WALL_T = 0.008                 # back wall thickness
POST = 0.0045                  # corner post size
LEAN_Y = (GY1 - GY0) / (GZ1 - TZ)   # back wall y change per metre of height (negative: leans in)
LEAN_X = (GX1 - GX0) / (GZ1 - TZ)


def gy(z):
    return GY0 + LEAN_Y * (z - TZ)


def gx(z):
    return GX0 + LEAN_X * (z - TZ)


def build():
    b = Builder(ID, seed=21)
    j = lambda a: b.j(a)
    # ---- tray: flared sides, top ring around the floor opening (no hidden top under the floor), no bottom
    bot = [(-TWB / 2, -TDB / 2, 0), (TWB / 2, -TDB / 2, 0), (TWB / 2, TDB / 2, 0), (-TWB / 2, TDB / 2, 0)]
    top = [(-TW / 2 + j(.0015), -TD / 2 + j(.0015), TZ + j(.001)), (TW / 2 + j(.0015), -TD / 2 + j(.0015), TZ + j(.001)),
           (TW / 2 + j(.0015), TD / 2 + j(.0015), TZ + j(.001)), (-TW / 2 + j(.0015), TD / 2 + j(.0015), TZ + j(.001))]
    inn = [(-GX0, -GY0, TZ), (GX0, -GY0, TZ), (GX0, GY0, TZ), (-GX0, GY0, TZ)]
    V = bot + top + inn
    F = [[0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7],              # sides
         [4, 5, 9, 8], [5, 6, 10, 9], [6, 7, 11, 10], [7, 4, 8, 11]]       # top ring
    b.add_bmesh_geometry('tray', V, F)
    # ---- floor plate (planks), a hair above the tray ring
    b.add_bmesh_geometry('floor', [(-GX0 + .001, -GY0 + .001, FLOOR), (GX0 - .001, -GY0 + .001, FLOOR),
                                   (GX0 - .001, GY0 - WALL_T, FLOOR), (-GX0 + .001, GY0 - WALL_T, FLOOR)], [[0, 1, 2, 3]])
    # ---- glass panes (single quads, outward): front, left, right
    fb = [(-GX0, -GY0, TZ), (GX0, -GY0, TZ)]
    ft = [(-GX1, -GY1, GZ1), (GX1, -GY1, GZ1)]
    b.add_bmesh_geometry('glass', [fb[0], fb[1], ft[1], ft[0]], [[0, 1, 2, 3]])
    b.add_bmesh_geometry('glass', [(-GX0, GY0, TZ), (-GX0, -GY0, TZ), (-GX1, -GY1, GZ1), (-GX1, GY1, GZ1)], [[0, 1, 2, 3]])
    b.add_bmesh_geometry('glass', [(GX0, -GY0, TZ), (GX0, GY0, TZ), (GX1, GY1, GZ1), (GX1, -GY1, GZ1)], [[0, 1, 2, 3]])
    # ---- corner posts (bright glass edges), leaning with the taper
    h = GZ1 - TZ
    for sx in (-1, 1):
        for sy in (-1, 1):
            cx, cy = sx * (GX0 - POST / 2 + 0.0008), sy * (GY0 - POST / 2 + 0.0008)
            b.box('glass_edge', (cx, cy, TZ + h / 2), (POST, POST, h),
                  shear=(sx * (GX1 - GX0), sy * (GY1 - GY0)))
    # ---- clay back wall = the back of the case (outer face at the glass line, leaning in)
    wx0, wx1 = GX0 - POST + 0.0008, GX1 - POST + 0.0008
    V = [(-wx0, GY0, TZ), (wx0, GY0, TZ), (wx0, GY0 - WALL_T, TZ), (-wx0, GY0 - WALL_T, TZ),
         (-wx1, GY1, GZ1), (wx1, GY1, GZ1), (wx1, GY1 - WALL_T, GZ1), (-wx1, GY1 - WALL_T, GZ1)]
    F = [[1, 0, 4, 5],        # outer (+Y)
         [3, 2, 6, 7],        # inner (-Y)
         [0, 3, 7, 4],        # -X edge
         [2, 1, 5, 6]]        # +X edge
    b.add_bmesh_geometry('backwall', V, F)
    # ---- door and window through the wall, proud on both faces, following the wall lean
    def through(part, x0, x1, z0, z1, proud):
        zc = (z0 + z1) / 2
        yc = gy(z0) - WALL_T / 2
        b.box(part, ((x0 + x1) / 2, yc, z0 + (z1 - z0) / 2), (x1 - x0, WALL_T + 2 * proud, z1 - z0),
              shear=(0, LEAN_Y * (z1 - z0)))
    through('door', -0.0265, 0.0305, FLOOR, FLOOR + 0.098, 0.0018)          # dark door frame
    through('door', -0.0215, 0.0255, FLOOR, FLOOR + 0.0925, 0.0034)         # plank door slab
    through('window', 0.0405, 0.0805, 0.1015, 0.1415, 0.0028)
    # ---- top frame + murky glass plate
    # frame = a ring (outer sides, top rim, inner hole walls) so light and the eye get in through the top glass
    RIM = 0.0085
    ox, oy, ix, iy = FW / 2, FD / 2, FW / 2 - RIM, FD / 2 - RIM
    zt, zg = FZ1, FZ1 - 0.0035
    V = [(-ox, -oy, GZ1), (ox, -oy, GZ1), (ox, oy, GZ1), (-ox, oy, GZ1)]
    V += [(x + j(.0008), y + j(.0008), zt + j(.0006)) for x, y, _ in V]
    V += [(-ix, -iy, zt), (ix, -iy, zt), (ix, iy, zt), (-ix, iy, zt)]
    V += [(-ix, -iy, zg), (ix, -iy, zg), (ix, iy, zg), (-ix, iy, zg)]
    F = [[0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7],             # outer sides
         [4, 5, 9, 8], [5, 6, 10, 9], [6, 7, 11, 10], [7, 4, 8, 11],         # top rim
         [8, 9, 13, 12], [9, 10, 14, 13], [10, 11, 15, 14], [11, 8, 12, 15]]  # hole walls (face inward)
    b.add_bmesh_geometry('frame', V, F)
    b.add_bmesh_geometry('topglass', V[12:16], [[0, 1, 2, 3]])
    # ---- rug
    b.box('rug', (-0.003, -0.036, FLOOR + 0.001), (0.066, 0.056, 0.002), rot_z=0.03)
    # ---- bed along the left side, headboard at the back
    z0 = FLOOR
    x0, x1, y0, y1 = -0.109, -0.041, -0.050, 0.052
    xc = (x0 + x1) / 2; bw = x1 - x0
    for lx in (x0 + 0.0047, x1 - 0.0047):                 # legs a hair inside the frame (no coplanar faces)
        b.box('bedwood', (lx, y0 + 0.0047, z0 + 0.007), (0.008, 0.008, 0.014))
    b.box('bedwood', (xc, (y0 + y1) / 2, z0 + 0.017), (bw, y1 - y0, 0.010), jitter=0.0005)        # frame
    b.box('mattress', (xc, 0.034, z0 + 0.030), (bw - 0.004, 0.036, 0.018))
    b.box('blanket', (xc, (y0 - 0.002 + 0.017) / 2, z0 + 0.0305), (bw + 0.004, 0.017 - y0 + 0.002, 0.023), jitter=0.0006)
    b.box('pillow', (xc + 0.001, 0.034, z0 + 0.0435), (bw - 0.016, 0.026, 0.010), jitter=0.0008)
    b.box('bedwood', (xc, 0.056, z0 + 0.0255), (bw, 0.008, 0.051))                              # headboard panel
    for px in (x0 + 0.0045, x1 - 0.0045):
        b.box('bedwood', (px, 0.056, z0 + 0.035), (0.010, 0.0095, 0.070))                         # posts (proud)
    # ---- table at the right with a mug and a book
    tx, ty, th = 0.075, 0.021, 0.052
    b.box('tablewood', (tx, ty, z0 + th - 0.0045), (0.061, 0.062, 0.009), jitter=0.0006)
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.box('tablewood', (tx + sx * 0.024, ty + sy * 0.024, z0 + (th - 0.008) / 2), (0.009, 0.009, th - 0.008))
    tz = z0 + th
    b.lathe('mug', [(0.0085, tz), (0.0088, tz + 0.016)], sides=8, rot=0.2, cap_bottom=False, offset=(0.061, 0.004, 0))
    a = math.radians(30)
    b.box('mug', (0.061 + 0.0105 * math.cos(a), 0.004 + 0.0105 * math.sin(a), tz + 0.008), (0.0045, 0.0032, 0.010), rot_z=a)
    b.box('book', (0.087, 0.027, tz + 0.0045), (0.031, 0.026, 0.009), rot_z=0.12)
    # ---- 3-legged stool
    sx_, sy_, sh = 0.044, -0.031, 0.029
    b.lathe('stool', [(0.0148, z0 + sh - 0.006), (0.0148, z0 + sh)], sides=8, rot=0.1, cap_bottom=False, offset=(sx_, sy_, 0))
    for k in range(3):                       # splayed legs: feet out, tops tucked under the seat
        a = math.radians(90 + 120 * k)
        lx, ly = sx_ + 0.0125 * math.cos(a), sy_ + 0.0125 * math.sin(a)
        b.box('stool', (lx, ly, z0 + (sh - 0.006) / 2), (0.0048, 0.0048, sh - 0.006),
              shear=(-0.0065 * math.cos(a), -0.0065 * math.sin(a)), rot_z=a)
    # ---- remove faces no camera ever sees: downward faces (all cameras look from above) and faces under the frame
    bm = b.bm
    dead = []
    parts = b.parts
    for f in bm.faces:
        n = f.normal if f.normal.length > 0 else None
        f.normal_update()
        c = f.calc_center_median()
        if f.normal.z < -0.7:
            dead.append(f)
        elif f.normal.z > 0.7 and abs(c.z - GZ1) < 0.0015 and parts[f.material_index] != 'frame':
            dead.append(f)          # post tops under the frame
    bmesh.ops.delete(bm, geom=dead, context='FACES')
    return b


def unwrap(o, shrink=None, margin=0.006):
    """Kit smart-project unwrap; then the islands of low-detail parts are shrunk and everything is repacked."""
    BK.unwrap(o, margin=margin, angle=50)
    if shrink:
        me = o.data
        parts = list(o['parts'])
        uv = me.uv_layers.active.data
        for pname, s in shrink.items():
            if pname not in parts:
                continue
            pi = parts.index(pname)
            for p in me.polygons:
                if p.material_index != pi:
                    continue
                ls = list(p.loop_indices)
                c = np.mean([uv[l].uv[:] for l in ls], axis=0)
                for l in ls:
                    uv[l].uv = tuple(c + (np.array(uv[l].uv[:]) - c) * s)
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.select_all(action='SELECT')
        bpy.ops.uv.pack_islands(rotate=True, margin=margin)
        bpy.ops.object.mode_set(mode='OBJECT')


clear_scene()
b = build()
obj = b.finish()
print('fit', check_fit(obj))
unwrap(obj, shrink={'glass': 0.3, 'topglass': 0.6, 'tray': 0.88, 'frame': 0.88, 'glass_edge': 0.5,
                    # furniture is small and seen close through the glass: more texels
                    'bedwood': 1.5, 'mattress': 1.5, 'blanket': 1.7, 'pillow': 1.5, 'tablewood': 1.5, 'mug': 1.6,
                    'book': 1.6, 'stool': 1.6, 'rug': 1.4, 'door': 1.2, 'window': 1.3})

cam_path = os.path.join(HERE, 'cameras.json')
if os.path.exists(cam_path):
    cams = json.load(open(cam_path))
else:
    cams = B4.default_cams({'az': 0, 'el': 12, 'dist': 1.85, 'fov': 9.5, 'target': [0, 0, 0.1], 'shift': [0, 0], 'roll': 0})
views = B4.four_views(PREFIX, cams)
if '--opt' in sys.argv:
    keys = ('dist', 'shift', 'el') if '--el' in sys.argv else ('dist', 'shift')
    print('opt', B4.optimize_views(obj, views, keys=keys))
    json.dump({v['name']: v['cam'] for v in views}, open(cam_path, 'w'), indent=1)
iou = B4.align_all(obj, views, os.path.join(OUT, 'align.png'))
print('IoU', iou)
if '--align-only' in sys.argv:
    sys.exit()

# ---------------------------------------------------------------- texture
GLASS_ALPHA = 0.13
size = 512      # kit density gives 1024 for the full surface; 512 keeps furniture readable (hidden faces removed)
tex_path = os.path.join(OUT, f'T_{ID}.png')
PARTS = list(obj['parts'])


def concept_palette(view, box, k=4):
    im = cv2.cvtColor(cv2.imread(f'{PREFIX}_{view}.png'), cv2.COLOR_BGR2RGB)
    y0, y1, x0, x1 = box
    pal, w = BK._kmeans_palette(im[y0:y1, x0:x1].reshape(-1, 3).astype(np.float32), k)
    return [tuple(map(float, c)) for c in pal]


# each view paints only what it really depicts (the side views draw their own room composition: the far clay wall
# there must not land on furniture of the other side)
CASE = ['tray', 'frame', 'backwall']
VIEW_PARTS = {
    # bed wood: the front concept draws the bed turned (headboard on the left), so its wood comes from the left view
    # and the part palette; from the front the headboard panel would get the clay wall
    'front': CASE + ['floor', 'door', 'window', 'rug', 'mattress', 'blanket', 'pillow', 'tablewood', 'mug',
                     'book', 'stool'],
    'back': CASE + ['door', 'window'],
    'left': CASE + ['floor', 'bedwood', 'mattress', 'blanket', 'pillow'],
    'right': CASE + ['floor', 'tablewood', 'mug', 'book', 'stool'],
}
for v in views:
    v['parts'] = VIEW_PARTS[v['name']]
    v['bonus'] = {'front': 0.08, 'back': 0.04}.get(v['name'], 0.0)

# bake on a copy WITHOUT the glass panes/posts/top plate (they must neither occlude nor receive the room picture)
bake_obj = obj.copy(); bake_obj.data = obj.data.copy()
bpy.context.scene.collection.objects.link(bake_obj)
bm = bmesh.new(); bm.from_mesh(bake_obj.data)
skip = {PARTS.index(p) for p in ('glass', 'glass_edge', 'topglass')}
bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index in skip], context='FACES')
bm.to_mesh(bake_obj.data); bm.free()

part_colors = {
    'mattress': [(196, 170, 128), (182, 152, 112), (205, 182, 140)],
    'tablewood': [(117, 80, 57), (104, 70, 50), (128, 90, 62)],
    'bedwood': [(100, 66, 48), (88, 58, 42), (112, 76, 54)],
    'stool': [(136, 91, 62), (120, 80, 55)],
}
tex, st, raw = B4.bake_multi(bake_obj, views, size=size, part_colors=part_colors, erode_px=2, min_facing=0.12,
                             quantize=0, return_raw=True, debug_png=os.path.join(OUT, 'coverage.png'))
bpy.data.objects.remove(bake_obj)
print('bake', json.dumps(st))


INTERIOR = ['floor', 'door', 'window', 'rug', 'bedwood', 'mattress', 'blanket', 'pillow', 'tablewood', 'mug', 'book',
            'stool']


def paint(o, tex):
    """Painted parts (never projected): glass tint + streaks, bright corner posts, murky top glass."""
    ys, xs, X, Nt, pt = B4.texel_points(o, size)
    t = tex.astype(np.float32)
    A = np.full((size, size), 255.0, np.float32)     # texture alpha: used by the glass material only
    rng = np.random.default_rng(21)
    blk = np.kron(rng.random((size // 3 + 2, size // 3 + 2)), np.ones((3, 3)))[:size, :size]
    # glass panes: faint grey-green tint; a couple of broken vertical highlight bands (concept: light blocks along the
    # glass), no room picture
    g = pt == PARTS.index('glass')
    along = np.where(np.abs(Nt[g, 1]) > 0.7, X[g, 0], X[g, 1])
    half0 = np.where(np.abs(Nt[g, 1]) > 0.7, gx(X[g, 2]), gy(X[g, 2]))
    rel = (along + half0) / (2 * half0)                   # 0..1 across the pane (outside view: left..right varies)
    zr = (X[g, 2] - TZ) / (GZ1 - TZ)
    streak = ((np.abs(rel - 0.16) < 0.022) & (zr > 0.25)) | ((np.abs(rel - 0.84) < 0.012) & (zr < 0.7))
    streak &= (blk[ys[g], xs[g]] > 0.2) & (np.abs(Nt[g, 0]) > 0.7)     # side panes only
    base = np.array([118, 136, 120], np.float32) + (blk[ys[g], xs[g]][:, None] - 0.5) * 8
    hi = np.array([206, 220, 198], np.float32)
    col = np.where(streak[:, None], hi, base)
    # light bands along the vertical pane edges (the concept's bright glass edges)
    half = np.where(np.abs(Nt[g, 1]) > 0.7, gx(X[g, 2]), gy(X[g, 2]))
    edge = (half - np.abs(along)) < 0.0075
    col = np.where((edge & (blk[ys[g], xs[g]] > 0.25))[:, None], np.array([190, 204, 178], np.float32), col)
    t[ys[g], xs[g]] = col
    A[ys[g], xs[g]] = np.where(streak, 0.26, np.where(edge, 0.34, GLASS_ALPHA)) * 255
    # the room was drawn behind tinted glass and is seen through the glass again: a bit more colour and light
    inner = np.isin(pt, [PARTS.index(p) for p in INTERIOR]) | ((pt == PARTS.index('backwall')) & (Nt[:, 1] < -0.5))
    hsv = cv2.cvtColor(np.clip(t[ys[inner], xs[inner]], 0, 255).astype(np.uint8)[None], cv2.COLOR_RGB2HSV).astype(np.float32)
    hsv[..., 1] *= 1.08; hsv[..., 2] *= 1.18
    t[ys[inner], xs[inner]] = cv2.cvtColor(np.clip(hsv, 0, 255).astype(np.uint8), cv2.COLOR_HSV2RGB)[0].astype(np.float32)
    # the texel-footprint blur flattens the small painted stripes / bands: restore some contrast per part
    for pn, kc in (('blanket', 1.45), ('rug', 1.3), ('door', 1.25), ('window', 1.3), ('pillow', 1.15), ('book', 1.2),
                   ('mug', 1.2), ('tablewood', 1.15), ('stool', 1.15), ('bedwood', 1.15), ('floor', 1.2)):
        sp = pt == PARTS.index(pn)
        c = t[ys[sp], xs[sp]]
        t[ys[sp], xs[sp]] = (c - c.mean(0)) * kc + c.mean(0)
    fl = pt == PARTS.index('floor')                    # ochre planks (the side views show them lighter)
    t[ys[fl], xs[fl]] *= np.array([1.16, 1.12, 1.0], np.float32)
    mg = pt == PARTS.index('mug')                      # cream clay mug
    t[ys[mg], xs[mg]] = t[ys[mg], xs[mg]] * 0.5 + np.array([200, 176, 132], np.float32) * 0.5
    # corner posts: light grey-green, a little darker at the bottom
    e = pt == PARTS.index('glass_edge')
    k = np.clip((X[e, 2] - TZ) / (GZ1 - TZ), 0, 1)[:, None]
    ecol = np.array([128, 150, 128], np.float32) * (1 - k) + np.array([170, 198, 172], np.float32) * k
    ecol += (blk[ys[e], xs[e]][:, None] - 0.5) * 16
    t[ys[e], xs[e]] = ecol
    # murky top glass: blocky olive / grey-green patches from the concept's top
    tg = pt == PARTS.index('topglass')
    pal = np.array(concept_palette('front', (118, 146, 160, 500), 4) + concept_palette('back', (120, 145, 120, 480), 3), np.float32)
    big = cv2.resize(rng.random((size // 6 + 2, size // 6 + 2)).astype(np.float32), None, fx=6, fy=6,
                     interpolation=cv2.INTER_NEAREST)[:size, :size]
    lum = pal @ np.array([0.3, 0.59, 0.11]); pal = pal[np.argsort(lum)]
    idx = np.clip((big[ys[tg], xs[tg]] * 0.7 + blk[ys[tg], xs[tg]] * 0.3) * len(pal), 0, len(pal) - 1).astype(int)
    t[ys[tg], xs[tg]] = pal[idx]
    A[ys[tg], xs[tg]] = (0.58 + 0.22 * big[ys[tg], xs[tg]]) * 255     # murky, half see-through
    # fill gutters / unpainted edge texels from the nearest painted texel
    valid = np.zeros((size, size), bool)
    valid[ys, xs] = True
    _, inds = ndimage.distance_transform_edt(~valid, return_indices=True)
    t = t[inds[0], inds[1]]
    A = A[inds[0], inds[1]]
    pm = np.zeros((size, size), bool)
    pm[ys, xs] = np.isin(pt, [PARTS.index(p) for p in ('glass', 'glass_edge', 'topglass')])
    pm = pm[inds[0], inds[1]]
    return np.clip(t, 0, 255).astype(np.uint8), np.clip(A, 0, 255).astype(np.uint8), pm


tex, alpha, painted = paint(obj, raw)
texq = BK._quantize(tex, 40)
texq[painted] = tex[painted]          # the painted glass / posts keep their green-greys (palette has few)
tex = texq
cv2.imwrite(tex_path, np.dstack([cv2.cvtColor(tex, cv2.COLOR_RGB2BGR), alpha]))
if '--bake-only' in sys.argv:
    sys.exit()
_, GLASS_MAT = R4.apply_texture(obj, tex_path, ID, glass_parts=['glass', 'topglass'], glass_alpha=GLASS_ALPHA)
bpy.data.images[os.path.basename(tex_path)].alpha_mode = 'CHANNEL_PACKED'   # colour independent of the alpha channel
GT = [n for n in GLASS_MAT.node_tree.nodes if n.type == 'TEX_IMAGE'][0]
GLASS_MAT.node_tree.links.new(GT.outputs['Alpha'], GLASS_MAT.node_tree.nodes['Principled BSDF'].inputs['Alpha'])


def glass_shadow_trick(on):
    """Preview renders only: the glass is seen by the camera but not by light / bounce rays (like Unity, where
    transparent glass neither shadows nor occludes the ambient; Cycles would otherwise darken the whole room). The alpha goes straight from the texture to the BSDF again before the FBX/.blend export."""
    nt = GLASS_MAT.node_tree
    bsdf = nt.nodes.get('Principled BSDF')
    for n in [n for n in nt.nodes if n.name.startswith('shadowfix')]:
        nt.nodes.remove(n)
    if not on:
        nt.links.new(GT.outputs['Alpha'], bsdf.inputs['Alpha'])
        return
    lp = nt.nodes.new('ShaderNodeLightPath'); lp.name = 'shadowfix_lp'
    m = nt.nodes.new('ShaderNodeMath'); m.name = 'shadowfix_m'; m.operation = 'MULTIPLY'
    nt.links.new(GT.outputs['Alpha'], m.inputs[0])
    nt.links.new(lp.outputs['Is Camera Ray'], m.inputs[1])
    nt.links.new(m.outputs[0], bsdf.inputs['Alpha'])


glass_shadow_trick(True)
QS = int(os.environ.get('QS', 400))
if os.environ.get('NOGLASS'):
    GLASS_MAT.node_tree.nodes['shadowfix_m'].inputs[1].default_value = 0.0
    GLASS_MAT.node_tree.links.remove(GLASS_MAT.node_tree.nodes['shadowfix_m'].inputs[1].links[0])
if '--quick' in sys.argv:
    R._world((0.36, 0.34, 0.32), 0.9); R._lights()
    tiles = []
    for c in (views[0]['cam'], {'az': -40, 'el': 22, 'dist': 1.05, 'fov': 18, 'target': [0, 0, 0.1], 'shift': [0, 0]},
              views[2]['cam']):
        tiles.append(R4._render(c, QS, QS, samples=12))
    cv2.imwrite('/tmp/quick21.png', np.concatenate(tiles, 1))
    sys.exit()
stats = check_fit(obj)
stats['texture'] = f'{size}x{size}'
stats['iou'] = iou
R4.preview4(obj, views, os.path.join(OUT, 'preview.png'))
if '--preview-only' in sys.argv:
    sys.exit()
R.shelf_fit_render(obj, os.path.join(OUT, 'shelf.png'))
glass_shadow_trick(False)
R.export(obj, OUT, ID, tex_path)
json.dump({'id': ID, 'stats': stats, 'bake': st, 'cameras': {v['name']: v['cam'] for v in views}},
          open(os.path.join(OUT, 'stats.json'), 'w'), indent=1)
R4.card4(OUT, ID, stats)
print('done', stats)
