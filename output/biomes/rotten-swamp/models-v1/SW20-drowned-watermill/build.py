"""SW20 Drowned watermill: mill house on stilts (SM_SW20_DrownedMill) + separate water wheel
(SM_SW20_DrownedMillWheel, pivot = axle centre, axle along X)."""
import sys, os, math, random, json
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, '/home/claude/swamp/kit'); sys.path.insert(0, HERE)
import sk, mbi
from mbi import V, board, wbox, strip, oface, crooked
import bpy

OUT = os.path.join(HERE, 'out')
SHEET = '/home/claude/ref/swamp/sheets/20-drowned-watermill.png'
os.makedirs(OUT, exist_ok=True)

DECK = 2.4                       # deck top
GIR_TOP = DECK - 0.07            # girder top (deck boards 0.07)
PILE_TOP = GIR_TOP - 0.25
HX0, HX1, HY0, HY1 = -1.75, 1.2, -1.3, 1.95   # house wall centre lines (tall, narrow two-storey house)
WZ1 = 4.4                        # lower storey wall top = belt beam
BELT = WZ1
UP0 = BELT + .15                 # upper storey starts
PLATE = 5.35                     # roof foot on the side walls
XC = (HX0 + HX1) / 2             # ridge x
HALF = (HX1 - HX0) / 2
RIDGE = PLATE + HALF * 1.19      # roof underside at ridge (~50 deg)
SLOPE = (RIDGE - PLATE) / HALF
WHEEL_C = V((3.5, -0.6, 2.0))    # wheel axle centre (pre-ground coordinates)
WATER = 0.6


def roof_z(x):
    return PLATE + (HALF - abs(x - XC)) * SLOPE


def window(props, pl, c, out, right, w, h, seed, depth=0.0, frame_off=0.13):
    c, out, right = V(c), V(out).normalized(), V(right).normalized()
    up = V((0, 0, 1))
    q = c - out * depth
    pts = [q - right * w / 2 - up * h / 2, q + right * w / 2 - up * h / 2, q + right * w / 2 + up * h / 2, q - right * w / 2 + up * h / 2]
    u0, v0, u1, v1 = props.region('window')
    oface(props, pts, [(u0, v0), (u1, v0), (u1, v1), (u0, v1)], out)
    f = c + out * frame_off
    fw = 0.08
    rnd = random.Random(seed)
    # top / bottom (sill sticks out) / sides
    board(pl, f + up * (h / 2 + fw / 2) - right * (w / 2 + .1), f + up * (h / 2 + fw / 2) + right * (w / 2 + .1), up, fw, .05, seed=seed)
    board(pl, f - up * (h / 2 + fw / 2) - right * (w / 2 + .14) + out * .03, f - up * (h / 2 + fw / 2) + right * (w / 2 + .12) + out * .03,
          out, .14, .05, seed=seed + 1, ends=('flat', 'slant'))
    for sgn in (-1, 1):
        board(pl, f + right * sgn * (w / 2 + fw / 2) - up * (h / 2 + .02), f + right * sgn * (w / 2 + fw / 2) + up * (h / 2 + .02), right, fw, .05, seed=seed + 2 + sgn)
    # cross bar of the window (real mullion)
    board(pl, q + out * .02 - up * h / 2, q + out * .02 + up * h / 2, right, .04, .03, seed=seed + 7)
    board(pl, q + out * .02 - right * w / 2, q + out * .02 + right * w / 2, up, .04, .03, seed=seed + 8)


def wall_pieces(u0, u1, z0, z1, openings):
    """Rectangles (u0,u1,z0,z1) covering a wall minus rectangular openings (ou0,ou1,oz0,oz1)."""
    out = []
    cuts = sorted(openings)
    u = u0
    for (a, b, c, d) in cuts:
        if a > u:
            out.append((u, a, z0, z1))
        if c > z0:
            out.append((a, b, z0, c))
        if d < z1:
            out.append((a, b, d, z1))
        u = b
    if u < u1:
        out.append((u, u1, z0, z1))
    return out


