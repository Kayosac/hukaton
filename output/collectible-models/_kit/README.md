
## Four-view items (20+, concept sheets with front / back / left / right)
Items 20-24 have a 2x2 concept sheet. It is already split into 4 RGBA concepts with clean masks:
`/home/claude/concepts/<NN-slug>_{front,back,left,right}.png` (627x627; the sheet itself is `<NN-slug>-four-views-v1.png`).
View convention: front = camera at -Y (az 0), back az 180, LEFT = camera at -X (az -90, the item's front appears on the
RIGHT of that picture), RIGHT = camera at +X (az +90). Elevation ~12 deg, long lens. The views are NOT calibrated
(scale and angle differ a little; sides are sometimes slightly 3/4): build ONE geometry that is the best compromise.
- `bake4.py`: `default_cams`, `four_views`, `optimize_views` (fits each view's camera: dist/shift, add 'az' only if a
  view is clearly turned), `align_all` (IoU per view + overlay sheet), `bake_multi` (each texel from the view that sees
  it most head-on, real depth test, thin unseen bands copied from the nearest seen texel, the rest palette noise),
  `texel_points` (for painting fixes after the bake).
- `render4.py`: `apply_texture(obj, tex, ID, glass_parts=())` (optional 2nd transparent material for real see-through
  glass), `preview4` (4 concepts / model from the 4 fitted cameras / 4 free views), `card4`.
- Worked example: `/home/claude/items/24-petrified-queen-egg/build.py` (finished, approved by the lead). Note the
  'tip' trick (faces only ever seen at grazing angles become their own part filled with concept colours) and the
  despeckle step.
- Because all 4 sides are drawn, follow the concept on EVERY side (seam at the back, needles at the front, chip on the
  front-left, etc.). Target IoU >= 0.9 in every view.
