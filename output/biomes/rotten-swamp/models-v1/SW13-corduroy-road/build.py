"""SW13 Гать (corduroy log road) - 5 modular pieces on a 4 m grid.

GRID / JOINT CONVENTION (all modules):
  * pivot (0,0,0) = centre of the ENTRY joint edge, at the bottom of the runners (stringers).
  * the road enters from -Y and runs along +Y; a module occupies the 4x4 m cell x in [-2,2], y in [0,4].
  * every open joint is 2.25 m wide (log ends at |offset| <= ~1.15), centred on a cell edge midpoint:
      straight / hole : entry (0,0) -> exit (0,4), exit direction +Y
      corner          : entry (0,0) -> exit (+2,2), exit direction +X  (right turn; enter through the exit
                        edge to get a left turn - the piece is symmetric under swapping its two joints)
      T-junction      : entry (0,0) -> exits (-2,2) dir -X and (+2,2) dir +X
      end             : entry (0,0) only; the road dies out towards +Y inside the cell
  * runners (two 4-sided hewn stringers) sit at +-0.75 m from the road centre line, z 0..0.17, and are cut flat
    exactly on the joint planes, so neighbouring modules' runners butt end-to-end.
  * deck top (log tops) = z 0.45, water line = z 0.30 (deck +0.15 above water).
  * to place: next module origin = previous module exit point, rotated so its +Y = exit direction
    (rotZ = -90 deg for +X, +90 deg for -X, 180 for -Y).
"""
import sys, math, random, os, json
sys.path.insert(0, '/home/claude/swamp/kit'); sys.path.insert(0, '/home/claude/swamp/items/_pathsfences')
import sk, pflib as pf
from mathutils import Vector as V, Matrix
import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
SHEET = '/home/claude/ref/swamp/sheets/13-corduroy-road.png'
HW = 1.125      # half road width
RX = 0.75       # runner offset
TOP = 0.45      # deck top
WATER = 0.30
RUN_H = 0.36


def M():
    return {'Bark': sk.Mesh('Bark'), 'BarkMossy': sk.Mesh('BarkMossy'), 'Props': sk.Mesh('Props'),
            'Moss': sk.Mesh('Moss'), 'Plants': sk.Mesh('Plants')}


def log(ms, p0, p1, r, seed, ends=('cut', 'cut'), sink=0.0, tilt=0.0, r1=None, sides=None, top=TOP, crook=0.05,
        npts=6, mossy=None, roll=None, blen=0.16, sag=0.0):
    """Cross log from p0 to p1 (xy), flat-ish top at `top`-sink; tilt = z drop of the p1 end (m).
    r1: radius at p1 end (taper)."""
    rnd = random.Random(seed)
    sides = sides or rnd.choice((6, 6, 7))
    p0 = V((p0[0], p0[1], 0)); p1 = V((p1[0], p1[1], 0))
    d = (p1 - p0); L = d.length; d.normalize()
    side = V((-d.y, d.x, 0))
    su = rnd.uniform(0.74, 0.84); ss = rnd.uniform(1.08, 1.16)
    top = top + rnd.uniform(-0.022, 0.022)
    r1 = r if r1 is None else r1
    path, radii, sc = [], [], []
    c1, c2 = rnd.uniform(-1, 1), rnd.uniform(-1, 1)
    for i in range(npts):
        s = i / (npts - 1)
        rr = (r + (r1 - r) * s) * rnd.uniform(0.95, 1.05)
        lat = crook * (c1 * math.sin(math.pi * s) + c2 * 0.5 * math.sin(2 * math.pi * s)) if 0 < i < npts - 1 else 0
        z = top - sink - tilt * s - sag * math.sin(math.pi * s) - rr * su * math.cos(math.pi / sides) + (rnd.uniform(-0.01, 0.01) if 0 < i < npts - 1 else 0)
        path.append(p0 + d * (L * s) + side * lat + V((0, 0, z)))
        radii.append(rr); sc.append((su, ss))
    if mossy is None:
        mossy = rnd.random() < 0.15
    m = ms['BarkMossy' if mossy else 'Bark']
    rot = math.pi / sides + (roll if roll is not None else rnd.uniform(-0.06, 0.06))
    return pf.beam(m, path, radii, sides=sides, rot=rot, scale=sc, ends=ends, props=ms['Props'],
                   cap_region=rnd.choice(('end_grain_0', 'end_grain_1')), seed=seed, broken_len=blen, up_hint=(0, 0, 1))


