"""Outing soundtrack, tracks 16-20."""
import sys
import numpy as np
from engine import *
from ost_b import drone


def t16():
    """«Сундук»: a shiny find. 3/4 100 bpm, G major with a surprise Eb; glockenspiel tune, harp, pizz, celesta sparkle, coins."""
    s = Song("16_sunduk", 100, 3, 36, seed=116)
    ch = prog([('GM7', 2), ('Em7', 2), ('CM7', 2), ('D', 1), ('EbM7', 1)], s.bars)
    sc = Scale('G', 'ionian')
    s.sections([(4, {'harp', 'pad'}), (16, {'harp', 'pad', 'glock', 'bass', 'perc'}), (12, {'harp', 'pad', 'glock', 'bass', 'perc', 'spark'}),
                (4, {'harp', 'pad', 'spark'})])
    s.arp('harp', ch, 'harp', [0, 1, 2, 3, 2, 1], 0.5, lo=55, span=2, pan=[-.3, .3])
    s.pads('pad', ch, 'pad', center=60, n=4, cutoff=1800, a=0.8, r=1.5)
    s.melody('glock', ch, sc, 'glock', 76, 93, phrase_bars=2, plan='AABA', start=4, durs=(0.5, 1, 1.5, 2), weights=(3, 3, 1, 1), end_rest=1.0)
    s.bassline('bass', ch, [(0, 0, .4), (1, 7, .3), (2, 12, .3)], inst='pizz', octave=2)
    s.drums('perc', {'x': 'x.x.x.', 'r': 'r.....'}, vel={'x': .4, 'r': .4}, pans={'x': .4})
    s.arp('spark', ch, 'glock', [6, 5, 4, 3, None, None], 0.25, lo=84, span=2, pan=[-.5, .5], vel=0.5)
    for at in s.rng.uniform(0, s.L / SR, 6):
        s.loop.add('perc', coin(s.rng), at, pan=s.rng.uniform(-.6, .6), gain=0.5)
    for k, v in dict(harp=-21, pad=-25, glock=-17, bass=-22, perc=-27, spark=-27).items():
        s.role(k, v)
    s.bus_fx('harp', rev=('hall', 0.4))
    s.bus_fx('glock', rev=('hall', 0.5), delay=(1.5, 0.25, 0.15))
    s.bus_fx('spark', rev=('hall', 0.8))
    s.bus_fx('pad', chorus=3, rev=('hall', 0.4))
    s.render(target_rms=-20.0, wow=(6, 2))


def t17():
    """«Старая пасека»: a crooked village dance with bees. 5/8 (2+3), D mixolydian; accordion, balalaika, tuba, woodblocks, buzzing."""
    s = Song("17_staraya_paseka", 150, 2.5, 64, seed=117)
    ch = prog([('D', 2), ('C', 2), ('D', 2), ('G', 1), ('A7', 1)], s.bars)
    sc = Scale('D', 'mixolydian')
    s.sections([(8, {'bal', 'tuba', 'bees', 'wood'}), (24, {'bal', 'tuba', 'bees', 'wood', 'acc'}), (8, {'bal', 'bees', 'wood', 'flute'}),
                (24, {'bal', 'tuba', 'bees', 'wood', 'acc', 'flute'})])
    s.bassline('tuba', ch, [(0, 0, .8), (1, 7, .8)], inst=lambda m, d, r: tuba(m, d, r, slide=2.5), octave=2)
    for bar in range(s.bars):
        for st in (0.5, 1.5, 2.0):
            for k, m in enumerate(ch[bar].voicing(64, 3)):
                s.note('bal', 'pluck', m, bar * s.bpb + st + k * 0.02, 0.3, 0.7, pan=0.35, tau=0.5, pos=0.12, bright=1.2, damp=True)
    s.drums('wood', {'w': 'W.w.ww.r.r'}, steps=10, vel={'W': .6, 'w': .4, 'r': .35}, pans={'w': -.5, 'r': .5})
    s.melody('acc', ch, sc, 'accordion', 64, 81, phrase_bars=4, plan='AABA', start=8, durs=(0.5, 1, 1.5), weights=(3, 3, 1), end_rest=1.0)
    s.melody('flute', ch, sc, 'flute', 74, 88, phrase_bars=4, plan='AB', start=32, durs=(0.5, 1, 1.5, 2.5), weights=(2, 2, 2, 1), end_rest=1.0, seed=17)
    s.loop.add_full('bees', buzz_layer(s.L, s.rng, f=200, n_bees=4))
    for k, v in dict(tuba=-21, bal=-22, wood=-26, acc=-18, flute=-18, bees=-30).items():
        s.role(k, v)
    s.bus_fx('acc', rev=('room', 0.3), chorus=2)
    s.bus_fx('bal', rev=('room', 0.3))
    s.bus_fx('flute', rev=('hall', 0.4))
    s.bus_fx('tuba', rev=('room', 0.15))
    s.render(target_rms=-19.5, wow=(10, 3))


