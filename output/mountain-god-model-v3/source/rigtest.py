"""Throw-away deformation test: build an armature from MG01_rig_guides.json, bind the body with automatic
(heat) weights, pose a few chains and render before/after. Proves the mesh is rig-ready; the armature is
NOT part of the delivered model.
Run: python3 -I rigtest.py BODY_BLEND GUIDES_JSON OUTDIR
"""
import sys, os, json, math
sys.path.insert(0, '/home/claude/work/mg')
import bpy
from mathutils import Vector as V
import bl

blend, guides, out = sys.argv[1], sys.argv[2], sys.argv[3]
os.makedirs(out, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=blend)
sc = bpy.context.scene
body = bpy.data.objects['MG01_Body']
G = json.load(open(guides))

arm_data = bpy.data.armatures.new('MG01_TestRig')
arm = bpy.data.objects.new('MG01_TestRig', arm_data)
sc.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
for o in bpy.context.selected_objects:
    o.select_set(False)
arm.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
eb = arm_data.edit_bones
joint_pos = {}
for key, chain in G['chains'].items():
    for j in chain:
        joint_pos[j['name']] = V(j['pos'])
bones_of_chain = {}
root = eb.new('Root'); root.head = V((0, 0, 0)); root.tail = V((0, 0, 1.0))
for key, chain in G['chains'].items():
    if len(chain) < 2:
        continue
    names = []
    for a, b in zip(chain, chain[1:]):
        bn = eb.new(a['name'])
        bn.head, bn.tail = V(a['pos']), V(b['pos'])
        if (bn.tail - bn.head).length < 1e-3:
            bn.tail = bn.head + V((0, 0, 0.05))
        if names:
            bn.parent = eb[names[-1]]
            bn.use_connect = True
        names.append(bn.name)
    bones_of_chain[key] = names
# parent chain roots to the bone whose HEAD is the named parent joint (or whose chain contains it)
name_to_bone = {}
for key, names in bones_of_chain.items():
    for n in names:
        name_to_bone[n] = n
    # last joint of a chain has no bone of its own: map it to the last bone
    last = G['chains'][key][-1]['name']
    name_to_bone[last] = names[-1]
for key, names in bones_of_chain.items():
    p = G['parents'].get(key, 'Root')
    pb = name_to_bone.get(p, 'Root')
    if pb in eb and pb != names[0]:
        eb[names[0]].parent = eb[pb]
bpy.ops.object.mode_set(mode='OBJECT')
print('bones', len(arm_data.bones))

# bind with automatic weights
for o in bpy.context.selected_objects:
    o.select_set(False)
body.select_set(True)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
res = bpy.ops.object.parent_set(type='ARMATURE_AUTO')
print('parent_set', res)
# limb masking: a vertex may only be weighted to bones of its own limb group (+ spine/hub bones);
# heat weights otherwise bleed across fused limbs (thigh in palm, small-arm roots in shoulders).
chain_of_bone = {}
for key, names in bones_of_chain.items():
    for n in names:
        chain_of_bone[n] = key
shared = {'SPINE', 'HUB', 'ROOT'}
def limb_of_chain(key):
    if key in shared or key.startswith('HEAD_'):
        return 'HEAD' if key.startswith('HEAD_') else None
    return key.split('_')[0]
vg_bone = {vg.index: vg.name for vg in body.vertex_groups if vg.name in arm_data.bones}
logical = {vg.index: vg.name for vg in body.vertex_groups if vg.name not in arm_data.bones}
masked = 0
for v in body.data.vertices:
    lg = [logical[g.group] for g in v.groups if g.group in logical and g.weight > 0.5]
    if not lg:
        continue
    own = lg[0]
    keep, tot = [], 0.0
    for g in v.groups:
        if g.group in vg_bone:
            lb = limb_of_chain(chain_of_bone.get(vg_bone[g.group], 'ROOT'))
            ok = lb is None or lb == own or (own == 'CORE' and lb is None)
            if ok:
                tot += g.weight
            else:
                g.weight = 0.0
                masked += 1
    if tot > 1e-6:
        for g in v.groups:
            if g.group in vg_bone and g.weight > 0:
                g.weight = g.weight / tot
print('masked weights', masked)
# verts left without any weight after masking -> nearest bone of their own limb (spine for CORE/HEAD)
from mathutils.geometry import intersect_point_line
def seg_dist(p, b):
    q, t = intersect_point_line(p, b.head_local, b.tail_local)
    t = min(1, max(0, t))
    return (p - (b.head_local + (b.tail_local - b.head_local) * t)).length
refilled = 0
for v in body.data.vertices:
    tot = sum(g.weight for g in v.groups if g.group in vg_bone)
    if tot > 1e-6:
        continue
    lg = [logical[g.group] for g in v.groups if g.group in logical and g.weight > 0.5]
    own = lg[0] if lg else 'CORE'
    cands = [b for b in arm_data.bones if limb_of_chain(chain_of_bone.get(b.name, 'ROOT')) == (None if own == 'CORE' else own)
             or (own == 'CORE' and chain_of_bone.get(b.name) in ('SPINE', 'HUB'))]
    if not cands:
        continue
    b = min(cands, key=lambda bb: seg_dist(v.co, bb))
    vg = body.vertex_groups.get(b.name) or body.vertex_groups.new(name=b.name)
    vg.add([v.index], 1.0, 'REPLACE')
    vg_bone[vg.index] = b.name
    refilled += 1
print('refilled', refilled)
unweighted = sum(1 for v in body.data.vertices if not any(g.weight > 0.01 and body.vertex_groups[g.group].name in arm_data.bones for g in v.groups))
print('unweighted verts', unweighted, 'of', len(body.data.vertices))

for o in bpy.data.objects:
    if o.name.startswith('RG') or o.type == 'CURVE':
        o.hide_render = True
arm.hide_render = True
paths = bl.turntable(sc, out, 'rest', angles=(0, 135))
# pose: swing chains to stress joints
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='POSE')
pb = arm.pose.bones
def rot(name, axis, deg):
    if name in pb:
        b = pb[name]
        b.rotation_mode = 'XYZ'
        r = list(b.rotation_euler)
        r['XYZ'.index(axis)] += math.radians(deg)
        b.rotation_euler = r
for key, names in bones_of_chain.items():
    if key in ('C01', 'C02', 'S01', 'S02', 'S03', 'A01', 'A02', 'A03', 'A04', 'L01', 'L02', 'L03', 'L04'):
        for i, n in enumerate(names[1:3]):
            rot(n, 'X', 25 if i == 0 else -20)
rot('Neck', 'X', 20); rot('Chest', 'Z', 12)
for key, names in bones_of_chain.items():
    if '_f' in key or 'thumb' in key:
        for n in names[1:]:
            rot(n, 'X', 30)
bpy.ops.object.mode_set(mode='OBJECT')
paths += bl.turntable(sc, out, 'posed', angles=(0, 135))
bl.contact_sheet(paths, os.path.join(out, 'rigtest_sheet.png'), cols=4, scale=0.5)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, 'rigtest.blend'))
json.dump(dict(bones=len(arm_data.bones), unweighted=unweighted, verts=len(body.data.vertices)), open(os.path.join(out, 'rigtest.json'), 'w'))
