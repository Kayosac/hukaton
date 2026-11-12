#!/usr/bin/env python3
"""Builds the god-mountains level figures (SVG) from data/route.json.

Run from anywhere:  python3 tools/build_figures.py
PNG export (optional): rsvg-convert -z 2 figures/X.svg -o figures/X.png
"""
import json
import math
import os
import sys
from xml.sax.saxutils import escape

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = json.load(open(os.path.join(ROOT, "data", "route.json"), encoding="utf-8"))
FIG = os.path.join(ROOT, "figures")
FONT = "DejaVu Sans, Arial, sans-serif"

ZC = {"Z0": "#3b3a63", "Z1": "#5b4a9b", "Z2": "#8a4fb0", "Z3": "#b0457a",
      "Z4": "#d0703a", "Z5": "#b88a12", "Z6": "#3f8a62", "Z7": "#2f7fb5"}
ROCK, ROCK_EDGE, PATH, VOID, SEA, INK = "#6c6178", "#4a4155", "#c9b8a6", "#fbfaf7", "#d6d0e8", "#2d2836"
NODES = DATA["nodes"]
NODE = {n["id"]: n for n in NODES}
MK = DATA["markers"]


class Svg:
    def __init__(self, w, h, bg="#f2eef4"):
        self.w, self.h, self.parts = w, h, []
        self.parts.append(f'<rect width="{w}" height="{h}" fill="{bg}"/>')

    def add(self, s):
        self.parts.append(s)

    def text(self, x, y, s, size=14, fill=INK, anchor="start", weight="normal", style=""):
        self.add(f'<text x="{x:.1f}" y="{y:.1f}" font-size="{size}" fill="{fill}" text-anchor="{anchor}" '
                 f'font-weight="{weight}" font-family="{FONT}" {style}>{escape(s)}</text>')

    def lines(self, x, y, rows, size=14, gap=1.32, **kw):
        for i, r in enumerate(rows):
            self.text(x, y + i * size * gap, r, size=size, **kw)

    def save(self, name):
        defs = ('<defs>'
                '<marker id="arr" markerUnits="userSpaceOnUse" markerWidth="13" markerHeight="13" refX="10" refY="6.5" orient="auto">'
                '<path d="M0 0 L13 6.5 L0 13z" fill="#2d2836"/></marker>'
                '<marker id="arrw" markerUnits="userSpaceOnUse" markerWidth="12" markerHeight="12" refX="9" refY="6" orient="auto">'
                '<path d="M0 0 L12 6 L0 12z" fill="#ffffff"/></marker>'
                '<marker id="arrb" markerUnits="userSpaceOnUse" markerWidth="18" markerHeight="18" refX="14" refY="9" orient="auto">'
                '<path d="M0 0 L18 9 L0 18z" fill="#2f7fb5"/></marker>'
                '<pattern id="cloud" width="18" height="18" patternUnits="userSpaceOnUse">'
                '<rect width="18" height="18" fill="#fbfaf7"/><circle cx="4" cy="5" r="1.1" fill="#ddd7e4"/>'
                '<circle cx="13" cy="13" r="1.1" fill="#ddd7e4"/></pattern>'
                '<pattern id="roof" width="8" height="8" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">'
                '<line x1="0" y1="0" x2="0" y2="8" stroke="#3a3244" stroke-width="2.2" stroke-opacity=".55"/></pattern>'
                '<pattern id="hatch" width="7" height="7" patternUnits="userSpaceOnUse" patternTransform="rotate(-45)">'
                '<line x1="0" y1="0" x2="0" y2="7" stroke="#4a4155" stroke-width="1.2" stroke-opacity=".5"/></pattern>'
                '</defs>')
        svg = (f'<svg xmlns="http://www.w3.org/2000/svg" width="{self.w}" height="{self.h}" '
               f'viewBox="0 0 {self.w} {self.h}">{defs}' + "".join(self.parts) + "</svg>")
        path = os.path.join(FIG, name)
        open(path, "w", encoding="utf-8").write(svg)
        return path


def poly(points, fill, stroke="none", sw=0, extra=""):
    p = " ".join(f"{x:.1f},{y:.1f}" for x, y in points)
    return f'<polygon points="{p}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}" {extra}/>'


def pline(points, stroke, sw, extra=""):
    p = " ".join(f"{x:.1f},{y:.1f}" for x, y in points)
    return f'<polyline points="{p}" fill="none" stroke="{stroke}" stroke-width="{sw}" {extra}/>'


def offset_path(pts, w):
    """Left/right edges of a polyline with per-point half widths."""
    left, right = [], []
    for i, (x, y, hw) in enumerate(pts):
        if i == 0:
            dx, dy = pts[1][0] - x, pts[1][1] - y
        elif i == len(pts) - 1:
            dx, dy = x - pts[i - 1][0], y - pts[i - 1][1]
        else:
            dx, dy = pts[i + 1][0] - pts[i - 1][0], pts[i + 1][1] - pts[i - 1][1]
        L = math.hypot(dx, dy) or 1
        nx, ny = -dy / L, dx / L
        left.append((x + nx * hw, y + ny * hw))
        right.append((x - nx * hw, y - ny * hw))
    return left, right


