"""Rocks under MG01's support hands (S01–S03), built with the god-mountains kit's rock functions.

Each is a crooked summit rising out of the cloud sea: a crown just under the palm, slopes through the clawed fingertips
(the claws dig in 6–10 m), a broad shoulder and a body that goes down into the clouds; two or three crags stand beside the
hand where no finger or forearm passes. Built in metres at god coordinates x 100 (pivot: the god's origin), so in Unity
the rock goes to the god group's position with the same 180 deg turn and scale 1.

usage: python3 hand_rocks.py <kit.py> <tex dir> <hands.json> <out dir>"""
import json
import math
import os
import random
import sys

KIT, TEX, HANDS, OUT = sys.argv[1:5]
src = open(KIT).read()
src = src[:src.index("\nassets = [")]
G = {"__file__": KIT, "__name__": "kit"}
sys.argv = [KIT, TEX, OUT]
exec(compile(src, KIT, "exec"), G)
import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402
from mathutils.bvhtree import BVHTree  # noqa: E402

detail_rock, joints, asset = G["detail_rock"], G["joints"], G["asset"]
hands = json.load(open(HANDS))
S = 100.0  # metres per god unit
SEA = -50.0
BOTTOM = -330.0  # well inside the cloud layer (top -50, 260 m thick)
report = []


def ring(c, r, z, n, rnd, jitter=.18, phase=0.0, squash=(1, 1)):
    return [(c.x + math.cos(phase + 2 * math.pi * k / n) * r * squash[0] * rnd.uniform(1 - jitter, 1 + jitter),
             c.y + math.sin(phase + 2 * math.pi * k / n) * r * squash[1] * rnd.uniform(1 - jitter, 1 + jitter),
             z + rnd.uniform(-1, 1) * r * .04) for k in range(n)]


