"""Concept -> texture projection bake, silhouette alignment and UV unwrap.

Workflow (see kit/README.md):
  1. build mesh with geo.Builder (parts!)          2. unwrap(obj)
  3. cam = {...}; align_report(obj, concept, cam, 'align.png')  -> IoU + overlay; or optimize_camera(...)
  4. tex = bake(obj, concept, cam, views=[...], size=256)       -> texture png + coverage debug
"""
import math, json
import numpy as np
import cv2
from scipy import ndimage
import cam as C

# ---------------------------------------------------------------- mesh data
def mesh_arrays(obj, transform=None):
    """Triangles of obj: P (T,3,3) world positions, UV (T,3,2), part (T,), N (T,3) face normals."""
    me = obj.data
    me.calc_loop_triangles()
    mw = np.array(obj.matrix_world)
    V = np.array([v.co[:] for v in me.vertices])
    V = (np.c_[V, np.ones(len(V))] @ mw.T)[:, :3]
    tris = me.loop_triangles
    idx = np.array([t.vertices[:] for t in tris])
    loops = np.array([t.loops[:] for t in tris])
    P = V[idx]
    if me.uv_layers.active:
        uvd = me.uv_layers.active.data
        UVall = np.array([uvd[i].uv[:] for i in range(len(uvd))])
        UV = UVall[loops]
    else:
        UV = np.zeros((len(tris), 3, 2))
    part = np.array([t.material_index for t in tris])
    if transform is not None:
        P = P @ np.asarray(transform)[:3, :3].T + np.asarray(transform)[:3, 3]
    N = np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0])
    N /= np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-12)
    return P, UV, part, N

# ---------------------------------------------------------------- unwrap
def unwrap(obj, margin=0.012, angle=50):
    import bpy
    bpy.context.view_layer.objects.active = obj
    for o in bpy.context.view_layer.objects:
        o.select_set(o == obj)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(angle), island_margin=margin, area_weight=0.0,
                             correct_aspect=True, scale_to_bounds=False)
    try:
        bpy.ops.uv.pack_islands(rotate=True, margin=margin)
    except Exception:
        pass
    bpy.ops.object.mode_set(mode='OBJECT')

def surface_area(obj):
    P, _, _, _ = mesh_arrays(obj)
    return float(0.5 * np.linalg.norm(np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0]), axis=1).sum())

def texture_size_for(obj, density=640, efficiency=0.62):
    """Smallest power of two giving >= `density` texels per metre (consistent pixel size across items)."""
    need = math.sqrt(surface_area(obj) * density ** 2 / efficiency)
    s = 64
    while s < need and s < 1024:
        s *= 2
    return s

# ---------------------------------------------------------------- concept image
def load_concept(path):
    im = cv2.imread(path, cv2.IMREAD_UNCHANGED)
    if im.ndim == 2:
        im = cv2.cvtColor(im, cv2.COLOR_GRAY2BGR)
    if im.shape[2] == 4:
        alpha = im[:, :, 3]
        rgb = cv2.cvtColor(im[:, :, :3], cv2.COLOR_BGR2RGB)
        mask = alpha > 128
    else:
        rgb = cv2.cvtColor(im, cv2.COLOR_BGR2RGB)
        mask = None
    if mask is None or mask.all():
        mask = concept_mask(rgb)
    return rgb, mask

def concept_mask(rgb, thr=16):
    lab = cv2.cvtColor(rgb, cv2.COLOR_RGB2LAB).astype(np.float32)
    b = 12
    border = np.concatenate([lab[:b].reshape(-1, 3), lab[-b:].reshape(-1, 3), lab[:, :b].reshape(-1, 3), lab[:, -b:].reshape(-1, 3)])
    bg = np.median(border, axis=0)
    # background can be a soft gradient: also compare against a heavily blurred background estimate
    d = np.linalg.norm(lab - bg, axis=2)
    m = d > thr
    m = ndimage.binary_opening(m, iterations=2)
    lab_, n = ndimage.label(m)
    if n > 1:
        sizes = ndimage.sum(m, lab_, range(1, n + 1))
        keep = np.argsort(sizes)[::-1]
        big = sizes[keep[0]]
        m = np.isin(lab_, [k + 1 for k in keep if sizes[k] > big * 0.02])
    m = ndimage.binary_closing(m, iterations=3)
    # fill only small holes (texture speckles); keep real openings such as a handle loop
    holes = ndimage.binary_fill_holes(m) & ~m
    hl, hn = ndimage.label(holes)
    if hn:
        hs = ndimage.sum(holes, hl, range(1, hn + 1))
        small = [i + 1 for i, s in enumerate(hs) if s < 0.002 * m.sum()]
        m |= np.isin(hl, small)
    return m

