"""Shared setup for MG01's animation: open the test rig, clean it, add IK (support hands pinned to their rocks,
cradle hands holding the thighs, C01 free for the reach), helpers to pose bones about armature axes."""
import math
import bpy
from mathutils import Vector, Quaternion, Matrix

SRC = "/mnt/user-data/uploads/hukaton/output/mountain-god-model-v3/rig_preview/MG01_rig_preview.blend"
FPS = 30
# The lip of the summit platform, in the god's own coordinates (Blender, 1 unit = 100 m): the scene places the god at
# (42.4, -400, 1666.3) turned 180°, scale 100; the lip is at (42.4, 13.2, 62.3) in the level.
LIP = Vector((0.0, -16.04, 4.132))
FINGER_R = .2  # radius of C01's index finger at its tip (units)


def setup():
    bpy.ops.wm.open_mainfile(filepath=SRC)
    sc = bpy.context.scene
    sc.render.fps = FPS
    keep = {"MG01_TestRig", "MG01_Body", "MG01_Cloth", "MG01_Leaves"}
    for o in list(bpy.data.objects):
        if o.name not in keep:
            bpy.data.objects.remove(o, do_unlink=True)
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)
    arm = bpy.data.objects["MG01_TestRig"]
    arm.animation_data_clear()
    for pb in arm.pose.bones:
        for c in list(pb.constraints):
            pb.constraints.remove(c)
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.location = (0, 0, 0)
        pb.scale = (1, 1, 1)
    separate_limbs(bpy.data.objects["MG01_Body"], arm)
    # the hat and the beard hang from the head (they came parented to the neck): nods and tilts carry them
    bpy.context.view_layer.objects.active = arm
    for o in bpy.context.view_layer.objects:
        o.select_set(o == arm)
    bpy.ops.object.mode_set(mode="EDIT")
    for n in ("hat_00", "beard_00"):
        eb = arm.data.edit_bones[n]
        eb.use_connect = False
        eb.parent = arm.data.edit_bones["head_00"]
    bpy.ops.object.mode_set(mode="OBJECT")
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
    bpy.context.view_layer.update()
    return arm


FREE = ("C01", "A01", "A02", "A03", "A04")


def separate_limbs(body, arm, keep_root=2.6):
    """The body is one fused mesh: C01's palm grows into thigh L01, the small arms touch the head and C02. Faces that
    join a free limb (C01, A01–A04) to anything but its own root on the torso would stretch into strings when the
    limb moves, so they are cut out and both sides capped."""
    import bmesh
    limbs = [vg for vg in body.vertex_groups if vg.name in ("CORE", "HEAD", "S01", "S02", "S03", "C01", "C02", "A01", "A02",
                                                              "A03", "A04", "L01", "L02", "L03", "L04")]
    idx = {vg.index: vg.name for vg in limbs}
    me = body.data
    limb = []
    for v in me.vertices:
        best = max(((g.weight, idx[g.group]) for g in v.groups if g.group in idx), default=(0, "CORE"))
        limb.append(best[1])
    roots = {n: arm.matrix_world @ arm.data.bones[n + "_seg00"].head_local for n in FREE}
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    cut = []
    for f in bm.faces:
        s = {limb[v.index] for v in f.verts}
        free = s & set(FREE)
        if not free or len(s) == 1:
            continue
        other = s - free
        n = next(iter(free))
        if len(free) > 1 or other - {"CORE"}:
            cut.append(f)
        elif (body.matrix_world @ f.calc_center_median() - roots[n]).length > keep_root:
            cut.append(f)  # touches the torso away from its own shoulder (C01 under the pelvis)
    bmesh.ops.delete(bm, geom=cut, context="FACES")
    boundary = [e for e in bm.edges if e.is_boundary]
    res = bmesh.ops.holes_fill(bm, edges=boundary, sides=0)
    bmesh.ops.triangulate(bm, faces=res["faces"])
    bm.to_mesh(me)
    bm.free()
    me.update()
    print("separate_limbs: cut", len(cut), "faces, capped", len(res["faces"]), "holes")
    # Deleting faces drops their loose vertices: indices moved, so read the limbs again.
    limb = []
    for v in me.vertices:
        best = max(((g.weight, idx[g.group]) for g in v.groups if g.group in idx), default=(0, "CORE"))
        limb.append(best[1])
    # Weights: a free limb's skin follows only its own bones (away from its root); cloth never follows a free limb.
    bones = set(b.name for b in arm.data.bones)
    def drop(v, gname):
        own = limb[v.index]
        if own in FREE:
            return not gname.startswith(own + "_") and (body.matrix_world @ v.co - roots[own]).length > keep_root
        # the rest of the body never follows a free limb, except right at that limb's root
        if gname.startswith(FREE):
            return (body.matrix_world @ v.co - roots[gname[:3]]).length > keep_root
        return False
    def segs(names):
        return [(n, arm.matrix_world @ arm.data.bones[n].head_local, arm.matrix_world @ arm.data.bones[n].tail_local) for n in names]

    def nearest(p, cands):
        best, bd = None, 1e18
        for n, a, b in cands:
            ab = b - a
            t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
            d = (p - (a + ab * t)).length
            if d < bd:
                best, bd = n, d
        return best

    own_bones = {n: segs([b.name for b in arm.data.bones if b.name.startswith(n + "_")]) for n in FREE}
    body_bones = segs([b.name for b in arm.data.bones if not b.name.startswith(FREE)])

    def refill(ob, v):
        p = ob.matrix_world @ v.co
        if ob == body and limb[v.index] in FREE:
            return nearest(p, own_bones[limb[v.index]])
        return nearest(p, body_bones)

    fixed = clean_weights(body, bones, drop, refill)
    for name in ("MG01_Cloth", "MG01_Leaves"):
        fixed += clean_weights(bpy.data.objects[name], bones, lambda v, gname: gname.startswith(FREE), refill)
    print("separate_limbs: weights cleaned on", fixed, "vertices")


