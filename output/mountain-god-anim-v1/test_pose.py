import sys, math
sys.path.insert(0, "/home/claude/gm-art/god/anim")
import bpy
from mathutils import Vector, Matrix, Quaternion
import godrig as G

arm = G.setup()
T = G.rig(arm)
vl = bpy.context.view_layer
def world_tail(b): return arm.matrix_world @ arm.pose.bones[b].tail
def world_head(b): return arm.matrix_world @ arm.pose.bones[b].head
# rest check: IK on, targets at rest -> hands must not move
before = {b: world_tail(b).copy() for b in ("S01_wrist_00","S02_wrist_00","S03_wrist_00","C02_wrist_00","C01_wrist_00","S01_shoulder")}
vl.update()
for b,v in before.items(): print("rest drift", b, round((world_tail(b)-v).length,4))

# straighten the index finger (f0): every segment along f0_00
def straighten(w=1.0):
    for b in ("C01_f0_01", "C01_f0_02"):
        pb = arm.pose.bones[b]; pb.rotation_quaternion = Quaternion()
    vl.update()
    d0 = (world_tail("C01_f0_00") - world_head("C01_f0_00")).normalized()
    for b in ("C01_f0_01", "C01_f0_02"):
        vl.update()
        pb = arm.pose.bones[b]
        cur = (world_tail(b) - world_head(b)).normalized()
        q = cur.rotation_difference(d0)
        # world rotation -> local: conjugate by the bone's current world rotation
        mw = (arm.matrix_world @ pb.matrix).to_quaternion()
        ql = mw.inverted() @ q @ mw
        pb.rotation_quaternion = Quaternion().slerp(ql, w) @ pb.rotation_quaternion
    vl.update()
straighten()
wt = G.tail_matrix(arm, "C01_wrist_00")
tip = world_tail("C01_f0_02"); base = world_head("C01_f0_00")
f_rest = (tip - base).normalized()
w_rest = (world_head("C01_wrist_00") - world_tail("C01_wrist_00")).normalized()
print("finger len", round((tip-base).length,3), "f_rest", f_rest, "tip-wrist", round((tip - wt.translation).length,3))
def frame(f, w):
    x = f.normalized(); y = (w - x * w.dot(x)).normalized(); z = x.cross(y)
    return Matrix((x, y, z)).transposed()
P = G.LIP + Vector((0, -.05, -.01 - G.FINGER_R))
fd = Vector((0, -1, 0)); w_des = Vector((0, 1, .6))
R = frame(fd, w_des) @ frame(f_rest, w_rest).inverted()
Rq = R.to_quaternion()
pos = P - Rq @ (tip - wt.translation)
M1 = Matrix.Translation(pos) @ (Rq @ wt.to_quaternion()).to_matrix().to_4x4()
T["C01"].matrix_world = M1
# lean the torso a little and drop the pelvis
G.set_rot(arm, "Pelvis", ((1, 0, 0), 4))
G.set_rot(arm, "Spine", ((1, 0, 0), 5))
G.set_rot(arm, "Chest", ((1, 0, 0), 3))
G.move(arm, "Pelvis", (0, 0, -.35))
vl.update()
print("tip now", world_tail("C01_f0_02"), "wanted", P, "miss", round((world_tail("C01_f0_02") - P).length, 3))
for s in ("S01","S02","S03"):
    print("support", s, "miss", round((world_tail(s+"_wrist_00") - T[s].matrix_world.translation).length, 4))
import json
json.dump({"M1": [list(r) for r in M1], "P": list(P)}, open("/tmp/god/m1.json", "w"))
# renders
sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'SINGLE'
sc.display.shading.single_color = (.45, .5, .75); sc.display.shading.show_cavity = True
sc.render.resolution_x, sc.render.resolution_y = 800, 450
cam = bpy.data.objects.new("c", bpy.data.cameras.new("c")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.sensor_fit = 'VERTICAL'; cam.data.angle = math.radians(75); cam.data.clip_end = 1000
# lip marker
bpy.ops.mesh.primitive_cube_add(size=1, location=G.LIP + Vector((0, -.07, -.02)))
c = bpy.context.active_object; c.scale = (.14, .14, .04)
def shot(eye, tgt, path):
    cam.location = Vector(eye); cam.rotation_euler = (Vector(tgt) - Vector(eye)).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = path; bpy.ops.render.render(write_still=True)
shot(G.LIP + Vector((0, -.003, .016)), Vector((.5, 2, 11)), "/tmp/god/t_player.png")
shot(Vector((-26, -14, 9)), Vector((0, -6, 8)), "/tmp/god/t_side.png")
shot(G.LIP + Vector((-.25, -.25, .1)), G.LIP + Vector((0, .3, -.1)), "/tmp/god/t_dock.png")
