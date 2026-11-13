"""SW08 Ольха на ходулях — stilt-root alders, 2 variants.
a: tall crooked trunk on four broad splayed stilt roots, layered flat crown pads
b: squat bent trunk on three unequal arching roots (one long arch to the left), low crown
Run: python3 build.py [norender]"""
import sys, os, math, random, json
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, '/home/claude/swamp/kit')
import sk
import treelib as T
from mathutils import Vector

OUT = os.path.join(HERE, 'out')
CONCEPT = '/home/claude/ref/swamp/sheets/08-stilt-alders.png'
WATER = 0.4   # water line above the pivot (root feet go 0.4 m under)


def make_wind(H, trunk_amp, crown_amp):
    def Wb(p):                       # bark: slight sway growing with height
        h = max(0.0, p.z - 1.5) / max(H - 1.5, 1)
        return (trunk_amp * h * h, 0.0)

    def Wc(p):                       # crown cards: stronger sway (+ flutter set per card)
        h = max(0.0, p.z - 1.5) / max(H - 1.5, 1)
        return (crown_amp * (0.35 + 0.65 * h * h), 0.0)
    return Wb, Wc


def root(bm, bmm, J, ang, reach, top_r, seed, Wb, drop=None, arch=0.25, sides=5, fork=True, flat=False):
    """Stilt root from inside the trunk junction J outwards and down into the water (z<0 at the foot).
    Arching tube: first runs out almost level, then bends down. Mossy bark on the lower half."""
    rnd = random.Random(seed)
    d = Vector((math.cos(ang), math.sin(ang), 0))
    Jz = J.z
    ctrl = [J + Vector((0, 0, 0.25)),
            J + d * reach * 0.28 + Vector((0, 0, -Jz * 0.06 + arch * 0.4)),
            J + d * reach * 0.58 + Vector((0, 0, -Jz * 0.3 + arch * 0.2)),
            J + d * reach * 0.84 + Vector((0, 0, -Jz * 0.68)),
            J + d * reach * 0.98 + Vector((0, 0, -Jz * 0.92)),
            J + d * reach * 1.04 + Vector((0, 0, -Jz - 0.02))]
    if flat:
        ctrl = [J + Vector((0, 0, 0.25)),
                J + d * reach * 0.3 + Vector((0, 0, -Jz * 0.05 + arch * 0.25)),
                J + d * reach * 0.6 + Vector((0, 0, -Jz * 0.18 + arch * 0.1)),
                J + d * reach * 0.85 + Vector((0, 0, -Jz * 0.5)),
                J + d * reach * 0.97 + Vector((0, 0, -Jz * 0.85)),
                J + d * reach * 1.0 + Vector((0, 0, -Jz - 0.02))]
    ctrl = [Vector((p.x, p.y, p.z)) for p in ctrl]
    rr = [top_r * 1.05, top_r, top_r * .9, top_r * .85, top_r * .9, top_r * 1.0]
    path, rad = T.resample(ctrl, rr, 8, amp=0.06, seed=seed, keep_first=1)
    lobes = {len(path) - 1: [rnd.uniform(1.15, 1.5) if k % 2 else 1.0 for k in range(sides)],
             len(path) - 2: [rnd.uniform(1.0, 1.25) for k in range(sides)]}
    n = len(path)
    # mossy bark on the upper side of the upper half of the root (k=0 is the top side), plain bark below
    T.tube(lambda i, k: bmm if (i < n - 3 and (k in (0, 1, sides - 1)) and rnd.random() < .85) else bm, path, rad,
           sides=sides, rot=rnd.uniform(0, 1), lobes=lobes, seed=seed, jitter=0.05, wind=Wb, caps=(False, True))
    if fork:
        # small side root splitting off near the foot (knee)
        s = T.lerp_path(path, 0.62)
        sd = (d + Vector((-d.y, d.x, 0)) * rnd.choice((-1, 1)) * rnd.uniform(.6, 1.0)).normalized()
        sp = [s, s + sd * 0.35 + Vector((0, 0, -0.1)), s + sd * 0.6 + Vector((0, 0, -s.z * 0.55)),
              s + sd * 0.7 + Vector((0, 0, -s.z - 0.02))]
        sr = top_r * 0.45
        T.tube(bmm, sp, [sr, sr * .85, sr * .8, sr * .95], sides=4, seed=seed + 5, jitter=0.05, wind=Wb,
               caps=(False, True))
    return path


