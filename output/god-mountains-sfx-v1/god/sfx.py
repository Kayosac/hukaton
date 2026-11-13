"""Sounds for the god's clips (MG01: Notice 4.4 s, Reach 11 s, Lift 28 s), timed to the keys in
output/mountain-god-anim-v1/clips.py. Low, slow, enormous: sub drops, rock grinding, groaning joints, air pushed by an
arm the size of a ridge, gravel raining off his body far below."""
import sys, os
import numpy as np
from lib import *

OUT = sys.argv[1] if len(sys.argv) > 1 else "out"
os.makedirs(OUT, exist_ok=True)


def gravel(dur, pts, lo=900, hi=3500):
    """Small stones rattling off far away."""
    e = env_points(dur, pts)
    g = grind(dur, e, lo, hi, grain=(.006, .03))
    return lp(g, 4200) * .5


def rumble(dur, pts, f=120):
    return lp(brown(dur), f, 2) * env_points(dur, pts)


def sub(dur, f_pts, a_pts):
    t = t_axis(dur)
    f = np.interp(t, [p[0] for p in f_pts], [p[1] for p in f_pts])
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * env_points(dur, a_pts)


def notice():
    D = 10.0
    b = np.zeros((secs(D), 2))
    # the dip to push off, then up: grinding rock along the cradle, a groan of effort
    gr = grind(D, env_points(D, [(0, 0), (.4, .2), (1.2, 1), (2.6, .8), (3.8, .15), (4.6, 0), (D, 0)]), 70, 520)
    place(b, widen(gr, 13), 0, .55)
    gf = np.interp(t_axis(D), [0, .35, 3.0, 3.6, 4.4, D], [30, 30, 46, 44, 40, 40])
    g = groan(D, gf, bright=420) * env_points(D, [(0, 0), (.4, .3), (1.0, .9), (2.8, 1), (4.2, .2), (5, 0), (D, 0)])
    place(b, widen(g, 9), 0, .5)
    place(b, rumble(D, [(0, 0), (.3, .5), (1.5, 1), (3.5, .7), (6, .1), (D, 0)]), 0, .9)
    place(b, sub(D, [(0, 26), (1, 30), (3.5, 36), (D, 30)], [(0, 0), (.5, .6), (2.5, 1), (4.5, .3), (6, 0), (D, 0)]), 0, .6)
    place(b, whoosh(D, env_points(D, [(0, 0), (.8, .5), (2.2, 1), (3.6, .2), (4.5, 0), (D, 0)]),
                    lambda s: 90 + 160 * min(1, s / 2.5)) [:, None] * np.array([[.6, .5]]), 0, .4)
    # the heavy settle at the top of the rise
    place(b, boom(5, 44, 21, drop=.9, drive=2.4, click=.25), 3.25, .85)
    place(b, widen(gravel(7, [(0, 0), (.4, 1), (2, .6), (5, .1), (7, 0)])), 2.9, .35)
    return b


