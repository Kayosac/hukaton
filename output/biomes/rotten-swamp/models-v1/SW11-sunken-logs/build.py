"""SW11 Топляк — sunken logs, 2 variants. python3 build.py [render|norender|bf] [only-letters]"""
import sys, os, math, random, json
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, '/home/claude/swamp/items/SW10-uprooted-snags'); sys.path.insert(0, '/home/claude/swamp/kit')
import sk
from dwlib import *

OUT = os.path.join(HERE, 'out')
SHEET = '/home/claude/ref/swamp/sheets/11-sunken-logs.png'
D = math.radians


def log_profile(hu, hw, notch=0.0, crack=0.0, seed=0, jit=.05, n=8, notch_w=.5):
    """n-gon with a flat top face (frame x = up, y = side): 3 crack verts inserted on the upper +y face (after
    vertex 0) and 3 notch verts on the lower -y face (after vertex n//2). CCW, n + 6 verts.
    Notch wall edges are indices n//2 + 4 and n//2 + 5."""
    rnd = random.Random(seed)
    def P(a):
        f = 1 + rnd.uniform(-jit, jit)
        return V((math.cos(D(a)) * hu * f, math.sin(D(a)) * hw * f, 0))
    vs = [P(180 / n + 360 * k / n) for k in range(n)]
    vs[0].x = vs[-1].x = max(vs[0].x, vs[-1].x)          # level top face (walkable)
    def ins(A, B, depth, w):
        c1 = A.lerp(B, .5 - .05 - w); c3 = A.lerp(B, .5 + .05 + w)
        m = A.lerp(B, .5)
        c2 = m - m.normalized() * depth * m.length
        return [c1, c2, c3]
    cr = ins(vs[0], vs[1], crack, crack * .25)
    h = n // 2 + 1                 # notch on the -y side at about 9 o'clock (stays above the water line)
    nt = ins(vs[h], vs[h + 1], notch, notch * notch_w)
    pts = [vs[0]] + cr + vs[1:h + 1] + nt + vs[h + 1:]
    return [(p.x, p.y) for p in pts]


def log(bark, props, path, hu, hw, seed, notch_front=.6, crack_rows=(), crack_depth=.25, back_jag=(.0, .12, 2),
        bulge=None, v0=0.0, n_sides=8):
    """Straight/bent log along path. Front end (path[0]) split by a big V notch, back end broken (jagged)."""
    rnd = random.Random(seed)
    n = len(path)
    profs = []
    for i in range(n):
        s = 1 + rnd.uniform(-.04, .04)
        if bulge and i in bulge:
            s *= bulge[i]
        notch = notch_front if i == 0 else (notch_front * .5 if i == 1 else 0.0)
        crack = crack_depth if i in crack_rows else 0.0
        profs.append(log_profile(hu * s, hw * s, notch, crack, seed + i, n=n_sides))
    N = n_sides + 6
    hts = jag(N, back_jag[0], back_jag[1], seed + 5, spikes=back_jag[2])
    hts = [h if k not in (0, N - 1) else min(h, .03) for k, h in enumerate(hts)]  # keep top edge low
    hn = n_sides // 2 + 1
    rings, F = ptube(bark, path, profs, offs=crown_offsets(N, hts, n - 1), up_hint=(0, 0, 1), v0=v0,
                     alt=(props, {hn + 4, hn + 5}, 'small_wood', (0, 2)))
    poly_cap(props, list(reversed(rings[0])), -F[0][2], region='end_grain_0')
    broken_cap(props, rings[-1], F[-1][2], depth=.03, region='end_grain_1')
    return rings, F


def bark_slab(bark, path, a0, a1, r, thick, seed, lift=.08):
    """Peeling bark plate lying on the log: a C-shell segment, its far end lifting off."""
    n = len(path)
    profs = []
    for i in range(n):
        ro = r + .02 + (lift * (i / (n - 1)) ** 2)
        profs.append(c_profile(D(a0), D(a1), ro, ro - thick, 4, seed + i, jit=.03))
    rings, F = ptube(bark, path, profs, up_hint=(0, 0, 1))
    poly_cap(bark, list(reversed(rings[0])), -F[0][2])
    poly_cap(bark, rings[-1], F[-1][2])