def pad(pl, c, R, seed, Wc, flutter=0.035, layers=3, tilt=0.12):
    """Flat layered crown pad of alder_clump cards (like the forest crowns): a low ring, middle ring,
    top cards and tilted rim cards that give the pad a visible edge from the side."""
    rnd = random.Random(seed)
    c = Vector(c)
    regs = ('alder_clump_0', 'alder_clump_1')

    def card(cen, size, yaw, pitch, roll=0.0):
        cy, sy = math.cos(yaw), math.sin(yaw)
        Rv = Vector((cy, sy, 0)) * size * 0.62
        Fv = Vector((-sy, cy, 0)) * size * 0.48
        # pitch tilts the card around its right axis (outer edge down), roll around forward
        Fv = Fv * math.cos(pitch) + Vector((0, 0, -size * 0.48 * math.sin(pitch)))
        Rv = Rv * math.cos(roll) + Vector((0, 0, size * 0.62 * math.sin(roll)))
        T.clump(pl, cen, Rv, Fv, rnd.choice(regs), lift=size * .1, droop=size * .12, wind=Wc,
                flutter=flutter, seed=rnd.randint(0, 9999))

    a0 = rnd.uniform(0, 6.28)
    rings = [  # (count, radial pos, z, size, pitch range, angle offset)
        (8, .6, -.06, (.78, .92), (.04, .18), 0.0),     # bottom ring, widest, almost flat
        (9, .88, -.13, (.48, .6), (.28, .5), .4),       # rim: slight droop at the edge
        (6, .34, .04, (.7, .85), (0.0, .14), .2),       # middle ring
        (3, .12, .1, (.6, .7), (0.0, .1), .9),          # top
    ]
    for cnt, rp, z, sz, pr, off in rings:
        for i in range(cnt):
            a = a0 + off + i * 2 * math.pi / cnt + rnd.uniform(-.25, .25)
            d = Vector((math.cos(a), math.sin(a), 0))
            card(c + d * R * rp + Vector((0, 0, z * R)), R * rnd.uniform(*sz), a + math.pi / 2,
                 rnd.uniform(*pr), rnd.uniform(-tilt, tilt))


def limb(bk, start, end, r0, r1, seed, Wb, sides=5, bend=0.12, sub=2):
    pts = T.crook([start, end], bend, seed, sub=sub)
    rr = [r0 + (r1 - r0) * i / (len(pts) - 1) for i in range(len(pts))]
    T.tube(bk, pts, rr, sides=sides, seed=seed, jitter=0.04, wind=Wb, caps=(False, True))
    return pts


def crown_branch(bk, pl, src, pad_c, R, r0, seed, Wb, Wc, twigs=3):
    """Branch from `src` (inside trunk/limb) to just under a pad centre + short twigs fanning into the pad."""
    rnd = random.Random(seed)
    pad_c = Vector(pad_c)
    end = pad_c + Vector((0, 0, -0.22 * R))
    pts = limb(bk, src, end, r0, r0 * 0.45, seed, Wb, sides=5 if r0 > 0.07 else 4)
    for t in range(twigs):
        a = rnd.uniform(0, 6.28)
        e = pad_c + Vector((math.cos(a), math.sin(a), 0)) * R * rnd.uniform(.45, .7) + Vector((0, 0, -0.18 * R))
        s = T.lerp_path(pts, rnd.uniform(.7, .9))
        limb(bk, s, e, r0 * 0.4, r0 * 0.18, seed * 3 + t, Wb, sides=4, bend=0.1, sub=1)
    pad(pl, pad_c, R * 1.12, seed + 1000, Wc)


def finish(vid, bk, bm, pl, Wb=None):
    ob = bk.to_object(vid + '_Bark')
    om_ = bm.to_object(vid + '_BarkMossy')
    moss = sk.moss_drape(om_, vid + '_Moss', min_nz=0.45, keep=0.7, seed=4, offset=0.02)
    moss2 = sk.moss_drape(ob, vid + '_Moss2', min_nz=0.6, keep=0.5, seed=5, offset=0.02)
    if moss and moss2:
        # join both moss skins into one '<vid>_Moss' object (one object per material)
        import bpy
        for o in bpy.context.view_layer.objects:
            o.select_set(False)
        moss.select_set(True); moss2.select_set(True)
        bpy.context.view_layer.objects.active = moss
        bpy.ops.object.join()
    elif moss2:
        moss = moss2; moss.name = vid + '_Moss'
    if Wb:
        T.apply_wind(moss, Wb)
    op = pl.to_object(vid + '_Plants')
    objs = [o for o in (ob, om_, moss, op) if o]
    return objs