def t18():
    """«Привал»: warm lo-fi rest by the fire. 72 bpm swung, F major; nylon chords, sub bass, soft drums, whistle, fire crackle."""
    s = Song("18_prival", 72, 4, 20, seed=118, swing=0.08)
    ch = prog([('FM9', 1), ('Em7', 1), ('Dm9', 1), ('C6', 1), ('BbM7', 1), ('Am7', 1), ('Gm9', 1), ('C7sus4', 1)], s.bars)
    sc = Scale('F', 'ionian')
    s.sections([(4, {'keys', 'fire', 'bass'}), (8, {'keys', 'fire', 'bass', 'drums'}), (8, {'keys', 'fire', 'bass', 'drums', 'lead'})])
    for bar in range(s.bars):
        for st, d, g in [(0, 2.2, 1.0), (2.5, 1.2, 0.7)]:
            for k, m in enumerate(ch[bar].voicing(60, 4)):
                s.note('keys', 'pluck', m, bar * 4 + st + k * 0.03, d, 0.8 * g, pan=-0.3 + 0.2 * k, tau=1.5, pos=0.22, bright=0.7, damp=True)
    s.bassline('bass', ch, [(0, 0, 1.4), (2.5, 7, 0.8), (3.5, 0, .4)], octave=2, cutoff=200, saw=0.15)
    kick = 'k.....k...k.....'
    s.drums('drums', {'k': kick, 's': '....s.......s...', 'h': 'h.h.h.h.h.h.h.hh'}, vel={'k': .8, 's': .45, 'h': .25})
    s.melody('lead', ch, sc, lambda m, d, r: flute(m, d, r, breath=0.3, vib=0.004), 69, 84, phrase_bars=2, plan='ABAC', start=12,
             durs=(0.5, 1, 1.5, 2), weights=(2, 2, 2, 1), end_rest=1.5)
    L = s.L
    fire = crackle(L, s.rng, rate=10, level=0.05) + clp(chp(s.rng.randn(L, 2), 80), 400) * 0.05
    s.loop.add_full('fire', fire)
    for k, v in dict(keys=-19, bass=-21, drums=-22, lead=-18, fire=-30).items():
        s.role(k, v)
    s.bus_fx('keys', chorus=2, rev=('room', 0.4), duck=(s.kick_times(kick, 16), 0.3))
    s.bus_fx('lead', rev=('hall', 0.45), delay=(0.75, 0.3, 0.2))
    s.bus_fx('drums', lp=6000, rev=('room', 0.2))
    s.render(target_rms=-20.0, wow=(10, 2.5), master_lp=8000, crackle_level=0.006)


