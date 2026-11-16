import sys, os, math, random
sys.path.insert(0, '/home/claude/ant/models'); from lib_sup import *
from mathutils import Vector, Matrix
D = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(D, 'out'); os.makedirs(OUT, exist_ok=True)
REND = '--norender' not in sys.argv

ak.reset()
rnd = random.Random(33)
FZ = 0.25          # walkable basin floor
RUN = 0.95         # front lip ramp run (FZ/RUN -> 14.7 deg)
# polygonal outer outline (CCW from the front-right end of the opening); the front edge C[-1]->C[0] is the opening
C = [Vector((p[0], p[1], 0.0)) for p in [(1.62, -2.52), (2.25, -2.22), (3.32, -1.45), (3.52, 0.62), (2.38, 2.32),
                                         (0.18, 2.55), (-2.22, 2.36), (-3.46, 0.88), (-3.40, -1.32), (-2.30, -2.20),
                                         (-1.68, -2.52)]]
# per rim edge (C[i] -> C[i+1]): (height at start, height at end), thickness, segments, cracks [(f, depth, kind)]
# kind: 'top' V notch in the rim top, 'out' notch down the outer wall, 'in' notch into the inner wall
EDGES = [((0.60, 0.60), 0.90, 2, []),                                   # front-right blunt stub
         ((0.80, 0.92), 0.95, 2, [(.5, .18, 'out')]),                   # front-right   (step up from the stub)
         ((1.10, 1.30), 1.05, 4, [(.3, .25, 'top'), (.72, .2, 'out')]),  # right         (step)
         ((1.38, 1.58), 1.18, 4, [(.45, .28, 'top'), (.8, .22, 'in')]),  # back-right
         ((1.74, 1.68), 1.28, 6, [(.22, .3, 'out'), (.5, .3, 'top'), (.78, .25, 'in')]),   # back (highest)
         ((1.66, 1.50), 1.22, 5, [(.3, .28, 'top'), (.65, .24, 'out')]),  # back-left
         ((1.30, 1.18), 1.10, 4, [(.4, .25, 'out'), (.8, .2, 'in')]),     # left          (step down)
         ((1.14, 1.02), 1.04, 3, [(.55, .22, 'out')]),                   # left-front
         ((0.86, 0.80), 0.96, 2, [(.5, .16, 'top')]),                    # front-left    (step down)
         ((0.62, 0.62), 0.90, 2, [])]                                    # front-left blunt stub
NC = len(C)

def mitre(i):
    a, b, c = C[(i - 1) % NC], C[i], C[(i + 1) % NC]
    n1 = Vector((-(b - a).y, (b - a).x, 0)).normalized()   # left normal of a CCW edge = inward
    n2 = Vector((-(c - b).y, (c - b).x, 0)).normalized()
    m = (n1 + n2).normalized()
    return m / max(m.dot(n1), .5)

def rim_profile(base, inward, h, t, jit, crack=None):
    """inside->outside: floor edge, inner wall top, inner rim edge, outer rim edge, outer facet break, base."""
    j = list(jit)
    dP1, dP3, dP4, zTop = 0.0, 0.0, 0.0, 0.0
    if crack:
        dep, kind = crack
        dep *= .6
        if kind == 'top':
            zTop = -dep; dP3 = dep * .25
        elif kind == 'out':
            dP3 = dep * .9; dP4 = dep * .8; zTop = -dep * .35
        else:
            dP1 = -dep * .9; zTop = -dep * .3
    return [base + inward * t + Vector((0, 0, FZ)),
            base + inward * (t * .90 + j[0] + dP1) + Vector((0, 0, h - .14 + j[1] + zTop)),
            base + inward * (t * .80 + dP1 * .6) + Vector((0, 0, h + j[2] + zTop)),
            base + inward * (t * .27 + j[3] + dP3) + Vector((0, 0, h - .02 + j[2] * .5 + zTop)),
            base + inward * (t * .13 + j[4] + dP4) + Vector((0, 0, h * .52 + j[5])),
            base]

