import sys, os, math, random
sys.path.insert(0, '/home/claude/ant/models'); from lib_sup import *
from mathutils import Vector, Matrix
D = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(D, 'out'); os.makedirs(OUT, exist_ok=True)
REND = '--norender' not in sys.argv

ak.reset()
HX, HY, HZ = 0.205, 0.155, 0.108
BUL = 0.022           # face puffing
ZR = [-1, -.6, -.2, .15, .55, 1]
XR = [-1, -.6, -.2, .2, .6, 1]
YR = [-1, -.5, 0, .5, 1]

def shape(b, extra=0.0):
    """box coords b in [-1,1]^3 -> world point (puffed, crooked). extra: push outward (cap shell)."""
    x, y, z = b
    p = Vector((x * HX, y * HY, z * HZ))
    # puff each face towards its normal, zero on the edges
    for a, (u, v) in ((0, (1, 2)), (1, (0, 2)), (2, (0, 1))):
        if abs(b[a]) > .999:
            amt = BUL * (1 - b[u] ** 2) * (1 - b[v] ** 2) * (0.6 if a == 2 and b[a] < 0 else 1.0)
            p[a] += math.copysign(amt + extra, b[a])
    # extra on edges/corners for the shell: push along each saturated axis
    p += hjit((x, y, z), 0.008, 2)
    # crooked: top leans +x, slight sag in the middle of the top, taper
    t = (z + 1) / 2
    p.x += 0.012 * t; p.y -= 0.006 * t
    p.x *= 1 - .03 * t; p.y *= 1 - .04 * t
    if abs(z) > .999 and z > 0:
        p.z -= 0.012 * (1 - x * x) * (1 - y * y)
    return p + Vector((0, 0, HZ))

body = ak.Mesh('Props')
faces = []
def grid_face(axis, sign, R1, R2, fn, out):
    for i in range(len(R1) - 1):
        for j in range(len(R2) - 1):
            pts = []
            for (a, b) in ((R1[i], R2[j]), (R1[i + 1], R2[j]), (R1[i + 1], R2[j + 1]), (R1[i], R2[j + 1])):
                c = [0, 0, 0]; c[axis] = sign
                o = [k for k in range(3) if k != axis]
                c[o[0]] = a; c[o[1]] = b
                pts.append(fn(tuple(c)))
            out.append((pts, tuple(sign if k == axis else 0 for k in range(3))))

body_faces = []
for s in (-1, 1):
    grid_face(0, s, YR, ZR, shape, body_faces)
    grid_face(1, s, XR, ZR, shape, body_faces)
grid_face(2, -1, XR, YR, shape, body_faces)
grid_face(2, 1, XR, YR, shape, body_faces)
LO = Vector((-HX - .03, -HY - .03, 0)); SZ = Vector((2 * HX + .06, 2 * HY + .06, 2 * HZ + .03))
uvf = box_uv_fit('paper', LO, SZ)
for pts, d in body_faces:
    face_out(body, pts, d, uvf)

# --- cap/flap shell: top + upper part of the sides, offset outwards, wavy lower edge with a thickness strip
OFF = 0.013
def wav(c):
    x, y, z = c
    return 0.10 + 0.22 * math.sin(2.3 * x + 1.0) * (1 if abs(y) > .999 else .6) - 0.12 * (y < -.999) * (x < 0) + \
        hjit((x, y, 0), .06, 5).x * 3 + (.12 if (round(x * 5) + round(y * 5)) % 2 else -.05) * (z < -.99)
def capz(c):
    x, y, z = c
    if z <= -.99:          # bottom row -> wavy edge height
        return (x, y, wav(c))
    zz = wav(c) + (z + 1) / 2 * (1 - wav(c)) if z < .999 else 1
    return (x, y, zz)
def cap_shape(c, off=OFF):
    q = capz(c)
    p = shape(q)
    n = Vector((q[0] if abs(q[0]) > .999 else 0, q[1] if abs(q[1]) > .999 else 0, 1 if q[2] > .999 else 0))
    return p + n.normalized() * off if n.length else p
CZ = [-1, -.4, .3, 1]
cap_faces = []
for s in (-1, 1):
    grid_face(0, s, YR, CZ, cap_shape, cap_faces)
    grid_face(1, s, XR, CZ, cap_shape, cap_faces)
grid_face(2, 1, XR, YR, cap_shape, cap_faces)
for pts, d in cap_faces:
    face_out(body, pts, d, uvf)
