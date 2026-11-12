"""MG01 body: UV unwrap + procedural BaseColor painting (T_MG01_Body_BaseColor.png).

One skinned body mesh (body + beard + hat + blue branch stems). Paint regions come from the face
material slot names (PRE_body / PRE_beard / PRE_hat / PRE_branch, anything else -> body) or, once
assign_texture() has replaced the slots, from the face attribute 'mg_region'.
Vertex groups = limb IDs; a face's group = dominant vertex group of its vertices.

Typical use
    eyes = paint.eye_decals_from_layout(ob, anchors=hand_eye_anchors)    # E01..E10
    paint.unwrap(ob, 2048, 8, keep_together=paint.protect_from_decals(eyes))
    paint.paint(ob, eyes + dashes, '.../T_MG01_Body_BaseColor.png', size=2048)
    paint.assign_texture(ob, '.../T_MG01_Body_BaseColor.png')          # preview material, Closest

Public API
    unwrap(obj, texture_size=2048, margin_px=8, keep_together=None, ...)  -> stats dict
        NOTE: triangulates n-gons (> 4 verts) in place (verts/groups/materials/tri count unchanged).
    anchor_from_canon(obj, px, py, want_groups=None)                      -> dict(pos, normal, group, ...)
    snap_to_group(obj, px, py, groups)                                    -> anchor on the intended limb
    eye_decals_from_layout(obj, anchors=None, ...)                        -> [decal, ...] (E01..E10)
    dash_decal_from_canon(obj, px, py, length_px, kind, roll, group)      -> closed-eye / mouth decal
    paint(obj, decals, out_path, size=2048, seed=0, ao=True, ...)         -> stats dict (writes PNG)
    protect_from_decals(decals)                                           -> keep_together for unwrap()
    assign_texture(obj, image_path, ...)                                  -> material (Image Texture, Closest)
    uv_stats(obj, texture_size)                                           -> px/m, pack efficiency, stretch

Decal dict: kind in {'eye_open','eye_closed','mouth','brow'}, pos, normal, up, size (width, m),
optional: group (limb id: paint only texels of that group), aspect (height/width, eye default 0.42),
roll (deg around normal), look (iris x offset, fraction of half width), id, thickness (fraction of
size for dashes), bend (dash centre-line arch, + = ends down), miss (str -> decal is skipped).
Decals are projected along their normal; only the first surface along the normal is painted
(BVH occlusion), so a decal never lands twice (finger over palm, far side of a crease).

Anti-aliasing decision: none. Every texel is classified by its centre only (binary coverage), all
painted lines are forced to >= ~1-1.5 texels so they never break up; with Point filtering in Unity
this gives clean hard pixel edges. Empty texels are filled with the nearest island colour (full
bleed), so mip-maps never pull in background; islands are >= margin_px apart.
"""
import math, os, time
import numpy as np
import bpy, bmesh
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
import layout as L

UV_NAME = 'UVMap'
REGIONS = ['body', 'beard', 'hat', 'branch']
REGION_ATTR = 'mg_region'
DEBUG_DIR = '/home/claude/work/out/paint'


def _cross2(a, b):
    return a[..., 0] * b[..., 1] - a[..., 1] * b[..., 0]


def _hex(h):
    h = h.lstrip('#')
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)], np.float32)


# sRGB palette (guide hex where given; derived shades marked)
COL = dict(
    body=_hex('#5C70A4'), body_light=_hex('#7B8DB7'), body_shadow=_hex('#405482'),
    beard=_hex('#A0AAC3'), beard_dark=_hex('#858FAD'), beard_light=_hex('#B2BBD1'),
    hat=_hex('#6B4A86'), hat_dark=_hex('#4F3567'), hat_light=_hex('#7A5895'),
    branch=_hex('#4A5E9A'), branch_dark=_hex('#3B4C80'), branch_light=_hex('#5A6EA8'),
    stroke=_hex('#3A3546'),
    sclera=_hex('#C2B6CC'), sclera_corner=_hex('#A597B5'), iris=_hex('#9C89A8'), iris_rim=_hex('#82708F'),
    pupil=_hex('#2E2A38'), glint=_hex('#E4DCEA'),
    lid=_hex('#465188'), lid_rim=_hex('#7786BC'),
)


# ============================================================================ mesh helpers
def _activate(obj):
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)


def _region_of_name(name):
    n = (name or '').lower()
    for i, r in enumerate(REGIONS):
        if i and r in n:
            return i
    return 0


def face_regions(obj):
    """Per polygon region index into REGIONS (attribute 'mg_region' wins over material names)."""
    me = obj.data
    P = len(me.polygons)
    at = me.attributes.get(REGION_ATTR)
    if at is not None and at.domain == 'FACE' and len(at.data) == P:
        r = np.empty(P, np.int32)
        at.data.foreach_get('value', r)
        return r
    mi = np.empty(P, np.int32)
    me.polygons.foreach_get('material_index', mi)
    lut = np.array([_region_of_name(m.name if m else '') for m in me.materials] or [0], np.int32)
    return lut[np.clip(mi, 0, len(lut) - 1)]


def store_regions(obj):
    """Write the material-slot regions into the face attribute 'mg_region' (survives slot changes)."""
    r = face_regions(obj)
    me = obj.data
    at = me.attributes.get(REGION_ATTR) or me.attributes.new(REGION_ATTR, 'INT', 'FACE')
    at.data.foreach_set('value', r)
    return r


def face_groups(obj):
    """(per polygon group index or -1, group names) - dominant vertex group by summed weight."""
    me = obj.data
    names = [g.name for g in obj.vertex_groups]
    nv, P = len(me.vertices), len(me.polygons)
    if not names:
        return np.full(P, -1, np.int32), names
    W = np.zeros((nv, len(names)), np.float32)
    for v in me.vertices:
        for g in v.groups:
            if g.group < len(names):
                W[v.index, g.group] = g.weight
    lv = np.empty(len(me.loops), np.int32)
    me.loops.foreach_get('vertex_index', lv)
    ls = np.empty(P, np.int32)
    me.polygons.foreach_get('loop_start', ls)
    S = np.add.reduceat(W[lv], ls, axis=0)
    g = S.argmax(1).astype(np.int32)
    g[S.max(1) <= 0] = -1
    return g, names


def _mesh_arrays(obj):
    """World-space arrays: verts, loop-triangles (verts, loops, poly), poly normals/centres/areas."""
    me = obj.data
    me.calc_loop_triangles()
    M = np.array(obj.matrix_world, np.float64)
    Nm = np.array(obj.matrix_world.to_3x3().inverted_safe().transposed(), np.float64)
    nv, nt, P = len(me.vertices), len(me.loop_triangles), len(me.polygons)
    co = np.empty(nv * 3, np.float64); me.vertices.foreach_get('co', co)
    V = co.reshape(-1, 3) @ M[:3, :3].T + M[:3, 3]
    tv = np.empty(nt * 3, np.int32); me.loop_triangles.foreach_get('vertices', tv)
    tl = np.empty(nt * 3, np.int32); me.loop_triangles.foreach_get('loops', tl)
    tp = np.empty(nt, np.int32); me.loop_triangles.foreach_get('polygon_index', tp)
    pn = np.empty(P * 3, np.float64); me.polygons.foreach_get('normal', pn)
    pn = pn.reshape(-1, 3) @ Nm.T
    pn /= np.maximum(np.linalg.norm(pn, axis=1, keepdims=True), 1e-12)
    pc = np.empty(P * 3, np.float64); me.polygons.foreach_get('center', pc)
    pc = pc.reshape(-1, 3) @ M[:3, :3].T + M[:3, 3]
    tv, tl = tv.reshape(-1, 3), tl.reshape(-1, 3)
    tA = 0.5 * np.linalg.norm(np.cross(V[tv[:, 1]] - V[tv[:, 0]], V[tv[:, 2]] - V[tv[:, 0]]), axis=1)
    pa = np.bincount(tp, tA, minlength=P)
    return dict(V=V, tv=tv, tl=tl, tp=tp, pn=pn, pc=pc, pa=pa, tA=tA)


def _uv_loops(obj):
    me = obj.data
    uv = me.uv_layers.get(UV_NAME)
    if uv is None:
        raise RuntimeError(f'{obj.name}: no UV map {UV_NAME!r}; run unwrap() first')
    a = np.empty(len(me.loops) * 2, np.float64)
    uv.uv.foreach_get('vector', a)
    return a.reshape(-1, 2)


_BVH = {}


class _TriBVH:
    """BVH over the mesh's loop triangles (the exact triangles that are painted/rendered - a BVH over
    polygons may split a non-planar quad along the other diagonal and self-hit). Returned face
    indices are POLYGON indices."""

    def __init__(self, obj):
        me = obj.data
        me.calc_loop_triangles()
        M = obj.matrix_world
        verts = [M @ v.co for v in me.vertices]
        tris = [tuple(t.vertices) for t in me.loop_triangles]
        self.t2p = np.array([t.polygon_index for t in me.loop_triangles], np.int64)
        self.tree = BVHTree.FromPolygons(verts, tris, all_triangles=True)

    def ray_cast(self, o, d, dist=1e30):
        loc, nrm, i, dd = self.tree.ray_cast(o, d, dist)
        return loc, nrm, (None if i is None else int(self.t2p[i])), dd

    def find_nearest(self, p, dist=1e30):
        loc, nrm, i, dd = self.tree.find_nearest(p, dist)
        return loc, nrm, (None if i is None else int(self.t2p[i])), dd


