"""PENUS teaser v4: the end glitch on the rendered frames, then the hard black (glitch_plan.py).
Run after a render: python3 teaser_glitch.py [frames folder]  (default hukaton/Logs/teaser-frames).
The clean frames of the glitch are kept in <folder>-clean, so running it again starts from them."""
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


def rgb_split(a, dx, dy=0):
    out = a.copy()
    out[..., 0] = np.roll(a[..., 0], (-dy, -dx), (0, 1))
    out[..., 2] = np.roll(a[..., 2], (dy, dx), (0, 1))
    return out


def bands(a, n, shift, hmin=6, hmax=70):
    out = a.copy()
    H = a.shape[0]
    for _ in range(n):
        y = rng.integers(0, H - hmax); h = rng.integers(hmin, hmax)
        out[y:y + h] = np.roll(out[y:y + h], int(rng.integers(-shift, shift)), 1)
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
            blk = (blk // 64) * 64 + 32                       # posterised macroblock
        elif kind == 1:
            blk = blk[..., [2, 0, 1]]                         # channels swapped
        else:
            blk = np.repeat(np.repeat(blk[::8, ::8], 8, 0), 8, 1)[:h, :w]   # smeared
        out[dy:dy + h, dx:dx + w] = blk
    return out


def scanlines(a, depth=0.25):
    out = a.astype(np.float32)
    out[::2] *= 1 - depth
    return out


def noise(a, amount):
    return a + rng.normal(0, amount, a.shape).astype(np.float32)


def finish(a):
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))


def main():
    os.makedirs(CLEAN, exist_ok=True)
    first = min(f for f, _ in G.PLAN)
    # A new render rewrites frame 0 (the glitch never touches it): then the clean copies are taken again.
    mark = os.path.join(CLEAN, "render.txt")
    render = str(os.path.getmtime(path(FOLDER, 0)))
    fresh = not os.path.exists(mark) or open(mark).read() != render
    for f in range(first, G.TOTAL):
        if fresh or not os.path.exists(path(CLEAN, f)):
            shutil.copy2(path(FOLDER, f), path(CLEAN, f))
    open(mark, "w").write(render)
    prev = None
    for f, kind in G.PLAN:
        a = np.asarray(Image.open(path(CLEAN, f)).convert("RGB")).astype(np.int16)
        k = (f - first + 1) / len(G.PLAN)                      # grows towards the cut
        if kind == "light":
            o = rgb_split(bands(a, 3, 60), 6)
        elif kind == "bands":
            o = rgb_split(bands(a, 9, 220), 12)
        elif kind == "freeze":
            o = rgb_split(np.roll(prev, int(rng.integers(-60, 60)), 0), int(10 + 20 * k), 2)
        elif kind == "blocks":
            o = rgb_split(blocks(bands(a, 6, 160), 40), 18)
        elif kind == "dark":
            o = bands(a // 8, 4, 300, 2, 10)
            for _ in range(3):
                y = rng.integers(0, a.shape[0] - 6)
                o[y:y + rng.integers(2, 6)] = 230
        elif kind == "heavy":
            o = np.roll(blocks(bands(a, 22, 420), 70), int(rng.integers(-220, 220)), 0)
            o = (rgb_split(o, 30, 4) // 40) * 40 + 20
        else:  # shred
            o = a.copy()
            y = 0
            while y < a.shape[0]:
                h = int(rng.integers(3, 14))
                o[y:y + h] = np.roll(a[y:y + h], int(rng.integers(-420, 420)), 1)
                y += h
            o = blocks(rgb_split(o, 40, 6), 90, 128, 64)
            o[rng.integers(0, a.shape[0] - 200):][:200] = 255 - o[rng.integers(0, a.shape[0] - 200):][:200]
        o = noise(scanlines(o, 0.18 + 0.25 * k), 6 + 18 * k)
        finish(o).save(path(FOLDER, f))
        prev = np.asarray(Image.open(path(FOLDER, f))).astype(np.int16)
    black = Image.new("RGB", Image.open(path(CLEAN, G.CUT)).size)
    for f in range(G.CUT, G.TOTAL):
        black.save(path(FOLDER, f))
    print("glitched", [f for f, _ in G.PLAN], "black", G.CUT, "-", G.TOTAL - 1)


if __name__ == "__main__":
    main()
