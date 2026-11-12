"""Organic body: voxel union of all body shells -> fillets at every junction + soft organic smoothing ->
detail-weighted decimation to the triangle budget -> crisp hat and twig stems unioned back in.
No hard boolean creases at limb roots, wrists, ankles or finger roots, so nothing reads as a break.
"""
import math, time
import bpy, bmesh
from mathutils import Vector as V
from mathutils.bvhtree import BVHTree
import core, bl

VOXEL = 0.03          # m (working scale) - small enough for the thin claw fingers
MID_FACES = 450000    # intermediate density for the smoothing / displacement passes
TARGET_TRIS = 38000   # body after hat + stems are unioned back in (v3: budget x2 again)
import numpy as np


def shell_kind(s):
    n = s.name
    if s.mat in ('hat', 'branch'):
        return 'crisp'
    if n.startswith(('BEARD', 'HEAD_nose', 'HEAD_ear')) or '_nose' in n or '_brow' in n or '_cheek' in n or '_chin' in n:
        return 'feature'
    if n.startswith('HEAD'):
        return 'head'
    if '_f' in n[-3:] or 'thumb' in n or 'toe' in n or 'claw' in n:
        return 'finger'
    if 'palm' in n or 'foot' in n or 'wrist' in n or 'heel' in n:
        return 'hand'
    return 'limb'


# (smooth strength, decimation detail weight)
KIND = {'limb': (1.0, 0.12), 'hand': (0.6, 0.55), 'finger': (0.45, 0.8), 'cradle_finger': (0.8, 0.6), 'claw': (0.0, 1.0),
        'frag': (0.6, 0.8), 'head': (0.35, 0.9), 'feature': (0.08, 1.0)}


def shell_radius(s):
    xs = [v.x for v in s.verts]; ys = [v.y for v in s.verts]; zs = [v.z for v in s.verts]
    d = sorted((max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs)))
    return max(0.05, d[0] * 0.5)   # half of the thinnest extent ~ local radius


