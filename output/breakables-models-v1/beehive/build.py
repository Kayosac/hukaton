"""DB06 Beehive debris (forest beehive SM_SP04_Beehive): a broken hive wall board, a honeycomb frame,
the cracked lid and a chunk of comb. Board/lid use the same M_Forest_Special regions as the hive boxes / lid,
fresh breaks M_Forest_Props 'lightdiag', comb M_Forest_Props 'amber' (+ 'cream' capped cells)."""
import sys, math, random, json, os
sys.path.insert(0, '/home/claude/brk/work')
import bpy, bmesh, numpy as np
from mathutils import Vector, Matrix
import bk
from bk import PROPS as P, sub

bpy.ops.wm.read_factory_settings(use_empty=True)
OUT = '/home/claude/brk/out/beehive/'
os.makedirs(OUT, exist_ok=True)
SP, PR = 'M_Forest_Special', 'M_Forest_Props'
# T_Forest_Special pixel rects (studied from SP04 UVs): olive boards of the upper box (3 boards, seams at y 352/370),
# brown boards of the lower box, red-brown mossy lid planks (4 planks, grain along image v = world Y on the lid)
BOARD = (134, 352, 230, 371)      # middle olive board incl. both dark seams, nails at both ends
LID = (102, 402, 198, 450)
FRESH = P['lightdiag']            # fresh broken wood
AMBER, CREAM = P['amber'], P['cream']

# ================================================================ helpers
def plank_xz(b, outline, depth, yoff, uvf, side_uv, brk, m_face=0, m_side=0, m_break=1):
    """Plank lying in XZ (front -Y) from a CCW (x, z) outline. depth[k], yoff[k] per outline vertex.
    uvf(x, z) -> uv of the front/back faces, side_uv(k, x, z, back) for intact edges, brk = set of edge
    indices k (k -> k+1) that are fresh breaks (m_break, FRESH)."""
    n = len(outline)
    F = [Vector((x, yoff[k] - depth[k] / 2, z)) for k, (x, z) in enumerate(outline)]
    Bk = [Vector((x, yoff[k] + depth[k] / 2, z)) for k, (x, z) in enumerate(outline)]
    b.face(F, [uvf(p.x, p.z) for p in F], m_face)
    b.face(Bk[::-1], [uvf(p.x, p.z) for p in Bk[::-1]], m_face)
    for k in range(n):
        k1 = (k + 1) % n
        q = [F[k], Bk[k], Bk[k1], F[k1]]
        if k in brk:
            L = (F[k1] - F[k]).length
            uv = [bk.uvr(PR, FRESH, .2, .1), bk.uvr(PR, FRESH, .45, .1), bk.uvr(PR, FRESH, .45, .1 + L / .15),
                  bk.uvr(PR, FRESH, .2, .1 + L / .15)]
            b.face(q, uv, m_break)
        else:
            b.face(q, [side_uv(k, F[k]), side_uv(k, Bk[k], True), side_uv(k1, Bk[k1], True), side_uv(k1, F[k1])], m_side)
    return F, Bk

def tri_all(o):
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method='BEAUTY', ngon_method='EAR_CLIP')
    bm.to_mesh(o.data); bm.free()
    for p in o.data.polygons: p.use_smooth = False

