"""AN30 Hanging chain (Подвесная цепь): 6 big angular (octagonal, square-section) links turned alternately 90 deg,
plus an open half-link at each end for attaching. Rusty iron = Props `iron`.
PIVOT = top attach point (top of the upper open link, x=y=z=0); the chain hangs down along -Z, ~1.2 m.
Bottom attach point (lantern hook) is the open lower half-link at z ~ -1.2."""
import sys, os, math, random
sys.path.insert(0, '/home/claude/ant/models')
from an_woodlib import *
import ak

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
SHEET = '/home/claude/ant/concepts/environment-pack-v3/asset-concepts-v1/sheets/30-chain.png'
RENDER = '--norender' not in sys.argv

ak.reset()
p = ak.Mesh('Props')
rnd = random.Random(3)

TB = 0.036            # bar thickness (square section)
RB = TB / 2 * math.sqrt(2)
BC = 0.042            # centreline half width -> outer width 0.12
AC = 0.09            # centreline half length
PITCH = 2 * AC - TB
HALF = 0.115          # open half-link prong length (centreline, from its closed bar)

def octa(a, b, ca, cb):
    """centreline of an elongated octagon (local: x across, z along)."""
    return [(b, -a + ca), (b, a - ca), (b - cb, a), (-b + cb, a), (-b, a - ca), (-b, -a + ca), (-b + cb, -a), (b - cb, -a)]

def densify(pts, r, amp=0.004):
    out = []
    for i in range(len(pts)):
        a, b = pts[i], pts[(i + 1) % len(pts)]
        out.append(a)
        mx, mz = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2
        L = math.hypot(mx, mz) or 1
        k = r.uniform(-1, 1.4) * amp
        out.append((mx + mx / L * k, mz + mz / L * k))
    return out

def flakes(P, n, seed):
    r = random.Random(seed)
    cen = sum(P, Vector()) / len(P)
    for k in range(n):
        q = P[r.randrange(len(P))]
        o = (q - cen); o = o - Vector((0, 0, 0)) ; o.normalize()
        side = Vector((r.uniform(-1, 1), r.uniform(-1, 1), r.uniform(-1, 1))).normalized()
        c = q + (o * 0.6 + side * 0.8).normalized() * RB * 0.85
        s_ = r.uniform(0.008, 0.014)
        lump(p, c - Vector((0, 0, s_ * .3)), (s_ * 1.3, s_, s_ * .8), r.choice(('iron', 'dark', 'iron')), sides=5, rings=2,
             jitter=0.3, seed=seed * 10 + k, flat_bottom=False, rot=r.uniform(0, 6))

def place(pts2, c, plane, tilt, seed):
    r = random.Random(seed)
    ex = Vector((1, 0, 0)) if plane == 0 else Vector((0, 1, 0))
    ex = Matrix.Rotation(tilt, 3, 'Z') @ ex
    ez = Vector((0, 0, 1))
    R = Matrix.Rotation(r.uniform(-0.05, 0.05), 3, ex)  # small swing
    out = []
    for x, z in pts2:
        v = ex * x + ez * z
        out.append(Vector(c) + R @ v + Vector((r.uniform(-.003, .003), r.uniform(-.003, .003), r.uniform(-.003, .003))))
    nrm = R @ ex.cross(ez)
    return out, nrm

# top half-link (open upwards), its closed bar at z = -HALF
links = []
z_top_bar = -HALF
plane = 1
pts = [(BC * 0.8, 0.0), (BC, -HALF + 0.045), (BC - 0.03, -HALF), (-BC + 0.03, -HALF), (-BC, -HALF + 0.045), (-BC * 0.8, 0.0)]
pts = [(x, z - (-TB / 2)) if False else (x, z) for x, z in pts]
P, N = place([(x * 1.0, z - TB * 0.5) for x, z in pts], (0, 0, 0), plane, 0.0, 1)
rope(p, P, RB, up=N, sides=4, rot=math.pi / 4, region='iron', fit=True, caps=True)
links.append(('half_top', P))
# full links
zc = -HALF - TB * 0.5 + TB - AC  # first link: its top bar centreline TB above the half-link's bar
centres = []
for i in range(6):
    plane = i % 2
    c = (rnd.uniform(-.004, .004), rnd.uniform(-.004, .004), zc - i * PITCH)
    ca = AC * rnd.uniform(0.38, 0.46)
    cb = BC * rnd.uniform(0.45, 0.6)
    oc = densify(octa(AC * rnd.uniform(0.97, 1.03), BC * rnd.uniform(0.93, 1.07), ca, cb), rnd, amp=0.005)
    P, N = place(oc, c, plane, rnd.uniform(-0.08, 0.08), 10 + i)
    rope(p, P, RB * rnd.uniform(0.92, 1.06), up=N, sides=4, rot=math.pi / 4, region='iron', closed=True, fit=True, jitter=0.0015, seed=200 + i)
    flakes(P, 3, 300 + i)
    # forge-weld seam bump on one long side
    q = P[2]
    obox(p, q, N.cross(Vector((0, 0, 1))) if N.cross(Vector((0, 0, 1))).length > .1 else Vector((1, 0, 0)), (0, 0, 1), N,
         (TB * 1.25, 0.03, TB * 1.2), region='iron', jitter=0.002, seed=400 + i)
    centres.append(c)