def _bvh(obj):
    me = obj.data
    co = np.empty(len(me.vertices) * 3, np.float64)
    me.vertices.foreach_get('co', co)
    key = (obj.name, len(me.vertices), len(me.polygons), round(float(co.sum()), 5), round(float(np.abs(co).sum()), 5),
           tuple(round(x, 5) for x in np.array(obj.matrix_world).ravel()))
    b = _BVH.get(key)
    if b is None:
        b = _TriBVH(obj)
        _BVH.clear()
        _BVH[key] = b
    return b


# ============================================================================ 1. unwrap
def protect_from_decals(decals):
    """keep_together entries for unwrap(): the open-eye decals (their footprint stays in one chart)."""
    return [dict(pos=Vector(d['pos']), normal=Vector(d['normal']), up=d.get('up'), size=d['size'], group=d.get('group'),
                 roll=d.get('roll', 0.0)) for d in decals if not d.get('miss') and d.get('kind') == 'eye_open']


def _hiddenness(n):
    """+1 = back/underside (seam-friendly), -1 = front/top (keep seams away)."""
    return 0.55 * n[..., 1] - 0.45 * n[..., 2]


def segment_charts(obj, tau=0.02, tau_merge=0.06, small_frac=0.0025, keep_together=None, hid_w=0.6, erode=True, return_raw=False, verbose=False):
    """Developable chart segmentation -> per-face chart id (np.int32).
    Charts grow face by face, most visible faces first (front/top), as long as the area-weighted
    covariance of the chart's face normals keeps its smallest eigenvalue <= tau (normals stay on one
    circle = cylinder / cone / plane-like, i.e. it flattens with little stretch) and the chart stays a
    topological disc. A straight limb segment therefore becomes one strip that wraps all the way
    round and closes at its most hidden side (underside/back); bends start new charts. Charts that
    stay tiny are merged into the neighbour they share most edge length with (tau_merge).
    keep_together patches [(pos, normal, radius)] seed charts so each eye lies inside one chart."""
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table(); bm.edges.ensure_lookup_table(); bm.verts.ensure_lookup_table()
    Fn = len(bm.faces)
    M3 = obj.matrix_world.to_3x3()
    sc2 = abs(M3.determinant()) ** (2.0 / 3.0)
    nrm = np.array([tuple((M3 @ f.normal).normalized()) for f in bm.faces], np.float64).reshape(-1, 3)
    area = np.array([f.calc_area() for f in bm.faces], np.float64) * sc2
    cen = np.array([tuple(obj.matrix_world @ f.calc_center_median()) for f in bm.faces], np.float64).reshape(-1, 3)
    fverts = [[v.index for v in f.verts] for f in bm.faces]
    fedges = [[e.index for e in f.edges] for f in bm.faces]
    everts = [(e.verts[0].index, e.verts[1].index) for e in bm.edges]
    elen = np.array([e.calc_length() for e in bm.edges]) * math.sqrt(sc2)
    region = face_regions(obj)
    adj = [[] for _ in range(Fn)]
    for e in bm.edges:
        lf = e.link_faces
        if len(lf) == 2:
            a, b = lf[0].index, lf[1].index
            if region[a] == region[b]:
                adj[a].append((b, e.index))
                adj[b].append((a, e.index))
    bm.free()
    hid = _hiddenness(nrm)
    chart = np.full(Fn, -1, np.int32)
    patch = np.full(Fn, -1, np.int32)
    C = []  # chart stats

    def lam_min(S1, S2, A):
        m = S1 / A
        return np.linalg.eigvalsh(S2 / A - np.outer(m, m))[0]

    def disc_ok(faces):
        """Topological disc: chi == 1 and every boundary vertex on exactly 2 boundary edges."""
        V, E, use = set(), set(), {}
        for f in faces:
            V.update(fverts[f])
            for e in fedges[f]:
                E.add(e); use[e] = use.get(e, 0) + 1
        if len(V) - len(E) + len(faces) != 1:
            return False, V, E
        deg = {}
        for e, k in use.items():
            if k == 1:
                for v in everts[e]:
                    deg[v] = deg.get(v, 0) + 1
        return all(d == 2 for d in deg.values()), V, E

    def add(st, f, cid):
        chart[f] = cid
        st['faces'].append(f)
        st['V'].update(fverts[f]); st['E'].update(fedges[f])
        a = area[f]; n = nrm[f]
        st['S1'] += a * n; st['S2'] += a * np.outer(n, n); st['A'] += a

    import heapq

    def claim(cid, init):
        st = dict(faces=[], V=set(), E=set(), S1=np.zeros(3), S2=np.zeros((3, 3)), A=0.0)
        C.append(st)
        for f in init:
            if chart[f] < 0:
                add(st, f, cid)
        return st

    def grow(cid, init=None):
        st = claim(cid, init) if init is not None else C[cid]
        heap = []
        c0 = cen[st['faces']].mean(0)

        def prio(g):   # compact growth from the seed, hidden faces deferred -> closing seam on hidden side
            return float(np.linalg.norm(cen[g] - c0)) * (1.0 + hid_w * hid[g])
        for f in st['faces']:
            for g, _ in adj[f]:
                if chart[g] < 0:
                    heapq.heappush(heap, (prio(g), g))
        while heap:
            _, g = heapq.heappop(heap)
            if chart[g] >= 0:
                continue
            dV = sum(v not in st['V'] for v in fverts[g])
            dE = sum(e not in st['E'] for e in fedges[g])
            if dV - dE + 1 != 0:
                continue
            a, n = area[g], nrm[g]
            if lam_min(st['S1'] + a * n, st['S2'] + a * np.outer(n, n), st['A'] + a) > tau:
                continue
            add(st, g, cid)
            for h, _ in adj[g]:
                if chart[h] < 0:
                    heapq.heappush(heap, (prio(h), h))
        return st

    # 1) protected patches first (connected faces near each eye, facing like the eye)
    vco = np.array([tuple(obj.matrix_world @ v.co) for v in me.vertices], np.float64).reshape(-1, 3)
    pg, gnames = face_groups(obj)
    masks = []
    for kt in (keep_together or []):
        if isinstance(kt, dict):   # decal footprint: same acceptance test as the painter
            nn_, uu_, rr_ = (x.astype(np.float64) for x in _decal_frame(kt))
            p, W = np.array(kt['pos'], np.float64), float(kt['size'])

            def inside(q):
                d = q - p
                return (np.abs(d @ rr_) < 0.58 * W) & (np.abs(d @ uu_) < 0.3 * W) & (np.abs(d @ nn_) < 0.35 * W)
            vin = inside(vco)
            touch = np.array([vin[fv].any() for fv in fverts]) | inside(cen)
            bvh = _bvh(obj)    # faces under sample points of the eye (big faces contain the eye)
            for sx in np.linspace(-0.55, 0.55, 11):
                for sy in np.linspace(-0.22, 0.22, 5):
                    hit = bvh.find_nearest(Vector(p + rr_ * sx * W + uu_ * sy * W), 0.35 * W)
                    if hit[2] is not None:
                        touch[hit[2]] = True
            if kt.get('group') in gnames:
                touch &= pg == gnames.index(kt['group'])
            masks.append(touch & (nrm @ nn_ > 0.3))
        else:
            p, n, r = kt
            p, n = np.array(p, np.float64), np.array(n, np.float64)
            vin = np.linalg.norm(vco - p, axis=1) < r
            touch = np.array([vin[fv].any() for fv in fverts]) | (np.linalg.norm(cen - p, axis=1) < r)
            masks.append(touch & (nrm @ n > 0.2))
    # footprints sharing faces (eyes close together) become one patch
    merged = True
    while merged and len(masks) > 1:
        merged = False
        for i in range(len(masks)):
            for j in range(i + 1, len(masks)):
                if (masks[i] & masks[j]).any():
                    masks[i] = masks[i] | masks.pop(j)
                    merged = True
                    break
            if merged:
                break
    for ok in masks:
        ok = ok & (chart < 0)
        while ok.any():                       # every connected component is one patch
            s0 = int(np.nonzero(ok)[0][0])
            comp, stack = {s0}, [s0]
            while stack:
                f = stack.pop()
                for g, _ in adj[f]:
                    if ok[g] and g not in comp:
                        comp.add(g); stack.append(g)
            comp = sorted(comp)
            ok[comp] = False
            patch[comp] = len(C)
            claim(len(C), comp)
    for cid_ in range(len(C)):          # grow patches only after all of them are claimed
        grow(cid_)
    # 2) seeds: most visible first
    for f in np.argsort(hid, kind='stable'):
        if chart[f] < 0:
            grow(len(C), [int(f)])
    n_raw = len(C)
    raw = chart.copy()
    def lam_cap(so):   # no ratcheting: a chart may only get slightly less developable than tau
        return min(tau_merge, max(lam_min(so['S1'], so['S2'], so['A']), tau) * 1.25)

    # 3) merge small charts into the best neighbour
    total = area.sum()
    small = small_frac * total
    alive = set(range(len(C)))
    changed = True
    while changed:
        changed = False
        for cid in sorted(alive, key=lambda c: C[c]['A']):
            st = C[cid]
            if st['A'] >= small or cid not in alive:
                continue
            shared = {}
            for f in st['faces']:
                for g, e in adj[f]:
                    o = chart[g]
                    if o != cid:
                        shared[o] = shared.get(o, 0.0) + elen[e]
            for o, _ in sorted(shared.items(), key=lambda kv: -kv[1]):
                so = C[o]
                if lam_min(st['S1'] + so['S1'], st['S2'] + so['S2'], st['A'] + so['A']) > lam_cap(so):
                    continue
                ok, V, E = disc_ok(st['faces'] + so['faces'])
                if not ok:
                    continue
                for f in st['faces']:
                    chart[f] = o
                so['faces'] += st['faces']; so['V'] = V; so['E'] = E
                so['S1'] = so['S1'] + st['S1']; so['S2'] = so['S2'] + st['S2']; so['A'] += st['A']
                alive.discard(cid)
                changed = True
                break
    # 4) erode tentacles: a face hanging on its chart by a single edge moves to a neighbouring chart
    #    where it has >= 2 neighbours (keeps both charts discs and within tau_merge)
    def chi_ok(faces):
        return disc_ok(faces)
    moved = 0
    for _ in range(6 if erode else 0):
        n_mv = 0
        for f in range(Fn):
            c = chart[f]
            same = [g for g, _ in adj[f] if chart[g] == c]
            if len(same) != 1:
                continue
            cnt = {}
            for g, _ in adj[f]:
                if chart[g] != c:
                    cnt[chart[g]] = cnt.get(chart[g], 0) + 1
            for o, k in sorted(cnt.items(), key=lambda kv: -kv[1]):
                if k < 2:
                    break
                so, sc_ = C[o], C[c]
                a, n = area[f], nrm[f]
                if lam_min(so['S1'] + a * n, so['S2'] + a * np.outer(n, n), so['A'] + a) > lam_cap(so):
                    continue
                rest = [x for x in sc_['faces'] if x != f]
                ok1, V1, E1 = chi_ok(so['faces'] + [f])
                ok2, V2, E2 = chi_ok(rest) if rest else (True, set(), set())
                if not (ok1 and ok2):
                    continue
                so['faces'].append(f); so['V'], so['E'] = V1, E1
                so['S1'] = so['S1'] + a * n; so['S2'] = so['S2'] + a * np.outer(n, n); so['A'] += a
                sc_['faces'] = rest; sc_['V'], sc_['E'] = V2, E2
                sc_['S1'] = sc_['S1'] - a * n; sc_['S2'] = sc_['S2'] - a * np.outer(n, n); sc_['A'] -= a
                chart[f] = o
                n_mv += 1
                break
        moved += n_mv
        if not n_mv:
            break
    # compact ids
    _, chart = np.unique(chart, return_inverse=True)
    if verbose:
        print(f'[paint.segment] faces={Fn} raw charts={n_raw} after merge={chart.max() + 1} eroded={moved}')
    if return_raw:
        return chart.astype(np.int32), raw.astype(np.int32), patch
    return chart.astype(np.int32)


