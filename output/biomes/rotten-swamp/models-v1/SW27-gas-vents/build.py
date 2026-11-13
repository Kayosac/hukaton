"""SW27 Газовое жерло — gas vents, 2 variants. python3 build.py [render|norender|bf] [only-letters]"""
import sys, os, math, random, json
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, '/home/claude/swamp/items/SW10-uprooted-snags'); sys.path.insert(0, '/home/claude/swamp/kit')
import sk
from dwlib import *

OUT = os.path.join(HERE, 'out')
SHEET = '/home/claude/ref/swamp/sheets/27-gas-vents.png'
GROUND = 0.0


def mud_face(mud, pts):
    """Mud face with per-face dominant-axis planar UVs (no stretching on the steep crater/mound sides)."""
    pts = [V(p) for p in pts]
    n = (pts[1] - pts[0]).cross(pts[2] - pts[0])
    if len(pts) == 4:
        n += (pts[2] - pts[0]).cross(pts[3] - pts[0])
    if n.length < 1e-10:
        return
    n.normalize()
    t = sk.MATS[mud.mat][2]
    if abs(n.z) > .6:
        uvs = [(p.x / t, p.y / t) for p in pts]
    else:
        h = V((-n.y, n.x, 0)).normalized()
        uvs = [(p.dot(h) / t, p.z / t) for p in pts]
    mud.face(pts, uvs)


def region_face(props, pts, region, centre, extent):
    u0, v0, u1, v1 = props.region(region)
    C = V(centre)
    uvs = []
    for p in pts:
        d = V(p) - C
        a = min(1, max(0, d.x / extent * .5 + .5)); b = min(1, max(0, (d.y + d.z * .5) / extent * .5 + .5))
        uvs.append((u0 + a * (u1 - u0), v0 + b * (v1 - v0)))
    props.face([V(p) for p in pts], uvs)


def vent_mound(mud, props, S=14, R=(.62, .5), mouth=(0, 0), mrad=(.17, .15), mang=0.0, top_z=.45, rim_tilt=(0, 0),
               depth=.65, seed=0, tongues=4, lump=.04, profile_p=1.6, slope_rings=4, skirt=.12, centre=(0, 0),
               outline_jit=.1, crust_rings=1, peak=None):
    """Mud mound around a deep vent mouth. Returns dict with mouth centre/normal and the ring grid."""
    rnd = random.Random(seed)
    M = V((mouth[0], mouth[1], 0))
    Cc = V((centre[0], centre[1], 0))
    ca, sa = math.cos(mang), math.sin(mang)
    rimz = []
    rim = []
    base = []
    for s in range(S):
        a = 2 * math.pi * s / S
        dx, dy = math.cos(a) * mrad[0], math.sin(a) * mrad[1]
        dx, dy = dx * ca - dy * sa, dx * sa + dy * ca
        f = 1 + rnd.uniform(-.12, .12)
        z = top_z + rim_tilt[0] * math.cos(a) + rim_tilt[1] * math.sin(a) + rnd.uniform(-.02, .02)
        rim.append(V((M.x + dx * f, M.y + dy * f, z)))
        # outline point along the same direction from the mound centre
        dir_ = V((math.cos(a), math.sin(a), 0))
        bf = 1 + rnd.uniform(-outline_jit, outline_jit)
        base.append(V((Cc.x + dir_.x * R[0] * bf, Cc.y + dir_.y * R[1] * bf, -.03)))
    # slope rings from rim (j=0) to base (j=slope_rings)
    grid = [rim]
    for j in range(1, slope_rings + 1):
        s_ = j / slope_rings
        ring = []
        for k in range(S):
            p = rim[k].lerp(base[k], s_)
            zr = rim[k].z
            z = zr * (1 - s_ ** profile_p) + base[k].z * s_ ** profile_p
            if peak is not None:
                # extra hump (tilted / lumpy mound): raise points near the peak position
                pk, ph, pr = peak
                dd = (V((p.x, p.y, 0)) - V((pk[0], pk[1], 0))).length
                z += ph * max(0, 1 - dd / pr) ** 1.5 * (1 - s_) ** .5
            if 0 < j < slope_rings:
                z += rnd.uniform(-lump, lump)
                p += V((rnd.uniform(-lump, lump), rnd.uniform(-lump, lump), 0))
            ring.append(V((p.x, p.y, z)))
        grid.append(ring)
    # skirt ring sinking into the ground (no visible bottom edge)
    grid.append([V((Cc.x + (p.x - Cc.x) * (1 + skirt / max(R)), Cc.y + (p.y - Cc.y) * (1 + skirt / max(R)), -.14))
                 for p in grid[-1]])
    tongue_ks = set(rnd.sample(range(S), tongues))
    tongue_len = {k: rnd.choice((1, 1, 2, 2, 3)) for k in range(S)}
    ext = max(R) * .9
    for j in range(len(grid) - 1):
        for k in range(S):
            k2 = (k + 1) % S
            q = [grid[j + 1][k], grid[j + 1][k2], grid[j][k2], grid[j][k]]
            if j < crust_rings or (k in tongue_ks and j < crust_rings + tongue_len[k]):
                region_face(props, q, 'vent_crust', M, ext)
            else:
                mud_face(mud, q)
    # throat: rings going down into a deep hole; upper band crust, the rest dark
    nrm = V((-rim_tilt[0], -rim_tilt[1], max(.2, 2 * max(mrad)))).normalized()
    nrm = (rim[0] - rim[0]).normalized() if False else nrm
    rc = sum(rim, V()) / S
    throat = [rim]
    for d, sc in ((.07, .9), (.25, .75), (.45, .62), (depth, .5)):
        throat.append([rc + (p - rc) * sc - nrm * d + V((rnd.uniform(-.01, .01), rnd.uniform(-.01, .01), 0))
                       for p in rim])
    for j in range(len(throat) - 1):
        for k in range(S):
            k2 = (k + 1) % S
            # inner wall faces point towards the hole axis
            q = [throat[j][k], throat[j][k2], throat[j + 1][k2], throat[j + 1][k]]
            region_face(props, q, 'vent_crust' if j == 0 else 'vent_hole', rc, max(mrad) * 2)
    poly_cap(props, list(reversed(throat[-1])), nrm, region='vent_hole')
    return {'mouth': rc, 'normal': nrm, 'grid': grid, 'rim': rim, 'base': base}


