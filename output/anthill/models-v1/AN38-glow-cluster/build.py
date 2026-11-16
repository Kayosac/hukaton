import sys, math, os
sys.path.insert(0, '/home/claude/ant/models')
from lib_organic import *

HERE = os.path.dirname(os.path.abspath(__file__))
RENDER = '--norender' not in sys.argv

# five unequal mushrooms, fixed order: (base xy, top xyz, stem r base/top, cap R, cap H, tilt, seed)
SHROOMS = [
    ((0.04, 0.08), (0.1, 0.12, 0.62), (0.1, 0.075), 0.3, 0.2, (-0.08, 0.06), 1),     # big centre-back
    ((-0.16, 0.0), (-0.42, -0.06, 0.4), (0.075, 0.06), 0.23, 0.15, (0.1, -0.38), 2),   # left, leaning left
    ((0.18, 0.02), (0.4, 0.04, 0.36), (0.07, 0.055), 0.19, 0.12, (0.1, 0.4), 3),     # right, leaning right
    ((0.08, -0.16), (0.14, -0.24, 0.22), (0.055, 0.045), 0.13, 0.085, (0.2, 0.15), 4),  # small front-right
    ((-0.2, -0.14), (-0.3, -0.22, 0.15), (0.045, 0.038), 0.095, 0.065, (0.25, -0.2), 5),  # tiny front-left
]


def build():
    ak.reset()
    p = ak.Mesh('Props'); r = ak.Mesh('Root')
    # tiny shared root base: overlapping faceted lumps
    ak.blob(r, (0.0, 0.0, 0.0), (0.34, 0.26, 0.13), rings=3, sides=8, jitter=0.18, seed=1)
    ak.blob(r, (-0.2, -0.1, 0.0), (0.2, 0.17, 0.09), rings=2, sides=7, jitter=0.2, seed=2)
    ak.blob(r, (0.22, 0.0, 0.0), (0.2, 0.16, 0.1), rings=2, sides=7, jitter=0.2, seed=3)
    ak.blob(r, (0.05, 0.16, 0.0), (0.18, 0.13, 0.08), rings=2, sides=7, jitter=0.2, seed=4)
    for (bxy, top, (rb, rt), R, H, tilt, sd) in SHROOMS:
        B = Vector((bxy[0], bxy[1], 0.0))
        T = Vector(top)
        mid = B.lerp(T, 0.5) + Vector((0, 0, 0.0))
        # slight bow away from the cluster centre
        bow = Vector((B.x, B.y, 0)).normalized() * 0.03 if B.xy.length > 0.05 else Vector((0.02, 0, 0))
        path = [B + Vector((0, 0, 0.0)), B.lerp(T, 0.33) + bow, B.lerp(T, 0.66) + bow * 1.2, T]
        atlas_tube(p, path, [rb * 1.35, rb, (rb + rt) / 2, rt], 7, 'stem', along='u', fy_range=(0.05, 0.95),
                   jitter=0.006, seed=sd, caps=(False, True))
        cap(p, T + Vector((0, 0, -0.01)), R=R, H=H, sides=10, tilt=tilt, spin=sd * 1.1, seed=10 + sd, top='cap_yellow',
            under='gill_glow_yellow', stem_r=rt * 1.1, droop=0.1, lump=0.07, rim=0.1, sxy=(1.0, 0.95), under_depth=R * 0.22, glow_rim=True)
    clamp(p, 2, lo=0.0); clamp(r, 2, lo=0.0); drop_bottom(r)
    po = p.to_object('AN38a_Props'); ro = r.to_object('AN38a_Root')
    ak.auto_uv(ro)
    objs = [po, ro]
    ak.ground(objs)
    return objs


if __name__ == '__main__':
    objs = build()
    rec = outputs(objs, HERE, 'SM_AN38_GlowCluster.fbx', 'AN38a', 'GlowCluster', SHEETS + '38-glow-cluster.png',
                  manifest_extra=dict(solidity='solid (small) / decorative', collider='none or one small box ~1.0 x 0.8 x 0.6',
                                      emissive=True, emissive_parts='gill_glow under the five caps',
                                      pivot='bottom centre', light_hint='one warm yellow-green point light ~0.5 m',
                                      notes='five unequal mushrooms on a shared root lump (Root), caps cap_yellow'),
                  views=RENDER, extra_views=[(-20, 3, 'eye level')] if RENDER else None)
    write_manifest(HERE, [rec])
    print(rec)
