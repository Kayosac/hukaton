"""Ivy atlas for the god mountains: 4 × 4 cells, pixel art in the foliage palette, 128 px drawn, ×2 nearest -> 256.
Cells (col, row from the top): rows 0–2 single ivy leaves (stem at the cell's bottom centre, tip up);
row 3: cols 0–1 stem strips, cols 2–3 clusters of three leaves. usage: python3 ivy_texture.py out.png"""
import math
import sys
import random
from PIL import Image

S, C = 128, 32
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
px = img.load()
r = random.Random(20261112)


def hexc(h):
    return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5)) + (255,)


PALETTES = [  # front, light, dark, rib, outline
    ("#7A8065", "#8E9470", "#62694F", "#A3A884", "#4C523E"),
    ("#6F7858", "#848C66", "#58604A", "#9CA27A", "#454B37"),
    ("#858558", "#9A9A68", "#6C6C48", "#B0AE80", "#50503A"),
    ("#646C50", "#78805E", "#525A42", "#8E9670", "#3E4434"),
    ("#7D6A7A", "#92808E", "#655666", "#A8979F", "#4E4250"),  # the strange one: mauve like the stone
]


def leaf(ox, oy, size, pal, tilt=0.0, lobes=5):
    """Ivy leaf with its stem point at (ox, oy) (pixels), tip toward -y rotated by tilt (radians)."""
    front, light, dark, rib, outline = (hexc(c) for c in pal)
    R = size
    cx, cy = ox + math.sin(tilt) * R * .55, oy - math.cos(tilt) * R * .55
    tips = [0.0, 1.15, -1.15, 2.25, -2.25][:lobes]
    lens = [1.0, .8, .8, .55, .55][:lobes]

    def rad(phi):
        best = .42
        for a, l in zip(tips, lens):
            d = (phi - a + math.pi) % (2 * math.pi) - math.pi
            best = max(best, l * max(0.0, math.cos(d * 1.6)) ** 1.6)
        return best * R

    for y in range(int(cy - R - 2), int(cy + R + 3)):
        for x in range(int(cx - R - 2), int(cx + R + 3)):
            if not (0 <= x < S and 0 <= y < S):
                continue
            dx, dy = x + .5 - cx, y + .5 - cy
            # angle from "up" (leaf axis), rotated by tilt
            phi = math.atan2(dx, -dy) - tilt
            d = math.hypot(dx, dy)
            rr = rad(phi)
            if d <= rr:
                col = light if (dx * math.cos(tilt) + dy * math.sin(tilt)) < 0 else front
                if d > rr - 1.15:
                    col = outline
                px[x, y] = col
    # veins from the centre toward each lobe tip
    for a, l in zip(tips, lens):
        for k in range(int(R * l * .85)):
            x = int(cx + math.sin(a + tilt) * k)
            y = int(cy - math.cos(a + tilt) * k)
            if 0 <= x < S and 0 <= y < S and px[x, y][3] and px[x, y] != hexc(pal[4]):
                px[x, y] = rib
    # leaf stalk down to the stem point
    for k in range(int(R * .55) + 1):
        x = int(round(ox + math.sin(tilt) * k))
        y = int(round(oy - math.cos(tilt) * k))
        if 0 <= x < S and 0 <= y < S and not px[x, y][3]:
            px[x, y] = dark


for i in range(12):
    col, row = i % 4, i // 4
    pal = PALETTES[4] if i in (5, 10) else PALETTES[r.randrange(4)]
    size = r.uniform(10.5, 13.5)
    leaf(col * C + C // 2, row * C + C - 2, size, pal, tilt=r.uniform(-.25, .25), lobes=r.choice((3, 5, 5)))

# stems: wiggly vertical strips
for col in (0, 1):
    x0 = col * C + C // 2
    for y in range(3 * C, 4 * C):
        x = x0 + int(round(math.sin((y + col * 9) * .35) * 1.5))
        for dx, c in ((-1, "#4E4436"), (0, "#6B5E48"), (1, "#5A4E3E")):
            px[x + dx, y] = hexc(c)
# clusters of three leaves
for col in (2, 3):
    pal = PALETTES[r.randrange(4)]
    bx, by = col * C + C // 2, 4 * C - 2
    leaf(bx - 6, by - 4, 8.5, pal, tilt=-.6, lobes=5)
    leaf(bx + 6, by - 5, 8.0, PALETTES[r.randrange(4)], tilt=.55, lobes=3)
    leaf(bx, by, 9.5, pal, tilt=r.uniform(-.15, .15), lobes=5)

img.resize((S * 2, S * 2), Image.NEAREST).save(sys.argv[1])
print("ok")
