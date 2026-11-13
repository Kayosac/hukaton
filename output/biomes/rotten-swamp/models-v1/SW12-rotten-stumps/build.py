"""SW12 Пень в воде — rotten stumps, 2 variants. python3 build.py [render|norender|bf] [only-letters]"""
import sys, os, math, random, json
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, '/home/claude/swamp/items/SW10-uprooted-snags'); sys.path.insert(0, '/home/claude/swamp/kit')
import sk
from dwlib import *

OUT = os.path.join(HERE, 'out')
SHEET = '/home/claude/ref/swamp/sheets/12-rotten-stumps.png'
WATER = 0.0


def wedge_root(bark, start, ang, L, h, w, seed, end_z=-.18, rise=.0, sides=5):
    """Buttress root: triangular (ridge up) cross-section flowing from the trunk side down into the water."""
    rnd = random.Random(seed)
    S = V(start)
    d = V((math.cos(ang), math.sin(ang), 0)); side = V((-d.y, d.x, 0))
    ctrl = [S, S + d * L * .3 + V((0, 0, rise)) + side * rnd.uniform(-.03, .03),
            S + d * L * .65 + side * rnd.uniform(-.06, .06) + V((0, 0, (WATER + .04 - S.z) * .7)),
            S + d * L + side * rnd.uniform(-.05, .05) + V((0, 0, end_z - S.z))]
    path = bez(ctrl, 6)
    base = [(1.0, 0.0), (-.55, .9), (-.6, -.9)] if sides == 3 else [(1.0, 0.0), (.2, .9), (-.8, .75), (-.8, -.75), (.2, -.9)]
    profs = []
    for i in range(6):
        f = i / 5
        hh = h * (1 - .7 * f); ww = w * (1 - .55 * f)
        profs.append([(x * hh * (1 + rnd.uniform(-.1, .1)), y * ww * (1 + rnd.uniform(-.1, .1))) for x, y in base])
    rings, F = ptube(bark, path, profs, up_hint=(0, 0, 1))
    poly_cap(bark, rings[-1], F[-1][2])
    return path, rings


def hollow_trunk(bark, props, base, top, ro_bot, ro_top, thick, floor_z, sides, seed, hts, n=6, flare=1.25,
                 lean=(0, 0), buttress=None):
    """Real hollow: outer skin, inner skin (inward), end-grain rim joining them at a jagged top, dark floor."""
    rnd = random.Random(seed)
    B, T = V(base), V(top)
    path = [B.lerp(T, i / (n - 1)) + V((lean[0], lean[1], 0)) * math.sin(math.pi * i / (n - 1)) +
            (V((rnd.uniform(-.025, .025), rnd.uniform(-.025, .025), 0)) if 0 < i < n - 1 else V()) for i in range(n)]
    bp = prof(sides, 1, 1, jit=.1, seed=seed, ajit=.2)
    profs, iprofs = [], []
    for i in range(n):
        f = i / (n - 1)
        r = ro_bot + (ro_top - ro_bot) * f
        if i == 0:
            r *= flare
        profs.append([(x * r, y * r) for x, y in bp])
    if buttress:
        flare_profiles(profs, buttress[0], buttress[1])
    orings, oF = ptube(bark, path, profs, offs=crown_offsets(sides, hts, n - 1))
    # inner: from floor up, same frames orientation (vertical path), reversed order -> faces point inward
    ipath = [V((p.x, p.y, max(p.z, floor_z))) for p in path]
    first = max(i for i in range(n) if path[i].z <= floor_z) if path[0].z <= floor_z else 0
    ipath = [V((path[first].x, path[first].y, floor_z))] + path[first + 1:]
    iprofs = []
    for i in range(len(ipath)):
        f = (ipath[i].z - B.z) / (T.z - B.z)
        r = ro_bot + (ro_top - ro_bot) * f - thick
        iprofs.append([(x * r, y * r) for x, y in reversed(bp)])
    rh = list(reversed(hts))
    irings, iF = ptube(bark, ipath, iprofs, offs=crown_offsets(sides, rh, len(ipath) - 1), v0=.4)
    # rim: outer[k] <-> inner index (sides-1-k)
    o = orings[-1]; inn = irings[-1]
    for k in range(sides):
        k2 = (k + 1) % sides
        a, b = o[k], o[k2]
        c, d = inn[sides - 1 - k2], inn[sides - 1 - k]
        poly_cap(props, [a, b, c, d], oF[-1][2], region='end_grain_1')
    poly_cap(props, list(reversed(irings[0])), (0, 0, 1), region='dark')
    poly_cap(bark, orings[0], -oF[0][2])
    return orings, irings, oF


