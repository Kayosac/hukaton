"""Step 7: pack the rest-pose skin, UVs and weights for godrig.use_rebuilt, copy the colour map."""
import os
WORK = os.environ.get("REBUILD_WORK", "/tmp/god/rb")
import sys, numpy as np, shutil
res, png, out = sys.argv[1], sys.argv[2], sys.argv[3]
z = np.load(res); d = np.load(WORK + "/split.npz")
uv = np.load(png.replace(".png", "_uv.npy"))
assert len(uv) == 3 * len(z["RF"]), (len(uv), len(z["RF"]))
np.savez_compressed(out + "/MG01_Body_rebuilt.npz", RV=z["RV"].astype(np.float32), RF=z["RF"].astype(np.int32), UV=uv.astype(np.float32),
                    WR=z["WR"].astype(np.float32), bones=d["bones"])
shutil.copy(png, out + "/T_MG01_BodyAnim_BaseColor.png")
print("ok", len(z["RV"]), len(z["RF"]))
