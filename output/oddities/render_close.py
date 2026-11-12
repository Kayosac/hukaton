import os, math, bpy
from mathutils import Vector
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'out')
def shoot(blend, name, target_z, distance, ang, lift):
    bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT, blend))
    sc = bpy.context.scene; sc.render.engine = 'BLENDER_WORKBENCH'
    sh = sc.display.shading; sh.light = 'STUDIO'; sh.color_type = 'TEXTURE'; sh.show_shadows = True
    sc.render.resolution_x, sc.render.resolution_y = 700, 800
    w = bpy.data.worlds.new('w'); sc.world = w; w.color = (.35, .3, .3)
    cd = bpy.data.cameras.new('c'); cd.lens = 50
    cam = bpy.data.objects.new('c', cd); sc.collection.objects.link(cam); sc.camera = cam
    t = Vector((0, 0, target_z)); a = math.radians(ang)
    cam.location = Vector((math.sin(-a) * distance, -math.cos(a) * distance, target_z + lift))
    cam.rotation_euler = (t - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = os.path.join(OUT, f'close_{name}.png'); bpy.ops.render.render(write_still=True)
shoot('OD01_Payphone.blend', 'phone', 1.0, 3.6, -22, .4)
shoot('OD02_OldTV.blend', 'tv', .42, 2.0, -18, .25)
shoot('OD01_Payphone.blend', 'sign', 1.88, 1.2, 0, .4)
