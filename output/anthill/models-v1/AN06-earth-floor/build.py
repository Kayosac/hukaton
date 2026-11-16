import sys, os, random
sys.path.insert(0, '/home/claude/ant/models')
from shell_lib import *

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
ak.reset()
T = 0.15


def tile(vid, seed):
    """4x4 m tileable slab: straight edges on the metre grid, edge verts every 0.5 m at exactly z = 0.15."""
    rnd = random.Random(seed)
    s = Solid()
    N = 8
    G = [[None] * (N + 1) for _ in range(N + 1)]
    for i in range(N + 1):
        for j in range(N + 1):
            x, y = -2 + 4 * i / N, -2 + 4 * j / N
            edge = i in (0, N) or j in (0, N)
            z = T
            if not edge:
                x += rnd.uniform(-0.14, 0.14); y += rnd.uniform(-0.14, 0.14)
                z += rnd.uniform(-0.03, 0.02)
            G[i][j] = Vector((x, y, z))
    for i in range(N):
        for j in range(N):
            s.quad(G[i][j], G[i + 1][j], G[i + 1][j + 1], G[i][j + 1], 'Floor')
    ring = [G[i][0] for i in range(N)] + [G[N][j] for j in range(N)] + [G[i][N] for i in range(N, 0, -1)] + [G[0][j] for j in range(N, 0, -1)]
    bot = [Vector((p.x, p.y, 0)) for p in ring]
    for k in range(len(ring)):
        k2 = (k + 1) % len(ring)
        s.quad(ring[k], ring[k2], bot[k2], bot[k], 'Earth')
    s.face(list(reversed(bot)), 'Earth')
    return s, s.finish(vid)


def patch(vid, seed):
    """Irregular trodden-earth patch like the concept sheet (overlay on terrain, edges taper to the ground)."""
    rnd = random.Random(seed)
    s = Solid()
    A = 16
    R0 = [1.0 + 0.12 * math.sin(3 * k * 2 * math.pi / A + 1) + rnd.uniform(-0.1, 0.1) for k in range(A)]
    rings = []
    for r, z, jz in ((0.62, 0.12, 0.015), (1.25, 0.115, 0.02), (1.95, 0.09, 0.01), (2.02, 0.0, 0.0)):
        ring = []
        for k in range(A):
            a = 2 * math.pi * k / A + (rnd.uniform(-0.08, 0.08) if r < 1.9 else 0)
            rr = r * R0[k] if r < 1.9 else (1.95 * R0[k] + (0.07 if z == 0 else 0))
            rr = min(rr, 2.05) if r > 1.9 else rr
            ring.append(Vector((math.cos(a) * rr, math.sin(a) * rr, z + rnd.uniform(-jz, jz))))
        rings.append(ring)
    C = Vector((0.05, -0.04, 0.125))
    for k in range(A):
        s.face([C, rings[0][k], rings[0][(k + 1) % A]], 'Floor')
    for i in range(3):
        m = 'Floor' if i < 2 else 'Earth'
        for k in range(A):
            k2 = (k + 1) % A
            s.quad(rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k], m)
    s.face(list(reversed(rings[3])), 'Earth')
    return s, s.finish(vid)


sa, oa = tile('AN06a', 61)
sb, ob = patch('AN06b', 62)
stats('AN06a', oa, sa); stats('AN06b', ob, sb)
fa = os.path.join(OUT, 'SM_AN06_EarthFloor.fbx')
fb = os.path.join(OUT, 'SM_AN06b_EarthFloorPatch.fbx')
ak.export_fbx(oa, fa)
ak.export_fbx(ob, fb)
recs = [ak.variant_record('AN06a', 'Earth floor tile 4 x 4 x 0.15 (tileable)', oa, fa, solidity='walk-on-top',
                          collider='box 4 x 4 x 0.15 (top at z=0.15)', emissive=False,
                          joins={'grid': '4 m tile, pivot bottom centre, edges x = +/-2, y = +/-2, straight and flat',
                                 'walk_surface': 'z = +0.15 exactly on all four edges (interior varies -0.03..+0.02), so tiles meet without steps in any rotation',
                                 'texture': 'Floor top + Earth sides use world-planar UVs at the kit density (2 m per repeat); procedural tunnel floors should use the same planar mapping'},
                          max_top_slope_deg=round(max_slope(oa), 1),
                          notes='gently faceted trodden top (0.5 m grid, jittered interior), vertical Earth edge, closed bottom.'),
        ak.variant_record('AN06b', 'Earth floor patch (irregular, concept outline)', ob, fb, solidity='walk-on-top',
                          collider='none (overlay, the floor/terrain below collides) or mesh', emissive=False,
                          joins={'grid': 'free placement overlay, pivot bottom centre; not a snapping piece'},
                          notes='irregular-outline trodden patch like the concept sheet; top 0.12 m, edge tapers to 0.09 then a short Earth lip to the ground.')]
write_manifest(OUT, recs)
ak.save_blend(os.path.join(HERE, 'AN06-earth-floor.blend'))
if wants_render():
    # 2x2 tiled check
    tiles = []
    for k, (dx, dy) in enumerate(((4, 0), (0, 4), (4, 4))):
        for o in tile('AN06t%d' % k, 70 + k)[1]:
            o.location = (dx, dy, 0); tiles.append(o)
    bpy.context.view_layer.update()
    card = renders(oa, OUT, os.path.join(SHEETS, '06-earth-floor.png'), el=30,
                   extra=[('b PATCH', ob, dict(az=-45, el=30)), ('a TILED 2x2', oa + tiles, dict(az=-30, el=35))],
                   title='AN06 earth floor (a tile, b patch)')
    print(card)
