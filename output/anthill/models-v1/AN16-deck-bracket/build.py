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
     jitter=0.012, seed=21, caps=(None, 'bevel'), endm=p, bevel=0.3)
# beam: in front of the post, top flat at TOP
bh, bd = 0.23, 0.18
by = -0.11 - bd / 2
beam(w, [(0.14, by, TOP - bh / 2), (-0.3, by - 0.006, TOP - bh / 2 - 0.004), (-0.85, by + 0.004, TOP - bh / 2 - 0.012), (-1.36, by + 0.006, TOP - bh / 2)],
     [bh, bh - 0.01, bh - 0.03, bh], [bd, bd + 0.01, bd, bd - 0.01], up=(0, 1, 0), jitter=0.0, seed=22, caps=('flat', 'flat'), endm=p)
# diagonal brace (front plane), foot pegged to the post, head under the beam
ry = by + 0.01
beam(w, [(-0.02, ry, 0.30), (-0.3, ry - 0.006, 0.66), (-0.56, ry + 0.004, 1.0), (-0.86, ry, TOP - bh + 0.04)], [0.16, 0.15, 0.165, 0.15],
     [0.14, 0.145, 0.14, 0.14], up=(0, 1, 0), jitter=0.01, seed=23, caps=('flat', 'flat'))
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
                        notes='Post, beam, brace and 6 square pegs (incl. through-ends on the back) are separate real geometry; '
                              'beam/peg ends use the end_grain disc.')
save_manifest(os.path.join(OUT, 'manifest.json'), {'asset': 'AN16 deck-bracket', 'variants': [rec]})
print('TRIS', rec['tris'], rec['tris_by_material'], 'SIZE', rec['size_xyz_m'], 'TOP', round(hi[2], 3), top_x)
if RENDER:
    render_set(objs, OUT, SHEET, 'AN16 Deck bracket  %d tris  %s m' % (rec['tris'], rec['size_xyz_m']))
