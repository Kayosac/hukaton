"""DB04 Lantern debris: parts of the ground lantern SM_PR02_LanternGround (0.29 x 0.42 m).
Cage = source base plate + window frame (cutout, closed as a hex prism) + top collar + handle with rope grip,
remaining panes as non-glowing amber glass core with two panes knocked out; Lid = the source pyramid cap, closed,
dented and crooked; Glass = a jagged amber shard of a pane. Source shapes/UVs are copied from the FBX and deformed."""
import sys, os, math, random, json, shutil; sys.path.insert(0, '/home/claude/brk/work')
import bpy, bmesh, numpy as np
from mathutils import Vector, Matrix
import bk
from bk import PROPS as P, sub
bpy.ops.wm.read_factory_settings(use_empty=True)
MP, MC = 'M_Forest_Props', 'M_Forest_PropsCutout'
OUT = '/home/claude/brk/out/lantern/'
os.makedirs(OUT, exist_ok=True)
px = lambda x, y: (x / 256.0, 1 - y / 256.0)

src = {o.name.split('.')[0]: o for o in bk.import_src('SM_PR02_LanternGround.fbx')}
SL = src['PR02_Lantern'].data
uvl = SL.uv_layers[0].data
def src_faces(idx):
    out = []
    for i in idx:
        p = SL.polygons[i]
        out.append(([Vector(SL.vertices[v].co) for v in p.vertices], [tuple(uvl[l].uv) for l in p.loop_indices]))
    return out
R = lambda a, b: list(range(a, b + 1))
F_BASE = src_faces(R(0, 15) + R(77, 84))
F_LID = src_faces(R(16, 40) + R(85, 111))
F_HANDLE = src_faces(R(41, 60) + R(112, 131))
F_ROPE = src_faces(R(61, 76) + R(132, 139))
FRAME_TOP = [(-0.114, 0.016), (-0.054, 0.100), (0.050, 0.090), (0.093, -0.005), (0.032, -0.089), (-0.072, -0.079)]
FRAME_BOT = [(-0.106, 0.012), (-0.045, 0.096), (0.058, 0.086), (0.101, -0.009), (0.040, -0.094), (-0.063, -0.083)]
GLASS_TOP = [(-0.100, 0.014), (-0.047, 0.087), (0.042, 0.078), (0.078, -0.004), (0.026, -0.076), (-0.063, -0.067)]
GLASS_BOT = [(-0.092, 0.010), (-0.040, 0.083), (0.049, 0.074), (0.086, -0.008), (0.034, -0.080), (-0.055, -0.071)]
DARK = P['darkpatch']


def finish(b, name):
    o = b.obj(name)
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.triangulate(bm, faces=bm.faces, quad_method='BEAUTY', ngon_method='EAR_CLIP')
    bm.to_mesh(o.data); bm.free()
    for p in o.data.polygons: p.use_smooth = False
    return o


def hull_col(o, pts_local, name=None):
    """Convex hull collider from a subset of the piece's points (object-local), origin = o's origin."""
    bm = bmesh.new()
    for p in pts_local: bm.verts.new(Vector(p))
    bmesh.ops.convex_hull(bm, input=bm.verts)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    bmesh.ops.triangulate(bm, faces=bm.faces)
    nm = (name or o.name) + '_COL'
    me = bpy.data.meshes.new(nm); bm.to_mesh(me); bm.free()
    c = bpy.data.objects.new(nm, me); bpy.context.scene.collection.objects.link(c)
    c.location = o.location.copy(); c.display_type = 'WIRE'
    return c


def hexprism(b, top, bot, zt, zb, wall_uv, cap_rect, m, D, cap_m=None, hole=None):
    """Closed (possibly skewed) hex prism; wall_uv(k, corner) -> uv for corner in ('tl','tr','br','bl')."""
    n = len(top)
    T = [tuple(D(Vector((x, y, zt)))) for x, y in top]
    Bv = [tuple(D(Vector((x, y, zb)))) for x, y in bot]
    for k in range(n):
        k2 = (k + 1) % n
        b.face([Bv[k], Bv[k2], T[k2], T[k]], [wall_uv(k, 'bl'), wall_uv(k, 'br'), wall_uv(k, 'tr'), wall_uv(k, 'tl')], m)
    cm = m if cap_m is None else cap_m
    cu = lambda x, y: bk.uvr(MP, cap_rect, 0.5 + x * 3, 0.5 + y * 3)
    if hole is None:
        b.face(T, [cu(x, y) for x, y in top], cm)
    else:   # open-looking top: annulus sloping down into a near-black inner hex
        sc, dz = hole
        ti = [(x * sc, y * sc) for x, y in top]
        Ti = [tuple(D(Vector((x, y, zt - dz)))) for x, y in ti]
        for k in range(n):
            k2 = (k + 1) % n
            b.face([T[k], T[k2], Ti[k2], Ti[k]], [cu(*top[k]), cu(*top[k2]), cu(*ti[k2]), cu(*ti[k])], cm)
        b.face(Ti, [bk.uvr(MP, P['darkcorner'], 0.5 + x * 4, 0.5 + y * 4) for x, y in ti], cm)
    b.face(Bv[::-1], [cu(x, y) for x, y in bot][::-1], cm)
    return T, Bv


