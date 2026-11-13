"""SW21 Sunken flat-bottom boats: a = narrow, pointed bow (SM_SW21_SunkenBoatNarrow);
b = wide, blunt raked bow (SM_SW21_SunkenBoatWide). Length along Y, bow towards -Y (front)."""
import sys, os, math, random, json
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, '/home/claude/swamp/kit'); sys.path.insert(0, '/home/claude/swamp/items/SW20-drowned-watermill')
import sk, mbi
from mbi import V, board, strip, oface
import bpy
from mathutils import Matrix

OUT = os.path.join(HERE, 'out')
SHEET = '/home/claude/ref/swamp/sheets/21-sunken-boats.png'
os.makedirs(OUT, exist_ok=True)
TH = .06          # plank thickness


class Hull:
    def __init__(self, L, hb, ht, zs_mid, bow, rake=0.0, bow_rise=.16, sheer_rise=.17, stern_w=.85):
        self.L, self.hb0, self.ht0, self.zs0, self.bow = L, hb, ht, zs_mid, bow
        self.rake, self.bow_rise, self.sheer_rise, self.stern_w = rake, bow_rise, sheer_rise, stern_w

    def t(self, y):
        return min(1, max(0, (y + self.L / 2) / self.L))

    def shape(self, t, top):
        if self.bow == 'pointed':
            f = min(1, t / .5) ** (.8 if top else .95)
        else:
            k = min(1, t / .32)
            f = (.5 if top else .42) + (.5 if top else .58) * (1 - (1 - k) ** 2)
        if t > .7:
            f *= 1 - (1 - self.stern_w) * ((t - .7) / .3) ** 1.5
        return f

    def hb(self, y):
        return self.hb0 * self.shape(self.t(y), False)

    def ht(self, y):
        return self.ht0 * self.shape(self.t(y), True)

    def zb(self, y):
        t = self.t(y)
        r = self.bow_rise * max(0, 1 - t / .4) ** 2
        return r + .03 * max(0, (t - .85) / .15)

    def zs(self, y):
        t = self.t(y)
        return self.zs0 + self.sheer_rise * max(0, 1 - t / .45) ** 2 + .04 * t

    def y_bow(self, hn):
        return -self.L / 2 - self.rake * hn

    def side(self, y, hn, sd, out=0.0):
        hb, ht = self.hb(y), self.ht(y)
        x = hb + (ht - hb) * hn
        z = self.zb(y) + (self.zs(y) - self.zb(y)) * hn
        return V((sd * (x + out), y, z))


