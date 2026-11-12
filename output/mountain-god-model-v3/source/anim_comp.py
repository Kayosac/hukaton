"""Composite A/B passes into two-panel frames (model | x-ray skeleton) with captions.
Run: python3 anim_comp.py INDIR OUTDIR [sheet]"""
import sys, os, json, glob
from PIL import Image, ImageDraw, ImageFont, ImageEnhance

IN, OUT = sys.argv[1], sys.argv[2]
SHEET = len(sys.argv) > 3 and sys.argv[3] == 'sheet'
os.makedirs(OUT, exist_ok=True)
meta = json.load(open(os.path.join(IN, 'meta.json')))
FD = '/usr/share/fonts/truetype/dejavu/'
f_title = ImageFont.truetype(FD + 'DejaVuSans-Bold.ttf', 17)
f_small = ImageFont.truetype(FD + 'DejaVuSans.ttf', 13)
LEG = [('#FFD34D', 'корпус'), ('#FF7AB8', 'голова'), ('#4DE3FF', 'опоры S'), ('#7DFF8A', 'руки C'),
       ('#B98CFF', 'руки A'), ('#FF9A4D', 'ноги L')]
def plural(n):
    if n % 10 == 1 and n % 100 != 11:
        return 'кость'
    if n % 10 in (2, 3, 4) and n % 100 not in (12, 13, 14):
        return 'кости'
    return 'костей'
frames = []
nclips = len(meta['clips'])
for ci, c in enumerate(meta['clips']):
    files = sorted(glob.glob(os.path.join(IN, f"{c['name']}_*_A.png")))
    for fa in files:
        fb = fa[:-5] + 'B.png'
        a = Image.open(fa).convert('RGB')
        b = Image.open(fb).convert('RGBA')
        w, h = a.size
        ghost = ImageEnhance.Color(a).enhance(0.25)
        ghost = Image.blend(ghost, Image.new('RGB', a.size, (24, 20, 40)), 0.68).convert('RGBA')
        ghost.alpha_composite(b)
        top, bot = 34, 28
        fr = Image.new('RGB', (2 * w + 6, h + top + bot), (34, 28, 52))
        fr.paste(a, (0, top)); fr.paste(ghost.convert('RGB'), (w + 6, top))
        d = ImageDraw.Draw(fr)
        d.text((10, 8), f"{ci + 1}/{nclips}  {c['title']}", font=f_title, fill=(245, 240, 255))
        d.text((10, top + 8), 'модель', font=f_small, fill=(40, 30, 70))
        d.text((w + 16, top + 8), f"скелет: {meta['bones']} {plural(meta['bones'])}", font=f_small, fill=(225, 220, 245))
        lab0 = 'цепочки:'
        d.text((10, h + top + 6), lab0, font=f_small, fill=(200, 195, 225))
        x = 18 + int(d.textlength(lab0, font=f_small))
        for col, lab in LEG:
            d.rectangle([x, h + top + 9, x + 11, h + top + 20], fill=col)
            d.text((x + 16, h + top + 6), lab, font=f_small, fill=(225, 220, 245))
            x += 26 + int(d.textlength(lab, font=f_small))
        frames.append(fr)
for i, fr in enumerate(frames):
    fr.save(os.path.join(OUT, f'f{i:04d}.png'))
if SHEET:
    cols = 4
    fw, fh = frames[0].size
    sc = 0.5
    tw, th = int(fw * sc), int(fh * sc)
    rows = (len(frames) + cols - 1) // cols
    sh = Image.new('RGB', (cols * tw, rows * th), (20, 20, 20))
    for i, fr in enumerate(frames):
        sh.paste(fr.resize((tw, th), Image.LANCZOS), ((i % cols) * tw, (i // cols) * th))
    sh.save(os.path.join(OUT, 'sheet.png'))
print('frames', len(frames))
