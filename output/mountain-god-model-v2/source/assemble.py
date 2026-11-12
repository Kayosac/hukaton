"""MG01 full assembly: all parts -> exact boolean union body + cloth + leaves, groups, UV, paint, guides.
Run: python3 -I /home/claude/work/mg/assemble.py OUTDIR [--fast]
"""
import sys, os, math, json, time
sys.path.insert(0, '/home/claude/work/mg')
import bpy, bmesh
from mathutils import Vector as V, Euler, Matrix
from mathutils.bvhtree import BVHTree
import core, bl, layout as L, rig

OUT = sys.argv[1] if len(sys.argv) > 1 and not sys.argv[1].startswith('--') else '/home/claude/work/out/assembly'
FAST = '--fast' in sys.argv
ORGANIC = '--hard' not in sys.argv  # v2: organic voxel body with fillets (default)
os.makedirs(OUT, exist_ok=True)
LOG = {}


def limb(lid):
    return next(l for l in L.LIMBS if l['id'] == lid)


def last_radius(lid):
    r = limb(lid)['radii'][-1]
    return max(r) if isinstance(r, (tuple, list)) else r


# ------------------------------------------------------------------ parts
def base_shells():
    sh = []
    t, _ = core.tube('CORE', L.TORSO['pts'], L.TORSO['radii'], sides=10, samples=3, up_hint=(1, 0, 0), group='CORE', seed=1,
                     ring_jitter=0.05)
    sh.append(t)
    h, _ = core.tube('HUB', L.HUB['pts'], L.HUB['radii'], sides=8, samples=2, group='CORE', seed=2)
    sh.append(h)
    sm = core.blob('SHOULDER', L.SHOULDER['centre'], L.SHOULDER['radii'], rings=6, sides=10, jitter=0.03, group='CORE', seed=4,
                   rot=Euler([math.radians(a) for a in L.SHOULDER['tilt_deg']]).to_matrix())
    sh.append(sm)
    if hasattr(L, 'HUB_MASS'):
        sh.append(core.blob('HUB_MASS', L.HUB_MASS['centre'], L.HUB_MASS['radii'], rings=7, sides=12, jitter=0.04,
                            group='CORE', seed=6))
    sb, _ = core.tube('SHOULDER_BAR', L.SHOULDER_BAR['pts'], L.SHOULDER_BAR['radii'], sides=6, samples=2, group='CORE', seed=5)
    sh.append(sb)
    for i, lb in enumerate(L.LIMBS):
        big = lb['kind'] in ('cradle', 'support')
        s, _ = core.tube(lb['id'], lb['pts'], lb['radii'], sides=lb['sides'], samples=lb['samples'], group=lb['id'], seed=10 + i,
                         ring_jitter=0.15 if big else 0.05, jitter=0.085 if big else 0.06)
        sh.append(s)
    return sh


def hand_shells():
    import hands
    shells, joints, eyes = [], {}, {}
    for i, (hid, d) in enumerate(L.SUPPORT_HANDS.items()):
        r = hands.support_hand(hid, d['wrist'], d['knuckles'], d['tips'], d['thumb_index'], last_radius(hid), seed=100 + i)
        shells += r['shells']; joints[hid] = r['joints']
        if r.get('eye'):
            eyes[d['eye']] = (r['eye'], hid)
    for i, (hid, d) in enumerate(L.CRADLE_HANDS.items()):
        caps = [(a, b, r0 + 0.06, r1 + 0.06) for a, b, r0, r1 in hands._default_body()]  # small gap: hand grips, not welded
        r = hands.cradle_hand(hid, d['wrist'], d['palm'], d['knuckles'], d['tips'], d['thumb'], last_radius(hid), seed=110 + i, body=caps)
        LOG.setdefault('hand_fixes', {})[hid] = r.get('fixes')
        shells += r['shells']; joints[hid] = r['joints']
    for i, (hid, d) in enumerate(L.SMALL_HANDS.items()):
        if d['style'] == 'open':
            r = hands.open_hand(hid, d['wrist'], d['palm'], V(d['normal']), d['tips'], d['thumb'], last_radius(hid), seed=120 + i)
            if r.get('eye'):
                eyes[d['eye']] = (r['eye'], hid)
        else:
            r = hands.claw_hand(hid, d['wrist'], d['palm'], d['tips'], d['thumb'], last_radius(hid) * 1.6, seed=120 + i)  # claws thick enough to survive the voxel pass
        shells += r['shells']; joints[hid] = r['joints']
    for i, (lid, d) in enumerate(L.FEET.items()):
        r = hands.foot(lid, d['ankle'], d['toe'], V(d['sole_normal']), last_radius(lid) * 1.35, seed=130 + i)
        shells += r['shells']; joints[lid] = r['joints']
    return shells, joints, eyes


