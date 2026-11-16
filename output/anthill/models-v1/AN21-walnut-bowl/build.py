import sys, os, math, random
sys.path.insert(0, '/home/claude/ant/models'); from lib_sup import *
from mathutils import Vector, Matrix
D = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(D, 'out'); os.makedirs(OUT, exist_ok=True)
REND = '--norender' not in sys.argv

ak.reset()
rnd = random.Random(21)
N = 32                     # columns around
RX, RY, H0 = 0.75, 0.55, 0.62
TH = 0.08                  # shell thickness
A_MAX = 0.56 * math.pi     # meridian goes a bit past the equator -> rim curls in slightly
TS = [0.0, .18, .31, .43, .54, .64, .73, .81, .88, .94, .975, 1.0]

def ang(k):
    return 2 * math.pi * k / N

def notch(th, c, w, d):
    x = math.atan2(math.sin(th - c), math.cos(th - c)) / w
    return d * max(0.0, 1 - abs(x)) ** 1.3

# rim height per column: wavy broken rim with notches, chips and V cracks
CRACKS = {5: .12, 17: .1, 27: .13}
CHIPS = {2: .035, 9: .05, 12: .03, 21: .04, 24: .035, 30: .05}
HC = []
for k in range(N):
    th = ang(k)
    h = H0 + 0.035 * math.sin(2 * th + .7) + 0.025 * math.sin(5 * th + 2.0) + rnd.uniform(-.02, .02)
    h -= notch(th, math.radians(-55), .45, .14)    # deep front-right notch
    h -= notch(th, math.radians(140), .4, .11)     # back-left notch
    h -= notch(th, math.radians(35), .3, .06)
    h -= notch(th, math.radians(-150), .25, .05)
    if k in CHIPS:
        h -= CHIPS[k]                              # chips knocked out of the rim
    if k in CRACKS:
        h -= CRACKS[k]                             # narrow deep V cracks
    HC.append(h)

GJ = [[rnd.uniform(-.022, .022) for k in range(N)] for t in TS]
def groove(k, i, t):
    th = ang(k)
    return 1 + .045 * math.sin(4 * th + 3.2 * t + .4) + .025 * math.sin(7 * th - 4 * t) + GJ[i][k]

def zprof(t):
    a = t * A_MAX
    return (1 - math.cos(a)) / (1 - math.cos(A_MAX))

def outer(k, i):
    t = TS[i]; th = ang(k); a = t * A_MAX
    g = groove(k, i, t) if i > 0 else 1
    if k in CRACKS and t > .55:
        g -= .07 * (t - .55) / .45                 # crack runs down the outside as a groove
    r = math.sin(a)
    z = zprof(t) * HC[k]
    if i == 1:
        z = 0.012                                  # flat little base
    lean = 0.04 * t                                # whole shell leans a touch to -x/+y
    return Vector((math.cos(th) * RX * r * g - lean, math.sin(th) * RY * r * g + lean * .5, z))

def inner(k, i):
    t = TS[i]; th = ang(k)
    if i == len(TS) - 1:
        o = outer(k, i)
        d = Vector((o.x / RX ** 2, o.y / RY ** 2, 0)).normalized()
        return Vector((o.x - d.x * TH, o.y - d.y * TH, HC[k] - 0.025))
    a = t * A_MAX
    r = math.sin(a)
    j = 1 + GJ[i][(k + 5) % N] * .5
    z = TH + zprof(t) * (HC[k] - 0.025 - TH)
    lean = 0.04 * t
    return Vector((math.cos(th) * (RX - TH) * r * j - lean, math.sin(th) * (RY - TH) * r * j + lean * .5, z))

O = [[outer(k, i) for k in range(N)] for i in range(len(TS))]
I = [[inner(k, i) for k in range(N)] for i in range(len(TS))]
m = ak.Mesh('Props')
uo = lambda i, k: R('shell_out', k / N * 2, TS[i])
ui = lambda i, k: R('shell_in', k / N * 2, TS[i])
# outer skin (rings 1..end), bottom disc
skin(m, O[1:], lambda i, k: uo(i + 1, k))
cO = O[0][0]
for k in range(N):
    k2 = (k + 1) % N
    m.tri(O[1][k2], O[1][k], cO, R('shell_out', .5 + .3 * math.cos(ang(k2)), .5 + .3 * math.sin(ang(k2)), 0),
          R('shell_out', .5 + .3 * math.cos(ang(k)), .5 + .3 * math.sin(ang(k)), 0), R('shell_out', .5, .5, 0))
