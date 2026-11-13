# Hakaton collectible items — Blender kit

Goal: a real 3D model (Blender, exported FBX) of ONE shop item from its concept image, that looks like the concept
and stands freely on a slot of the shop display case (витрина).

Blender = the `bpy` 5.3 Python module (`python3 script.py`, `import bpy`). No GUI. Only 2 CPU cores are shared with
other agents: keep test renders few, use `--align-only` runs while shaping geometry.

## Files
- Concepts: `/home/claude/concepts/collectible-concepts/NN-*.png` (+ `NN-*-notes.md` on the user's Mac are not here;
  the README.md beside the PNGs has the one-line brief for each item).
- Kit: `geo.py` (mesh Builder with parts), `cam.py` (camera), `bake.py` (unwrap, silhouette alignment, projection
  bake from the concept), `render.py` (texture material, preview sheet, display-case fit render, card, FBX export).
- Worked example: `example_build_jug.py` (item 06). Copy it to `/home/claude/items/NN-slug/build.py` and adapt.
- Display case (real game asset) and textures: `/mnt/user-data/uploads/hukaton/hukaton/Assets/ShopArt/...`
- Style reference (approved shop): `/mnt/user-data/uploads/hukaton/output/shop-art-pack-v1/01-concepts/01-approved-counter-view.png`

## Hard rules (from the user and the project's KayoSpace style guide)
1. **Fit.** Pivot = bottom centre at origin, min Z = 0, item stands upright on its base. Height <= 0.22 m and the
   horizontal footprint inside 0.26 x 0.26 m (`geo.check_fit` must say `fits_shelf: True`). Slot pad is 0.32 x 0.45,
   top-row clearance 0.24. Game convention: all items are about one physical size regardless of the real object, so
   scale every item so its largest dimension is about 0.18-0.22 m (a 0.2 m apple is fine).
2. **Look like the concept.** Same silhouette, proportions, parts, asymmetry, colours, painted details. The texture is
   projected from the concept itself (bake.py), so geometry must match the concept silhouette well: aim for
   silhouette IoU >= 0.88 at the concept camera and check the overlay `out/align.png` (red = model only,
   blue = concept only) — fix the GEOMETRY until red/blue fringes are thin, do not just let the camera optimizer
   shrink things. A recoloured/jittered blockout is NOT acceptable.
3. **Style = PSX weirdcore (CARIMARA).** Very simple crooked geometry: few sides (5-12), few rings, big flat faces,
   meaningful asymmetry and slight lean, never uniform wobbly rubber. Model silhouette, openings, real depth and
   protruding parts (eyes on the mushroom cap are real bulging spheres, handles are real loops with holes, lenses are
   recessed, the hypercube is real bars). PAINT the small stuff (scratches, seams, stitches, numbers, engraving,
   veins, wood grain, screen icons) — that comes from the concept projection. Matte, no gloss. No modeled micro-bevels.
4. **Budget.** Usually 400-2500 triangles after triangulation (complex items like bonsai/teddy/clock may go to ~4000).
   No LODs. One mesh object, one material, one texture (power of two, `texture_size_for` keeps one texel density for
   all items — do not override the size unless the user's text would be unreadable).
5. **Text on items is English only** (the juice carton says JUICE). Never add Russian text.
6. Hidden faces (back, bottom, inside) must not be garbage: use `views` in the bake (mirror for symmetric items,
   `T_rot_z` copies restricted to `parts` that are rotationally symmetric, e.g. a lathe body), and give parts that the
   concept never shows a sensible palette via `part_colors`. Check `out/coverage.png` (red texels = filled with the
   part's palette noise) and the back views of `preview.png`.
7. Don't touch the user's Unity project or Mac files — the lead agent delivers your output.

## Workflow
1. Look at the concept (Read tool on the PNG) and write down masses, parts, side counts, sizes in metres.
2. Build with `geo.Builder`: `lathe` (vessels, mushroom, bottle, goblet), `box` (crooked boxes, carton, case,
   clock), `tube` (handles, bails, wires, straws, limbs, cables, closed=True for rings/torus), `blob` (low-poly
   ellipsoids: eyes, teddy parts, apple), `add_bmesh_geometry` (anything custom: faceted gem apple, cube frames).
   You may also edit `b.bm` verts directly before `finish()` (e.g. pulling a spout). Each part name becomes a texture
   region owner — split parts by material/colour (body, handle, glass, lid, eyes...).
3. `python3 build.py --opt --align-only` -> camera.json + out/align.png. Start the camera near the concept look
   (most concepts: front three-quarter, el 12-25 deg, fov ~18). Optimize only az/dist/shift; set el and roll by eye.
   Iterate geometry until the overlay is tight.
4. Full run `python3 build.py` -> out/: `T_<ID>.png`, `<ID>.fbx`, `<ID>.blend`, `preview.png` (concept vs model at
   the concept camera + 4 turnaround views), `shelf.png` (on the real display case from game camera B), `card.jpg`
   (what the user will see), `coverage.png`, `stats.json`.
5. LOOK at card.jpg critically (Read it). Compare to the concept like the user would: wrong proportions, smeared
   texture, stretched pixels, garbage on hidden sides, missing parts, broken normals (black/inside-out faces),
   floating parts -> fix and rerun. Typically 3-6 iterations.

## Naming
ID = `CI<NN>_<PascalName>` (e.g. `CI06_GoldenJug`). Output folder `/home/claude/items/NN-slug/out/`.

## Report back (final message, short)
ID, folder, tris, size (m), texture size, IoU, what you modeled vs painted, known compromises.

## Hidden sides (only one concept view exists — decided by the user)
There is exactly ONE concept image per item. Do not wait for more views. Make the unseen sides believable:
- symmetric items: mirror views; lathe/radial parts: `T_rot_z` copies restricted to those parts;
- grazing surfaces (rim tops, insides, thin edges) must not get stretched streaks: keep `min_facing` >= 0.3 and let
  them use other views or the palette fill; for a part the concept barely shows, pass `part_colors` picked from the
  concept by eye (2-5 colours, dark->light) so the fill reads as the same painted material;
- if a hidden side needs a feature (stitches on the teddy's back, back panel of a clock, a hinge), you may paint it
  into the texture after the bake with numpy/cv2 in the same chunky pixel manner (draw at texel scale, colours from
  the item's own palette).

## Lessons from finished items (read)
- Concave shapes (dimples, cups, mouths): the kit's painter's rasterizer can let back faces win; see
  /home/claude/items/05-diamond-apple/bake_local.py (proper depth test, concept preferred where usable) and copy it
  into your folder if you need it. Do not edit /home/claude/kit itself.
- Projection of fine regular patterns (straw strands, rope) smears at item texel density: item 04 repaints such
  materials procedurally along each part's axis with colours k-means-sampled from the concept. That's fine.
- Removing the concept's large-scale light/shadow before projecting (item 03 concept_delit) makes front and filled
  back match without seams.
- Keep only a camera shift / small az in the optimizer so geometry, not the camera, does the fitting.
