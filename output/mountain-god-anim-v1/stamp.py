import glob, os
from PIL import Image, ImageDraw, ImageFont
F = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 17)
SEG = [(0, "1. Сидит, руки-опоры держатся за скалы"), (2.5, "2. Заметил игрока, приподнялся"), (7.0, "3. Тянет руку: теряет ложе, ноги и таз оседают"),
       (16.0, "3. Палец у кромки, ждёт прыжка"), (18.5, "4. Притягивает к лицу"), (30.5, "5. Разговор (колпак и борода на пружинах)")]
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
run(sorted(glob.glob("final/player_*.png")) + sorted(glob.glob("final/rider_*.png")), "st_story")
run(sorted(glob.glob("final/wide_*.png")), "st_wide")
