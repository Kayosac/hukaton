"""14 Ornate mantel clock - collectible shelf item. Run: python3 build.py [--opt] [--align-only] [--quick]

Masses (front three-quarter concept, camera a little right of front and above):
- base slab (wide crooked box) on 4 splayed gold bracket feet, gold step band on top of it
- central tower box with an overhanging cornice and a low hip roof, gold shell crest standing on the roof
- two side wings: S-volute outlines extruded backward (thinner at the top), gold S scroll bands proud of the wing
  front with raised round curl "eyes"
- octagonal gold bezel protruding from the tower, cream dial recessed inside it, small hub
Front outlines are traced in concept pixels (gold mask contours + hand-placed corners) and back-projected through the
design camera CAM0 onto each part's front plane; depths are chosen by eye from the visible +X side faces.
Painted (concept projection): hour marks + hands, gold inlay lines, leaves/flowers, rays on the crest, wood grain,
scuffs. Hidden -X sides = mirror of the +X sides; back = plain wood panel painted procedurally.
"""
import sys, os, json, math
sys.path.insert(0, '/home/claude/kit')
HERE = os.path.dirname(os.path.abspath(__file__))
import numpy as np
import cv2
from scipy import ndimage
import bpy, bmesh
from mathutils import Vector, Matrix
from geo import Builder, clear_scene, check_fit
sys.path.insert(0, HERE)
import bake_local as BK          # item 05's kit copy: true z-buffer (stepped boxes occlude each other)
import render as R
import cam as C

CONCEPT = '/home/claude/concepts/collectible-concepts/14-ornate-mantel-clock-v1.png'
ID = 'CI14_OrnateMantelClock'
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)
WORK = os.path.join(HERE, 'tmp'); os.makedirs(WORK, exist_ok=True)
CW, CH = 1536, 1024

# design camera: geometry is back-projected through it (it never changes; camera.json is the fitted one)
CAM0 = {'az': 22.0, 'el': 3.0, 'dist': 2.25, 'fov': 8.0, 'target': [0.0, 0.0, 0.10], 'shift': [0.0, 0.0], 'roll': 0.0}


def bp(pts, y0, cam=CAM0):
    """Concept pixels [(u, v), ...] -> 3D points on the plane y = y0 (ray through the design camera)."""
    pos, right, upc, f = C.basis(cam)
    big = max(CW, CH)
    fpx = (big / 2) / math.tan(math.radians(cam['fov']) / 2)
    sx, sy = cam.get('shift', [0, 0])
    out = []
    for u, v in pts:
        a = (u - CW / 2 - sx * big) / fpx
        b_ = (CH / 2 - sy * big - v) / fpx
        d = f + right * a + upc * b_
        t = (y0 - pos[1]) / d[1]
        p = pos + d * t
        out.append((float(p[0]), float(y0), float(p[2])))
    return out


def z_at(v, y0, u=768):
    return bp([(u, v)], y0)[0][2]


# ------------------------------------------------------------------ traced outlines (concept pixels)
def gold_mask():
    im = cv2.imread(CONCEPT)
    lab = cv2.cvtColor(cv2.GaussianBlur(im, (0, 0), 2), cv2.COLOR_BGR2LAB).astype(int)
    g = (lab[..., 2] > 155) & (lab[..., 0] > 110)
    g = ndimage.binary_opening(g, iterations=2)
    return ndimage.binary_closing(g, iterations=4)


