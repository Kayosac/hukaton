"""Shared setup for MG01's animation: open the test rig, clean it, add IK (support hands pinned to their rocks,
cradle hands holding the thighs, C01 free for the reach), helpers to pose bones about armature axes."""
import math
import os
import bpy
from mathutils import Vector, Quaternion, Matrix

SRC = "/mnt/user-data/uploads/hukaton/output/mountain-god-model-v3/rig_preview/MG01_rig_preview.blend"
FPS = 30
# The lip of the summit platform, in the god's own coordinates (Blender, 1 unit = 100 m): the scene places the god at
# (42.4, -400, 1666.3) turned 180°, scale 100; the lip is at (42.4, 13.2, 62.3) in the level.
LIP = Vector((0.0, -16.04, 4.132))
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
REBUILT = os.path.join(OUT, "MG01_Body_rebuilt.npz")  # rebuild/run.sh: the new skin (rest), its UVs and weights
REBUILT_TEX = os.path.join(OUT, "T_MG01_BodyAnim_BaseColor.png")  # its colour, baked from the old skin
FINGER_R = .165  # radius of C01's index finger near its tip, rebuilt skin (units)


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
    body = bpy.data.objects["MG01_Body"]
    if os.path.exists(REBUILT):
        # the skin rebuilt whole (rebuild/run.sh): torn where limbs come apart, every opening healed into smooth skin
        separate_limbs(body, arm, fill=False)
        use_rebuilt(body)
    else:
        separate_limbs(body, arm)
        smooth_body(body)
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


def separate_limbs(body, arm, keep_root=2.6, fill=True):
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
    bm.verts.layers.int.new("fill")  # before any vertex is held: adding a layer later re-wraps the vertices
    bm.verts.layers.int.new("fillno")
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
    wires = [e for e in bm.edges if not e.link_faces]
    if wires:
        bmesh.ops.delete(bm, geom=wires, context="EDGES")
    if ripped:
        print("separate_limbs: corners still shared by parts that move apart, ripped:", ripped)
    # each vertex now sits on one side: its faces' part (limb, and the bone most of those faces follow); kept in a
    # vertex layer, since the fills below re-wrap the vertices
    codes = []
    code_of = {}
    pl = bm.verts.layers.int.new("part")
    for v in bm.verts:
        c = Counter(part[f][:2] for f in v.link_faces if f in part)
        key = c.most_common(1)[0][0] if c else ("CORE", "")
        if key not in code_of:
            code_of[key] = len(codes)
            codes.append(key)
        v[pl] = code_of[key]
    caps = fill_openings(bm) if fill else 0
    bm.verts.index_update()
    pl = bm.verts.layers.int["part"]
    vside = [codes[v[pl]][0] for v in bm.verts]
    vsdom = [codes[v[pl]][1] for v in bm.verts]
    bm.to_mesh(me)
    bm.free()
    me.update()
    print("separate_limbs: tore", len(torn), "edges, filled", caps, "openings")
    if fill:
        fair_fills(body)

    # Weights: each side follows its own bones. A free limb's skin follows only its limb, and inside the limb only
    # bones near the vertex's own (a hand never follows its shoulder); the rest never follows a free limb, except right
    # at that limb's root joint.
    # the bone a vertex's faces follow decides (not the vertex's own heaviest weight: a fused corner can be heavier
    # on the part it was torn from)
    dom2 = vsdom
    # for the rebuild: each vertex's side, its bone, and whether it is a free limb's skin at its root joint
    separate_limbs.sides = (list(vside), list(vsdom), [vside[i] in FREE and vsdom[i] in root_set[vside[i]] and
                            (W @ me.vertices[i].co - roots[vside[i]]).length <= reach[vside[i]] for i in range(len(vside))])
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
    if fill:
        fixed += fill_weights(body)
    for name in ("MG01_Cloth", "MG01_Leaves"):
        ob = bpy.data.objects[name]
        fixed += clean_weights(ob, allb, lambda v, g, ob=ob: g.startswith(FREE) and
                               not (g in root_set[g[:3]] and (ob.matrix_world @ v.co - roots[g[:3]]).length <= reach[g[:3]]), refill)
    print("separate_limbs: weights cleaned on", fixed, "vertices")