def stub(bark, props, base, direction, L, r, seed, hts=(0, .05, .01, .04, 0)):
    B = V(base); Dd = V(direction).normalized()
    path = [B, B + Dd * L * .55 + V((0, 0, .01)), B + Dd * L]
    rings, F = ptube(bark, path, [prof(5, r, r * .85, jit=.12, seed=seed), prof(5, r * .85, r * .75, seed=seed + 1),
                                  prof(5, r * .75, r * .65, seed=seed + 2)],
                     offs=crown_offsets(5, list(hts), 2), up_hint=(0, 0, 1))
    broken_cap(props, rings[-1], F[-1][2], depth=.01, region='end_grain_0')
    return rings


def sample(points, n):
    return bez(points, n)


# ------------------------------------------------------------------ A: long straight log (6 m along Y)
def build_a():
    vid = 'SW11a'
    bark, props, pl = sk.Mesh('Bark'), sk.Mesh('Props'), sk.Mesh('Plants')
    n = 15
    ctrl = [V((0, -3, .38)), V((.05, -1.5, .36)), V((-.04, 0, .33)), V((.04, 1.5, .35)), V((-.02, 3, .39))]
    path = wob(sample(ctrl, n), .025, 3)
    rings, F = log(bark, props, path, .38, .4, 11, notch_front=.85, crack_rows=(5, 6, 7, 8), crack_depth=.32,
                   bulge={4: 1.07, 10: 1.06, 11: .97})
    # peeling bark slabs on the side / lower side (not on the walkable top)
    bark_slab(bark, path[6:10], 100, 150, .41, .035, 21, lift=.09)
    bark_slab(bark, path[2:5], 215, 255, .41, .035, 22, lift=.06)
    bark_slab(bark, list(reversed(path[10:13])), 95, 135, .41, .03, 23, lift=.07)
    # broken twig stubs on the sides (below the top face)
    stub(bark, props, path[3] + V((.3, 0, .05)), (1, .3, .45), .35, .07, 31)
    stub(bark, props, path[9] + V((-.3, 0, -.02)), (-1, -.2, .3), .3, .06, 32)
    stub(bark, props, path[12] + V((.3, 0, -.05)), (1, .5, .1), .25, .055, 33)
    objs = finish(vid, {'Bark': bark, 'Props': props, 'Plants': pl}, moss_from=['Bark'],
                  moss_kw=dict(min_nz=.72, keep=.6, seed=7), drip_min_z=.12)
    return objs


# ------------------------------------------------------------------ B: short bent log with broken branch
def build_b():
    vid = 'SW11b'
    bark, props, pl = sk.Mesh('Bark'), sk.Mesh('Props'), sk.Mesh('Plants')
    n = 11
    ctrl = [V((-.45, -1.6, .4)), V((.05, -.85, .44)), V((.5, -.05, .38)), V((.2, .8, .47)), V((-.35, 1.5, .42))]
    path = wob(sample(ctrl, n), .02, 4)
    rings, F = log(bark, props, path, .4, .46, 41, notch_front=.8, crack_rows=(3, 4), crack_depth=.25,
                   bulge={5: 1.1, 6: 1.06}, back_jag=(0, .1, 2))
    # broken branch rising from the top-side, behind the middle (to one side of the walkable line)
    p = path[7]
    br = [p + V((-.14, 0, .12)), p + V((-.2, .1, .45)), p + V((-.24, .17, .68)), p + V((-.24, .2, .8))]
    bprof = prof(7, .13, .12, jit=.12, seed=7)
    brings, bF = ptube(bark, br, [scale_prof(bprof, 1.3), bprof, scale_prof(bprof, .88), scale_prof(bprof, .82)],
                       offs=crown_offsets(7, [0, .1, .03, .16, .05, .0, .07], 3), up_hint=(1, 0, 0))
    broken_cap(props, brings[-1], bF[-1][2], depth=.02, region='end_grain_1')
    splinter(bark, br[-1] + V((.02, 0, .0)), (.1, .1, 1), .12, .025, seed=9)
    bark_slab(bark, path[2:5], 100, 145, .45, .035, 51, lift=.08)
    stub(bark, props, path[8] + V((.32, 0, -.05)), (1, -.2, .25), .28, .06, 52)
    mm = sk.Mesh('Moss')
    moss_cape(mm, pl, brings, 0, 2, ks_facing(brings, 1, (0, -1, 0), .2), seed=3, drip_len=(.06, .12))
    objs = finish(vid, {'Bark': bark, 'Props': props, 'Plants': pl, 'Moss': mm}, moss_from=['Bark'],
                  moss_kw=dict(min_nz=.72, keep=.62, seed=8), drip_min_z=.15)
    return objs


