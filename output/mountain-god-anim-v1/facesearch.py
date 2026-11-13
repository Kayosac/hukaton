"""Search a talk pose for C01's hand that hides nothing of the god seen from the player's spot."""
import sys, os, math, json
sys.argv = ["clips.py", "/tmp/god/prev/tf_out"]
sys.path.insert(0, "/tmp/god/prev"); sys.path.insert(0, "/home/claude/gm-art/god/anim")
import bpy, numpy as np
from mathutils import Vector, Matrix
import clips as C
import env
C.start_reach(); C.fit_dock()
body = bpy.data.objects["MG01_Body"]
me = body.data
names = {vg.index: vg.name for vg in body.vertex_groups}
dom = []
for v in me.vertices:
    g = max(v.groups, key=lambda x: x.weight, default=None)
    dom.append(names[g.group] if g else "")
handish = np.array([d.startswith("C01_") and d not in ("C01_seg00", "C01_shoulder") for d in dom])
red = bpy.data.materials.new("red"); red.diffuse_color = (1, 0, 0, 1)
white = bpy.data.materials.new("white"); white.diffuse_color = (1, 1, 1, 1)
me.materials.clear(); me.materials.append(white); me.materials.append(red)
pv = np.array([p.vertices[0] for p in me.polygons])
idx = np.zeros(len(me.polygons), int)
for i, p in enumerate(me.polygons):
    idx[i] = 1 if handish[list(p.vertices)].sum() >= 2 else 0
me.polygons.foreach_set("material_index", idx)
for o in bpy.data.objects:
    if o.type == 'MESH' and o != body: o.hide_render = True
sc, cam = env.scene((320, 240))
sh = sc.display.shading; sh.light = 'FLAT'; sh.color_type = 'MATERIAL'; sh.show_backface_culling = True
sc.world = bpy.data.worlds.new("w"); sc.world.color = (0, 0, 0)
sh.background_type = 'WORLD'
sc.render.image_settings.file_format = 'PNG'
def score(tag):
    worst = 0; misses = []
    for t in (0.0, 2.0, 4.0, 6.0, 9.0):
        C.apply(C.talk(t))
        eye = C.spot_of(C.FACE_TIP, C.FACE_DIR) + Vector((0, 0, .017))
        head = (C.whead("head_00") + C.wtail("head_00")) / 2
        tgt = head + Vector((0, 0, -1.5))  # face, beard, chest
        env.look(cam, eye, tgt, 50); cam.data.sensor_fit = 'VERTICAL'; cam.data.angle = math.radians(70); cam.data.clip_start = .003
        f = f"/tmp/god/rb/fs/{tag}_{t:g}.png"
        sc.render.filepath = f; bpy.ops.render.render(write_still=True)
        img = bpy.data.images.load(f); a = np.array(img.pixels[:]).reshape(240, 320, 4); bpy.data.images.remove(img)
        r = (a[..., 0] > .5) & (a[..., 1] < .3)
        cen = r[20:220, 40:280].mean()  # most of the view
        worst = max(worst, cen)
        misses.append((C.wtail("C01_f0_02") - C.FACE_TIP).length)
    top = max(C.whead(b).z for b in ("C01_shoulder", "C01_seg02", "C01_seg03", "C01_seg04", "C01_elbow", "C01_seg06", "C01_seg07"))
    return worst, max(misses), C.wrist_bend(), top
res = []
os.makedirs("/tmp/god/rb/fs", exist_ok=True)
for f_ in os.listdir("/tmp/god/rb/fs"): os.remove("/tmp/god/rb/fs/" + f_)
HEAD = C.HEAD
cands = []
for phi in (0, 25, 45):
    for d in (3.2, 4.5):
        for h in (-1.6, -3.0):
            for wd in ("down", "up", "out", "in"):
                cands.append((phi, d, h, wd))
if len(sys.argv) > 1 and sys.argv[-1].startswith("only="):
    pass
for phi, d, h, wd in cands:
    a = math.radians(phi)
    off = Vector((-math.sin(a) * d, -math.cos(a) * d, h))
    spot = HEAD + off
    fdir = Vector((HEAD.x - spot.x, HEAD.y - spot.y, 0)).normalized()
    C.FACE_SPOT = spot; C.FACE_DIR = fdir
    C.FACE_TIP = spot + fdir * C.SPOT_BACK + Vector((0, 0, -C.G.FINGER_R))
    side = fdir.cross(Vector((0, 0, 1))).normalized()
    w = {"up": Vector((0, 0, 1)), "down": Vector((0, 0, -1)), "out": side, "in": -side}[wd]
    C.M_FACE = C.hand_matrix(C.FACE_TIP, fdir, -fdir + w)
    def face(m):
        C.M_FACE = m
        return C.talk(0)
    try:
        C.M_FACE = C.fit_target(C.M_FACE, C.FACE_TIP, C.FACE_DIR, face, rounds=4)
        tag = f"p{phi}_d{d}_h{h}_{wd}"
        occ, miss, bend, top = score(tag)
    except Exception as e:
        print("FAIL", phi, d, h, wd, e); continue
    res.append(dict(phi=phi, d=d, h=h, wd=wd, occ=round(float(occ), 3), miss=round(float(miss), 3), bend=round(bend, 1), top=round(top, 2)))
    print("CAND", res[-1], flush=True)
json.dump(res, open("/tmp/god/rb/facesearch.json", "w"), indent=1)