def fungus_on(fun, rings, i, k, frac=.5, out=.02, seed=0, n=3, size=1.0, up_bias=.6):
    """Mushroom cluster growing out of a trunk face (between ring i and i+1, edge k)."""
    N = len(rings[i]); k2 = (k + 1) % N
    p = rings[i][k].lerp(rings[i][k2], .5).lerp(rings[i + 1][k].lerp(rings[i + 1][k2], .5), frac)
    c = sum(rings[i], V()) / N
    o = p - c; o.z = 0; o.normalize()
    mushroom_cluster(fun, p - o * .03, o * (1 - up_bias) + V((0, 0, up_bias)), seed=seed, n=n, size=size)
    return p, o


def shelf_moss(mm, pl, centre, outdir, w, depth, z, seed):
    """Small moss pad under a mushroom cluster with drips."""
    o = V(outdir).normalized(); s = V((-o.y, o.x, 0))
    C = V(centre)
    pts = [C - s * w, C - s * w + o * depth, C + s * w + o * depth, C + s * w]
    pts = [V((p.x, p.y, z)) for p in pts]
    mm.face(pts, [(p.x / .8, p.y / .8) for p in pts])
    pl.card((pts[1] + pts[2]) / 2 + V((0, 0, .005)), (pts[2] - pts[1]) / 2, V((0, 0, .14)), 'moss_drip_0', hang=True,
            wind_flutter=.01)


# ------------------------------------------------------------------ A: low closed stump
def buttress_stump(bark, zs, r_top, sides, butt, seed, z_b0, lean=(0, 0), v0=0.0, crown=None, centre=(0, 0)):
    """One flaring mass: a crooked trunk whose lower rings widen into buttress ridges (butt: {k: strength}) that
    run out and down into the water. zs: ring heights bottom..top. Returns rings, frames, path."""
    rnd = random.Random(seed)
    n = len(zs)
    z_bot, z_top = zs[0], zs[-1]
    path = []
    for i, z in enumerate(zs):
        f = (z - z_bot) / (z_top - z_bot)
        path.append(V((centre[0] + lean[0] * f + (rnd.uniform(-.015, .015) if 0 < i < n - 1 else 0),
                       centre[1] + lean[1] * f + (rnd.uniform(-.015, .015) if 0 < i < n - 1 else 0), z)))
    base = prof(sides, 1, 1, jit=.08, seed=seed, ajit=.15)
    shift = {k: rnd.uniform(-.18, .18) for k in butt}
    profs = []
    for i, z in enumerate(zs):
        g = max(0.0, (z_b0 - z) / (z_b0 - z_bot))       # 0 above the buttress start, 1 at the bottom
        r = r_top * (1 + .18 * g ** .8 + rnd.uniform(-.02, .02))
        ring = []
        for k, (x, y) in enumerate(base):
            fct = 1.0
            ang_sh = 0.0
            for kb, B in butt.items():
                dk = min((k - kb) % sides, (kb - k) % sides)
                if dk == 0:
                    fct += B * g ** 1.3; ang_sh = shift[kb] * g
                elif dk == 1:
                    fct += .12 * B * g ** 1.6
            ca, sa = math.cos(ang_sh), math.sin(ang_sh)
            xx, yy = x * ca - y * sa, x * sa + y * ca
            ring.append((xx * r * fct, yy * r * fct))
        profs.append(ring)
    offs = crown_offsets(sides, crown, n - 1) if crown else None
    rings, F = ptube(bark, path, profs, offs=offs, v0=v0)
    poly_cap(bark, list(reversed(rings[0])), -F[0][2])
    return rings, F, path


def top_cracks(props, C, R, n, seed, z):
    """Radial dry cracks on a flat end-grain top (thin dark wedges just above the cap)."""
    rnd = random.Random(seed)
    a0 = rnd.uniform(0, 6.3)
    for j in range(n):
        a = a0 + 2 * math.pi * j / n + rnd.uniform(-.35, .35)
        d = V((math.cos(a), math.sin(a), 0)); s = V((-d.y, d.x, 0))
        r0, r1 = R * rnd.uniform(.1, .3), R * rnd.uniform(.85, .97)
        w = R * rnd.uniform(.03, .05)
        pts = [C + d * r0 + V((0, 0, z)), C + d * r1 - s * w + V((0, 0, z)), C + d * r1 + s * w + V((0, 0, z))]
        poly_cap(props, pts, (0, 0, 1), region='dark')