# lower edge thickness strip (faces down)
ring = []
for x in XR[:-1]: ring.append((x, -1, -1))
for y in YR[:-1]: ring.append((1, y, -1))
for x in reversed(XR[1:]): ring.append((x, 1, -1))
for y in reversed(YR[1:]): ring.append((-1, y, -1))
for i in range(len(ring)):
    a, b = ring[i], ring[(i + 1) % len(ring)]
    A1, B1 = cap_shape(a), cap_shape(b); A0, B0 = cap_shape(a, 0.0), cap_shape(b, 0.0)
    face_out(body, [A0, B0, B1, A1], (0, 0, -1), uvf)
# back diagonal fold: a triangular flap lying on the back face (+Y)
fy = HY + BUL * .55 + 0.012
tri = [Vector((-0.17, fy, 0.03)), Vector((0.16, fy + .004, 0.025)), Vector((0.12, fy + .012, 0.155))]
tb = [p - Vector((0, 0.012, 0)) for p in tri]
face_out(body, tri, (0, 1, 0), uvf)
for i in range(3):
    a, b = i, (i + 1) % 3
    face_out(body, [tri[a], tri[b], tb[b], tb[a]], (tri[a] + tri[b]) / 2 - sum(tri, Vector()) / 3 + Vector((0, .01, 0)), uvf)
# side fold (-X face): diagonal crease plate like the concept's left side
fx = -HX - BUL * .5 - 0.012
tri2 = [Vector((fx, -0.13, 0.02)), Vector((fx - .004, 0.12, 0.03)), Vector((fx - .01, 0.05, 0.13))]
tb2 = [p + Vector((0.012, 0, 0)) for p in tri2]
face_out(body, tri2, (-1, 0, 0), uvf)
for i in range(3):
    a, b = i, (i + 1) % 3
    face_out(body, [tri2[a], tri2[b], tb2[b], tb2[a]], (tri2[a] + tri2[b]) / 2 - sum(tri2, Vector()) / 3 + Vector((-.01, 0, 0)), uvf)

# --- rope: two bands crossing on top, knot at the crossing (offset to +x), loose ends
RR = 0.016
TOP = 2 * HZ + OFF + 0.01
def rope_pt(c):
    x, y, z = c
    n = Vector((x if abs(x) > .999 else 0, y if abs(y) > .999 else 0, z if abs(z) > .999 else 0)).normalized()
    covered = z > .999 or z > wav(c) + .05
    return shape(c) + n * (RR * .8 + (OFF if covered else 0.0))
def loop_yz(x0):
    seq = [(-1, 1), (-.5, 1), (0, 1), (.5, 1), (1, 1), (1, .5), (1, 0), (1, -.5), (1, -1), (.5, -1), (0, -1),
           (-.5, -1), (-1, -1), (-1, -.5), (-1, 0), (-1, .5)]
    return [rope_pt((x0, y, z)) for y, z in seq]
def loop_xz(y0):
    seq = [(-1, 1), (-.5, 1), (0, 1), (.5, 1), (1, 1), (1, .5), (1, 0), (1, -.5), (1, -1), (.5, -1), (0, -1),
           (-.5, -1), (-1, -1), (-1, -.5), (-1, 0), (-1, .5)]
    return [rope_pt((x, y0, z)) + Vector((0, 0, .004 if z > .99 else 0)) for x, z in seq]
def round_loop(pts, k=0.15):
    """cut corners: replace each point by two points towards its neighbours (Chaikin-ish once)."""
    out = []
    n = len(pts)
    for i in range(n):
        a, b = Vector(pts[i]), Vector(pts[(i + 1) % n])
        out.append(a.lerp(b, k)); out.append(a.lerp(b, 1 - k))
    return out
XB = 0.42; YB = 0.2
closed_loop_tube(body, round_loop(loop_yz(XB)), RR, 'rope', sides=5, metres_u=.25, seed=1)
closed_loop_tube(body, round_loop(loop_xz(YB)), RR, 'rope', sides=5, metres_u=.25, seed=2)
kc = shape((XB, YB, 1)) + Vector((0, 0, OFF + RR * 2))
rope_knot(body, kc, r=0.017, size=0.033, seed=4, axis=(0, 0, 1),
          ends=[[kc + Vector((.01, -.01, .01)), kc + Vector((.05, -.05, .0)), kc + Vector((.08, -.10, -.012))],
                [kc + Vector((-.01, -.012, .01)), kc + Vector((-.025, -.07, -.006)), kc + Vector((-.03, -.11, -.016))]])