# ======================================================================= 1) cage
def lean(p):
    """Whole-cage bend: lean the top to +x/-y, twist a few degrees, squeeze one side."""
    p = Vector(p); h = max(p.z - 0.06, 0) / 0.30
    a = math.radians(5.0) * h
    x, y = p.x * math.cos(a) - p.y * math.sin(a), p.x * math.sin(a) + p.y * math.cos(a)
    x += 0.020 * h * h; y -= 0.010 * h
    if p.z > 0.06 and p.x < 0: x *= 1 - 0.07 * min(h * 1.6, 1)   # left side pushed in a bit
    return Vector((x, y, p.z))

def base_def(p):
    """Base plate with its front-right corner bent up."""
    p = Vector(p); w = max(0.0, (p.x - p.y - 0.17) / 0.12)
    return Vector((p.x, p.y, p.z + 0.030 * w * w))

def handle_def(p):
    """Handle: ends dropped into the collar, the loop bent backwards and sideways."""
    p = Vector(p)
    if p.z < 0.296:                 # end rings sat inside the (missing) lid
        p.z -= 0.010
        p.x *= 0.9
    h = max(p.z - 0.28, 0) / 0.13
    p.y += 0.045 * h ** 1.4
    p.x += 0.012 * h - 0.010 * h * h
    p.z -= 0.012 * h * max(p.x + 0.05, 0) / 0.15     # right half sagged
    return lean(p)

bc = bk.B([MP, MC])
for pts, uvs in F_BASE:
    bc.face([tuple(base_def(p)) for p in pts], uvs, 0)
# window frame: closed hex prism with the cutout frame on the walls (source UVs), dark caps
frame_uv = lambda k, c: {'tl': px(54, 70), 'tr': px(94, 70), 'br': px(94, 118), 'bl': px(54, 118)}[c]
hexprism(bc, FRAME_TOP, FRAME_BOT, 0.270, 0.060, frame_uv, DARK, 1, lean, cap_m=0)
# top collar the lid sat on (dark metal), handle ends go into it
COL_TOP = [(x * 1.16, y * 1.16) for x, y in FRAME_TOP]
COL_BOT = [(x * 1.13, y * 1.13) for x, y in FRAME_TOP]
cw = lambda k, c: bk.uvr(MP, DARK, (k + (c in ('tr', 'br'))) / 6.0, 0.62 if c in ('tl', 'tr') else 0.5)
hexprism(bc, COL_TOP, COL_BOT, 0.284, 0.262, cw, DARK, 0, lean, hole=(0.72, 0.014))
# glass core: remaining panes as non-glowing amber; panes 4 (front) and 5 (front-left) knocked out:
# their shared corner is pulled to the centre, the two inner faces are the dark lantern interior
gt = [Vector((x, y, 0.268)) for x, y in GLASS_TOP]; gb = [Vector((x, y, 0.062)) for x, y in GLASS_BOT]
gt[5] = Vector((0.004, 0.004, 0.268)); gb[5] = Vector((0.010, 0.002, 0.062))
GT = [tuple(lean(p)) for p in gt]; GB = [tuple(lean(p)) for p in gb]
amb = lambda c: {'tl': px(22, 122), 'tr': px(54, 122), 'br': px(54, 162), 'bl': px(22, 162)}[c]
for k in range(6):
    k2 = (k + 1) % 6
    if k in (4, 5):
        d = lambda fu, fv: bk.uvr(MP, sub(DARK, 0.55, 1.0, 0.55, 1.0), fu, fv)
        bc.face([GB[k], GB[k2], GT[k2], GT[k]], [d(0, 0), d(1, 0), d(1, 1), d(0, 1)], 0)
    else:
        bc.face([GB[k], GB[k2], GT[k2], GT[k]], [amb('bl'), amb('br'), amb('tr'), amb('tl')], 0)
