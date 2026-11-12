"""Presentation renders (Cycles, matte, lilac sky like the canon) of MG01.blend.
Run: python3 -I render_final.py ASM_DIR OUTDIR [samples]"""
import sys, os, math, json
sys.path.insert(0, '/home/claude/work/mg')
import bpy
from mathutils import Vector as V
import bl, layout as L

D, OUT = sys.argv[1], sys.argv[2]
SAMPLES = int(sys.argv[3]) if len(sys.argv) > 3 else 24
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=os.path.join(D, 'MG01.blend'))
sc = bpy.context.scene
off = V(json.load(open(os.path.join(D, 'assembly_log.json')))['pivot_offset'])
for o in bpy.data.objects:
    if o.name.startswith('RG') or o.type == 'CURVE' or '_LOD' in o.name:
        o.hide_render = True
sc.render.engine = 'CYCLES'
sc.cycles.device = 'CPU'
sc.cycles.samples = SAMPLES
sc.cycles.use_denoising = True
try:
    sc.cycles.denoiser = 'OPENIMAGEDENOISE'
except Exception:
    pass
sc.cycles.max_bounces = 3
sc.render.film_transparent = False
sc.view_settings.view_transform = 'Standard'
sc.view_settings.look = 'None'
w = bpy.data.worlds.new('sky')
sc.world = w
w.use_nodes = True
bg = w.node_tree.nodes['Background']
bg.inputs[0].default_value = bl.hex_rgb('#B3A8DA')
bg.inputs[1].default_value = 0.9
sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN'))
sun.data.energy = 2.6
sun.data.angle = math.radians(8)
sun.rotation_euler = (math.radians(52), math.radians(-18), math.radians(-28))
sun.data.color = (1.0, 0.97, 0.94)
sc.collection.objects.link(sun)
# ground disc to anchor the hands
bpy.ops.mesh.primitive_circle_add(vertices=48, radius=26, fill_type='NGON', location=(0, 0, 0))
gnd = bpy.context.active_object
gm = bpy.data.materials.new('ground'); gm.use_nodes = True
gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = bl.hex_rgb('#6E5F86')
gm.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 1.0
gnd.data.materials.append(gm)
# matte everything (spec: no specular)
for m in bpy.data.materials:
    if m.use_nodes:
        for n in m.node_tree.nodes:
            if n.type == 'BSDF_PRINCIPLED':
                n.inputs['Roughness'].default_value = 1.0
                if 'Specular IOR Level' in n.inputs:
                    n.inputs['Specular IOR Level'].default_value = 0.0
views = {
    'front_canon': dict(loc=L.CAM + off, rot=(90, 0, 0), lens=L.LENS_MM, w=1199, h=1312),
    'three_quarter_left': dict(loc=(off.x - 24, off.y - 30, 7.0), look=(off.x + 0.5, off.y, 9.2), lens=52, w=1000, h=1100),
    'three_quarter_right': dict(loc=(off.x + 26, off.y - 28, 6.0), look=(off.x + 0.5, off.y, 9.2), lens=52, w=1000, h=1100),
    'back': dict(loc=(off.x + 3, off.y + 38, 9.0), look=(off.x + 0.5, off.y, 9.2), lens=50, w=1000, h=1100),
    'side_left': dict(loc=(off.x - 40, off.y + 1, 9.0), look=(off.x, off.y, 9.2), lens=50, w=1000, h=1100),
    'upper_close': dict(loc=(off.x + 1.5, off.y - 13, 13.5), look=(off.x + 0.7, off.y, 13.0), lens=45, w=1000, h=1000),
    'pelvis_q3': dict(loc=(off.x - 9, off.y - 12, 11.5), look=(off.x + 0.5, off.y + 0.3, 10.0), lens=40, w=900, h=900),
    'pelvis_side': dict(loc=(off.x + 14, off.y - 2, 10.5), look=(off.x + 0.5, off.y + 0.3, 10.0), lens=40, w=900, h=900),
    'pelvis_front': dict(loc=(off.x + 0.5, off.y - 14, 9.0), look=(off.x + 0.5, off.y + 0.3, 10.2), lens=40, w=900, h=900),
    'cu_feet': dict(loc=(L.FEET['L01']['ankle'] + L.FEET['L02']['ankle']) / 2 + off + V((-1.5, -9.0, -0.5)),
                    look=(L.FEET['L01']['ankle'] + L.FEET['L02']['ankle']) / 2 + off + V((0, 0, -0.6)), lens=40, w=1000, h=700),
    'hub_back_low': dict(loc=(off.x - 6, off.y + 16, 3.5), look=(off.x, off.y + 2.0, 6.5), lens=32, w=900, h=900),
    'claw_A03': dict(loc=(off.x - 6.5, off.y - 7, 12.5), look=(off.x - 3.3, off.y - 0.8, 11.6), lens=45, w=800, h=800),
    'hand_C02': dict(loc=(off.x + 9, off.y - 7, 13.5), look=(off.x + 3.2, off.y + 0.3, 12.0), lens=40, w=800, h=800),
    'low_hero': dict(loc=(off.x - 6.5, off.y - 15.5, 1.4), look=(off.x + 0.5, off.y, 9.5), lens=22, w=1000, h=1100),
}
only = sys.argv[4].split(',') if len(sys.argv) > 4 else None
for n, v in views.items():
    if only and n not in only:
        continue
    sc.render.resolution_x, sc.render.resolution_y = v['w'], v['h']
    if 'rot' in v:
        bl.make_camera(sc, v['loc'], rot_deg=v['rot'], lens=v['lens'])
    else:
        bl.make_camera(sc, v['loc'], look_at=v['look'], lens=v['lens'])
    bl.render(sc, os.path.join(OUT, f'MG01_{n}.png'))
    print('rendered', n)
