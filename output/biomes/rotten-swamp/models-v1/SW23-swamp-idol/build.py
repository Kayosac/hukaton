"""SW23 Swamp idol (SM_SW23_SwampIdol): crooked 6-sided carved post of 3 bent segments, splintered top,
carved face (real nose/brow, recessed eyes & mouth) on the front (-Y) only, moss drape, sedge/cattails at the foot."""
import sys, os, math, random, json
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, '/home/claude/swamp/kit'); sys.path.insert(0, '/home/claude/swamp/items/SW20-drowned-watermill')
import sk, mbi
from mbi import V, board, strip, oface
import bpy
import numpy as np
from mathutils import Matrix

OUT = os.path.join(HERE, 'out')
SHEET = '/home/claude/ref/swamp/sheets/23-swamp-idol.png'
os.makedirs(OUT, exist_ok=True)
SIDES = 6
ANG = [0, 62, 118, 180, 226, 314]     # wide front facet (226..314 deg) for the face


def frame_rings(path, radii, scale, rot=0.0, jit=0.0, seed=0):
    """Rings of a 6-sided post along path; x axis kept ~world X so facet 4 (240..300 deg) faces -Y."""
    rnd = random.Random(seed)
    P = [V(p) for p in path]; n = len(P)
    rings = []
    for i in range(n):
        t = (P[min(i + 1, n - 1)] - P[max(i - 1, 0)]).normalized()
        x = V((1, 0, 0)); x = (x - t * x.dot(t)).normalized()
        y = t.cross(x)
        sx, sy = scale[i]
        ring = []
        for k in range(SIDES):
            a = rot + math.radians(ANG[k])
            p = P[i] + x * math.cos(a) * radii[i] * sx + y * math.sin(a) * radii[i] * sy
            ring.append(p + V((rnd.uniform(-jit, jit), rnd.uniform(-jit, jit), rnd.uniform(-jit, jit) * .5)))
        rings.append(ring)
    return rings


def skin_rings(m, rings, path, skip=None, uvoff=(0, 0)):
    """Side quads between rings (tiling UV, grain along the post). skip=(facet, i_from, i_to): facet left open."""
    t = m.tile()
    L = [0.0]
    for i in range(1, len(path)):
        L.append(L[-1] + (V(path[i]) - V(path[i - 1])).length)
    for i in range(len(rings) - 1):
        c = (V(path[i]) + V(path[i + 1])) / 2
        acc = 0.0
        for k in range(SIDES):
            k2 = (k + 1) % SIDES
            w = (rings[i][k] - rings[i][k2]).length
            if skip and k == skip[0] and skip[1] <= i < skip[2]:
                acc += w; continue
            pts = [rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k]]
            u0, u1 = acc / t + uvoff[0], (acc + w) / t + uvoff[0]
            v0, v1 = L[i] / t + uvoff[1], L[i + 1] / t + uvoff[1]
            oface(m, pts, [(u0, v0), (u1, v0), (u1, v1), (u0, v1)], sum(pts, V()) / 4 - c)
            acc += w


def cap(m, ring, want):
    t = m.tile()
    c = sum(ring, V()) / len(ring)
    oface(m, ring, [((p - c).x / t, (p - c).y / t) for p in ring], want)


# stave boundaries (deg): a wide front stave 226..314 (-Y) carries the face; 5 more staves around
STAVES = [226, 314, 368, 426, 482, 534]
TOPZ = 2.13
CRACKS = (.78, 1.33)
FACE0 = 1.36
KEYS = [(-.12, (0, 0)), (.25, (0, .01)), (.55, (.06, .03)), (.78, (.11, .03)), (1.0, (.05, 0)), (1.18, (-.04, -.04)),
        (1.33, (-.08, -.06)), (1.55, (-.05, -.075)), (1.85, (.03, -.065)), (TOPZ, (.12, -.025))]


