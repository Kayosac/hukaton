"""SW18 Колодец-журавль (well sweep) - 1 variant, 3 FBX: base (crib + forked post), lever (pivot on the axle),
bucket with its rope (pivot at the hang point on the lever tip).   python3 build.py [norender]"""
import sys, os, math, json, random
sys.path.insert(0, '/home/claude/swamp/kit')
sys.path.insert(0, '/home/claude/swamp/items/SW16-sunken-hut')
import sk, hutlib as H
from mathutils import Vector, Matrix

D = os.path.dirname(os.path.abspath(__file__))
OUT = D + '/out'
SHEET = '/home/claude/ref/swamp/sheets/18-well-sweep.png'


def strand(K, path, r, region, sides=4, seg_len=0.55, seed=0):
    """Thin tube (rope / iron) with UVs inside an atlas region (Props); length mapped along region U,
    restarting every seg_len metres."""
    rnd = random.Random(seed)
    u0, v0, u1, v1 = sk.Mesh.region(region)
    P = [Vector(p) for p in path]
    # resample
    pts = [P[0]]
    for a, b in zip(P[:-1], P[1:]):
        n = max(1, int(math.ceil((b - a).length / seg_len)))
        for i in range(1, n + 1):
            pts.append(a + (b - a) * (i / n))
    rings = []
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        up = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
        up = (up - t * up.dot(t)).normalized()
        sd = t.cross(up)
        rings.append([p + up * math.cos(2 * math.pi * k / sides + 0.4) * r + sd * math.sin(2 * math.pi * k / sides + 0.4) * r
                      for k in range(sides)])
    M = K['Props']
    for i in range(len(rings) - 1):
        for k in range(sides):
            k2 = (k + 1) % sides
            va, vb = v0 + (v1 - v0) * k / sides, v0 + (v1 - v0) * (k + 1) / sides
            M.quad(rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k], (u0, va), (u0, vb), (u1, vb), (u1, va))
    return rings


def ring_around(K, c, axis, r, w, region='rope', n=7, seed=0):
    """Lashing / hoop: short open band of quads around an axis."""
    axis = Vector(axis).normalized()
    up = Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((1, 0, 0))
    up = (up - axis * up.dot(axis)).normalized()
    sd = axis.cross(up)
    u0, v0, u1, v1 = sk.Mesh.region(region)
    C = Vector(c)
    pts = [[C + axis * (s * w / 2) + (up * math.cos(2 * math.pi * k / n) + sd * math.sin(2 * math.pi * k / n)) * r
            for k in range(n)] for s in (-1, 1)]
    for k in range(n):
        k2 = (k + 1) % n
        ua, ub = u0 + (u1 - u0) * k / n, u0 + (u1 - u0) * (k + 1) / n
        # outward normal: (b - a) x axis ... order chosen so faces point out
        K['Props'].quad(pts[0][k], pts[0][k2], pts[1][k2], pts[1][k], (ua, v0), (ub, v0), (ub, v1), (ua, v1))
        # flat rim faces (top / bottom of the band) so it reads from both ends
    return pts