def t19():
    """«Пора домой»: the way back. Retrowave ballad 84 bpm, Bb major / G minor; opens with the shop lullaby on a music box,
    gated snare, EP, pads, pulse bass, gliding lead, choir."""
    s = Song("19_pora_domoy", 84, 4, 28, seed=119)
    ch = prog([('BbM7', 2), ('F/A', 2), ('Gm7', 2), ('EbM7', 1), ('F', 1)], s.bars)
    sc = Scale('Bb', 'ionian')
    s.sections([(4, {'box', 'pad'}), (8, {'pad', 'ep', 'bass', 'drums'}), (12, {'pad', 'ep', 'bass', 'drums', 'lead'}),
                (4, {'pad', 'ep', 'choir', 'box'})])
    # the shop lullaby's opening phrase (moved to G minor), on the music box: home is near
    E8 = 0.5
    phrase = [(74, 3), (70, 1), (72, 1), (74, 1), (79, 4), (77, 1), (74, 1)]
    for start in (0, 24 * 4):
        b = start
        for m, d in phrase:
            s.note('box', 'music_box', m + 12, b, d * E8, 0.9, pan=0.2)
            b += d * E8
    s.pads('pad', ch, 'pad', center=60, n=4, cutoff=2000, a=0.8, r=1.5, spread=14)
    s.pads('ep', ch, 'ep', center=62, n=4, legato=0.45, vel=0.8, spread=0.5)
    s.bassline('bass', ch, [(b * 0.5, 0, 0.42) for b in range(8)], octave=2, cutoff=700, saw=0.5, a=0.004, r=0.05)
    kick = 'k.........k.....'
    s.drums('drums', {'k': kick, 's': '....S.......S...', 'h': 'h.h.h.h.h.h.h.h.'}, vel={'k': 1, 'S': .8, 'h': .3})
    s.melody('lead', ch, sc, lambda m, d, r: lead(m, d, r, cutoff=2800, glide_from=m - 2), 65, 84, phrase_bars=4, plan='ABAC', start=12,
             durs=(1, 1.5, 2, 3), weights=(2, 2, 2, 1), end_rest=2.0)
    s.pads('choir', ch, 'choir', center=64, n=3, vowel='a', a=1.5, r=2.5)
    for k, v in dict(box=-19, pad=-22, ep=-23, bass=-21, drums=-19, lead=-17, choir=-23).items():
        s.role(k, v)
    s.bus_fx('box', rev=('hall', 0.6))
    s.bus_fx('pad', chorus=4, rev=('hall', 0.5), duck=(s.kick_times(kick, 16), 0.2))
    s.bus_fx('ep', chorus=3, rev=('room', 0.3))
    s.bus_fx('lead', delay=(0.75, 0.35, 0.25), rev=('hall', 0.5), chorus=2)
    s.bus_fx('drums', rev=('plate', 0.25))
    s.bus_fx('choir', rev=('huge', 0.7))
    s.render(target_rms=-19.5, wow=(6, 1.5))


def t20():
    """«Бог в собственных руках»: awe. 54 bpm, C minor; huge choir, organ, sub drone, slow horn, timpani rolls, high bells."""
    s = Song("20_bog_v_sobstvennyh_rukah", 54, 4, 16, seed=120)
    ch = prog([('Cm', 2), ('AbM7', 2), ('Fm9', 2), ('G7sus4', 1), ('G', 1)], s.bars)
    sc = Scale('C', 'aeolian')
    s.sections([(4, {'drone', 'choir'}), (4, {'drone', 'choir', 'organ', 'bells'}), (6, {'drone', 'choir', 'organ', 'bells', 'horn', 'timp'}),
                (2, {'drone', 'choir'})])
    s.loop.add_full('drone', drone(s, [(24, 1), (31, .6), (36, .3)], 200))
    for vow, cen in [('a', 60), ('o', 48)]:
        s.pads('choir', ch, 'choir', center=cen, n=4, vowel=vow, a=2.5, r=3.5, voices=4)
    s.pads('organ', ch, 'organ', center=55, n=4, legato=0.98)
    s.arp('bells', ch, lambda m, d, r: glass(m, 0.4, r, a=0.005, r=4.0), [3, None, None, None, None, None, 2, None], 0.5, lo=72, span=2, pan=[-.5, .5])
    s.melody('horn', ch, sc, 'brass', 55, 70, phrase_bars=2, plan='AB', start=8, durs=(2, 3, 4), weights=(1, 2, 1), end_rest=2, flo=350, fhi=1500, a=0.4, r=1.5)
    s.drums('timp', {'t': ['T...............', '............tttt']}, vel={'T': 1, 't': .4})
    for k, v in dict(drone=-21, choir=-19, organ=-24, bells=-25, horn=-19, timp=-22).items():
        s.role(k, v)
    s.bus_fx('choir', rev=('huge', 0.9))
    s.bus_fx('organ', rev=('cave', 0.6))
    s.bus_fx('bells', rev=('huge', 0.9))
    s.bus_fx('horn', rev=('huge', 0.8))
    s.bus_fx('timp', rev=('cave', 0.6))
    s.render(target_rms=-19.5, wow=(4, 1))


if __name__ == '__main__':
    for name in sys.argv[1:] or ['t16', 't17', 't18', 't19', 't20']:
        globals()[name]()