def runner(ms, p_list, seed, ends=('cut', 'cut'), z_end=None):
    """Hewn 4-sided stringer along polyline (xy), bottom at z=0. z_end: optional z drop at the far end."""
    rnd = random.Random(seed)
    path = []
    n = len(p_list)
    for i, p in enumerate(p_list):
        dz = (z_end or 0) * (i / (n - 1)) ** 2
        path.append(V((p[0], p[1], RUN_H / 2 + dz + (rnd.uniform(-0.008, 0.008) if 0 < i < n - 1 else 0))))
    sc = [(1.0, 0.68 * rnd.uniform(0.95, 1.08)) for _ in path]
    return pf.beam(ms['Bark'], path, RUN_H / 2 * 1.414, sides=4, rot=math.pi / 4, scale=sc, ends=ends,
                   props=ms['Props'], cap_region='end_grain_1', seed=seed, broken_len=0.18)


def straight_runners(ms, seed, y0=0.0, y1=4.0, ends=(('cut', 'cut'), ('cut', 'cut'))):
    for k, x in enumerate((-RX, RX)):
        pts = [(x, y0)] + [(x + random.Random(seed + k * 7 + j).uniform(-0.02, 0.02), y0 + (y1 - y0) * j / 4) for j in (1, 2, 3)] + [(x, y1)]
        runner(ms, pts, seed + k, ends=ends[k])


def fill(y0, y1, seed, rmin=0.22, rmax=0.30, inset=0.09, ov=0.84, inset1=None):
    """Fat cross logs packed tight (slightly overlapping) between two joint planes, the first/last log inset
    from the joint so the runner ends stick out. Returns list of [yc, r]."""
    rnd = random.Random(seed)
    W = 1.12
    i1 = inset if inset1 is None else inset1
    r = rnd.uniform(rmin, rmax)
    logs = [[y0 + inset + r * W, r]]
    while True:
        r = rnd.uniform(rmin, rmax)
        yc = logs[-1][0] + (logs[-1][1] + r) * W * ov
        if yc + r * W > y1 - i1 + 0.12:
            break
        logs.append([yc, r])
    if len(logs) > 1:
        a = logs[0][0]
        k = (y1 - i1 - logs[-1][1] * W - a) / (logs[-1][0] - a)
        for l in logs:
            l[0] = a + (l[0] - a) * k
    else:
        logs[0][0] = (y0 + y1) / 2
        logs[0][1] = min(logs[0][1], ((y1 - y0) / 2 - inset) / W)
    return logs


def splinters(ms, rings, d, seed, n=2):
    """Splinters hanging from a broken log end down into the gap. rings: from log(); d: direction of the log."""
    rnd = random.Random(seed)
    ring = rings[-1]
    c = sum(ring, V()) / len(ring)
    d = V((d[0], d[1], 0)).normalized()
    for k in range(n):
        p = ring[rnd.randrange(len(ring))].lerp(c, 0.4)
        L = rnd.uniform(0.25, 0.45)
        q1 = p + d * L * 0.4 + V((rnd.uniform(-.04, .04), rnd.uniform(-.04, .04), -L * 0.35))
        q2 = p + d * L * 0.6 + V((rnd.uniform(-.06, .06), rnd.uniform(-.06, .06), -L * 0.9))
        pf.beam(ms['Bark'], [p - d * 0.04, q1, q2], [rnd.uniform(.03, .045), .025, .012], sides=4, ends=('cut', 'point'),
                seed=seed + k, up_hint=(0, 0, 1))


def sedges(ms, pts, seed):
    rnd = random.Random(seed)
    for i, (x, y) in enumerate(pts):
        kind = 'cattail' if rnd.random() < 0.25 else 'sedge'
        h = rnd.uniform(0.75, 1.1) if kind == 'cattail' else rnd.uniform(0.5, 0.8)
        pf.tuft(ms['Plants'], (x, y, WATER - 0.08), h, h * 0.9, seed + i * 3, kind=kind, n=3)


