"""Pixel art for the PC mini-game «Закрой окно» (virus-ad popups on the backroom PC).

Everything is drawn 1:1 for the PC's 640x480 screen: hard pixels, no antialiasing.
Run without arguments: writes PNGs (and .meta files with fresh guids, only if missing) into
hukaton/Assets/Computer/Art/Popups/ and a contact sheet output/pc-popups/preview.png.
Optional: python3 make_popup_art.py <out dir>  (PNGs only, no metas).
"""
import os, sys, math, random, uuid
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
UI = os.path.join(ROOT, 'hukaton', 'Assets', 'Computer', 'Art', 'Ui')
DEFAULT_OUT = os.path.join(ROOT, 'hukaton', 'Assets', 'Computer', 'Art', 'Popups')
OUT = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_OUT
WRITE_META = len(sys.argv) <= 1
os.makedirs(OUT, exist_ok=True)

W, H = 80, 60


def hx(c, a=255):
    c = c.lstrip('#'); return (int(c[0:2], 16), int(c[2:4], 16), int(c[4:6], 16), a)


def lerp(a, b, t): return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(4))


INK = hx('#1A1020')
WHITE = hx('#FFFFFF')
BAYER = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) / 16.0

# ---------------------------------------------------------------- masks


def M(fn, w=W, h=H):
    m = Image.new('L', (w, h), 0); fn(ImageDraw.Draw(m)); return np.array(m) > 0


def ell(box, w=W, h=H): return M(lambda d: d.ellipse(box, fill=255), w, h)
def rect(box, w=W, h=H): return M(lambda d: d.rectangle(box, fill=255), w, h)
def poly(pts, w=W, h=H): return M(lambda d: d.polygon(pts, fill=255), w, h)
def line(pts, width=1, w=W, h=H): return M(lambda d: d.line(pts, fill=255, width=width), w, h)


def shift(m, dx, dy):
    r = np.zeros_like(m); h, w = m.shape
    ys, yd = (slice(0, h - dy), slice(dy, h)) if dy >= 0 else (slice(-dy, h), slice(0, h + dy))
    xs, xd = (slice(0, w - dx), slice(dx, w)) if dx >= 0 else (slice(-dx, w), slice(0, w + dx))
    r[yd, xd] = m[ys, xs]; return r


def erode(m): return m & shift(m, 1, 0) & shift(m, -1, 0) & shift(m, 0, 1) & shift(m, 0, -1)
def dilate(m): return m | shift(m, 1, 0) | shift(m, -1, 0) | shift(m, 0, 1) | shift(m, 0, -1)
def dilate8(m): return dilate(m) | shift(m, 1, 1) | shift(m, -1, -1) | shift(m, 1, -1) | shift(m, -1, 1)


def rows(y0, y1, w=W, h=H):
    m = np.zeros((h, w), bool); m[max(0, y0):max(0, y1 + 1)] = True; return m


def cols(x0, x1, w=W, h=H):
    m = np.zeros((h, w), bool); m[:, max(0, x0):max(0, x1 + 1)] = True; return m


def ascii_mask(art, x, y, w=W, h=H, ch='X', scale=1):
    m = np.zeros((h, w), bool)
    for j, r in enumerate(art):
        for i, c in enumerate(r):
            if c in ch:
                for sy in range(scale):
                    for sx in range(scale):
                        X, Y = x + i * scale + sx, y + j * scale + sy
                        if 0 <= X < w and 0 <= Y < h: m[Y, X] = True
    return m


def dither(t, ys=None, xs=None):
    """Boolean field: True where value t (array or scalar per-pixel) beats the Bayer threshold."""
    h, w = t.shape
    th = np.tile(BAYER, (h // 4 + 1, w // 4 + 1))[:h, :w]
    return t > th

# ---------------------------------------------------------------- layer


class L:
    def __init__(s, w=W, h=H): s.px = np.zeros((h, w, 4), np.uint8); s.w, s.h = w, h
    def put(s, m, c): s.px[m] = c; return s

    def part(s, m, fill, lt=None, dk=None, line=INK, lw=1):
        inner = erode(m) if line is not None else m
        s.put(m, fill)
        if lt is not None:
            hl = inner & ~shift(inner, 1, 1)
            if lw > 1: hl |= inner & ~shift(inner, 2, 2)
            s.put(hl, lt)
        if dk is not None:
            s.put(inner & ~shift(inner, -1, -1), dk)
        if line is not None: s.put(m & ~inner, line)
        return s

    def alpha(s): return s.px[..., 3] > 0

    def outline(s, c=INK, diag=False):
        a = s.alpha(); o = (dilate8(a) if diag else dilate(a)) & ~a; s.px[o] = c; return s

    def over(s, other, dx=0, dy=0):
        src = other.px
        if dx or dy:
            src = np.zeros_like(other.px); h, w = s.h, s.w
            ys, yd = (slice(0, h - dy), slice(dy, h)) if dy >= 0 else (slice(-dy, h), slice(0, h + dy))
            xs, xd = (slice(0, w - dx), slice(dx, w)) if dx >= 0 else (slice(-dx, w), slice(0, w + dx))
            src[yd, xd] = other.px[ys, xs]
        m = src[..., 3] > 0; s.px[m] = src[m]; return s

    def img(s): return Image.fromarray(s.px, 'RGBA')

# ---------------------------------------------------------------- tiny font (digits and a few symbols)


FONT = {
    '0': ["XXX", "X.X", "X.X", "X.X", "XXX"], '1': [".X.", "XX.", ".X.", ".X.", "XXX"],
    '2': ["XXX", "..X", "XXX", "X..", "XXX"], '3': ["XXX", "..X", ".XX", "..X", "XXX"],
    '4': ["X.X", "X.X", "XXX", "..X", "..X"], '5': ["XXX", "X..", "XXX", "..X", "XXX"],
    '6': ["XXX", "X..", "XXX", "X.X", "XXX"], '7': ["XXX", "..X", ".X.", ".X.", ".X."],
    '8': ["XXX", "X.X", "XXX", "X.X", "XXX"], '9': ["XXX", "X.X", "XXX", "..X", "XXX"],
    '!': ["X", "X", "X", ".", "X"], '?': ["XXX", "..X", ".XX", "...", ".X."],
    '%': ["X.X", "..X", ".X.", "X..", "X.X"], 'x': ["...", "X.X", ".X.", "X.X", "..."],
    '-': ["...", "...", "XXX", "...", "..."], '+': ["...", ".X.", "XXX", ".X.", "..."],
    '.': [".", ".", ".", ".", "X"], ' ': [".", ".", ".", ".", "."],
}


def text_mask(s, x, y, scale=1, gap=1, w=W, h=H):
    m = np.zeros((h, w), bool)
    for ch in s:
        g = FONT[ch]; m |= ascii_mask(g, x, y, w, h, scale=scale); x += (len(g[0]) + gap) * scale
    return m


def text_w(s, scale=1, gap=1): return sum((len(FONT[c][0]) + gap) * scale for c in s) - gap * scale


def text(layer, s, x, y, col, scale=1, outline=INK, shadow=None):
    m = text_mask(s, x, y, scale, w=layer.w, h=layer.h)
    if shadow is not None: layer.put(shift(m, 1, 1) & ~m, shadow)
    if outline is not None: layer.put(dilate8(m) & ~m, outline)
    layer.put(m, col)

# ---------------------------------------------------------------- stamps


HEART7 = [".XX.XX.", "XXXXXXX", "XXXXXXX", ".XXXXX.", "..XXX..", "...X..."]
HEART5 = ["XX.XX", "XXXXX", ".XXX.", "..X.."]
HEART13 = ["..XXX...XXX..", ".XXXXX.XXXXX.", "XXXXXXXXXXXXX", "XXXXXXXXXXXXX", "XXXXXXXXXXXXX", ".XXXXXXXXXXX.",
           "..XXXXXXXXX..", "...XXXXXXX...", "....XXXXX....", ".....XXX.....", "......X......"]
COIN = [".XXX.", "XXXXX", "XXXXX", "XXXXX", ".XXX."]
NOTE = ["..XX", "..XX", "..X.", "..X.", "XXX.", "XXX.", ".X.."]
NOTE = ["...XX", "...XX", "...X.", "...X.", ".XXX.", "XXXX.", ".XX.."]
SKULL = [".XXXXX.", "XXXXXXX", "X..X..X", "X..X..X", "XXXX XXX"[:7], ".XX.XX.", ".X.X.X."]
SKULL = [".XXXXXXX.", "XXXXXXXXX", "XX..X..XX", "XX..X..XX", "XXXXXXXXX", "XXXX.XXXX", ".XXXXXXX.", ".X.X.X.X.", ".XXXXXXX."]
BONE = ["XX....XX", "XXXXXXXX", "XXXXXXXX", "XX....XX"]


def stamp(layer, art, x, y, fill, line=INK, lt=None, dk=None):
    m = ascii_mask(art, x, y, layer.w, layer.h)
    layer.part(dilate(m) if line is not None else m, fill, lt, dk, line)


def coin(layer, x, y, light=False):
    m = ascii_mask(COIN, x, y, layer.w, layer.h)
    layer.put(dilate(m) & ~m, hx('#5A3A04'))
    layer.put(m, hx('#F3C548')); layer.put(m & ~shift(m, 1, 1), hx('#FFF2A0'))
    layer.put(m & ~shift(m, -1, -1), hx('#C08A12'))
    if 0 <= y + 2 < layer.h and 0 <= x + 2 < layer.w: layer.px[y + 2, x + 2] = hx('#C08A12')


def sparkle(layer, x, y, kind, col=hx('#FFF6A8'), core=WHITE):
    def p(X, Y, c):
        if 0 <= X < layer.w and 0 <= Y < layer.h: layer.px[Y, X] = c
    if kind == 0:
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)): p(x + dx, y + dy, col)
        p(x, y, core)
    elif kind == 1:
        for k in (1, 2):
            for dx, dy in ((k, 0), (-k, 0), (0, k), (0, -k)): p(x + dx, y + dy, col if k == 2 else core)
        p(x, y, core)
    else:
        for k in (1, 2, 3):
            for dx, dy in ((k, 0), (-k, 0), (0, k), (0, -k)): p(x + dx, y + dy, col if k > 1 else core)
        for dx, dy in ((1, 1), (-1, -1), (1, -1), (-1, 1)): p(x + dx, y + dy, col)
        p(x, y, core)


