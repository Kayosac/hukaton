#!/bin/bash
# Rebuild MG01's animated skin whole: split -> pose apart -> Poisson -> back to rest -> UVs and baked colour -> out/.
# usage: ./run.sh   (work files in $REBUILD_WORK, default /tmp/god/rb; needs pymeshlab, xatlas, scipy)
set -e
cd "$(dirname "$0")"
export REBUILD_WORK=${REBUILD_WORK:-/tmp/god/rb}
mkdir -p "$REBUILD_WORK"
W=$REBUILD_WORK
rm -f ../out/MG01_Body_rebuilt.npz       # the split below must come from the source model, not the last rebuild
python3 dump_split.py
python3 frames_S.py
python3 relax.py
python3 poisson.py "$W/relaxed.npz" "$W/poisson.npz" 10 1500000 240000
python3 pullback.py "$W/relaxed.npz" "$W/poisson.npz" "$W/res.npz"
python3 fairheal.py "$W/res.npz" "$W/res_fh.npz"
python3 weightfix.py "$W/res_fh.npz" "$W/relaxed.npz" "$W/res_fhw.npz"
python3 check_seams.py "$W/res_fhw.npz"
python3 bake.py -- "$W/res.npz" "$W/T_MG01_BodyAnim_BaseColor.png" 2048 .04 .1 "$W/relaxed.npz"
python3 finalize.py "$W/res_fhw.npz" "$W/T_MG01_BodyAnim_BaseColor.png" ../out