def moss_ok(c):
    return c.z > 0.38


DRAPE = [('Bark', 0.85, 0.2, 3, moss_ok), ('BarkMossy', 0.85, 0.3, 4, moss_ok)]


# ------------------------------------------------------------------ modules
def straight(vid='SW13a'):
    ms = M(); rnd = random.Random(13)
    for i, (yc, r) in enumerate(fill(0, 4, 1)):
        sink = 0.07 + rnd.uniform(0, 0.03) if i == 2 else 0.0      # one sunk log
        sag = 0.06 if i == 5 else 0.0                                 # one sagging log
        tilt = rnd.uniform(-0.03, 0.03)
        log(ms, (-HW + rnd.uniform(-0.07, 0.06), yc), (HW + rnd.uniform(-0.06, 0.07), yc + rnd.uniform(-0.03, 0.03)),
            r, 100 + i, sink=sink, tilt=tilt, sag=sag)
    straight_runners(ms, 50)
    sedges(ms, [(-1.5, 0.8), (1.55, 2.6), (-1.6, 3.3)], 7)
    return pf.finish(vid, ms, DRAPE)


def corner(vid='SW13b'):
    ms = M(); rnd = random.Random(21)
    O = V((2, 0, 0))
    rin, rout = 2 - HW, 2 + HW          # 0.875 .. 3.125
    nf = 8
    phi0, phi1 = 0.14, math.pi / 2 - 0.14
    phis = [phi0 + (phi1 - phi0) * i / (nf - 1) for i in range(nf)]

    def rad_full(rr):
        return 0.11 + (0.27 - 0.11) * (rr - rin) / (rout - rin)

    def pt(phi, rr):         # phi measured from the entry joint line (phi=0 -> along -X from O)
        return O + V((-math.cos(phi) * rr, math.sin(phi) * rr, 0))
    for i, ph in enumerate(phis):
        a = rin + rnd.uniform(-0.02, 0.08); b = rout + rnd.uniform(-0.1, 0.0)
        sink = 0.07 if i == 4 else 0.0
        log(ms, pt(ph, a)[:2], pt(ph, b)[:2], rad_full(a), 200 + i, r1=rad_full(b) * rnd.uniform(.95, 1.05),
            sink=sink, tilt=-rnd.uniform(0, 0.03), crook=0.02, sag=0.04 if i == 2 else 0.0)
    # wedge fillers where the fan opens up (between logs and next to the joint lines)
    W = 1.12
    bounds = [(0.0, lambda rr: 0.09)] + [(p, lambda rr: rad_full(rr) * W) for p in phis] + [(math.pi / 2, lambda rr: 0.09)]
    for j in range(len(bounds) - 1):
        (pa, fa), (pb, fb) = bounds[j], bounds[j + 1]
        rr = 2.7
        g = rr * (pb - pa) - fa(rr) - fb(rr)
        pc = pa + (fa(rr) + g / 2) / rr
        pts, rads = [], []
        for rr in [1.5 + 0.2 * k for k in range(10)] + [rout - 0.05]:
            f = min(rr * (pc - pa) - fa(rr), rr * (pb - pc) - fb(rr)) / W
            if f >= 0.05 and rr <= rout:
                pts.append(rr); rads.append(f)
        if len(pts) >= 2:
            a, b = pts[0], pts[-1] + rnd.uniform(-0.05, 0.0)
            log(ms, pt(pc, a)[:2], pt(pc, b)[:2], rads[0], 260 + j, r1=rads[-1], sides=5, crook=0.0, npts=3,
                sink=0.0 if j % 3 else 0.02)
    # runners: arcs at radius 2 -/+ 0.75 from O, with straight lead-in/out exactly normal to the joints
    for k, R in enumerate((2 - RX, 2 + RX)):
        pts = [(2 - R, 0.0), (2 - R, 0.08)]
        for q in range(1, 6):
            ph = 0.08 / R + (math.pi / 2 - 0.16 / R) * q / 6
            p = pt(ph, R); pts.append((p.x, p.y))
        pts += [(2 - 0.08, R), (2.0, R)]
        runner(ms, pts, 70 + k)
    sedges(ms, [(1.6, 0.35), (-1.6, 2.4), (0.2, 3.7), (1.65, 0.95)], 8)
    return pf.finish(vid, ms, DRAPE)


