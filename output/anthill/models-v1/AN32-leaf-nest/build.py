import sys, os, math, random
sys.path.insert(0, '/home/claude/ant/models'); from lib_sup import *
from mathutils import Vector, Matrix
D = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(D, 'out'); os.makedirs(OUT, exist_ok=True)
REND = '--norender' not in sys.argv

ak.reset()
rnd = random.Random(32)
RX, RY = 0.98, 0.73
NS = 18
TOPZ, RIMZ = 0.07, 0.045
rj = [1 + rnd.uniform(-.04, .04) for k in range(NS)]
def ring(scale, z, jz=0.0, off=0.0):
    out = []
    for k in range(NS):
        a = 2 * math.pi * k / NS
        out.append(Vector((math.cos(a) * (RX * rj[k] * scale + off), math.sin(a) * (RY * rj[k] * scale + off),
                           z + rnd.uniform(-jz, jz))))
    return out
base = ring(1.0, 0.0, off=0.02)
rim = ring(1.0, RIMZ, .006)
top = ring(.95, TOPZ, .006)
mid = ring(.55, TOPZ - .012, .008)
cen = Vector((0.03, -0.02, TOPZ - .02))
e = ak.Mesh('Earth'); fl = ak.Mesh('Floor')
z0 = lambda pts, n: [(0, 0)] * len(pts)
for k in range(NS):
    k2 = (k + 1) % NS
    face_out(e, [base[k], base[k2], rim[k2], rim[k]], (base[k] + base[k2]) * .5, z0)
    face_out(e, [rim[k], rim[k2], top[k2], top[k]], (rim[k] + rim[k2]) * .5 + Vector((0, 0, .5)), z0)
    face_out(fl, [top[k], top[k2], mid[k2], mid[k]], (0, 0, 1), z0)
    face_out(fl, [mid[k], mid[k2], cen], (0, 0, 1), z0)
eo = e.to_object('AN32a_Earth'); ak.auto_uv(eo)
fo = fl.to_object('AN32a_Floor'); ak.auto_uv(fo)

# six broad leaves standing round the rim like open petals, overlapping
lv = ak.Mesh('Props')
specs = [  # (angle deg, L, W, tilt deg, radial scale, dz, seed)
    (8, 1.02, 0.44, 42, .80, .00, 1),
    (66, 0.96, 0.42, 36, .77, .02, 2),
    (122, 1.05, 0.46, 46, .80, .00, 3),
    (186, 0.98, 0.44, 40, .78, .015, 4),
    (243, 1.08, 0.45, 32, .80, .0, 5),
    (300, 1.00, 0.43, 38, .77, .02, 6),
]
for ang, L, W, tilt, rs, dz, sd in specs:
    a = math.radians(ang + rnd.uniform(-4, 4))
    rad = 1 / math.sqrt((math.cos(a) / RX) ** 2 + (math.sin(a) / RY) ** 2)
    c = Vector((math.cos(a) * rad * rs, math.sin(a) * rad * rs, 0))
    t = math.radians(tilt)
    z = TOPZ + W * .55 * math.sin(t) - 0.04 + dz
    curv = 0.5 / (rad * rs)
    bend = lambda p, s, f, curv=curv: Vector((0, -curv * p.x * p.x, 0))
    leaf(lv, place(c.x, c.y, z, a - math.pi / 2, rx=t, ry=math.radians(rnd.uniform(-5, 5))), L=L, W=W * 1.1, thick=0.095,
         belly=.5, seed=sd, droop=0.05, curl=-0.03, serr=.1, bend=bend, n=9)
lo = lv.to_object('AN32a_Props')
objs = [eo, fo, lo]
ak.ground(objs)
check_normals(objs)
f = os.path.join(D, 'SM_AN32_LeafNest.fbx'); ak.export_fbx(objs, f)
rec = ak.variant_record('AN32a', 'Leaf nest (clutch bedding)', objs, f)
print(rec)
write_manifest(OUT, 'AN32 leaf-nest', [rec], solidity='walk-on-top (low disc), leaves passable or low obstacle',
               collider='low cylinder/box for the disc (h 0.07); leaves no collider or a ring of 6 boxes', emissive=False,
               notes='Pivot bottom centre. Earth disc (Earth sides/chamfer, Floor top, slightly dished) + 6 separate broad solid '
               'leaves standing round the rim like petals, overlapping, bent to the ring. Centre left open for AN31 eggs '
               '(separate mesh). Veins painted (Props leaf_green).')
if REND:
    t, l = renders(objs, OUT, 'a', el=26)
    card('32-leaf-nest', OUT, t, l, 'AN32 leaf nest')
ak.save_blend(os.path.join(D, 'AN32_LeafNest.blend'))
