"""Pixel sprites for the PC's «Склад» app, drawn 1:1 in the style of make_sprites.py (main copy, output/pc-computer).
Run: python3 make_storage_sprites.py <out dir>   (out dir: hukaton/Assets/Computer/Art/Ui)
- xp_icon_storage.png 32x32: a crooked stack of wooden crates (desktop, start menu, title bar, taskbar).
- xp_rarity_gem.png 9x9: a white gem with a dark outline, tinted by the item's rarity colour in the list."""
import sys
from PIL import Image, ImageDraw

OUT = sys.argv[1]
def hx(c, a=255):
    c = c.lstrip('#'); return (int(c[0:2], 16), int(c[2:4], 16), int(c[4:6], 16), a)
OUTLINE = hx('#3A2618')

def crate(dr, x0, y0, x1, y1, light, mid, dark):
    """A box with plank lines, a darker side band and a highlight on top; every edge 1 px, no antialias."""
    dr.rectangle([x0, y0, x1, y1], fill=hx(mid), outline=OUTLINE)
    dr.line([(x0 + 1, y0 + 1), (x1 - 1, y0 + 1)], fill=hx(light))          # lit top edge
    dr.line([(x0 + 1, y1 - 1), (x1 - 1, y1 - 1)], fill=hx(dark))           # shaded bottom
    h = y1 - y0
    for k in (1, 2):                                                         # plank seams
        y = y0 + round(h * k / 3)
        dr.line([(x0 + 1, y), (x1 - 1, y)], fill=hx(dark))
    # the cross brace
    dr.line([(x0 + 2, y1 - 2), (x1 - 2, y0 + 2)], fill=hx(light))
    dr.line([(x0 + 3, y1 - 2), (x1 - 2, y0 + 3)], fill=hx(dark))
    # corner nails
    for nx, ny in ((x0 + 2, y0 + 2), (x1 - 2, y0 + 2), (x0 + 2, y1 - 2), (x1 - 2, y1 - 2)):
        dr.point((nx, ny), fill=hx('#2A1C12'))

def icon_storage():
    img = Image.new('RGBA', (32, 32), (0, 0, 0, 0)); dr = ImageDraw.Draw(img)
    dr.rectangle([2, 29, 30, 30], fill=hx('#000000', 70))                    # soft floor shadow (2 rows)
    crate(dr, 1, 16, 15, 29, '#E8B46A', '#C0803A', '#7E4E1E')               # bottom left
    crate(dr, 16, 17, 30, 29, '#D9A55C', '#A86C2E', '#6E4218')              # bottom right, a pixel lower: crooked
    # top crate, shifted and tilted one pixel per side
    top = Image.new('RGBA', (17, 15), (0, 0, 0, 0)); tdr = ImageDraw.Draw(top)
    crate(tdr, 0, 0, 15, 13, '#F0C27A', '#CC8E44', '#86561F')
    img.alpha_composite(top, (8, 3))
    # a green XP tag on the top crate: "it's the shop's stock"
    dr.rectangle([19, 6, 24, 10], fill=hx('#2DB52D'), outline=hx('#14501A'))
    dr.point((20, 7), fill=hx('#9BE89B'))
    return img

def gem():
    rows = ["...XXX...", "..XWWWX..", ".XWWWWWX.", "XWWWWWWWX", "XWWWWWWWX", ".XWWWWWX.", "..XWWWX..", "...XWX...", "....X...."]
    img = Image.new('RGBA', (9, 9), (0, 0, 0, 0)); d = img.load()
    for y, r in enumerate(rows):
        for x, ch in enumerate(r):
            if ch == 'X': d[x, y] = hx('#2A2A2A')
            elif ch == 'W': d[x, y] = hx('#FFFFFF')
    d[3, 2] = hx('#FFFFFF'); d[2, 3] = hx('#FFFFFF')
    return img

icon_storage().save(f"{OUT}/xp_icon_storage.png")
gem().save(f"{OUT}/xp_rarity_gem.png")
