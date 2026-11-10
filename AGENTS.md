# Working copy

- By default work directly in the user's open main project (`/Users/kayosac/hukaton`) and its already open Unity Editor. Before creating your own worktree, clone, branch copy or separate Unity project, ASK the user and wait for a clear yes; never create one on your own initiative.

# Project art direction

For shop visitors and NPC character illustrations, use the user-approved style in `output/shop-visitors/style-current.json`.
The canonical visual reference is `output/shop-visitors/09-peasant-line-details.png`. Inspect it before generating or editing a character, and supply it as a style reference where supported.
Preserve the simple rounded proportions, thick irregular outer contour, thinner interior detail lines, and flat muted colors. The user approved the first simple peasant with added line detail, not the later angular or elongated versions.
Deliver new NPC character images as PNG with real alpha transparency. Set `transparent_background: true` for image generation and preserve the alpha channel. The reference image's light background is not part of the approved style; do not reproduce it or draw a checkerboard background.
The approved peasant sprites are `output/shop-visitors/10-peasant-transparent.png` (front) and `output/shop-visitors/11-peasant-back-mirrored.png` (back, raised coin hand on image left and sack on image right). The user reverted to the first back-view version; do not use the subsequent template-based or lowered-arm variants as approved references. Preserve these selected sprites unless an edit is explicitly requested.
Every NPC sprite must have both a front view and a corresponding rear view. For the rear view, mirror the screen-space direction and prop sides and preserve the character's identity, costume, proportions, drawing style, and transparent PNG format, using the approved peasant rear view as the example. For face-only expression variants, reuse the existing matching rear view when no visible rear details change. Expression edits must change only the face; preserve all other sprite pixels, pose, canvas, clothing, props, and transparency.
Peasant expressions must retain the approved minimal facial vocabulary: small solid-black eyes with simple lids, original rounded nose, sparse eyebrow strokes and a small black mouth. Do not add white eyeballs, teeth, large open mouths, new face shading or exaggerated cartoon features. Preserve the source sprite outside local expression regions and verify unchanged pixels outside those regions.
Keep this style by default unless the user explicitly changes it. This direction concerns NPC illustrations; separate card-art rules remain independent.

# Required NPC expression workflow

- Every new NPC deliverable includes five full-body transparent PNGs: default front, matching back, displeased front, happy front, and enraged/angry front. Face-only variations share the matching back sprite.
- Start from one canonical default front sprite. Inspect its actual face and preserve its individual identity, proportions, line weight, facial marks, palette, and level of simplification. Use the approved peasant expressions in `output/shop-visitors/peasant-expressions.json` as workflow examples, not as a face to copy onto all characters.
- Crop the face from that canonical sprite as an internal working reference. Generate expression changes only in this crop. Do not independently regenerate complete characters for each emotion.
- Change only the minimum necessary eyebrow, eyelid/eye and mouth marks. Keep hair, head silhouette, nose, ears and non-expression facial details fixed. Retain the source's simple black eyes and small mouth; do not introduce teeth, white eyeballs or a different rendering style unless the user requests them.
- Composite only the edited expression regions back onto the original full sprite using tight local masks. Adapt crop coordinates and masks to each character; do not reuse the peasant's coordinates blindly. Deterministic masked assembly is part of this user-required workflow. Preserve the original pixels everywhere outside the masks and preserve the entire original alpha channel.
- Verify the saved PNGs: same canvas dimensions, zero changed pixels outside the expression masks, and zero changed alpha pixels relative to the default. Inspect the full sprites and face regions for style drift, seams, clipped features, and recognizability before delivery. The peasant implementation and pixel verification are in `output/shop-visitors/expression-work/compose.swift` and `verification.json`.
- Deliver and display the FULL sprites inline, with PNG download links. Face crops are internal intermediates, never the final deliverables. Preserve the default and back files; record the expression files and shared back in a per-character manifest.

# Default project fonts

