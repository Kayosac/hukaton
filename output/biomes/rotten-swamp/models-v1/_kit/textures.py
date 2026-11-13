"""Rotten Swamp shared texture library (pixel-painted, point-sampled, PSX like ForestArt).

All tiling textures are painted at half resolution (chunky 2x2 pixels) from periodic noise, quantised to palettes
picked from the swamp concept sheets, then upscaled with nearest neighbour.
Run: python3 textures.py  -> tex/*.png + tex/regions.json + tex/overview.png
"""
import json, math, os
import numpy as np
import cv2
from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'tex')
os.makedirs(OUT, exist_ok=True)
RNG = np.random.default_rng(7)

def hexes(*h):
    return np.array([[int(x[i:i + 2], 16) for i in (1, 3, 5)] for x in h], np.float32)

# ---------------------------------------------------------------- palettes (dark -> light)
P_BARK = hexes('#2a1f1d', '#3a2b27', '#4a3731', '#5b443a', '#6c5244', '#7e6150')
P_MOSS = hexes('#4b4222', '#605428', '#786a30', '#918239', '#a99845', '#bfac55')
P_BIRCH = hexes('#2e2624', '#5e5149', '#8c7f73', '#a99a8b', '#bcae9e', '#cdc1b1')
P_PEEL = hexes('#4f3a32', '#654a3e', '#7b5c4c')
P_PLANK = hexes('#2c221f', '#43342d', '#574439', '#6a5446', '#7d6554', '#8f7764')
P_LOGW = hexes('#2a1e1b', '#3f2d27', '#533c32', '#68493b', '#7c5a47', '#906b55')
P_ROOF = hexes('#33231f', '#4a3229', '#5e4033', '#73503e', '#8a604a')
P_MUD = hexes('#231b18', '#30251f', '#3e3027', '#4d3c30', '#5d493a', '#6d5744')
P_GREY = hexes('#2f2a27', '#4a433d', '#625a52', '#7a7168', '#91877c')
P_SEDGE = hexes('#4b4524', '#66602d', '#837a36', '#a19341', '#b9a752', '#7a5a33', '#946c3c')
P_WILLOW = hexes('#4a4421', '#625a29', '#7d7231', '#998a3b', '#b39f48', '#c9b45c')
P_ALDER = hexes('#3c3d1f', '#504f26', '#67642e', '#807a37', '#9a9042')
P_DUCK = hexes('#3f4a24', '#56622c', '#6e7a35', '#87903f', '#a2a64c')
P_LILY = hexes('#2f3d22', '#40522a', '#556833', '#6b7d3d', '#7a6a3a')

# ---------------------------------------------------------------- noise helpers
def pnoise(h, w, cells, seed, octaves=4, aniso=(1, 1)):
    """Periodic value noise in [0,1], tiles seamlessly. aniso stretches cells (cx, cy)."""
    r = np.random.default_rng(seed)
    acc = np.zeros((h, w), np.float32); amp = 1.0; tot = 0
    for o in range(octaves):
        cy = max(1, int(cells * aniso[1] * 2 ** o)); cx = max(1, int(cells * aniso[0] * 2 ** o))
        g = r.random((cy, cx)).astype(np.float32)
        g = np.tile(g, (3, 3))
        up = cv2.resize(g, (w * 3, h * 3), interpolation=cv2.INTER_CUBIC)[h:2 * h, w:2 * w]
        acc += up * amp; tot += amp; amp *= 0.5
    acc /= tot
    acc = (acc - acc.min()) / (acc.max() - acc.min() + 1e-6)
    return acc

def warp(img, dx, dy):
    h, w = img.shape[:2]
    X, Y = np.meshgrid(np.arange(w, dtype=np.float32), np.arange(h, dtype=np.float32))
    return cv2.remap(img, (X + dx) % w, (Y + dy) % h, cv2.INTER_LINEAR, borderMode=cv2.BORDER_WRAP)

def quant(v, pal, levels=None):
    """v in [0,1] -> palette colours by bands."""
    n = len(pal)
    idx = np.clip((v * n).astype(int), 0, n - 1)
    return pal[idx]

def blend_mask(base, over, mask):
    return np.where(mask[..., None], over, base)

def up2(img):
    return cv2.resize(img, (img.shape[1] * 2, img.shape[0] * 2), interpolation=cv2.INTER_NEAREST)

def save(name, rgb, alpha=None):
    rgb = np.clip(rgb, 0, 255).astype(np.uint8)
    if alpha is not None:
        a = (np.clip(alpha, 0, 1) * 255).astype(np.uint8)
        Image.fromarray(np.dstack([rgb, a]), 'RGBA').save(os.path.join(OUT, name))
    else:
        Image.fromarray(rgb, 'RGB').save(os.path.join(OUT, name))

def moss_mask(h, w, seed, cover=0.35, cells=3):
    cover = max(cover, 0.002)
    n = pnoise(h, w, cells * 2, seed, 4)
    n2 = pnoise(h, w, cells * 8, seed + 1, 2)
    v = n * 0.65 + n2 * 0.35
    return v > np.quantile(v, 1 - cover), v

def moss_colour(h, w, seed):
    v = pnoise(h, w, 6, seed, 3)
    return quant(v * 0.85 + 0.1, P_MOSS)

# ---------------------------------------------------------------- tiling textures
def bark(size=128, seed=11, moss=0.22, pal=P_BARK):
    h = w = size
    plates = pnoise(h, w, 2, seed, 4, aniso=(3, 0.7))
    fiss = pnoise(h, w, 2, seed + 3, 3, aniso=(5, 0.5))
    dx = (pnoise(h, w, 2, seed + 5, 2) - .5) * 10
    plates = warp(plates, dx, dx * 0.2)
    v = plates * 0.75 + 0.25
    crack = np.abs(fiss - 0.5) < 0.06
    col = quant(v * 0.9, pal[1:])
    col = blend_mask(col, pal[0], crack)
    # vertical light edge on plates
    edge = (np.roll(crack, 1, 1) & ~crack)
    col = blend_mask(col, pal[-1], edge & (plates > .55))
    m, mv = moss_mask(h, w, seed + 9, moss, 2)
    mc = quant(np.clip((mv - mv[m].min()) / (np.ptp(mv[m]) + 1e-6) * 0.8 + 0.05, 0, .99), P_MOSS[:5])
    col = blend_mask(col, mc, m)
    return up2(col)