VARIANTS = [
    ('a', 'SunkenLogLong', 'Топляк: длинное прямое бревно', build_a, .25),
    ('b', 'SunkenLogBent', 'Топляк: короткое изогнутое с обломком ветки', build_b, .28),
]


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else 'render'
    only = sys.argv[2] if len(sys.argv) > 2 else None
    sk.reset()
    groups, recs, cardv = [], [], []
    for L, name, ru, fn, wl0 in VARIANTS:
        objs = fn()
        d = sk.ground(objs)
        wl = round(wl0 + d.z, 3)
        fbx = os.path.join(OUT, 'SM_SW11_%s.fbx' % name)
        sk.export_fbx(objs, fbx)
        rec = sk.variant_record('SW11' + L, name, objs, fbx, name_ru=ru, water_line_z=wl, solidity='walk-on-top',
                                burns=False,
                                collider='one capsule/box along the log (Bark); top face is flat & continuous for '
                                         'walking; branch stub (b) small box or no collider; Moss/Props/Plants no '
                                         'separate collider',
                                moving_parts=None, pivot='bottom centre; log runs along Y (catalog Z = length)',
                                notes='sunken: about a third of the height under the water line; front end has the V split above water')
        recs.append(rec); groups.append(objs); cardv.append(('SW11' + L + ' ' + name, objs, wl))
        print(rec['id'], rec['tris'], rec['size_xyz_m'], rec['tris_by_material'], 'wl', wl)
    json.dump(recs, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)
    if mode == 'render':
        make_card(cardv, OUT, SHEET, 'SW11 Toplyak / sunken logs - concept (top) vs model (a, b)',
                  views=((35, 22), (145, 22), (90, 14)), extra_view=(-30, 10), margin=.95, only=[('SW11' + c) for c in only] if only else None)
        # replace the 4th tile by a close-up of the split front end (end-grain face + V split)
        tiles, labels = [], []
        for lab, objs, wl in cardv:
            vid = lab.split()[0]
            lo, hi = sk.bounds(objs)
            p = os.path.join(OUT, 'r_%s_dry.png' % vid)
            sk.render(objs, p, az=30 if vid.endswith('a') else 5, el=14, size=(320, 320), samples=10, water_z=wl,
                      target=(((lo[0] + hi[0]) / 2 - .1) if vid.endswith('a') else lo[0] + .5, lo[1] + .7, wl + .1), dist=4.0)
            for j in range(3):
                tiles.append(os.path.join(OUT, 'r_%s_%d.png' % (vid, j)))
            tiles.append(p)
            labels += ['%s az%d' % (lab, a) for a in (35, 145, 90)] + ['%s front end close-up' % lab]
        sk.sheet(tiles, os.path.join(OUT, 'card.jpg'), 4, labels=labels, concept=SHEET,
                 title='SW11 Toplyak / sunken logs - concept (top) vs model (a, b)')
    if mode == 'bf':
        for lab, objs, wl in cardv:
            backface_render(objs, os.path.join(HERE, 'work', 'bf_%s.png' % lab.split()[0]))
    place_side_by_side(groups, 1.0)
    sk.save_blend(os.path.join(OUT, 'SW11_SunkenLogs.blend'))


if __name__ == '__main__':
    main()
