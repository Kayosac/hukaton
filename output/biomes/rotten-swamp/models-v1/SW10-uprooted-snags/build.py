"""SW10 Коряга — uprooted snags, 3 variants. python3 build.py [render|norender] [only-letters]"""
import sys, os, math, random, json
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE); sys.path.insert(0, '/home/claude/swamp/kit')
import sk
from dwlib import *

OUT = os.path.join(HERE, 'out')
SHEET = '/home/claude/ref/swamp/sheets/10-uprooted-snags.png'
WATER = 0.0          # build-space water level; roots end below it
D = math.radians


def roots_fan(bark, centre, specs, seed=0, sides=6):
    """specs: list of (angle_deg, start_r, start_z, length, r0, rise). Thick tentacle roots: leave the trunk thick,
    arch up and out, curl down into the water with blunt ends."""
    out = []
    for j, (a, sr, sz, L, r0, rise) in enumerate(specs):
        ang = D(a)
        st = V(centre) + V((math.cos(ang) * sr, math.sin(ang) * sr, sz))
        path, rings, F = root(bark, st, ang, L, r0, r0 * .5, rise=rise, end_z=WATER - .25 - .04 * (j % 3),
                              sides=sides, seed=seed + j * 13, n=12, flat=.88, wobble=.012, twist=.2,
                              shape='tentacle', blunt=True, taper_p=1.3)
        out.append((path, rings, F, r0))
    return out


def sub_root(bark, parent_path, at, ang, L, r0, seed):
    p = parent_path[at]
    root(bark, p + V((0, 0, -.02)), ang, L, r0, r0 * .55, rise=.04, end_z=WATER - .25, sides=5, seed=seed, n=8,
         flat=.85, wobble=.01, twist=.2, shape='tentacle', blunt=True, taper_p=1.2)


def plants_ring(pl, pts, seed):
    rnd = random.Random(seed)
    for i, (x, y) in enumerate(pts):
        if i % 3 == 2:
            pl.cross_cards((x, y, WATER - .05), .35, rnd.uniform(.9, 1.2), 'cattail_leaves_%d' % (i % 2), n=2,
                           rot=rnd.uniform(0, 3), seed=seed + i, wind_sway=.06, wind_flutter=.02)
        else:
            sedge(pl, (x, y, WATER - .05), h=rnd.uniform(.45, .65), w=rnd.uniform(.4, .55), seed=seed + i)


# ------------------------------------------------------------------ A: vertical fan of roots
def build_a():
    vid = 'SW10a'
    bark, props, pl = sk.Mesh('Bark'), sk.Mesh('Props'), sk.Mesh('Plants')
    rings, F, C = trunk(bark, props, (0, 0, .42), (.12, .08, 1.12), .36, .27, sides=7, seed=11, n=6,
                        crown=(.02, .3, 2), lean_mid=(-.06, .03), v0=.3, flare=1.12, chamfer=False)
    # split-off slab rising on the back-left (second prong of the broken top)
    slab_path = [V((-.12, .1, .65)), V((-.22, .15, .9)), V((-.28, .2, 1.15)), V((-.3, .22, 1.3))]
    sp = prof(4, .13, .06, jit=.15, seed=5, rot=.4)
    srings, sF = ptube(bark, slab_path, [sp, scale_prof(sp, .95), scale_prof(sp, .8), scale_prof(sp, .7)],
                       offs=crown_offsets(4, [.0, .12, .03, .2], 3), v0=.7)
    broken_cap(props, srings[-1], sF[-1][2], depth=.02, region='end_grain_1')
    for j, (dx, dy, h) in enumerate([(.13, -.05, .22), (-.05, -.15, .18), (.05, .14, .16)]):
        top_c = C + V((dx, dy, .0))
        splinter(bark, top_c - V((0, 0, .06)), (dx * 1.5, dy * 1.5, 1), h, .035, seed=40 + j)
    specs = [(-168, .12, .5, 1.2, .26, .11), (-116, .12, .58, 1.0, .25, .13), (-64, .12, .46, .95, .27, .09),
             (-14, .12, .52, 1.2, .28, .12), (38, .12, .58, 1.0, .25, .13), (92, .12, .46, .85, .24, .08),
             (142, .12, .52, 1.0, .25, .1)]
    R = roots_fan(bark, (0, 0, 0), specs, seed=100)
    sub_root(bark, R[0][0], 4, D(-150), .45, .11, 300)
    sub_root(bark, R[3][0], 5, D(-20), .4, .1, 301)
    sub_root(bark, R[5][0], 4, D(60), .4, .1, 302)
    plants_ring(pl, [(-1.05, -.5), (1.1, -.45), (.85, .65), (-.8, .6)], 20)
    mm = sk.Mesh('Moss')
    moss_cape(mm, pl, rings, 3, len(rings) - 2, ks_facing(rings, 3, (-.5, -.8, 0), .6), seed=8)
    moss_cape(mm, pl, srings, 2, 3, ks_facing(srings, 1, (-.3, -.9, 0), .3), seed=9)
    objs = finish(vid, {'Bark': bark, 'Props': props, 'Plants': pl, 'Moss': mm}, moss_from=['Bark'],
                  moss_kw=dict(min_nz=.6, keep=.55, seed=4))
    return objs


