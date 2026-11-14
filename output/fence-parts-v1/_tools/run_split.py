import sys, json, os; sys.path.insert(0, '/home/claude/fence/work')
import bpy, fk, sk, numpy as np
from mathutils import Vector
fbx, vid = sys.argv[1], sys.argv[2]
OUT = f'/home/claude/fence/out/{vid}/'; os.makedirs(OUT, exist_ok=True)
parts, cols, grass = fk.split(fbx, vid)
rows = []
for p, c in zip(parts, cols):
    P = fk.V(p); d = P.max(0) - P.min(0)
    rows.append({'name': p.name, 'tris': fk.tris(p), 'size_m': [round(float(x), 3) for x in d],
                 'pivot': [round(x, 3) for x in p.location], 'materials': [m.name.split('.')[0] for m in p.data.materials],
                 'col': c.name, 'col_tris': fk.tris(c), 'convex': fk.is_convex(c)})
if grass: rows.append({'name': grass.name, 'tris': fk.tris(grass), 'note': 'grass at the base: static, no collider'})
objs = parts + cols + ([grass] if grass else [])
name = fbx.replace('.fbx', '_Parts.fbx')
fk.export(objs, OUT + name)
json.dump(rows, open(OUT + 'manifest.json', 'w'), indent=1, ensure_ascii=False)
for r in rows: print(r)
# renders: intact (parts), exploded parts, collision
vis = parts + ([grass] if grass else [])
t = [sk.render(vis, OUT + 'a.png', az=-30, el=18, size=(520, 420), samples=8)]
# exploded: push parts apart from section centre
C = np.mean([np.array(p.location[:]) for p in parts], 0)
orig = [p.location.copy() for p in parts]
for p in parts:
    d = np.array(p.location[:]) - C; d[2] = abs(d[2]) * .6 + .3
    p.location = p.location + Vector((d * 1.1).tolist())
    p.rotation_euler = (0.35 * np.sign(d[0] + .01), 0.2, 0.3 * np.sign(d[1] + .01))
bpy.context.view_layer.update()
t.append(sk.render(parts, OUT + 'b.png', az=-30, el=18, size=(520, 420), samples=8))
for p, o in zip(parts, orig): p.location = o; p.rotation_euler = (0, 0, 0)
# collision view: COL as solid flat grey
m = bpy.data.materials.new('colm'); m.use_nodes = True; m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (.35, .6, .9, 1)
for c in cols:
    c.data.materials.append(m)
t.append(sk.render(cols, OUT + 'c.png', az=-30, el=18, size=(520, 420), samples=8))
sk.sheet(t, OUT + 'card.jpg', cols=3, labels=[f'{vid} parts', f'{vid} exploded', f'{vid} _COL'])
sk.save_blend(OUT + f'{vid}_parts.blend')