def tjunction(vid='SW13c'):
    ms = M(); rnd = random.Random(31)
    # bar (along X), logs run along Y from y=0.875 to 3.125
    for i, (xc, r) in enumerate(fill(-2, 2, 3)):
        y0 = 2 - HW + rnd.uniform(0.0, 0.06)
        y1 = 2 + HW + rnd.uniform(-0.07, 0.06)
        sink = 0.07 if i == 5 else 0.0
        log(ms, (xc, y0), (xc + rnd.uniform(-0.03, 0.03), y1), r, 300 + i, sink=sink, tilt=rnd.uniform(-0.03, 0.04),
            sag=0.05 if i == 2 else 0.0)
    # stem (along Y), logs along X between the entry joint and the bar
    for i, (yc, r) in enumerate(fill(0, 2 - HW, 5, rmin=0.17, rmax=0.21, inset1=0.0)):
        log(ms, (-HW + rnd.uniform(-0.07, 0.05), yc), (HW + rnd.uniform(-0.05, 0.07), yc), r, 340 + i)
    for k, y in enumerate((2 - RX, 2 + RX)):
        pts = [(-2, y)] + [(-2 + j, y + rnd.uniform(-0.02, 0.02)) for j in (1, 2, 3)] + [(2, y)]
        runner(ms, pts, 80 + k)
    for k, x in enumerate((-RX, RX)):
        runner(ms, [(x, 0), (x, 0.5), (x, 2 - RX - 0.12)], 84 + k)
    sedges(ms, [(-1.55, 0.45), (1.5, 0.6), (-0.6, 3.6), (1.3, 3.55)], 9)
    return pf.finish(vid, ms, DRAPE)


def hole(vid='SW13d'):
    ms = M(); rnd = random.Random(41)
    logs = fill(0, 4, 4)
    for i, (yc, r) in enumerate(logs):
        xa = -HW + rnd.uniform(-0.07, 0.05); xb = HW + rnd.uniform(-0.05, 0.07)
        if 1.3 < yc < 2.75:
            # the hole: logs survive on the left edge (on the intact left runner), broken ends sag into the gap
            xbk = rnd.uniform(-0.45, 0.05) - (0.2 if i % 2 else 0)
            p0, p1 = (xa, yc), (xbk, yc + rnd.uniform(-0.04, 0.04))
            rg = log(ms, p0, p1, r, 400 + i, ends=('cut', 'broken'), tilt=rnd.uniform(0.12, 0.2), sink=0.01 * (i % 2),
                     blen=0.32)
            splinters(ms, rg, (p1[0] - p0[0], p1[1] - p0[1]), 700 + i, n=2)
            if i % 2 == 0:
                # right stub, unsupported: dropped and tilted into the water
                q0, q1 = (HW + rnd.uniform(0, 0.08), yc + rnd.uniform(-.05, .05)), (rnd.uniform(0.5, 0.75), yc + rnd.uniform(-.08, .08))
                rg = log(ms, q0, q1, r * 0.92, 450 + i, ends=('cut', 'broken'), sink=0.1, tilt=0.2, crook=0.01, blen=0.28)
                splinters(ms, rg, (q1[0] - q0[0], q1[1] - q0[1]), 720 + i, n=1)
        elif 0.85 < yc <= 1.3 or 2.75 <= yc < 3.2:
            p0, p1 = (xa, yc), (xb - rnd.uniform(0.25, 0.45), yc)
            rg = log(ms, p0, p1, r, 400 + i, ends=('cut', 'broken'), tilt=0.06, blen=0.3)
            splinters(ms, rg, (1, 0), 740 + i, n=2)
        else:
            log(ms, (xa, yc), (xb, yc + rnd.uniform(-0.03, 0.03)), r, 400 + i, tilt=rnd.uniform(-0.03, 0.03))
    # fallen fragments lying in the hole, half under water
    log(ms, (-0.15, 1.6), (0.8, 2.15), 0.17, 480, ends=('broken', 'broken'), sink=0.14, tilt=0.1, crook=0.02, blen=0.25)
    log(ms, (0.95, 2.55), (0.25, 2.3), 0.15, 481, ends=('broken', 'broken'), sink=0.2, tilt=-0.04, crook=0.02, blen=0.25)
    # left runner whole, right runner broken across the hole (broken ends drop a little)
    runner(ms, [(-RX, 0), (-RX + .02, 1), (-RX - .01, 2), (-RX + .01, 3), (-RX, 4)], 90)
    runner(ms, [(RX, 0), (RX + 0.01, 0.7), (RX - 0.01, 1.4)], 91, ends=('cut', 'broken'), z_end=-0.06)
    runner(ms, [(RX, 4), (RX + 0.02, 3.3), (RX + 0.02, 2.65)], 92, ends=('cut', 'broken'), z_end=-0.08)
    sedges(ms, [(1.55, 1.7), (-1.55, 0.7), (1.5, 3.4), (0.5, 2.05)], 10)
    return pf.finish(vid, ms, DRAPE)


