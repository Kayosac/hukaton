"""Side concepts without the fly (the fly is painted on the front and back tables only, so it appears once per side and
is never doubled onto a side facet in 3/4 views). Fly = low-saturation / dark blobs in a box -> convex hull -> inpaint,
then the blotchy brush texture is restored with the high-pass of a nearby fly-free amber patch."""
import cv2, numpy as np

# y0, y1, x0, x1 of the fly; donor offset (dy, dx) of a fly-free amber patch of the same view
# y0, y1, x0, x1 of the fly; (y, x) corner of a 48x48 fly-free amber patch whose high-pass is tiled over the hole
BOX = {'left': ((178, 352, 296, 418), (250, 228)), 'right': ((182, 352, 205, 322), (250, 228))}


def clean(src, dst, box, donor, s_thr=0.60, dbg=None):
    im = cv2.imread(src, cv2.IMREAD_UNCHANGED)
    bgr = im[:, :, :3].copy()
    S = cv2.cvtColor(bgr, cv2.COLOR_BGR2HSV)[:, :, 1].astype(np.float32) / 255.0
    L = cv2.cvtColor(bgr, cv2.COLOR_BGR2LAB)[:, :, 0].astype(np.float32)
    y0, y1, x0, x1 = box
    m = np.zeros(S.shape, np.uint8)
    sub = ((S[y0:y1, x0:x1] < s_thr) | (L[y0:y1, x0:x1] < 106)) & (im[y0:y1, x0:x1, 3] > 128)
    m[y0:y1, x0:x1] = sub.astype(np.uint8)
    m = cv2.morphologyEx(m, cv2.MORPH_OPEN, np.ones((2, 2), np.uint8))
    n, lab, st, cen = cv2.connectedComponentsWithStats(m)
    pts = []
    for i in range(1, n):
        cx, cy = cen[i]
        if st[i, cv2.CC_STAT_AREA] > 25 and x0 + 8 < cx < x1 - 8 and y0 + 8 < cy < y1 - 8:
            ys, xs = np.nonzero(lab == i)
            pts.append(np.stack([xs, ys], 1))
    keep = np.zeros_like(m)
    hull = cv2.convexHull(np.concatenate(pts).astype(np.int32))
    cv2.fillConvexPoly(keep, hull, 1)
    keep = cv2.dilate(keep, np.ones((7, 7), np.uint8))
    out = cv2.inpaint(bgr, keep, 9, cv2.INPAINT_TELEA).astype(np.float32)
    py, px = donor
    # donor patch: always the clean blotchy amber of the LEFT view (the right view has no edge-free patch)
    f = cv2.imread(src.replace('_right', '_left'))[:, :, :3].astype(np.float32)
    hp = (f - cv2.GaussianBlur(f, (0, 0), 5))[py:py + 48, px:px + 48]
    tile = np.concatenate([hp, hp[:, ::-1]], 1)
    tile = np.concatenate([tile, tile[::-1]], 0)
    reps = (S.shape[0] // 96 + 1, S.shape[1] // 96 + 1, 1)
    big = np.roll(np.tile(tile, reps), (y0 % 96, x0 % 96), (0, 1))[:S.shape[0], :S.shape[1]]
    w = cv2.GaussianBlur(keep.astype(np.float32), (0, 0), 2)[..., None]
    out = out + big * w
    out = np.clip(out, 0, 255).astype(np.uint8)
    cv2.imwrite(dst, np.dstack([out, im[:, :, 3]]))
    if dbg:
        v = bgr.copy(); v[keep > 0] = (v[keep > 0] * 0.5 + np.array([0, 0, 128])).astype(np.uint8)
        crop = np.concatenate([v[y0 - 20:y1 + 20, x0 - 20:x1 + 20], out[y0 - 20:y1 + 20, x0 - 20:x1 + 20]], 1)
        cv2.imwrite(dbg, cv2.resize(crop, None, fx=3, fy=3, interpolation=cv2.INTER_NEAREST))
    return dst


def clean_all(prefix, out_dir, dbg_dir=None):
    res = {}
    for v, (b, d) in BOX.items():
        res[v] = clean(f'{prefix}_{v}.png', f'{out_dir}/concept_nofly_{v}.png', b, d,
                       dbg=f'{dbg_dir}/dbg_{v}.png' if dbg_dir else None)
    return res


if __name__ == '__main__':
    clean_all('/home/claude/concepts/22-amber-fly', '/tmp/a22', '/tmp/a22')
