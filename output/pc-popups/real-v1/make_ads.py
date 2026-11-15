# Генератор рекламных баннеров для мини-игры «Закрой окно» (игровой ПК, Гоблинсофт).
# Подход: «стоковое фото» 2000-х, неряшливо вырезанное и пережатое в JPEG,
# крикливый текст пиксельными шрифтами игры (без сглаживания), 2 кадра мигания.
# Запуск: python3 make_ads.py [папка_с_фото] [папка_вывода]
# Нужны: Pillow, numpy, scipy. Пути к шрифтам/скинам — переменные ниже.
from PIL import Image, ImageDraw, ImageFont
import numpy as np
from scipy import ndimage
import io, math, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = sys.argv[1] if len(sys.argv) > 1 else HERE          # где лежат photo_*.png
OUT = sys.argv[2] if len(sys.argv) > 2 else HERE          # куда класть pc_ad_*.png
ASSETS = os.environ.get('HUKATON_ASSETS', '/mnt/user-data/uploads/hukaton/hukaton/Assets/')
FONTS = ASSETS + 'Content/UI/Typography/Fonts/'
ITH = ImageFont.truetype(FONTS + 'Ithaca.ttf', 16)
ITH32 = ImageFont.truetype(FONTS + 'Ithaca.ttf', 32)
PIX = ImageFont.truetype(FONTS + 'PixeloidSans.ttf', 9)
W, H = 180, 150

# slug -> (заголовок окна, скин окна: red/lime/pink или None = обычное окно XP)
TITLES = {
    'prize':    ('Вы выиграли!!!', 'red'),
    'mortgage': ('Ипотека одобрена', 'lime'),
    'mimics':   ('Мимики рядом с вами', 'pink'),
    'virus':    ('Гоблинсофт Защитник', None),
    'hearts':   ('Сердца БЕСПЛАТНО!', 'lime'),
    'diet':     ('1 странный трюк', 'red'),
    'oracle':   ('Гадалка Онлайн', 'pink'),
    'speedup':  ('Ускоритель Гвоздика', None),
    'cassette': ('Хиты болот 2003', 'pink'),
    'income':   ('Заработок дома', 'lime'),
    'bones':    ('Кости ищут кости', 'red'),
    'update':   ('Центр обновлений', None),
    'room':     ('Комната', None),
}

WARN = []


# ---------------------------------------------------------------- helpers
def text(d, xy, s, font=PIX, fill=(0, 0, 0), outline=None, shadow=None, anchor='la', check=True):
    d.fontmode = '1'
    x, y = xy
    if check:
        bb = d.textbbox((x, y), s, font=font, anchor=anchor)
        if bb[0] < 0 or bb[2] > W:
            WARN.append(f'text out of banner: {s!r} {bb}')
    if shadow:
        d.text((x + shadow[0], y + shadow[1]), s, font=font, fill=shadow[2], anchor=anchor)
    if outline:
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                if dx or dy:
                    d.text((x + dx, y + dy), s, font=font, fill=outline, anchor=anchor)
    d.text((x, y), s, font=font, fill=fill, anchor=anchor)


def tw(s, font=PIX):
    return font.getbbox(s)[2]


def wrap(s, width, font=PIX):
    out, cur = [], ''
    for w in s.split(' '):
        t = (cur + ' ' + w).strip()
        if tw(t, font) <= width:
            cur = t
        else:
            out.append(cur); cur = w
    out.append(cur)
    return out


def jpeg(im, q=38):
    b = io.BytesIO(); im.convert('RGB').save(b, 'JPEG', quality=q); b.seek(0)
    return Image.open(b).convert('RGB')


def load(name):
    # Фото генератора — RGBA; по краям бывает альфа 192. Цвет берём как есть,
    # альфу выкидываем (не композим на чёрное!).
    return Image.open(os.path.join(SRC, name)).convert('RGBA').convert('RGB')


def cover(im, size):
    tw_, th = size
    sc = max(tw_ / im.width, th / im.height)
    im = im.resize((max(tw_, round(im.width * sc)), max(th, round(im.height * sc))), Image.LANCZOS)
    x0 = (im.width - tw_) // 2; y0 = (im.height - th) // 2
    return im.crop((x0, y0, x0 + tw_, y0 + th))


def rectphoto(name, crop, size, q=38, offset=None):
    im = load(name).crop(crop)
    if offset is None:
        im = cover(im, size)
    else:
        sc = max(size[0] / im.width, size[1] / im.height)
        im = im.resize((round(im.width * sc), round(im.height * sc)), Image.LANCZOS)
        im = im.crop((offset[0], offset[1], offset[0] + size[0], offset[1] + size[1]))
    return jpeg(im, q)


def cutout(name, crop, h=None, w=None, q=38, thr=226):
    """Вырезать белый студийный фон: пережать в JPEG, убрать почти-белые области,
    связанные с краем (белки глаз/зубы внутри остаются), край — неряшливый."""
    im = load(name).crop(crop)
    sc = (h / im.height) if h else (w / im.width)
    im = im.resize((max(1, round(im.width * sc)), max(1, round(im.height * sc))), Image.LANCZOS)
    im = jpeg(im, q)
    a = np.asarray(im).astype(int)
    white = (a[..., 0] > thr) & (a[..., 1] > thr) & (a[..., 2] > thr)
    lab, n = ndimage.label(white)
    edge = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))) - {0}
    bg = np.isin(lab, list(edge))
    alpha = np.where(bg, 0, 255).astype(np.uint8)
    out = im.convert('RGBA'); out.putalpha(Image.fromarray(alpha, 'L'))
    return out


