"""SW01 Кочка — 4 low slumped peat hummocks: a tall oval, b low elongated (with cattails), c double, d wide flat.
Mound = lofted crooked 9-10-sided rings: Mud flare under water, Peat walls sloping ~35-60 deg with a convex profile,
one side sagging lower and running out longer, broad faceted walkable top (Peat). Patchy Moss skin on the flatter
top faces spilling over the edge with moss drip cards; sedge tufts (bent blade strips + fan cards) at the back of the
top and at the water line; small flat duckweed bits floating at the water line; cattails on b."""
import sys, os, json, math, random
sys.path.insert(0, '/home/claude/swamp/items/_plants_lib')
import plib, sk
from plib import V, hdir
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out'); os.makedirs(OUT, exist_ok=True)
SHEET = '/home/claude/ref/swamp/sheets/01-hummocks.png'
WL = 0.25   # water line above the pivot (submerged part of the mound)
def mound(mud, peat, c, rx, ry, H, seed, ns=9, sag=0.0, sag_az=0.0, dome=0.025):
    """Low slumped peat mound. rx, ry = half size at the water line. Rings (fraction of H, radius factor):
    under-water flare (Mud) -> water line -> 3 wall rings narrowing with a convex profile (walls ~35-60 deg) ->
    top edge -> inner top ring -> centre (broad faceted walkable top, Peat). sag: how much lower (fraction of H)
    the side towards sag_az is; that side's walls run out longer (slumped), the high side stays steeper."""
    rnd = random.Random(seed)
    ph = [rnd.uniform(0, 6.3) for _ in range(3)]
    am = [0.09, 0.06, 0.04]
    sd = V((math.cos(sag_az), math.sin(sag_az), 0))
    spec = [(-WL, 1.22, 0.08), (0.0, 1.0, 0.07), (0.3, 0.9, 0.08), (0.62, 0.77, 0.08), (0.86, 0.64, 0.07),
            (1.0, 0.54, 0.06), (1.02, 0.3, 0.1)]
    rot = rnd.uniform(0, 1)
    rings = []
    for zi, (fz, f, j) in enumerate(spec):
        r = plib.blob_ring(c, rx * f, ry * f, 0, ns, rnd, j, rot=rot, phase=ph, amps=am)
        for p in r:
            d = V((p.x - c[0], p.y - c[1], 0))
            # side factor: +1 on the sagging side, -1 on the high side
            sf = d.normalized().dot(sd) if d.length > 1e-6 else 0
            z = WL + fz * H
            if zi >= 2:
                # planar tilt: drop grows linearly towards sag_az across the whole mound (consistent top slope)
                lin = max(-1.0, min(1.0, d.dot(sd) / max(rx, ry)))
                z -= sag * H * min(fz, 1.0) * (0.5 + 0.5 * lin)
                # slumped side bulges out (longer, gentler wall), high side pulled in a little
                p.x += sd.x * sf * rx * 0.08 * (fz < 0.95) if sf > 0 else sd.x * sf * rx * 0.03
                p.y += sd.y * sf * ry * 0.08 * (fz < 0.95) if sf > 0 else sd.y * sf * ry * 0.03
                z += rnd.uniform(-0.03, 0.03) * H * (zi < 5)          # chunky, non-banded facets
            elif zi == 1:
                z += rnd.uniform(-0.02, 0.02)
            else:
                z = rnd.uniform(0.0, 0.03)
            p.z = z
        rings.append(r)
    cz = WL + H * (1.0 + dome * 0.5) - sag * H * 0.5
    tc = (c[0] - sd.x * rx * 0.08 + rnd.uniform(-.03, .03), c[1] - sd.y * ry * 0.08 + rnd.uniform(-.03, .03), cz)
    plib.loft(mud, rings[0:2])
    plib.loft(peat, rings[1:], top_center=tc)
    return rings

class Surface:
    def __init__(self, obj):
        me = obj.data
        self.t = BVHTree.FromPolygons([v.co.copy() for v in me.vertices], [list(p.vertices) for p in me.polygons])
    def z(self, x, y, default=WL - 0.04):
        hit = self.t.ray_cast(V((x, y, 5)), V((0, 0, -1)))
        return hit[0].z if hit[0] is not None else default

def tuft(pl, rnd, p, h, kind='sedge', **kw):
    if kind == 'sedge':
        plib.sedge_tuft(pl, p, h, rnd=rnd, **kw)

def cattails(pl, pr, rnd, base, n=2, h=0.95):
    for i in range(n):
        b = V(base) + V((rnd.uniform(-.03, .03), rnd.uniform(-.03, .03), 0))
        pts = plib.arc_path(b, rnd.uniform(0, 6.3), 0.04, 0.14 + 0.08 * i, h * (1 - 0.15 * i), segs=5, p=1.3)
        plib.cattail_stem(pl, pr, pts, r=0.013, rnd=rnd, cob_from=0.72, cob_to=0.9, cob_r=0.033)
    plib.leaves_ring(pl, rnd, 7, 0.4, 0.7, 0.08, kinks=0.3, spread=(0.2, 0.7), droop=(0.5, 1.2))  # at origin
    return

