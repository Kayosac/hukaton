"""Tileable noise for the cloud sea shader (512 px, RGBA, linear):
R big puffs (Worley domes, 8 + 16 cells), G macro variation (smooth fbm), B fine detail (fbm),
A small puffs (Worley domes, 32 cells). usage: python3 cloud_noise.py out.png"""
import sys
import numpy as np
from PIL import Image

N = 512
rng = np.random.default_rng(20261112)
yy, xx = np.mgrid[0:N, 0:N].astype(np.float64) + .5


def worley(cells, seed):
    r = np.random.default_rng(seed)
    pts = r.random((cells, cells, 2))
    s = N / cells
    cx, cy = (xx // s).astype(int), (yy // s).astype(int)
    f1 = np.full((N, N), 1e9)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            nx, ny = cx + dx, cy + dy
            p = pts[ny % cells, nx % cells]
            px, py = (nx + p[..., 0]) * s, (ny + p[..., 1]) * s
            f1 = np.minimum(f1, np.hypot(xx - px, yy - py))
    return f1 / s


def domes(cells, seed, r=.78):
    f = worley(cells, seed)
    return np.sqrt(np.clip(1 - (f / r) ** 2, 0, 1))


def fbm(k0, power, seed):
    r = np.random.default_rng(seed)
    w = r.standard_normal((N, N))
    F = np.fft.fft2(w)
    kx = np.fft.fftfreq(N)[None, :] * N
    ky = np.fft.fftfreq(N)[:, None] * N
    k = np.hypot(kx, ky)
    k[0, 0] = 1
    filt = (k >= k0) / k ** power
    out = np.real(np.fft.ifft2(F * filt))
    out -= out.min()
    return out / out.max()


def norm(a):
    a = a - a.min()
    return a / a.max()


R = norm(.68 * domes(8, 1) + .32 * domes(16, 2))
G = fbm(1, 2.2, 3)
B = fbm(6, 1.4, 4)
A = norm(.7 * domes(32, 5) + .3 * domes(16, 6, .7))
img = np.stack([R, G, B, A], -1)
Image.fromarray((img * 255 + .5).astype(np.uint8), "RGBA").save(sys.argv[1])
print("ok", [round(float(c.mean()), 3) for c in (R, G, B, A)])