# ================================================================ 1. wall board
def wall_board():
    rnd = random.Random(3)
    L, H = 0.58, 0.12
    x0, x1 = -L / 2, L / 2
    # CCW (x, z): bottom edge left -> right, splintered right end bottom -> top, top edge right -> left
    out = [(x0, -0.06), (-0.15, -0.061), (-0.01, -0.059), (0.11, -0.06),
           (0.165, -0.06), (0.225, -0.045), (0.19, -0.03), (0.285, -0.014), (0.215, -0.002), (0.24, 0.016),
           (0.2, 0.03), (0.30, 0.052), (0.262, 0.06),
           (0.12, 0.06), (-0.02, 0.061), (-0.16, 0.059), (x0, 0.06), (x0 - 0.003, 0.0)]
    brk = set(range(4, 12))
    out = [(x + rnd.uniform(-.002, .002), z + rnd.uniform(-.0015, .0015)) for x, z in out]
    tips = {7: 0.45, 11: 0.4, 5: 0.7, 9: 0.75}
    depth = [0.025 * tips.get(k, 1.0) + rnd.uniform(-.002, .002) for k in range(len(out))]
    yoff = [0.007 * (1 - (x / 0.3) ** 2) + 0.012 * max(0, x - 0.17) / 0.13 * (1 if k in (7, 11) else 0)
            for k, (x, z) in enumerate(out)]   # bowed board, splinter tips bent outward
    b = bk.B([SP, PR])
    uvf = lambda x, z: bk.uvr(SP, BOARD, (x - x0) / L, (z + 0.06) / H)
    def side(k, p, back=False):
        x, z = p.x, p.z
        if abs(x - x0) < 0.01 and abs(out[k][0] - x0) < 0.01:      # sawn left end: thin seam strip
            return bk.uvr(SP, BOARD, 0.02 + (0.02 if back else 0), (z + 0.06) / H)
        return bk.uvr(SP, sub(BOARD, 0, 1, 0.35, 0.65), (x - x0) / L, 1.0 if back else 0.0)
    plank_xz(b, out, depth, yoff, uvf, side, brk)
    o = b.obj('DB06_Beehive_WallBoard')
    # a little twist along the length
    for v in o.data.vertices:
        a = math.radians(2.5) * (v.co.x / 0.29)
        y, z = v.co.y, v.co.z
        v.co.y, v.co.z = y * math.cos(a) - z * math.sin(a), y * math.sin(a) + z * math.cos(a)
    return o

