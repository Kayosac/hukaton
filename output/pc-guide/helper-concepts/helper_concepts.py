"""Pixel-art concepts of the PC assistant for the Goblinsoft desktop (640x480 logical grid, 1 px = 1 PC pixel).

Three candidates: Гвоздик (rusty nail), Дискетыч (goblin in a floppy), Пустышка (blank card).
Each gets idle / talk / point frames (48x48 cells), a 1x sheet and a 4x preview; plus overview.png
(3x, on a crop of the PC desktop with XP balloons).

Run:  python3 helper_concepts.py [assets_dir]
assets_dir defaults to <worktree>/hukaton/Assets (worktree = 3 folders up from this script).
Outputs are written next to this script.
"""
import sys, os, math, random
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(HERE, '..', '..', '..', 'hukaton', 'Assets'))
UI = os.path.join(ASSETS, 'Computer', 'Art', 'Ui')
FONTS = os.path.join(ASSETS, 'Content', 'UI', 'Typography', 'Fonts')


def hx(c, a=255):
    c = c.lstrip('#'); return (int(c[0:2], 16), int(c[2:4], 16), int(c[4:6], 16), a)


FW, FH = 48, 48
CLEAR = (0, 0, 0, 0)
OUT = hx('#140D09')      # outline, shadow side
OUT2 = hx('#3A271C')     # outline, lit side (sel-out)
WHITE = hx('#FFFFFF')
EYE_SH = hx('#C9CED6')
PUPIL = hx('#120C0A')
MOUTH = hx('#3E0F10')
TONGUE = hx('#C9483C')
FANG = hx('#FFF6DC')

# goblin greens (from RoundGoblinAtlas, lifted a notch so they read at 1x)
G_BODY = (hx('#5E7040'), hx('#7F9458'), hx('#3F4C2A'))
G_NOSE = (hx('#7A8E52'), hx('#9DB070'), hx('#56683A'))
G_EAR = (hx('#97A66A'), hx('#B8C68A'), hx('#6B7944'))
G_DARK = hx('#2C3619')
G_EYE = hx('#F1D54E')
# rust and steel
RUST = (hx('#A4582C'), hx('#D08A4A'), hx('#6E3418'))
RUST_DEEP = hx('#55250F')
STEEL = (hx('#8D949B'), hx('#C3C9CE'), hx('#60666D'))
# floppy
FLOP = (hx('#3C4258'), hx('#5D6683'), hx('#262A3A'))
SHUT = (hx('#B9C0C8'), hx('#E6EAEE'), hx('#7D848D'))
LABEL = (hx('#F5F2E6'), hx('#FFFFFF'), hx('#D3CDB8'))
INK_BLUE = hx('#2F5FD0')
INK_RED = hx('#C8402E')
XP_BLUE = hx('#245EDB')
# paper card
PAPER = (hx('#F7F2E6'), hx('#FFFFFF'), hx('#D2C7AE'))
PAPER_FOLD = (hx('#D8C69E'), hx('#E9DCBC'), hx('#B8A47A'))
GRAPHITE = hx('#4E3F33')
BLUSH = hx('#EBA59A')


class Frame:
    def __init__(self, seed=1):
        self.img = Image.new('RGBA', (FW, FH), CLEAR); self.px = self.img.load(); self.rnd = random.Random(seed)

    def put(self, x, y, c):
        x, y = int(round(x)), int(round(y))
        if 0 <= x < FW and 0 <= y < FH: self.px[x, y] = c

    def get(self, x, y):
        if 0 <= x < FW and 0 <= y < FH: return self.px[x, y]
        return CLEAR

    def part(self, mask, cols, outline=True, shade=True, diag=0.25, skip=None):
        """Fill a mask with base/light/dark shading (light from top-left) and a 1 px irregular outline
        drawn over whatever lies below (gives separation lines between parts)."""
        base, light, dark = cols
        if outline:
            ring = {}
            for (x, y) in mask:
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    q = (x + dx, y + dy)
                    if q not in mask: ring.setdefault(q, []).append((dx, dy))
            for (x, y) in mask:
                for dx, dy in ((1, 1), (-1, 1), (1, -1), (-1, -1)):
                    q = (x + dx, y + dy)
                    if q not in mask and q not in ring and self.rnd.random() < diag: ring[q] = [(dx, dy)]
            for q, dirs in ring.items():
                if skip and q in skip: continue
                lit = all(d[0] > 0 or d[1] > 0 for d in dirs)  # ring sits on the top-left of the shape
                c = OUT2 if lit and self.rnd.random() < .8 else OUT
                self.put(q[0], q[1], c)
        for (x, y) in mask:
            c = base
            if shade:
                if (x + 1, y) not in mask or (x, y + 1) not in mask: c = dark
                elif (x - 1, y) not in mask or (x, y - 1) not in mask: c = light
            self.put(x, y, c)

    def line(self, pts, c):
        for a, b in zip(pts, pts[1:]):
            for p in bres(a, b): self.put(p[0], p[1], c)


