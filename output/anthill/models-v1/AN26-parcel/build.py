import sys, os, math, random
sys.path.insert(0, '/home/claude/ant/models'); from lib_sup import *
from mathutils import Vector, Matrix
D = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(D, 'out'); os.makedirs(OUT, exist_ok=True)
REND = '--norender' not in sys.argv

ak.reset()
HX, HY, HZ = 0.205, 0.155, 0.108
BUL = 0.022           # face puffing
ZR = [-1, -.45, .1, .55, 1]
XR = [-1, -.35, .35, 1]
YR = [-1, -.3, .3, 1]

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
    x, y, _ = c
    return 0.10 + 0.22 * math.sin(2.3 * x + 1.0) * (1 if abs(y) > .999 else .6) - 0.12 * (y < -.999) * (x < 0) + \
        hjit((x, y, 0), .06, 5).x * 3
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
CZ = [-1, 0, 1]
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
ob = body.to_object('AN26a_Props')
ak.ground([ob])
check_normals([ob])
f = os.path.join(D, 'SM_AN26_Parcel.fbx'); ak.export_fbx([ob], f)
rec = ak.variant_record('AN26a', 'Tied parcel', [ob], f)
print(rec)
write_manifest(OUT, 'AN26 parcel', [rec], solidity='solid (pickup / interest item)', collider='box', emissive=False,
               notes='Pivot bottom centre. Puffed crooked wrapped body, wrapping flap shell with wavy overhanging lower '
               'edge, folded paper triangles on back and left side, two rope bands crossing on top, knot with two '
               'loose ends modelled. Paper colour/creases painted (Props paper), rope (Props rope).')
if REND:
    t, l = renders([ob], OUT, 'a', el=24)
    card('26-parcel', OUT, t, l, 'AN26 parcel')
ak.save_blend(os.path.join(D, 'AN26_Parcel.blend'))