def end(vid='SW13e'):
    ms = M(); rnd = random.Random(51)
    logs = fill(0, 4, 6)
    for i, (yc, r) in enumerate(logs):
        xa = -HW + rnd.uniform(-0.07, 0.05); xb = HW + rnd.uniform(-0.05, 0.07)
        if yc < 1.65:
            log(ms, (xa, yc), (xb, yc + rnd.uniform(-.03, .03)), r, 500 + i, tilt=rnd.uniform(-0.03, 0.03))
            continue
        k = (yc - 1.65) / 2.3             # 0 .. 1 decay towards the far end
        if yc > 3.5:
            break
        a = xa + rnd.uniform(0.0, 0.3) * k + (0.3 * k if i % 2 else 0)
        b = xb - rnd.uniform(0.1, 0.4) * k - (0.0 if i % 2 else 0.35 * k)
        # decayed logs: one end still on a runner, the other end dipping into the water
        tilt = (0.1 + 0.1 * k) * (1 if i % 2 else -1)
        p0, p1 = (a, yc + rnd.uniform(-.05, .05)), (b, yc + rnd.uniform(-.1, .1))
        rg = log(ms, p0, p1, r * (1 - 0.1 * k), 500 + i,
                 ends=('broken' if k > 0.15 else 'cut', 'broken'), sink=0.02 + 0.05 * k + (abs(tilt) if tilt < 0 else 0),
                 tilt=tilt, blen=0.28 + 0.1 * k)
        splinters(ms, rg, (p1[0] - p0[0], p1[1] - p0[1]), 760 + i, n=2)
    # last loose fragment, mostly under water
    log(ms, (-0.6, 3.3), (0.35, 3.55), 0.16, 590, ends=('broken', 'broken'), sink=0.25, tilt=0.06, blen=0.25)
    # old stumps of the former road
    for j, (x, y, h, rr) in enumerate(((-0.8, 3.6, 0.58, 0.15), (0.25, 3.82, 0.45, 0.13), (0.78, 3.45, 0.64, 0.16),
                                        (-0.28, 3.88, 0.38, 0.1))):
        path = [(x, y, 0.0), (x + rnd.uniform(-.03, .03), y + rnd.uniform(-.03, .03), h * 0.5),
                (x + rnd.uniform(-.05, .05), y + rnd.uniform(-.05, .05), h)]
        pf.beam(ms['Bark'], path, [rr * 1.1, rr, rr * 0.95], sides=6, ends=('cut', 'broken'), props=ms['Props'],
                cap_region='end_grain_0', seed=560 + j, broken_len=0.12)
    runner(ms, [(-RX, 0), (-RX + .02, 1), (-RX, 2.0), (-RX + .03, 2.8)], 95, ends=('cut', 'broken'), z_end=-0.05)
    runner(ms, [(RX, 0), (RX - .01, 1), (RX + 0.02, 2.2), (RX + 0.06, 3.15)], 96, ends=('cut', 'broken'), z_end=-0.1)
    sedges(ms, [(-1.55, 1.2), (1.5, 2.5), (-1.25, 3.45), (1.25, 3.8)], 11)
    return pf.finish(vid, ms, DRAPE)