def clean_weights(ob, bones, drop, refill):
    gi = {vg.index: vg for vg in ob.vertex_groups}
    n = 0
    for v in ob.data.vertices:
        ws = [(gi[g.group], g.weight) for g in v.groups if gi[g.group].name in bones and g.weight > 1e-5]
        gone = [vg for vg, w in ws if drop(v, vg.name)]
        if not gone and ws:
            continue
        keep = [(vg, w) for vg, w in ws if vg not in gone]
        total = sum(w for _, w in keep)
        for vg in gone:
            vg.remove([v.index])
        if total < 1e-6:
            # nothing allowed is left: the nearest allowed bone takes the vertex whole
            name = refill(ob, v)
            vg = ob.vertex_groups.get(name) or ob.vertex_groups.new(name=name)
            vg.add([v.index], 1.0, "REPLACE")
        else:
            for vg, w in keep:
                vg.add([v.index], w / total, "REPLACE")
        n += 1
    return n


def empty(name, matrix=None, parent=None, bone=None):
    e = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(e)
    e.empty_display_size = .3
    if parent is not None:
        e.parent = parent
        if bone:
            e.parent_type = "BONE"
            e.parent_bone = bone
    bpy.context.view_layer.update()
    if matrix is not None:
        e.matrix_world = matrix
    return e


def tail_matrix(arm, bone):
    """World matrix at a bone's tail, with the bone's rest orientation (current pose)."""
    pb = arm.pose.bones[bone]
    m = arm.matrix_world @ pb.matrix
    t = arm.matrix_world @ pb.tail
    return Matrix.Translation(t) @ m.to_quaternion().to_matrix().to_4x4()


def ik(arm, bone, target, chain, rotation=True):
    c = arm.pose.bones[bone].constraints.new("IK")
    c.target = target
    c.chain_count = chain
    c.use_tail = True
    c.use_rotation = rotation
    c.use_stretch = False
    c.iterations = 500
    return c


def rig(arm):
    """IK targets: S hands on their rocks (world, fixed); C02 holds thigh L02; C01 holds thigh L01 through T_C01,
    which the clips animate (world) from the thigh to the lip and to the face."""
    t = {}
    for s, chain in (("S01", 4), ("S02", 4), ("S03", 5)):
        t[s] = empty("T_" + s, tail_matrix(arm, s + "_wrist_00"))
        ik(arm, s + "_wrist_00", t[s], chain)
    t["C02"] = empty("T_C02", parent=arm, bone="L02_seg01")
    t["C02"].matrix_world = tail_matrix(arm, "C02_wrist_00")
    ik(arm, "C02_wrist_00", t["C02"], 7)
    # Where C01's palm sits on thigh L01: an empty riding the thigh, read every frame to drive T_C01.
    t["C01_thigh"] = empty("T_C01_thigh", parent=arm, bone="L01_seg01")
    t["C01_thigh"].matrix_world = tail_matrix(arm, "C01_wrist_00")
    t["C01"] = empty("T_C01", tail_matrix(arm, "C01_wrist_00"))
    ik(arm, "C01_wrist_00", t["C01"], 8)
    bpy.context.view_layer.update()
    return t


def local_axis(arm, bone, axis):
    """An armature-space axis in the bone's rest space."""
    return (arm.data.bones[bone].matrix_local.to_3x3().inverted() @ Vector(axis)).normalized()


def rot(arm, bone, axis, deg):
    """Rotation of a bone about an armature-space axis (degrees), as its local quaternion."""
    return Quaternion(local_axis(arm, bone, axis), math.radians(deg))


def set_rot(arm, bone, *rots):
    q = Quaternion()
    for axis, deg in rots:
        q = rot(arm, bone, axis, deg) @ q
    arm.pose.bones[bone].rotation_quaternion = q


def move(arm, bone, delta):
    """Translate a bone by an armature-space offset."""
    arm.pose.bones[bone].location = arm.data.bones[bone].matrix_local.to_3x3().inverted() @ Vector(delta)


def smooth(x):
    x = min(1.0, max(0.0, x))
    return x * x * (3 - 2 * x)


def smoother(x):
    x = min(1.0, max(0.0, x))
    return x * x * x * (x * (x * 6 - 15) + 10)


def settle(x, overshoot=.12, wobbles=1.6):
    """0 -> 1 with a damped overshoot, like weight landing: x in 0..1 of the move."""
    x = max(0.0, x)
    if x >= 1.6:
        return 1.0
    base = smoother(min(x, 1.0))
    over = overshoot * math.sin(math.pi * wobbles * max(0.0, x - .55) / 1.05) * math.exp(-3.2 * max(0.0, x - .55)) if x > .55 else 0.0
    return base + over
