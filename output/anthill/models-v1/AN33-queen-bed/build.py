import sys, os, math, random
sys.path.insert(0, '/home/claude/ant/models'); from lib_sup import *
from mathutils import Vector, Matrix
D = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(D, 'out'); os.makedirs(OUT, exist_ok=True)
REND = '--norender' not in sys.argv

ak.reset()
rnd = random.Random(33)
FZ = 0.25          # walkable basin floor
RUN = 0.95         # front lip ramp run (FZ/RUN -> 14.7 deg)
# polygonal outer outline (CCW from the front-right end of the opening); the front edge C8->C0 is the opening
C = [Vector((p[0], p[1], 0.0)) for p in [(1.62, -2.52), (2.25, -2.22), (3.32, -1.45), (3.52, 0.62), (2.38, 2.32), (0.18, 2.55),
                         (-2.22, 2.36), (-3.46, 0.88), (-3.40, -1.32), (-2.30, -2.20), (-1.68, -2.52)]]
# per rim edge (C[i] -> C[i+1]): (height at start, height at end), thickness, segments. Heights slope along a
# facet; where neighbouring facets disagree a vertical step face appears at the corner.
EDGES = [((0.48, 0.48), 0.80, 1),   # front-right blunt stub
         ((0.62, 0.70), 0.85, 1),   # front-right   (step up from the stub)
         ((0.84, 0.98), 0.92, 2),   # right         (step)
         ((1.02, 1.18), 1.02, 2),   # back-right
         ((1.25, 1.22), 1.10, 3),   # back (highest)
         ((1.22, 1.12), 1.05, 3),   # back-left
         ((0.98, 0.90), 0.95, 2),   # left          (step down)
         ((0.88, 0.80), 0.90, 2),   # left-front
         ((0.66, 0.62), 0.86, 1),   # front-left    (step down)
         ((0.50, 0.50), 0.80, 1)]   # front-left blunt stub
NC = len(C)

def mitre(i):
    """inward mitre direction at corner i (unit-depth offset)."""
    a, b, c = C[(i - 1) % NC], C[i], C[(i + 1) % NC]
    n1 = Vector((-(b - a).y, (b - a).x, 0)).normalized()   # left normal of a CCW edge = inward
    n2 = Vector((-(c - b).y, (c - b).x, 0)).normalized()
    m = (n1 + n2).normalized()
    return m / max(m.dot(n1), .5)

def rim_profile(base, inward, h, t, jit):
    """outer base -> ... ; returned inside->outside: floor edge, inner wall top, inner rim edge, outer rim edge,
    outer facet break, base. Steep walls, flat top, hard edges."""
    j = jit
    return [base + inward * t + Vector((0, 0, FZ)),
            base + inward * (t * .90 + j[0]) + Vector((0, 0, h - .14 + j[1])),
            base + inward * (t * .80) + Vector((0, 0, h + j[2])),
            base + inward * (t * .27 + j[3]) + Vector((0, 0, h - .02 + j[2] * .5)),
            base + inward * (t * .13 + j[4]) + Vector((0, 0, h * .52 + j[5])),
            base]

def ramp_profile(base, inward):
    return [base + inward * RUN * f + Vector((0, 0, FZ * f)) for f in (1, .8, .6, .4, .2, 0)]

E = ak.Mesh('Earth'); F = ak.Mesh('Floor')
uv0 = lambda pts, n: [(0, 0)] * len(pts)
floor_ring = []

def strip(mesh, cols, tangent):
    for k in range(len(cols) - 1):
        A, B = cols[k], cols[k + 1]
        for j in range(len(A) - 1):
            mesh.face([A[j], A[j + 1], B[j + 1], B[j]], [(0, 0)] * 4)
    # end caps (the visible part of a higher strip's cap forms the step down to its neighbour)
    for cols_end, d in ((cols[0], -tangent), (cols[-1], tangent)):
        poly = list(cols_end) + [Vector((cols_end[0].x, cols_end[0].y, 0))]
        face_out(mesh, poly, d, uv0)

