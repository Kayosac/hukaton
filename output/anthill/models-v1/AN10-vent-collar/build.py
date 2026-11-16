import sys, os, random
sys.path.insert(0, '/home/claude/ant/models')
from shell_lib import *
from detail_lib import *

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
ak.reset()
# rim section (r = outward from the rim centre line, z), closed: inner face, top, outer face, bottom
SEC = [(-0.44, 0.14), (-0.5, 0.48), (-0.46, 0.85), (-0.3, 1.1), (-0.05, 1.2), (0.22, 1.12), (0.44, 0.92),
       (0.53, 0.55), (0.5, 0.18), (0.4, 0.0), (0.0, 0.0), (-0.32, 0.02)]


def centre(a):
    """Rim centre line: elongated bean (4 x 2.8 m outer), slight waist on one side."""
    x = 1.66 * math.cos(a)
    y = 0.98 * math.sin(a) * (1 - 0.16 * math.cos(a) ** 2 * (1 if math.sin(a) > 0 else 0.3))
    return Vector((x, y, 0))


def forbid(lo, hi):
    return abs(lo.x) > 2.12 or abs(hi.x) > 2.12 or abs(lo.y) > 1.5 or abs(hi.y) > 1.5 or lo.z < -1.6


def inward(p, n):
    return n.x * p.x / 1.66 ** 2 + n.y * p.y / 0.98 ** 2 < -0.05


INNER = lambda p, n: inward(p, n) and abs(n.z) < 0.75
OUTER = lambda p, n: not inward(p, n) and abs(n.z) < 0.75
TOP = lambda p, n: n.z > 0.6
UNDER = lambda p, n: n.z < -0.4
LIP = lambda p, n: inward(p, n) and p.z < 0.35


def build(vid, seed):
    rnd = random.Random(seed)
    s = Solid()
    N = 22
    hs = [rnd.uniform(-0.12, 0.12) for _ in range(N)]
    secs = []
    for i in range(N):
        a = 2 * math.pi * i / N + rnd.uniform(-0.04, 0.04)
        p = centre(a)
        t = (centre(a + 0.01) - centre(a - 0.01)).normalized()
        n = Vector((t.y, -t.x, 0))                      # outward (counter-clockwise loop)
        wid = 0.64 * rnd.uniform(0.88, 1.15)
        sec = []
        for k, (r, z) in enumerate(SEC):
            zz = z
            if z > 0.2:
                zz = z + hs[i] * (z / 1.2) + rnd.uniform(-0.05, 0.05)
            rr = r * wid + (rnd.uniform(-0.05, 0.05) if z > 0 else 0)
            sec.append(p + n * rr + Vector((0, 0, 0.82 * max(0.0, zz))))
        secs.append(sec)
    s.loft(secs, 'Earth', wrap=True)
    extra = [(40, hang_root, INNER, ('Earth',), lambda p, n: {'length': rnd.uniform(0.4, 1.3) + p.z}),
             (18, hang_root, UNDER, ('Earth',), lambda p, n: {'length': rnd.uniform(0.2, 0.6)}),
             (14, root_tip, INNER, ('Earth',), lambda p, n: {'length': rnd.uniform(0.3, 0.7)}),
             (26, clod, LIP, ('Earth',), lambda p, n: {'r': rnd.uniform(0.05, 0.12)}),
             (16, hang_drip, UNDER, ('Earth',), None),
             (40, pebble, OUTER, ('Earth',), None), (12, clod, OUTER, ('Earth',), None)]
    dress_shell(s, rnd, forbid, INNER, UNDER, TOP, None, None, k=0.75, extra=extra)
    return s, s.finish(vid)


s, objs = build('AN10a', 101)
stats('AN10a', objs, s)
fbx = os.path.join(OUT, 'SM_AN10_VentCollar.fbx')
ak.export_fbx(objs, fbx)
lo, hi = ak.bounds(objs)
JOINS = {
    'pivot': 'bottom centre (z = 0 = the flat underside, the face seen from the room below)',
    'placement': 'set into a ceiling / AN03 vault opening with the underside flush or 0.1-0.3 m proud of the ceiling surface; the slot (about 2.7 x 1.2 m clear, through) must line up with the hole in the ceiling',
    'clear_slot_m': 'approx 2.7 x 1.2 (bean shape, waist on +Y side)',
    'light': 'no beam/glass/plants modelled; light is a separate source in Unity',
}
recs = [ak.variant_record('AN10a', 'Vent collar (ceiling light slit rim)', objs, fbx, solidity='solid',
                          collider='mesh (ring)', joins=JOINS, emissive=False,
                          notes='thick uneven closed earth ring around an elongated through-slot: crooked top height, chamfered inner bottom edge, bulging outer face, flat underside. Details: hair rootlets and root tips dangling through the slot (down to ~1.5 m below the underside), crumbling clods along the inner lip, clay drips, pebbles, ledges, burrows, cracks, husks, needles on top.')]
write_manifest(OUT, recs)
ak.save_blend(os.path.join(HERE, 'AN10-vent-collar.blend'))
if wants_render():
    card = renders(objs, OUT, os.path.join(SHEETS, '10-vent-collar.png'), el=30,
                   extra=[('FROM BELOW (in ceiling)', objs, dict(az=-20, el=-55))], title='AN10 vent collar')
    print(card)
