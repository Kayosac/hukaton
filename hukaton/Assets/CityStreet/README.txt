CITY STREET BACKDROP

Prefab: Prefabs/CityStreetBackdrop.prefab
Scene: Assets/Game/Scenes/Hakaton.unity
Hierarchy: Shop — authored room, services and route / CityStreetBackdrop

Exactly two editable Quad objects, four triangles total:
- Street floor: 160 x 20 metres, local Y=0.006, Z=0.08..20.08.
- Vertical backdrop: 160 x 40 metres, local Z=6, Y=-8..32.
Prefab origin is the shop threshold; in the Shop root it is at (0.05, 0, 2.4).

Both MeshRenderers use saved URP/Unlit materials and PNG textures.
The street panorama depicts a 24 x 12 metre strip. Mirrored horizontal
wrapping extends the sides; vertical Clamp extends the sky above it.
Paving repeats every 2.4 metres. Point filtering, mipmaps, no compression.
Edit material tiling/offset to change drawing scale or which house faces
the doorway. Edit the two child Transforms to change physical coverage.

No runtime construction, scripts, colliders, lights, NPCs or new mechanics.
The existing Entry Landing renderers are disabled in the game scene;
their original collision and customer route remain. The city is parented
to the Shop root and follows the existing shop/expedition visibility.

Artwork generated with the built-in image_gen tool.