def gradient(im, box, c0, c1, horizontal=False):
    d = ImageDraw.Draw(im)
    x0, y0, x1, y1 = box
    n = (x1 - x0) if horizontal else (y1 - y0)
    for i in range(n + 1):
        t = i / max(1, n)
        c = tuple(int(c0[j] + (c1[j] - c0[j]) * t) for j in range(3))
        if horizontal:
            d.line([(x0 + i, y0), (x0 + i, y1)], fill=c)
        else:
            d.line([(x0, y0 + i), (x1, y0 + i)], fill=c)


def rays(im, cx, cy, n, col, phase=0):
    d = ImageDraw.Draw(im)
    for i in range(0, n, 2):
        a0 = i * 2 * math.pi / n + phase; a1 = a0 + 2 * math.pi / n
        d.polygon([(cx, cy), (cx + 300 * math.cos(a0), cy + 300 * math.sin(a0)),
                   (cx + 300 * math.cos(a1), cy + 300 * math.sin(a1))], fill=col)


def burst(d, cx, cy, r1, r2, n, fill, outline):
    pts = []
    for i in range(n * 2):
        r = r1 if i % 2 == 0 else r2
        t = i * math.pi / n - math.pi / 2
        pts.append((round(cx + r * math.cos(t)), round(cy + r * math.sin(t))))
    d.polygon(pts, fill=fill, outline=outline)


