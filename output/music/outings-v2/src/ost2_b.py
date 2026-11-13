"""Outing soundtrack v2, weird tracks 25-28."""
import sys
import numpy as np
from engine2 import *


def t25():
    """«Торговый центр под водой»: vaporwave. An 80s mall-pop tune played slow and low (90 bpm, F major), gated snare,
    slap bass, EP, brass stabs, synth hook — smeared with chorus, sunk under a lowpass and a long hall."""
    s = Song2("25_torgovyy_tsentr_pod_vodoy", 90, 4, 24, seed=125)
    ch = prog([('FM7', 2), ('Dm7', 2), ('BbM7', 2), ('C7sus4', 1), ('C', 1)], s.bars)
    s.sections([(4, {'ep', 'pad'}), (8, {'ep', 'pad', 'drums', 'bass', 'stab'}), (12, {'ep', 'pad', 'drums', 'bass', 'stab', 'hook', 'choir'})])
    s.pads('ep', ch, 'ep', center=62, n=4, legato=0.5, vel=0.8)
    s.pads('pad', ch, 'pad', center=57, n=4, cutoff=1600, a=0.8, r=2.0)
    s.kit('drums', {'k': "k.......k.k.....", 's': "....S.......S...", 'h': "h.h.h.h.h.h.h.h."})
    s.riff('bass', 'slap', "f1 . f2! . . f1 . c2 . f1 . . a1 . c2 . | d1 . d2! . . d1 . a1 . d1 . . f1 . a1 . | "
                           "bb1 . bb2! . . bb1 . f1 . bb1 . . d2 . f2 . | c1 . c2! . . c1 . g1 . c1 . . e1 . g1 .", vel=0.9)
    s.chords('stab', stab, ". . . . . . FM7 . . . . . . . . . | . . . . . . Dm7 . . . . . . . . . | . . . . . . BbM7 . . . . . . . . . | . . . . . . C . . . . . . . . . ",
             center=64, n=3, legato=0.6)
    s.riff('hook', lambda m, d, r: lead(m, d, r, cutoff=2600, sq=0.4), "a4 - - c5 - - f5 - e5 - - c5 - - a4 - | g4 - - - - - - - . . . . . . . . | "
           "f4 - - a4 - - d5 - c5 - - a4 - - g4 - | e4 - - - - - - - . . . . . . . .", vel=0.8)
    s.pads('choir', ch, 'choir', center=64, n=3, vowel='o', a=1.5, r=2.0)
    for k, v in dict(ep=-19, pad=-22, drums=-17, bass=-18, stab=-21, hook=-17, choir=-22).items():
        s.role(k, v)
    s.bus_fx('drums', rev=('plate', 0.35))
    s.bus_fx('hook', delay=(0.75, 0.4, 0.3), chorus=5)
    s.bus_fx('ep', chorus=6)
    s.bus_fx('choir', rev=('huge', 0.6))

    def sink(s_, mix):
        x = stereo_chorus(mix, s_.rng, depth_ms=7, rate=0.3, mix=0.5)
        x = clp(x, 2600, 2)
        ir = make_ir(np.random.RandomState(3), t60=5.0, damp=2000)
        return 0.7 * x + 0.5 * creverb(x, ir)
    render2(s, -18.5, wow=(20, 3), out_dir='ost2/out', post=sink)


