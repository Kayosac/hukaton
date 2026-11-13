"""SW02 Рогоз — 3 cattail clumps: a tall leaning bunch, b short broken, c wide with an arching stem.
Stalks = thin 4-sided prisms (Plants 'reed_stem', with wind), cobs = 6-sided prisms (Props 'cattail_cob') around the
upper part of the stalk with a spike above, leaves = long V-folded strap strips (Plants 'cattail_leaf_0/1'),
some snapped and folded down, + crossing fan cards (cattail_leaves_0/1) in the core, + snapped dead stalks."""
import sys, os, json, math, random
sys.path.insert(0, '/home/claude/swamp/items/_plants_lib')
import plib, sk
from plib import V, hdir

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)
SHEET = '/home/claude/ref/swamp/sheets/02-cattails.png'

def leaf(pl, rnd, base, az, length, width, tilt0=0.15, tilt1=0.9, kink=None, segs=5):
    """Strap leaf. kink=(f, drop_angle): snapped at fraction f, the rest hangs down/outwards."""
    if kink is None:
        pts = plib.arc_path(base, az, tilt0, tilt1, length, segs=segs, p=1.7)
    else:
        f, dang = kink
        n1 = max(2, int(segs * f))
        pts = plib.arc_path(base, az, tilt0, tilt0 + 0.25, length * f, segs=n1, p=1.5)
        d = V((math.sin(dang) * math.cos(az), math.sin(dang) * math.sin(az), math.cos(dang)))
        rest = length * (1 - f)
        for i in range(1, 3):
            pts.append(pts[-1] + d * rest / 2 + V((0, 0, -0.02 * i)))
    for p in pts[1:]:   # drooping / snapped leaves never dip below the root
        p.z = max(p.z, 0.015)
    plib.blade(pl, pts, width, 'cattail_leaf_%d' % (1 if kink or rnd.random() < .3 else 0),
               fold=rnd.uniform(.15, .3), twist=rnd.uniform(-.6, .6), roll=rnd.uniform(-.5, .5),
               sway=0.06 * length)

def leaves_ring(pl, rnd, n, Lmin, Lmax, width, kinks=0.3, spread=(0.15, 0.6), droop=(0.5, 1.3), az0=0.0,
                pref_az=None, pref=0.0):
    for i in range(n):
        az = az0 + 2 * math.pi * i / n + rnd.uniform(-.4, .4)
        if pref_az is not None:
            da = math.atan2(math.sin(pref_az - az), math.cos(pref_az - az)); az += da * pref
        L = rnd.uniform(Lmin, Lmax)
        t0 = rnd.uniform(*spread)
        base = V((0, 0, 0)) + hdir(az) * rnd.uniform(0.01, 0.05)
        if rnd.random() < kinks:
            leaf(pl, rnd, base, az, L, width * rnd.uniform(.9, 1.15), t0, kink=(rnd.uniform(.45, .7), rnd.uniform(1.9, 2.6)))
        else:
            leaf(pl, rnd, base, az, L, width * rnd.uniform(.9, 1.15), t0, t0 + rnd.uniform(*droop))

def broken_stalk(pl, props, rnd, base, az, h, tilt=0.15):
    pts = plib.arc_path(base, az, tilt * .5, tilt, h, segs=3, p=1.0)
    plib.cattail_stem(pl, props, pts, r=0.02, rnd=rnd, broken=True)

def snapped_with_cob(pl, props, rnd, base, az, h1, hang):
    """Stalk snapped at h1, upper part folded down with the cob hanging (as on the sheet, row 2 / 3)."""
    pts = plib.arc_path(base, az, 0.08, 0.2, h1, segs=4, p=1.0)
    d = V((math.sin(2.5) * math.cos(az), math.sin(2.5) * math.sin(az), math.cos(2.5)))
    for i in range(3):
        pts.append(pts[-1] + d * hang / 3)
    plib.cattail_stem(pl, props, pts, r=0.015, rnd=rnd, cob_from=0.8, cob_to=0.97, cob_r=0.04)