VARIANTS = [('SW13a', 'CorduroyStraight', straight, 'straight 4 m: entry (0,0) -> exit (0,4) dir +Y'),
            ('SW13b', 'CorduroyCorner', corner, 'corner 90 deg: entry (0,0) -> exit (2,2) dir +X'),
            ('SW13c', 'CorduroyTJunction', tjunction, 'T: entry (0,0) -> exits (-2,2) dir -X and (2,2) dir +X'),
            ('SW13d', 'CorduroyHole', hole, 'straight with a collapsed hole: entry (0,0) -> exit (0,4)'),
            ('SW13e', 'CorduroyEnd', end, 'end: entry (0,0), dies out towards +Y')]


def assembled(groups):
    """Copy modules onto the grid for a connection check: S -> hole -> T; T right: corner -> end; T left: straight."""
    lay = [('SW13a', (0, -4), 0), ('SW13d', (0, 0), 0), ('SW13c', (0, 4), 0),
           ('SW13b', (2, 6), -90), ('SW13e', (4, 4), -180),
           ('SW13a', (-2, 6), 90), ('SW13b', (-6, 6), 90), ('SW13e', (-8, 8), 0)]
    objs = []
    for vid, (x, y), a in lay:
        T = Matrix.Translation(V((x, y, 0))) @ Matrix.Rotation(math.radians(a), 4, 'Z')
        for o in groups[vid]:
            me = o.data.copy()
            me.transform(T)
            ob = bpy.data.objects.new('asm_' + o.name, me)
            bpy.context.scene.collection.objects.link(ob)
            objs.append(ob)
    return objs


if __name__ == '__main__':
    sk.reset()
    os.makedirs(OUT, exist_ok=True)
    quick = 'quick' in sys.argv
    recs, tiles, labels, groups = [], [], [], {}
    for vid, name, fn, joint in VARIANTS:
        objs = fn(vid)
        groups[vid] = objs
        fbx = os.path.join(OUT, 'SM_SW13_%s.fbx' % name)
        sk.export_fbx(objs, fbx)
        recs.append(sk.variant_record(vid, name, objs, fbx, water_line_z=WATER, deck_top_z=TOP,
                    pivot='centre of the entry joint edge, z=0 = runner bottom (lowest point)',
                    joints=joint, solidity='walk-on-top', burns=False,
                    collider='mesh collider from the Bark+BarkMossy+Props logs/runners (or a box per log); '
                             'Plants/Moss no collider; the hole stays open' if vid == 'SW13d' else
                             'mesh collider from Bark+BarkMossy+Props (logs+runners); Plants and Moss no collider',
                    moving_parts='none', notes='raw logs - do not burn'))
        for tag, az, el in (('fq', 40, 34), ('rq', -140, 34)):
            p = os.path.join(OUT, '%s_%s.png' % (vid, tag))
            sk.render(objs, p, az=az, el=el, size=(400, 300), water_z=WATER, samples=8 if quick else 12, fov=26,
                      target=(0, 2, 0.15), dist=13.0)
            tiles.append(p); labels.append('%s %s %s' % (vid, name, tag))
    asm = assembled(groups)
    p = os.path.join(OUT, 'assembled_top.png')
    sk.render(asm, p, az=0, el=89, size=(400, 300), samples=8, fov=30)
    tiles.append(p); labels.append('grid check (top)')
    p = os.path.join(OUT, 'assembled_persp.png')
    sk.render(asm, p, az=-30, el=40, size=(400, 300), samples=8, fov=30, water_z=WATER)
    tiles.append(p); labels.append('grid check, in water')
    for o in asm:
        bpy.data.objects.remove(o)
    sk.sheet(tiles, os.path.join(OUT, 'card.jpg'), cols=4, labels=labels, concept=SHEET, concept_h=360,
             title='SW13 Gat (corduroy road) - concept on top; model renders below')
    for i, (vid, *_r) in enumerate(VARIANTS):
        pf.place(groups[vid], i * 5.0)
    json.dump(recs, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)
    sk.save_blend(os.path.join(OUT, 'SW13_corduroy_road.blend'))
    for r in recs:
        print(r['id'], r['tris'], r['size_xyz_m'], r['tris_by_material'])