def component_poly(g, bbox, eyes=(), eps=3.0, fill=True, grow=0):
    """Outline polygon of the gold component inside bbox (x0, y0, x1, y1); eyes = discs that fill spiral notches."""
    lab, n = ndimage.label(g)
    x0, y0, x1, y1 = bbox
    sub = lab[y0:y1, x0:x1]
    ids, cnt = np.unique(sub[sub > 0], return_counts=True)
    m = (lab == ids[np.argmax(cnt)]).astype(np.uint8)
    for x, y, r in eyes:
        cv2.circle(m, (x, y), r, 1, -1)
    m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (17, 17)))
    if fill:
        m = ndimage.binary_fill_holes(m).astype(np.uint8)
    if grow:
        m = cv2.dilate(m, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * grow + 1, 2 * grow + 1)))
    cs, _ = cv2.findContours(m, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    c = max(cs, key=cv2.contourArea)
    p = cv2.approxPolyDP(c, eps, True)[:, 0].astype(float)
    return p, m


G = gold_mask()
SCROLL_L, mL = component_poly(G, (130, 310, 440, 740), eyes=[(352, 397, 30), (378, 408, 20), (238, 660, 38)], grow=3)
SCROLL_R, mR = component_poly(G, (980, 330, 1280, 770), eyes=[(1078, 415, 34), (1185, 700, 38), (1240, 690, 18)], grow=4)
CREST, _ = component_poly(G, (560, 5, 940, 225), eps=3.0, grow=0)
OCT = [(620, 253), (818, 253), (948, 385), (950, 585), (818, 735), (610, 728), (480, 578), (482, 398)]
DIAL_C, DIAL_R = (720.0, 497.0), 196.0

# ------------------------------------------------------------------ planes (metres, front = -y)
DS = 0.92                      # depth scale (front outlines are fixed by the concept; depths chosen by eye)
Y_SLAB = (-0.046 * DS, 0.066 * DS)
Y_BAND = (-0.042 * DS, 0.036 * DS)
Y_TOWER = (-0.031 * DS, 0.031 * DS)
Y_CORN = (-0.035 * DS, 0.035 * DS)
Y_WING_F = -0.025 * DS
SCROLL_PROUD = 0.005
Y_CREST = (-0.024, -0.004)
Y_FOOT = 0.026                 # foot depth

clear_scene()
b = Builder(ID, seed=14)


HIDDEN = []      # faces buried inside other parts (deleted before finish: no wasted texels)


def prism(part, pts3, yb, jitter=0.0, drop=()):
    """Extrude a front polygon (3D points on a y plane) straight back to y = yb (float or callable(z) -> y)."""
    mi = b.part(part)
    F = [b.bm.verts.new((x + b.j(jitter), y, z + b.j(jitter))) for x, y, z in pts3]
    Bk = [b.bm.verts.new((v.co.x, yb(v.co.z) if callable(yb) else yb, v.co.z)) for v in F]
    n = len(F)
    # orientation: front face must look toward -y
    area = sum(F[i].co.x * F[(i + 1) % n].co.z - F[(i + 1) % n].co.x * F[i].co.z for i in range(n))
    if area < 0:
        F.reverse(); Bk.reverse()
    fr = b._face(F, mi); bk = None if 'back' in drop else b._face(list(reversed(Bk)), mi)
    for i in range(n):
        k = (i + 1) % n
        f = b._face([F[k], F[i], Bk[i], Bk[k]], mi)
        if f is not None:
            f.normal_update()
            if ('bottom' in drop and f.normal.z < -0.9) or ('top' in drop and f.normal.z > 0.9):
                HIDDEN.append(f)
    return F, Bk, fr, bk


def quad(u0, v0, u1, v1):
    return [(u0, v1), (u1, v1), (u1, v0), (u0, v0)]


def zline(p, q):
    """z(x) of the straight edge through the 3D points p, q (the stacked boxes follow each other's crooked edges)."""
    (x0, _, z0), (x1, _, z1) = p, q
    return lambda x: z0 + (x - x0) * (z1 - z0) / (x1 - x0)


def sit_on(pts, zfun, n_bottom=2, gap=0.001):
    """First n_bottom points of a front quad are lowered/raised onto the edge zfun (slightly embedded)."""
    return [(x, y, zfun(x) - gap) if i < n_bottom else (x, y, z) for i, (x, y, z) in enumerate(pts)]


# --- base slab: left end traced (bullnose), right end = the same profile mirrored and fitted to the concept's
# right edge (its side face hides the profile)
left_end = bp([(118, 897), (127, 846), (138, 820), (163, 804)], Y_SLAB[0])
r_bot, r_top = bp([(1214, 926), (1217, 800)], Y_SLAB[0])
xm = 0.5 * (left_end[0][0] + r_bot[0])
zl0, zl1 = left_end[0][2], left_end[-1][2]
right_end = [(2 * xm - x, y, r_bot[2] + (z - zl0) / (zl1 - zl0) * (r_top[2] - r_bot[2])) for x, y, z in left_end]
print('slab z  left', round(zl0, 4), round(zl1, 4), ' right', round(r_bot[2], 4), round(r_top[2], 4))
slab = [left_end[0]] + right_end + left_end[1:][::-1]     # L0 R0 R1 R2 R3 L3 L2 L1
prism('base', slab, Y_SLAB[1])
SLAB_TOP = zline(left_end[-1], right_end[-1])
SLAB_BOT = zline(left_end[0], right_end[0])

# --- gold band step
band = sit_on(bp([(172, 801), (1219, 801), (1219, 760), (172, 760)], Y_BAND[0]), SLAB_TOP)
prism('band', band, Y_BAND[1], drop=('bottom',))
BAND_TOP = zline(band[3], band[2])
Z_BAND_TOP = 0.5 * (band[2][2] + band[3][2])

# --- tower
tw = sit_on(bp([(425, 760), (1002, 760), (1002, 267), (425, 267)], Y_TOWER[0]), BAND_TOP)
prism('tower', tw, Y_TOWER[1], drop=('bottom', 'top'))
TOWER_TOP = zline(tw[3], tw[2])

# --- cornice (front face) + hip roof
cf = sit_on(bp([(407, 267), (1005, 267), (1005, 232), (407, 232)], Y_CORN[0]), TOWER_TOP)
prism('cornice', cf, Y_CORN[1], drop=('bottom', 'top'))
CORN_TOP = zline(cf[3], cf[2])
cx0, cx1 = cf[0][0], cf[1][0]
ROOF_IN, ROOF_IN_X, ROOF_H = 0.010, (0.028, 0.018), 0.010
roof_b = [(x, y, CORN_TOP(x) - 0.001) for x, y in ((cx0, Y_CORN[0]), (cx1, Y_CORN[0]), (cx1, Y_CORN[1]), (cx0, Y_CORN[1]))]
roof_t = [(x, y, CORN_TOP(x) + ROOF_H) for x, y in ((cx0 + ROOF_IN_X[0], Y_CORN[0] + ROOF_IN), (cx1 - ROOF_IN_X[1], Y_CORN[0] + ROOF_IN),
                                                   (cx1 - ROOF_IN_X[1], Y_CORN[1] - ROOF_IN), (cx0 + ROOF_IN_X[0], Y_CORN[1] - ROOF_IN))]
b.loft('roof', [roof_b, roof_t], closed_ends=(False, True))
ROOF_TOP = lambda x: CORN_TOP(x) + ROOF_H


# --- wings: region between the S scroll and the tower, extruded back (thinner at the top)
def wing_region(m, tower_u, side, notch, grow):
    """Wing front outline (pixels): scroll mask + the area it encloses with the tower edge and the band."""
    r = m.copy()
    cv2.circle(r, notch[:2], notch[2], 1, -1)     # the wing body fills the notch under the top curl
    ys = np.nonzero(r.any(1))[0]
    y_top = ys.min() + 40
    if side < 0:
        cv2.rectangle(r, (tower_u - 4, y_top), (tower_u + 16, 765), 1, -1)
        xs = np.nonzero(r[700])[0]
        cv2.rectangle(r, (int(xs.min()) + 8, 690), (tower_u + 16, 765), 1, -1)
    else:
        cv2.rectangle(r, (tower_u - 16, y_top), (tower_u + 4, 765), 1, -1)
        xs = np.nonzero(r[700])[0]
        cv2.rectangle(r, (tower_u - 16, 690), (int(xs.max()) - 8, 765), 1, -1)
    r = ndimage.binary_fill_holes(r).astype(np.uint8)
    r = cv2.dilate(r, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * grow + 1, 2 * grow + 1)))
    cs, _ = cv2.findContours(r, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    c = max(cs, key=cv2.contourArea)
    return cv2.approxPolyDP(c, 3.5, True)[:, 0].astype(float)


WING_BACK_PROFILE = ((0.0, 0.028), (0.45, 0.031), (0.68, 0.016), (0.85, -0.002), (1.0, -0.006))


def wing_back(z):
    """Back plane of the wing by height: deep at the S belly, thin at the top curl (as the concept's +X side)."""
    t = float(np.clip((z - Z_BAND_TOP) / 0.09, 0, 1))
    ts, ys = zip(*WING_BACK_PROFILE)
    return float(np.interp(t, ts, ys))


def left_wing_px():
    """Left wing front outline = the concept silhouette left of the tower and above the band (the camera sits at
    the right, so on the left the silhouette IS the front outline)."""
    cm = BK.load_concept(CONCEPT)[1].astype(np.uint8)
    r = np.zeros_like(cm)
    r[300:762, :446] = cm[300:762, :446]
    r[300:762, 428:446] = 1
    r = ndimage.binary_opening(r, iterations=2).astype(np.uint8)
    lab_, n_ = ndimage.label(r)
    sizes = ndimage.sum(r, lab_, range(1, n_ + 1))
    r = (lab_ == 1 + int(np.argmax(sizes))).astype(np.uint8)
    cs, _ = cv2.findContours(r, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    c = max(cs, key=cv2.contourArea)
    return cv2.approxPolyDP(c, 3.0, True)[:, 0].astype(float)


# left wing from the silhouette; right wing from its traced scroll + the notch under the top curl + tower strip
# (the concept draws the right wing ~7 % narrower than the left: kept, it reads as the concept's crookedness)
wl = bp(left_wing_px(), Y_WING_F)
wr = bp(wing_region(mR, 1000, 1, (1112, 450, 26), 3), Y_WING_F)
for w3, sc in ((wl, SCROLL_L), (wr, SCROLL_R)):
    w3 = [(x, y, max(z, BAND_TOP(x) - 0.001)) for x, y, z in w3]
    prism('wing', w3, wing_back, drop=('bottom',))
    # gold S scroll band proud of the wing front
    s3 = bp(sc, Y_WING_F - SCROLL_PROUD)
    prism('scroll', s3, Y_WING_F + 0.003, drop=('back',))

# --- octagonal bezel (16-point rings: octagon corners + edge midpoints) and recessed dial
oct16 = []
for i in range(8):
    a, c = OCT[i], OCT[(i + 1) % 8]
    oct16 += [a, ((a[0] + c[0]) / 2, (a[1] + c[1]) / 2)]
ang = [math.atan2(v - DIAL_C[1], u - DIAL_C[0]) for u, v in oct16]
circ = lambda r: [(DIAL_C[0] + r * math.cos(t), DIAL_C[1] + r * math.sin(t)) for t in ang]
inset = lambda s: [(DIAL_C[0] + (u - DIAL_C[0]) * s, DIAL_C[1] + (v - DIAL_C[1]) * s) for u, v in oct16]
YT = Y_TOWER[0]
Y_BEZ, Y_DIAL = YT - 0.010, YT - 0.003
rings_px = [(oct16, YT + 0.002), (oct16, YT - 0.006), (inset(0.91), Y_BEZ), (circ(DIAL_R * 1.04), Y_BEZ), (circ(DIAL_R), Y_DIAL)]
bez = [bp(px, y) for px, y in rings_px]
bez_rings = b.loft('bezel', bez, closed_ends=(False, False))
# dial disk (16-gon) slightly behind the bezel's inner wall foot
dial_pts = bp(circ(DIAL_R), Y_DIAL)
mi_d = b.part('dial')
dv = [b.bm.verts.new(p) for p in dial_pts]
b._face(list(reversed(dv)), mi_d)
DIAL_CENTER = bp([DIAL_C], Y_DIAL)[0]
# small hub on the dial
hub_c = DIAL_CENTER
hub_rings = b.lathe('hub', [(0.0050, -0.001), (0.0046, 0.0022), (0.0, 0.0036)], sides=6, rot=0.3, cap_bottom=True, cap_top=False)
M_hub = Matrix.Translation(hub_c) @ Matrix.Rotation(math.radians(90), 4, 'X')   # lathe +z -> -y (out of the dial)
for rg in hub_rings:
    for v in rg:
        v.co = M_hub @ v.co

# --- crest (gold shell fan) standing on the roof: front face fans from a bulged centre
cr = [list(p) for p in CREST]
cr3 = bp(cr, Y_CREST[0])
# bottom edge points sink into the roof
cr3 = [(x, y, ROOF_TOP(x) - 0.004) if v > 205 else (x, y, z) for (x, y, z), (u, v) in zip(cr3, cr)]
mi_c = b.part('crest')
F = [b.bm.verts.new(p) for p in cr3]
Bk = [b.bm.verts.new((p[0], Y_CREST[1], p[2])) for p in cr3]
n = len(F)
area = sum(F[i].co.x * F[(i + 1) % n].co.z - F[(i + 1) % n].co.x * F[i].co.z for i in range(n))
if area < 0:
    F.reverse(); Bk.reverse()
cen = bp([(725, 170)], Y_CREST[0] - 0.004)[0]
cv_ = b.bm.verts.new(cen)
for i in range(n):
    k = (i + 1) % n
    b._face([F[i], F[k], cv_], mi_c)
    b._face([F[k], F[i], Bk[i], Bk[k]], mi_c)
b._face(list(reversed(Bk)), mi_c)

# --- feet: splayed gold bracket blocks under the slab corners
def foot(px, yf, depth):
    p3 = bp(px, yf)
    p3 = [(x, y, SLAB_BOT(x) + 0.002 if i >= 2 else z) for i, (x, y, z) in enumerate(p3)]
    prism('feet', p3, yf + depth, drop=('top',))
    return p3


fl = foot([(138, 972), (222, 976), (246, 892), (160, 892)], Y_SLAB[0] + 0.004, Y_FOOT)
fr = foot([(1088, 999), (1180, 1001), (1182, 905), (1056, 905)], Y_SLAB[0] + 0.004, Y_FOOT)
# back feet: front feet moved to the back corners
for p3 in (fl, fr):
    dy = Y_SLAB[1] - 0.004 - Y_FOOT - p3[0][1]
    prism('feet', [(x, y + dy, z) for x, y, z in p3], Y_SLAB[1] - 0.004, drop=('top',))

# ------------------------------------------------------------------ open shells: make bezel / dial / hub face out
for pn in ('bezel', 'dial', 'hub', 'crest'):
    k = b.parts.index(pn)
    fs = [f for f in b.bm.faces if f.material_index == k]
    for f in fs:
        f.normal_update()
    if sum(f.normal.y * f.calc_area() for f in fs) > 0:
        bmesh.ops.reverse_faces(b.bm, faces=fs)
bmesh.ops.delete(b.bm, geom=list(set(HIDDEN)), context='FACES_ONLY')
bmesh.ops.delete(b.bm, geom=[v for v in b.bm.verts if not v.link_faces], context='VERTS')

# ------------------------------------------------------------------ hidden-side parts by face direction
PB = b.part('back'); PBOT = b.part('bottom')
WOOD = [b.parts.index(p) for p in ('base', 'tower', 'cornice', 'wing', 'roof')]
for f in b.bm.faces:
    f.normal_update()
for f in b.bm.faces:
    if f.material_index in WOOD and f.normal.y > 0.6:
        f.material_index = PB
    elif f.material_index in WOOD + [b.parts.index('band')] and f.normal.z < -0.6:
        f.material_index = PBOT
bmesh.ops.triangulate(b.bm, faces=[f for f in b.bm.faces if len(f.verts) > 4], quad_method='BEAUTY', ngon_method='BEAUTY')

_co = np.array([v.co[:] for v in b.bm.verts])
SHIFT = np.array([-(_co[:, 0].min() + _co[:, 0].max()) / 2, -(_co[:, 1].min() + _co[:, 1].max()) / 2, -_co[:, 2].min()])
obj = b.finish()
fit = check_fit(obj)
print('fit', fit)
PARTS = list(obj['parts'])

BK.unwrap(obj)
cam_path = os.path.join(HERE, 'camera.json')
cam0 = json.loads(json.dumps(CAM0)); cam0['target'] = list(np.array(CAM0['target']) + SHIFT)
cam = json.load(open(cam_path)) if os.path.exists(cam_path) else cam0
if '--opt' in sys.argv:
    cam = cam0     # geometry is back-projected through CAM0: the design camera is the fitted camera
    json.dump(cam, open(cam_path, 'w'), indent=1)
print('IoU', BK.align_report(obj, CONCEPT, cam, os.path.join(OUT, 'align.png')), cam)


def edge_overlay(obj, cam, out_png):
    """Concept with the model's hard edges drawn on top (inner edges the silhouette IoU cannot see)."""
    me = obj.data
    V = np.array([v.co[:] for v in me.vertices])
    u, v, _ = C.project(V, cam, CW, CH)
    im = cv2.imread(CONCEPT)
    P, UV, part, N = BK.mesh_arrays(obj)
    idb, zb = BK.rasterize(P, part, cam, CW, CH)
    fn = {}
    for p in me.polygons:
        fn[p.index] = p.normal
    ek = {}
    for p in me.polygons:
        for k in p.edge_keys:
            ek.setdefault(k, []).append(p.index)
    for k, fs in ek.items():
        if len(fs) == 2 and fn[fs[0]].dot(fn[fs[1]]) > 0.985:
            continue
        a, c = k
        # visible if the midpoint is not hidden
        mid = (V[a] + V[c]) / 2
        mu, mv, mz = C.project(mid[None], cam, CW, CH)
        iu, iv = int(np.clip(mu[0], 0, CW - 1)), int(np.clip(mv[0], 0, CH - 1))
        col = (60, 255, 60) if mz[0] <= zb[iv, iu] + 0.004 else (60, 120, 60)
        if col[1] == 120:
            continue
        cv2.line(im, (int(u[a]), int(v[a])), (int(u[c]), int(v[c])), col, 1, cv2.LINE_AA)
    cv2.imwrite(out_png, im)


if '--align-only' in sys.argv:
    edge_overlay(obj, cam, os.path.join(WORK, 'edges.png'))
    sys.exit()

# ------------------------------------------------------------------ bake
def texel_points(obj, size, part_names):
    """(ys, xs, X world pos, N normal) for every texel of the given parts (UV-space raster, as in the bake)."""
    P, UV, part, N = BK.mesh_arrays(obj)
    ks = [PARTS.index(p) for p in part_names]
    tri_img = np.full((size, size), -1, np.int32)
    uvp = UV * size
    uvp[..., 1] = size - uvp[..., 1]
    for t in np.nonzero(np.isin(part, ks))[0]:
        cv2.fillPoly(tri_img, [np.round(uvp[t] * 16).astype(np.int32)], int(t), lineType=cv2.LINE_8, shift=4)
    ys, xs = np.nonzero(tri_img >= 0)
    t = tri_img[ys, xs]
    a0, a1, a2 = uvp[t, 0], uvp[t, 1], uvp[t, 2]
    px, py = xs + 0.5, ys + 0.5
    den = (a1[:, 1] - a2[:, 1]) * (a0[:, 0] - a2[:, 0]) + (a2[:, 0] - a1[:, 0]) * (a0[:, 1] - a2[:, 1])
    den = np.where(np.abs(den) < 1e-12, 1e-12, den)
    l0 = ((a1[:, 1] - a2[:, 1]) * (px - a2[:, 0]) + (a2[:, 0] - a1[:, 0]) * (py - a2[:, 1])) / den
    l1 = ((a2[:, 1] - a0[:, 1]) * (px - a2[:, 0]) + (a0[:, 0] - a2[:, 0]) * (py - a2[:, 1])) / den
    l0, l1 = np.clip(l0, 0, 1), np.clip(l1, 0, 1)
    l2 = np.clip(1 - l0 - l1, 0, 1)
    sm = np.maximum(l0 + l1 + l2, 1e-9)
    l0, l1, l2 = l0 / sm, l1 / sm, l2 / sm
    X = P[t, 0] * l0[:, None] + P[t, 1] * l1[:, None] + P[t, 2] * l2[:, None]
    return ys, xs, X, N[t]


WOOD_PAL = np.array([(78, 48, 40), (96, 60, 47), (110, 70, 52), (124, 80, 57), (140, 92, 63)], np.float32)
GOLD_PAL = [(150, 104, 56), (176, 128, 70), (196, 150, 86), (212, 170, 104)]


def paint_back(tex, size):
    """Plain painted wood back: horizontal grain in the concept's wood colours, a darker inset panel frame on the
    tower back and a few light scuffs - drawn at texel scale (1 texel ~ 1.5 mm)."""
    rng = np.random.default_rng(1414)
    ys, xs, X, Nn = texel_points(obj, size, ['back'])
    step = 0.0016
    gx = ((X[:, 0] + 0.2) / step).astype(int); gz = (X[:, 2] / step).astype(int)
    gw, gh = gx.max() + 8, gz.max() + 8
    field = rng.random((gh, gw)).astype(np.float32)
    field = cv2.GaussianBlur(field, (0, 0), sigmaX=7.0, sigmaY=0.7)
    field = (field - field.min()) / max(np.ptp(field), 1e-6)
    v = field[gz, gx]
    idx = np.clip((v * 1.25 - 0.12) * len(WOOD_PAL), 0, len(WOOD_PAL) - 1).astype(int)
    col = WOOD_PAL[idx].copy()
    # inset panel on the tower back: darker groove 2 texels wide, slightly lighter field inside
    xb0, xb1 = tw[0][0] + SHIFT[0], tw[1][0] + SHIFT[0]
    zb0, zb1 = BAND_TOP(tw[0][0]) + SHIFT[2], TOWER_TOP(tw[0][0]) + SHIFT[2]
    on_tower = np.abs(X[:, 1] - (Y_TOWER[1] + SHIFT[1])) < 0.002
    ins = 0.012
    inside = on_tower & (X[:, 0] > xb0 + ins) & (X[:, 0] < xb1 - ins) & (X[:, 2] > zb0 + ins) & (X[:, 2] < zb1 - ins)
    ring = on_tower & (X[:, 0] > xb0 + ins - 0.0032) & (X[:, 0] < xb1 - ins + 0.0032) & \
        (X[:, 2] > zb0 + ins - 0.0032) & (X[:, 2] < zb1 - ins + 0.0032) & ~inside
    col[inside] *= 1.08
    col[ring] = (58, 36, 30)
    # light diagonal scuffs like the concept's
    for _ in range(14):
        cx, cz = rng.uniform(X[:, 0].min(), X[:, 0].max()), rng.uniform(0.02, X[:, 2].max())
        ang, ln = rng.uniform(-0.6, 0.6), rng.uniform(0.008, 0.02)
        d = np.stack([X[:, 0] - cx, X[:, 2] - cz], 1)
        along = d @ np.array([math.cos(ang), math.sin(ang)])
        perp = d @ np.array([-math.sin(ang), math.cos(ang)])
        hit = (np.abs(along) < ln / 2) & (np.abs(perp) < 0.0009)
        col[hit] = (168, 116, 74)
    col *= rng.uniform(0.96, 1.04, (len(col), 1))
    tex[ys, xs] = np.clip(col, 0, 255).astype(np.uint8)
    return tex


size = BK.texture_size_for(obj)
views = [{'name': 'orig', 'T': np.eye(4), 'bonus': 0.3},
         {'name': 'mirror_x', 'T': BK.T_mirror_plane('x'), 'bonus': 0.05},
         {'name': 'crest_back', 'T': BK.T_mirror_plane('y', at=0.5 * (Y_CREST[0] + Y_CREST[1]) + SHIFT[1]), 'parts': ['crest']}]
part_colors = {'back': [tuple(c) for c in WOOD_PAL], 'bottom': [(62, 40, 34), (78, 50, 40), (92, 60, 46)],
               'feet': GOLD_PAL}
tex_path = os.path.join(OUT, f'T_{ID}.png')
tex, st = BK.bake(obj, CONCEPT, cam, views=views, size=size, out_png=tex_path, debug_png=os.path.join(OUT, 'coverage.png'),
                  part_colors=part_colors, erode_px=2, min_facing=0.3)
print('bake', json.dumps(st))
tex = paint_back(tex.copy(), size)


def lift_sides_and_dial(tex):
    """The concept paints the +X sides in deep shadow (and the -X sides copy them): bring every wood/gold side
    face part of the way to its front brightness, so the game light does not darken them twice. The dial reads as
    pale cream in the concept: lift it a little toward cream."""
    for pn in ('base', 'band', 'tower', 'cornice', 'wing', 'scroll', 'feet', 'roof'):
        ys, xs, X, Nn = texel_points(obj, size, [pn])
        if not len(ys):
            continue
        col = tex[ys, xs].astype(np.float32)
        lum = col @ np.array([0.3, 0.59, 0.11], np.float32)
        front = Nn[:, 1] < -0.7
        side = np.abs(Nn[:, 0]) > 0.6
        if front.sum() < 30 or side.sum() < 30:
            continue
        g = float(np.clip((np.percentile(lum[front], 70) / max(np.percentile(lum[side], 70), 1.0)) ** 0.6, 1.0, 1.6))
        col[side] *= g
        tex[ys, xs] = np.clip(col, 0, 255).astype(np.uint8)
        print('side gain', pn, round(g, 3))
    return tex


tex = lift_sides_and_dial(tex)


def repaint_dial(tex):
    """The hands and hour marks are 1-2 texels wide: re-bake the dial from a cleaned copy of the concept (flat cream
    paper keeping its blotches, marks/hands as one flat dark brown, slightly bolder) without the texel blur, so they
    come out as crisp chunky pixels instead of a smeared line."""
    im = cv2.imread(CONCEPT)[:, :, ::-1].astype(np.float32)
    H_, W_ = im.shape[:2]
    yy, xx = np.mgrid[0:H_, 0:W_]
    inside = (xx - DIAL_C[0]) ** 2 + (yy - DIAL_C[1]) ** 2 < (DIAL_R + 6) ** 2
    lum = im @ np.array([0.3, 0.59, 0.11], np.float32)
    dark = ((lum < 112) & inside).astype(np.uint8)
    dark = cv2.morphologyEx(dark, cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))
    dark = cv2.dilate(dark, np.ones((3, 3), np.uint8), iterations=2) > 0
    paper = inside & ~dark
    out = im.copy()
    out[paper] = im[paper] * 0.55 + np.array([214, 192, 168], np.float32) * 0.45
    out[dark] = (66, 42, 36)
    src = os.path.join(WORK, 'concept_dial.png')
    cv2.imwrite(src, cv2.cvtColor(np.clip(out, 0, 255).astype(np.uint8), cv2.COLOR_RGB2BGR))
    t2, _ = BK.bake(obj, src, cam, views=[{'name': 'orig', 'T': np.eye(4), 'parts': ['dial', 'hub']}], size=size,
                    erode_px=1, min_facing=0.3, texel_blur=False, quantize=0)
    ys, xs, X, Nn = texel_points(obj, size, ['dial'])
    tex[ys, xs] = t2[ys, xs]
    return tex


tex = repaint_dial(tex)
cv2.imwrite(tex_path, cv2.cvtColor(tex, cv2.COLOR_RGB2BGR))
R.apply_texture(obj, tex_path, ID)
stats = check_fit(obj)
stats['texture'] = f'{size}x{size}'
stats['iou'] = round(BK.align_report(obj, CONCEPT, cam), 4)
R.render_views(obj, os.path.join(OUT, 'preview.png'), concept_cam=cam, concept_size=(CW, CH), concept_path=CONCEPT)
if '--quick' in sys.argv:
    sys.exit()
R.shelf_fit_render(obj, os.path.join(OUT, 'shelf.png'))
R.export(obj, OUT, ID, tex_path)
json.dump({'id': ID, 'stats': stats, 'bake': st, 'camera': cam}, open(os.path.join(OUT, 'stats.json'), 'w'), indent=1)
R.card(OUT, ID, stats)
print('done', stats)
