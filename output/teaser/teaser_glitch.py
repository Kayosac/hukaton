"""PENUS teaser: the end glitch on the rendered frames, then the hard black (glitch_plan.py).
Run after a render: python3 teaser_glitch.py [frames folder]  (default hukaton/Logs/teaser-frames).
The clean frames it breaks are kept in <folder>-clean and taken again after a new render, so it can run again."""
import os, shutil, sys
import numpy as np
from PIL import Image
import glitch_plan as G

HERE = os.path.dirname(os.path.abspath(__file__))
FOLDER = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(HERE, "..", "..", "hukaton", "Logs", "teaser-frames"))
CLEAN = FOLDER.rstrip("/") + "-clean"
rng = np.random.default_rng(6560)


def path(folder, f):
    return os.path.join(folder, f"frame_{f:04d}.png")


def roll_rows(a, shift):
    H, W = a.shape[:2]
    idx = (np.arange(W)[None, :] - np.asarray(shift, int)[:, None]) % W
    return a[np.arange(H)[:, None], idx]


def rgb_split(a, dx, dy=0):
    out = a.copy()
    out[..., 0] = np.roll(a[..., 0], (-dy, -dx), (0, 1))
    out[..., 2] = np.roll(a[..., 2], (dy, dx), (0, 1))
    return out


def bands(a, n, shift, hmin=4, hmax=60):
    out = a.copy()
    H = a.shape[0]
    for _ in range(n):
        y = int(rng.integers(0, H - hmax)); h = int(rng.integers(hmin, hmax))
        out[y:y + h] = np.roll(out[y:y + h], int(rng.integers(-shift, shift + 1)), 1)
    return out


def blocks(a, n, w=96, h=48):
    out = a.copy()
    H, W = a.shape[:2]
    for _ in range(n):
        sy, sx = rng.integers(0, H - h), rng.integers(0, W - w)
        dy, dx = rng.integers(0, H - h), rng.integers(0, W - w)
        blk = a[sy:sy + h, sx:sx + w].copy()
        kind = rng.integers(0, 3)
        if kind == 0:
            blk = (blk // 64) * 64 + 32
        elif kind == 1:
            blk = blk[..., [2, 0, 1]]
        else:
            blk = np.repeat(np.repeat(blk[::8, ::8], 8, 0), 8, 1)[:h, :w]
        out[dy:dy + h, dx:dx + w] = blk
    return out


def smooth_noise(n, cells, amp):
    knots = rng.standard_normal(cells + 2)
    x = np.linspace(0, cells, n)
    return np.interp(x, np.arange(cells + 2), knots) * amp


def melt(a, prev, s, f):
    """The picture runs: rows wave sideways, columns drip down, colours bleed and trail."""
    H, W = a.shape[:2]
    y = np.arange(H)
    ph = f * 0.37
    shift = (70 * s ** 1.4) * np.sin(2 * np.pi * (y / 230 + ph)) + (30 * s ** 2) * np.sin(2 * np.pi * (y / 47 - ph * 1.7))
    o = roll_rows(a, shift)
    drip = np.clip(smooth_noise(W, 24, 1.0), 0, None) * (320 * s ** 1.8)            # columns sag downwards
    rows = np.clip(np.arange(H)[:, None] - drip[None, :], 0, H - 1).astype(int)
    o = o[rows, np.arange(W)[None, :]]
    o = rgb_split(o, int(6 + 46 * s), int(10 * s))
    if prev is not None:                                                               # the last frames smear on
        w = 0.65 * s ** 1.2
        trail = np.roll(prev, (int(4 + 14 * s), int(rng.integers(-6, 7))), (0, 1))
        o = (o * (1 - w) + trail * w).astype(np.int16)
    if s > 0.45:
        lv = max(4, int(48 - 44 * (s - 0.45) / 0.55))
        o = (o // (256 // lv)) * (256 // lv) + (128 // lv)
    return o


def glitch(a, prev, kind, x):
    if kind == "tick":                                                                 # barely there
        return rgb_split(bands(a, int(1 + 3 * x), int(8 + 40 * x), 2, 14), int(1 + 4 * x))
    if kind == "light":
        return rgb_split(bands(a, 3, 60), 6)
    if kind == "bands":
        return rgb_split(bands(a, int(5 + 10 * x), int(120 + 200 * x)), int(8 + 10 * x))
    if kind == "freeze":
        return rgb_split(np.roll(prev if prev is not None else a, int(rng.integers(-60, 61)), 0), int(8 + 20 * x), 2)
    if kind == "blocks":
        return rgb_split(blocks(bands(a, 6, 160), int(20 + 50 * x)), 18)
    if kind == "dark":
        o = bands(a // 8, 4, 300, 2, 10)
        for _ in range(3):
            yy = int(rng.integers(0, a.shape[0] - 6)); o[yy:yy + int(rng.integers(2, 6))] = 230
        return o
    if kind == "heavy":
        o = np.roll(blocks(bands(a, 22, 420), 70), int(rng.integers(-220, 221)), 0)
        return (rgb_split(o, 30, 4) // 40) * 40 + 20
    o = a.copy()                                                                       # shred
    yy = 0
    while yy < a.shape[0]:
        h = int(rng.integers(3, 14)); o[yy:yy + h] = np.roll(a[yy:yy + h], int(rng.integers(-420, 421)), 1); yy += h
    o = blocks(rgb_split(o, 40, 6), 90, 128, 64)
    y0 = int(rng.integers(0, a.shape[0] - 200)); o[y0:y0 + 200] = 255 - o[y0:y0 + 200]
    return o


def main():
    os.makedirs(CLEAN, exist_ok=True)
    first = G.START
    mark = os.path.join(CLEAN, "render.txt")
    render = str(os.path.getmtime(path(FOLDER, 0)))
    fresh = not os.path.exists(mark) or open(mark).read() != render
    for f in range(first, G.TOTAL):
        if fresh or not os.path.exists(path(CLEAN, f)):
            shutil.copy2(path(FOLDER, f), path(CLEAN, f))
    open(mark, "w").write(render)
    by_frame = {}
    for f, kind, s in G.PLAN:
        by_frame.setdefault(f, []).append((kind, s))
    prev = None
    for f in range(first, G.CUT):
        a = np.asarray(Image.open(path(CLEAN, f)).convert("RGB")).astype(np.int16)
        events = by_frame.get(f, [])
        o = a
        for kind, s in events:
            if kind == "melt":
                o = melt(o, prev, s, f)
        for kind, s in events:
            if kind != "melt":
                o = glitch(o, prev, kind, s)
        x = (f - first) / (G.CUT - first)
        if events or x > 0.5:
            o = o.astype(np.float32)
            o[::2] *= 1 - (0.08 + 0.3 * x * x)                                       # scanlines creep in
            o += rng.normal(0, 2 + 16 * x * x, o.shape).astype(np.float32)
        out = np.clip(o, 0, 255).astype(np.uint8)
        Image.fromarray(out).save(path(FOLDER, f))
        prev = out.astype(np.int16)
    black = Image.new("RGB", Image.open(path(CLEAN, G.CUT)).size)
    for f in range(G.CUT, G.TOTAL):
        black.save(path(FOLDER, f))
    kinds = {}
    for _, k, _ in G.PLAN: kinds[k] = kinds.get(k, 0) + 1
    print("glitched", G.START, "-", G.CUT - 1, kinds, "black", G.CUT, "-", G.TOTAL - 1)


if __name__ == "__main__":
    main()