cu = lambda p: bk.uvr(MP, P['amber'], 0.5 + p[0] * 3, 0.5 + p[1] * 3)
bc.face(GT, [cu(p) for p in GT], 0); bc.face(GB[::-1], [cu(p) for p in GB][::-1], 0)
# jagged remnant of pane 5 left in the frame's bottom-left corner (thin closed wedge)
o0 = Vector((*GLASS_BOT[0], 0.070)); o5 = Vector((*GLASS_BOT[5], 0.070)); up = Vector((*GLASS_TOP[0], 0.268)) - Vector((*GLASS_BOT[0], 0.062))
rem = [o0, o0.lerp(o5, 0.62), o0.lerp(o5, 0.30) + up * 0.20, o0 + up * 0.52]
nrm = (o5 - o0).cross(up).normalized()
if nrm.dot(Vector((o0.x, o0.y, 0))) > 0: nrm = -nrm          # inward
front = [tuple(lean(p - nrm * 0.0008)) for p in rem]; back = [tuple(lean(p + nrm * 0.004)) for p in rem]
ra = [bk.uvr(MP, P['amber'], u, v) for u, v in ((0.1, 0.1), (0.7, 0.1), (0.45, 0.35), (0.1, 0.6))]
bc.face(front, ra, 0); bc.face(back[::-1], ra[::-1], 0)
for k in range(4):
    k2 = (k + 1) % 4
    bc.face([front[k], front[k2], back[k2], back[k]], [ra[k], ra[k2], ra[k2], ra[k]], 0)
# handle (source loop, ends capped) + rope grip
for pts, uvs in F_HANDLE + F_ROPE:
    bc.face([tuple(handle_def(p)) for p in pts], uvs, 0)
ends = [[(0.091, -0.002, 0.286), (0.091, 0.013, 0.286), (0.107, 0.014, 0.284), (0.106, -0.002, 0.284)],
        [(-0.104, -0.001, 0.291), (-0.104, 0.013, 0.291), (-0.120, 0.014, 0.289), (-0.119, -0.002, 0.289)]]
# use the exact source coordinates of the end rings so they weld
hv = {tuple(round(c, 3) for c in p): p for pts, _ in F_HANDLE for p in pts}
for ring in ends:
    q = [hv[tuple(round(c, 3) for c in p)] for p in ring]
    bc.face([tuple(handle_def(p)) for p in q], [bk.uvr(MP, DARK, u, v) for u, v in ((0, 0), (.1, 0), (.1, .1), (0, .1))], 0)
cage = finish(bc, 'DB04_Lantern_Cage')
CAGE_C = cage.location.copy()      # B.obj moved the origin here (bounds centre)
body_pts = []   # for the collider: base + frame + collar (no handle)
for pts, _ in F_BASE: body_pts += [base_def(p) for p in pts]
for (x, y) in COL_TOP: body_pts.append(lean(Vector((x, y, 0.286))))
for (x, y) in FRAME_BOT: body_pts.append(lean(Vector((x, y, 0.060))))

# ======================================================================= 2) lid: dented, crooked
rnd = random.Random(7)
jit = {}
def lid_def(p):
    p = Vector(p); key = tuple(round(c, 4) for c in p)
    if key not in jit:
        jit[key] = Vector((rnd.uniform(-.004, .004), rnd.uniform(-.004, .004), rnd.uniform(-.003, .003)))
    q = p + jit[key] * (1 if p.z > 0.27 else 0.4)
    h = (p.z - 0.268) / 0.054
    q.x += 0.016 * h; q.y -= 0.010 * h            # top pushed off-centre
    a = math.radians(9) * h                         # and twisted
    x, y = q.x * math.cos(a) - q.y * math.sin(a), q.x * math.sin(a) + q.y * math.cos(a)
    q.x, q.y = x, y
    # front-left of the rim squashed in and down (hit the ground)
    w = max(0.0, (-p.x - p.y) / 0.16 - 0.25)
    q.z -= 0.016 * w * (1 if p.z > 0.27 else 0.5)
    q.x += 0.012 * w; q.y += 0.008 * w
    return q
