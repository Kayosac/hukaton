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


FREE = ("C01", "C02", "A01", "A02", "A03", "A04", "L01", "L02")
FAR_HOPS = 5  # bones further apart than this in the skeleton never share a face or a vertex inside a free limb (a hand
# lying on its own shoulder); closer ones are a joint (the small arms' short wrists put the fingers 4 bones from the
# elbow), which must stay sewn


def root_bone(arm, limb):
    """The bone a limb grows from the torso with: an arm's seg00, a leg's hip."""
    return limb + "_seg00" if limb + "_seg00" in arm.data.bones else limb + "_hip"


def separate_limbs(body, arm, keep_root=2.6):
    """The body is one fused mesh: C01's palm grows into thigh L01, C02 hugs L02's knee, the small arms touch the head
    and C02, the ordinary legs touch each other and the belly. Faces that join a free limb (both C arms, A01–A04, the
    legs L01 and L02 that drop) to anything but its own root on the torso would stretch into strings when the limb
    moves, so they are cut out and both sides capped."""
    import bmesh
    limbs = [vg for vg in body.vertex_groups if vg.name in ("CORE", "HEAD", "S01", "S02", "S03", "C01", "C02", "A01", "A02",
                                                              "A03", "A04", "L01", "L02", "L03", "L04")]
    idx = {vg.index: vg.name for vg in limbs}
    me = body.data
    limb = []
    for v in me.vertices:
        best = max(((g.weight, idx[g.group]) for g in v.groups if g.group in idx), default=(0, "CORE"))
        limb.append(best[1])
    roots = {n: arm.matrix_world @ arm.data.bones[root_bone(arm, n)].head_local for n in FREE}
    # how far from its root a limb may stay joined to the torso: the shoulders are broad, the hips tight (the raised
    # thigh of L02 lies on the belly well above the hip and must come away when the leg drops)
    reach = {n: (1.3 if n.startswith("L") else keep_root) for n in FREE}
    root_set = {n: {n + "_seg00", n + "_shoulder", n + "_hip"} for n in FREE}
    gname = {vg.index: vg.name for vg in body.vertex_groups}
    dom = []
    for v in me.vertices:
        best = max(((g.weight, gname[g.group]) for g in v.groups if gname[g.group] in arm.data.bones), default=(0, ""))
        dom.append(best[1])
    hops = bone_hops(arm)
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
        elif (body.matrix_world @ f.calc_center_median() - roots[n]).length > reach[n]:
            cut.append(f)  # touches the torso away from its own root (C01 under the pelvis, L02's thigh on the belly)
        elif any(limb[v.index] == n and dom[v.index] not in root_set[n] for v in f.verts):
            cut.append(f)  # near the root, but it is the hand or the thigh lying on the torso, not the joint itself
    # inside a free limb: a hand lying on its own shoulder (C02's fingers rest on its upper arm) is fused too
    far_cut = {}
    for f in bm.faces:
        if f in cut:
            continue
        s = {limb[v.index] for v in f.verts}
        if len(s) == 1 and next(iter(s)) in FREE:
            ds = [dom[v.index] for v in f.verts if dom[v.index]]
            if any(hops(a, b) > FAR_HOPS for a in ds for b in ds):
                cut.append(f)
                far_cut[next(iter(s))] = far_cut.get(next(iter(s)), 0) + 1
    limb_of = {v: limb[v.index] for v in bm.verts}
    bmesh.ops.delete(bm, geom=cut, context="FACES")
    capped = cap_holes(bm, limb_of)
    bm.to_mesh(me)
    bm.free()
    me.update()
    print("separate_limbs: cut", len(cut), "faces, capped", capped, "openings; inside limbs", far_cut)
    # Deleting faces drops their loose vertices: indices moved, so read the limbs again.
    limb = []
    for v in me.vertices:
        best = max(((g.weight, idx[g.group]) for g in v.groups if g.group in idx), default=(0, "CORE"))
        limb.append(best[1])
    gname2 = {vg.index: vg.name for vg in body.vertex_groups}
    dom2 = [max(((g.weight, gname2[g.group]) for g in v.groups if gname2[g.group] in arm.data.bones), default=(0, ""))[1]
            for v in me.vertices]
    # Weights: a free limb's skin follows only its own bones (away from its root); cloth never follows a free limb.
    bones = set(b.name for b in arm.data.bones)
    def drop(v, gname):
        own = limb[v.index]
        if own in FREE:
            if gname.startswith(own + "_"):
                return dom2[v.index] in arm.data.bones and hops(gname, dom2[v.index]) > FAR_HOPS
            joint = dom2[v.index] in root_set[own]
            return not joint or (body.matrix_world @ v.co - roots[own]).length > reach[own]
        # the rest of the body never follows a free limb, except right at that limb's root
        if gname.startswith(FREE):
            return (body.matrix_world @ v.co - roots[gname[:3]]).length > reach[gname[:3]]
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
        ob = bpy.data.objects[name]
        fixed += clean_weights(ob, bones, lambda v, gname, ob=ob: gname.startswith(FREE) and
                               (ob.matrix_world @ v.co - roots[gname[:3]]).length > reach[gname[:3]], refill)
    print("separate_limbs: weights cleaned on", fixed, "vertices")


