"""God mountains asset kit (A01–A09 + G01 frame + G03 hollow ring), built with Blender's bpy.

Every rock part is the convex hull of hand-placed points (big planes, chamfered corners, crooked tiers), coplanar
triangles merged into flat faces. UVs are planar per face in metres (Unity materials tile 4 m per 256 px); faces looking
up get slot "GM_Top" (lichen, dust), the rest "GM_Side". Plants use the RGBA atlas directly ("GM_Leaf").
Front of every asset is -Y (toward the player / the path), up is +Z, pivot as noted per asset.

usage: python3 kit.py <tex dir> <out dir> [--render <png>]
"""
import math
import os
import random
import sys

import bpy  # noqa: E402  (bmesh is only importable after bpy)
import bmesh
from mathutils import Vector

TEX, OUT = sys.argv[1], sys.argv[2]
RENDER = sys.argv[sys.argv.index("--render") + 1] if "--render" in sys.argv else None
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


# ------------------------------------------------------------------ materials (for preview renders; Unity remaps by name)

def image_mat(name, image, scale, alpha=False):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 1
    if "Specular IOR Level" in bsdf.inputs:
        bsdf.inputs["Specular IOR Level"].default_value = 0
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(os.path.join(TEX, image))
    tex.interpolation = "Closest"
    uv = nt.nodes.new("ShaderNodeTexCoord")
    mp = nt.nodes.new("ShaderNodeMapping")
    mp.inputs["Scale"].default_value = (scale, scale, 1)
    nt.links.new(uv.outputs["UV"], mp.inputs["Vector"])
    nt.links.new(mp.outputs["Vector"], tex.inputs["Vector"])
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    if alpha:
        nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
        m.blend_method = "CLIP" if hasattr(m, "blend_method") else None
    return m


MAT = {
    "GM_Top": image_mat("GM_Top", "T_GM_RockTop.png", .25),
    "GM_Side": image_mat("GM_Side", "T_GM_RockSide.png", .25),
    "GM_FarTop": image_mat("GM_FarTop", "T_GM_Far.png", .25),
    "GM_FarSide": image_mat("GM_FarSide", "T_GM_Far.png", .25),
    "GM_Leaf": image_mat("GM_Leaf", "T_GM_Foliage.png", 1, alpha=True),
}
_crack = bpy.data.materials.new("GM_Crack")
_crack.use_nodes = True
_crack.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (.07, .05, .08, 1)
_crack.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 1
MAT["GM_Crack"] = _crack

rng = random.Random(20261112)


# ------------------------------------------------------------------ geometry helpers

def V(*a):
    return Vector(a)


def _hull_bm(pts):
    bm = bmesh.new()
    for p in pts:
        bm.verts.new(V(*p))
    bmesh.ops.convex_hull(bm, input=bm.verts)
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(1.0), verts=bm.verts, edges=bm.edges)
    return bm


def _split(pts, co, no):
    """Split the convex hull of pts by a plane: point sets of both sides (plus the section)."""
    bm = _hull_bm(pts)
    neg, pos = [], []
    for v in bm.verts:
        d = (v.co - co).dot(no)
        (neg if d <= 0 else pos).append(tuple(v.co))
    for e in bm.edges:
        a, b = e.verts
        da, db = (a.co - co).dot(no), (b.co - co).dot(no)
        if da * db < 0:
            q = tuple(a.co.lerp(b.co, da / (da - db)))
            neg.append(q)
            pos.append(q)
    bm.free()
    return neg, pos


def _volume_ok(pts):
    if len(pts) < 4:
        return False
    xs, ys, zs = zip(*pts)
    return min(max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs)) > .06


def _finish(name, bm, parent, far, bevel, uv_shift):
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, offset_type="OFFSET", segments=1, profile=.5,
                        affect="EDGES", clamp_overlap=True)
    bm.normal_update()
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    top, side = ("GM_FarTop", "GM_FarSide") if far else ("GM_Top", "GM_Side")
    me.materials.append(MAT[top])
    me.materials.append(MAT[side])
    uvl = me.uv_layers.new(name="UV")
    sh = uv_shift or (rng.uniform(0, 4), rng.uniform(0, 4))
    for poly in me.polygons:
        n = poly.normal
        poly.material_index = 0 if n.z > .6 else 1
        poly.use_smooth = False
        if abs(n.z) > .7:
            ax = (V(1, 0, 0), V(0, 1, 0))
        else:
            t = V(0, 0, 1).cross(n).normalized()
            ax = (t, V(0, 0, 1))
        for li in poly.loop_indices:
            p = me.vertices[me.loops[li].vertex_index].co
            uvl.data[li].uv = (p.dot(ax[0]) + sh[0], p.dot(ax[1]) + sh[1])
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    ob.parent = parent
    return ob


def hull_part(name, pts, parent, far=False, uv_shift=None, bevel=0.0):
    """Convex hull of points -> one object with flat faces, metric UVs and Top/Side material slots."""
    return _finish(name, _hull_bm(pts), parent, far, bevel, uv_shift)


def rock(name, pts, parent, cuts=(), chips=3, chip=(.15, .4), bevel=.07, far=False, keep_front=None, seed=None):
    """A detailed rock mass: the hull of pts fractured by planes (strata, cracks) into convex pieces, each with its
    corners chipped and its edges bevelled. Everything is cut away from the hull, nothing added: the rock never
    reaches past the simple hull it was designed as. keep_front: (axis, value) — chips never touch faces lying on
    that plane below z 3.2 (the walkway side of corridor walls stays a plane)."""
    r = random.Random(seed if seed is not None else rng.random())
    pieces = [list(pts)]
    for co, no in cuts:
        co, no = V(*co), V(*no).normalized()
        nxt = []
        for pc in pieces:
            a, b = _split(pc, co, no)
            if _volume_ok(a) and _volume_ok(b):
                nxt += [a, b]
            else:
                nxt.append(pc)
        pieces = nxt
    objs = []
    for i, pc in enumerate(pieces):
        for _ in range(chips):
            bm = _hull_bm(pc)
            verts = [v.co.copy() for v in bm.verts]
            bm.free()
            c = sum(verts, V(0, 0, 0)) / len(verts)
            vtx = r.choice(verts)
            if keep_front and abs(vtx[keep_front[0]] - keep_front[1]) < .02 and vtx.z < 3.4:
                continue
            d = (vtx - c)
            if d.length < .3:
                continue
            no = (d.normalized() + V(r.uniform(-.3, .3), r.uniform(-.3, .3), r.uniform(-.3, .3))).normalized()
            size = r.uniform(*chip)
            inner, _ = _split(pc, vtx - no * size, no)
            if _volume_ok(inner):
                pc = inner
        objs.append(_finish(f"{name}_{i}", _hull_bm(pc), parent, far, bevel, None))
    return objs


from mathutils import noise as mnoise  # noqa: E402
from mathutils.bvhtree import BVHTree  # noqa: E402