# ================================================================ hex comb surface
def comb(b, cells, R, clamp, rim_y, d_back, wall, seed, cream_cells=(), jit=0.0, wall_depth=0.02):
    """Hex comb slab in XZ (front -Y). cells: list of (cx, cz). Front: each cell = 6 tris fanned from a centre
    (capped cells bulge out cream, open cells dip in amber). Back: a pillow fan. Boundary edges flagged by
    wall(p0, p1) get a broken wall of wall_depth, otherwise the back pinches onto the front edge."""
    rnd = random.Random(seed)
    key = lambda p: (round(p[0], 4), round(p[1], 4))
    corners = {}
    polys = []
    for (cx, cz) in cells:
        poly = []
        for k in range(6):
            a = math.radians(30 + 60 * k)
            x, z = cx + R * math.cos(a), cz + R * math.sin(a)
            kk = key((x, z))
            if kk not in corners:
                xx, zz = x, z
                if clamp:
                    xx = min(max(xx, clamp[0]), clamp[1]); zz = min(max(zz, clamp[2]), clamp[3])
                corners[kk] = [xx, zz]
            if not poly or poly[-1] != kk:
                poly.append(kk)
        polys.append(poly)
    # boundary edges (used once)
    cnt = {}
    for poly in polys:
        for a, c in zip(poly, poly[1:] + poly[:1]):
            e = frozenset((a, c)); cnt[e] = cnt.get(e, 0) + 1
    bedge = [(a, c) for poly in polys for a, c in zip(poly, poly[1:] + poly[:1]) if cnt[frozenset((a, c))] == 1]
    bverts = {a for a, c in bedge}
    # jitter broken boundary corners
    for kk in bverts:
        x, z = corners[kk]
        if not (clamp and (abs(x - clamp[0]) < 1e-4 or abs(x - clamp[1]) < 1e-4 or abs(z - clamp[2]) < 1e-4 or abs(z - clamp[3]) < 1e-4)):
            corners[kk] = [x + rnd.uniform(-jit, jit), z + rnd.uniform(-jit, jit)]
    FY = {kk: (0.0 if kk in bverts else rim_y + rnd.uniform(-.002, .002)) for kk in corners}
    V = lambda kk, y=None: Vector((corners[kk][0], FY[kk] if y is None else y, corners[kk][1]))
    # front cells
    for ci, (poly, (cx, cz)) in enumerate(zip(polys, cells)):
        capped = ci in cream_cells
        cy = (-0.012 if capped else 0.008) + rnd.uniform(-.002, .002)
        mx = sum(corners[k][0] for k in poly) / len(poly); mz = sum(corners[k][1] for k in poly) / len(poly)
        C = Vector((mx, cy, mz))
        rect = CREAM if capped else AMBER
        cu = (0.18, 0.18) if capped else (0.5, 0.55)
        for a, c in zip(poly, poly[1:] + poly[:1]):
            ua = math.atan2(corners[a][1] - mz, corners[a][0] - mx); uc = math.atan2(corners[c][1] - mz, corners[c][0] - mx)
            s = 0.12 if capped else 0.45
            b.face([C, V(c), V(a)], [bk.uvr(PR, rect, *cu), bk.uvr(PR, rect, cu[0] + s * math.cos(uc) * (0.5 if capped else 1), cu[1] + s * math.sin(uc) * (0.5 if capped else 1)),
                                     bk.uvr(PR, rect, cu[0] + s * math.cos(ua) * (0.5 if capped else 1), cu[1] + s * math.sin(ua) * (0.5 if capped else 1))], 0)
    # boundary loop in order
    nxt = {a: c for a, c in bedge}
    loop = [bedge[0][0]]
    while True:
        n_ = nxt[loop[-1]]
        if n_ == loop[0]: break
        loop.append(n_)
    assert len(loop) == len(bedge), (len(loop), len(bedge))
    walled = {i for i in range(len(loop)) if wall(corners[loop[i]], corners[loop[(i + 1) % len(loop)]])}
    vwall = {loop[i] for i in walled} | {loop[(i + 1) % len(loop)] for i in walled}
    walled = {i for i in range(len(loop)) if loop[i] in vwall or loop[(i + 1) % len(loop)] in vwall}
    BK = {kk: (V(kk, wall_depth + rnd.uniform(-.003, .003)) if kk in vwall else V(kk)) for kk in loop}
    xs = [corners[k][0] for k in loop]; zs = [corners[k][1] for k in loop]
    lo, hi = (min(xs), min(zs)), (max(xs), max(zs))
    fuv = lambda p: bk.uvr(PR, AMBER, (p.x - lo[0]) / (hi[0] - lo[0] + 1e-6), (p.z - lo[1]) / (hi[1] - lo[1] + 1e-6))
    cen = Vector(((lo[0] + hi[0]) / 2, d_back, (lo[1] + hi[1]) / 2))
    nL = len(loop)
    for i in range(nL):
        a, c = loop[i], loop[(i + 1) % nL]
        b.face([BK[a], BK[c], cen], [fuv(BK[a]), fuv(BK[c]), bk.uvr(PR, AMBER, .5, .5)], 0)
        if i in walled:
            Lg = (V(c) - V(a)).length
            q = [V(a), V(c), BK[c], BK[a]]
            q2 = []
            for p in q:
                if not q2 or (p - q2[-1]).length > 1e-6: q2.append(p)
            if (q2[0] - q2[-1]).length < 1e-6: q2.pop()
            if len(q2) >= 3:
                uv = [bk.uvr(PR, AMBER, .1, .3), bk.uvr(PR, AMBER, .1 + Lg / .1, .3), bk.uvr(PR, AMBER, .1 + Lg / .1, .5),
                      bk.uvr(PR, AMBER, .1, .5)][:len(q2)]
                b.face(q2, uv, 0)
    return corners, loop, bverts

