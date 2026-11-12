"""Bake the 5 preview clips into Blender actions on the test rig (IK of S02 baked to FK), so the scene can be
scrubbed in Blender. Output: MG01_rig_preview.blend (test armature + body/cloth/leaves skinned + bone viz).
Run: python3 -I anim_bake.py OUT_BLEND"""
import sys, os, types
OUT = sys.argv[1]
# reuse the clip definitions of anim.py without rendering: exec it up to the render loop
src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'anim_preview.py')).read().split('def render_frame')[0]
sys.argv = ['anim.py', 'preview', '/tmp/bake_dummy']
g = {'__name__': 'anim_bake'}
exec(compile(src, 'anim.py', 'exec'), g)
import bpy
from mathutils import Quaternion as Q
sc, arm, pbs, acc = g['sc'], g['arm'], g['pbs'], g['acc']
ikc, ik_t, S02_WRIST = g['ikc'], g['ik_t'], g['S02_WRIST']
sc.render.fps = 15
IKB = ['S02_shoulder', 'S02_elbow', 'S02_seg03']
arm.animation_data_create()
first = None
for name, fn, n, cam, title in g['CLIPS']:
    poses = []
    for f in range(n + 1):           # last key == first key -> clean loop
        acc.clear(); ik_t.location = S02_WRIST
        ikc.influence = 1.0 if name == 'slam' else 0.0
        fn((f % n) / n)
        g['apply_pose']()
        q = {pb.name: pb.rotation_quaternion.copy() for pb in pbs}
        if name == 'slam':
            for b in IKB:
                pb = pbs[b]
                m = arm.convert_space(pose_bone=pb, matrix=pb.matrix, from_space='POSE', to_space='LOCAL')
                q[b] = m.to_quaternion()
        poses.append(q)
    moving = [b for b in poses[0] if any(abs(1 - abs(p[b].dot(Q()))) > 1e-6 for p in poses)]
    act = bpy.data.actions.new(f'MG01_test_{name}')
    act.use_fake_user = True
    arm.animation_data.action = act
    ikc.influence = 0.0
    for f, q in enumerate(poses):
        for b in moving:
            pbs[b].rotation_quaternion = q[b]
            pbs[b].keyframe_insert('rotation_quaternion', frame=f + 1)
    act['clip_title'] = title
    act['frame_range'] = (1, n + 1)
    print('baked', name, len(moving), 'bones', n, 'frames')
    first = first or act
# IK constraint stays on the rig for experiments but is off; the IK target empty is kept
ikc.influence = 0.0
arm.animation_data.action = first
for pb in pbs:
    pb.rotation_quaternion = (1, 0, 0, 0)
sc.frame_start, sc.frame_end = 1, 41
arm.hide_render = True
arm.hide_viewport = False
arm.show_in_front = True
arm.data.display_type = 'OCTAHEDRAL'
bpy.data.objects['MG01_BoneViz'].hide_viewport = True
bpy.data.objects['MG01_BoneViz'].hide_render = True
bpy.ops.wm.save_as_mainfile(filepath=OUT, compress=True)
print('saved', OUT)
