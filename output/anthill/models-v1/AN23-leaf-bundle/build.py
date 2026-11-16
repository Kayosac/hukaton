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
STEMS = []
def leaf_s(M, L, W, seed, **kw):
    """leaf + its petiole stem sticking out of the -x tip."""
    leaf(lv, M, L=L, W=W, seed=seed, n=11, tears=2, **kw)
    r2 = random.Random(seed)
    p0 = M @ Vector((-L / 2 + .04, 0, 0.004)); d = (M.to_3x3() @ Vector((-1, r2.uniform(-.3, .3), 0))).normalized()
    p1 = p0 + d * .07 + Vector((0, 0, -.01)); p2 = p1 + d * .06 + Vector((0, r2.uniform(-.02, .02), -.02))
    STEMS.append([p0, p1, p2])
for i, (L, W, dx, dy, rz, dr, sd) in enumerate(stack):
    z = 0.035 + i * LAY + rnd.uniform(-.006, .006)
    leaf_s(place(dx, dy, z, math.radians(rz), rx=math.radians(rnd.uniform(-2, 2))), L, W, sd, thick=0.066,
           belly=.4, droop=dr, curl=0.012, serr=.2)
ztop = 0.035 + 4 * LAY
leaf_s(place(-0.04, 0.06, ztop - .005, math.radians(16), rx=math.radians(-2)), 1.08, 0.66, 7, thick=0.06,
       droop=0.03, curl=0.02, serr=.2)
leaf_s(place(0.03, -0.06, ztop + 0.035, math.radians(-20), rx=math.radians(3)), 1.02, 0.6, 8, thick=0.055,
       droop=0.06, curl=0.02, serr=.2)
# extra thinner leaves sandwiched between layers, sticking out at odd angles (front, back, ends)
for i, (z, rz, dx, dy, L, W, reg) in enumerate(((0.08, 75, -0.25, -0.1, .78, .36, 'leaf_green'),
                                                 (0.17, -62, 0.3, 0.12, .72, .32, 'leaf_dry_1'),
                                                 (0.26, 100, -0.05, 0.18, .8, .34, 'leaf_green'),
                                                 (0.33, 28, -0.32, -0.05, .7, .3, 'leaf_dry_0'),
                                                 (0.12, -15, 0.25, -0.02, .85, .4, 'leaf_green'))):
    leaf(lv, place(dx, dy, z, math.radians(rz), rx=math.radians(rnd.uniform(-4, 4))), L=L, W=W, thick=0.03,
         belly=.4, seed=20 + i, region=reg, droop=.04, curl=.01, serr=.22, n=9, tears=1)
# leaf bits fallen at the base
for i in range(5):
    a = rnd.uniform(0, 6.3)
    small_leaf(lv, (math.cos(a) * .7, math.sin(a) * .55, 0.012), rnd.uniform(.16, .26), rnd.uniform(.08, .12),
               rnd.choice(('leaf_green', 'leaf_dry_0', 'leaf_dry_1')), seed=40 + i, rz=a + rnd.uniform(-1, 1))
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
# third, thinner strand wrapped crooked over the two, an X wrap on top, petioles, frayed loose ends, twig, crumbs
closed_loop_tube(rp, band(0.11, dz=0.012, sy=1.03, seed=6), 0.013, 'rope', sides=4, metres_u=.35, seed=6)
for sgn in (-1, 1):
    atlas_tube(rp, [(0.04, sgn * 0.28, TOPZ - .005), (0.11, sgn * 0.1, TOPZ + .02), (0.19, -sgn * 0.08, TOPZ + .02),
                    (0.24, -sgn * 0.26, TOPZ - .005)], 0.014, 'rope', sides=4, metres_u=.35, caps=(False, False))
for st in STEMS:
    atlas_tube(rp, st, [.012, .009, .006], 'needles', sides=4, metres_u=.2, caps=(False, True))
end1 = [(0.2, -0.14, TOPZ - .02), (0.23, -0.2, TOPZ - .05), (0.24, -0.25, TOPZ - .1)]
atlas_tube(rp, end1, [.018, .016, .013], 'rope', sides=5, metres_u=.3, caps=(False, False))
fibres(rp, end1[-1], (0.1, -0.3, -1), n=4, length=.06, r=.005, seed=11)
end2 = [(0.115, -0.44, 0.22), (0.13, -0.47, 0.15), (0.12, -0.48, 0.06), (0.1, -0.5, 0.02)]
atlas_tube(rp, end2, [.018, .017, .015, .013], 'rope', sides=5, metres_u=.3, caps=(False, False))
fibres(rp, end2[-1], (0, -1, -.4), n=4, length=.07, r=.005, seed=12)
end3 = [(0.1, -0.43, 0.25), (0.06, -0.47, 0.2), (0.05, -0.5, 0.15)]
atlas_tube(rp, end3, [.016, .014, .012], 'rope', sides=4, metres_u=.3, caps=(False, False))
fibres(rp, end3[-1], (-.3, -1, -.5), n=3, length=.05, r=.004, seed=13)
twig(rp, (-0.1, 0.2, 0.21), (-0.6, 1, 0.25), .5, r=.016, seed=14, branches=2, segs=4)
twig(rp, (0.35, -0.1, 0.13), (1, -0.5, 0.1), .35, r=.011, seed=15, branches=1, segs=3)
scatter(rp, 24, ring_sampler(0, 0, 0.55, 0.85, sy=.75), (.01, .022), 'leaf_dry_0', seed=16, sides=4)
scatter(rp, 10, ring_sampler(0, 0, 0.55, 0.85, sy=.75), (.012, .025), 'earth_props', seed=17, sides=4)
ro = rp.to_object('AN23a_Props_rope')
# one object per material: merge leaves + rope into AN23a_Props
import bmesh
bm = bmesh.new(); bm.from_mesh(lo.data); bm.from_mesh(ro.data)
me = bpy.data.meshes.new('AN23a_Props'); bm.to_mesh(me); bm.free()
for p in me.polygons: p.use_smooth = False
ob = bpy.data.objects.new('AN23a_Props', me); bpy.context.scene.collection.objects.link(ob)
me.materials.append(ak.material('Props')); ob['mat'] = 'Props'
bpy.data.objects.remove(lo); bpy.data.objects.remove(ro)
ak.ground([ob], center_xy=False)
check_normals([ob])
f = os.path.join(D, 'SM_AN23_LeafBundle.fbx'); ak.export_fbx([ob], f)
rec = ak.variant_record('AN23a', 'Tied leaf bundle', [ob], f)
print(rec)
write_manifest(OUT, 'AN23 leaf-bundle', [rec], solidity='solid', collider='box (1.15 x 0.85 x 0.55)', emissive=False,
               notes='Pivot bottom centre. 6 main solid lens leaves (4 stacked + 2 crossing on top) with torn bites and '
               'petiole stems, 5 thinner leaves sandwiched between layers sticking out at odd angles, double rope band + '
               'a third thin strand + an X wrap on top, knots on top and front, 3 loose ends with frayed fibres, 2 twigs '
               'stuck in, 5 fallen leaf bits and ~34 crumbs at the base. Veins/rope twist painted.')
if REND:
    t, l = renders([ob], OUT, "a", el=24, margin=.85)
    card('23-leaf-bundle', OUT, t, l, 'AN23 leaf bundle (v2 detail)')
ak.save_blend(os.path.join(D, 'AN23_LeafBundle.blend'))
