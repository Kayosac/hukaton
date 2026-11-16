"""AN16 Deck bracket (Кронштейн настила): crooked wall post, horizontal beam pegged to its front face, diagonal
brace, square wooden pegs. Post at +X (wall side), beam runs out to -X. The beam top is a flat support at
z = 1.50 for the boardwalk SM_ST10_Boardwalk (its feet/runners sit on it); nothing rises above the beam top.
Pivot bottom centre, front -Y."""
import sys, os, math
sys.path.insert(0, '/home/claude/ant/models')
from an_woodlib import *
import ak

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
SHEET = '/home/claude/ant/concepts/environment-pack-v3/asset-concepts-v1/sheets/16-deck-bracket.png'
RENDER = '--norender' not in sys.argv
TOP = 1.50

ak.reset()
w = ak.Mesh('Wood')
p = ak.Mesh('Props')

# post (wall side): slightly crooked, bulge in the middle, chamfered top just under the beam top
post = [(0.0, 0.0, 0.0), (0.006, 0.0, 0.12), (0.012, 0.0, 0.42), (0.03, 0.01, 0.62), (-0.018, 0.01, 0.8), (0.008, -0.005, 1.12), (0.0, 0.0, 1.42)]
beam(w, post, [0.28, 0.255, 0.27, 0.285, 0.235, 0.255, 0.24], [0.25, 0.235, 0.235, 0.24, 0.225, 0.23, 0.22], up=(0, 1, 0),
     jitter=0.012, seed=21, caps=(None, 'bevel'), endm=p, bevel=0.3,
     cracks=[(3, 0.1, 0.55, 0.03), (0, 0.45, 0.8, 0.025), (2, 0.2, 0.6, 0.02), (1, 0.0, 0.25, 0.03)],
     chips=[(0, 0.22, 0.1, 0.035), (3, 0.7, 0.12, 0.03), (1, 0.5, 0.1, 0.03), (2, 0.4, 0.1, 0.03)])
# beam: in front of the post, top flat at TOP
bh, bd = 0.23, 0.18
by = -0.11 - bd / 2
beam(w, [(0.14, by, TOP - bh / 2), (-0.3, by - 0.006, TOP - bh / 2 - 0.004), (-0.85, by + 0.004, TOP - bh / 2 - 0.012), (-1.36, by + 0.006, TOP - bh / 2)],
     [bh, bh - 0.01, bh - 0.03, bh], [bd, bd + 0.01, bd, bd - 0.01], up=(0, 1, 0), jitter=0.0, seed=22, caps=('flat', 'flat'), endm=p,
     cracks=[(0, 0.35, 0.8, 0.03), (3, 0.6, 1.0, 0.03), (1, 0.15, 0.5, 0.025), (2, 0.5, 0.9, 0.02)],
     chips=[(1, 0.62, 0.12, 0.035), (0, 0.97, 0.06, 0.03), (2, 0.3, 0.1, 0.03)])
# diagonal brace (front plane), foot pegged to the post, head under the beam
ry = by + 0.01
beam(w, [(-0.02, ry, 0.30), (-0.3, ry - 0.006, 0.66), (-0.56, ry + 0.004, 1.0), (-0.86, ry, TOP - bh + 0.04)], [0.16, 0.15, 0.165, 0.15],
     [0.14, 0.145, 0.14, 0.14], up=(0, 1, 0), jitter=0.01, seed=23, caps=('flat', 'flat'),
     cracks=[(0, 0.2, 0.7, 0.025), (2, 0.0, 0.3, 0.02)], chips=[(1, 0.45, 0.1, 0.03), (3, 0.8, 0.08, 0.025)])
# wedge shim driven between the brace head and the beam
beam(w, [(-0.98, ry - 0.01, TOP - bh - 0.025), (-0.74, ry - 0.01, TOP - bh - 0.005)], [0.05, 0.012], [0.12, 0.11], up=(0, 1, 0),
     jitter=0.004, seed=24, caps=('flat', 'flat'))
