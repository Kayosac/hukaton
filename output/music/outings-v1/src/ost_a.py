"""Outing soundtrack, tracks 01-05."""
import sys
import numpy as np
from engine import *


def t01():
    """«Первый шаг»: start hill, calm morning. 6/8, D lydian. Kalimba, soft pad, flute, glock."""
    s = Song("01_pervyy_shag", 90, 3, 48, seed=101)
    ch = prog([('DM7', 2), ('E/D', 2), ('Bm7', 2), ('GM7', 1), ('A7sus4', 1)], s.bars)
    sc = Scale('D', 'lydian')
    s.sections([(4, {'pad', 'kal'}), (8, {'pad', 'kal', 'bass'}), (16, {'pad', 'kal', 'bass', 'flute', 'perc'}),
                (16, {'pad', 'kal', 'bass', 'flute', 'glock', 'perc'}), (4, {'pad', 'kal'})])
    s.pads('pad', ch, 'pad', center=57, n=4, cutoff=1100, a=1.5, r=2.0)
    s.arp('kal', ch, 'kalimba', [0, 2, 1, 3, 2, 4], 0.5, lo=62, span=2, accent=[1, .6, .7, .8, .6, .7], pan=[-.3, .2, -.1, .3, 0, -.2])
    s.bassline('bass', ch, [(0, 0, 2.6)], octave=2, cutoff=260, saw=0.12, a=0.05, r=0.5)
    s.melody('flute', ch, sc, 'flute', 69, 86, phrase_bars=4, plan='ABAC', start=12, durs=(0.5, 1, 1.5, 3), weights=(1, 2, 3, 1), end_rest=1.5)
    s.arp('glock', ch, 'glock', [4, None, None, 5, None, None], 0.5, lo=74, span=2, pan=0.4)
    s.drums('perc', {'x': '..x..x', 'w': 'w.....'}, vel={'x': .5, 'w': .5}, pans={'x': .5, 'w': -.4})
    for k, v in dict(pad=-24, kal=-21, bass=-23, flute=-17, glock=-28, perc=-31).items():
        s.role(k, v)
    s.bus_fx('pad', chorus=3, rev=('hall', 0.5))
    s.bus_fx('kal', rev=('hall', 0.35))
    s.bus_fx('flute', rev=('hall', 0.55), delay=(1.5, 0.3, 0.2))
    s.bus_fx('glock', rev=('hall', 0.6))
    s.render(target_rms=-20.5, wow=(6, 1.5))


def t02():
    """«Кривая тропа II»: limping 7/8 walk, E phrygian; drone, kalimba, wooden perc, retro pulse bass, flute; muffled break."""
    s = Song("02_krivaya_tropa_2", 150, 3.5, 48, seed=102)
    ch = prog([('Em', 2), ('FM7', 2), ('Em', 2), ('Dsus2', 1), ('C', 1)], s.bars)
    sc = Scale('E', 'phrygian')
    s.sections([(8, {'drone', 'kal', 'perc'}), (16, {'drone', 'kal', 'perc', 'bass', 'pad', 'flute'}),
                (8, {'drone', 'kal', 'perc', 'bass', 'pad', 'flute', 'lead'}), (8, {'drone', 'kal', 'pad', 'choir'}),
                (8, {'drone', 'kal', 'perc', 'bass', 'pad', 'lead'})])
    s.arp('kal', ch, 'kalimba', [0, 2, 1, 3, 2, 1, 4], 0.5, lo=64, span=2, accent=[1, .6, 1, .6, 1, .6, .7], pan=[-.4, .3, -.2, .4, 0, -.3, .2])
    s.drums('perc', {'w': 'W.w.w..', 'k': 'k...k..', 'x': '.x.x.xx'}, vel={'W': .8, 'w': .6, 'k': .7, 'x': .4}, pans={'w': .4, 'W': -.3, 'x': .5})
    s.bassline('bass', ch, [(b * 0.5, 0, 0.42) for b in range(7)], octave=2, cutoff=800, saw=0.5, a=0.004, r=0.04)
    s.pads('pad', ch, 'pad', center=60, n=4, cutoff=1500, a=0.8, r=1.5, spread=14)
    s.melody('flute', ch, sc, 'flute', 67, 84, phrase_bars=2, plan='AABA', start=8, durs=(0.5, 1, 1.5), weights=(2, 3, 1), end_rest=1.0)
    s.melody('lead', ch, sc, 'lead', 72, 88, phrase_bars=2, plan='AB', start=24, durs=(1, 1.5, 2), end_rest=1.0, cutoff=2600, seed=7)
    s.pads('choir', ch, 'choir', center=64, n=3, every=2, vowel='u', a=2.0, r=3.0)
    for k, v in dict(drone=-22, kal=-21, perc=-24, bass=-21, pad=-25, flute=-17, lead=-19, choir=-22).items():
        s.role(k, v)
    # drone: E1/B1, whole cycles
    n = np.arange(s.L)
    dr = np.zeros((s.L, 2))
    for m, g in [(28, 1), (35, .5), (40, .25)]:
        cyc = round(mtof(m) * s.L / SR)
        dr[:, 0] += g * np.sin(2 * np.pi * cyc * n / s.L)
        dr[:, 1] += g * np.sin(2 * np.pi * (cyc + 1) * n / s.L)
    s.loop.add_full('drone', clp(dr, 300))
    s.bus_fx('kal', rev=('hall', 0.35))
    s.bus_fx('flute', rev=('hall', 0.5), delay=(1.75, 0.35, 0.25))
    s.bus_fx('lead', rev=('hall', 0.4), delay=(0.75, 0.3, 0.2), chorus=3)
    s.bus_fx('pad', chorus=4, rev=('hall', 0.4))
    s.bus_fx('choir', rev=('cave', 0.8))
    s.bus_fx('perc', rev=('room', 0.2))
    s.render(target_rms=-19.5, wow=(9, 2), muffle=[(32, 40)])