def triangulate_ngons(obj, max_sides=4):
    """Triangulate polygons with > max_sides verts (BEAUTY). Vertices, vertex groups, materials and
    the triangle count are unchanged; the UVs then live on exactly the triangles Unity renders, so a
    concave boolean n-gon cannot fold its UVs. Returns the number of n-gons split."""
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    ng = [f for f in bm.faces if len(f.verts) > max_sides]
    if ng:
        bmesh.ops.triangulate(bm, faces=ng, quad_method='BEAUTY', ngon_method='BEAUTY')
        bm.to_mesh(me)
        me.update()
    bm.free()
    return len(ng)


def unwrap(obj, texture_size=2048, margin_px=8, keep_together=None, charts='developable', tau=0.02,
           angle_limit=60.0, method='MINIMUM_STRETCH', triangulate=True, verbose=True):
    """Create/overwrite UV map 'UVMap' with uniform texel density, packed into 0..1 with margin_px gaps.
    charts='developable' (default): segment_charts() (seams on hidden sides, big strip islands);
    charts='smart': Smart UV Project charts. Then min-stretch unwrap per chart -> average island
    scale (uniform px/m) -> pack (rotate, concave).
    keep_together: protect_from_decals(eyes) (eye footprints) or [(pos, normal, radius), ...] world
    patches - each stays inside one chart (never cut by a seam).
    Distorted / self-overlapping charts are detected after unwrapping and re-cut (raw charts, then
    Smart UV) automatically. margin_px = minimum gap between islands in texels.
    Returns stats (px_per_m, pack_eff, islands, density_p5/p95, aniso, charts_still_bad...)."""
    t0 = time.time()
    _activate(obj)
    me = obj.data
    n_ngons = triangulate_ngons(obj) if triangulate else 0
    uv = me.uv_layers.get(UV_NAME) or me.uv_layers.new(name=UV_NAME)   # other UV layers are left alone
    me.uv_layers.active = uv
    uv.active_render = True
    n_glued = 0
    n_split = 0
    if charts == 'developable':
        cid, raw, patch = segment_charts(obj, tau=tau, keep_together=keep_together, return_raw=True, verbose=verbose)
        _set_seams(obj, cid)
        keep_together = None
    bpy.ops.object.mode_set(mode='EDIT')
    ts = bpy.context.scene.tool_settings
    ts.use_uv_select_sync = True
    bpy.ops.mesh.select_mode(type='FACE')
    bpy.ops.mesh.reveal()
    bpy.ops.mesh.select_all(action='SELECT')
    if charts != 'developable':
        bpy.ops.mesh.mark_seam(clear=True)
        bpy.ops.uv.smart_project(angle_limit=math.radians(angle_limit), island_margin=0.0, area_weight=0.0,
                                 correct_aspect=True, scale_to_bounds=False)
        bpy.ops.uv.seams_from_islands(mark_seams=True, mark_sharp=False)
    if keep_together:
        bm = bmesh.from_edit_mesh(me)
        Mi = obj.matrix_world.inverted()
        M3t = obj.matrix_world.to_3x3().transposed()
        tag = {}
        for k, (p, n, r) in enumerate(keep_together):
            pl = Mi @ Vector(p)
            nl = (M3t @ Vector(n)).normalized()      # world normal -> local (exact for rotation+uniform scale)
            rl = r / max(obj.matrix_world.to_scale())
            for f in bm.faces:
                if (f.calc_center_median() - pl).length < rl and f.normal.dot(nl) > 0.2:
                    tag.setdefault(f.index, k)
        for e in bm.edges:
            if e.seam and len(e.link_faces) == 2:
                a, b = (tag.get(f.index) for f in e.link_faces)
                if a is not None and a == b:
                    e.seam = False
                    n_glued += 1
        bmesh.update_edit_mesh(me)
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.unwrap(method=method, fill_holes=True, correct_aspect=True, margin=0.0)
    n_bad_left = 0
    if charts == 'developable':
        # self-check: distorted charts -> 1st their raw (pre-merge) charts, 2nd Smart UV on those faces
        # (eye patches always stay one chart), each time re-unwrapped
        for it in range(4):
            bpy.ops.object.mode_set(mode='OBJECT')
            bad = _bad_charts(obj, cid)
            if not bad or it == 3:
                n_bad_left = len(bad)
                break
            sel = np.isin(cid, bad)
            n_split += len(bad)
            nxt = cid.max() + 1
            relax = sel
            if it == 0:
                cid = np.where(sel, nxt + raw, cid)
            elif it == 2:   # last resort for what is still folded: one island per face (patches kept)
                pt = sel & (patch >= 0)
                cid = np.where(pt, nxt + patch, np.where(sel, nxt + patch.max() + 1 + np.arange(len(cid)), cid))
            else:
                pt = sel & (patch >= 0)
                sm = sel & (patch < 0)
                cid = np.where(pt, nxt + patch, cid)
                if sm.any():
                    me.polygons.foreach_set('select', sm)
                    me.update()
                    bpy.ops.object.mode_set(mode='EDIT')
                    bpy.ops.uv.smart_project(angle_limit=math.radians(angle_limit), island_margin=0.0, area_weight=0.0,
                                             correct_aspect=True, scale_to_bounds=False)
                    bpy.ops.object.mode_set(mode='OBJECT')
                    isl = poly_islands(obj)
                    cid = np.where(sm, nxt + patch.max() + 1 + isl, cid)
                relax = pt          # keep the Smart UV projection as is (planar, no folds)
            _, cid = np.unique(cid, return_inverse=True)
            cid = cid.astype(np.int32)
            _set_seams(obj, cid)
            if not relax.any():
                continue
            me.polygons.foreach_set('select', relax)
            me.update()
            bpy.ops.object.mode_set(mode='EDIT')
            bpy.ops.uv.select_all(action='SELECT')
            bpy.ops.uv.unwrap(method=method, fill_holes=True, correct_aspect=True, margin=0.0)
        if obj.mode != 'EDIT':
            bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.average_islands_scale(scale_uv=False, shear=False)
    bpy.ops.uv.pack_islands(udim_source='CLOSEST_UDIM', rotate=True, rotate_method='ANY', scale=True,
                            merge_overlap=False, margin_method='FRACTION', margin=0.5 * margin_px / texture_size,
                            shape_method='CONCAVE')
    bpy.ops.object.mode_set(mode='OBJECT')
    st = uv_stats(obj, texture_size)
    st.update(time_s=round(time.time() - t0, 2), charts=charts, charts_split=n_split, charts_still_bad=n_bad_left,
              ngons_triangulated=n_ngons,
              margin_px=margin_px, method=method)
    if charts != 'developable':
        st.update(angle_limit=angle_limit, seams_unglued=n_glued)
    if verbose:
        print('[paint.unwrap] %s' % ', '.join(f'{k}={v}' for k, v in st.items()))
    return st


