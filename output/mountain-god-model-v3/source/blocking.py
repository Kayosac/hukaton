"""Blocking build: tubes + placeholder hands, renders canon overlay and turntable.
Run: python3 -I /home/claude/work/mg/blocking.py [outdir]"""
import sys, os, math
sys.path.insert(0, '/home/claude/work/mg')
import bpy
from mathutils import Vector as V
import core, bl, layout as L

OUT = sys.argv[1] if len(sys.argv) > 1 else '/home/claude/work/out/blocking'
os.makedirs(OUT, exist_ok=True)


def placeholder_hand(hid, wrist, knuckles, tips, r_f=0.18, seed=0):
    shells = []
    kc = sum(knuckles, V()) / len(knuckles)
    pal, _ = core.tube(hid + '_palm', [wrist, (wrist + kc) / 2, kc], [r_f * 2.2, (r_f * 3.2, r_f * 1.6), (r_f * 3.6, r_f * 1.4)],
                       sides=6, samples=1, group=hid, seed=seed)
    shells.append(pal)
    for i, (k, t) in enumerate(zip(knuckles, tips)):
        m = (k + t) / 2 + V((0, 0, 0.15 * (k - t).length))
        f, _ = core.tube(f'{hid}_f{i}', [k, m, t], [r_f, r_f * 0.9, r_f * 0.75], sides=4, samples=1, tip_end=True, group=hid, seed=seed + i)
        shells.append(f)
    return shells


def build():
    sc = bl.reset()
    shells = []
    t, _ = core.tube('CORE', L.TORSO['pts'], L.TORSO['radii'], sides=8, samples=2, up_hint=(1, 0, 0), group='CORE', seed=1)
    shells.append(t)
    h, _ = core.tube('HUB', L.HUB['pts'], L.HUB['radii'], sides=8, samples=2, group='CORE', seed=2)
    shells.append(h)
    hd = core.blob('HEAD', L.HEAD['centre'], (0.65, 0.55, 0.85), rings=5, sides=8, group='HEAD', seed=3)
    shells.append(hd)
    from mathutils import Euler
    import math as m
    sm = core.blob('SHOULDER', L.SHOULDER['centre'], L.SHOULDER['radii'], rings=5, sides=8, group='CORE', seed=4,
                   rot=Euler([m.radians(a) for a in L.SHOULDER['tilt_deg']]).to_matrix())
    shells.append(sm)
    sb, _ = core.tube('SHOULDER_BAR', L.SHOULDER_BAR['pts'], L.SHOULDER_BAR['radii'], sides=6, samples=2, group='CORE', seed=5)
    shells.append(sb)
    for i, lb in enumerate(L.LIMBS):
        s, _ = core.tube(lb['id'], lb['pts'], lb['radii'], sides=lb['sides'], samples=lb['samples'], group=lb['id'], seed=10 + i)
        shells.append(s)
    for hid, d in L.SUPPORT_HANDS.items():
        shells += placeholder_hand(hid, d['wrist'], d['knuckles'], d['tips'], r_f=0.24 if hid != 'S02' else 0.3)
    for hid, d in L.CRADLE_HANDS.items():
        shells += placeholder_hand(hid, d['wrist'], d['knuckles'], d['tips'], r_f=0.24)
        th, _ = core.tube(hid + '_th', d['thumb'], [0.3, 0.2], sides=4, samples=2, tip_end=True, group=hid)
        shells.append(th)
    for hid, d in L.SMALL_HANDS.items():
        k = [d['palm'] + (tp - d['palm']) * 0.35 for tp in d['tips']]
        shells += placeholder_hand(hid, d['wrist'], k, d['tips'], r_f=0.06)
    for lid, d in L.FEET.items():
        s, _ = core.tube(lid + '_foot', [d['ankle'], d['toe']], [0.27, (0.25, 0.14)], sides=6, samples=2, group=lid)
        shells.append(s)
    # hat cone
    hat, _ = core.tube('HAT', [L.HEAD['hat_brim'], (L.HEAD['hat_brim'] + L.HEAD['hat_tip']) / 2 + V((0, 0, 0.8)), L.HEAD['hat_tip']],
                       [(1.2, 1.0), 0.45, 0.12], sides=6, samples=2, tip_end=True, group='HAT', mat='hat')
    shells.append(hat)
    ob = bl.shells_to_object('MG01_blocking', shells)
    print('tris', bl.tri_count(ob))
    return sc, ob


if __name__ == '__main__':
    sc, ob = build()
    p = bl.canon_render(sc, os.path.join(OUT, 'canon.png'))
    sc.camera.data.lens = L.LENS_MM
    sc.camera.location = L.CAM
    p = bl.render(sc, os.path.join(OUT, 'canon.png'))
    bl.overlay(p, os.path.join(OUT, 'overlay.png'))
    tt = bl.turntable(sc, OUT, 'tt', angles=(0, 90, 180, 270))
    bl.contact_sheet(tt, os.path.join(OUT, 'turntable.png'), cols=4, scale=0.5)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'blocking.blend'))