def head_shells():
    import head
    neck_top, neck_r = L.TORSO['pts'][-1], max(L.TORSO['radii'][-1])
    r = head.build_head(L.HEAD['centre'], L.HEAD['size'], L.HEAD['chin'], neck_top, neck_r, L.HEAD['hat_brim'], L.HEAD['hat_tip'], seed=200)
    for s in r['beard']:
        s.mat = 'beard'; s.group = 'HEAD'
    for s in r['hat']:
        s.mat = 'hat'; s.group = 'HEAD'
    for s in r['head']:
        s.group = 'HEAD'
    return r['head'] + r['beard'] + r['hat'], r['joints'], r['decals']


# ------------------------------------------------------------------ raycast helpers
def bvh_of(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    return BVHTree.FromObject(ob, dg)


def face_group(ob, fi):
    me = ob.data
    counts = {}
    for vi in me.polygons[fi].vertices:
        for g in me.vertices[vi].groups:
            n = ob.vertex_groups[g.group].name
            counts[n] = counts.get(n, 0) + g.weight
    return max(counts, key=counts.get) if counts else None


def canon_ray(bvh, px, py):
    d = V(((px - L.CX0) / L.F_PX, 1.0, -(py - L.CY0) / L.F_PX)).normalized()
    return bvh.ray_cast(L.CAM, d, 200.0)


def tangent_up(normal, angle_deg=0.0, ref=V((0, 0, 1))):
    n = V(normal).normalized()
    u = ref - n * ref.dot(n)
    if u.length < 1e-4:
        u = V((0, -1, 0)) - n * n.y
    u.normalize()
    if angle_deg:
        u = Matrix.Rotation(math.radians(angle_deg), 3, n) @ u
    return u


# eye widths (m) - bigger torso/shoulder eyes than the painter default, like the canon
EYE_CLAMP = {'E06': (1.45, 1.6), 'E07': (0.78, 0.88), 'E08': (0.8, 0.9), 'E09': (0.8, 0.9), 'E10': (0.88, 0.98)}

# face fragments: canon pixel, size (face height, m), in-plane rotation of 'up' (deg, + = counter-clockwise seen from outside)
FRAG = {'F01': (2.6, -38), 'F02': (2.6, 0), 'F03': (2.0, 0), 'F04': (2.4, 10), 'F05': (1.8, -15), 'F06': (1.9, 0)}


def _group_centrelines(group):
    cl = []
    if group == 'CORE':
        for d in (L.TORSO, L.HUB, L.SHOULDER_BAR):
            cl += [s[0] for s in core.catmull_rom(d['pts'], 4)]
        cl.append(V(L.SHOULDER['centre']))
    else:
        for lb in L.LIMBS:
            if lb['id'] == group:
                cl += [s[0] for s in core.catmull_rom(lb['pts'], 4)]
    return cl


def host_radius(group, pos):
    cl = _group_centrelines(group)
    if not cl:
        return None
    return min((V(pos) - c).length for c in cl)


def fragment_shells(pre):
    import head
    bvh = bvh_of(pre)
    shells, decals, rep = [], [], {}
    for fid, (px, py) in L.FACES.items():
        loc, nrm, fi, dist = canon_ray(bvh, px, py)
        if loc is None:
            rep[fid] = 'MISS'
            continue
        grp = face_group(pre, fi)
        size, ang = FRAG[fid]
        up = tangent_up(nrm, ang)
        r = head.face_fragment(fid, loc, nrm, up, size, seed=300 + int(fid[1:]), host_radius=host_radius(grp, loc))
        for s in r['shells']:
            s.group = grp
        shells += r['shells']
        for d in r['decals']:
            d['group'] = grp
        decals += r['decals']
        rep[fid] = dict(group=grp, pos=[round(c, 2) for c in loc])
    LOG['fragments'] = rep
    return shells, decals


# ------------------------------------------------------------------ union + groups
def union(ob):
    bpy.context.view_layer.objects.active = ob
    for o in bpy.context.selected_objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.intersect_boolean(operation='UNION', use_swap=False, use_self=True, solver='EXACT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.remove_doubles(threshold=0.0005)
    bpy.ops.mesh.dissolve_degenerate(threshold=0.0005)
    bpy.ops.mesh.delete_loose()
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.fill_holes(sides=0)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    # boolean cut lines leave collinear valence-2 verts -> dissolve them (fewer tris after triangulation)
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    v2 = []
    for v in bm.verts:
        if len(v.link_edges) == 2:
            a_, b_ = [e.other_vert(v).co - v.co for e in v.link_edges]
            if a_.length > 1e-6 and b_.length > 1e-6 and a_.normalized().dot(b_.normalized()) < -0.985:
                v2.append(v)
    bmesh.ops.dissolve_verts(bm, verts=v2)
    # edges shared by 4 faces (two parts touching exactly) -> split so every edge has 2 faces
    nm4 = [e for e in bm.edges if len(e.link_faces) > 2]
    if nm4:
        bmesh.ops.split_edges(bm, edges=nm4)
    LOG['split_touching_edges'] = len(nm4)
    bm.to_mesh(ob.data)
    bm.free()
    LOG['dissolved_collinear_verts'] = len(v2)


def fix_groups(ob, shells):
    """Union-created verts get the group of the nearest original part surface; every vert ends with ONE group."""
    groups = {}
    for s in shells:
        g = s.group or s.name
        verts = [tuple(p) for p in s.verts]
        polys = [list(f) for f in s.faces]
        groups.setdefault(g, []).append((verts, polys))
    trees = {}
    for g, parts in groups.items():
        vs, ps = [], []
        for verts, polys in parts:
            off = len(vs)
            vs += verts
            ps += [[i + off for i in p] for p in polys]
        trees[g] = BVHTree.FromPolygons(vs, ps)
    me = ob.data
    for g in groups:
        if g not in ob.vertex_groups:
            ob.vertex_groups.new(name=g)
    fixed = 0
    for v in me.vertices:
        if len(v.groups) == 1 and v.groups[0].weight > 0.99:
            continue
        best, bd = None, 1e9
        for g, t in trees.items():
            hit = t.find_nearest(v.co)
            if hit[0] is not None and hit[3] < bd:
                bd, best = hit[3], g
        for gg in list(v.groups):
            ob.vertex_groups[gg.group].remove([v.index])
        ob.vertex_groups[best].add([v.index], 1.0, 'REPLACE')
        fixed += 1
    LOG['groups_fixed_verts'] = fixed


def clean_group_islands(ob, min_size=25):
    """Small islands of one limb group inside another (e.g. a few CORE verts on an arm) -> majority neighbour group."""
    me = ob.data
    names = {vg.index: vg.name for vg in ob.vertex_groups}
    grp = [names[max(v.groups, key=lambda g: g.weight).group] if v.groups else None for v in me.vertices]
    nb = [[] for _ in me.vertices]
    for e in me.edges:
        a, b = e.vertices
        nb[a].append(b); nb[b].append(a)
    fixed, changed = 0, True
    for _ in range(3):
        if not changed:
            break
        changed = False
        seen = set()
        for i in range(len(grp)):
            if i in seen:
                continue
            st, comp = [i], []
            while st:
                x = st.pop()
                if x in seen:
                    continue
                seen.add(x); comp.append(x)
                st += [y for y in nb[x] if grp[y] == grp[i] and y not in seen]
            if len(comp) >= min_size:
                continue
            cs = set(comp)
            votes = {}
            for x in comp:
                for y in nb[x]:
                    if y not in cs:
                        votes[grp[y]] = votes.get(grp[y], 0) + 1
            if not votes:
                continue
            new = max(votes, key=votes.get)
            for x in comp:
                grp[x] = new
            fixed += len(comp); changed = True
    for vg in ob.vertex_groups:
        vg.remove(list(range(len(me.vertices))))
    for i, g in enumerate(grp):
        if g:
            ob.vertex_groups[g].add([i], 1.0, 'REPLACE')
    return fixed


def mesh_report(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    nm = sum(1 for e in bm.edges if len(e.link_faces) != 2)
    bm.faces.ensure_lookup_table()
    seen, comps = set(), []
    for f in bm.faces:
        if f.index in seen:
            continue
        st, n = [f], 0
        while st:
            x = st.pop()
            if x.index in seen:
                continue
            seen.add(x.index); n += 1
            for e in x.edges:
                for g in e.link_faces:
                    if g.index not in seen:
                        st.append(g)
        comps.append(n)
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    bm.free()
    return dict(tris=tris, faces=len(ob.data.polygons), verts=len(ob.data.vertices), nonmanifold_edges=nm,
                components=len(comps), component_sizes=sorted(comps, reverse=True)[:8])


# ------------------------------------------------------------------ main
def build_shells():
    """All closed body shells (+ decals, joints, leaf cards). Scene must be reset by the caller."""
    shells = base_shells()
    joints, hand_eyes, head_decals, head_joints = {}, {}, [], {}
    try:
        hs, joints, hand_eyes = hand_shells()
        shells += hs
        LOG['hands'] = 'hands.py'
    except ImportError as e:
        LOG['hands'] = f'MISSING ({e}) - blocking placeholders'
        import blocking
        for hid, d in L.SUPPORT_HANDS.items():
            shells += blocking.placeholder_hand(hid, d['wrist'], d['knuckles'], d['tips'], r_f=0.26)
    try:
        hs, head_joints, head_decals = head_shells()
        shells += hs
        LOG['head'] = 'head.py'
    except ImportError as e:
        LOG['head'] = f'MISSING ({e})'
        shells.append(core.blob('HEAD', L.HEAD['centre'], (0.65, 0.55, 0.85), group='HEAD', seed=3))
    pre = bl.shells_to_object('MG01_pre', shells)
    frag_decals = []
    try:
        fs, frag_decals = fragment_shells(pre)
        shells += fs
    except (ImportError, AttributeError) as e:
        LOG['fragments'] = f'MISSING ({e})'
    stems, leaf_cards, branch_joints = [], [], {}
    try:
        import details
        stems, leaf_cards, branch_joints, roots = details.build_branches(pre)
        for st in stems:
            st.mat = 'branch'
        shells += stems
        LOG['branches'] = {k: dict(host=v['host_group'], method=v.get('method')) for k, v in roots.items()}
    except (ImportError, AttributeError) as e:
        LOG['branches'] = f'MISSING ({e})'
    bpy.data.objects.remove(pre)
    LOG['pre_union_tris'] = core.shells_tri_count(shells)
    return shells, dict(joints=joints, hand_eyes=hand_eyes, head_decals=head_decals, head_joints=head_joints,
                        frag_decals=frag_decals, leaf_cards=leaf_cards, branch_joints=branch_joints)


def main():
    t0 = time.time()
    sc = bl.reset()
    shells, ctx = build_shells()
    joints, hand_eyes, head_decals, head_joints = ctx['joints'], ctx['hand_eyes'], ctx['head_decals'], ctx['head_joints']
    frag_decals, leaf_cards, branch_joints = ctx['frag_decals'], ctx['leaf_cards'], ctx['branch_joints']

    t1 = time.time()
    if ORGANIC:
        import organic
        body = organic.organic_body(shells, 'MG01_Body', LOG.setdefault('organic', {}))
        organic.assign_regions(body, shells)
    else:
        body = bl.shells_to_object('MG01_Body', shells)
        union(body)
    LOG['union_s'] = round(time.time() - t1, 2)
    fix_groups(body, shells)
    LOG['group_islands_fixed'] = clean_group_islands(body)
    LOG['body'] = mesh_report(body)
    print('BODY', LOG['body'])

    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'MG01_union.blend'))

    # ---------------- paint
    import paint
    anchors = {eid: dict(pos=e['pos'], normal=e['normal'], up=e['up'], size=e['size'], group=hid) for eid, (e, hid) in hand_eyes.items()}
    spec = {k: dict(v) for k, v in paint.EYE_SPEC.items()}
    for k, cl in EYE_CLAMP.items():
        if k in spec:
            spec[k]['clamp'] = cl
    spec['E07']['groups'] = ('CORE',)   # torso eye stays on the torso (not on the cradle-arm shoulder)
    for eid in ('E04', 'E05'):
        if eid in anchors and anchors[eid].get('size'):
            anchors[eid]['size'] *= 1.2
        if eid in anchors:
            anchors[eid]['up'] = None   # keep the palm eyes upright in the world, not along the fingers
    eyes = paint.eye_decals_from_layout(body, spec=spec, anchors=anchors)
    for d in head_decals:
        d.setdefault('group', 'HEAD')
    decals = eyes + head_decals + frag_decals
    if ORGANIC:   # smoothing moved the surface a little: put every decal back onto it
        bvh = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
        for d in decals:
            if d.get('miss'):
                continue
            hit = bvh.find_nearest(V(d['pos']))
            if hit[0] is not None:
                n0 = V(d['normal']).normalized()
                d['pos'] = hit[0]
                d['normal'] = (n0 + hit[1]).normalized() if n0.dot(hit[1]) > 0 else n0
    LOG['eyes'] = {d.get('id', '?'): dict(group=d.get('group'), miss=d.get('miss'), snapped=d.get('snapped')) for d in eyes}
    keep = paint.protect_from_decals(decals)
    t1 = time.time()
    LOG['uv'] = paint.unwrap(body, texture_size=2048, margin_px=8, keep_together=keep, verbose=False)
    LOG['uv_s'] = round(time.time() - t1, 1)
    tex_body = os.path.join(OUT, 'T_MG01_Body_BaseColor.png')
    t1 = time.time()
    st = paint.paint(body, decals, tex_body, size=1024 if FAST else 2048, seed=0, ao=not FAST,
                     debug_path=os.path.join(OUT, 'debug_uv_layout.png'), verbose=False)
    LOG['paint'] = {k: v for k, v in st.items() if k in ('px_per_m', 'misses', 'coverage', 'overlap_texels', 'times')}
    LOG['paint_s'] = round(time.time() - t1, 1)
    paint.assign_texture(body, tex_body)

    # ---------------- cloth + leaves
    import details
    tex_cloth = os.path.join(OUT, 'T_MG01_Cloth_BaseColor.png')
    tex_leaves = os.path.join(OUT, 'T_MG01_Leaves_BaseColor.png')
    cloth = details.loincloth(**{**details.cloth_fit_from_layout(seed=5), 'n_cols': 18})
    details.cloth_texture(tex_cloth, cloth['uv_layout'])
    cloth_ob = details.cloth_object(cloth, tex_cloth, name='MG01_Cloth')
    # drop tiny cloth bits that ended up buried inside the body
    bm = bmesh.new(); bm.from_mesh(cloth_ob.data); bm.verts.ensure_lookup_table()
    seen, kill = set(), []
    for v0 in bm.verts:
        if v0.index in seen:
            continue
        st, comp = [v0], []
        while st:
            x = st.pop()
            if x.index in seen:
                continue
            seen.add(x.index); comp.append(x)
            st += [e.other_vert(x) for e in x.link_edges if e.other_vert(x).index not in seen]
        if len(comp) < 8:
            kill += comp
    if kill:
        bmesh.ops.delete(bm, geom=kill, context='VERTS'); bm.to_mesh(cloth_ob.data)
    bm.free()
    LOG['cloth_removed_bits_verts'] = len(kill)
    for vg in list(cloth_ob.vertex_groups):
        cloth_ob.vertex_groups.remove(vg)
    cloth_ob.vertex_groups.new(name='CORE').add(list(range(len(cloth_ob.data.vertices))), 1.0, 'REPLACE')
    details.leaves_texture(tex_leaves)
    leaves_ob = details.leaves_object(leaf_cards, tex_leaves, name='MG01_Leaves')
    LOG['cloth_tris'] = bl.tri_count(cloth_ob)
    LOG['leaves_tris'] = bl.tri_count(leaves_ob)

    # ---------------- pivot: ground centre of the three support contacts
    cs = [sum(d['tips'], V()) / len(d['tips']) for d in L.SUPPORT_HANDS.values()]
    c = sum(cs, V()) / 3
    off = V((-c.x, -c.y, 0.0))
    LOG['pivot_offset'] = [round(off.x, 3), round(off.y, 3), 0.0]
    for ob in (body, cloth_ob, leaves_ob):
        ob.data.transform(Matrix.Translation(off))
        ob.data.update()
    root = bpy.data.objects.new('MG01_Root', None)
    root.empty_display_type = 'ARROWS'
    sc.collection.objects.link(root)
    for ob in (body, cloth_ob, leaves_ob):
        ob.parent = root
    # rig guides (shifted the same way)
    g = rig.collect(head_joints=head_joints, hand_joints=joints)
    for chain in g['chains'].values():
        for j in chain:
            j['pos'] = [round(j['pos'][0] + off.x, 4), round(j['pos'][1] + off.y, 4), j['pos'][2]]
    for t in g['ik_targets'].values():
        t['pos'] = [round(t['pos'][0] + off.x, 4), round(t['pos'][1] + off.y, 4), t['pos'][2]]
    g['branch_chains'] = {k: [[round(p[0] + off.x, 4), round(p[1] + off.y, 4), round(p[2], 4)] for p in v] for k, v in branch_joints.items()}
    rig.write_json(g, os.path.join(OUT, 'MG01_rig_guides.json'))
    rig.build_guides(g)
    bm = bmesh.new()
    bm.from_mesh(body.data)
    nm4 = [e for e in bm.edges if len(e.link_faces) > 2]
    if nm4:
        bmesh.ops.split_edges(bm, edges=nm4)
        bm.to_mesh(body.data)
    bm.free()
    LOG['split_touching_edges_final'] = len(nm4)
    # drop sliver islands left by the split (a few faces, invisible)
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.faces.ensure_lookup_table()
    seen, kill = set(), []
    for f in bm.faces:
        if f.index in seen:
            continue
        st, comp = [f], []
        while st:
            x = st.pop()
            if x.index in seen:
                continue
            seen.add(x.index); comp.append(x)
            for e in x.edges:
                for g2 in e.link_faces:
                    if g2.index not in seen:
                        st.append(g2)
        if len(comp) < 6 and all(len(e.link_faces) == 2 for f in comp for e in f.edges):
            kill += comp   # only tiny CLOSED floaters; open pieces are part of the surface
    if kill:
        bmesh.ops.delete(bm, geom=kill, context='FACES')
        bm.to_mesh(body.data)
    bm.free()
    LOG['removed_sliver_faces'] = len(kill)
    LOG['body_final'] = mesh_report(body)
    LOG['total_tris_lod0'] = LOG['body_final']['tris'] + LOG['cloth_tris'] + LOG['leaves_tris']
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'MG01.blend'))
    LOG['total_s'] = round(time.time() - t0, 1)
    json.dump(LOG, open(os.path.join(OUT, 'assembly_log.json'), 'w'), indent=1, default=str)
    print(json.dumps({k: v for k, v in LOG.items() if k not in ('uv',)}, indent=1, default=str))
    return sc, body


if __name__ == '__main__':
    main()