def _tri_jacobian(obj, m=None, uvl=None):
    """Per loop-triangle: singular values (s1>=s2) of the 3D->UV map, signed UV area, 3D area."""
    m = m or _mesh_arrays(obj)
    uvl = _uv_loops(obj) if uvl is None else uvl
    tl, tv, V = m['tl'], m['tv'], m['V']
    U = uvl[tl]
    e1, e2 = V[tv[:, 1]] - V[tv[:, 0]], V[tv[:, 2]] - V[tv[:, 0]]
    ax = e1 / np.maximum(np.linalg.norm(e1, axis=1, keepdims=True), 1e-12)
    nn = np.cross(e1, e2); nn /= np.maximum(np.linalg.norm(nn, axis=1, keepdims=True), 1e-12)
    ay = np.cross(nn, ax)
    E = np.stack([np.stack([(e1 * ax).sum(1), (e2 * ax).sum(1)], 1), np.stack([(e1 * ay).sum(1), (e2 * ay).sum(1)], 1)], 1)
    D = np.stack([U[:, 1] - U[:, 0], U[:, 2] - U[:, 0]], 2)
    good = np.abs(np.linalg.det(E)) > 1e-12
    s = np.zeros((len(tv), 2))
    J = D[good] @ np.linalg.inv(E[good])
    s[good] = np.linalg.svd(J, compute_uv=False)
    sa = 0.5 * _cross2(U[:, 1] - U[:, 0], U[:, 2] - U[:, 0])
    return s, sa, m['tA'], good


def _bad_charts(obj, cid, aniso_max=2.2, dens_lo=0.6, dens_hi=1.65, frac=0.06):
    """Charts whose unwrap is distorted (stretch, density spread or flipped triangles)."""
    m = _mesh_arrays(obj)
    s, sa, A3, good = _tri_jacobian(obj, m)
    tc = cid[m['tp']]
    # ignore degenerate boolean slivers (no texels to speak of, but infinite stretch / trivial overlap)
    V, tv = m['V'], m['tv']
    l2 = sum(((V[tv[:, i]] - V[tv[:, (i + 1) % 3]]) ** 2).sum(1) for i in range(3))
    qual = 4 * math.sqrt(3) * A3 / np.maximum(l2, 1e-18)
    good = good & (A3 > 1e-6) & (qual > 0.03)
    dens = np.sqrt(np.maximum(s[:, 0] * s[:, 1], 0))
    an = s[:, 0] / np.maximum(s[:, 1], 1e-12)
    bad = []
    # UV self-overlap (SLIM is locally injective only): charts with triangles on multiply-covered texels
    # (only overlaps inside one chart count: a partial uv.unwrap re-packs selected islands over others)
    uvl = _uv_loops(obj)
    *_, (ovf, ovt) = _rasterize(uvl[m['tl']] * 1024, 1024, return_hits=True)
    keep = good[ovt]
    ovf, ovt = ovf[keep], ovt[keep]
    if len(ovt):
        key = ovf.astype(np.int64) * (int(tc.max()) + 1) + tc[ovt]
        uk, kc = np.unique(key, return_counts=True)
        selfov = uk[kc > 1] % (int(tc.max()) + 1)
        oc, on = np.unique(selfov, return_counts=True)
        bad += [int(c) for c, k in zip(oc, on) if k >= 3]
    for c in np.unique(tc):
        if int(c) in bad:
            continue
        k = np.nonzero((tc == c) & good)[0]
        if not len(k):
            continue
        w = A3[k] / max(A3[k].sum(), 1e-12)
        med = np.median(dens[k]) or 1e-12
        flip = np.sign(sa[k]) != np.sign((sa[k] * A3[k]).sum())
        b = (an[k] > aniso_max) | (dens[k] / med < dens_lo) | (dens[k] / med > dens_hi) | flip
        if (w * b).sum() > frac:
            bad.append(int(c))
    return bad


def _set_seams(obj, cid):
    me = obj.data
    ne, nl = len(me.edges), len(me.loops)
    le = np.empty(nl, np.int32); me.loops.foreach_get('edge_index', le)
    lt = np.empty(len(me.polygons), np.int32); me.polygons.foreach_get('loop_total', lt)
    lp = np.repeat(np.arange(len(me.polygons)), lt)
    cnt = np.bincount(le, minlength=ne)
    cmin = np.full(ne, np.iinfo(np.int32).max); cmax = np.full(ne, -1)
    np.minimum.at(cmin, le, cid[lp]); np.maximum.at(cmax, le, cid[lp])
    seam = (cnt != 2) | (cmin != cmax)
    me.edges.foreach_set('use_seam', seam)
    me.update()
    return int(seam.sum())


def poly_islands(obj):
    """Per polygon UV island index (UVMap)."""
    from bpy_extras import bmesh_utils
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    isl = bmesh_utils.bmesh_linked_uv_islands(bm, bm.loops.layers.uv[UV_NAME])
    out = np.full(len(obj.data.polygons), -1, np.int32)
    for k, faces in enumerate(isl):
        for f in faces:
            out[f.index] = k
    bm.free()
    return out


def uv_stats(obj, texture_size=2048):
    """Texel density / packing / stretch statistics of UVMap."""
    m = _mesh_arrays(obj)
    s, sa, A3, good = _tri_jacobian(obj, m)
    uvA = np.abs(sa)
    ok = A3 > 1e-12
    dens = texture_size * math.sqrt(uvA.sum() / A3.sum())
    rel = np.sqrt(uvA[ok] / A3[ok]) * texture_size / dens
    w = A3[ok] / A3[ok].sum()

    def wq(x, q, ww=None):
        ww = w if ww is None else ww
        o = np.argsort(x); c = np.cumsum(ww[o]); return float(x[o][min(np.searchsorted(c, q), len(x) - 1)])
    g = good & ok
    an = s[g, 0] / np.maximum(s[g, 1], 1e-12)
    wa = A3[g] / A3[g].sum()
    tv = m['tv']
    # islands
    me = obj.data
    bm = bmesh.new(); bm.from_mesh(me)
    from bpy_extras import bmesh_utils
    isl = bmesh_utils.bmesh_linked_uv_islands(bm, bm.loops.layers.uv[UV_NAME])
    bm.free()
    return dict(px_per_m=round(dens, 1), pack_eff=round(float(uvA.sum()), 3), islands=len(isl),
                density_p5=round(wq(rel, 0.05), 2), density_p95=round(wq(rel, 0.95), 2),
                aniso_mean=round(float((an * wa).sum()), 2), aniso_p95=round(wq(an, 0.95, wa), 2), flipped_tris=int(((np.sign(sa) < 0) & g).sum()),
                area_m2=round(float(A3.sum()), 1), tris=len(tv))


# ============================================================================ 2. anchors
def _canon_ray(px, py):
    d = Vector(((px - L.CX0) / L.F_PX, 1.0, -(py - L.CY0) / L.F_PX)).normalized()
    return Vector(L.CAM), d