def smooth_rock(name, pts, parent, bulge=.05, cuts=2, wobble=.03, bevel=.06, keep_front=False, far=False, seed=0, relief=.32):
    """Concept rock: one convex mass of big planes; edges with a narrow bevel; every face subdivided and the whole mass
    gently swollen (face middles out, corners stay) with a little low noise: simple, slightly crooked, never lumpy.
    keep_front: nothing comes forward of the plane y = 0 below 3.4 m (the path side of corridor walls)."""
    bm = _hull_bm(pts)
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, offset_type="OFFSET", segments=1, profile=.5,
                        affect="EDGES", clamp_overlap=True)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    long_edges = [e for e in bm.edges if e.calc_length() > bevel * 4 + .25]
    if cuts and long_edges:
        bmesh.ops.subdivide_edges(bm, edges=long_edges, cuts=cuts, use_grid_fill=True)
        bmesh.ops.triangulate(bm, faces=bm.faces)
    vs = [v.co.copy() for v in bm.verts]
    lo = V(min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs))
    hi = V(max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs))
    c = (lo + hi) / 2
    half = (hi - lo) / 2
    off = V(seed * 13.1, seed * 7.7, seed * 3.3)
    for v in bm.verts:
        q = v.co - c
        nq = V(q.x / max(half.x, 1e-3), q.y / max(half.y, 1e-3), q.z / max(half.z, 1e-3))
        d = min(1.0, nq.length / 1.732)
        # swell: points well inside the bounding shape (face middles) move out, extreme corners stay
        v.co = v.co + q * (bulge * (1 - d) ** 1.5)
        sc = max(half.x, half.y, half.z)
        v.co = v.co + mnoise.noise_vector(v.co / (sc * .9) + off) * wobble * sc
    if keep_front:
        # The path side: nothing forward of y = 0 below 3.4 m, but not a plane either. Swells fold back into
        # recesses and a coarse noise digs shallow hollows (up to `relief` m), so the face reads as worn rock.
        for v in bm.verts:
            if v.co.z < 3.6 and v.co.y < relief + .05:
                n = mnoise.noise(V(v.co.x * .55, v.co.z * .55, seed * 1.7)) * .5 + .5
                n2 = mnoise.noise(V(v.co.x * 1.6 + 9, v.co.z * 1.6, seed * 2.3)) * .5 + .5
                fade = 1.0 if v.co.z < 3.0 else max(0.0, (3.6 - v.co.z) / .6)
                v.co.y = abs(v.co.y) + (relief * (n * .75 + n2 * .25) ** 1.4) * fade
    return _finish(name, bm, parent, far, 0, None)


def detail_rock(name, pts, parent, cuts=(), chips=4, chip=(.2, .55), bulge=.035, bevel=.06, wobble=.018,
                keep_front=False, far=False, seed=0, cuts_sub=2, relief=.32):
    """Concept silhouette with detail: the hull of pts split by planes (dipping beds, oblique joints) into a few
    unequal blocks, corners chipped, every block bevelled and gently swollen, so the joints read as cracks and grooves.
    Only cuts, never additions: the rock stays inside its silhouette."""
    r = random.Random(seed)
    pieces = [list(pts)]
    for co, no in cuts:
        co, no = V(*co), V(*no).normalized()
        nxt = []
        for pc in pieces:
            a, b = _split(pc, co, no)
            if _volume_ok(a) and _volume_ok(b):
                nxt += [a, b]
            else:
                nxt.append(pc)
        pieces = nxt
    obs = []
    for i, pc in enumerate(pieces):
        for _ in range(chips):
            bm = _hull_bm(pc)
            verts = [v.co.copy() for v in bm.verts]
            bm.free()
            c = sum(verts, V(0, 0, 0)) / len(verts)
            vtx = r.choice(verts)
            d = vtx - c
            if d.length < .4:
                continue
            no = (d.normalized() + V(r.uniform(-.35, .35), r.uniform(-.35, .35), r.uniform(-.35, .35))).normalized()
            inner, _ = _split(pc, vtx - no * r.uniform(*chip), no)
            if _volume_ok(inner):
                pc = inner
        if keep_front:
            # Each block of the front sits back by its own few cm: the joints become steps, not just lines.
            back = r.uniform(0, .38)
            pc = [(V(*q) + V(0, back, 0)) if V(*q).y < .06 else V(*q) for q in pc]
        obs.append(smooth_rock(f"{name}_{i}", pc, parent, bulge=bulge, cuts=cuts_sub, wobble=wobble, bevel=bevel,
                               keep_front=keep_front, far=far, seed=seed * 7 + i, relief=relief))
    return obs


def joints(spec, seed):
    """spec: list of ('bed', z, tilt) or ('joint', x, z, tilt) -> cutting planes."""
    r = random.Random(seed)
    out = []
    for item in spec:
        if item[0] == "bed":
            _, z, tilt = item
            ax, ay = math.radians(r.uniform(-tilt, tilt)), math.radians(r.uniform(-tilt, tilt))
            out.append(((0, 0, z), (math.sin(ax), math.sin(ay), 1)))
        else:
            _, x, z, tilt = item
            az = math.radians(r.uniform(-35, 35))
            dip = math.radians(r.uniform(8, tilt))
            out.append(((x, 0, z), (math.cos(az) * math.cos(dip), math.sin(az) * math.cos(dip), math.sin(dip) * r.choice((-1, 1)))))
    return out


def paint_crack(ob, start, direction, length, normal, seed, width=.05):
    """A painted crack as on the concepts: a jagged dark strip lying on the surface (no collider)."""
    r = random.Random(seed)
    dg = bpy.context.evaluated_depsgraph_get()
    ob.data.update()
    bvh = BVHTree.FromObject(ob, dg)
    n = V(*normal).normalized()
    d = V(*direction).normalized()
    side = n.cross(d).normalized()
    pts = []
    p = V(*start)
    step = length / 7
    for i in range(8):
        hit = bvh.ray_cast(p + n * 2.0, -n, 6.0)
        if hit[0] is None:
            break
        pts.append(hit[0] + hit[1] * .008)
        p = p + d * step + side * r.uniform(-.5, .5) * step
    if len(pts) < 3:
        return None
    bm = bmesh.new()
    rows = []
    for i, q in enumerate(pts):
        w = width * (1 - abs(i / (len(pts) - 1) - .4)) + .01
        rows.append((bm.verts.new(q - side * w), bm.verts.new(q + side * w)))
    for (a0, a1), (b0, b1) in zip(rows, rows[1:]):
        bm.faces.new((a0, a1, b1, b0))
    me = bpy.data.meshes.new(ob.name + "_crack")
    bm.to_mesh(me)
    bm.free()
    me.materials.append(MAT["GM_Crack"])
    cr = bpy.data.objects.new(ob.name + "_crack", me)
    bpy.context.collection.objects.link(cr)
    cr.parent = ob.parent
    return cr


def strata(z_levels, tilt=6, x_range=(-1, 1), seed=None):
    r = random.Random(seed)
    out = []
    for z in z_levels:
        ax, ay = math.radians(r.uniform(-tilt, tilt)), math.radians(r.uniform(-tilt, tilt))
        out.append(((0, 0, z), (math.sin(ax), math.sin(ay), 1)))
    return out


def cracks(xs, tilt=10, seed=None, z=0.0):
    """Joints: steep planes through (x, ., z) with a random azimuth (±40°) and dip (60–85°)."""
    r = random.Random(seed)
    out = []
    for x in xs:
        az = math.radians(r.uniform(-40, 40))
        dip = math.radians(r.uniform(5, 30) + tilt * .5)
        out.append(((x, 0, z), (math.cos(az) * math.cos(dip), math.sin(az) * math.cos(dip), math.sin(dip) * r.choice((-1, 1)))))
    return out


def pebble(name, at, size, parent, seed):
    r = random.Random(seed)
    x, y, z = at
    pts = [(x + r.uniform(-1, 1) * size, y + r.uniform(-1, 1) * size * .8, z + r.uniform(0, 1) * size * .7) for _ in range(9)]
    pts += [(x + r.uniform(-1, 1) * size, y + r.uniform(-1, 1) * size * .8, z) for _ in range(4)]
    return hull_part(name, pts, parent, bevel=min(.04, size * .12))


