"""SW15 Сваи и колья — stakes, 3 variants. python3 build.py [render|norender|bf] [only-letters]"""
import sys, os, math, random, json
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, '/home/claude/swamp/items/SW10-uprooted-snags'); sys.path.insert(0, '/home/claude/swamp/kit')
import sk
from dwlib import *

OUT = os.path.join(HERE, 'out')
SHEET = '/home/claude/ref/swamp/sheets/15-stakes.png'
WATER = 0.0
BOT = -0.4          # posts are driven 0.4 m below the water line


def post(bark, props, pts, r0, r1, seed, sides=5, crown=(.02, .16, 2), flat=.85, tw=.25, region='end_grain_0'):
    """Crooked 5-sided post along kinked polyline pts (bends as hard kinks + extra rings), splintered top."""
    rnd = random.Random(seed)
    P = [V(p) for p in pts]
    n = len(P)
    base = prof(sides, 1, flat, jit=.14, seed=seed, rot=rnd.uniform(0, 1), ajit=.2)
    profs = []
    for i in range(n):
        f = i / (n - 1)
        r = r0 + (r1 - r0) * f
        a = tw * f
        ca, sa = math.cos(a), math.sin(a)
        j = prof(sides, 1, 1, jit=.08, seed=seed + i * 3)
        profs.append([((x * ca - y * sa) * r * (1 + jx * .1), (x * sa + y * ca) * r * (1 + jy * .1))
                      for (x, y), (jx, jy) in zip(base, j)])
    hts = jag(sides, crown[0], crown[1], seed + 9, spikes=crown[2])
    rings, F = ptube(bark, P, profs, offs=crown_offsets(sides, hts, n - 1), v0=rnd.uniform(0, 1))
    C = broken_cap(props, rings[-1], F[-1][2], depth=.025, region=region)
    poly_cap(bark, list(reversed(rings[0])), -F[0][2])
    return rings, F, C


def rope_coils(props, centre_pts, radius_at, z0, turns, pitch, rr, seed, tilt=(0.12, 0.05), seg_len=.45):
    """Rope wound around a post: helix of a 4-sided tube, split in short pieces so the rope texture keeps its scale."""
    rnd = random.Random(seed)
    pts = []
    steps = int(turns * 10)
    for s in range(steps + 1):
        a = 2 * math.pi * s / 10 + rnd.uniform(-.05, .05)
        z = z0 + pitch * s / 10
        c = centre_pts(z)
        r = radius_at(z) + rr * .9
        tz = tilt[0] * math.cos(a) * r + tilt[1] * math.sin(a) * r
        pts.append(V((c.x + math.cos(a) * r, c.y + math.sin(a) * r, z + tz)))
    # split into pieces of ~seg_len, each mapped to the full rope region (u along length)
    piece, acc, out = [pts[0]], 0.0, []
    for p in pts[1:]:
        acc += (p - piece[-1]).length
        piece.append(p)
        if acc >= seg_len:
            out.append(piece); piece = [p]; acc = 0.0
    if len(piece) > 1:
        out.append(piece)
    for pc in out:
        ptube(props, pc, prof(4, rr, rr * .9, seed=seed, rot=.4), region='rope', uv_swap=True, up_hint=(0, 0, 1))
    return pts


def hanging_rope(props, start, drop, rr, seed, swing=(.04, .02)):
    rnd = random.Random(seed)
    S = V(start)
    pts = [S + V((swing[0] * math.sin(f * 2.2) + rnd.uniform(-.01, .01), swing[1] * f, -drop * f)) for f in
           [i / 6 for i in range(7)]]
    profs = [prof(4, rr * (1 - .2 * i / 6), rr * .9 * (1 - .2 * i / 6), seed=seed, rot=.4) for i in range(7)]
    rings, F = ptube(props, pts, profs, region='rope', uv_swap=True, up_hint=(1, 0, 0))
    # frayed end: small splayed cone
    poly_cap(props, rings[-1], F[-1][2], region='rope', center=pts[-1] + F[-1][2] * rr * 2.5)
    poly_cap(props, list(reversed(rings[0])), -F[0][2], region='rope')


def moss_spots(rings, seed=0, n=5, zmax=1.2):
    """Olive moss patches on a few post faces (mostly the lower, wetter half) as on the concept."""
    rnd = random.Random(seed)
    mm = sk.Mesh('Moss')
    N = len(rings[0])
    cand = [(i, k) for i in range(1, len(rings) - 2) for k in range(N) if rings[i + 1][k].z < zmax and rings[i][k].z > -.05]
    for i, k in rnd.sample(cand, min(n, len(cand))):
        moss_cape(mm, None, rings, i, i + 1, [k], cols=2, reach=(.35, 1.0), seed=rnd.randint(0, 999))
    return mm


