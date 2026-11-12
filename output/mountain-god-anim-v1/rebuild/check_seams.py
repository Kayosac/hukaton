"""Check: faces whose corners follow different limbs (they would stretch into curtains when the limbs part)."""
import os
WORK = os.environ.get("REBUILD_WORK", "/tmp/god/rb")
import numpy as np, sys
from collections import Counter
d = np.load(WORK + "/split.npz"); bones=[str(b) for b in d["bones"]]
z = np.load(sys.argv[1]); RV, RF, WR, dist = z["RV"], z["RF"], z["WR"], z["dist"]
for limb in ("C01", "C02", "L01", "L02", "A03", "beard"):
    cols = [i for i, b in enumerate(bones) if b.startswith(limb)]
    s = WR[:, cols].sum(1)
    sf = s[RF]
    jump = sf.max(1) - sf.min(1)
    bad = jump > .5
    c = RV[RF[bad]].mean(1) if bad.any() else np.zeros((0,3))
    far = dist[RF[bad]].max(1) if bad.any() else []
    print(limb, "faces with a weight jump >.5:", int(bad.sum()), "healed among them:", int((np.array(far) > .03).sum()) if bad.any() else 0,
          "centre", c.mean(0).round(2) if len(c) else "", "spread", c.std(0).round(2) if len(c) else "")