def build_mill():
    rnd = random.Random(20)
    bark = sk.Mesh('Bark'); pl = sk.Mesh('Planks'); lw = sk.Mesh('LogWall'); rf = sk.Mesh('Roof')
    props = sk.Mesh('Props'); plants = sk.Mesh('Plants'); moss = sk.Mesh('Moss')

    # ---------------------------------------------------------------- stilts (8) + moss collars
    PX = [-2.8, -0.35, 2.15]; PY = [-2.75, -0.05, 2.55]
    piles = []
    for i, x in enumerate(PX):
        for j, y in enumerate(PY):
            if i == 1 and j == 1:
                continue
            s = i * 3 + j
            r = random.Random(s)
            bot = V((x + r.uniform(-.18, .18), y + r.uniform(-.18, .18), 0))
            top = V((x, y, PILE_TOP + .03))
            path = crooked(bot, top, 5, .05, s)
            rad = [.28, .25, .22, .21, .2]
            bark.tube(path, rad, sides=6, jitter=.012, seed=s, rot=r.uniform(0, 1))
            piles.append((x, y))
            # moss/slime collar around the water line (jagged top)
            zc = [.15, .55, .85 + r.uniform(-.1, .15)]
            cp = [bot.lerp(top, z / top.z) for z in zc]
            moss.tube(cp, [.29, .26, .245], sides=6, caps=(False, False), jitter=.035, seed=s + 50, rot=r.uniform(0, 1))
            for k in range(3):
                a = r.uniform(0, 6.28)
                d = V((math.cos(a), math.sin(a), 0))
                p = cp[2] + d * .26
                plants.card(p, d.cross(V((0, 0, 1))) * .1, V((0, 0, r.uniform(.25, .45))), 'moss_drip_%d' % (k % 2),
                            hang=True, wind_flutter=.01)
    # extra bearing post for the wheel axle + bearing block + tie beam
    bp = V((2.66, WHEEL_C.y, 0))
    bark.tube(crooked(bp + V((.1, -.08, 0)), V((2.62, WHEEL_C.y, GIR_TOP)), 4, .03, 77), [.2, .17, .16, .15], sides=6, jitter=.01, seed=77)
    moss.tube([V((2.75, WHEEL_C.y - .07, .15)), V((2.72, WHEEL_C.y - .05, .55)), V((2.7, WHEEL_C.y - .04, .95))], [.22, .2, .19],
              sides=6, caps=(False, False), jitter=.03, seed=78)
    wbox(bark, (2.42, WHEEL_C.y - .22, WHEEL_C.z - .2), (2.86, WHEEL_C.y + .22, WHEEL_C.z - .1), grain='x', jitter=.01, seed=79)
    wbox(bark, (2.42, WHEEL_C.y - .22, WHEEL_C.z + .1), (2.86, WHEEL_C.y + .22, WHEEL_C.z + .2), grain='x', jitter=.01, seed=80)
    for sy in (-1, 1):
        wbox(bark, (2.44, WHEEL_C.y + sy * .2 - .06, WHEEL_C.z - .1), (2.84, WHEEL_C.y + sy * .2 + .06, WHEEL_C.z + .1), grain='x', seed=81)
    board(bark, (2.15, -0.05, 1.45), (2.66, WHEEL_C.y, 1.4), (0, 0, 1), .16, .12, seed=82)
    board(bark, (2.15, -2.75, 1.3), (2.68, WHEEL_C.y - .05, 1.9), (0, 0, 1), .15, .1, seed=83)

    # ---------------------------------------------------------------- girders along X, joists
    for j, y in enumerate(PY):
        board(bark, (-3.12, y, GIR_TOP - .13), (2.55, y + rnd.uniform(-.05, .05), GIR_TOP - .13), (0, 0, 1), .26, .24, seed=100 + j,
              sag=.03, ends=('slant', 'slant'), segs=3)
    # side joists along Y under the deck edge (rim beams)
    for x in (-3.0, 2.42):
        board(bark, (x, -3.0, GIR_TOP - .3), (x, 2.85, GIR_TOP - .3), (0, 0, 1), .16, .16, seed=int(110 + x * 10), sag=.04, segs=3)
    # ---------------------------------------------------------------- cross bracing (X braces + knee braces)
    def xbrace(p, q, out, seed, z0=.75, z1=2.05):
        p, q, out = V(p), V(q), V(out)
        o1, o2 = out * .25, out * .33
        a, b = V((p.x, p.y, z0)), V((q.x, q.y, z1))
        c, d = V((q.x, q.y, z0 + .05)), V((p.x, p.y, z1 - .05))
        board(bark, a + o1, b + o1, out.cross(b - a), .22, .08, seed=seed, ends=('slant', 'slant'))
        board(bark, c + o2, d + o2, out.cross(d - c), .21, .08, seed=seed + 1, ends=('slant', 'broken' if seed % 3 == 0 else 'slant'))
    xbrace((-2.8, -2.75), (-0.35, -2.75), (0, -1, 0), 200)
    xbrace((-0.35, -2.75), (2.15, -2.75), (0, -1, 0), 202, z0=.9)
    xbrace((-2.8, 2.55), (-0.35, 2.55), (0, 1, 0), 204)
    xbrace((-0.35, 2.55), (2.15, 2.55), (0, 1, 0), 206, z0=.85)
    xbrace((-2.8, -2.75), (-2.8, -0.05), (-1, 0, 0), 208)
    xbrace((-2.8, -0.05), (-2.8, 2.55), (-1, 0, 0), 210, z0=.95)
    xbrace((2.15, -0.05), (2.15, 2.55), (1, 0, 0), 212)
    # knee braces pile -> girder (front)
    for x, sx in ((-2.8, 1), (2.15, -1), (-0.35, 1)):
        board(bark, (x + sx * .1, -2.95, 1.55), (x + sx * .75, -2.95, GIR_TOP - .2), (0, -1, 0), .14, .08, seed=int(220 + x * 10))

    # ---------------------------------------------------------------- deck boards (along Y), gaps + broken ends
    x = -3.12; k = 0
    while x < 2.52:
        w = rnd.uniform(.22, .28)
        cx = x + w / 2
        inside = (cx - w / 2 > HX0 + .05) and (cx + w / 2 < HX1 - .05)
        zt = DECK - .035 + rnd.uniform(-.012, .012)
        y0 = -3.02 + rnd.uniform(-.05, .08); y1 = 2.88 + rnd.uniform(-.08, .04)
        e0 = 'broken' if rnd.random() < .3 else 'flat'
        e1 = 'broken' if rnd.random() < .3 else 'flat'
        if k == 2:                    # missing board piece (hole in the walkway) + loose stub
            board(pl, (cx, -.9, zt), (cx, y1, zt), (1, 0, 0), w, .07, seed=300 + k, ends=('broken', e1), segs=2)
            board(pl, (cx, y0, zt), (cx, -2.2, zt), (1, 0, 0), w, .07, seed=301 + k, ends=(e0, 'broken'))
        elif inside:
            board(pl, (cx, y0, zt), (cx, -0.85, zt), (1, 0, 0), w, .07, seed=300 + k, ends=(e0, 'flat'))
            board(pl, (cx, HY1 - .2, zt), (cx, y1, zt), (1, 0, 0), w, .07, seed=320 + k, ends=('flat', e1))
        else:
            ys = rnd.uniform(-.4, .3)
            board(pl, (cx, y0, zt), (cx, ys, zt + rnd.uniform(-.01, .01)), (1, 0, 0), w, .07, seed=300 + k, ends=(e0, 'flat'), segs=2)
            board(pl, (cx, ys + .01, zt), (cx, y1, zt), (1, 0, 0), w, .07, seed=340 + k, ends=('flat', e1), segs=2)
        x += w + rnd.uniform(.015, .04); k += 1
    # fascia boards on deck edges
    board(pl, (-3.14, -3.06, DECK - .19), (2.55, -3.06, DECK - .17), (0, 0, 1), .2, .05, seed=360, ends=('broken', 'flat'), segs=3)
    board(pl, (-3.16, -3.05, DECK - .19), (-3.16, 2.9, DECK - .2), (0, 0, 1), .2, .05, seed=361, segs=3)
    board(pl, (-3.12, 2.92, DECK - .2), (2.5, 2.92, DECK - .18), (0, 0, 1), .2, .05, seed=362, ends=('flat', 'broken'), segs=3)
    for i in range(10):            # moss drips along the deck edge
        p = V((rnd.uniform(-3.0, 2.3), -3.1, DECK - .12)) if i < 6 else V((-3.2, rnd.uniform(-2.8, 2.6), DECK - .12))
        R = V((.14, 0, 0)) if i < 6 else V((0, .14, 0))
        plants.card(p, R, V((0, 0, rnd.uniform(.25, .4))), 'moss_drip_%d' % (i % 2), hang=True, wind_flutter=.01)

    # ---------------------------------------------------------------- house frame: sills, posts, belt (storey line), plates
    for (a, b) in (((HX0 - .15, HY0, DECK + .09), (HX1 + .15, HY0, DECK + .09)), ((HX0 - .15, HY1, DECK + .09), (HX1 + .15, HY1, DECK + .09))):
        board(bark, a, b, (0, 0, 1), .18, .2, seed=400 + int(a[1] * 10))
    for (a, b) in (((HX0, HY0 - .15, DECK + .09), (HX0, HY1 + .15, DECK + .09)), ((HX1, HY0 - .15, DECK + .09), (HX1, HY1 + .15, DECK + .09))):
        board(bark, a, b, (0, 0, 1), .18, .2, seed=410 + int(a[0] * 10))
    for (a, b) in (((HX0 - .22, HY0 - .02, BELT + .07), (HX1 + .22, HY0 - .02, BELT + .08)), ((HX0 - .22, HY1 + .02, BELT + .08), (HX1 + .22, HY1 + .02, BELT + .06))):
        board(bark, a, b, (0, 0, 1), .16, .24, seed=420 + int(a[1] * 10), sag=.025, segs=2)
    for (a, b) in (((HX0, HY0 - .25, BELT + .07), (HX0, HY1 + .25, BELT + .07)), ((HX1, HY0 - .25, BELT + .08), (HX1, HY1 + .25, BELT + .07))):
        board(bark, a, b, (0, 0, 1), .16, .24, seed=430 + int(a[0] * 10), sag=.035, segs=2)
    for (a, b) in (((HX0 - .1, HY0 - .35, PLATE - .07), (HX0 - .1, HY1 + .35, PLATE - .07)), ((HX1 + .1, HY0 - .35, PLATE - .06), (HX1 + .1, HY1 + .35, PLATE - .07))):
        board(bark, a, b, (0, 0, 1), .16, .22, seed=435 + int(a[0] * 10), sag=.04, segs=2)
    posts = [(HX0, HY0), (HX1, HY0), (HX0, HY1), (HX1, HY1), (HX0, .45), (HX1, -.1), (XC + .45, HY1)]
    for i, (px, py) in enumerate(posts):
        board(bark, (px, py, DECK), (px + rnd.uniform(-.04, .04), py + rnd.uniform(-.04, .04), BELT), (1, 0, 0), .26, .26, seed=440 + i)
    for i, (px, py) in enumerate(posts[:4]):     # upper storey corner posts (jettied out a little)
        ox = -.08 if px < XC else .08; oy = -.08 if py < 0 else .08
        board(bark, (px + ox, py + oy, UP0), (px + ox + rnd.uniform(-.03, .03), py + oy, PLATE - .14), (1, 0, 0), .22, .22, seed=450 + i)

    # ---------------------------------------------------------------- lower storey walls: LogWall pieces with openings
    T = .1
    # -X wall (window)
    for (u0, u1, z0, z1) in wall_pieces(HY0, HY1, DECK + .18, WZ1, [(.15, .8, 3.25, 3.8)]):
        wbox(lw, (HX0 - T, u0, z0), (HX0 + T, u1, z1), grain='z', jitter=.012, seed=int(u0 * 100 + z0 * 10))
    window(props, pl, (HX0, .475, 3.525), (-1, 0, 0), (0, -1, 0), .65, .55, 500)
    # +X wall (doorway to the porch + window)
    for (u0, u1, z0, z1) in wall_pieces(HY0, HY1, DECK + .18, WZ1, [(-1.2, -.35, DECK + .18, 3.95), (.55, 1.15, 3.25, 3.8)]):
        wbox(lw, (HX1 - T, u0, z0), (HX1 + T, u1, z1), grain='z', jitter=.012, seed=int(u0 * 100 + z0 * 10) + 7)
    window(props, pl, (HX1, .85, 3.525), (1, 0, 0), (0, 1, 0), .6, .55, 510)
    u0_, v0_, u1_, v1_ = props.region('dark')
    def dark(pts, want):
        oface(props, [V(p) for p in pts], [(u0_, v0_), (u1_, v0_), (u1_, v1_), (u0_, v1_)], want)
    dark([(HX1 - .5, -1.3, DECK), (HX1 - .5, -.25, DECK), (HX1 - .5, -.25, 4.05), (HX1 - .5, -1.3, 4.05)], (1, 0, 0))
    board(bark, (HX1 + .02, -1.25, 4.03), (HX1 + .02, -.3, 4.0), (0, 0, 1), .16, .26, seed=515)   # lintel
    # back wall (window)
    for (u0, u1, z0, z1) in wall_pieces(HX0, HX1, DECK + .18, WZ1, [(-.75, -.15, 3.25, 3.8)]):
        wbox(lw, (u0, HY1 - T, z0), (u1, HY1 + T, z1), grain='z', jitter=.012, seed=int(u0 * 100 + z0 * 10) + 13)
    window(props, pl, (-.45, HY1, 3.525), (0, 1, 0), (1, 0, 0), .6, .55, 520)
    # front wall: vertical boards (door + window)
    door = (-1.3, -.42, DECK, 4.12); win = (.3, .95, 3.28, 3.83)
    x = HX0 - .12; i = 0
    yb = HY0 - .1
    while x < HX1 + .12:
        w = rnd.uniform(.17, .24)
        a, b = x, min(x + w, HX1 + .13)
        cx = (a + b) / 2
        spans = [(DECK - .02 + rnd.uniform(-.04, .02), BELT + .02)]
        for (o0, o1, oz0, oz1) in (door, win):
            if cx > o0 - .02 and cx < o1 + .02:
                spans = [(z0, oz0) for (z0, z1) in spans if oz0 - z0 > .2] + [(oz1, z1) for (z0, z1) in spans if z1 - oz1 > .1]
        for (z0, z1) in spans:
            board(pl, (cx, yb + rnd.uniform(-.01, .01), z0), (cx + rnd.uniform(-.02, .02), yb, z1), (1, 0, 0), b - a - .015, .06,
                  seed=600 + i, ends=('broken' if z0 < DECK + .05 and rnd.random() < .4 else 'flat', 'flat'))
            i += 1
        x = b
    for (u0, u1, z0, z1) in wall_pieces(HX0 + .1, HX1 - .1, DECK + .18, WZ1, [(door[0], door[1], DECK + .18, door[3]), (win[0], win[1], win[2], win[3])]):
        wbox(lw, (u0, HY0 - .04, z0), (u1, HY0 + .1, z1), grain='z', seed=int(u0 * 100) + 17)
    window(props, pl, (.625, HY0 - .04, 3.555), (0, -1, 0), (1, 0, 0), .65, .55, 530, frame_off=.1)
    dark([(-1.4, -.9, DECK), (-.3, -.9, DECK), (-.3, -.9, 4.2), (-1.4, -.9, 4.2)], (0, -1, 0))
    # door leaf, ajar outwards (hinge at x=-1.3)
    hinge = V((door[0] + .02, yb - .04, 0)); ang = math.radians(-28)
    Rz = lambda p: V((hinge.x + (p[0] - hinge.x) * math.cos(ang) - (p[1] - hinge.y) * math.sin(ang),
                      hinge.y + (p[0] - hinge.x) * math.sin(ang) + (p[1] - hinge.y) * math.cos(ang), p[2]))
    dw = door[1] - door[0] - .04
    for k in range(4):
        bx = hinge.x + dw * (k + .5) / 4
        board(pl, Rz((bx, hinge.y, DECK + .04)), Rz((bx, hinge.y, door[3] - .03 - (k == 3) * .08)), Rz((1, 0, 0)) - Rz((0, 0, 0)),
              dw / 4 - .01, .05, seed=700 + k, ends=('flat', 'broken' if k == 3 else 'flat'))
    for zz in (DECK + .35, door[3] - .35):
        board(pl, Rz((hinge.x + .03, hinge.y - .05, zz)), Rz((hinge.x + dw - .04, hinge.y - .05, zz)), (0, 0, 1), .12, .04, seed=710)
    board(pl, Rz((hinge.x + .08, hinge.y - .055, DECK + .4)), Rz((hinge.x + dw - .1, hinge.y - .055, door[3] - .4)),
          Rz((-1, 0, 1)) - Rz((0, 0, 0)), .1, .035, seed=712)
    board(bark, (door[0] - .05, yb - .06, door[3] + .06), (door[1] + .05, yb - .06, door[3] + .05), (0, 0, 1), .14, .1, seed=715)

    # ---------------------------------------------------------------- upper storey: side knee walls (vertical boards, window each)
    for xw, sgn, (wy0, wy1), sd in ((HX0 - .16, -1, (.25, .8), 560), (HX1 + .16, 1, (.45, 1.0), 580)):
        y = HY0 - .2; i = 0
        while y < HY1 + .2:
            w = rnd.uniform(.17, .24)
            a, b = y, min(y + w, HY1 + .2)
            cy_ = (a + b) / 2
            spans = [(UP0 - .03, PLATE - .02)]
            if wy0 - .02 < cy_ < wy1 + .02:
                spans = [(UP0 - .03, 4.72), (5.14, PLATE - .02)]
            for (z0, z1) in spans:
                board(pl, (xw, cy_, z0), (xw + rnd.uniform(-.01, .01), cy_, z1), (0, 1, 0), b - a - .015, .06, seed=sd + i)
                i += 1
            y = b
        window(props, pl, (xw, (wy0 + wy1) / 2, 4.93), (sgn, 0, 0), (0, -sgn, 0), wy1 - wy0, .42, sd + 50, depth=.02, frame_off=.05)
        xd = xw - sgn * .35
        dark([(xd, wy0 - .1, 4.6), (xd, wy1 + .1, 4.6), (xd, wy1 + .1, 5.15), (xd, wy0 - .1, 5.15)], (sgn, 0, 0))

    # ---------------------------------------------------------------- gables = upper storey front/back walls (boards up to the roof)
    gws = {800: (XC - .36, XC + .36, 4.8, 5.42), 900: (XC - .3, XC + .3, 4.85, 5.35)}
    for gy, nrm, sd in ((HY0 - .14, (0, 1, 0), 800), (HY1 + .14, (0, -1, 0), 900)):
        gw = gws[sd]
        x = HX0 - .22; i = 0
        while x < HX1 + .22:
            w = rnd.uniform(.17, .24)
            a, b = x, min(x + w, HX1 + .22)
            z0 = UP0 - .03 + rnd.uniform(-.02, .02)
            za, zb = max(roof_z(a) - .03, z0 + .08), max(roof_z(b) - .03, z0 + .08)
            cx = (a + b) / 2
            P = (lambda gy_: (lambda s, h: V((a + h, gy_, s))))(gy)
            Nf = (lambda n_: (lambda s, h: V(n_)))(nrm)
            if gw[0] - .02 < cx < gw[1] + .02:
                strip(pl, [((z0, 0), (z0, b - a - .012)), ((gw[2], 0), (gw[2], b - a - .012))], P, Nf, .05, uvoff=(rnd.random(), 0))
                strip(pl, [((gw[3], 0), (gw[3], b - a - .012)), ((za, 0), (zb, b - a - .012))], P, Nf, .05, uvoff=(rnd.random(), 0))
            else:
                strip(pl, [((z0, 0), (z0, b - a - .012)), ((za, 0), (zb, b - a - .012))], P, Nf, .05, uvoff=(rnd.random(), 0))
            x = b; i += 1
        # collar/tie board across the gable at the roof foot + belt cover board
        board(pl, (HX0 - .3, gy - V(nrm).y * .05, PLATE + .02), (HX1 + .3, gy - V(nrm).y * .05, PLATE), (0, 0, 1), .12, .05, seed=sd + 50)
        out = -V(nrm)
        window(props, pl, (XC, gy, (gw[2] + gw[3]) / 2), out, (out.y * -1, 0, 0), gw[1] - gw[0], gw[3] - gw[2], sd + 60, frame_off=.06)
        yd = gy + V(nrm).y * .45
        dark([(XC - .45, yd, gw[2] - .05), (XC + .45, yd, gw[2] - .05), (XC + .45, yd, gw[3] + .05), (XC - .45, yd, gw[3] + .05)], out)

    # ---------------------------------------------------------------- main roof: 4 overlapping courses per slope
    OVY0, OVY1 = HY0 - .4, HY1 + .4
    OVX = .32
    run = HALF + OVX
    hlen = run * math.sqrt(1 + SLOPE * SLOPE)
    ncour = 5
    for side in (-1, 1):
        dx = V((side * 1, 0, -SLOPE)).normalized()        # down-slope direction
        nrm = V((side * SLOPE, 0, 1)).normalized()         # outward roof normal
        top = V((XC, 0, RIDGE + .02))
        for c in range(ncour):
            h0 = hlen * c / ncour - (.1 if c else 0); h1 = hlen * (c + 1) / ncour + .02
            if c == ncour - 1:
                h1 = hlen + .06
            ys = sorted([OVY0 + rnd.uniform(-.06, .04), OVY1 + rnd.uniform(-.04, .06)] + [rnd.uniform(-.6, .0), rnd.uniform(.6, 1.3)])
            for p in range(3):
                ya, yb2 = ys[p], ys[p + 1] + .02
                tw = rnd.uniform(-.03, .03); lift = rnd.uniform(.0, .03)
                def P(s, h, h0=h0, h1=h1, tw=tw, lift=lift):
                    hh = hlen - h                      # distance from the ridge
                    sag = -.1 * math.sin(math.pi * (s - OVY0) / (OVY1 - OVY0)) * (1 - abs(hh - hlen / 2) / hlen)
                    edge = (h - h0) / max(h1 - h0, 1e-6)
                    off = (.07 + lift) * (1 - edge) + tw * (s - ya)
                    return top + dx * hh + V((0, s, 0)) + nrm * off + V((0, 0, sag))
                Nn = lambda s, h: -nrm
                st = []
                n = 4
                for q in range(n + 1):
                    s = ya + (yb2 - ya) * q / n
                    jag = rnd.uniform(-.07, .05) if 0 < q < n else 0
                    st.append(((s, h0 + jag + (.0 if c else 0)), (s, h1)))
                strip(rf, st, P, Nn, .06, swap=True, uvoff=(rnd.random(), c * .25))
        # barge boards along both gable edges
        for gy in (OVY0 - .02, OVY1 + .02):
            a = top + dx * .05 + V((0, gy, -.05)); b = top + dx * (hlen + .05) + V((0, gy, -.05))
            board(pl, a - nrm * .06, b - nrm * .06, nrm, .22, .05, seed=950 + side + int(gy))
    # ridge boards
    for side in (-1, 1):
        nrm = V((side * SLOPE, 0, 1)).normalized()
        dx = V((side * 1, 0, -SLOPE)).normalized()
        a = V((XC, OVY0 - .05, RIDGE + .14)) + dx * .1; b = V((XC, OVY1 + .05, RIDGE + .12)) + dx * .1
        board(pl, a, b, dx, .26, .05, seed=960 + side, sag=.08, segs=3)
    # purlins / ridge beam ends under the roof (visible at the gable overhang)
    board(bark, (XC, OVY0 + .05, RIDGE - .1), (XC, OVY1 - .05, RIDGE - .1), (0, 0, 1), .18, .16, seed=970, segs=2, sag=.06)
    for side in (-1, 1):
        xx = XC + side * HALF * .55
        board(bark, (xx, OVY0 + .15, roof_z(xx) - .1), (xx, OVY1 - .15, roof_z(xx) - .1), (0, 0, 1), .15, .14, seed=975 + side, segs=2, sag=.04)

    # ---------------------------------------------------------------- chimney (crooked stack of 3 blocks)
    cx, cy = XC - .78, 1.0
    z = UP0 + .05
    for i, hgt in enumerate((.9, .85, .8)):
        off = V((rnd.uniform(-.03, .03), rnd.uniform(-.03, .03), 0)) + V((i * .02, 0, 0))
        wbox(props, (cx - .27 + off.x, cy - .27 + off.y, z), (cx + .27 + off.x, cy + .27 + off.y, z + hgt), region='chimney', jitter=.01, seed=980 + i)
        z += hgt
    wbox(props, (cx - .33 + .04, cy - .33, z), (cx + .33 + .04, cy + .33, z + .1), region='chimney', jitter=.01, seed=985)
    oface(props, [V((cx - .17, cy - .17, z + .101)), V((cx + .23, cy - .17, z + .101)), V((cx + .23, cy + .17, z + .101)), V((cx - .17, cy + .17, z + .101))],
          [(u0_, v0_), (u1_, v0_), (u1_, v1_), (u0_, v1_)], (0, 0, 1))
    chim_top = z + .1

    # ---------------------------------------------------------------- lean-to over the +X porch (mill machinery side)
    LX0, LX1 = HX1 + .1, 2.85
    LZ0, LZ1 = 4.3, 3.95
    for i, py in enumerate((-1.75, .45, 2.45)):
        board(bark, (2.45, py, DECK - .02), (2.45 + rnd.uniform(-.05, .05), py, LZ1 - .05), (1, 0, 0), .16, .16, seed=1000 + i)
    board(bark, (2.45, -2.05, LZ1 - .01), (2.45, 2.75, LZ1 - .01), (0, 0, 1), .16, .16, seed=1005, sag=.05, segs=2)
    for i, py in enumerate((-1.75, .45, 2.45)):       # porch rafters
        board(bark, (HX1 + .1, py + .1, LZ0 - .1), (2.95, py + .1, LZ1 - .14), (0, 1, 0), .1, .1, seed=1010 + i)
    ldx = V((LX1 - LX0, 0, LZ1 - LZ0)); lh = ldx.length; ldx.normalize()
    lnrm = V((-ldx.z, 0, ldx.x)) if ldx.x > 0 else V((ldx.z, 0, -ldx.x))
    if lnrm.z < 0: lnrm = -lnrm
    for c in range(2):
        h0 = lh * c / 2 - (.08 if c else 0); h1 = lh * (c + 1) / 2 + (.12 if c == 1 else .02)
        for p, (ya, yb2) in enumerate(((-2.1, .2), (.18, 2.82))):
            def P(s, h, h0=h0, h1=h1):
                hh = lh - h
                edge = (h - h0) / max(h1 - h0, 1e-6)
                return V((LX0, s, LZ0)) + ldx * hh + lnrm * (.06 * (1 - edge)) + V((0, 0, -.06 * math.sin(math.pi * (s + 2.1) / 4.9)))
            st = [((ya + (yb2 - ya) * q / 3, h0 + (rnd.uniform(-.06, .04) if 0 < q < 3 else 0)), (ya + (yb2 - ya) * q / 3, h1)) for q in range(4)]
            strip(rf, st, P, lambda s, h: -lnrm, .06, swap=True, uvoff=(rnd.random(), .5 * c))

    # ---------------------------------------------------------------- railing (front + -X side + back corner), one broken
    def rail_run(pts, seed, broken=None):
        r = random.Random(seed)
        tops = []
        for i, p in enumerate(pts):
            h = .95 + r.uniform(-.06, .05)
            base = V((p[0], p[1], DECK - .02))
            topp = base + V((r.uniform(-.05, .05), r.uniform(-.05, .05), h))
            board(bark, base, topp, (1, 0, 0), .12, .12, seed=seed + i, ends=('flat', 'slant'))
            tops.append(topp)
        for i in range(len(pts) - 1):
            a, b = tops[i], tops[i + 1]
            d = (b - a).normalized()
            for k, dz in enumerate((-.08, -.48)):
                aa, bb = a + V((0, 0, dz)) - d * .06, b + V((0, 0, dz)) + d * .06
                if broken == (i, k):
                    mid = aa.lerp(bb, .45)
                    board(pl, aa, mid, (0, 0, 1), .1, .05, seed=seed + 40, ends=('flat', 'broken'))
                    board(pl, bb, bb.lerp(aa, .3) + V((0, 0, -.35)), (0, 0, 1), .1, .05, seed=seed + 41, ends=('flat', 'broken'))
                    continue
                board(pl, aa, bb, (0, 0, 1), .1, .05, seed=seed + 20 + i * 2 + k, sag=.02 if k else .0)
    rail_run([(1.3, -2.95), (.25, -2.96), (-.85, -2.94), (-1.95, -2.96), (-3.04, -2.95)], 1100, broken=(1, 1))
    rail_run([(-3.04, -2.95), (-3.06, -1.8), (-3.05, -.6), (-3.04, .6), (-3.06, 1.7), (-3.04, 2.8)], 1150, broken=(3, 0))
    rail_run([(-3.04, 2.8), (-1.95, 2.82)], 1190)

    objs = {'Bark': bark, 'Planks': pl, 'LogWall': lw, 'Roof': rf, 'Props': props}
    res = {k: m.to_object('SW20a_' + k) for k, m in objs.items()}
    # moss: roof (heavy, with drips), deck/planks (light), bark beams (light)
    mo = [moss.to_object('SW20a_MossCollars')]
    mo.append(sk.moss_drape(res['Roof'], 'm1', min_nz=.55, keep=.42, seed=3, drips=plants))
    mo.append(sk.moss_drape(res['Planks'], 'm2', min_nz=.85, keep=.22, seed=4))
    mo.append(sk.moss_drape(res['Bark'], 'm3', min_nz=.5, keep=.3, seed=5))
    mo.append(sk.moss_drape(res['LogWall'], 'm4', min_nz=.8, keep=.5, seed=6))
    mobj = mbi.merge(mo, 'SW20a_Moss')
    pobj = plants.to_object('SW20a_Plants')
    out = [res['Bark'], res['Planks'], res['LogWall'], res['Roof'], res['Props'], mobj, pobj]
    # global settling: the house leans a bit to +X/-Y, the front-left deck corner has sunk
    for o in out:
        for v in o.data.vertices:
            v.co = settle(v.co)
    return out, chim_top


