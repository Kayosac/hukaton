# MG01 skin rebuild (v5)

The source model is one fused low-poly mesh: hands grow into thighs and the chest, the beard into the chest. v4 tore
the skin where limbs move apart and capped each opening; the caps showed as fans, seams and fins. v5 rebuilds the
whole skin instead, so the healed places are ordinary skin with ordinary topology.

## Steps (`run.sh`)

1. `dump_split.py` — the split skin from `godrig.separate_limbs(fill=False)`: torn where limbs come apart, no fills,
   cleaned weights, per-vertex side (limb, bone, root joint).
2. `frames_S.py` — bone matrices at moments of the clips. `TalkL01`: the Talk pose with leg L01 a third of the way
   down (both legs all the way down press into each other at the hips).
3. `relax.py` — opens the skin: the split skin (subdivided twice) is posed as `TalkL01`, the beard and arm A03 slid off
   the chest and the pelvis; then skins that face each other closer than 0.10 and are not one skin along the surface
   (geodesic test), or that cross while their parts move apart, are pushed off each other by a smooth field.
4. `poisson.py` — screened Poisson (depth 10) through the opened skin: one closed smooth surface, every opening
   healed; decimated to 240k triangles.
5. `pullback.py` — back to rest: each vertex takes the weights and the push of the nearest old skin (harmonic where
   healed) and goes through the inverse of the pose.
6. `fairheal.py` — healed sheets that came back crumpled (built where the pose stretched the skin) are faired smooth
   between the skin around them; creases near them relaxed.
7. `weightfix.py` — the spring bones (beard, hat) keep only skin that was theirs: a healed place on the chest next to
   the beard does not swing with the beard's spring (in Unity it tore open into a dark pocket).
8. `bake.py` — UVs by xatlas (about 2000 charts, 80 % of the map used); colour baked from the old skin (Cycles,
   selected to active, in the opened pose); healed places take the colour of the nearest old skin; every pixel
   outside the charts takes its nearest chart pixel (no dark borders in filtering or mip levels).
9. `finalize.py` — `out/MG01_Body_rebuilt.npz` (rest skin, UVs, weights) and `out/T_MG01_BodyAnim_BaseColor.png`.

`godrig.setup()` uses the rebuilt skin when that file exists (`use_rebuilt`: at most four bones a vertex), otherwise
the v4 split and caps.

## Checks

- `check_seams.py` — faces whose corners follow different limbs (they would stretch into curtains when the limbs
  part). After the rebuild: C01 55, C02 54, L01 77 faces, about the weight seams the old skin already had at the root
  joints; L02 and A03 keep more at their hips (they barely part there); the beard 242, near its root (where it
  hardly moves).

Needs `pymeshlab`, `xatlas`, `scipy` (pip). Work files go to `$REBUILD_WORK` (default `/tmp/god/rb`).
