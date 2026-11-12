"""04 Straw doll (CI04_StrawDoll) — collectible shelf item. Run: python3 build.py [--opt] [--align-only]

Masses are measured from the concept silhouette (front view, el ~10 deg): concept pixel -> metres with
X = (px - 751) * S, Z = (969 - py) * S, S = 0.21 m / 933 px.
Modeled: head bundle, torso bundle, one arm bundle through the shoulders, two splayed leg bundles, frayed hand and
foot fringes (jagged spikes, feet stand on their tips at z=0), raised rope bindings (two-wrap bands) at neck, wrists,
waist and ankles, belt knot and two hanging rope ends. Painted (projected from the concept): straw strands, rope twist.
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
CONCEPT = '/home/claude/concepts/collectible-concepts/04-straw-doll-v1.png'
ID = 'CI04_StrawDoll'
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)

S = 0.21 / 933
def X(px): return (px - 751) * S
def Z(py): return (969 - py) * S
def P(px, py, y=0.0): return (X(px), y, Z(py))

clear_scene()
b = Builder(ID, seed=4)
AXES = {}   # part -> [(kind, polyline, radius)] used to paint strands / rope plies around each bundle's own axis


def basis(axis):
    a = Vector(axis).normalized()
    xc = Vector((1, 0, 0)) if abs(a.x) < .9 else Vector((0, 1, 0))
    xc = (xc - a * xc.dot(a)).normalized()
    return a, xc, a.cross(xc)


def band(part, center, axis, r_in, r_out, h, sides=9, scale=(1, 1), rot=0.3, jitter=0.0006, wraps=2):
    """Raised rope binding around `axis`: open ring band whose inner edges are buried in the bundle.
    wraps=2 gives two rope turns (a groove in the middle)."""
    a = Vector(axis).normalized()
    c = Vector(center)
    if wraps == 2:
        prof = [(r_in, -h), (r_out * 0.97, -0.72 * h), (r_out, -0.42 * h), (r_out * 0.9, 0.0),
                (r_out, 0.42 * h), (r_out * 0.97, 0.72 * h), (r_in, h)]
    else:
        prof = [(r_in, -h), (r_out, -0.45 * h), (r_out, 0.45 * h), (r_in, h)]
    mi = b.part(part, True)
    rings = [b.ring_verts(b.ring(c + a * t, r, sides, z_axis=a, rot=rot, scale=scale, jitter=jitter)) for r, t in prof]
    for r0, r1 in zip(rings, rings[1:]):
        b.bridge(r0, r1, mi)
    AXES.setdefault(part, []).append(('band', [tuple(c - a * h), tuple(c + a * h)], r_out))


def fringe(part, base, axis, r0, r1, length, tips=6, flat_z=None, var=0.25, rot=0.0, scale=(1, 1), notch=0.5):
    """Frayed straw end: a flaring cone from `base` along `axis` whose end ring is castellated: every cut strand
    group ends in a blunt flat tip (2 verts) followed by a notch (1 vert), lengths vary like the concept.
    flat_z: put every tip at this z (feet: the doll stands on the flat tips)."""
    a, xc, yc = basis(axis)
    c = Vector(base)
    n = tips * 3
    mi = b.part(part, True)
    ring0, ring1 = [], []
    for i in range(n):
        ang = rot + 2 * math.pi * i / n
        d = xc * (math.cos(ang) * scale[0]) + yc * (math.sin(ang) * scale[1])
        ring0.append(c + d * r0)
        if i % 3 == 0:
            Ltip = length * (1 + b.j(var))
        if i % 3 < 2:
            L = Ltip + b.j(0.0012); rr = r1 * (1 + b.j(0.10))
        else:
            L = length * (notch + b.j(0.1)); rr = r1 * 0.86
        p = c + a * L + d * rr
        p += Vector((b.j(0.0006), b.j(0.0006), b.j(0.0006)))
        if flat_z is not None:
            p.z = flat_z if i % 3 < 2 else flat_z + length * (1 - notch) * (0.8 + b.j(0.2))
        ring1.append(p)
    v0 = b.ring_verts(ring0); v1 = b.ring_verts(ring1)
    b.bridge(v0, v1, mi)
    b.cap(v0, mi, flip=True)
    end = c + a * (length * 0.45)
    if flat_z is not None:
        end.z = flat_z + 0.003
    b.cap(v1, mi, center=tuple(end))
    AXES.setdefault(part, []).append(('straw', [tuple(c), tuple(c + a * length)], (r0 + r1) / 2))


# ---------------------------------------------------------------- head bundle (egg, gathered into the neck)
HX = X(738)
head_prof = [(0.0140, Z(380)), (0.0168, Z(326)), (0.0236, Z(300)), (0.0296, Z(264)), (0.0350, Z(205)),
             (0.0356, Z(160)), (0.0300, Z(108)), (0.0236, Z(70)), (0.0128, Z(44)), (0.0, Z(37))]
b.lathe('head', head_prof, sides=10, jitter=0.0009, rot=0.25, offset=(HX, 0, 0), scale=(1, 0.93),
        lean=(0.0, 0.0), cap_bottom=True)
AXES['head'] = [('straw', [(HX, 0, Z(390)), (HX, 0, Z(37))], 0.030)]

# ---------------------------------------------------------------- torso bundle (neck -> hips)
TX = X(750)
TSY = 0.85
torso_prof = [(0.0, Z(738)), (0.0200, Z(724)), (0.0300, Z(700)), (0.0340, Z(665)), (0.0318, Z(625)),
              (0.0262, Z(585)), (0.0250, Z(540)), (0.0262, Z(490)), (0.0268, Z(440)), (0.0258, Z(408)),
              (0.0225, Z(392)), (0.0140, Z(380))]
trings = b.lathe('body', torso_prof, sides=10, jitter=0.0007, rot=0.1, offset=(TX, 0, 0), scale=(1, TSY))
for ring in trings:                       # hips thin out front-to-back so they flow into the leg bundles (no ledge)
    for v in ring:
        k = float(np.clip((Z(640) - v.co.z) / (Z(640) - Z(735)), 0, 1))
        v.co.y *= 1 - 0.5 * k
AXES['body'] = [('straw', [(TX, 0, Z(760)), (TX, 0, Z(372))], 0.027)]

def torso_r(z):
    zs = [p[1] for p in torso_prof]; rs = [p[0] for p in torso_prof]
    return float(np.interp(z, zs, rs))

# ---------------------------------------------------------------- arms: ONE bundle through the shoulders
arm_px = [(523, 549), (558, 510), (594, 462), (645, 443), (751, 441), (851, 446), (897, 468), (944, 497), (982, 538)]
arm_r = [0.0094, 0.0108, 0.0120, 0.0132, 0.0134, 0.0132, 0.0120, 0.0106, 0.0094]
b.tube('arms', [P(x, y) for x, y in arm_px], radii=arm_r, sides=8, jitter=0.0005)
AXES['arms'] = [('straw', [P(x, y) for x, y in arm_px], 0.011)]

# wrists: binding + frayed hand
for side, wrist, toward, part in ((-1, (527, 545), (482, 612), 'hand_l'), (1, (983, 533), (1028, 604), 'hand_r')):
    w = Vector(P(*wrist)); t = Vector(P(*toward))
    ax = (t - w).normalized()
    band('rope', w, ax, 0.0080, 0.0128, 0.0058, sides=8, rot=0.2)
    fringe(part, w + ax * 0.002, ax, 0.0082, 0.0142, 0.0185 if side < 0 else 0.0185, tips=6, var=0.12, rot=0.15 * side)

# ---------------------------------------------------------------- legs (splayed, lean outward), ankle bindings, feet
legs = {'leg_l': (X(598), X(661)), 'leg_r': (X(862), X(830))}   # (x at z=0, x at z=Z(750))
for part, (x0, x1) in legs.items():
    z1 = Z(750)
    lean = (x1 - x0) / z1
    th = 0.0012 if part == 'leg_l' else 0.0
    prof = [(0.0118, 0.016), (0.0140, 0.024), (0.0150, Z(812)), (0.0172 + th, Z(770)), (0.0190 + th, Z(735)),
            (0.0196 + th, Z(690)), (0.0180, Z(645))]
    b.lathe(part, prof, sides=8, jitter=0.0006, rot=0.2, offset=(x0, 0, 0), lean=(lean, 0.0), scale=(1, 1.05))
    AXES[part] = [('straw', [(x0, 0, 0.0), (x0 + lean * 0.075, 0, 0.075)], 0.016)]
    zb = Z(850)
    cb = (x0 + lean * zb, 0.0, zb)
    ax = Vector((lean, 0, 1)).normalized()
    band('rope', cb, ax, 0.0118, 0.0165, 0.0075, sides=9, rot=0.1, scale=(1, 0.93))
    foot = 'foot_l' if part == 'leg_l' else 'foot_r'
    zf = 0.0215
    fringe(foot, (x0 + lean * zf, 0.0, zf), (-lean, 0, -1), 0.0125, 0.0176, zf, tips=7, flat_z=0.0,
           rot=0.2, scale=(1, 0.93), notch=0.6)

# ---------------------------------------------------------------- neck binding
band('rope', (X(751), 0, Z(357)), (0, 0, 1), 0.0125, 0.0192, 0.0066, sides=9, rot=0.2, scale=(1, 0.97))

# ---------------------------------------------------------------- belt, knot, hanging ends
zbelt = Z(566)
band('rope', (TX, 0, zbelt), (0, 0, 1), 0.0215, 0.0285, 0.0068, sides=10, rot=0.1, scale=(1, TSY))
def front_y(z, pad):
    k = float(np.clip((Z(640) - z) / (Z(640) - Z(735)), 0, 1))
    return -(torso_r(z) * TSY * (1 - 0.5 * k) + pad)
kx, kz = X(772), Z(568)
b.blob('knot', (kx, front_y(kz, 0.0050), kz), (0.0076, 0.0062, 0.0066), rings=4, sides=7, jitter=0.0004, squash_top=0.85)
AXES['knot'] = [('ply', [(kx, front_y(kz, 0.005), kz - 0.007), (kx, front_y(kz, 0.005), kz + 0.007)], 0.007)]
for path_px in ([(764, 582), (760, 615), (758, 664)], [(782, 585), (812, 618), (848, 658)]):
    pts = [(X(x), front_y(Z(y), 0.0035), Z(y)) for x, y in path_px]
    b.tube('rope_ends', pts, radius=0.0031, sides=5, jitter=0.0003)
    AXES.setdefault('rope_ends', []).append(('ply', pts, 0.0031))

_co = np.array([v.co[:] for v in b.bm.verts])
SHIFT = np.array([-(_co[:, 0].min() + _co[:, 0].max()) / 2, -(_co[:, 1].min() + _co[:, 1].max()) / 2, -_co[:, 2].min()])
obj = b.finish()
fit = check_fit(obj)
print('fit', fit)

BK.unwrap(obj, margin=0.006, angle=60)   # every texel is painted + gutters dilated: thin margins, denser texels
cam_path = os.path.join(HERE, 'camera.json')
cam = json.load(open(cam_path)) if os.path.exists(cam_path) else \
    {'az': 0, 'el': 10, 'dist': 1.11, 'fov': 18, 'target': [0, 0, 0.105], 'shift': [-0.012, 0.006], 'roll': 0}
if '--opt' in sys.argv:
    cam, s = BK.optimize_camera(obj, CONCEPT, cam, keys=('az', 'dist', 'shift'))
    json.dump(cam, open(cam_path, 'w'), indent=1)
print('IoU', BK.align_report(obj, CONCEPT, cam, os.path.join(OUT, 'align.png')), cam)
if '--align-only' in sys.argv:
    sys.exit()

size = BK.texture_size_for(obj)
# part axes (after finish() recentred the mesh) for rotational copies of the lathe bundles
me = obj.data
parts = list(obj['parts'])
def part_center(name):
    mi = parts.index(name)
    vs = {v for p in me.polygons if p.material_index == mi for v in p.vertices}
    co = np.array([me.vertices[i].co[:] for i in vs])
    return ((co[:, 0].min() + co[:, 0].max()) / 2, (co[:, 1].min() + co[:, 1].max()) / 2)
views = [{'name': 'orig', 'T': np.eye(4), 'bonus': 0.3},
         {'name': 'mirror_y', 'T': BK.T_mirror_plane('y'), 'bonus': 0.12}]
for name in ('head', 'body', 'leg_l', 'leg_r', 'foot_l', 'foot_r'):
    c = part_center(name)
    views += [{'name': f'{name}_rot{a}', 'T': BK.T_rot_z(a, c), 'parts': [name], 'bonus': 0.0}
              for a in (40, -40, 80, -80, 120, -120)]
tex_path = os.path.join(OUT, f'T_{ID}.png')
tex, st = BK.bake(obj, CONCEPT, cam, views=views, size=size, out_png=tex_path, debug_png=os.path.join(OUT, 'coverage.png'))
print('bake', json.dumps(st))

# ---------------------------------------------------------------- painted strands and rope plies
# The concept's only surface detail is straw strands (light/dark straw stripes along each bundle) and twisted rope.
# Projected at the shared texel density those stripes smear into blotches, so they are painted here per texel in
# 3D: strand stripes run around each bundle's own axis (so they read the same from every side), rope plies twist
# diagonally around each binding. All colours are sampled from the concept (k-means of straw and rope pixels).
import cv2
from scipy.spatial import cKDTree

def concept_palette(boxes, k):
    rgb, cm = BK.load_concept(CONCEPT)
    px = np.concatenate([rgb[y0:y1, x0:x1][cm[y0:y1, x0:x1]] for x0, y0, x1, y1 in boxes]).astype(np.float32)
    crit = (cv2.TERM_CRITERIA_EPS + cv2.TERM_CRITERIA_MAX_ITER, 30, 0.5)
    _, lab, cen = cv2.kmeans(px, k, None, crit, 3, cv2.KMEANS_PP_CENTERS)
    w = np.bincount(lab.ravel(), minlength=k) / len(lab)
    o = np.argsort(cen @ np.array([0.3, 0.59, 0.11]))
    return cen[o], w[o]

STRAW, STRAW_W = concept_palette([(640, 80, 840, 300), (650, 620, 860, 730), (600, 760, 700, 800)], 6)
STRAW = np.clip(STRAW * 1.08, 0, 255); STRAW_W = STRAW_W * np.array([0.5, 0.8, 1, 1.1, 1.3, 1.5])
ROPE_BOOST = 1.1  # concept's lit straw is the read colour
ROPE, ROPE_W = concept_palette([(680, 338, 810, 378), (640, 545, 740, 590), (580, 830, 690, 870)], 4)
print('straw palette', STRAW.astype(int).tolist(), STRAW_W.round(2).tolist())
print('rope palette', ROPE.astype(int).tolist(), ROPE_W.round(2).tolist())

def texel_points(obj, size):
    Pm, UVm, partm, Nm = BK.mesh_arrays(obj)
    tri_img = np.full((size, size), -1, np.int32)
    uvp = UVm * size
    uvp[..., 1] = size - uvp[..., 1]
    for t in range(len(Pm)):
        cv2.fillPoly(tri_img, [np.round(uvp[t] * 16).astype(np.int32)], int(t), lineType=cv2.LINE_8, shift=4)
    ys, xs = np.nonzero(tri_img >= 0)
    t = tri_img[ys, xs]
    px, py = xs + 0.5, ys + 0.5
    a0, a1, a2 = uvp[t, 0], uvp[t, 1], uvp[t, 2]
    den = (a1[:, 1] - a2[:, 1]) * (a0[:, 0] - a2[:, 0]) + (a2[:, 0] - a1[:, 0]) * (a0[:, 1] - a2[:, 1])
    den = np.where(np.abs(den) < 1e-12, 1e-12, den)
    l0 = np.clip(((a1[:, 1] - a2[:, 1]) * (px - a2[:, 0]) + (a2[:, 0] - a1[:, 0]) * (py - a2[:, 1])) / den, 0, 1)
    l1 = np.clip(((a2[:, 1] - a0[:, 1]) * (px - a2[:, 0]) + (a0[:, 0] - a2[:, 0]) * (py - a2[:, 1])) / den, 0, 1)
    l2 = np.clip(1 - l0 - l1, 0, 1)
    ssum = l0 + l1 + l2
    Xp = (Pm[t, 0] * l0[:, None] + Pm[t, 1] * l1[:, None] + Pm[t, 2] * l2[:, None]) / ssum[:, None]
    return ys, xs, Xp, partm[t]

def axis_coords(Xp, poly):
    """For points Xp and a polyline: distance to it, angle around it (0 = toward the front, -Y), arc length."""
    poly = np.asarray(poly, float) + SHIFT
    best = np.full(len(Xp), np.inf); th = np.zeros(len(Xp)); al = np.zeros(len(Xp))
    acc = 0.0
    for p0, p1 in zip(poly, poly[1:]):
        d = p1 - p0; L = np.linalg.norm(d); d = d / L
        sv = np.clip((Xp - p0) @ d, 0, L)
        F = p0 + sv[:, None] * d
        R_ = Xp - F
        dist = np.linalg.norm(R_, axis=1)
        e1 = np.array([0, -1.0, 0]); e1 = e1 - d * (e1 @ d); e1 /= np.linalg.norm(e1)
        e2 = np.cross(d, e1)
        m = dist < best
        best[m] = dist[m]
        th[m] = np.arctan2(R_[m] @ e2, R_[m] @ e1)
        al[m] = acc + sv[m]
        acc += L
    return best, th, al

def h(*a):
    """Cheap deterministic hash of integer arrays -> [0,1)."""
    x = np.zeros_like(np.asarray(a[0]), dtype=np.int64)
    for i, v in enumerate(a):
        x = x * 1000003 + np.asarray(v, np.int64) * (2654435761 + 97 * i)
    x = (x ^ (x >> 13)) * 1274126177
    x = x ^ (x >> 16)
    return (x & 0xFFFFFF) / float(0x1000000)

def pick(pal, w, u):
    cdf = np.cumsum(w) / np.sum(w)
    return pal[np.clip(np.searchsorted(cdf, u * 0.999), 0, len(pal) - 1)]

def paint(obj, size, base_tex):
    ys, xs, Xp, tp = texel_points(obj, size)
    col = np.zeros((len(Xp), 3), np.float32)
    # rope surface points for the darkening of straw gathered under the bindings
    me = obj.data
    rope_ids = [parts.index(n) for n in ('rope', 'knot') if n in parts]
    rv = np.array([me.vertices[v].co[:] for p in me.polygons if p.material_index in rope_ids for v in p.vertices])
    rope_tree = cKDTree(rv)
    for k, name in enumerate(parts):
        sel = np.nonzero(tp == k)[0]
        if not len(sel):
            continue
        axes = AXES[name]
        X_ = Xp[sel]
        # assign each texel to its nearest axis of this part
        D = np.stack([axis_coords(X_, ax[1])[0] for ax in axes], 1)
        own = D.argmin(1)
        for ai, (kind, poly, rad) in enumerate(axes):
            m = own == ai
            if not m.any():
                continue
            idx = sel[m]
            dist, th, al = axis_coords(Xp[idx], poly)
            seed = k * 31 + ai * 7 + 3
            if kind == 'straw':
                sw = 0.0066 if rad > 0.02 else (0.0056 if rad > 0.01 else 0.0046)   # strand width, concept ~1 cm
                n = max(7, int(round(2 * math.pi * rad / sw)))
                thw = th + 0.09 * np.sin(al * 95 + seed) + 0.05 * np.sin(al * 260 + seed * 1.7)
                f = (thw + math.pi) / (2 * math.pi) * n
                strand = np.floor(f).astype(np.int64) % n
                seg = np.floor(al / 0.032 + h(strand, seed + 1) * 3).astype(np.int64)
                alt = ((strand + (h(strand, seed + 11) > 0.9)) % 2).astype(float)   # light/dark rhythm like the concept
                u = 0.5 * alt + 0.42 * h(strand, seed) + 0.08 * h(strand, seg, seed + 2)
                u = np.where(alt > 0, u * 0.8 + 0.2, u * 0.85 + 0.08)        # keep the darkest/lightest for accents
                c = STRAW[np.clip((u * len(STRAW)).astype(int), 0, len(STRAW) - 1)].copy()   # dark half / light half
                dark = h(strand, seg, seed + 5) < 0.05                               # a few deep shadowed strands
                c[dark] = STRAW[1]
                frac = f - np.floor(f)
                c *= (0.9 + 0.08 * np.sin(np.pi * frac))[:, None]               # strands round off at their edges
                c *= (0.95 + 0.1 * h(strand, np.floor(al / 0.0035).astype(np.int64), seed + 9))[:, None]  # brush marks
                if name.startswith(('hand', 'foot')):
                    L = np.linalg.norm(np.subtract(poly[1], poly[0]))
                    c *= (0.92 + 0.14 * np.clip(al / L, 0, 1))[:, None]           # cut ends a bit lighter
                # straw is darker where it is gathered under a rope binding
                dr, _ = rope_tree.query(Xp[idx])
                c *= (0.66 + 0.34 * np.clip((dr - 0.0012) / 0.006, 0, 1))[:, None]
            else:
                circ = 2 * math.pi * rad
                if kind == 'band':
                    hh = np.linalg.norm(np.subtract(poly[1], poly[0])) / 2
                    t = al - hh
                    wrap = (t > 0).astype(np.int64)
                    nply = max(5, int(round(circ / 0.0105)))
                    f = th / (2 * math.pi) * nply + t / 0.0105 * (1 if True else -1) + wrap * 0.5
                    edge = np.abs(np.abs(t) / hh - 0.5) * 2         # 0 at the middle of a wrap, 1 at groove/edge
                else:
                    nply = 2
                    f = th / (2 * math.pi) * nply + al / 0.010
                    edge = np.abs(th) / math.pi * 0.9                # lit on the front, darker underneath
                fr = f - np.floor(f)
                ply = np.floor(f).astype(np.int64)
                lit = (1 - np.clip(edge, 0, 1)) ** 1.3                      # each wrap is lit along its middle
                lo, hi = ROPE[1] * ROPE_BOOST, ROPE[3] * ROPE_BOOST * 1.06
                c = lo[None] * (1 - lit[:, None]) + hi[None] * lit[:, None]
                c *= (0.95 + 0.1 * h(ply, seed))[:, None]
                g = fr < 0.24
                c[g] = c[g] * 0.45 + ROPE[0] * 0.55                         # twist grooves
                c *= (1.0 - 0.25 * np.clip(edge - 0.7, 0, 1) / 0.3)[:, None]
            col[idx] = c
    tex = np.zeros((size, size, 3), np.float32)
    have = np.zeros((size, size), bool)
    tex[ys, xs] = col; have[ys, xs] = True
    _, inds = ndimage.distance_transform_edt(~have, return_indices=True)
    tex = tex[inds[0], inds[1]]
    return np.clip(tex, 0, 255).astype(np.uint8)

from scipy import ndimage
from PIL import Image
tex = paint(obj, size, tex)
tex = np.array(Image.fromarray(tex).quantize(colors=48, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).convert('RGB'))
cv2.imwrite(tex_path, cv2.cvtColor(tex, cv2.COLOR_RGB2BGR))
R.apply_texture(obj, tex_path, ID)
stats = check_fit(obj)
stats['texture'] = f'{size}x{size}'
_v = np.array([v.co[:] for v in obj.data.vertices])
_feet = _v[_v[:, 2] < 0.0005][:, :2]
_P, _, _, _ = BK.mesh_arrays(obj)
_vol = np.einsum('ij,ij->i', _P[:, 0], np.cross(_P[:, 1], _P[:, 2]))
_com = ((_P.sum(1) / 4) * _vol[:, None]).sum(0) / _vol.sum()          # approx volume centroid (overlapping shells)
stats['stand'] = {'com_xy': [round(float(c), 4) for c in _com[:2]], 'com_z': round(float(_com[2]), 4),
                  'contact_x': [round(float(_feet[:, 0].min()), 4), round(float(_feet[:, 0].max()), 4)],
                  'contact_y': [round(float(_feet[:, 1].min()), 4), round(float(_feet[:, 1].max()), 4)]}
print('stand', stats['stand'])
stats['iou'] = round(BK.align_report(obj, CONCEPT, cam), 3)
if '--no-render' in sys.argv:
    sys.exit()
R.render_views(obj, os.path.join(OUT, 'preview.png'), concept_cam=cam, concept_size=(1536, 1024), concept_path=CONCEPT)
R.shelf_fit_render(obj, os.path.join(OUT, 'shelf.png'))
R.export(obj, OUT, ID, tex_path)
json.dump({'id': ID, 'stats': stats, 'bake': st, 'camera': cam}, open(os.path.join(OUT, 'stats.json'), 'w'), indent=1)
R.card(OUT, ID, stats)
print('done', stats)
