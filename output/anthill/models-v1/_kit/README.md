# Anthill («Муравейник») — Blender modelling kit

Goal: real 3D models (Blender → FBX for Unity) of the anthill dungeon's environment assets, from the concept
turnaround sheets, in the SAME style as the game's existing art (ForestArt / TutorialCave / SwampArt): PSX-like,
crooked, flat-shaded, point-sampled painted textures.

Blender = the `bpy` 5.3 Python module (`python3 build.py`, `import bpy`). No GUI. The machine has only 2 CPU cores
shared by several agents: render small (≤ 480 px tiles, 8–12 samples), never render in loops while shaping.

## Files
- Concept sheets (one asset per PNG, 2×2 views: top-left FRONT-LEFT ¾, top-right FRONT-RIGHT ¾,
  bottom-left REAR-RIGHT ¾, bottom-right REAR-LEFT ¾):
  `/home/claude/ant/concepts/environment-pack-v3/asset-concepts-v1/sheets/NN-slug.png`
- Briefs (size proposal X × Y × Z in metres, **Y = height** in the brief!, purpose, rooms):
  `.../asset-concepts-v1/briefs/NN-slug.md`; full prompt text: `.../asset-concepts-v1/assets.json`.
- Room concepts (how the assets are used together): `/home/claude/ant/concepts/environment-pack-v3/fungal-concepts/*.png`,
  plans `/home/claude/ant/concepts/environment-pack-v3/plans/*.png`, room passports `.../rooms/*.md`.
- Existing game art style reference (renders of real game models: crates, sacks, lanterns, fences, wagon,
  cave roots, mushrooms): `/home/claude/ant/refsheet.jpg`. Existing FBX in `/home/claude/ant/ref/`.
- Kit: `/home/claude/ant/kit/ak.py` (mesh writer with UVs, `blob()`, `auto_uv()`, materials, render, sheet, export)
  and shared textures `/home/claude/ant/kit/tex/` (overview.png shows them; Props atlas regions in regions.json).
  Do NOT edit the kit or the textures. If you truly need a new atlas region, say so in your report.
- Smoke test example: `/home/claude/ant/test/t.py`.

## Materials (shared by every asset — one object per material)
| Mat | Texture | Use |
|---|---|---|
| `Earth` | tiling 2 m, red-brown clay with horizontal ochre strata | walls, corners, vaults, collars, niches, mound, queen bed sides, ramp sides, bed rims |
| `Floor` | tiling 2 m, trodden earth | walkable tops: floor tile, ramp tops, niche shelf top, bed tops, cultivation bed soil? (no — use Props `substrate`) |
| `Root` | tiling 1.6 m, root bark, grain along V | root pillar, wall root, root arch, mushroom-free roots |
| `Wood` | tiling 1.6 m, boards/beams, grain along V | timber prop, deck bracket, railing posts, barricade, handcart, bins |
| `Cloth` | tiling 1.2 m, burgundy weave | queen-bed cloth |
| `Props` | 512 atlas (regions.json) | `end_grain`, `root_end`, `rope`, `iron`, `dark`, `cloth`, `egg`, `seed`, `shell_out`, `shell_in`, `paper`, `leaf_green`, `leaf_dry_0/1/2`, `resin`, `substrate` (glowing mycelium veins), `cap_mint`, `cap_yellow`, `gill_glow` (emissive), `stem`, `stem_glow` (emissive towards the top), `earth_props`, `wood_props`, `root_props` (small items on the atlas) |

Glow: `gill_glow`, `stem_glow` and the mycelium in `substrate` are emissive through `T_Ant_Props_Emission.png`.
Use them only where the concept glows (undersides of caps, upper stem of the tall mushroom, compost veins).

UVs: tiling materials — use the helpers (tube/box/planar set UVs in metres) or build faces and call
`ak.auto_uv(obj)` (box projection; Earth strata come out horizontal). Keep texel density = the kit tile sizes;
never squash a whole asset into 0–1. Atlas (Props) — `region=` in box/planar/card/cap, or compute UVs inside
`Mesh.region(name)`.