# ------------------------------------------------------------------ A: low closed stump
def build_a():
    vid = 'SW12a'
    bark, props, pl, fun, mm = sk.Mesh('Bark'), sk.Mesh('Props'), sk.Mesh('Plants'), sk.Mesh('Props'), sk.Mesh('Moss')
    sides = 12
    zs = [-.22, -.1, .02, .12, .22, .32, .4]
    butt = {0: .95, 3: .7, 5: 1.0, 8: .65, 10: .85}
    rings, F, path = buttress_stump(bark, zs, .3, sides, butt, 61, z_b0=.34, lean=(.02, .01), v0=.2)
    # chamfered rim (moss drapes over it) and a flat end-grain top with radial cracks
    top = rings[-1]
    c = sum(top, V()) / sides
    rim = [c + (p - c) * .88 + V((0, 0, .035)) for p in top]
    for k in range(sides):
        k2 = (k + 1) % sides
        bark.quad(top[k], top[k2], rim[k2], rim[k], (k / sides, 0), ((k + 1) / sides, 0), ((k + 1) / sides, .03),
                  (k / sides, .03))
    poly_cap(props, rim, (0, 0, 1), region='end_grain_0')
    top_cracks(props, c + V((0, 0, .035)), .26, 4, 5, .004)
    moss_cape(mm, pl, rings, 4, 6, ks_facing(rings, 5, (-.3, -1, 0), .3), seed=5, drip_len=(.08, .18))
    moss_cape(mm, pl, rings, 4, 6, ks_facing(rings, 5, (.8, .5, 0), .6), seed=15, drip_len=(.08, .16))
    # mushroom clusters on the right-front side (separate object)
    k = ks_facing(rings, 4, (1, -.6, 0), .6)[0]
    p, o = fungus_on(fun, rings, 4, k, frac=.3, seed=1, n=3, size=1.7, up_bias=.75)
    shelf_moss(mm, pl, p - o * .02, o, .1, .1, p.z - .01, 3)
    k = ks_facing(rings, 3, (.7, .7, 0), .6)[0]
    fungus_on(fun, rings, 3, k, frac=.6, seed=2, n=2, size=1.1, up_bias=.7)
    sedge(pl, (-.55, .35, WATER - .04), h=.5, w=.4, seed=3)
    sedge(pl, (.6, .4, WATER - .04), h=.45, w=.35, seed=4)
    fo = fun.to_object(vid + '_Fungus')
    objs = finish(vid, {'Bark': bark, 'Props': props, 'Plants': pl, 'Moss': mm}, moss_from=['Bark'],
                  moss_kw=dict(min_nz=.6, keep=.45, seed=9), drip_min_z=.06, extra=[fo])
    return objs