# pegs: through the beam into the post, through the brace foot, at the brace head; through-ends on the back
fy = by - bd / 2
peg(w, (0.0, fy, TOP - bh / 2 + 0.01), (0, -1, 0), size=0.075, length=0.05, endm=p, seed=31)
peg(w, (-0.03, ry - 0.07, 0.38), (0, -1, 0), size=0.075, length=0.05, endm=p, seed=32)
peg(w, (-0.78, ry - 0.07, TOP - bh - 0.07), (0, -1, 0), size=0.07, length=0.05, endm=p, seed=33)
peg(w, (-0.70, fy, TOP - bh / 2 - 0.02), (0, -1, 0), size=0.07, length=0.045, endm=p, seed=34)
peg(w, (0.0, 0.115, TOP - bh / 2 + 0.01), (0, 1, 0), size=0.07, length=0.04, endm=p, seed=35)
peg(w, (-0.02, 0.115, 0.38), (0, 1, 0), size=0.07, length=0.04, endm=p, seed=36)


# ---------------- detail pass
# support cleat under the beam on the post's side, nailed
beam(w, [(-0.15, by + 0.005, TOP - bh - 0.16), (-0.15, by + 0.005, TOP - bh - 0.005)], 0.09, 0.15, up=(0, 1, 0), jitter=0.006,
     seed=40, caps=('flat', 'flat'), chips=[(0, 0.2, 0.05, 0.02)])
for i, z in enumerate((TOP - bh - 0.05, TOP - bh - 0.12)):
    nail(p, (-0.15 + (i - .5) * 0.03, by - 0.075, z), (0, -1, 0), size=0.018, out=0.006, seed=41 + i, region='dark')
# iron angle plate over the beam/post joint on the front, with nails
obox(p, (0.03, fy - 0.005, TOP - bh / 2 - 0.02), (1, 0, 0), (0, 0, 1), (0, -1, 0), (0.13, 0.17, 0.01), region='iron', jitter=0.002)
obox(p, (0.04, fy - 0.005, TOP - bh - 0.1), (1, 0, 0), (0, 0, 1), (0, -1, 0), (0.07, 0.16, 0.01), region='iron', jitter=0.002)
for i, (x, z) in enumerate(((-0.01, TOP - 0.05), (0.075, TOP - 0.06), (0.07, TOP - bh - 0.06), (0.06, TOP - bh - 0.15), (-0.01, TOP - 0.17))):
    nail(p, (x, fy - 0.01, z), (0, -1, 0), size=0.02, out=0.007, seed=50 + i, region='dark')
# iron strap around the post foot
strap(p, Vector((0.01, 0.0, 0.2)), (0.02, 0, 1), (1, 0, 0), 0.135, 0.12, width=0.06, thick=0.01, seed=60, nails=4)
# rope lashing around the brace foot and the post, with a loose end
for i, tl in enumerate((-0.1, 0.12)):
    c = Vector((-0.025, -0.06, 0.4 + i * 0.065))
    e2 = Vector((1, 0, tl)).normalized()
    rope(p, loop_rect(c, e2, (0, 1, 0), 0.175, 0.225, ch=0.25, jit=0.004, seed=70 + i), 0.024, up=e2.cross(Vector((0, 1, 0))), sides=6,
         closed=True, step=0.09, k=5, seed=72 + i)
t0 = Vector((-0.17, -0.29, 0.42))
rope(p, [t0, t0 + Vector((-0.02, -0.01, -0.1)), t0 + Vector((-0.06, -0.02, -0.2)), t0 + Vector((-0.05, -0.01, -0.29))], 0.022,
     up=(1, 0, 0), sides=6, step=0.05, k=4, seed=74, flare_end=1.4)
# patch over a hole on the post's side, loose nails, splinters
patch(w, p, (0.145, 0.0, 0.95), (1, 0, 0), size=(0.15, 0.12), seed=80)
for i, (pp, nn) in enumerate((((0.13, -0.04, 0.3), (1, 0, 0)), ((-0.6, fy, 1.25), (0, -1, 0)), ((-1.1, fy, 1.33), (0, -1, 0)),
                              ((-0.35, ry - 0.07, 0.72), (0, -1, 0)), ((0.02, -0.12, 0.8), (0, -1, 0)), ((-0.125, 0.04, 0.9), (-1, 0, 0)))):
    nail(p, pp, nn, size=0.02, out=0.007, seed=90 + i, bent=(i in (2, 4)), region='iron' if i % 2 else 'dark')
