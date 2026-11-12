"""Hangover coin 3D model from the six HUD coin sprites (Assets/Content/UI/ShopHUD/HangoverCoinAtlas.png).

The sprites are generated views, so the shape is fitted to the two near-frontal ones (Coin_Y_000 = front,
Coin_Y_180 = back): a 12-sided prism with per-vertex radii, outer chamfer, raised rim and recessed field
(fit.json: silhouette overlap ~97 % on both). Textures are the sprites' own pixels re-projected through the
fitted views (front from 000, back from 180, side walls from whichever of the two sees them; hidden left
walls mirror the right ones), padded at the edges, in one 4096 texture. Meant for an unlit material: the
sprites' painted light is kept, so in their own poses the model reads 1:1.
Output: HangoverCoin.obj (diameter 1 unit; in Unity the face looks along -Z, the back along +Z), HangoverCoin_Albedo.png.
Run: python3 build_coin.py <path to HangoverCoinAtlas.png> <out dir>
"""
import json, os, sys
import numpy as np
from PIL import Image
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
atlas_path, out_dir = sys.argv[1], sys.argv[2]
fit = json.load(open(os.path.join(HERE, "fit.json")))
N = fit["N"]
rho = np.radians(fit["rho_deg"])
radii = np.array(fit["radii_px"])          # outer radius per vertex, sprite px
T = fit["T_px"]                            # thickness, sprite px
th = {"000": np.radians(fit["theta000_deg"]), "180": np.radians(fit["theta180_deg"])}
cen = {"000": fit["c000"], "180": fit["c180"]}
atlas = np.asarray(Image.open(atlas_path).convert("RGBA")).astype(np.float32)
cell = {"000": atlas[0:1254, 0:1254], "180": atlas[1254:2508, 0:1254]}

# ring profile (sprite px): outer chamfer, rim, inner step down to the recessed field
CHAMFER_R, CHAMFER_Z = 46.0, 34.0
RIM_W = 44.0
STEP_R, FIELD_D = 12.0, 14.0
Rmean = radii.mean()
k1 = 1 - CHAMFER_R / Rmean
k2 = k1 - RIM_W / Rmean
k3 = k2 - STEP_R / Rmean
RINGS = [(1.0, T / 2 - CHAMFER_Z), (k1, T / 2), (k2, T / 2), (k3, T / 2 - FIELD_D)]
ang = np.pi / 2 + np.pi / N + rho + np.arange(N) * 2 * np.pi / N
V = np.stack([radii * np.cos(ang), radii * np.sin(ang)], 1)          # outer polygon, face plane

def project(view, x, y, z):
    t = th[view]
    X = x * np.cos(t) + z * np.sin(t)
    return cen[view][0] + X, cen[view][1] - y

def sample(img, u, v):
    """Bilinear RGBA sample at float pixel coords."""
    h, w = img.shape[:2]
    u = np.clip(u, 0, w - 1.001); v = np.clip(v, 0, h - 1.001)
    u0 = np.floor(u).astype(int); v0 = np.floor(v).astype(int)
    fu = (u - u0)[..., None]; fv = (v - v0)[..., None]
    a = img[v0, u0]; b = img[v0, u0 + 1]; c = img[v0 + 1, u0]; d = img[v0 + 1, u0 + 1]
    return (a * (1 - fu) + b * fu) * (1 - fv) + (c * (1 - fu) + d * fu) * fv

def face_k(x, y):
    """Polygon-normalised radius: 1 on the outer polygon (per-sector linear interpolation of radii)."""
    a = np.arctan2(y, x)
    rel = np.mod(a - ang[0], 2 * np.pi) / (2 * np.pi / N)
    i = np.floor(rel).astype(int) % N
    f = rel - np.floor(rel)
    # distance to the edge between vertex i and i+1 along the ray (exact for a straight edge)
    p0 = V[i]; p1 = V[(i + 1) % N]
    d = np.stack([np.cos(a), np.sin(a)], -1)
    e = p1 - p0
    denom = d[..., 0] * e[..., 1] - d[..., 1] * e[..., 0]
    tray = (p0[..., 0] * e[..., 1] - p0[..., 1] * e[..., 0]) / np.where(np.abs(denom) < 1e-9, 1e-9, denom)
    r = np.hypot(x, y)
    return r / np.maximum(tray, 1e-6)

def face_z(k):
    z = np.full_like(k, T / 2 - FIELD_D)
    z = np.where(k >= k3, T / 2 - FIELD_D + (k - k3) / (k2 - k3) * FIELD_D, z)
    z = np.where(k >= k2, T / 2, z)
    z = np.where(k >= k1, T / 2 - (k - k1) / (1 - k1) * CHAMFER_Z, z)
    return z

def pad(rgba, valid, iterations=64):
    """Fill invalid texels from the nearest valid ones (edge padding)."""
    idx = ndimage.distance_transform_edt(~valid, return_distances=False, return_indices=True)
    out = rgba[idx[0], idx[1]]
    out[..., 3] = 255
    return out

# ---------------------------------------------------------------- textures
TEX = 4096
FACE = 2048
EXT = radii.max() * 1.03                     # face texture covers [-EXT, EXT]^2
albedo = np.zeros((TEX, TEX, 4), np.float32)

def face_texture(view, sign):
    g = (np.arange(FACE) + 0.5) / FACE * 2 * EXT - EXT
    x, y = np.meshgrid(g, -g)                # row 0 = top
    k = face_k(x, y)
    z = sign * face_z(k)
    xs = x if sign > 0 else x                # coin-local x (back seen mirrored by the view itself)
    u, v = project(view, xs, y, z)
    s = sample(cell[view], u, v)
    inside = k <= 1.0
    valid = inside & (s[..., 3] > 230)
    # keep only texels whose ray hits this face first (not the side band in front of it)
    return pad(s, valid), inside

