import sys, os, math, random
sys.path.insert(0, '/home/claude/ant/models'); from lib_sup import *
from mathutils import Vector, Matrix
from mathutils.geometry import delaunay_2d_cdt, intersect_point_line
D = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(D, 'out'); os.makedirs(OUT, exist_ok=True)
REND = '--norender' not in sys.argv

ak.reset()
rnd = random.Random(35)
# outline seen from the front (x right, z up), CCW: top lobe (two bumps), three drips: left short, centre long, right medium
OL = [(-0.58, 0.55), (-0.60, 0.64), (-0.55, 0.72), (-0.45, 0.79), (-0.30, 0.81), (-0.16, 0.77), (-0.04, 0.71),
      (0.08, 0.715), (0.20, 0.75), (0.32, 0.77), (0.44, 0.74), (0.55, 0.67), (0.60, 0.57), (0.59, 0.47),
      (0.54, 0.40), (0.49, 0.33), (0.47, 0.26), (0.43, 0.215), (0.375, 0.215), (0.33, 0.25), (0.30, 0.31),
      (0.27, 0.37), (0.21, 0.385),
      (0.15, 0.35), (0.12, 0.26), (0.11, 0.17), (0.09, 0.08), (0.05, 0.02), (0.0, 0.0), (-0.06, 0.02), (-0.10, 0.08),
      (-0.12, 0.16), (-0.14, 0.24), (-0.17, 0.31), (-0.21, 0.355),
      (-0.26, 0.36), (-0.30, 0.33), (-0.33, 0.28), (-0.37, 0.25), (-0.42, 0.25), (-0.46, 0.285), (-0.50, 0.34),
      (-0.54, 0.42)]
OL = [Vector((x + rnd.uniform(-.008, .008), z + rnd.uniform(-.008, .008))) for x, z in OL]
OL.reverse()   # make CCW in (x, z)
area = sum(OL[i].x * OL[(i + 1) % len(OL)].y - OL[(i + 1) % len(OL)].x * OL[i].y for i in range(len(OL)))
if area < 0:
    OL.reverse()
n = len(OL)

def inside(p):
    c = False
    for i in range(n):
        a, b = OL[i], OL[(i + 1) % n]
        if (a.y > p.y) != (b.y > p.y):
            x = a.x + (p.y - a.y) / (b.y - a.y) * (b.x - a.x)
            if x > p.x:
                c = not c
    return c

def edist(p):
    best = 9
    for i in range(n):
        a, b = OL[i], OL[(i + 1) % n]
        ab = b - a
        t = max(0, min(1, (p - a).dot(ab) / ab.length_squared))
        best = min(best, (a + ab * t - p).length)
    return best

pts = list(OL)
S = 0.05
for ix in range(-13, 14):
    for iz in range(0, 18):
        p = Vector((ix * S + (iz % 2) * S * .5 + rnd.uniform(-.012, .012), iz * S + .03 + rnd.uniform(-.012, .012)))
        if inside(p) and edist(p) > S * .45:
            pts.append(p)
res = delaunay_2d_cdt(pts, [], [list(range(n))], 1, 1e-6)
V2, tris = res[0], res[2]
EDGE = 0.045
def depth(p):
    d = edist(p)
    y = EDGE + 0.105 * min(d / 0.14, 1) ** 0.6
    y += 0.012 * math.sin(p.x * 23 + p.y * 3) * min(d / .06, 1)      # flow ridges running down
    return y
def P3(p, front=True):
    if not front:
        return Vector((p.x, 0.0, p.y))
    return Vector((p.x, -depth(p), p.y))
x0, x1 = min(p.x for p in OL), max(p.x for p in OL)
z0, z1 = min(p.y for p in OL), max(p.y for p in OL)
def uvf(pts3, nrm):
    return [R('resin', (z1 - q.z) / (z1 - z0 + .01), (q.x - x0) / (x1 - x0 + .01), fold=False) for q in pts3]
m = ak.Mesh('Props')
for t in tris:
    q = [P3(V2[i]) for i in t]
    face_out(m, q, (0, -1, 0), uvf)
# flat back (outline only)
back = delaunay_2d_cdt(list(OL), [], [list(range(n))], 1, 1e-6)
for t in back[2]:
    q = [P3(back[0][i], False) for i in t]
    face_out(m, q, (0, 1, 0), uvf)
# side band: back outline -> front outline
for i in range(n):
    a, b = OL[i], OL[(i + 1) % n]
    q = [P3(a, False), P3(b, False), P3(b), P3(a)]
    e = b - a
    out = Vector((e.y, 0, -e.x))     # outward of a CCW (x,z) outline
    face_out(m, q, out, uvf)
# ---- details v2: trapped seeds / bug specks, thin drips on the wall, separate drops below
def surf(x, z):
    p = Vector((x, z))
    return P3(p) if inside(p) else None