def ramp_profile(base, inward):
    return [base + inward * RUN * f + Vector((0, 0, FZ * f)) for f in (1, .8, .6, .4, .2, 0)]

E = ak.Mesh('Earth'); F = ak.Mesh('Floor'); PR = ak.Mesh('Props'); RT = ak.Mesh('Root')
uv0 = lambda pts, n: [(0, 0)] * len(pts)
floor_ring = []

def strip(mesh, cols, tangent):
    for k in range(len(cols) - 1):
        A, B = cols[k], cols[k + 1]
        for j in range(len(A) - 1):
            mesh.face([A[j], A[j + 1], B[j + 1], B[j]], [(0, 0)] * 4)
    for cols_end, d in ((cols[0], -tangent), (cols[-1], tangent)):
        poly = list(cols_end) + [Vector((cols_end[0].x, cols_end[0].y, 0))]
        face_out(mesh, poly, d, uv0)

def edge_info(i, f):
    a, b = C[i], C[i + 1]
    (h0, h1), t, _, _ = EDGES[i]
    n_in = Vector((-(b - a).y, (b - a).x, 0)).normalized()
    return a.lerp(b, f), n_in, h0 + (h1 - h0) * f, t, (b - a).normalized()

for i in range(NC - 1):
    (h0, h1), t, nseg, cracks = EDGES[i]
    a, b = C[i], C[i + 1]
    ma, mb = mitre(i), mitre(i + 1)
    n_in = Vector((-(b - a).y, (b - a).x, 0)).normalized()
    fs = [(s / nseg, None) for s in range(nseg + 1)]
    for (fc, dep, kind) in cracks:
        w = .035 * 3 / max((b - a).length, 1)
        fs = [x for x in fs if abs(x[0] - fc) > w * 1.6]
        fs += [(fc - w, None), (fc, (dep, kind)), (fc + w, None)]
    fs.sort(key=lambda x: x[0])
    cols = []
    for f, crack in fs:
        base = a.lerp(b, f)
        end = f in (0.0, 1.0)
        inward = (ma if f == 0 else mb) if end else n_in
        if not end and crack is None:
            base = base - n_in * rnd.uniform(-.12, .08)
        jit = [rnd.uniform(-.05, .05), rnd.uniform(-.04, .03), rnd.uniform(-.03, .03), rnd.uniform(-.05, .05),
               rnd.uniform(-.06, .06), rnd.uniform(-.06, .06)]
        if crack:
            jit = [0] * 6
        cols.append(rim_profile(base, inward, h0 + (h1 - h0) * f, t, jit, crack))
    strip(E, cols, (b - a).normalized())
    for s, col in enumerate(cols):
        if s == 0 and floor_ring and (floor_ring[-1] - col[0]).length < 1e-4:
            continue
        floor_ring.append(col[0])
a, b = C[-1], C[0]
cols = [ramp_profile(a, mitre(NC - 1)), ramp_profile(a.lerp(b, .5) + Vector((0, -.04, 0)),
        Vector((-(b - a).y, (b - a).x, 0)).normalized()), ramp_profile(b, mitre(0))]
strip(F, cols, (b - a).normalized())
floor_ring += [cols[0][0], cols[1][0], cols[2][0]]
c = Vector((0.1, 0.2, 0))
rings = [floor_ring]
for s, dz in ((.75, -.015), (.5, -.03), (.25, -.035)):
    rings.append([Vector((c.x + (p.x - c.x) * s + rnd.uniform(-.05, .05), c.y + (p.y - c.y) * s + rnd.uniform(-.05, .05),
                          FZ + dz + rnd.uniform(-.008, .008))) for p in floor_ring])