def t26():
    """«Гоблинский лифт»: elevator bossa in E phrygian, 92 bpm. Harpsichord comping, vibes, Hammond pad, upright bass,
    rim clave, a floor ding, and a lift that sags in pitch now and then."""
    s = Song2("26_goblinskiy_lift", 92, 4, 24, seed=126)
    ch = prog([('Em9', 1), ('FM7', 1), ('Em9', 1), ('FM7', 1), ('Am9', 1), ('Bb7', 1), ('Em9', 1), ('F#m7b5', 1)], s.bars)
    s.sections([(4, {'harp', 'bass', 'perc', 'org'}), (20, {'harp', 'bass', 'perc', 'org', 'vib'})])
    hc = lambda ns, d, r: sum(harpsichord(m, d, r) for m in ns)
    s.chords('harp', hc, " ".join(["{c} . . {c} . . {c} . . . {c} . . {c} . .".format(c=c.name) for c in ch[:8]]), center=62, n=4, legato=0.6, vel=0.7)
    s.bassline('bass', ch, [(0, 0, 1.4), (1.5, 7, .4), (2, 7, 1.4), (3.5, 0, .4)], inst=lambda m, d, r: pluck(m, d, r, tau=0.8, pos=0.3, bright=0.6, damp=True), octave=2)
    s.kit('perc', {'i': ["i..i..i...i..i..", "..i..i..i...i..."], 'x': "x.xxx.xxx.xxx.xx"}, vel=0.6)
    s.pads('org', ch, 'organ', center=55, n=3, legato=0.98, vel=0.6)
    s.riff('vib', 'vibes', "b4 - - - d5 - - - f#5 - - - e5 - - - | e5 - - - c5 - - - a4 - - - . . . . | g4 - - - b4 - - - d5 - e5 - f#5 - - - | "
                           "e5 - - - - - - - . . . . . . . . | c5 - - - e5 - - - g5 - - - b5 - - - | ab5 - - - f5 - - - d5 - - - . . . . | "
                           "g5 - - - f#5 - - - e5 - - - b4 - - - | c5 - - - - - - - a4 - - - . . . .", vel=0.9)
    for b in (0, 12):
        s.hit('ding', glass(84, 0.2, s.rng, a=0.003, r=2.0), b * 4, vel=1.0)
        s.hit('ding', glass(80, 0.2, s.rng, a=0.003, r=2.0), b * 4 + 0.75, vel=1.0)
    for k, v in dict(harp=-19, bass=-20, perc=-25, org=-24, vib=-17, ding=-20).items():
        s.role(k, v)
    s.bus_fx('vib', rev=('room', 0.35))
    s.bus_fx('org', rev=('room', 0.3), chorus=3)
    s.bus_fx('ding', rev=('hall', 0.6))

    def sag(s_, mix):
        L = s_.L
        n = np.arange(L)
        d = np.zeros(L)
        for c in (3, 7):     # two slow sags per loop, whole cycles
            d += 400 * np.maximum(np.sin(2 * np.pi * c * n / L), 0) ** 8
        pos = (n - d) % L
        i0 = np.floor(pos).astype(int)
        fr = pos - i0
        x = mix[i0] * (1 - fr)[:, None] + mix[(i0 + 1) % L] * fr[:, None]
        return cbp(x, 120, 6500, 1)
    render2(s, -19.0, comp=False, wow=(10, 2), out_dir='ost2/out', post=sag, crackle_level=0.004)