def cores(pl, rnd, h, n=2, regs=('cattail_leaves_0', 'cattail_leaves_1')):
    for k in range(n):
        a = math.pi * k / n + rnd.uniform(-.3, .3)
        reg = regs[k % len(regs)]
        rw, rh = sk.REG['T_Swamp_Plants.png']['regions'][reg][2:]
        pl.card(V((0, 0, 0)), hdir(a) * h * 0.5 * rw / rh * 1.3, V((0, 0, h)), reg, bend=h * .08, segs=3,
                wind_sway=0.05 * h, wind_flutter=0.02)

def cat_a(vid):
    """Tall leaning bunch: three cob stalks arching to +X, leaves spreading, a few snapped."""
    rnd = random.Random(21)
    pl = sk.Mesh('Plants'); pr = sk.Mesh('Props')
    lean = 0.0   # toward +X
    for (dy, h, t0, t1, cr) in ((0.0, 1.78, 0.12, 0.62, 0.036), (0.03, 1.55, 0.18, 0.75, 0.032),
                                (-0.03, 1.3, 0.22, 0.85, 0.03)):
        pts = plib.arc_path(V((-0.02, dy, 0)), lean + rnd.uniform(-.25, .25), t0, t1, h, segs=7, p=1.6)
        plib.cattail_stem(pl, pr, pts, r=0.016, rnd=rnd, cob_from=0.79, cob_to=0.94, cob_r=cr * 1.25)
    leaves_ring(pl, rnd, 9, 0.5, 0.85, 0.09, kinks=0.35, spread=(0.2, 0.7), droop=(0.5, 1.2))
    leaves_ring(pl, rnd, 4, 0.9, 1.25, 0.08, kinks=0.0, spread=(0.05, 0.2), droop=(0.2, 0.5), pref_az=lean, pref=0.6)
    cores(pl, rnd, 0.75)
    return plib.finish([pl, pr], vid)

def cat_b(vid):
    """Short broken clump: 4 upright cobs, a snapped stalk with a hanging cob, broken dead stalks, dense leaves."""
    rnd = random.Random(22)
    pl = sk.Mesh('Plants'); pr = sk.Mesh('Props')
    for (x, y, h, az, t) in ((0.0, 0.0, 1.18, 0.4, 0.05), (-0.06, 0.03, 0.98, 2.6, 0.14), (0.07, -0.02, 1.02, -0.6, 0.16),
                             (0.03, 0.06, 0.86, 1.5, 0.2)):
        pts = plib.arc_path(V((x, y, 0)), az, t * .4, t, h, segs=5, p=1.2)
        plib.cattail_stem(pl, pr, pts, r=0.016, rnd=rnd, cob_from=0.68, cob_to=0.9, cob_r=0.045)
    snapped_with_cob(pl, pr, rnd, V((-0.08, -0.04, 0)), 3.4, 0.62, 0.32)
    broken_stalk(pl, pr, rnd, V((0.09, 0.04, 0)), 0.6, 0.72, 0.3)
    broken_stalk(pl, pr, rnd, V((-0.04, 0.08, 0)), 2.0, 0.6, 0.35)
    broken_stalk(pl, pr, rnd, V((0.05, -0.07, 0)), -1.2, 0.36, 0.25)
    leaves_ring(pl, rnd, 11, 0.45, 0.75, 0.085, kinks=0.4, spread=(0.3, 0.9), droop=(0.5, 1.3))
    cores(pl, rnd, 0.55)
    return plib.finish([pl, pr], vid)

