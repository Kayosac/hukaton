"""Oddity textures v2 (after the user's concepts): 512x512 point-filtered atlases in the ChestArt look — flat muted
paint broken by blocky rust patches, details (keypad, slots, handset sign, dial ticks, grille) painted in."""
import os, numpy as np
from PIL import Image

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'out')
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(23)

def field(h, w, block=4, octaves=((1, .6), (3, .3), (8, .1))):
    """Blocky value noise in [0,1]: a few scales of random blocks, upsampled by kron (hard pixel edges)."""
    acc = np.zeros((h, w))
    for scale, amp in octaves:
        b = block * scale
        g = rng.random((h // b + 2, w // b + 2))
        up = np.kron(g, np.ones((b, b)))[:h, :w]
        acc += up * amp
    fine = np.kron(rng.random((h // block + 1, w // block + 1)), np.ones((block, block)))[:h, :w]
    return np.clip(acc * .85 + fine * .15, 0, 1)

def paint(h, w, base, light, dark, rust, rust_light, rust_amount=.3, block=4, streak=False):
    """Base paint with lighter/darker mottling and rust patches where the noise is high."""
    n = field(h, w, block)
    m = field(h, w, block, ((1, 1),))
    img = np.empty((h, w, 3), np.uint8)
    img[:] = base
    img[m > .66] = light
    img[m < .22] = dark
    r = field(h, w, block, ((3, .45), (7, .45), (1, .1)))
    if streak:  # rust running down from the top edge
        r = np.clip(r + np.linspace(.25, -.15, h)[:, None], 0, 1)
    thr = 1 - rust_amount
    img[r > thr] = rust
    img[r > thr + (1 - thr) * .45] = rust_light
    img[(r > thr - .04) & (r <= thr)] = dark       # dark rim around the rust
    return img

def rect(img, x0, y0, x1, y1, colour):
    img[y0:y1, x0:x1] = colour

BEIGE = dict(base=(196, 170, 118), light=(214, 190, 138), dark=(166, 140, 96), rust=(142, 74, 40), rust_light=(176, 98, 52))
RUST = dict(base=(132, 66, 38), light=(150, 80, 44), dark=(104, 52, 34), rust=(180, 104, 56), rust_light=(196, 150, 100))
OLIVE = dict(base=(118, 116, 78), light=(134, 130, 90), dark=(96, 94, 64), rust=(150, 92, 52), rust_light=(184, 150, 98))
GREY = dict(base=(88, 84, 76), light=(104, 98, 88), dark=(64, 60, 56), rust=(120, 76, 50), rust_light=(140, 96, 64))

# ---------------- payphone ----------------
PHONE = {
    'beige':     (0, 0, 256, 256),
    'rust':      (256, 0, 512, 128),
    'olive':     (256, 128, 384, 384),
    'grey':      (384, 128, 512, 256),
    'phoneface': (0, 256, 256, 512),
    'sign':      (384, 256, 512, 320),
    'handset':   (384, 320, 448, 384),
    'door':      (448, 320, 512, 384),
    'lamp':      (384, 384, 448, 416),
    'bolt':      (448, 384, 512, 416),
    'base':      (256, 384, 384, 512),
    'patch':     (384, 416, 512, 512),
}
phone = np.zeros((512, 512, 3), np.uint8)
def P(name, pal, amount=.3, block=4, streak=False):
    x0, y0, x1, y1 = PHONE[name]
    phone[y0:y1, x0:x1] = paint(y1 - y0, x1 - x0, **pal, rust_amount=amount, block=block, streak=streak)
P('beige', BEIGE, .22, 5, streak=True)
P('rust', RUST, .25, 5)
P('olive', OLIVE, .22, 5, streak=True)
P('grey', GREY, .15, 2)
P('phoneface', GREY, .12, 4)
P('sign', BEIGE, .22, 2)
P('handset', dict(base=(92, 60, 46), light=(110, 72, 54), dark=(70, 46, 36), rust=(120, 70, 46), rust_light=(130, 84, 58)), .1, 2)
P('door', OLIVE | dict(base=(150, 140, 100), light=(166, 156, 112), dark=(124, 116, 84)), .18, 2)
P('lamp', dict(base=(226, 230, 200), light=(240, 242, 214), dark=(204, 208, 180), rust=(200, 196, 170), rust_light=(210, 206, 180)), .05, 2)
P('bolt', RUST, .4, 2)
P('base', OLIVE, .45)
P('patch', BEIGE | dict(base=(206, 186, 140), light=(222, 204, 158)), .2, 2)
# phone face (front, 256x256, top-left origin): coin slot, keypad 3x4, return slot, screws
fx, fy = 0, 256
rect(phone, fx + 6, fy + 6, fx + 250, fy + 250, (70, 66, 60)); phone[fy + 10:fy + 246, fx + 10:fx + 246] = paint(236, 236, **GREY, rust_amount=.12, block=4)
rect(phone, fx + 168, fy + 26, fx + 196, fy + 82, (200, 186, 150)); rect(phone, fx + 176, fy + 34, fx + 188, fy + 76, (28, 26, 24))     # coin slot
rect(phone, fx + 128, fy + 98, fx + 232, fy + 214, (60, 58, 54))                                                          # keypad plate
for r in range(4):
    for c in range(3):
        x0 = fx + 136 + c * 32; y0 = fy + 104 + r * 27
        rect(phone, x0, y0, x0 + 24, y0 + 20, (120, 104, 78)); rect(phone, x0 + 2, y0 + 2, x0 + 22, y0 + 16, (200, 180, 140))
rect(phone, fx + 140, fy + 222, fx + 220, fy + 248, (150, 140, 116)); rect(phone, fx + 150, fy + 228, fx + 210, fy + 242, (26, 24, 22))  # return slot
for (bx, by) in ((14, 14), (234, 14), (14, 234), (234, 234)):
    rect(phone, fx + bx, fy + by, fx + bx + 8, fy + by + 8, (120, 110, 90))
# sign: dark handset glyph on beige (128x64)
sx, sy = 384, 256
gly = (52, 40, 34)
rect(phone, sx + 42, sy + 22, sx + 86, sy + 30, gly)            # bar
rect(phone, sx + 34, sy + 26, sx + 46, sy + 42, gly)            # left cup
rect(phone, sx + 82, sy + 26, sx + 94, sy + 42, gly)            # right cup
rect(phone, sx + 30, sy + 40, sx + 50, sy + 46, gly); rect(phone, sx + 78, sy + 40, sx + 98, sy + 46, gly)
# lamp glow strip and door keyhole / hinges
rect(phone, 448 + 44, 320 + 30, 448 + 52, 320 + 40, (40, 34, 30))
rect(phone, 448 + 4, 320 + 10, 448 + 10, 320 + 22, (110, 100, 80)); rect(phone, 448 + 4, 320 + 42, 448 + 10, 320 + 54, (110, 100, 80))
Image.fromarray(phone).save(os.path.join(OUT, 'T_Payphone.png'))

# ---------------- television ----------------
TV = {
    'casing':   (0, 0, 256, 256),
    'bezel':    (256, 0, 512, 128),
    'back':     (256, 128, 512, 256),
    'panel':    (0, 256, 128, 512),
    'knob':     (128, 256, 192, 320),
    'olive':    (192, 256, 256, 320),
    'hole':     (128, 320, 192, 384),
    'rod':      (192, 320, 256, 384),
    'feet':     (128, 384, 256, 512),
    'red':      (256, 256, 288, 288),
    'cord':     (288, 256, 320, 288),
    'dark':     (320, 256, 352, 288),
}
tv = np.zeros((512, 512, 3), np.uint8)
def T(name, pal, amount=.3, block=4, streak=False):
    x0, y0, x1, y1 = TV[name]
    tv[y0:y1, x0:x1] = paint(y1 - y0, x1 - x0, **pal, rust_amount=amount, block=block, streak=streak)
CASING = dict(base=(126, 62, 36), light=(142, 74, 42), dark=(100, 48, 30), rust=(166, 96, 52), rust_light=(190, 130, 76))
BACK = dict(base=(118, 92, 64), light=(132, 104, 72), dark=(96, 74, 52), rust=(140, 78, 44), rust_light=(160, 100, 58))
T('casing', CASING, .28, 5)
T('bezel', BEIGE, .16, 3)
T('back', BACK, .3)
T('panel', BEIGE, .14, 3)
T('knob', dict(base=(112, 74, 52), light=(130, 88, 62), dark=(84, 56, 40), rust=(140, 86, 56), rust_light=(150, 98, 66)), .1, 2)
T('olive', OLIVE, .3, 2)
T('hole', dict(base=(40, 36, 32), light=(48, 44, 38), dark=(30, 28, 26), rust=(60, 46, 36), rust_light=(70, 52, 40)), .05, 2)
T('rod', dict(base=(160, 146, 120), light=(180, 166, 136), dark=(130, 116, 94), rust=(140, 84, 50), rust_light=(160, 100, 60)), .3, 2)
T('feet', dict(base=(112, 80, 54), light=(126, 92, 62), dark=(88, 62, 42), rust=(140, 84, 50), rust_light=(156, 100, 60)), .25, 4)
rect(tv, 256, 256, 288, 288, (176, 52, 40)); rect(tv, 262, 262, 282, 282, (196, 64, 48))
rect(tv, 288, 256, 320, 288, (58, 52, 46))
rect(tv, 320, 256, 352, 288, (34, 30, 28))
# panel (128x256, front): dial ticks around the upper knob, grille slots, the red button sits on its own box
px, py = 0, 256
for k in range(7):
    a = np.radians(200 - k * 40)
    cx, cy = px + 64 + int(np.cos(a) * 44), py + 52 - int(np.sin(a) * 44)
    rect(tv, cx - 2, cy - 2, cx + 3, cy + 3, (110, 72, 46))
for i in range(6):
    y = py + 150 + i * 13
    rect(tv, px + 18, y, px + 110, y + 6, (70, 52, 38))
# back vent slots painted on the back region: a few horizontal slots on top
bx, by = 256, 128
for i in range(5):
    y = by + 20 + i * 12
    rect(tv, bx + 60, y, bx + 196, y + 5, (54, 40, 30))
Image.fromarray(tv).save(os.path.join(OUT, 'T_OldTV.png'))

# ---------------- screen pictures ----------------
snow = (rng.random((96, 128)) * 50 + 40).astype(np.uint8)
stripes = np.repeat((np.arange(96) % 4 < 2)[:, None], 128, 1) * 14
Image.fromarray(np.stack([snow * .85 + stripes, snow * .9 + stripes, snow * .75 + stripes], -1).clip(0, 255).astype(np.uint8)).save(os.path.join(OUT, 'T_OldTV_Static.png'))
pic = np.zeros((96, 128, 3), np.uint8)
bars = [(190, 190, 186), (196, 188, 60), (60, 180, 186), (66, 176, 64), (176, 60, 170), (176, 52, 48), (52, 54, 170)]
for i, c in enumerate(bars): pic[:, i * 128 // 7:(i + 1) * 128 // 7] = c
yy, xx = np.mgrid[0:96, 0:128]
eye = ((xx - 64) / 46) ** 2 + ((yy - 48) / 24) ** 2 < 1
pic[eye] = (232, 228, 214)
pic[(((xx - 70) ** 2 + (yy - 50) ** 2) < 17 ** 2) & eye] = (70, 110, 60)
pic[(((xx - 71) ** 2 + (yy - 51) ** 2) < 8 ** 2) & eye] = (14, 12, 14)
pic[(xx - 75) ** 2 + (yy - 45) ** 2 < 9] = (250, 250, 250)
pic[np.abs(((xx - 64) / 46) ** 2 + ((yy - 48) / 24) ** 2 - 1) < .12] = (20, 16, 16)
for y in range(0, 96, 3): pic[y] = (pic[y] * .75).astype(np.uint8)
Image.fromarray(pic).save(os.path.join(OUT, 'T_OldTV_Eye.png'))
print('textures ok')
