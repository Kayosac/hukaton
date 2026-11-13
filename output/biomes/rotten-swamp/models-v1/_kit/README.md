# Rotten Swamp («Гнилая топь») — Blender modelling kit

Goal: real 3D models (Blender → FBX for Unity) of the swamp biome's props, from the approved concept sheets,
in the SAME style as the game's existing forest art (ForestArt) but recognisably a different biome.

Blender = the `bpy` 5.3 Python module (`python3 build.py`, `import bpy`). No GUI. The machine has only 2 CPU cores
shared by several agents: render small (≤ 520 px tiles, 8–16 samples), do not render in loops while shaping.

## Files
- Concept sheets: `/home/claude/ref/swamp/sheets/NN-*.png` (each: variants in rows, 2–3 views per variant).
  Catalog with sizes, variant names, construction notes: `/home/claude/ref/swamp/CATALOG.md` (section SWNN).
  Modelling rules: `/home/claude/ref/swamp/MODELING.md`. Approved overall look: `/home/claude/ref/swamp/references/approved-overview.png`.
- Forest style reference (renders of the existing game models): `/home/claude/ref/fp0.jpg`, `fp1.jpg`, `fp2.jpg`,
  `/home/claude/ref/ForestDemo_views.jpg`; forest textures `/home/claude/ref/forest_tex.png`; forest FBX in `/home/claude/ref/forest/`.
- Kit: `/home/claude/swamp/kit/sk.py` (mesh writer with UVs + wind, materials, moss drape, render, sheet, export).
  Shared textures `/home/claude/swamp/kit/tex/` (overview.png shows all of them; atlas regions in regions.json).
  Do NOT edit the kit or textures (except the one agent explicitly told it owns the Plants atlas painting).
- Smoke test example: `/home/claude/swamp/test/t.py`.

## Style (read carefully — the user is strict about art)
1. **Real modelled assets that look like the concept**: same silhouette, proportions, parts, lean, damage. A recoloured
   or jittered blockout is NOT acceptable. Check every variant against its concept views.
2. **PSX / ForestArt look**: simple CROOKED geometry with big flat faces (5–8 sided logs/trunks, few rings, flat
   shading), everything slightly crooked, sagging, leaning ("всё кривое, хаотичное и странное") — the irregularity is in
   the 3D form itself, not only in the texture. No micro-bevels, no subdivision smoothing.
3. **Detail budget**: the user wants much more detail than the catalog's triangle budgets — aim for roughly
   **5–10× the catalog budget**, spent on silhouette and construction: more crooked segments, real separate
   logs/boards/beams/posts, broken board ends, gaps and holes as real geometry, roots and branches, moss skins and
   moss drips, grass tufts at the base. Fine stuff (grain, cracks, lichen spots, nails) stays painted (textures).
4. **Textures are SHARED** (like ForestArt: one material per kind, reused by every asset). Materials (kit `MATS`):
   `Bark`, `BarkMossy` (tiling, grain along V), `Birch`, `Planks` (boards, grain along V), `LogWall` (stacked horizontal
   logs, 5 per repeat), `Roof` (moss-grown shingles, eaves at V=0), `Mud` (peat/silt with moss), `Moss` (moss skin),
   `Props` (opaque atlas: end grain, window, dark opening, chimney, rope, rag, fungus, cattail cob, lily petal, iron,
   idol face, tar, vent crust/hole, grey wood, small wood), `Plants` (alpha-cutout atlas: sedge, grass, cattail leaves,
   duckweed, lily pads, willow strands, alder clumps, moss drips, net, rag card). Respect texel density: use the kit
   tile sizes (tiling UVs in metres), keep texture direction sensible (bark/plank grain along the piece).
5. Colours/mood: swamp = colder purple-brown wet wood, olive/yellow-green moss, pale dead birch; NOT the forest's warm
   red-orange. All matte.
6. Moss: use `sk.moss_drape()` on upward faces of logs, roofs, stumps (as in the concept), with moss drip cards
   (`Plants` regions `moss_drip_0/1`) hanging from roof eaves / log edges where the concept shows drips.
7. Plants / leaves / strands / nets / rags = alpha cards (`Plants`), never modelled blades or strands.
   Give them wind: `card(... wind_sway=, wind_flutter=)` (metres; 0 at the root or hang point). Trunks/branches of
   trees may get a little sway too (tube(wind=...)). Solid structures: no wind.

## Conventions
- Blender metres, Z up, **front of the asset faces −Y**. Pivot = asset origin (0,0,0) at the bottom centre: use
  `sk.ground(objs)` at the end (lowest point at z=0, XY centred). Keep the full lower parts (they go under water/ground
  when placed). Record the intended **water line** height (z above pivot) for things that stand in water
  (`water_line_z` in the manifest), as shown on the concept (e.g. a sunken hut: the water line mark on the sheet).
- One FBX per variant: `SM_SW<NN>_<PascalName>.fbx` (e.g. `SM_SW11_SunkenLogLong.fbx`). Inside: one object per
  material named `SW<NN><letter>_<Mat>` (e.g. `SW11a_BarkMossy`, `SW11a_Props`, `SW11a_Moss`, `SW11a_Plants`).
  Separately moving parts (mill wheel, well sweep lever, bucket) are separate FBX files or separate objects with
  their own pivot as requested in the catalog — say which in the manifest.
- Export with `sk.export_fbx(objs, path)` (1 unit = 1 m, Forward −Z, Up Y, transforms applied).
- Sizes: use the catalog's proposed X×Y×Z (within the table's range). Walkable tops must be walkable (no snags).

## Workflow per item (SWNN)
1. Read the catalog section and LOOK at the concept sheet (Read tool on the PNG; crop/zoom with PIL into
   `work/` to see each view). Write down masses, parts, counts, sizes in metres.
2. `items/SWNN-slug/build.py`: one function per variant building Mesh objects → objects; build all variants in one
   run; export FBX per variant; save one `.blend` with all variants side by side.
3. Renders: for every variant render the views the concept shows (`sk.render(az=, el=)`, same angles as the
   concept), plus one in-water/in-situ view if it stands in water (`water_z=`). Make `out/card.jpg` with
   `sk.sheet(..., concept=sheet_png)` = concept on top, model renders below, labels.
4. LOOK at card.jpg critically like the user would: silhouette, proportions, parts count, lean, broken bits, texture
   direction/stretching, black/inside-out faces, floating parts, plants too thin. Fix and re-run (usually 3–6 rounds).
5. `out/manifest.json`: list of `sk.variant_record(...)` entries + per variant: `water_line_z`, `solidity`
   (from the brief: solid / walk-on-top / soft-passable / surface), `burns` (bool), collider suggestion
   (which objects/parts are solid, e.g. "trunk+branches solid, strands no collider"), moving parts & pivots, notes.

## Report back (final message, short)
For each item: folder, variants (FBX names), tris, sizes, what is modelled vs painted, known compromises,
and the path of card.jpg. Do NOT copy anything to the user's Mac/Unity — the lead agent delivers.