def stones_on(e, name, spots, seed):
    r = random.Random(seed)
    for i, (x, y, z, sz) in enumerate(spots):
        pebble(f"{name}_{i}", (x + r.uniform(-.1, .1), y + r.uniform(-.1, .1), z), sz, e, seed + i)


def box_pts(x0, x1, y0, y1, z0, z1):
    return [(x, y, z) for x in (x0, x1) for y in (y0, y1) for z in (z0, z1)]


def asset(name):
    e = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(e)
    return e


def jit(pts, a):
    return [(x + rng.uniform(-a, a), y + rng.uniform(-a, a), z + rng.uniform(-a, a)) for x, y, z in pts]


# ------------------------------------------------------------------ outlines traced from the concept sheets
import json as _json  # noqa: E402

OUTLINES = _json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "outlines.json")))


def _merge_slivers(parts, min_frac=.14):
    """Vertical cuts can leave thin slivers: merge any part narrower than min_frac of the view into its neighbour."""
    parts = [list(map(tuple, p)) for p in parts]
    total = max(x for p in parts for x, _ in p) - min(x for p in parts for x, _ in p)
    parts.sort(key=lambda p: min(x for x, _ in p))
    changed = True
    while changed and len(parts) > 1:
        changed = False
        for i, p in enumerate(parts):
            w = max(x for x, _ in p) - min(x for x, _ in p)
            if w < total * min_frac:
                j = i + 1 if i + 1 < len(parts) else i - 1
                parts[j] = parts[j] + p
                del parts[i]
                changed = True
                break
    return parts


def extrude(part, depth, taper=.85, top_drop=.06, y0=0.0, z_shift=0.0, lean=0.0, h=None, sink=1.0):
    """3D points of an outline part (x, z): the front at y = y0 (leaning back by `lean` m at the top, above 3.2 m),
    the back at y0 + depth tapered toward the part's centre and its top dropped a little (visible top planes).
    The bottom edge is pushed `sink` m under the floor."""
    xs = [x for x, _ in part]
    zs = [z for _, z in part]
    cx = (min(xs) + max(xs)) / 2
    zmin, zmax = min(zs), max(zs)
    hh = h or (zmax - zmin)
    pts = []
    for x, z in part:
        z = z + z_shift
        if z <= zmin + z_shift + 1e-3:
            z -= sink
        yf = y0 + (lean * max(0.0, z - 3.2) / max(hh - 3.2, 1.0) if lean else 0.0)
        pts.append((x, yf, z))
        zb = z - top_drop * hh * max(0.0, (z - zmin) / max(hh, 1e-3)) if z > zmin else z
        pts.append((cx + (x - cx) * taper, y0 + depth, zb))
    return pts


def view_parts(sheet, view, scale=1.0, min_frac=.14):
    parts = _merge_slivers(OUTLINES[sheet][view], min_frac)
    return [[(x * scale, z * scale) for x, z in p] for p in parts]


# ------------------------------------------------------------------ A01 cliff walls (concept A01, its three views)
# Each view of the sheet is one wall: its silhouette traced from the sheet, split into the slab and the wedge.
# The front (-y) is the path side: flat from the floor to 3.2 m, leaning back above. Pivot: front bottom middle.

def a01_from_view(e, tag, view, height, depth, lean, seed):
    parts = view_parts("A01-cliff-wall", view, height / 18.0)
    widest = max(parts, key=lambda p: max(x for x, _ in p) - min(x for x, _ in p))
    obs = []
    for i, part in enumerate(parts):
        main = part is widest
        pts = extrude(part, depth if main else depth * .75, taper=.86 if main else .8, top_drop=.07,
                      y0=0.0 if main else .18, lean=lean if main else lean * .7, h=height)
        # Every part stands on the ground: a traced part whose foot is higher (a wedge on the slab) reaches down too,
        # so no part of a wall ever shows a gap under it.
        zfoot = min(p[2] for p in pts)
        if zfoot > -.9:
            pts = [(x, y, -1.0) if z <= zfoot + 1e-3 else (x, y, z) for x, y, z in pts]
        xs = [p[0] for p in pts]
        zs = [p[2] for p in pts]
        x0, x1, ztop = min(xs), max(xs), max(zs)
        r = random.Random(seed + i)
        spec = [("bed", ztop * r.uniform(.3, .45), 14)] + ([("bed", ztop * r.uniform(.62, .78), 12)] if main else []) + \
            [("joint", r.uniform(x0 + (x1 - x0) * .25, x1 - (x1 - x0) * .25), ztop * r.uniform(.55, .85), 30)] + \
            ([("joint", r.uniform(x0 + (x1 - x0) * .2, x1 - (x1 - x0) * .2), ztop * .25, 25)] if main and x1 - x0 > 6 else [])
        if main:
            spec.append(("joint", r.uniform(x0 + (x1 - x0) * .3, x1 - (x1 - x0) * .3), r.uniform(1.2, 2.2), 58))
        # A plain core behind the detailed front: where chips and joints meet, the eye finds rock, not sky.
        cx = (x0 + x1) / 2
        core = [(cx + (x - cx) * .97, max(y, .7) if y < depth * .5 else y - .2, z if z < 0 else z * .97) for x, y, z in pts]
        hull_part(f"{tag}_core{i}", core, e)
        obs += detail_rock(f"{tag}_{'slab' if main else 'wedge'}{i}", pts, e, joints(spec, seed + i), chips=6 if main else 4,
                           chip=(.25, .7) if main else (.15, .45), bulge=.004, wobble=.004, keep_front=True, seed=seed + i, relief=0.0)
    return obs, widest


def a01_wall(letter, view, seed):
    e = asset(f"A01 Wall {letter}")
    obs, _ = a01_from_view(e, f"A01{letter}", view, 13.0, 4.6, 1.1, seed)
    xs = [v.co.x for v in obs[0].data.vertices]
    if letter == "A":
        paint_crack(obs[0], (min(xs) + 2.2, -1, 7.6), (.25, 0, -1), 3.6, (0, -1, 0), seed + 9)
    return e


def a01_wall_a():
    return a01_wall("A", 0, 101)


def a01_wall_b():
    return a01_wall("B", 1, 111)


def a01_wall_c():
    return a01_wall("C", 2, 121)


def a01_cliff():
    """A01 per card: the left view at 18 m."""
    e = asset("A01 Cliff")
    obs, _ = a01_from_view(e, "A01", 0, 18.0, 6.5, 1.8, 131)
    xs = [v.co.x for v in obs[0].data.vertices]
    paint_crack(obs[0], (min(xs) + 3.0, -1, 10.5), (.25, 0, -1), 5.0, (0, -1, 0), 139, width=.08)
    return e


# ------------------------------------------------------------------ A02 ledge (concept: an L-shaped corner block)
# x along the ledge, y outward (+y the abyss), z = 0 the walkway. Two slabs meeting at the corner with a dusty flat
# top, the underside cut at a slope, a raised hump at the outer end. Pivot: inner corner of the top.