def cat_c(vid):
    """Wide clump: two upright cobs, one long stalk arching over with the cob hanging, broken stalks, wide leaves."""
    rnd = random.Random(23)
    pl = sk.Mesh('Plants'); pr = sk.Mesh('Props')
    for (x, y, h, az, t, cr) in ((-0.05, 0.0, 1.75, 2.8, 0.06, 0.037), (0.03, 0.02, 1.45, 0.3, 0.1, 0.034)):
        pts = plib.arc_path(V((x, y, 0)), az, t * .3, t, h, segs=6, p=1.2)
        plib.cattail_stem(pl, pr, pts, r=0.017, rnd=rnd, cob_from=0.77, cob_to=0.92, cob_r=cr * 1.25)
    # long arching stalk: rises, bends over to +X, cob hangs near the tip
    pts = plib.arc_path(V((0.04, -0.02, 0)), 0.1, 0.1, 3.05, 1.95, segs=11, p=2.6)
    plib.cattail_stem(pl, pr, pts, r=0.016, rnd=rnd, cob_from=0.82, cob_to=0.96, cob_r=0.04)
    broken_stalk(pl, pr, rnd, V((-0.1, 0.05, 0)), 2.6, 0.7, 0.45)
    broken_stalk(pl, pr, rnd, V((0.1, 0.06, 0)), 0.8, 0.55, 0.3)
    leaves_ring(pl, rnd, 11, 0.6, 0.95, 0.11, kinks=0.3, spread=(0.5, 1.05), droop=(0.6, 1.3))
    # wide low fan of broad leaves arching out to both sides (as on the sheet)
    for side_az in (0.0, math.pi):
        leaves_ring(pl, rnd, 7, 0.75, 1.1, 0.12, kinks=0.2, spread=(0.75, 1.2), droop=(0.4, 0.95), az0=side_az,
                    pref_az=side_az, pref=0.65)
    leaves_ring(pl, rnd, 5, 0.55, 0.85, 0.11, kinks=0.25, spread=(0.3, 0.7), droop=(0.6, 1.1))
    leaves_ring(pl, rnd, 3, 0.8, 1.0, 0.09, kinks=0.0, spread=(0.05, 0.2), droop=(0.2, 0.5))
    cores(pl, rnd, 0.75, n=3, regs=('cattail_leaves_0', 'cattail_leaves_1', 'sedge_2'))
    return plib.finish([pl, pr], vid)

VARIANTS = [('SW02a', 'CattailTallLean', cat_a), ('SW02b', 'CattailShortBroken', cat_b),
            ('SW02c', 'CattailWideArch', cat_c)]

def main(render=True):
    sk.reset()
    recs, allv, tiles, labels = [], [], [], []
    for vid, name, fn in VARIANTS:
        objs = fn(vid)
        sk.ground(objs)
        fbx = os.path.join(OUT, 'SM_%s_%s.fbx' % (vid[:4], name))
        sk.export_fbx(objs, fbx)
        recs.append(sk.variant_record(vid, name, objs, fbx, water_line_z=0.15, solidity='soft-passable', burns=True,
                                      collider='none (soft, passable)', moving_parts=None,
                                      notes='pivot at the root; stalks+leaves on Plants with wind UV2; cobs on Props '
                                            'carry the same wind UV2 values as their stalk (needs wind on the Props '
                                            'shader or cobs will not follow the swaying stalk); water_line_z is a '
                                            'suggested planting depth (shallow water/shore)'))
        allv.append(objs)
        if render:
            for j, (az, el) in enumerate([(-20, 10), (25, 10), (90, 6)]):
                f = os.path.join(OUT, '%s_%d.png' % (vid, j))
                sk.render(objs, f, az=az, el=el, size=(300, 300), samples=8)
                tiles.append(f); labels.append('%s %s az%d' % (vid, name, az))
    if render:
        sk.sheet(tiles, os.path.join(OUT, 'card.jpg'), cols=3, labels=labels, concept=SHEET,
                 title='SW02 Cattails — concept (top) vs model')
    json.dump(recs, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)
    plib.side_by_side(allv)
    sk.save_blend(os.path.join(OUT, 'SW02_cattails.blend'))
    for r in recs:
        print(r['id'], r['tris'], r['tris_by_material'], r['size_xyz_m'])

if __name__ == '__main__':
    main('--norender' not in sys.argv)
