"""Build the delivery folder from an assembly dir: relative texture paths, previews, logs, sources.
Run: python3 -I package.py ASM_DIR DELIVER_DIR"""
import sys, os, shutil, json, glob
sys.path.insert(0, '/home/claude/work/mg')
import bpy

A, D = sys.argv[1], sys.argv[2]
os.makedirs(D, exist_ok=True)
for sub in ('textures', 'previews', 'checks', 'source'):
    os.makedirs(os.path.join(D, sub), exist_ok=True)
for t in ('T_MG01_Body_BaseColor.png', 'T_MG01_Cloth_BaseColor.png', 'T_MG01_Leaves_BaseColor.png'):
    shutil.copy2(os.path.join(A, t), os.path.join(D, 'textures', t))
shutil.copy2(os.path.join(A, 'MG01.fbx'), os.path.join(D, 'MG01.fbx'))
shutil.copy2(os.path.join(A, 'MG01_rig_guides.json'), os.path.join(D, 'MG01_rig_guides.json'))
shutil.copy2(os.path.join(A, 'assembly_log.json'), os.path.join(D, 'checks', 'assembly_log.json'))
if os.path.exists(os.path.join(A, 'debug_uv_layout.png')):
    shutil.copy2(os.path.join(A, 'debug_uv_layout.png'), os.path.join(D, 'checks', 'uv_layout.png'))

bpy.ops.wm.open_mainfile(filepath=os.path.join(A, 'MG01.blend'))
for img in bpy.data.images:
    if img.filepath:
        img.filepath = os.path.abspath(os.path.join(D, 'textures', os.path.basename(bpy.path.abspath(img.filepath))))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(D, 'MG01.blend'), relative_remap=True, compress=True)
bpy.ops.file.make_paths_relative()
bpy.ops.wm.save_mainfile(compress=True)
# verify the textures resolve from the delivered blend
bpy.ops.wm.open_mainfile(filepath=os.path.join(D, 'MG01.blend'))
bad = [i.name for i in bpy.data.images if i.filepath and not os.path.exists(bpy.path.abspath(i.filepath))]
print('missing textures in delivered blend:', bad)
for b1 in glob.glob(os.path.join(D, '*.blend1')):
    os.remove(b1)
for f in glob.glob('/home/claude/work/mg/*.py') + ['/home/claude/work/mg/README_AGENTS.md']:
    shutil.copy2(f, os.path.join(D, 'source', os.path.basename(f)))
print('packaged', D)