def t27():
    """«Лиминальный коридор»: beatless. Flickering fluorescent hum, muzak bleeding through the walls, distant knocks in a huge
    space, a slowed choir that swells now and then. 60 bpm, C minor-ish."""
    s = Song2("27_liminalnyy_koridor", 60, 4, 18, seed=127)
    ch = prog([('CM9', 1), ('Am9', 1), ('Dm9', 1), ('G9', 1), ('EbM7', 1), ('Cm9', 1)], s.bars)
    s.pads('muz', ch, 'ep', center=62, n=4, legato=0.6, vel=0.8)
    s.riff('muz', 'vibes', "e5 - - g5 - - b5 - a5 - - - g5 - - - | . . . . c5 - - e5 - - d5 - - - . . ", vel=0.7)
    s.bassline('muz', ch, [(0, 0, 1.5), (2, 7, 1.5)], octave=2, cutoff=300, saw=0.1)
    for b in (2, 8, 14):
        for m in (55, 60, 63):
            s.note('choir', 'choir', m, b * 4, 8, 0.8, vowel='u', a=3.0, r=4.0, voices=4)
    for t in s.rng.uniform(0, s.L / SR, 7):
        s.loop.add('knock', tom(s.rng, 120, 70, 0.15), t, pan=s.rng.uniform(-.8, .8), gain=s.rng.uniform(.4, 1))
    s.hit('knock', ringtone(81, 0.15, s.rng) + 0, 9 * 4 + 1, vel=0.3)
    L = s.L
    n = np.arange(L)
    cyc = round(120 * L / SR)
    hum = np.zeros(L)
    for h, g in [(1, 1), (2, .5), (3, .35), (5, .2), (7, .1)]:
        hum += g * np.sin(2 * np.pi * cyc * h * n / L)
    flick = np.ones(L)
    for t in s.rng.uniform(0, L / SR, 10):
        a = int(t * SR)
        k = int(s.rng.uniform(0.05, 0.4) * SR)
        pat = (s.rng.rand(k // 400 + 1) > 0.5).repeat(400)[:k]
        flick[(a + np.arange(k)) % L] = 0.2 + 0.8 * pat
    s.loop.add_full('hum', np.stack([hum * flick, hum * flick * 0.9], 1))
    for k, v in dict(muz=-24, choir=-21, knock=-23, hum=-28).items():
        s.role(k, v)
    s.bus_fx('muz', bp=(160, 700), rev=('hall', 0.8))
    s.bus_fx('choir', rev=('huge', 0.9))
    s.bus_fx('knock', rev=('huge', 1.0), dry=0.4)
    s.bus_fx('hum', hp=100, lp=3000)
    render2(s, -20.5, comp=False, wow=(10, 2), out_dir='ost2/out')


def t28():
    """«Сломанный кассетник»: a swing jazz trio on a chewed tape. 88 bpm, Bb. Ride, walking bass, EP comping, vibes;
    constant dropouts, two tape stops, a rewind, heavy wow."""
    s = Song2("28_slomannyy_kassetnik", 88, 4, 24, seed=128, swing=0.17)
    ch = prog([('BbM7', 1), ('G7b9', 1), ('Cm7', 1), ('F7', 1), ('Dm7', 1), ('G7', 1), ('Cm7', 1), ('F7', 1)], s.bars)
    s.kit('drums', {'r': "r...r.r.r...r.r.", 'u': "....u.......u...", 'k': "k.........k....."}, vel=0.8)
    s.bassline('bass', ch, [(0, 0, .9), (1, 4, .9), (2, 7, .9), (3, 'n', .9)], inst=lambda m, d, r: pluck(m, d, r, tau=0.9, pos=0.3, bright=0.6, damp=True), octave=2)
    s.chords('ep', lambda ns, d, r: sum(fm_epiano(m, d, r, vel=0.6) for m in ns),
             " ".join(["{c} - - - - - {c} - - . . . . . . .".format(c=c.name) for c in ch[:8]]), center=62, n=4, legato=0.9)
    s.riff('vib', 'vibes', "f5 - - d5 - - bb4 - c5 - d5 - - - . . | b4 - - - ab4 - - - f4 - - - . . . . | eb5 - - c5 - - g4 - bb4 - c5 - - - . . | "
                           "a4 - - - c5 - - - eb5 - - - . . . . | f5 - - - d5 - - - a4 - - - . . . . | b4 - - - d5 - - - f5 - - - . . . . | "
                           "eb5 - - - g5 - - - c5 - - - bb4 - - - | a4 - - - - - - - . . . . . . . .", start_bar=8, vel=0.9)
    for k, v in dict(drums=-21, bass=-19, ep=-19, vib=-17).items():
        s.role(k, v)
    s.bus_fx('vib', rev=('room', 0.3))
    s.bus_fx('ep', rev=('room', 0.25))

    def chewed(s_, mix):
        x = mix.mean(1, keepdims=True) * np.array([[1.0, 0.95]]) * 0.6 + mix * 0.4
        x = cbp(x, 100, 6000, 1)
        x = drive(x * 1.3, 1.6)
        x = crush(x, 10, 2)
        x = dropout(x, s_.rng, list(s_.rng.uniform(1, s_.L / SR - 1, 16)), dur=0.18)
        x = tape_stop(x, s_.t(7 * 4 + 2), 1.5 * s_.Q, tail=0.3)
        x = rewind(x, s_.t(15 * 4 + 3), s_.Q)
        x = tape_stop(x, s_.t(23 * 4 + 1), 2.5 * s_.Q, tail=0.05)
        return x
    render2(s, -19.0, comp=False, wow=(30, 6), out_dir='ost2/out', post=chewed, crackle_level=0.01)


if __name__ == '__main__':
    for name in sys.argv[1:] or ['t25', 't26', 't27', 't28']:
        globals()[name]()