# inner skin (flipped), inner bottom fan
skin(m, I[1:], lambda i, k: ui(i + 1, k), flip=True)
cI = Vector((-.0, 0, TH))
for k in range(N):
    k2 = (k + 1) % N
    m.tri(I[1][k], I[1][k2], cI, R('shell_in', .5 + .3 * math.cos(ang(k)), .5 + .3 * math.sin(ang(k)), 0),
          R('shell_in', .5 + .3 * math.cos(ang(k2)), .5 + .3 * math.sin(ang(k2)), 0), R('shell_in', .5, .5, 0))
# rim top (broken edge): outer rim -> inner rim
o, n = O[-1], I[-1]
for k in range(N):
    k2 = (k + 1) % N
    m.quad(o[k], o[k2], n[k2], n[k], R('shell_in', k / N * 2, .02), R('shell_in', (k + 1) / N * 2, .02),
           R('shell_in', (k + 1) / N * 2, .12), R('shell_in', k / N * 2, .12))
# ---- details: seeds and husk flakes inside, crumbs, broken shell chips lying around
for i, (x, y, rz, L) in enumerate(((-0.13, 0.05, .4, .24), (0.11, -0.07, 2.2, .22), (0.02, 0.17, 1.2, .2))):
    Ms = Matrix.Translation((x, y, TH + 0.05)) @ Matrix.Rotation(rz, 4, 'Z') @ Matrix.Rotation(.15 * (i - 1), 4, 'X')
    seed_body(m, Ms, L=L, H=L * .16, Wd=L * .22, sides=8, nring=6, seed=60 + i, chips=2)
for i in range(4):
    a = rnd.uniform(0, 6.3)
    shard(m, (math.cos(a) * .2, math.sin(a) * .15, TH + .012), rnd.uniform(.03, .05), 'seed', 'shell_in', seed=70 + i, curv=.4, th=.005)
scatter(m, 14, ring_sampler(0, 0, 0.0, 0.3, z=TH + .005, sy=.7), (.008, .018), 'seed', seed=8, sides=4)
# chips broken from the rim lying beside the bowl + crumbs of shell on the ground
for i, (x, y, sz) in enumerate(((0.92, -0.35, .11), (-0.62, -0.62, .09), (0.55, 0.62, .08), (-0.95, 0.2, .07))):
    shard(m, (x, y, 0.02), sz, 'shell_out', 'shell_in', seed=80 + i, curv=.55, th=.03,
          M=Matrix.Rotation(rnd.uniform(-.4, .4), 3, 'X') @ Matrix.Rotation(rnd.uniform(0, 6), 3, 'Z'))
scatter(m, 26, ring_sampler(0, 0, 0.62, 1.05, sy=.72), (.012, .03), 'shell_out', seed=9, sides=4)
scatter(m, 10, ring_sampler(0, 0, 0.55, 0.9, sy=.72), (.01, .02), 'seed', seed=10, sides=4)
ob = m.to_object('AN21a_Props')
ak.ground([ob], center_xy=False)
check_normals([ob])
f = os.path.join(D, 'SM_AN21_WalnutBowl.fbx'); ak.export_fbx([ob], f)
rec = ak.variant_record('AN21a', 'Walnut shell bowl', [ob], f)
print(rec)
write_manifest(OUT, 'AN21 walnut-bowl', [rec], solidity='solid (container, open top)',
               collider='mesh collider or 6-8 boxes around the rim + floor; convex if no items go inside',
               emissive=False, notes='Pivot bottom centre. Two-sided shell 8 cm thick: shell_out outside, shell_in inside, '
               'wavy rim with notches, 6 chips and 3 deep V cracks (cracks continue as grooves down the outside), diagonal '
               'faceted ridges; 3 small seeds + husk flakes + crumbs inside; 4 broken shell chips and ~36 crumbs on the ground.')
if REND:
    t, l = renders([ob], OUT, "a", el=30, margin=.9)
    card('21-walnut-bowl', OUT, t, l, 'AN21 walnut bowl (v2 detail)')
ak.save_blend(os.path.join(D, 'AN21_WalnutBowl.blend'))