for i, (pp, dd) in enumerate((((-1.36, by - 0.06, TOP - 0.05), (-1, -0.2, 0.0)), ((-1.37, by + 0.05, TOP - 0.18), (-1, 0.3, -0.3)),
                              ((-0.86, ry - 0.06, 1.24), (-0.6, -1, -0.4)), ((0.1, -0.12, 0.62), (0.3, -1, 0.2)),
                              ((-0.04, ry - 0.06, 0.29), (0.2, -1, -0.6)))):
    splinter(p, pp, dd, length=0.055 + 0.015 * (i % 3), r=0.008, seed=100 + i)
# wedges at the post foot
for i, (x, y, r) in enumerate(((0.155, 0.0, 0.0), (-0.155, 0.05, 3.1416), (0.03, 0.135, 1.5708))):
    dv = Vector((math.cos(r), math.sin(r), 0))
    c = Vector((x, y, 0.06))
    beam(w, [c + dv * 0.05 + Vector((0, 0, -0.06)), c - dv * 0.0 + Vector((0, 0, 0.06))], [0.11, 0.1], [0.05, 0.01],
         up=dv.cross(Vector((0, 0, 1))), jitter=0.004, seed=110 + i, caps=(None, 'flat'))
# earth heaped at the post foot + litter
import random as _r
rr = _r.Random(3)
for i in range(7):
    a = i / 7 * 6.283 + rr.uniform(-.2, .2)
    s_ = rr.uniform(0.05, 0.09)
    lump(p, (math.cos(a) * 0.18, math.sin(a) * 0.16, 0), (s_ * 1.3, s_, s_ * 0.75), 'earth_props', sides=6, rings=2, jitter=0.25,
         seed=120 + i, rot=a)
scatter_ground(p, (-0.05, -0.05, 0), 0.5, squash=(1.2, 0.8), seed=13, clods=10, pebbles=8, chips=7, leaves=4, shavings=3, needles=5,
               ring_min=0.45)

wo = w.to_object('AN16a_Wood')
po = p.to_object('AN16a_Props')
objs = [wo, po]
d = ak.ground(objs)
lo, hi = ak.bounds(objs)
fbx = os.path.join(HERE, 'SM_AN16_DeckBracket.fbx')
ak.export_fbx(objs, fbx)
ak.save_blend(os.path.join(HERE, 'AN16_DeckBracket.blend'))
top_x = (round(-1.36 + d.x, 3), round(0.14 + d.x, 3))
rec = ak.variant_record('AN16a', 'DeckBracket', objs, fbx,
                        solidity='solid', collider='box: post; box: beam; box: brace (or one convex hull)', emissive=False,
                        deck_fit={'beam_top_z': round(TOP + d.z, 3), 'beam_top_x': top_x, 'beam_y': [round(by - bd / 2 + d.y, 3), round(by + bd / 2 + d.y, 3)],
                                  'wall_side': '+X (post back face against the wall / under the deck edge)',
                                  'usage': 'two brackets per SM_ST10_Boardwalk (1.7 x 2.0), beams across its 1.7 m width, placed under its '
                                           'end feet (about 0.25 m from each 2.0 m end); the boardwalk feet sit at beam_top_z. Nothing of the '
                                           'bracket rises above beam_top_z.'},
                        notes='Modelled: post, beam, brace, shim, support cleat, 6 pegs, cracks (V-grooves) and chipped edges, iron angle plates + strap with nails, '
                              'rope lashing with loose end, nailed patch, loose/bent nails, splinters, 3 foot wedges, earth heaped at the foot, '
                              'clods/pebbles/chips/shavings/needles/leaves. Ends use the end_grain disc. Nothing rises above beam_top_z.')
save_manifest(os.path.join(OUT, 'manifest.json'), {'asset': 'AN16 deck-bracket', 'variants': [rec]})
print('BEAMTOP', rec['deck_fit']['beam_top_z'], 'MAXZ', round(float(hi[2]), 4))
print('TRIS', rec['tris'], rec['tris_by_material'], 'SIZE', rec['size_xyz_m'], 'TOP', round(hi[2], 3), top_x)
if RENDER:
    render_set(objs, OUT, SHEET, 'AN16 Deck bracket  %d tris  %s m' % (rec['tris'], rec['size_xyz_m']),
               extra=[(objs, 'detail', dict(az=-30, el=22, target=Vector((-0.2, -0.1, 0.8)) + d, dist=2.6))])