bl = bk.B([MP])
DENT = {(0.004, -0.048, 0.315), (-0.089, -0.097, 0.287), (0.036, -0.109, 0.287), (-0.056, -0.042, 0.315),
        (-0.045, 0.061, 0.315), (0.058, 0.108, 0.287), (-0.067, 0.121, 0.287), (0.014, 0.055, 0.315)}
quads = {}
for pts, uvs in F_LID:
    ks = [tuple(round(c, 3) for c in p) for p in pts]
    # the two slope quads (each 2 tris in the source) get a pushed-in centre vertex = dents
    grp = None
    for gi, g in enumerate(((0.004, -0.048, 0.315), (-0.045, 0.061, 0.315))):
        if all(k in DENT for k in ks) and len(set(ks)) == 3 and (g in ks or
           (gi == 0 and (-0.089, -0.097, 0.287) in ks and (0.036, -0.109, 0.287) in ks) or
           (gi == 1 and (0.058, 0.108, 0.287) in ks and (-0.067, 0.121, 0.287) in ks)):
            grp = gi
    if grp is not None:
        for p, uv in zip(pts, uvs): quads.setdefault(grp, {})[tuple(round(c, 4) for c in p)] = (p, uv)
        continue
    bl.face([tuple(lid_def(p)) for p in pts], uvs, 0)
for gi, d in quads.items():
    vs = list(d.values())
    c = sum((v[0] for v in vs), Vector()) / len(vs)
    cuv = (sum(v[1][0] for v in vs) / len(vs), sum(v[1][1] for v in vs) / len(vs))
    # order the 4 corners around the centre
    nrm = (vs[1][0] - vs[0][0]).cross(vs[2][0] - vs[0][0]).normalized()
    ax = (vs[0][0] - c).normalized(); ay = nrm.cross(ax)
    vs.sort(key=lambda v: math.atan2((v[0] - c).dot(ay), (v[0] - c).dot(ax)))
    out = Vector((c.x, c.y, 0)).normalized()
    cd = c - out * (0.030 if gi == 0 else 0.020) - Vector((0, 0, 0.020 if gi == 0 else 0.013))
    for k in range(4):
        a, bb = vs[k], vs[(k + 1) % 4]
        bl.face([tuple(lid_def(a[0])), tuple(lid_def(bb[0])), tuple(lid_def(cd))], [a[1], bb[1], cuv], 0)
# close the bottom (inner hex of the underside ring)
inner = [Vector(p) for p in [(-0.108, 0.015, 0.268), (-0.051, 0.095, 0.268), (0.047, 0.085, 0.268),
                             (0.087, -0.004, 0.268), (0.030, -0.084, 0.268), (-0.068, -0.074, 0.268)]]
lv = {tuple(round(c, 3) for c in p): p for pts, _ in F_LID for p in pts}
inner = [lv[tuple(round(c, 3) for c in p)] for p in inner]
bl.face([tuple(lid_def(p)) for p in inner][::-1], [bk.uvr(MP, sub(DARK, .5, 1, .5, 1), .5 + p.x * 4, .5 + p.y * 4) for p in inner][::-1], 0)
lid = finish(bl, 'DB04_Lantern_Lid')

# ======================================================================= 3) glass shard
rnd = random.Random(23)
ol = [(0.0, 0.0), (0.038, -0.002), (0.074, 0.002),            # straight edge that sat in the frame
      (0.052, 0.019), (0.057, 0.027), (0.034, 0.052), (0.027, 0.030), (0.008, 0.026)]
bg = bk.B([MP])
TH = 0.004
Tp = [(x, y, TH / 2 + rnd.uniform(-.0006, .0006)) for x, y in ol]
Bp = [(x, y, -TH / 2 + rnd.uniform(-.0006, .0006)) for x, y in ol]
bend = lambda p: (p[0], p[1], p[2] + 0.04 * (p[0] - 0.036) ** 2 * 3)
Tp = [bend(p) for p in Tp]; Bp = [bend(p) for p in Bp]
au = lambda x, y: bk.uvr(MP, P['amber'], 0.1 + x * 11, 0.1 + y * 11)
bg.face(Tp, [au(x, y) for x, y in ol], 0); bg.face(Bp[::-1], [au(x, y) for x, y in ol][::-1], 0)
for k in range(len(ol)):
    k2 = (k + 1) % len(ol)
    bg.face([Bp[k], Bp[k2], Tp[k2], Tp[k]], [au(*ol[k]), au(*ol[k2]), au(ol[k2][0], ol[k2][1] + .003), au(ol[k][0], ol[k][1] + .003)], 0)