## Style (the user is strict about art — read carefully)
1. **Real modelled assets that look like the concept**: same silhouette, proportions, parts, counts, lean, damage.
   A recoloured or jittered blockout is NOT acceptable. Check every view of the sheet.
2. **PSX / game look**: simple CROOKED geometry with big flat faces, everything slightly crooked, sagging, leaning
   ("всё кривое, хаотичное и странное") — the irregularity is in the 3D form, not only in the texture.
   Flat shading, no micro-bevels, no subdivision smoothing.
3. **Detail budget**: the user wants much more detail than a minimal low-poly — spend triangles on silhouette and
   construction: real separate boards/beams/pegs/rope wraps, broken ends, chips, holes as real geometry, crooked
   root branches and flares, faceted cracks/ledges in the earth. Fine stuff (grain, strata, fibres) stays painted.
   Rough guide: small props 150–800 tris, medium 800–3000, big shell pieces / mound 1500–5000.
4. All matte. Colours come from the shared textures — no per-asset textures, no vertex colours.
5. No glowing anything except fungi (see Glow). No eyes/runes/ornaments.

## Conventions
- Blender metres, **Z up**, **front faces −Y**. The brief's X × Y × Z has **Y = height** → in Blender that is
  X × (depth) × Z(height): brief "4 × 4 × 0,8" = 4 m wide (X), 4 m high (Z), 0.8 m thick (Y).
- Pivot = origin at the bottom centre (`ak.ground(objs)`), unless the item says otherwise:
  - modular shell pieces (walls, corner, collar, vault, floor, ramps, niche): pivot at bottom centre AND the join
    ends must be clean and on the metre grid (e.g. wall joins at x = ±2.0, collar opening centred on x = 0),
    so the level builder can snap them; write the join description into the manifest (`joins`).
  - hanging things (chain, wall fungi, resin patch, wall root): pivot where it attaches; say which in the manifest.
- Walkable tops (floor, ramps, deck, bed, niche shelf) must be walkable: no snags, slopes ≤ 18° on ramps, clear
  passage widths ≥ 2.4 m for collars/arches the player walks through.
- One FBX per asset (variants only if the brief asks or it is cheap and clearly useful):
  `SM_AN<NN>_<PascalName>.fbx` (e.g. `SM_AN01_EarthWall.fbx`). Inside: one object per material named
  `AN<NN>a_<Mat>` (e.g. `AN01a_Earth`, `AN01a_Floor`). Variants: letters b, c…
- Export with `ak.export_fbx(objs, path)` (1 unit = 1 m, transforms applied).

## Workflow per asset (ANNN)
1. Read the brief, LOOK at the concept sheet (Read tool on the PNG; crop/zoom with PIL into `work/` to see each
   view). Write down masses, parts, counts, sizes in metres.
2. `/home/claude/ant/models/AN<NN>-<slug>/build.py`: builds the objects, exports FBX, saves one `.blend`.
3. Renders: the four concept views (`ak.render(objs, png, az=-45, el=20)`, `az=45`, `az=135`, `az=-135`), plus one
   in-situ view if useful (e.g. the collar with a 1.8 m human box next to it for scale). Then
   `out/card.jpg` with `ak.sheet(tiles, ..., cols=4, concept=sheet_png)` = concept on top, renders below, labels.
4. LOOK at card.jpg critically like the user would: silhouette, proportions, parts count, lean, broken bits, texture
   direction/stretching, black/inside-out faces (normals!), floating parts, holes. Fix and re-run (usually 3–6 rounds).
5. `out/manifest.json`: `ak.variant_record(...)` per variant + `solidity` (solid / walk-on-top / passable / surface),
   `collider` suggestion (box/convex/mesh, which parts), `joins` (for modular pieces), `attach` (for hanging),
   `emissive` (bool), notes.

## Report back (final message, short)
Per asset: folder, FBX names, tris, size, what is modelled vs painted, known compromises, path of card.jpg.
Do NOT copy anything to the user's Mac/Unity — the lead agent delivers.