def centre(z):
    for (z0, c0), (z1, c1) in zip(KEYS, KEYS[1:]):
        if z <= z1:
            f = (z - z0) / (z1 - z0)
            return V((c0[0] + (c1[0] - c0[0]) * f, c0[1] + (c1[1] - c0[1]) * f, z))
    return V((KEYS[-1][1][0], KEYS[-1][1][1], z))


def stave_ring(z, r, sx, sy, groove=.85, shrink=1.0, bulge=None, jit=0.0, rnd=None):
    """18-vertex ring: per stave (outer start, outer end, groove)."""
    c = centre(z)
    t = (centre(min(z + .05, TOPZ)) - centre(max(z - .05, -.12))).normalized()
    x = V((1, 0, 0)); x = (x - t * x.dot(t)).normalized(); y = t.cross(x)
    e = math.radians(5)
    ring = []
    for k in range(len(STAVES)):
        a0 = math.radians(STAVES[k]); a1 = math.radians(STAVES[(k + 1) % len(STAVES)] + (360 if k == len(STAVES) - 1 else 0))
        rk = r * shrink * (bulge[k] if bulge else 1)
        for (ang, rr) in ((a0 + e, rk), (a1 - e, rk), (a1, r * shrink * groove)):
            p = c + x * math.cos(ang) * rr * sx + y * math.sin(ang) * rr * sy
            if jit and rnd:
                p += V((rnd.uniform(-jit, jit), rnd.uniform(-jit, jit), 0))
            ring.append(p)
    return ring, t