M = len(floor_ring)
for r in range(len(rings) - 1):
    for k in range(M):
        k2 = (k + 1) % M
        face_out(F, [rings[r][k], rings[r][k2], rings[r + 1][k2], rings[r + 1][k]], (0, 0, 1), uv0)
cz = Vector((c.x, c.y, FZ - .035))
for k in range(M):
    face_out(F, [rings[-1][k], rings[-1][(k + 1) % M], cz], (0, 0, 1), uv0)

# ================================================================ details
RIMS = list(range(NC - 1))
WEIGHTS = [(C[i + 1] - C[i]).length for i in RIMS]
def pick_edge(r):
    return r.choices(RIMS, WEIGHTS)[0]
def foot(r, d0=0.05, d1=0.7):
    i = pick_edge(r); f = r.uniform(.05, .95)
    B, n_in, h, t, tg = edge_info(i, f)
    return B - n_in * r.uniform(d0, d1)
def rim_top(r):
    i = pick_edge(r); f = r.uniform(.05, .95)
    B, n_in, h, t, tg = edge_info(i, f)
    return B + n_in * r.uniform(t * .35, t * .72) + Vector((0, 0, h - .01))
def floor_edge(r, d0=0.08, d1=0.55):
    i = pick_edge(r); f = r.uniform(.08, .92)
    B, n_in, h, t, tg = edge_info(i, f)
    return B + n_in * (t + r.uniform(d0, d1)) + Vector((0, 0, FZ - .01))
def outer_wall(r, z0=.15, z1=.5):
    i = pick_edge(r); f = r.uniform(.1, .9)
    B, n_in, h, t, tg = edge_info(i, f)
    z = h * r.uniform(z0, z1)
    return B + n_in * (t * .13 * z / (h * .52)) + Vector((0, 0, z)), -n_in, h

# clods: big ones leaning at the foot, medium on the rim top, small on the floor along the walls (Earth)
for k in range(46):
    p = foot(rnd, -.08, .45); s = rnd.uniform(.08, .26)
    ak.blob(E, (p.x, p.y, 0), (s * rnd.uniform(1, 1.5), s, s * rnd.uniform(.6, .9)), rings=2, sides=6, jitter=.25, seed=100 + k)
for k in range(14):
    p = rim_top(rnd); s = rnd.uniform(.06, .14)
    ak.blob(E, (p.x, p.y, p.z - s * .2), (s * 1.3, s, s * .7), rings=2, sides=5, jitter=.25, seed=200 + k)
for k in range(22):
    p = floor_edge(rnd, .0, .35); s = rnd.uniform(.05, .13)
    ak.blob(E, (p.x, p.y, p.z), (s * 1.3, s, s * .6), rings=2, sides=5, jitter=.25, seed=300 + k)
# outer ledges: flat earth slabs jutting from the outer wall
for k in range(10):
    p, out, h = outer_wall(rnd, .25, .45)
    tg = Vector((-out.y, out.x, 0))
    E.box(p + out * .12, (rnd.uniform(.35, .7), .3, .1), rot=(0, rnd.uniform(-.12, .12), math.atan2(tg.y, tg.x)),
          jitter=.03, seed=400 + k, taper=(.85, .8))
# pebbles and crumbs (Props)
scatter(PR, 34, lambda r: foot(r, -.05, .6), (.03, .08), 'pebble', seed=500, sides=5, flat=.5)
scatter(PR, 18, lambda r: floor_edge(r, 0, .5), (.025, .05), 'pebble', seed=501, sides=5, flat=.6)
scatter(PR, 8, rim_top, (.03, .06), 'pebble', seed=502, sides=5, flat=.6)
scatter(PR, 90, lambda r: foot(r, -.05, .7), (.012, .03), 'earth_props', seed=503, sides=4)
scatter(PR, 50, lambda r: floor_edge(r, 0, .7), (.01, .022), 'earth_props', seed=504, sides=4)
scatter(PR, 30, rim_top, (.012, .028), 'earth_props', seed=505, sides=4)
# egg-shell shards (old hatchings) on the floor and at the foot
for k in range(18):
    p = floor_edge(rnd, .05, .8) if k < 11 else foot(rnd, .1, .55)
    shard(PR, p + Vector((0, 0, .03)), rnd.uniform(.07, .15), 'egg', 'shell_in', seed=600 + k, curv=.7, th=.012,
          M=Matrix.Rotation(rnd.uniform(-.5, .5), 3, 'X') @ Matrix.Rotation(rnd.uniform(0, 6.3), 3, 'Z'))
