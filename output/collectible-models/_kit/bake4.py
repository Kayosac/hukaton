"""Multi-view projection bake for FOUR-VIEW concept sheets (front / back / left / right).

The new concept sheets (items 20+) show the same object from 4 sides, so the hidden sides do not need to be invented:
every texel is taken from the concept view that sees it most head-on.

    import bake4 as B4
    views = B4.four_views('/home/claude/concepts/20-button-drum', cams)   # cams: {'front': cam, 'back': cam, ...}
    tex, stats = B4.bake_multi(obj, views, size=512, out_png=..., debug_png=...)

A view is a dict: {'name', 'concept' (RGBA png path), 'cam' (cam.py dict), 'T' (4x4, optional), 'parts' (allowed part
names or None), 'bonus' (score bonus, optional), 'gain' (rgb multiplier applied to that view's colours, optional)}.

Cameras: front az=0 (camera at -Y), back az=180, left az=-90 (camera at -X: the item's front appears on the RIGHT of the
picture), right az=+90. All sheets: elevation ~12 deg, long lens. The sheets are NOT calibrated: fit every view's
camera separately (optimize_view: dist/shift/small az), geometry must still be the compromise that fits all four.
"""
import math, json
import numpy as np
import cv2
from scipy import ndimage
import cam as C
import bake as BK

VIEW_AZ = {'front': 0.0, 'back': 180.0, 'left': -90.0, 'right': 90.0}


def default_cams(base):
    """base: cam dict for the front. Returns 4 cams with the azimuths of the sheet views."""
    out = {}
    for k, az in VIEW_AZ.items():
        c = json.loads(json.dumps(base))
        c['az'] = base.get('az', 0.0) + az
        out[k] = c
    return out


def four_views(prefix, cams, bonus=None, names=('front', 'back', 'left', 'right')):
    bonus = bonus or {}
    return [{'name': n, 'concept': f'{prefix}_{n}.png', 'cam': cams[n], 'T': np.eye(4), 'parts': None,
             'bonus': bonus.get(n, 0.0)} for n in names]


def align_all(obj, views, out_png=None, tile=420):
    """IoU per view + one overlay sheet (red = model only, blue = concept only)."""
    scores, tiles = {}, []
    for v in views:
        p = '/tmp/_b4_align.png'
        s = BK.align_report(obj, v['concept'], v['cam'], p if out_png else None)
        scores[v['name']] = round(s, 4)
        if out_png:
            im = cv2.imread(p)
            im = cv2.resize(im, (tile, int(im.shape[0] * tile / im.shape[1])))
            cv2.putText(im, v['name'], (10, im.shape[0] - 14), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)
            tiles.append(im)
    if out_png:
        cv2.imwrite(out_png, np.concatenate(tiles, 1))
    scores['mean'] = round(float(np.mean([scores[v['name']] for v in views])), 4)
    return scores


def optimize_views(obj, views, keys=('dist', 'shift'), iters=4):
    """Fit every view's camera on its own concept (in place). Returns IoU per view."""
    res = {}
    for v in views:
        v['cam'], s = BK.optimize_camera(obj, v['concept'], v['cam'], keys=keys, iters=iters)
        res[v['name']] = round(s, 4)
    return res