def organic_body(shells, name='MG01_Body', log=None, eye_fn=None):
    log = log if log is not None else {}
    t0 = time.time()
    soft = [s for s in shells if shell_kind(s) != 'crisp']
    crisp = [s for s in shells if shell_kind(s) == 'crisp']
    ob = bl.shells_to_object(name, soft)
    bpy.context.view_layer.objects.active = ob
    for o in bpy.context.selected_objects:
        o.select_set(False)
    ob.select_set(True)
    m = ob.modifiers.new('vox', 'REMESH')
    m.mode = 'VOXEL'; m.voxel_size = VOXEL; m.adaptivity = 0.0; m.use_smooth_shade = False
    bpy.ops.object.modifier_apply(modifier=m.name)

    log['voxel_faces'] = len(ob.data.polygons)
    boxes = []
    for s in soft:
        if (s.group or '') in ('A03', 'A04') and s.name != s.group:
            xs = [p.x for p in s.verts]; ys = [p.y for p in s.verts]; zs = [p.z for p in s.verts]
            boxes.append((min(xs) - 0.1, max(xs) + 0.1, min(ys) - 0.1, max(ys) + 0.1, min(zs) - 0.1, max(zs) + 0.1))
    vg_p = ob.vertex_groups.new(name='_protect')
    prot = [v.index for v in ob.data.vertices
            if any(b[0] <= v.co.x <= b[1] and b[2] <= v.co.y <= b[3] and b[4] <= v.co.z <= b[5] for b in boxes)]
    if prot:
        vg_p.add(prot, 1.0, 'REPLACE')
    d = ob.modifiers.new('mid', 'DECIMATE')
    d.decimate_type = 'COLLAPSE'; d.ratio = min(1.0, MID_FACES / max(1, len(ob.data.polygons)))
    # (no vertex-group protection here: it blew up memory on the 1.3M-face voxel mesh)
    bpy.ops.object.modifier_apply(modifier=d.name)

    log['mid_faces'] = len(ob.data.polygons)
    t1 = time.time()

    # --- per-vertex junction / smoothing / detail weights from the source shells
    vs, ps, owner = [], [], []
    rad, kinds = [], []
    for si, s in enumerate(soft):
        off = len(vs)
        vs += [tuple(p) for p in s.verts]
        for f in s.faces:
            ps.append([i + off for i in f]); owner.append(si)
        k = shell_kind(s)
        if k == 'finger' and ((s.group or '') in ('C01', 'C02') or (s.group or '').startswith('L')):
            k = 'cradle_finger'   # long fingers wrapped around a thigh, and toes: rounder, no notches
        if (s.group or '') in ('A03', 'A04') and s.name != s.group:
            k = 'claw'            # thin claw hands: no smoothing, full detail
        if s.name[:2] == 'F0' and s.name[2:3].isdigit():
            k = 'frag'            # face fragments: soften the wedges so they melt in
        rad.append(shell_radius(s)); kinds.append(k)
    tree = BVHTree.FromPolygons(vs, ps)
    me = ob.data
    vg_s = ob.vertex_groups.new(name='_smooth')
    vg_d = ob.vertex_groups.new(name='_detail')
    vg_r = ob.vertex_groups.new(name='_round')
    vg_n = ob.vertex_groups.new(name='_noise')
    limb_vg = {}
    for s in soft:
        g = s.group or s.name
        if g not in limb_vg:
            limb_vg[g] = ob.vertex_groups.get(g) or ob.vertex_groups.new(name=g)
    NOISE = {'limb': 1.0, 'hand': 0.6, 'cradle_finger': 0.3, 'finger': 0.2}
    RMAX = 1.0
    for v in me.vertices:
        hits = tree.find_nearest_range(v.co, RMAX)
        best = {}
        for co, nrm, fi, dist in hits:
            s = owner[fi]
            if s not in best or dist < best[s]:
                best[s] = dist
        if not best:
            hit = tree.find_nearest(v.co)
            best = {owner[hit[2]]: hit[3]} if hit[0] is not None else {0: 0.0}
        order = sorted(best.items(), key=lambda kv: kv[1])
        s1, d1 = order[0]
        sm, dt = KIND[kinds[s1]]
        junc = 0.0
        if len(order) > 1:
            s2, d2 = order[1]
            R = min(RMAX, 0.75 * min(rad[s1], rad[s2]) + 0.06)
            junc = max(0.0, 1.0 - d2 / R) if d2 < R else 0.0
            # fillet strength follows the finer of the two parts (finger roots stay tighter)
            junc *= 0.7 if 'finger' in (kinds[s1], kinds[s2]) else 1.0
        if kinds[s1] == 'claw':
            junc = min(junc, 0.3)
        if kinds[s1] in ('cradle_finger', 'finger') and (soft[s1].group or '') not in ('A01', 'A02'):  # thin open-hand fingers would turn into needles
            full = 1.0 if kinds[s1] == 'cradle_finger' else 0.7   # v3: every flesh finger gets rounded
            far = len(order) < 2 or kinds[order[1][0]] in ('cradle_finger', 'finger') or order[1][1] > 0.25
            vg_r.add([v.index], full if far else full * 0.5, 'REPLACE')
        vg_s.add([v.index], min(1.0, max(sm * 0.45, junc)), 'REPLACE')
        vg_d.add([v.index], min(1.0, dt + 0.9 * junc), 'REPLACE')
        nz = NOISE.get(kinds[s1], 0.0)
        if nz:
            vg_n.add([v.index], nz, 'REPLACE')
        limb_vg[soft[s1].group or soft[s1].name].add([v.index], 1.0, 'REPLACE')

    log['weights_s'] = round(time.time() - t1, 1)

    # --- fillets + organic softening (volume preserving)
    ls = ob.modifiers.new('fillet', 'LAPLACIANSMOOTH')
    ls.vertex_group = '_smooth'; ls.iterations = 24; ls.lambda_factor = 0.9; ls.lambda_border = 0.0
    ls.use_volume_preserve = True; ls.use_normalized = True
    bpy.ops.object.modifier_apply(modifier=ls.name)
    # boxy wrapped fingers and toes: many more iterations so the square phalanges become round fingers
    rr = ob.modifiers.new('round', 'LAPLACIANSMOOTH')
    rr.vertex_group = '_round'; rr.iterations = 90; rr.lambda_factor = 1.0; rr.lambda_border = 0.0
    rr.use_volume_preserve = True; rr.use_normalized = True
    bpy.ops.object.modifier_apply(modifier=rr.name)
    sm2 = ob.modifiers.new('soft', 'SMOOTH')
    sm2.vertex_group = '_smooth'; sm2.factor = 0.5; sm2.iterations = 6
    bpy.ops.object.modifier_apply(modifier=sm2.name)

    # --- soft organic irregularity on limbs (low-frequency lumps, no tube-perfect surfaces)
    tex = bpy.data.textures.get('mg_noise') or bpy.data.textures.new('mg_noise', 'CLOUDS')
    tex.noise_scale = 0.9; tex.noise_depth = 1
    dp = ob.modifiers.new('lumps', 'DISPLACE')
    dp.texture = tex; dp.texture_coords = 'GLOBAL'; dp.direction = 'NORMAL'
    dp.strength = 0.12; dp.mid_level = 0.5; dp.vertex_group = '_noise'
    bpy.ops.object.modifier_apply(modifier=dp.name)

    # --- eyes get real geometry: a slight eyeball dome and lid ridges (the paint lands on top later)
    eye_decals = []
    if eye_fn is not None:
        eye_decals = eye_fn(ob)
        log['eyelid_verts'] = eyelids(ob, eye_decals, vg_d)

    # --- detail-weighted decimation to the budget
    crisp_tris = core.shells_tri_count(crisp)
    target = max(4000, TARGET_TRIS - int(crisp_tris * 1.4))
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    dm = ob.modifiers.new('budget', 'DECIMATE')
    dm.decimate_type = 'COLLAPSE'; dm.use_collapse_triangulate = True
    dm.vertex_group = '_detail'; dm.vertex_group_factor = 6.0
    dm.ratio = target / tris
    bpy.ops.object.modifier_apply(modifier=dm.name)

    log['decimated_tris'] = sum(len(p.vertices) - 2 for p in ob.data.polygons)

    # --- crisp hat + twig stems back in (exact boolean union keeps their hard planes)
    if crisp:
        cob = bl.shells_to_object(name + '_crisp', crisp)
        # self-union the crisp parts first (hat cone+brim, stem forks) so the final union sees clean shells
        bpy.context.view_layer.objects.active = cob
        for o in bpy.context.selected_objects:
            o.select_set(False)
        cob.select_set(True)
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.mesh.intersect_boolean(operation='UNION', use_swap=False, use_self=True, solver='EXACT')
        bpy.ops.object.mode_set(mode='OBJECT')
        mod = ob.modifiers.new('crisp', 'BOOLEAN')
        mod.operation = 'UNION'; mod.solver = 'EXACT'; mod.object = cob
        try:
            mod.material_mode = 'TRANSFER'
        except Exception:
            pass
        bpy.context.view_layer.objects.active = ob
        for o in bpy.context.selected_objects:
            o.select_set(False)
        ob.select_set(True)
        bpy.ops.object.modifier_apply(modifier=mod.name)
        # material slots of the crisp parts
        for ms in cob.data.materials:
            if ms.name not in [x.name for x in ob.data.materials if x]:
                ob.data.materials.append(ms)
        bpy.data.objects.remove(cob)
    for g in ('_smooth', '_detail', '_protect', '_round', '_noise'):
        if g in ob.vertex_groups:
            ob.vertex_groups.remove(ob.vertex_groups[g])
    # smooth shading everywhere; sharp edges are decided later per part (smart_sharp)
    for p in ob.data.polygons:
        p.use_smooth = True
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(ob.data); bm.free()
    log['organic_s'] = round(time.time() - t0, 1)
    return ob, eye_decals