def drip(b, top, length, w, seed):
    """Small hanging honey drip: a 4-tri pyramid pointing down, its base hidden in the comb."""
    rnd = random.Random(seed)
    t = Vector(top)
    base = [t + Vector((w * math.cos(a), w * 0.8 * math.sin(a), 0.004)) for a in
            (rnd.uniform(0, 1), 2.1 + rnd.uniform(0, .3), 4.2 + rnd.uniform(0, .3))]
    tip = t + Vector((rnd.uniform(-.004, .004), rnd.uniform(-.004, .004), -length))
    U = [bk.uvr(PR, AMBER, .35, .7), bk.uvr(PR, AMBER, .65, .7), bk.uvr(PR, AMBER, .5, .2)]
    for i in range(3):
        b.face([base[i], base[(i + 1) % 3], tip], U, 0)
    b.face(base[::-1], U, 0)

# ================================================================ 2. frame with honeycomb
def frame():
    rnd = random.Random(8)
    b = bk.B([PR])
    W, H = 0.45, 0.25
    J = lambda a=0.004: rnd.uniform(-a, a)
    # top bar with lugs (ears), side bars, bottom bar - light crate battens, grain along each bar
    b.box((J(0.003), J(0.002), H - 0.012), (0.49, 0.026, 0.024), rot=(J(1), J(1.5), J(1)), grain=0, side=P['plankH'],
          grain_u=True, jitter=0.002, seed=1)
    for sx in (-1, 1):
        b.box((sx * 0.214 + J(0.003), J(0.002), (H - 0.024) / 2 + 0.0), (0.018, 0.022, H - 0.024 + 0.006),
              rot=(J(1.5), J(2.5), 0), grain=2, side=P['plankH'], grain_u=True, jitter=0.002, seed=2 + sx)
    b.box((J(0.003), J(0.002), 0.008), (0.43, 0.02, 0.014), rot=(J(1), J(1.5), J(1)), grain=0, side=P['plankH'],
          grain_u=True, jitter=0.002, seed=5)
    # comb: 3 rows of big hex cells, the lower-right broken out
    R = 0.06
    dx, dz = math.sqrt(3) * R, 1.5 * R
    cells = []
    for r in range(3):
        for c in range(-3, 4):
            cx = c * dx + (dx / 2 if r % 2 else 0) - 0.01
            cz = 0.045 + r * dz
            if abs(cx) > 0.255: continue
            if (cx > 0.0 and cz < 0.08) or (cx > 0.14 and cz < 0.15) or cx > 0.19: continue
            cells.append((cx, cz))
    clamp = (-0.214, 0.214, 0.008, H - 0.012)
    def wall(p0, p1):   # broken edges are the ones not lying on a bar centre line
        on = lambda p: abs(p[0] - clamp[0]) < 1e-4 or abs(p[0] - clamp[1]) < 1e-4 or abs(p[1] - clamp[2]) < 1e-4 or abs(p[1] - clamp[3]) < 1e-4
        return not (on(p0) and on(p1))
    cream = {i for i, (cx, cz) in enumerate(cells) if cz > 0.15 and rnd.random() < 0.75}
    comb(b, cells, R, clamp, rim_y=-0.007, d_back=0.026, wall=wall, seed=9, cream_cells=cream, jit=0.008,
         wall_depth=0.012)
    o = b.obj('DB06_Beehive_Frame')
    # crooked: skew the frame a little (racked) and twist
    for v in o.data.vertices:
        v.co.x += 0.022 * (v.co.z / 0.25 - 0.5)                       # racked
        v.co.y += 0.012 * (v.co.x / 0.24) * (v.co.z / 0.25 - 0.5)     # twisted
    return o, len(cells), len(cream)

