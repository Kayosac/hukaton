"""Outing soundtrack, tracks 06-10."""
import sys
import numpy as np
from engine import *


def drone(s, notes, lp_hz=300):
    n = np.arange(s.L)
    dr = np.zeros((s.L, 2))
    for m, g in notes:
        cyc = round(mtof(m) * s.L / SR)
        dr[:, 0] += g * np.sin(2 * np.pi * cyc * n / s.L)
        dr[:, 1] += g * np.sin(2 * np.pi * (cyc + 1) * n / s.L)
    return clp(dr, lp_hz)


def t06():
    """«Каньон»: vast, echoing. 4/4 70 bpm, C minor; drone, taiko, distant horn, echoing plucks, cave reverb."""
    s = Song("06_kanyon", 70, 4, 24, seed=106)
    ch = prog([('C5', 2), ('Bb5', 2), ('Ab5', 2), ('G5', 1), ('Gsus4', 1)], s.bars)
    sc = Scale('C', 'penta')
    s.sections([(4, {'drone', 'pad'}), (8, {'drone', 'pad', 'taiko', 'pl'}), (8, {'drone', 'pad', 'taiko', 'pl', 'horn'}),
                (4, {'drone', 'horn', 'pad'})])
    s.loop.add_full('drone', drone(s, [(24, 1), (31, .5), (36, .3)]))
    s.pads('pad', ch, 'strings', center=55, n=3, a=2.0, r=3.0, cutoff=1000)
    s.drums('taiko', {'t': ['T.......t.....t.', 'T.....t.....T...']}, vel={'T': 1, 't': .6})
    s.arp('pl', ch, lambda m, d, r: synth_pluck(m, d, r, lo=400, hi=3500, decay=0.12),
          [0, None, None, 2, None, None, 1, None, None, None, 3, None, None, None, None, None], 0.25, lo=60, span=2, pan=[-.5, .5, 0])
    s.melody('horn', ch, sc, 'brass', 55, 72, phrase_bars=4, plan='AB', start=12, durs=(2, 3, 4), weights=(2, 2, 1), end_rest=3, flo=400, fhi=1800, a=0.3, r=1.0)
    for k, v in dict(drone=-23, pad=-25, taiko=-20, pl=-24, horn=-18).items():
        s.role(k, v)
    s.bus_fx('taiko', rev=('cave', 0.5))
    s.bus_fx('pl', delay=(0.75, 0.5, 0.6), rev=('cave', 0.6))
    s.bus_fx('horn', rev=('cave', 0.9), dry=0.6)
    s.bus_fx('pad', rev=('cave', 0.5))
    s.render(target_rms=-20.5, wow=(5, 1.5))


def t07():
    """«Горный перевал»: majestic retrowave sunrise. 4/4 92 bpm, F mixolydian; supersaw, pulse bass, gated snare, arps, lead, choir."""
    s = Song("07_gornyy_pereval", 92, 4, 32, seed=107)
    ch = prog([('FM7', 2), ('Eb', 2), ('Bb', 2), ('C', 2)], s.bars)
    sc = Scale('F', 'mixolydian')
    s.sections([(4, {'pad', 'arp'}), (8, {'pad', 'arp', 'bass', 'drums'}), (12, {'pad', 'arp', 'bass', 'drums', 'lead'}),
                (8, {'pad', 'arp', 'bass', 'drums', 'lead', 'choir'})])
    s.pads('pad', ch, 'pad', center=60, n=4, cutoff=2400, a=0.6, r=1.5, spread=16)
    s.arp('arp', ch, lambda m, d, r: synth_pluck(m, d, r, lo=500, hi=6000, decay=0.1), [0, 1, 2, 3, 2, 1, 3, 4], 0.25, lo=60, span=2, pan=[-.4, .4])
    s.bassline('bass', ch, [(b * 0.5, 0, 0.42) for b in range(8)], octave=2, cutoff=1000, saw=0.6, a=0.004, r=0.05)
    kick = 'k.......k.......'
    s.drums('drums', {'k': kick, 's': '....S.......S...', 'h': 'h.h.h.h.h.h.h.h.'}, vel={'k': 1, 'S': .8, 'h': .35})
    s.melody('lead', ch, sc, 'lead', 65, 84, phrase_bars=4, plan='ABAC', start=12, durs=(0.5, 1, 1.5, 2, 3), weights=(1, 2, 2, 2, 1), end_rest=2, cutoff=3000)
    s.pads('choir', ch, 'choir', center=64, n=3, every=2, vowel='a', a=1.5, r=2.5)
    for k, v in dict(pad=-22, arp=-25, bass=-21, drums=-19, lead=-17, choir=-23).items():
        s.role(k, v)
    s.bus_fx('pad', chorus=4, rev=('hall', 0.5), duck=(s.kick_times(kick, 16), 0.25))
    s.bus_fx('arp', delay=(0.75, 0.35, 0.3), rev=('hall', 0.3))
    s.bus_fx('lead', delay=(0.75, 0.35, 0.25), rev=('hall', 0.5), chorus=2)
    s.bus_fx('drums', rev=('plate', 0.25))
    s.bus_fx('choir', rev=('huge', 0.7))
    s.render(target_rms=-19.0, wow=(5, 1.5))


