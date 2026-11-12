"""01 Fly agaric with eyes — collectible shelf item.
Run: python3 build.py [--opt] [--align-only]

Model: crooked 9-sided dome cap (tilted back + leaning right, offset from the stalk), real bulging low-poly eyeballs set
into the cap (front ones placed by ray-casting the concept's eye centres onto the cap, back ones are rotated copies
that share the front eyes' UV islands so they carry the same painted pupils/veins), a gill cone under the cap,
a flaring stalk with a ragged collar and a wide base. Texture = concept projection (+ rotated views for the hidden
side of cap/gills/stalk).
"""
import sys, os, json, math
sys.path.insert(0, '/home/claude/kit')
import numpy as np
import cv2
import bpy, bmesh
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
from geo import Builder, clear_scene, check_fit
import bake_local as BK   # local copy of kit/bake.py with small fixes (see its docstring)
import render as R
import cam as C

HERE = os.path.dirname(os.path.abspath(__file__))
CONCEPT = '/home/claude/concepts/collectible-concepts/01-fly-agaric-eyes-v5.png'
ID = 'CI01_FlyAgaricEyes'
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)
CW, CH = 1377, 1142

cam_path = os.path.join(HERE, 'camera.json')
CAM0 = {'az': 0, 'el': 0, 'dist': 0.48, 'fov': 30, 'target': [0, 0, 0.09], 'shift': [0, 0], 'roll': 0}
cam = json.load(open(cam_path)) if os.path.exists(cam_path) else CAM0

# ------------------------------------------------------------------ shape parameters (metres)
CAP_POS = Vector((0.007, 0.0, 0.083))      # rim centre of the cap (stalk axis is x=y=0)
CAP_TILT_X = math.radians(-10)              # negative: front rim up, back rim down (shows the gills)
CAP_TILT_Y = math.radians(2)               # lean to the right
CAP_SIDES = 9
# cap lathe profile (r, z) in cap space: gill cone (inner->rim) then the outer dome up to the top
CAP_PROFILE = [(0.012, 0.034), (0.052, 0.020), (0.095, 0.001),            # gills (2 bands)
               (0.108, -0.004), (0.113, 0.010), (0.109, 0.029),           # thick rim
               (0.097, 0.052), (0.080, 0.076), (0.058, 0.097), (0.028, 0.109), (0.0, 0.112)]
N_GILL_BANDS = 2

# stalk lathe (r, z), bottom -> top, ends inside the cap
STALK_PROFILE = [(0.041, 0.000), (0.050, 0.007), (0.052, 0.017), (0.046, 0.028), (0.037, 0.041),
                 (0.034, 0.052), (0.030, 0.065), (0.024, 0.082), (0.023, 0.105), (0.023, 0.135), (0.0, 0.142)]
STALK_SIDES = 9
# ragged collar: hangs from the stalk at ~z .055 down to a zig-zag edge at ~z .037
SKIRT_PROFILE = [(0.030, 0.050), (0.042, 0.042), (0.045, 0.039), (0.034, 0.066)]
SKIRT_SIDES = 13