def eyelids(ob, decals, vg_detail=None):
    """Push the surface out around each painted eye: low eyeball dome + upper/lower lid ridges."""
    me = ob.data
    n = len(me.vertices)
    co = np.empty(n * 3); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
    vn = np.empty(n * 3); me.vertices.foreach_get('normal', vn); vn = vn.reshape(-1, 3)
    disp = np.zeros((n, 3))
    touched = np.zeros(n, bool)
    for d in decals:
        if d.get('miss') or d.get('kind', 'eye_open') != 'eye_open':
            continue
        N = np.array(d['normal'], float); N /= np.linalg.norm(N)
        U = np.array(d['up'], float); U = U - N * U.dot(N); U /= np.linalg.norm(U)
        R = np.cross(U, N)
        w = float(d['size']); h = 0.42 * w
        rel = co - np.array(d['pos'], float)
        dn, u, v = rel @ N, rel @ R, rel @ U
        sel = (np.abs(dn) < 0.45 * w) & (u * u + v * v < (1.0 * w) ** 2) & (vn @ N > 0.25)
        r = np.sqrt((u / (0.5 * w)) ** 2 + (v / (0.5 * h)) ** 2)
        dome = np.where(r < 1.0, 0.045 * w * (1 - r * r), 0.0)
        upper = 0.35 + 0.65 * np.clip(v / (0.5 * h) + 0.5, 0, 1)
        ridge = 0.055 * w * np.exp(-((r - 1.12) / 0.24) ** 2) * upper
        fade = np.clip((2.0 - r) / 0.6, 0, 1)
        amt = np.where(sel, (dome + ridge) * fade, 0.0)
        disp += amt[:, None] * N[None, :]
        touched |= sel & (r < 1.9)
    co += disp
    me.vertices.foreach_set('co', co.ravel())
    me.update()
    if vg_detail is not None:
        idx = np.nonzero(touched)[0].tolist()
        if idx:
            vg_detail.add(idx, 1.0, 'REPLACE')
    return int(touched.sum())


