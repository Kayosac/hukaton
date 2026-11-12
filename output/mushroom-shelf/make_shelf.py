"""Builds the bracket-fungus shelf («гриб-полка») of the Mushroom trampoline card.

    python3 make_shelf.py            # Unity mesh assets + .blend + FBX/OBJ sources + preview renders
    python3 make_shelf.py --no-render

Geometry: shelf_geometry.py (Unity space: x along the wall, y up, z out of the wall, origin = hinge on the wall).
Unity assets (text Mesh .asset, same format as the other mushroom meshes; writer verified byte-identical on them):
    hukaton/Assets/Game/Prefabs/Cards/MushroomTrampoline/Mushroom shelf.asset           (3 submeshes: top/under/rim)
    hukaton/Assets/Game/Prefabs/Cards/MushroomTrampoline/Mushroom shelf collider.asset  (convex hull)
Blender (bpy 5.3, Z up): Unity (x, y, z) -> Blender (x, z, y); the axis swap also converts the handedness, so the
triangle order is kept and the model looks the same in both.
"""
import math
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import shelf_geometry as geo  # noqa: E402
import unity_mesh  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
UNITY_DIR = os.path.join(REPO, "hukaton", "Assets", "Game", "Prefabs", "Cards", "MushroomTrampoline")
SHELF_ASSET = os.path.join(UNITY_DIR, "Mushroom shelf.asset")
COLLIDER_ASSET = os.path.join(UNITY_DIR, "Mushroom shelf collider.asset")
SHELF_GUID = "f9a13f8f857543d8b6bec88d1b002561"
COLLIDER_GUID = "08d2309966ec4a25b48d0120bdc2613f"
PREVIEWS = os.path.join(HERE, "previews")

# Palette of the existing trampoline (URP Lit _BaseColor, sRGB) — the shelf reuses these very materials in Unity.
COLOURS = {
    "Trampoline cap": (0.78, 0.32, 0.38),
    "Trampoline gills": (0.86, 0.74, 0.62),
    "Trampoline spots": (0.95, 0.9, 0.76),
    "Mushroom stem": (0.8392157, 0.79607844, 0.6901961),
}


def write_unity_assets():
    v, n, subs = geo.build()
    unity_mesh.write_mesh(SHELF_ASSET, "Mushroom shelf", v, n, subs)
    unity_mesh.write_meta(SHELF_ASSET + ".meta", SHELF_GUID)
    cv, cn, ct = geo.collider()
    unity_mesh.write_mesh(COLLIDER_ASSET, "Mushroom shelf collider", cv, cn, [ct])
    unity_mesh.write_meta(COLLIDER_ASSET + ".meta", COLLIDER_GUID)
    check(v, n, subs, cv, ct)
    return (v, n, subs), (cv, cn, ct)


def check(v, n, subs, cv, ct):
    """Sanity checks printed for the record: winding vs normals, closed hull, level top, collider fits the body."""
    dev = []
    for tris in subs:
        a, b, c = v[tris[:, 0]], v[tris[:, 1]], v[tris[:, 2]]
        fn = np.cross(b - a, c - a)
        fn /= np.maximum(1e-12, np.linalg.norm(fn, axis=1))[:, None]
        for k in range(3):
            dev.append(np.degrees(np.arccos(np.clip((fn * n[tris[:, k]]).sum(1), -1, 1))))
    dev = np.concatenate(dev)
    print(f"visual: {len(v)} vertices, triangles per submesh {[len(s) for s in subs]}, "
          f"normal vs face: mean {dev.mean():.1f}°, 99th pct {np.percentile(dev, 99):.1f}°")
    print(f"visual bbox min {v.min(0).round(3)} max {v.max(0).round(3)}")
    # collider: closed (every edge twice) and convex (all vertices behind every face plane)
    edges = {}
    for tri in ct:
        for i in range(3):
            e = tuple(sorted((tri[i], tri[(i + 1) % 3])))
            edges[e] = edges.get(e, 0) + 1
    closed = all(count == 2 for count in edges.values())
    worst = 0.0
    for tri in ct:
        a, b, c = cv[tri]
        fn = np.cross(b - a, c - a)
        fn /= np.linalg.norm(fn)
        worst = max(worst, float(((cv - a) @ fn).max()))
    up = [tri for tri in ct if np.cross(cv[tri[1]] - cv[tri[0]], cv[tri[2]] - cv[tri[0]])[1] > 0]
    top_normals = []
    for tri in ct:
        a, b, c = cv[tri]
        fn = np.cross(b - a, c - a)
        fn /= np.linalg.norm(fn)
        centroid = (a + b + c) / 3
        if fn[1] > 0.5 and 0.05 < centroid[2] < 0.75 and abs(centroid[0]) < 0.55:
            top_normals.append(fn[1])
    print(f"collider: {len(cv)} vertices, {len(ct)} triangles, closed {closed}, convex (max outside {worst:.2e} m), "
          f"up-facing tris {len(up)}, landing-area normal.y min {min(top_normals):.4f}")
    # how far the visible body pokes out of the hull, and how far the hull reaches past the body (top area)
    body = v[np.unique(np.concatenate([subs[0].reshape(-1), subs[1].reshape(-1)]))]
    out = 0.0
    for tri in ct:
        a, b, c = cv[tri]
        fn = np.cross(b - a, c - a)
        fn /= np.linalg.norm(fn)
        out = max(out, float(((body - a) @ fn).max()))
    print(f"body vertices outside the collider hull by at most {out * 100:.2f} cm")


