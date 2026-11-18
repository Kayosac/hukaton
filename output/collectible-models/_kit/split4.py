"""Split a 2x2 four-view concept sheet (front | back / left | right) into 4 RGBA concepts with a clean mask."""
import cv2, numpy as np, os, sys
from scipy import ndimage

def mask(rgb, thr=8, close=6):
    lab = cv2.cvtColor(rgb, cv2.COLOR_RGB2LAB).astype(np.float32)
    lab = cv2.GaussianBlur(lab, (0, 0), 1.2)
    b = 10
    border = np.concatenate([lab[:b].reshape(-1, 3), lab[-b:].reshape(-1, 3), lab[:, :b].reshape(-1, 3), lab[:, -b:].reshape(-1, 3)])
    bg = np.median(border, axis=0)
    m = np.linalg.norm(lab - bg, axis=2) > thr
    m = ndimage.binary_opening(m, iterations=1)
    l, n = ndimage.label(m)
    if n > 1:
        s = ndimage.sum(m, l, range(1, n + 1))
        m = np.isin(l, [i + 1 for i in range(n) if s[i] > s.max() * 0.02])
    m = ndimage.binary_closing(np.pad(m, close), iterations=close)[close:-close, close:-close]
    m = ndimage.binary_fill_holes(m)
    return m

def split(sheet, out_prefix, thr=8, close=6):
    im = cv2.imread(sheet); H, W = im.shape[:2]; h, w = H // 2, W // 2
    out = {}
    for name, (r, c) in {'front': (0, 0), 'back': (0, 1), 'left': (1, 0), 'right': (1, 1)}.items():
        cell = im[r * h:(r + 1) * h, c * w:(c + 1) * w]
        m = mask(cv2.cvtColor(cell, cv2.COLOR_BGR2RGB), thr, close)
        p = f'{out_prefix}_{name}.png'
        cv2.imwrite(p, np.dstack([cell, (m * 255).astype(np.uint8)]))
        out[name] = p
    return out

if __name__ == '__main__':
    split(sys.argv[1], sys.argv[2], *(float(a) for a in sys.argv[3:]))