# ------------------------------------------------------------------ B: tall hollow stump with side step
def build_b():
    vid = 'SW12b'
    bark, props, pl, fun, mm = sk.Mesh('Bark'), sk.Mesh('Props'), sk.Mesh('Plants'), sk.Mesh('Props'), sk.Mesh('Moss')
    sides = 12
    zs = [-.24, -.12, .0, .12, .26, .42, .6, .8, .98]
    butt = {1: .9, 4: .65, 6: 1.0, 9: .8, 11: .6}
    hts = [.0, .05, .02, .1, .04, .0, .03, .16, .2, .07, .02, .04]
    orings, oF, path = buttress_stump(bark, zs, .36, sides, butt, 81, z_b0=.5, lean=(-.04, .03), crown=hts)
    # real hollow: inner wall (faces inward) from the floor to the jagged rim, end-grain rim, dark floor
    floor_z = .3
    ip = [p for p in path if p.z >= floor_z]
    ip = [V((ip[0].x, ip[0].y, floor_z))] + ip
    bp = []
    c_top = path[-1]
    for p in orings[-1]:
        d = p - c_top
        d = d - oF[-1][2] * d.dot(oF[-1][2])
        bp.append(d)
    iprofs = []
    for i, p in enumerate(ip):
        # inner wall follows the top ring shape, thickness ~0.09 m
        x, y, t = frames(ip)[i]
        ring = []
        for d in reversed(bp):
            dl = d.length
            ring.append((d.dot(x) * (dl - .09) / dl, d.dot(y) * (dl - .09) / dl))
        iprofs.append(ring)
    irings, iF = ptube(bark, ip, iprofs, offs=crown_offsets(sides, list(reversed(hts)), len(ip) - 1), v0=.4)
    o, inn = orings[-1], irings[-1]
    for k in range(sides):
        k2 = (k + 1) % sides
        poly_cap(props, [o[k], o[k2], inn[sides - 1 - k2], inn[sides - 1 - k]], oF[-1][2], region='end_grain_1')
    poly_cap(props, list(reversed(irings[0])), (0, 0, 1), region='dark')
    # side step: a lower broken chunk grown on the front-right (+X,-Y), flat end-grain top at ~0.42 m
    sp = [V((.36, -.22, -.2)), V((.42, -.24, .15)), V((.45, -.26, .42))]
    dprof = [(0.0, -.34), (.22, -.28), (.32, -.02), (.24, .26), (0.0, .34), (-.2, 0)]
    srings, sF = ptube(bark, sp, [[(a * 1.25, b * 1.15) for a, b in dprof], [(a * 1.05, b) for a, b in dprof], dprof],
                       up_hint=(1, -.3, 0), v0=.6)
    poly_cap(props, srings[-1], (0, 0, 1), region='end_grain_0')
    stc = sum(srings[-1], V()) / len(srings[-1])
    top_cracks(props, stc, .22, 3, 9, .004)
    # moss: ragged drapes running down from the rim + on the step
    moss_cape(mm, pl, orings, 7, 8, ks_facing(orings, 7, (-.5, -1, 0), .3), seed=6, drip_len=(.12, .26), reach=(.3, 1))
    moss_cape(mm, pl, orings, 7, 8, ks_facing(orings, 7, (.3, 1, 0), .5), seed=7, drip_len=(.1, .2))
    mushroom_cluster(fun, stc + V((.08, -.04, -.01)), (0, 0, 1), seed=11, n=3, size=1.5)
    shelf_moss(mm, pl, stc + V((-.05, 0, 0)), (1, -.3, 0), .14, .16, stc.z + .012, 4)
    k = ks_facing(orings, 5, (-1, -.3, 0), .7)[0]
    fungus_on(fun, orings, 5, k, frac=.5, seed=12, n=2, size=1.2, up_bias=.7)
    sedge(pl, (-.65, .4, WATER - .04), h=.55, w=.4, seed=5)
    sedge(pl, (.6, .55, WATER - .04), h=.5, w=.4, seed=6)
    sedge(pl, (-.5, -.6, WATER - .04), h=.45, w=.35, seed=7)
    fo = fun.to_object(vid + '_Fungus')
    objs = finish(vid, {'Bark': bark, 'Props': props, 'Plants': pl, 'Moss': mm}, moss_from=['Bark'],
                  moss_kw=dict(min_nz=.6, keep=.45, seed=10), drip_min_z=.06, extra=[fo])
    return objs


VARIANTS = [
    ('a', 'StumpLowClosed', 'Пень: низкий закрытый', build_a),
    ('b', 'StumpTallHollow', 'Пень: высокий полый со ступенью', build_b),
]


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else 'render'
    only = sys.argv[2] if len(sys.argv) > 2 else None
    sk.reset()
    groups, recs, cardv = [], [], []
    for L, name, ru, fn in VARIANTS:
        objs = fn()
        d = sk.ground(objs)
        wl = round(WATER + d.z, 3)
        fbx = os.path.join(OUT, 'SM_SW12_%s.fbx' % name)
        sk.export_fbx(objs, fbx)
        tbo = {o.name: sk.tris([o]) for o in objs}
        rec = sk.variant_record('SW12' + L, name, objs, fbx, name_ru=ru, water_line_z=wl, solidity='walk-on-top',
                                burns=True,
                                collider=('trunk convex hull (Bark) with the flat top as standing surface; roots no '
                                          'collider or low boxes' if L == 'a' else
                                          'outer trunk as 6-8 boxes around the hollow (or a convex hull + no-walk top '
                                          'rim), step chunk = separate box (standable at its flat top)') +
                                         '; Fungus/Plants no collider',
                                separate_objects={'SW12%s_Fungus' % L: 'Props material; pale mushroom clusters, '
                                                  'removable (delete the object)'},
                                tris_by_object=tbo, moving_parts=None, pivot='bottom centre (root tips at z=0)',
                                notes='roots go under the water line')
        recs.append(rec); groups.append(objs); cardv.append(('SW12' + L + ' ' + name, objs, wl))
        print(rec['id'], rec['tris'], rec['size_xyz_m'], rec['tris_by_material'], 'wl', wl)
    json.dump(recs, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)
    if mode == 'render':
        make_card(cardv, OUT, SHEET, 'SW12 Pen v vode / rotten stumps - concept (top) vs model (a, b)',
                  views=((-35, 22), (145, 22), (35, 22)), extra_view=(-60, 45),
                  only=[('SW12' + c) for c in only] if only else None)
    if mode == 'bf':
        for lab, objs, wl in cardv:
            backface_render(objs, os.path.join(HERE, 'work', 'bf_%s.png' % lab.split()[0]), el=45)
    place_side_by_side(groups, 1.0)
    sk.save_blend(os.path.join(OUT, 'SW12_RottenStumps.blend'))


if __name__ == '__main__':
    main()
