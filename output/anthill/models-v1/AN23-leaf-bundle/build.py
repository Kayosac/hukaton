import sys, os, math, random
sys.path.insert(0, '/home/claude/ant/models'); from lib_sup import *
from mathutils import Vector, Matrix
D = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(D, 'out'); os.makedirs(OUT, exist_ok=True)
REND = '--norender' not in sys.argv

ak.reset()
rnd = random.Random(23)
lv = ak.Mesh('Props')
LAY = 0.088
# (L, W, dx, dy, rz deg, droop, seed) - four stacked leaves, then two crossing leaves on top (= six)
stack = [(1.12, 0.80, 0.00, 0.01, 3, 0.02, 1),
         (1.18, 0.84, -0.04, -0.02, -5, 0.03, 2),
         (1.10, 0.78, 0.05, 0.03, 7, 0.02, 3),
         (1.16, 0.82, -0.02, -0.03, -3, 0.035, 4)]
for i, (L, W, dx, dy, rz, dr, sd) in enumerate(stack):
    z = 0.035 + i * LAY + rnd.uniform(-.006, .006)
    leaf(lv, place(dx, dy, z, math.radians(rz), rx=math.radians(rnd.uniform(-2, 2))), L=L, W=W, thick=0.066,
         belly=.4, seed=sd, droop=dr, curl=0.012, serr=.14)
ztop = 0.035 + 4 * LAY
leaf(lv, place(-0.04, 0.06, ztop - .005, math.radians(16), rx=math.radians(-2)), L=1.08, W=0.66, thick=0.06,
     seed=7, droop=0.03, curl=0.02, serr=.16)
leaf(lv, place(0.03, -0.06, ztop + 0.035, math.radians(-20), rx=math.radians(3)), L=1.02, W=0.6, thick=0.055,
     seed=8, droop=0.06, curl=0.02, serr=.16)
lo = lv.to_object('AN23a_Props_leaves')

# rope band: two strands around the stack (YZ plane) near x = +0.1, squeezing the leaves, + knots
rp = ak.Mesh('Props')
TOPZ = ztop + 0.035 + 0.06
def band(x0, dz=0.0, sy=1.0, seed=0):
    pts = []
    prof = [(-0.30, -0.012), (0.0, -0.016), (0.30, -0.012), (0.41, 0.05), (0.40, 0.18), (0.38, 0.32), (0.41, 0.44),
            (0.33, TOPZ - .01), (0.12, TOPZ + .01), (-0.10, TOPZ + .012), (-0.33, TOPZ - .01), (-0.41, 0.44),
            (-0.38, 0.30), (-0.40, 0.16), (-0.41, 0.05)]
    r2 = random.Random(seed)
    for y, z in prof:
        pts.append(Vector((x0 + r2.uniform(-.01, .01) + 0.03 * (z / TOPZ), y * sy, z + dz)))
    return pts
closed_loop_tube(rp, band(0.085, seed=1), 0.021, 'rope', sides=5, metres_u=.35, seed=1)
closed_loop_tube(rp, band(0.135, dz=0.004, sy=1.01, seed=2), 0.021, 'rope', sides=5, metres_u=.35, seed=2)
# knot on top and knot on the front flank
rope_knot(rp, (0.12, -0.02, TOPZ + .01), r=0.02, size=0.045, seed=3, axis=(0, 0, 1),
          ends=[[(0.12, -0.04, TOPZ + .03), (0.17, -0.09, TOPZ + .02), (0.20, -0.14, TOPZ - .02)]])
rope_knot(rp, (0.115, -0.415, 0.24), r=0.02, size=0.04, seed=5, axis=(0, -1, 0))
ro = rp.to_object('AN23a_Props_rope')
# one object per material: merge leaves + rope into AN23a_Props
import bmesh
bm = bmesh.new(); bm.from_mesh(lo.data); bm.from_mesh(ro.data)
me = bpy.data.meshes.new('AN23a_Props'); bm.to_mesh(me); bm.free()
for p in me.polygons: p.use_smooth = False
ob = bpy.data.objects.new('AN23a_Props', me); bpy.context.scene.collection.objects.link(ob)
me.materials.append(ak.material('Props')); ob['mat'] = 'Props'
bpy.data.objects.remove(lo); bpy.data.objects.remove(ro)
ak.ground([ob])
check_normals([ob])
f = os.path.join(D, 'SM_AN23_LeafBundle.fbx'); ak.export_fbx([ob], f)
rec = ak.variant_record('AN23a', 'Tied leaf bundle', [ob], f)
print(rec)
write_manifest(OUT, 'AN23 leaf-bundle', [rec], solidity='solid', collider='box (1.15 x 0.85 x 0.55)', emissive=False,
               notes='Pivot bottom centre. 6 separate solid lens-shaped leaves (domed top, knife edge, serrated outline, '
               'drooping tips; 4 stacked + 2 crossing on top), double rope band with knot on top (+loose end) and on the '
               'front flank modelled. Veins painted (Props leaf_green), rope twist painted (Props rope).')
if REND:
    t, l = renders([ob], OUT, 'a', el=22)
    card('23-leaf-bundle', OUT, t, l, 'AN23 leaf bundle')
ak.save_blend(os.path.join(D, 'AN23_LeafBundle.blend'))
