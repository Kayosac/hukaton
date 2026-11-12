"""God mountains: pixel-painted tileable textures (256², hard pixels, concept palette).

Native resolution 128 px per tile, upscaled 2x with nearest -> 2 px blocks, like the approved cave.
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

OUT = sys.argv[1] if len(sys.argv) > 1 else "tex"
os.makedirs(OUT, exist_ok=True)
N = 128


def hexc(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], dtype=np.float32)


def tile_noise(rng, n, scale):
    """Tileable smooth noise in [0,1]: white noise low-passed in Fourier space (periodic by construction)."""
    w = rng.standard_normal((n, n))
    f = np.fft.fft2(w)
    ky = np.fft.fftfreq(n)[:, None]
    kx = np.fft.fftfreq(n)[None, :]
    k = np.sqrt(kx * kx + ky * ky)
    sigma = 1.0 / scale
    f *= np.exp(-(k / sigma) ** 2)
    r = np.real(np.fft.ifft2(f))
    r -= r.min()
    r /= r.max() + 1e-9
    return r


def aniso_noise(rng, n, sx, sy):
    w = rng.standard_normal((n, n))
    f = np.fft.fft2(w)
    ky = np.fft.fftfreq(n)[:, None]
    kx = np.fft.fftfreq(n)[None, :]
    f *= np.exp(-((kx * sx) ** 2 + (ky * sy) ** 2))
    r = np.real(np.fft.ifft2(f))
    r -= r.min()
    r /= r.max() + 1e-9
    return r


def warp(rng, field, amount, scale):
    n = field.shape[0]
    dx = (tile_noise(rng, n, scale) - .5) * amount
    dy = (tile_noise(rng, n, scale) - .5) * amount
    yy, xx = np.mgrid[0:n, 0:n]
    sx = (xx + dx).round().astype(int) % n
    sy = (yy + dy).round().astype(int) % n
    return field[sy, sx]


def quantize(field, palette, rng, jag=.06):
    """Field 0..1 -> palette index with jagged (ordered-ish) edges between clusters."""
    jitter = (rng.random(field.shape) - .5) * jag
    f = np.clip(field + jitter, 0, .9999)
    idx = (f * len(palette)).astype(int)
    out = np.zeros(field.shape + (3,), np.float32)
    for i, c in enumerate(palette):
        out[idx == i] = hexc(c)
    return out, idx


def equalize(field):
    flat = field.ravel()
    order = flat.argsort()
    ranks = np.empty_like(order)
    ranks[order] = np.arange(flat.size)
    return (ranks / flat.size).reshape(field.shape)


def cracks(img, rng, count, color, light=None, length=(18, 40)):
    n = img.shape[0]
    c = hexc(color)
    lc = hexc(light) if light else None
    for _ in range(count):
        x, y = rng.integers(0, n), rng.integers(0, n)
        ang = rng.uniform(0, math.tau)
        steps = rng.integers(*length)
        for s in range(steps):
            if rng.random() < .22:
                ang += rng.choice([-1, 1]) * rng.uniform(.5, 1.1)
            x = (x + math.cos(ang)) % n
            y = (y + math.sin(ang) * 1.0) % n
            ix, iy = int(x) % n, int(y) % n
            img[iy, ix] = c
            if lc is not None and rng.random() < .5:
                img[(iy + 1) % n, ix] = lc
            if rng.random() < .07:  # short branch
                bx, by, ba = x, y, ang + rng.choice([-1, 1]) * 1.2
                for _ in range(rng.integers(3, 8)):
                    bx, by = (bx + math.cos(ba)) % n, (by + math.sin(ba)) % n
                    img[int(by) % n, int(bx) % n] = c


def blobs(rng, n, coverage, scale, warp_amt=10):
    f = tile_noise(rng, n, scale)
    f = warp(rng, f, warp_amt, 10)
    thr = np.quantile(f, 1 - coverage)
    return f > thr


def speckle(img, rng, mask_prob, color):
    n = img.shape[0]
    m = rng.random((n, n)) < mask_prob
    img[m] = hexc(color)


def save(img, name, up=2):
    a = np.clip(img, 0, 255).astype(np.uint8)
    im = Image.fromarray(a, "RGB" if a.shape[2] == 3 else "RGBA")
    if up > 1:
        im = im.resize((im.width * up, im.height * up), Image.NEAREST)
    im.save(os.path.join(OUT, name))
    return im


def stone(rng, palette, big_scale=36, small_scale=9, warp_amt=12):
    """Mottled painted stone: broad clusters + medium clusters, jagged edges."""
    big = tile_noise(rng, N, big_scale)
    mid = tile_noise(rng, N, small_scale)
    f = .62 * big + .38 * mid
    f = warp(rng, f, warp_amt, 9)
    f = equalize(f)
    img, idx = quantize(f, palette, rng, jag=.09)
    return img, idx


def rock_side(seed, pal, crack_col, crack_light, lichen=None, lichen_cov=0.0):
    rng = np.random.default_rng(seed)
    big = tile_noise(rng, N, 30)
    mid = tile_noise(rng, N, 10)
    f = .7 * big + .3 * mid
    f = warp(rng, f, 10, 12)
    f = equalize(f)
    img, idx = quantize(f, pal, rng, jag=.07)
    # soft vertical strata: every few pixels a band shifted one tone
    # painted chips: light pixels on the upper-left edge of darker clusters
    edge = (idx[1:, 1:] > idx[:-1, :-1])
    chips = np.zeros((N, N), bool)
    chips[1:, 1:] = edge & (rng.random((N - 1, N - 1)) < .25)
    img[chips] = hexc(pal[-1])
    if lichen:
        m = blobs(rng, N, lichen_cov, 10, 8)
        img[m] = hexc(lichen[0])
        img[m & (rng.random((N, N)) < .3)] = hexc(lichen[1])
    cracks(img, rng, 2, crack_col, None, (14, 30))
    return img


def rock_top(seed, pal, lichen, dust, cov=.2):
    rng = np.random.default_rng(seed)
    img, _ = stone(rng, pal, big_scale=30, small_scale=8)
    d = blobs(rng, N, .20, 20, 12)
    img[d] = hexc(dust[0])
    img[d & (rng.random((N, N)) < .2)] = hexc(dust[1])
    m = blobs(rng, N, cov, 16, 12)
    inner = blobs(rng, N, cov * .5, 6, 8) & m
    img[m] = hexc(lichen[0])
    img[inner] = hexc(lichen[1])
    img[m & (rng.random((N, N)) < .1)] = hexc(lichen[2])
    cracks(img, rng, 2, "#2E2836", None, (10, 24))
    return img


def path(seed):
    rng = np.random.default_rng(seed)
    pal = ["#5F5258", "#6B5D62", "#78696B", "#857472", "#93817C"]
    img, _ = stone(rng, pal, big_scale=28, small_scale=7, warp_amt=10)
    # trodden lighter dust flakes
    flakes = blobs(rng, N, .12, 7, 6)
    img[flakes] = hexc("#9C8A83")
    # lichen patches in clusters
    m = blobs(rng, N, .16, 16, 12)
    inner = blobs(rng, N, .08, 6, 8) & m
    img[m] = hexc("#7F7C5D")
    img[inner] = hexc("#8E8B67")
    img[m & (rng.random((N, N)) < .1)] = hexc("#6E6C52")
    # pebbles: small dark 1-2 px dots with a light top pixel
    for _ in range(70):
        x, y = rng.integers(0, N, 2)
        img[y, x] = hexc("#4E4249")
        if rng.random() < .6:
            img[(y - 1) % N, x] = hexc("#A39189")
    return img


def carved(seed):
    """Steps and the frame: smoother, slightly lighter, chisel lines."""
    rng = np.random.default_rng(seed)
    pal = ["#5A4F63", "#64586D", "#6E6277", "#786C80"]
    img, _ = stone(rng, pal, big_scale=40, small_scale=10, warp_amt=6)
    for y in range(0, N, 16):
        for x in range(N):
            if rng.random() < .82:
                img[(y + rng.integers(0, 2)) % N, x] = hexc("#4A4154")
    cracks(img, rng, 1, "#2E2836", None, (8, 16))
    return img


def far(seed):
    rng = np.random.default_rng(seed)
    pal = ["#857C9C", "#8C83A3", "#938AAA", "#9A91B0"]
    img, _ = stone(rng, pal, big_scale=40, small_scale=12, warp_amt=14)
    m = blobs(rng, N, .12, 18, 12)
    img[m] = hexc("#9C9A92")
    return img


def cloud(seed):
    rng = np.random.default_rng(seed)
    f = .6 * aniso_noise(rng, N, 30, 8) + .4 * tile_noise(rng, N, 14)
    f = warp(rng, f, 12, 6)
    f = equalize(f)
    img, _ = quantize(f, ["#D3CCDF", "#DCD6E7", "#E5E0EE", "#EEEAF5", "#F6F3FA"], rng, jag=.05)
    return img


def foliage(seed):
    """RGBA atlas 256²: A08 rosette leaf (left top), A07 twig with leaves (right top), bark (bottom half)."""
    rng = np.random.default_rng(seed)
    S = 128
    img = np.zeros((S, S, 4), np.float32)
    front, light, dark, rib = hexc("#7A8065"), hexc("#8E9470"), hexc("#62694F"), hexc("#A3A884")
    outline = hexc("#4C523E")

    def leaf(cx, top, bottom, half_w, lean=0.0):
        """Pointed leaf from (cx, bottom) up to (cx+lean, top); midrib; lighter left half."""
        h = bottom - top
        for y in range(top, bottom + 1):
            t = (bottom - y) / h  # 0 at base, 1 at tip
            w = half_w * math.sin(min(1, t * 1.15) * math.pi) ** .8 * (1 - .15 * t)
            if t < .08:
                w = max(w, .9)
            c = cx + lean * t
            for x in range(int(math.floor(c - w)), int(math.ceil(c + w)) + 1):
                if 0 <= x < S and abs(x - c) <= w:
                    col = light if x < c - .5 else front
                    if abs(x - c) > w - 1.1:
                        col = outline
                    elif ((x - c) * .5 + y) % 7 < 1 and abs(x - c) > 1:  # side veins
                        col = dark
                    img[y, x, :3] = col
                    img[y, x, 3] = 255
            ix = int(round(c))
            if 0 <= ix < S and t < .93:
                img[y, ix, :3] = rib
                img[y, ix, 3] = 255

    # A08 rosette leaf: one tall leaf in the left half (u 0..0.5, v 0.5..1 in Unity = top-left in image)
    leaf(32, 3, 62, 11)
    # A07 leaf cluster: six big leaves from a short twig in the lower left of the right top quarter
    twig = hexc("#5A4E52")
    d = Image.new("L", (S, S), 0)
    dr = ImageDraw.Draw(d)
    dr.line([(66, 63), (78, 50), (88, 42)], fill=255, width=3)
    tw = np.array(d) > 0
    img[tw, :3] = twig
    img[tw, 3] = 255

    def leaf_dir(x0, y0, ang, length, half_w):
        """Filled pointed leaf from base (x0, y0) along an angle (0 = up, positive = right)."""
        ux, uy = math.sin(ang), -math.cos(ang)
        nx, ny = -uy, ux
        left, right = [], []
        for i in range(0, int(length) + 1):
            t = i / length
            w = half_w * math.sin(min(1, t * 1.15) * math.pi) ** .8 * (1 - .15 * t)
            cx, cy = x0 + ux * i, y0 + uy * i
            left.append((cx - nx * w, cy - ny * w))
            right.append((cx + nx * w, cy + ny * w))
        m = Image.new("L", (S, S), 0)
        ImageDraw.Draw(m).polygon(left + right[::-1], fill=255)
        mask = np.array(m) > 0
        mask[:, :64] = False
        mask[64:, :] = False
        ys, xs = np.nonzero(mask)
        side = (xs - x0) * nx + (ys - y0) * ny
        for yy, xx, sd in zip(ys, xs, side):
            img[yy, xx, :3] = light if sd < 0 else front
            img[yy, xx, 3] = 255
        er = mask.copy()
        er[1:, :] &= mask[:-1, :]; er[:-1, :] &= mask[1:, :]; er[:, 1:] &= mask[:, :-1]; er[:, :-1] &= mask[:, 1:]
        edge = mask & ~er
        img[edge, :3] = outline
        for i in range(int(length * .9)):
            px, py = int(round(x0 + ux * i)), int(round(y0 + uy * i))
            if 64 <= px < S and 0 <= py < 64 and mask[py, px]:
                img[py, px, :3] = rib

    for (x0, y0, ang, ln, hw) in [(78, 50, -.9, 22, 6), (82, 46, -.35, 26, 7), (88, 42, .15, 24, 7),
                                  (88, 42, .7, 26, 7), (84, 45, 1.25, 24, 6), (74, 54, 1.6, 18, 5)]:
        leaf_dir(x0, y0, ang, ln, hw)
    # bark (rows 64..127): slate-brown with stripes and lichen
    pal = ["#4C4247", "#574B50", "#62555A", "#6E6064"]
    bark = aniso_noise(rng, S, 4, 40)
    bark = equalize(bark)[64:, :]
    b, _ = quantize(bark, pal, rng, jag=.08)
    lich = blobs(rng, S, .12, 10, 8)[64:, :]
    b[lich] = hexc("#7F7C5D")
    img[64:, :, :3] = b
    img[64:, :, 3] = 255
    return img


def stroke_stone(seed, base, light, dark, stroke=(40, 11), patch=30, lichen=None, lichen_cov=0.0, dust=None):
    """Concept-style painted stone: a muted base with long brush strokes of lighter and darker tones (pixel-jagged),
    broad soft patches, optional lichen clusters (tops only) and dusty areas."""
    rng = np.random.default_rng(seed)
    img = np.zeros((N, N, 3), np.float32)
    img[:] = hexc(base[0])
    # broad patches between the base tones
    f = equalize(warp(rng, tile_noise(rng, N, patch), 8, 14))
    for i, c in enumerate(base[1:]):
        img[f > 1 - (i + 1) * .28] = hexc(c)
    # long strokes: anisotropic noise, thresholded into thin bands
    for cols, thr in ((light, .84), (dark, .86)):
        g = equalize(warp(rng, aniso_noise(rng, N, stroke[0], stroke[1]), 4, 10))
        m = g > thr
        img[m] = hexc(cols[0])
        if len(cols) > 1:
            img[m & (g > thr + .1)] = hexc(cols[1])
    if dust:
        d = blobs(rng, N, .2, 18, 10)
        img[d] = hexc(dust[0])
        img[d & (rng.random((N, N)) < .25)] = hexc(dust[1])
    if lichen:
        m = blobs(rng, N, lichen_cov, 12, 10)
        core = m & blobs(rng, N, lichen_cov * .6, 5, 6)
        img[m] = hexc(lichen[0])
        img[core] = hexc(lichen[1])
        img[m & (rng.random((N, N)) < .15)] = hexc(lichen[2])
    return img


MAUVE = ["#665466", "#604E60", "#6A586A"]
LICHEN = ["#857A5E", "#958A66", "#A79D74"]
save(stroke_stone(11, MAUVE, ["#6C5B6C", "#726172"], ["#5E4E60"]), "T_GM_RockSide.png")
save(stroke_stone(12, ["#6C5A6C", "#685668", "#705E6E"], ["#746272", "#7A6878"], ["#625264"], lichen=LICHEN, lichen_cov=.11,
                  dust=["#7A6670", "#836E76"]), "T_GM_RockTop.png")
save(stroke_stone(13, ["#4E424E", "#483C48", "#544654"], ["#584A58", "#605060"], ["#423642"]), "T_GM_InnerSide.png")
save(stroke_stone(14, ["#544654", "#4E424E", "#5A4B5A"], ["#5E4F5E", "#665666"], ["#463A46"], lichen=["#5E5848", "#686050", "#726A56"],
                  lichen_cov=.06), "T_GM_InnerTop.png")
save(stroke_stone(15, ["#7A6670", "#73606A", "#806B74"], ["#87727A", "#907B82"], ["#6A5862"], stroke=(24, 4), lichen=["#7F7A5C", "#8E8866", "#9C9670"],
                  lichen_cov=.08), "T_GM_Path.png")
save(carved(16), "T_GM_Carved.png")
save(stroke_stone(17, ["#8A7E94", "#857990", "#8F8399"], ["#958A9E", "#9C91A6"], ["#7E7389"], lichen=["#98957E", "#A29E86", "#ABA78E"], lichen_cov=.08),
     "T_GM_Far.png")
save(cloud(18), "T_GM_Cloud.png")
save(foliage(19), "T_GM_Foliage.png")
print("ok", sorted(os.listdir(OUT)))