# bottom: forged J-hook hanging from link 6 by an eye
bar6 = centres[-1][2] - AC
ER = 0.03
eye_c = Vector((0, 0, bar6 + TB - ER))
eye = [eye_c + Vector((math.cos(a) * ER, 0, math.sin(a) * ER)) for a in [2 * math.pi * k / 10 + math.pi / 2 for k in range(10)]]
rope(p, eye, RB * 0.9, up=(0, 1, 0), sides=4, rot=math.pi / 4, region='iron', closed=True, fit=True)
z0 = eye_c.z - ER
HR = 0.04
zh = z0 - 0.13
hook = [Vector((0, 0, z0 - 0.005)), Vector((0.002, 0, z0 - 0.06)), Vector((0.0, 0, zh + 0.01))]
for k in range(1, 8):
    a = math.pi + k * (math.pi * 1.15) / 7
    hook.append(Vector((HR + math.cos(a) * HR, 0, zh + math.sin(a) * HR)))
hook.append(hook[-1] + Vector((0.004, 0, 0.022)))
rad = [RB * 0.95] * (len(hook) - 3) + [RB * 0.7, RB * 0.45, RB * 0.15]
rope(p, hook, RB, up=(0, 1, 0), sides=4, rot=math.pi / 4, region='iron', fit=True, radii=rad)
flakes(hook, 3, 390)
# top: round iron ring through the upper open link (hanging point)
bar_top = -HALF - TB * 0.5
RR = 0.055
ring_c = Vector((0, 0, bar_top + TB + RR - TB / 2))
ringp = [ring_c + Vector((math.cos(a) * RR * 1.05, 0, math.sin(a) * RR)) for a in [2 * math.pi * k / 12 for k in range(12)]]
rope(p, ringp, RB * 0.85, up=(0, 1, 0), sides=6, region='iron', closed=True, fit=True)
flakes(ringp, 2, 395)

po = p.to_object('AN30a_Props')
objs = [po]
lo, hi = ak.bounds(objs)
# pivot: top attach point at the origin
po.data.transform(Matrix.Translation(Vector((-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, -hi[2]))))
hook_bowl = zh - HR + RB - hi[2]
lo, hi = ak.bounds(objs)
fbx = os.path.join(HERE, 'SM_AN30_Chain.fbx')
ak.export_fbx(objs, fbx)
ak.save_blend(os.path.join(HERE, 'AN30_Chain.blend'))
rec = ak.variant_record('AN30a', 'Chain', objs, fbx,
                        solidity='passable (decor)', collider='none (or a thin capsule if it must stop the rope/cards)',
                        attach={'pivot': 'top attach point = top of the iron ring (0,0,0); hangs along -Z',
                                'bottom_attach_z': round(float(hook_bowl), 3), 'bottom_attach': 'inside of the J-hook bowl (lantern bail hangs here)',
                                'repeat': 'stack copies: next chain ring hangs in the hook (pivot at bottom_attach_z)'},
                        emissive=False,
                        notes='6 uneven octagonal links (square section, alternating 90 deg, swing/jitter, bulged sides, forge-weld bumps), rust flakes, '
                              'open upper half-link with a round hanging ring, forged J-hook on an eye at the bottom; Props `iron`/`dark`.')
save_manifest(os.path.join(OUT, 'manifest.json'), {'asset': 'AN30 chain', 'variants': [rec]})
print('TRIS', rec['tris'], 'SIZE', rec['size_xyz_m'], 'Z', lo[2], hi[2])
if RENDER:
    render_set(objs, OUT, SHEET, 'AN30 Chain  %d tris  %s m' % (rec['tris'], rec['size_xyz_m']), el=12,
               extra=[(objs, 'closeup', dict(az=-30, el=15, target=(0, 0, -0.55), dist=1.1)),
                      (objs, 'top', dict(az=-40, el=15, target=(0, 0, -0.15), dist=0.75)),
                      (objs, 'hook', dict(az=-40, el=10, target=(0.02, 0, -1.08), dist=0.75))])