def tufts(pl, spots, seed):
    rnd = random.Random(seed)
    for i, (x, y) in enumerate(spots):
        sedge(pl, (x, y, WATER - .05), h=rnd.uniform(.35, .55), w=rnd.uniform(.3, .4), seed=seed + i, n=2)


def centre_fn(pts):
    P = [V(p) for p in pts]
    def f(z):
        for i in range(len(P) - 1):
            if P[i].z <= z <= P[i + 1].z:
                t = (z - P[i].z) / (P[i + 1].z - P[i].z)
                return P[i].lerp(P[i + 1], t)
        return P[-1]
    return f


# ------------------------------------------------------------------ A: bare broken forked post (2.5 m)
def build_a():
    vid = 'SW15a'
    bark, props, pl = sk.Mesh('Bark'), sk.Mesh('Props'), sk.Mesh('Plants')
    pts = [(0, 0, BOT), (.0, 0, 0), (.04, .01, .25), (.09, .01, .5), (.14, .0, .75), (.17, .01, .9), (.16, .03, 1.1),
           (.1, .03, 1.3), (.05, .02, 1.45), (.03, .0, 1.6), (.04, -.02, 1.75), (.06, -.03, 1.9), (.07, -.03, 2.02)]
    rings, F, C = post(bark, props, pts, .16, .12, 11, crown=(.02, .2, 2))
    # fork: second broken limb leaving at the upper bend, angled away
    fb = [V((.04, .0, 1.32)), V((.2, -.01, 1.5)), V((.38, -.02, 1.7)), V((.52, -.03, 1.86)), V((.58, -.03, 1.96))]
    fp = prof(5, .1, .085, jit=.12, seed=4)
    fr, fF = ptube(bark, fb, [scale_prof(fp, 1.25), fp, scale_prof(fp, .92), scale_prof(fp, .86), scale_prof(fp, .82)],
                   offs=crown_offsets(5, [0, .09, .02, .13, .03], 4), up_hint=(1, 0, 0))
    broken_cap(props, fr[-1], fF[-1][2], depth=.015, region='end_grain_1')
    for j, (d, h) in enumerate([((.2, .1, 1), .14), ((-.25, -.1, 1), .1)]):
        splinter(bark, C + V((d[0] * .1, d[1] * .1, -.02)), d, h, .022, seed=20 + j)
    splinter(bark, fb[-1] + V((0, 0, -.01)), (.5, 0, 1), .08, .018, seed=30)
    tufts(pl, [(-.22, .1), (.2, -.12)], 40)
    return finish(vid, {'Bark': bark, 'Props': props, 'Plants': pl, 'Moss': moss_spots(rings, seed=len(vid) + ord(vid[-1]))}), None


# ------------------------------------------------------------------ B: post with rope wraps (2 m)
def build_b():
    vid = 'SW15b'
    bark, props, pl = sk.Mesh('Bark'), sk.Mesh('Props'), sk.Mesh('Plants')
    pts = [(0, 0, BOT), (0, 0, 0), (-.03, 0, .4), (-.06, .01, .8), (-.05, 0, 1.1), (.0, -.01, 1.3),
           (.1, -.02, 1.45), (.22, -.03, 1.56), (.33, -.03, 1.62), (.4, -.03, 1.64)]
    rings, F, C = post(bark, props, pts, .17, .13, 21, crown=(.02, .12, 2), flat=.8)
    cf = centre_fn(pts)
    rad = lambda z: .15 - .02 * (z / 1.5)
    coil = rope_coils(props, cf, rad, 1.12, 2.3, .05, .024, 5, tilt=(.2, .05))
    # knot + two hanging ends on the right side
    k = max(coil, key=lambda p: p.x)
    hanging_rope(props, k + V((.01, -.01, -.02)), .42, .022, 7)
    hanging_rope(props, k + V((.0, .03, -.03)), .3, .02, 8, swing=(.03, .04))
    tufts(pl, [(-.25, .08), (.22, .15), (.05, -.25)], 50)
    return finish(vid, {'Bark': bark, 'Props': props, 'Plants': pl, 'Moss': moss_spots(rings, seed=len(vid) + ord(vid[-1]))}), None