def bone_hops(arm):
    """hops(a, b): number of parent links between two bones in the skeleton."""
    par = {b.name: (b.parent.name if b.parent else None) for b in arm.data.bones}
    def chain(n):
        out = []
        while n:
            out.append(n)
            n = par[n]
        return out
    cache = {}
    def hops(a, b):
        if a == b:
            return 0
        k = (a, b) if a < b else (b, a)
        if k not in cache:
            ca, cb = chain(a), chain(b)
            ib = {n: i for i, n in enumerate(cb)}
            cache[k] = next((i + ib[n] for i, n in enumerate(ca) if n in ib), 99)
        return cache[k]
    return hops


def cap_holes(bm, limb_of):
    """Close the openings left by the cut. An opening's rim can run over two limbs (a finger's side and the torso's
    side of the same cut): each limb's stretch of the rim is closed on its own by a fan around its middle, so no cap
    ever bridges two limbs. The middle vertex takes the rim's averaged weights."""
    import bmesh
    deform = bm.verts.layers.deform.active
    rim = {}
    for e in bm.edges:
        if e.is_boundary:
            for v in e.verts:
                rim.setdefault(v, []).append(e)
    seen = set()
    loops = []
    for v0 in rim:
        if v0 in seen:
            continue
        loop, v, prev = [], v0, None
        while v not in seen:
            seen.add(v)
            loop.append(v)
            nxt = [e.other_vert(v) for e in rim[v] if e.other_vert(v) is not prev]
            nxt = [w for w in nxt if w not in seen] or nxt
            if not nxt:
                break
            prev, v = v, nxt[0]
        if len(loop) >= 3:
            loops.append(loop)
    new_faces = []
    for loop in loops:
        names = [limb_of.get(v, "CORE") for v in loop]
        if len(set(names)) == 1:
            runs = [loop]
        else:
            k = next(i for i in range(len(loop)) if names[i] != names[i - 1])
            loop, names = loop[k:] + loop[:k], names[k:] + names[:k]
            runs, cur = [], [loop[0]]
            for v, n, pn in zip(loop[1:], names[1:], names[:-1]):
                if n == pn:
                    cur.append(v)
                else:
                    runs.append(cur)
                    cur = [v]
            runs.append(cur)
        for run in runs:
            if len(run) < 3:
                continue
            c = bm.verts.new(sum((v.co for v in run), run[0].co * 0) / len(run))
            limb_of[c] = limb_of.get(run[0], "CORE")
            if deform:
                acc = {}
                for v in run:
                    for g, w in v[deform].items():
                        acc[g] = acc.get(g, 0) + w / len(run)
                for g, w in acc.items():
                    c[deform][g] = w
            ring = run + [run[0]] if len(set(limb_of.get(v) for v in run)) == 1 else run
            for a, b in zip(ring[:-1], ring[1:]):
                try:
                    new_faces.append(bm.faces.new((a, b, c)))
                except ValueError:
                    pass
    if new_faces:
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    return len(loops)


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
    """IK targets: S hands on their rocks (world, fixed; the chains reach to the arm roots so the god can stand up
    tall); C01 and C02 hold thighs L01 and L02 through world targets that the clips drive: from the thigh helpers
    (riding the thighs) to wherever the hands go."""
    t = {}
    for s, chain in (("S01", 5), ("S02", 5), ("S03", 6)):
        t[s] = empty("T_" + s, tail_matrix(arm, s + "_wrist_00"))
        ik(arm, s + "_wrist_00", t[s], chain)
    # Where C02's palm sits on thigh L02 (riding it), and the target the clips drive (world): it lets go too.
    t["C02_thigh"] = empty("T_C02_thigh", parent=arm, bone="L02_seg01")
    t["C02_thigh"].matrix_world = tail_matrix(arm, "C02_wrist_00")
    t["C02"] = empty("T_C02", tail_matrix(arm, "C02_wrist_00"))
    ik(arm, "C02_wrist_00", t["C02"], 7)
    # Where C01's palm sits on thigh L01: an empty riding the thigh, read every frame to drive T_C01.
    t["C01_thigh"] = empty("T_C01_thigh", parent=arm, bone="L01_seg01")
    t["C01_thigh"].matrix_world = tail_matrix(arm, "C01_wrist_00")
    t["C01"] = empty("T_C01", tail_matrix(arm, "C01_wrist_00"))
    ik(arm, "C01_wrist_00", t["C01"], 8)
    # the palm resists bending at the wrist: when the long arm has to fold, the elbow and the forearm take it
    pb = arm.pose.bones["C01_wrist_00"]
    pb.ik_stiffness_x = pb.ik_stiffness_y = pb.ik_stiffness_z = .97
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