def a02_ledge():
    e = asset("A02 Ledge rim")
    arm = [(-5, 0, 0), (5, 0, 0), (5, 2.6, .05), (-5, 2.5, 0), (-5, 0, -2.6), (5, 0, -3.0), (5, 2.6, -1.4), (-5, 2.5, -1.2)]
    ob = detail_rock("A02_arm", arm, e, joints([("joint", -1.5, 0, 20), ("joint", 1.8, 0, 20)], 21), chips=4, seed=21)[0]
    corner = [(2.2, 0, 0), (5, 0, 0), (5, -4.5, 0), (2.4, -4.5, 0), (2.2, 0, -3.0), (5, 0, -3.0), (5, -4.5, -2.2), (2.4, -4.5, -2.4)]
    detail_rock("A02_corner", corner, e, joints([("bed", -1.5, 12)], 22), chips=4, seed=22)
    hump = [(3.6, .2, -.2), (5.1, .2, -.2), (5.1, 2.4, -.2), (3.6, 2.3, -.2), (4.0, .7, .7), (4.9, .9, .9), (4.6, 2.0, .6)]
    smooth_rock("A02_hump", hump, e, bulge=.05, seed=23)
    paint_crack(ob, (-1.0, 3.5, -.4), (.2, 0, -1), 2.2, (0, 1, 0), 24)
    return e


# ------------------------------------------------------------------ A03 overhang (concept: wedge support + thick console)
# Pivot: bottom of the support at the wall line (y = 0); the console hangs toward -y over the path.

def a03_overhang():
    """Concept A03, first view: a leaning wedge support and a thick console springing from its top (an elbow),
    cantilevered toward -y (over the path), the support in the wall (y > 0). Profile points from the traced outline
    (outlines.json, A03 view 1), extruded 6 m across with the far side tapered. Pivot: bottom of the support at the wall line."""
    e = asset("A03 Overhang")
    support = [(-6.5, .4), (-3.0, 0), (-.4, .5), (0, 2.9), (-.6, 8.6), (-3.2, 8.0), (-4.4, 6.8)]
    console = [(-2.6, 6.2), (0, 2.9), (1.4, 4.8), (6.2, 6.4), (6.4, 7.0), (6.5, 8.5), (5.2, 9.2), (3.3, 9.4), (1.4, 9.0), (-.6, 8.6), (-3.2, 8.0)]

    def solid(profile, half_near, half_far, sink):
        pts = []
        for px, pz in profile:
            z = pz - (sink if pz < .6 else 0)
            for d in (-half_near, half_near):
                pts.append((d, -px, z))
            for d in (-half_far, half_far):  # the cut side face is slightly narrower toward the top
                pts.append((d * .92, -px, z + .0))
        return pts

    detail_rock("A03_support", solid(support, 3.0, 2.6, .5), e, joints([("bed", 3.6, 14), ("joint", 0, 6, 25)], 31), chips=5, seed=31)
    ob = detail_rock("A03_console", solid(console, 3.2, 2.9, 0), e, joints([("joint", .5, 7.5, 20), ("bed", 7.6, 10)], 32), chips=5, seed=32)
    ob = max(ob, key=lambda o: max(v.co.z for v in o.data.vertices))
    stones_on(e, "A03_stone", [(-1.6, -3.6, 9.4, .35), (1.2, -1.2, 9.3, .28), (-.6, 1.0, 9.1, .3)], 34)
    paint_crack(ob, (.8, -4.6, 14), (.25, 1, 0), 2.6, (0, 0, 1), 33)
    return e


# ------------------------------------------------------------------ A04 summit step (concept: low polygon prisms)
# Pivot: middle of the front edge (the lip) of the lower plateau top, z = 0 its top. Front (-y) faces the god.

def a04_summit():
    """Concept A04: two flattened, irregular polygon prisms — a broad low plateau (sides about a fifth of its width,
    tapering out) and a smaller high step at the physical back left; the front right corner cut; a crack across the
    plateau. Pivot: middle of the lip (front edge of the plateau top), z = 0 its top; front (-y) faces the god."""
    e = asset("A04 Summit")
    low = [(-9.8, 0), (-2.0, -.4), (6.4, 0), (9.6, 2.8), (10.9, 8.2), (8.6, 14.6), (2.0, 16.4), (-6.8, 16.0), (-10.8, 11.5), (-11.2, 4.6)]
    cx, cy = 0, 8
    top = [(cx + (x - cx) * .94, cy + (y - cy) * .94, 0) for x, y in low]
    base = [(cx + (x - cx) * 1.12, cy + (y - cy) * 1.12 + .3, -3.6) for x, y in low]
    obs = detail_rock("A04_plateau", top + base, e, joints([("joint", -3.0, 0, 6), ("joint", 4.0, 0, 6), ("bed", -2.0, 6)], 41),
                      chips=5, chip=(.4, 1.1), bulge=.02, wobble=.008, seed=41, cuts_sub=3)
    ob = max(obs, key=lambda o: len(o.data.vertices))
    up = [(-9.6, 10.6), (-4.6, 10.0), (-2.6, 12.8), (-3.4, 16.2), (-8.0, 16.6), (-10.6, 14.0)]
    ucx, ucy = -6.4, 13.4
    detail_rock("A04_step", [(ucx + (x - ucx) * .84, ucy + (y - ucy) * .84, 2.4 + (x + 10) * .05) for x, y in up] +
                [(ucx + (x - ucx) * 1.08, ucy + (y - ucy) * 1.08, -1.2) for x, y in up], e, joints([("bed", .8, 12), ("joint", -6.6, 1, 20)], 42),
                chips=4, seed=42)
    stones_on(e, "A04_stone", [(-9.0, 14.6, 2.8, .45), (-5.2, 12.4, 2.75, .32), (8.6, 10.0, .05, .4), (7.2, 13.2, .05, .3), (-9.6, 6.0, .05, .35)], 45)
    paint_crack(ob, (-1.5, 4.0, 1.0), (1, .45, 0), 7.5, (0, 0, 1), 43, width=.07)
    return e


def a04_root():
    """The mountain under the summit: from the plateau's underside down to the cloud sea (-470 m), two shoulders."""
    e = asset("A04 Root")
    top = [(-10.0, .5), (9.5, .5), (10.4, 9.0), (7.8, 15.6), (-7.8, 15.8), (-10.2, 8.5)]
    mid = [(x * 2.6 + rng.uniform(-3, 3), (y - 8) * 2.6 + 8 + rng.uniform(-3, 3)) for x, y in top]
    smooth_rock("A04R_upper", [(x, y, -3.2) for x, y in top] + [(x, y, -120) for x, y in mid], e, bulge=.04, bevel=.5, seed=44)
    low = [(x * 2.0 + rng.uniform(-8, 8), (y - 8) * 2.0 + 8 + rng.uniform(-8, 8)) for x, y in mid]
    smooth_rock("A04R_lower", [(x * .98, y, -115) for x, y in mid] + [(x, y, -470) for x, y in low], e, bulge=.04, bevel=2, seed=45)
    return e


# ------------------------------------------------------------------ A05 boulders (concept: three wedge views)

def a05_boulder(tag, seed):
    """Concept A05, one view each: the traced silhouette extruded 0.75 of its width deep, tapered back."""
    e = asset(f"A05 Boulder {tag}")
    part = OUTLINES["A05-boulder"]["ABC".index(tag)][0]
    w = max(x for x, _ in part) - min(x for x, _ in part)
    h = max(z for _, z in part)
    pts = [(x, y + (max(0.0, z) / max(h, 1e-3)) * w * .18, z) if y < -w * .3 else (x, y, z)
           for x, y, z in extrude(part, w * .72, taper=.78, top_drop=.12, y0=-w * .36, sink=0)]
    obs = detail_rock(f"A05{tag}_rock", pts, e, joints([("joint", random.Random(seed).uniform(-.5, .5), 1.0, 25), ("bed", 1.3, 16)], seed),
                      chips=4, chip=(.12, .35), bulge=.05, bevel=.05, seed=seed)
    ob = max(obs, key=lambda o: len(o.data.vertices))
    if tag == "A":
        paint_crack(ob, (-.7, -3, 1.7), (.25, 0, -1), 1.3, (0, -1, 0), seed + 1, width=.035)
    return e