def anchor_from_canon(obj, px, py, want_groups=None, max_hits=8, smooth_radius=0.0):
    """Ray from the canon camera through canon pixel (px,py) onto obj (world space).
    Returns dict(pos, normal, group, face, dist, occluders, miss) or None if nothing is hit.
    want_groups: accept only hits on these limb groups facing the camera; occluding hits in front
    are skipped and listed in 'occluders'; if none qualifies the first hit is returned with 'miss' set.
    smooth_radius > 0: normal = area-weighted average of same-group, similarly facing faces nearby."""
    bvh = _bvh(obj)
    pg, names = face_groups(obj)
    o, d = _canon_ray(px, py)
    hits, start = [], o.copy()
    for _ in range(max_hits):
        loc, nrm, idx, dist = bvh.ray_cast(start, d)
        if loc is None:
            break
        g = names[pg[idx]] if pg[idx] >= 0 else None
        hits.append(dict(pos=loc, normal=nrm.normalized(), group=g, face=idx, dist=(loc - o).length))
        start = loc + d * 1e-3
    if not hits:
        return None
    pick, occ, miss = hits[0], [], None
    if want_groups:
        for h in hits:
            if h['group'] in want_groups and h['normal'].dot(-d) > 0.05:
                pick = h
                break
            occ.append(h['group'])
        else:
            pick, occ = hits[0], []
            miss = f"canon px ({px},{py}) hits {[h['group'] for h in hits]} - none of {list(want_groups)} facing the camera"
    res = dict(pick)
    res['occluders'] = occ
    res['miss'] = miss
    res['ray'] = d
    if smooth_radius > 0:
        res['normal'] = _smooth_normal(obj, res['pos'], res['normal'], res['group'], smooth_radius)
    return res


def _smooth_normal(obj, pos, n0, group, radius):
    m = _mesh_arrays(obj)
    pg, names = face_groups(obj)
    gi = names.index(group) if group in names else -1
    c, pn, pa = m['pc'], m['pn'], m['pa']
    sel = (np.linalg.norm(c - np.array(pos), axis=1) < radius) & (pn @ np.array(n0) > 0.35)
    if gi >= 0:
        sel &= pg == gi
    if not sel.any():
        return Vector(n0)
    w = pa[sel] * np.clip(pn[sel] @ np.array(n0), 0, 1)
    n = (pn[sel] * w[:, None]).sum(0)
    n = Vector(n).normalized()
    return n if n.length > 0.5 else Vector(n0)


# ============================================================================ 4. eye layout
# Canon measurements (00-canonical.png): almond width in px, tilt (deg, screen), iris look offset.
# size clamp = target range from the brief; 'up': 'world' or 'wrist' (back of support hands).
EYE_SPEC = {
    'E01': dict(groups=('S01',), w_px=52, clamp=(0.70, 0.90), roll=0, look=0.0, up='wrist'),
    'E02': dict(groups=('S02',), w_px=92, clamp=(1.20, 1.40), roll=0, look=0.0, up='wrist'),
    'E03': dict(groups=('S03',), w_px=58, clamp=(0.70, 0.90), roll=0, look=0.0, up='wrist'),
    'E04': dict(groups=('A01',), w_px=42, clamp=(0.45, 0.55), roll=-6, look=0.0, up='world'),
    'E05': dict(groups=('A02',), w_px=36, clamp=(0.45, 0.55), roll=-25, look=0.06, up='world'),
    'E06': dict(groups=('C01', 'CORE'), w_px=90, clamp=(1.20, 1.40), roll=0, look=0.0, up='world'),
    'E07': dict(groups=('CORE', 'C01'), w_px=40, clamp=(0.55, 0.70), roll=-32, look=0.16, up='world'),
    'E08': dict(groups=('CORE',), w_px=42, clamp=(0.55, 0.70), roll=8, look=0.10, up='world'),
    'E09': dict(groups=('CORE',), w_px=40, clamp=(0.55, 0.70), roll=16, look=0.10, up='world'),
    'E10': dict(groups=('CORE',), w_px=50, clamp=(0.55, 0.70), roll=-6, look=0.12, up='world'),
}


def _project_up(up, n):
    n = Vector(n).normalized()
    u = Vector(up) - n * Vector(up).dot(n)
    if u.length < 1e-4:
        for alt in ((0, 1, 0), (1, 0, 0)):
            u = Vector(alt) - n * Vector(alt).dot(n)
            if u.length > 1e-4:
                break
    return u.normalized()


def snap_to_group(obj, px, py, groups, min_facing=0.15):
    """Fallback anchor: the camera-facing point of limb `groups` closest to the canon ray of (px,py)
    (e.g. the canon pixel lands on a neighbouring finger -> nearest visible spot of the right hand).
    Returns an anchor dict like anchor_from_canon or None."""
    m = _mesh_arrays(obj)
    pg, names = face_groups(obj)
    gi = [names.index(g) for g in groups if g in names]
    if not gi:
        return None
    o, d = _canon_ray(px, py)
    o, dn = np.array(o), np.array(d)
    c, pn, pa = m['pc'], m['pn'], m['pa']
    sel = np.isin(pg, gi) & (pn @ -dn > min_facing) & (pa > 1e-6)
    if not sel.any():
        return None
    ids = np.nonzero(sel)[0]
    v = c[ids] - o
    perp = np.linalg.norm(v - np.outer(v @ dn, dn), axis=1)
    i = ids[np.argmin(perp)]
    pos = Vector(c[i])
    # make sure the chosen point is not hidden behind another surface of the same limb
    bvh = _bvh(obj)
    loc, nrm, idx, dist = bvh.ray_cast(Vector(o), (pos - Vector(o)).normalized())
    if loc is not None and idx != i and (loc - pos).length > 0.05 and pg[idx] in gi and nrm.dot(-d) > min_facing:
        pos, i = loc, idx
    return dict(pos=pos, normal=Vector(pn[i]), group=names[pg[i]], face=int(i), dist=(pos - Vector(o)).length,
                occluders=[], miss=None, ray=d, snapped=float(perp.min()))


def eye_decals_from_layout(obj, spec=None, anchors=None, snap=True, view_blend=0.35, verbose=True):
    """Eye decals for E01..E10 from layout.EYES canon pixels (anchor_from_canon).
    anchors: optional {eid: dict(pos, normal, up=None, size=None, group=None)} from the part builders
             (e.g. dorsal centre of the support hands) - used instead of the canon raycast.
    Canon pixels are hints: if the ray lands on the wrong limb and snap=True, the eye is moved to the
    camera-facing spot of the intended limb nearest to the ray (reported as 'snapped'); with
    snap=False (or if the limb is not found) the decal gets 'miss' and paint() skips it.
    Each decal: id, kind, pos, normal, up, size (canon estimate clamped to the brief's range), group,
    roll, look, est_size, occluders, note, miss.
    view_blend: body eyes (up='world') project along normalize(surface normal + view_blend*(to camera)),
    so a crease/junction normal at the hit cannot squash the eye in the canonical view; hand eyes
    (up='wrist') and builder anchors use the true surface normal."""
    spec = spec or EYE_SPEC
    anchors = anchors or {}
    out = []
    for eid, (px, py) in L.EYES.items():
        sp = spec.get(eid, dict(groups=None, w_px=50, clamp=(0.5, 1.4), roll=0, look=0, up='world'))
        note = ''
        if eid in anchors:
            an = anchors[eid]
            a = dict(pos=Vector(an['pos']), normal=Vector(an['normal']).normalized(), group=an.get('group') or sp['groups'][0],
                     occluders=[], miss=None, ray=_canon_ray(px, py)[1])
            note = 'builder anchor'
        else:
            a = anchor_from_canon(obj, px, py, want_groups=sp['groups'])
            if (a is None or a['miss']) and snap and sp['groups']:
                b = snap_to_group(obj, px, py, sp['groups'])
                if b is not None:
                    note = f"snapped to {b['group']} ({b['snapped']:.2f} m from ray; ray hit {a['group'] if a else None})"
                    a = b
        if a is None:
            out.append(dict(id=eid, kind='eye_open', miss=f'canon px ({px},{py}) hits nothing', pos=Vector(), normal=Vector((0, -1, 0)),
                            up=Vector((0, 0, 1)), size=1.0))
            if verbose:
                print(f'[paint.eyes] {eid}: MISS (no hit)')
            continue
        est0 = sp['w_px'] * (a['pos'].y - L.CAM.y) / L.F_PX
        n = a['normal'] if eid in anchors else _smooth_normal(obj, a['pos'], a['normal'], a['group'], est0 * 0.45)
        if eid not in anchors and sp['up'] != 'wrist' and view_blend:
            n = (n * (1.0 - view_blend) - a['ray'] * view_blend).normalized()
        if eid in anchors and anchors[eid].get('up') is not None:
            up = _project_up(anchors[eid]['up'], n)
        elif sp['up'] == 'wrist':
            hid = sp['groups'][0]
            w = L.SUPPORT_HANDS.get(hid, {}).get('wrist')
            up = _project_up((Vector(w) - a['pos']) if w is not None else Vector((0, 0, 1)), n)
        else:
            up = _project_up((0, 0, 1), n)
        # foreshortening of the horizontal eye axis in the canon view
        right = up.cross(n)
        d = a['ray']
        fs = (right - d * right.dot(d)).length
        est = est0 / max(fs, 0.6)
        lo, hi = sp['clamp']
        size = min(max(est, lo), hi)
        if eid in anchors and anchors[eid].get('size'):
            size = float(anchors[eid]['size'])
        dec = dict(id=eid, kind='eye_open', pos=a['pos'], normal=n, up=up, size=size, group=a['group'],
                   roll=sp.get('roll', 0), look=sp.get('look', 0.0), est_size=round(est, 3),
                   occluders=a['occluders'], miss=a['miss'], note=note, canon_px=(px, py))
        out.append(dec)
        if verbose:
            print(f"[paint.eyes] {eid}: group={a['group']} size={size:.2f} (canon est {est:.2f}) "
                  f"pos=({a['pos'].x:.2f},{a['pos'].y:.2f},{a['pos'].z:.2f}) n=({n.x:.2f},{n.y:.2f},{n.z:.2f})"
                  + (f" occluders={a['occluders']}" if a['occluders'] else '') + (f' [{note}]' if note else '')
                  + (f'  MISS: {a["miss"]}' if a['miss'] else ''))
    return out


