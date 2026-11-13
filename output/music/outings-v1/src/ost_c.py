"""Outing soundtrack, tracks 11-15."""
import sys
import numpy as np
from engine import *
from ost_b import drone


def t11():
    """«Ночной лес»: eerie night. 3/4 66 bpm, D minor; low strings, sparse celesta, harp, pizz bass, crickets, owl."""
    s = Song("11_nochnoy_les", 66, 3, 24, seed=111)
    ch = prog([('Dm', 2), ('Gm', 2), ('Bb', 2), ('A', 2)], s.bars)
    sc = Scale('D', 'harmonic')
    s.sections([(4, {'pad', 'night'}), (8, {'pad', 'night', 'pizz', 'cel'}), (8, {'pad', 'night', 'pizz', 'cel', 'harp'}), (4, {'pad', 'night', 'cel'})])
    s.pads('pad', ch, 'strings', center=50, n=3, cutoff=800, a=1.5, r=2.0)
    s.bassline('pizz', ch, [(0, 0, .5), (2, 7, .5)], inst='pizz', octave=2)
    s.melody('cel', ch, sc, 'glock', 74, 91, phrase_bars=2, plan='ABAC', start=4, durs=(0.5, 1, 1.5, 3), weights=(1, 2, 2, 2), end_rest=2.0, vel=0.7)
    s.arp('harp', ch, 'harp', [0, 1, 2, 3, None, None], 0.5, lo=50, span=2, pan=-0.3)
    t = 0.0
    while t < s.L / SR:
        s.loop.add('night', cricket(s.rng), t, pan=s.rng.uniform(-.9, .9), gain=s.rng.uniform(.2, .6))
        t += s.rng.uniform(0.4, 1.6)
    for at in s.rng.uniform(0, s.L / SR, 4):        # owl: two low breathy hoots
        for k in range(2):
            s.loop.add('night', flute(55 - k * 2, 0.35, s.rng, breath=0.5, a=0.05), at + k * 0.5, pan=s.rng.uniform(-.8, .8), gain=0.7)
    for k, v in dict(pad=-24, pizz=-23, cel=-19, harp=-23, night=-29).items():
        s.role(k, v)
    s.bus_fx('pad', chorus=3, rev=('hall', 0.6))
    s.bus_fx('cel', rev=('hall', 0.7), delay=(1.5, 0.35, 0.25))
    s.bus_fx('harp', rev=('hall', 0.4))
    s.bus_fx('night', rev=('hall', 0.5))
    s.bus_fx('pizz', rev=('room', 0.3))
    s.render(target_rms=-21.0, wow=(6, 1.5))


def t12():
    """«Странное»: weirdcore. Elevator muzak from a payphone: jazzy EP, vibes, walking bass, brushes; modem chirps, stutters,
    heavy tape wobble, a section that sinks behind the wall."""
    s = Song("12_strannoe", 90, 4, 24, seed=112, swing=0.12)
    ch = prog([('CM9', 1), ('Am9', 1), ('Dm9', 1), ('G9', 1), ('EM7', 1), ('Am9', 1), ('Fm9', 1), ('G7sus4', 1)], s.bars)
    sc = Scale('C', 'ionian')
    s.pads('ep', ch, 'ep', center=62, n=4, legato=0.6, vel=0.8, spread=0.5)
    s.bassline('bass', ch, [(0, 0, .9), (1, 7, .9), (2, 12, .9), (3, 'n', .9)], octave=2, cutoff=600, saw=0.2)
    s.melody('vib', ch, sc, 'vibes', 69, 86, phrase_bars=2, plan='ABAC', start=4, durs=(0.5, 1, 1.5, 2), weights=(2, 3, 2, 1), end_rest=1.0)
    s.drums('drums', {'b': '....b.......b...', 'x': 'x.x.x.x.x.x.x.x.', 'k': 'k.......k.......'}, vel={'b': .7, 'x': .3, 'k': .5})
    for at in np.arange(6, s.L / SR, 13.0):
        s.loop.add('modem', modem(s.rng, s.rng.uniform(0.5, 1.2)), at + s.rng.uniform(0, 3), pan=s.rng.uniform(-.6, .6))
    for k, v in dict(ep=-20, bass=-21, vib=-18, drums=-25, modem=-28).items():
        s.role(k, v)
    s.bus_fx('ep', chorus=5, rev=('room', 0.3))
    s.bus_fx('vib', rev=('room', 0.4))
    s.bus_fx('modem', bp=(500, 3500))

    def phone(s_, mix):
        x = 0.75 * cbp(mix, 320, 3200, 2) + 0.25 * mix
        times = list(s_.rng.uniform(0, s_.L / SR, 5))
        return glitch(x, s_.rng, times, slice_sec=0.07, repeats=5)
    s.render(target_rms=-20.0, wow=(25, 5), muffle=[(16, 20)], post=phone, crackle_level=0.008)


