"""UVs for the rebuilt skin and its colour baked from the old skin (both in the spread pose)."""
import os
WORK = os.environ.get("REBUILD_WORK", "/tmp/god/rb")

import sys, bpy, numpy as np, time, os
a = sys.argv[sys.argv.index("--")+1:]
res, out_png, size = a[0], a[1], int(a[2])
t0 = time.time()
bpy.ops.wm.open_mainfile(filepath=WORK + "/split.blend")
TEX = "/mnt/user-data/uploads/hukaton/output/mountain-god-model-v3/textures/T_MG01_Body_BaseColor.png"
for o in list(bpy.data.objects):
    if o.name != "MG01_Body":
        bpy.data.objects.remove(o, do_unlink=True)
hi = bpy.data.objects["MG01_Body"]
hi.modifiers.clear(); hi.parent = None
V0 = np.load(WORK + "/split.npz")["V"]
P = np.load(a[5] if len(a) > 5 else WORK + "/relaxed.npz")["X"][:len(V0)]
hi.data.vertices.foreach_set("co", P.ravel())
hi.data.update()
m_hi = hi.data.materials[0]
img_src = next(n for n in m_hi.node_tree.nodes if n.type == 'TEX_IMAGE')
img_src.image = bpy.data.images.load(TEX, check_existing=True)
img_src.interpolation = 'Linear'
z = np.load(res); RP, RF = z["RP"], z["RF"]
me = bpy.data.meshes.new("MG01_BodyLo"); me.from_pydata(RP.tolist(), [], RF.tolist()); me.update()
me.polygons.foreach_set("use_smooth", [True]*len(me.polygons))
lo = bpy.data.objects.new("Lo", me); bpy.context.scene.collection.objects.link(lo)
vl = bpy.context.view_layer
for o in bpy.context.scene.objects: o.select_set(o == lo)
vl.objects.active = lo
# UVs by xatlas: a couple of thousand charts packed tight (Blender's smart project left 85 % of the texture empty)
import xatlas
atlas = xatlas.Atlas(); atlas.add_mesh(RP.astype(np.float32), RF.astype(np.uint32))
co = xatlas.ChartOptions(); co.max_iterations = 2
po = xatlas.PackOptions(); po.resolution = size; po.padding = 6; po.bilinear = True
atlas.generate(co, po)
_vm, idx, uvs = atlas[0]
assert idx.shape[0] == len(RF)
uvl_ = uvs[idx].reshape(-1, 2)
uvlay = me.uv_layers.new(name="UVMap"); uvlay.data.foreach_set("uv", uvl_.ravel())
print("xatlas charts", atlas.chart_count, "utilization", round(atlas.utilization, 2))
print("unwrapped", time.time()-t0)
img = bpy.data.images.new("T_MG01_BodyAnim_BaseColor", size, size, alpha=False)
img.generated_color = (1.0, 0.0, 1.0, 1)  # magenta: where nothing was hit
mat = bpy.data.materials.new("M_MG01_BodyAnim"); mat.use_nodes = True
nt = mat.node_tree; tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = img
nt.links.new(tn.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"]); nt.nodes.active = tn
me.materials.append(mat)
sc = bpy.context.scene
sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = 1
sc.render.bake.use_selected_to_active = True
sc.render.bake.cage_extrusion = float(a[3]) if len(a) > 3 else .06
sc.render.bake.max_ray_distance = float(a[4]) if len(a) > 4 else .15
sc.render.bake.margin = 12
for o in sc.objects: o.select_set(o in (lo, hi))
vl.objects.active = lo
bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, use_selected_to_active=True, margin=12)
px1 = np.array(img.pixels[:]).reshape(size, size, 4)
# the healed places lie away from the old skin: there the colour comes from the nearest old skin, per vertex (smooth
# across a cap), baked from a colour attribute of the new skin itself
from mathutils import Vector as _V
from mathutils.bvhtree import BVHTree
from PIL import Image as _Img
tex = np.asarray(_Img.open(TEX).convert("RGB")).astype(np.float32) / 255.0
tex = np.where(tex <= .04045, tex / 12.92, ((tex + .055) / 1.055) ** 2.4)  # linear, like the bake
th, tw = tex.shape[:2]
hme = hi.data; hme.calc_loop_triangles()
huv = np.zeros(len(hme.loops) * 2); hme.uv_layers[0].data.foreach_get("uv", huv); huv = huv.reshape(-1, 2)
hco = np.zeros(len(hme.vertices) * 3); hme.vertices.foreach_get("co", hco); hco = hco.reshape(-1, 3)
tris = [(lt.vertices[:], lt.loops[:]) for lt in hme.loop_triangles]
bvh = BVHTree.FromPolygons([_V(p) for p in hco], [t[0] for t in tris])
vc = np.zeros((len(RP), 3))
for i, p in enumerate(RP):
    loc, nrm, fi, dd = bvh.find_nearest(_V(p))
    (a0, a1, a2), (l0, l1, l2) = tris[fi]
    A, B, C = hco[a0], hco[a1], hco[a2]; P = np.array(loc)
    v0, v1, v2 = B - A, C - A, P - A
    d00, d01, d11, d20, d21 = v0 @ v0, v0 @ v1, v1 @ v1, v2 @ v0, v2 @ v1
    den = d00 * d11 - d01 * d01 or 1e-12
    w1 = (d11 * d20 - d01 * d21) / den; w2 = (d00 * d21 - d01 * d20) / den; w0 = 1 - w1 - w2
    u, v = w0 * huv[l0] + w1 * huv[l1] + w2 * huv[l2]
    vc[i] = tex[min(th - 1, max(0, int((1 - v % 1) * th))), min(tw - 1, max(0, int((u % 1) * tw)))]