# ------------------------------------------------------------------ C: short split post with a rag (1 m)
def build_c():
    vid = 'SW15c'
    bark, props, pl = sk.Mesh('Bark'), sk.Mesh('Props'), sk.Mesh('Plants')
    pts = [(0, 0, BOT), (0, 0, 0), (.02, 0, .15), (.03, 0, .3), (.0, .01, .45), (.03, 0, .6)]
    rings, F, C = post(bark, props, pts, .17, .15, 31, crown=(.0, .02, 0), flat=.8, tw=.1)
    # split top: two half-posts diverging, each with a splintered end
    hp = [(.12, -.07), (.13, .07), (-.0, .1), (-.05, .0), (-.0, -.1)]
    hp = [(x, y * 1.3) for x, y in hp]
    for j, (sx, lean, h, s) in enumerate([(-1, -.05, .86, 41), (1, .06, .74, 42)]):
        pp = [V((.03 + sx * .05, .0, .5)), V((.03 + sx * .06 + lean * .4, 0, .62)), V((.03 + sx * .065 + lean * .7, 0, .72)),
              V((.03 + sx * .07 + lean, .0, h))]
        prf = [(x * sx * .9, y) for x, y in hp]
        if sx < 0:
            prf = list(reversed(prf))
        hts = jag(5, .0, .14, s, spikes=1)
        hr, hF = ptube(bark, pp, [prf, scale_prof(prf, .95), scale_prof(prf, .9), scale_prof(prf, .85)], offs=crown_offsets(5, hts, 3),
                       up_hint=(1, 0, 0), v0=.3 * j)
        broken_cap(props, hr[-1], hF[-1][2], depth=.02, region='end_grain_0')
        splinter(bark, pp[-1] + V((0, 0, -.02)), (sx * .2, .1, 1), .09, .02, seed=s + 5)
    # rag band tied around the post + knot, hanging rag card that flutters
    cf = centre_fn(pts)
    zc = .52
    c = cf(zc)
    band = []
    nb = 7
    for kk in range(nb):
        a = 2 * math.pi * kk / nb
        band.append((math.cos(a) * .175, math.sin(a) * .16))
    br, bF = ptube(props, [c + V((0, 0, -.05)), c + V((0, 0, .06))],
                   [band, [(x * .97, y * .97) for x, y in band]],
                   offs=lambda i, k, x, y, t: V((0, 0, .03 * math.cos(2 * math.pi * k / nb))), region='rag')
    knot = c + V((.18, -.03, .02))
    props.box(knot, (.06, .07, .07), rot=(0, .4, .3), region='rag', seed=3)
    pl.card(knot + V((.02, 0, .0)), V((.0, .11, .0)), V((.05, 0, .34)), 'rag_card', hang=True, segs=3, bend=.05,
            wind_flutter=.08)
    pl.card(knot + V((.0, -.02, .0)), V((.03, -.06, .0)), V((.09, 0, .2)), 'rag_card', hang=True, segs=2, bend=.03,
            wind_flutter=.06)
    tufts(pl, [(-.25, .05), (.25, .15)], 60)
    return finish(vid, {'Bark': bark, 'Props': props, 'Plants': pl, 'Moss': moss_spots(rings, seed=len(vid) + ord(vid[-1]))}), None


VARIANTS = [
    ('a', 'StakeBare', 'Свая: голый обломок с развилкой', build_a),
    ('b', 'StakeRope', 'Свая: столб с верёвкой', build_b),
    ('c', 'StakeRag', 'Кол: короткий с тряпкой', build_c),
]


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else 'render'
    only = sys.argv[2] if len(sys.argv) > 2 else None
    sk.reset()
    groups, recs, cardv = [], [], []
    for L, name, ru, fn in VARIANTS:
        objs, _ = fn()
        d = sk.ground(objs)
        wl = round(WATER + d.z, 3)
        fbx = os.path.join(OUT, 'SM_SW15_%s.fbx' % name)
        sk.export_fbx(objs, fbx)
        rec = sk.variant_record('SW15' + L, name, objs, fbx, name_ru=ru, water_line_z=wl, solidity='solid',
                                burns=True,
                                tris_by_object={o.name: sk.tris([o]) for o in objs},
                                collider='one capsule along the post (Bark); rope/rag/sedge no collider',
                                moving_parts=None, pivot='bottom end of the post (0.4 m below the water line)',
                                notes='rag card (Plants rag_card) flutters via the Wind UV2 channel' if L == 'c' else '')
        recs.append(rec); groups.append(objs); cardv.append(('SW15' + L + ' ' + name, objs, wl))
        print(rec['id'], rec['tris'], rec['size_xyz_m'], rec['tris_by_object'], 'wl', wl)
    json.dump(recs, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)
    if mode == 'render':
        make_card(cardv, OUT, SHEET, 'SW15 Svai i kolya / stakes - concept (top) vs model (a, b, c)',
                  views=((35, 12), (145, 12), (90, 10)), extra_view=(-35, 15), margin=1.0,
                  only=[('SW15' + c) for c in only] if only else None)
    if mode == 'bf':
        for lab, objs, wl in cardv:
            backface_render(objs, os.path.join(HERE, 'work', 'bf_%s.png' % lab.split()[0]))
    place_side_by_side(groups, .6)
    sk.save_blend(os.path.join(OUT, 'SW15_Stakes.blend'))


if __name__ == '__main__':
    main()
