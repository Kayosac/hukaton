import glob, os, sys
SRC = sys.argv[1] if len(sys.argv) > 1 else "final"
from PIL import Image, ImageDraw, ImageFont
F = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 17)
SEG = [(0, "1. Сидит глубоко, руки-опоры держатся за скалы"), (2.5, "2. Заметил игрока: встаёт на руках-опорах"),
       (7.6, "3. Отпускает себя: руки вниз, ноги падают, оседает"), (10.2, "3. Тянет руку к кромке"),
       (18.6, "3. Палец у кромки, ждёт прыжка"), (21.1, "4. Притягивает к лицу"), (33.1, "5. Разговор (колпак и борода на пружинах)")]
def label(t):
    return [s for a, s in SEG if a <= t + 1e-6][-1]
def run(names, out):
    os.makedirs(out, exist_ok=True)
    k = 0
    for f in names:
        i = int(os.path.basename(f).split("_")[1][:4])
        t = i * 2 / 30
        im = Image.open(f).convert("RGB")
        d = ImageDraw.Draw(im)
        txt = label(t)
        d.rectangle((0, 0, 640, 30), fill=(20, 18, 26))
        d.text((10, 5), txt, font=F, fill=(240, 236, 220))
        d.text((585, 5), f"{t:4.1f}s", font=F, fill=(160, 156, 150))
        im.save(f"{out}/{k:04d}.png"); k += 1
run(sorted(glob.glob(SRC + "/player_*.png")) + sorted(glob.glob(SRC + "/rider_*.png")), "st_story")
run(sorted(glob.glob(SRC + "/wide_*.png")), "st_wide")
run(sorted(glob.glob(SRC + "/body_*.png")), "st_body")