def bres(a, b):
    x0, y0 = int(round(a[0])), int(round(a[1])); x1, y1 = int(round(b[0])), int(round(b[1]))
    dx, dy = abs(x1 - x0), -abs(y1 - y0); sx = 1 if x0 < x1 else -1; sy = 1 if y0 < y1 else -1; err = dx + dy; out = []
    while True:
        out.append((x0, y0))
        if x0 == x1 and y0 == y1: break
        e2 = 2 * err
        if e2 >= dy: err += dy; x0 += sx
        if e2 <= dx: err += dx; y0 += sy
    return out


def m_poly(pts):
    m = Image.new('L', (FW, FH), 0); ImageDraw.Draw(m).polygon([(round(x), round(y)) for x, y in pts], fill=255)
    return mask_of(m)


def m_ell(x0, y0, x1, y1):
    m = Image.new('L', (FW, FH), 0); ImageDraw.Draw(m).ellipse([x0, y0, x1, y1], fill=255)
    return mask_of(m)


def m_rect(x0, y0, x1, y1): return {(x, y) for x in range(x0, x1 + 1) for y in range(y0, y1 + 1)}


def m_thick(pts, r=1):
    """A thick polyline (r=1 -> 2 px wide)."""
    s = set()
    for a, b in zip(pts, pts[1:]):
        for (x, y) in bres(a, b):
            for dx in range(r + 1):
                for dy in range(r + 1): s.add((x + dx - r // 2, y + dy - r // 2))
    return s


def mask_of(m):
    px = m.load(); return {(x, y) for y in range(FH) for x in range(FW) if px[x, y] > 127}


def rot(cx, cy, ang):
    a = math.radians(ang); ca, sa = math.cos(a), math.sin(a)
    return lambda lx, ly: (cx + lx * ca - ly * sa, cy + lx * sa + ly * ca)


def tilted_ell(cx, cy, rx, ry, ang, n=40):
    T = rot(cx, cy, ang); return [T(rx * math.cos(2 * math.pi * i / n), ry * math.sin(2 * math.pi * i / n)) for i in range(n)]


def hand(f, x, y, cols, point=None):
    """2x2 fist with outline; point=(dx,dy) adds a 1 px finger sticking out that way."""
    f.part(m_rect(x, y, x + 1, y + 1), cols, diag=0)
    if point:
        dx, dy = point
        fx, fy = (x if dx < 0 else x + 1) + dx, (y if dy < 0 else y + 1) + dy
        f.put(fx, fy, cols[1]); f.put(fx + dx, fy + dy, OUT)
        f.put(fx - dx, fy + dy, OUT); f.put(fx + dx, fy, OUT)


def eye(f, box, look, lid=0, lid_col=None):
    """White eye with outline; look = pupil offset (-1..1, -1..1); lid = rows covered from the top."""
    x0, y0, x1, y1 = box
    m = m_ell(x0, y0, x1, y1)
    f.part(m, (WHITE, WHITE, EYE_SH), diag=0)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    px, py = int(round(cx - .5 + look[0] * (x1 - x0) / 4)), int(round(cy - 1 + look[1] * (y1 - y0) / 4))
    for p in m_rect(px, py, px + 1, py + 2):
        if p in m: f.put(p[0], p[1], PUPIL)
    if (px, py) in m: f.put(px, py, WHITE)  # glint
    if lid:
        for (x, y) in m:
            if y < y0 + lid: f.put(x, y, lid_col)
            elif y == y0 + lid: f.put(x, y, OUT)


# ------------------------------------------------------------------ 1. Гвоздик
def nail(pose):
    f = Frame(11)
    def cx(y):  # crooked centre line: leans a bit, then bends at the knee
        c = 23.5 - (y - 20) * 0.08
        if y > 28: c = 23.5 - 0.64 + (y - 28) * 0.36
        return c
    def w(y):  # a cylinder of constant width, then a short sharp point
        return 9 if y <= 39 else max(1, 9 - (y - 39) * 1.25)
    shank, rows = set(), {}
    for y in range(7, 47):
        c, ww = cx(y), w(y)
        xs = list(range(int(round(c - ww / 2)), int(round(c - ww / 2)) + max(1, int(round(ww)))))
        rows[y] = xs; shank |= {(x, y) for x in xs}
    # small goblin ears sticking out under the cap
    lear = m_poly([(20, 14), (9, 10), (6, 10), (9, 14), (20, 19)])
    rear = m_poly([(27, 14), (37, 10), (40, 10), (38, 14), (27, 19)])
    f.part(lear, G_EAR); f.part(rear, G_EAR)
    f.line([(13, 13), (9, 12)], G_EAR[2]); f.line([(34, 13), (37, 12)], G_EAR[2])
    f.part(shank, STEEL, shade=False)
    # cylinder shading + rust blotches (rust gets heavier towards the point)
    r = random.Random(5)
    def rusty(x, y):
        n = math.sin(x * 0.9 + y * 0.45) + math.sin(y * 0.33 - x * 0.7 + 1.3) + 0.55 * math.sin(y * 1.3 + x * 2.1)
        return n + (y - 20) * 0.045 > 0.75
    for y, xs in rows.items():
        n = len(xs)
        for i, x in enumerate(xs):
            t = i / max(1, n - 1)
            k = 2 if t > .72 else (1 if .14 < t < .4 else 0)
            if n <= 2: k = 0
            pal = RUST if rusty(x, y) else STEEL
            c = (pal[0], pal[1], pal[2])[k]
            if pal is RUST and r.random() < .08: c = RUST_DEEP
            f.put(x, y, c)
    # rust drips under the cap
    for x, l in ((21, 4), (25, 3)):
        for y in range(9, 9 + l): f.put(x, y, RUST[2] if y == 8 + l else RUST[0])
    # cap: a tilted flat disc, steel with a rusty rim
    cap = m_poly(tilted_ell(23.5, 5.0, 12.0, 3.3, -6))
    f.part(cap, STEEL)
    for (x, y) in cap:
        if (x, y + 1) not in cap: f.put(x, y, RUST[2])
        elif rusty(x * 2 + 3, y * 3) and r.random() < .6: f.put(x, y, RUST[0])
    f.line([(16, 4), (26, 3)], STEEL[1]); f.put(27, 3, WHITE)
    # arms (bent wire) + green goblin hands
    if pose == 'point':
        f.line([(19, 25), (12, 24), (8, 20)], OUT); hand(f, 6, 17, G_NOSE, point=(-1, -1))
    else:
        f.line([(19, 25), (15, 28), (14, 32)], OUT); hand(f, 13, 33, G_NOSE)
    if pose == 'talk':
        f.line([(28, 24), (32, 22), (34, 18)], OUT); hand(f, 34, 16, G_NOSE)
    else:
        f.line([(28, 25), (31, 28), (33, 31)], OUT); hand(f, 33, 32, G_NOSE)
    # eyes: one goggling, one droopy
    look = {'idle': (-.6, .2), 'talk': (-.2, 0), 'point': (-1, -1)}[pose]
    eye(f, (14, 10, 21, 18), look)
    eye(f, (25, 11, 31, 17), look, lid=2 if pose != 'point' else 1, lid_col=STEEL[2])
    # crooked grin with one fang
    if pose == 'talk':
        mo = m_poly([(19, 21), (24, 20), (28, 19), (27, 23), (24, 25), (20, 24)])
        f.part(mo, (MOUTH, MOUTH, MOUTH), diag=0)
        for p in m_rect(21, 23, 25, 25):
            if p in mo: f.put(p[0], p[1], TONGUE)
        f.put(21, 21, FANG); f.put(21, 22, FANG); f.put(26, 20, FANG)
    else:
        f.line([(18, 21), (19, 22), (24, 22), (27, 20), (28, 19)], OUT); f.put(28, 18, OUT)
        f.put(22, 23, FANG); f.put(22, 24, OUT); f.put(21, 23, OUT); f.put(23, 23, OUT)
        if pose == 'point': f.line([(24, 21), (26, 19)], OUT)
    return f.img


# ------------------------------------------------------------------ 2. Дискетыч
def disk(pose):
    f = Frame(23)
    T = rot(24, 35, -3)
    # ears behind the head
    f.part(m_poly([(15, 11), (5, 6), (2, 6), (4, 9), (8, 13), (14, 17)]), G_EAR)
    f.part(m_poly([(32, 11), (42, 5), (45, 5), (44, 8), (40, 13), (33, 17)]), G_EAR)
    f.line([(12, 11), (7, 8)], G_EAR[2]); f.line([(35, 11), (40, 8)], G_EAR[2])
    head = m_ell(12, 1, 35, 26)
    f.part(head, G_BODY)
    for (x, y) in head:  # a few wrinkles/bumps like the RoundGoblin
        if (x, y) in {(17, 4), (18, 4), (27, 3), (28, 3), (29, 4)}: f.put(x, y, G_BODY[2])
    # brows, eyes, nose
    look = {'idle': (-.3, .3), 'talk': (0, .3), 'point': (-1, -.6)}[pose]
    for ex in (17, 26):
        em = m_rect(ex, 9, ex + 3, 11)
        f.part(em, (G_EYE, G_EYE, G_EYE), diag=0)
        px = ex + 1 + (1 if look[0] > 0 else (-1 if look[0] < -.5 else 0))
        f.put(px, 10 + (0 if look[1] < 0 else 1), PUPIL); f.put(px + 1, 10 + (0 if look[1] < 0 else 1), PUPIL)
    if pose == 'talk':  # brows up
        f.line([(16, 7), (21, 6)], G_DARK); f.line([(16, 6), (20, 5)], G_DARK)
        f.line([(26, 6), (31, 7)], G_DARK); f.line([(27, 5), (31, 6)], G_DARK)
    else:  # scowl
        f.line([(16, 7), (22, 9)], G_DARK); f.line([(16, 8), (21, 9)], G_DARK)
        f.line([(25, 9), (31, 7)], G_DARK); f.line([(26, 9), (31, 8)], G_DARK)
    f.part(m_ell(21, 11, 27, 17), G_NOSE, diag=0)
    f.put(22, 15, G_DARK); f.put(25, 16, G_DARK)
    # mouth
    if pose == 'talk':
        mo = m_poly([(18, 18), (29, 18), (28, 22), (19, 22)])
        f.part(mo, (MOUTH, MOUTH, MOUTH), diag=0)
        for p in m_rect(20, 21, 27, 22):
            if p in mo: f.put(p[0], p[1], TONGUE)
        f.put(19, 21, FANG); f.put(19, 20, FANG); f.put(28, 21, FANG); f.put(28, 20, FANG)
    else:
        f.line([(17, 19), (19, 20), (28, 20), (30, 18)], OUT)
        f.put(19, 19, FANG); f.put(27, 19, FANG); f.put(27, 18, FANG)
    # the floppy: crooked box the goblin sits in
    body = m_poly([T(-14, -12), T(10, -12), T(14, -8), T(14, 12), T(-14, 12)])
    f.part(body, FLOP)
    f.part(m_poly([T(-6, -12), T(6, -12), T(6, -3), T(-6, -3)]), SHUT, diag=0)
    f.part(m_poly([T(1, -10), T(3, -10), T(3, -5), T(1, -5)]), (FLOP[2], FLOP[2], FLOP[2]), outline=False)
    lab = m_poly([T(-11, 0), T(11, 0), T(11, 12), T(-11, 12)])
    f.part(lab, LABEL, outline=False)
    for (x, y) in lab:
        if (x, y - 1) not in lab: f.put(x, y, XP_BLUE)
    f.line([T(-9, 4), T(-6, 3), T(-3, 4), T(1, 3), T(4, 4)], INK_BLUE)
    f.line([T(-9, 7), T(-5, 7), T(-1, 6), T(6, 7)], INK_BLUE)
    f.line([T(-8, 9), T(-2, 10)], INK_RED)
    for p in (T(-12.5, 9), T(12.5, 9)):
        f.put(p[0], p[1], FLOP[2]); f.put(p[0], p[1] + 1, FLOP[2])
    # hands gripping the rim
    def grip(x, y):
        f.part(m_rect(x, y, x + 3, y + 2), G_NOSE, diag=0)
        f.put(x + 1, y + 2, G_DARK); f.put(x + 2, y + 2, G_DARK) if False else None
        f.put(x + 1, y + 2, G_DARK); f.put(x + 3, y + 2, G_DARK)
    if pose == 'point':
        arm = m_thick([(12, 24), (8, 22), (5, 19)], 1)
        f.part(arm, G_BODY, diag=0)
        f.part(m_rect(3, 16, 5, 18), G_NOSE, diag=0)
        f.put(2, 15, G_NOSE[1]); f.put(1, 14, OUT); f.put(2, 14, OUT); f.put(1, 15, OUT); f.put(3, 15, OUT)
    else:
        grip(10, 21)
    grip(33, 22)
    return f.img


# ------------------------------------------------------------------ 3. Пустышка
def card(pose):
    f = Frame(37)
    T = rot(24, 21, 5)
    # stick legs and little boots first (behind the card)
    f.line([T(-4, 15), (19, 41), (18, 44)], OUT); f.line([T(4, 15), (28, 41), (29, 44)], OUT)
    for x0 in (15, 29):
        for p in m_rect(x0, 44, x0 + 3, 45): f.put(p[0], p[1], OUT)
        f.put(x0 + 1, 44, OUT2)
    # a blank playing card: rounded corners, game-card proportions
    sheet = m_poly([T(-10, -16), T(10, -16), T(12, -14), T(12, 14), T(10, 16), T(-10, 16), T(-12, 14), T(-12, -14)])
    f.part(sheet, PAPER)
    # pencil-drawn layout of a game card, left empty: frame, title plate, text plate
    r = random.Random(3)
    j = lambda: r.uniform(-.5, .5)
    def scribble(pts, close=True):
        q = [T(x + j(), y + j()) for x, y in pts]
        if close: q.append(T(pts[0][0] + 1.5, pts[0][1] - 1))  # overshoot: the loop doesn't quite meet
        f.line(q, GRAPHITE)
    scribble([(9, 13), (-9, 13), (-9, -13), (9, -13)])
    f.line([T(9, -10), T(10, -6)], GRAPHITE)
    f.line([T(-8, 10), T(-6, 12)], GRAPHITE)
    # empty cost slot in the corner, like on the game's cards
    for lx, ly in ((-7, -12), (-6, -12), (-5, -11), (-5, -10), (-6, -9), (-7, -9), (-8, -10), (-8, -11)):
        p = T(lx, ly); f.put(p[0], p[1], GRAPHITE)
    # face
    look = {'idle': (-.4, .2), 'talk': (0, 0), 'point': (-1, -1)}[pose]
    lx, ly = T(-4, -3); rx, ry = T(3.5, -2.5)
    eye(f, (int(lx) - 2, int(ly) - 3, int(lx) + 2, int(ly) + 2), look)
    eye(f, (int(rx) - 1, int(ry) - 2, int(rx) + 2, int(ry) + 2), look)
    bl = T(-6.5, 2); br = T(6, 3)
    f.put(bl[0], bl[1], BLUSH); f.put(bl[0] - 1, bl[1], BLUSH); f.put(br[0], br[1], BLUSH); f.put(br[0] + 1, br[1], BLUSH)
    if pose == 'talk':
        c = T(0, 4)
        mo = m_ell(int(c[0]) - 2, int(c[1]) - 1, int(c[0]) + 1, int(c[1]) + 2)
        f.part(mo, (MOUTH, MOUTH, MOUTH), diag=0)
        f.put(int(c[0]), int(c[1]) + 1, TONGUE); f.put(int(c[0]) - 1, int(c[1]) + 1, TONGUE)
    else:
        f.line([T(-3, 3), T(-1, 4), T(2, 4), T(3, 2)], OUT)
    # stick arms with mitten hands
    sl, sr = T(-12, 1), T(12, 0)
    if pose == 'point':
        f.line([sl, (9, 15), (6, 10)], OUT); hand(f, 4, 8, PAPER, point=(-1, -1))
    else:
        f.line([sl, (9, 26), (9, 31)], OUT); hand(f, 8, 32, PAPER)
    if pose == 'talk':
        f.line([sr, (40, 18), (41, 14)], OUT); hand(f, 41, 12, PAPER)
    else:
        f.line([sr, (39, 26), (38, 31)], OUT); hand(f, 37, 32, PAPER)
    return f.img


CONCEPTS = [
    ('gvozdik', 'Гвоздик', nail, 'Похоже, вы впервые включили компьютер!', 'talk'),
    ('disketych', 'Дискетыч', disk, 'Это «Карты». Тыкните дважды. Не бойтесь, оно не кусается. Почти.', 'point'),
    ('pustyshka', 'Пустышка', card, 'Я ничего не умею. Зато ничего и не сломаю!', 'talk'),
]
POSES = ('idle', 'talk', 'point')


def sheet(fn):
    s = Image.new('RGBA', (FW * 3, FH), CLEAR)
    for i, p in enumerate(POSES): s.alpha_composite(fn(p), (i * FW, 0))
    return s


def up(img, k): return img.resize((img.width * k, img.height * k), Image.NEAREST)


# ------------------------------------------------------------------ overview
def font(name, size): return ImageFont.truetype(os.path.join(FONTS, name), size)


def wrap(text, fnt, width):
    words, lines, cur = text.split(), [], ''
    for wd in words:
        t = (cur + ' ' + wd).strip()
        if fnt.getlength(t) <= width or not cur: cur = t
        else: lines.append(cur); cur = wd
    if cur: lines.append(cur)
    return lines


def text1(img, xy, s, fnt, col):
    """Crisp 1-bit text (no antialias)."""
    m = Image.new('L', img.size, 0); d = ImageDraw.Draw(m); d.fontmode = '1'; d.text(xy, s, font=fnt, fill=255)
    img.paste(Image.new('RGBA', img.size, col), (0, 0), m)


def balloon(img, x, bottom, w, title, body, tail_to):
    fb, ft = font('PixeloidSans.ttf', 9), font('Ithaca.ttf', 16)
    lines = wrap(body, fb, w - 12)
    h = 6 + 13 + len(lines) * 11 + 4
    y = bottom - h
    d = ImageDraw.Draw(img)
    yel, blk = hx('#FFFFE1'), hx('#000000')
    # tail: a thin wedge from the bottom edge to the speaker
    bx = x + w - 34
    d.polygon([(bx, y + h - 1), (bx + 14, y + h - 1), tail_to], fill=yel)
    d.line([(bx, y + h - 1), tail_to], fill=blk); d.line([(bx + 14, y + h - 1), tail_to], fill=blk)
    d.rounded_rectangle([x, y, x + w - 1, y + h - 1], radius=5, fill=yel, outline=blk)
    d.line([(bx + 1, y + h - 1), (bx + 13, y + h - 1)], fill=yel)
    # info icon + bold title + close cross, like an XP balloon tip
    d.ellipse([x + 5, y + 5, x + 14, y + 14], fill=XP_BLUE, outline=hx('#0B3A9E'))
    d.line([(x + 9, y + 9), (x + 9, y + 12)], fill=WHITE); d.point((x + 9, y + 7), fill=WHITE)
    d.line([(x + 10, y + 9), (x + 10, y + 12)], fill=WHITE); d.point((x + 10, y + 7), fill=WHITE)
    text1(img, (x + 18, y + 2), title, ft, blk)
    cx = x + w - 12
    d.rectangle([cx - 1, y + 4, cx + 7, y + 12], outline=hx('#9D9D8E'))
    d.line([(cx + 1, y + 6), (cx + 5, y + 10)], fill=hx('#555555')); d.line([(cx + 5, y + 6), (cx + 1, y + 10)], fill=hx('#555555'))
    for i, ln in enumerate(lines): text1(img, (x + 6, y + 18 + i * 11), ln, fb, blk)
    return h


def overview():
    wp = Image.open(os.path.join(UI, 'pc_wallpaper.png')).convert('RGBA').resize((640, 480), Image.NEAREST)
    bar = Image.open(os.path.join(UI, 'xp_taskbar.png')).convert('RGBA')
    tray = Image.open(os.path.join(UI, 'xp_tray.png')).convert('RGBA')
    icon = Image.open(os.path.join(UI, 'xp_icon_cards.png')).convert('RGBA')
    coin_path = os.path.join(UI, 'xp_coin.png')
    PW, DH, TB, STRIP = 232, 150, 30, 64
    out = Image.new('RGBA', (PW * 3, DH + TB + STRIP), hx('#ECE9D8'))
    fl, fs = font('PixeloidSans.ttf', 9), font('Ithaca.ttf', 16)
    for i, (key, name, fn, line, pose) in enumerate(CONCEPTS):
        ox = i * PW
        panel = Image.new('RGBA', (PW, DH + TB))
        panel.alpha_composite(wp.crop((640 - PW, 450 - DH, 640, 450)), (0, 0))
        for x in range(0, PW, bar.width): panel.alpha_composite(bar, (x, DH))
        tw = 64
        tr = tray.crop((0, 0, 2, 30)); panel.alpha_composite(tr, (PW - tw, DH))
        fill = tray.crop((4, 0, 8, 30))
        for x in range(PW - tw + 2, PW, 4): panel.alpha_composite(fill, (x, DH))
        if os.path.exists(coin_path): panel.alpha_composite(Image.open(coin_path).convert('RGBA'), (PW - tw + 5, DH + 9))
        text1(panel, (PW - 30, DH + 9), '15:58', fl, WHITE)
        # a desktop icon to point at
        panel.alpha_composite(icon, (8, 8))
        lw = fl.getlength('Карты')
        ImageDraw.Draw(panel).rectangle([24 - lw / 2 - 1, 41, 24 + lw / 2 + 1, 51], fill=hx('#0B3A9E') if pose == 'point' else (0, 0, 0, 0))
        text1(panel, (24 - lw / 2 + 1, 41), 'Карты', fl, hx('#000000'))
        text1(panel, (24 - lw / 2, 40), 'Карты', fl, WHITE)
        # assistant in the bottom-right corner, balloon up-left of it
        spr = fn(pose)
        sx, sy = PW - FW - 4, DH - FH - 1
        panel.alpha_composite(spr, (sx, sy))
        balloon(panel, sx - 136, sy + 2, 150, name, line, (sx + 12, sy + 12))
        out.alpha_composite(panel, (ox, 0))
        # frame strip under the desktop
        sh = sheet(fn)
        out.alpha_composite(sh, (ox + 8, DH + TB + 4))
        for k, p in enumerate(POSES):
            text1(out, (ox + 8 + k * FW + 24 - fl.getlength(p) / 2, DH + TB + 52), p, fl, hx('#555555'))
        text1(out, (ox + 8 + 3 * FW + 8, DH + TB + 18), name, fs, hx('#000000'))
        text1(out, (ox + 8 + 3 * FW + 8, DH + TB + 34), f'{i + 1} / 3', fl, hx('#555555'))
        ImageDraw.Draw(out).line([(ox + PW - 1, 0), (ox + PW - 1, out.height)], fill=hx('#000000'))
    return up(out, 3)


if __name__ == '__main__':
    for key, name, fn, line, pose in CONCEPTS:
        s = sheet(fn)
        s.save(os.path.join(HERE, f'{key}_sheet.png'))
        up(s, 4).save(os.path.join(HERE, f'{key}_sheet_x4.png'))
    overview().save(os.path.join(HERE, 'overview.png'))
    print('ok')