def t03():
    """«Листопад»: autumn forest, light sadness. 3/4, A dorian; harp, soft EP, glass melody, brushes, VHS wobble."""
    s = Song("03_listopad", 84, 3, 40, seed=103)
    ch = prog([('Am7', 2), ('D9', 2), ('FM7', 2), ('Em7', 1), ('G6', 1)], s.bars)
    sc = Scale('A', 'dorian')
    s.sections([(8, {'harp', 'pad'}), (16, {'harp', 'pad', 'bass', 'ep', 'perc'}), (16, {'harp', 'pad', 'bass', 'ep', 'perc', 'glass'})])
    s.arp('harp', ch, 'harp', [0, 2, 3, 4, 3, 2], 0.5, lo=57, span=2, accent=[1, .6, .7, .8, .6, .6], pan=[-.3, -.1, .1, .3, .1, -.1])
    s.pads('pad', ch, 'pad', center=55, n=3, cutoff=900, a=1.5, r=2.0)
    s.bassline('bass', ch, [(0, 0, 2.5)], octave=2, cutoff=300, saw=0.2, a=0.02, r=0.4)
    s.pads('ep', ch, 'ep', center=64, n=4, legato=0.5, vel=0.8, spread=0.4)
    s.melody('glass', ch, sc, 'glass', 69, 86, phrase_bars=4, plan='ABAC', start=24, durs=(1, 1.5, 2, 3), weights=(2, 2, 2, 1), end_rest=2.0, a=0.15, r=1.5)
    s.drums('perc', {'b': '..b.b.', 'x': 'x.x.xx', 'k': 'k.....'}, vel={'b': .6, 'x': .35, 'k': .5}, pans={'x': .5})
    for k, v in dict(harp=-20, pad=-25, bass=-23, ep=-23, glass=-18, perc=-27).items():
        s.role(k, v)
    s.bus_fx('harp', rev=('hall', 0.35))
    s.bus_fx('ep', chorus=3, rev=('room', 0.3))
    s.bus_fx('glass', rev=('hall', 0.6), delay=(1.5, 0.3, 0.2))
    s.bus_fx('pad', chorus=4, rev=('hall', 0.4))
    s.render(target_rms=-20.5, wow=(13, 3), crackle_level=0.006)