# ---------------------------------------------------------------- rasterization
def rasterize(P, part, cam, W, H):
    """Painter's ID buffer (tri index, -1 empty) and perspective-correct depth buffer."""
    u, v, z = C.project(P.reshape(-1, 3), cam, W, H)
    u, v, z = u.reshape(-1, 3), v.reshape(-1, 3), z.reshape(-1, 3)
    order = np.argsort(-z.mean(1))
    idb = np.full((H, W), -1, np.int32)
    tmp = np.zeros((H, W), np.int32)
    for t in order:
        if (z[t] <= 0).any():
            continue
        pts = np.stack([u[t], v[t]], 1)
        pts = np.round(pts * 16).astype(np.int32)
        cv2.fillPoly(idb, [pts], int(t), lineType=cv2.LINE_8, shift=4)
    # depth
    zb = np.full((H, W), np.inf, np.float32)
    ys, xs = np.nonzero(idb >= 0)
    if len(ys):
        t = idb[ys, xs]
        px, py = xs + 0.5, ys + 0.5
        u0, v0, u1, v1, u2, v2 = u[t, 0], v[t, 0], u[t, 1], v[t, 1], u[t, 2], v[t, 2]
        den = (v1 - v2) * (u0 - u2) + (u2 - u1) * (v0 - v2)
        den = np.where(np.abs(den) < 1e-9, 1e-9, den)
        a = ((v1 - v2) * (px - u2) + (u2 - u1) * (py - v2)) / den
        b = ((v2 - v0) * (px - u2) + (u0 - u2) * (py - v2)) / den
        c = 1 - a - b
        iz = a / z[t, 0] + b / z[t, 1] + c / z[t, 2]
        zb[ys, xs] = 1 / np.maximum(iz, 1e-9)
    return idb, zb

def model_mask(obj, cam, W, H):
    P, UV, part, N = mesh_arrays(obj)
    idb, _ = rasterize(P, part, cam, W, H)
    return idb >= 0

# ---------------------------------------------------------------- alignment
def iou(a, b):
    return float((a & b).sum() / max((a | b).sum(), 1))

def align_report(obj, concept_path, cam, out_png=None):
    rgb, cm = load_concept(concept_path)
    H, W = cm.shape
    mm = model_mask(obj, cam, W, H)
    score = iou(mm, cm)
    if out_png:
        vis = (rgb * 0.55).astype(np.uint8)
        vis[mm & ~cm] = (vis[mm & ~cm] * 0.4 + np.array([255, 40, 40]) * 0.6).astype(np.uint8)   # model only: red
        vis[cm & ~mm] = (vis[cm & ~mm] * 0.4 + np.array([40, 120, 255]) * 0.6).astype(np.uint8)  # concept only: blue
        cv2.putText(vis, f'IoU {score:.3f}  red=model only  blue=concept only', (20, 40), cv2.FONT_HERSHEY_SIMPLEX, 1.0, (255, 255, 255), 2)
        cv2.imwrite(out_png, cv2.cvtColor(vis, cv2.COLOR_RGB2BGR))
    return score