def t13():
    """«Звериная драка»: tense fight. 7/8 140 bpm, E hijaz; taiko and toms, heavy kick, low brass stabs, driven bass, brass lead."""
    s = Song("13_zverinaya_draka", 140, 3.5, 40, seed=113)
    ch = prog([('E', 2), ('F', 2), ('E', 2), ('Dm', 1), ('F', 1)], s.bars)
    sc = Scale('E', 'hijaz')
    s.sections([(4, {'drums', 'bass'}), (12, {'drums', 'bass', 'stabs', 'perc'}), (16, {'drums', 'bass', 'stabs', 'perc', 'lead'}),
                (8, {'drums', 'bass', 'perc'})])
    kick = 'K...K...K.K...'
    s.drums('drums', {'k': kick, 's': '....S.....S..s', 'h': 'h.h.h.h.h.h.hh'}, vel={'K': 1, 'S': .8, 's': .4, 'h': .3})
    s.drums('perc', {'t': ['T.....t...t...', 'T...t...m.m.n.']}, vel={'T': 1, 't': .7, 'm': .6, 'n': .5})
    s.bassline('bass', ch, [(b * 0.5, 0 if b % 3 else 12, 0.4) for b in range(7)], octave=1, cutoff=900, saw=0.8, a=0.003, r=0.04)
    s.bassline('stabs', ch, [(0, 12, 0.6), (2, 12, 0.4), (3, 13, 0.4)], inst=lambda m, d, r: brass(m, d, r, flo=300, fhi=2200, a=0.02, r=0.1), octave=2)
    s.melody('lead', ch, sc, 'brass', 60, 76, phrase_bars=2, plan='AABA', start=16, durs=(0.5, 1, 1.5), weights=(3, 2, 1), end_rest=0.5,
             steps=(-1, 1, 1, 2, -2, 3), flo=600, fhi=3500, a=0.03, r=0.2)
    for k, v in dict(drums=-18, perc=-21, bass=-19, stabs=-22, lead=-18).items():
        s.role(k, v)
    s.bus_fx('drums', rev=('room', 0.2))
    s.bus_fx('perc', rev=('hall', 0.4))
    s.bus_fx('stabs', rev=('hall', 0.3))
    s.bus_fx('lead', rev=('hall', 0.4), delay=(0.5, 0.25, 0.15))
    s.bus_fx('bass', duck=(s.kick_times(kick, 14), 0.25))
    s.render(target_rms=-18.5, wow=(4, 1.5), drive=1.6)