# ------------------------------------------------------------------ B: low sideways snag
def build_b():
    vid = 'SW10b'
    bark, props, pl = sk.Mesh('Bark'), sk.Mesh('Props'), sk.Mesh('Plants')
    cx = .95
    rings, F, C = trunk(bark, props, (cx, 0, .15), (cx + .07, .02, .98), .42, .37, sides=7, seed=21, n=5,
                        flat_top=True, lean_mid=(.03, 0), grain='end_grain_1', chamfer=True, v0=.1)
    # three big sweeping roots to the left (-X), long, arching, plus short ones right/back
    specs = [(176, .2, .64, 2.15, .3, .12), (150, .2, .56, 1.7, .27, .09), (-156, .2, .6, 1.95, .28, .11),
             (-95, .24, .42, .6, .22, .03), (-30, .24, .4, .55, .22, .03), (35, .24, .42, .6, .21, .03),
             (100, .24, .45, .65, .22, .04)]
    R = roots_fan(bark, (cx, 0, 0), specs, seed=200)
    sub_root(bark, R[0][0], 6, D(160), .55, .12, 400)
    sub_root(bark, R[2][0], 6, D(-140), .5, .11, 401)
    sub_root(bark, R[1][0], 5, D(120), .45, .11, 402)
    # broken root stub sticking up from the left root mass (snag silhouette)
    stub = [V((cx - .25, -.05, .7)), V((cx - .5, -.08, .92)), V((cx - .62, -.06, 1.0))]
    sr, sF = ptube(bark, stub, [prof(5, .1, .08, seed=7), prof(5, .07, .06, seed=8), prof(5, .055, .05, seed=9)],
                   offs=crown_offsets(5, [0, .06, .01, .03, 0], 2))
    broken_cap(props, sr[-1], sF[-1][2], depth=.01, region='end_grain_0')
    plants_ring(pl, [(-.7, -.6), (1.5, -.5), (1.3, .55), (-.9, .55), (.2, .65)], 30)
    objs = finish(vid, {'Bark': bark, 'Props': props, 'Plants': pl}, moss_from=['Bark'],
                  moss_kw=dict(min_nz=.6, keep=.5, seed=5))
    return objs


# ------------------------------------------------------------------ C: hollow split
def c_profile(a0, a1, ro, ri, n, seed):
    """C-shaped (half shell) profile: outer arc a0->a1 CCW, inner arc back. Non-convex, CCW."""
    rnd = random.Random(seed)
    outer = []
    for k in range(n):
        a = a0 + (a1 - a0) * k / (n - 1)
        f = 1 + rnd.uniform(-.08, .08)
        outer.append((math.cos(a) * ro * f, math.sin(a) * ro * f))
    inner = []
    for k in range(n - 1, -1, -1):
        a = a0 + (a1 - a0) * k / (n - 1)
        f = 1 + rnd.uniform(-.08, .08)
        inner.append((math.cos(a) * ri * f, math.sin(a) * ri * f))
    return outer + inner


def half_shell(bark, props, base, top, a0, a1, ro_bot, ro_top, thick, n_arc, seed, hts, lean=(0, 0)):
    B, T = V(base), V(top)
    n = 5
    rnd = random.Random(seed)
    path = [B.lerp(T, i / (n - 1)) + V((lean[0], lean[1], 0)) * math.sin(math.pi * i / (n - 1)) +
            (V((rnd.uniform(-.03, .03), rnd.uniform(-.03, .03), 0)) if 0 < i < n - 1 else V()) for i in range(n)]
    profs = []
    for i in range(n):
        f = i / (n - 1)
        ro = ro_bot + (ro_top - ro_bot) * f
        if i == 0:
            ro *= 1.25
        profs.append(c_profile(a0, a1, ro, ro - thick, n_arc, seed + i))
    rings, F = ptube(bark, path, profs, offs=crown_offsets(2 * n_arc, hts, n - 1), up_hint=(1, 0, 0))
    # jagged top rim = end grain (fan cap of the C would cross the hollow) -> quads between outer & inner
    top_r = rings[-1]
    for k in range(n_arc - 1):
        o0, o1 = top_r[k], top_r[k + 1]
        i0, i1 = top_r[2 * n_arc - 1 - k], top_r[2 * n_arc - 2 - k]
        poly_cap(props, [o0, o1, i1, i0], F[-1][2], region='end_grain_0')
    poly_cap(bark, rings[0], -F[0][2])
    return rings, F


