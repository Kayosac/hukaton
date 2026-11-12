"""MG01 for Unity with its clips: the rig (hat and beard hanging from the head), the skinned body, cloth and leaves in
the rest (sitting) pose, and the seven baked clips (Idle, Notice, AlertIdle, Reach, ReachHold, Lift, Talk) as FBX takes.
Same axes as MG01.fbx. usage: python3 export_anim.py <MG01_anim.blend> <out.fbx>"""
import sys
import bpy

SRC, OUT = sys.argv[1], sys.argv[2]
bpy.ops.wm.open_mainfile(filepath=SRC)
for o in list(bpy.data.objects):
    if o.type == "EMPTY":
        bpy.data.objects.remove(o, do_unlink=True)
arm = bpy.data.objects["MG01_TestRig"]
for pb in arm.pose.bones:
    for c in list(pb.constraints):
        pb.constraints.remove(c)
    pb.rotation_mode = "QUATERNION"
    pb.rotation_quaternion = (1, 0, 0, 0)
    pb.location = (0, 0, 0)
    pb.scale = (1, 1, 1)
arm.name = "MG01_Rig"
arm.data.name = "MG01_Rig"
arm.animation_data_create()
arm.animation_data.action = None
for n in ("MG01_Cloth", "MG01_Leaves"):
    o = bpy.data.objects[n]
    mw = o.matrix_world.copy()
    o.parent = arm
    o.matrix_world = mw
for n in ("MG01_Body", "MG01_Cloth", "MG01_Leaves"):
    o = bpy.data.objects[n]
    for m in o.modifiers:
        if m.type == "ARMATURE":
            m.object = arm
acts = [a for a in bpy.data.actions if a.name.startswith("MG01_")]
for a in acts:
    a.use_fake_user = True
    print(a.name, a.frame_range[:])
sc = bpy.context.scene
sc.render.fps = 30
bpy.context.view_layer.update()
for o in sc.objects:
    o.select_set(o.name in {"MG01_Rig", "MG01_Body", "MG01_Cloth", "MG01_Leaves"})
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={"ARMATURE", "MESH"}, apply_unit_scale=True,
                         apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=False,
                         add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X", use_armature_deform_only=False,
                         bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
                         bake_anim_use_all_actions=True, bake_anim_force_startend_keying=True, bake_anim_step=1.0,
                         bake_anim_simplify_factor=0.5, mesh_smooth_type="OFF", use_triangles=False, path_mode="STRIP",
                         use_mesh_modifiers=False)
print("exported", OUT)