def t04():
    """«Чаща»: thicket maze. 5/4, C octatonic; marimba in 7s against the bar, kalimba 4-against-5, clock, sparse reed."""
    s = Song("04_chashcha", 96, 5, 28, seed=104)
    ch = prog([('Cm', 2), ('F#m', 2), ('Ebm', 2), ('Am', 2)], s.bars)
    sc = Scale('C', 'octatonic')
    s.sections([(4, {'mar', 'drone', 'clock'}), (8, {'mar', 'drone', 'clock', 'kal', 'bass'}),
                (8, {'mar', 'drone', 'clock', 'kal', 'bass', 'reed', 'toms'}), (4, {'mar', 'clock', 'drone'}),
                (4, {'mar', 'drone', 'clock', 'kal', 'bass', 'reed'})])
    s.arp('mar', ch, 'marimba', [0, 2, 1, 3, 2, 4, 1], 0.5, lo=60, span=2, accent=[1, .7, .8, .7, .9, .7, .7], pan=[-.4, .4, -.2, .2, 0, -.4, .4])
    s.arp('kal', ch, 'kalimba', [3, 1, 4, 2], 1.25, lo=72, span=2, pan=0.5)
    s.drums('clock', {'q': 'qQqQq'}, vel={'q': .7, 'Q': .6}, pans={'q': -.5, 'Q': -.5})
    s.bassline('bass', ch, [(0, 0, 2.5), (3, 6, 1.5)], octave=2, cutoff=300, saw=0.25)
    s.melody('reed', ch, sc, 'reed', 55, 72, phrase_bars=2, plan='ABAC', start=12, durs=(0.5, 1, 1.5, 2), weights=(1, 2, 2, 1), end_rest=2.0, steps=(-1, 1, -2, 2, 3, -3))
    s.drums('toms', {'m': ['m.........', '......n.n.']}, vel={'m': .7, 'n': .5})
    s.pads('drone', ch, 'pad', center=48, n=2, cutoff=500, a=2.0, r=2.0, every=2)
    for k, v in dict(mar=-20, kal=-25, clock=-27, bass=-22, reed=-19, toms=-26, drone=-25).items():
        s.role(k, v)
    s.bus_fx('mar', rev=('room', 0.3))
    s.bus_fx('kal', rev=('hall', 0.4), delay=(0.75, 0.4, 0.3))
    s.bus_fx('reed', rev=('hall', 0.5))
    s.bus_fx('clock', rev=('room', 0.3))
    s.bus_fx('toms', rev=('hall', 0.4))
    s.render(target_rms=-20.0, wow=(10, 2), muffle=[(20, 24)])


def t05():
    """«Вдоль реки»: flowing 12/8, E lydian; celesta and harp arpeggios, string pad, flute, a stream texture."""
    s = Song("05_vdol_reki", 120, 6, 24, seed=105)
    ch = prog([('EM9', 2), ('F#/E', 2), ('C#m7', 2), ('AM7', 1), ('B6', 1)], s.bars)
    sc = Scale('E', 'lydian')
    s.sections([(4, {'cel', 'pad', 'water'}), (8, {'cel', 'pad', 'water', 'bass', 'harp'}),
                (8, {'cel', 'pad', 'water', 'bass', 'harp', 'flute'}), (4, {'cel', 'water', 'pad'})])
    s.arp('cel', ch, 'glock', [0, 1, 2, 3, 4, 3, 2, 1, 2, 3, 4, 5], 0.5, lo=71, span=2, accent=[1, .5, .6, .7, .8, .6, .5, .5, .6, .7, .8, .6], vel=0.6,
          pan=[-.5, -.3, -.1, .1, .3, .5, .3, .1, -.1, -.3, -.5, -.3])
    s.arp('harp', ch, 'harp', [0, None, 2, None, 1, None, 3, None, 2, None, 1, None], 0.5, lo=52, span=2, pan=-0.2)
    s.pads('pad', ch, 'strings', center=60, n=4, a=1.2, r=2.0, cutoff=1400)
    s.bassline('bass', ch, [(0, 0, 5.5)], octave=2, cutoff=250, saw=0.1, a=0.1, r=0.8)
    s.melody('flute', ch, sc, 'flute', 71, 88, phrase_bars=2, plan='ABAC', start=12, durs=(1, 1.5, 3, 4.5), weights=(1, 3, 2, 1), end_rest=1.5)
    # stream: band noise with shimmering AM (whole cycles)
    n = np.arange(s.L)
    w = np.zeros((s.L, 2))
    for fc, cyc in [(1400, 37), (2600, 53), (3900, 71)]:
        band = cbp(s.rng.randn(s.L, 2), fc * 0.8, fc * 1.25)
        w += band * (0.6 + 0.4 * np.sin(2 * np.pi * cyc * n / s.L + s.rng.uniform(0, 6)))[:, None]
    s.loop.add_full('water', w)
    for k, v in dict(cel=-23, harp=-22, pad=-24, bass=-24, flute=-17, water=-33).items():
        s.role(k, v)
    s.bus_fx('cel', rev=('hall', 0.5), delay=(0.75, 0.3, 0.2))
    s.bus_fx('harp', rev=('hall', 0.35))
    s.bus_fx('flute', rev=('hall', 0.55))
    s.bus_fx('pad', chorus=3, rev=('hall', 0.5))
    s.render(target_rms=-20.5, wow=(5, 1.5))


if __name__ == '__main__':
    for name in sys.argv[1:] or ['t01', 't02', 't03', 't04', 't05']:
        globals()[name]()
