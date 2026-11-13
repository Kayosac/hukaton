"""Pixel-art screen overlays and the placeholder badge icon for the player debuffs.
Hard alpha steps, no smoothing (the game's "БЕЗ МЫЛА" rule). Writes into the Unity HUD art folder."""
import sys, numpy as np
from PIL import Image

OUT = sys.argv[1]
W, H = 384, 216
rng = np.random.default_rng(7)
yy, xx = np.mgrid[0:H, 0:W]
u = (xx + .5) / W * 2 - 1
v = (yy + .5) / H * 2 - 1

def noise(scale, seed):
    r = np.random.default_rng(seed)
    gw, gh = int(W / scale) + 2, int(H / scale) + 2
    g = r.random((gh, gw))
    big = np.array(Image.fromarray((g * 255).astype(np.uint8)).resize((W + 2 * scale, H + 2 * scale), Image.BILINEAR), float) / 255
    return big[:H, :W]

def steps(a, n=4):
    return np.floor(np.clip(a, 0, 1) * n + .0001) / n

def save(rgb, a, name):
    img = np.zeros((H, W, 4), np.uint8)
    img[..., :3] = np.array(rgb, np.uint8)
    img[..., 3] = (np.clip(a, 0, 1) * 255).astype(np.uint8)
    Image.fromarray(img, 'RGBA').save(f"{OUT}/{name}")

# Blind: a ragged black ring, a clear hole in the middle.
d = np.sqrt((u * 1.0) ** 2 + (v * 1.25) ** 2)
ragged = d + (noise(12, 1) - .5) * .22 + (noise(4, 2) - .5) * .08
a = steps((ragged - .5) / .5, 5)
save((0, 0, 0), a, "DebuffBlind.png")

# Edge glow: white, strongest at the edges (tinted per debuff in the HUD).
e = np.maximum(np.abs(u) ** 3, np.abs(v) ** 3)
edge = e + (noise(10, 3) - .5) * .25
save((255, 255, 255), steps((edge - .35) / .65, 4) * .9, "DebuffEdge.png")

# Cracks: jagged glass cracks running in from the edges and corners.
crack = np.zeros((H, W), float)
def walk(x, y, ang, length, width, seed):
    r = np.random.default_rng(seed)
    for _ in range(length):
        ix, iy = int(x), int(y)
        if 0 <= ix < W and 0 <= iy < H:
            crack[max(0, iy - width):iy + width + 1, max(0, ix - width):ix + width + 1] = 1
        ang += r.normal(0, .14)
        x += np.cos(ang); y += np.sin(ang)
        if r.random() < .035 and length > 20:
            walk(x, y, ang + r.choice([-1, 1]) * r.uniform(.5, 1.1), int(length * .45), 0, seed + int(r.integers(1000)))
starts = [(0, 0, .6), (W - 1, 0, 2.5), (0, H - 1, -.6), (W - 1, H - 1, -2.5), (W * .3, 0, 1.4), (W * .72, H - 1, -1.7),
          (0, H * .55, .1), (W - 1, H * .4, 3.0)]
for i, (x, y, ang) in enumerate(starts):
    walk(x, y, ang, 70, 1, 100 + i)
# Dark outline around the white lines so they read on light skies.
pad = np.pad(crack, 1)
outline = np.zeros_like(crack)
for dy in (-1, 0, 1):
    for dx in (-1, 0, 1):
        outline = np.maximum(outline, pad[1 + dy:H + 1 + dy, 1 + dx:W + 1 + dx])
img = np.zeros((H, W, 4), np.uint8)
img[outline > 0] = (40, 44, 52, 200)
img[crack > 0] = (235, 242, 255, 255)
Image.fromarray(img, 'RGBA').save(f"{OUT}/DebuffCracks.png")

# Placeholder badge icon: a white exclamation mark in a ragged circle, tinted per debuff in the HUD.
S = 32
ic = np.zeros((S, S, 4), np.uint8)
cy, cx = np.mgrid[0:S, 0:S]
dist = np.hypot(cy - 15.5, cx - 15.5)
ic[dist <= 14.5] = (30, 20, 20, 255)
ic[dist <= 13] = (255, 255, 255, 255)
ic[6:19, 14:18] = (30, 20, 20, 255)
ic[21:25, 14:18] = (30, 20, 20, 255)
Image.fromarray(ic, 'RGBA').save(f"{OUT}/DebuffPlaceholderIcon.png")
print("ok")
