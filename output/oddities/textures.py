"""Oddity textures: 512x512 pixel-noise atlases for the payphone and the old television (ChestArt/brazier look:
blocky value noise posterised to a small muted palette, details painted into the colour, point-filtered)."""
import os, numpy as np
from PIL import Image

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'out')
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(23)

def noise(w, h, colours, block=4, spots=.9):
    gw, gh = w // block + 1, h // block + 1
    base = rng.random((gh, gw))
    blur = (base + np.roll(base, 1, 0) + np.roll(base, 1, 1) + np.roll(np.roll(base, 1, 0), 1, 1)) / 4
    sp = rng.random((gh, gw)) ** 6
    value = np.clip(blur * .55 + sp * spots, 0, .999)
    index = np.minimum((value ** 1.6 * len(colours)).astype(int), len(colours) - 1)
    tile = np.array(colours, dtype=np.uint8)[index]
    return np.kron(tile, np.ones((block, block, 1), dtype=np.uint8))[:h, :w]

# 5x7 glyphs for the sign (Cyrillic subset)
GLYPHS = {
    'Т': ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.."],
    'Е': ["#####", "#....", "#....", "####.", "#....", "#....", "#####"],
    'Л': ["..###", ".#..#", ".#..#", ".#..#", ".#..#", "#...#", "#...#"],
    'Ф': ["..#..", ".###.", "#.#.#", "#.#.#", ".###.", "..#..", "..#.."],
    'О': [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
    'Н': ["#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
}

def text(img, x, y, word, colour, scale=2, skew=None):
    cx = x
    for i, ch in enumerate(word):
        g = GLYPHS[ch]
        dy = 0 if skew is None else skew[i % len(skew)]
        for r, row in enumerate(g):
            for c, on in enumerate(row):
                if on == '#':
                    img[y + dy + r * scale:y + dy + (r + 1) * scale, cx + c * scale:cx + (c + 1) * scale] = colour
        cx += 6 * scale

def rect(img, x0, y0, x1, y1, colour):
    img[y0:y1, x0:x1] = colour

# ---------------- payphone ----------------
PHONE = {  # (x0, y0, x1, y1) pixels, origin top-left
    'blue':      (0, 0, 256, 256),
    'metal':     (256, 0, 384, 256),
    'concrete':  (384, 0, 512, 128),
    'dark':      (384, 128, 448, 192),
    'black':     (448, 128, 512, 192),
    'lamp':      (384, 192, 512, 256),
    'faceplate': (0, 256, 256, 512),
    'sign':      (256, 256, 512, 384),
    'rust':      (256, 384, 512, 512),
}
phone = np.zeros((512, 512, 3), np.uint8)
pal = {
    'blue': [(58, 92, 112), (52, 84, 104), (64, 100, 118), (82, 112, 120), (120, 110, 86)],
    'metal': [(112, 116, 112), (102, 106, 104), (124, 126, 120), (92, 86, 78), (132, 104, 78)],
    'concrete': [(126, 122, 112), (118, 114, 104), (134, 130, 118), (100, 98, 90)],
    'dark': [(46, 44, 46), (54, 52, 52), (40, 38, 40)],
    'black': [(30, 30, 32), (38, 38, 40), (26, 26, 28)],
    'lamp': [(214, 222, 196), (226, 232, 206), (198, 206, 180)],
    'faceplate': [(150, 152, 146), (140, 142, 138), (160, 160, 152), (118, 110, 98)],
    'sign': [(196, 168, 74), (186, 158, 68), (206, 178, 84), (150, 120, 60)],
    'rust': [(96, 60, 44), (110, 68, 46), (84, 54, 40), (126, 80, 50)],
}
for k, (x0, y0, x1, y1) in PHONE.items():
    phone[y0:y1, x0:x1] = noise(x1 - x0, y1 - y0, pal[k], 4 if k in ('blue', 'concrete', 'rust') else 2)
# chipped paint showing metal on the blue
for _ in range(26):
    x, y = rng.integers(0, 240), rng.integers(0, 240)
    rect(phone, x, y, x + rng.integers(4, 14), y + rng.integers(2, 8), (112, 116, 112))
# faceplate: little display, keypad 3x4, coin slot, card slot, bolts
fx, fy = 0, 256
rect(phone, fx + 40, fy + 22, fx + 216, fy + 70, (40, 54, 44))           # display
rect(phone, fx + 48, fy + 30, fx + 120, fy + 40, (110, 150, 96))           # display text bar
for r in range(4):
    for c in range(3):
        x0 = fx + 64 + c * 44; y0 = fy + 92 + r * 34
        rect(phone, x0, y0, x0 + 30, y0 + 22, (60, 60, 62))
        rect(phone, x0 + 2, y0 + 2, x0 + 28, y0 + 18, (176, 176, 168))
        rect(phone, x0 + 12, y0 + 6, x0 + 18, y0 + 14, (60, 60, 62))
rect(phone, fx + 196, fy + 96, fx + 206, fy + 160, (28, 28, 30))           # coin slot
rect(phone, fx + 20, fy + 236, fx + 236, fy + 246, (36, 36, 38))           # card slot
for (bx, by) in [(10, 10), (240, 10), (10, 240), (240, 240)]:
    rect(phone, fx + bx, fy + by, fx + bx + 6, fy + by + 6, (80, 78, 74))
# sign: dark-blue band with yellow letters, a little crooked
sx, sy = 256, 256
rect(phone, sx + 6, sy + 20, sx + 250, sy + 108, (44, 64, 104))
text(phone, sx + 22, sy + 36, 'ТЕЛЕФОН', (226, 196, 86), scale=5, skew=[0, 1, 0, 2, 1, 0, 1])
Image.fromarray(phone).save(os.path.join(OUT, 'T_Payphone.png'))

# ---------------- television ----------------
TV = {
    'veneer':  (0, 0, 256, 256),
    'bezel':   (256, 0, 512, 128),
    'plastic': (256, 128, 512, 256),
    'panel':   (0, 256, 128, 384),
    'knob':    (128, 256, 192, 320),
    'chrome':  (192, 256, 256, 320),
    'hole':    (128, 320, 256, 384),
    'legs':    (0, 384, 256, 512),
    'screen':  (256, 256, 512, 512),
}
tv = np.zeros((512, 512, 3), np.uint8)
tpal = {
    'veneer': [(104, 64, 38), (96, 58, 34), (114, 72, 42), (86, 52, 32), (132, 86, 50)],
    'bezel': [(186, 168, 132), (176, 158, 124), (196, 178, 140), (150, 132, 102)],
    'plastic': [(66, 54, 46), (58, 48, 42), (74, 62, 52), (88, 70, 54)],
    'panel': [(120, 104, 82), (110, 96, 76), (130, 112, 88)],
    'knob': [(40, 34, 30), (52, 44, 38), (70, 60, 50)],
    'chrome': [(160, 160, 154), (140, 140, 136), (184, 182, 172), (110, 104, 96)],
    'hole': [(22, 20, 20), (28, 26, 24)],
    'legs': [(70, 44, 28), (62, 40, 26), (80, 50, 32)],
    'screen': [(40, 52, 50), (46, 58, 56)],
}
for k, (x0, y0, x1, y1) in TV.items():
    tv[y0:y1, x0:x1] = noise(x1 - x0, y1 - y0, tpal[k], 4 if k in ('veneer', 'plastic', 'legs') else 2)
# wood grain stripes on the veneer
for y in range(0, 256, 2):
    if rng.random() < .22: tv[y, 0:256] = (82, 50, 30)
# speaker grille slots on the panel
for y in range(270, 380, 8):
    rect(tv, 12, y, 116, y + 3, (64, 54, 44))
Image.fromarray(tv).save(os.path.join(OUT, 'T_OldTV.png'))

# ---------------- TV screen pictures (CRT content) ----------------
# static: a dim blue-grey snow frame; the shader adds moving noise on top
snow = (rng.random((96, 128)) * 70 + 30).astype(np.uint8)
snow_rgb = np.stack([snow * .8, snow * .9, snow], -1).astype(np.uint8)
Image.fromarray(snow_rgb).save(os.path.join(OUT, 'T_OldTV_Static.png'))
# the picture after a knock: a huge pixel eye looking out, test-card stripes behind (weirdcore)
pic = np.zeros((96, 128, 3), np.uint8)
bars = [(190, 190, 186), (196, 188, 60), (60, 180, 186), (66, 176, 64), (176, 60, 170), (176, 52, 48), (52, 54, 170)]
for i, c in enumerate(bars):
    pic[:, i * 128 // 7:(i + 1) * 128 // 7] = c
yy, xx = np.mgrid[0:96, 0:128]
eye = ((xx - 64) / 46) ** 2 + ((yy - 48) / 24) ** 2 < 1
pic[eye] = (232, 228, 214)
iris = ((xx - 70) ** 2 + (yy - 50) ** 2) < 17 ** 2
pic[iris & eye] = (70, 110, 60)
pupil = ((xx - 71) ** 2 + (yy - 51) ** 2) < 8 ** 2
pic[pupil & eye] = (14, 12, 14)
pic[(xx - 75) ** 2 + (yy - 45) ** 2 < 9] = (250, 250, 250)
lid = (np.abs(((xx - 64) / 46) ** 2 + ((yy - 48) / 24) ** 2 - 1) < .12)
pic[lid] = (20, 16, 16)
for y in range(0, 96, 3): pic[y] = (pic[y] * .75).astype(np.uint8)
Image.fromarray(pic).save(os.path.join(OUT, 'T_OldTV_Eye.png'))
print('textures ok')
