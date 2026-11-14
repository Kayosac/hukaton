# Breakables debris — shared brief (Hakaton, Unity game)

Debris pieces for breakable props in a crooked low-poly PSX forest style. You model in Blender via the `bpy` 5.3
Python module (`python3 script.py`, no GUI). Machine has 2 CPU cores shared by 3 agents: render small
(≤ 420 px tiles, 8–10 samples), never render in tight loops; a full card render per round is fine.

## Look at first
- Source props (what breaks): `/home/claude/brk/work/src_sheet.png` (renders), source FBX in `/home/claude/brk/ref/`.
- Atlas textures: `/home/claude/brk/ref/T_Forest_Props.png` (256²), `T_Forest_Special.png` (256×1024),
  `T_Forest_Structures_Wood.png`, `T_Pumpkin.png`. Labelled Props regions: `/home/claude/brk/work/regions.png`
  (pixel rects in `bk.PROPS`). Inspect source UVs with `/home/claude/brk/work/inspect_src.py`.
- Example of the finished style + kit usage: `/home/claude/brk/work/crate.py` → `/home/claude/brk/work/crate_card.jpg`
  (a new iron-bound crate: crooked boxes, atlas UVs, `_COL`, export, card).

## Kit — `/home/claude/brk/work/bk.py` (do NOT edit it; put extra helpers in your own script)
- `bk.B([mat names])` mesh builder: `face(pts, uvs, m)`, `proj_face(...)` planar UV, `box(...)` crooked wood box
  with grain-aware UVs, `obj(name)` → object with origin at its bounds centre (pivot = centre of the piece).
- `bk.uvr(mat, rect, fu, fv)` pixel rect → UV; `bk.sub(rect, ...)` sub-rect; regions `bk.PROPS[...]`.
- Materials: use ONLY existing game materials by exact name: `M_Forest_Props` (opaque atlas), `M_Forest_PropsCutout`
  (same atlas, alpha clip), `M_Forest_PropsGlow`, `M_Forest_Special`, `M_Forest_Structures_Wood`, `M_Pumpkin`.
  One object may hold 2 materials (submeshes), e.g. pumpkin skin `M_Pumpkin` + flesh `M_Forest_Props` amber.
- `bk.col(o)` convex hull collider `<name>_COL` (≤255 tris, same origin); `bk.box_col(o)` 12-tri box.
  `bk.is_convex`, `bk.check_manifold`, `bk.report(objs)` → manifest rows.
- `bk.render(objs, png, az, el, size, samples)`, `bk.sheet(...)`, `bk.export_fbx(objs, path)`, `bk.save_blend`.
- `bk.import_src('SM_PR09_PotLow.fbx')` imports a source prop with its textures (to compare / sample shapes).

## Technical requirements (from the user — strict)
- FBX, 1 unit = 1 m. Blender: Z up, front faces −Y; `bk.export_fbx` maps that to Unity Y up, front +Z.
- Pivot of every piece in the CENTRE of the piece (`B.obj()` default does it).
- Each piece = a separate object with a clear name: `DB0N_<Set>_<Piece>` (e.g. `DB01_Clay_Rim`).
- Collision = separate objects named `<piece name>_COL`, each CONVEX, ≤ 255 tris, same origin as its piece.
  Thin curved shards: a convex hull of the shard is fine (slightly fatter than the shard is OK).
- Budget per piece: 60–120 tris (stay in it; tiny pieces may be lower). No LODs.
- Style: crooked, irregular, flat-shaded big facets, like the source props (hex/oct sections, jittered verts, no
  bevels, no smoothing). Fine detail is painted by the atlas, not modelled. Texture direction sensible, no visible
  stretching worse than the sources, no black/inside-out faces (check normals), closed meshes (open_edges 0).
- Broken edges must read as BROKEN: jagged outlines, irregular thickness, splinters for wood. Shards are the same
  material/colours as the whole prop so they read as "that pot broke".
- Lay the pieces out side by side in the FBX (spaced ~2× their size along X) so the file is easy to inspect.

## Deliver (in `/home/claude/brk/out/<set>/`)
- `SM_DB0N_<Set>.fbx` (all pieces + their _COL), `<set>.blend`, `build.py` (copy of your script),
  `card.jpg`: every piece rendered from 2 angles (+ one shot of all pieces lying together next to the source prop for
  scale, `bk.import_src`), `manifest.json` = `bk.report(objs)` + a note per piece.
- LOOK at your card critically (Read tool on the jpg) and iterate 2–4 rounds: silhouette, does it read as the
  described piece, scale vs the source prop, texture, crookedness. Then report back briefly: pieces, tris, sizes,
  card path, known compromises. Do NOT copy anything to the user's machine or Unity — the lead agent integrates.