def optimize_camera(obj, concept_path, cam, keys=('az', 'dist', 'shift'), iters=4, scale=0.25):
    """Coordinate descent on silhouette IoU at quarter resolution. Returns (cam, iou)."""
    rgb, cm = load_concept(concept_path)
    H, W = cm.shape
    h, w = int(H * scale), int(W * scale)
    cms = cv2.resize(cm.astype(np.uint8), (w, h), interpolation=cv2.INTER_NEAREST).astype(bool)
    P, UV, part, N = mesh_arrays(obj)
    def score(c):
        idb, _ = rasterize(P, part, c, w, h)
        return iou(idb >= 0, cms)
    cam = json.loads(json.dumps(cam))
    best = score(cam)
    steps = {'az': 6.0, 'el': 5.0, 'dist': cam['dist'] * 0.08, 'roll': 3.0, 'shift0': 0.02, 'shift1': 0.02}
    for it in range(iters):
        for k in keys:
            subs = ['shift0', 'shift1'] if k == 'shift' else [k]
            for s in subs:
                improved = True
                while improved:
                    improved = False
                    for sign in (1, -1):
                        c2 = json.loads(json.dumps(cam))
                        if s.startswith('shift'):
                            c2.setdefault('shift', [0, 0])[int(s[-1])] += sign * steps[s]
                        else:
                            c2[s] += sign * steps[s]
                        sc = score(c2)
                        if sc > best + 1e-4:
                            cam, best, improved = c2, sc, True
                            break
        for s in steps:
            steps[s] *= 0.5
    return cam, best

# ---------------------------------------------------------------- views
def T_mirror_x():
    return np.diag([-1.0, 1, 1, 1])

def T_rot_z(deg, center=(0, 0)):
    a = math.radians(deg)
    T = np.eye(4)
    T[:2, :2] = [[math.cos(a), -math.sin(a)], [math.sin(a), math.cos(a)]]
    cx, cy = center
    T[:2, 3] = np.array([cx, cy]) - T[:2, :2] @ np.array([cx, cy])
    return T

def T_mirror_plane(axis='x', at=0.0):
    T = np.eye(4)
    i = 'xyz'.index(axis)
    T[i, i] = -1
    T[i, 3] = 2 * at
    return T

# ---------------------------------------------------------------- bake
def _kmeans_palette(cols, k=6):
    cols = np.asarray(cols, np.float32)
    if len(cols) < k * 4:
        return np.clip(cols.reshape(-1, 3)[:max(1, len(cols))], 0, 255) if len(cols) else np.array([[128, 110, 90]], np.float32), None
    crit = (cv2.TERM_CRITERIA_EPS + cv2.TERM_CRITERIA_MAX_ITER, 20, 1.0)
    _, lab, cen = cv2.kmeans(cols, k, None, crit, 2, cv2.KMEANS_PP_CENTERS)
    w = np.bincount(lab.ravel(), minlength=k) / len(lab)
    return cen, w