# front eyes from the concept: (u, v) visible disc centre px, radius px, protrusion (0 = centre on the surface),
# pixel nudge for the ray (side eyes show only their outer half), roll deg
FRONT_EYES = [
    dict(n='E1', uv=(836, 128), r=118, pro=0.03, nudge=(0, 20)),    # top right
    dict(n='E2', uv=(487, 158), r=102, pro=0.0, nudge=(0, 18)),    # top left
    dict(n='E3', uv=(1078, 251), r=84, pro=0.12, nudge=(-12, 4)),   # right upper side
    dict(n='E4', uv=(298, 277), r=64, pro=0.05, nudge=(12, 4)),     # left upper small
    dict(n='E5', uv=(845, 415), r=148, pro=0.15, nudge=(0, 0)),     # front right big
    dict(n='E6', uv=(457, 412), r=140, pro=0.15, nudge=(0, 0)),     # front left big
    dict(n='E7', uv=(189, 470), r=92, pro=-0.12, nudge=(28, 0)),     # far left
    dict(n='E8', uv=(1165, 492), r=106, pro=0.05, nudge=(-26, 0)),  # far right
]
# back eyes: copy of a front eye rotated about the cap axis to azimuth `az` (deg in cap space, 0 = front -Y,
# +90 = +X side), scaled by `s`, rolled about its own axis
BACK_EYES = [
    # lower row (front ones sit at az -52..48, lat ~-16); on the back kept higher so they never hang below the rim
    dict(src='E5', az=88, lat=-4, s=0.72, roll=40, pro=-0.3),
    dict(src='E6', az=124, lat=-3, s=0.90, roll=-25),
    dict(src='E8', az=163, lat=-2, s=1.00, roll=180),
    dict(src='E5', az=-158, lat=-3, s=0.85, roll=150),
    dict(src='E6', az=-122, lat=-2, s=0.80, roll=80),
    dict(src='E7', az=-88, lat=-4, s=0.80, roll=0, pro=-0.3),
    # upper row (front: az -40..39, lat 0..13)
    dict(src='E1', az=78, lat=20, s=0.70, roll=60, pro=-0.3),
    dict(src='E2', az=108, lat=17, s=0.90, roll=-70),
    dict(src='E1', az=143, lat=22, s=0.95, roll=200),
    dict(src='E2', az=-178, lat=20, s=0.85, roll=100),
    dict(src='E1', az=-140, lat=22, s=0.90, roll=-40),
    dict(src='E2', az=-104, lat=18, s=0.80, roll=150),
    dict(src='E4', az=-76, lat=20, s=0.8, roll=0, pro=-0.3),
    # crown
    dict(src='E6', az=175, lat=50, s=0.85, roll=30),
    dict(src='E5', az=-35, lat=55, s=0.75, roll=-60),
    dict(src='E2', az=75, lat=48, s=0.80, roll=120),
]
VERBOSE = '--v' in sys.argv
S = 0.92            # global scale (largest dimension ~0.22 m)
EYE_UV_BOOST = 1.3  # eyes carry the painted pupils/veins: a bit more texel density


def cap_matrix():
    return Matrix.Translation(CAP_POS) @ Matrix.Rotation(CAP_TILT_Y, 4, 'Y') @ Matrix.Rotation(CAP_TILT_X, 4, 'X')


def eye_template(seed, sides=8, rings=4, squash=0.88):
    """Unit eyeball, pole (+Z) = outward. Returns verts, faces (deterministic per seed)."""
    rng = np.random.default_rng(seed)
    rot = rng.uniform(0, 2 * math.pi)
    verts = [(0, 0, -1.0)]
    for i in range(1, rings):
        th = math.pi * i / rings
        s, c = math.sin(th), -math.cos(th)
        for k in range(sides):
            a = rot + 2 * math.pi * k / sides
            j = 1 + rng.uniform(-0.06, 0.06)
            verts.append((math.cos(a) * s * j, math.sin(a) * s * j, c * (squash if c > 0 else 1) * j))
    verts.append((0, 0, squash))
    faces = []
    ring = lambda i: [1 + (i - 1) * sides + k for k in range(sides)]
    r1 = ring(1)
    for k in range(sides):
        faces.append([0, r1[(k + 1) % sides], r1[k]])
    for i in range(1, rings - 1):
        a, b_ = ring(i), ring(i + 1)
        for k in range(sides):
            k2 = (k + 1) % sides
            faces.append([a[k], a[k2], b_[k2], b_[k]])
    top = len(verts) - 1
    rl = ring(rings - 1)
    for k in range(sides):
        faces.append([rl[k], rl[(k + 1) % sides], top])
    return verts, faces


def frame_from_normal(n, roll_deg):
    z = Vector(n).normalized()
    x = Vector((0, 0, 1)).cross(z)
    if x.length < 1e-6:
        x = Vector((1, 0, 0))
    x.normalize()
    y = z.cross(x)
    M = Matrix((x, y, z)).transposed()   # columns x, y, z
    return M @ Matrix.Rotation(math.radians(roll_deg), 3, 'Z')


def ray_from_pixel(c, u, v):
    pos, right, upc, f = C.basis(c)
    big = max(CW, CH)
    fpx = (big / 2) / math.tan(math.radians(c['fov']) / 2)
    sx, sy = c.get('shift', [0, 0])
    xc = (u - CW / 2 - sx * big) / fpx
    yc = (CH / 2 - v - sy * big) / fpx
    d = f + right * xc + upc * yc
    return Vector(pos), Vector(d / np.linalg.norm(d)), fpx