def birch(size=128, seed=21):
    h = w = size
    base = pnoise(h, w, 3, seed, 3, aniso=(0.6, 2))
    col = quant(base * 0.45 + 0.5, P_BIRCH[2:])
    # dark horizontal lenticel dashes
    r = np.random.default_rng(seed)
    img = col.copy()
    for _ in range(70):
        y = r.integers(0, h); x = r.integers(0, w); L = r.integers(3, 14)
        th = 1 if r.random() < .7 else 2
        for t in range(th):
            for k in range(L):
                img[(y + t) % h, (x + k) % w] = P_BIRCH[0] if r.random() < .6 else P_BIRCH[1]
    # big dark bands (birch scars)
    for _ in range(7):
        y = r.integers(0, h); x = r.integers(0, w); L = r.integers(10, 30); th = r.integers(2, 5)
        for t in range(th):
            for k in range(L):
                if r.random() < .9:
                    img[(y + t) % h, (x + k + t) % w] = P_BIRCH[0]
    # peeled brown patches
    pm = pnoise(h, w, 3, seed + 4, 3)
    peel = pm > np.quantile(pm, 0.74)
    img = blend_mask(img, quant(pnoise(h, w, 8, seed + 6, 2) * .99, P_PEEL), peel)
    m, mv = moss_mask(h, w, seed + 8, 0.08, 2)
    img = blend_mask(img, quant(np.clip(mv, 0, .99), P_MOSS[1:]), m)
    return up2(img)

def planks(size=128, seed=31, boards=6, pal=P_PLANK, moss=0.16):
    """Vertical boards (grain along V)."""
    h = w = size
    img = np.zeros((h, w, 3), np.float32)
    r = np.random.default_rng(seed)
    edges = np.sort(r.choice(np.arange(4, w - 4), boards - 1, replace=False))
    edges = np.concatenate([[0], edges, [w]])
    grain = pnoise(h, w, 2, seed + 1, 3, aniso=(6, 0.4))
    for i in range(len(edges) - 1):
        a, b = edges[i], edges[i + 1]
        tone = r.uniform(-0.15, 0.15)
        v = np.clip(grain[:, a:b] * 0.55 + 0.3 + tone, 0, .99)
        img[:, a:b] = quant(v, pal[1:])
        img[:, a] = pal[0]
        if b - a > 3:
            img[:, a + 1] = pal[1]
        # knot
        if r.random() < .6:
            ky = r.integers(4, h - 4); kx = int((a + b) / 2)
            cv2.circle(img, (kx, ky), 2, pal[0].tolist(), -1)
            cv2.circle(img, (kx, ky), 1, pal[2].tolist(), -1)
        # rot hole / crack
        if r.random() < .35:
            ky = r.integers(0, h - 12); L = r.integers(5, 16)
            img[ky:ky + L, min(b - 2, a + 2 + r.integers(0, max(1, b - a - 4)))] = pal[0]
    # horizontal seams (board ends) on some boards
    m, mv = moss_mask(h, w, seed + 9, moss, 2)
    img = blend_mask(img, quant(np.clip((mv - mv[m].min()) / (np.ptp(mv[m]) + 1e-6) * 0.8 + 0.05, 0, .99), P_MOSS[:5]), m)
    return up2(img)

