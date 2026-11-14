import sys, math, random, json; sys.path.insert(0, '/home/claude/brk/work')
import bpy, bmesh, numpy as np
from mathutils import Vector, Matrix
import bk
from bk import PROPS as P, sub
bpy.ops.wm.read_factory_settings(use_empty=True)
rnd = random.Random(17)
M = 'M_Forest_Props'
b = bk.B([M])
S = 0.45          # half size: crate is 0.9 m like the stub
planks = [sub(P['planksV'], i / 4, (i + 1) / 4 - 0.02) for i in range(4)]
def J(a=0.004): return rnd.uniform(-a, a)

# corner posts (dark frame wood, grain along the post)
for i, (x, y) in enumerate([(-1, -1), (1, -1), (1, 1), (-1, 1)]):
    b.box((x * (S - 0.036), y * (S - 0.036), 0.45 + J(0.006)), (0.072, 0.072, 0.9 + J(0.01)), rot=(J(1), J(1), J(3)),
          grain=2, side=P['plankH'], grain_u=True, jitter=0.004, seed=10 + i)

# walls: 3 vertical planks per side, a little inset from the posts, gaps between
def wall(axis, sign, seed):
    xs = [-0.375 + 0.75 * (k + 0.5) / 3 for k in range(3)]
    for k, xc in enumerate(xs):
        w = 0.75 / 3 - 0.008 + J(0.004)
        h = 0.86 + J(0.012)
        zc = 0.45 + J(0.006)
        rect = planks[(seed + k) % 4]
        if axis == 'y':
            c = (xc + J(0.003), sign * (S - 0.03), zc); size = (w, 0.026, h); rot = (J(1.2), J(0.8), 0)
        else:
            c = (sign * (S - 0.03), xc + J(0.003), zc); size = (0.026, w, h); rot = (J(0.8), J(1.2), 0)
        b.box(c, size, rot=rot, grain=2, side=rect, jitter=0.004, seed=seed * 10 + k)
    # horizontal battens top and bottom (frame wood), proud of the planks
    for zc in (0.075 + J(0.01), 0.825 + J(0.01)):
        L = 0.76 + J(0.01)
        if axis == 'y':
            c = (J(0.006), sign * (S - 0.004), zc); size = (L, 0.024, 0.088 + J(0.008)); g = 0; rot = (0, J(1.5), 0)
        else:
            c = (sign * (S - 0.004), J(0.006), zc); size = (0.024, L, 0.088 + J(0.008)); g = 1; rot = (J(1.5), 0, 0)
        b.box(c, size, rot=rot, grain=g, side=P['plankH'], grain_u=True, jitter=0.004, seed=seed * 10 + int(zc * 100))
wall('y', -1, 1); wall('y', 1, 2); wall('x', -1, 3); wall('x', 1, 4)
# diagonal brace on the right side (like the forest crate)
L = math.hypot(0.70, 0.60)
b.box((S + 0.016, 0.0, 0.45), (0.022, L, 0.08), rot=(math.degrees(math.atan2(0.60, 0.70)) + J(1), 0, 0), grain=1,
      side=P['plankH'], grain_u=True, jitter=0.004, seed=77)

# lid: 4 planks running front-back, one a bit lifted and skewed; two cross battens
for k in range(4):
    xc = -0.4 + 0.8 * (k + 0.5) / 4
    lift = 0.012 if k == 2 else 0.0
    b.box((xc + J(0.004), J(0.005), 0.885 + lift), (0.8 / 4 - 0.008, 0.88 + J(0.01), 0.026),
          rot=(J(1) + (1.8 if k == 2 else 0), J(1), J(2.5)), grain=1, side=planks[(k + 1) % 4], jitter=0.004, seed=40 + k)
# bottom: 4 planks
for k in range(4):
    xc = -0.4 + 0.8 * (k + 0.5) / 4
    b.box((xc, J(0.004), 0.015), (0.8 / 4 - 0.01, 0.86, 0.024), rot=(J(0.6), J(0.6), J(1.5)), grain=1,
          side=planks[k % 4], jitter=0.003, seed=50 + k)