def build_boat(vid, H, hole_t, seed, tilt):
    rnd = random.Random(seed)
    pl = sk.Mesh('Planks'); props = sk.Mesh('Props'); plants = sk.Mesh('Plants')
    L = H.L
    Hm = H.zs0                         # metres per unit hn (for UV)
    y_st = L / 2 - .005                # stern end of strakes
    # ------------------------------------------------ side strakes (3 per side, clinker overlap), hole on +X side
    bands = [(0, .37), (.33, .69), (.65, 1.0)]
    for sd in (-1, 1):
        for k, (a, b) in enumerate(bands):
            out = .012 * k
            def P(s, h, sd=sd, out=out, a=a, b=b):
                hn = h / Hm
                kick = .012 * (1 - (hn - a) / (b - a))       # lower edge kicks out (clinker)
                return H.side(s, hn, sd, out + kick)
            Nf = lambda s, h, sd=sd: V((-sd, 0, 0))
            n = 12
            def stations(s0, s1, lo_fn=None, hi_fn=None):
                st = []
                for q in range(n + 1):
                    s = s0 + (s1 - s0) * q / n
                    lo = a * Hm if lo_fn is None else lo_fn(s)
                    hi = b * Hm if hi_fn is None else hi_fn(s)
                    st.append(((s, lo), (s, hi)))
                return st
            # bow end: per hn the strake starts at y_bow(hn) (raked bow) / or 0-width point (pointed)
            def mk(s0, s1, lo_fn=None, hi_fn=None, e0=None, e1=None):
                st = stations(s0, s1, lo_fn, hi_fn)
                if H.rake and s0 <= -L / 2 + .01:
                    (l0, h0) = st[0]
                    st[0] = ((H.y_bow(l0[1] / Hm) + .01, l0[1]), (H.y_bow(h0[1] / Hm) + .01, h0[1]))
                if e1 is not None:     # broken end at s1
                    (l, h) = st[-1]
                    r = random.Random(e1)
                    st[-1] = ((l[0] - r.uniform(.05, .14), l[1] + .01), (h[0] - r.uniform(.0, .08), h[1]))
                    st.append(((l[0] + r.uniform(-.02, .05), (l[1] + h[1]) / 2 - .02), (h[0] + r.uniform(.05, .12), h[1] - .02)))
                if e0 is not None:
                    (l, h) = st[0]
                    r = random.Random(e0)
                    st[0] = ((l[0] + r.uniform(.04, .12), l[1]), (h[0] + r.uniform(.0, .06), h[1] - .01))
                    st.insert(0, ((l[0] - r.uniform(.03, .1), l[1] + .02), (h[0] + r.uniform(-.06, .0), (l[1] + h[1]) / 2 + .02)))
                strip(pl, st, P, Nf, TH, uvoff=(rnd.random(), rnd.random()))
            yb0 = -L / 2 + (.0 if H.bow == 'pointed' else .0)
            ys0 = H.y_bow(0) + .005 if H.rake else -L / 2 + .02
            if sd == 1:
                ya, yb = -L / 2 + hole_t[0] * L, -L / 2 + hole_t[1] * L
                ym = (ya + yb) / 2
                hw = (yb - ya) / 2
                if k == 1:          # middle strake broken through (splintered ends)
                    mk(ys0, ya, e1=seed + 1)
                    mk(yb, y_st, e0=seed + 2)
                    continue
                if k == 2:          # upper strake: broken through in the middle of the hole, bitten at its sides
                    def lo(s, ym=ym, hw=hw, a=a):
                        f = max(0, 1 - abs((s - ym + .05) / (hw + .02)))
                        return a * Hm + .2 * f ** .35 + (.05 if f > .2 and int(s * 29) % 2 else 0)
                    mk(ys0, ym - hw * .45, lo_fn=lo, e1=seed + 3)
                    mk(ym + hw * .35, y_st, lo_fn=lo, e0=seed + 4)
                    continue
                if k == 0:          # lower strake: top edge bitten
                    def hi(s, ym=ym, hw=hw, b=b):
                        f = max(0, 1 - abs((s - ym - .04) / (hw - .02)))
                        return b * Hm - (.15 * f ** .4 + (.04 if f > .2 and int(s * 23) % 2 else 0))
                    mk(ys0, y_st, hi_fn=hi); continue
            mk(ys0, y_st)
    # broken plank hanging out of the hole (bent outwards)
    yh = -L / 2 + (hole_t[0] + hole_t[1]) / 2 * L
    hl = .5 * (hole_t[1] - hole_t[0]) * L
    p0 = H.side(yh + hl + .08, .5, 1, .04); p1 = H.side(yh + .05, .62, 1, .2)
    board(pl, p0, p1, (0, 0, 1), .14, .045, seed=seed + 5, ends=('flat', 'broken'))
    p0 = H.side(yh - hl - .1, .45, 1, .04); p1 = H.side(yh - hl * .35, .3, 1, .14)
    board(pl, p0, p1, (0, 0, 1), .12, .04, seed=seed + 6, ends=('flat', 'broken'))
    # ------------------------------------------------ bottom boards (along Y)
    nb = 4 if H.hb0 < .45 else 5
    yb_start = -L / 2 + (.05 if H.bow == 'pointed' else .01)
    for j in range(nb):
        f0, f1 = j / nb, (j + 1) / nb - .01
        def P(s, h, f0=f0, f1=f1):
            w = H.hb(s) + .03
            x = -w + 2 * w * (f0 + (f1 - f0) * h / .3)
            return V((x, s, H.zb(s) + .005))
        st = [((yb_start + (L / 2 - yb_start) * q / 10, 0), (yb_start + (L / 2 - yb_start) * q / 10, .3)) for q in range(11)]
        strip(pl, st, P, lambda s, h: V((0, 0, -1)), .045, uvoff=(rnd.random(), rnd.random()))
    # ------------------------------------------------ transom(s)
    def transom(yfn, nrm, hn0, hn1, ystation, sd_seed, bites=False):
        for k, (a, b) in enumerate(((hn0, (hn0 + hn1) / 2 + .02), ((hn0 + hn1) / 2 - .01, hn1))):
            za = H.zb(ystation) + (H.zs(ystation) - H.zb(ystation)) * a
            zb_ = H.zb(ystation) + (H.zs(ystation) - H.zb(ystation)) * b
            def xw(z):
                hn = (z - H.zb(ystation)) / (H.zs(ystation) - H.zb(ystation))
                return H.hb(ystation) + (H.ht(ystation) - H.hb(ystation)) * hn + .03
            P = lambda s, h: V((s, yfn(h), h))
            st = [((-xw(za), za), (-xw(zb_), zb_)), ((0, za - .01), (0, zb_ + rnd.uniform(-.015, .01))), ((xw(za), za), (xw(zb_), zb_))]
            strip(pl, st, P, lambda s, h: V(nrm), TH, uvoff=(rnd.random(), rnd.random()), swap=True)
    transom(lambda h: L / 2 + .002, (0, -1, 0), -.05, 1.04, L / 2 - .01, 1)
    if H.bow != 'pointed':
        zb0 = H.zb(-L / 2)
        transom(lambda h: H.y_bow((h - zb0) / (H.zs(-L / 2) - zb0)) - .003, (0, 1, 0), -.03, 1.03, -L / 2 + .01, 2)
    else:
        # stem post at the pointed bow
        a = V((0, -L / 2 - .02, H.zb(-L / 2) - .03)); b = V((0, -L / 2 - .05, H.zs(-L / 2) + .05))
        board(pl, a, b, (1, 0, 0), .075, .08, seed=seed + 9)
    # ------------------------------------------------ gunwales (rails along the sheer, both sides)
    for sd in (-1, 1):
        def P(s, h, sd=sd):
            p = H.side(s, 1.0, sd, .07)
            return p + V((-sd * h, 0, .05))
        y0 = (H.y_bow(1) if H.rake else -L / 2 + .03)
        st = [((y0 + (L / 2 + .02 - y0) * q / 12, 0), (y0 + (L / 2 + .02 - y0) * q / 12, .14)) for q in range(13)]
        if sd == 1 and seed % 2 == 0:     # broken rail on the hole side (narrow boat)
            st = st[:8] + [((st[8][0][0] - .04, 0), (st[8][1][0] + .05, .14))]
            strip(pl, st, P, lambda s, h: V((0, 0, -1)), .085, uvoff=(rnd.random(), 0))
            st2 = [((st[7][0][0] + .32, 0), (st[7][1][0] + .26, .14))] + \
                  [((y0 + (L / 2 + .02 - y0) * q / 12, 0), (y0 + (L / 2 + .02 - y0) * q / 12, .14)) for q in range(10, 13)]
            strip(pl, st2, P, lambda s, h: V((0, 0, -1)), .085, uvoff=(rnd.random(), 0))
        else:
            strip(pl, st, P, lambda s, h: V((0, 0, -1)), .085, uvoff=(rnd.random(), 0))
    # ------------------------------------------------ ribs (frames) + floor timbers + risers
    nrib = 5 if L < 3.5 else 7
    for i in range(nrib):
        t = .16 + .7 * i / (nrib - 1)
        y = -L / 2 + t * L
        for sd in (-1, 1):
            a = H.side(y, .04, sd, -TH - .022); b = H.side(y, .93, sd, -TH - .022)
            board(pl, a, b, (0, 1, 0), .08, .05, seed=seed + 20 + i * 2 + sd)
        board(pl, V((-H.hb(y) + .02, y, H.zb(y) + .07)), V((H.hb(y) - .02, y, H.zb(y) + .07)), (0, 1, 0), .07, .045, seed=seed + 40 + i)
    for sd in (-1, 1):
        ya_, yb_ = -L / 2 + .2 * L, -L / 2 + .9 * L
        def P(s, h, sd=sd):
            return H.side(s, .86, sd, -TH - .01) + V((0, 0, -h))
        st = [((ya_ + (yb_ - ya_) * q / 8, 0), (ya_ + (yb_ - ya_) * q / 8, .06)) for q in range(9)]
        strip(pl, st, P, lambda s, h, sd=sd: V((-sd, 0, 0)), .035, uvoff=(rnd.random(), 0))
    # ------------------------------------------------ thwarts (2 benches), ends stick out over the gunwale
    thw = (.36, .7) if H.bow == 'pointed' else (.34, .68)
    for i, t in enumerate(thw):
        y = -L / 2 + t * L
        z = H.zs(y) + .06
        w = H.ht(y) + .1
        board(pl, V((-w, y + rnd.uniform(-.02, .02), z)), V((w, y, z + rnd.uniform(-.015, .015))), (0, 1, 0), .26, .07, seed=seed + 60 + i,
              ends=('flat', 'broken' if (i == 1 and seed % 2) else 'flat'))
    # ------------------------------------------------ mooring rope at the bow (trails into the water)
    yb_ = (H.y_bow(1) if H.rake else -L / 2) - .02
    zt = H.zs(-L / 2) - .02
    rope = [V((0, yb_, zt)), V((.05, yb_ - .12, zt - .08)), V((.12, yb_ - .22, zt - .3)), V((.2, yb_ - .26, zt - .45))]
    mbi.rtube(props, rope, .022, 5, 'rope')
    mbi.rtube(props, [V((-.06, yb_ + .03, zt + .01)), V((.06, yb_ + .03, zt + .01))], .035, 5, 'rope')

    po = pl.to_object(vid + '_Planks')
    # tilt (half sunken: listing + stern down), before moss/plants so they stay vertical-ish
    M = Matrix.Rotation(math.radians(tilt[0]), 4, 'Y') @ Matrix.Rotation(math.radians(tilt[1]), 4, 'X')
    po.data.transform(M)
    pr = props.to_object(vid + '_Props'); pr.data.transform(M)
    def mpred(c, n):
        return n.z > .75 and abs(c.x) > .3 and c.z > .25
    mo = mbi.skin(po, vid + '_Moss', mpred, keep=.5, seed=seed, offset=.008)
    mo2 = mbi.skin(po, 'mb', lambda c, n: n.z > .8 and c.z < .2, keep=.25, seed=seed + 1, offset=.006)
    mo = mbi.merge([mo, mo2], vid + '_Moss')
    # hanging slime on the outer hull below the gunwale (cards)
    for i in range(34):
        sd = 1 if i % 2 else -1
        y = -L / 2 + rnd.uniform(.15, .9) * L
        if sd == 1 and hole_t[0] - .08 < (y + L / 2) / L < hole_t[1] + .08:
            continue
        p = M @ (H.side(y, 1.0, sd, .075 if i % 5 else -.07) + V((0, 0, .04)))
        plants.card(p, V((0, rnd.uniform(.16, .24), 0)), V((0, 0, rnd.uniform(.3, .5))), 'moss_drip_%d' % (i % 2), hang=True, wind_flutter=.01)
    return [po, pr, mo], plants, M


