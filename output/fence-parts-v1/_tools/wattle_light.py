"""SW19b wattle: light collision. Intact section = posts + stakes + 4 panel boxes (the hole stays open);
each rod's own _COL becomes a 12-tri box (used only once it is knocked loose)."""
import sys, json; sys.path.insert(0, '/home/claude/fence/work')
import bpy, bmesh, numpy as np, fk, sk
from mathutils import Vector
OUT = '/home/claude/fence/out/SW19b/'
bpy.ops.wm.open_mainfile(filepath=OUT + 'SW19b_parts.blend')
def world_pts(o): return np.array([(o.matrix_world @ v.co)[:] for v in o.data.vertices])
def box(name, lo, hi, at=None):
    c = (lo + hi) / 2 if at is None else at
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts: v.co = Vector([(lo[i] + hi[i]) / 2 - c[i] + v.co[i] * (hi[i] - lo[i]) for i in range(3)])
    bmesh.ops.triangulate(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.get(name)
    if o: o.data = me
    else:
        o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    o.location = Vector(c.tolist()); o.display_type = 'WIRE'
    return o
rods = [o for o in bpy.data.objects if o.type == 'MESH' and '_Rod_' in o.name and not o.name.endswith('_COL')]
# 1) each rod: own _COL = its bounding box in section space (rods lie along X), same pivot as the rod
for r in rods:
    P = world_pts(r); box(r.name + '_COL', P.min(0), P.max(0), at=np.array(r.location[:]))
# 2) section boxes from rod groups
info = []
for r in rods:
    P = world_pts(r); info.append((r.name, P.min(0), P.max(0)))
full = [(n, lo, hi) for n, lo, hi in info if hi[0] - lo[0] > 2.0]
left = [(n, lo, hi) for n, lo, hi in info if 1.0 < hi[0] - lo[0] <= 2.0]
short = [(n, lo, hi) for n, lo, hi in info if hi[0] - lo[0] <= 1.0]
right = [g for g in short if (g[1][2] + g[2][2]) / 2 < 0.95]
top_rest = [g for g in short if (g[1][2] + g[2][2]) / 2 >= 0.95]   # short bits on the top edge
zmid = np.median([(lo[2] + hi[2]) / 2 for _, lo, hi in full])
groups = {
 'Bottom': [g for g in full if (g[1][2] + g[2][2]) / 2 < 0.6],
 'Top': [g for g in full if (g[1][2] + g[2][2]) / 2 >= 0.6] + top_rest,
 'MidLeft': left, 'MidRight': right}
sec = []
for i, (k, g) in enumerate(groups.items()):
    lo = np.min([x[1] for x in g], 0); hi = np.max([x[2] for x in g], 0)
    sec.append(box(f'SW19b_Section_{k}_COL', lo, hi))
    print(k, [x[0] for x in g], lo.round(2), hi.round(2))
bpy.context.view_layer.update()
# export: visual parts + their _COL + section boxes
objs = [o for o in bpy.data.objects if o.type == 'MESH' and o.name not in ('water',)]
fk.export(objs, OUT + 'SM_SW19_WaterFenceWattle_Parts.fbx')
sk.save_blend(OUT + 'SW19b_parts.blend')
# card tile: intact-section collision = posts + stakes + section boxes
m = bpy.data.materials.get('colm')
intact = [o for o in objs if o.name.endswith('_COL') and ('_Post_' in o.name or '_Stake_' in o.name or '_Section_' in o.name)]
for o in intact:
    if not o.data.materials: o.data.materials.append(m)
sk.render(intact, OUT + 'c.png', az=-30, el=18, size=(520, 420), samples=8)
sk.sheet([OUT + 'a.png', OUT + 'b.png', OUT + 'c.png'], OUT + 'card.jpg', cols=3,
         labels=['SW19b parts', 'SW19b exploded', 'SW19b section _COL (9)'])
man = json.load(open(OUT + 'manifest.json'))
for r in man:
    if '_Rod_' in r.get('name', ''): r['col_tris'] = 12; r['col_note'] = 'box, for the loose rod only'
man.append({'section_collision': [o.name for o in intact], 'count': len(intact),
            'note': 'intact section: posts + stakes + 4 panel boxes (Bottom/Top/MidLeft/MidRight), the hole stays open; '
                    'rod _COL boxes are for rods knocked loose'})
json.dump(man, open(OUT + 'manifest.json', 'w'), indent=1, ensure_ascii=False)
print('section colliders', len(intact), sum(fk.tris(o) for o in intact), 'tris')