def build():
    rnd = random.Random(18)
    # ------------------------------------------------ base: crib + forked post
    KB = H.Kit('SW18a', seed=1)
    cy = 0.85                        # crib centre y
    S = 1.4                          # crib (log centre lines)
    d, r = 0.27, 0.17
    tilt = math.radians(3)
    # crib built at origin then moved/tilted below
    KC = KB
    from copy import deepcopy
    crib_rows = 3
    H.cabin(KC, S, S, 0.0, crib_rows, d, r, sides=6, rot=0.0, sq=(1.0, 0.75), seed=181, flush=False,
            ext=(0.06, 0.12), broken={'left': {2: (-0.2, 0.25)}})
    ctop = d * (crib_rows - 0.5) + r
    for (x, y) in ((-S / 2, -S / 2), (S / 2, -S / 2), (-S / 2, S / 2), (S / 2, S / 2)):
        H.post(KC, x + (0.1 if x < 0 else -0.1) * 0 + (-0.1 if x < 0 else 0.1), y + (-0.1 if y < 0 else 0.1),
               -0.12, ctop + rnd.uniform(0.08, 0.16), w=0.18, seed=rnd.randint(0, 999))
    # rim boards
    for i, (a, b) in enumerate((((-S / 2 - 0.15, -S / 2 - 0.02), (S / 2 + 0.15, -S / 2 - 0.02)),
                                ((-S / 2 - 0.15, S / 2 + 0.02), (S / 2 + 0.15, S / 2 + 0.02)),
                                ((-S / 2 - 0.02, -S / 2 + 0.1), (-S / 2 - 0.02, S / 2 - 0.1)),
                                ((S / 2 + 0.02, -S / 2 + 0.1), (S / 2 + 0.02, S / 2 - 0.1)))):
        H.beam(KC, (a[0], a[1], ctop + 0.035), (b[0], b[1], ctop + 0.035 + rnd.uniform(-.02, .02)), 0.26, 0.07,
               seed=rnd.randint(0, 999), kinks=1)
    # dark well shaft (looking down) - a quad facing up inside the crib
    u0, v0, u1, v1 = sk.Mesh.region('dark')
    h = S / 2 - r * 0.7
    KC['Props'].quad((-h, -h, ctop - 0.35), (h, -h, ctop - 0.35), (h, h, ctop - 0.35), (-h, h, ctop - 0.35),
                     (u0, v0), (u1, v0), (u1, v1), (u0, v1))
    KC.drape('LogWall', keep=0.55, min_nz=0.6, seed=3)
    KC.drape('Planks', keep=0.4, min_nz=0.6, seed=4)
    # crib: tilt + move before the post is added (apply to existing bmesh verts)
    Mc = Matrix.Translation((0, cy, 0)) @ Matrix.Rotation(tilt, 4, 'X') @ Matrix.Rotation(-0.06, 4, 'Z') @ Matrix.Rotation(math.radians(2), 4, 'Y')
    for m in KB.m.values():
        for v in m.bm.verts:
            v.co = Mc @ v.co
    # forked post (BarkMossy)
    B = Vector((-0.02, -0.32, -0.4))
    F = Vector((0.16, -0.68, 2.4))
    H.log(KB, B, F, 0.42, sides=8, kinks=3, mat='BarkMossy', r1=0.33, seed=5, caps=(True, False), wob=0.05,
          rjit=0.08)
    prong_top = []
    for sx in (-1, 1):
        p0 = F + Vector((sx * 0.1, 0, -0.35))
        p1 = F + Vector((sx * 0.56, -0.06 + sx * 0.02, 1.55 + sx * 0.08))
        H.log(KB, p0, p1, 0.25, sides=7, kinks=2, mat='BarkMossy', r1=0.17, seed=6 + sx, caps=(False, True),
              wob=0.03)
        prong_top.append(p1)
    A = F + Vector((0, -0.04, 1.2))            # axle centre (lever pivot)
    H.log(KB, A + Vector((-0.8, 0, 0)), A + Vector((0.8, 0, 0)), 0.06, sides=6, kinks=0, mat='Props',
          band_mode=False, cap_region='end_grain_grey', seed=8)
    # wedge/lashing at fork
    ring_around(KB, F + Vector((0, 0, -0.05)), (0.0, 0.08, 1), 0.36, 0.12, seed=9)
    # grass tufts around crib and post
    for i, p in enumerate([(-0.85, 0.3), (0.8, 0.5), (0.75, 1.75), (-0.7, 1.7), (-0.35, -0.75), (0.35, -0.5),
                           (0.05, 1.75), (-0.85, 1.05)]):
        H.grass(KB, (p[0], p[1], 0.02), n=2, h=(0.45, 0.8), seed=40 + i, spread=0.18)
    base = KB.build()

    # ------------------------------------------------ lever (beam + counterweight) in world pose
    KL = H.Kit('SW18a_Lever', seed=2)
    alpha = math.radians(16)
    def onbeam(s, off=0.0):
        return A + Vector((0, s * math.cos(alpha), s * math.sin(alpha) + off))
    s_short, s_long = -1.5, 2.9
    path = [onbeam(s_short), onbeam(-0.5, 0.02), onbeam(0.8, -0.03), onbeam(2.0, 0.04), onbeam(s_long)]
    path = [p + Vector((rnd.uniform(-0.03, 0.03), 0, 0)) for p in path]
    H.log(KL, path[0], path[1], 0.155, sides=7, kinks=1, mat='Bark', r1=0.15, seed=11, caps=(True, False))
    for i in range(1, 4):
        H.log(KL, path[i], path[i + 1], 0.15 - 0.016 * i, sides=7, kinks=1, mat='Bark', r1=0.15 - 0.016 * (i + 1),
              seed=11 + i, caps=(False, i == 3))
    # joints between kinks: small sleeve rings hide nothing; segments overlap by sharing end points
    tip = onbeam(s_long - 0.12)
    sh = onbeam(s_short + 0.12)
    beam_dir = (onbeam(1) - onbeam(0)).normalized()
    ring_around(KL, tip, beam_dir, 0.11, 0.12, seed=12)
    ring_around(KL, sh, beam_dir, 0.165, 0.12, seed=13)
    # counterweight: short rope + mossy block
    cw_top = sh - Vector((0, 0, 0.5))
    strand(KL, [sh - Vector((0, 0, 0.12)), cw_top + Vector((0, 0, 0.02))], 0.02, 'rope', seed=14)
    KL['Bark'].box(cw_top - Vector((0, 0, 0.26)), (0.48, 0.45, 0.52), rot=(0.04, -0.05, 0.12), jitter=0.025, seed=15,
                   grain='z')
    ring_around(KL, cw_top - Vector((0, 0, 0.14)), (0, 0, 1), 0.28, 0.05, seed=16)
    KL.drape('Bark', keep=0.45, min_nz=0.55, seed=17)
    lever = KL.build()

    # ------------------------------------------------ bucket + rope (hangs from the lever tip)
    KK = H.Kit('SW18a_Bucket', seed=3)
    hang = tip - Vector((0, 0, 0.1))
    rope_len = 1.25
    bail_top = hang - Vector((0, 0, rope_len))
    strand(KK, [hang, hang - Vector((0.01, 0, rope_len * 0.5)), bail_top + Vector((0, 0, 0.02))], 0.018, 'rope', seed=20)
    # bucket body: staves
    rb, rt, bh = 0.17, 0.205, 0.4
    rim = bail_top - Vector((0, 0, 0.21))
    bot = rim - Vector((0, 0, bh))
    n = 9
    for i in range(n):
        a0, a1 = 2 * math.pi * i / n, 2 * math.pi * (i + 1) / n
        am = (a0 + a1) / 2
        hh = bh + rnd.uniform(-0.015, 0.025)
        dirm = Vector((math.cos(am), math.sin(am), 0))
        tang = Vector((-math.sin(am), math.cos(am), 0))
        w0 = 2 * rb * math.sin(math.pi / n) * 0.98
        w1 = 2 * rt * math.sin(math.pi / n) * 0.98
        o_b = bot + dirm * rb * math.cos(math.pi / n)
        o_t = bot + dirm * rt * math.cos(math.pi / n) + Vector((0, 0, hh))
        th = 0.026
        pts_out = [o_b - tang * w0 / 2, o_b + tang * w0 / 2, o_t + tang * w1 / 2, o_t - tang * w1 / 2]
        pts_in = [p - dirm * th for p in pts_out]
        T = 1.6
        Mpl = KK['Planks']
        uo = rnd.random()
        Mpl.quad(*pts_out, (uo, 0), (uo + w0 / T, 0), (uo + w1 / T, hh / T), (uo, hh / T))
        Mpl.quad(*reversed(pts_in), (uo, hh / T), (uo + w1 / T, hh / T), (uo + w0 / T, 0), (uo, 0))
        Mpl.quad(pts_out[3], pts_out[2], pts_in[2], pts_in[3], (0, 0), (w1 / T, 0), (w1 / T, th / T), (0, th / T))
    # bottom disc (end grain) facing up inside + down outside
    ring = [bot + Vector((math.cos(2 * math.pi * k / n) * rb * 0.97, math.sin(2 * math.pi * k / n) * rb * 0.97, 0.03))
            for k in range(n)]
    KK['Props'].cap(ring, region='end_grain_grey')
    KK['Props'].cap(list(reversed([p - Vector((0, 0, 0.03)) for p in ring])), region='end_grain_grey')
    # iron hoops
    for f in (0.18, 0.78):
        rr = rb + (rt - rb) * f + 0.012
        ring_around(KK, bot + Vector((0, 0, bh * f)), (0, 0, 1), rr, 0.035, region='iron', n=n)
    # iron bail (arc over the top, ears at the rim)
    arc = []
    for k in range(9):
        t = math.pi * k / 8
        arc.append(rim + Vector((math.cos(t) * (rt + 0.015), 0, math.sin(t) * 0.21 - 0.03)))
    strand(KK, arc, 0.012, 'iron', seed=21, seg_len=0.2)
    bucket = KK.build()
    return base, lever, bucket, A, hang, ctop


