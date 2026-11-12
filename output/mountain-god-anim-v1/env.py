"""Preview scene for the MG01 clips: textured Workbench, the hand rocks, a cloud plane at the sea top, the platform
stand-in, a camera."""
import bpy, math, os
from mathutils import Vector, Matrix, Quaternion
U = "/mnt/user-data/uploads/hukaton/output/mountain-god-model-v3/"
ROCKS = "/home/claude/gm-art/god/rocks/out/MG01_hand_rocks.blend"
LIP = Vector((0.0, -16.04, 4.132))
SEA = -0.5


def open_anim(path="/home/claude/gm-art/god/anim/out/MG01_anim.blend"):
    bpy.ops.wm.open_mainfile(filepath=path)
    for o in list(bpy.data.objects):
        if o.type == 'EMPTY':
            bpy.data.objects.remove(o, do_unlink=True)
    arm = bpy.data.objects["MG01_TestRig"]
    for pb in arm.pose.bones:
        for c in pb.constraints:
            c.mute = True
    arm.hide_render = True
    return arm


def textures():
    tex = {"Body": "T_MG01_Body_BaseColor.png", "Cloth": "T_MG01_Cloth_BaseColor.png", "Leaves": "T_MG01_Leaves_BaseColor.png"}
    for m in bpy.data.materials:
        key = next((k for k in tex if "MG01_" + k in m.name), None)
        if not key or not m.node_tree:
            continue
        t = next((n for n in m.node_tree.nodes if n.type == 'TEX_IMAGE'), None)
        t.image = bpy.data.images.load(U + "textures/" + tex[key], check_existing=True)
        t.interpolation = 'Closest'
        m.node_tree.nodes.active = t


def rocks():
    with bpy.data.libraries.load(ROCKS) as (src, dst):
        dst.objects = [n for n in src.objects]
    for o in dst.objects:
        if o is not None:
            bpy.context.scene.collection.objects.link(o)
    for o in dst.objects:
        if o is not None and o.parent is None:
            o.scale = (.01, .01, .01)  # built in metres
    for m in bpy.data.materials:
        if m.name.startswith("GM_") and m.node_tree:
            t = next((n for n in m.node_tree.nodes if n.type == 'TEX_IMAGE'), None)
            if t:
                m.node_tree.nodes.active = t


def flat_mat(name, rgb):
    """A flat colour that Workbench's texture mode shows: a 4 x 4 image of it."""
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1)
    m.use_nodes = True
    nt = m.node_tree
    img = bpy.data.images.new(name, 4, 4)
    img.pixels = [*rgb, 1.0] * 16
    t = nt.nodes.new("ShaderNodeTexImage")
    t.image = img
    nt.links.new(t.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
    nt.nodes.active = t
    return m


def scene(res=(640, 360)):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sh = sc.display.shading
    sh.light = 'STUDIO'
    sh.color_type = 'TEXTURE'
    sh.show_cavity = True
    sh.cavity_type = 'WORLD'
    sh.show_shadows = True
    sh.shadow_intensity = .3
    sc.display.light_direction = (-.45, -.35, .82)
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGB'
    sc.render.film_transparent = False
    sc.world = sc.world or bpy.data.worlds.new("w")
    sc.world.color = (.64, .68, .8)
    sc.view_settings.view_transform = 'Standard'
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.sensor_fit = 'VERTICAL'
    cam.data.clip_start = .004
    cam.data.clip_end = 3000
    bpy.ops.mesh.primitive_plane_add(size=600, location=(0, 0, SEA))
    p = bpy.context.active_object
    p.name = "sea"
    p.data.materials.append(flat_mat("sea", (.9, .9, .95)))
    # the summit the player stands on: a block behind the lip down into the clouds
    bpy.ops.mesh.primitive_cube_add(size=1)
    b = bpy.context.active_object
    b.name = "platform"
    b.scale = (1.0, 1.2, LIP.z - SEA + .5)
    b.location = (0, LIP.y - .6, (LIP.z + SEA - .5) / 2)
    b.data.materials.append(flat_mat("platform", (.42, .38, .4)))
    return sc, cam


def look(cam, eye, tgt, fov=60, roll=0.0):
    cam.location = Vector(eye)
    q = (Vector(tgt) - Vector(eye)).to_track_quat('-Z', 'Y')
    if roll:
        q = q @ Quaternion((0, 0, 1), math.radians(roll))
    cam.rotation_euler = q.to_euler()
    cam.data.angle = math.radians(fov)