def use_rebuilt(body, max_influences=4):
    """Swap the body's mesh for the rebuilt skin: smooth, closed, with its own UVs, material (M_MG01_BodyAnim, baked
    colour) and weights (at most four bones a vertex, as Unity skins)."""
    import numpy as np
    z = np.load(REBUILT)
    RV, RF, UV, WR = z["RV"], z["RF"], z["UV"], z["WR"]
    bones = [str(b) for b in z["bones"]]
    me = bpy.data.meshes.new("MG01_Body")
    me.from_pydata(RV.tolist(), [], RF.tolist())
    me.update()
    uv = me.uv_layers.new(name="UVMap")
    uv.data.foreach_set("uv", UV.ravel())
    me.polygons.foreach_set("use_smooth", [True] * len(me.polygons))
    mat = bpy.data.materials.get("M_MG01_BodyAnim") or bpy.data.materials.new("M_MG01_BodyAnim")
    mat.use_nodes = True
    nt = mat.node_tree
    tn = next((n for n in nt.nodes if n.type == "TEX_IMAGE"), None) or nt.nodes.new("ShaderNodeTexImage")
    tn.image = bpy.data.images.load(REBUILT_TEX, check_existing=True)
    nt.links.new(tn.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
    nt.nodes.active = tn
    me.materials.append(mat)
    old = body.data
    body.data = me
    bpy.data.meshes.remove(old)
    me.name = "MG01_Body"
    for vg in list(body.vertex_groups):
        body.vertex_groups.remove(vg)
    import bmesh
    top = np.argsort(-WR, axis=1)[:, :max_influences]
    w = np.take_along_axis(WR, top, 1)
    w[w < .01] = 0
    w /= w.sum(1, keepdims=True)
    used = sorted(set(top[w > 0].tolist()))
    gidx = {b: body.vertex_groups.new(name=bones[b]).index for b in used}
    bm = bmesh.new()
    bm.from_mesh(me)
    dl = bm.verts.layers.deform.verify()
    bm.verts.ensure_lookup_table()
    for v, bs, ws in zip(bm.verts, top.tolist(), w.tolist()):
        d = v[dl]
        for b, x in zip(bs, ws):
            if x > 0:
                d[gidx[b]] = x
    bm.to_mesh(me)
    bm.free()
    groups = gidx
    print("use_rebuilt:", len(RV), "verts,", len(RF), "faces,", len(groups), "bones")


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


def boundary_cycles(bm):
    """Every opening's rim as a list of vertices, walked against its faces' winding (a fill made in that order faces
    outward like its neighbours)."""
    nxt = {}
    for e in bm.edges:
        if e.is_boundary:
            l = next(l for l in e.link_faces[0].loops if l.edge == e)
            nxt.setdefault(l.link_loop_next.vert, []).append(l.vert)
    used, cycles = set(), []
    for start in list(nxt):
        for first in nxt[start]:
            if (start, first) in used:
                continue
            cyc, cur, nv, ok = [start], start, first, True
            while True:
                used.add((cur, nv))
                if nv is start:
                    break
                cyc.append(nv)
                cand = [x for x in nxt.get(nv, []) if (nv, x) not in used]
                if not cand or len(cyc) > 20000:
                    ok = False
                    break
                cur, nv = nv, cand[0]
            if ok and len(cyc) >= 3:
                cycles.append(cyc)
    return cycles


FILLS = []  # (centre, outward normal, radius) of every filled opening, for checks


def fill_openings(bm):
    """Rebuild the skin where a limb came away: every opening is filled with real surface, not a lid. Rings of new
    vertices step in from the rim to its middle about one face apart, joined into triangles wound like the faces
    around them; `fair_fills` then bends all of it into a smooth continuation of the skin around. New vertices take
    their part and weights from the rim (inverse distance)."""
    import math
    from collections import Counter
    mark = bm.verts.layers.int["fill"]
    pl = bm.verts.layers.int["part"]
    fno = bm.verts.layers.int["fillno"]
    deform = bm.verts.layers.deform.active
    filled = failed = 0
    simple = []
    for cyc in boundary_cycles(bm):
        # a rim passing twice through one corner is two rims touching there: fill each on its own
        stack = [cyc]
        while stack:
            cy = stack.pop()
            seen = {}
            for i, v in enumerate(cy):
                if v in seen:
                    j = seen[v]
                    stack += [cy[j:i], cy[i:] + cy[:j]]
                    break
                seen[v] = i
            else:
                if len(cy) >= 3:
                    simple.append(cy)
    for cyc in simple:
        if len(set(cyc)) != len(cyc):
            failed += 1
            continue
        pts = [v.co.copy() for v in cyc]
        c = sum(pts, pts[0] * 0) / len(pts)
        n = len(cyc)
        seg = [(pts[(i + 1) % n] - pts[i]).length for i in range(n)]
        per = sum(seg)
        L = per / n
        R = max((p - c).length for p in pts)
        k = max(0, min(14, math.ceil(R / max(L, 1e-6)) - 1))
        rim_code = Counter(v[pl] for v in cyc).most_common(1)[0][0]
        rim_info = [(v.co.copy(), dict(v[deform].items()) if deform else {}) for v in cyc]
        # arc-length parameter of every rim corner; inner rings sample the rim shrunk toward its middle, with fewer
        # corners as they shrink, so the triangles stay about even (no fan of slivers into a pole)
        par = [0.0]
        for i in range(n - 1):
            par.append(par[-1] + seg[i] / per)

        def rim_at(sv):
            sv %= 1.0
            i = max(j for j in range(n) if par[j] <= sv + 1e-12)
            j = (i + 1) % n
            span = seg[i] / per
            f = 0.0 if span <= 0 else (sv - par[i]) / span
            return pts[i].lerp(pts[j], min(1.0, max(0.0, f)))
        rings = [(list(cyc), list(par))]
        new = []
        for j in range(1, k + 1):
            t = j / (k + 1)
            m = max(3, round(n * (1 - t)))
            ps = [(q + .5 * (j % 2)) / m for q in range(m)]
            ring = [bm.verts.new(rim_at(sv).lerp(c, t)) for sv in ps]
            new += ring
            rings.append((ring, ps))
        mid = bm.verts.new(c)
        new.append(mid)

        def face(tri):
            try:
                bm.faces.new(tri)
            except ValueError:
                pass
        for (A, pa), (B, pb) in zip(rings[:-1], rings[1:]):
            # zip two rings by parameter: always advance the side whose next corner comes first
            ia = ib = 0
            na, nb = len(A), len(B)
            a0 = min(range(na), key=lambda q: pa[q])
            b0 = min(range(nb), key=lambda q: (pb[q] - pa[a0]) % 1.0)
            while ia < na or ib < nb:
                ca, cb = A[(a0 + ia) % na], B[(b0 + ib) % nb]
                sa = pa[(a0 + ia + 1) % na] + (1.0 if a0 + ia + 1 >= na else 0.0)
                sb = pb[(b0 + ib + 1) % nb] + (1.0 if b0 + ib + 1 >= nb else 0.0)
                if ib >= nb or (ia < na and sa <= sb):
                    face((ca, A[(a0 + ia + 1) % na], cb))
                    ia += 1
                else:
                    face((ca, B[(b0 + ib + 1) % nb], cb))
                    ib += 1
        last = rings[-1][0]
        for i in range(len(last)):
            face((last[i], last[(i + 1) % len(last)], mid))
        for v in cyc:
            v[mark] = 3
            v[fno] = filled + 1
        FILLS.append((c.copy(), (sum(((pts[i] - c).cross(pts[(i + 1) % n] - c) for i in range(n)), c * 0)).normalized(), R))
        for v in new:
            v[mark] = 1
            v[fno] = filled + 1
            v[pl] = rim_code
            if deform:
                near = sorted(rim_info, key=lambda r: (r[0] - v.co).length)[:6]
                acc, tot = {}, 0.0
                for co, ws in near:
                    kk = 1.0 / max((co - v.co).length, 1e-4) ** 2
                    tot += kk
                    for g, wt in ws.items():
                        acc[g] = acc.get(g, 0) + wt * kk
                for g, wt in acc.items():
                    v[deform][g] = wt / tot
        filled += 1
    if failed:
        print("fill_openings: rims passing twice through a corner, not filled:", failed)
    return filled


def _in_poly(q, poly):
    inside = False
    for i in range(len(poly)):
        a, b = poly[i - 1], poly[i]
        if (a.y > q.y) != (b.y > q.y):
            x = a.x + (q.y - a.y) * (b.x - a.x) / (b.y - a.y)
            if x > q.x:
                inside = not inside
    return inside


def _seg_dist(q, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (q - a).dot(ab) / max(ab.length_squared, 1e-12)))
    return (q - (a + ab * t)).length