def sparkles(layer, rnd, n, avoid=None, cols_=(hx('#FFF6A8'),), kinds=(0, 0, 1, 1, 2), margin=3):
    for _ in range(n):
        for _try in range(30):
            x, y = rnd.randint(margin, layer.w - 1 - margin), rnd.randint(margin, layer.h - 1 - margin)
            if avoid is None or not avoid[y, x]: break
        sparkle(layer, x, y, rnd.choice(kinds), rnd.choice(cols_))

# ---------------------------------------------------------------- backgrounds


def bg_fill(c): l = L(); l.px[:] = c; return l


def bg_rays(c1, c2, cx=40, cy=30, n=14, phase=0.0):
    l = L(); yy, xx = np.mgrid[0:H, 0:W]
    ang = (np.arctan2(yy - cy + .5, xx - cx + .5) / (2 * np.pi) + 1) % 1
    k = (np.floor(ang * n * 2 + phase * 2).astype(int)) % 2 == 0
    l.px[k] = c1; l.px[~k] = c2; return l


def bg_checker(c1, c2, s=6, ox=0, oy=0):
    l = L(); yy, xx = np.mgrid[0:H, 0:W]
    k = (((xx + ox) // s + (yy + oy) // s) % 2) == 0
    l.px[k] = c1; l.px[~k] = c2; return l


def bg_stripes(c1, c2, s=5, ox=0, slope=1):
    l = L(); yy, xx = np.mgrid[0:H, 0:W]
    k = (((xx + slope * yy + ox) // s) % 2) == 0
    l.px[k] = c1; l.px[~k] = c2; return l


def bg_vgrad(stops, dith=True):
    l = L()
    for y in range(H):
        t = y / (H - 1)
        for j in range(len(stops) - 1):
            if stops[j][0] <= t <= stops[j + 1][0]:
                u = (t - stops[j][0]) / max(1e-6, stops[j + 1][0] - stops[j][0])
                if dith:
                    for x in range(W):
                        l.px[y, x] = stops[j + 1][1] if u > BAYER[y % 4, x % 4] else stops[j][1]
                else: l.px[y] = lerp(stops[j][1], stops[j + 1][1], u)
                break
    return l


def bg_dots(base, dot, s=8, ox=0, oy=0, big=False):
    l = bg_fill(base)
    for y in range(-s, H + s, s):
        for x in range(-s, W + s, s):
            X = x + ox + ((y // s) % 2) * (s // 2); Y = y + oy
            m = ell([X - 1, Y - 1, X + 1, Y + 1]) if not big else ell([X - 2, Y - 2, X + 2, Y + 2])
            l.put(m, dot)
    return l


def marquee(layer, c_line, c_on, c_off, f, inset=0):
    """2-px banner frame: outer line + a chasing run of 'bulbs' that shifts each frame."""
    i = inset; w, h = layer.w, layer.h
    ring = []
    for x in range(i + 1, w - 1 - i): ring.append((x, i + 1))
    for y in range(i + 1, h - 1 - i): ring.append((w - 2 - i, y))
    for x in range(w - 2 - i, i, -1): ring.append((x, h - 2 - i))
    for y in range(h - 2 - i, i, -1): ring.append((i + 1, y))
    for k, (x, y) in enumerate(ring): layer.px[y, x] = c_on if ((k + f * 3) // 3) % 2 == 0 else c_off
    for x in range(i, w - i): layer.px[i, x] = c_line; layer.px[h - 1 - i, x] = c_line
    for y in range(i, h - i): layer.px[y, i] = c_line; layer.px[y, w - 1 - i] = c_line


def frame_plain(layer, c):
    layer.px[0, :] = c; layer.px[-1, :] = c; layer.px[:, 0] = c; layer.px[:, -1] = c

# ================================================================ ADS
GOLD = (hx('#F2C230'), hx('#FFF07A'), hx('#B07A10'))
GREEN = (hx('#7DBA3A'), hx('#BFE36A'), hx('#4E7A1E'))


def mirror_x(m, cx2):
    """Mirror mask around x = cx2/2 (cx2 = 2*axis)."""
    r = np.zeros_like(m); ys, xs = np.nonzero(m)
    xs2 = cx2 - xs; ok = (xs2 >= 0) & (xs2 < m.shape[1]); r[ys[ok], xs2[ok]] = True; return r


def ad_prize(f):
    bg = bg_rays(hx('#FF2E6E'), hx('#FFD23F'), 40, 26, 12, 0) if f == 0 else bg_rays(hx('#FFD23F'), hx('#FF7A1F'), 40, 26, 12, .5)
    s = L(); dy = -1 if f else 0
    # goblin ears instead of handles
    ear = poly([(28, 15 + dy), (12, 6 + dy), (16, 12 + dy), (14, 15 + dy), (28, 23 + dy)])
    s.part(ear, *GREEN); s.part(ear | mirror_x(ear, 80), *GREEN)
    inner = poly([(26, 17 + dy), (17, 10 + dy), (19, 14 + dy), (26, 20 + dy)])
    s.put(inner | mirror_x(inner, 80), hx('#E58AA0'))
    bowl = ell([24, -6 + dy, 56, 34 + dy]) & rows(10 + dy, 60)
    s.part(bowl, *GOLD)
    rim = ell([23, 7 + dy, 57, 14 + dy]); s.part(rim, GOLD[0], GOLD[1], GOLD[2])
    s.put(ell([26, 9 + dy, 54, 12 + dy]), hx('#7A4A08'))
    s.put(ell([27, 9 + dy, 53, 11 + dy]) & rows(9 + dy, 9 + dy), hx('#5A3404'))
    # face: happy closed eyes, grin, tooth
    for ex in (33, 44):
        s.put(line([(ex, 20 + dy), (ex + 1, 19 + dy), (ex + 2, 20 + dy)]), INK)
    s.put(line([(35, 24 + dy), (37, 26 + dy), (42, 26 + dy), (44, 24 + dy)]), INK)
    s.put(rect([38, 27 + dy, 39, 27 + dy]), WHITE)
    s.put(rect([30, 22 + dy, 31, 22 + dy]) | rect([48, 22 + dy, 49, 22 + dy]), hx('#FF8A5A'))
    # shine
    s.put(line([(28, 16 + dy), (28, 21 + dy), (30, 25 + dy)]), hx('#FFFBD8'))
    stem = rect([38, 33 + dy, 42, 39 + dy]); s.part(stem, *GOLD)
    s.part(ell([35, 34 + dy, 45, 38 + dy]), *GOLD)
    base = poly([(33, 40 + dy), (47, 40 + dy), (49, 44 + dy), (31, 44 + dy)]); s.part(base, *GOLD)
    plinth = rect([29, 44 + dy, 51, 48 + dy]); s.part(plinth, hx('#7A3A1C'), hx('#A8582C'), hx('#4E2410'))
    s.put(rect([36, 46 + dy, 44, 46 + dy]), hx('#F2C230'))
    # ribbon with 1000000
    rib = poly([(17, 50), (63, 50), (60, 53), (63, 56), (17, 56), (20, 53)])
    s.part(rib, hx('#E0202E'), hx('#FF6A6A'), hx('#8E0A16'))
    text(s, '1000000', 40 - text_w('1000000') // 2, 51, WHITE, outline=None, shadow=hx('#8E0A16'))
    fx = L(); rnd = random.Random(10 + f)
    sparkles(fx, rnd, 14, avoid=dilate8(dilate8(s.alpha())), cols_=(WHITE, hx('#FFF6A8'), hx('#9AF7FF')))
    for (x, y) in ((5, 22), (73, 22)) if f == 0 else ((5, 19), (73, 25)):
        text(fx, '!', x, y, hx('#FFF04A') if f == 0 else WHITE, scale=2)
    out = bg.over(fx).over(s)
    marquee(out, INK, hx('#FFF04A'), hx('#E0202E'), f)
    return out


def ad_mortgage(f):
    bg = bg_rays(hx('#B6FF2E'), hx('#6EE05A'), 40, 30, 10, 0) if f == 0 else bg_rays(hx('#FFE14A'), hx('#B6FF2E'), 40, 30, 10, .5)
    # swamp water
    water = L(); water.put(rows(47, 60), hx('#2F5A3A'))
    for (x0, y0) in ((6, 50), (52, 53), (30, 56), (64, 49)):
        water.put(rect([x0 + 2 * f, y0, x0 + 5 + 2 * f, y0]), hx('#5E9A62'))
    water.put(rows(47, 47), hx('#1E3A24'))
    s = L()
    rock = (ell([10, 18, 48, 62]) | ell([22, 10, 46, 42]) | ell([34, 22, 62, 60]) | ell([18, 24, 34, 50])) & rows(0, 48)
    s.part(rock, hx('#8A7F6A'), hx('#B2A68A'), hx('#5A5244'))
    # lumps and cracks
    for b in ([24, 20, 30, 25], [40, 28, 46, 32], [15, 34, 20, 38], [50, 38, 56, 42]):
        s.put(ell(b) & ~shift(ell(b), -1, -1), hx('#6A6150'))
    s.put(line([(30, 14), (31, 18), (29, 21)]) | line([(52, 30), (54, 34)]), hx('#4A4236'))
    # moss on top (dithered)
    yy, xx = np.mgrid[0:H, 0:W]
    moss = erode(rock) & rows(10, 24) & dither(np.clip((26 - yy) / 12.0, 0, 1))
    s.put(moss, hx('#5E8A2A')); s.put(moss & ~shift(moss, 0, 1), hx('#8CC04A'))
    # door hole
    hole = (ell([25, 30, 41, 58]) & rows(0, 47))
    s.part(hole, hx('#1B1410'), None, None, hx('#3A2E24'))
    s.put(ell([29, 35, 31, 37]) | ell([34, 35, 36, 37]), hx('#FFE14A') if f == 0 else hx('#1B1410'))  # something lives there
    # drips
    for i, (x, y0) in enumerate(((27, 33), (39, 34), (20, 31))):
        y = y0 + (3 if (f + i) % 2 else 0)
        s.put(rect([x, y, x, y + 1]), hx('#7ADCFF')); s.put(rect([x, y + 2, x, y + 2]), hx('#3A9ACF'))
    # crooked sign "2x2"
    s.put(rect([56, 30, 57, 48]), hx('#5A3A1E'))
    board = poly([(49, 24), (67, 22), (68, 31), (50, 32)]); s.part(board, hx('#C9A15A'), hx('#EBCB84'), hx('#8A6428'))
    text(s, '2x2', 53, 25, INK, outline=None)
    # floating golden key
    k = L(); ky = 2 if f else 0
    ring = ell([3, 5 + ky, 13, 15 + ky]) & ~ell([6, 8 + ky, 10, 12 + ky])
    k.part(ring, *GOLD)
    shaft = rect([12, 9 + ky, 30, 11 + ky]); k.part(shaft, *GOLD)
    k.part(rect([24, 11 + ky, 26, 15 + ky]), *GOLD); k.part(rect([28, 11 + ky, 30, 17 + ky]), *GOLD)
    fx = L(); rnd = random.Random(20 + f)
    for (x, y, kk) in ((16, 4 + ky, 2), (33, 8 + ky, 1), (6, 19 + ky, 0), (23, 3 + ky, 0)) if f == 0 else ((19, 3 + ky, 1), (34, 13 + ky, 2), (2, 18 + ky, 1), (27, 5 + ky, 0)):
        sparkle(fx, x, y, kk, hx('#FFF6A8'))
    sparkles(fx, rnd, 5, avoid=dilate8(s.alpha() | k.alpha()) | rows(40, 60) | (cols(0, 36) & rows(0, 22)), cols_=(WHITE,), kinds=(0, 1))
    out = bg.over(water).over(fx).over(s).over(k)
    marquee(out, INK, hx('#FFFFFF'), hx('#2F8A3A'), f)
    return out


def ad_mimics(f):
    bg = bg_dots(hx('#FF5FA8'), hx('#FFB8DA'), 8, f * 4, 0, big=True) if f == 0 else bg_dots(hx('#B03AE0'), hx('#FF7AD0'), 8, 4, 4, big=True)
    s = L()
    wood = (hx('#B0652E'), hx('#E08C45'), hx('#6E3A16'))
    body = rect([18, 34, 62, 54]); s.part(body, *wood)
    s.put(rows(43, 43) & erode(body), hx('#8A4A20'))
    lid = (ell([18, 10, 62, 40]) & rows(0, 24)) | rect([18, 22, 62, 30])
    s.part(lid, *wood)
    s.put(rows(26, 26) & erode(lid), hx('#8A4A20'))
    # mouth gap with teeth and a heart tongue
    gap = rect([19, 31, 61, 33]); s.put(gap, hx('#3A0A18'))
    for x in range(21, 60, 4):
        s.put(poly([(x, 31), (x + 2, 31), (x + 1, 33)]) & ~shift(rect([0, 0, 0, 0]), 0, 0), WHITE)
    tongue = ascii_mask(HEART7, 36, 32); s.part(dilate(tongue) & rows(32, 60), hx('#FF4A7A'), hx('#FF9AB8'), hx('#B0103E'))
    # gold bands and lock
    for x0 in (23, 53):
        b = rect([x0, 12, x0 + 3, 54]) & (lid | body)
        s.part(b, *GOLD)
    lock = rect([36, 38, 44, 46]); s.part(lock, *GOLD)
    s.put(rect([40, 41, 40, 43]), INK)
    # eyes on the lid
    for i, ex in enumerate((32, 46)):
        wink = (f == 1 and i == 1)
        if wink:
            s.put(line([(ex - 3, 19), (ex - 1, 17), (ex + 1, 17), (ex + 3, 19)]), INK)
        else:
            e = ell([ex - 3, 14, ex + 3, 21]); s.part(e, WHITE, None, hx('#D8D0E0'))
            s.put(rect([ex - 1 + (1 if f else 0), 16, ex + (1 if f else 0), 18]), INK)
            s.put(rect([ex - 1 + (1 if f else 0), 16, ex - 1 + (1 if f else 0), 16]), WHITE)
        # lashes
        s.put(rect([ex - 4, 14, ex - 4, 14]) | rect([ex + 4, 14, ex + 4, 14]) | rect([ex, 12, ex, 12]), INK)
        s.put(rect([ex - 3, 23, ex - 1, 23]), hx('#FF7AA8'))
    fx = L(); rnd = random.Random(30 + f)
    spots = ((7, 8), (66, 6), (5, 36), (68, 40), (12, 50), (64, 24)) if f == 0 else ((9, 4), (68, 10), (4, 28), (70, 34), (8, 46), (60, 48))
    for i, (x, y) in enumerate(spots):
        art = HEART7 if i % 2 == 0 else HEART5
        stamp(fx, art, x, y, hx('#FF2A4A') if (i + f) % 3 else hx('#FFFFFF'), INK, hx('#FF9AA8'))
    sparkles(fx, rnd, 6, avoid=dilate8(s.alpha() | fx.alpha()), cols_=(WHITE,), kinds=(0, 1))
    out = bg.over(fx).over(s)
    marquee(out, INK, hx('#FFFFFF'), hx('#FF2A8A'), f)
    return out


def ad_virus(f):
    bg = bg_stripes(hx('#FFD21F'), hx('#2A1A10'), 6, 0) if f == 0 else bg_stripes(hx('#FF2A2A'), hx('#2A0A10'), 6, 3)
    s = L()
    ear = poly([(26, 24), (6, 14), (12, 22), (8, 26), (26, 34)])
    s.part(ear | mirror_x(ear, 80), *GREEN)
    s.put((poly([(24, 26), (13, 19), (15, 23), (24, 31)])) | mirror_x(poly([(24, 26), (13, 19), (15, 23), (24, 31)]), 80), hx('#D08A9A'))
    head = ell([22, 10, 58, 50]); s.part(head, hx('#8EB84A'), hx('#C8E07A'), hx('#5A7A28'))
    # sick spots
    for (x, y, r) in ((28, 16, 2), (49, 14, 1), (52, 32, 2), (27, 34, 1), (42, 12, 1), (35, 42, 1), (47, 42, 2)):
        m = ell([x - r, y - r, x + r, y + r]); s.put(m, hx('#B0389A') if f == 0 else hx('#E0303A'))
        s.put(m & ~shift(m, 1, 1), hx('#E07AD0') if f == 0 else hx('#FF8A6A'))
    # droopy eyes with crossed pupils, eye bags
    for i, ex in enumerate((33, 47)):
        e = ell([ex - 5, 19, ex + 5, 28]); s.part(e, hx('#F4F0C0'), None, hx('#D8D09A'))
        s.put(e & rows(19, 22), hx('#6E8A30'))  # heavy lids
        s.put(e & rows(23, 23) & erode(e), hx('#4A5A20'))
        px = ex + (2 if i == 0 else -2)
        s.put(rect([px - 1, 24, px, 26]), hx('#E0202E')); s.put(rect([px, 25, px, 25]), INK)
        s.put(line([(ex - 4, 29), (ex + 3, 29)]), hx('#7A5A9A'))
    # wobbly mouth + thermometer
    s.put(line([(33, 40), (35, 39), (37, 40), (39, 39), (41, 40)]), INK)
    th = line([(41, 39), (60, 30)], 3); s.part(th, WHITE, None, hx('#B8C0D0'))
    s.put(line([(43, 38), (52 + 4 * f, 34 - 2 * f)]), hx('#E0202E'))
    s.part(ell([38, 37, 43, 42]), hx('#E0202E'), hx('#FF7A7A'), hx('#8E0A16'))
    # sweat
    for (x, y) in ((22, 12), (57, 18)) if f == 0 else ((21, 15), (58, 21)):
        s.part(poly([(x, y), (x + 2, y + 4), (x - 2, y + 4)]) | ell([x - 2, y + 2, x + 2, y + 6]), hx('#7ADCFF'), WHITE, hx('#3A8ACF'))
    fx = L()
    sk = ascii_mask(SKULL, 66, 4)
    fx.part(dilate(sk), hx('#FF2A2A') if f == 0 else WHITE, None, None, INK)
    fx.put(ascii_mask(SKULL, 66, 4, ch='.') & erode(dilate(sk)), INK)
    sk2 = ascii_mask(SKULL, 4, 4)
    fx.part(dilate(sk2), WHITE if f == 0 else hx('#FF2A2A'), None, None, INK)
    fx.put(ascii_mask(SKULL, 4, 4, ch='.') & erode(dilate(sk2)), INK)
    text(fx, '37', 5, 41, hx('#FF2A2A') if f == 0 else hx('#FFFFFF'), scale=2, outline=WHITE if f == 0 else INK)
    text(fx, '!', 72, 42, WHITE if f else hx('#FF2A2A'), scale=2, outline=INK if f else WHITE)
    out = bg.over(s).over(fx)
    marquee(out, INK, hx('#FF2A2A'), hx('#FFFFFF'), f)
    return out


def ad_hearts(f):
    bg = bg_rays(hx('#3FD0FF'), hx('#2A6AFF'), 40, 30, 12, 0) if f == 0 else bg_rays(hx('#FF7AD0'), hx('#FFE14A'), 40, 30, 12, .5)
    s = L()
    red = (hx('#FF2A4A'), hx('#FF9AA8'), hx('#B0102E'))
    for i, (x, y) in enumerate(((7, 9), (33, 5), (59, 9))):
        yy = y + (-1 if (i + f) % 2 else 0)
        h = ascii_mask(HEART13, x, yy); s.part(dilate(h), *red)
        s.put(rect([x + 2, yy + 2, x + 3, yy + 3]), WHITE)
    # download arrow
    arr = rect([36, 20 + f, 44, 33 + f]) | poly([(29, 33 + f), (51, 33 + f), (40, 44 + f)])
    s.part(arr, hx('#2DB52D'), hx('#9BE89B'), hx('#14501A'), lw=2)
    # XP progress bar
    bar = rect([13, 47, 67, 55]); s.part(bar, WHITE, None, hx('#D8D8D8'), hx('#686868'))
    nb = 3 if f == 0 else 6
    for i in range(nb):
        x = 15 + i * 7; blk = rect([x, 49, x + 5, 53])
        s.put(blk, hx('#2DB52D')); s.put(blk & rows(49, 49), hx('#B6F0AE')); s.put(blk & rows(51, 51), hx('#64D064'))
    fx = L(); rnd = random.Random(40 + f)
    sparkles(fx, rnd, 10, avoid=dilate8(dilate8(s.alpha())), cols_=(WHITE, hx('#FFF6A8')))
    for (x, y) in ((70, 33), (8, 33)) if f == 0 else ((10, 35), (68, 30)):
        coin(fx, x, y)
    out = bg.over(fx).over(s)
    marquee(out, INK, hx('#FFFFFF'), hx('#FF2A4A'), f)
    return out


def ad_diet(f):
    bg = bg_checker(hx('#FFE14A'), hx('#FF9A2A'), 6) if f == 0 else bg_checker(hx('#B6FF2E'), hx('#FFE14A'), 6, 3, 3)
    s = L()
    sack = (hx('#C9A15A'), hx('#EBCB84'), hx('#8A6428'))
    fat = ell([3, 18, 34, 54]) | poly([(15, 18), (25, 18), (28, 24), (12, 24)])
    s.part(fat, *sack)
    s.part(ell([12, 10, 28, 20]) & rows(0, 18) | poly([(13, 12), (27, 12), (24, 19), (16, 19)]), *sack)
    s.part(rect([14, 18, 26, 20]), hx('#E0202E'), hx('#FF7A7A'), hx('#8E0A16'))
    # coin emblem + sad sweaty face
    s.part(ell([12, 30, 26, 44]), *GOLD); s.put(ell([15, 33, 23, 41]) & ~erode(ell([15, 33, 23, 41])), hx('#C08A12'))
    s.put(rect([10, 26, 11, 27]) | rect([29, 26, 30, 27]), INK)
    s.put(line([(9, 24), (12, 23)]) | line([(28, 23), (31, 24)]), INK)
    s.put(rect([33, 21, 33, 23]), hx('#7ADCFF'))
    # thin bag, proud
    thin = poly([(62, 20), (71, 20), (74, 50), (59, 50)]) | ell([61, 14, 72, 22])
    s.part(thin, *sack)
    s.part(rect([62, 20, 71, 21]), hx('#E0202E'), None, None)
    s.put(line([(64, 28), (64, 29)]) | line([(68, 28), (68, 29)]), INK)
    s.put(line([(63, 33), (65, 35), (67, 35), (69, 33)]), INK)
    s.put(line([(61, 40), (63, 38)]) | line([(69, 44), (71, 42)]), hx('#8A6428'))
    # arrow
    arr = rect([39 + 2 * f, 23, 49 + 2 * f, 27]) | poly([(49 + 2 * f, 19), (49 + 2 * f, 31), (55 + 2 * f, 25)])
    s.part(arr, hx('#E0202E'), hx('#FF7A7A'), hx('#8E0A16'))
    fx = L()
    text(fx, '-40', 34, 37, hx('#E0202E') if f == 0 else WHITE, scale=2, outline=WHITE if f == 0 else INK)
    for (x, y) in ((40, 6), (33, 13), (47, 10)) if f == 0 else ((42, 4), (36, 9), (50, 13)):
        coin(fx, x, y)
    sparkle(fx, 74, 8 if f == 0 else 54, 2); sparkle(fx, 52, 46 if f == 0 else 16, 1)
    out = bg.over(s).over(fx)
    marquee(out, INK, hx('#E0202E'), hx('#FFFFFF'), f)
    return out


def ad_oracle(f):
    bg = L(); yy, xx = np.mgrid[0:H, 0:W]
    r = np.sqrt((xx - 40) ** 2 + ((yy - 24) * 1.2) ** 2)
    ring = ((r.astype(int) + (2 if f else 0)) // 4) % 2 == 0
    bg.px[ring] = hx('#5B1A9A') if f == 0 else hx('#1A2A8A'); bg.px[~ring] = hx('#2A0B4A') if f == 0 else hx('#0A0F3A')
    st = L(); rnd = random.Random(50 + f)
    for _ in range(18):
        x, y = rnd.randint(2, 77), rnd.randint(2, 57); st.px[y, x] = rnd.choice((WHITE, hx('#FFE14A'), hx('#9AF7FF')))
    s = L()
    glow = ell([16, 0, 64, 46]) & ~ell([20, 3, 60, 43])
    s.put(glow & dither(np.full((H, W), .5)), hx('#FF7AE0') if f else hx('#C07AFF'))
    ball = ell([20, 3, 60, 43])
    s.part(ball, hx('#8A5ADF'), None, hx('#4A2A9A'))
    # inner swirl (dithered toward the center)
    dc = np.sqrt((xx - 40) ** 2 + (yy - 23) ** 2)
    s.put(erode(ball) & dither(np.clip(1 - dc / 18, 0, 1) * .9), hx('#C8A0FF'))
    # eye
    eye = ell([26, 16, 54, 30]); s.part(eye, WHITE, None, hx('#E0D0F0'))
    ix = 40 + (-4 if f else 3)
    iris = ell([ix - 6, 17, ix + 6, 29]) & eye
    s.put(iris, hx('#FF9A1F')); s.put(iris & ~shift(iris, 1, 1), hx('#FFE14A')); s.put(iris & ~shift(iris, -1, -1), hx('#C0500A'))
    s.put(rect([ix - 1, 18, ix, 28]) & eye, INK)
    s.put(rect([ix + 2, 19, ix + 3, 20]) & eye, WHITE)
    s.put(eye & ~erode(eye), INK)
    s.put(line([(27, 15), (32, 12), (40, 11), (48, 12), (53, 15)]), INK)  # brow-lid
    for x in (30, 36, 44, 50):
        s.put(line([(x, 13 if x in (36, 44) else 14), (x - 1 if x < 40 else x + 1, 10 if x in (36, 44) else 12)]), INK)
    # glass highlight
    s.put(line([(25, 14), (26, 10), (29, 7)]), WHITE); s.put(rect([31, 6, 32, 6]), WHITE)
    stand = poly([(26, 40), (54, 40), (50, 47), (30, 47)]); s.part(stand, *GOLD)
    s.part(rect([22, 47, 58, 52]), hx('#7A3A1C'), hx('#A8582C'), hx('#4E2410'))
    s.put(rect([30, 49, 50, 49]), hx('#F2C230'))
    # the boar's leftover bone
    stamp(s, BONE, 8, 49, hx('#F2EEDC'), INK, WHITE, hx('#B8B09A'))
    fx = L()
    for (x, y, k) in ((10, 10, 2), (69, 14, 1), (66, 40, 2), (12, 32, 0)) if f == 0 else ((8, 16, 1), (71, 8, 2), (68, 34, 0), (14, 38, 2)):
        sparkle(fx, x, y, k, hx('#9AF7FF'))
    text(fx, '?', 70, 44 if f == 0 else 22, hx('#FFE14A'), scale=1)
    text(fx, '?', 4, 22 if f == 0 else 40, hx('#FFE14A'), scale=1)
    out = bg.over(st).over(s).over(fx)
    marquee(out, INK, hx('#FFE14A'), hx('#7A2ADF'), f)
    return out


def ad_speedup(f):
    bg = bg_stripes(hx('#FF3A2A'), hx('#FF8A1F'), 4, f * 4, 0) if f == 0 else bg_stripes(hx('#2A6AFF'), hx('#3FD0FF'), 4, 2, 0)
    bg2 = L()
    for y in range(H):
        if (y // 4) % 2 == f % 2: bg2.px[y, :] = bg.px[y, 0]
    s = L()
    body = poly([(8, 46), (58, 46), (66, 40), (64, 34), (58, 34), (52, 42), (10, 42)]) | ell([6, 41, 14, 47])
    slug = (hx('#C8D86A'), hx('#EEF59A'), hx('#8A9A3A'))
    s.part(body, *slug)
    head = ell([54, 26, 68, 40]); s.part(head, *slug)
    for (x0, x1) in ((58, 56), (64, 66)):
        s.put(line([(x0, 28), (x1, 20)]), INK)
        s.part(ell([x1 - 2, 17, x1 + 2, 21]), WHITE, None, None)
        s.put(rect([x1, 19, x1, 19]), INK)
    s.put(line([(60, 35), (62, 36), (64, 35)]), INK)
    s.put(rect([66, 30, 66, 33]), hx('#7ADCFF'))  # sweat
    shell = ell([16, 16, 48, 46])
    s.part(shell, hx('#B05A2A'), hx('#E08C45'), hx('#6E3A16'))
    for rr in (11, 7, 3):
        sp = ell([32 - rr, 31 - rr, 32 + rr, 31 + rr]); s.put(sp & ~erode(sp) & ~rows(0, 30 - rr // 2) | (sp & ~erode(sp) & cols(0, 31)), hx('#6E3A16'))
    # the rusty nail strapped on like a rocket, with goblin ears
    nail = rect([18, 9, 50, 13]) | poly([(50, 9), (50, 13), (57, 11)])
    s.part(nail, hx('#9AA0A8'), hx('#D8DCE0'), hx('#5A6068'))
    s.part(rect([14, 6, 18, 16]), hx('#9AA0A8'), hx('#D8DCE0'), hx('#5A6068'))
    for x in (24, 31, 38, 45):
        s.put(rect([x, 10 + (x % 3 == 0), x + 1, 11 + (x % 3 == 0)]) & erode(nail), hx('#B8602A'))
    ear = poly([(22, 9), (19, 3), (25, 8)]); s.part(ear, *GREEN, line=INK)
    s.part(rect([30, 13, 31, 17]), hx('#5A3A1E'), None, None)  # strap
    # flame
    fl = poly([(14, 8), (3 - 2 * f, 11), (14, 14)]); s.part(fl, hx('#FF8A1F'), hx('#FFE14A'), hx('#E0202E'))
    s.put(poly([(13, 10), (8 - f, 11), (13, 12)]), hx('#FFF6A8'))
    # hourglass
    hg = L()
    hg.part(rect([66, 4, 76, 6]), hx('#7A3A1C'), None, None); hg.part(rect([66, 22, 76, 24]), hx('#7A3A1C'), None, None)
    glass = poly([(67, 7), (75, 7), (72, 14), (75, 21), (67, 21), (70, 14)]); hg.part(glass, hx('#DFF4FF'), WHITE, hx('#9AC8E0'))
    hg.put(poly([(68, 8), (74, 8), (71, 13)]) if f == 0 else poly([(69, 9), (73, 9), (71, 12)]), hx('#FFC84A'))
    hg.put(poly([(71, 16), (74, 20), (68, 20)]) if f == 0 else poly([(71, 15), (74, 20), (68, 20)]), hx('#FFC84A'))
    hg.put(rect([71, 13, 71, 15]), hx('#FFC84A'))
    # progress bar stuck at 1%
    bar = rect([8, 50, 52, 56]); s.part(bar, WHITE, None, hx('#D8D8D8'), hx('#686868'))
    s.put(rect([10, 52, 12, 54]), hx('#2DB52D'))
    text(s, '1%', 56, 51, WHITE, outline=INK)
    sp = L()
    for y in (20, 26, 36) if f == 0 else (22, 30, 38):
        sp.put(rect([2 + f, y, 9 + f, y]), WHITE)
    out = bg.over(sp).over(s).over(hg)
    marquee(out, INK, hx('#FFFFFF'), hx('#FFE14A'), f)
    return out


def ad_cassette(f):
    bg = L()
    sky = bg_vgrad([(0, hx('#FF3AB0') if f == 0 else hx('#7A2ADF')), (.45, hx('#FF9A3A') if f == 0 else hx('#FF3AB0')), (.62, hx('#FFE14A'))])
    bg.over(sky)
    floor = rows(36, 60); bg.put(floor, hx('#2A0B4A'))
    for y in (37, 39, 42, 46, 51, 57):
        yy = y + (1 if f and y > 37 else 0)
        if yy < H: bg.put(rows(yy, yy), hx('#3FF0FF'))
    for k in range(-8, 9):
        bg.put(line([(40 + k * 3, 36), (40 + k * 14, 60)]) & floor, hx('#3FF0FF'))
    s = L()
    # the free second cassette behind
    back = rect([20, 6, 74, 38]); s.part(back, hx('#E0202E'), hx('#FF7A7A'), hx('#8E0A16'))
    s.put(rect([24, 9, 70, 20]), hx('#FFC0C0'))
    shell = rect([6, 14, 62, 48]); s.part(shell, hx('#3A3A46'), hx('#6A6A7A'), hx('#1E1E28'))
    label = rect([10, 17, 58, 34]); s.part(label, hx('#F4EBD0'), WHITE, hx('#C8BC98'))
    s.put(rect([11, 26, 57, 27]), hx('#FF3AB0')); s.put(rect([11, 28, 57, 29]), hx('#3FD0FF'))
    # frog on the label
    frog = ell([12, 18, 26, 31]); s.part(frog, *GREEN)
    for ex in (15, 22):
        s.part(ell([ex - 2, 16, ex + 2, 21]), WHITE, None, None)
        s.put(rect([ex, 18 + f, ex, 19]), INK)
    s.put(line([(15, 26), (17, 28), (21, 28), (23, 26)]), INK)
    s.put(rect([14, 25, 14, 25]) | rect([24, 25, 24, 25]), hx('#FF7AA8'))
    text(s, '2003', 33, 19, INK, outline=None)
    win = rect([20, 36, 48, 45]); s.part(win, hx('#1A1020'), None, None, hx('#6A6A7A'))
    for cx in (26, 42):
        reel = ell([cx - 4, 37, cx + 4, 45]); s.part(reel, WHITE, None, hx('#C8C8D0'))
        s.put(rect([cx, 41, cx, 41]), INK)
        for a in range(3):
            ang = a * 2 * math.pi / 3 + (f * math.pi / 3)
            s.put(rect([cx + round(2 * math.cos(ang)), 41 + round(2 * math.sin(ang)), cx + round(2 * math.cos(ang)), 41 + round(2 * math.sin(ang))]), INK)
    s.put(rect([30, 41, 38, 42]), hx('#5A3A1E'))
    for (x, y) in ((8, 16), (59, 16), (8, 45), (59, 45)):
        s.put(rect([x, y, x + 1, y + 1]), hx('#9A9AAA'))
    fx = L()
    for i, (x, y) in enumerate(((66, 42), (3, 2), (70, 2)) if f == 0 else ((68, 40), (4, 4), (66, 3))):
        stamp(fx, NOTE, x, y, hx('#FFE14A') if i % 2 else hx('#3FF0FF'), INK)
    text(fx, 'x2', 63, 25 if f == 0 else 23, hx('#FFE14A') if f == 0 else WHITE, scale=2)
    out = bg.over(s).over(fx)
    marquee(out, INK, hx('#3FF0FF'), hx('#FF3AB0'), f)
    return out


def ad_income(f):
    bg = bg_rays(hx('#2DD05A'), hx('#FFE14A'), 40, 22, 14, 0) if f == 0 else bg_rays(hx('#FFE14A'), hx('#FF9A1F'), 40, 22, 14, .5)
    s = L()
    dress = ell([16, 38, 64, 72]); s.part(dress, hx('#FF4AB0'), hx('#FF9AD8'), hx('#B01A70'))
    s.put(ell([35, 37, 45, 43]) & dress, hx('#7DBA3A'))
    ear = poly([(26, 22), (8, 12), (14, 20), (10, 23), (26, 30)])
    s.part(ear | mirror_x(ear, 80), *GREEN)
    head = ell([24, 8, 56, 42]); s.part(head, *GREEN)
    # hair: purple bun and bangs
    hair = ell([33, 0, 47, 11]) | (ell([23, 6, 57, 28]) & rows(0, 16)) | ell([22, 10, 28, 24]) | ell([52, 10, 58, 24])
    for x0 in (27, 33, 39, 45):
        hair &= ~(ell([x0, 14, x0 + 6, 20]) & rows(15, 20))
    s.part(hair, hx('#7A2ADF'), hx('#B07AFF'), hx('#4A1A8A'))
    s.put(rect([38, 4, 41, 5]), hx('#FFE14A'))
    # eyes with lashes, one winks on frame 1
    for i, ex in enumerate((33, 47)):
        if f == 1 and i == 1:
            s.put(line([(ex - 3, 21), (ex, 19), (ex + 3, 21)]), INK)
        else:
            e = ell([ex - 3, 17, ex + 3, 24]); s.part(e, WHITE, None, None)
            s.put(rect([ex - 1, 19, ex + 1, 22]), hx('#2A6A2A')); s.put(rect([ex, 20, ex, 21]), INK); s.put(rect([ex - 1, 19, ex - 1, 19]), WHITE)
        s.put(line([(ex - 4, 16), (ex - 2, 15)]) | line([(ex + 2, 15), (ex + 4, 16)]), INK)
        s.put(rect([ex - 2, 27, ex, 27]) if i == 0 else rect([ex, 27, ex + 2, 27]), hx('#FF7AA8'))
    grin = ell([31, 26, 49, 37]) & rows(30, 60); s.part(grin, hx('#8E0A26'), None, None)
    s.put(rect([33, 31, 47, 32]) & grin, WHITE); s.put(rect([37, 31, 37, 32]) | rect([43, 31, 43, 32]), hx('#C8C0B0'))
    s.put(ell([37, 34, 43, 37]) & grin & rows(35, 60), hx('#FF4A6A'))
    # coin earrings
    coin(s, 21, 30); coin(s, 54, 30)
    fx = L(); rnd = random.Random(60 + f)
    for (x, y) in ((4, 6), (12, 16), (70, 4), (64, 14), (5, 30), (72, 26), (10, 40), (68, 38)):
        coin(fx, x + rnd.randint(-2, 2), y + (4 if f else 0) + rnd.randint(-1, 1))
    text(fx, '5000', 40 - text_w('5000', 2) // 2, 45, hx('#FFE14A') if f == 0 else WHITE, scale=2, outline=INK, shadow=None)
    sparkles(fx, rnd, 5, avoid=dilate8(s.alpha() | fx.alpha()), cols_=(WHITE,), kinds=(0, 1))
    out = bg.over(s).over(fx)
    marquee(out, INK, hx('#FFE14A'), hx('#2DD05A'), f)
    return out


def ad_bones(f):
    base = hx('#3A0F4A') if f == 0 else hx('#5A0F3A')
    bg = bg_fill(base)
    for y in range(-4, H + 8, 10):
        for x in range(-4, W + 8, 12):
            X = x + ((y // 10) % 2) * 6 + (3 if f else 0)
            bg.put(ascii_mask(HEART5, X, y + (2 if f else 0)), hx('#7A1A6A') if f == 0 else hx('#A01A5A'))
    s = L()
    bone = (hx('#F2EEDC'), WHITE, hx('#B8B09A'))
    # spine and ribs
    s.part(rect([38, 30, 42, 56]), *bone)
    for i, y in enumerate((34, 39, 44, 49)):
        wdt = 16 - i * 2
        rib = (ell([40 - wdt, y - 2, 40 + wdt, y + 6]) & ~ell([40 - wdt + 2, y, 40 + wdt - 2, y + 6])) & rows(0, y + 3)
        s.part(rib, *bone)
    # arms around a heart
    arm = line([(24, 32), (20, 42), (30, 48)], 3) | mirror_x(line([(24, 32), (20, 42), (30, 48)], 3), 80)
    s.part(arm, *bone)
    hb = 1 if f else 0
    hrt = ascii_mask(HEART13, 34 - hb, 40 - hb, scale=1)
    if f: hrt = dilate(hrt)
    s.part(dilate(hrt), hx('#FF2A4A'), hx('#FF9AA8'), hx('#B0102E'))
    skull = ell([26, 3, 54, 27]) | rect([31, 20, 49, 30]); s.part(skull, *bone)
    for ex in (34, 46):
        so = ell([ex - 5, 10, ex + 4, 19]); s.put(so, hx('#2A0A1A'))
        art = HEART7 if f == 0 else HEART7
        s.put(ascii_mask(HEART7, ex - 4, 12) & so, hx('#FF4A7A') if f == 0 else hx('#FFB0C8'))
    s.put(poly([(40, 20), (38, 23), (42, 23)]), hx('#2A0A1A'))
    for x in range(32, 49, 3): s.put(rect([x, 26, x + 1, 28]), WHITE); s.put(rect([x + 2, 26, x + 2, 28]), hx('#B8B09A'))
    s.put(rect([31, 25, 49, 25]) | rect([31, 29, 49, 29]), INK)
    fx = L()
    for (x, y) in ((4, 8), (66, 46)) if f == 0 else ((4, 44), (66, 8)):
        stamp(fx, BONE, x, y, hx('#F2EEDC'), INK, WHITE, hx('#B8B09A'))
    for (x, y) in ((64, 10), (8, 46)) if f == 0 else ((66, 40), (6, 12)):
        stamp(fx, HEART7, x, y, hx('#FF2A4A'), INK, hx('#FF9AA8'))
    out = bg.over(fx).over(s)
    marquee(out, INK, hx('#FF7AD0'), hx('#5A1A6A'), f)
    return out


def gear_mask(cx, cy, r_out, r_in, teeth, rot):
    yy, xx = np.mgrid[0:H, 0:W]
    dx, dy = xx - cx + .5, yy - cy + .5; r = np.sqrt(dx * dx + dy * dy); a = np.arctan2(dy, dx) + rot
    tooth = (np.floor(a / (2 * np.pi) * teeth * 2) % 2) == 0
    return ((r <= r_in) | ((r <= r_out) & tooth)) & (r >= r_in * .35)


def ad_update(f):
    bg = bg_stripes(hx('#2A6AE8'), hx('#4A8AF8'), 5, f * 5, -1) if f == 0 else bg_stripes(hx('#FFE14A'), hx('#2A6AE8'), 5, 2, -1)
    s = L()
    g = gear_mask(40, 27, 24, 19, 10, f * math.pi / 10)
    s.part(g, hx('#9AA0A8'), hx('#D8DCE0'), hx('#5A6068'))
    sh = poly([(26, 9), (40, 5), (54, 9), (54, 26), (47, 40), (40, 45), (33, 40), (26, 26)])
    s.part(sh, hx('#F4F0E0'), WHITE, hx('#B8B09A'))
    # Goblinsoft emblem: a plain swamp-green shield with a gold rim and a crooked nail (Gvozdik) driven into it
    s.part(sh, hx('#C8A030'), hx('#FFE07A'), hx('#7A5A10'))
    q = erode(erode(sh))
    s.put(q, hx('#4E8A3A')); s.put(q & ~shift(q, 1, 1), hx('#8CC04A')); s.put(q & ~shift(q, -1, -1), hx('#2A5A1E'))
    s.put(q & ~shift(q, 2, 2) & ~(q & ~shift(q, 1, 1)), hx('#6EA844'))
    # dent where the nail went in, with cracks
    s.put(ell([36, 34, 44, 38]), hx('#1E3A16'))
    s.put(line([(37, 37), (33, 40), (31, 39)]) | line([(43, 36), (48, 39)]) | line([(40, 38), (39, 42)]), hx('#1E3A16'))
    b = 1 if f == 0 else -1
    shaft = rect([38, 18, 42, 23]) | rect([38 + b, 23, 42 + b, 29]) | rect([38 - b, 29, 42 - b, 34]) | poly([(38, 34), (42, 34), (40, 37)])
    s.part(shaft, hx('#9AA0A8'), hx('#D8DCE0'), hx('#5A6068'))
    s.put(rect([40 + b, 25, 41 + b, 26]) | rect([39 - b, 31, 40 - b, 31]) | rect([40, 20, 40, 20]), hx('#B8602A'))
    nh = ell([31, 13, 49, 20]); s.part(nh, hx('#9AA0A8'), hx('#D8DCE0'), hx('#5A6068'))
    s.put(rect([34, 15, 35, 15]) | rect([45, 17, 46, 17]), hx('#B8602A'))
    nail_ear = poly([(32, 15), (22, 9), (25, 14), (23, 17), (32, 19)])
    s.part(nail_ear | mirror_x(nail_ear, 80), *GREEN)
    s.put(poly([(30, 16), (26, 13), (27, 15), (30, 18)]) | mirror_x(poly([(30, 16), (26, 13), (27, 15), (30, 18)]), 80), hx('#D08A9A'))
    # version tag
    tag = rect([3, 4, 27, 12]); s.part(tag, WHITE, None, hx('#D8D8D8'), INK)
    text(s, '0.0.0.1', 5, 6, hx('#2A6AE8'), outline=None)
    bar = rect([8, 49, 60, 56]); s.part(bar, WHITE, None, hx('#D8D8D8'), hx('#686868'))
    if f: s.put(rect([10, 51, 12, 54]), hx('#2DB52D'))
    text(s, '0%' if f == 0 else '1%', 63, 50, WHITE, outline=INK)
    fx = L()
    sparkle(fx, 10, 30 if f == 0 else 22, 2, hx('#FFE14A')); sparkle(fx, 70, 32 if f == 0 else 40, 1, hx('#FFE14A'))
    text(fx, '!', 12, 38 if f == 0 else 36, hx('#FFE14A'), scale=2)
    out = bg.over(fx).over(s)
    marquee(out, INK, hx('#FFFFFF'), hx('#2A6AE8'), f)
    return out


def ad_room(f):
    """Weirdcore: an empty liminal room, a door ajar with an eye in the gap. Quiet; frame 1 = the light flickers, the eye closes."""
    dim = 1 if f else 0
    def C(c, k=.0):
        c = hx(c); return lerp(c, hx('#3A3424'), k + dim * .12)
    out = L(); yy, xx = np.mgrid[0:H, 0:W]
    vx, vy = 40, 27
    back = rect([27, 16, 53, 40])
    ceil = poly([(0, 0), (80, 0), (53, 16), (27, 16)])
    floor = poly([(0, 60), (80, 60), (53, 40), (27, 40)])
    lw = poly([(0, 0), (27, 16), (27, 40), (0, 60)]); rw = poly([(80, 0), (53, 16), (53, 40), (80, 60)])
    out.put(lw, C('#D4C47A', .12)); out.put(rw, C('#D4C47A', .2)); out.put(back, C('#E0D28A'))
    out.put(ceil, C('#ECE6C8', .05)); out.put(floor, C('#A89A64', .1))
    # wallpaper stripes (dithered, faint)
    stripes = ((xx % 4) == 0) & dither(np.full((H, W), .5))
    out.put(stripes & back, C('#CDBF78')); out.put(stripes & (lw | rw), C('#BCAE6A', .15))
    # carpet: vertical texture lines converging
    for k in range(-6, 7):
        out.put(line([(vx + k * 2, 40), (vx + k * 10, 60)]) & floor & dither(np.full((H, W), .4)), C('#9A8C58', .12))
    # baseboards
    out.put(rows(39, 40) & back, C('#8A7A4A')); out.put(line([(0, 58), (27, 39)]) | line([(80, 58), (53, 39)]), C('#8A7A4A'))
    # fluorescent panel
    lamp = poly([(33, 5), (47, 5), (49, 9), (31, 9)])
    out.put(lamp, hx('#FFFFF0') if f == 0 else C('#C8C4A8'))
    if f == 0: out.put(lamp & ~erode(lamp), C('#DCD8C0'))
    # a window on the left wall: daytime sky inside a windowless room
    winm = poly([(8, 12), (19, 18), (19, 32), (8, 32)])
    out.put(winm, hx('#7EB8F0')); out.put(winm & ell([9, 20, 17, 27]), hx('#FFFFFF')); out.put(winm & ell([12, 17, 20, 23]) & rows(0, 21), hx('#EAF4FF'))
    out.put(dilate(winm) & ~winm, C('#8A7A4A'))
    # door ajar on the back wall
    door = rect([37, 22, 46, 40]); out.put(door, C('#6A5636'))
    out.put(door & ~erode(door), C('#4A3A22'))
    gapm = rect([44, 23, 45, 39]); out.put(gapm, hx('#0A0806'))
    out.put(rect([39, 31, 40, 31]), C('#C8A84A'))
    if f == 0:
        out.put(rect([44, 28, 45, 30]), hx('#F4F0E0')); out.put(rect([45, 29, 45, 29]), hx('#1A1020')); out.put(rect([44, 29, 44, 29]), hx('#7A3A2A'))
    else:
        out.put(rect([44, 30, 45, 30]), hx('#3A3424'))
    # a single outlet, an off-center chair-shadow stain on the carpet
    out.put(rect([60, 42, 61, 44]), C('#F0E8C8')); out.put(rect([60, 43, 61, 43]) & cols(61, 61), C('#5A4A2A'))
    stain = ell([18, 50, 30, 55]) & dither(np.full((H, W), .35)); out.put(stain, C('#7A6C40'))
    # soft vignette
    d = np.sqrt(((xx - 40) / 40.0) ** 2 + ((yy - 30) / 30.0) ** 2)
    vg = dither(np.clip((d - .92) * 1.3, 0, 1)); out.put(vg, hx('#3A3424'))
    frame_plain(out, hx('#3A3424'))
    return out


ADS = [('prize', ad_prize), ('mortgage', ad_mortgage), ('mimics', ad_mimics), ('virus', ad_virus), ('hearts', ad_hearts),
       ('diet', ad_diet), ('oracle', ad_oracle), ('speedup', ad_speedup), ('cassette', ad_cassette), ('income', ad_income),
       ('bones', ad_bones), ('update', ad_update), ('room', ad_room)]

# ================================================================ window skins (same geometry as xp_window.png)
RAMPS = {
    'red': [hx('#5A0008'), hx('#B0000E'), hx('#E8101E'), hx('#FF3A3A'), hx('#FFA0A0')],
    'lime': [hx('#1E5A00'), hx('#3E9A00'), hx('#6ED400'), hx('#A8FF1E'), hx('#E8FFA0')],
    'pink': [hx('#4A0A6A'), hx('#9A1AB0'), hx('#D02ACF'), hx('#FF5AD8'), hx('#FFC0F0')],
}


def ramp(stops, t):
    t = min(1, max(0, t)) * (len(stops) - 1); i = min(len(stops) - 2, int(t)); return lerp(stops[i], stops[i + 1], t - i)


def window_skin(src, name):
    a = np.array(src.convert('RGBA')).copy(); out = a.copy()
    body = (a[..., 0] == 236) & (a[..., 1] == 233) & (a[..., 2] == 216)
    gmin, gmax = 49.0, 178.0
    for y in range(a.shape[0]):
        for x in range(a.shape[1]):
            if body[y, x]: continue
            r, g, b, al = a[y, x]
            t = (g - gmin) / (gmax - gmin)
            c = ramp(RAMPS[name], t); out[y, x] = (c[0], c[1], c[2], al)
    return Image.fromarray(out, 'RGBA')

# ================================================================ fake close button (21x21)


def round_mask(w, h, r):
    m = np.ones((h, w), bool)
    for y in range(h):
        for x in range(w):
            for cx, cy in ((r, r), (w - 1 - r, r), (w - 1 - r, h - 1 - r), (r, h - 1 - r)):
                inx = x < r if cx == r else x > w - 1 - r
                iny = y < r if cy == r else y > h - 1 - r
                if inx and iny and (x - cx) ** 2 + (y - cy) ** 2 > (r + .35) ** 2: m[y, x] = False
    return m


def close_fake(state='normal'):
    s = 21; l = L(s, s)
    # a hair redder/pinker than the real one (#F4A486 -> #D9542D)
    top, bot = hx('#F79A8A'), hx('#DD4434')
    if state == 'hover': top, bot = hx('#FFB8A8'), hx('#EC5A46')
    if state == 'pressed': top, bot = hx('#C4402E'), hx('#A42A1C')
    for y in range(s): l.px[y, :] = lerp(top, bot, y / (s - 1))
    l.px[0, :] = WHITE; l.px[-1, :] = WHITE; l.px[:, 0] = WHITE; l.px[:, -1] = WHITE
    # crooked cross: one stroke sits a pixel low and bends at its end
    m = line([(6, 6), (14, 14)], w=s, h=s) | line([(7, 6), (15, 14)], w=s, h=s)
    m |= line([(14, 7), (7, 14)], w=s, h=s) | line([(15, 7), (8, 14)], w=s, h=s)
    m |= rect([6, 15, 7, 15], w=s, h=s)
    m &= ~rect([15, 14, 15, 14], w=s, h=s)
    l.put(m, WHITE)
    l.px[~round_mask(s, s, 3)] = (0, 0, 0, 0)
    return l.img()

# ================================================================ dialog icons 32x32


def icon_warning():
    l = L(32, 32)
    tri = poly([(15, 2), (16, 2), (30, 28), (1, 28)], 32, 32)
    l.part(tri, hx('#FFD21F'), hx('#FFF29A'), hx('#D89A00'), hx('#6B4A00'))
    l.put(poly([(15, 9), (17, 9), (17, 19), (16, 20), (15, 19)], 32, 32) | rect([14, 9, 18, 10], 32, 32) & ~rect([14, 9, 14, 9], 32, 32), INK)
    l.put(rect([15, 22, 17, 24], 32, 32), INK)
    l.put(rect([0, 29, 31, 29], 32, 32) & dilate(tri) & ~tri, (0, 0, 0, 0))
    return l


def icon_error():
    l = L(32, 32)
    c = ell([2, 2, 29, 29], 32, 32)
    l.part(c, hx('#E0302A'), hx('#FF8A7A'), hx('#9A1010'), hx('#5A0808'))
    l.put(ell([6, 4, 18, 12], 32, 32) & c & ~ell([6, 7, 22, 16], 32, 32), hx('#FFB0A0'))
    x = line([(10, 10), (21, 21)], 3, 32, 32) | line([(21, 10), (10, 21)], 3, 32, 32)
    l.put(dilate(x) & ~x & erode(c), hx('#9A1010'))
    l.put(x, WHITE)
    return l


def icon_question():
    l = L(32, 32)
    ear = poly([(6, 12), (0, 5), (2, 11), (0, 14), (6, 19)], 32, 32)
    l.part(ear | mirror_x(ear, 31), *GREEN)
    c = ell([4, 3, 27, 27], 32, 32)
    l.part(c, hx('#2A6AE8'), hx('#8AB8FF'), hx('#123A9A'), hx('#0A1E5A'))
    l.put(ell([8, 5, 18, 12], 32, 32) & c & ~ell([8, 8, 22, 16], 32, 32), hx('#C0D8FF'))
    q = [".XXXXX.", "XXX.XXX", "XX...XX", "....XXX", "...XXX.", "..XXX..", "..XX...", "..XX...", ".......", "..XX...", "..XX..."]
    qm = ascii_mask(q, 12, 9, 32, 32)
    l.put(dilate(qm) & ~qm & erode(c), hx('#123A9A')); l.put(qm, WHITE)
    return l


def bsod_face():
    """48x48, white on transparent: a sad goblin-eared monitor with a ':(' face."""
    s = 48; l = L(s, s); Wt = WHITE
    scr = rect([8, 6, 39, 33], s, s)
    l.put(scr & ~erode(erode(scr)), Wt)
    # goblin ears
    for side in (0, 1):
        e = poly([(7, 13), (0, 7), (2, 14), (1, 18), (7, 21)], s, s)
        if side: e = mirror_x(e, 47)
        l.put(e & ~erode(e), Wt)
    # stand
    l.put(rect([21, 34, 26, 38], s, s) & ~rect([23, 34, 24, 37], s, s), Wt)
    l.put(rect([14, 39, 33, 41], s, s), Wt)
    # the face  : (
    l.put(rect([16, 13, 18, 16], s, s) | rect([28, 13, 30, 16], s, s), Wt)
    frown = line([(15, 28), (17, 25), (20, 24), (27, 24), (30, 25), (32, 28)], 2, s, s)
    l.put(frown, Wt)
    # a single tear
    l.put(rect([17, 18, 17, 19], s, s) | rect([16, 20, 18, 21], s, s), Wt)
    # a crooked crack across the glass corner
    l.put(line([(33, 8), (31, 11), (34, 13), (32, 16)], 1, s, s), Wt)
    return l

# ================================================================ write


META_SRC = {'icon': 'xp_icon_cards.png.meta', 'window': 'xp_window.png.meta', 'close': 'xp_close.png.meta'}


def write_png(img, name, kind):
    p = os.path.join(OUT, name + '.png'); img.save(p)
    if WRITE_META:
        mp = p + '.meta'
        if not os.path.exists(mp):
            src = open(os.path.join(UI, META_SRC[kind])).read().split('\n')
            src[1] = 'guid: ' + uuid.uuid4().hex
            open(mp, 'w').write('\n'.join(src))
    return img


def main():
    if WRITE_META:
        fm = OUT.rstrip('/') + '.meta'
        if not os.path.exists(fm):
            open(fm, 'w').write('fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' % uuid.uuid4().hex)
    sheet = []
    for slug, fn in ADS:
        fr = [write_png(fn(f).img(), 'pc_ad_%s_%d' % (slug, f), 'icon') for f in (0, 1)]
        sheet.append(('ad', slug, fr))
    src = Image.open(os.path.join(UI, 'xp_window.png'))
    skins = [(n, write_png(window_skin(src, n), 'xp_window_ad_' + n, 'window')) for n in ('red', 'lime', 'pink')]
    closes = [('real', Image.open(os.path.join(UI, 'xp_close.png')).convert('RGBA')),
              ('fake', write_png(close_fake(), 'xp_close_fake', 'close')),
              ('real hover', Image.open(os.path.join(UI, 'xp_close_hover.png')).convert('RGBA')),
              ('fake hover', write_png(close_fake('hover'), 'xp_close_fake_hover', 'close')),
              ('fake pressed', write_png(close_fake('pressed'), 'xp_close_fake_pressed', 'close'))]
    icons = [(n, write_png(fn().img(), 'xp_icon_' + n, 'icon')) for n, fn in (('warning', icon_warning), ('error', icon_error), ('question', icon_question))]
    face = write_png(bsod_face().img(), 'pc_bsod_face', 'icon')
    if WRITE_META: preview(sheet, src, skins, closes, icons, face)
    print('ok')


def preview(sheet, src, skins, closes, icons, face):
    S = 4; PAD = 12
    from PIL import ImageFont
    try: font = ImageFont.load_default(size=14)
    except TypeError: font = ImageFont.load_default()
    def big(im, k=S): return im.resize((im.width * k, im.height * k), Image.NEAREST)
    cell_w = (W * S) * 2 + PAD * 3; cell_h = H * S + 28
    ncol = 3; nrow = (len(sheet) + ncol - 1) // ncol
    sw = ncol * cell_w + PAD
    extra_h = 64 * S + 40 + 21 * S + 40 + 48 * S + 40
    sheet_img = Image.new('RGBA', (sw, nrow * cell_h + extra_h + PAD * 2), hx('#3A3A44'))
    d = ImageDraw.Draw(sheet_img)
    for i, (_, slug, fr) in enumerate(sheet):
        cx, cy = PAD + (i % ncol) * cell_w, PAD + (i // ncol) * cell_h
        d.text((cx, cy), 'pc_ad_%s  (0 | 1)' % slug, fill=WHITE, font=font)
        sheet_img.alpha_composite(big(fr[0]), (cx, cy + 20)); sheet_img.alpha_composite(big(fr[1]), (cx + W * S + PAD, cy + 20))
    y = PAD + nrow * cell_h; x = PAD
    for n, im in [('xp_window (orig)', src.convert('RGBA'))] + [('xp_window_ad_' + n, im) for n, im in skins]:
        d.text((x, y), n, fill=WHITE, font=font); sheet_img.alpha_composite(big(im), (x, y + 20)); x += 64 * S + PAD * 3
    y += 64 * S + 40; x = PAD
    for n, im in closes:
        d.text((x, y), n, fill=WHITE, font=font); sheet_img.alpha_composite(big(im), (x, y + 20)); x += 21 * S + 70
    for n, im in icons:
        d.text((x, y), 'xp_icon_' + n, fill=WHITE, font=font); sheet_img.alpha_composite(big(im), (x, y + 20)); x += 32 * S + 60
    y += 32 * S + 50; x = PAD
    d.text((x, y), 'pc_bsod_face (on XP blue)', fill=WHITE, font=font)
    blue = Image.new('RGBA', (face.width * S + 40, face.height * S + 40), hx('#0A2AA8')); blue.alpha_composite(big(face), (20, 20))
    sheet_img.alpha_composite(blue, (x, y + 20))
    # a mock: three popups on the wallpaper-ish blue, at x2
    mock = Image.new('RGBA', (240, 170), hx('#2A6AD8'))
    for i, ((_, slug, fr), (sk, skim)) in enumerate(zip([sheet[0], sheet[3], sheet[12]], skins)):
        win = Image.new('RGBA', (96, 90), (0, 0, 0, 0))
        # 9-slice by hand: L8 R8 T26 B4
        def slice9(im, w, h, l=8, r=8, t=26, b=4):
            o = Image.new('RGBA', (w, h)); sw_, sh_ = im.size
            xs = [(0, l, 0, l), (l, sw_ - r, l, w - r), (sw_ - r, sw_, w - r, w)]
            ys = [(0, t, 0, t), (t, sh_ - b, t, h - b), (sh_ - b, sh_, h - b, h)]
            for (sx0, sx1, dx0, dx1) in xs:
                for (sy0, sy1, dy0, dy1) in ys:
                    if dx1 > dx0 and dy1 > dy0:
                        o.alpha_composite(im.crop((sx0, sy0, sx1, sy1)).resize((dx1 - dx0, dy1 - dy0), Image.NEAREST), (dx0, dy0))
            return o
        win = slice9(skim, 88, 90)
        win.alpha_composite(fr[0], (4, 26))
        cb = closes[1][1] if i == 1 else closes[0][1]
        win.alpha_composite(cb, (88 - 21 - 4, 3))
        mock.alpha_composite(win, (10 + i * 72, 10 + i * 30))
    d.text((x + 260, y), 'mock (popups on desktop, x2; middle one has the fake X)', fill=WHITE, font=font)
    need_h = y + 20 + 170 * 2 + PAD
    if need_h > sheet_img.height:
        g = Image.new('RGBA', (sheet_img.width, need_h), hx('#3A3A44')); g.alpha_composite(sheet_img); sheet_img = g
    sheet_img.alpha_composite(big(mock, 2), (x + 260, y + 20))
    sheet_img.save(os.path.join(HERE, 'preview.png'))


if __name__ == '__main__':
    main()