def a06_slab(tag, seed):
    """Concept A06: a pentagon slab with one square notch (two convex parts)."""
    r = random.Random(seed)
    e = asset(f"A06 Slab {tag}")

    def zt(x):
        return .5 + (x + 1.9) / 3.8 * .08 + r.uniform(-.02, .02)

    a = [(-1.9, -1.1), (.4, -1.1), (.4, 1.05), (-1.7, 1.0)]
    b = [(.35, -.5), (1.1, -.5), (1.9, -.1), (1.6, 1.1), (.35, 1.05)]
    ob = smooth_rock(f"A06{tag}_a", [(x, y, 0) for x, y in a] + [(x, y, zt(x)) for x, y in a], e, bulge=.03, bevel=.04, seed=seed)
    smooth_rock(f"A06{tag}_b", [(x, y, 0) for x, y in b] + [(x, y, zt(x)) for x, y in b], e, bulge=.03, bevel=.04, seed=seed + 1)
    paint_crack(ob, (-1.2, -.6, 2), (1, .5, 0), 1.0, (0, 0, 1), seed + 2, width=.03)
    return e


def roof_slab(tag, seed):
    r = random.Random(seed)
    e = asset(f"Roof slab {tag}")
    pts = [(x, y, z) for x in (-2, 2) for y in (-1.2, 1.2) for z in (0, .9)]
    pts = [(x + r.uniform(-.12, .12), y + r.uniform(-.1, .1), z + (r.uniform(-.1, .15) if z > 0 else r.uniform(-.05, .05))) for x, y, z in pts]
    smooth_rock(f"Roof{tag}", pts, e, bulge=.012, wobble=.012, bevel=.05, seed=seed)
    return e


# ------------------------------------------------------------------ A09 distant massifs (concept: three views)

def a09_massif(tag, seed):
    """Concept A09, one view each: the traced tiers extruded deep and tapered (a pyramid-like massif); the tallest
    tier deepest and set back, lower tiers stand in front."""
    e = asset(f"A09 Massif {tag}")
    parts = view_parts("A09-distant-massif", "ABC".index(tag), 1.0, .12)
    hmax = max(z for p in parts for _, z in p)
    for i, part in enumerate(parts):
        h = max(z for _, z in part)
        back = .3 + .7 * h / hmax
        y0 = -240 + 480 * (1 - back) * .6
        pts = [(x, y + (z / max(h, 1e-3)) * 480 * back * .38, z) if y < y0 + 1 else (x, y, z)
               for x, y, z in extrude(part, 480 * back, taper=.62, top_drop=.12, y0=y0, sink=0)]
        obs = detail_rock(f"A09{tag}_{i}", pts, e, joints([("bed", h * .45, 10), ("joint", sum(x for x, _ in part) / len(part), h * .7, 25)], seed + i),
                          chips=4, chip=(15, 45), bulge=.035, bevel=4, wobble=.012, far=True, seed=seed + i)
        ob = max(obs, key=lambda o: len(o.data.vertices))
        if h == hmax:
            xs = [v.co.x for v in ob.data.vertices]
            paint_crack(ob, (min(xs) + (max(xs) - min(xs)) * .3, -600, h * .6), (.1, 0, -1), h * .3, (0, -1, 0), seed + 9, width=2.4)
    return e


# ------------------------------------------------------------------ G01 stone frame, G03 hollow ring (same language)

def g01_frame():
    """Opening 1.4 × 2.4; x across the passage, y along it, pivot at the floor in the middle of the opening."""
    e = asset("G01 Stone frame")
    smooth_rock("G01_post_l", [(-1.2, -.32, 0), (-.7, -.3, 0), (-.7, .3, 0), (-1.2, .32, 0),
                               (-1.08, -.26, 2.72), (-.72, -.28, 2.7), (-.72, .26, 2.72), (-1.06, .28, 2.76)], e, bulge=.05, bevel=.04, seed=51)
    smooth_rock("G01_post_r", [(.7, -.3, 0), (1.2, -.32, 0), (1.18, .32, 0), (.7, .3, 0),
                               (.72, -.27, 2.62), (1.1, -.28, 2.68), (1.08, .28, 2.66), (.73, .27, 2.6)], e, bulge=.05, bevel=.04, seed=52)
    smooth_rock("G01_lintel", [(-1.35, -.34, 2.42), (1.4, -.32, 2.38), (1.36, .34, 2.44), (-1.36, .33, 2.47),
                               (-1.25, -.3, 2.98), (1.3, -.3, 2.84), (1.27, .3, 2.88), (-1.28, .32, 3.02)], e, bulge=.04, bevel=.04, seed=53)
    return e


def g03_hollow(r_in, gaps):
    """Ring of leaning stone slabs around the hollow (open to the sky); the inner face starts at r_in on the floor and
    leans away; gaps (degrees, half widths) are the ways in and out. Pivot at the centre of the floor."""
    e = asset("G03 Hollow ring")
    k = 0
    a = 0.0
    while a < 360:
        step = rng.uniform(19, 26)
        mid = a + step / 2
        if any(abs((mid - g + 180) % 360 - 180) < w + step / 2 for g, w in gaps):
            a += step
            continue
        a0, a1 = math.radians(a - 1.5), math.radians(a + step + 1.5)
        h = rng.uniform(9, 15)
        ro = r_in / math.cos(math.radians(step / 2 + 1.5)) + rng.uniform(1.8, 2.4)

        def P(ang, rad, z):
            return (math.cos(ang) * rad, math.sin(ang) * rad, z)

        ri = r_in / math.cos(math.radians(step / 2 + 1.5))
        lean = rng.uniform(.4, 1.4)
        pts = [P(a0, ri, -1), P(a1, ri, -1), P(a1, ro, -1), P(a0, ro, -1),
               P(a0, ri + lean, h - rng.uniform(0, 3)), P(a1, ri + lean * .8, h), P(a1, ro + .3, h - 1.5), P(a0, ro, h - 2)]
        detail_rock(f"G03_slab_{k}", pts, e, joints([("bed", rng.uniform(4.5, 8.5), 16)], 90 + k), chips=4, chip=(.2, .5), seed=90 + k)
        k += 1
        a += step
    return e


# ------------------------------------------------------------------ plants
def leaf_card(bm, uvl, base, direction, width, length, u0, u1, v0, v1, bend=0.0, roll=0.0):
    d = direction.normalized()
    side = d.cross(V(0, 0, 1))
    if side.length < 1e-3:
        side = V(1, 0, 0)
    side.normalize()
    if roll:
        side = (side * math.cos(roll) + d.cross(side) * math.sin(roll)).normalized()
    mid = base + d * length * .55 + V(0, 0, -bend * length * .3)
    tip = base + d * length + V(0, 0, -bend * length)
    vs = [bm.verts.new(p) for p in (base - side * width * .5, base + side * width * .5,
                                    mid + side * width * .5, mid - side * width * .5,
                                    tip + side * width * .5, tip - side * width * .5)]
    f1 = bm.faces.new((vs[0], vs[1], vs[2], vs[3]))
    f2 = bm.faces.new((vs[3], vs[2], vs[4], vs[5]))
    uvs = {vs[0]: (u0, v0), vs[1]: (u1, v0), vs[2]: (u1, v0 + (v1 - v0) * .55), vs[3]: (u0, v0 + (v1 - v0) * .55),
           vs[4]: (u1, v1), vs[5]: (u0, v1)}
    for f in (f1, f2):
        for loop in f.loops:
            loop[uvl].uv = uvs[loop.vert]


