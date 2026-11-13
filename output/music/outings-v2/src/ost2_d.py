"""Outing soundtrack v2, breakbeat tracks 35-40."""
import sys
import numpy as np
from engine2 import *
from ost2_c import S0, S1, AMEN, FUNKY, scratch_hits


def t35():
    """«Кассета из-под подушки»: JSR hip-hop 92 bpm, G minor. Swung boom-bap break, wah guitar, upright-ish bass,
    dark toy-piano hook, scratches, 'yo'."""
    s = Song2("35_kasseta_iz_pod_podushki", 92, 4, 24, seed=135, swing=0.1)
    s.sections([(4, {'brk', 'toy'}), (8, {'brk', 'bass', 'wah', 'toy', 'fx'}), (4, {'brk', 'bass', 'fx'}), (8, {'brk', 'bass', 'wah', 'toy', 'fx'})])
    pat = {'k': ["k.......k.k.....", "k.......k..k..k."], 's': ["....s.......s...", "....s..g....s..g"], 'h': "h.h.h.h.h.h.h.hh"}
    brk = s.make_break(pat, 2, swing=0.1, crunch=True)
    s.chop('brk', brk, [S0, S1, S0, S1[:12] + ['s28', 's28', 30, 31]])
    s.riff('bass', 'slap', "g1 . . g1 . bb1 . c2 . d2 . . f1 . g1 . | g1 . . g1 . bb1 . c2 . f1 . . d1 . f1 .", vel=0.9)
    s.chords('wah', lambda ns, d, r: wah_guitar(ns, d, r), ". . Gm7 . . . Gm7 . . . Gm7 . . Gm7 . . | . . C9 . . . C9 . . . C9 . . Eb . .",
             center=62, n=4, legato=0.5, pan=-0.3)
    s.riff('toy', 'toy', "d5 . bb4 . g4 . a4 bb4 . . f4 . g4 . . . | d5 . bb4 . g4 . a4 bb4 . . c5 . d5 . . .", pan=0.3)
    ah = vox('ah', 58, s.rng, grit=0.2)
    scratch_hits(s, 'fx', ah, range(3, 24, 4), 'baby', 3.0, 1.0, 4.0)
    for b in (4, 16):
        s.hit('fx', vox('yo', 55, s.rng, grit=0.3), b * 4, vel=1.0)
    for k, v in dict(brk=-15, bass=-17, wah=-20, toy=-19, fx=-19).items():
        s.role(k, v)
    s.bus_fx('toy', rev=('room', 0.4), delay=(0.75, 0.3, 0.2))
    s.bus_fx('wah', rev=('room', 0.25))
    render2(s, -16.5, wow=(6, 2), drive=1.5, out_dir='ost2/out', crackle_level=0.006)