# dry leaves: floor edge, rim top, foot
for k in range(24):
    sel = k % 3
    p = floor_edge(rnd, .05, .9) if sel == 0 else (rim_top(rnd) if sel == 1 else foot(rnd, .05, .6))
    small_leaf(PR, p + Vector((0, 0, .015)), rnd.uniform(.22, .42), rnd.uniform(.1, .18),
               rnd.choice(('leaf_dry_0', 'leaf_dry_1', 'leaf_dry_2', 'leaf_green')), seed=700 + k,
               rx=rnd.uniform(-.25, .25), droop=.02, curl=.02)
# twigs at the foot
for k in range(12):
    i_ = pick_edge(rnd); B_, n_, _, _, tg_ = edge_info(i_, rnd.uniform(.1, .9))
    p = B_ - n_ * rnd.uniform(.1, .45)
    a = math.atan2(tg_.y, tg_.x) + rnd.uniform(-.5, .5) + rnd.choice((0, math.pi))
    twig(PR, p + Vector((0, 0, .015)), (math.cos(a), math.sin(a), 0), rnd.uniform(.4, .8), r=rnd.uniform(.012, .022),
         seed=800 + k, branches=rnd.randint(1, 3), segs=4, ground=True)
# tiny burrow holes in the outer wall with clod lips
for k in range(9):
    p, out, h = outer_wall(rnd, .15, .45)
    burrow(PR, p + out * .01, out, r=rnd.uniform(.045, .08), depth=.18, seed=900 + k)
    for q in range(3):
        a = q * 2.1 + rnd.uniform(-.3, .3)
        tg = Vector((-out.y, out.x, 0))
        lp = p + out * .03 + (tg * math.cos(a) + Vector((0, 0, 1)) * math.sin(a)) * .085
        ak.blob(E, lp, (.05, .04, .035), rings=2, sides=5, jitter=.2, seed=950 + k * 3 + q, flat_bottom=False)
# baby mushrooms in the shade at the foot (non-glowing)
for k in range(6):
    p = foot(rnd, .0, .25)
    for q in range(rnd.randint(2, 4)):
        off = Vector((rnd.uniform(-.1, .1), rnd.uniform(-.1, .1), 0))
        mushroom(PR, p + off, h=rnd.uniform(.06, .14), cap_r=rnd.uniform(.03, .055), seed=1000 + k * 5 + q,
                 lean=(rnd.uniform(-.3, .3), rnd.uniform(-.3, .3)), cap_region=rnd.choice(('cap_mint', 'cap_yellow')))