front, fin = face_texture("000", +1)
back, bin_ = face_texture("180", -1)
albedo[0:FACE, 0:FACE] = front
albedo[0:FACE, FACE:2 * FACE] = back

# side walls: 12 quads, each a 340 x 512 cell in the bottom strip (u along the edge, v along the thickness)
SW, SH = 340, 512
side_y0 = FACE
normals = []
for i in range(N):
    p0, p1 = V[i], V[(i + 1) % N]
    e = p1 - p0
    n = np.array([e[1], -e[0]]); n /= np.linalg.norm(n)
    normals.append(n)
normals = np.array(normals)

def side_cell(i):
    """Sample quad i (or its mirror / nearest visible stand-in) from the view that sees it best."""
    n = normals[i]
    src = i
    if n[0] < 0.35:                          # left walls are never seen: mirror x; edge-on top/bottom: nearest good one
        mirror_n = np.array([-n[0], n[1]])
        cand = [j for j in range(N) if normals[j][0] >= 0.35]
        src = max(cand, key=lambda j: np.dot(normals[j], mirror_n))
    p0, p1 = V[src], V[(src + 1) % N]
    if src != i and n[0] < 0:                # mirrored stand-in runs the other way along the edge
        p0, p1 = p1, p0
    uu = (np.arange(SW) + 0.5) / SW
    vv = (np.arange(SH) + 0.5) / SH
    U, Vv = np.meshgrid(uu, vv)
    pts = p0[None, None, :] * (1 - U[..., None]) + p1[None, None, :] * U[..., None]
    zt = (T / 2 - CHAMFER_Z) * (1 - 2 * Vv)  # v=0 front edge, v=1 back edge
    best = None
    for view in ("000", "180"):
        t = th[view]
        facing = -normals[src][0] * np.sin(t)
        if best is None or facing > best[0]:
            best = (facing, view)
    view = best[1]
    u, v = project(view, pts[..., 0], pts[..., 1], zt)
    s = sample(cell[view], u, v)
    valid = s[..., 3] > 230
    return pad(s, valid) if valid.any() else s

for i in range(N):
    albedo[side_y0:side_y0 + SH, i * SW:(i + 1) * SW] = side_cell(i)
albedo[..., 3] = 255
Image.fromarray(np.clip(albedo, 0, 255).astype(np.uint8)).save(os.path.join(out_dir, "HangoverCoin_Albedo.png"), optimize=True)

# ---------------------------------------------------------------- mesh (OBJ, Unity-friendly: +Y up, +Z front)
S = 1.0 / (2 * Rmean * np.cos(np.pi / N))   # diameter (flat to flat, mean) = 1 unit
verts, uvs, faces = [], [], []
def vtx(p, uv):
    verts.append(p); uvs.append(uv); return len(verts)

def face_uv(x, y, col0):
    return ((col0 + (x + EXT) / (2 * EXT) * FACE) / TEX, 1 - ((EXT - y) / (2 * EXT) * FACE) / TEX)

for sign, col0 in ((+1, 0), (-1, FACE)):
    rings = []
    for k, z in RINGS:
        ring = []
        for j in range(N):
            x, y = V[j] * k
            ring.append(vtx((x * S, y * S, sign * z * S), face_uv(x, y, col0)))
        rings.append(ring)
    centre = vtx((0, 0, sign * (T / 2 - FIELD_D) * S), face_uv(0, 0, col0))
    for a, b in zip(rings[:-1], rings[1:]):
        for j in range(N):
            j2 = (j + 1) % N
            q = [a[j], a[j2], b[j2], b[j]]
            faces.append(q if sign > 0 else q[::-1])
    for j in range(N):
        tri = [rings[-1][j], rings[-1][(j + 1) % N], centre]
        faces.append(tri if sign > 0 else tri[::-1])

zt = (T / 2 - CHAMFER_Z)
for i in range(N):
    p0, p1 = V[i], V[(i + 1) % N]
    c0, r0 = i * SW, side_y0
    def suv(u, v): return ((c0 + u * SW) / TEX, 1 - (r0 + v * SH) / TEX)
    a = vtx((p0[0] * S, p0[1] * S, zt * S), suv(0.002, 0.002))
    b = vtx((p1[0] * S, p1[1] * S, zt * S), suv(0.998, 0.002))
    c = vtx((p1[0] * S, p1[1] * S, -zt * S), suv(0.998, 0.998))
    d = vtx((p0[0] * S, p0[1] * S, -zt * S), suv(0.002, 0.998))
    faces.append([a, d, c, b])

with open(os.path.join(out_dir, "HangoverCoin.obj"), "w") as f:
    f.write("# Hangover coin — fitted to the HUD coin sprites (see output/coin-model/build_coin.py)\n")
    f.write("o HangoverCoin\n")
    # model space: x right, y up, z toward the viewer of the front face (right-handed). Written rotated 180 deg
    # about Y; Unity's OBJ import then negates X, so in Unity the front faces -Z (towards a default camera) unmirrored.
    for p in verts: f.write("v %.6f %.6f %.6f\n" % (-p[0], p[1], -p[2]))
    for uv in uvs: f.write("vt %.6f %.6f\n" % uv)
    for fc in faces: f.write("f " + " ".join("%d/%d" % (i, i) for i in fc) + "\n")
print("verts", len(verts), "faces", len(faces), "diameter units", 2 * Rmean * np.cos(np.pi / N) * S, "thickness units", T * S)