def a07_shrub():
    """Concept A07: two thick stems from one root arching toward +x, three branch ends; big single leaves (two or
    three at each end) and lichen on the bark. 1.6 × 1.1 × 0.8."""
    e = asset("A07 Shrub")
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UV")
    r = random.Random(7)

    def tube(points, radii, sides=6):
        rings = []
        for i, (p, rad) in enumerate(zip(points, radii)):
            d = (points[min(i + 1, len(points) - 1)] - points[max(i - 1, 0)]).normalized()
            a = d.cross(V(0, 1, 0))
            if a.length < 1e-3:
                a = d.cross(V(1, 0, 0))
            a.normalize()
            b = d.cross(a).normalized()
            rings.append([bm.verts.new(p + (a * math.cos(k * 2 * math.pi / sides) + b * math.sin(k * 2 * math.pi / sides)) * rad
                                       * (1 + r.uniform(-.12, .12))) for k in range(sides)])
        for i in range(len(rings) - 1):
            for k in range(sides):
                k1 = (k + 1) % sides
                f = bm.faces.new((rings[i][k], rings[i][k1], rings[i + 1][k1], rings[i + 1][k]))
                v0, v1 = i / (len(rings) - 1) * .45, (i + 1) / (len(rings) - 1) * .45
                for loop, uv in zip(f.loops, ((k / sides, v0), ((k + 1) / sides, v0), ((k + 1) / sides, v1), (k / sides, v1))):
                    loop[uvl].uv = uv
        bm.faces.new(rings[-1][::-1])

    def arc(p0, p1, lift, n=6):
        pts = []
        for i in range(n):
            t = i / (n - 1)
            q = p0.lerp(p1, t) + V(0, 0, math.sin(t * math.pi) * lift)
            pts.append(q + V(r.uniform(-.02, .02), r.uniform(-.02, .02), 0))
        return pts

    root = V(0, 0, -.05)
    s1 = arc(root, V(1.15, .1, .62), .42)
    s2 = arc(root + V(-.08, .1, 0), V(.42, -.22, 1.08), .3)
    tube(s1, [.11, .095, .08, .065, .05, .04])
    tube(s2, [.1, .085, .07, .055, .045, .035])
    b1 = arc(s1[3], s1[3] + V(.38, -.25, .32), .08, 4)
    b2 = arc(s2[3], s2[3] + V(.3, .3, .3), .08, 4)
    b3 = arc(s1[2], s1[2] + V(.22, -.32, .4), .06, 4)
    for b in (b1, b2, b3):
        tube(b, [.05, .04, .032, .025])
    tips = [s1[-1], s2[-1], b1[-1], b2[-1], b3[-1]]
    for p in tips:
        for k in range(3):
            ang = r.uniform(-1.4, 1.4)
            d = V(math.cos(ang) * .8 + .35, math.sin(ang), r.uniform(.15, .6))
            leaf_card(bm, uvl, p, d, .22, .44, .125, .375, .5, 1.0, bend=.18, roll=r.uniform(.7, 1.3) * r.choice((-1, 1)))
    me = bpy.data.meshes.new("A07_shrub")
    bm.to_mesh(me)
    bm.free()
    me.materials.append(MAT["GM_Leaf"])
    for p in me.polygons:
        p.use_smooth = False
    ob = bpy.data.objects.new("A07_shrub", me)
    bpy.context.collection.objects.link(ob)
    ob.parent = e
    return e


def a08_rosette():
    """0.8 × 0.7 × 0.65: five leaf cards from one point, one torn (shorter, narrower)."""
    e = asset("A08 Rosette")
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UV")
    for i, (yaw, rise, ln, w) in enumerate([(0, 62, .72, .32), (75, 50, .66, .3), (145, 56, .7, .32), (215, 44, .62, .3), (290, 58, .46, .2)]):
        a, b = math.radians(yaw), math.radians(rise)
        d = V(math.cos(a) * math.cos(b), math.sin(a) * math.cos(b), math.sin(b))
        leaf_card(bm, uvl, V(0, 0, -.03), d, w, ln, .125, .375, .5, 1.0, bend=.15)
    me = bpy.data.meshes.new("A08_rosette")
    bm.to_mesh(me)
    bm.free()
    me.materials.append(MAT["GM_Leaf"])
    ob = bpy.data.objects.new("A08_rosette", me)
    bpy.context.collection.objects.link(ob)
    ob.parent = e
    return e


# ------------------------------------------------------------------ v2 additions (each with its own random stream,
# built after the v1 set so the v1 assets come out exactly as before)

def own_rng(seed):
    """Run an asset builder on its own random stream; the shared one (UV shifts of v1 assets) stays untouched."""
    def wrap(fn):
        def run(*a, **k):
            global rng
            saved, rng = rng, random.Random(seed)
            try:
                return fn(*a, **k)
            finally:
                rng = saved
        return run
    return wrap


def a09_massif_v2(tag, seed):
    """A09 with detail for the panorama: the traced tiers split by three dipping beds and three joints, chipped
    hard; crags and needles on every tier's crest; low buttresses at the foot of the front."""
    @own_rng(900 + seed)
    def build():
        r = random.Random(seed * 31)
        e = asset(f"A09 Massif {tag}")
        parts = view_parts("A09-distant-massif", "ABC".index(tag), 1.0, .12)
        hmax = max(z for p in parts for _, z in p)
        for i, part in enumerate(parts):
            h = max(z for _, z in part)
            xs = [x for x, _ in part]
            x0, x1 = min(xs), max(xs)
            back = .3 + .7 * h / hmax
            y0 = -240 + 480 * (1 - back) * .6
            pts = [(x, y + (z / max(h, 1e-3)) * 480 * back * .38, z) if y < y0 + 1 else (x, y, z)
                   for x, y, z in extrude(part, 480 * back, taper=.62, top_drop=.12, y0=y0, sink=0)]
            spec = [("bed", h * f, 13) for f in (.24, .47, .7)] + \
                [("joint", x0 + (x1 - x0) * f + r.uniform(-20, 20), h * r.uniform(.3, .8), 32) for f in (.25, .5, .75)]
            obs = detail_rock(f"A09{tag}_{i}", pts, e, joints(spec, seed + i), chips=5, chip=(10, 30), bulge=.03,
                              bevel=3, wobble=.014, far=True, seed=seed + i)
            ob = max(obs, key=lambda o: len(o.data.vertices))
            if h == hmax:
                vx = [v.co.x for v in ob.data.vertices]
                paint_crack(ob, (min(vx) + (max(vx) - min(vx)) * .3, -600, h * .6), (.1, 0, -1), h * .3, (0, -1, 0), seed + 9, width=2.4)
            # crags and needles along the crest: on the highest outline points of this tier
            tops = sorted(part, key=lambda q: -q[1])[:6]
            yf = y0 + 480 * back * .38
            for k, (cx, cz) in enumerate(tops[:r.randint(3, 5)]):
                # blocky stepped crags, wider than tall, leaning a little: the crest breaks into tiers, not needles
                w = r.uniform(.07, .14) * (x1 - x0)
                hh = r.uniform(.07, .14) * h
                cy = yf + r.uniform(20, 80)
                lean = r.uniform(-.25, .25) * w
                base = [(cx + dx * w * r.uniform(.8, 1.1), cy + dy * w * .7 * r.uniform(.8, 1.1), cz - hh * .6) for dx in (-1, 1) for dy in (-1, 1)]
                apex = [(cx + lean + dx * w * r.uniform(.45, .75), cy + dy * w * .45 * r.uniform(.6, 1), cz + hh * r.uniform(.75, 1.0)) for dx in (-1, 1) for dy in (-1, 1)]
                detail_rock(f"A09{tag}_{i}_crag{k}", base + apex, e, joints([("bed", cz + hh * .1, 12)], seed + 70 + k), chips=2,
                            chip=(6, 16), bulge=.02, bevel=2, wobble=.01, far=True, seed=seed * 13 + k)
            # buttresses in front of the tier's foot
            for k in range(r.randint(1, 3)):
                bw = r.uniform(.12, .22) * (x1 - x0)
                bx = r.uniform(x0 + bw, x1 - bw)
                bh = r.uniform(.15, .32) * h
                by = y0 - r.uniform(20, 70)
                bpts = [(bx + dx * bw, by + dy * bw * .7, 0) for dx in (-1, 1) for dy in (-1, 1)] + \
                       [(bx + r.uniform(-.6, .6) * bw, by + r.uniform(-.2, .5) * bw * .7, bh * r.uniform(.7, 1)) for _ in range(3)]
                detail_rock(f"A09{tag}_{i}_buttress{k}", bpts, e, joints([("bed", bh * .5, 12)], seed + 50 + k), chips=3,
                            chip=(8, 22), bulge=.03, bevel=2, wobble=.012, far=True, seed=seed * 17 + k)
        return e
    return build()