def _texels(obj, size):
    P, UV, part, N = BK.mesh_arrays(obj)
    tri = np.full((size, size), -1, np.int32)
    uvp = UV * size
    uvp[..., 1] = size - uvp[..., 1]
    for t in range(len(P)):
        cv2.fillPoly(tri, [np.round(uvp[t] * 16).astype(np.int32)], int(t), lineType=cv2.LINE_8, shift=4)
    ys, xs = np.nonzero(tri >= 0)
    t = tri[ys, xs]
    a0, a1, a2 = uvp[t, 0], uvp[t, 1], uvp[t, 2]
    px, py = xs + 0.5, ys + 0.5
    den = (a1[:, 1] - a2[:, 1]) * (a0[:, 0] - a2[:, 0]) + (a2[:, 0] - a1[:, 0]) * (a0[:, 1] - a2[:, 1])
    den = np.where(np.abs(den) < 1e-12, 1e-12, den)
    l0 = ((a1[:, 1] - a2[:, 1]) * (px - a2[:, 0]) + (a2[:, 0] - a1[:, 0]) * (py - a2[:, 1])) / den
    l1 = ((a2[:, 1] - a0[:, 1]) * (px - a2[:, 0]) + (a0[:, 0] - a2[:, 0]) * (py - a2[:, 1])) / den
    ok = (l0 > -0.04) & (l1 > -0.04) & (1 - l0 - l1 > -0.04)
    ys, xs, t, l0, l1 = ys[ok], xs[ok], t[ok], np.clip(l0[ok], 0, 1), np.clip(l1[ok], 0, 1)
    l2 = np.clip(1 - l0 - l1, 0, 1)
    X = P[t, 0] * l0[:, None] + P[t, 1] * l1[:, None] + P[t, 2] * l2[:, None]
    return P, part, N, ys, xs, t, X


def texel_points(obj, size):
    """Per texel: ys, xs, 3D position, face normal, part index (for painting fixes after the bake)."""
    P, part, N, ys, xs, t, X = _texels(obj, size)
    return ys, xs, X, N[t], part[t]


