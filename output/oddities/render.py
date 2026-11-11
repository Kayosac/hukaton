"""Preview renders of the oddities from the front 3/4 and the side (Workbench, textured, flat)."""
import os, sys, math
import bpy
from mathutils import Vector

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'out')

def shoot(blend, name, target_z, distance, extra=None):
    bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT, blend))
    if extra: extra()
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    shading = scene.display.shading
    shading.light = 'STUDIO'; shading.color_type = 'TEXTURE'
    shading.show_shadows = True
    scene.render.resolution_x, scene.render.resolution_y = 520, 620
    world = bpy.data.worlds.new('w'); scene.world = world; world.color = (.08, .1, .1)
    cam_data = bpy.data.cameras.new('cam'); cam_data.lens = 40
    cam = bpy.data.objects.new('cam', cam_data); scene.collection.objects.link(cam); scene.camera = cam
    target = Vector((0, 0, target_z))
    for tag, ang in (('front', -25), ('side', -80), ('back', 150)):
        a = math.radians(ang)
        # Unity front (+z) is Blender -y
        cam.location = Vector((math.sin(-a) * distance, -math.cos(a) * distance, target_z + .35))
        cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(OUT, f'preview_{name}_{tag}.png')
        bpy.ops.render.render(write_still=True)

def dangle():
    h = bpy.data.objects['OD01_Handset']
    h.rotation_euler = (0, math.radians(-150), 0)

shoot('OD01_Payphone.blend', 'payphone', 1.05, 3.4)
shoot('OD01_Payphone.blend', 'payphone_used', 1.05, 3.4, dangle)
shoot('OD02_OldTV.blend', 'tv', .42, 2.0)
