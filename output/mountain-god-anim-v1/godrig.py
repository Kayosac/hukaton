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
FINGER_R = .14  # radius of C01's index finger near its tip, after smoothing (units)


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
    smooth_body(bpy.data.objects["MG01_Body"])
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
LIMB_PREFIXES = ("S01", "S02", "S03", "C01", "C02", "A01", "A02", "A03", "A04", "L01", "L02", "L03", "L04")


def vside_dom_limb(bone):
    """True when a bone belongs to a limb chain (not the torso)."""
    return bool(bone) and bone.startswith(LIMB_PREFIXES)


FAR_HOPS = 5  # bones further apart than this in the skeleton never share a face or a vertex inside a free limb (a hand
# lying on its own shoulder); closer ones are a joint (the small arms' short wrists put the fingers 4 bones from the
# elbow), which must stay sewn


def root_bone(arm, limb):
    """The bone a limb grows from the torso with: an arm's seg00, a leg's hip."""
    return limb + "_seg00" if limb + "_seg00" in arm.data.bones else limb + "_hip"


def separate_limbs(body, arm, keep_root=2.6):
    """The body is one fused mesh: C01's palm grows into thigh L01, C02 hugs L02's knee and lies on its own shoulder,
    the small arms touch the head and C02, the ordinary legs touch each other and the belly. Where a free limb (both C
    arms, A01-A04, the dropping legs L01 and L02) is fused to anything but its own root, the skin is split along face
    edges (nothing is deleted): every face goes to the part most of its corners belong to, and edges between parts
    that will move apart are torn. Each side's opening is closed by a domed cap wound like its neighbours (so it faces
    out and is never culled), and the weights of each side follow only that side's bones."""
    import bmesh
    from collections import Counter
    LIMBS = ("CORE", "HEAD", "S01", "S02", "S03", "C01", "C02", "A01", "A02", "A03", "A04", "L01", "L02", "L03", "L04")
    W = body.matrix_world
    me = body.data
    gname = {vg.index: vg.name for vg in body.vertex_groups}
    bones = arm.data.bones
    vlimb, vdom = [], []
    for v in me.vertices:
        vlimb.append(max(((g.weight, gname[g.group]) for g in v.groups if gname[g.group] in LIMBS), default=(0, "CORE"))[1])
        vdom.append(max(((g.weight, gname[g.group]) for g in v.groups if gname[g.group] in bones), default=(0, ""))[1])
    roots = {n: arm.matrix_world @ bones[root_bone(arm, n)].head_local for n in FREE}
    # how far from its root a limb may stay joined to the torso: the shoulders are broad, the hips tight (the raised
    # thigh of L02 lies on the belly well above the hip and must come away when the leg drops)
    reach = {n: (1.3 if n.startswith("L") else keep_root) for n in FREE}
    root_set = {n: {n + "_seg00", n + "_shoulder", n + "_hip"} for n in FREE}
    hops = bone_hops(arm)

    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    part = {}
    for f in bm.faces:
        lim = Counter(vlimb[v.index] for v in f.verts).most_common(1)[0][0]
        dom = Counter(vdom[v.index] for v in f.verts if vlimb[v.index] == lim).most_common(1)[0][0]
        part[f] = (lim, dom, W @ f.calc_center_median())

    def joint(lim, dom, c):
        return lim in FREE and dom in root_set[lim] and (c - roots[lim]).length <= reach[lim]

    def tear(fa, fb):
        (la, da, ca), (lb, db, cb) = part[fa], part[fb]
        if (da == "beard_00") != (db == "beard_00"):
            # the beard hangs on the chest: it swings on its spring and nods with the head
            return (db if da == "beard_00" else da) not in ("head_00", "neck_00", "Neck", "hat_00")
        if la not in FREE and lb not in FREE:
            return False
        if la == lb:
            return bool(da) and bool(db) and hops(da, db) > FAR_HOPS
        if la in FREE and lb in FREE:
            return not (joint(la, da, ca) and joint(lb, db, cb))
        free, fd, fc, other = (la, da, ca, lb) if la in FREE else (lb, db, cb, la)
        return other not in ("CORE", "HEAD") or not joint(free, fd, fc)

    torn = {e for e in bm.edges if len(e.link_faces) == 2 and tear(*e.link_faces)}
    # a few stray faces cut off on their own (a fingertip lying on a thigh, a corner of a palm) would float or drag
    # strings: they join the part around them instead, and the tears are worked out again
    for _ in range(4):
        parent = {f: f for f in bm.faces}

        def find(f):
            while parent[f] is not f:
                parent[f] = parent[parent[f]]
                f = parent[f]
            return f
        for e in bm.edges:
            if e not in torn and len(e.link_faces) == 2:
                x, y = find(e.link_faces[0]), find(e.link_faces[1])
                if x is not y:
                    parent[x] = y
        groups = {}
        for f in bm.faces:
            groups.setdefault(find(f), []).append(f)
        moved = 0
        for g in groups.values():
            if len(g) >= 60:
                continue
            gs = set(g)
            around = Counter(part[h][:2] for f in g for e in f.edges if e in torn for h in e.link_faces if h not in gs)
            if not around:
                continue
            lim, dom = around.most_common(1)[0][0]
            for f in g:
                part[f] = (lim, dom, part[f][2])
            moved += len(g)
        if not moved:
            break
        print("separate_limbs: stray faces joined the part around them:", moved)
        torn = {e for e in bm.edges if len(e.link_faces) == 2 and tear(*e.link_faces)}
    bmesh.ops.split_edges(bm, edges=list(torn))
    ripped = rip_incompatible(bm, part, tear)
    if ripped:
        print("separate_limbs: corners still shared by parts that move apart, ripped:", ripped)
    # each vertex now sits on one side: its faces' part (limb, and the bone most of those faces follow)
    side, sdom = {}, {}
    for v in bm.verts:
        c = Counter(part[f][:2] for f in v.link_faces if f in part)
        (side[v], sdom[v]) = c.most_common(1)[0][0] if c else ("CORE", "")
    caps = cap_openings(bm, side, sdom)
    bm.verts.index_update()
    global CAP_IDX
    CAP_IDX = {c.index for c in CAP_CENTERS}
    vside = [side[v] for v in bm.verts]
    vsdom = [sdom[v] for v in bm.verts]
    bm.to_mesh(me)
    bm.free()
    me.update()
    print("separate_limbs: tore", len(torn), "edges, capped", caps, "openings")

    # Weights: each side follows its own bones. A free limb's skin follows only its limb, and inside the limb only
    # bones near the vertex's own (a hand never follows its shoulder); the rest never follows a free limb, except right
    # at that limb's root joint.
    # the bone a vertex's faces follow decides (not the vertex's own heaviest weight: a fused corner can be heavier
    # on the part it was torn from)
    dom2 = vsdom
    def drop(v, g):
        own = vside[v.index]
        p = body.matrix_world @ v.co
        if own in FREE:
            if g.startswith(own + "_"):
                return dom2[v.index] in bones and hops(g, dom2[v.index]) > FAR_HOPS
            if g.startswith(FREE) or g.startswith(LIMB_PREFIXES):
                return True  # never another limb, not even at a joint: only the torso blends into a root
            return not (dom2[v.index] in root_set[own] and (p - roots[own]).length <= reach[own])
        if dom2[v.index] == "beard_00":
            return g not in ("beard_00", "head_00", "neck_00", "Neck")
        if g == "beard_00":
            return True
        if g.startswith(FREE) and (own != "CORE" or vside_dom_limb(dom2[v.index])):
            return True  # another limb's skin never follows a free limb
        if g.startswith(FREE):
            lim = g[:3]
            return not (g in root_set[lim] and (p - roots[lim]).length <= reach[lim])
        return False

    def segs(names):
        return [(n, arm.matrix_world @ bones[n].head_local, arm.matrix_world @ bones[n].tail_local) for n in names]

    def nearest(p, cands):
        best, bd = None, 1e18
        for n, a, b in cands:
            ab = b - a
            t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
            d = (p - (a + ab * t)).length
            if d < bd:
                best, bd = n, d
        return best

    own_bones = {n: segs([b.name for b in bones if b.name.startswith(n + "_")]) for n in FREE}
    body_bones = segs([b.name for b in bones if not b.name.startswith(FREE)])

    def refill(ob, v):
        p = ob.matrix_world @ v.co
        if ob == body and vside[v.index] in FREE:
            d = vsdom[v.index]
            near = [s for s in own_bones[vside[v.index]] if not d or hops(s[0], d) <= FAR_HOPS]
            return nearest(p, near or own_bones[vside[v.index]])
        return nearest(p, body_bones)

    allb = set(b.name for b in bones)
    fixed = clean_weights(body, allb, drop, refill)
    for name in ("MG01_Cloth", "MG01_Leaves"):
        ob = bpy.data.objects[name]
        fixed += clean_weights(ob, allb, lambda v, g, ob=ob: g.startswith(FREE) and
                               not (g in root_set[g[:3]] and (ob.matrix_world @ v.co - roots[g[:3]]).length <= reach[g[:3]]), refill)
    print("separate_limbs: weights cleaned on", fixed, "vertices")