glass = finish(bg, 'DB04_Lantern_Glass')

# ======================================================================= layout, colliders, export
pieces = [(cage, 'Lantern body without the lid: source base plate (front-right corner bent up), window frame '
                 '(M_Forest_PropsCutout frame region on a closed hex prism), dark top collar, handle loop + rope grip '
                 '(source UVs) bent back/sideways; whole cage leans and twists. Panes 4/5 (front, front-left) knocked '
                 'out (dark interior + small jagged remnant), the other 4 panes non-glowing amber (M_Forest_Props). '
                 'Collider = one hull of base+frame+collar, handle left out.'),
          (lid, 'Pyramid lid cap copied from the source (darkpatch UVs), bottom closed, two pushed-in dents on the '
                'slopes, front-left rim squashed down, top pushed off-centre and twisted.'),
          (glass, 'Jagged pane shard ~7 x 5 cm, 4 mm thick, one straight frame edge, slight curve; amber region on '
                  'M_Forest_Props (opaque, no glow).')]
x = 0.0
for i, (o, _) in enumerate(pieces):
    lo, hi = bk.bounds([o]); w = float(hi[0] - lo[0])
    if i: x += max(w, 0.1)
    o.location = Vector((x, 0, float(hi[2] - lo[2]) / 2))
    x += max(w, 0.1) + 0.08
bpy.context.view_layer.update()
objs = []
for o, _ in pieces:
    if o is cage:
        c = hull_col(o, [p - CAGE_C for p in body_pts])
    else:
        c = bk.col(o)
    objs += [o, c]
notes = {o.name: n for o, n in pieces}
rep = bk.report(objs)
for r in rep:
    if r['name'] in notes: r['note'] = notes[r['name']]
    if r['name'].endswith('_COL'): r['note'] = 'convex hull' + (' of base plate + frame + collar (handle left out)' if 'Cage' in r['name'] else ' of the piece')
print(json.dumps(rep, indent=1))
for k, o in src.items(): bpy.data.objects.remove(o)
bpy.context.view_layer.update()
bk.export_fbx(objs, OUT + 'SM_DB04_LanternParts.fbx')
json.dump({'set': 'DB04 Lantern', 'source_props': ['SM_PR02_LanternGround'], 'fbx': 'SM_DB04_LanternParts.fbx',
           'pieces': rep}, open(OUT + 'manifest.json', 'w'), indent=1)
shutil.copy(__file__, OUT + 'build.py')
bk.save_blend(OUT + 'lantern.blend')

# ======================================================================= card
if '--norender' not in sys.argv:
    tiles, labels = [], []
    for o, _ in pieces:
        for az, el in ((-30, 22), (140, 38)):
            tiles.append(bk.render_objs([o], OUT + f'_t_{o.name}_{az}.png', az=az, el=el, size=(360, 360), samples=8))
            labels.append(f'{o.name} {az}/{el}')
    srcl = bk.import_src('SM_PR02_LanternGround.fbx')
    for o in srcl: o.location = (-0.32, 0.05, 0)
    saved = {o.name: (o.location.copy(), o.rotation_euler.copy()) for o, _ in pieces}
    lo, hi = bk.bounds([cage]); cage.location = (0.0, 0.0, cage.location.z)
    lid.rotation_euler = (math.radians(14), math.radians(-8), 0.6); lid.location = (0.26, -0.16, 0.03)
    glass.rotation_euler = (0, 0, 0.9); glass.location = (0.22, 0.10, 0.003)
    bpy.context.view_layer.update()
    tiles.append(bk.render([o for o, _ in pieces] + srcl, OUT + '_t_g1.png', az=-25, el=28, size=(360, 360), samples=8))
    tiles.append(bk.render([o for o, _ in pieces] + srcl, OUT + '_t_g2.png', az=150, el=45, size=(360, 360), samples=8))
    labels += ['scale: source PR02 | pieces', 'scale (back)']
    for o, _ in pieces: o.location, o.rotation_euler = saved[o.name]
    for o in srcl: bpy.data.objects.remove(o)
    bpy.context.view_layer.update()
    bk.sheet(tiles, OUT + 'card.jpg', cols=4, labels=labels, title='DB04 Lantern parts  (SM_PR02_LanternGround)')
    for f in os.listdir(OUT):
        if f.startswith('_t_'): os.remove(OUT + f)