def build(c, shift):
    """Build the whole mesh; `shift` (x, y) is subtracted at the end so the bbox is centred."""
    b = Builder(ID, seed=11)
    sh = Vector((shift[0], shift[1], 0))
    # camera expressed in the unshifted build frame
    cb = json.loads(json.dumps(c))
    cb['target'] = [c['target'][0] / S + shift[0], c['target'][1] / S + shift[1], c['target'][2] / S]
    cb['dist'] = c['dist'] / S

    # ---- cap (+ gills as the first bands of the same lathe)
    rings = b.lathe('cap', CAP_PROFILE, sides=CAP_SIDES, jitter=0.0016, rot=0.21, cap_bottom=False, cap_top=False,
                    flat=True)
    gi = b.part('gills', flat=True)
    # crooked cap: a few verts pushed for asymmetry (rim dips a bit at one side)
    for k, v in enumerate(rings[4]):
        v.co.z -= 0.004 * max(0.0, math.cos(math.atan2(v.co.y, v.co.x) - 0.6))
    for r in rings:                       # the right half of the cap is a bit fuller (concept asymmetry)
        for v in r:
            if v.co.x > 0:
                v.co.x *= 1.04
    gill_verts = set(v for r in rings[:N_GILL_BANDS + 1] for v in r)
    for f in b.bm.faces:
        if all(v in gill_verts for v in f.verts):
            f.material_index = gi
    b.cap(rings[0], gi, flip=True, center=(0, 0, CAP_PROFILE[0][1] + 0.004))   # hidden inside the stalk
    Mc = cap_matrix()
    cap_verts = [v for r in rings for v in r] + [v for v in b.bm.verts if v not in set(x for r in rings for x in r)]
    for v in cap_verts:
        v.co = Mc @ v.co
    Mci = Mc.inverted()
    cap_faces = list(b.bm.faces)
    b.bm.verts.index_update()
    bvh = BVHTree.FromBMesh(b.bm)

    # ---- eyes
    ei = b.part('eyes', flat=True)
    uvlay = b.bm.loops.layers.uv.new('UVMap')
    lay_id = b.bm.faces.layers.int.new('eye_id')
    lay_src = b.bm.faces.layers.int.new('eye_src')
    eyes = {}
    cap_center = Mc @ Vector((0, 0, 0.03))
    for idx, e in enumerate(FRONT_EYES):
        u, v = e['uv'][0] + e['nudge'][0], e['uv'][1] + e['nudge'][1]
        o, d, fpx = ray_from_pixel(cb, u, v)
        hit, nrm, fi, dist = bvh.ray_cast(o, d)
        if hit is None:  # walk toward the cap centre in the image until we hit
            cu, cv_ = C.project(np.array([cap_center]), cb, CW, CH)[:2]
            for t in np.linspace(0, 1, 40):
                o, d, fpx = ray_from_pixel(cb, u + (cu[0] - u) * t, v + (cv_[0] - v) * t)
                hit, nrm, fi, dist = bvh.ray_cast(o, d)
                if hit is not None:
                    break
        rad = e['r'] * 1.06 * dist / fpx
        radial = (hit - cap_center).normalized()
        n = (nrm.normalized() * 0.5 + radial * 0.5).normalized()
        centre = hit + n * rad * e['pro']
        view = (Vector(C.basis(cb)[0]) - centre).normalized()
        # the eyeball's pole (centre of its UV disc) looks at the concept camera: the painted side is the pole side
        eyes[e['n']] = dict(seed=idx + 1, centre=centre, n=view, rad=rad, roll=e.get('roll', 0), idx=idx + 1,
                            sides=8 if rad > 0.016 else 7)
    # back eyes (built FIRST in face order, so the bake lets the front eyes own the shared texels)
    cc_local = Vector((0, 0, 0.03))
    for k, e in eyes.items():
        dl = (Mci @ e['centre'] - cc_local).normalized()
        e['az'] = math.degrees(math.atan2(dl.x, -dl.y)); e['lat'] = math.degrees(math.asin(max(-1, min(1, dl.z))))
        if VERBOSE:
            print(f"{k}: az {e['az']:.0f} lat {e['lat']:.0f} rad {e['rad']*1000:.1f}mm")
    back = []
    for j, e in enumerate(BACK_EYES):
        s = eyes[e['src']]
        az, lat = math.radians(e['az']), math.radians(e['lat'])
        dt = Vector((math.sin(az) * math.cos(lat), -math.cos(az) * math.cos(lat), math.sin(lat)))
        p_out = Mc @ (cc_local + dt * 0.3)
        n_w = (Mc.to_3x3() @ dt).normalized()
        hit, nrm, fi, dist = bvh.ray_cast(p_out, -n_w)
        n_eye = (nrm.normalized() * 0.5 + n_w * 0.5).normalized()
        rad = s['rad'] * e['s']
        centre = hit + n_eye * rad * e.get('pro', -0.12)
        # eye frame = source frame carried by the rotation, then rolled about the new axis
        # the painted pole side of the source eye faces outward
        Mb = frame_from_normal(n_eye, e['roll'])
        back.append(dict(seed=s['seed'], sides=s['sides'], src_rad=s['rad'], centre=centre, n=n_eye, frame=Mb, rad=rad, idx=100 + j, src=s['idx']))
    order = back + [dict(v, src=0) for v in eyes.values()]
    slot = {}
    for e in order:
        # one equal-area azimuthal disc per eye (pole = pupil side), in metres, parked far outside 0-1;
        # back eyes reuse their source eye's disc exactly
        key = e['src'] or e['idx']
        if key not in slot:
            slot[key] = (10 + 0.12 * len(slot), 0.0, e.get('src_rad', e['rad']))
        ox, oy, R_uv = slot[key]
        tv, tf = eye_template(e['seed'], sides=e['sides'])
        Mr = e['frame'] if 'frame' in e else frame_from_normal(e['n'], e['roll'])
        vs = [b.bm.verts.new(e['centre'] + Mr @ (Vector(p) * e['rad'])) for p in tv]
        for fl in tf:
            f = b._face([vs[i] for i in fl], ei)
            f[lay_id] = e['idx']; f[lay_src] = e['src']
            dirs = [Vector(tv[i]).normalized() for i in fl]
            mid_phi = math.atan2(sum(d.y for d in dirs if d.z > -0.999) or 1e-9, sum(d.x for d in dirs if d.z > -0.999))
            for lp, d in zip(f.loops, dirs):
                th = math.acos(max(-1, min(1, d.z / 1.0)))
                ph = mid_phi if d.z < -0.999 else math.atan2(d.y, d.x)
                # area fraction inside theta: the visible front hemisphere gets 80 % of the disc
                g = 0.8 * (1 - math.cos(th)) if th <= math.pi / 2 else 0.8 - 0.2 * math.cos(th)
                rr = 2 * R_uv * math.sqrt(max(g, 0.0))
                lp[uvlay].uv = (ox + rr * math.cos(ph), oy + rr * math.sin(ph))

    # ---- stalk, collar
    b.lathe('stalk', STALK_PROFILE, sides=STALK_SIDES, jitter=0.0012, rot=0.4, cap_bottom=True, cap_top=False,
            lean=(0.03, 0.0))
    sk = b.lathe('skirt', SKIRT_PROFILE, sides=SKIRT_SIDES, jitter=0.0008, rot=0.1, cap_bottom=False, cap_top=False)
    rng = np.random.default_rng(5)
    for k in range(SKIRT_SIDES):
        a0 = math.atan2(sk[2][k].co.y, sk[2][k].co.x)
        da = math.atan2(math.sin(a0 + math.radians(62)), math.cos(a0 + math.radians(62)))
        # torn edge: small teeth + one long tongue hanging toward the front (as in the concept)
        drop = (0.0018 if k % 2 == 0 else -0.0018) + rng.uniform(-0.001, 0.001) + 0.0055 * math.exp(-(da / 0.4) ** 2) - 0.002
        out = rng.uniform(-0.002, 0.003)
        for ri, w in ((1, 0.8), (2, 1.0)):
            vv = sk[ri][k]
            a = math.atan2(vv.co.y, vv.co.x)
            vv.co.z -= drop * w
            vv.co.x += math.cos(a) * out; vv.co.y += math.sin(a) * out
    for v in sk[0] + sk[1] + sk[2] + sk[3]:
        v.co.x += 0.03 * v.co.z - 0.004    # follow the stalk lean
    for v in b.bm.verts:
        v.co = (v.co - sh) * S
    return b