# soften: the nearest old skin of a cap is its rim; average over neighbours a few times
import scipy.sparse as _sp
E_ = np.concatenate([RF[:, [0, 1]], RF[:, [1, 2]], RF[:, [2, 0]]])
A_ = _sp.coo_matrix((np.ones(len(E_)), (E_[:, 0], E_[:, 1])), shape=(len(RP), len(RP))).tocsr(); A_ = ((A_ + A_.T) > 0).astype(float)
Avg_ = _sp.diags(1 / np.maximum(np.asarray(A_.sum(1)).ravel(), 1)) @ A_
for _ in range(6):
    vc = .5 * vc + .5 * (Avg_ @ vc)
# a dark stroke of the old map next to a healed place must not spread over it
lum_ = vc.mean(1); floor_ = .75 * np.median(lum_)
vc *= np.maximum(1.0, floor_ / np.maximum(lum_, 1e-4))[:, None]
ca = me.color_attributes.new("near", 'FLOAT_COLOR', 'POINT')
ca.data.foreach_set("color", np.c_[vc, np.ones(len(vc))].ravel())
img2 = bpy.data.images.new("near", size, size, alpha=False); img2.generated_color = (0, 0, 0, 1)
an = nt.nodes.new("ShaderNodeVertexColor"); an.layer_name = "near"
bsdf = nt.nodes["Principled BSDF"]
nt.links.new(an.outputs["Color"], bsdf.inputs["Base Color"])
tn.image = img2
bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, use_selected_to_active=False, margin=12)
px2 = np.array(img2.pixels[:]).reshape(size, size, 4)
tn.image = img
nt.links.new(tn.outputs["Color"], bsdf.inputs["Base Color"])
from scipy.ndimage import binary_dilation
lum1 = px1[..., :3].mean(-1)
miss1 = binary_dilation(lum1 < .004, iterations=1)
miss2 = np.zeros_like(miss1)
mean = px1[~miss1, :3].mean(0)
out = px1.copy()
out[miss1] = px2[miss1]
# healed places (new skin away from the old): their colour comes from around, not from whatever a ray met
healed_f = (z["dist"][RF] > .03).any(1)
from PIL import Image as _I2, ImageDraw as _D2
uvh = np.zeros(len(me.loops) * 2); me.uv_layers[0].data.foreach_get("uv", uvh); uvh = uvh.reshape(-1, 3, 2)
mh = _I2.new("L", (size, size), 0); dh = _D2.Draw(mh)
for tri in uvh[healed_f]:
    dh.polygon([(float(u * size), float((1 - v) * size)) for u, v in tri], fill=255)
hm = binary_dilation(np.asarray(mh)[::-1] > 0, iterations=1)
out[hm] = px2[hm]
print("healed texels", int(hm.sum()))
# where the islands are (rasterized UV triangles, a pixel in): only those pixels are trusted; every other pixel takes the
# colour of the nearest trusted one, so filtering and mip levels never pull in the dark border or the background
from PIL import Image as _I, ImageDraw as _D
uvl = np.zeros(len(me.loops) * 2); me.uv_layers[0].data.foreach_get("uv", uvl); uvl = uvl.reshape(-1, 3, 2)
msk = _I.new("L", (size, size), 0); dr = _D.Draw(msk)
for tri in uvl:
    dr.polygon([(float(u * size), float((1 - v) * size)) for u, v in tri], fill=255)
inside = np.asarray(msk)[::-1] > 0  # image rows run bottom-up in Blender
from scipy.ndimage import binary_erosion, distance_transform_edt
good = binary_erosion(inside, iterations=1) & ~(miss1 & miss2)
_, (iy, ix) = distance_transform_edt(~good, return_indices=True)
out = out[iy, ix]
out[..., 3] = 1
print("missed near", int(miss1.sum()), "missed both", int((miss1 & miss2).sum()), "inside", int(inside.sum()), "good", int(good.sum()))
img.pixels[:] = out.ravel()
print("baked", time.time()-t0)
img.filepath_raw = out_png; img.file_format = 'PNG'; img.save()
uv = np.zeros(len(me.loops)*2); me.uv_layers[0].data.foreach_get("uv", uv)
np.save(out_png.replace(".png", "_uv.npy"), uv.reshape(-1, 2))
print("saved", time.time()-t0)