def button(d, box, label, face=(20, 150, 20), dark=(0, 60, 0), light=(120, 230, 120),
           low=(0, 90, 0), tcol=(255, 255, 0), tout=(0, 50, 0), font=PIX):
    x0, y0, x1, y1 = box
    d.rectangle(box, fill=face, outline=dark)
    d.line([(x0 + 1, y0 + 1), (x1 - 1, y0 + 1)], fill=light)
    d.line([(x0 + 1, y1 - 1), (x1 - 1, y1 - 1)], fill=low)
    asc = 9 if font is PIX else 12
    ty = (y0 + y1 + 1) // 2 - asc // 2 - (1 if font is PIX else 3)
    text(d, ((x0 + x1) // 2 + 1, ty), label, font, tcol, outline=tout, anchor='ma')


def xpbutton(d, box, label, focus=False):
    x0, y0, x1, y1 = box
    d.rectangle(box, fill=(244, 243, 238), outline=(0, 60, 116))
    for p in [(x0, y0), (x1, y0), (x0, y1), (x1, y1)]:
        d.point(p, fill=(236, 233, 216))
    d.line([(x0 + 1, y1 - 1), (x1 - 1, y1 - 1)], fill=(214, 208, 197))
    d.line([(x1 - 1, y0 + 1), (x1 - 1, y1 - 1)], fill=(214, 208, 197))
    if focus:
        d.rectangle([x0 + 1, y0 + 1, x1 - 1, y1 - 1], outline=(122, 158, 233))
        d.rectangle([x0 + 2, y0 + 2, x1 - 2, y1 - 2], outline=(188, 212, 246))
    text(d, ((x0 + x1) // 2 + 1, (y0 + y1) // 2 - 5), label, PIX, (0, 0, 0), anchor='ma')


def xpprogress(d, box, frac, blocks=(40, 200, 40), offset=0):
    x0, y0, x1, y1 = box
    d.rectangle(box, fill=(255, 255, 255), outline=(104, 104, 104))
    inner = x1 - x0 - 3
    filled = round(inner * frac)
    x = x0 + 2 + offset
    while x + 6 <= x0 + 2 + max(filled, 6) + offset and x + 6 <= x1 - 1:
        d.rectangle([x, y0 + 2, x + 5, y1 - 2], fill=blocks)
        d.line([(x, y0 + 2), (x + 5, y0 + 2)], fill=tuple(min(255, c + 90) for c in blocks))
        x += 8


HEART = ["01101100", "11111110", "11111110", "01111100", "00111000", "00010000"]
CHECK = ["0000001", "0000011", "1000110", "1101100", "0111000", "0010000"]
EYE_SPARK = ["00100", "00100", "11011", "00100", "00100"]


def bitmap(d, x, y, bm, col, scale=1):
    for j, row in enumerate(bm):
        for i, c in enumerate(row):
            if c == '1':
                d.rectangle([x + i * scale, y + j * scale, x + i * scale + scale - 1, y + j * scale + scale - 1], fill=col)


def arrow_right(d, x, y, col=(150, 0, 0), fill=(255, 255, 255)):
    d.polygon([(x + 8, y - 3), (x + 14, y), (x + 8, y + 3)], fill=fill, outline=col)
    d.line([(x, y), (x + 8, y)], fill=col, width=2)


def underline(d, xy, s, font, col, anchor='ma'):
    bb = d.textbbox(xy, s, font=font, anchor=anchor)
    d.line([(bb[0], bb[3] + 1), (bb[2], bb[3] + 1)], fill=col)


def new_frame(bg=(255, 255, 255)):
    im = Image.new('RGBA', (W, H), bg + (255,))
    return im, ImageDraw.Draw(im)


# ---------------------------------------------------------------- banners
def ad_prize(k):
    im, d = new_frame()
    for y in range(H):
        t = y / (H - 1)
        c = (255, int(30 + 190 * t), int(20 * t)) if k == 0 else (255, int(200 - 150 * t), int(20 + 30 * (1 - t)))
        d.line([(0, y), (W, y)], fill=c)
    rays(im, 56, 96, 16, (255, 240, 90) if k == 0 else (255, 255, 170), 0 if k == 0 else math.pi / 16)
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W - 1, 21], fill=(200, 0, 0) if k == 0 else (20, 20, 160))
    text(d, (W // 2, 2), 'ПОЗДРАВЛЯЕМ!!!', ITH, (255, 236, 0) if k == 0 else (255, 255, 255), outline=(0, 0, 0), anchor='ma')
    d.rectangle([0, 22, W - 1, 33], fill=(0, 0, 0))
    text(d, (W // 2, 23), 'Вы 1 000 000-й посетитель лавки!', PIX, (255, 255, 255), anchor='ma')
    # эталон: глобальный порог по белому (так и было одобрено)
    im0 = load('photo_prize.png').crop((40, 10, 500, 512))
    sc = 110 / im0.height
    im0 = jpeg(im0.resize((max(1, round(im0.width * sc)), 110), Image.LANCZOS), 38)
    a = np.asarray(im0)
    alpha = np.where((a[..., 0] > 232) & (a[..., 1] > 232) & (a[..., 2] > 232), 0, 255).astype(np.uint8)
    p = im0.convert('RGBA'); p.putalpha(Image.fromarray(alpha, 'L'))
    im.alpha_composite(p, (-10, H - 110))
    d = ImageDraw.Draw(im)
    burst(d, 133, 64, 30, 22, 14, (255, 250, 40) if k == 0 else (255, 60, 60), (120, 0, 0))
    text(d, (133, 48), 'ПРИЗ', PIX, (200, 0, 0) if k == 0 else (255, 255, 255), anchor='ma')
    text(d, (133, 57), '5000', ITH, (0, 0, 0) if k == 0 else (255, 240, 0), anchor='ma')
    text(d, (133, 71), 'монет!', PIX, (200, 0, 0) if k == 0 else (255, 255, 255), anchor='ma')
    bx0, by0, bx1, by1 = 92, 104, 175, 121
    d.rectangle([bx0, by0, bx1, by1], fill=(20, 150, 20), outline=(0, 60, 0))
    d.line([(bx0 + 1, by0 + 1), (bx1 - 1, by0 + 1)], fill=(120, 230, 120))
    d.line([(bx0 + 1, by1 - 1), (bx1 - 1, by1 - 1)], fill=(0, 90, 0))
    text(d, ((bx0 + bx1) // 2 + 1, by0 + 4), 'ЗАБРАТЬ >>', PIX, (255, 255, 0) if k == 0 else (255, 255, 255), outline=(0, 50, 0), anchor='ma')
    if k == 0:
        d.polygon([(84, 110), (90, 113), (84, 116)], fill=(255, 255, 255), outline=(150, 0, 0))
        d.line([(76, 113), (84, 113)], fill=(150, 0, 0), width=2)
    text(d, (W - 2, H - 24), '*акция действует', PIX, (90, 40, 0), anchor='ra')
    text(d, (W - 2, H - 14), 'до вчерашнего дня', PIX, (90, 40, 0), anchor='ra')
    return im


def ad_mortgage(k):
    im, d = new_frame()
    # фон — «фото объекта»: кривая пещера у болота
    bg = rectphoto('photo_mortgage_cave.png', (60, 0, 470, 341), (W, H), q=34)
    im.paste(bg, (0, 0))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W - 1, 19], fill=(0, 140, 0) if k == 0 else (210, 0, 0))
    text(d, (W // 2, 2), 'ВЫ ОДОБРЕНЫ!', ITH, (255, 240, 0) if k == 0 else (255, 255, 255), outline=(0, 0, 0), anchor='ma')
    d.rectangle([0, 20, W - 1, 31], fill=(0, 0, 0))
    text(d, (W // 2, 21), 'Ипотека на пещеру 2×2 м!', PIX, (255, 255, 255), anchor='ma')
    # риелтор
    p = cutout('photo_mortgage.png', (10, 30, 512, 512), h=100)
    im.alpha_composite(p, (W - p.width + 8, H - 13 - p.height + 4))
    d = ImageDraw.Draw(im)
    burst(d, 37, 61, 28, 21, 13, (255, 245, 30) if k == 0 else (255, 70, 50), (110, 0, 0))
    text(d, (38, 36), '0%', ITH32, (210, 0, 0) if k == 0 else (255, 255, 0), outline=None if k == 0 else (90, 0, 0), anchor='ma')
    text(d, (37, 64), 'взнос!', PIX, (0, 0, 0) if k == 0 else (255, 255, 0), anchor='ma')
    text(d, (5, 92), 'Вид на болото!', PIX, (255, 255, 255), outline=(0, 70, 0), anchor='la')
    button(d, (4, 105, 78, 122), 'ОФОРМИТЬ', face=(20, 150, 20) if k == 0 else (240, 120, 0),
           dark=(0, 60, 0) if k == 0 else (110, 40, 0), light=(120, 230, 120) if k == 0 else (255, 200, 120),
           low=(0, 90, 0) if k == 0 else (160, 70, 0), tcol=(255, 255, 0) if k == 0 else (255, 255, 255),
           tout=(0, 50, 0) if k == 0 else (90, 30, 0))
    if k == 1:
        d.rectangle([2, 34, 37, 44], fill=(255, 0, 0))
        text(d, (4, 34), 'НОВОЕ!', PIX, (255, 255, 255))
    d.rectangle([0, H - 13, W - 1, H - 1], fill=(0, 0, 0))
    text(d, (W // 2, H - 12), '*ставка 99% годовых. Пожизненно', PIX, (200, 200, 200), anchor='ma')
    return im


def ad_mimics(k):
    im, d = new_frame((255, 200, 230))
    gradient(im, (0, 32, W - 1, H - 1), (255, 215, 238), (255, 150, 205))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W - 1, 19], fill=(220, 0, 120) if k == 0 else (120, 0, 160))
    text(d, (W // 2, 2), 'ГОРЯЧИЕ МИМИКИ', ITH, (255, 255, 0) if k == 0 else (255, 255, 255), outline=(60, 0, 30), anchor='ma')
    d.rectangle([0, 20, W - 1, 31], fill=(0, 0, 0))
    text(d, (W // 2, 21), 'в вашем районе! Онлайн: 4', PIX, (255, 255, 255), anchor='ma')
    src = load('photo_mimics.png')
    cells = [(8, 8, 250, 250), (262, 8, 504, 250), (8, 262, 250, 504), (262, 262, 504, 504)]
    names = ['Люся', 'Клёпа', 'Глазка', 'Роза']
    for i, c in enumerate(cells):
        x = 1 + i * 45
        y = 35
        d.rectangle([x, y, x + 43, y + 64], fill=(255, 255, 255), outline=(170, 60, 120))
        ph = jpeg(cover(src.crop(c), (42, 40)), 36)
        im.paste(ph, (x + 1, y + 1))
        d = ImageDraw.Draw(im)
        text(d, (x + 22, y + 42), names[i], PIX, (150, 0, 80), anchor='ma')
        dot = (0, 200, 0) if (k == 0 or i % 2) else (180, 255, 180)
        d.rectangle([x + 4, y + 56, x + 6, y + 58], fill=dot)
        text(d, (x + 9, y + 53), '0,3 км', PIX, (90, 90, 90))
        if k == 1 and i == 2:
            d.rectangle([x + 1, y + 1, x + 33, y + 10], fill=(255, 0, 0))
            text(d, (x + 3, y + 1), 'НОВАЯ', PIX, (255, 255, 0))
    hc = (230, 0, 60) if k == 0 else (255, 255, 255)
    bitmap(d, 18, 109, HEART, hc); bitmap(d, 155, 109, HEART, hc)
    button(d, (32, 104, 148, 121), 'НАПИСАТЬ', face=(255, 90, 0) if k == 0 else (230, 0, 100),
           dark=(120, 30, 0), light=(255, 190, 120), low=(170, 50, 0), tcol=(255, 255, 255), tout=(110, 20, 0))
    text(d, (W // 2, H - 23), '*всем мимикам есть 18 лет', PIX, (120, 0, 60), anchor='ma')
    text(d, (W // 2, H - 13), 'и иногда они кусаются', PIX, (120, 0, 60), anchor='ma')
    return im


def ad_virus(k):
    im, d = new_frame((255, 255, 255))
    d.rectangle([0, 0, W - 1, 18], fill=(220, 0, 0) if k == 0 else (255, 230, 0))
    for x in (8, W - 18):
        d.polygon([(x + 5, 3), (x + 10, 14), (x, 14)], fill=(255, 230, 0) if k == 0 else (220, 0, 0), outline=(0, 0, 0))
        d.line([(x + 5, 6), (x + 5, 10)], fill=(0, 0, 0)); d.point((x + 5, 12), fill=(0, 0, 0))
    text(d, (W // 2, 2), 'ВНИМАНИЕ!', ITH, (255, 255, 255) if k == 0 else (220, 0, 0), outline=(0, 0, 0), anchor='ma')
    ph = rectphoto('photo_virus.png', (30, 20, 490, 512), (54, 58), q=34)
    d.rectangle([2, 22, 57, 81], outline=(128, 128, 128))
    im.paste(ph, (3, 23))
    d = ImageDraw.Draw(im)
    text(d, (61, 21), 'Ваш гоблин заражён', PIX, (0, 0, 0))
    text(d, (61, 30), '37 ВИРУСАМИ!', ITH, (210, 0, 0))
    for i, s in enumerate(['× Сопли.exe', '× Лень.dll', '× Чих32.sys']):
        text(d, (63, 46 + i * 10), s, PIX, (210, 0, 0) if (k == 0 or i != 1) else (255, 150, 150))
    text(d, (3, 84), 'Сканирование... ' + ('97%' if k == 0 else '98%'), PIX, (60, 60, 60))
    xpprogress(d, (3, 94, 176, 103), 0.97 if k == 0 else 0.98, blocks=(40, 190, 40) if k == 0 else (220, 40, 40))
    button(d, (14, 108, 166, 125), 'ОЧИСТИТЬ СЕЙЧАС', face=(220, 0, 0) if k == 0 else (20, 150, 20),
           dark=(90, 0, 0) if k == 0 else (0, 60, 0), light=(255, 120, 120) if k == 0 else (120, 230, 120),
           low=(140, 0, 0) if k == 0 else (0, 90, 0), tcol=(255, 255, 255) if k == 0 else (255, 255, 0),
           tout=(80, 0, 0) if k == 0 else (0, 50, 0))
    if k == 0:
        arrow_right(d, 0, 116)
    d.rectangle([0, H - 14, W - 1, H - 1], fill=(236, 233, 216))
    d.line([(0, H - 14), (W - 1, H - 14)], fill=(172, 168, 153))
    text(d, (3, H - 12), 'Лечение платное. Болезнь — нет.', PIX, (90, 90, 90))
    return im


def ad_hearts(k):
    im, d = new_frame()
    gradient(im, (0, 0, W - 1, H - 1), (0, 0, 110), (0, 140, 255))
    rays(im, 40, 100, 20, (20, 60, 190) if k == 0 else (30, 90, 220), 0 if k == 0 else math.pi / 20)
    d = ImageDraw.Draw(im)
    text(d, (W // 2, 2), 'СКАЧАТЬ БОЛЬШЕ', ITH, (255, 240, 0), outline=(0, 0, 60), anchor='ma')
    text(d, (W // 2, 11), 'СЕРДЕЦ!', ITH32, (255, 30, 60) if k == 0 else (255, 255, 255),
         outline=(255, 255, 255) if k == 0 else (200, 0, 40), anchor='ma')
    p = cutout('photo_hearts.png', (50, 25, 470, 512), h=104)
    im.alpha_composite(p, (-12, H - 14 - p.height + 2))
    d = ImageDraw.Draw(im)
    for i, s in enumerate(['бесплатно', 'без смс', 'без регистрации']):
        bitmap(d, 84, 47 + i * 11, CHECK, (80, 255, 80))
        text(d, (93, 44 + i * 11), s, PIX, (255, 255, 255), outline=(0, 0, 80))
    button(d, (90, 81, 176, 100), 'СКАЧАТЬ', face=(20, 170, 20) if k == 0 else (255, 200, 0),
           dark=(0, 60, 0) if k == 0 else (120, 70, 0), light=(140, 240, 140) if k == 0 else (255, 250, 170),
           low=(0, 100, 0) if k == 0 else (180, 120, 0), tcol=(255, 255, 255) if k == 0 else (200, 0, 0),
           tout=(0, 50, 0) if k == 0 else (255, 255, 255), font=ITH)
    # стрелка вниз
    ax, ay = 167, 86 + (0 if k == 0 else 2)
    d.polygon([(ax - 4, ay), (ax + 4, ay), (ax, ay + 5)], fill=(255, 255, 255) if k == 0 else (200, 0, 0))
    d.rectangle([90, 104, 176, 129], fill=(0, 0, 0), outline=(120, 120, 120))
    text(d, (133, 104), 'скачали:', PIX, (160, 160, 160), anchor='ma')
    text(d, (133, 114), '1 337' if k == 0 else '1 338', ITH, (0, 255, 0), anchor='ma')
    d.rectangle([0, H - 13, W - 1, H - 1], fill=(0, 0, 0))
    text(d, (W // 2, H - 12), 'гоблинов! Вирусов почти нет*', PIX, (255, 230, 0), anchor='ma')
    return im


def ad_diet(k):
    im, d = new_frame((255, 255, 255))
    d.rectangle([0, 0, W - 1, 27], fill=(255, 240, 0) if k == 0 else (255, 255, 255))
    text(d, (W // 2, 1), 'Гоблины НЕНАВИДЯТ', ITH, (0, 0, 0) if k == 0 else (210, 0, 0), anchor='ma')
    text(d, (W // 2, 16), 'этот 1 странный трюк!', PIX, (210, 0, 0) if k == 0 else (0, 0, 0), anchor='ma')
    d.line([(0, 28), (W - 1, 28)], fill=(0, 0, 0))
    for x, name, crop, lab, col in [(3, 'photo_diet_before.png', (30, 10, 470, 500), 'ДО', (220, 0, 0)),
                                    (97, 'photo_diet_after.png', (50, 0, 490, 500), 'ПОСЛЕ', (0, 160, 0))]:
        ph = rectphoto(name, crop, (78, 76), q=34)
        d.rectangle([x - 1, 31, x + 78, 108], outline=(150, 150, 150))
        im.paste(ph, (x, 32))
        d = ImageDraw.Draw(im)
        lw = tw(lab) + 6
        d.rectangle([x, 96, x + lw, 107], fill=col)
        text(d, (x + 3, 96), lab, PIX, (255, 255, 255))
    # стрелка между фото
    d.polygon([(83, 64), (92, 70), (83, 76)], fill=(255, 0, 0) if k == 0 else (255, 200, 0), outline=(100, 0, 0))
    text(d, (W // 2, 111), '-40 монет за 1 день!', ITH, (210, 0, 0), anchor='ma')
    link = (0, 0, 238) if k == 0 else (85, 26, 139)
    text(d, (W // 2, 126), 'Узнать трюк >>', PIX, link, anchor='ma')
    underline(d, (W // 2, 126), 'Узнать трюк >>', PIX, link)
    if k == 1:
        text(d, (W - 3, 126), 'ЖМИ!', PIX, (255, 0, 0), anchor='ra')
    text(d, (W // 2, H - 11), '*врачи-гоблины в шоке', PIX, (150, 150, 150), anchor='ma')
    return im


def ad_oracle(k):
    im, d = new_frame((0, 0, 0))
    bg = rectphoto('photo_oracle.png', (0, 30, 512, 457), (W, H), q=36)
    im.paste(bg, (0, 0))
    d = ImageDraw.Draw(im)
    # блёстки (мигают)
    pts = [(12, 30), (166, 22), (150, 98), (20, 104), (100, 18), (70, 70)] if k == 0 else \
          [(28, 24), (172, 40), (140, 120), (8, 88), (120, 30), (60, 110)]
    for (x, y) in pts:
        bitmap(d, x - 2, y - 2, EYE_SPARK, (255, 255, 200))
    text(d, (W // 2, 2), 'Гадалка Онлайн', ITH, (255, 215, 0) if k == 0 else (255, 140, 230), outline=(60, 0, 80), anchor='ma')
    for i, s in enumerate(['Узнай, кто', 'съел твоего', 'кабана!']):
        text(d, (3, 34 + i * 10), s, PIX, (255, 255, 255), outline=(60, 0, 80))
    burst(d, 152, 62, 27, 20, 12, (255, 215, 0) if k == 0 else (220, 0, 160), (80, 0, 80))
    text(d, (152, 47), '1-й', PIX, (80, 0, 80) if k == 0 else (255, 255, 255), anchor='ma')
    text(d, (152, 56), 'вопрос', PIX, (80, 0, 80) if k == 0 else (255, 255, 255), anchor='ma')
    text(d, (152, 65), '0 монет', PIX, (200, 0, 0) if k == 0 else (255, 255, 0), anchor='ma')
    button(d, (116, 128, 177, 146), 'СПРОСИТЬ', face=(140, 0, 200) if k == 0 else (255, 200, 0),
           dark=(50, 0, 80), light=(210, 140, 255) if k == 0 else (255, 250, 180),
           low=(90, 0, 130) if k == 0 else (180, 120, 0), tcol=(255, 255, 255) if k == 0 else (80, 0, 80),
           tout=(50, 0, 80) if k == 0 else None)
    text(d, (3, H - 22), '*ответ придёт', PIX, (230, 200, 255), outline=(30, 0, 40))
    text(d, (3, H - 12), 'во сне', PIX, (230, 200, 255), outline=(30, 0, 40))
    return im


def ad_speedup(k):
    im, d = new_frame((236, 233, 216))
    d.rectangle([0, 0, W - 1, 17], fill=(210, 0, 0) if k == 0 else (255, 230, 0))
    text(d, (W // 2, 1), 'Ваш ПК работает МЕДЛЕННО!', ITH, (255, 255, 0) if k == 0 else (210, 0, 0),
         outline=(0, 0, 0) if k == 0 else None, anchor='ma')
    ph = rectphoto('photo_speedup.png', (0, 60, 512, 500), (74, 64), q=34)
    d.rectangle([2, 21, 77, 86], outline=(128, 128, 128))
    im.paste(ph, (3, 22))
    d = ImageDraw.Draw(im)
    # график до/после
    d.rectangle([81, 21, 177, 86], fill=(255, 255, 255), outline=(128, 128, 128))
    text(d, (129, 22), 'Скорость ПК:', PIX, (0, 0, 0), anchor='ma')
    d.line([(112, 34), (112, 82)], fill=(0, 0, 0))
    text(d, (110, 38), 'ДО', PIX, (0, 0, 0), anchor='ra')
    d.rectangle([113, 40, 116, 47], fill=(220, 0, 0))
    text(d, (119, 38), '3%', PIX, (220, 0, 0))
    text(d, (110, 58), 'ПОСЛЕ', PIX, (0, 0, 0), anchor='ra')
    d.rectangle([113, 60, 113 + (60 if k == 0 else 62), 67], fill=(0, 170, 0))
    text(d, (174, 70), '9000%', PIX, (0, 140, 0), anchor='ra')
    if k == 1:
        bitmap(d, 168, 52, EYE_SPARK, (255, 200, 0))
    text(d, (W // 2, 90), 'Установите', PIX, (0, 0, 0), anchor='ma')
    text(d, (W // 2, 100), '«Ускоритель Гвоздика»!', PIX, (0, 0, 160), anchor='ma')
    button(d, (40, 113, 140, 130), 'УСКОРИТЬ ПК', face=(20, 150, 20) if k == 0 else (240, 120, 0),
           dark=(0, 60, 0) if k == 0 else (110, 40, 0), light=(120, 230, 120) if k == 0 else (255, 200, 120),
           low=(0, 90, 0) if k == 0 else (160, 70, 0), tcol=(255, 255, 0) if k == 0 else (255, 255, 255),
           tout=(0, 50, 0) if k == 0 else (90, 30, 0))
    if k == 0:
        arrow_right(d, 22, 121)
    text(d, (W // 2, H - 12), '*ускоряет только улиток', PIX, (120, 120, 120), anchor='ma')
    return im


def ad_cassette(k):
    im, d = new_frame((0, 0, 0))
    gradient(im, (0, 28, W - 1, H - 1), (20, 0, 60), (0, 0, 0))
    gradient(im, (0, 0, W - 1, 27), (255, 120, 0) if k == 0 else (255, 0, 160), (255, 0, 160) if k == 0 else (255, 120, 0), horizontal=True)
    d = ImageDraw.Draw(im)
    text(d, (W // 2, 1), 'КУПИ КАССЕТУ —', ITH, (255, 255, 255), outline=(80, 0, 40), anchor='ma')
    text(d, (W // 2, 13), 'ПОЛУЧИ ВТОРУЮ!', ITH, (255, 255, 0), outline=(80, 0, 40), anchor='ma')
    # обложка сборника
    d.rectangle([6, 34, 79, 107], fill=(80, 80, 80))
    ph = rectphoto('photo_cassette.png', (30, 20, 490, 480), (72, 72), q=36)
    im.paste(ph, (4, 32))
    d = ImageDraw.Draw(im)
    d.rectangle([3, 31, 76, 104], outline=(255, 255, 255))
    text(d, (40, 33), 'ХИТЫ БОЛОТ', PIX, (255, 255, 0), outline=(0, 0, 0), anchor='ma')
    text(d, (40, 90), '2003', ITH, (255, 80, 200), outline=(255, 255, 255), anchor='ma')
    if k == 1:
        d.rectangle([47, 44, 79, 54], fill=(255, 0, 0))
        text(d, (49, 44), 'НОВОЕ', PIX, (255, 255, 255))
    for i, s in enumerate(['1. Ква-бейби', '2. Тина моя', '3. Комар-диджей']):
        text(d, (82, 33 + i * 11), s, PIX, (120, 255, 255) if (k == 0 or i != 0) else (255, 255, 255))
    burst(d, 152, 86, 22, 16, 12, (255, 235, 0) if k == 0 else (255, 0, 120), (120, 0, 60))
    text(d, (152, 77), 'x2', ITH, (210, 0, 0) if k == 0 else (255, 255, 255), outline=None, anchor='ma')
    text(d, (82, 76), 'всего', PIX, (255, 255, 255))
    text(d, (82, 85), '99 монет', ITH, (255, 200, 0))
    button(d, (82, 112, 177, 129), 'КУПИТЬ', face=(255, 0, 140) if k == 0 else (0, 190, 220),
           dark=(80, 0, 40) if k == 0 else (0, 60, 80), light=(255, 140, 210) if k == 0 else (160, 250, 255),
           low=(160, 0, 90) if k == 0 else (0, 110, 140), tcol=(255, 255, 255), tout=(80, 0, 40) if k == 0 else (0, 60, 80))
    text(d, (4, 112), 'Лучшие хиты', PIX, (255, 255, 255))
    text(d, (4, 121), 'болот 2003!', PIX, (255, 255, 255))
    text(d, (W // 2, H - 12), '*вторая кассета пустая', PIX, (140, 140, 160), anchor='ma')
    return im


def ad_income(k):
    im, d = new_frame((255, 255, 255))
    d.rectangle([0, 0, W - 1, 12], fill=(0, 51, 153))
    text(d, (3, 1), 'БОЛОТНЫЕ НОВОСТИ', PIX, (255, 255, 255))
    text(d, (W - 3, 1), 'Финансы', PIX, (255, 220, 0), anchor='ra')
    text(d, (W // 2, 14), 'Заработок, не выходя', ITH, (200, 0, 0), anchor='ma')
    text(d, (W // 2, 26), 'из лавки!', ITH, (200, 0, 0), anchor='ma')
    ph = rectphoto('photo_income.png', (70, 0, 470, 470), (66, 76), q=34)
    d.rectangle([2, 41, 69, 118], outline=(150, 150, 150))
    im.paste(ph, (3, 42))
    d = ImageDraw.Draw(im)
    q = '«Мама одного гоблина из Болотного получает 5000 монет в день»'
    lines = wrap(q, 104)
    for i, s in enumerate(lines):
        col = (0, 0, 0)
        text(d, (74, 42 + i * 10), s, PIX, col)
    yb = 42 + len(lines) * 10 + 3
    link = (0, 0, 238) if k == 0 else (210, 0, 0)
    text(d, (125, yb), 'Узнай как!', ITH, link, anchor='ma')
    underline(d, (125, yb), 'Узнай как!', ITH, link)
    button(d, (74, 102, 177, 119), 'УЗНАТЬ >>', face=(20, 150, 20) if k == 0 else (240, 120, 0),
           dark=(0, 60, 0) if k == 0 else (110, 40, 0), light=(120, 230, 120) if k == 0 else (255, 200, 120),
           low=(0, 90, 0) if k == 0 else (160, 70, 0), tcol=(255, 255, 0) if k == 0 else (255, 255, 255),
           tout=(0, 50, 0) if k == 0 else (90, 30, 0))
    if k == 1:
        d.rectangle([3, 42, 35, 52], fill=(255, 0, 0))
        text(d, (5, 42), 'ЖМИ!', PIX, (255, 255, 0))
    d.line([(0, H - 26), (W - 1, H - 26)], fill=(200, 200, 200))
    text(d, (W // 2, H - 24), 'Мама из Болотного — не ваша.', PIX, (130, 130, 130), anchor='ma')
    text(d, (W // 2, H - 13), 'Результат не гарантирован*', PIX, (130, 130, 130), anchor='ma')
    return im


def ad_bones(k):
    im, d = new_frame((0, 0, 0))
    gradient(im, (0, 0, W - 1, H - 1), (30, 0, 20), (150, 0, 50))
    d = ImageDraw.Draw(im)
    text(d, (W // 2, 1), 'Знакомства для одиноких', ITH, (255, 255, 255), anchor='ma')
    text(d, (W // 2, 13), 'СКЕЛЕТОВ', ITH, (255, 120, 180) if k == 0 else (255, 255, 0), outline=(90, 0, 40), anchor='ma')
    # анкета с фото
    d.rectangle([3, 30, 80, 121], fill=(255, 255, 255), outline=(255, 150, 200))
    ph = rectphoto('photo_bones.png', (20, 10, 500, 512), (74, 78), q=36)
    im.paste(ph, (5, 32))
    d = ImageDraw.Draw(im)
    d.rectangle([5, 110, 78, 120], fill=(255, 220, 235))
    dot = (0, 200, 0) if k == 0 else (190, 255, 190)
    d.rectangle([8, 113, 10, 115], fill=dot)
    text(d, (13, 110), 'онлайн', PIX, (0, 120, 0))
    text(d, (85, 30), 'Кость, 300 лет', PIX, (255, 255, 0))
    for i, s in enumerate(['Ищу: кость', 'Рост: весь', 'Вес: 0 кг', 'Мясо: нет']):
        text(d, (85, 42 + i * 10), s, PIX, (255, 220, 235))
    hc = (255, 60, 120) if k == 0 else (255, 255, 255)
    bitmap(d, 155, 44, HEART, hc); bitmap(d, 165, 56, HEART, (255, 255, 255) if k == 0 else (255, 60, 120))
    button(d, (84, 86, 177, 103), 'ПОЗНАКОМИТЬСЯ', face=(230, 0, 90) if k == 0 else (255, 120, 180),
           dark=(90, 0, 40), light=(255, 130, 180) if k == 0 else (255, 210, 230),
           low=(150, 0, 60) if k == 0 else (200, 60, 120), tcol=(255, 255, 255), tout=(90, 0, 40))
    text(d, (130, 107), 'кости ищут', PIX, (255, 255, 255), anchor='ma')
    text(d, (130, 116), 'кости', PIX, (255, 255, 255), anchor='ma')
    d.rectangle([0, H - 13, W - 1, H - 1], fill=(0, 0, 0))
    text(d, (W // 2, H - 12), '*некромантам скидка 0%', PIX, (200, 120, 160), anchor='ma')
    return im


def ad_update(k):
    im, d = new_frame((236, 233, 216))
    d.rectangle([0, 0, W - 1, 33], fill=(255, 255, 255))
    d.line([(0, 34), (W - 1, 34)], fill=(172, 168, 153)); d.line([(0, 35), (W - 1, 35)], fill=(255, 255, 255))
    text(d, (4, 2), 'Доступно обновление', ITH, (0, 0, 0))
    text(d, (4, 14), 'Гоблинсофт 0.0.0.1', PIX, (0, 0, 0))
    text(d, (4, 23), 'Размер: 4 ТБ', PIX, (110, 110, 110))
    # «Гвоздик» — фото ржавого гвоздя с ушами, вырезанное
    nail = cutout('photo_update.png', (160, 20, 350, 500), h=72)
    im.alpha_composite(nail, (W - nail.width + 2, 37))
    d = ImageDraw.Draw(im)
    text(d, (4, 39), 'Загрузка: 1%', PIX, (0, 0, 0))
    xpprogress(d, (4, 50, 140, 60), 0.0, offset=0 if k == 0 else 8)
    text(d, (4, 63), 'Осталось: 9999 лет', PIX, (60, 60, 60))
    text(d, (4, 77), 'Перезагрузить сейчас?', PIX, (0, 0, 0))
    d.rectangle([4, 92, 12, 100], fill=(255, 255, 255), outline=(28, 81, 128))
    bitmap(d, 5, 93, CHECK, (33, 161, 33))
    text(d, (16, 91), 'Не спрашивать никогда', PIX, (150, 150, 150))
    xpbutton(d, (28, 112, 86, 130), 'Да', focus=(k == 0))
    xpbutton(d, (94, 112, 152, 130), 'Да', focus=(k == 1))
    text(d, (W - 3, H - 12), 'Отмена недоступна', PIX, (160, 160, 160), anchor='ra')
    return im


def ad_room(k):
    im, d = new_frame((0, 0, 0))
    src = load('photo_room.png')
    sc = H / src.height
    big = src.resize((round(src.width * sc), H), Image.LANCZOS)
    ox = 24
    ph = big.crop((ox, 0, ox + W, H))
    if k == 1:
        # лампа чуть мигнула
        ph = Image.eval(ph, lambda v: int(v * 0.9))
    ph = jpeg(ph, 30)
    im.paste(ph, (0, 0))
    d = ImageDraw.Draw(im)
    if k == 1:
        # в щели двери — глаз
        gx = round(310 * sc) - ox
        gy = round(118 * sc)
        d.line([(gx - 2, gy), (gx + 2, gy)], fill=(150, 145, 120))
        d.line([(gx - 1, gy - 1), (gx + 1, gy - 1)], fill=(80, 76, 60))
        d.point((gx, gy), fill=(15, 10, 5))
    text(d, (W // 2, H - 28), 'Ты проснулся. Почему ты здесь?', PIX, (205, 200, 160), anchor='ma')
    text(d, (W // 2, H - 15), 'Окно не закроется.', PIX, (130, 125, 95), anchor='ma')
    return im


ADS = [('prize', ad_prize), ('mortgage', ad_mortgage), ('mimics', ad_mimics), ('virus', ad_virus),
       ('hearts', ad_hearts), ('diet', ad_diet), ('oracle', ad_oracle), ('speedup', ad_speedup),
       ('cassette', ad_cassette), ('income', ad_income), ('bones', ad_bones), ('update', ad_update),
       ('room', ad_room)]


# ---------------------------------------------------------------- preview
def nine(src, w, h, L=8, B=4, R=8, T=26):
    out = Image.new('RGBA', (w, h)); sw, sh = src.size
    xs = [(0, L, 0, L), (L, sw - R, L, w - R), (sw - R, sw, w - R, w)]
    ys = [(0, T, 0, T), (T, sh - B, T, h - B), (sh - B, sh, h - B, h)]
    for sx0, sx1, dx0, dx1 in xs:
        for sy0, sy1, dy0, dy1 in ys:
            if dx1 > dx0 and dy1 > dy0:
                out.paste(src.crop((sx0, sy0, sx1, sy1)).resize((dx1 - dx0, dy1 - dy0), Image.NEAREST), (dx0, dy0))
    return out


def preview(frames, path, scale=2):
    skins = {}
    for s in ('red', 'lime', 'pink'):
        p = ASSETS + f'Computer/Art/Popups/xp_window_ad_{s}.png'
        if os.path.exists(p):
            skins[s] = Image.open(p).convert('RGBA')
    plain = Image.open(ASSETS + 'Computer/Art/Ui/xp_window.png').convert('RGBA')
    close = Image.open(ASSETS + 'Computer/Art/Ui/xp_close.png').convert('RGBA')
    ww, wh = W + 8, H + 30
    cols = 4  # два баннера в ряд, по два кадра
    gap = 12
    rows = math.ceil(len(frames) / 2)
    sw = 2 * (2 * ww + gap) + 3 * gap + 10
    sh = rows * (wh + 14 + gap) + gap
    sheet = Image.new('RGBA', (sw, sh), (58, 110, 165, 255))
    wp = ASSETS + 'Computer/Art/Ui/pc_wallpaper.png'
    if os.path.exists(wp):
        sheet.alpha_composite(Image.open(wp).convert('RGBA').resize((sw, sh), Image.NEAREST))
    dd = ImageDraw.Draw(sheet)
    for i, (slug, fr) in enumerate(frames):
        title, skin = TITLES[slug]
        col, row = i % 2, i // 2
        bx = gap + col * (2 * ww + gap + gap + 10)
        by = gap + row * (wh + 14 + gap)
        text(dd, (bx, by), f'{slug}  [{skin or "xp"}]', PIX, (255, 255, 255), outline=(0, 0, 0), check=False)
        for k in (0, 1):
            w = nine(skins.get(skin, plain), ww, wh)
            wd = ImageDraw.Draw(w)
            text(wd, (6, 6), title, ITH, (255, 255, 255), shadow=(1, 1, (0, 0, 60)), check=False)
            w.alpha_composite(close, (ww - 26, 3))
            w.alpha_composite(fr[k], (4, 26))
            sheet.alpha_composite(w, (bx + k * (ww + 6), by + 13))
    sheet = sheet.resize((sheet.width * scale, sheet.height * scale), Image.NEAREST)
    sheet.save(path)


if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    frames = []
    for slug, fn in ADS:
        fr = []
        for k in (0, 1):
            im = fn(k).convert('RGBA')
            assert im.size == (W, H)
            im.save(os.path.join(OUT, f'pc_ad_{slug}_{k}.png'))
            fr.append(im)
        frames.append((slug, fr))
    preview(frames, os.path.join(OUT, 'preview_all.png'))
    for t, _ in TITLES.values():
        if tw(t, ITH) > 128:
            WARN.append(f'title too wide for the bar (128): {t!r} {tw(t, ITH)}')
    for w in WARN:
        print('WARN', w)
    print('ok', len(frames))