def build_idol():
    rnd = random.Random(23)
    pl = sk.Mesh('Planks'); props = sk.Mesh('Props'); bark = sk.Mesh('Bark'); mud = sk.Mesh('Mud')
    plants = sk.Mesh('Plants'); moss = sk.Mesh('Moss')
    NS = len(STAVES) * 3
    bulge = [1.0, 1.03, .98, 1.04, .97, 1.02]
    # ring stations: (z, radius, sx, crack?)
    zs = [-.12, .12, .35, .58, .76, ('c', .78), .80, 1.0, 1.17, 1.31, ('c', 1.33), FACE0, 1.56, 1.76, 1.95, TOPZ]
    def rad(z):
        if z < .3: return .31 - (z + .12) / .42 * .07
        if z < 1.33: return .24 - (z - .3) * .015
        return .235
    def sxf(z):
        return 1.08 + .1 * max(0, min(1, (z - .3) / 1.1))
    rings = []; Zs = []
    for st in zs:
        crack = isinstance(st, tuple)
        z = st[1] if crack else st
        ring, _ = stave_ring(z, rad(z), sxf(z), 1.0, shrink=.88 if crack else 1.0, bulge=bulge, jit=0 if z >= FACE0 else .008, rnd=rnd)
        rings.append(ring); Zs.append(z)
    fi0 = Zs.index(FACE0)
    t = pl.tile()
    L = [0.0]
    for i in range(1, len(Zs)):
        L.append(L[-1] + (centre(Zs[i]) - centre(Zs[i - 1])).length)
    for i in range(len(rings) - 1):
        c = (centre(Zs[i]) + centre(Zs[i + 1])) / 2
        acc = 0.0
        for j in range(NS):
            j2 = (j + 1) % NS
            w = (rings[i][j] - rings[i][j2]).length
            if j == 0 and i >= fi0:
                acc += w; continue
            pts = [rings[i][j], rings[i][j2], rings[i + 1][j2], rings[i + 1][j]]
            oface(pl, pts, [(acc / t, L[i] / t), ((acc + w) / t, L[i] / t), ((acc + w) / t, L[i + 1] / t), (acc / t, L[i + 1] / t)],
                  sum(pts, V()) / 4 - c)
            acc += w
    cap(pl, rings[0], V((0, 0, -1)))
    top = rings[-1]
    tdir = (centre(TOPZ) - centre(TOPZ - .15)).normalized()
    cT = sum(top, V()) / len(top)
    cap(pl, top, tdir)
    # ------------------------------------------------ the face on the front stave (Props idol_face): brow block, deep eye slits, long wedge nose
    R = rings[fi0:]
    def F(u, vt):
        v = 1 - vt
        n = len(R) - 1
        f = v * n; i = min(int(f), n - 1); w = f - i
        L0 = R[i][0].lerp(R[i + 1][0], w); R0 = R[i][1].lerp(R[i + 1][1], w)
        return L0.lerp(R0, u)
    nrm = (R[0][1] - R[0][0]).cross(R[-1][0] - R[0][0]).normalized()
    if nrm.y > 0:
        nrm = -nrm
    u0r, v0r, u1r, v1r = props.region('idol_face')
    def UV(u, vt):
        return (u0r + u * (u1r - u0r), v0r + (1 - vt) * (v1r - v0r))
    us = [0, .05, .15, .41, .6, .85, .95, 1]
    vs = [0, .125, .225, .34, .39, .55, .81, 1]
    rec = [((.15, .41), (.34, .39), .08), ((.6, .85), (.34, .39), .08)]
    def depth(i, j):
        if not (0 <= i < len(us) - 1 and 0 <= j < len(vs) - 1):
            return 0.0
        cu, cv = (us[i] + us[i + 1]) / 2, (vs[j] + vs[j + 1]) / 2
        for (ua, ub), (va, vb), d in rec:
            if ua < cu < ub and va < cv < vb:
                return d
        return 0.0
    for i in range(len(us) - 1):
        for j in range(len(vs) - 1):
            d = depth(i, j)
            corners = [(us[i], vs[j + 1]), (us[i + 1], vs[j + 1]), (us[i + 1], vs[j]), (us[i], vs[j])]
            pts = [F(u, v) - nrm * d for (u, v) in corners]
            oface(props, pts, [UV(u, v) for (u, v) in corners], nrm)
            if d > 0:
                cc = sum(pts, V()) / 4
                for (ea, eb, di, dj) in ((0, 1, 0, 1), (1, 2, 1, 0), (2, 3, 0, -1), (3, 0, -1, 0)):
                    nd = depth(i + di, j + dj)
                    if abs(nd - d) < 1e-6:
                        continue
                    (ua, va), (ub, vb) = corners[ea], corners[eb]
                    pa, pb = F(ua, va), F(ub, vb)
                    wpts = [pa - nrm * nd, pb - nrm * nd, pb - nrm * d, pa - nrm * d]
                    du = .01
                    oface(props, wpts, [UV(ua, va), UV(ub, vb), UV(ub + du, vb + du), UV(ua + du, va + du)], cc - (pa + pb) / 2)
    down = (F(.5, 1) - F(.5, 0)).normalized()
    side = (F(1, .5) - F(0, .5)).normalized()
    # long wedge nose (narrow at the brow, deep at the tip)
    nb = [(.4, .33), (.62, .33), (.66, .69), (.36, .69)]
    B = [F(u, v) for (u, v) in nb]
    T = [B[0] + nrm * .07, B[1] + nrm * .07, B[2] + nrm * .24 + down * .04, B[3] + nrm * .24 + down * .04]
    T[0], T[1] = T[0].lerp(T[1], .15), T[1].lerp(T[0], .15)
    oface(props, T, [UV(u, v) for (u, v) in nb], nrm)
    oface(props, [B[0], T[0], T[3], B[3]], [UV(.34, .33), UV(.4, .33), UV(.38, .69), UV(.32, .69)], -side)
    oface(props, [B[1], T[1], T[2], B[2]], [UV(.68, .33), UV(.62, .33), UV(.64, .69), UV(.7, .69)], side)
    oface(props, [B[3], B[2], T[2], T[3]], [UV(.36, .69), UV(.66, .69), UV(.66, .74), UV(.36, .74)], down)
    oface(props, [B[0], B[1], T[1], T[0]], [UV(.4, .3), UV(.62, .3), UV(.62, .33), UV(.4, .33)], -down)
    # heavy flat brow block
    bb = [(.02, .215), (.98, .215), (.98, .335), (.02, .335)]
    B = [F(u, v) for (u, v) in bb]
    T = [B[0] + nrm * .11 - down * .01, B[1] + nrm * .11 - down * .01, B[2] + nrm * .12, B[3] + nrm * .12]
    oface(props, T, [UV(u, v) for (u, v) in bb], nrm)
    oface(props, [B[0], B[1], T[1], T[0]], [UV(.02, .17), UV(.98, .17), UV(.98, .215), UV(.02, .215)], -down)
    oface(props, [B[3], B[2], T[2], T[3]], [UV(.02, .335), UV(.98, .335), UV(.98, .37), UV(.02, .37)], down)
    oface(props, [B[0], T[0], T[3], B[3]], [UV(.0, .215), UV(.02, .215), UV(.02, .335), UV(.0, .335)], -side)
    oface(props, [B[1], T[1], T[2], B[2]], [UV(1, .215), UV(.98, .215), UV(.98, .335), UV(1, .335)], side)
    # ------------------------------------------------ the top splits into tall splintered stave fingers of different heights
    heights = [.3, .52, .36, .6, .28, .46]
    for k in range(len(STAVES)):
        p0, p1 = top[3 * k], top[3 * k + 1]
        mid = (p0 + p1) / 2
        out = (mid - cT); out = (out - tdir * out.dot(tdir)).normalized()
        base = mid - out * .045
        wv = (p1 - p0); w = wv.length * .86
        h = heights[k]
        tip = base + tdir * h + out * rnd.uniform(.02, .08) + wv.normalized() * rnd.uniform(-.03, .03)
        board(pl, base - tdir * .08, tip, wv, w, .085, seed=40 + k, ends=('flat', 'broken'), segs=2)
        if k in (1, 3, 5):    # a thinner split-off sliver beside some fingers
            sl = base - out * .06 + wv.normalized() * w * .3
            board(pl, sl - tdir * .05, sl + tdir * h * .7 + out * .03, wv, w * .35, .05, seed=60 + k, ends=('flat', 'broken'))
    # ------------------------------------------------ draped moss: ragged skins lying on the staves (1 cm offset) at the cracks
    # (shoulders) and spilling down from the head top, with drip cards from the ragged lower edge. Never over the face.
    def surf(j, sp, z):
        z = max(Zs[0], min(Zs[-1], z))
        i = 0
        while i < len(Zs) - 2 and Zs[i + 1] < z:
            i += 1
        f = (z - Zs[i]) / max(Zs[i + 1] - Zs[i], 1e-6)
        j2 = (j + 1) % NS
        lo = rings[i][j].lerp(rings[i][j2], sp); hi = rings[i + 1][j].lerp(rings[i + 1][j2], sp)
        p = lo.lerp(hi, f)
        c = centre(z)
        d = V((p.x - c.x, p.y - c.y, 0))
        return p + (d.normalized() * .012 if d.length > 1e-6 else V())
    tm = sk.MATS['Moss'][2]
    def drape(j, z_top, z_mid, drop, seed, drips=True):
        """Moss patch on stave segment j: from z_top down through z_mid (crack) to a ragged lower edge."""
        rr = random.Random(seed)
        NC = 3
        cols = [k / NC for k in range(NC + 1)]
        bots = [z_mid - drop * rr.uniform(.35, 1.0) for _ in cols]
        # ragged side ends: shrink the top/bottom at the outer columns a little
        rows = lambda k: [z_top - (rr.uniform(0, .03) if k in (0, NC) else 0), z_mid + .02, z_mid, z_mid - .02,
                          (z_mid - .02 + bots[k]) / 2, bots[k]]
        grid = [[surf(j, sp, z) for z in rows(k)] for k, sp in enumerate(cols)]
        for k in range(NC):
            for r in range(len(grid[0]) - 1):
                pts = [grid[k][r], grid[k + 1][r], grid[k + 1][r + 1], grid[k][r + 1]]
                cc = sum(pts, V()) / 4; c = centre(cc.z)
                want = V((cc.x - c.x, cc.y - c.y, 0))
                ax = V((0, 0, 1)); u = want.normalized().cross(ax)
                oface(moss, pts, [(p.dot(u) / tm, p.z / tm) for p in pts], want)
        if drips:
            for k in range(NC):
                if rr.random() < .55:
                    p0, p1 = grid[k][-1], grid[k + 1][-1]
                    mid = (p0 + p1) / 2; c = centre(mid.z)
                    out = V((mid.x - c.x, mid.y - c.y, 0)).normalized()
                    plants.card(mid + out * .006 + V((0, 0, .02)), (p1 - p0) / 2 * 1.1, V((0, 0, rr.uniform(.14, .3))),
                                'moss_drip_%d' % rr.randint(0, 1), hang=True, wind_flutter=.01)
    front = {17, 0, 1}            # segments of the face stave and its grooves
    for j in range(NS):
        # lower crack: around most of the post
        if rnd.random() < .7:
            drape(j, CRACKS[0] + rnd.uniform(.06, .16), CRACKS[0], rnd.uniform(.12, .3), 100 + j)
        # upper crack (face sits just above it): front segments only get the part below the crack
        if rnd.random() < .75:
            if j in front:
                drape(j, CRACKS[1] - .005, CRACKS[1] - .02, rnd.uniform(.08, .2), 200 + j)
            else:
                drape(j, CRACKS[1] + rnd.uniform(.08, .2), CRACKS[1], rnd.uniform(.12, .32), 200 + j)
        # head top: spill down the back and sides
        if j not in front and rnd.random() < .85:
            zt = TOPZ - .005
            drape(j, zt, zt - .04, rnd.uniform(.15, .4), 300 + j)
    # ------------------------------------------------ base: thin low peat/moss mound, fallen stick
    rings = []
    NM = 14
    wob = [rnd.uniform(.75, 1.25) for _ in range(NM)]
    for (rr, z) in ((.33, .09), (.5, .05), (.66, -.06)):
        ring = []
        for k in range(NM):
            a = 2 * math.pi * k / NM
            q = rr * (wob[k] if rr > .4 else 1) * rnd.uniform(.92, 1.08)
            ring.append(V((math.cos(a) * q * 1.1, math.sin(a) * q, z + rnd.uniform(-.015, .015))))
        rings.append(ring)
    t = sk.MATS['Mud'][2]
    for i in range(len(rings) - 1):
        for k in range(NM):
            k2 = (k + 1) % NM
            pts = [rings[i][k], rings[i + 1][k], rings[i + 1][k2], rings[i][k2]]
            oface(mud, pts, [(p.x / t, p.y / t) for p in pts], (0, 0, 1))
    oface(mud, rings[0], [(p.x / t, p.y / t) for p in rings[0]], (0, 0, 1))
    stick = [V((-.6, -.3, .03)), V((-.2, -.38, .1)), V((.22, -.48, .07))]
    rr_ = bark.tube(stick, [.045, .05, .04], sides=5, jitter=.008, seed=5, caps=(False, False))
    props.cap(list(reversed(rr_[0])), region='end_grain_1'); props.cap(rr_[-1], region='end_grain_0')
    bark.tube([V((-.25, -.37, .1)), V((-.17, -.52, .19))], [.02, .008], sides=4, seed=6)
    # sedge / cattail tufts at the foot
    for k, (x, y, s, reg) in enumerate(((-.45, .05, .7, 'sedge_0'), (.47, -.12, .65, 'sedge_1'), (.3, .38, .55, 'sedge_2'),
                                        (-.28, .42, .5, 'sedge_1'), (-.02, -.55, .35, 'grass_low'), (.52, .25, 1.0, 'cattail_leaves_0'),
                                        (-.5, -.35, .45, 'sedge_2'))):
        plants.cross_cards((x, y, .02), .55 * s + .1, s, reg, n=3, seed=k, wind_sway=.06, wind_flutter=.02)
    for k, (x, y, h) in enumerate(((.52, .22, 1.05), (.6, .3, .9))):
        stem = [V((x, y, 0)), V((x + .02, y + .01, h * .5)), V((x + .05 + k * .03, y, h))]
        mbi.rtube(props, stem, .008, 3, 'small_wood', caps=(False, False))
        tp = stem[-1]; dd = (stem[-1] - stem[-2]).normalized()
        mbi.rtube(props, [tp - dd * .02, tp + dd * .17], [.026, .024], 6, 'cattail_cob')
        mbi.rtube(props, [tp + dd * .17, tp + dd * .26], .005, 3, 'small_wood', caps=(False, True))
    po = pl.to_object('SW23a_Planks')
    mo = mbi.skin(po, 'm1', lambda c, n: n.z > .45 and c.z > TOPZ - .05, keep=.8, seed=2, offset=.01)
    mo3 = mbi.skin(po, 'm3', lambda c, n: n.y > .2 and c.z < .7, keep=.3, seed=5, offset=.008)
    mud_o = mud.to_object('SW23a_Mud')
    mo2 = mbi.skin(mud_o, 'm2', lambda c, n: True, keep=.45, seed=3, offset=.008)
    mc = moss.to_object('mc')
    return [po, bark.to_object('SW23a_Bark'), props.to_object('SW23a_Props'), mud_o,
            mbi.merge([mo, mo2, mo3, mc], 'SW23a_Moss'), plants.to_object('SW23a_Plants')]