def logwall(size=128, seed=41, logs=5, pal=P_LOGW, moss=0.05):
    """Horizontal stacked round logs (grain along U, logs stacked along V). Tiles both ways."""
    h = w = size
    img = np.zeros((h, w, 3), np.float32)
    lh = h // logs
    grain = pnoise(h, w, 2, seed + 1, 3, aniso=(0.4, 6))
    r = np.random.default_rng(seed)
    for i in range(logs):
        y0 = i * lh; y1 = (i + 1) * lh if i < logs - 1 else h
        yy = np.arange(y0, y1)
        t = (yy - y0) / max(1, (y1 - y0 - 1))
        shade = np.sin(t * math.pi) ** 0.6  # round log: lighter middle
        v = grain[y0:y1] * 0.35 + shade[:, None] * 0.55 + r.uniform(-.08, .08)
        img[y0:y1] = quant(np.clip(v, 0, .99), pal[1:])
        img[y0] = pal[0]; img[y1 - 1] = pal[0]
        img[min(y1 - 2, y0 + 1)] = pal[1]
        # vertical cracks
        for _ in range(3):
            x = r.integers(0, w); L = r.integers(3, 9)
            img[y0 + 2:y0 + 2 + L // 2, x] = pal[1]
    # moss lies in the grooves (bottom of each log)
    m, mv = moss_mask(h, w, seed + 9, moss, 3)
    groove = np.zeros((h, w), bool)
    for i in range(logs):
        y1 = (i + 1) * lh
        groove[max(0, y1 - 3):y1] = True
    gm = groove & (pnoise(h, w, 8, seed + 2, 2) > .62)
    img = blend_mask(img, quant(np.clip(mv, 0, .99), P_MOSS[1:]), m | gm)
    return up2(img)

def roof(size=128, seed=51):
    """Moss-grown shingle/plank roof: rows of shingles along U, eaves at V=0 (bottom). Heavy moss."""
    h = w = size
    img = np.zeros((h, w, 3), np.float32)
    rows = 6; rh = h // rows
    r = np.random.default_rng(seed)
    grain = pnoise(h, w, 2, seed + 1, 3, aniso=(6, 0.5))
    for i in range(rows):
        y0 = i * rh; y1 = y0 + rh
        off = r.integers(0, 12)
        x = -off
        while x < w:
            sw = r.integers(10, 26)
            tone = r.uniform(-.18, .12)
            xs = np.arange(max(0, x), min(w, x + sw))
            if len(xs):
                v = np.clip(grain[y0:y1][:, xs] * 0.5 + 0.35 + tone, 0, .99)
                img[y0:y1][:, xs] = quant(v, P_ROOF[1:])
                img[y0:y1, xs[0]] = P_ROOF[0]
            x += sw
        img[y1 - 1] = P_ROOF[0]   # shadow under each row
    m, mv = moss_mask(h, w, seed + 9, 0.40, 2)
    img = blend_mask(img, quant(np.clip((mv - mv[m].min()) / (np.ptp(mv[m]) + 1e-6) * 0.85 + 0.05, 0, .99), P_MOSS[:5]), m)
    img = np.flipud(img)
    return up2(img)

def mud(size=128, seed=61, moss=0.3, grass=True):
    h = w = size
    v = pnoise(h, w, 4, seed, 4)
    dx = (pnoise(h, w, 2, seed + 2, 2) - .5) * 8
    v = warp(v, dx, -dx)
    img = quant(v * 0.95, P_MUD)
    m, mv = moss_mask(h, w, seed + 9, moss, 3)
    img = blend_mask(img, quant(np.clip((mv - mv[m].min()) / (np.ptp(mv[m]) + 1e-6) * 0.8 + 0.05, 0, .99), P_MOSS[:5]), m)
    if grass:
        r = np.random.default_rng(seed)
        for _ in range(140):
            x = r.integers(0, w); y = r.integers(0, h); L = r.integers(2, 5)
            c = P_SEDGE[r.integers(2, 5)]
            for k in range(L):
                img[(y - k) % h, (x + (k // 2) * (1 if r.random() < .5 else 0)) % w] = c
    return up2(img)

def moss(size=64, seed=71):
    h = w = size
    v = pnoise(h, w, 4, seed, 4)
    img = quant(v * 0.99, P_MOSS)
    r = np.random.default_rng(seed)
    for _ in range(60):
        x = r.integers(0, w); y = r.integers(0, h)
        img[y, x] = P_MOSS[-1] if r.random() < .5 else P_MOSS[0]
    return up2(img)


P_PEAT = hexes('#2b1f1f', '#3b2a28', '#4b3532', '#5c423c', '#6d5047', '#7d5e52')
def peat(size=128, seed=91):
    """Peat / hummock walls: purple-brown faceted blotches, a few root fibres, sparse small moss specks."""
    h = w = size
    v = pnoise(h, w, 5, seed, 4, aniso=(1.4, 0.8))
    dx = (pnoise(h, w, 3, seed + 2, 2) - .5) * 6
    v = warp(v, dx, dx * .5)
    img = quant(v * 0.98, P_PEAT)
    r = np.random.default_rng(seed)
    for _ in range(40):   # root fibres / dark cracks, short diagonal
        x = r.integers(0, w); y = r.integers(0, h); L = r.integers(3, 9); sx = r.choice([-1, 1])
        for k in range(L):
            img[(y + k) % h, (x + sx * (k // 2)) % w] = P_PEAT[0]
    for _ in range(25):   # pale fibre glints
        x = r.integers(0, w); y = r.integers(0, h)
        img[y, x] = P_PEAT[5]; img[(y + 1) % h, x] = P_PEAT[4]
    m, mv = moss_mask(h, w, seed + 9, 0.10, 3)
    img = blend_mask(img, quant(np.clip((mv - mv[m].min()) / (np.ptp(mv[m]) + 1e-6) * 0.7 + 0.05, 0, .99), P_MOSS[:5]), m)
    return up2(img)


P_STONE = hexes('#2a2724', '#3d3833', '#514a43', '#655d54', '#797066', '#8c8378')
def stone(size=128, seed=93):
    """Rough grey-brown foundation stones / boulders, mossy cracks (tiling)."""
    h = w = size
    Y, X = np.mgrid[0:h, 0:w]
    r = np.random.default_rng(seed)
    pts = r.random((22, 2)) * [w, h]
    d1 = np.full((h, w), 1e9); d2 = np.full((h, w), 1e9); idx = np.zeros((h, w), int)
    for i, (px, py) in enumerate(pts):
        for ox in (-w, 0, w):
            for oy in (-h, 0, h):
                d = np.hypot(X - px - ox, Y - py - oy)
                closer = d < d1
                d2 = np.where(closer, d1, np.minimum(d2, d)); idx = np.where(closer, i, idx); d1 = np.where(closer, d, d1)
    edge = (d2 - d1) < 2.2
    tone = r.uniform(.25, .75, len(pts))[idx]
    v = np.clip(tone * .6 + pnoise(h, w, 8, seed + 1, 3) * .4 + (1 - np.clip((d2 - d1) / 10, 0, 1)) * -.15, 0, .99)
    img = quant(v, P_STONE[1:])
    img[edge] = P_STONE[0]
    m, mv = moss_mask(h, w, seed + 9, 0.14, 3)
    img = blend_mask(img, quant(np.clip((mv - mv[m].min()) / (np.ptp(mv[m]) + 1e-6) * 0.7 + 0.05, 0, .99), P_MOSS[:5]), m & ~edge)
    return up2(img)

# ---------------------------------------------------------------- props atlas (opaque, 256 -> 512)
REG_PROPS = {}
def props_atlas():
    S = 256
    img = np.zeros((S, S, 3), np.float32) + P_PLANK[2]
    def put(name, x, y, tile):
        th, tw = tile.shape[:2]
        img[y:y + th, x:x + tw] = tile
        REG_PROPS[name] = [x * 2, y * 2, tw * 2, th * 2]   # px in the final 512 image (x, y from top, w, h)
    r = np.random.default_rng(81)
    # end grain (cut log face): rings, rotten centre, bark rim  (two variants)
    for k in range(2):
        t = np.zeros((32, 32, 3), np.float32)
        Y, X = np.mgrid[0:32, 0:32]
        d = np.hypot(X - 15.5 + k, Y - 15.5) + (pnoise(32, 32, 3, 90 + k, 2) - .5) * 3
        ring = (np.floor(d / 2.6) % 2)
        pal = P_PLANK if k == 0 else P_LOGW
        t[:] = np.where(ring[..., None] > 0, pal[4], pal[3])
        t[d < 4 + k * 2] = pal[1]
        t[(d > 13) ] = P_BARK[2]
        t[(d > 14.6)] = P_BARK[1]
        crack = (np.abs(X - 15.5 - (Y - 15.5) * 0.3) < 0.7) & (d < 12) & (Y < 16)
        t[crack] = pal[0]
        put('end_grain_%d' % k, 32 * k, 0, t)
    # end grain grey (crosses / stakes)
    t = np.zeros((32, 32, 3), np.float32)
    Y, X = np.mgrid[0:32, 0:32]; d = np.hypot(X - 15.5, Y - 15.5)
    t[:] = np.where((np.floor(d / 3) % 2)[..., None] > 0, P_GREY[3], P_GREY[2]); t[d > 14] = P_GREY[1]
    put('end_grain_grey', 64, 0, t)
    # window: dark glass + cross frame
    t = np.zeros((32, 32, 3), np.float32) + P_PLANK[3]
    t[3:29, 3:29] = hexes('#1c1a18')[0]
    t[3:29, 15:17] = P_PLANK[3]; t[15:17, 3:29] = P_PLANK[3]
    t[4:14, 4:14] = hexes('#2a2a26')[0]; t[5:8, 5:8] = hexes('#3d3b33')[0]
    t[0:3] = P_PLANK[4]; t[29:] = P_PLANK[1]
    put('window', 96, 0, t)
    # dark opening (door gap / interior)
    t = np.zeros((32, 32, 3), np.float32) + hexes('#161311')[0]
    t[26:] = hexes('#211b17')[0]
    put('dark', 128, 0, t)
    # chimney clay / adobe
    v = pnoise(32, 32, 4, 95, 3)
    t = quant(v * .99, hexes('#4d3a33', '#5f483e', '#71564a', '#826456'))
    for y in range(0, 32, 6):
        t[y] = hexes('#3a2b26')[0]
    put('chimney', 160, 0, t)
    # rope (along U)
    t = np.zeros((16, 64, 3), np.float32)
    for x in range(64):
        for y in range(16):
            s = ((x + y * 0.8) % 6) / 6
            t[y, x] = hexes('#5f5240', '#7a6b52', '#94846a')[min(2, int(s * 3))]
    t[0] = hexes('#3a3227')[0]; t[15] = hexes('#3a3227')[0]
    put('rope', 192, 0, t)
    # rag cloth (opaque, dirty linen) 32x32
    v = pnoise(32, 32, 3, 96, 3)
    t = quant(v * .99, hexes('#5b4f43', '#73665a', '#8a7d6c', '#a09280'))
    t[::5] = hexes('#4b4137')[0]
    put('rag', 192, 16, t)
    # fungus cap top / side, gill, stem
    v = pnoise(32, 32, 3, 97, 2)
    put('fungus_cap', 224, 16, quant(v * .6 + .35, hexes('#7a6a58', '#a8957d', '#c7b59a', '#ddd0b8')))
    g = np.zeros((16, 32, 3), np.float32) + hexes('#8b7a66')[0]; g[:, ::3] = hexes('#5d5044')[0]
    put('fungus_gill', 224, 48, g)
    v = pnoise(16, 32, 3, 98, 2)
    put('fungus_stem', 224, 64, quant(v * .5 + .4, hexes('#8f806c', '#b3a38b', '#cbbda4')))
    # cattail cob (velvet brown)
    v = pnoise(16, 32, 4, 99, 2)
    put('cattail_cob', 192, 48, quant(v * .99, hexes('#3a2418', '#4d3020', '#5e3c28', '#704a33')))
    # lily flower petal (cream white)
    v = pnoise(16, 32, 3, 100, 2)
    put('lily_petal', 192, 64, quant(v * .5 + .45, hexes('#a39a86', '#c8bfaa', '#ddd5c2', '#efe8d8')))
    # iron (bucket hoops, nails) / bucket wood
    v = pnoise(16, 32, 4, 101, 2)
    put('iron', 192, 80, quant(v * .99, hexes('#26221f', '#3a3430', '#4f4640', '#5b4031')))
    # idol face (front of a carved post, concept SW23): stave grain, heavy flat brow, deep horizontal eye slits,
    # long wedge nose, NO mouth; weathered, a few moss tufts at the top
    t = planks(64, 120, boards=4, pal=P_PLANK, moss=0.0)[::2, ::2][:, :32].copy()
    t = cv2.resize(t, (48, 96), interpolation=cv2.INTER_NEAREST)
    dk = hexes('#1a1311')[0]
    # brow block: lighter band with shadow below
    t[22:30, 3:45] = P_PLANK[4]; t[22, 3:45] = P_PLANK[5]; t[30:32, 3:45] = P_PLANK[0]
    # eye slits (deep, slightly uneven)
    t[33:37, 7:20] = dk; t[33:37, 28:41] = dk; t[37, 8:19] = P_PLANK[1]; t[37, 29:40] = P_PLANK[1]
    # long wedge nose: lit left face, shadow right
    for y in range(31, 64):
        w_ = 3 + (y - 31) // 8
        t[y, 24 - w_:24] = P_PLANK[4]; t[y, 24:24 + w_] = P_PLANK[2]; t[y, 24 + w_:26 + w_] = P_PLANK[0]
    t[64:67, 17:33] = P_PLANK[0]
    # cheek cuts
    t[44:58, 9] = P_PLANK[1]; t[44:58, 38] = P_PLANK[1]
    # chin band
    t[78:80, :] = P_PLANK[0]
    mm = pnoise(96, 48, 4, 121, 2)
    mzone = (np.arange(96)[:, None] < 12) & (mm > .5)
    t = blend_mask(t, quant(mm * .99, P_MOSS), mzone)
    put('idol_face', 0, 32, t)
    # tar crust / ooze (dark glossy look painted)
    v = pnoise(32, 32, 4, 102, 3)
    put('tar', 48, 32, quant(v * .99, hexes('#0d0b0b', '#171313', '#221c1a', '#3a302b')))
    # rust/brown vent crust (gas vents): orange-brown deposit rings
    v = pnoise(32, 32, 5, 103, 3)
    put('vent_crust', 80, 32, quant(v * .99, hexes('#3a2618', '#5a3a1f', '#7a5228', '#946a35', '#a88a4a')))
    # vent hole inside (dark wet)
    v = pnoise(32, 32, 5, 104, 2)
    put('vent_hole', 112, 32, quant(v * .99, hexes('#100c0a', '#1b1512', '#29201a')))
    # grey weathered wood strip (crosses / stakes), grain along U
    t = planks(64, 130, boards=3, pal=P_GREY, moss=0.08)[::2, ::2]
    put('grey_wood', 48, 64, np.rot90(t[:, :32]).copy()[:32, :64] if False else t[:32, :64])
    # wood for small items (bucket staves etc.)
    t = planks(64, 131, boards=6, pal=P_PLANK, moss=0.05)[::2, ::2]
    put('small_wood', 112, 64, t[:32, :64])
    out = up2(img)
    save('T_Swamp_Props.png', out)

# ---------------------------------------------------------------- plants atlas (cutout, alpha) 512
REG_PLANTS = {}
def plants_atlas():
    S = 256
    rgb = np.zeros((S, S, 3), np.float32); a = np.zeros((S, S), np.float32)
    def put(name, x, y, t_rgb, t_a, note=''):
        th, tw = t_a.shape
        rgb[y:y + th, x:x + tw] = t_rgb; a[y:y + th, x:x + tw] = t_a
        REG_PLANTS[name] = [x * 2, y * 2, tw * 2, th * 2]
    r = np.random.default_rng(201)

    # ---- broad faceted blades (ForestArt-like: flat colour facets, light/dark half along the midrib, brown tips)
    # palettes picked from sheets 01/02/03 (dark -> light), local to the plants atlas
    PB_GREEN = hexes('#3f3920', '#58502a', '#706731', '#8a7e38', '#a39441', '#b9a64d', '#cab55b')
    PB_TIP = hexes('#4a3324', '#64432e', '#7d5538', '#956a45', '#a87c52')
    PB_DRY = hexes('#4a3d22', '#665528', '#86702f', '#a38a3a', '#bba046', '#cdb257')
    PB_CAT = hexes('#433922', '#5d4f2a', '#776633', '#917d3b', '#a99145', '#bea452')

    def draw_blade(cr, ca, pts, widths, pal, tip_pal, k, tip_start=0.72, seed=0, edge=True, blotch=0.35,
                   torn=False):
        """Paint one blade along centreline pts [(x,y)...] with half-widths per point. Left half light facet,
        right half dark facet; random stepped blotch bands; brown tip from tip_start (jagged)."""
        rr = np.random.default_rng(seed)
        P = np.array(pts, np.float32); n = len(P)
        W = np.array(widths, np.float32)
        T = np.gradient(P, axis=0); T /= (np.linalg.norm(T, axis=1, keepdims=True) + 1e-6)
        N = np.stack([-T[:, 1], T[:, 0]], 1)
        L = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(P, axis=0), axis=1))]); L /= max(L[-1], 1e-6)
        ts = tip_start + rr.uniform(-.08, .08)
        bandl = bandr = k
        for i in range(n - 1):
            t = L[i]
            # blotches change on one facet at a time and rarely -> streaks along the blade, not stripes
            if rr.random() < blotch * 0.35:
                if rr.random() < .5:
                    bandl = int(np.clip(k + rr.integers(-1, 2), 1, len(pal) - 2))
                else:
                    bandr = int(np.clip(k + rr.integers(-1, 2), 1, len(pal) - 2))
            brown = t > ts + rr.uniform(-.06, .06)
            if brown:
                bi = int(np.clip(2 + (1 if t > ts + .12 else 0) + rr.integers(-1, 1), 0, len(tip_pal) - 2))
                cl, cd = tip_pal[bi + 1], tip_pal[bi]
            else:
                cl, cd = pal[min(bandl + 1, len(pal) - 1)], pal[max(bandr - 1, 0)]
            c0, c1 = P[i], P[i + 1]
            w0, w1 = W[i], W[i + 1]
            ql = np.array([c0, c0 + N[i] * w0, c1 + N[i + 1] * w1, c1])
            qr = np.array([c0, c0 - N[i] * w0, c1 - N[i + 1] * w1, c1])
            for q, col in ((qr, cd), (ql, cl)):
                qi = np.round(q).astype(np.int32)
                m = np.zeros(ca.shape, np.uint8)
                cv2.fillPoly(m, [qi], 1, lineType=8)
                cv2.polylines(m, [qi], True, 1, lineType=8)
                cr[m > 0] = col; ca[m > 0] = 1
        if edge:   # continuous dark outline on the shaded side (like the forest grass)
            e = np.round(P - N * np.maximum(W[:, None] - 0.5, 0)).astype(np.int32)
            m = np.zeros(ca.shape, np.uint8)
            cv2.polylines(m, [e], False, 1, lineType=8)
            sel = (m > 0) & (ca > 0)
            cr[sel] = cr[sel] * 0.75
        if torn:   # ragged torn end: knock out a few pixels near the tip
            tip = P[-1]
            for _ in range(4):
                x = int(tip[0] + rr.integers(-2, 3)); y = int(tip[1] + rr.integers(0, 4))
                if 0 <= x < ca.shape[1] and 0 <= y < ca.shape[0]:
                    ca[y, x] = 0

    def centreline(x0, y0, length, ang, curl, nseg=14, droop=0.0):
        """Blade centre line from base (x0,y0) going up at angle ang (0 = up, + = right); the direction rotates by
        curl (rad over the length) and droop pulls the outer part down (arching blades)."""
        pts = []; x, y = x0, y0; a = ang
        for s in range(nseg + 1):
            pts.append((x, y))
            t = s / nseg
            a += curl / nseg + droop * t * np.sign(ang if ang != 0 else 1) / nseg * 2
            x += math.sin(a) * length / nseg; y -= math.cos(a) * length / nseg
        return pts

    def width_profile(nseg, W, keep=0.62, base=0.75):
        out = []
        for s in range(nseg + 1):
            t = s / nseg
            if t < 0.12:
                w = W * (base + (1 - base) * t / 0.12)
            elif t < keep:
                w = W * (1 - 0.18 * (t - .12) / (keep - .12))
            else:
                w = W * 0.82 * (1 - (t - keep) / (1 - keep)) ** 0.9
            out.append(max(w, 0.0))
        return out

    def fan_card(w, h, n, pal, seed, spread=1.15, lean=0.0, curl=0.5, wmin=2.6, wmax=4.2, lmin=.5, lmax=1.0,
                 droop=0.6, tip_pal=PB_TIP):
        """Tuft side view: n broad blades from the bottom centre. Back blades darker, front ones lighter."""
        rr = np.random.default_rng(seed)
        cr = np.zeros((h, w, 3), np.float32); ca = np.zeros((h, w), np.float32)
        bl = []
        for i in range(n):
            a = rr.uniform(-spread, spread) * (0.35 + 0.65 * rr.random()) + lean
            bl.append(a)
        bl.sort(key=lambda a: -abs(a - lean))   # outer (more leaning) blades first = behind
        for i, a in enumerate(bl):
            nseg = 16
            Lb = rr.uniform(lmin, lmax) * h * (1.0 - 0.12 * min(abs(a - lean), 1.2))
            x0 = w / 2 + rr.uniform(-w * .07, w * .07) + a * w * 0.04
            pts = centreline(x0, h - 1, Lb, a, curl * np.sign(a) * rr.uniform(.4, 1.2), nseg, droop * rr.uniform(.3, 1.1))
            W = rr.uniform(wmin, wmax)
            k = int(np.clip(1 + i * (len(pal) - 3) / max(n - 1, 1) + rr.integers(-1, 2), 1, len(pal) - 2))
            draw_blade(cr, ca, pts, width_profile(nseg, W), pal, tip_pal, k, tip_start=rr.uniform(.7, .86),
                       seed=seed * 31 + i)
        return cr, ca

    # sedge cards (3 variants) 64x64 each, row y=0: upright fan / wide tangled / one-sided lean
    c, al = fan_card(64, 64, 13, PB_GREEN, 300, spread=0.85, curl=0.35, droop=0.15, wmin=2.2, wmax=3.2, lmin=.7, lmax=1.12)
    put('sedge_0', 0, 0, c, al)
    c, al = fan_card(64, 64, 15, PB_GREEN, 301, spread=1.4, curl=0.6, droop=0.35, wmin=2.0, wmax=3.0, lmin=.55, lmax=1.0)
    put('sedge_1', 64, 0, c, al)
    c, al = fan_card(64, 64, 13, PB_GREEN, 302, spread=0.7, lean=0.5, curl=0.45, droop=0.2, wmin=2.2, wmax=3.2, lmin=.7, lmax=1.15)
    put('sedge_2', 128, 0, c, al)
    # grass tuft small 64x32 (drier, ochre, low)
    c, al = fan_card(64, 32, 14, PB_DRY, 310, spread=1.2, curl=0.4, droop=0.2, wmin=1.5, wmax=2.2, lmin=.7, lmax=1.1)
    put('grass_low', 192, 0, c, al)
    # cattail leaves cards (long broad straps, torn / snapped tips) 32x96 x2
    for k in range(2):
        rr = np.random.default_rng(320 + k)
        cr = np.zeros((96, 32, 3), np.float32); ca = np.zeros((96, 32), np.float32)
        angs = sorted([rr.uniform(-.22, .22) for _ in range(6)], key=lambda q: -abs(q))
        for i, ag in enumerate(angs):
            nseg = 20
            Lb = rr.uniform(62, 94)
            pts = centreline(16 + rr.uniform(-2, 2) - ag * 10, 95, Lb, ag, np.sign(ag) * rr.uniform(.05, .25), nseg, 0.02)
            if rr.random() < .35:   # snapped leaf: last third folds over
                cut = int(nseg * rr.uniform(.6, .75)); base = pts[cut]; dd = np.sign(ag) or 1
                for j in range(cut + 1, nseg + 1):
                    s = j - cut
                    pts[j] = (base[0] + dd * s * 1.6, base[1] + s * 2.4)
            W = rr.uniform(2.0, 2.8)
            wp = width_profile(nseg, W, keep=0.8, base=0.9)
            draw_blade(cr, ca, pts, wp, PB_CAT, PB_TIP, int(rr.integers(1, 4)), tip_start=rr.uniform(.78, .9),
                       seed=900 + k * 10 + i, blotch=0.5, torn=rr.random() < .6)
        put('cattail_leaves_%d' % k, k * 32, 64, cr, ca)

    # duckweed patches 64x64 x4 (top-down): clusters of tiny round leaflets (clover-like), ragged rim, gaps
    def leaflet(cr, ca, x, y, r, col, dark):
        for (dx, dy) in ((0, 0), (r * .9, 0), (-r * .5, r * .8), (-r * .5, -r * .8)):
            cx, cy = x + dx, y + dy
            cv2.circle(ca, (int(round(cx)), int(round(cy))), int(round(r)), 1, -1, lineType=8)
            m = np.zeros(ca.shape, np.uint8)
            cv2.circle(m, (int(round(cx)), int(round(cy))), int(round(r)), 1, -1, lineType=8)
            cr[m > 0] = dark
            m2 = np.zeros(ca.shape, np.uint8)
            cv2.circle(m2, (int(round(cx - .5)), int(round(cy - .5))), max(1, int(round(r - 1))), 1, -1, lineType=8)
            cr[m2 > 0] = col

    PD = hexes('#3e4022', '#55562b', '#6c6c32', '#848039', '#9b9442')       # olive, yellower than P_DUCK (sheet 04)
    PD_HI = hexes('#a99a48', '#b3a24e', '#98863f')                            # ochre leaflet highlights
    for k in range(4):
        rr = np.random.default_rng(340 + k)
        n = pnoise(64, 64, 3, 340 + k, 3)
        Y, X = np.mgrid[0:64, 0:64]
        if k == 0:
            shape = 1 - np.hypot(X - 32, Y - 32) / 30
        elif k == 1:   # crescent
            shape = (1 - np.hypot(X - 32, Y - 32) / 30) - np.clip(1 - np.hypot(X - 42, Y - 26) / 20, 0, 1) * 1.3
        elif k == 2:   # band
            shape = 1 - np.abs(Y - 32 - 6 * np.sin(X / 9)) / 11 - np.abs(X - 32) / 60
        else:          # two blobs
            shape = np.maximum(1 - np.hypot(X - 22, Y - 26) / 18, 1 - np.hypot(X - 44, Y - 40) / 16)
        dens = shape + (n - .5) * 0.7
        cr = np.zeros((64, 64, 3), np.float32); ca = np.zeros((64, 64), np.float32)
        tone = pnoise(64, 64, 5, 350 + k, 2)
        cnt = 0
        while cnt < 1100:
            cnt += 1
            x, y = rr.uniform(2, 62), rr.uniform(2, 62)
            d = dens[int(y), int(x)]
            p = 0.5 if d > 0.12 else (0.03 if d > -0.1 else 0.002)   # dense inside, a few loose bits outside
            if rr.random() > p:
                continue
            ti = int(np.clip(tone[int(y), int(x)] * 2.6 + rr.integers(0, 2) + 1, 1, 4))
            r = rr.choice([2.0, 2.4, 2.9, 3.3]) if d > 0.12 else rr.choice([1.6, 2.0])
            leaflet(cr, ca, x, y, r, PD_HI[rr.integers(0, 3)] if rr.random() < .18 else PD[ti], PD[max(ti - 1, 0)])
        holes = (pnoise(64, 64, 9, 360 + k, 2) > .78) & (dens < .5)   # open water gaps inside the patch
        ca[holes] = 0
        put('duckweed_%d' % k, 64 + (k % 2) * 64, 64 + (k // 2) * 64, cr, ca)
    # lily pad 32x32 (top-down: radial facets, V notch, rotten brown ragged rim, yellow blotches) x2
    PL_PAD = hexes('#4b4f27', '#5b5f2e', '#6b6e35', '#7b7b3b', '#948a44')
    PL_ROT = hexes('#3a2a20', '#54392a', '#6e4c34', '#8a6a3c')
    for k in range(2):
        Y, X = np.mgrid[0:32, 0:32]
        d = np.hypot(X - 15.5, Y - 15.5); ang = np.arctan2(Y - 15.5, X - 15.5)
        rim = 14.8 - (pnoise(32, 32, 6, 360 + k, 2) - .5) * 3.0
        m = d < rim
        m &= ~((np.abs(((ang - (0.6 + k * 2.2)) + np.pi) % (2 * np.pi) - np.pi) < 0.2 + 0.02 * d / 2) & (d > 1.5))
        if k == 1:
            m &= np.hypot(X - 9, Y - 21) > 1.6   # rot hole
        seg = ((ang + np.pi) / (2 * np.pi) * 13).astype(int)
        base = np.where(seg % 2 == 0, 2, 3) + (pnoise(32, 32, 3, 362 + k, 2) > .62)
        col = PL_PAD[np.clip(base, 0, 4)]
        bl = pnoise(32, 32, 5, 366 + k, 2) > .7
        col[bl] = PL_PAD[4]
        rotn = pnoise(32, 32, 5, 364 + k, 2)
        rot = (d > rim - 4.5 + rotn * 3) & ((rotn > .45) | (d > rim - 2))
        col[rot] = PL_ROT[np.clip((rotn[rot] * 4).astype(int), 1, 3)]
        col[(d > rim - 1.2) & m] = PL_ROT[0]
        put('lily_pad_%d' % k, 64 + k * 32, 192, col, m.astype(np.float32))
    # willow strands card 32x128 x2, hang point at TOP: 3-4 chunky tapering tassels per card, each a column of
    # overlapping elongated leaf dabs (light ochre left side, dark olive right side), ragged lengths, gaps between
    for k in range(2):
        rr = np.random.default_rng(370 + k)
        cr = np.zeros((128, 32, 3), np.float32); ca = np.zeros((128, 32), np.float32)
        xs = (4.5, 12.5, 20.0, 27.5) if k == 0 else (6.0, 16.0, 26.0)
        wmax = 7.5 if k == 0 else 9.0
        for x0 in xs:
            x0 += rr.uniform(-1, 1)
            L = rr.uniform(80, 126)
            sway = rr.uniform(-3, 3)
            y = rr.uniform(0, 3)
            while y < L:
                t = y / L
                w = max(2.0, wmax * (1 - 0.75 * t ** 1.3)) * rr.uniform(.7, .95)
                h = rr.uniform(8, 12) * (1 - 0.3 * t)
                cx = x0 + sway * t * t + rr.uniform(-1.2, 1.2)
                ang = rr.uniform(-22, 22)
                c = (int(round(cx)), int(round(y + h / 2)))
                ax = (max(1, int(round(w / 2))), max(2, int(round(h / 2))))
                cv2.ellipse(ca, c, ax, ang, 0, 360, 1, -1, lineType=8)
                m = np.zeros(ca.shape, np.uint8)
                cv2.ellipse(m, c, ax, ang, 0, 360, 1, -1, lineType=8)
                cr[m > 0] = P_WILLOW[rr.integers(0, 2)]                      # dark olive side / outline
                m2 = np.zeros(ca.shape, np.uint8)
                cl = (c[0] - max(1, ax[0] // 2), c[1] - 1)
                cv2.ellipse(m2, cl, (max(1, ax[0] - 1), max(1, ax[1] - 1)), ang, 0, 360, 1, -1, lineType=8)
                sel = (m2 > 0) & (m > 0)
                cr[sel] = P_WILLOW[rr.integers(2, 4)]                        # mid olive-ochre
                m3 = np.zeros(ca.shape, np.uint8)
                cv2.ellipse(m3, (cl[0] - 1, cl[1] - 1), (max(1, ax[0] // 2), max(1, ax[1] - 2)), ang, 0, 360, 1, -1,
                            lineType=8)
                sel = (m3 > 0) & (m > 0)
                cr[sel] = P_WILLOW[rr.integers(4, 6)]                        # light ochre highlight (left)
                y += h * rr.uniform(.4, .6)
            # pointed tip
            tip = (int(round(x0 + sway)), int(min(127, L + 3)))
            for d in range(4):
                yy = tip[1] - d
                if 0 <= yy < 128 and 0 <= tip[0] < 32:
                    ca[yy, tip[0]] = 1; cr[yy, tip[0]] = P_WILLOW[1]
        put('willow_strands_%d' % k, 192 + k * 32, 64, cr, ca)
    # alder foliage pad 64x48 x2: flat lobed "cloud" layer seen slightly from below/side - scalloped leafy outline,
    # lighter olive-ochre top edge, darker underside band, lobes shingled from top to bottom
    for k in range(2):
        rr = np.random.default_rng(380 + k)
        cr = np.zeros((48, 64, 3), np.float32); ca = np.zeros((48, 64), np.float32)
        top_y, bot_y = (10, 36) if k == 0 else (12, 34)
        lobes = []
        for i in range(26 if k == 0 else 22):
            u = rr.uniform(-1, 1)
            x = 32 + u * (27 if k == 0 else 25)
            prof = 1 - u * u                                     # pad is thicker in the middle
            y = rr.uniform(top_y + (1 - prof) * 9, bot_y - 4 - (1 - prof) * 6)
            lobes.append((y, x, rr.uniform(4.5, 7.5)))
        lobes.sort()
        for (y, x, r) in lobes:
            m = np.zeros(ca.shape, np.uint8)
            cv2.circle(m, (int(round(x)), int(round(y))), int(round(r)), 1, -1, lineType=8)
            ca[m > 0] = 1; cr[m > 0] = P_ALDER[1]
            m2 = np.zeros(ca.shape, np.uint8)
            cv2.circle(m2, (int(round(x - 1)), int(round(y - 1.5))), int(round(r - 1.5)), 1, -1, lineType=8)
            cr[(m2 > 0) & (m > 0)] = P_ALDER[2 + int(rr.integers(0, 2))]
            m3 = np.zeros(ca.shape, np.uint8)
            cv2.ellipse(m3, (int(round(x - 1.5)), int(round(y - r * .45))), (max(1, int(r * .6)), max(1, int(r * .35))),
                        0, 0, 360, 1, -1, lineType=8)
            cr[(m3 > 0) & (m > 0)] = P_ALDER[4 - int(rr.integers(0, 2))]   # light olive-ochre top of each lobe
            # leaf tips around the lobe -> scalloped / pointy outline
            for j in range(5):
                la = rr.uniform(math.pi * 0.9, math.pi * 2.1)      # mostly up and sideways
                tx, ty = x + math.cos(la) * (r + 4.0), y + math.sin(la) * (r + 4.0)
                b1 = (x + math.cos(la + .45) * r * .8, y + math.sin(la + .45) * r * .8)
                b2 = (x + math.cos(la - .45) * r * .8, y + math.sin(la - .45) * r * .8)
                tri = np.round(np.array([b1, (tx, ty), b2])).astype(np.int32)
                m = np.zeros(ca.shape, np.uint8); cv2.fillPoly(m, [tri], 1, lineType=8)
                ca[m > 0] = 1; cr[m > 0] = P_ALDER[3] if la > math.pi * 1.2 and la < math.pi * 1.8 else P_ALDER[2]
        # flat darker underside band with a few hanging leaf tips
        for x in range(64):
            col = ca[:, x] > 0
            if not col.any():
                continue
            yb = int(np.nonzero(col)[0].max())
            cut = min(yb, bot_y + int(rr.integers(-1, 2)))
            ca[cut + 1:, x] = 0
            for yy in range(max(0, cut - 4), cut + 1):
                if ca[yy, x] > 0:
                    cr[yy, x] = P_ALDER[0] if yy > cut - 2 else P_ALDER[1]
            if rr.random() < .18 and cut + 3 < 48:
                ca[cut + 1:cut + 3, x] = 1; cr[cut + 1:cut + 3, x] = P_ALDER[0]
        put('alder_clump_%d' % k, 0, 160 + k * 48, cr, ca)
    # hanging moss drips 64x32 (top edge attaches, drips down) - for roofs, branches, logs
    for k in range(2):
        rr = np.random.default_rng(390 + k)
        cr = np.zeros((32, 64, 3), np.float32); ca = np.zeros((32, 64), np.float32)
        x = 0
        while x < 64:
            w_ = rr.integers(2, 6); L = rr.integers(4, 30 if k == 0 else 16)
            for yi in range(L):
                ww = max(1, int(w_ * (1 - yi / L * .7)))
                c = P_MOSS[rr.integers(2, 6)] if yi < L - 2 else P_MOSS[1]
                for xi in range(x, min(64, x + ww)):
                    cr[yi, xi] = c; ca[yi, xi] = 1
            x += w_ + rr.integers(0, 3)
        cr[0:3] = P_MOSS[3]; ca[0:3] = 1
        put('moss_drip_%d' % k, (192, 128)[k], (32, 192)[k], cr, ca)
    # fishing net (torn) 64x64
    cr = np.zeros((64, 64, 3), np.float32) + hexes('#6b5d48')[0]; ca = np.zeros((64, 64), np.float32)
    for i in range(0, 64, 6):
        for j in range(64):
            jj = j + int(2 * math.sin(i * .3 + j * .1))
            if 0 <= jj < 64:
                ca[jj, i] = 1; ca[i, jj] = 1
    hole = pnoise(64, 64, 3, 395, 2) > .68
    ca[hole] = 0
    ca[44:, :] *= (np.random.default_rng(396).random((20, 64)) > np.linspace(0, .9, 20)[:, None])
    cr[ca > 0] = hexes('#5c4f3d', '#77684f')[np.random.default_rng(397).integers(0, 2, int((ca > 0).sum()))]
    put('net', 192, 192, cr, ca)
    # ragged hanging cloth card 32x48 (alpha, torn bottom)
    cr = np.zeros((32, 32, 3), np.float32); ca = np.ones((32, 32), np.float32)
    v = pnoise(32, 32, 3, 398, 3)
    cr[:] = quant(v * .99, hexes('#4f4438', '#6a5c4c', '#857563', '#9c8b76'))
    for x in range(32):
        cut = 20 + int(np.random.default_rng(399 + x).integers(0, 12))
        ca[cut:, x] = 0
    ca[pnoise(32, 32, 4, 400, 2) > .8] = 0
    put('rag_card', 128, 224, cr, ca)
    # ---- NEW regions (empty atlas space, 256-scale (64..192, 224..256)): single broad blades for 3D-bent strips
    # sedge_blade_0..3: 16x32 one blade each (base at the bottom edge, tip at the top), full card width = blade width
    for k in range(4):
        rr = np.random.default_rng(500 + k)
        cr = np.zeros((32, 16, 3), np.float32); ca = np.zeros((32, 16), np.float32)
        nseg = 14
        pts = [(7.5 + (0.6 * math.sin(s / nseg * 3 + k)), 31.4 - 31.0 * s / nseg) for s in range(nseg + 1)]
        wp = width_profile(nseg, 7.4 - (k % 2) * 0.6, keep=(0.7, 0.74, 0.66, 0.8)[k], base=0.85)
        draw_blade(cr, ca, pts, wp, PB_GREEN if k != 2 else PB_DRY, PB_TIP, (3, 2, 3, 2)[k],
                   tip_start=(0.78, 0.74, 0.82, 0.72)[k], seed=520 + k, blotch=0.45, torn=(k == 3), edge=False)
        put('sedge_blade_%d' % k, 64 + k * 16, 224, cr, ca)
    # cattail_leaf_0/1: 12x32 single long strap (0 pointed, 1 torn/snapped end)
    for k in range(2):
        cr = np.zeros((32, 12, 3), np.float32); ca = np.zeros((32, 12), np.float32)
        nseg = 16
        pts = [(5.5, 31.4 - 31.0 * s / nseg) for s in range(nseg + 1)]
        wp = width_profile(nseg, 5.2, keep=0.86 if k == 0 else 0.97, base=0.85)
        if k == 1:
            wp[-1] = 2.5
        draw_blade(cr, ca, pts, wp, PB_CAT, PB_TIP, 2 + k, tip_start=0.82 if k == 0 else 0.7, seed=540 + k,
                   blotch=0.55, torn=(k == 1))
        put('cattail_leaf_%d' % k, 160 + k * 12, 224, cr, ca)
    # reed_stem: 8x32 opaque stalk strip (vertical facet bands), for thin cattail stalk prisms
    cr = np.zeros((32, 8, 3), np.float32); ca = np.ones((32, 8), np.float32)
    rr = np.random.default_rng(560)
    for xi, ci in enumerate((1, 2, 3, 3, 4, 3, 2, 1)):
        cr[:, xi] = PB_CAT[ci]
    for yi in range(32):   # few darker brown blotches
        if rr.random() < .2:
            cr[yi, rr.integers(0, 8)] = PB_TIP[2]
    put('reed_stem', 184, 224, cr, ca)
    out_rgb = up2(rgb); out_a = up2(a)
    # bleed colour into transparent texels (avoid dark fringes with mipmaps)
    m = out_a > 0
    bled = out_rgb.copy()
    for _ in range(6):
        blur = cv2.blur(bled * m[..., None], (5, 5)); w_ = cv2.blur(m.astype(np.float32), (5, 5))
        fill = (~m) & (w_ > 0)
        bled[fill] = blur[fill] / w_[fill][:, None]
        m = m | fill
    save('T_Swamp_Plants.png', bled, out_a)

# ---------------------------------------------------------------- run
TILING = {}
def main():
    TILING['T_Swamp_Bark'] = bark(); save('T_Swamp_Bark.png', TILING['T_Swamp_Bark'])
    TILING['T_Swamp_BarkMossy'] = bark(seed=12, moss=0.5); save('T_Swamp_BarkMossy.png', TILING['T_Swamp_BarkMossy'])
    TILING['T_Swamp_Birch'] = birch(); save('T_Swamp_Birch.png', TILING['T_Swamp_Birch'])
    TILING['T_Swamp_Planks'] = planks(); save('T_Swamp_Planks.png', TILING['T_Swamp_Planks'])
    TILING['T_Swamp_LogWall'] = logwall(); save('T_Swamp_LogWall.png', TILING['T_Swamp_LogWall'])
    TILING['T_Swamp_Roof'] = roof(); save('T_Swamp_Roof.png', TILING['T_Swamp_Roof'])
    TILING['T_Swamp_Mud'] = mud(); save('T_Swamp_Mud.png', TILING['T_Swamp_Mud'])
    TILING['T_Swamp_Moss'] = moss(); save('T_Swamp_Moss.png', TILING['T_Swamp_Moss'])
    TILING['T_Swamp_Peat'] = peat(); save('T_Swamp_Peat.png', TILING['T_Swamp_Peat'])
    TILING['T_Swamp_Stone'] = stone(); save('T_Swamp_Stone.png', TILING['T_Swamp_Stone'])
    props_atlas(); plants_atlas()
    json.dump({'T_Swamp_Props.png': {'size': 512, 'regions': REG_PROPS},
               'T_Swamp_Plants.png': {'size': 512, 'regions': REG_PLANTS}},
              open(os.path.join(OUT, 'regions.json'), 'w'), indent=1)
    # overview
    files = ['T_Swamp_Bark', 'T_Swamp_BarkMossy', 'T_Swamp_Birch', 'T_Swamp_Planks', 'T_Swamp_LogWall', 'T_Swamp_Roof',
             'T_Swamp_Mud', 'T_Swamp_Moss']
    ov = Image.new('RGB', (256 * 4 + 512 * 2, 512), (60, 60, 60))
    for i, f in enumerate(files):
        im = Image.open(os.path.join(OUT, f + '.png')).convert('RGB').resize((256, 256), Image.NEAREST)
        ov.paste(im, ((i % 4) * 256, (i // 4) * 256))
    pr = Image.open(os.path.join(OUT, 'T_Swamp_Props.png')).convert('RGB'); ov.paste(pr, (1024, 0))
    pl = Image.open(os.path.join(OUT, 'T_Swamp_Plants.png'))
    bg = Image.new('RGBA', pl.size, (60, 60, 60, 255)); bg.alpha_composite(pl); ov.paste(bg.convert('RGB'), (1536, 0))
    ov.save(os.path.join(OUT, 'overview.png'))

if __name__ == '__main__':
    main()