def main(render=True):
    sk.reset()
    base, lever, bucket, A, hang, ctop = build()
    allo = base + lever + bucket
    # ground on the base only, move everything with it
    dz = sk.ground(base)
    from mathutils import Matrix
    for o in lever + bucket:
        o.data.transform(Matrix.Translation(dz))
    A = A + dz
    hang = hang + dz
    water_z = dz.z + ctop - 0.22   # water just under the crib rim (crib nearly in water)
    # pivots: lever geometry around the axle, bucket geometry around the hang point
    for o in lever:
        o.data.transform(Matrix.Translation(-A))
    for o in bucket:
        o.data.transform(Matrix.Translation(-hang))
    f_base = OUT + '/SM_SW18_WellSweep.fbx'
    f_lev = OUT + '/SM_SW18_WellSweepLever.fbx'
    f_buc = OUT + '/SM_SW18_WellSweepBucket.fbx'
    sk.export_fbx(base, f_base)
    sk.export_fbx(lever, f_lev)
    sk.export_fbx(bucket, f_buc)
    hang_in_lever = hang - A
    unity = lambda v: [round(v.x, 3), round(v.z, 3), round(v.y, 3)]   # Blender (x, y, z) -> Unity (x, y=z, z=y)
    recs = [
        sk.variant_record('SW18a', 'WellSweep', base, f_base, water_line_z=round(water_z, 3),
                          solidity='solid', burns=True,
                          collider='crib: box (top rim walkable edge); post: capsule along the trunk; grass no collider',
                          moving_parts='lever SM_SW18_WellSweepLever (pivot = axle) at base-local %s m '
                                       '(Unity %s); rotates about the X axis (horizontal axle across the fork)'
                                       % ([round(c, 3) for c in A], unity(A)),
                          notes='crib nearly in water; dark quad = shaft. The axle pin belongs to the base.'),
        sk.variant_record('SW18a_Lever', 'WellSweepLever', lever, f_lev, water_line_z=None,
                          solidity='solid', burns=True, collider='capsule along the beam; box counterweight',
                          pivot='axle centre (rotation axis = local X); modelled pose: long arm raised 16 deg',
                          place_at_base_local=[round(c, 3) for c in A],
                          bucket_attach_lever_local=[round(c, 3) for c in hang_in_lever],
                          bucket_attach_unity=unity(hang_in_lever),
                          notes='beam + lashings + counterweight with its short rope (rigid with the beam)'),
        sk.variant_record('SW18a_Bucket', 'WellSweepBucket', bucket, f_buc, water_line_z=None,
                          solidity='prop (no collider or small capsule)', burns=True, collider='none / small capsule',
                          pivot='top of the rope = hang point under the lever tip; parent it to the lever at '
                                'bucket_attach_lever_local and keep its rotation world-upright so it hangs',
                          notes='rope 1.25 m + 9 staves + bottom + 2 iron hoops + iron bail'),
    ]
    json.dump(recs, open(OUT + '/manifest.json', 'w'), indent=1, ensure_ascii=False)
    for r_ in recs:
        print(json.dumps({k: r_[k] for k in ('id', 'tris', 'size_xyz_m')}))
    # put the moving parts back in place for the .blend and renders (object transforms only)
    for o in lever:
        o.location = A
    for o in bucket:
        o.location = hang
    import bpy
    bpy.context.view_layer.update()
    sk.save_blend(OUT + '/SW18_WellSweep.blend')
    if render:
        views = [('v1', 55, 12), ('v2', -125, 12), ('side', 90, 4), ('w_34', 35, 22), ('bucket', -30, 20),
                 ('lever', 60, 15)]
        tiles = []
        for lab, az, el in views:
            p = D + '/work/a_%s.png' % lab
            objs = allo if lab not in ('bucket', 'lever') else (bucket if lab == 'bucket' else lever)
            sk.render(objs, p, az=az, el=el, size=(360, 360), samples=10, water_z=water_z if lab.startswith('w') else None)
            tiles.append(p)
        sk.sheet(tiles, OUT + '/card.jpg', cols=3, labels=['SW18a ' + v[0] for v in views], concept=SHEET,
                 title='SW18 Well sweep  base %d + lever %d + bucket %d tris  base %s  water_line_z=%.2f'
                       % (recs[0]['tris'], recs[1]['tris'], recs[2]['tris'], recs[0]['size_xyz_m'], water_z))


if __name__ == '__main__':
    main(render='norender' not in sys.argv)
