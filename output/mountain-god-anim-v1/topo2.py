import sys, bpy, bmesh
sys.path.insert(0, "/home/claude/gm-art/god/anim")
import godrig as G
arm = G.setup()
body = bpy.data.objects["MG01_Body"]
bm = bmesh.new(); bm.from_mesh(body.data)
bound = sum(1 for e in bm.edges if e.is_boundary)
nonman = sum(1 for e in bm.edges if not e.is_manifold and not e.is_boundary)
flip = 0
for e in bm.edges:
    if len(e.link_faces) == 2:
        dirs = []
        for f in e.link_faces:
            l = next(l for l in f.loops if l.edge == e)
            dirs.append((l.vert, l.link_loop_next.vert))
        if dirs[0][0] == dirs[1][0]:
            flip += 1
print("TOPO verts", len(bm.verts), "faces", len(bm.faces), "boundary", bound, "nonmanifold", nonman, "flipped-winding edges", flip)
# components
seen = set(); comps = []
for f in bm.faces:
    if f in seen: continue
    n = 0; st = [f]
    while st:
        g = st.pop()
        if g in seen: continue
        seen.add(g); n += 1
        for e in g.edges:
            for h in e.link_faces:
                if h not in seen: st.append(h)
    comps.append(n)
print("TOPO components", sorted(comps, reverse=True)[:12])