def _chunk_pts(r, cx, cy, z0, sx, sy, sz, n=9):
    return [(cx + r.uniform(-1, 1) * sx, cy + r.uniform(-1, 1) * sy, z0 + r.uniform(0, 1) * sz) for _ in range(n)] + \
           [(cx + r.uniform(-1, 1) * sx, cy + r.uniform(-1, 1) * sy, z0) for _ in range(4)]


def p01_pebbles(tag, seed):
    """Small stones lying in a loose drift, about 1.2 × 0.7 m; pivot on the ground in the middle."""
    @own_rng(1000 + seed)
    def build():
        r = random.Random(seed)
        e = asset(f"P01 Pebbles {tag}")
        for k in range(r.randint(8, 12)):
            sz = r.uniform(.05, .2) * (1.4 if k == 0 else 1)
            x, y = r.gauss(0, .38), r.gauss(0, .22)
            hull_part(f"P01{tag}_{k}", _chunk_pts(r, x, y, -.02, sz, sz * .8, sz * .75), e, bevel=min(.02, sz * .12))
        return e
    return build()


def p02_rubble(tag, seed):
    """Broken chips heaped against a wall: the wall at y = 0 (+y into it), the heap spills toward -y up to 0.7 m,
    highest by the wall (0.5 m). Pivot on the ground at the wall line."""
    @own_rng(1100 + seed)
    def build():
        r = random.Random(seed)
        e = asset(f"P02 Rubble {tag}")
        k = 0
        for layer, (count, ymin, ymax, z0, smin, smax) in enumerate([(9, -.7, .05, -.03, .08, .26), (5, -.35, .08, .12, .1, .24), (3, -.18, .1, .26, .08, .18)]):
            for _ in range(count):
                s_ = r.uniform(smin, smax)
                x, y = r.uniform(-.6, .6) * (1 - layer * .25), r.uniform(ymin, ymax)
                pts = _chunk_pts(r, x, y, z0, s_ * r.uniform(.8, 1.3), s_ * r.uniform(.6, 1.0), s_ * r.uniform(.5, .9), 7)
                hull_part(f"P02{tag}_{k}", pts, e, bevel=.015)
                k += 1
        return e
    return build()


def p03_flat_stones(seed):
    """Two or three thin slabs lying over each other, 0.3–0.7 m; pivot on the ground in the middle."""
    @own_rng(1200 + seed)
    def build():
        r = random.Random(seed)
        e = asset("P03 Flat stones")
        z = -.01
        for k in range(3):
            w, d, t = r.uniform(.3, .7), r.uniform(.2, .45), r.uniform(.05, .09)
            x, y = r.uniform(-.25, .25), r.uniform(-.15, .15)
            a = r.uniform(0, math.pi)
            ca, sa = math.cos(a), math.sin(a)
            outline = [(r.uniform(.75, 1) * math.cos(q) * w * .5, r.uniform(.75, 1) * math.sin(q) * d * .5) for q in [j * math.pi / 3 + r.uniform(-.2, .2) for j in range(6)]]
            tilt = r.uniform(-.04, .04)
            pts = []
            for ox, oy in outline:
                px_, py_ = x + ox * ca - oy * sa, y + ox * sa + oy * ca
                pts += [(px_, py_, z + ox * tilt), (px_ * .97, py_ * .97, z + t + ox * tilt)]
            smooth_rock(f"P03_{k}", pts, e, bulge=.02, wobble=.01, bevel=.012, seed=seed + k)
            z += t * .55
        return e
    return build()


def p04_cairn(seed):
    """A crooked stack of five flat stones, 0.75 m: someone was here, or no one. Pivot on the ground in the middle."""
    @own_rng(1300 + seed)
    def build():
        r = random.Random(seed)
        e = asset("P04 Cairn")
        z, x, y = -.02, 0.0, 0.0
        for k in range(5):
            w = .46 * (1 - k * .14) * r.uniform(.85, 1.1)
            t = r.uniform(.09, .16)
            a = r.uniform(0, math.pi)
            ca, sa = math.cos(a), math.sin(a)
            x += r.uniform(-.05, .05)
            y += r.uniform(-.05, .05)
            tx, ty = r.uniform(-.08, .08), r.uniform(-.08, .08)
            pts = []
            for j in range(7):
                q = j * 2 * math.pi / 7 + r.uniform(-.2, .2)
                ox, oy = math.cos(q) * w * .5 * r.uniform(.8, 1), math.sin(q) * w * .38 * r.uniform(.8, 1)
                px_, py_ = x + ox * ca - oy * sa, y + ox * sa + oy * ca
                pts += [(px_, py_, z + ox * tx + oy * ty), (x + (px_ - x) * .85, y + (py_ - y) * .85, z + t + ox * tx + oy * ty)]
            smooth_rock(f"P04_{k}", pts, e, bulge=.04, wobble=.015, bevel=.015, seed=seed + k)
            z += t * .92
        return e
    return build()


def p05_chunk(tag, seed):
    """A block fallen from the walls, 0.5–0.8 m, cracked once. Pivot on the ground in the middle."""
    @own_rng(1400 + seed)
    def build():
        r = random.Random(seed)
        e = asset(f"P05 Chunk {tag}")
        sx, sy, sz = r.uniform(.3, .42), r.uniform(.22, .32), r.uniform(.25, .4)
        detail_rock(f"P05{tag}", _chunk_pts(r, 0, 0, -.04, sx, sy, sz, 10), e, joints([("joint", r.uniform(-.1, .1), sz * .5, 30)], seed),
                    chips=3, chip=(.05, .14), bulge=.03, bevel=.02, wobble=.012, seed=seed)
        return e
    return build()