def settle(p):
    x, y, z = p
    w = min(1, max(0, (z - .8) / 1.4)); w = w * w * (3 - 2 * w)
    cf = min(1, max(0, (-x - .5) / 2.6)) * min(1, max(0, (-y + .5) / 3.5))
    dz = -.16 * w * cf
    hz = max(0, z - DECK)
    return V((x + .03 * hz + .02 * w, y - .018 * hz, z + dz - .001 * hz * hz))


def build_wheel():
    pl = sk.Mesh('Planks'); bark = sk.Mesh('Bark'); props = sk.Mesh('Props')
    rnd = random.Random(21)
    N = 12
    def rp(x, r, a):
        return V((x, r * math.cos(a), r * math.sin(a)))
    # two rims of 12 crooked segments
    for side in (-1, 1):
        xs = side * .34
        for k in range(N):
            a0 = 2 * math.pi * k / N - .03; a1 = 2 * math.pi * (k + 1) / N + .03
            am = (a0 + a1) / 2
            r = 1.6 + rnd.uniform(-.015, .015)
            board(pl, rp(xs + rnd.uniform(-.01, .01), r, a0), rp(xs, r, a1), rp(0, 1, am), .24, .08, seed=10 + k + side * 50,
                  ends=('slant', 'slant'))
    # soling boards (closed drum) – 2 boards per segment, one missing / one broken
    for k in range(N * 2):
        a = 2 * math.pi * (k + .5) / (N * 2)
        if k == 7:
            continue
        r = 1.745
        tang = V((0, -math.sin(a), math.cos(a)))
        ends = ('flat', 'broken') if k in (3, 16) else ('flat', 'flat')
        board(pl, rp(-.42, r, a), rp(.42 - (.15 if k == 16 else 0), r, a), tang, .44, .05, seed=100 + k, ends=ends)
    # paddles (radial boards beyond the drum)
    for k in range(N):
        a = 2 * math.pi * k / N
        rad = rp(0, 1, a)
        r0, r1 = 1.76, 1.9 + rnd.uniform(-.02, .0)
        board(pl, rp(-.42, (r0 + r1) / 2, a), rp(.42, (r0 + r1) / 2, a), rad, r1 - r0, .06, seed=200 + k,
              ends=('broken' if k == 5 else 'flat', 'flat'))
    # spokes: 10 per side
    for side in (-1, 1):
        xs = side * .3
        for k in range(10):
            a = 2 * math.pi * (k + .25) / 10
            tang = V((0, -math.sin(a), math.cos(a)))
            board(bark, rp(xs, .2, a), rp(xs + side * .03, 1.57, a + rnd.uniform(-.02, .02)), tang, .13, .08, seed=300 + k + side * 20)
    # hub: octagonal block + flanges, axle with stubs (axle along X, pivot = centre)
    bark.tube([V((-.42, 0, 0)), V((.42, 0, 0))], [.3, .3], sides=8, rot=math.pi / 8, caps=(True, True))
    for side in (-1, 1):
        bark.tube([V((side * .2, 0, 0)), V((side * .38, 0, 0))], [.37, .37], sides=8, caps=(True, True))
    rings = bark.tube([V((-.98, 0, 0)), V((-.42, 0, 0))], [.1, .1], sides=8, caps=(False, False))
    props.cap(list(reversed(rings[0])), region='end_grain_1')
    rings = bark.tube([V((.42, 0, 0)), V((.62, 0, 0))], [.1, .1], sides=8, caps=(False, False))
    props.cap(rings[-1], region='end_grain_0')
    # iron bands on the hub
    for side in (-1, 1):
        mbi.rtube(props, [V((side * .43, 0, 0)), V((side * .47, 0, 0))], [.16, .16], 8, 'iron')
    po = pl.to_object('SW20w_Planks'); bo = bark.to_object('SW20w_Bark'); pr = props.to_object('SW20w_Props')
    def pred(c, n):
        r = V((0, c.y, c.z))
        return r.length > 1.65 and r.length > 1e-3 and n.dot(r.normalized()) > .7
    mo = mbi.skin(po, 'SW20w_Moss', pred, keep=.45, seed=7, offset=.01)
    mo2 = mbi.skin(po, 'mw2', lambda c, n: abs(n.x) > .9 and V((0, c.y, c.z)).length > 1.45 and c.z > -0.3, keep=.3, seed=8, offset=.008)
    mo = mbi.merge([mo, mo2], 'SW20w_Moss')
    return [po, bo, pr, mo]