def bake(obj, concept_path, cam, views=None, size=None, out_png=None, debug_png=None, part_colors=None,
         erode_px=3, min_facing=0.3, quantize=40, seed=3, fill_block=3, texel_blur=True):
    """Project the concept onto obj's UV texture.

    views: list of dicts {'name', 'T' (4x4 mesh transform applied before viewing with `cam`), 'parts' (list of part
           names allowed, or None for all), 'bonus' (score bonus)}. Default: original + mirror across x=0.
    part_colors: {part: [(r,g,b), ...]} overrides the fill palette of hidden texels for that part
                 (default = k-means of that part's projected colours).
    Returns (texture HxWx3 uint8, coverage stats).
    """
    rng = np.random.default_rng(seed)
    parts = list(obj.get('parts', []))
    rgb, cm = load_concept(concept_path)
    H, W = cm.shape
    if size is None:
        size = texture_size_for(obj)
    if views is None:
        views = [{'name': 'orig', 'T': np.eye(4), 'parts': None, 'bonus': 0.25},
                 {'name': 'mirror_x', 'T': T_mirror_x(), 'parts': None, 'bonus': 0.0}]
    P, UV, part, N = mesh_arrays(obj)

    # texel -> (tri, barycentric) via UV-space rasterization
    tri_img = np.full((size, size), -1, np.int32)
    uvp = UV * size
    uvp[..., 1] = size - uvp[..., 1]
    for t in range(len(P)):
        pts = np.round(uvp[t] * 16).astype(np.int32)
        cv2.fillPoly(tri_img, [pts], int(t), lineType=cv2.LINE_8, shift=4)
    ys, xs = np.nonzero(tri_img >= 0)
    t = tri_img[ys, xs]
    px, py = xs + 0.5, ys + 0.5
    a0, a1, a2 = uvp[t, 0], uvp[t, 1], uvp[t, 2]
    den = (a1[:, 1] - a2[:, 1]) * (a0[:, 0] - a2[:, 0]) + (a2[:, 0] - a1[:, 0]) * (a0[:, 1] - a2[:, 1])
    den = np.where(np.abs(den) < 1e-12, 1e-12, den)
    l0 = ((a1[:, 1] - a2[:, 1]) * (px - a2[:, 0]) + (a2[:, 0] - a1[:, 0]) * (py - a2[:, 1])) / den
    l1 = ((a2[:, 1] - a0[:, 1]) * (px - a2[:, 0]) + (a0[:, 0] - a2[:, 0]) * (py - a2[:, 1])) / den
    # texels that fillPoly put on a triangle edge but lie outside it: drop them (filled later by dilation),
    # otherwise they sample the concept at a wrong 3D point and draw dotted lines along triangle edges
    inside = (l0 > -0.04) & (l1 > -0.04) & (1 - l0 - l1 > -0.04)
    ys, xs, t, l0, l1 = ys[inside], xs[inside], t[inside], l0[inside], l1[inside]
    l0, l1 = np.clip(l0, 0, 1), np.clip(l1, 0, 1)
    l2 = np.clip(1 - l0 - l1, 0, 1)
    X = P[t, 0] * l0[:, None] + P[t, 1] * l1[:, None] + P[t, 2] * l2[:, None]
    Nn = N[t]
    tpart = part[t]

    # pre-filter concept to roughly the texel footprint
    src = rgb.astype(np.float32)
    if texel_blur:
        u0, v0, z0 = C.project(X, cam, W, H)
        bigpx = max(W, H) / (2 * math.tan(math.radians(cam['fov']) / 2))
        ppm = bigpx / max(np.median(z0), 1e-3)          # concept pixels per metre
        tpm = size / math.sqrt(surface_area(obj) / 0.62)  # texels per metre
        sigma = max(0.0, 0.42 * ppm / tpm)
        if sigma > 0.6:
            src = cv2.GaussianBlur(src, (0, 0), sigma)

    best = np.full(len(X), -1.0)
    col = np.zeros((len(X), 3), np.float32)
    src_view = np.full(len(X), -1, np.int32)
    # original-view ownership: which part the concept pixel shows (from the model rendered with cam)
    idb0, zb0 = rasterize(P, part, cam, W, H)
    own = np.where(idb0 >= 0, part[np.maximum(idb0, 0)], -1)
    own[~cm] = -1
    if erode_px > 0:
        own_e = np.full_like(own, -1)
        for k in range(len(parts)):
            m = ndimage.binary_erosion(own == k, iterations=erode_px)
            own_e[m] = k
        own = own_e
    for vi, view in enumerate(views):
        T = np.asarray(view.get('T', np.eye(4)), float)
        allow = view.get('parts')
        Pt = P @ T[:3, :3].T + T[:3, 3]
        idb, zb = rasterize(Pt, part, cam, W, H) if vi or not np.allclose(T, np.eye(4)) else (idb0, zb0)
        Xt = X @ T[:3, :3].T + T[:3, 3]
        Nt = Nn @ T[:3, :3].T
        Nt /= np.maximum(np.linalg.norm(Nt, axis=1, keepdims=True), 1e-12)
        u, v, z = C.project(Xt, cam, W, H)
        ui, vi_ = np.floor(u).astype(int), np.floor(v).astype(int)
        inb = (ui >= 0) & (ui < W) & (vi_ >= 0) & (vi_ < H)
        ok = inb.copy()
        uic, vic = np.clip(ui, 0, W - 1), np.clip(vi_, 0, H - 1)
        hit = idb[vic, uic]
        ok &= hit >= 0
        ok &= part[np.maximum(hit, 0)] == tpart
        ok &= np.abs(zb[vic, uic] - z) < 0.006 + 0.02 * np.abs(z) * 0.05
        ok &= own[vic, uic] == tpart
        facing = np.einsum('ij,ij->i', Nt, C.view_dir(Xt, cam))
        ok &= facing > min_facing
        if allow is not None:
            allowed_ids = [parts.index(p) for p in allow if p in parts]
            ok &= np.isin(tpart, allowed_ids)
        sc = facing + view.get('bonus', 0.0)
        upd = ok & (sc > best)
        if upd.any():
            # bilinear sample
            uu, vv = u[upd] - 0.5, v[upd] - 0.5
            x0 = np.clip(np.floor(uu).astype(int), 0, W - 2); y0 = np.clip(np.floor(vv).astype(int), 0, H - 2)
            fx, fy = (uu - x0)[:, None], (vv - y0)[:, None]
            s = (src[y0, x0] * (1 - fx) * (1 - fy) + src[y0, x0 + 1] * fx * (1 - fy) +
                 src[y0 + 1, x0] * (1 - fx) * fy + src[y0 + 1, x0 + 1] * fx * fy)
            col[upd] = s
            best[upd] = sc[upd]
            src_view[upd] = vi

    # fill hidden texels with the part's own palette as blocky noise
    covered = src_view >= 0
    tex = np.zeros((size, size, 3), np.float32)
    have = np.zeros((size, size), bool)
    tex[ys[covered], xs[covered]] = col[covered]
    have[ys[covered], xs[covered]] = True
    gh = size // fill_block + 2
    noise = rng.random((gh, gh))
    noise = cv2.GaussianBlur(noise.astype(np.float32), (0, 0), 0.8)
    noise = (noise - noise.min()) / max(np.ptp(noise), 1e-6)
    noise = np.kron(noise, np.ones((fill_block, fill_block)))[:size, :size]
    stats = {'texture_size': size, 'texels': int(len(X)), 'covered_from_concept': round(float(covered.mean()), 3), 'parts': {}}
    for k, pname in enumerate(parts):
        sel = tpart == k
        if not sel.any():
            continue
        if part_colors and pname in part_colors:
            pal = np.array(part_colors[pname], np.float32); w = np.ones(len(pal)) / len(pal)
        else:
            pal, w = _kmeans_palette(col[sel & covered], 6)
            if w is None:
                w = np.ones(len(pal)) / len(pal)
        # order palette by luminance, pick by noise value weighted by cluster sizes
        lum = pal @ np.array([0.3, 0.59, 0.11])
        o = np.argsort(lum); pal, w = pal[o], np.asarray(w)[o]
        cdf = np.cumsum(w) / np.sum(w)
        miss = sel & ~covered
        if miss.any():
            nv = noise[ys[miss], xs[miss]]
            idx = np.searchsorted(cdf, nv * 0.999)
            tex[ys[miss], xs[miss]] = pal[np.clip(idx, 0, len(pal) - 1)]
            have[ys[miss], xs[miss]] = True
        stats['parts'][pname] = {'texels': int(sel.sum()), 'from_concept': round(float((sel & covered).sum() / sel.sum()), 3)}
    # dilate into gutters
    if (~have).any():
        _, inds = ndimage.distance_transform_edt(~have, return_indices=True)
        tex = tex[inds[0], inds[1]]
    tex = np.clip(tex, 0, 255).astype(np.uint8)
    if quantize:
        pil_q = _quantize(tex, quantize)
        tex = pil_q
    if out_png:
        cv2.imwrite(out_png, cv2.cvtColor(tex, cv2.COLOR_RGB2BGR))
    if debug_png:
        dbg = np.zeros((size, size, 3), np.uint8)
        pal = np.array([[60, 200, 80], [60, 120, 255], [255, 200, 40], [220, 80, 220], [80, 220, 220], [255, 120, 60], [200, 200, 200], [120, 80, 40]])
        dbg[ys, xs] = [255, 40, 40]
        for vi in range(len(views)):
            m = src_view == vi
            dbg[ys[m], xs[m]] = pal[vi % len(pal)]
        cv2.imwrite(debug_png, cv2.cvtColor(dbg, cv2.COLOR_RGB2BGR))
    stats['views'] = {v['name']: int((src_view == i).sum()) for i, v in enumerate(views)}
    return tex, stats

def _quantize(tex, n):
    from PIL import Image
    im = Image.fromarray(tex).quantize(colors=n, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)
    return np.array(im.convert('RGB'))