# ---------------------------------------------------------------- plan
def plan():
    S = 8.0
    X0, X1, Y0, Y1 = -56, 104, -47, 108
    OX, OY = 50, 120
    W = int(OX + (X1 - X0) * S + 500)
    H = int(OY + (Y1 - Y0) * S + 60)
    g = Svg(W, H)

    def P(x, y):
        return (OX + (x - X0) * S, OY + (Y1 - y) * S)

    def PL(pts):
        return [P(x, y) for x, y in pts]

    g.text(OX, 44, "Горы бога · пеший маршрут игрока · план", 30, weight="bold")
    g.text(OX, 76, "v1 · 12.11.2026 · предложение для блокинга · север сверху · координаты в метрах от точки появления", 16, fill="#5a5266")
    g.text(OX, 100, "Бог за северным краем плана: расстояние и масштаб задаются после сборки сцены с маршрутом", 16, fill="#5a5266")

    # background: whole frame = cloud abyss, north band = cloud sea
    g.add(f'<rect x="{OX}" y="{OY}" width="{(X1-X0)*S}" height="{(Y1-Y0)*S}" fill="url(#cloud)" stroke="#bdb5c8"/>')
    g.add(poly(PL([(X0, 80), (X1, 80), (X1, Y1), (X0, Y1)]), SEA))
    # ledge south edge (for the rock boundary)
    ledge_ids = ["N24", "N25", "N26", "N27", "N28", "N29", "N30", "N31"]
    lpts = [(NODE[i]["x"], NODE[i]["y"], NODE[i]["width_m"] / 2) for i in ledge_ids]
    lleft, lright = offset_path(lpts, 0)
    rock = [(-56, -47), (-14, -47), (-15, -22), (-17, 0), (-10, 24), (-4, 40), (0.5, 47.0), (2.0, 47.8)]
    rock += lright
    rock += [(88, 53.2), (104, 53.5), (104, 70), (96, 84), (91, 82.5), (90.6, 96.4), (76.4, 96.4), (76, 82.6),
             (60, 86), (40, 84), (20, 87), (0, 84.5), (-20, 86.5), (-40, 83.5), (-56, 80.5)]
    g.add(poly(PL(rock), ROCK, ROCK_EDGE, 2))
    g.text(*P(-52, 74), "СКАЛЬНЫЙ МАССИВ И ХРЕБЕТ-ШИРМА", 15, fill="#efe9f3", weight="bold")
    g.text(*P(-52, 70.6), "гребень на 25–45 м выше тропы; закрывает север", 13, fill="#e3dcea")
    g.text(*P(30, 72), "ХРЕБЕТ-ШИРМА", 15, fill="#efe9f3", weight="bold")
    g.text(*P(-50, 101), "ОБЛАЧНОЕ МОРЕ — далеко внизу; из него торчат вершины, в нём стоит бог", 15, fill="#4e4766", weight="bold")
    g.text(*P(-6, 26), "БЕЛАЯ БЕЗДНА", 17, fill="#9a90aa", weight="bold")
    g.text(*P(-6, 22.2), "облако прижато к южному склону;", 13, fill="#9a90aa")
    g.text(*P(-6, 19.2), "видно 6–20 м, дальше белое", 13, fill="#9a90aa")

    # path channels
    def channel(ids, roof=False):
        pts = [(NODE[i]["x"], NODE[i]["y"], NODE[i]["width_m"] / 2) for i in ids]
        L, R = offset_path(pts, 0)
        g.add(poly(PL(L + R[::-1]), PATH, "#7b6c5f", 1))
        if roof:
            g.add(poly(PL(L + R[::-1]), "url(#roof)"))

    pocket = [(-38.6, -32.6), (-31.2, -33.2), (-30.4, -25.2), (-34.2, -23.4), (-35.9, -23.4), (-39.2, -25.8)]
    g.add(poly(PL(pocket), PATH, "#7b6c5f", 1))
    g.add(poly(PL(pocket), "url(#roof)"))
    channel(["N01", "N02", "N03", "N04", "N05", "N06", "N07", "N08", "N09"], roof=True)
    channel(["N09", "N10", "N11", "N12", "N13", "N14", "N15", "N16", "N17", "N18"])
    channel(["N18", "N19", "N20", "N21", "N22"])
    cx, cy = P(-3.8, 49.5)
    g.add(f'<circle cx="{cx:.1f}" cy="{cy:.1f}" r="{3*S:.1f}" fill="{PATH}" stroke="#7b6c5f"/>')
    g.add(poly(PL([(-0.6, 48.8), (3.2, 48.8), (3.2, 50.2), (-0.6, 50.2)]), PATH, "#7b6c5f", 1))
    channel(["N24", "N25", "N26", "N27", "N28", "N29", "N30", "N31"])
    channel(["N31", "N32", "N33", "N34", "N35", "N36"])
    plat = [(77, 82.6), (90, 82.6), (90.4, 96), (76.6, 96)]
    g.add(poly(PL(plat), PATH, "#7b6c5f", 1))
    # stairs ticks between N19 and N20
    a, b = NODE["N19"], NODE["N20"]
    for k in range(1, 25):
        t = k / 25
        x, y = a["x"] + (b["x"] - a["x"]) * t, a["y"] + (b["y"] - a["y"]) * t
        dx, dy = b["x"] - a["x"], b["y"] - a["y"]
        L = math.hypot(dx, dy)
        nx, ny = -dy / L * .7, dx / L * .7
        g.add(pline(PL([(x - nx, y - ny), (x + nx, y + ny)]), "#6b5c50", 1))
    # frame S5
    fx, fy = P(0, 49.5)
    g.add(f'<rect x="{fx-3:.1f}" y="{fy-0.9*S-4:.1f}" width="6" height="{1.8*S+8:.1f}" fill="#2d2836"/>')
    # lip boulders
    for bx, by, r in [(78, 95.2, 1.3), (79.6, 96.2, 1.0), (88.9, 95.3, 1.3), (87.3, 96.3, 1.0),
                      (76.9, 89, 1.2), (90.2, 88, 1.2), (77.2, 84.4, 1.0), (89.8, 84.2, 1.0)]:
        bx_, by_ = P(bx, by)
        g.add(f'<circle cx="{bx_:.1f}" cy="{by_:.1f}" r="{r*S:.1f}" fill="#857a8f" stroke="#4a4155"/>')
    # ledge rim boulders (outer, south side)
    for i in range(len(lright) - 1):
        (x1, y1), (x2, y2) = lright[i], lright[i + 1]
        for t in (.25, .75):
            bx, by = x1 + (x2 - x1) * t, y1 + (y2 - y1) * t - .5
            px, py = P(bx, by)
            g.add(f'<circle cx="{px:.1f}" cy="{py:.1f}" r="{0.55*S:.1f}" fill="#857a8f" stroke="#4a4155" stroke-width=".8"/>')

    # route centerline, coloured by zone
    for a, b in zip(NODES, NODES[1:]):
        (x1, y1), (x2, y2) = P(a["x"], a["y"]), P(b["x"], b["y"])
        g.add(f'<line x1="{x1:.1f}" y1="{y1:.1f}" x2="{x2:.1f}" y2="{y2:.1f}" stroke="{ZC[b["zone"]]}" '
              f'stroke-width="3.2" stroke-linecap="round"/>')
    for a, b in zip(NODES, NODES[1:]):
        (x1, y1), (x2, y2) = P(a["x"], a["y"]), P(b["x"], b["y"])
        mx, my = (x1 + x2) / 2, (y1 + y2) / 2
        dx, dy = x2 - x1, y2 - y1
        L = math.hypot(dx, dy) or 1
        if L > 40:
            g.add(f'<line x1="{mx - dx/L*6:.1f}" y1="{my - dy/L*6:.1f}" x2="{mx + dx/L*4:.1f}" y2="{my + dy/L*4:.1f}" '
                  f'stroke="{ZC[b["zone"]]}" stroke-width="3" marker-end="url(#arr)"/>')
    for n in NODES:
        x, y = P(n["x"], n["y"])
        g.add(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="2.6" fill="#fff" stroke="{ZC[n["zone"]]}" stroke-width="1.6"/>')

    # node labels (selected)
    lab = {"N00": (12, 18), "N09": (-46, 4), "N12": (8, 4), "N15": (8, 4), "N18": (-42, -6), "N19": (6, -8),
           "N20": (8, 10), "N21": (8, 10), "N27": (-12, -12), "N31": (8, 20),
           "N33": (-44, 4), "N36": (12, 4)}
    for k, (dx, dy) in lab.items():
        x, y = P(NODE[k]["x"], NODE[k]["y"])
        g.text(x + dx, y + dy, k, 11, fill="#3a3346")

    # spawn arrow
    sx, sy = P(-34.5, -29.5)
    g.add(f'<circle cx="{sx:.1f}" cy="{sy:.1f}" r="7" fill="{ZC["Z0"]}" stroke="#fff" stroke-width="2"/>')
    g.add(f'<line x1="{sx:.1f}" y1="{sy+6:.1f}" x2="{sx:.1f}" y2="{sy+20:.1f}" stroke="#fff" stroke-width="3" marker-end="url(#arrw)"/>')

    # zone badges
    badges = {"Z0": (-50, -36), "Z1": (-27, -2), "Z2": (-15, 44), "Z3": (-6, 66), "Z4": (-8, 37),
              "Z5": (8, 40), "Z6": (63, 68), "Z7": (56, 92)}
    anchors = {"Z0": (-36, -29), "Z1": (-30.5, 0), "Z2": (-24, 45), "Z3": (-12, 58), "Z4": (-3.8, 46.8),
               "Z5": (14, 47.5), "Z6": (80, 66), "Z7": (77, 91)}
    for z, (bx, by) in badges.items():
        x, y = P(bx, by)
        ax, ay = P(*anchors[z])
        g.add(f'<line x1="{x:.1f}" y1="{y:.1f}" x2="{ax:.1f}" y2="{ay:.1f}" stroke="{ZC[z]}" stroke-width="1.5"/>')
        name = DATA["zones"][z]["name"]
        wbox = 34 + len(name) * 9.4
        g.add(f'<rect x="{x-2:.1f}" y="{y-16:.1f}" width="{wbox:.1f}" height="22" rx="4" fill="{ZC[z]}"/>')
        g.text(x + 4, y, f"{z} {name}", 13, fill="#fff", weight="bold")

    # beats
    for bt in DATA["beats"]:
        x, y = P(*bt["pos"])
        g.add(f'<polygon points="{x:.1f},{y-11:.1f} {x+11:.1f},{y:.1f} {x:.1f},{y+11:.1f} {x-11:.1f},{y:.1f}" '
              f'fill="#ffd34d" stroke="#2d2836" stroke-width="1.5"/>')
        g.text(x, y + 4.5, bt["id"], 10, anchor="middle", weight="bold")

    # checkpoints
    for cp in MK["checkpoints"]:
        x, y = P(cp["pos"][0], cp["pos"][1])
        g.add(f'<rect x="{x-7:.1f}" y="{y-7:.1f}" width="14" height="14" fill="#2e9e57" stroke="#fff" stroke-width="1.5"/>')
        g.text(x + 11, y + 18, cp["id"], 11, fill="#1f6e3c", weight="bold")

    # mouth, edge trigger, lip, dock
    mx_, my_ = P(83.5, 82)
    g.add(f'<circle cx="{mx_:.1f}" cy="{my_:.1f}" r="7" fill="#2f7fb5" stroke="#fff" stroke-width="2"/>')
    g.text(mx_ - 14, my_ + 4, "M", 12, anchor="end", weight="bold", fill="#1d5a85")
    e1, e2 = P(77, 93.5), P(90, 93.5)
    g.add(f'<line x1="{e1[0]:.1f}" y1="{e1[1]:.1f}" x2="{e2[0]:.1f}" y2="{e2[1]:.1f}" stroke="#d1343b" stroke-width="3" stroke-dasharray="7 5"/>')
    g.text(e2[0] + 6, e2[1] + 4, "E", 13, fill="#d1343b", weight="bold")
    l1, l2 = P(80.5, 96), P(86.5, 96)
    g.add(f'<line x1="{l1[0]:.1f}" y1="{l1[1]:.1f}" x2="{l2[0]:.1f}" y2="{l2[1]:.1f}" stroke="#2d2836" stroke-width="5"/>')
    dx_, dy_ = P(83.5, 96.5)
    g.add(f'<circle cx="{dx_:.1f}" cy="{dy_-14:.1f}" r="8" fill="#ffffff" stroke="#2f7fb5" stroke-width="3"/>')
    g.text(dx_, dy_ - 10, "D", 11, anchor="middle", weight="bold", fill="#1d5a85")

    # view wedge from the mouth (horizontal FOV ~107 at 16:9, vertical 75)
    half = math.radians(53.7)
    Lw = 12
    a_ = (83.5 + Lw * math.sin(-half), 82 + Lw * math.cos(half))
    b_ = (83.5 + Lw * math.sin(half), 82 + Lw * math.cos(half))
    g.add(poly(PL([(83.5, 82), a_, b_]), "#2f7fb5", extra='fill-opacity=".12"'))
    gx, gy = P(83.5, 98.5)
    g.add(f'<line x1="{gx:.1f}" y1="{gy:.1f}" x2="{gx:.1f}" y2="{OY+6:.1f}" stroke="#2f7fb5" stroke-width="4" marker-end="url(#arrb)"/>')
    g.text(gx - 10, OY + 30, "к богу MG01 →", 15, anchor="end", fill="#1d5a85", weight="bold")
    g.text(gx - 10, OY + 48, "в кадре с устья M", 13, anchor="end", fill="#1d5a85")

    # scale bar and north
    bx, by = OX + 20, OY + (Y1 - Y0) * S - 24
    g.add(f'<rect x="{bx}" y="{by}" width="{10*S}" height="8" fill="#2d2836"/>'
          f'<rect x="{bx+10*S}" y="{by}" width="{10*S}" height="8" fill="#fff" stroke="#2d2836"/>')
    g.text(bx, by - 6, "0", 12)
    g.text(bx + 10 * S, by - 6, "10", 12, anchor="middle")
    g.text(bx + 20 * S, by - 6, "20 м", 12, anchor="middle")
    nx, ny = OX + (X1 - X0) * S - 40, OY + 50
    g.add(f'<line x1="{nx}" y1="{ny+30}" x2="{nx}" y2="{ny-12}" stroke="#2d2836" stroke-width="3" marker-end="url(#arr)"/>')
    g.text(nx, ny + 48, "С", 16, anchor="middle", weight="bold")

    # inset: corner module x3
    ix, iy, iw, ih = P(37.5, 36)[0], P(37.5, 36)[1], 520, 380
    g.add(f'<rect x="{ix:.1f}" y="{iy:.1f}" width="{iw}" height="{ih}" fill="#ffffff" stroke="#8a4fb0" stroke-width="2.5" rx="6"/>')
    g.text(ix + 16, iy + 28, "Модуль «Угол» (Z2) · ставится 3 раза подряд", 15, weight="bold", fill="#5e2f80")
    s2 = 16.0
    ox2, oy2 = ix + 26, iy + ih - 40

    def Q(x, y):
        return (ox2 + (x + 2.5) * s2, oy2 - y * s2)

    mod = [(0, 0, .8), (0, 6, .8), (4, 8.5, .8), (5, 14, .8)]
    L, R = offset_path(mod, 0)
    wall_l = [(x - .2, y) for x, y in L]
    g.add(poly([Q(*p) for p in L + R[::-1]], PATH, "#7b6c5f", 1))
    g.add(pline([Q(*p) for p in L], ROCK_EDGE, 6))
    g.add(pline([Q(*p) for p in R], ROCK_EDGE, 6))
    g.add(pline([Q(x, y) for x, y, _ in mod], ZC["Z2"], 3, 'marker-end="url(#arr)"'))
    items = [((-1.35, 7.1), "К", "ниша, ветреный куст A07, свет сверху"),
             ((1.15, 3.0), "П", "плита A06 лежит у стены"),
             ((-0.2, 9.2), "Т", "трещина «Г» (D02) на стене напротив входа"),
             ((5.9, 10.6), "В", "валун A05 во внешнем углу")]
    for (x, y), k, _ in items:
        qx, qy = Q(x, y)
        g.add(f'<circle cx="{qx:.1f}" cy="{qy:.1f}" r="10" fill="#fff" stroke="#5e2f80" stroke-width="2"/>')
        g.text(qx, qy + 4.5, k, 12, anchor="middle", weight="bold", fill="#5e2f80")
    tx = ix + 178
    rows = ["Вход с юга, 6 м прямо, поворот направо,",
            "выход на север. Длина 16,3 м, подъём 1 м.",
            "", "Ориентиры, одинаковые в углах 1 и 2:"]
    rows += [f"{k} — {t}" for _, k, t in items]
    rows += ["", "Угол 3 отличается только двумя вещами:",
             "К — ниша пустая: куста нет, лунка и листья;",
             "П — та же плита висит неподвижно", "      на высоте 2,2 м над тропой.",
             "Всё остальное — копия того же префаба."]
    g.lines(tx, iy + 62, rows, 12.2, fill="#3a3346")

    # right panel legend
    px0 = OX + (X1 - X0) * S + 30
    g.text(px0, OY + 10, "Зоны", 20, weight="bold")
    y = OY + 40
    for z, info in DATA["zones"].items():
        zn = [n for n in NODES if n["zone"] == z]
        hs = [n["h"] for n in zn]
        g.add(f'<rect x="{px0}" y="{y-14}" width="34" height="20" rx="3" fill="{ZC[z]}"/>')
        g.text(px0 + 17, y + 1, z, 12, fill="#fff", anchor="middle", weight="bold")
        g.text(px0 + 44, y, f'{info["name"]} · {info["length_m"]:.0f} м', 14, weight="bold")
        g.text(px0 + 44, y + 18, info["role"], 12, fill="#4a4356")
        hr = (f"пол {min(hs):g} м" if min(hs) == max(hs) else f"пол {min(hs):g}…{max(hs):g} м") if hs else ""
        g.text(px0 + 44, y + 34, hr, 12, fill="#6a6276")
        y += 58
    g.text(px0, y + 4, f'Всего ≈ {DATA["total_length_m"]:.0f} м · ≈ 1 мин 05 с чистой ходьбы', 13, weight="bold")
    g.text(px0, y + 22, "4 м/с; с осмотром ожидается 2–3 мин", 12, fill="#4a4356")
    y += 56
    g.text(px0, y, "Обозначения", 20, weight="bold")
    y += 26
    sym = [("rect", PATH, "тропа (ширина в масштабе)"), ("roof", None, "под крышей из плит: неба нет"),
           ("bead", "#ffd34d", "S1–S7 — странные моменты"), ("cp", "#2e9e57", "CP — возврат после падения"),
           ("e", "#d1343b", "E — край: отнять управление"), ("m", "#2f7fb5", "M — устье: открытие вида"),
           ("d", "#2f7fb5", "D — сюда причаливает ладонь"), ("rim", "#857a8f", "камни-борта выше 1,5 м"),
           ("frame", "#2d2836", "S5 — каменная рамка")]
    for kind, col, label in sym:
        if kind == "rect":
            g.add(f'<rect x="{px0}" y="{y-12}" width="34" height="14" fill="{col}" stroke="#7b6c5f"/>')
        elif kind == "roof":
            g.add(f'<rect x="{px0}" y="{y-12}" width="34" height="14" fill="{PATH}" stroke="#7b6c5f"/>'
                  f'<rect x="{px0}" y="{y-12}" width="34" height="14" fill="url(#roof)"/>')
        elif kind == "bead":
            g.add(f'<polygon points="{px0+17},{y-15} {px0+28},{y-5} {px0+17},{y+5} {px0+6},{y-5}" fill="{col}" stroke="#2d2836"/>')
        elif kind == "cp":
            g.add(f'<rect x="{px0+10}" y="{y-12}" width="14" height="14" fill="{col}"/>')
        elif kind == "e":
            g.add(f'<line x1="{px0}" y1="{y-5}" x2="{px0+34}" y2="{y-5}" stroke="{col}" stroke-width="3" stroke-dasharray="7 5"/>')
        elif kind in ("m", "d"):
            g.add(f'<circle cx="{px0+17}" cy="{y-5}" r="7" fill="{col if kind=="m" else "#fff"}" stroke="#2f7fb5" stroke-width="2"/>')
        elif kind == "rim":
            g.add(f'<circle cx="{px0+17}" cy="{y-5}" r="6" fill="{col}" stroke="#4a4155"/>')
        elif kind == "frame":
            g.add(f'<rect x="{px0+14}" y="{y-14}" width="6" height="18" fill="{col}"/>')
        g.text(px0 + 44, y, label, 13)
        y += 24
    y += 14
    g.text(px0, y, "Правила плана", 20, weight="bold")
    g.lines(px0, y + 26, [
        "Север закрыт всегда: крышей (Z0–Z1),",
        "стенами и изгибами (Z2–Z4, Z6),",
        "хребтом-ширмой (Z5) и облаком.",
        "Бог впервые виден из изгиба N35 —",
        "в проёме устья; целиком — в M.",
        "",
        "Невидимых коллайдеров нет: края держат",
        "видимые камни; падение в бездну —",
        "затемнение и возврат на ближний CP.",
        "",
        "Высоты, ширины и длины — для блокинга;",
        "уточняются ходьбой контроллера.",
    ], 13, fill="#3a3346")
    return g.save("route-plan.svg")


# ---------------------------------------------------------------- profile
def profile():
    sx = 5.0
    Hmin, Hmax, sy = -2, 24, 16.0
    OX, OY = 120, 175
    total = DATA["total_length_m"]
    W = int(OX + total * sx + 80)
    prof_h = (Hmax - Hmin) * sy
    bands = [("atm", "Воздух", 54), ("vis", "Видно вперёд", 46), ("light", "Свет", 30), ("breath", "Дыхание горы", 80),
             ("sound", "Звук", 46), ("ctrl", "Управление", 46)]
    H = int(OY + prof_h + 40 + sum(b[2] + 10 for b in bands) + 70)
    g = Svg(W, H)
    g.text(OX, 50, "Горы бога · профиль маршрута: высота, воздух, свет, звук", 30, weight="bold")
    g.text(OX, 80, "По горизонтали — пройденные метры вдоль тропы (развёртка), по вертикали — высота пола от точки появления. "
                   "Время — при ходьбе 4 м/с без остановок.", 15, fill="#5a5266")

    def X(c):
        return OX + c * sx

    def Y(h):
        return OY + (Hmax - h) * sy

    ch = {n["id"]: n["chainage_m"] for n in NODES}
    zspan = {}
    for a, b in zip(NODES, NODES[1:]):
        z = b["zone"]
        s0, s1 = zspan.get(z, (a["chainage_m"], b["chainage_m"]))
        zspan[z] = (min(s0, a["chainage_m"]), max(s1, b["chainage_m"]))
    zspan["Z0"] = (0, ch["N01"])
    bottom = Y(Hmin) + 40 + sum(b[2] + 10 for b in bands)
    for z, (s0, s1) in zspan.items():
        g.add(f'<rect x="{X(s0):.1f}" y="{OY-28}" width="{(s1-s0)*sx:.1f}" height="{bottom-OY+28:.1f}" '
              f'fill="{ZC[z]}" fill-opacity=".07"/>')
        g.add(f'<rect x="{X(s0):.1f}" y="{OY-28}" width="{(s1-s0)*sx:.1f}" height="22" fill="{ZC[z]}"/>')
        nm = f'{z} {DATA["zones"][z]["name"]}'
        if len(nm) * 7.6 > (s1 - s0) * sx - 6:
            nm = z
        g.text(X((s0 + s1) / 2), OY - 12, nm, 12, fill="#fff", anchor="middle", weight="bold")
        g.add(f'<line x1="{X(s0):.1f}" y1="{OY-6}" x2="{X(s0):.1f}" y2="{bottom:.1f}" stroke="{ZC[z]}" stroke-opacity=".5"/>')

    # grid
    for h in range(0, 25, 4):
        g.add(f'<line x1="{OX}" y1="{Y(h):.1f}" x2="{X(total):.1f}" y2="{Y(h):.1f}" stroke="#d8d1de"/>')
        g.text(OX - 10, Y(h) + 4, f"{h} м", 12, anchor="end", fill="#6a6276")
    # cloud
    s_frame = ch["N23"] + 3.8
    g.add(poly([(X(s_frame), Y(Hmax)), (X(ch["N33"]), Y(Hmax)), (X(ch["N33"]), Y(Hmin)), (X(s_frame), Y(Hmin))],
               "#ffffff", extra='fill-opacity=".55"'))
    g.text(X(150), Y(21.5), "ОБЛАКО на южном склоне", 14, fill="#8f86a0", weight="bold")
    # ground fill
    pts = [(X(n["chainage_m"]), Y(n["h"])) for n in NODES]
    g.add(poly(pts + [(X(total), Y(Hmin)), (X(0), Y(Hmin))], ROCK, extra='fill-opacity=".85"'))
    g.add(pline(pts, "#2d2836", 3))
    # roof over Z0-Z1 and choke ceiling
    g.add(poly([(X(0), Y(4.5)), (X(ch["N09"]), Y(3.2 + 4.5)), (X(ch["N09"]), Y(3.2 + 6)), (X(0), Y(6))], "url(#roof)"))
    g.text(X(4), Y(7.4), "крыша из заклиненных плит 4,5–6 м над полом", 12, fill="#3a3244")
    g.add(poly([(X(ch["N20"]), Y(1.2 + 2.1)), (X(ch["N21"]), Y(1.2 + 2.1)), (X(ch["N21"]), Y(1.2 + 3.3)),
                (X(ch["N20"]), Y(1.2 + 3.3))], "url(#roof)"))
    g.text((X(ch["N20"]) + X(ch["N21"])) / 2, Y(-0.6), "горловина: потолок 2,1 м", 11, fill="#ffffff", weight="bold", anchor="middle")

    for n in NODES:
        x, y = X(n["chainage_m"]), Y(n["h"])
        g.add(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="3" fill="#fff" stroke="#2d2836"/>')
    for k in ["N09", "N18", "N20", "N24", "N27", "N31", "N33", "N36"]:
        n = NODE[k]
        g.text(X(n["chainage_m"]), Y(n["h"]) + 18, k, 10, anchor="middle", fill="#f2edf6")
    # annotations on profile
    ann = [("N19", "лестница вниз −5 м", -26), ("N22", "пандус +1,8 м", -40), ("N31", "поворот в хребет", -22),
           ("N36", "устье M", -44), ("N38", "край E", -24)]
    for k, t, dy in ann:
        n = NODE[k]
        x, y = X(n["chainage_m"]), Y(n["h"])
        g.add(f'<line x1="{x:.1f}" y1="{y-5:.1f}" x2="{x:.1f}" y2="{y+dy+4:.1f}" stroke="#2d2836" stroke-width="1"/>')
        g.text(x, y + dy, t, 12, anchor="middle", weight="bold")
    # beats on profile
    bch = {"S1": 1, "S3": ch["N16"], "S4": (ch["N19"] + ch["N21"]) / 2, "S5": s_frame,
           "S6": (ch["N27"] + ch["N29"]) / 2, "S7": ch["N34"]}
    for k, c in bch.items():
        x = X(c)
        y = OY - 44
        g.add(f'<polygon points="{x:.1f},{y-11:.1f} {x+11:.1f},{y:.1f} {x:.1f},{y+11:.1f} {x-11:.1f},{y:.1f}" '
              f'fill="#ffd34d" stroke="#2d2836" stroke-width="1.5"/>')
        g.text(x, y + 4.5, k, 10, anchor="middle", weight="bold")
    g.add(f'<line x1="{X(0):.1f}" y1="{Y(Hmax)+12:.1f}" x2="{X(total):.1f}" y2="{Y(Hmax)+12:.1f}" stroke="#b39a2a" '
          f'stroke-width="2" stroke-dasharray="3 4"/>')
    g.text(X(60), Y(Hmax) + 8, "S2 — дыхание горы слышно на всём пути", 12, fill="#7a6514", weight="bold")
    for cp in MK["checkpoints"]:
        c = {"CP1": ch["N24"], "CP2": ch["N27"], "CP3": ch["N36"] + 3}[cp["id"]]
        h = {"CP1": 3.0, "CP2": 9.0, "CP3": 20.1}[cp["id"]]
        g.add(f'<rect x="{X(c)-6:.1f}" y="{Y(h)+26:.1f}" width="12" height="12" fill="#2e9e57" stroke="#fff"/>')
        g.text(X(c), Y(h) + 52, cp["id"], 11, fill="#ffffff", weight="bold", anchor="middle")

    # time axis
    ty = Y(Hmin) + 22
    for sec in range(0, int(total / 4) + 1, 10):
        x = X(sec * 4)
        g.add(f'<line x1="{x:.1f}" y1="{Y(Hmin):.1f}" x2="{x:.1f}" y2="{Y(Hmin)+6:.1f}" stroke="#2d2836"/>')
        g.text(x, ty, f"{sec//60}:{sec%60:02d}", 11, anchor="middle", fill="#4a4356")
    for m in range(50, int(total) + 1, 50):
        g.text(X(m), Y(Hmin) - 6, f"{m} м", 11, anchor="middle", fill="#f2edf6")
    g.text(OX - 10, ty, "время", 11, anchor="end", fill="#4a4356")

    # bands
    y = Y(Hmin) + 40
    segs = {
        "atm": [(0, ch["N22"], "#3f3a4c", "Нутро: тёмный холодный туман", "#fff"),
                (ch["N22"], s_frame, "#7d7590", "порог", "#fff"),
                (s_frame, ch["N33"], "#f7f5fa", "Облако: белое, всё тоньше к верху", "#4a4356"),
                (ch["N33"], ch["N36"], "#b8aed0", "смена", "#2d2836"),
                (ch["N36"], total, "#a99fc6", "Высота: ясно", "#fff")],
        "vis": [(0, ch["N22"], "#e6e1ec", "стены ближе 6–8 м; туман ~15 м", "#2d2836"),
                (ch["N22"], s_frame, "#e6e1ec", "", "#2d2836"),
                (s_frame, ch["N31"], "#ffffff", "8 м у рамки → 22 м у конца полки", "#2d2836"),
                (ch["N31"], ch["N36"], "#e6e1ec", "стены горла", "#2d2836"),
                (ch["N36"], total, "#cfe3f2", "километры", "#1d5a85")],
        "light": [(0, ch["N01"], "#3a3548", "", ""), (ch["N01"], ch["N22"], "#55506a", "", ""),
                  (ch["N22"], s_frame, "#6e6884", "", ""), (s_frame, ch["N31"], "#f3f0f7", "", ""),
                  (ch["N31"], ch["N35"], "#5e5872", "", ""), (ch["N35"], ch["N36"], "#e9e1f0", "", ""),
                  (ch["N36"], total, "#efe0c8", "", "")],
        "sound": [(0, s_frame, "#efe8f3", "капли, шорох, эхо шагов", "#2d2836"),
                  (s_frame, ch["N31"], "#ffffff", "глухо после рамки; ветер", "#2d2836"),
                  (ch["N31"], ch["N34"], "#efe8f3", "ветер в щели", "#2d2836"),
                  (ch["N34"], ch["N36"], "#2d2836", "тишина", "#fff"),
                  (ch["N36"], total, "#cfe3f2", "простор", "#1d5a85")],
        "ctrl": [(0, total, "#d9f0df", "ходьба и взгляд; бега нет; рука пустая; HUD скрыт — до линии края E, дальше ведёт сценарий", "#1f6e3c")],
    }
    for key, label, hb in bands:
        g.text(OX - 10, y + hb / 2 + 4, label, 12, anchor="end", weight="bold")
        if key == "breath":
            g.add(f'<rect x="{OX}" y="{y}" width="{total*sx:.1f}" height="{hb}" fill="#fff" stroke="#d8d1de"/>')
            curve = [(0, .1), (50, .2), (93, .3), (95, 0), (100, 0), (102, .32), (110, .4), (121, .5), (s_frame - .5, .5),
                     (s_frame, .2), (140, .55), (213, .75), (ch["N34"] - 1, .95), (ch["N34"], 0),
                     (ch["N36"] - .5, 0), (ch["N36"], 1), (total, 1)]
            g.add(pline([(X(c), y + hb - v * (hb - 8)) for c, v in curve], "#b88a12", 3))
            g.text(X(97), y + hb - 6, "пауза под висящей плитой", 10, anchor="middle", fill="#7a6514")
            g.text(X(ch["N36"]) - 4, y + 14, "вдох совпадает с грудью бога →", 10, anchor="end", fill="#7a6514")
        elif key == "light":
            for s0, s1, col, t, tc in segs[key]:
                g.add(f'<rect x="{X(s0):.1f}" y="{y}" width="{(s1-s0)*sx:.1f}" height="{hb}" fill="{col}" stroke="#d8d1de"/>')
            g.text(X(60), y + 20, "тускло, холодно, редкие лучи сверху", 11, fill="#fff")
            g.text(X(170), y + 20, "яркий рассеянный белый", 11, fill="#4a4356")
            g.text(X(ch["N36"]) + 4, y + 20, "солнце", 11, fill="#4a4356")
        else:
            for s0, s1, col, t, tc in segs[key]:
                g.add(f'<rect x="{X(s0):.1f}" y="{y}" width="{max(0,(s1-s0))*sx:.1f}" height="{hb}" fill="{col}" stroke="#d8d1de"/>')
                if t:
                    g.text(X((s0 + s1) / 2), y + hb / 2 + 4, t, 11, anchor="middle", fill=tc)
        y += hb + 10
    g.text(OX, y + 26, "Смена тумана на «высоту» — только в изгибе N33, откуда не видно наружу. Пробный вариант звука и света; "
                       "числа тумана — в LEVEL_DESIGN.md, глава 7.", 13, fill="#4a4356")
    return g.save("route-profile.svg")


# ---------------------------------------------------------------- finale section
def finale():
    W, H = 1700, 1200
    g = Svg(W, H)
    g.text(50, 50, "Горы бога · финал: край, ладонь, подъём к лицу · разрез с юга на север", 30, weight="bold")
    g.text(50, 82, "Слева — в масштабе (сетка 5 м). Справа — бог условно: положение и масштаб задаются после сборки "
                   "сцены. Номера F0–F8 — порядок шагов.", 15, fill="#5a5266")
    s = 9.0
    OX, OY = 60, 520
    BASE = OY + 30 * s

    def P(yn, h):  # yn = plan north coordinate, h = height
        return (OX + (yn - 46) * s, OY - h * s)

    # sea of clouds band
    g.add(f'<rect x="0" y="{P(0,-30)[1]:.1f}" width="{W}" height="{H-P(0,-30)[1]:.1f}" fill="url(#cloud)"/>')
    g.add(f'<rect x="0" y="{P(0,-30)[1]-30:.1f}" width="{W}" height="30" fill="{SEA}"/>')
    g.text(70, P(0, -30)[1] + 34, "облачное море далеко внизу (глубина обрыва — после сборки сцены)", 14, fill="#6a6280", weight="bold")
    # grid
    for yn in range(50, 101, 5):
        x, _ = P(yn, 0)
        g.add(f'<line x1="{x:.1f}" y1="{P(0,46)[1]:.1f}" x2="{x:.1f}" y2="{P(0,-26)[1]:.1f}" stroke="#e2dbe7"/>')
        g.text(x, P(0, -28)[1], f"y {yn}", 10, anchor="middle", fill="#8a8296")
    # ridge mass: south face at 54, crest ~45, north face at 82, promontory to 96
    ridge = [P(48, -26), P(50, 3), P(53, 16.5), P(54.5, 30), P(60, 45), P(70, 47), P(78, 40), P(82, 26), P(82, 20),
             P(96, 20.5), P(96.2, 14), P(94, 4), P(95.5, -10), P(93, -26)]
    g.add(poly(ridge, ROCK, ROCK_EDGE, 2))
    # gorge cut (dashed path inside ridge, seen as a slot)
    gorge = [P(54, 16.5), P(62, 17.5), P(69, 18.5), P(75, 19.5), P(82, 20)]
    g.add(pline(gorge, "#3f8a62", 5, 'stroke-dasharray="10 6"'))
    g.text(*P(55, 27), "Z6 Горло сквозь хребет:", 13, fill="#e8f4ec", weight="bold")
    g.text(*P(55, 25), "наружу не видно до устья M", 13, fill="#e8f4ec")
    # ledge south side
    g.add(pline([P(46, 3), P(54, 16.5)], "#b88a12", 5))
    g.text(*P(46.5, 8), "Z5 полка", 13, fill="#7a5c06", weight="bold", anchor="start")
    # platform surface
    g.add(pline([P(82, 20), P(96, 20.5)], "#2f7fb5", 6))
    # player eye at mouth and at edge
    for yn, h, lab in [(82, 20, "M"), (93.5, 20.4, "E")]:
        x, y = P(yn, h)
        g.add(f'<line x1="{x:.1f}" y1="{y:.1f}" x2="{x:.1f}" y2="{y-1.8*s:.1f}" stroke="#2d2836" stroke-width="3"/>')
        g.add(f'<circle cx="{x:.1f}" cy="{y-1.6*s:.1f}" r="4" fill="#2d2836"/>')
        g.text(x, y + 22, lab, 15, anchor="middle", weight="bold", fill="#d1343b" if lab == "E" else "#1d5a85")
    ex, ey = P(93.5, 20.4)
    g.add(f'<line x1="{ex:.1f}" y1="{ey-60:.1f}" x2="{ex:.1f}" y2="{ey+8:.1f}" stroke="#d1343b" stroke-width="2.5" stroke-dasharray="6 4"/>')
    lx, ly = P(96, 20.5)
    g.text(lx - 4, ly + 40, "кромка", 13, weight="bold", anchor="end")
    dx, dy = P(96.6, 20.5)
    # palm docked (schematic)
    palm = [P(96.2, 20.5), P(104, 20.2), P(116, 19.5), P(124, 17.5), P(126, 14), P(118, 13.5), P(104, 15), P(96.4, 18.8)]
    g.add(poly(palm, "#6f86c9", "#3c4f8a", 2, 'fill-opacity=".9"'))
    g.text(*P(103, 11.2), "ладонь A02 у кромки (F2–F3)", 13, weight="bold", fill="#3c4f8a")
    g.text(*P(103, 8.8), "зазор ≤ 0,3 м по высоте, ≤ 0,5 м вперёд", 12, fill="#3c4f8a")
    g.add(f'<circle cx="{dx:.1f}" cy="{dy-12:.1f}" r="8" fill="#fff" stroke="#2f7fb5" stroke-width="3"/>')
    g.text(dx, dy - 8, "D", 11, anchor="middle", weight="bold", fill="#1d5a85")

    # break and god schematic
    bx = 1235
    g.add(f'<line x1="{bx}" y1="120" x2="{bx}" y2="{BASE}" stroke="#8a8296" stroke-width="2" stroke-dasharray="8 8"/>')
    g.add(f'<path d="M{bx-10} 420 L{bx+10} 440 L{bx-10} 460 L{bx+10} 480" fill="#f2eef4" stroke="#8a8296" stroke-width="2"/>')
    g.text(bx + 14, 114, "разрыв: расстояние до бога не в масштабе", 13, fill="#6a6276")
    gx = 1460
    body = (f'<g fill="#5f74c2" stroke="#34478a" stroke-width="2">'
            f'<path d="M{gx-70} {BASE} C{gx-40} 600 {gx-60} 420 {gx-20} 300 L{gx+30} 300 C{gx+70} 420 {gx+50} 600 {gx+80} {BASE} Z"/>'
            f'<ellipse cx="{gx+5}" cy="250" rx="46" ry="62"/>'
            f'<path d="M{gx-46} 205 L{gx+56} 205 L{gx+10} 110 Z" fill="#6b3f86"/>'
            f'<path d="M{gx-20} 360 C{gx-120} 330 {gx-190} 300 {gx-230} 330" fill="none" stroke-width="24" stroke="#5f74c2"/>'
            f'</g>')
    g.add(body)
    g.text(gx + 5, BASE + 40, "MG01 условно", 15, anchor="middle", weight="bold", fill="#34478a")
    g.text(gx + 5, BASE + 60, "не в масштабе", 13, anchor="middle", fill="#34478a")
    # face hold point H
    hx, hy = gx - 120, 250
    g.add(f'<circle cx="{hx}" cy="{hy}" r="10" fill="#fff" stroke="#2f7fb5" stroke-width="3"/>')
    g.text(hx, hy + 4, "H", 11, anchor="middle", weight="bold", fill="#1d5a85")
    g.text(hx - 16, hy - 18, "точка разговора перед лицом", 13, anchor="end", weight="bold", fill="#1d5a85")
    # paths
    g.add(f'<path d="M{gx-235} 335 C{gx-420} 420 {bx+40} 500 {dx+250:.1f} {dy+5:.1f}" fill="none" stroke="#3c4f8a" '
          f'stroke-width="3" stroke-dasharray="9 6" marker-end="url(#arr)"/>')
    g.text(gx - 330, 440, "F2 рука опускается к кромке", 14, weight="bold", fill="#3c4f8a")
    g.add(f'<path d="M{dx+120:.1f} {dy-40:.1f} C{dx+300:.1f} {dy-220:.1f} {hx-260} {hy+120} {hx-14} {hy+6}" fill="none" '
          f'stroke="#2f7fb5" stroke-width="4" marker-end="url(#arrb)"/>')
    g.text(dx + 250, dy - 230, "F5 подъём на ладони, 15–20 с", 14, weight="bold", fill="#1d5a85")
    g.text(dx + 250, dy - 211, "взгляд свободен, ходьба закрыта", 12, fill="#1d5a85")
    # first sight line from M to god head (schematic)
    mx, my = P(82, 21.6)
    g.add(f'<line x1="{mx:.1f}" y1="{my:.1f}" x2="{gx-40}" y2="270" stroke="#2f7fb5" stroke-width="1.5" stroke-dasharray="4 5"/>')
    g.text(mx + 160, my - 110, "F0 первый взгляд с устья M", 13, fill="#1d5a85", weight="bold")

    # steps list
    steps = [
        ("F0", "Выход из горла в M: свободное управление, вид на бога, пауза"),
        ("F1", "Пересёк E (2,5 м до кромки): ходьба и мышь закрыты, взгляд ведётся к богу"),
        ("F2", "Бог опускает поднятую ладонь A02, кончики пальцев встают в D, 8–12 с"),
        ("F3", "Взгляд и ходьба возвращены (без прыжка): игрок сам шагает на ладонь"),
        ("F4", "Стоит на ладони ≥ 0,5 с и дальше 2 м от кромки: ходьба закрыта"),
        ("F5", "Подъём к лицу; в конце взгляд ведётся к глазам"),
        ("F6", "Разговор: реплики бога, каждая — по E"),
        ("F7", "Последняя реплика: в руке «Пора домой»; ЛКМ — бросок"),
        ("F8", "Обычный возврат: вспышка, затемнение, лавка"),
    ]
    yy = BASE + 110
    g.add(f'<rect x="50" y="{yy-30}" width="1030" height="{len(steps)*27+30}" fill="#ffffff" stroke="#d8d1de" rx="6"/>')
    for i, (k, t) in enumerate(steps):
        g.text(70, yy + i * 27, k, 15, weight="bold", fill="#2f7fb5")
        g.text(118, yy + i * 27, t, 15)
    return g.save("finale-section.svg")


# ---------------------------------------------------------------- control table
def sequence():
    cols = ["Дверь\nлавки", "Z0–Z6\nпуть", "Z7 вид\n(F0)", "Край\n(F1)", "Рука\n(F2)", "Шаг\n(F3)", "Подъём\n(F4–F5)",
            "Разговор\n(F6)", "Карта\n(F7)", "Возврат\n(F8)"]
    rows = [
        ("Ходьба", ["—", "да", "да", "нет", "нет", "да", "нет", "нет", "нет", "нет"]),
        ("Взгляд мышью", ["—", "да", "да", "нет: ведётся", "нет: ведётся", "да", "да, в конце ведётся", "нет", "да", "нет"]),
        ("Прыжок", ["—", "да", "да", "нет", "нет", "нет", "нет", "нет", "нет", "нет"]),
        ("Бег", ["—", "нет*", "нет*", "нет", "нет", "нет", "нет", "нет", "нет", "нет"]),
        ("Карты / ЛКМ", ["—", "рука пустая", "рука пустая", "нет", "нет", "нет", "нет", "нет", "только «Пора домой»", "—"]),
        ("E", ["выход", "—", "—", "—", "—", "—", "—", "следующая реплика", "закрыть реплику", "—"]),
        ("Камера", ["затемнение", "игрок", "игрок", "к богу", "за ладонью", "игрок", "игрок → лицо", "лицо", "лицо", "вспышка"]),
        ("Тело игрока", ["—", "мотор", "мотор", "мотор стоит", "мотор стоит", "мотор", "едет с ладонью", "на ладони", "на ладони", "в лавку"]),
        ("Сохранение", ["нет", "нет", "нет", "нет", "нет", "нет", "нет", "нет", "нет", "флаг «бог встречен»"]),
    ]
    cw, rh, lw = 150, 50, 170
    W, H = lw + cw * len(cols) + 80, 210 + rh * len(rows) + 120
    g = Svg(W, H)
    g.text(40, 50, "Горы бога · что разрешено игроку на каждом шаге", 30, weight="bold")
    g.text(40, 82, "От двери лавки до возврата. Зелёное — разрешено, красное — закрыто, синее — ведёт сценарий. "
                   "* бег закрыт по предложению, решает пользователь.", 15, fill="#5a5266")
    x0, y0 = 40 + lw, 130
    for i, c in enumerate(cols):
        x = x0 + i * cw
        g.add(f'<rect x="{x}" y="{y0}" width="{cw-4}" height="64" fill="#3b3a63" rx="4"/>')
        for j, part in enumerate(c.split("\n")):
            g.text(x + cw / 2 - 2, y0 + 26 + j * 20, part, 14, anchor="middle", fill="#fff", weight="bold")
    for r, (name, vals) in enumerate(rows):
        y = y0 + 72 + r * rh
        g.text(40 + lw - 12, y + 30, name, 15, anchor="end", weight="bold")
        for i, v in enumerate(vals):
            x = x0 + i * cw
            if v.startswith("да"):
                col, tc = "#d9f0df", "#1f6e3c"
            elif v.startswith("нет"):
                col, tc = "#f5d3d3", "#a3242a"
            elif v in ("—",):
                col, tc = "#f1eef4", "#9a92a6"
            else:
                col, tc = "#d6e6f5", "#1d5a85"
            g.add(f'<rect x="{x}" y="{y}" width="{cw-4}" height="{rh-4}" fill="{col}" rx="3"/>')
            words = v.split(" ")
            lines, cur = [], ""
            for w in words:
                if len(cur) + len(w) + 1 > 17:
                    lines.append(cur)
                    cur = w
                else:
                    cur = (cur + " " + w).strip()
            lines.append(cur)
            for j, ln in enumerate(lines[:2]):
                g.text(x + cw / 2 - 2, y + rh / 2 + 3 - (len(lines[:2]) - 1) * 8 + j * 16, ln, 12, anchor="middle", fill=tc)
    g.text(40, H - 40, "Пауза (Escape) разрешена на всех шагах: сценарий открывает её через PlayerControlGate.AllowPause, "
                       "как разговор с гоблином в пещере.", 13, fill="#4a4356")
    return g.save("control-sequence.svg")


if __name__ == "__main__":
    os.makedirs(FIG, exist_ok=True)
    for f in (plan, profile, finale, sequence):
        print(f())