def t36():
    """«Гоп-рейв на погосте»: electro breaks 140 bpm, E minor. 808 kick and cowbell, sub, square acid, robot voice, modem."""
    s = Song2("36_gop_reyv_na_pogoste", 140, 4, 36, seed=136)
    s.sections([(4, {'drums', 'acid'}), (12, {'drums', 'acid', 'sub', 'lead', 'robot'}), (4, {'acid', 'robot', 'fx'}),
                (16, {'drums', 'acid', 'sub', 'lead', 'robot', 'fx'})])
    s.kit('drums', {'b': ["b......b..b.....", "b......b..b..b.."], 'c': "....c.......c...", 'h': "h.hhh.hhh.hhh.hh",
                    'w': [".......w..w....w", "..w.....w..w...."]})
    s.riff('acid', 'acid', "e2 . e2 e3! . e2 . g2~ | . e2 b2~ . a2 . g2 . | e2 . e2 e3! . e2 . d3~ | . b2 . g2 . a2~ b2 .", sq=True,
           cut=350, env=2400, res=5.0, decay=0.12, vel=0.7)
    s.riff('sub', 'sub', "e1 - - - - - - e1 - - e1 - - - - - | c1 - - - - - - c1 - - d1 - - - - - ", vel=1.0)
    s.riff('lead', lambda m, d, r: chip(m, d, r, width=0.25, vib=0.01), "e5 - - - g5 - - - f#5 - e5 - d5 - b4 - | e5 - - - . . b4 - c5 - b4 - a4 - g4 - ", vel=0.6)
    words = ['hi', 'oh', 'hey', 'oh']
    for b in range(4, 36, 2):
        if s.on('robot', b):
            y = vox(words[(b // 2) % 4], 52, s.rng, speed=0.9, breath=0.0, grit=0.9)
            s.hit('robot', crush(y, 6, 4), b * 4 + 3, vel=0.9, pan=0.2)
    for b in (16, 17, 18, 19, 27, 35):
        s.hit('fx', modem(s.rng, 0.8), b * 4 + 1, vel=0.6, pan=s.rng.uniform(-.5, .5))
    for k, v in dict(drums=-15, acid=-18, sub=-17, lead=-20, robot=-20, fx=-24).items():
        s.role(k, v)
    s.bus_fx('acid', delay=(0.75, 0.3, 0.2))
    s.bus_fx('lead', delay=(0.75, 0.35, 0.25), rev=('hall', 0.3))
    s.bus_fx('robot', rev=('room', 0.3))
    s.bus_fx('fx', bp=(600, 3500))
    render2(s, -16.0, wow=(2, 1), drive=1.6, out_dir='ost2/out')


def t37():
    """«Пиксельный шабаш»: chiptune breakcore 165 bpm, A harmonic minor. Shredded amen, 8-bit lead and bass, choir underneath."""
    s = Song2("37_pikselnyy_shabash", 165, 4, 40, seed=137)
    s.sections([(4, {'lead', 'cbass'}), (12, {'brk', 'lead', 'cbass', 'arp'}), (8, {'brk', 'cbass', 'choir', 'arp'}),
                (16, {'brk', 'lead', 'cbass', 'arp', 'choir'})])
    brk = s.make_break(AMEN, 2, crunch=True)
    brk = crush(brk, 8, 3)
    core = [S0, S1[:8] + ['s24', 's24', 's24', 's24', 's28', 's28', 's28', 's28'], [0, 's1', 2, 's3', 4, 5, 's6', 's6', 's8', 9, 10, 's11', 12, 's13', 's14', 's15'],
            S1[:4] + ['r20', 'r21', 'r22', 'r23'] + S0[8:], S0, ['s0'] * 4 + ['s4'] * 4 + ['s8'] * 4 + ['s12', 's13', 's14', 's15'], S0[:12] + ['r12', 'r13', 'r14', 'r15'], S1]
    s.chop('brk', brk, core)
    s.riff('lead', lambda m, d, r: chip(m, d, r, width=0.25, vib=0.012),
           "a4 . c5 . e5 . a5 . g#5 . e5 . c5 . b4 . | a4 - - - . . e4 . f4 . e4 . d4 . c4 . | "
           "a4 . c5 . e5 . a5 . b5 . g#5 . e5 . f5 . | e5 - - - d5 - c5 - b4 - g#4 - a4 - - - ", vel=0.6)
    s.riff('cbass', lambda m, d, r: chip(m, d, r, width=0.5), "a2 a3 a2 a3 a2 a3 a2 a3 f2 f3 f2 f3 f2 f3 f2 f3 | d2 d3 d2 d3 d2 d3 d2 d3 e2 e3 e2 e3 e2 e3 e2 e3", vel=0.5)
    s.arp('arp', prog([('Am', 1), ('F', 1), ('Dm', 1), ('E', 1)], s.bars), lambda m, d, r: chip(m, d, r, width=0.125), [0, 1, 2, 3, 2, 1], 0.125,
          lo=69, span=2, vel=0.35)
    s.pads('choir', prog([('Am', 1), ('F', 1), ('Dm', 1), ('E', 1)], s.bars), 'choir', center=60, n=3, vowel='a', a=0.3, r=0.6)
    for k, v in dict(brk=-15, lead=-18, cbass=-19, arp=-24, choir=-22).items():
        s.role(k, v)
    s.bus_fx('lead', delay=(0.75, 0.3, 0.2), rev=('room', 0.2))
    s.bus_fx('arp', rev=('room', 0.3))
    s.bus_fx('choir', rev=('cave', 0.5))
    render2(s, -16.0, wow=(2, 1), drive=1.7, out_dir='ost2/out')


def t38():
    """«Бешеная тыква»: brass funk breakbeat 112 bpm, D dorian. Funky break with tambourine, horn section riffs, wah, slap, Hammond, shouts."""
    s = Song2("38_beshenaya_tykva", 112, 4, 32, seed=138)
    s.sections([(4, {'brk', 'wah', 'bass'}), (12, {'brk', 'wah', 'bass', 'horns', 'org', 'fx'}), (4, {'brk', 'bass', 'org', 'fx'}),
                (12, {'brk', 'wah', 'bass', 'horns', 'org', 'fx'})])
    pat = dict(FUNKY)
    pat['t'] = "..t...t...t...t."
    brk = s.make_break(pat, 2, swing=0.08)
    s.chop('brk', brk, [S0, S1, S0, S1[:12] + ['s28', 's29', 's30', 's31']])
    s.riff('horns', lambda m, d, r: stab([m, m + 12], d, r, bright=3800),
           "d4! . . f4 . g4 . a4! . . c5 - a4 . g4 . | f4 . d4 . . . . . . . . . a4! - g4 - | d4! . . f4 . g4 . a4! . . c5 - d5 - . . | c5 . a4 . g4 . f4 . d4 - - - . . . .",
           vel=0.9)
    s.chords('wah', lambda ns, d, r: wah_guitar(ns, d, r, f0=400, f1=2600, decay=0.09),
             "Dm7 . Dm7 Dm7 . Dm7 . Dm7 . Dm7 Dm7 . G9 . G9 . ", center=62, n=4, legato=0.4, pan=-0.35)
    s.riff('bass', 'slap', "d1 . d2! . . d1 . f1 g1 . d2! . c2 . a1 . | d1 . d2! . . d1 . f1 g1 . g2! . f2 . e2 .", vel=0.9)
    s.chords('org', lambda ns, d, r: sum(hammond(m, d, r) for m in ns), "Dm7 - - - - - . . . . . . G7 - . . ", center=60, n=4, legato=0.95, pan=0.3)
    for b in (4, 8, 12, 20, 24, 28):
        s.hit('fx', vox('hey' if b % 8 == 4 else 'ha', 60, s.rng, grit=0.4), b * 4 + (0 if b % 8 == 4 else 3.5), vel=1.0)
    for b in (16, 18):
        s.hit('fx', vox('woo', 67, s.rng, glide=4), b * 4 + 1, vel=0.9)
    for k, v in dict(brk=-15, horns=-17, wah=-20, bass=-17, org=-22, fx=-19).items():
        s.role(k, v)
    s.bus_fx('horns', rev=('room', 0.35))
    s.bus_fx('org', rev=('room', 0.3), chorus=2)
    s.bus_fx('fx', rev=('room', 0.35))
    render2(s, -16.0, wow=(4, 1.5), drive=1.6, out_dir='ost2/out')


def t39():
    """«Туман на скорости»: dark drum'n'bass 174 bpm, F minor. Two-step beat over a ghost break, reese, sub, harp, choir."""
    s = Song2("39_tuman_na_skorosti", 174, 4, 48, seed=139)
    s.sections([(8, {'harp', 'choir', 'ghost'}), (16, {'drums', 'ghost', 'reese', 'sub', 'harp', 'choir'}), (8, {'harp', 'choir', 'sub', 'ghost'}),
                (16, {'drums', 'ghost', 'reese', 'sub', 'harp', 'choir'})])
    s.kit('drums', {'k': ["k.........k.....", "k.........k..k.."], 's': "....S.......S...", 'h': ".h.h.h.h.h.h.h.h"})
    brk = s.make_break(AMEN, 2, crunch=True)
    s.chop('ghost', brk, [S0, S1])
    s.riff('reese', 'reese', "f1 - - - - - - - - - - - ab1 - g1 - | f1 - - - - - - - db1 - - - eb1 - - - ", vel=1.0, cutoff=600)
    s.riff('sub', 'sub', "f1 - - - - - - - - - - - ab1 - g1 - | f1 - - - - - - - db1 - - - eb1 - - - ", vel=1.0)
    ch = prog([('Fm9', 2), ('DbM7', 1), ('Eb', 1)], s.bars)
    s.arp('harp', ch, 'harp', [0, 2, 4, 3, 1, 3, 5, 4], 0.25, lo=65, span=2, pan=[-.4, .4], vel=0.7)
    s.pads('choir', ch, 'choir', center=60, n=3, vowel='o', a=1.5, r=2.5)
    for k, v in dict(drums=-15, ghost=-22, reese=-18, sub=-18, harp=-21, choir=-22).items():
        s.role(k, v)
    s.bus_fx('ghost', hp=400, lp=8000, rev=('room', 0.2))
    s.bus_fx('harp', delay=(0.75, 0.35, 0.25), rev=('hall', 0.5))
    s.bus_fx('choir', rev=('cave', 0.6))
    render2(s, -16.0, wow=(3, 1), drive=1.6, out_dir='ost2/out')


def t40():
    """«Сигнал из колодца»: «Странное» meets breaks, 118 bpm. A payphone muzak bed chopped in 16ths over a funky break,
    DTMF, modem scratches, a tape stop and a rewind."""
    s = Song2("40_signal_iz_kolodtsa", 118, 4, 32, seed=140)
    s.sections([(4, {'muzraw'}), (12, {'muzraw', 'brk', 'bass', 'fx'}), (4, {'muzraw', 'fx'}), (12, {'muzraw', 'brk', 'bass', 'fx'})])
    ch = prog([('CM9', 1), ('Am9', 1), ('Dm9', 1), ('G9', 1), ('EM7', 1), ('Am9', 1), ('Fm9', 1), ('G7sus4', 1)], s.bars)
    s.pads('muzraw', ch, 'ep', center=62, n=4, legato=0.95, vel=0.8)
    s.riff('muzraw', 'vibes', "e5 - - g5 - - e5 - d5 - c5 - a4 - - - | c5 - - e5 - - d5 - c5 - a4 - g4 - - - | "
                              "g#5 - - e5 - - b4 - c5 - e5 - a5 - - - | ab5 - - f5 - - c5 - d5 - - - . . . .", vel=0.8)
    brk = s.make_break(FUNKY, 2, swing=0.1, crunch=True)
    s.chop('brk', brk, [S0, S1, S0, S1[:12] + ['s28', 's28', 'r30', 'r31']])
    s.riff('bass', 'sub', "c2 . . c2 . . . . a1 . . a1 . . c2 . | d2 . . d2 . . . . g1 . . g1 . . b1 . | e2 . . e2 . . . . a1 . . a1 . . c2 . | f1 . . f1 . . . . g1 . . g1 . . . .", vel=0.9)
    md = modem(s.rng, 1.5)
    for b in range(5, 32, 4):
        if s.on('fx', b):
            s.hit('fx', scratch(md, s.Q, 'chirp', rate=6, depth=0.2), b * 4 + 3, vel=0.7)
    for b, digits in ((16, '5551'), (18, '0666'), (24, '1984')):
        for k, d in enumerate(digits):
            s.hit('fx', dtmf(d), b * 4 + k * 0.5, vel=0.5)
    for k, v in dict(muz=-17, brk=-15, bass=-17, fx=-21).items():
        s.role(k, v)
    s.bus_fx('fx', bp=(500, 3500))

    def extras(s_, layers):
        raw = layers.pop('muzraw')
        raw = cbp(raw, 330, 3200, 2) + 0.15 * raw
        # rhythmic gate over the muzak in 16ths (on from bar 4), a stutter on bar ends
        L = s_.L
        g = np.ones(L)
        gate = "x.xx.x.xx.x.xxx."
        step = s_.Q / 4
        for bar in range(4, s_.bars):
            if 16 <= bar < 20:
                continue
            for i, c in enumerate(gate):
                if c == '.':
                    a = int((bar * 4 * s_.Q + i * step) * SR)
                    n = int(step * SR)
                    w = np.ones(n)
                    k = int(0.003 * SR)
                    w[:k] = np.linspace(1, 0.15, k)
                    w[k:-k] = 0.15
                    w[-k:] = np.linspace(0.15, 1, k)
                    g[(a + np.arange(n)) % L] = w
        layers['muz'] = glitch(raw * g[:, None], s_.rng, [s_.t(b * 4 + 3.5) for b in (7, 11, 23, 27)], slice_sec=step / 2, repeats=4)
        return layers

    def post(s_, mix):
        mix = tape_stop(mix, s_.t(15 * 4 + 2), 2 * s_.Q, tail=0.05)
        mix = rewind(mix, s_.t(31 * 4 + 3), s_.Q * 0.9)
        return mix
    render2(s, -16.5, wow=(14, 3), drive=1.5, out_dir='ost2/out', extras=extras, post=post, crackle_level=0.006)


if __name__ == '__main__':
    for name in sys.argv[1:] or ['t35', 't36', 't37', 't38', 't39', 't40']:
        globals()[name]()