# ---------------------------------------------------------------- Blender

def to_blender(points):
    p = np.asarray(points, dtype=np.float64)
    return np.stack([p[:, 0], p[:, 2], p[:, 1]], axis=1)


def quat_rotate(q, v):
    """Unity quaternion (x, y, z, w) applied to Unity-space vectors."""
    x, y, z, w = q
    u = np.array([x, y, z])
    v = np.asarray(v, dtype=np.float64)
    return v + 2 * np.cross(u, np.cross(u, v) + w * v)


def srgb_to_linear(c):
    return tuple((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


def make_material(bpy, name, colour, roughness=0.95, emission=None, alpha=1.0):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    bsdf = nodes.get("Principled BSDF")
    lin = srgb_to_linear(colour)
    bsdf.inputs["Base Color"].default_value = (*lin, 1)
    bsdf.inputs["Roughness"].default_value = roughness
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = (*srgb_to_linear(emission), 1)
        bsdf.inputs["Emission Strength"].default_value = 2.0
    if alpha < 1:
        bsdf.inputs["Alpha"].default_value = alpha
    mat.diffuse_color = (*lin, alpha)
    return mat


def add_mesh(bpy, name, vertices, normals, submeshes, materials, location=(0, 0, 0)):
    verts = to_blender(vertices)
    faces, mat_index = [], []
    for k, tris in enumerate(submeshes):
        for tri in tris:
            # Unity and Blender both take cross(b - a, c - a) as the front; the axis swap mirrors it, so reverse.
            faces.append((int(tri[0]), int(tri[2]), int(tri[1])))
            mat_index.append(k)
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts.tolist(), [], faces)
    mesh.update()
    for poly, k in zip(mesh.polygons, mat_index):
        poly.material_index = k
        poly.use_smooth = True
    if normals is not None:
        mesh.normals_split_custom_set_from_vertices(to_blender(normals).tolist())
    for mat in materials:
        mesh.materials.append(mat)
    obj = bpy.data.objects.new(name, mesh)
    obj.location = location
    bpy.context.scene.collection.objects.link(obj)
    return obj


def floor_mushroom(bpy, mats, unity_position):
    """The existing trampoline mushroom from its Unity meshes (Stem; Cap at y 0.7 with its authored tilt; Spots)."""
    d = UNITY_DIR
    _, sv, sn, ss, _ = unity_mesh.read_mesh(os.path.join(d, "Mushroom stem.asset"))
    _, cv, cn, cs, _ = unity_mesh.read_mesh(os.path.join(d, "Mushroom cap.asset"))
    _, pv, pn, ps, _ = unity_mesh.read_mesh(os.path.join(d, "Mushroom spots.asset"))
    q = (0.043592814, 0.0015222946, -0.03486628, 0.99843967)
    off = np.array(unity_position)
    cap_v = quat_rotate(q, cv) + np.array([0, 0.7, 0]) + off
    cap_n = quat_rotate(q, cn)
    spot_v = quat_rotate(q, pv) + np.array([0, 0.7, 0]) + off
    spot_n = quat_rotate(q, pn)
    objs = [
        add_mesh(bpy, "Floor mushroom stem", sv + off, sn, ss, [mats["Mushroom stem"]]),
        add_mesh(bpy, "Floor mushroom cap", cap_v, cap_n, cs, [mats["Trampoline cap"], mats["Trampoline gills"]]),
        add_mesh(bpy, "Floor mushroom spots", spot_v, spot_n, ps, [mats["Trampoline spots"]]),
    ]
    return objs


def rock_wall(bpy, mat):
    """A plain grey wall: a solid slab whose face is the Unity z = 0 plane (what is sunk into it stays hidden)."""
    v = np.array([[-3.2, -1.5, 0.0], [3.2, -1.5, 0.0], [3.2, 2.3, 0.0], [-3.2, 2.3, 0.0],
                  [-3.2, -1.5, -0.7], [3.2, -1.5, -0.7], [3.2, 2.3, -0.7], [-3.2, 2.3, -0.7]])
    # Unity winding (cross(b - a, c - a) outward) for every face of the box.
    quads = [(0, 1, 2, 3), (5, 4, 7, 6), (4, 0, 3, 7), (1, 5, 6, 2), (3, 2, 6, 7), (4, 5, 1, 0)]
    tris = []
    for a, b, c, d in quads:
        tris += [[a, b, c], [a, c, d]]
    tris = np.array(tris)
    centre = v.mean(axis=0)
    for k, (a, b, c) in enumerate(tris):
        if np.dot(np.cross(v[b] - v[a], v[c] - v[a]), (v[a] + v[b] + v[c]) / 3 - centre) < 0:
            tris[k] = [a, c, b]
    return add_mesh(bpy, "Wall", v, None, [tris], [mat])


def ground(bpy, mat):
    v = np.array([[-3.2, -1.5, -0.1], [3.2, -1.5, -0.1], [3.2, -1.5, 5.0], [-3.2, -1.5, 5.0]])
    return add_mesh(bpy, "Ground", v, None, [np.array([[0, 3, 2], [0, 2, 1]])], [mat])


def capsule(bpy, mat, unity_feet, radius=0.315, height=1.8):
    bpy.ops.mesh.primitive_cylinder_add(radius=radius, depth=height - 2 * radius, vertices=24)
    body = bpy.context.active_object
    bpy.ops.mesh.primitive_uv_sphere_add(radius=radius, segments=24, ring_count=12)
    top = bpy.context.active_object
    bpy.ops.mesh.primitive_uv_sphere_add(radius=radius, segments=24, ring_count=12)
    bottom = bpy.context.active_object
    fx, fy, fz = to_blender([unity_feet])[0]
    body.location = (fx, fy, fz + height / 2)
    top.location = (fx, fy, fz + height - radius)
    bottom.location = (fx, fy, fz + radius)
    for o in (body, top, bottom):
        o.data.materials.append(mat)
        for p in o.data.polygons:
            p.use_smooth = True
    return [body, top, bottom]


def look_at(obj, target):
    from mathutils import Vector
    direction = Vector(target) - obj.location
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def camera(bpy, name, unity_from, unity_to, lens=40, ortho=None):
    cam = bpy.data.objects.new(name, bpy.data.cameras.new(name))
    bpy.context.scene.collection.objects.link(cam)
    cam.location = tuple(to_blender([unity_from])[0])
    look_at(cam, tuple(to_blender([unity_to])[0]))
    cam.data.lens = lens
    if ortho:
        cam.data.type = "ORTHO"
        cam.data.ortho_scale = ortho
    cam.data.clip_start = 0.05
    return cam


def build_scene(shelf, hull):
    import bpy
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    mats = {name: make_material(bpy, name, colour) for name, colour in COLOURS.items()}
    wall_mat = make_material(bpy, "Grey wall", (0.52, 0.52, 0.5), 0.9)
    ground_mat = make_material(bpy, "Grey ground", (0.42, 0.42, 0.4), 0.95)
    ref_mat = make_material(bpy, "Player capsule (1.8 m)", (0.66, 0.68, 0.72), 0.7)
    hull_mat = make_material(bpy, "Collider wire", (1.0, 0.55, 0.1), 0.5, emission=(1.0, 0.55, 0.1))

    v, n, subs = shelf
    shelf_obj = add_mesh(bpy, "Mushroom shelf", v, n, subs,
                         [mats["Trampoline cap"], mats["Trampoline gills"], mats["Trampoline spots"]])
    cv, cn, ct = hull
    hull_obj = add_mesh(bpy, "Mushroom shelf collider", cv, None, [ct], [hull_mat])
    wire = hull_obj.modifiers.new("wire", "WIREFRAME")
    wire.thickness = 0.006
    wire.use_even_offset = False
    wire.use_relative_offset = False
    hull_obj.hide_render = True
    hull_obj.hide_viewport = False

    wall = rock_wall(bpy, wall_mat)
    floor = ground(bpy, ground_mat)
    floor_objs = floor_mushroom(bpy, mats, (2.05, -1.5, 1.05))
    ref = capsule(bpy, ref_mat, (-1.95, -1.5, 1.15))

    world = bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = (0.62, 0.66, 0.72, 1)
    bg.inputs["Strength"].default_value = 0.75
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    scene.collection.objects.link(sun)
    sun.data.energy = 3.2
    sun.data.angle = math.radians(8)
    look_at(sun, tuple(to_blender([(-0.35, -1.0, -0.75)])[0] + np.array(sun.location)))

    # A fill light from below, only for the underside picture (in the game the underside sits in shadow).
    under = bpy.data.objects.new("Underside fill", bpy.data.lights.new("Underside fill", "SUN"))
    scene.collection.objects.link(under)
    under.data.energy = 2.2
    for attr in ("use_shadow", "cast_shadow"):   # the ground would block a sun from below
        if hasattr(under.data, attr):
            setattr(under.data, attr, False)
    if hasattr(under.data, "cycles") and hasattr(under.data.cycles, "cast_shadow"):
        under.data.cycles.cast_shadow = False
    look_at(under, tuple(to_blender([(0.95, 0.55, -0.35)])[0]))
    under.hide_render = True

    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 40
    try:
        scene.cycles.use_denoising = True
    except Exception:
        pass
    scene.view_settings.view_transform = "Standard"
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 960
    scene.render.film_transparent = False
    return {
        "bpy": bpy, "scene": scene, "shelf": shelf_obj, "hull": hull_obj, "wall": wall, "floor": floor,
        "floor_mushroom": floor_objs, "capsule": ref, "underlight": under,
    }


def render(ctx, name, cam, show=(), hide=()):
    bpy, scene = ctx["bpy"], ctx["scene"]
    scene.camera = cam
    for key in show:
        for o in (ctx[key] if isinstance(ctx[key], list) else [ctx[key]]):
            o.hide_render = False
    for key in hide:
        for o in (ctx[key] if isinstance(ctx[key], list) else [ctx[key]]):
            o.hide_render = True
    scene.render.filepath = os.path.join(PREVIEWS, name)
    bpy.ops.render.render(write_still=True)
    for key in show:
        for o in (ctx[key] if isinstance(ctx[key], list) else [ctx[key]]):
            o.hide_render = True
    for key in hide:
        for o in (ctx[key] if isinstance(ctx[key], list) else [ctx[key]]):
            o.hide_render = False


def main():
    render_previews = "--no-render" not in sys.argv
    shelf, hull = write_unity_assets()
    ctx = build_scene(shelf, hull)
    bpy = ctx["bpy"]
    os.makedirs(PREVIEWS, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "mushroom_shelf.blend"))

    # Sources for other tools: the shelf and its collider alone, Blender axes converted back for FBX/OBJ (Y up).
    for o in bpy.context.scene.objects:
        o.select_set(o.name in ("Mushroom shelf", "Mushroom shelf collider"))
    bpy.ops.export_scene.fbx(filepath=os.path.join(HERE, "mushroom_shelf.fbx"), use_selection=True,
                             axis_forward="-Z", axis_up="Y", apply_unit_scale=True, bake_space_transform=True,
                             mesh_smooth_type="OFF", use_mesh_modifiers=False)
    try:
        bpy.ops.wm.obj_export(filepath=os.path.join(HERE, "mushroom_shelf.obj"), export_selected_objects=True,
                              forward_axis="NEGATIVE_Z", up_axis="Y", apply_modifiers=False, export_materials=True)
    except Exception as exc:  # the OBJ exporter is optional
        print("OBJ export skipped:", exc)

    if not render_previews:
        return
    cams = {
        "three_quarter": camera(bpy, "cam 3/4", (1.65, 1.15, 2.35), (0.05, -0.08, 0.38), lens=42),
        "side": camera(bpy, "cam side", (4.0, -0.05, 0.42), (0.0, -0.05, 0.42), ortho=1.75),
        "under": camera(bpy, "cam under", (0.55, -1.25, 1.75), (0.0, -0.15, 0.35), lens=40),
        "scale": camera(bpy, "cam scale", (0.35, 0.65, 5.6), (0.1, -0.55, 0.6), lens=34),
        "collider": camera(bpy, "cam collider", (1.4, 1.0, 2.1), (0.05, -0.08, 0.38), lens=42),
        "top": camera(bpy, "cam top", (0.05, 3.5, 0.42), (0.05, 0.0, 0.42), ortho=2.3),
    }
    cams["top"].rotation_euler = (0, 0, math.pi)   # straight down, the wall at the top of the picture
    render(ctx, "01_shelf_three_quarter.png", cams["three_quarter"], hide=("capsule",))
    render(ctx, "02_shelf_side_profile.png", cams["side"], hide=("capsule", "floor_mushroom"))
    render(ctx, "03_shelf_underside.png", cams["under"], show=("underlight",), hide=("capsule", "floor_mushroom"))
    render(ctx, "04_shelf_with_floor_mushroom_and_player.png", cams["scale"])
    render(ctx, "05_collider_wire_overlay.png", cams["collider"], show=("hull",), hide=("capsule", "floor_mushroom"))
    render(ctx, "06_shelf_top_plan.png", cams["top"], show=("hull",), hide=("capsule", "floor_mushroom"))


if __name__ == "__main__":
    main()
