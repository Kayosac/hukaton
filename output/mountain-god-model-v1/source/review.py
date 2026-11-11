"""Review renders of an assembled MG01.blend: canon-camera overlay + textured turntable + close-ups.
Run: python3 -I review.py BLEND OUTDIR [--solid]"""
import sys, os, math
sys.path.insert(0, '/home/claude/work/mg')
import bpy
from mathutils import Vector as V
import bl, layout as L

blend, out = sys.argv[1], sys.argv[2]
solid = '--solid' in sys.argv
os.makedirs(out, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=blend)
sc = bpy.context.scene
for o in bpy.data.objects:
    if o.name.startswith('RG') or o.type == 'CURVE':
        o.hide_render = True
    if '_LOD1' in o.name or '_LOD2' in o.name:
        o.hide_render = True
root = bpy.data.objects.get('MG01_Root')
off = V((0, 0, 0))
log = os.path.join(os.path.dirname(blend), 'assembly_log.json')
if os.path.exists(log):
    import json
    po = json.load(open(log)).get('pivot_offset')
    if po:
        off = V(po)
ct = 'MATERIAL' if solid else 'TEXTURE'
# canon camera (shifted by the pivot offset so the overlay still lines up)
bl.setup_workbench(sc, ct)
cam = bl.make_camera(sc, L.CAM + off, rot_deg=(90, 0, 0), lens=L.LENS_MM)
p = bl.render(sc, os.path.join(out, 'canon.png'))
bl.overlay(p, os.path.join(out, 'overlay.png'))
tgt = (0.5 + off.x, off.y + 0.3, 9.0)
tt = bl.turntable(sc, out, 'tt', target=tgt, dist=40, height=9.5, angles=(0, 45, 90, 135, 180, 225, 270, 315), w=800, h=880, color_type=ct)
bl.contact_sheet(tt, os.path.join(out, 'turntable.png'), cols=4, scale=0.5)
# low hero angle + top
bl.setup_workbench(sc, ct, 900, 980)
bl.make_camera(sc, (off.x - 14, off.y - 26, 2.0), look_at=(off.x + 0.5, off.y, 9.0), lens=32)
hero = bl.render(sc, os.path.join(out, 'hero_low.png'))
bl.make_camera(sc, (off.x + 0.5, off.y + 0.01, 60), look_at=(off.x + 0.5, off.y, 0), ortho=26)
top = bl.render(sc, os.path.join(out, 'top.png'))
# close-ups: head, torso eyes, front support hand, cradle hands
shots = {
    'cu_head': ((2.4, -9.5, 16.6), (1.6, 0.2, 15.6), 40),
    'cu_torso': ((0.5, -10, 13.0), (0.6, 0.0, 12.8), 40),
    'cu_S02': ((2.5, -14, 5.5), (0.0, -4.5, 1.5), 35),
    'cu_cradle': ((1.5, -13, 11.0), (0.8, 0.0, 10.0), 32),
    'cu_back': ((0.5, 16, 12.0), (0.6, 0.5, 11.0), 35),
}
cus = []
bl.setup_workbench(sc, ct, 800, 800)
for n, (loc, look, lens) in shots.items():
    bl.make_camera(sc, V(loc) + off, look_at=V(look) + off, lens=lens)
    cus.append(bl.render(sc, os.path.join(out, n + '.png')))
bl.contact_sheet(cus + [hero], os.path.join(out, 'closeups.png'), cols=3, scale=0.5)
print('review done', out)