# iron bands at x = +-0.3 wrapping front, top, back, bottom (inner loop follows battens/planks)
iron = P['darkpatch']
def band(x0, seed):
    r = random.Random(seed)
    yb, yp, t, w = S + 0.012, S - 0.012, 0.007, 0.055
    loop = [(-yb, -0.004), (-yb, 0.122), (-yp, 0.14), (-yp, 0.76), (-yb, 0.778), (-yb, 0.902),
            (yb, 0.902), (yb, 0.778), (yp, 0.76), (yp, 0.14), (yb, 0.122), (yb, -0.004)]
    loop = [(y + r.uniform(-.002, .002), z + r.uniform(-.002, .002)) for y, z in loop]
    n = len(loop)
    cen = Vector((0, 0.45))
    def outward(i):
        p = Vector(loop[i]); q0 = Vector(loop[i - 1]); q1 = Vector(loop[(i + 1) % n])
        d0 = (p - q0).normalized(); d1 = (q1 - p).normalized()
        n0 = Vector((d0.y, -d0.x)); n1 = Vector((d1.y, -d1.x))
        nn = (n0 + n1).normalized()
        if nn.dot(p - cen) < 0: nn = -nn
        return p + nn * t / max(0.5, nn.dot(n0 if n0.dot(p - cen) > 0 else -n0))
    out = [outward(i) for i in range(n)]
    per = sum((Vector(loop[(i + 1) % n]) - Vector(loop[i])).length for i in range(n))
    acc = 0.0
    xw = [x0 - w / 2 + r.uniform(-.003, .003), x0 + w / 2 + r.uniform(-.003, .003)]
    for i in range(n):
        j = (i + 1) % n
        seg = (Vector(loop[j]) - Vector(loop[i])).length
        u0, u1 = (acc / per) * 3, ((acc + seg) / per) * 3
        acc += seg
        fu = lambda u: u % 1.0
        a, c = Vector(loop[i]), Vector(loop[j]); ao, co = out[i], out[j]
        def V(x, p): return (x, p.x, p.y)
        uvA = lambda fu_, fv_: bk.uvr(M, iron, fu_, fv_)
        # outer surface, inner surface, two edges
        f0, f1 = (u0 % 1.0), (u0 % 1.0) + (u1 - u0)
        f1 = min(f1, 1.0)
        b.face([V(xw[0], ao), V(xw[1], ao), V(xw[1], co), V(xw[0], co)], [uvA(0, f0), uvA(1, f0), uvA(1, f1), uvA(0, f1)])
        b.face([V(xw[0], c), V(xw[1], c), V(xw[1], a), V(xw[0], a)], [uvA(0, f1), uvA(1, f1), uvA(1, f0), uvA(0, f0)])
        b.face([V(xw[0], a), V(xw[0], ao), V(xw[0], co), V(xw[0], c)], [uvA(0, f0), uvA(.1, f0), uvA(.1, f1), uvA(0, f1)])
        b.face([V(xw[1], c), V(xw[1], co), V(xw[1], ao), V(xw[1], a)], [uvA(.9, f1), uvA(1, f1), uvA(1, f0), uvA(.9, f0)])
band(-0.3, 5); band(0.3, 6)

o = b.obj('PR17_Crate', center_origin=False)
# whole-crate crookedness: lean the top a little, twist slightly
for v in o.data.vertices:
    z = v.co.z / 0.9
    v.co.x += 0.014 * z * z
    v.co.y -= 0.008 * z
    a = math.radians(1.2) * z
    x, y = v.co.x, v.co.y
    v.co.x, v.co.y = x * math.cos(a) - y * math.sin(a), x * math.sin(a) + y * math.cos(a)
# keep the stub's 0.9 m footprint, pivot at the bottom centre
V = np.array([v.co[:] for v in o.data.vertices]); lo, hi = V.min(0), V.max(0)
o.data.transform(Matrix.Scale(0.9 / float(max(hi - lo)), 4))
V = np.array([v.co[:] for v in o.data.vertices]); lo, hi = V.min(0), V.max(0)
o.data.transform(Matrix.Translation((-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, -lo[2])))
c = bk.box_col(o)
objs = [o, c]
print(json.dumps(bk.report(objs)))
OUT = '/home/claude/brk/out/crate/'
import os; os.makedirs(OUT, exist_ok=True)
bk.export_fbx(objs, OUT + 'SM_PR17_CrateBound.fbx')
tiles = []
for az, el in ((-30, 22), (35, 18), (160, 40), (90, 5)):
    tiles.append(bk.render([o], OUT + f'v_{az}.png', az=az, el=el, size=(480, 480), samples=12))
bk.sheet(tiles, OUT + 'card.jpg', cols=4, labels=['front-left', 'front-right', 'back-top', 'side'])
bk.save_blend(OUT + 'SM_PR17_CrateBound.blend')