def t14():
    """«Погоня»: chase. Darksynth 160 bpm, D minor; four-on-the-floor, gated snare, 16th octave bass, arps, lead, strings."""
    s = Song("14_pogonya", 160, 4, 40, seed=114)
    ch = prog([('Dm', 2), ('Bb', 2), ('C', 2), ('A', 2)], s.bars)
    sc = Scale('D', 'harmonic')
    s.sections([(4, {'bass', 'arp'}), (16, {'bass', 'arp', 'drums', 'pad'}), (4, {'arp', 'pad'}), (16, {'bass', 'arp', 'drums', 'pad', 'lead'})])
    kick = 'k...k...k...k...'
    s.drums('drums', {'k': kick, 's': '....S.......S...', 'h': 'hhhhhhhhhhhhhhhh', 'o': '..o...o...o...o.'},
            vel={'k': 1, 'S': .8, 'h': .25, 'o': .3}, pans={'h': .3, 'o': -.3})
    s.bassline('bass', ch, [(b * 0.25, 12 if b % 2 else 0, 0.2) for b in range(16)],
               inst=lambda m, d, r: synth_pluck(m, d, r, lo=200, hi=2000, decay=0.06), octave=1)
    s.arp('arp', ch, lambda m, d, r: synth_pluck(m, d, r, lo=700, hi=6000, decay=0.06, saw=False), [0, 1, 2, 3, 4, 3, 2, 1], 0.25, lo=62, span=2, pan=[-.4, .4])
    s.pads('pad', ch, 'strings', center=60, n=3, cutoff=1600, a=0.4, r=0.8)
    s.melody('lead', ch, sc, 'lead', 69, 86, phrase_bars=4, plan='ABAC', start=24, durs=(0.5, 1, 1.5, 2), weights=(2, 3, 2, 1), end_rest=1.5, cutoff=3800, sq=0.3)
    for k, v in dict(drums=-18, bass=-20, arp=-24, pad=-25, lead=-17).items():
        s.role(k, v)
    kt = s.kick_times(kick, 16)
    s.bus_fx('pad', chorus=3, rev=('hall', 0.4), duck=(kt, 0.35))
    s.bus_fx('arp', delay=(0.75, 0.3, 0.25), rev=('room', 0.3), duck=(kt, 0.2))
    s.bus_fx('lead', delay=(0.75, 0.3, 0.2), rev=('hall', 0.4), chorus=2)
    s.bus_fx('drums', rev=('plate', 0.15))
    s.render(target_rms=-18.5, wow=(3, 1), drive=1.5)


def t15():
    """«Мимик»: sneaky, comic-sinister. 2/4 112 bpm, C harmonic minor; staccato pizz ostinato, creeping bassoon, ticks, glints, breathing."""
    s = Song("15_mimik", 112, 2, 56, seed=115)
    ch = prog([('Cm', 2), ('Ab', 2), ('Fm', 2), ('G7', 2)], s.bars)
    sc = Scale('C', 'harmonic')
    s.sections([(8, {'pizz', 'tick'}), (16, {'pizz', 'tick', 'reed', 'bass'}), (8, {'pizz', 'tick', 'glint', 'breath'}),
                (16, {'pizz', 'tick', 'reed', 'bass', 'glint'}), (8, {'pizz', 'tick', 'breath'})])
    s.arp('pizz', ch, 'pizz', [0, 2, 1, 2], 0.5, lo=55, span=1, accent=[1, .6, .8, .6], pan=[-.3, .3])
    s.bassline('bass', ch, [(0, 0, .3), (1, 7, .3)], inst='pizz', octave=2)
    s.drums('tick', {'w': 'w.r.', 'x': '...x'}, vel={'w': .5, 'r': .4, 'x': .3}, pans={'w': -.5, 'r': .5})
    s.melody('reed', ch, sc, 'reed', 50, 67, phrase_bars=4, plan='ABAC', start=8, durs=(0.25, 0.5, 0.5, 1), weights=(1, 3, 2, 1), end_rest=1.0,
             steps=(-1, 1, 1, -1, 2, -2), legato=0.6, cutoff=1100, width=0.15)
    s.arp('glint', ch, 'glock', [5, None, None, None, None, None, None, None], 0.5, lo=79, span=2, pan=0.5, vel=0.6)
    s.pads('breath', ch, 'choir', center=48, n=2, every=4, vowel='u', a=1.5, r=1.5)
    for k, v in dict(pizz=-20, tick=-26, reed=-17, bass=-22, glint=-26, breath=-25).items():
        s.role(k, v)
    s.bus_fx('pizz', rev=('room', 0.35))
    s.bus_fx('reed', rev=('room', 0.4))
    s.bus_fx('glint', rev=('hall', 0.7))
    s.bus_fx('breath', rev=('hall', 0.5))
    s.render(target_rms=-20.0, wow=(6, 2))


if __name__ == '__main__':
    for name in sys.argv[1:] or ['t11', 't12', 't13', 't14', 't15']:
        globals()[name]()