def dash_decal_from_canon(obj, px, py, length_px, kind='eye_closed', roll=0.0, group=None, **kw):
    """Helper: closed-eye / mouth dash anchored at a canon pixel; size from canon length in px."""
    a = anchor_from_canon(obj, px, py, want_groups=(group,) if group else None)
    if group and (a is None or a['miss']):
        b = snap_to_group(obj, px, py, (group,))
        if b is not None:
            kw.setdefault('note', f"snapped to {group} ({b['snapped']:.2f} m from ray)")
            a = b
    if a is None:
        return dict(kind=kind, miss=f'canon px ({px},{py}) hits nothing', pos=Vector(), normal=Vector((0, -1, 0)), up=Vector((0, 0, 1)), size=0.3)
    size = length_px * (a['pos'].y - L.CAM.y) / L.F_PX
    n = a['normal']
    up = _project_up((0, 0, 1), n)
    d = dict(kind=kind, pos=a['pos'], normal=n, up=up, size=size, group=a['group'], roll=roll, miss=a['miss'], canon_px=(px, py))
    d.update(kw)
    return d


# ============================================================================ 3. paint
class _Noise3:
    """Deterministic smooth value noise (trilinear, smoothstep) on a periodic lattice."""

    def __init__(self, seed, G=64):
        self.G = G
        self.t = np.random.default_rng(seed).uniform(-1, 1, G * G * G).astype(np.float32)

    def __call__(self, p):
        G = self.G
        pf = np.floor(p)
        f = (p - pf).astype(np.float32)
        i = pf.astype(np.int64) % G
        u = f * f * (3 - 2 * f)
        x0, y0, z0 = i[:, 0], i[:, 1], i[:, 2]
        x1, y1, z1 = (x0 + 1) % G, (y0 + 1) % G, (z0 + 1) % G
        t = self.t

        def v(x, y, z):
            return t[(x * G + y) * G + z]
        ux, uy, uz = u[:, 0], u[:, 1], u[:, 2]
        a = v(x0, y0, z0) + (v(x1, y0, z0) - v(x0, y0, z0)) * ux
        b = v(x0, y1, z0) + (v(x1, y1, z0) - v(x0, y1, z0)) * ux
        c = v(x0, y0, z1) + (v(x1, y0, z1) - v(x0, y0, z1)) * ux
        d = v(x0, y1, z1) + (v(x1, y1, z1) - v(x0, y1, z1)) * ux
        e = a + (b - a) * uy
        g = c + (d - c) * uy
        return e + (g - e) * uz


def _sstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def _lerp(a, b, t):
    t = np.asarray(t, np.float32)
    if t.ndim == 1:
        t = t[:, None]
    return a + (b - a) * t


def _tone(base, light, shadow, tone):
    """tone in [-1,1]: >0 towards light, <0 towards shadow."""
    t = np.clip(tone, -1, 1)[:, None].astype(np.float32)
    return np.where(t >= 0, base + (light - base) * t, base + (shadow - base) * (-t))


def _rasterize(uvpx, size, chunk=3_000_000, return_hits=False):
    """uvpx (T,3,2) in texel units (texel (i,j) centre = (i+.5, j+.5), j from v=0 upward).
    Returns fid (size*size int32, -1 empty), barycentrics b1,b2 (float32), n_overlap texels."""
    T = len(uvpx)
    mn, mx = uvpx.min(1), uvpx.max(1)
    x0 = np.clip(np.ceil(mn[:, 0] - 0.5), 0, size - 1).astype(np.int64)
    x1 = np.clip(np.floor(mx[:, 0] - 0.5), 0, size - 1).astype(np.int64)
    y0 = np.clip(np.ceil(mn[:, 1] - 0.5), 0, size - 1).astype(np.int64)
    y1 = np.clip(np.floor(mx[:, 1] - 0.5), 0, size - 1).astype(np.int64)
    w = np.maximum(x1 - x0 + 1, 0)
    h = np.maximum(y1 - y0 + 1, 0)
    w[(mx[:, 0] - 0.5) < x0] = 0
    h[(mx[:, 1] - 0.5) < y0] = 0
    n = w * h
    fid = np.full(size * size, -1, np.int32)
    b1 = np.zeros(size * size, np.float32)
    b2 = np.zeros(size * size, np.float32)
    strict_all, hit_tris, hit_flat = [], [], []
    cs = np.cumsum(n)
    a = 0
    while a < T:
        b = int(np.searchsorted(cs, (cs[a - 1] if a else 0) + chunk, side='right'))
        b = max(b, a + 1)
        nn = n[a:b]
        tot = int(nn.sum())
        if tot:
            tri = np.repeat(np.arange(a, b), nn)
            k = np.arange(tot) - np.repeat(np.cumsum(nn) - nn, nn)
            ww = w[tri]
            xx = x0[tri] + k % ww
            yy = y0[tri] + k // ww
            A = uvpx[tri, 0]
            v0 = uvpx[tri, 1] - A
            v1 = uvpx[tri, 2] - A
            v2x = xx + 0.5 - A[:, 0]
            v2y = yy + 0.5 - A[:, 1]
            den = v0[:, 0] * v1[:, 1] - v1[:, 0] * v0[:, 1]
            den = np.where(np.abs(den) < 1e-12, np.nan, den)
            l1 = (v2x * v1[:, 1] - v1[:, 0] * v2y) / den
            l2 = (v0[:, 0] * v2y - v2x * v0[:, 1]) / den
            l0 = 1 - l1 - l2
            eps = -1e-6
            ins = (l0 >= eps) & (l1 >= eps) & (l2 >= eps)
            flat = yy * size + xx
            fid[flat[ins]] = tri[ins]
            b1[flat[ins]] = l1[ins]
            b2[flat[ins]] = l2[ins]
            st = (l0 > 1e-3) & (l1 > 1e-3) & (l2 > 1e-3)
            strict_all.append(flat[st])
            if return_hits:
                hit_tris.append(tri[st])
                hit_flat.append(flat[st])
        a = b
    sa = np.concatenate(strict_all) if strict_all else np.zeros(0, np.int64)
    cnt = np.bincount(sa, minlength=size * size)
    if return_hits:
        ht = np.concatenate(hit_tris) if hit_tris else np.zeros(0, np.int64)
        hf = np.concatenate(hit_flat) if hit_flat else np.zeros(0, np.int64)
        multi = cnt[sa] > 1
        return fid, b1, b2, int((cnt > 1).sum()), (hf[multi], ht[multi])
    return fid, b1, b2, int((cnt > 1).sum())


def _hemi_dirs(n):
    """Cosine-weighted hemisphere directions around +Z (deterministic spiral)."""
    i = np.arange(n) + 0.5
    r = np.sqrt(i / n)
    phi = i * math.pi * (3 - math.sqrt(5))
    return np.stack([r * np.cos(phi), r * np.sin(phi), np.sqrt(np.maximum(0, 1 - r * r))], 1)


def _loop_ao(obj, m, n_rays=20, dist=2.2):
    """Ambient occlusion per face corner (loop): rays from the corner pulled 15% to the face centre."""
    me = obj.data
    bvh = _bvh(obj)
    V, pn, pc = m['V'], m['pn'], m['pc']
    nl = len(me.loops)
    lv = np.empty(nl, np.int32); me.loops.foreach_get('vertex_index', lv)
    lp = np.empty(nl, np.int32)
    ls = np.empty(len(me.polygons), np.int32); me.polygons.foreach_get('loop_start', ls)
    lt = np.empty(len(me.polygons), np.int32); me.polygons.foreach_get('loop_total', lt)
    lp[:] = np.repeat(np.arange(len(me.polygons)), lt)
    H = _hemi_dirs(n_rays)
    ao = np.ones(nl, np.float32)
    # per-polygon frames
    for li in range(nl):
        p = lp[li]
        n = pn[p]
        t = np.cross(n, (1, 0, 0) if abs(n[0]) < 0.9 else (0, 1, 0)); t /= np.linalg.norm(t)
        b = np.cross(n, t)
        D = H[:, :1] * t + H[:, 1:2] * b + H[:, 2:] * n
        o = V[lv[li]] * 0.85 + pc[p] * 0.15 + n * 0.01
        oo = Vector(o)
        occ = 0.0
        for d in D:
            hit = bvh.ray_cast(oo, Vector(d), dist)
            if hit[0] is not None:
                occ += 1.0 - hit[3] / dist
        ao[li] = 1.0 - occ / n_rays
    return ao