def part_overlay(obj, c, path):
    rgb, cm = BK.load_concept(CONCEPT)
    P, UV, part, N = BK.mesh_arrays(obj)
    idb, _ = BK.rasterize(P, part, c, CW, CH)
    pid = np.where(idb >= 0, part[np.maximum(idb, 0)], -1)
    vis = (rgb * 0.8).astype(np.uint8).copy()
    cols = [(255, 60, 60), (60, 255, 60), (60, 160, 255), (255, 255, 60), (255, 60, 255), (60, 255, 255)]
    for k in range(int(part.max()) + 1):
        m = (pid == k).astype(np.uint8)
        cs, _ = cv2.findContours(m, cv2.RETR_LIST, cv2.CHAIN_APPROX_NONE)
        cv2.drawContours(vis, cs, -1, cols[k % len(cols)], 2)
    cs, _ = cv2.findContours(cm.astype(np.uint8), cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    cv2.drawContours(vis, cs, -1, (255, 255, 255), 1)
    for e in FRONT_EYES:
        cv2.circle(vis, e['uv'], int(e['r']), (255, 255, 255), 1)
    cv2.imwrite(path, cv2.cvtColor(vis, cv2.COLOR_RGB2BGR))


def shade_views(obj, path, extra=()):
    """Fast flat-shaded part-colour views (no Cycles) for checking the shape from all sides."""
    P, UV, part, N = BK.mesh_arrays(obj)
    cols = np.array([(200, 60, 50), (230, 225, 210), (190, 160, 120), (215, 200, 170), (170, 150, 120)], float)
    L = np.array([0.4, -0.7, 0.6]); L /= np.linalg.norm(L)
    tiles = []
    for az, el in ((0, 3), (90, 10), (180, 10), (-120, 25), (40, 60), (0, 89)) + tuple(extra):
        cc = {'az': az, 'el': el, 'dist': 0.9, 'fov': 18, 'target': [0, 0, 0.1], 'shift': [0, 0], 'roll': 0}
        idb, _ = BK.rasterize(P, part, cc, 360, 360)
        img = np.full((360, 360, 3), 70, np.uint8)
        m = idb >= 0
        t = idb[m]
        # light rotates with the view so every tile is lit from the camera's upper left
        pos, right, upc, f = C.basis(cc)
        Lv = -f * 0.7 + upc * 0.5 - right * 0.4; Lv /= np.linalg.norm(Lv)
        sh = 0.35 + 0.65 * np.clip(N[t] @ Lv, 0, 1)
        img[m] = np.clip(cols[part[t] % len(cols)] * sh[:, None], 0, 255).astype(np.uint8)
        cv2.putText(img, f'az{az} el{el}', (5, 20), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
        tiles.append(img)
    cv2.imwrite(path, cv2.cvtColor(np.concatenate([np.concatenate(tiles[:3], 1), np.concatenate(tiles[3:6], 1)], 0), cv2.COLOR_RGB2BGR))


def make(c):
    clear_scene()
    b0 = build(c, (0, 0))
    xs = [v.co.x for v in b0.bm.verts]; ys = [v.co.y for v in b0.bm.verts]
    shift = ((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2)
    b0.bm.free()
    b = build(c, shift)
    dz = -min(v.co.z for v in b.bm.verts)
    obj = b.finish(center_xy=False, ground=True)
    obj['place'] = [-shift[0], -shift[1], dz]
    return obj


obj = make(cam)
fit = check_fit(obj)
print('fit', fit)
if '--opt' in sys.argv:
    cam, s = BK.optimize_camera(obj, CONCEPT, cam, keys=('az', 'dist', 'shift'))
    json.dump(cam, open(cam_path, 'w'), indent=1)
    obj = make(cam)          # re-seat the eyes for the optimised camera
    fit = check_fit(obj)
iou = BK.align_report(obj, CONCEPT, cam, os.path.join(OUT, 'align.png'))
part_overlay(obj, cam, os.path.join(HERE, 'tmp', 'parts.png'))
shade_views(obj, os.path.join(HERE, 'tmp', 'views.png'))
print('IoU', round(iou, 4), cam)
if '--align-only' in sys.argv:
    sys.exit()

# ------------------------------------------------------------------ texture
me = obj.data
eid = [a.value for a in me.attributes['eye_id'].data]
bpy.context.view_layer.objects.active = obj
obj.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='DESELECT')
bpy.ops.object.mode_set(mode='OBJECT')
for p in me.polygons:
    p.select = eid[p.index] == 0
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.uv.smart_project(angle_limit=math.radians(50), island_margin=0.012, area_weight=0.0, correct_aspect=True,
                         scale_to_bounds=False)
bpy.ops.object.mode_set(mode='OBJECT')
uvl = me.uv_layers.active.data
P3, UV3, _, _ = BK.mesh_arrays(obj)
me.calc_loop_triangles()
tri_poly = np.array([t.polygon_index for t in me.loop_triangles])
non_eye = np.array([eid[i] == 0 for i in tri_poly])
a3 = 0.5 * np.linalg.norm(np.cross(P3[:, 1] - P3[:, 0], P3[:, 2] - P3[:, 0]), axis=1)
d1, d2 = UV3[:, 1] - UV3[:, 0], UV3[:, 2] - UV3[:, 0]
a2 = 0.5 * np.abs(d1[:, 0] * d2[:, 1] - d1[:, 1] * d2[:, 0])
dens = math.sqrt(a2[non_eye].sum() / a3[non_eye].sum())
for p in me.polygons:
    if eid[p.index]:
        for i in range(p.loop_start, p.loop_start + p.loop_total):
            u, v = uvl[i].uv
            uvl[i].uv = (u * S * dens * EYE_UV_BOOST, v * S * dens * EYE_UV_BOOST)
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.select_all(action='SELECT')
bpy.ops.uv.pack_islands(rotate=True, merge_overlap=True, margin=0.012)
bpy.ops.object.mode_set(mode='OBJECT')

px, py, pz = obj['place']
Mc = Matrix.Translation((0, 0, pz)) @ Matrix.Scale(S, 4) @ Matrix.Translation((px, py, 0)) @ cap_matrix()
Mc_np, Mci_np = np.array(Mc), np.array(Mc.inverted())
def T_cap(a):
    return Mc_np @ BK.T_rot_z(a) @ Mci_np
size = BK.texture_size_for(obj)
views = [{'name': 'orig', 'T': np.eye(4), 'bonus': 0.3}]
for a in (30, -30, 60, -60, 90, -90, 120, -120, 150, -150, 180):
    views.append({'name': f'cap{a}', 'T': T_cap(a), 'parts': ['cap', 'gills']})
for a in (40, -40, 80, -80, 120, -120, 160, -160, 200):
    views.append({'name': f'stalk{a}', 'T': BK.T_rot_z(a, center=(px * S, py * S)), 'parts': ['stalk', 'skirt']})
# concept pixels that show eye whites/pupils must never paint the cap (rotated copies would smear them on the back)
_rgb, _cm = BK.load_concept(CONCEPT)
_hsv = cv2.cvtColor(_rgb, cv2.COLOR_RGB2HSV)
_eye_px = ((_hsv[..., 1] < 120) & (_hsv[..., 2] > 120)) | (_hsv[..., 2] < 70)
_eye_px[600:] = False
_dark = _hsv[..., 2] < 105
_eye_px = cv2.dilate(_eye_px.astype(np.uint8), np.ones((5, 5), np.uint8)).astype(bool)
# outer ring of every eyeball (sides the concept never shows): calm fleshy pink, close tones (no streaks)
part_colors = {'eyes': [(176, 116, 102), (186, 128, 113), (194, 138, 122)]}
tex_path = os.path.join(OUT, f'T_{ID}.png')
tex, st = BK.bake(obj, CONCEPT, cam, views=views, size=size, out_png=tex_path,
                  debug_png=os.path.join(OUT, 'coverage.png'), part_colors=part_colors, min_facing=0.32,
                  exclude={'cap': _eye_px, 'skirt': _dark}, nearest_fill=('eyes', 'skirt'),
                  disc_fill={'eyes': 0.78})
print('bake', json.dumps(st))
if '--bake-only' in sys.argv:
    sys.exit()
R.apply_texture(obj, tex_path, ID)
for a in ('eye_id', 'eye_src'):
    me.attributes.remove(me.attributes[a])
stats = check_fit(obj)
stats['texture'] = f'{size}x{size}'
stats['iou'] = round(iou, 4)
R.render_views(obj, os.path.join(OUT, 'preview.png'), concept_cam=cam, concept_size=(CW, CH), concept_path=CONCEPT)
R.shelf_fit_render(obj, os.path.join(OUT, 'shelf.png'))
R.export(obj, OUT, ID, tex_path)
json.dump({'id': ID, 'stats': stats, 'bake': st, 'camera': cam}, open(os.path.join(OUT, 'stats.json'), 'w'), indent=1)
R.card(OUT, ID, stats)
print('done', stats)
