# Hangover coin — 3D model from the HUD coin sprites

Unity: `hukaton/Assets/Content/Currency/HangoverCoin/` — `HangoverCoin.prefab` (mesh + material), `HangoverCoin.obj`, `HangoverCoin_Albedo.png` (4096², uncompressed, bilinear, mipmaps), `HangoverCoin.mat` (URP Unlit).

- Diameter 1 unit (flat to flat), thickness 0.21. The face looks along −Z (towards a default camera), the plain back along +Z, pivot on the axis.
- 12-sided prism with per-vertex radii, an outer chamfer, a raised rim and a recessed field; 146 vertices, 108 faces.
- The material is unlit on purpose: the light is painted into the sprites, so the model keeps exactly their look. In the poses of `Coin_Y_000` and `Coin_Y_180` the render overlaps the sprite silhouette by 97 % with a mean colour difference of about 12 / 255.

## How it was made

The six sprites in `HangoverCoinAtlas.png` are separately generated views (see `output/ui-elements/currency/coin-spin-v1/manifest.json`), not renders of one object, so their widths do not agree geometrically. The shape is fitted to the two near-frontal views:

1. `fit_silhouette_v1.py`, then `fit_silhouette_chamfer.py`: a chamfered 12-sided prism is projected and its radii, thickness, view angles and centres are optimised for silhouette and side-band overlap with `Coin_Y_000` and `Coin_Y_180` → `fit.json` (both views are turned about 33–35°).
2. `build_coin.py <HangoverCoinAtlas.png> <out dir>`: writes the OBJ and the texture. Face and back are the sprites' own pixels re-projected through the fitted views (so they are frontal in the texture), side walls are taken from whichever view sees them, the hidden left walls mirror the right ones, edges are padded.

`coin-sprites-vs-model.png` — all six sprites above the model turned to match them. `coin-overlay-000-180.png` — sprite, model render and their difference for the two fitted views.

## Rebuild after changing the sprites

```
python3 fit_silhouette_chamfer.py      # only if the coin's outline changed (expects fit.json from the previous fit)
python3 build_coin.py ../../hukaton/Assets/Content/UI/ShopHUD/HangoverCoinAtlas.png ../../hukaton/Assets/Content/Currency/HangoverCoin
```

The HUD coin atlas imports at full size in this branch (Max Size 4096 instead of 128, uncompressed RGBA32). In the HUD it is shown at about 42 px with point filtering and no mipmaps, so it may shimmer there; turn on mipmaps or bilinear filtering for the HUD if that shows.