# roots poking out of the rim (outer wall, a few out of the inner wall high up) + hair rootlets
for k in range(13):
    inner = k >= 10
    i = pick_edge(rnd) if not inner else rnd.choice((3, 4, 5))
    f = rnd.uniform(.15, .85)
    B, n_in, h, t, tg = edge_info(i, f)
    if inner:
        z = h * rnd.uniform(.72, .88)
        p0 = B + n_in * (t * .9 - .15) + Vector((0, 0, z)); d = n_in
    else:
        z = h * rnd.uniform(.35, .8)
        p0 = B + n_in * (t * .13 * min(z / (h * .52), 1.6) + .15) + Vector((0, 0, z)); d = -n_in
    L = rnd.uniform(.7, 1.3) if not inner else rnd.uniform(.4, .7)
    side = tg * rnd.uniform(-.4, .4)
    pts = [p0]
    for s in range(1, 6):
        u = s / 5
        pts.append(p0 + (d + side) * L * u + Vector((0, 0, -L * .9 * u * u)) +
                   Vector((rnd.uniform(-.04, .04), rnd.uniform(-.04, .04), rnd.uniform(-.03, .03))))
    if not inner and pts[-1].z < .02:
        for q in pts:
            q.z = max(q.z, .03)
    r0 = rnd.uniform(.07, .12)
    RT.tube(pts, [r0 * (1 - .8 * s / 5) for s in range(6)], sides=5, jitter=.008, seed=1100 + k)
    for q in range(rnd.randint(2, 4)):
        s = rnd.randint(1, 4)
        b0 = pts[s]
        rd = (d * .3 + Vector((rnd.uniform(-.6, .6), rnd.uniform(-.6, .6), -1))).normalized()
        rl = rnd.uniform(.12, .3)
        RT.tube([b0, b0 + rd * rl * .5 + Vector((rnd.uniform(-.03, .03), 0, 0)), b0 + rd * rl],
                [r0 * .25, r0 * .15, r0 * .06], sides=3, seed=1200 + k * 5 + q, caps=(False, True))

eo = E.to_object('AN33a_Earth'); ak.auto_uv(eo)
fo = F.to_object('AN33a_Floor'); ak.auto_uv(fo)
po = PR.to_object('AN33a_Props')
ro = RT.to_object('AN33a_Root')
objs = [eo, fo, ro, po]
ak.ground(objs, center_xy=False)
check_normals(objs)
f = os.path.join(D, 'SM_AN33_QueenBed.fbx'); ak.export_fbx(objs, f)
rec = ak.variant_record('AN33a', 'Queen earth bed', objs, f)
print(rec)
write_manifest(OUT, 'AN33 queen-bed', [rec], solidity='walk-on-top (basin floor) + solid rim',
               collider='mesh collider on Earth + Floor (clods/roots/props decor: no collider or simple boxes); floor at '
                        'z=0.25 is walkable, front lip ramp 15 deg', emissive=False,
               notes='Pivot bottom centre. Polygonal horseshoe rim (Earth) of 10 straight facets around a 3.4 m front '
               'opening: steep outer walls with one facet break, flat rim tops with hard edges, faceted inner walls. Rim '
               'height highest at the back (~1.74 m, concept proportions), sloping per facet and stepping down at corners to '
               'blunt front stubs ~0.6 m. v2 details: 14 cracks cut into the rim (V notches in the rim top, down the outer '
               'wall, into the inner wall), 10 outer ledges, ~82 earth clods (foot/rim/floor edge), ~60 pebbles, ~170 '
               'crumbs, 18 egg-shell shards, 24 dry leaves, 12 twigs, 9 burrow holes with clod lips (Props dark), 6 clusters '
               'of baby mushrooms (non-glowing), 13 roots poking out of the rim with hair rootlets (Root). Front ramp kept '
               'clear (only crumbs nearby); basin centre kept clear for the queen. Front lip ramp 0.95 m (Floor) up to the '
               'basin floor (Floor, z=0.25).')
if REND:
    t, l = renders(objs, OUT, 'a', el=24, margin=0.8)
    for az, lab, tgt in ((-30, 'close front-left', (-2.2, -1.2, .6)), (60, 'close right', (2.8, 0.3, .6)), (165, 'close back', (0.6, 2.4, .8)), (-110, 'close left', (-3.0, 0.2, .6))):
        pth = os.path.join(OUT, 'a_close_%d.png' % az)
        ak.render(objs, pth, az=az, el=18, size=(360, 360), samples=8, target=tgt, dist=10)
        t.append(pth); l.append(lab)
    card('33-queen-bed', OUT, t, l, 'AN33 queen bed (v2 detail)')
ak.save_blend(os.path.join(D, 'AN33_QueenBed.blend'))