def smooth_body(body, levels=1):
    """The model is low-poly and flat-shaded: up close (the player rides the finger) its 400 m hand breaks into huge
    facets. One Catmull-Clark level (weights interpolate) and smooth shading make it a whole, round skin."""
    vl = bpy.context.view_layer
    for o in bpy.context.scene.objects:
        if o:
            o.select_set(o == body)
    vl.objects.active = body
    mod = body.modifiers.new("Smooth", "SUBSURF")
    mod.levels = mod.render_levels = levels
    mod.uv_smooth = "PRESERVE_BOUNDARIES"
    mod.boundary_smooth = "ALL"
    while body.modifiers[0] != mod:
        bpy.ops.object.modifier_move_up(modifier=mod.name)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    for p in body.data.polygons:
        p.use_smooth = True
    body.data.update()
    print("smooth_body:", len(body.data.vertices), "verts,", len(body.data.polygons), "faces")


def rip_incompatible(bm, part, tear):
    """Where a tear ends inside the skin, the corner at its end is still shared by faces that must come apart (a thumb
    tip lying on a hip): give each group of such faces its own copy of the corner (same place, weights, UVs)."""
    deform = bm.verts.layers.deform.active
    uvs = list(bm.loops.layers.uv.values())
    n = 0
    for v in list(bm.verts):
        faces = [f for f in v.link_faces if f in part]
        if len(faces) < 2:
            continue
        # groups of faces that may stay together
        groups = []
        for f in faces:
            for g in groups:
                if not any(tear(f, h) for h in g):
                    g.append(f)
                    break
            else:
                groups.append([f])
        if len(groups) < 2:
            continue
        groups.sort(key=len, reverse=True)
        for g in groups[1:]:
            v2 = bm.verts.new(v.co)
            if deform:
                for k, w in v[deform].items():
                    v2[deform][k] = w
            for f in g:
                verts = [v2 if x is v else x for x in f.verts]
                old_uv = [[l[u].uv.copy() for u in uvs] for l in f.loops]
                try:
                    nf = bm.faces.new(verts, f)
                except ValueError:
                    continue
                for l, uvl in zip(nf.loops, old_uv):
                    for u, val in zip(uvs, uvl):
                        l[u].uv = val
                part[nf] = part[f]
                bm.faces.remove(f)
                del part[f]
            n += 1
    return n