# ================================================================ 3. lid
def lid():
    rnd = random.Random(21)
    b = bk.B([SP, PR])
    XH, YH = 0.33, 0.35
    # CCW top-view outline; planks run along Y (seams at x = -0.165, 0, 0.165)
    out = [(-XH, -YH), (-0.2, -YH), (-0.08, -YH), (0.0, -YH),                                  # front edge
           (0.035, -0.315), (0.06, -0.345), (0.1, -0.29), (0.135, -0.322), (0.165, -0.27),      # plank 3 broken end
           (0.166, -0.18), (0.168, -0.13),                                                       # along the seam
           (0.2, -0.165), (0.225, -0.095), (0.262, -0.14), (0.29, -0.07), (0.315, -0.105), (XH, -0.06),  # plank 4 broken
           (XH, 0.12), (XH, YH),
           (0.12, YH), (-0.155, YH), (-0.161, 0.2), (-0.165, 0.04), (-0.169, 0.2), (-0.176, YH),  # crack notch
           (-XH, YH), (-XH, 0.12), (-XH, -0.12)]
    brk = set(range(3, 8)) | set(range(10, 16))
    out = [(x + rnd.uniform(-.003, .003), y + rnd.uniform(-.003, .003)) for x, y in out]
    n = len(out)
    def ztop(x, y):
        return 0.08 + 0.075 * (YH - y) / (2 * YH)
    zt, zb = [], []
    for k, (x, y) in enumerate(out):
        drop = 0.0
        if x < -0.16 and y > 0.0:      # the cracked-off left back part sags
            drop = 0.018 * (y / YH) * ((-x - 0.16) / 0.17)
        brkv = k in brk or (k - 1) in brk
        t = ztop(x, y) - (0.012 if brkv else 0) + rnd.uniform(-.004, .004)
        zt.append(t - drop); zb.append(0.0 + (0.008 if brkv else 0) + rnd.uniform(-.003, .003) - drop)
    T = [Vector((x, y, zt[k])) for k, (x, y) in enumerate(out)]
    Bm = [Vector((x, y, zb[k])) for k, (x, y) in enumerate(out)]
    top_uv = lambda p: bk.uvr(SP, LID, (XH - p.x) / (2 * XH), (YH - p.y) / (2 * YH))
    b.face(T, [top_uv(p) for p in T], 0)
    b.face(Bm[::-1], [top_uv(p) for p in Bm[::-1]], 0)
    for k in range(n):
        k1 = (k + 1) % n
        q = [Bm[k], Bm[k1], T[k1], T[k]]
        if k in brk:
            L = (T[k1] - T[k]).length
            uv = [bk.uvr(PR, FRESH, .3, .1), bk.uvr(PR, FRESH, .3 + L / .2, .1), bk.uvr(PR, FRESH, .3 + L / .2, .5),
                  bk.uvr(PR, FRESH, .3, .5)]
            b.face(q, uv, 1)
        else:
            d = T[k1] - T[k]
            if abs(d.y) > abs(d.x):      # long side of a plank: grain (v) along Y, thin strip in u
                uv = [bk.uvr(SP, LID, 0.02 + p.z * 0.6, (YH - p.y) / (2 * YH)) for p in q]
            else:                         # plank ends: u across the planks, thin strip in v
                uv = [bk.uvr(SP, LID, (XH - p.x) / (2 * XH), 0.02 + p.z * 0.5) for p in q]
            b.face(q, uv, 0)
    o = b.obj('DB06_Beehive_Lid')
    # crooked: slight warp of the whole lid
    for v in o.data.vertices:
        v.co.z += 0.012 * (v.co.x / 0.33) * (v.co.y / 0.35)
    return o

# ================================================================ 4. comb chunk
def comb_chunk():
    b = bk.B([PR])
    R = 0.024
    dx, dz = math.sqrt(3) * R, 1.5 * R
    cells = [(0, 0), (dx, 0), (2 * dx, 0), (-dx / 2, dz), (dx / 2, dz), (dx * 1.5, dz), (dx / 2 * -0, 2 * dz), (dx, 2 * dz)]
    comb(b, cells, R, None, rim_y=-0.006, d_back=0.03, wall=lambda a, c: True, seed=14, cream_cells={6, 7}, jit=0.006,
         wall_depth=0.024)
    drip(b, (0.01, 0.01, -0.018), 0.035, 0.007, 1)
    drip(b, (0.075, 0.008, -0.017), 0.022, 0.006, 2)
    o = b.obj('DB06_Beehive_CombChunk')
    for v in o.data.vertices:          # slightly curled chunk
        v.co.y += 0.25 * (v.co.x ** 2)
    return o