def lump(mud, centre, R, h, seed, S=9, rings=3):
    """Small low-poly mud lump merging into the mound (domes of 9 broad faces)."""
    rnd = random.Random(seed)
    C = V(centre)
    apex = C + V((rnd.uniform(-.05, .05) * R[0], rnd.uniform(-.05, .05) * R[1], h))
    grid = []
    for j in range(1, rings + 1):
        s_ = j / rings
        ring = []
        for k in range(S):
            a = 2 * math.pi * k / S + rnd.uniform(-.1, .1)
            f = 1 + rnd.uniform(-.12, .12)
            r = s_ ** .8
            ring.append(V((C.x + math.cos(a) * R[0] * r * f, C.y + math.sin(a) * R[1] * r * f,
                           C.z + h * (1 - s_ ** 2) + rnd.uniform(-.02, .02) * (j < rings))))
        grid.append(ring)
    grid.append([C + (p - C) * 1.12 - V((0, 0, .14)) for p in grid[-1]])
    for k in range(S):
        k2 = (k + 1) % S
        mud_face(mud, [grid[0][k], grid[0][k2], apex])
    for j in range(len(grid) - 1):
        for k in range(S):
            k2 = (k + 1) % S
            mud_face(mud, [grid[j + 1][k], grid[j + 1][k2], grid[j][k2], grid[j][k]])


def plants(pl, spots, seed):
    rnd = random.Random(seed)
    for i, (x, y, kind) in enumerate(spots):
        if kind == 'cat':
            pl.cross_cards((x, y, GROUND - .04), .3, rnd.uniform(.9, 1.15), 'cattail_leaves_%d' % (i % 2), n=2,
                           rot=rnd.uniform(0, 3), seed=seed + i, wind_sway=.06, wind_flutter=.02)
        else:
            sedge(pl, (x, y, GROUND - .04), h=rnd.uniform(.35, .5), w=rnd.uniform(.3, .42), seed=seed + i)


# ------------------------------------------------------------------ A: low round vent
def build_a():
    vid = 'SW27a'
    mud, props, pl, fun = sk.Mesh('Peat'), sk.Mesh('Props'), sk.Mesh('Plants'), sk.Mesh('Props')
    info = vent_mound(mud, props, S=16, R=(.64, .54), mouth=(.02, .04), mrad=(.22, .19), top_z=.4, rim_tilt=(0, .03),
                      depth=.7, seed=5, tongues=8, lump=.035, profile_p=1.7, slope_rings=6, crust_rings=2)
    lump(mud, (-.38, .3, -.02), (.22, .2), .14, 8)
    plants(pl, [(-.66, .02, 's'), (.66, -.1, 's'), (.5, .45, 'cat'), (-.35, .55, 's')], 20)
    mushroom_cluster(fun, V((-.52, -.28, .02)), (0, 0, 1), seed=31, n=3, size=1.3)
    fo = fun.to_object(vid + '_Fungus')
    objs = finish(vid, {'Peat': mud, 'Props': props, 'Plants': pl}, moss_from=['Peat'],
                  moss_kw=dict(min_nz=.65, keep=.6, seed=11, min_z=.2), drips=False, extra=[fo])
    return objs, info