def t08():
    """«Гнилая топь»: murky swamp. 5/4 64 bpm, B phrygian; low drone, accordion murk, bubbles, croaks, low reed, slow wood."""
    s = Song("08_gnilaya_top", 64, 5, 16, seed=108)
    ch = prog([('Bm', 2), ('CM7', 2), ('Em/B', 2), ('C/B', 2)], s.bars)
    sc = Scale('B', 'phrygian')
    s.sections([(4, {'drone', 'murk', 'wet'}), (8, {'drone', 'murk', 'wet', 'reed', 'perc', 'pizz'}), (4, {'drone', 'murk', 'wet', 'reed'})])
    s.loop.add_full('drone', drone(s, [(23, 1), (30, .4), (35, .2)], 220))
    s.pads('murk', ch, lambda m, d, r: accordion(m, d, r, a=1.0, r=1.5, cutoff=1100), center=55, n=3, legato=0.98)
    s.melody('reed', ch, sc, 'reed', 47, 64, phrase_bars=2, plan='ABAC', start=4, durs=(1, 1.5, 2, 3), weights=(1, 2, 2, 1), end_rest=2,
             steps=(-1, 1, -1, 2, -2, 0), cutoff=900, width=0.12)
    s.arp('pizz', ch, 'pizz', [0, None, None, 1, None, None, 2, None, None, None], 0.5, lo=40, span=1, pan=-0.3)
    s.drums('perc', {'W': 'W.....m...'}, vel={'W': .6, 'm': .5})
    for i in range(int(s.L / SR * 1.2)):
        s.loop.add('wet', bubble(s.rng), s.rng.uniform(0, s.L / SR), pan=s.rng.uniform(-.8, .8), gain=s.rng.uniform(.3, 1))
    for i in range(int(s.L / SR / 4)):
        s.loop.add('wet', croak(s.rng), s.rng.uniform(0, s.L / SR), pan=s.rng.uniform(-.9, .9), gain=s.rng.uniform(.4, 1))
    for k, v in dict(drone=-21, murk=-24, wet=-29, reed=-18, perc=-26, pizz=-24).items():
        s.role(k, v)
    s.bus_fx('murk', chorus=5, rev=('hall', 0.5))
    s.bus_fx('reed', rev=('hall', 0.5), chorus=3)
    s.bus_fx('wet', rev=('room', 0.4))
    s.bus_fx('pizz', rev=('room', 0.3))
    s.bus_fx('perc', rev=('hall', 0.5))
    s.render(target_rms=-20.5, wow=(14, 2.5))