def build_c():
    vid = 'SW10c'
    bark, props, pl = sk.Mesh('Bark'), sk.Mesh('Props'), sk.Mesh('Plants')
    mm = sk.Mesh('Moss')
    na = 7
    # left: tall hollow half-trunk, its open (split) side faces +X / front -> hollow visible through the ring
    lr, lF = half_shell(bark, props, (-.3, .02, .3), (-.42, .06, 1.42), D(40), D(300), .44, .34, .1, na, 31,
                        [.04, .1, .34, .2, .1, .03, .02] + [.02, .03, .1, .2, .34, .1, .04], lean=(-.05, 0))
    # dark rotten floor inside the hollow
    floor = [V((-.3 + math.cos(a) * .36, .02 + math.sin(a) * .34, .44)) for a in [2 * math.pi * k / 8 for k in range(8)]]
    poly_cap(props, floor, (0, 0, 1), region='dark')
    # right: the split-off half bends over as a thick arch -> big round see-through hole (axis ~Y)
    arch = bez([V((-.2, -.02, .95)), V((.15, -.06, 1.18)), V((.55, -.08, 1.12)), V((.82, -.06, .8)),
                V((.88, -.02, .35)), V((.84, .02, -.25))], 11)
    ap = prof(6, 1, .7, jit=.12, seed=57, rot=.3)
    arings, aF = ptube(bark, arch, [scale_prof(ap, .23 - .07 * i / 10) for i in range(11)], up_hint=(0, 0, 1))
    poly_cap(bark, arings[-1], aF[-1][2])
    # a broken splinter crest on the arch (where it split off the trunk)
    for j, (t, h) in enumerate([(3, .22), (4, .16)]):
        splinter(bark, arch[t] + V((0, 0, .12)), (.1 * j - .05, -.15, 1), h, .05, seed=80 + j)
    # low root closing the ring underneath
    low = bez([V((-.15, -.04, .52)), V((.25, -.1, .48)), V((.6, -.06, .3)), V((.9, -.1, -.25))], 8)
    lp = prof(5, 1, .8, jit=.15, seed=58)
    ptube(bark, low, [scale_prof(lp, .2 - .08 * i / 7) for i in range(8)], up_hint=(0, 0, 1))
    # roots
    specs_l = [(150, .3, .55, 1.05, .27, .1), (-158, .3, .52, 1.1, .27, .1), (-105, .3, .48, .85, .25, .07),
               (105, .3, .55, .85, .23, .1)]
    Rl = roots_fan(bark, (-.3, .02, 0), specs_l, seed=500)
    specs_r = [(-50, .1, .6, .9, .24, .08), (55, .1, .64, .85, .24, .1)]
    Rr = roots_fan(bark, (.82, -.04, 0), specs_r, seed=600)
    sub_root(bark, Rl[1][0], 6, D(-140), .45, .11, 700)
    moss_cape(mm, pl, lr, 3, 4, ks_facing(lr, 3, (-.6, -.7, 0), .75), seed=11)
    moss_cape(mm, pl, arings, 1, 5, ks_facing(arings, 3, (0, 0, 1), -2)[:2], seed=12)
    plants_ring(pl, [(-1.1, -.55), (1.25, .55), (-.9, .75), (.3, -.9)], 40)
    objs = finish(vid, {'Bark': bark, 'Props': props, 'Plants': pl, 'Moss': mm}, moss_from=['Bark'],
                  moss_kw=dict(min_nz=.6, keep=.5, seed=6))
    return objs


VARIANTS = [
    ('a', 'SnagRootFan', 'Коряга: вертикальный веер корней', build_a),
    ('b', 'SnagLowSideways', 'Коряга: низкая боковая', build_b),
    ('c', 'SnagHollowSplit', 'Коряга: полая расколотая', build_c),
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
        fbx = os.path.join(OUT, 'SM_SW10_%s.fbx' % name)
        sk.export_fbx(objs, fbx)
        rec = sk.variant_record('SW10' + L, name, objs, fbx, name_ru=ru, water_line_z=wl, solidity='solid',
                                burns=True,
                                collider='trunk + 3-5 main roots as convex hulls (Bark); Moss/Props share them; '
                                         'Plants (sedge, moss drips) no collider',
                                moving_parts=None, pivot='bottom centre (lowest root tip at z=0)',
                                notes='roots run below the water line; place with water at water_line_z')
        recs.append(rec)
        groups.append(objs)
        cardv.append(('SW10' + L + ' ' + name, objs, wl))
        print(rec['id'], rec['tris'], rec['size_xyz_m'], rec['tris_by_material'], 'wl', wl)
    json.dump(recs, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)
    if mode == 'render':
        make_card(cardv, OUT, SHEET, 'SW10 Koryaga / uprooted snags - concept (top) vs model (a, b, c)',
                  only=[('SW10' + c) for c in only] if only else None)
    if mode == 'bf':
        for lab, objs, wl in cardv:
            backface_render(objs, os.path.join(HERE, 'work', 'bf_%s.png' % lab.split()[0]))
    place_side_by_side(groups, 1.0)
    sk.save_blend(os.path.join(OUT, 'SW10_UprootedSnags.blend'))


if __name__ == '__main__':
    main()