def main(render=True):
    sk.reset()
    mill, chim_top = build_mill()
    d = sk.ground(mill)
    mount = settle(WHEEL_C) + d
    wheel = build_wheel()
    fbx_m = os.path.join(OUT, 'SM_SW20_DrownedMill.fbx'); fbx_w = os.path.join(OUT, 'SM_SW20_DrownedMillWheel.fbx')
    sk.export_fbx(mill, fbx_m); sk.export_fbx(wheel, fbx_w)
    wl = round(WATER + d.z, 3)
    recs = [sk.variant_record('SW20a', 'DrownedMill', mill, fbx_m, water_line_z=wl, solidity='solid', burns=False,
                              collider='piles+bearing post: capsules/boxes; deck: one box (top at z=%.2f); house walls: box; roof: 2 sloped boxes; '
                                       'braces, railing rails, drips, moss: no collider (railing posts optional). No ladder/stairs: the deck and the roof '
                                       'are reachable only via cards; keep diagonal braces without colliders so they are not a climbing route.' % (DECK + d.z),
                              moving_parts='water wheel = separate FBX SM_SW20_DrownedMillWheel (separate prefab child); put its pivot at '
                                           'wheel_mount_xyz_m (Blender X right, Y back, Z up, relative to the mill pivot) = wheel_mount_unity_guess '
                                           '(standard Unity FBX import: X mirrored, Blender Y -> -Z, Z -> Y; verify once against the bearing block '
                                           'under the +X deck edge, where the axle stub ends). Spin around the wheel local X axis (axle); paddles '
                                           'dip ~0.6 m below the water line.',
                              wheel_mount_xyz_m=[round(mount.x, 3), round(mount.y, 3), round(mount.z, 3)],
                              wheel_mount_unity_guess=[round(-mount.x, 3), round(mount.z, 3), round(-mount.y, 3)],
                              wheel_gap_to_mill_m='wheel inner face (local x=-0.42) is 0.24 m from the bearing post, ~0.6 m from the deck edge, ~1.7 m from the house wall, ~0.15 m horizontal clearance under the lean-to eave; axle stub ends inside the bearing block',
                              deck_top_z=round(DECK + d.z, 3),
                              notes='Front (-Y) = door + window + gable window, railing walkway on the front and -X side; porch with lean-to roof and '
                                    'the wheel on +X. Water line at the moss collars of the stilts. Pivot = bottom of the stilts, XY centred on the mill '
                                    '(without the wheel).'),
            sk.variant_record('SW20w', 'DrownedMillWheel', wheel, fbx_w, water_line_z=None, solidity='solid (moving)', burns=False,
                              collider='none or one thin cylinder; it spins', pivot='axle centre (0,0,0), axle along local X',
                              diameter_m=3.8, notes='12 rim segments x2, 10 spokes per side, 23 soling boards (one missing, two broken), '
                                                   '12 paddles, octagonal hub with iron bands, axle stub reaching the bearing block on the mill.')]
    json.dump(recs, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)
    # side-by-side blend: wheel moved to its mount for the assembled view
    for o in wheel:
        o.location = mount
    bpy.context.view_layer.update()
    mbi.check_orientation(mill + wheel)
    sk.save_blend(os.path.join(OUT, 'SW20.blend'))
    print('TRIS mill', sk.tris(mill), 'wheel', sk.tris(wheel), 'mount', mount, 'bounds', sk.bounds(mill))
    if render:
        R = os.path.join(HERE, 'work'); os.makedirs(R, exist_ok=True)
        allo = mill + wheel
        views = [('front 3/4', -38, 6, None), ('side -X', -95, 5, None), ('back 3/4', 140, 6, None), ('in water', -55, 16, WATER + d.z)]
        tiles = []
        for i, (lab, az, el, wz) in enumerate(views):
            p = os.path.join(R, 'r%d.png' % i)
            sk.render(allo, p, az=az, el=el, size=(440, 440), samples=10, water_z=wz, margin=1.0)
            tiles.append(p)
        for o in wheel:
            o.location = (0, 0, 0)
        bpy.context.view_layer.update()
        p = os.path.join(R, 'r4.png'); sk.render(wheel, p, az=60, el=12, size=(440, 440), samples=10, margin=1.05); tiles.append(p)
        p = os.path.join(R, 'r5.png'); sk.render(mill, p, az=40, el=40, size=(440, 440), samples=10, margin=1.0); tiles.append(p)
        sk.sheet(tiles, os.path.join(OUT, 'card.jpg'), 3, labels=[v[0] for v in views] + ['wheel (separate FBX)', 'mill without wheel'],
                 concept=SHEET, title='SW20 Drowned watermill')


if __name__ == '__main__':
    main(render='norender' not in sys.argv)
