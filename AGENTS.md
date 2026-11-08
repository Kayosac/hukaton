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