edge_list = [(i, EDGES[i]) for i in range(NC - 1)]
for i, ((h0, h1), t, nseg) in edge_list:
    a, b = C[i], C[i + 1]
    ma, mb = mitre(i), mitre(i + 1)
    n_in = Vector((-(b - a).y, (b - a).x, 0)).normalized()
    cols = []
    for s in range(nseg + 1):
        f = s / nseg
        base = a.lerp(b, f)
        inward = ma.lerp(mb, f) if 0 < s < nseg else (ma if s == 0 else mb)
        if 0 < s < nseg:
            base = base - n_in * rnd.uniform(-.12, .08)       # crease the long faces
            inward = n_in
        jit = [rnd.uniform(-.05, .05), rnd.uniform(-.04, .03), rnd.uniform(-.03, .03), rnd.uniform(-.05, .05),
               rnd.uniform(-.06, .06), rnd.uniform(-.06, .06)]
        hh = h0 + (h1 - h0) * f
        cols.append(rim_profile(base, inward, hh, t, jit))
    strip(E, cols, (b - a).normalized())
    for s, col in enumerate(cols):
        if s == 0 and floor_ring and (floor_ring[-1] - col[0]).length < 1e-4:
            continue
        floor_ring.append(col[0])
# front opening: ramp strip (Floor) C[-1] -> C[0]
a, b = C[-1], C[0]
cols = [ramp_profile(a, mitre(NC - 1)), ramp_profile(a.lerp(b, .5) + Vector((0, -.04, 0)),
        Vector((-(b - a).y, (b - a).x, 0)).normalized()), ramp_profile(b, mitre(0))]
strip(F, cols, (b - a).normalized())
floor_ring += [cols[0][0], cols[1][0], cols[2][0]]
# basin floor: concentric rings to a centre, nearly flat
c = Vector((0.1, 0.2, 0))
rings = [floor_ring]
for s, dz in ((.7, -.015), (.4, -.03), (.15, -.035)):
    rings.append([Vector((c.x + (p.x - c.x) * s + rnd.uniform(-.06, .06), c.y + (p.y - c.y) * s + rnd.uniform(-.06, .06),
                          FZ + dz + rnd.uniform(-.01, .01))) for p in floor_ring])
M = len(floor_ring)
for r in range(len(rings) - 1):
    for k in range(M):
        k2 = (k + 1) % M
        face_out(F, [rings[r][k], rings[r][k2], rings[r + 1][k2], rings[r + 1][k]], (0, 0, 1), uv0)
cz = Vector((c.x, c.y, FZ - .035))
for k in range(M):
    face_out(F, [rings[-1][k], rings[-1][(k + 1) % M], cz], (0, 0, 1), uv0)
# a few angular fallen clods at the outer foot
for (p, s, sd) in (((3.48, -0.45), .24, 1), ((-2.95, 1.72), .30, 2), ((1.25, 2.5), .2, 3)):
    ak.blob(E, (p[0], p[1], 0), (s * 1.3, s, s * .8), rings=2, sides=5, jitter=.2, seed=sd)
eo = E.to_object('AN33a_Earth'); ak.auto_uv(eo)
fo = F.to_object('AN33a_Floor'); ak.auto_uv(fo)
objs = [eo, fo]
ak.ground(objs)
check_normals(objs)
f = os.path.join(D, 'SM_AN33_QueenBed.fbx'); ak.export_fbx(objs, f)
rec = ak.variant_record('AN33a', 'Queen earth bed', objs, f)
print(rec)
write_manifest(OUT, 'AN33 queen-bed', [rec], solidity='walk-on-top (basin floor) + solid rim',
               collider='mesh collider (Earth + Floor); floor at z=0.25 is walkable, front lip ramp 15 deg', emissive=False,
               notes='Pivot bottom centre. Polygonal horseshoe rim (Earth) of 10 straight facets around a 3.4 m front '
               'opening: steep outer walls with one facet break, flat rim tops with hard edges, steep faceted inner walls '
               'creased on long edges. Rim height highest at the back (1.25 m), sloping per facet and stepping down at corners '
               '(right/left ~0.12 m steps, front 0.66/0.62) to blunt front stubs ~0.5 m; vertical step faces at the corners. '
               'Front lip ramp 0.95 m (Floor) up to the basin floor (Floor, z=0.25, almost flat). 3 fallen clods. '
               'Leaves/cloth/queen are separate.')
if REND:
    t, l = renders(objs, OUT, 'a', el=24, margin=0.86)
    card('33-queen-bed', OUT, t, l, 'AN33 queen bed (v2 polygonal)')
ak.save_blend(os.path.join(D, 'AN33_QueenBed.blend'))