def p06_tuft(tag, seed):
    """A tuft of leaves out of a crack, 0.25–0.4 m: crossed leaf cards (rosette leaf of the foliage atlas)."""
    @own_rng(1500 + seed)
    def build():
        r = random.Random(seed)
        e = asset(f"P06 Tuft {tag}")
        bm = bmesh.new()
        uvl = bm.loops.layers.uv.new("UV")
        n = r.randint(6, 9)
        for k in range(n):
            a = k * 2 * math.pi / n + r.uniform(-.3, .3)
            rise = math.radians(r.uniform(48, 78))
            d = V(math.cos(a) * math.cos(rise), math.sin(a) * math.cos(rise), math.sin(rise))
            leaf_card(bm, uvl, V(r.uniform(-.03, .03), r.uniform(-.03, .03), -.02), d, r.uniform(.07, .11), r.uniform(.2, .36),
                      .125, .375, .5, 1.0, bend=r.uniform(.1, .25), roll=r.uniform(-.6, .6))
        me = bpy.data.meshes.new(f"P06_tuft_{tag}")
        bm.to_mesh(me)
        bm.free()
        me.materials.append(MAT["GM_Leaf"])
        for poly in me.polygons:
            poly.use_smooth = False
        ob = bpy.data.objects.new(f"P06_tuft_{tag}", me)
        bpy.context.collection.objects.link(ob)
        ob.parent = e
        return e
    return build()


# ------------------------------------------------------------------ build all

assets = [
    a01_wall_a(), a01_wall_b(), a01_wall_c(), a01_cliff(), a02_ledge(), a03_overhang(), a04_summit(), a04_root(),
    a05_boulder("A", 1), a05_boulder("B", 2), a05_boulder("C", 3),
    a06_slab("A", 4), a06_slab("B", 5), roof_slab("A", 6), roof_slab("B", 7),
    a09_massif("A", 8), a09_massif("B", 9), a09_massif("C", 10),
    g01_frame(),
    g03_hollow(3.35, [(137.5, 22), (0, 15)]),
    a07_shrub(), a08_rosette(),
]
# v1 massifs stay in the list only so the shared random stream (and every asset after them) is unchanged; the
# panorama uses the detailed v2 massifs, exported under the same file names.
_v1_massifs = [a for a in assets if a.name.startswith("A09 Massif")]
assets = [a for a in assets if a not in _v1_massifs]
for e in _v1_massifs:
    for c in list(e.children):
        bpy.data.objects.remove(c, do_unlink=True)
    bpy.data.objects.remove(e, do_unlink=True)
assets = assets + [
    a09_massif_v2("A", 8), a09_massif_v2("B", 9), a09_massif_v2("C", 10),
    p01_pebbles("A", 1), p01_pebbles("B", 2), p02_rubble("A", 3), p02_rubble("B", 4), p03_flat_stones(5), p04_cairn(6),
    p05_chunk("A", 7), p05_chunk("B", 8), p06_tuft("A", 9), p06_tuft("B", 10),
]
ONLY = sys.argv[sys.argv.index("--only") + 1].split(",") if "--only" in sys.argv else None

# ------------------------------------------------------------------ export (one FBX per asset)
report = []
for e in assets:
    if ONLY and not any(e.name.startswith(p) for p in ONLY):
        continue
    for o in bpy.context.scene.objects:
        o.select_set(False)
    e.select_set(True)
    tris = 0
    for c in e.children:
        c.select_set(True)
        c.data.calc_loop_triangles()
        tris += len(c.data.loop_triangles)
    fn = os.path.join(OUT, e.name.replace(" ", "_") + ".fbx")
    bpy.ops.export_scene.fbx(filepath=fn, use_selection=True, object_types={"EMPTY", "MESH"}, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             mesh_smooth_type="FACE", add_leaf_bones=False, use_triangles=True, path_mode="STRIP")
    dims = Vector((0, 0, 0))
    mn = Vector((1e9, 1e9, 1e9))
    mx = Vector((-1e9, -1e9, -1e9))
    for c in e.children:
        for v in c.data.vertices:
            mn = Vector(map(min, mn, v.co))
            mx = Vector(map(max, mx, v.co))
    report.append(f"{e.name}: parts {len(e.children)}, tris {tris}, size {mx.x - mn.x:.2f} × {mx.y - mn.y:.2f} × {mx.z - mn.z:.2f}")
open(os.path.join(OUT, "kit-report.md"), "w").write("\n".join("- " + r for r in report) + "\n")
print("\n".join(report))

# ------------------------------------------------------------------ preview render (asset sheet)
if RENDER:
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 24
    scene.cycles.device = "CPU"
    scene.render.resolution_x, scene.render.resolution_y = 1600, 900
    world = bpy.data.worlds.new("W")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (.52, .46, .44, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = .7
    scene.world = world
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 2.6
    sun.rotation_euler = (math.radians(42), math.radians(-18), math.radians(-30))
    bpy.context.collection.objects.link(sun)
    for m in MAT.values():
        m.blend_method = "CLIP" if hasattr(m, "blend_method") else None
    scene.view_settings.view_transform = "Standard"
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    cam.data.clip_end = 20000
    bpy.context.collection.objects.link(cam)
    scene.camera = cam

    def shot(objs, path, dist, height, lens=35, target_z=None, yaw=-28):
        for a in objs:
            a.rotation_euler = (0, 0, math.radians(yaw)) if a.name != "A03 Overhang" else (0, 0, math.radians(yaw + 90))
        for o in scene.objects:
            if o.type == "MESH":
                o.hide_render = not any(o.parent == a for a in objs)
        # line the assets up along x
        x = 0.0
        sizes = []
        for a in objs:
            mn = min(v.co.x for c in a.children for v in c.data.vertices)
            mx = max(v.co.x for c in a.children for v in c.data.vertices)
            sizes.append((mn, mx))
        total = sum(mx - mn for mn, mx in sizes) + 1.5 * (len(objs) - 1)
        x = -total / 2
        tz = 0
        for a, (mn, mx) in zip(objs, sizes):
            a.location = (x - mn, 0, 0)
            x += (mx - mn) + 1.5
            tz = max(tz, max(v.co.z for c in a.children for v in c.data.vertices))
        tz = target_z if target_z is not None else tz * .45
        cam.data.lens = lens
        cam.location = (dist * .35, -dist, height)
        d = Vector((0, 0, tz)) - cam.location
        cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)

    base = RENDER[:-4]
    by = {a.name: a for a in assets}
    shot([by["A01 Wall A"], by["A01 Wall B"], by["A01 Wall C"], by["A01 Cliff"], by["G01 Stone frame"]], base + "-walls.png", 52, 9, 35, 7)
    shot([by["A02 Ledge rim"], by["A03 Overhang"]], base + "-pieces.png", 30, 9, 35, 3)
    shot([by["A05 Boulder A"], by["A05 Boulder B"], by["A05 Boulder C"], by["A06 Slab A"], by["A06 Slab B"], by["Roof slab A"]],
         base + "-rocks.png", 17, 5, 35, .8)
    shot([by["A04 Summit"]], base + "-summit.png", 30, 10, 35, 0)
    shot([by["A07 Shrub"], by["A08 Rosette"]], base + "-plants.png", 3.6, 1.4, 35, .55, yaw=-10)
    shot([by["A09 Massif A"], by["A09 Massif B"], by["A09 Massif C"]], base + "-massifs.png", 1900, 500, 35, 180)
    shot([by["G03 Hollow ring"]], base + "-hollow.png", 26, 22, 30, 3)
