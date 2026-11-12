"""Outlines of the concept views (02-asset-sheets): silhouette polygons, simplified to big edges, split at concave
corners into convex parts. Writes outlines.json: {sheet: [view: [part: [(x, z) metres...]]]} and a check image.

usage: python3 outlines.py <asset-sheets dir> <out json> <check png>
"""
import json
import sys

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
from skimage import measure

SRC, OUT, CHECK = sys.argv[1:4]

# sheet: (scale by 'w' or 'h', metres, flatten z, tolerance px)
SHEETS = {
    "A01-cliff-wall": ("h", 18.0, 1.0, 7),
    "A03-overhang": ("w", 13.0, 1.0, 7),
    "A04-summit": ("w", 20.0, .6, 6),
    "A05-boulder": ("w", 3.2, .85, 5),
    "A06-fallen-slab": ("w", 3.8, .5, 5),
    "A09-distant-massif": ("w", 600.0, .9, 6),
}


def views(name):
    im = np.array(Image.open(f"{SRC}/{name}.png").convert("RGB")).astype(float)
    bg = np.median(np.concatenate([im[5:25].reshape(-1, 3), im[-25:-5].reshape(-1, 3)]), axis=0)
    m = np.sqrt(((im - bg) ** 2).sum(2)) > 28
    m[:90, :220] = False
    m = ndimage.binary_opening(m, iterations=2)
    m = ndimage.binary_fill_holes(m)
    lab, k = ndimage.label(m)
    sizes = ndimage.sum(m, lab, range(1, k + 1))
    big = list(np.argsort(-sizes)[:3] + 1)
    out = []
    for b in big:
        mk = lab == b
        c = max(measure.find_contours(mk.astype(float), .5), key=len)
        poly = measure.approximate_polygon(c, tolerance=SHEETS[name][3])
        out.append((poly[:, 1], poly[:, 0], mk))  # x, y(px, down)
    out.sort(key=lambda t: t[0].min())
    return im, out


def signed_area(p):
    x, y = p[:, 0], p[:, 1]
    return .5 * np.sum(x * np.roll(y, -1) - np.roll(x, -1) * y)


def clip(poly, c, keep_left):
    """Sutherland–Hodgman against the vertical line x = c."""
    out = []
    n = len(poly)
    for i in range(n):
        a, b = poly[i], poly[(i + 1) % n]
        ina = a[0] <= c if keep_left else a[0] >= c
        inb = b[0] <= c if keep_left else b[0] >= c
        if ina:
            out.append(a)
        if ina != inb:
            t = (c - a[0]) / (b[0] - a[0])
            out.append((c, a[1] + t * (b[1] - a[1])))
    return out


def reflex(poly):
    p = np.array(poly)
    s = np.sign(signed_area(p))
    res = []
    n = len(p)
    for i in range(n):
        a, b, c = p[i - 1], p[i], p[(i + 1) % n]
        cross = (b[0] - a[0]) * (c[1] - b[1]) - (b[1] - a[1]) * (c[0] - b[0])
        if np.sign(cross) == -s and abs(cross) > 1e-6:
            res.append(b)
    return res


def convex_parts(poly, depth=0):
    """Split at reflex corners with vertical cuts until every part is convex (staircase and wedge silhouettes)."""
    r = reflex(poly)
    if not r or depth > 4:
        return [poly]
    xs = [p[0] for p in poly]
    # cut at the reflex vertex nearest the middle of the span
    mid = (min(xs) + max(xs)) / 2
    v = min(r, key=lambda p: abs(p[0] - mid))
    left, right = clip(poly, v[0], True), clip(poly, v[0], False)
    parts = []
    for part in (left, right):
        if len(part) >= 3 and abs(signed_area(np.array(part))) > 4:
            parts += convex_parts(part, depth + 1)
    return parts


result = {}
check = []
for name, (by, metres, flat, _) in SHEETS.items():
    im, vs = views(name)
    sheet = []
    canvas = Image.fromarray(im.astype(np.uint8)).copy()
    dr = ImageDraw.Draw(canvas)
    for xs, ys, mk in vs:
        poly = list(zip(xs, ys))
        x0, x1, y1 = xs.min(), xs.max(), ys.max()
        y0 = ys.min()
        k = metres / ((x1 - x0) if by == "w" else (y1 - y0))
        parts = convex_parts(poly)
        view = []
        for part in parts:
            dr.line([(float(a), float(b)) for a, b in part] + [(float(part[0][0]), float(part[0][1]))], fill=(255, 40, 40), width=4)
            view.append([[round((a - (x0 + x1) / 2) * k, 3), round((y1 - b) * k * flat, 3)] for a, b in part])
        sheet.append(view)
    result[name] = sheet
    canvas.thumbnail((1100, 400))
    check.append(canvas)
json.dump(result, open(OUT, "w"), indent=1)
H = sum(c.height for c in check)
sheet = Image.new("RGB", (1100, H), (30, 30, 30))
y = 0
for c in check:
    sheet.paste(c, (0, y))
    y += c.height
sheet.save(CHECK)
for n, s in result.items():
    print(n, [len(v) for v in s], [[len(p) for p in v] for v in s])