def ground_roots(objs, feet):
    """Pivot: XY = centre of the root feet, lowest point z=0."""
    from mathutils import Matrix
    lo, hi = sk.bounds(objs)
    cx = sum(f.x for f in feet) / len(feet); cy = sum(f.y for f in feet) / len(feet)
    d = Vector((-cx, -cy, -lo[2]))
    for o in objs:
        o.data.transform(Matrix.Translation(d))


# ------------------------------------------------------------------ variant a: tall on four roots
def build_a():
    vid = 'SW08a'
    H = 7.0
    Wb, Wc = make_wind(H, 0.05, 0.13)
    bk, bm, pl = sk.Mesh('Bark'), sk.Mesh('BarkMossy'), sk.Mesh('Plants')
    J = Vector((0, 0, 2.3))
    feet = []
    for i, (ang, reach, r, arch) in enumerate(((-2.35, 2.55, 0.56, 0.25), (-0.75, 2.45, 0.58, 0.3),
                                                (0.8, 2.25, 0.5, 0.2), (2.45, 2.5, 0.53, 0.3))):
        p = root(bk, bm, J, ang, reach, r, 10 + i, Wb, arch=arch)
        feet.append(p[-1])
    # crooked S trunk
    ctrl = [J + Vector((0, 0, -0.45)), J + Vector((0, 0, 0.1)), (0.35, -0.1, 3.3), (0.0, 0.05, 4.05),
            (-0.5, 0.15, 4.6), (-0.4, 0.1, 5.2), (0.0, -0.05, 5.75)]
    cr = [0.76, 0.84, 0.68, 0.57, 0.48, 0.4, 0.32]
    path, rad = T.resample(ctrl, cr, 10, amp=0.05, seed=3, keep_first=2)
    rnd = random.Random(7)
    lobes = {0: [1.0, 1.25, 1.0, 1.3, 1.1], 1: [1.1, 1.2, 1.15, 1.25, 1.1]}
    T.tube(lambda i, k: bm if (i < 4 and (k < 3 or rnd.random() < .5)) else bk, path, rad, sides=5, rot=0.4,
           lobes=lobes, seed=3, jitter=0.05, wind=Wb, caps=(True, False),
           cap_apex=(J + Vector((0.05, 0.0, -0.75)), None))
    top = path[-1]
    # crown pads: (centre, radius) -- big top, upper left/right, lower left, side branch pad to the right
    pads = [((0.15, 0.05, 6.75), 1.65), ((-1.75, 0.35, 6.05), 1.45), ((1.6, -0.35, 5.8), 1.4),
            ((-0.45, 1.35, 5.35), 1.25), ((-1.0, -1.3, 5.45), 1.15)]
    srcs = [0.98, 0.82, 0.85, 0.75, 0.8]
    for j, ((c, R), t) in enumerate(zip(pads, srcs)):
        crown_branch(bk, pl, T.lerp_path(path, t), c, R, 0.2 if j else 0.24, 30 + j, Wb, Wc)
    # trunk top continues into the top pad
    limb(bk, path[-2], Vector((0.1, 0.0, 6.35)), rad[-1], 0.08, 77, Wb)
    # low side branch to the right with its own pad (concept: the lone pad sticking out right)
    lb = crown_branch(bk, pl, T.lerp_path(path, .42), (2.45, -0.4, 4.35), 1.1, 0.19, 50, Wb, Wc)
    objs = finish(vid, bk, bm, pl, Wb)
    ground_roots(objs, feet)
    return vid, 'StiltAlderTall', objs


