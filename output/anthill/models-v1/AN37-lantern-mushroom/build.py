import sys, math, os
sys.path.insert(0, '/home/claude/ant/models')
from lib_organic import *

HERE = os.path.dirname(os.path.abspath(__file__))
RENDER = '--norender' not in sys.argv


def build():
    ak.reset()
    p = ak.Mesh('Props')
    # thick crooked S-curved stem with a wide flared foot; the cap sits offset from the base
    path = [(0.0, 0.0, -0.02), (0.02, 0.01, 0.14), (0.0, 0.02, 0.4), (-0.12, 0.04, 0.78), (-0.22, 0.05, 1.1),
            (-0.18, 0.03, 1.42), (0.0, -0.01, 1.7), (0.17, -0.05, 1.9), (0.24, -0.07, 2.0)]
    radii = [0.5, 0.38, 0.29, 0.25, 0.23, 0.21, 0.2, 0.21, 0.22]
    tp, tr = densify(path, radii, 3, wob=0.012, seed=37)
    rj = random.Random(5)
    jit = [rj.uniform(-0.07, 0.07) for _ in range(8)]

    def fy(t, k):
        # lower half: dark foot -> tan (no emission); upper half spans the whole emissive gradient of stem_glow.
        # The tan->green colour steps are zig-zagged around the stem (PSX-style dither) so they read as a fade.
        if t <= 0.5:
            return 0.03 + t / 0.5 * 0.27
        f = 0.36 + (t - 0.5) / 0.5 * 0.63
        w = max(0.0, 1 - abs(t - 0.64) / 0.2)
        return min(0.99, max(0.3, f + (0.11 if k % 2 else -0.11) * w + jit[k] * 0.3 * w))
    atlas_tube(p, tp, tr, 8, 'stem_glow', along='v', wraps=1.0, jitter=0.02, seed=37, caps=(True, True), fy_fn=fy,
               scale=[(1.12, 0.92) if i < 3 else (1.0, 1.0) for i in range(len(tp))])
    # broad faceted cap, tilted the way the stem leans, lumpy, droopy rim; glowing gills underneath
    cap(p, (0.24, -0.07, 1.98), R=0.8, H=0.5, sides=13, tilt=(-0.16, 0.24), spin=0.0, seed=7, top='cap_mint',
        under='gill_glow', stem_r=0.24, droop=0.1, lump=0.1, rim=0.15, sxy=(1.0, 0.92), under_depth=0.16,
        bumps=[(2.2, 0.08), (5.2, 0.05)])
    clamp(p, 2, lo=0.0)
    po = p.to_object('AN37a_Props')
    objs = [po]
    ak.ground(objs)
    return objs


if __name__ == '__main__':
    objs = build()
    rec = outputs(objs, HERE, 'SM_AN37_LanternMushroom.fbx', 'AN37a', 'LanternMushroom', SHEETS + '37-lantern-mushroom.png',
                  manifest_extra=dict(solidity='solid', collider='capsule on the stem (r 0.22, h 2.0) + optional flat cylinder for the cap',
                                      emissive=True, emissive_parts='gill_glow (cap underside) + stem_glow (upper stem) on Props',
                                      pivot='bottom centre', light_hint='point light pale mint ~1.7 m high under the cap',
                                      notes='single Props object; cap_mint top with painted cream flecks'),
                  views=RENDER, extra_views=[(-20, 0, 'eye level')] if RENDER else None)
    write_manifest(HERE, [rec])
    print(rec)