def paint(obj, decals, out_path, size=2048, seed=0, ao=True, debug_path=None, ao_rays=20, ao_dist=2.2,
          glint=True, occlusion=True, verbose=True):
    """Rasterise obj's UVMap at size x size, paint regions + decals, bleed, write sRGB PNG.
    Returns stats dict (times, coverage, overlap texels, px/m, painted texels per decal, misses)."""
    T0 = time.time()
    tm = {}
    m = _mesh_arrays(obj)
    uvl = _uv_loops(obj)
    V, tv, tl, tp, pn = m['V'], m['tv'], m['tl'], m['tp'], m['pn']
    pr = face_regions(obj)
    pg, gnames = face_groups(obj)
    U = uvl[tl] * size
    fid, b1, b2, n_over = _rasterize(U, size)
    tm['raster'] = time.time() - T0
    cov = np.nonzero(fid >= 0)[0]
    t = fid[cov]
    l1, l2 = b1[cov][:, None], b2[cov][:, None]
    l0 = 1 - l1 - l2
    P = (l0 * V[tv[t, 0]] + l1 * V[tv[t, 1]] + l2 * V[tv[t, 2]]).astype(np.float32)
    face = tp[t]
    Nrm = pn[face].astype(np.float32)
    R = pr[face]
    G = pg[face]
    # texel size in metres (global density)
    uvA = 0.5 * np.abs(_cross2(U[:, 1] - U[:, 0], U[:, 2] - U[:, 0]))
    px_per_m = math.sqrt(uvA.sum() / m['tA'].sum())
    texel_m = 1.0 / px_per_m

    # ---------------------------------------------------------------- AO
    if ao:
        t1 = time.time()
        try:
            aol = _loop_ao(obj, m, n_rays=ao_rays, dist=ao_dist)
            A = (l0[:, 0] * aol[tl[t, 0]] + l1[:, 0] * aol[tl[t, 1]] + l2[:, 0] * aol[tl[t, 2]]).astype(np.float32)
        except Exception as ex:  # pragma: no cover
            print('[paint] AO failed, skipped:', ex)
            A = np.ones(len(cov), np.float32)
        tm['ao'] = time.time() - t1
    else:
        A = np.ones(len(cov), np.float32)

    # ---------------------------------------------------------------- base regions
    t1 = time.time()
    nz = _Noise3(seed * 7 + 1)
    n1 = nz(P / 2.8) + 0.55 * nz(P / 1.25 + 31.7)
    n1 /= (n1.std() + 1e-6) * 2.2
    patch = _sstep(-0.22, 0.22, n1) * 2 - 1           # coarse painted patches (soft plateaus)
    up = Nrm[:, 2]
    occl = np.clip(1.0 - A, 0.0, 1.0)
    col = np.empty((len(cov), 3), np.float32)

    def fhash(f, k):
        x = (f.astype(np.uint64) * np.uint64(2654435761) + np.uint64(k * 97 + seed * 13)) % np.uint64(1000003)
        return (x.astype(np.float64) / 1000003.0).astype(np.float32)

    s = R == 0
    tone = 0.26 * patch[s] + 0.14 * n1[s] + 0.16 * up[s] - 1.1 * occl[s] ** 1.2
    col[s] = _tone(COL['body'], COL['body_light'], COL['body_shadow'], tone)

    s = R == 1  # beard: pale with a few darker long streak planes
    if s.any():
        q = P[s]
        st = nz(q * np.array([1 / 0.16, 1 / 0.16, 1 / 1.6], np.float32) + 11.0)
        st /= st.std() + 1e-6
        dark = _sstep(0.55, 0.8, st) * 0.75 + (fhash(face[s], 3) < 0.22) * 0.45
        c = _tone(COL['beard'], COL['beard_light'], COL['beard_dark'], 0.18 * up[s] + 0.12 * n1[s] - 0.9 * occl[s])
        col[s] = _lerp(c, COL['beard_dark'], np.clip(dark, 0, 0.8))

    s = R == 2  # hat: darker brim underside and band
    if s.any():
        q = P[s].astype(np.float64)
        wgt = m['pa'][face[s]]
        c0 = q.mean(0)
        ev, evec = np.linalg.eigh(np.cov((q - c0).T))
        ax = evec[:, 2]
        if ax[2] < 0:
            ax = -ax
        h = (q - c0) @ ax
        lo, hi = np.percentile(h, 1), np.percentile(h, 99.5)
        hn = (h - lo) / max(hi - lo, 1e-6)
        under = (Nrm[s] @ ax.astype(np.float32)) < -0.3
        band = (hn > 0.09) & (hn < 0.19)
        c = _tone(COL['hat'], COL['hat_light'], COL['hat_dark'], 0.2 * n1[s] + 0.25 * hn - 0.05 - 0.9 * occl[s])
        c = np.where(band[:, None], _lerp(c, COL['hat_dark'], 0.75), c)
        c = np.where(under[:, None], COL['hat_dark'] * 0.92, c)
        col[s] = c

    s = R == 3  # branch stems: blue family
    if s.any():
        col[s] = _tone(COL['branch'], COL['branch_light'], COL['branch_dark'], 0.4 * n1[s] + 0.15 * up[s] - 0.9 * occl[s])
    tm['base'] = time.time() - t1

    # ---------------------------------------------------------------- decals
    t1 = time.time()
    dstats, misses, disl = {}, [], {}
    bvh = _bvh(obj) if occlusion else None
    pisl = poly_islands(obj)
    for k, d in enumerate(decals):
        did = d.get('id') or f"{d.get('kind')}_{k}"
        if d.get('miss'):
            misses.append(f'{did}: {d["miss"]}')
            continue
        pidx = _paint_decal(col, P, Nrm, G, gnames, d, texel_m, glint, bvh=bvh)
        dstats[did] = int(len(pidx))
        if len(pidx) == 0:
            misses.append(f'{did}: painted 0 texels')
        elif d.get('kind') == 'eye_open':
            disl[did] = int(len(np.unique(pisl[face[pidx]])))
    tm['decals'] = time.time() - t1

    # ---------------------------------------------------------------- bleed + write
    t1 = time.time()
    img = np.zeros((size * size, 3), np.float32)
    img[cov] = col
    img = img.reshape(size, size, 3)
    mask = (fid >= 0).reshape(size, size)
    from scipy import ndimage
    idx = ndimage.distance_transform_edt(~mask, return_distances=False, return_indices=True)
    img = img[idx[0], idx[1]]
    out8 = np.clip(np.round(img * 255), 0, 255).astype(np.uint8)[::-1]   # row 0 = top (v=1)
    from PIL import Image
    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    Image.fromarray(out8, 'RGB').save(out_path, optimize=False)
    tm['write'] = time.time() - t1
    # ---------------------------------------------------------------- debug UV image
    if debug_path is None:
        os.makedirs(DEBUG_DIR, exist_ok=True)
        debug_path = os.path.join(DEBUG_DIR, os.path.splitext(os.path.basename(out_path))[0] + f'_uvdebug_{size}.png')
    if debug_path:
        _debug_image(obj, out8, uvl, size, debug_path, decals, P, Nrm, cov)
    stats = dict(out=out_path, debug=debug_path, size=size, px_per_m=round(px_per_m, 1),
                 texel_mm=round(texel_m * 1000, 1), coverage=round(len(cov) / size / size, 3),
                 overlap_texels=n_over, decal_texels=dstats, eye_islands=disl, misses=misses,
                 times={k: round(v, 2) for k, v in tm.items()}, total_s=round(time.time() - T0, 2))
    if verbose:
        print('[paint.paint]', {k: v for k, v in stats.items() if k not in ('decal_texels',)})
        print('[paint.paint] decal texels', dstats)
        print('[paint.paint] UV islands per eye (1 = never split by a seam)', disl)
    return stats


def _decal_frame(d):
    n = Vector(d['normal']).normalized()
    u = _project_up(d.get('up') or (0, 0, 1), n)
    roll = math.radians(d.get('roll', 0.0) or 0.0)
    if roll:
        u = (Matrix.Rotation(roll, 3, n) @ u).normalized()
    r = u.cross(n).normalized()
    return np.array(n, np.float32), np.array(u, np.float32), np.array(r, np.float32)