def reach():
    D = 17.0
    b = np.zeros((secs(D), 2))
    t = t_axis(D)
    # 0-0.4: a short squeeze of the grip
    place(b, widen(groan(1.5, np.linspace(48, 58, secs(1.5)), 600) * adsr(secs(1.5), .15, .9), 7), 0, .35)
    # 0.3-1.4: both hands slide off the thighs, stone on stone, left and right
    for side, f in ((-.7, 1.0), (.7, .92)):
        e = env_points(3, [(0, 0), (.3, .8), (.9, 1), (1.3, .4), (1.8, 0), (3, 0)])
        g = grind(3, e, 80 * f, 600 * f)
        place(b, pan(g, side), .25, .45)
    place(b, rumble(D, [(0, 0), (.4, .6), (1.4, .8), (2.4, 1), (4, .5), (9, .5), (10, 1), (12, .3), (D, 0)]), 0, .8)
    # 1.0-2.6: the legs drop and straighten — the air falls with them
    place(b, widen(whoosh(3, env_points(3, [(0, 0), (.6, .7), (1.2, 1), (1.6, .6), (2.4, 0), (3, 0)]),
                          lambda s: 420 - 300 * min(1, s / 1.6), q=.7), 15), 1.0, .55)
    place(b, sub(4, [(0, 70), (1.5, 28), (4, 26)], [(0, 0), (.4, .8), (1.3, 1), (3, .2), (4, 0)]), 1.0, .5)
    # the legs snap straight and swing back: two jolts through the whole body
    place(b, boom(7, 52, 20, drop=1.0, drive=2.8, click=.35), 2.15, 1.0)
    place(b, boom(5, 40, 20, drop=.8, drive=2.0, click=.15), 3.05, .55)
    # 1.15-3: the torso sags under the weight
    gf = np.interp(np.arange(secs(4)) / SR, [0, 1, 2, 4], [40, 33, 30, 30])
    place(b, widen(groan(4, gf, 380) * env_points(4, [(0, 0), (.4, .9), (1.6, 1), (2.6, .4), (4, 0)]), 11), 1.15, .5)
    place(b, widen(gravel(8, [(0, 0), (.5, 1), (3, .4), (8, 0)])), 2.2, .3)
    # 2.6-9.8: the arm swings out and up in a wide arc, coming at the player at the end
    A = 9.0
    e = env_points(A, [(0, 0), (1.2, .45), (3, .7), (4, .5), (5.5, .8), (6.6, 1), (7.1, .7), (8, .1), (A, 0)])
    w = whoosh(A, e, lambda s: 110 + 120 * np.sin(np.pi * min(1, s / 7.0)), q=.6)
    wl = whoosh(A, e, lambda s: 60 + 40 * min(1, s / 7), q=.5)
    p = np.interp(np.arange(secs(A)) / SR, [0, 4, 7, A], [-.7, -.3, .1, 0])
    a = (p + 1) * np.pi / 4
    place(b, np.stack([w * np.cos(a), w * np.sin(a)], 1) + widen(wl, 9) * .8, 2.6, .55)
    place(b, widen(groan(A, np.interp(np.arange(secs(A)) / SR, [0, 3, 6, A], [36, 44, 40, 34]), 300)
                   * env_points(A, [(0, 0), (1, .4), (4, .6), (7, .5), (A, 0)]), 13), 2.6, .3)
    # ~9.6: the finger comes to rest under the lip — the whole edge takes the weight
    place(b, boom(7, 46, 19, drop=1.1, drive=3.0, click=.5), 9.55, 1.0)
    cr = grind(2.5, env_points(2.5, [(0, 1), (.4, .8), (1.2, .3), (2.5, 0)]), 120, 1400)
    place(b, widen(cr, 5), 9.55, .55)
    place(b, widen(gravel(7, [(0, 0), (.2, 1), (2.5, .5), (7, 0)])), 9.7, .5)
    return b


def lift():
    D = 33.0
    b = np.zeros((secs(D), 2))
    t = t_axis(D)
    v = smooth(t / 4.5) * (1 - smooth((t - 20.5) / 7.5))
    v = np.where(t > 28, 0, v)
    # no hiss: the air the arm pushes is felt, not heard — a deep pressure that swells with the speed, slow low whooms
    # as the hand sweeps round, the body's own hum under it
    b += widen(lp(brown(D), 140, 2) * v * 1.2, 21)
    wh = whoosh(D, v ** 1.2, lambda s: 70 + 90 * float(np.interp(s, t[::441], v[::441])) + 25 * np.sin(s * .9), q=.5)
    b += widen(lp(wh, 260, 2), 13) * .8
    b += widen(sub(D, [(0, 30), (8, 36), (16, 33), (24, 38), (D, 34)], [(0, 0), (3, .5), (14, .7), (26, .5), (29, 0), (D, 0)]), 9) * .6
    # the arm's weight in the joints: deep groans as the pull bends round
    for at, f0, f1, g in ((.3, 34, 46, .55), (6.0, 40, 32, .45), (12.5, 36, 44, .45), (18.2, 44, 34, .5), (23.8, 38, 30, .4)):
        L = 4.0
        gr = groan(L, np.linspace(f0, f1, secs(L)), 360) * env_points(L, [(0, 0), (.6, 1), (2.4, .7), (L, 0)])
        place(b, widen(gr, 11), at, g)
        place(b, widen(grind(L, env_points(L, [(0, 0), (.8, .6), (2, .3), (L, 0)]), 70, 380), 7), at, .25)
    # lift-off and the settle at his face
    place(b, boom(6, 40, 20, drop=1.0, drive=2.0, click=.15), .2, .7)
    place(b, boom(5, 34, 20, drop=.7, drive=1.6, click=.05), 27.4, .45)
    return b


for name, fn, wet, rt in (("gm_god_notice", notice, .45, 6.0), ("gm_god_reach", reach, .4, 5.5), ("gm_god_lift", lift, .22, 3.5)):
    x = fn()
    x = bass_harmonics(x, .35)
    x = reverb(x, wet=wet, rt=rt, dark=2200, pre=.04)
    x = master(trim_tail(x, -58), -1.0)
    write(f"{OUT}/{name}.wav", x)
    print(name, f"{len(x) / SR:.1f}s")