# ---- details v2: second thinner strands, patches with stitches, torn flap corner, frayed ends, twig, leaf, crumbs
rnd = random.Random(26)
closed_loop_tube(body, round_loop(loop_yz(XB + .14)), RR * .7, 'rope', sides=4, metres_u=.25, seed=5)
closed_loop_tube(body, round_loop(loop_xz(YB - .18)), RR * .7, 'rope', sides=4, metres_u=.25, seed=6)
def patch(axis, sign, cu, cv, hu, hv, region, off, seed):
    o = [k for k in range(3) if k != axis]
    def bc(u, v):
        c = [0, 0, 0]; c[axis] = sign; c[o[0]] = u; c[o[1]] = v; return tuple(c)
    nrm = Vector([sign if k == axis else 0 for k in range(3)])
    r2 = random.Random(seed)
    corners = [(cu - hu, cv - hv), (cu + hu, cv - hv), (cu + hu, cv + hv), (cu - hu, cv + hv)]
    top = [shape(bc(u + r2.uniform(-.05, .05), v + r2.uniform(-.05, .05))) + nrm * (off + .006) for u, v in corners]
    bot = [p - nrm * .006 for p in top]
    face_out(body, top, nrm, lambda pts, n: [R(region, a, b, 0) for a, b in ((.1, .1), (.9, .1), (.9, .9), (.1, .9))])
    for i in range(4):
        j = (i + 1) % 4
        face_out(body, [bot[i], bot[j], top[j], top[i]], (top[i] + top[j]) / 2 - sum(top, Vector()) / 4,
                 lambda pts, n: [R(region, .1, .1, 0)] * 4)
        # two stitches crossing each edge
        for f in (.3, .7):
            pm = top[i].lerp(top[j], f)
            inward = (sum(top, Vector()) / 4 - pm).normalized()
            atlas_tube(body, [pm - inward * .012 + nrm * .002, pm + nrm * .006, pm + inward * .012 + nrm * .002],
                       .0035, 'rope', sides=3, metres_u=.1, caps=(False, False))
patch(1, -1, -.45, -.45, .28, .3, 'cloth', 0.0, 1)
patch(0, 1, .25, -.35, .3, .28, 'paper', 0.0, 2)
patch(2, 1, -.55, -.35, .25, .3, 'cloth', OFF, 3)
patch(1, 1, .35, -.5, .25, .25, 'shell_in', 0.0, 4)
# torn flap corner lifted on the top-left front
tc = shape((-1, -1, 1)) + Vector((0, 0, OFF))
tri3 = [tc + Vector((.0, .0, .004)), tc + Vector((.11, .01, .03)), tc + Vector((.02, .09, .035))]
face_out(body, tri3, (0, 0, 1), uvf); face_out(body, [p - Vector((0, 0, .006)) for p in tri3], (0, 0, -1), uvf)
# frayed fibres on the loose ends and at the knot
fibres(body, kc + Vector((.08, -.10, -.012)), (1, -1, -.3), n=4, length=.04, r=.004, seed=7)
fibres(body, kc + Vector((-.03, -.11, -.016)), (-.2, -1, -.4), n=3, length=.04, r=.004, seed=8)
fibres(body, kc + Vector((0, 0, .03)), (0, .3, 1), n=3, length=.03, r=.003, seed=9)
# a twig and a dry leaf tucked under the rope on top
twig(body, shape((XB - .3, -.8, 1)) + Vector((0, 0, OFF + .01)), (-.4, 1, -.08), .3, r=.008, seed=10, branches=1, segs=3, droop=.12)
small_leaf(body, shape((XB + .45, .3, 1)) + Vector((0, 0, OFF + .01)), .16, .07, 'leaf_dry_1', seed=11, rz=1.2, droop=.015)
# crumbs and fibres on the ground
scatter(body, 16, ring_sampler(0, 0, .26, .36, sy=.8), (.006, .014), 'paper', seed=12, sides=4)
scatter(body, 8, ring_sampler(0, 0, .25, .34, sy=.8), (.006, .012), 'earth_props', seed=13, sides=4)
ob = body.to_object('AN26a_Props')
ak.ground([ob], center_xy=False)
check_normals([ob])
f = os.path.join(D, 'SM_AN26_Parcel.fbx'); ak.export_fbx([ob], f)
rec = ak.variant_record('AN26a', 'Tied parcel', [ob], f)
print(rec)
write_manifest(OUT, 'AN26 parcel', [rec], solidity='solid (pickup / interest item)', collider='box', emissive=False,
               notes='Pivot bottom centre. Puffed crooked wrapped body, flap shell with torn toothed overhanging edge and a '
               'lifted torn corner, folded paper triangles on back and left side, two rope bands crossing on top + two '
               'thinner extra strands, knot with two loose ends and frayed fibres, 4 sewn-on patches (cloth/paper/shell) '
               'with stitches, a twig and a dry leaf tucked under the rope, crumbs on the ground. Paper creases painted.')
if REND:
    t, l = renders([ob], OUT, "a", el=26, margin=.95)
    card('26-parcel', OUT, t, l, 'AN26 parcel (v2 detail)')
ak.save_blend(os.path.join(D, 'AN26_Parcel.blend'))