- Use the approved pixel-font pair for all new and edited game UI unless the user explicitly requests another font: **Ithaca** for primary text, titles and prominent labels; **Pixeloid Sans Regular** for descriptions, secondary labels and small readable text. Preserve Latin and Cyrillic support, including Ёё.
- Use TextMeshPro and the existing saved font assets in `hukaton/Assets/UI/Typography/Resources/Fonts/`: `Ithaca Pixel.asset` and `PixeloidSans Pixel.asset`. Reuse the materials in `hukaton/Assets/UI/Typography/Materials/` and the configured `Ithaca Text` / `Pixeloid Body` prefabs in `hukaton/Assets/UI/Typography/Prefabs/`. Do not substitute Unity's built-in font, Liberation Sans or another fallback font.
- Preserve crisp pixel edges: static RASTER bitmap atlases, Point filtering, Clamp wrapping, no mipmaps, and `TextMeshPro/Mobile/Bitmap` materials with the correct atlas assigned. Do not regenerate these fonts as SDF or antialiased atlases, add soft outlines, or enable automatic font sizing.
- Keep font sizes at integer multiples of the native grid: Ithaca **16 px** (default **32**); Pixeloid Sans **9 px** (default **18**). Keep text Transform scale at one. For screen UI use Screen Space Overlay, Pixel Perfect, and Constant Pixel Size with an integer Canvas scale factor; avoid fractional scaling, stretching and rotation that distort the pixel grid.
- Keep text and its font/material references authored in saved prefabs and scenes. When changing typography, check Latin/Cyrillic rendering and pixel edges in Play mode at a native Game View scale, and run `PixelTypographyTests`. Usage details are in `hukaton/Assets/UI/Typography/README.md`.

# Unity authoring and maintainability

- Deliver editable Unity assets. Anything that belongs in a scene must already exist in a saved, preconfigured `.unity` scene and be visible in the Hierarchy before Play.
- Package reusable objects and logical assemblies as prefabs: characters/player rigs, items, interaction points, display slots, counters, and UI panels. Use prefab instances in scenes so shared edits propagate.
- Store materials, audio, configuration, and other reusable content as assets with serialized Inspector references. Expose gameplay tuning in the Inspector or dedicated configuration assets.
- Do not recreate authored scenes, geometry, cameras, lights, UI hierarchies, materials, or component setups in `Awake`, `Start`, runtime bootstraps, or persistent builder scripts. Runtime code handles gameplay and may instantiate already configured prefabs when gameplay requires spawning.
- Use serialized references for scene dependencies. Do not hide setup behind global object searches, disabling unrelated scene objects, or automatic fallback generation.
- Keep responsibilities readable: gameplay rules, input, presentation, and scene references should be distinguishable. A developer must be able to change layout, replace a model, and tune an object through Unity without editing assembly code.
- One-off migration/authoring scripts are temporary tools only. Run them to produce and save real assets, verify the result, then remove the scripts, their `.meta` files, and temporary menu commands before delivery. Do not leave builders, generators, or auto-rebuild hooks as the project's source of truth.
- Give each independently runnable prototype or game layer its own preconfigured scene. Preserve unrelated scenes and assets; configure scene entry points explicitly.
- Verify the saved scene from Edit mode and Play mode: prefab connections and serialized references are intact, no missing scripts, gameplay still works, and no removed builder is required to recreate the result.
- These rules apply to greybox prototypes too. “Only cubes” describes the art fidelity, not an exception to prefab/scene authoring.
- Keep shared character movement, camera feedback, input capture, and stamina in a reusable module independent of any game layer. Specialize the authored player through prefab variants. Modal interfaces claim and release control through the shared ownership API.
- Keep the gameplay HUD sparse. Do not display permanent movement, mouse-look, or mouse-wheel instructions. Show object interactions only for the focused, reachable, unobstructed target; show trade controls only during a conversation and stamina only when it is relevant.