def cap_openings(bm, side, sdom):
    """Close every opening: one fan per rim, wound against each rim edge's own face (so the cap faces out like its
    neighbours), its middle pushed out into a low dome. The middle vertex takes the rim's averaged weights."""
    import bmesh
    deform = bm.verts.layers.deform.active
    rim = [e for e in bm.edges if e.is_boundary]
    adj = {}
    for e in rim:
        for v in e.verts:
            adj.setdefault(v, []).append(e)
    seen, groups = set(), []
    for e0 in rim:
        if e0 in seen:
            continue
        comp, stack = [], [e0]
        while stack:
            e = stack.pop()
            if e in seen:
                continue
            seen.add(e)
            comp.append(e)
            for v in e.verts:
                stack.extend(x for x in adj[v] if x not in seen)
        groups.append(comp)
    for comp in groups:
        verts = list({v for e in comp for v in e.verts})
        mid = sum((v.co for v in verts), verts[0].co * 0) / len(verts)
        c = bm.verts.new(mid)
        CAP_CENTERS.append(c)
        side[c] = Counter_most(side[v] for v in verts)
        sdom[c] = Counter_most(sdom[v] for v in verts)
        if deform:
            acc = {}
            for v in verts:
                for g, w in v[deform].items():
                    acc[g] = acc.get(g, 0) + w / len(verts)
            for g, w in acc.items():
                c[deform][g] = w
        faces = []
        for e in comp:
            f0 = e.link_faces[0]
            lp = next(l for l in f0.loops if l.edge == e)
            a, b = lp.vert, lp.link_loop_next.vert
            try:
                faces.append(bm.faces.new((b, a, c)))
            except ValueError:
                pass
        if not faces:
            continue
        for f in faces:
            f.normal_update()
        n = sum((f.normal for f in faces), faces[0].normal * 0)
        if n.length > 1e-9:
            r = sum((v.co - mid).length for v in verts) / len(verts)
            c.co = mid + n.normalized() * r * .35
    return len(groups)


CAP_CENTERS = []


def Counter_most(it):
    from collections import Counter
    return Counter(it).most_common(1)[0][0]


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
    # no twist inside the chain: a long limb solved with roll folds its skin like a sweet wrapper. The hand and the
    # chain's root may still turn about their own length, so the hand can still face where it must.
    b = arm.pose.bones[bone].parent
    for _ in range(chain - 2):
        if b is None:
            break
        b.lock_ik_y = True
        b = b.parent
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