board = wall_board()
fr, ncell, ncream = frame()
lid_o = lid()
chunk = comb_chunk()
pieces = [board, fr, lid_o, chunk]
for o in pieces: tri_all(o)

# layout along X, resting on the ground; frame/board stand upright as in the hive
bpy.context.view_layer.update()
x = 0.0
for o in pieces:
    lo, hi = bk.bounds([o]); w = float(hi[0] - lo[0])
    x += w
    o.location = Vector((x, 0, o.location.z - float(lo[2])))
    x += w + 0.1
bpy.context.view_layer.update()
cols = [bk.col(o) for o in pieces]
objs = []
for o, c in zip(pieces, cols): objs += [o, c]
rows = bk.report(objs)
NOTES = {
 'DB06_Beehive_WallBoard': 'one olive wall board of the upper hive box (M_Forest_Special, same region as SP04_BoxUpper), bowed and twisted, right end splintered (fresh breaks M_Forest_Props lightdiag)',
 'DB06_Beehive_Frame': f'hive frame (top bar with lugs, sides, bottom bar; M_Forest_Props plankH) with a comb of {ncell} hex cells (open cells dip in, amber; {ncream} capped cells bulge, cream); lower right of the comb broken out; racked',
 'DB06_Beehive_Lid': 'hive lid like SP04_Lid (M_Forest_Special lid planks, sloped top), front-right corner broken off plank by plank (splintered, lightdiag), a crack along a plank seam with the back-left part sagging, warped',
 'DB06_Beehive_CombChunk': 'irregular broken piece of comb: 6 hex cells (2 capped cream), broken walls, 2 honey drips, slightly curled',
}
for r in rows:
    r['note'] = NOTES.get(r['name'], 'convex hull collider (same origin as its piece)')
print(json.dumps([(r['name'], r['tris'], r['size_m']) for r in rows]))
json.dump(rows, open(OUT + 'manifest.json', 'w'), indent=1)
bk.export_fbx(objs, OUT + 'SM_DB06_BeehiveParts.fbx')

if '--nocard' not in sys.argv:
    tiles, labels = [], []
    ANG = {'DB06_Beehive_Lid': ((-35, 35), (150, -30)), 'DB06_Beehive_CombChunk': ((-30, 20), (160, 25))}
    for o in pieces:
        for az, el in ANG.get(o.name, ((-30, 22), (150, 30))):
            tiles.append(bk.render_objs([o], OUT + f'_t_{o.name}_{az}.png', az=az, el=el, size=(420, 420), samples=9))
            labels.append(f'{o.name[5:]} az{az} el{el}')
    src = bk.import_src('SM_SP04_Beehive.fbx')
    save = [(o.location.copy(), o.rotation_euler.copy()) for o in pieces]
    place = {board: ((0.75, -0.25), (90, 0, 15)), fr: ((-0.7, -0.35), (90, 0, -25)), lid_o: ((0.15, -0.95), (8, -6, 20)),
             chunk: ((-0.45, -0.95), (0, 0, 40))}
    for o, ((x_, y_), rr) in place.items():
        o.rotation_euler = [math.radians(a) for a in rr]
        bpy.context.view_layer.update()
        lo, hi = bk.bounds([o])
        o.location = Vector((x_, y_, o.location.z - float(lo[2])))
        bpy.context.view_layer.update()
    tiles.append(bk.render_objs(pieces + src, OUT + '_t_scale.png', az=-20, el=30, size=(420, 420), samples=9))
    labels.append('with SM_SP04_Beehive')
    for o, (l, r) in zip(pieces, save):
        o.location = l; o.rotation_euler = r
    for s_ in src: bpy.data.objects.remove(s_)
    bk.sheet(tiles, OUT + 'card.jpg', cols=3, labels=labels, title='DB06 Beehive debris')
    for t in tiles: os.remove(t)
bk.save_blend(OUT + 'beehive.blend')
import shutil; shutil.copy(__file__, OUT + 'build.py')