def t09():
    """«Затопленная деревня»: everything as if heard underwater. 60 bpm, D minor; music box, low bells, choir, bubbles, heavy wobble."""
    s = Song("09_zatoplennaya_derevnya", 60, 4, 18, seed=109)
    ch = prog([('Dm', 2), ('Bb', 2), ('Gm', 2), ('A7', 2), ('Dm', 1)], s.bars)
    sc = Scale('D', 'harmonic')
    s.pads('choir', ch, 'choir', center=57, n=3, vowel='o', a=2.5, r=3.5)
    s.melody('box', ch, sc, 'music_box', 74, 91, phrase_bars=2, plan='ABAC', start=0, durs=(0.5, 1, 1.5, 2), weights=(2, 3, 2, 1), end_rest=1.5)
    s.arp('bells', ch, lambda m, d, r: glass(m, 0.3, r, a=0.005, r=3.0), [0, None, None, None, 2, None, None, None], 0.5, lo=50, span=1)
    s.bassline('bass', ch, [(0, 0, 3.5)], octave=1, cutoff=200, saw=0.1, a=0.3, r=1.0)
    for i in range(int(s.L / SR * 2)):
        s.loop.add('wet', bubble(s.rng), s.rng.uniform(0, s.L / SR), pan=s.rng.uniform(-.8, .8), gain=s.rng.uniform(.2, 1))
    for k, v in dict(choir=-22, box=-18, bells=-23, bass=-23, wet=-30).items():
        s.role(k, v)
    s.bus_fx('box', rev=('hall', 0.7), chorus=6)
    s.bus_fx('bells', rev=('cave', 0.8))
    s.bus_fx('choir', rev=('cave', 0.7), chorus=5)

    def under(s_, mix):
        return 0.7 * clp(mix, 900, 2) + 0.3 * clp(stereo_chorus(mix, s_.rng, depth_ms=8, rate=0.25, mix=0.6), 1600)
    s.render(target_rms=-21.0, wow=(22, 4), post=under)


def t10():
    """«Дальнее кольцо»: far from the start, tension rising. Darkwave 104 bpm, A minor; 16th pulse bass, dark strings, hats, claps, brass stabs."""
    s = Song("10_dalnee_koltso", 104, 4, 32, seed=110)
    ch = prog([('Am', 2), ('Bb', 2), ('Am', 2), ('Gm', 1), ('Bb', 1)], s.bars)
    sc = Scale('A', 'phrygian')
    s.sections([(8, {'bass', 'pad'}), (8, {'bass', 'pad', 'hats', 'arp'}), (12, {'bass', 'pad', 'hats', 'arp', 'kick', 'lead'}),
                (4, {'bass', 'pad'})])
    s.bassline('bass', ch, [(b * 0.25, 12 if b % 4 == 2 else 0, 0.2) for b in range(16)],
               inst=lambda m, d, r: synth_pluck(m, d, r, lo=150, hi=1400, decay=0.08), octave=1)
    s.pads('pad', ch, 'strings', center=57, n=3, cutoff=1100, a=1.2, r=1.5)
    kick = 'k...k...k...k...'
    s.drums('kick', {'k': kick, 'c': '....c.......c...'}, vel={'k': 1, 'c': .6})
    s.drums('hats', {'h': 'h.hhh.hhh.hhh.hh', 'o': '..o...o...o...o.'}, vel={'h': .4, 'o': .3}, pans={'h': .3, 'o': -.2})
    s.arp('arp', ch, lambda m, d, r: synth_pluck(m, d, r, lo=600, hi=4000, decay=0.07, saw=False), [0, 2, 4, 2, 1, 3, 5, 3], 0.5, lo=69, span=2, pan=[-.5, .5])
    s.melody('lead', ch, sc, 'brass', 57, 72, phrase_bars=4, plan='AB', start=16, durs=(1, 2, 3), weights=(2, 2, 1), end_rest=2, flo=500, fhi=2500)
    for k, v in dict(bass=-20, pad=-25, kick=-20, hats=-27, arp=-25, lead=-19).items():
        s.role(k, v)
    s.bus_fx('pad', chorus=3, rev=('hall', 0.5), duck=(s.kick_times(kick, 16), 0.3))
    s.bus_fx('arp', delay=(0.75, 0.4, 0.3), rev=('hall', 0.3))
    s.bus_fx('lead', rev=('hall', 0.5))
    s.bus_fx('kick', rev=('room', 0.1))
    s.render(target_rms=-19.0, wow=(4, 1.5))


if __name__ == '__main__':
    for name in sys.argv[1:] or ['t06', 't07', 't08', 't09', 't10']:
        globals()[name]()