# ------------------------------------------------------------------ variant b: squat bent trunk on three roots
def build_b():
    vid = 'SW08b'
    H = 4.5
    Wb, Wc = make_wind(H, 0.04, 0.12)
    bk, bm, pl = sk.Mesh('Bark'), sk.Mesh('BarkMossy'), sk.Mesh('Plants')
    J = Vector((0.2, 0, 1.9))
    feet = []
    # long low arch to the left, shorter root to the right-front, back root
    for i, (ang, reach, r, arch) in enumerate(((math.pi + 0.15, 3.1, 0.64, 0.9), (-0.35, 2.3, 0.58, 0.45),
                                                (1.45, 1.9, 0.5, 0.3))):
        p = root(bk, bm, J, ang, reach, r, 60 + i, Wb, arch=arch, sides=6 if i == 0 else 5, flat=(i == 0))
        feet.append(p[-1])
    # thick trunk bends right then curls back up-left to the crown
    ctrl = [J + Vector((-0.1, 0, -0.4)), J + Vector((0, 0, 0.1)), (0.75, -0.05, 2.55), (0.95, 0.0, 3.0),
            (0.7, 0.05, 3.4), (0.35, 0.0, 3.7), (0.15, -0.05, 3.95)]
    cr = [0.82, 0.9, 0.76, 0.6, 0.47, 0.37, 0.3]
    path, rad = T.resample(ctrl, cr, 9, amp=0.04, seed=63, keep_first=2)
    rnd = random.Random(9)
    lobes = {0: [1.0, 1.2, 1.05, 1.3, 1.1, 1.15], 1: [1.1, 1.15, 1.2, 1.1, 1.2, 1.1]}
    T.tube(lambda i, k: bm if (i < 5 and (k < 3 or rnd.random() < .45)) else bk, path, rad, sides=6, rot=0.2,
           lobes=lobes, seed=63, jitter=0.05, wind=Wb, caps=(True, False),
           cap_apex=(J + Vector((-0.1, 0.0, -0.7)), None))
    pads = [((-1.0, 0.15, 4.45), 1.55), ((0.75, -0.15, 4.75), 1.25), ((-2.45, -0.3, 3.75), 1.2),
            ((2.25, 0.35, 3.75), 1.1)]
    srcs = [0.97, 0.95, 0.85, 0.72]
    for j, ((c, R), t) in enumerate(zip(pads, srcs)):
        crown_branch(bk, pl, T.lerp_path(path, t), c, R, 0.19 if j else 0.22, 70 + j, Wb, Wc)
    objs = finish(vid, bk, bm, pl, Wb)
    ground_roots(objs, feet)
    return vid, 'StiltAlderLow', objs


VARIANTS = [build_a, build_b]
VIEWS = [(-35, 14, 'front 3/4', None), (145, 14, 'rear 3/4', None), (90, 12, 'side', None),
         (-30, 20, 'in water', WATER)]


def main(render=True):
    sk.reset()
    recs, tiles, labels, built = [], [], [], []
    for f in VARIANTS:
        built.append(f())
    for vid, name, objs in built:
        fbx = os.path.join(OUT, 'SM_SW08_%s.fbx' % name)
        sk.export_fbx(objs, fbx)
        rec = sk.variant_record(vid, name, objs, fbx, water_line_z=WATER, solidity='solid', burns=True,
                                collider='trunk + stilt roots solid (capsules per root/trunk segment or simplified '
                                         'mesh of the Bark/BarkMossy objects); branches above head height and '
                                         'crown cards (Plants) no collider. Open gaps under the roots stay passable '
                                         'only if the scene wants it (clearance under the junction ~%.1f m).' % (
                                             1.9 if vid.endswith('a') else 1.5),
                                moving_parts=None,
                                notes='Pivot = centre of the root feet at the bottom of the feet (feet go %.2f m '
                                      'under the water line). Objects: Bark (trunk top, branches, twigs), BarkMossy '
                                      '(lower trunk + stilt roots), Moss (drape on upward faces), Plants (layered '
                                      'alder_clump_0/1 crown cards, 8 tris each). Wind UV2: bark sway up to ~0.05 m, '
                                      'crown cards sway up to ~0.13 m + flutter 0.035 m.' % WATER)
        recs.append(rec)
        print(vid, name, rec['tris'], rec['tris_by_material'], rec['size_xyz_m'])
        if render:
            for az, el, lab, wz in VIEWS:
                png = os.path.join(HERE, 'work', '%s_%d_%s.png' % (vid, az, 'w' if wz else 'd'))
                sk.render(objs, png, az=az, el=el, size=(340, 340), samples=10, water_z=wz, margin=0.95)
                tiles.append(png); labels.append('%s %s %s az%d' % (vid, name, lab, az))
    for i, (vid, name, objs) in enumerate(built):
        for o in objs:
            o.location.x = i * 7.0
    sk.save_blend(os.path.join(OUT, 'SW08_stilt_alders.blend'))
    json.dump(recs, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)
    if render:
        sk.sheet(tiles, os.path.join(OUT, 'card.jpg'), 4, labels=labels, concept=CONCEPT,
                 title='SW08 stilt alders (Olha na hoduljah): concept (top) vs model (front 3/4, rear 3/4, side, in water)')


if __name__ == '__main__':
    main(render='norender' not in sys.argv)
