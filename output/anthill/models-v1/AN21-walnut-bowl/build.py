import sys, os, math, random
sys.path.insert(0, '/home/claude/ant/models'); from lib_sup import *
from mathutils import Vector, Matrix
D = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(D, 'out'); os.makedirs(OUT, exist_ok=True)
REND = '--norender' not in sys.argv

ak.reset()
rnd = random.Random(21)
N = 24                     # columns around
RX, RY, H0 = 0.75, 0.55, 0.62
TH = 0.08                  # shell thickness
A_MAX = 0.56 * math.pi     # meridian goes a bit past the equator -> rim curls in slightly
TS = [0.0, .2, .36, .51, .64, .76, .87, .95, 1.0]

def ang(k):
    return 2 * math.pi * k / N

def notch(th, c, w, d):
    x = math.atan2(math.sin(th - c), math.cos(th - c)) / w
    return d * max(0.0, 1 - abs(x)) ** 1.3

# rim height per column: wavy broken rim with notches and chips
HC = []
for k in range(N):
    th = ang(k)
    h = H0 + 0.035 * math.sin(2 * th + .7) + 0.025 * math.sin(5 * th + 2.0) + rnd.uniform(-.02, .02)
    h -= notch(th, math.radians(-55), .45, .14)    # deep front-right notch
    h -= notch(th, math.radians(140), .4, .11)     # back-left notch
    h -= notch(th, math.radians(35), .3, .06)
    h -= notch(th, math.radians(-150), .25, .05)
    if k in (3, 13, 19):
        h -= .04                                   # small chips
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
ob = m.to_object('AN21a_Props')
ak.ground([ob])
check_normals([ob])
f = os.path.join(D, 'SM_AN21_WalnutBowl.fbx'); ak.export_fbx([ob], f)
rec = ak.variant_record('AN21a', 'Walnut shell bowl', [ob], f)
print(rec)
write_manifest(OUT, 'AN21 walnut-bowl', [rec], solidity='solid (container, open top)',
               collider='mesh collider or 6-8 boxes around the rim + floor; convex if no items go inside',
               emissive=False, notes='Pivot bottom centre. Two-sided shell 8 cm thick: shell_out outside, shell_in inside, '
               'wavy rim with notches/chips, diagonal faceted ridges on the outside modelled.')
if REND:
    t, l = renders([ob], OUT, 'a', el=24)
    card('21-walnut-bowl', OUT, t, l, 'AN21 walnut bowl')
ak.save_blend(os.path.join(D, 'AN21_WalnutBowl.blend'))