def fill_weights(body, near=6):
    """New skin follows its rim as the rim finally follows its bones (after the cleaning): inverse-distance weights
    from the nearest rim corners of the same opening, so a fill never pulls toward a bone its rim has let go of."""
    me = body.data
    fill = me.attributes["fill"].data
    fno = me.attributes["fillno"].data
    rims = {}
    for v in me.vertices:
        if fill[v.index].value == 3:
            rims.setdefault(fno[v.index].value, []).append(v)
    gname = {vg.index: vg for vg in body.vertex_groups}
    n = 0
    for v in me.vertices:
        if fill[v.index].value != 1:
            continue
        rim = rims.get(fno[v.index].value)
        if not rim:
            continue
        rs = sorted(rim, key=lambda r: (r.co - v.co).length)[:near]
        acc, tot = {}, 0.0
        for r in rs:
            k = 1.0 / max((r.co - v.co).length, 1e-4) ** 2
            tot += k
            for g in r.groups:
                acc[g.group] = acc.get(g.group, 0) + g.weight * k
        for g in list(v.groups):
            gname[g.group].remove([v.index])
        for gi, w in acc.items():
            if w / tot > 1e-4:
                gname[gi].add([v.index], w / tot, "REPLACE")
        n += 1
    return n


def fair_fills(body, rings=0):
    """Shape the filled skin into the fairest surface that meets the skin around it with the same slope: the new
    vertices, the rim and `rings` rings of old skin around it move, two more rings stay put and set the slope, and the
    moving vertices solve the thin-plate (bi-Laplacian) equation. The seam melts away instead of showing a lid."""
    import numpy as np
    import scipy.sparse as sp
    import scipy.sparse.linalg as spla
    me = body.data
    n = len(me.vertices)
    fill = me.attributes["fill"].data
    flag = np.array([fill[i].value for i in range(n)])
    nbr = [[] for _ in range(n)]
    for e in me.edges:
        a, b = e.vertices
        nbr[a].append(b)
        nbr[b].append(a)
    free = set(np.nonzero((flag == 1) | (flag == 3))[0].tolist())
    for _ in range(rings):
        free |= {j for i in list(free) for j in nbr[i] if flag[j] != 1}
    if not free:
        return
    # the Laplacian over the free vertices and two rings around them
    region = set(free)
    for _ in range(2):
        region |= {j for i in list(region) for j in nbr[i]}
    idx = {v: k for k, v in enumerate(sorted(region))}
    rows, cols, vals = [], [], []
    for v, k in idx.items():
        ns = [j for j in nbr[v] if j in idx]
        if not ns:
            continue
        rows.append(k); cols.append(k); vals.append(1.0)
        for j in ns:
            rows.append(k); cols.append(idx[j]); vals.append(-1.0 / len(ns))
    m = len(idx)
    Lap = sp.csr_matrix((vals, (rows, cols)), shape=(m, m))
    L2 = (Lap @ Lap).tocsr()
    fk = np.array([idx[v] for v in sorted(free)])
    mask = np.zeros(m, bool)
    mask[fk] = True
    X = np.array([me.vertices[v].co[:] for v in sorted(region)])
    A = L2[fk][:, mask]
    B = L2[fk][:, ~mask]
    rhs = -B @ X[~mask]
    sol = np.column_stack([spla.spsolve(A.tocsc(), rhs[:, c]) for c in range(3)])
    order = sorted(region)
    free_sorted = [order[k] for k in np.nonzero(mask)[0]]
    for v, co in zip(free_sorted, sol):
        me.vertices[v].co = co
    me.update()
    print("fair_fills: shaped", int((flag == 1).sum()), "new vertices and", len(free) - int((flag == 1).sum()), "around them")


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