def debris(pl, rnd, surf, n, c, rx, ry):
    """Small flat duckweed / leaf bits floating at the water line around the base (optional context)."""
    for k in range(n):
        a = rnd.uniform(0, 2 * math.pi)
        f = rnd.uniform(1.02, 1.3)
        x, y = c[0] + math.cos(a) * rx * f, c[1] + math.sin(a) * ry * f
        s_ = rnd.uniform(0.1, 0.22)
        pl.flat_card((x, y, WL + 0.004 + 0.001 * (k % 3)), (s_, s_), rnd.choice(['duckweed_2', 'duckweed_3']),
                     rot=rnd.uniform(0, 6.3), z_jitter=0.002, seed=k, wind_flutter=0.006)

def build(vid, blocks, tufts, seed, cat=None, n_debris=7):
    """blocks: list of mound kwargs. tufts: list of (x, y, h, where('top'|'base'), kw)."""
    rnd = random.Random(seed)
    mud = sk.Mesh('Mud'); peat = sk.Mesh('Peat'); pl = sk.Mesh('Plants'); pr = sk.Mesh('Props')
    for i, b in enumerate(blocks):
        mound(mud, peat, seed=seed * 10 + i, **b)
    mo = mud.to_object('%s_Mud' % vid)
    po = peat.to_object('%s_Peat' % vid)
    # patchy moss: only fairly flat faces (top + upper chamfer), ~half kept, spilling over the edge as drip cards
    moss = sk.moss_drape(po, '%s_Moss' % vid, min_nz=0.62, offset=0.012, keep=0.55, seed=seed + 7, drips=pl)
    surf = Surface(po)
    sl = []
    for b in blocks:   # walkability probe: face slopes under a grid over the inner half of each top
        for i in range(-3, 4):
            for j in range(-3, 4):
                x, y = b['c'][0] + b['rx'] * 0.38 * i / 3, b['c'][1] + b['ry'] * 0.38 * j / 3
                if (i * i + j * j) > 9:
                    continue
                hit = surf.t.ray_cast(V((x, y, 5)), V((0, 0, -1)))
                if hit[0] is not None:
                    sl.append(math.degrees(math.acos(min(1.0, abs(hit[1].z)))))
    po['top_slopes'] = sl
    for (x, y, h, where, kw) in tufts:
        z = surf.z(x, y) - 0.03
        if where == 'base':
            z = min(z, WL - 0.03)
        plib.sedge_tuft(pl, (x, y, z), h, rnd=rnd, **kw)
    for b in blocks:
        debris(pl, rnd, surf, n_debris // len(blocks), b['c'], b['rx'], b['ry'])
    if cat:
        x, y, n, h = cat
        z = surf.z(x, y) - 0.04
        tmp_pl = sk.Mesh('Plants'); tmp_pr = sk.Mesh('Props')
        cattails(tmp_pl, tmp_pr, rnd, (0, 0, 0), n, h)
        for src, dst in ((tmp_pl, pl), (tmp_pr, pr)):
            o = src.to_object('tmp')
            o.data.transform(__import__('mathutils').Matrix.Translation(V((x, y, z))))
            _merge(dst, o)
    objs = [mo, po] + ([moss] if moss else []) + plib.finish([pl, pr], vid)
    return objs

def _merge(dst, obj):
    """Append a temp object's geometry (with UV + Wind layers) into a kit Mesh, then delete the object."""
    import bmesh, bpy
    bm = bmesh.new(); bm.from_mesh(obj.data)
    uvs = bm.loops.layers.uv
    su, sw = uvs.get('UVMap'), uvs.get('Wind')
    for f in bm.faces:
        pts = [v.co.copy() for v in f.verts]
        uv = [tuple(l[su].uv) for l in f.loops]
        w = [(l[sw].uv[0] - 1000.0, l[sw].uv[1] - 1000.0) for l in f.loops]
        dst.face(pts, uv, w)
    bm.free()
    me = obj.data; bpy.data.objects.remove(obj); bpy.data.meshes.remove(me)

DRY = ['sedge_blade_2', 'sedge_blade_2', 'sedge_blade_0', 'sedge_blade_3']
S = dict(n=9, spread=1.1, droop=1.0, width=0.085, fans=2, radius=0.04, regions=DRY, fan_regions=('sedge_0', 'grass_low'))
SB = dict(n=5, spread=1.4, droop=1.1, width=0.065, fans=1, fan_regions=('grass_low',), radius=0.05,
          min_tilt=0.3, regions=DRY)

def hum_a(vid):   # tall oval: 1.0 x 0.8 at the water line, 0.5 above water, slumps towards front-right
    return build(vid, [dict(c=(0, 0), rx=0.5, ry=0.4, H=0.5, ns=9, sag=0.35, sag_az=-0.6)],
                 [(-0.24, 0.2, 0.7, 'top', S), (0.42, -0.3, 0.3, 'base', SB), (-0.5, -0.12, 0.24, 'base', SB),
                  (0.18, 0.42, 0.26, 'base', SB)], 1)

def hum_b(vid):   # low elongated 1.3 x 0.65, 0.34 above water, cattails at the high -X end, slumps to +X
    return build(vid, [dict(c=(0, 0), rx=0.66, ry=0.33, H=0.34, ns=10, sag=0.5, sag_az=0.15)],
                 [(0.62, 0.12, 0.3, 'top', dict(SB, min_tilt=0.15)), (0.2, -0.38, 0.28, 'base', SB),
                  (-0.64, -0.25, 0.26, 'base', SB), (-0.52, 0.18, 0.5, 'top', dict(S, n=6))], 2,
                 cat=(-0.44, 0.04, 2, 0.95))

def hum_c(vid):   # double: higher block behind-left + lower slumped block front-right, crack between
    return build(vid, [dict(c=(-0.2, 0.12), rx=0.42, ry=0.38, H=0.5, ns=9, sag=0.25, sag_az=2.6),
                       dict(c=(0.32, -0.18), rx=0.36, ry=0.32, H=0.3, ns=9, sag=0.4, sag_az=-0.5)],
                 [(-0.4, 0.34, 0.7, 'top', S), (0.06, -0.04, 0.28, 'top', dict(SB, min_tilt=0.15)),
                  (0.62, 0.0, 0.25, 'base', SB), (-0.58, -0.22, 0.22, 'base', SB)], 3, n_debris=8)

def hum_d(vid):   # wide flat 1.3 x 1.1, 0.3 above water, gently sagging to the front
    return build(vid, [dict(c=(0, 0), rx=0.65, ry=0.55, H=0.3, ns=10, sag=0.3, sag_az=-1.4)],
                 [(-0.46, 0.34, 0.62, 'top', S), (0.52, 0.1, 0.3, 'top', dict(SB, min_tilt=0.2)),
                  (0.15, -0.62, 0.24, 'base', SB), (-0.7, -0.2, 0.22, 'base', SB), (0.45, 0.5, 0.28, 'base', SB)], 4,
                 n_debris=8)

VARIANTS = [('SW01a', 'HummockTall', hum_a), ('SW01b', 'HummockLong', hum_b), ('SW01c', 'HummockDouble', hum_c),
            ('SW01d', 'HummockFlat', hum_d)]

def main(render=True):
    sk.reset()
    recs, allv, tiles, labels = [], [], [], []
    for vid, name, fn in VARIANTS:
        objs = fn(vid)
        d = sk.ground(objs)
        fbx = os.path.join(OUT, 'SM_%s_%s.fbx' % (vid[:4], name))
        sk.export_fbx(objs, fbx)
        wl = round(WL + d.z, 3)
        po = [o for o in objs if o.name.endswith('_Peat')][0]
        top_sl = list(po['top_slopes'])
        print(vid, 'walk area (inner 38%% of each top) max slope %.1f deg, mean %.1f' % (max(top_sl), sum(top_sl) / len(top_sl)))
        recs.append(sk.variant_record(vid, name, objs, fbx, water_line_z=wl, solidity='walk-on-top', burns=False,
                                      collider='Peat + Mud meshes (or one convex hull of both) solid; Moss skin, Plants and Props: '
                                               'no collider',
                                      moving_parts=None,
                                      notes='pivot at the bottom centre of the submerged base; sink so the water '
                                            'line mark is at the water surface. Top walk surface: max face slope %.0f deg.' % max(top_sl)))
        allv.append(objs)
        if render:
            for j, (az, el, w) in enumerate([(-30, 22, wl), (35, 18, wl), (-60, 8, None)]):
                f = os.path.join(OUT, '%s_%d.png' % (vid, j))
                sk.render(objs, f, az=az, el=el, size=(300, 240), samples=8, water_z=w)
                tiles.append(f); labels.append('%s %s az%d%s' % (vid, name, az, ' water' if w else ''))
    if render:
        sk.sheet(tiles, os.path.join(OUT, 'card.jpg'), cols=3, labels=labels, concept=SHEET,
                 title='SW01 Hummocks — concept (top) vs model', concept_h=500)
    json.dump(recs, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1, ensure_ascii=False)
    plib.side_by_side(allv)
    sk.save_blend(os.path.join(OUT, 'SW01_hummocks.blend'))
    for r in recs:
        print(r['id'], r['tris'], r['tris_by_material'], r['size_xyz_m'], r['water_line_z'])

if __name__ == '__main__':
    main('--norender' not in sys.argv)
