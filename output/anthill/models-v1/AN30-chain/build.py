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
AC = 0.1015           # centreline half length
PITCH = 2 * AC - TB
HALF = 0.115          # open half-link prong length (centreline, from its closed bar)

def octa(a, b, ca, cb):
    """centreline of an elongated octagon (local: x across, z along)."""
    return [(b, -a + ca), (b, a - ca), (b - cb, a), (-b + cb, a), (-b, a - ca), (-b, -a + ca), (-b + cb, -a), (b - cb, -a)]

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
    P, N = place(octa(AC * rnd.uniform(0.98, 1.02), BC * rnd.uniform(0.95, 1.05), ca, cb), c, plane, rnd.uniform(-0.06, 0.06), 10 + i)
    rope(p, P, RB, up=N, sides=4, rot=math.pi / 4, region='iron', closed=True, fit=True)
    centres.append(c)
# bottom half-link (open downwards), closed bar inside link 6
zb = centres[-1][2] - AC + TB
plane = 0
pts = [(BC * 0.8, -HALF), (BC, -0.045), (BC - 0.03, 0.0), (-BC + 0.03, 0.0), (-BC, -0.045), (-BC * 0.8, -HALF)]
P, N = place(pts, (0, 0, zb), plane, 0.03, 30)
rope(p, P, RB, up=N, sides=4, rot=math.pi / 4, region='iron', fit=True, caps=True)

po = p.to_object('AN30a_Props')
objs = [po]
lo, hi = ak.bounds(objs)
# pivot: top attach point at the origin
po.data.transform(Matrix.Translation(Vector((-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, -hi[2]))))
lo, hi = ak.bounds(objs)
fbx = os.path.join(HERE, 'SM_AN30_Chain.fbx')
ak.export_fbx(objs, fbx)
ak.save_blend(os.path.join(HERE, 'AN30_Chain.blend'))
rec = ak.variant_record('AN30a', 'Chain', objs, fbx,
                        solidity='passable (decor)', collider='none (or a thin capsule if it must stop the rope/cards)',
                        attach={'pivot': 'top attach point = top of the upper open half-link (0,0,0); hangs along -Z',
                                'bottom_attach_z': round(float(lo[2]), 3),
                                'repeat': 'stack copies: next chain pivot at bottom_attach_z (+0.03 overlap)'},
                        emissive=False,
                        notes='6 full octagonal links (square section, alternating 90 deg, slight swing/jitter) + open half-links at both ends; '
                              'all real geometry, Props `iron` region (rust painted).')
save_manifest(os.path.join(OUT, 'manifest.json'), {'asset': 'AN30 chain', 'variants': [rec]})
print('TRIS', rec['tris'], 'SIZE', rec['size_xyz_m'], 'Z', lo[2], hi[2])
if RENDER:
    render_set(objs, OUT, SHEET, 'AN30 Chain  %d tris  %s m' % (rec['tris'], rec['size_xyz_m']), el=12,
               extra=[(objs, 'closeup', dict(az=-30, el=15, target=(0, 0, -0.35), dist=1.1))])