def finish(vid, objs, plants, H, M, wl_local, seed):
    """Ground, add duckweed inside at the water line, return objects + water line."""
    po = plants
    tmp = [o for o in objs if o]
    lo, hi = sk.bounds(tmp)
    # water line in the tilted (pre-ground) space: wl_local above the lowest point
    wz = lo[2] + wl_local
    rnd = random.Random(seed)
    L = H.L
    for i in range(3):
        y = -L / 2 + rnd.uniform(.3, .85) * L
        p = M @ V((0, y, 0))
        po.flat_card((p.x + rnd.uniform(-.1, .1), p.y, wz + .012), (min(.7, H.hb(y) * 1.6), .55), 'duckweed_%d' % rnd.randint(0, 3),
                     rot=rnd.uniform(0, 3.14), z_jitter=.005, wind_flutter=.005)
    pobj = po.to_object(vid + '_Plants')
    allo = tmp + [pobj]
    d = sk.ground(allo)
    return allo, round(wz + d.z, 3)


def main(render=True):
    sk.reset()
    Ha = Hull(3.0, .42, .56, .56, 'pointed', bow_rise=.13, sheer_rise=.18, stern_w=.82)
    Hb = Hull(3.4, .6, .74, .6, 'blunt', rake=.3, bow_rise=.15, sheer_rise=.14, stern_w=.86)
    va, pa, Ma = build_boat('SW21a', Ha, (.44, .74), 10, tilt=(-4, -2))
    va, wa = finish('SW21a', va, pa, Ha, Ma, .3, 10)
    vb, pb, Mb = build_boat('SW21b', Hb, (.46, .78), 21, tilt=(-4, -2))
    vb, wb = finish('SW21b', vb, pb, Hb, Mb, .32, 21)
    recs = []
    for vid, name, objs, wl in (('SW21a', 'SunkenBoatNarrow', va, wa), ('SW21b', 'SunkenBoatWide', vb, wb)):
        fbx = os.path.join(OUT, 'SM_SW21_%s.fbx' % name)
        sk.export_fbx(objs, fbx)
        recs.append(sk.variant_record(vid, name, objs, fbx, water_line_z=wl, solidity='solid', burns=True,
                                      collider='hull: 2-3 boxes (bottom + low sides) or a convex hull of SW%s_Planks without thwarts; '
                                               'rope, moss, drips, duckweed: no collider' % vid[2:],
                                      moving_parts='none', notes='Length along Y, bow to -Y. Already listing/stern-down (sunken); '
                                                                 'hole on the +X side. Duckweed cards inside sit at water_line_z.'))
        mbi.check_orientation(objs)
    json.dump(recs, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)
    for o in vb:
        o.location = (2.2, 0, 0)
    bpy.context.view_layer.update()
    sk.save_blend(os.path.join(OUT, 'SW21.blend'))
    for o in vb:
        o.location = (0, 0, 0)
    bpy.context.view_layer.update()
    print('TRIS', sk.tris(va), sk.tris(vb), [r['size_xyz_m'] for r in recs], wa, wb)
    if render:
        R = os.path.join(HERE, 'work')
        tiles = []; labels = []
        for vid, objs, wl in (('a', va, wa), ('b', vb, wb)):
            for j, (lab, az, el, wz) in enumerate((('3/4 bow', 55, 35, None), ('3/4 stern', 130, 35, None), ('side', 90, 6, None),
                                                   ('in water', 70, 25, wl))):
                p = os.path.join(R, '%s%d.png' % (vid, j))
                sk.render(objs, p, az=az, el=el, size=(480, 300), samples=10, water_z=wz, margin=1.0, fov=22)
                tiles.append(p); labels.append('SW21%s %s' % (vid, lab))
        sk.sheet(tiles, os.path.join(OUT, 'card.jpg'), 4, labels=labels, concept=SHEET, title='SW21 Sunken boats')


if __name__ == '__main__':
    main(render='norender' not in sys.argv)