def trapped(x, z, s, region, seed, sides=5):
    q = surf(x, z)
    if q is None:
        return None
    lump(m, q + Vector((0, s * .25, 0)), (s * 1.4, s * .7, s), region, sides=sides, seed=seed, jit=.15)
    return q
r2 = random.Random(77)
for i in range(9):                      # small trapped seeds
    while True:
        x, z = r2.uniform(-.5, .5), r2.uniform(.1, .75)
        if inside(Vector((x, z))) and edist(Vector((x, z))) > .05:
            break
    trapped(x, z, r2.uniform(.012, .022), 'seed', 500 + i)
for i in range(14):                     # dark specks (grit)
    while True:
        x, z = r2.uniform(-.55, .55), r2.uniform(.05, .78)
        if inside(Vector((x, z))) and edist(Vector((x, z))) > .03:
            break
    trapped(x, z, r2.uniform(.005, .01), 'dark', 600 + i, sides=4)
# two trapped little bugs: body + head lumps and 6 thin legs
for i, (bx, bz, ang) in enumerate(((-0.22, 0.6, .4), (0.3, 0.52, 2.1))):
    q = surf(bx, bz)
    d = Vector((math.cos(ang), 0, math.sin(ang)))
    lump(m, q + Vector((0, .006, 0)), (.03, .014, .018), 'dark', sides=6, seed=700 + i, jit=.1,
         M=Matrix.Rotation(-ang, 3, 'Y'))
    lump(m, q + d * .035 + Vector((0, .004, 0)), .011, 'dark', sides=5, seed=710 + i, jit=.1)
    side = Vector((-d.z, 0, d.x))
    for k in (-1, 0, 1):
        for sg in (-1, 1):
            a0 = q + d * (k * .012) + Vector((0, -.004, 0))
            a1 = a0 + side * sg * .022 + d * k * .01 + Vector((0, -.004, 0))
            a2 = a1 + side * sg * .012 + d * (k * .012 + .006) + Vector((0, .006, 0))
            atlas_tube(m, [a0, a1, a2], [.0025, .002, .0015], 'dark', sides=3, metres_u=.1, caps=(False, False))
# thin drips running down the wall from the lobe edge and drip tips (half buried in the wall)
for i, (x, ztop, L) in enumerate(((-0.53, .43, .16), (-0.40, .25, .11), (0.02, .0, .14), (0.40, .215, .2),
                                  (0.55, .41, .1), (0.21, .38, .12), (-0.24, .36, .09))):
    pts = [Vector((x, -0.011, ztop + .03)), Vector((x + r2.uniform(-.01, .01), -0.01, ztop - L * .5)),
           Vector((x + r2.uniform(-.015, .015), -0.008, ztop - L))]
    atlas_tube(m, pts, [.016, .011, .008], 'resin', sides=5, metres_u=.3, caps=(False, False))
    lump(m, pts[-1] + Vector((0, -.004, -.008)), (.014, .011, .018), 'resin', sides=5, seed=800 + i, jit=.1)
# separate drops on the wall below
for i, (x, z, s) in enumerate(((0.03, -0.1, .032), (-0.06, -0.19, .022), (0.41, 0.07, .026), (-0.39, 0.1, .02),
                               (0.08, -0.26, .014))):
    lump(m, (x, -s * .45, z), (s * .85, s * .6, s * 1.25), 'resin', sides=6, seed=900 + i, jit=.1)
ob = m.to_object('AN35a_Props')
# pivot: back plane (y = 0) at the centre of the patch in X and Z
ob.data.transform(Matrix.Translation((-(x0 + x1) / 2, 0, -(z0 + z1) / 2)))   # centre of the main patch outline
check_normals([ob])
f = os.path.join(D, 'SM_AN35_ResinPatch.fbx'); ak.export_fbx([ob], f)
rec = ak.variant_record('AN35a', 'Resin patch (wall)', [ob], f)
rec['attach'] = 'pivot on the flat back (y=0) at the centre of the patch; back faces +Y into the wall, front -Y'
print(rec)
write_manifest(OUT, 'AN35 resin-patch', [rec], solidity='surface (wall decal-relief)', collider='none (or thin box)',
               emissive=False, attach=rec['attach'],
               notes='Flat back, pillowy faceted front (max 15 cm, denser facets), wide lobe with two top bumps and three '
                     'drips (left short, centre long, right medium), flow ridges; 9 trapped seeds, 14 dark grit specks and 2 '
                     'trapped little bugs (body/head/6 legs) in the surface; 7 thin drips with bulb ends running down the wall; '
                     '5 separate drops below. Streaks painted (Props resin). No glow. Pivot = centre of the main patch.')
if REND:
    t, l = renders([ob], OUT, 'a', el=10)
    card('35-resin-patch', OUT, t, l, 'AN35 resin patch (v2 detail)')
ak.save_blend(os.path.join(D, 'AN35_ResinPatch.blend'))