# ------------------------------------------------------------------ B: tilted slit vent
def build_b():
    vid = 'SW27b'
    mud, props, pl, fun = sk.Mesh('Peat'), sk.Mesh('Props'), sk.Mesh('Plants'), sk.Mesh('Props')
    # big tilted lump: peak at the back-right, slit mouth on the front slope below the peak
    info = vent_mound(mud, props, S=16, R=(.95, .72), mouth=(.22, -.08), mrad=(.32, .045), mang=math.radians(-20),
                      top_z=.82, rim_tilt=(.08, .14), depth=.65, seed=9, tongues=8, lump=.05, profile_p=.9,
                      slope_rings=7, centre=(-.1, .05), outline_jit=.12, peak=((.4, .22), .36, .55), crust_rings=2)
    lump(mud, (-.62, -.25, -.02), (.32, .28), .26, 12, S=8)
    lump(mud, (.55, -.35, -.02), (.3, .26), .22, 13, S=8)
    lump(mud, (.7, .35, -.02), (.25, .22), .18, 14, S=7)
    plants(pl, [(-1.0, .05, 's'), (1.0, -.05, 'cat'), (-.4, .7, 'cat'), (.8, .6, 's')], 40)
    mushroom_cluster(fun, V((-.88, -.38, .02)), (0, 0, 1), seed=41, n=3, size=1.2)
    fo = fun.to_object(vid + '_Fungus')
    objs = finish(vid, {'Peat': mud, 'Props': props, 'Plants': pl}, moss_from=['Peat'],
                  moss_kw=dict(min_nz=.6, keep=.65, seed=12, min_z=.35), drips=False, extra=[fo])
    return objs, info


VARIANTS = [
    ('a', 'GasVentRound', 'Газовое жерло: низкое круглое', build_a),
    ('b', 'GasVentSlit', 'Газовое жерло: наклонное щелевидное', build_b),
]


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else 'render'
    only = sys.argv[2] if len(sys.argv) > 2 else None
    sk.reset()
    groups, recs, cardv = [], [], []
    for L, name, ru, fn in VARIANTS:
        objs, info = fn()
        d = sk.ground(objs)
        gl = round(GROUND + d.z, 3)
        m = info['mouth'] + d
        fbx = os.path.join(OUT, 'SM_SW27_%s.fbx' % name)
        sk.export_fbx(objs, fbx)
        rec = sk.variant_record('SW27' + L, name, objs, fbx, name_ru=ru, water_line_z=None, ground_line_z=gl,
                                solidity='walk-on-top', burns=False,
                                tris_by_object={o.name: sk.tris([o]) for o in objs},
                                vfx_vent_mouth_local_blender_xyz=[round(m.x, 3), round(m.y, 3), round(m.z, 3)],
                                vfx_vent_axis_blender=[round(c, 3) for c in info['normal']],
                                vfx_note='bubbles/gas VFX spawn at the mouth centre (rim level), rising along the '
                                         'axis; the hole goes ~0.6-0.7 m down (dark Props vent_hole); rust vent_crust '
                                         'ring + tongues around the mouth. Coordinates are Blender-local (X right, '
                                         'Y = back, Z up; front faces -Y), same frame as the FBX pivot; z = height '
                                         'above the pivot',
                                collider='mound as a convex hull of the Peat object (walkable), mouth area: trigger '
                                         'volume for the gas zone (game side); Plants/Fungus no collider',
                                separate_objects={'SW27%s_Fungus' % L: 'Props material; removable mushrooms at the base'},
                                moving_parts=None, pivot='bottom centre; the skirt sinks 0.14 m into the ground',
                                notes='set the mound into the bog so ground_line_z is at the ground surface')
        recs.append(rec); groups.append(objs); cardv.append(('SW27' + L + ' ' + name, objs, None))
        print(rec['id'], rec['tris'], rec['size_xyz_m'], rec['tris_by_object'], 'mouth', rec['vfx_vent_mouth_local_blender_xyz'])
    json.dump(recs, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)
    if mode == 'render':
        make_card(cardv, OUT, SHEET, 'SW27 Gazovoe zherlo / gas vents - concept (top) vs model (a, b)',
                  views=((-30, 22), (150, 22), (90, 14)), extra_view=(-20, 55), margin=.8,
                  only=[('SW27' + c) for c in only] if only else None)
    if mode == 'bf':
        for lab, objs, wl in cardv:
            backface_render(objs, os.path.join(HERE, 'work', 'bf_%s.png' % lab.split()[0]), el=40)
    place_side_by_side(groups, 1.0)
    sk.save_blend(os.path.join(OUT, 'SW27_GasVents.blend'))


if __name__ == '__main__':
    main()