# ------------------------------------------------------------------ smart shading
# dihedral angle (deg) above which an edge stays sharp, per part kind. Organic flesh is smooth unless
# it is a real fold; stylised hard pieces (hat folds, beard planes, wedge noses) keep their planes.
SHARP_DEG = {'hat': 30, 'branch': 55, 'beard': 24, 'feature': 34, 'frag': 62, 'claw': 60, 'head': 55,
             'finger': 78, 'cradle_finger': 78, 'hand': 72, 'limb': 76}
CRISP_KINDS = ('hat', 'beard', 'branch')


def _soft_kind(s):
    k = shell_kind(s)
    g = s.group or ''
    if s.name.startswith('BEARD'):
        return 'beard'
    if s.name[:2] == 'F0' and s.name[2:3].isdigit():
        return 'frag'
    if g in ('A03', 'A04') and s.name != g:
        return 'claw'
    if k == 'finger' and (g in ('C01', 'C02') or g.startswith('L')):
        return 'cradle_finger'
    return k


def face_kinds(ob, shells):
    soft = [s for s in shells if shell_kind(s) != 'crisp']
    vs, ps, owner = [], [], []
    for si, s in enumerate(soft):
        off = len(vs)
        vs += [tuple(p) for p in s.verts]
        for f in s.faces:
            ps.append([i + off for i in f]); owner.append(si)
    tree = BVHTree.FromPolygons(vs, ps)
    mats = [m.name if m else '' for m in ob.data.materials]
    reg = ob.data.attributes.get('mg_region')   # written by paint.assign_texture (0 body,1 beard,2 hat,3 branch)
    regv = None
    if reg is not None and reg.domain == 'FACE':
        regv = [x.value for x in reg.data]
    kinds = []
    for p in ob.data.polygons:
        mn = mats[p.material_index] if p.material_index < len(mats) else ''
        if regv is not None:
            mn = {2: 'hat', 3: 'branch'}.get(regv[p.index], mn)
        if mn.endswith('hat'):
            kinds.append('hat'); continue
        if mn.endswith('branch'):
            kinds.append('branch'); continue
        hit = tree.find_nearest(p.center)
        kinds.append(_soft_kind(soft[owner[hit[2]]]) if hit[0] is not None else 'limb')
    return kinds


def smart_sharp(ob, shells, log=None):
    """Smooth shading with sharp edges only where the design wants a hard edge."""
    kinds = face_kinds(ob, shells)
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bm.faces.ensure_lookup_table()
    sharp, stats = [], {}
    for e in bm.edges:
        lf = e.link_faces
        if len(lf) != 2:
            sharp.append(e.index); continue
        a, b = lf
        ang = math.degrees(a.normal.angle(b.normal, 0.0))
        ka, kb = kinds[a.index], kinds[b.index]
        th = min(SHARP_DEG.get(ka, 74), SHARP_DEG.get(kb, 74))
        if ka != kb and (ka in CRISP_KINDS or kb in CRISP_KINDS):
            th = min(th, 28)          # hat sitting on the skull, beard on the face, twig in the arm
        if ang > th:
            sharp.append(e.index)
            stats[ka] = stats.get(ka, 0) + 1
    bm.free()
    me = ob.data
    attr = me.attributes.get('sharp_edge') or me.attributes.new('sharp_edge', 'BOOLEAN', 'EDGE')
    vals = [False] * len(me.edges)
    for i in sharp:
        vals[i] = True
    attr.data.foreach_set('value', vals)
    for p in me.polygons:
        p.use_smooth = True
    me.update()
    if log is not None:
        log['sharp_edges'] = len(sharp)
        log['sharp_by_kind'] = stats
        from collections import Counter
        log['face_kinds'] = dict(Counter(kinds))
    return len(sharp)


def assign_regions(ob, shells):
    """Voxel remesh drops face materials: re-derive the paint region (body / beard) per face from the
    nearest source shell. Hat and twig faces keep the slots transferred by the boolean."""
    soft = [s for s in shells if shell_kind(s) != 'crisp']
    vs, ps, owner = [], [], []
    for si, s in enumerate(soft):
        off = len(vs)
        vs += [tuple(p) for p in s.verts]
        for f in s.faces:
            ps.append([i + off for i in f]); owner.append(si)
    tree = BVHTree.FromPolygons(vs, ps)
    names = [m.name if m else '' for m in ob.data.materials]
    def slot(key):
        mname = 'PRE_' + key
        if mname not in names:
            ob.data.materials.append(bl.preview_material(key))
            names.append(mname)
        return names.index(mname)
    body_i, beard_i = slot('body'), slot('beard')
    keep = {i for i, n in enumerate(names) if n in ('PRE_hat', 'PRE_branch')}
    for p in ob.data.polygons:
        if p.material_index in keep:
            continue
        hit = tree.find_nearest(p.center)
        p.material_index = beard_i if hit[0] is not None and soft[owner[hit[2]]].mat == 'beard' else body_i