def _paint_decal(col, P, Nrm, G, gnames, d, texel_m, glint=True, bvh=None):
    kind = d.get('kind', 'eye_open')
    W = float(d['size'])
    hw = W / 2
    n, u, r = _decal_frame(d)
    p = np.array(d['pos'], np.float32)
    D = P - p
    near = np.nonzero((D * D).sum(1) < (0.85 * W) ** 2)[0]
    if not len(near):
        return near
    Dn = D[near]
    z = Dn @ n
    ok = (np.abs(z) < 0.35 * W) & (Nrm[near] @ n > 0.3)
    grp = d.get('group')
    if grp is not None:
        gi = gnames.index(grp) if grp in gnames else -2
        ok &= G[near] == gi
    idx = near[ok]
    if bvh is not None and len(idx):
        # projector occlusion: only the first surface along the decal normal is painted (no copies on a
        # finger lying over the palm, on the far wall of a crease, etc.)
        nv = Vector(n.tolist())
        lim = 0.35 * W
        keep = np.ones(len(idx), bool)
        for k, i in enumerate(idx):
            o = Vector(P[i].tolist()) + Vector(Nrm[i].tolist()) * 2e-3 + nv * 2e-3
            if bvh.ray_cast(o, nv, lim)[0] is not None:
                keep[k] = False
        idx = idx[keep]
    if not len(idx):
        return idx
    x = (D[idx] @ r) / hw
    y = (D[idx] @ u) / hw
    tmin = texel_m / hw                     # one texel in half-width units
    c = col[idx].copy()
    if kind == 'eye_open':
        asp = (d.get('aspect') or 0.42) / 0.42
        painted = _eye(c, x, y / asp, tmin / asp, float(d.get('look', 0.0) or 0.0), glint)
    elif kind in ('eye_closed', 'mouth', 'brow'):
        painted = _dash(c, x, y, tmin, kind, d)
    else:
        raise ValueError('unknown decal kind ' + str(kind))
    col[idx] = c
    return idx[painted]


def _eye(c, x, y, tm, look, glint):
    """Almond eye in half-width units (x in [-1,1], visible opening height ~0.5)."""
    ax = np.abs(x)
    s = np.clip(1 - x * x, 0, 1)
    su = np.clip(1 - ax ** 2.3, 0, 1)
    yt = 0.29 * su ** 0.72 + 0.02 * x * s          # upper lid edge: full round arc, pointed corners
    yb = -0.21 * np.clip(1 - ax ** 2.1, 0, 1) ** 0.85   # lower lid edge: flatter arc
    w_tex = 2.0 / tm                               # eye width in texels
    t_lash = np.maximum(0.055 * (0.4 + 0.6 * np.sqrt(s)), 1.05 * tm)
    t_lid = 0.12 * s ** 0.6
    t_low = np.maximum(0.03 * (0.5 + 0.5 * s), 0.95 * tm)
    t_rim = np.maximum(0.065 * np.sqrt(s), 0.95 * tm) * (s > 0.12)
    inx = ax <= 1.0
    painted = np.zeros(len(x), bool)
    base = c.copy()
    # faint lower-lid shade
    y_r = yb - t_low - t_rim
    m = inx & (y < y_r) & (y >= y_r - 0.13 * s) & (ax < 0.95)
    c[m] = _lerp(base[m], COL['body_shadow'], 0.32); painted |= m
    # crease above the upper lid
    y_c = yt + t_lash + t_lid + 0.07 * s + 0.01
    t_c = np.maximum(0.035, 1.4 * tm)
    if w_tex >= 44:                                 # crease only where it can be drawn cleanly
        m = (ax < 0.78) & (y >= y_c) & (y < y_c + t_c)
        c[m] = _lerp(base[m], COL['body_shadow'], 0.5); painted |= m
    # upper lid band (dark blue) + lash line (stroke)
    m = inx & (y >= yt + t_lash) & (y < yt + t_lash + t_lid)
    c[m] = COL['lid']; painted |= m
    m = inx & (y >= yt) & (y < yt + t_lash)
    c[m] = COL['stroke']; painted |= m
    # pointed corners
    ext = max(0.06, 1.0 * tm)
    m = (ax > 1.0) & (ax < 1.0 + ext) & (np.abs(y) < np.maximum(t_lash * 0.55, 0.6 * tm) * (1 - (ax - 1) / ext))
    c[m] = COL['stroke']; painted |= m
    # lower outline + light lower rim
    m = inx & (y < yb) & (y >= yb - t_low)
    c[m] = _lerp(COL['stroke'], COL['lid'], 0.4); painted |= m
    m = (ax < 0.97) & (y < yb - t_low) & (y >= yb - t_low - t_rim)
    c[m] = COL['lid_rim']; painted |= m
    # opening: sclera, iris, pupil, glint, lid shadow
    op = inx & (y < yt) & (y >= yb)
    if op.any():
        xo, yo = x[op], y[op]
        cc = _lerp(COL['sclera'], COL['sclera_corner'], _sstep(0.45, 1.0, np.abs(xo)))
        cx, cy = look, -0.01
        ri = 0.31
        rr = np.hypot(xo - cx, yo - cy)
        ir = rr < ri
        cc[ir] = COL['iris']
        rim = ir & (rr > ri - max(0.055, 0.9 * tm))
        cc[rim] = COL['iris_rim']
        rp = max(0.105, 1.5 * tm)                  # >= 3x3 texel block, never a '+'
        pu = np.hypot(xo - cx, yo - (cy + 0.03)) < rp
        cc[pu] = COL['pupil']
        if glint and rp / tm >= 3.0:
            rg = max(0.035, 0.75 * tm)
            gl = np.hypot(xo - (cx - 0.045), yo - (cy + 0.085)) < rg
            cc[gl] = COL['glint']
        so = s[op]
        sh = yo > (yt[op] - np.maximum(0.10 * np.sqrt(so) + 0.015, 0.9 * tm))
        cc[sh & ~pu] *= 0.70
        c[op] = cc
        painted |= op
    return painted


def _dash(c, x, y, tm, kind, d):
    """Closed eye / mouth / brow stroke in half-width units."""
    th = d.get('thickness') or {'eye_closed': 0.12, 'mouth': 0.13, 'brow': 0.10}[kind]
    bend = d.get('bend')
    if bend is None:
        bend = {'eye_closed': 0.03, 'mouth': 0.09, 'brow': 0.10}[kind]
    ax = np.abs(x)
    vc = bend * (1 - x * x) - bend * 0.5
    t = np.maximum(2 * th * np.sqrt(np.clip(1 - ax ** 4, 0, 1)), 1.05 * tm)
    m = (ax <= 1) & (np.abs(y - vc) < t / 2)
    base = c.copy()
    if kind == 'brow':
        c[m] = _lerp(base[m], COL['body_shadow'], 0.7)
        return m
    c[m] = COL['stroke']
    hl = (ax <= 0.88) & (y < vc - t / 2) & (y >= vc - t / 2 - np.maximum(0.45 * t, 0.95 * tm))
    c[hl] = _lerp(base[hl], COL['body_light'], 0.55)
    return m | hl


def _debug_image(obj, out8, uvl, size, path, decals, P, Nrm, cov):
    from PIL import Image, ImageDraw
    im = Image.fromarray(out8, 'RGB')
    im = Image.blend(im, Image.new('RGB', im.size, (0, 0, 0)), 0.25)
    dr = ImageDraw.Draw(im)
    me = obj.data
    ls = np.empty(len(me.polygons), np.int32); me.polygons.foreach_get('loop_start', ls)
    lt = np.empty(len(me.polygons), np.int32); me.polygons.foreach_get('loop_total', lt)
    UV = uvl * size
    UV[:, 1] = size - UV[:, 1]
    for a, n in zip(ls, lt):
        pts = [tuple(UV[a + i]) for i in range(n)]
        dr.line(pts + [pts[0]], fill=(235, 235, 120), width=1)
    # decal labels at the texel nearest to each decal position
    for k, d in enumerate(decals):
        if d.get('miss'):
            continue
        p = np.array(d['pos'], np.float32)
        dd = ((P - p) ** 2).sum(1) - 50.0 * np.clip(Nrm @ np.array(d['normal'], np.float32), -1, 0)
        i = int(np.argmin(dd))
        flat = cov[i]
        yy, xx = divmod(int(flat), size)
        yy = size - 1 - yy
        lab = d.get('id') or d.get('kind', '?')[:5]
        dr.rectangle([xx - 2, yy - 2, xx + 2, yy + 2], outline=(255, 40, 40))
        dr.text((xx + 4, yy - 6), str(lab), fill=(255, 60, 60))
    im.save(path)
    return path


# ============================================================================ material
def assign_texture(obj, image_path, mat_name='M_MG01_Body', interpolation='Closest', replace_slots=True):
    """Body material with an Image Texture (Closest, Extend) feeding a matte Principled BSDF.
    Regions are first saved to the face attribute 'mg_region', then all slots are replaced."""
    store_regions(obj)
    img = bpy.data.images.load(image_path, check_existing=True)
    img.reload()
    img.colorspace_settings.name = 'sRGB'
    mat = bpy.data.materials.get(mat_name) or bpy.data.materials.new(mat_name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    bs = nt.nodes.new('ShaderNodeBsdfPrincipled')
    bs.inputs['Metallic'].default_value = 0.0
    bs.inputs['Roughness'].default_value = 1.0
    tex = nt.nodes.new('ShaderNodeTexImage')
    tex.image = img
    tex.interpolation = interpolation
    tex.extension = 'EXTEND'
    nt.links.new(tex.outputs['Color'], bs.inputs['Base Color'])
    nt.links.new(bs.outputs['BSDF'], out.inputs['Surface'])
    nt.nodes.active = tex
    if replace_slots:
        me = obj.data
        me.materials.clear()
        me.materials.append(mat)
        z = np.zeros(len(me.polygons), np.int32)
        me.polygons.foreach_set('material_index', z)
        me.update()
    return mat