def main(render=True):
    sk.reset()
    objs = build_idol()
    sk.ground(objs)
    # pivot at the post base centre (ground() centres XY on the whole bounds incl. plants)
    post = objs[0]
    pv = np.array([v.co[:] for v in post.data.vertices])
    base = pv[pv[:, 2] < pv[:, 2].min() + .25]
    off = V((-float(base[:, 0].mean()), -float(base[:, 1].mean()), 0))
    for o in objs:
        o.data.transform(Matrix.Translation(off))
    fbx = os.path.join(OUT, 'SM_SW23_SwampIdol.fbx')
    sk.export_fbx(objs, fbx)
    lo, hi = sk.bounds([post])
    rec = sk.variant_record('SW23a', 'SwampIdol', objs, fbx, water_line_z=None, ground_line_z=0.12, solidity='solid', burns=False,
                            post_size_xyz_m=[round(float(hi[i] - lo[i]), 3) for i in range(3)],
                            collider='post: 3 capsules or tilted boxes following the bends (SW23a_Planks); mound (Mud) is a thin skin at '
                                     'ground level - no collider; plants, moss, drips, stick: no collider',
                            moving_parts='none',
                            notes='Pivot = bottom of the post (0.12 m of the post is buried: put ground_line_z at terrain level). Face '
                                  '(Props idol_face) only on the front facet (-Y): continuous 6-stave post (real grooves between staves, horizontal cracks), heavy brow block, long wedge nose, recessed eye slits (no mouth, as the repainted texture). '
                                  'The thin mound + plants at the foot widen the overall bounds; the post alone is post_size_xyz_m.')
    json.dump([rec], open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)
    mbi.check_orientation(objs)
    sk.save_blend(os.path.join(OUT, 'SW23.blend'))
    print('TRIS', sk.tris(objs), rec['size_xyz_m'], rec['post_size_xyz_m'])
    if render:
        R = os.path.join(HERE, 'work'); os.makedirs(R, exist_ok=True); tiles = []
        views = (('front', 0, 6), ('3/4 right', 35, 8), ('side right', 90, 6), ('back 3/4', 150, 10))
        for i, (lab, az, el) in enumerate(views):
            p = os.path.join(R, 'r%d.png' % i)
            sk.render(objs, p, az=az, el=el, size=(300, 460), samples=10, margin=1.0, fov=22)
            tiles.append(p)
        sk.sheet(tiles, os.path.join(OUT, 'card.jpg'), 4, labels=[v[0] for v in views], concept=SHEET, title='SW23 Swamp idol')


if __name__ == '__main__':
    main(render='norender' not in sys.argv)