def bake_multi(obj, views, size=None, out_png=None, debug_png=None, part_colors=None, erode_px=2, min_facing=0.2,
               quantize=40, seed=3, fill_block=3, texel_blur=True, depth_tol=0.004, return_raw=False, near_fill=4):
    """Project several concept views onto obj's UVs; each texel takes the view that sees it most head-on
    (facing + bonus). Occlusion is a real depth test. Texels no view sees get blocky noise from the part's palette."""
    rng = np.random.default_rng(seed)
    parts = list(obj.get('parts', []))
    if size is None:
        size = BK.texture_size_for(obj)
    P, part, N, ys, xs, t, X = _texels(obj, size)
    Nn = N[t]
    tpart = part[t]
    tpm = size / math.sqrt(BK.surface_area(obj) / 0.62)
    best = np.full(len(X), -1.0)
    col = np.zeros((len(X), 3), np.float32)
    src_view = np.full(len(X), -1, np.int32)
    for vi, view in enumerate(views):
        rgb, cm = BK.load_concept(view['concept'])
        H, W = cm.shape
        cam = view['cam']
        T = np.asarray(view.get('T', np.eye(4)), float)
        Pt = P @ T[:3, :3].T + T[:3, 3]
        idb, zb = BK.rasterize(Pt, part, cam, W, H)
        own = np.where(idb >= 0, part[np.maximum(idb, 0)], -1)
        own[~cm] = -1
        if erode_px > 0:
            own_e = np.full_like(own, -1)
            for k in range(len(parts)):
                m = ndimage.binary_erosion(own == k, iterations=erode_px)
                own_e[m] = k
            own = own_e
        Xt = X @ T[:3, :3].T + T[:3, 3]
        Nt = Nn @ T[:3, :3].T
        Nt /= np.maximum(np.linalg.norm(Nt, axis=1, keepdims=True), 1e-12)
        u, v, z = C.project(Xt, cam, W, H)
        src = rgb.astype(np.float32)
        if texel_blur:
            bigpx = max(W, H) / (2 * math.tan(math.radians(cam['fov']) / 2))
            ppm = bigpx / max(np.median(z), 1e-3)
            sigma = max(0.0, 0.42 * ppm / tpm)
            if sigma > 0.6:
                src = cv2.GaussianBlur(src, (0, 0), sigma)
        if 'gain' in view:
            src = src * np.asarray(view['gain'], np.float32)
        ui, vi_ = np.floor(u).astype(int), np.floor(v).astype(int)
        ok = (ui >= 0) & (ui < W) & (vi_ >= 0) & (vi_ < H)
        uic, vic = np.clip(ui, 0, W - 1), np.clip(vi_, 0, H - 1)
        hit = idb[vic, uic]
        ok &= hit >= 0
        ok &= part[np.maximum(hit, 0)] == tpart
        ok &= np.abs(zb[vic, uic] - z) < depth_tol + 0.002 * np.abs(z)
        ok &= own[vic, uic] == tpart
        facing = np.einsum('ij,ij->i', Nt, C.view_dir(Xt, cam))
        ok &= facing > min_facing
        if view.get('parts') is not None:
            ok &= np.isin(tpart, [parts.index(p) for p in view['parts'] if p in parts])
        sc = facing + view.get('bonus', 0.0)
        upd = ok & (sc > best)
        if upd.any():
            uu, vv = u[upd] - 0.5, v[upd] - 0.5
            x0 = np.clip(np.floor(uu).astype(int), 0, W - 2); y0 = np.clip(np.floor(vv).astype(int), 0, H - 2)
            fx, fy = (uu - x0)[:, None], (vv - y0)[:, None]
            s = (src[y0, x0] * (1 - fx) * (1 - fy) + src[y0, x0 + 1] * fx * (1 - fy) +
                 src[y0 + 1, x0] * (1 - fx) * fy + src[y0 + 1, x0 + 1] * fx * fy)
            col[upd] = s
            best[upd] = sc[upd]
            src_view[upd] = vi
    covered = src_view >= 0
    tex = np.zeros((size, size, 3), np.float32)
    have = np.zeros((size, size), bool)
    tex[ys[covered], xs[covered]] = col[covered]
    have[ys[covered], xs[covered]] = True
    gh = size // fill_block + 2
    noise = cv2.GaussianBlur(rng.random((gh, gh)).astype(np.float32), (0, 0), 0.8)
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
            pal, w = BK._kmeans_palette(col[sel & covered], 6)
            if w is None:
                w = np.ones(len(pal)) / len(pal)
        lum = pal @ np.array([0.3, 0.59, 0.11])
        o = np.argsort(lum); pal, w = pal[o], np.asarray(w)[o]
        cdf = np.cumsum(w) / np.sum(w)
        miss = sel & ~covered
        if miss.any() and near_fill and (sel & covered).any():
            # thin unseen bands (part borders eroded in the concept, grazing strips): copy the nearest seen texel of
            # the same part instead of palette noise (noise draws dark seams along part borders)
            pm = np.zeros((size, size), bool)
            pm[ys[sel & covered], xs[sel & covered]] = True
            d, inds = ndimage.distance_transform_edt(~pm, return_indices=True)
            mys, mxs = ys[miss], xs[miss]
            close = d[mys, mxs] <= near_fill
            if close.any():
                tex[mys[close], mxs[close]] = tex[inds[0][mys[close], mxs[close]], inds[1][mys[close], mxs[close]]]
                have[mys[close], mxs[close]] = True
                mi = np.nonzero(miss)[0][close]
                miss = miss.copy(); miss[mi] = False
        if miss.any():
            idx = np.searchsorted(cdf, noise[ys[miss], xs[miss]] * 0.999)
            tex[ys[miss], xs[miss]] = pal[np.clip(idx, 0, len(pal) - 1)]
            have[ys[miss], xs[miss]] = True
        stats['parts'][pname] = {'texels': int(sel.sum()), 'from_concept': round(float((sel & covered).sum() / sel.sum()), 3)}
    if (~have).any():
        _, inds = ndimage.distance_transform_edt(~have, return_indices=True)
        tex = tex[inds[0], inds[1]]
    tex = np.clip(tex, 0, 255).astype(np.uint8)
    raw = tex.copy()
    if quantize:
        tex = BK._quantize(tex, quantize)
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
    if return_raw:
        return tex, stats, raw
    return tex, stats