def build(name, h, seed):
    rnd = random.Random(seed)
    e = asset(f"MG01 Hand Rock {name}")
    c = Vector(h["knuckle_center"]) * S
    c.z = 0
    r = h["knuckle_r"] * S
    crown_z = h["palm_under_z"] * S - 4.0
    tips = [Vector(t) * S for t in h["tips"].values()]
    # the forearm comes down from here: keep crags out of that side
    wh, wt = Vector(h["wrist_head"]) * S, Vector(h["wrist_tail"]) * S
    arm_dir = Vector((wh.x - wt.x, wh.y - wt.y, 0)).normalized()
    # crown under the palm, the claws on the slope, then shoulder, body, roots in the cloud
    pts = ring(c, r * .5, crown_z, 7, rnd, phase=rnd.uniform(0, 6))
    pts += ring(c, r * .78, crown_z - r * .1, 7, rnd, phase=rnd.uniform(0, 6))
    for t in tips:
        d = Vector((t.x - c.x, t.y - c.y, 0))
        p = t + d.normalized() * 4.0
        pts.append((p.x, p.y, t.z - 9.0))
    pts += ring(c, r * 1.75, -45, 9, rnd, phase=rnd.uniform(0, 6), squash=(1.1, .9))
    pts += ring(c, r * 2.3, SEA - 30, 8, rnd, phase=rnd.uniform(0, 6))
    pts += ring(c, r * 2.6, BOTTOM, 8, rnd, phase=rnd.uniform(0, 6))
    spec = [("bed", -20 + rnd.uniform(-8, 8), 9), ("bed", -95, 12),
            ("joint", c.x + r * rnd.uniform(-.9, -.5), -60, 14), ("joint", c.x + r * rnd.uniform(.5, .9), -70, 18)]
    obs = detail_rock(f"MG01HR_{name}", pts, e, joints(spec, seed), chips=3, chip=(10, 26), bulge=.03, bevel=2.5,
                      wobble=.012, far=True, seed=seed, cuts_sub=2)
    # crags beside the hand: angles away from the forearm and from every finger
    angs_f = [math.atan2(t.y - c.y, t.x - c.x) for t in tips]
    ang_arm = math.atan2(arm_dir.y, arm_dir.x)
    tip_d = sum((Vector((t.x - c.x, t.y - c.y, 0))).length for t in tips) / len(tips)
    crags = 0
    for k in range(60):
        if crags >= 3:
            break
        a = rnd.uniform(-math.pi, math.pi)
        if abs(math.remainder(a - ang_arm, 2 * math.pi)) < math.radians(70):
            continue
        if min(abs(math.remainder(a - af, 2 * math.pi)) for af in angs_f) < math.radians(17):
            continue
        dist = tip_d * rnd.uniform(1.15, 1.45)
        w = r * rnd.uniform(.22, .34)
        hh = rnd.uniform(55, 120)
        cx, cy = c.x + math.cos(a) * dist, c.y + math.sin(a) * dist
        lean = Vector((math.cos(a), math.sin(a), 0)) * w * rnd.uniform(.1, .35)
        base = [(cx + dx * w * rnd.uniform(.8, 1.1), cy + dy * w * rnd.uniform(.8, 1.1), -80) for dx in (-1, 1) for dy in (-1, 1)]
        apex = [(cx + lean.x + dx * w * rnd.uniform(.35, .6), cy + lean.y + dy * w * rnd.uniform(.35, .6), hh * rnd.uniform(.8, 1)) for dx in (-1, 1) for dy in (-1, 1)]
        detail_rock(f"MG01HR_{name}_crag{crags}", base + apex, e, joints([("bed", hh * .35, 10)], seed + 30 + k), chips=2,
                    chip=(6, 14), bulge=.02, bevel=2, wobble=.01, far=True, seed=seed * 7 + k)
        crags += 1
    # how the claws sit: distance from each fingertip (bone end) down to the rock
    dg = bpy.context.evaluated_depsgraph_get()
    gaps = []
    for t in tips:
        best = None
        for o in e.children:
            bvh = BVHTree.FromObject(o, dg)
            hit = bvh.ray_cast(o.matrix_world.inverted() @ (t + Vector((0, 0, 30))), Vector((0, 0, -1)), 200)
            if hit[0] is not None:
                z = (o.matrix_world @ hit[0]).z
                best = z if best is None else max(best, z)
        gaps.append(round(t.z - best, 1) if best is not None else None)
    palm = None
    for o in e.children:
        bvh = BVHTree.FromObject(o, dg)
        hit = bvh.ray_cast(o.matrix_world.inverted() @ Vector((c.x, c.y, 300)), Vector((0, 0, -1)), 600)
        if hit[0] is not None:
            z = (o.matrix_world @ hit[0]).z
            palm = z if palm is None else max(palm, z)
    if max(g for g in gaps if g is not None) > 13.0 or None in gaps or palm is None or palm > h["palm_under_z"] * S:
        for o in list(e.children):
            bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.objects.remove(e, do_unlink=True)
        return None
    report.append(f"{name} (seed {seed}): tip bone-end height over the rock (m) {gaps}; rock under the palm centre {palm:.1f} m, "
                  f"palm underside {h['palm_under_z'] * S:.1f} m; parts {len(e.children)}")
    return e


rocks = []
for n, s in (("S01", 501), ("S02", 502), ("S03", 503)):
    for k in range(40):
        e = build(n, hands[n], s + 10 * k)
        if e:
            rocks.append(e)
            break
    else:
        raise SystemExit("no seed fits " + n)
for e in rocks:
    for o in bpy.context.scene.objects:
        o.select_set(False)
    e.select_set(True)
    for ch in e.children:
        ch.select_set(True)
    fn = os.path.join(OUT, e.name.replace(" ", "_") + ".fbx")
    bpy.ops.export_scene.fbx(filepath=fn, use_selection=True, object_types={"EMPTY", "MESH"}, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             mesh_smooth_type="FACE", add_leaf_bones=False, use_triangles=True, path_mode="STRIP")
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "MG01_hand_rocks.blend"))
open(os.path.join(OUT, "hand-rocks-report.md"), "w").write("\n".join("- " + r for r in report) + "\n")
print("\n".join(report))
