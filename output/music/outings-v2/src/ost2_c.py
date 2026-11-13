"""Outing soundtrack v2, breakbeat tracks 29-34."""
import sys
import numpy as np
from engine2 import *

FUNKY = {'k': ["k.k.......k..k..", "k.k.......k....."],
         's': ["....s..g.g..s..g", "....s..g.g..s.g."],
         'h': ["h.h.h.h.h.h.h.o.", "h.h.h.h.h.h.h.h."]}
AMEN = {'k': ["k.k.......kk....", "k.k.......k....."],
        's': ["....s..g.g..s..g", "....s..g.g....s."],
        'h': ["h.h.h.h.h.h.h.h.", "h.h.h.h.h.h.h.o."]}
BIGBEAT = {'k': ["k.....k.k.......", "k.....k.k.....k."], 's': ["....S.......S...", "....S.......S.gg"], 'h': "h.hhh.hhh.hhh.hh"}

S0, S1 = straight(0), straight(1)


def edits(*pairs):
    return [list(p) for p in pairs]


def scratch_hits(s, layer, src, bars, kind='chirp', beat=3.0, dur_beats=1.0, rate=5.0, gain=1.0):
    for b in bars:
        s.hit(layer, scratch(src, dur_beats * s.Q, kind, rate=rate, depth=0.14), b * s.bpb + beat, vel=gain, pan=s.rng.uniform(-.3, .3))


def t29():
    """«Граффити на склепе»: JSR funk breakbeat. 104 bpm, E phrygian. Chopped funky break, slap bass, harpsichord hook,
    brass stabs, scratches, 'hey!'."""
    s = Song2("29_graffiti_na_sklepe", 104, 4, 32, seed=129)
    s.sections([(4, {'brk', 'harp'}), (12, {'brk', 'bass', 'harp', 'stab', 'fx'}), (4, {'harp', 'fx', 'bass'}),
                (12, {'brk', 'bass', 'harp', 'stab', 'fx'})])
    brk = s.make_break(FUNKY, 2, swing=0.12)
    v = S0[:12] + ['s12', 's12', 'r14', 15]
    s.chop('brk', brk, [S0, S1, S0, S1[:8] + [24, 25, 's26', 's26', 's28', 's28', 'r30', 31],
                        S0, S1, v, S1[:12] + ['s28', 's29', 's30', 's31']])
    s.riff('bass', 'slap', "e1 . e2! . . e1 g1 . a1 . e2! . g1 a1 b1 d2 | e1 . e2! . . e1 g1 . f1 - . e1 . . d2 e2!", vel=0.9)
    s.riff('harp', 'harpsichord', "e4 . f4 . g4 . f4 e4 . . b3 . c4 . . . | e4 . f4 . a4 . g4 f4 e4 - - - . . . . "
                                  "| e4 . f4 . g4 . f4 e4 . . b3 . c4 . d4 . | e4 . . b4 . a4 g4 f4 e4 - - - . . . .", pan=0.25)
    s.chords('stab', stab, ". . . . . . Em . . . . . . . F . | . . . . . . Em . . . Dm . . . . . ", center=64, n=3, legato=0.6)
    ah = vox('ah', 60, s.rng)
    scratch_hits(s, 'fx', ah, range(3, 32, 4), 'chirp', 3.0, 1.0, 6.0)
    for b in (4, 12, 20, 28):
        s.hit('fx', vox('hey', 64, s.rng, grit=0.3), b * 4, vel=1.0, pan=-0.2)
    s.hit('fx', vox('yeah', 59, s.rng, grit=0.3), 19 * 4 + 2, vel=1.0)
    for k, v in dict(brk=-15, bass=-17, harp=-19, stab=-20, fx=-19).items():
        s.role(k, v)
    s.bus_fx('harp', rev=('room', 0.35), delay=(0.75, 0.25, 0.15))
    s.bus_fx('stab', rev=('room', 0.3))
    s.bus_fx('fx', rev=('room', 0.3))
    render2(s, -16.5, wow=(4, 1.5), drive=1.6, out_dir='ost2/out')


def t30():
    """«Ведьмин бигбит»: big beat 128 bpm, A phrygian. Dirty breaks, acid line, siren, choir stabs, riser and drop."""
    s = Song2("30_vedmin_bigbit", 128, 4, 40, seed=130)
    s.sections([(8, {'brk', 'acid'}), (8, {'brk', 'acid', 'sub', 'siren'}), (2, {'acid', 'rise'}),
                (16, {'brk', 'acid', 'sub', 'choir', 'fx', 'rise'}), (6, {'brk', 'acid'})])
    brk = s.make_break(BIGBEAT, 2, crunch=True)
    brk = drive(brk * 1.5, 1.8)
    s.chop('brk', brk, [S0, S1, S0, S1, S0, S1, S0, S1[:8] + ['s24', 's24', 's26', 's26', 's28', 's28', 's30', 's30']])
    s.riff('acid', 'acid', "a1 a1 a2! a1 . a1 c2~ a1 | a1 . g1 a1 a2! . bb1~ a1 | a1 a1 a2! a1 . e2~ d2 c2 | a1 . g1 a1 c2! . bb1~ g1",
           cut=280, env=2200, res=7.0, vel=0.8)
    s.riff('sub', 'sub', "a1 - - - - - - - - - - - - - - - | f1 - - - - - - - - - - - g1 - - - ", vel=1.0)
    for b in range(8, 16, 2):
        s.hit('siren', siren(2 * s.bpb * s.Q, s.rng, f=700, depth=250, rate=1.5), b * 4, vel=0.6)
    s.hit('rise', riser(2 * s.bpb * s.Q, s.rng), 16 * 4, vel=1.0)
    for i in range(16):
        s.hit('rise', snare2(s.rng, tau=0.08), 16 * 4 + 4 + i * 0.25, vel=0.2 + 0.05 * i)
    s.hit('rise', impact(s.rng), 18 * 4, vel=1.0)
    for bar in range(18, 34):
        if bar % 2 == 0:
            for m in (69, 72, 76):
                s.note('choir', 'choir', m, bar * 4, 0.6, 0.8, vowel='a', a=0.01, r=0.4, voices=3)
            s.note('choir', 'choir', 70, bar * 4 + 2.5, 0.4, 0.6, vowel='o', a=0.01, r=0.4, voices=3)
    for b in (18, 26):
        s.hit('fx', vox('go', 57, s.rng, grit=0.8), b * 4 + 3.5, vel=1.0)
    for k, v in dict(brk=-15, acid=-18, sub=-18, siren=-26, rise=-19, choir=-20, fx=-19).items():
        s.role(k, v)
    s.bus_fx('acid', delay=(0.75, 0.3, 0.2), rev=('room', 0.2))
    s.bus_fx('choir', rev=('cave', 0.5))
    s.bus_fx('siren', rev=('hall', 0.5))
    s.bus_fx('fx', rev=('room', 0.3), delay=(0.5, 0.3, 0.25))
    render2(s, -16.0, wow=(3, 1), drive=1.8, out_dir='ost2/out')


def t31():
    """«Чёрный рейв»: jungle 172 bpm, D minor. Chopped amen, reese bass, church organ, ragga-ish shouts."""
    s = Song2("31_chernyy_reyv", 172, 4, 48, seed=131)
    s.sections([(8, {'organ', 'brkf'}), (16, {'brk', 'reese', 'organ', 'subb'}), (8, {'brk', 'fx', 'organ'}),
                (16, {'brk', 'reese', 'organ', 'fx', 'subb'})])
    brk = s.make_break(AMEN, 2, crunch=True)
    jungle = [S0, S1, S0[:8] + [8, 9, 's10', 's10', 12, 13, 'r14', 15], S1[:10] + ['s4', 's4', 's6', 's6', 's12', 's12'],
              S0, [16, 17, 2, 3, 20, 21, 6, 7, 8, 9, 26, 27, 12, 13, 30, 31], S0[:4] + ['r4', 'r5', 6, 7] + S1[8:],
              ['s0', 1, 's2', 3, 4, 5, 6, 7, 's8', 's8', 's8', 's8', 12, 's13', 's14', 's15']]
    s.chop('brk', brk, jungle)
    s.chop('brkf', brk, [S0, S1])
    s.riff('reese', 'reese', "d2 - - - - - - - - - - - f2 - e2 - | d2 - - - - - - - a1 - - - c2 - - - ", vel=1.0)
    s.riff('subb', 'sub', "d1 - - - - - - - - - - - f1 - e1 - | d1 - - - - - - - a1 - - - c1 - - - ", vel=1.0)
    s.chords('organ', lambda ns, d, r: sum(organ(m, d, r) for m in ns), "Dm - - - - - - - - - - - - - - - | Bb - - - - - - - Gm - - - A - - - ",
             center=60, n=4, legato=0.98)
    for b in range(24, 32):
        s.hit('fx', vox(['yo', 'oh', 'hey', 'woo'][b % 4], 62 + (b % 3) * 2, s.rng, speed=1.2, grit=0.4), b * 4 + (b % 2) * 1.5, vel=0.9)
    for b in (36, 44):
        s.hit('fx', vox('oh', 69, s.rng, glide=-5), b * 4 + 2, vel=0.8)
    for k, v in dict(brk=-15, brkf=-22, reese=-18, subb=-19, organ=-22, fx=-19).items():
        s.role(k, v)
    s.bus_fx('brkf', lp=900, rev=('room', 0.3))
    s.bus_fx('organ', rev=('cave', 0.5), chorus=2)
    s.bus_fx('fx', rev=('hall', 0.4), delay=(0.75, 0.35, 0.3))
    render2(s, -16.0, wow=(3, 1), drive=1.7, out_dir='ost2/out')


def t32():
    """«Скейт по крышкам гробов»: nu-skool breaks 135 bpm, F# minor. Electro bass, palm-muted guitar, scratches, glitches, choir."""
    s = Song2("32_skeyt_po_kryshkam", 135, 4, 36, seed=132)
    s.sections([(4, {'brk', 'bass'}), (12, {'brk', 'bass', 'gtr', 'fx'}), (4, {'choir', 'fx', 'bass'}), (16, {'brk', 'bass', 'gtr', 'fx', 'choir'})])
    pat = {'k': ["k.....k...k.....", "k.....k...k..k.."], 's': ["....S.......S...", "....S.......S..g"], 'h': ["h.h.h.h.h.h.h.h.", "hhh.h.hhh.h.h.oh"]}
    brk = s.make_break(pat, 2, crunch=True)
    s.chop('brk', brk, [S0, S1, S0, S1[:12] + ['s28', 's29', 's30', 's31'], S0, S1, S0[:8] + ['r8', 'r9', 'r10', 'r11'] + S0[12:], S1])
    s.riff('bass', 'acid', "f#1 . f#2 . . f#1 . a1 . b1 . f#1 c#2 . a1 . | f#1 . f#2 . . f#1 . e1 . e2 . e1 d2 . c#2 .", sq=True,
           cut=500, env=1500, res=2.5, decay=0.1, vel=0.9)
    s.riff('gtr', 'guitar', "f#2? f#2? . f#2? f#2? . a2 - f#2? f#2? . f#2? e2 - d2 - | f#2? f#2? . f#2? f#2? . c#3 - b2 - . a2 - - . .", vel=0.8)
    ah = vox('ah', 57, s.rng, grit=0.3)
    for b in range(4, 36, 2):
        if s.on('fx', b):
            kind = ['chirp', 'baby', 'transform', 'scribble'][(b // 2) % 4]
            s.hit('fx', scratch(ah, s.Q * 1.0, kind, rate=6, depth=0.12), b * 4 + 3, vel=0.9, pan=0.2)
    s.pads('choir', prog([('F#m', 2), ('D', 2), ('E', 2), ('C#m', 2)], s.bars), 'choir', center=62, n=3, vowel='o', a=1.0, r=1.5)
    for k, v in dict(brk=-15, bass=-17, gtr=-20, fx=-20, choir=-23).items():
        s.role(k, v)
    s.bus_fx('gtr', rev=('room', 0.2))
    s.bus_fx('choir', rev=('cave', 0.5))
    s.bus_fx('fx', rev=('room', 0.3))

    def glitchy(s_, mix):
        times = [s_.t(b * 4 + 3.5) for b in (7, 15, 23, 31)]
        return glitch(mix, s_.rng, times, slice_sec=s_.Q / 8, repeats=4)
    render2(s, -16.0, wow=(3, 1), drive=1.7, out_dir='ost2/out', post=glitchy)


def t33():
    """«Ню-метал гоблинов»: 98 bpm, drop D. Breakbeat drums, chugging guitar, DJ scratches, harpsichord intro, dark choir."""
    s = Song2("33_nyu_metal_goblinov", 98, 4, 28, seed=133)
    s.sections([(4, {'harp', 'fx', 'drumsl'}), (8, {'drums', 'bass', 'gtrm', 'fx'}), (8, {'drums', 'bass', 'gtr', 'choir', 'crash'}),
                (4, {'drums', 'fx', 'bass'}), (4, {'drums', 'bass', 'gtr', 'choir', 'crash'})])
    pat = {'k': ["k..k..k...k.....", "k..k..k...k..k.."], 's': "....S.......S...", 'h': "h.h.h.h.h.h.h.h."}
    s.kit('drums', pat)
    s.kit('drumsl', {'h': "h.h.h.h.h.h.h.h.", 'k': "k.......k......."}, vel=0.6)
    s.kit('crash', {'C': "C..............."})
    s.riff('gtrm', 'guitar', "d2? d2? d2? . d2? d2? . . d2? d2? d2? . f2? . g2? . | d2? d2? d2? . d2? d2? . . d2? . g#2? . g2? . f2? .", vel=0.7)
    s.riff('gtr', 'guitar', "d2 - - d2? d2? . f2 - - d2? d2? . g#2 - g2 - | d2 - - d2? d2? . f2 - - d2? c3 - - - bb2 - ", vel=0.9)
    s.riff('bass', 'sub', "d1 - - d1 d1 . f1 - - d1 d1 . g#1 - g1 - | d1 - - d1 d1 . f1 - - d1 c2 - - - bb1 - ", vel=1.0)
    s.riff('harp', 'harpsichord', "d4 . a4 . f4 . d4 . c#4 . a3 . d4 . e4 . | f4 . g4 . a4 . bb4 . a4 - - - . . . .")
    oh = vox('oh', 55, s.rng, grit=0.5)
    for b in list(range(0, 4)) + list(range(4, 12, 2)) + list(range(20, 24)):
        s.hit('fx', scratch(oh, s.Q * (2 if b >= 20 else 1), ['baby', 'chirp', 'transform', 'scribble'][b % 4], rate=5, depth=0.15),
              b * 4 + (2 if b >= 20 else 3), vel=1.0, pan=s.rng.uniform(-.4, .4))
    s.pads('choir', prog([('Dm', 2), ('Bb', 1), ('A', 1)], s.bars), 'choir', center=62, n=3, vowel='a', a=0.5, r=1.0)
    for k, v in dict(drums=-15, drumsl=-20, crash=-24, gtr=-16, gtrm=-17, bass=-18, harp=-18, fx=-18, choir=-22).items():
        s.role(k, v)
    s.bus_fx('drums', rev=('room', 0.2))
    s.bus_fx('gtr', rev=('room', 0.15))
    s.bus_fx('choir', rev=('cave', 0.6))
    s.bus_fx('harp', rev=('hall', 0.4))
    render2(s, -15.5, wow=(2, 1), drive=1.8, out_dir='ost2/out')


def t34():
    """«Двухшаговый некромант»: UK garage 2-step 132 bpm, C minor. Shuffled drums, organ bass, pitched vocal chops, harpsichord stabs."""
    s = Song2("34_dvuhshagovyy_nekromant", 132, 4, 36, seed=134, swing=0.14)
    s.sections([(4, {'drums', 'harp'}), (12, {'drums', 'bass', 'harp', 'vox'}), (4, {'harp', 'vox'}), (16, {'drums', 'bass', 'harp', 'vox', 'pad'})])
    s.kit('drums', {'k': ["k.......k.k.....", "k......k..k....."], 's': "....s.......s...", 'h': ".hh.hh.h.hh.hh.h", 'i': ["..i.....i.....i.", "......i.....i..."]})
    s.riff('bass', lambda m, d, r: drive(organ(m, d, r, bars=(1, .5, .2, .1, .6)), 1.6), "c2 . . c2 . . eb2 . . c2 . . g1 . bb1 . | c2 . . c2 . . f2 - eb2 . . c2 . . g1 .", vel=1.0)
    s.chords('harp', lambda ns, d, r: sum(harpsichord(m, d, r) for m in ns),
             "Cm9 . . Cm9 . . . . . . Cm9 . . . . . | Fm9 . . Fm9 . . . . AbM7 . . . G7 . . .", center=64, n=4, legato=0.5)
    words = ['oh', 'yeah', 'oh', 'hi', 'woo', 'yeah']
    for b in range(4, 36):
        if not s.on('vox', b):
            continue
        for k, beat in enumerate([1.5, 2.75] if b % 2 else [0.75, 3.25]):
            w = words[(b + k) % len(words)]
            m = [75, 72, 79, 70][(b + k) % 4]
            s.hit('vox', vox(w, m, s.rng, speed=1.4, glide=(-2 if k else 2)), b * 4 + beat, vel=0.8, pan=(-.35 if k else .35))
    s.pads('pad', prog([('Cm9', 2), ('Fm9', 1), ('G7', 1)], s.bars), 'pad', center=60, n=4, cutoff=1800, a=0.5, r=1.0)
    for k, v in dict(drums=-15, bass=-17, harp=-19, vox=-19, pad=-24).items():
        s.role(k, v)
    s.bus_fx('vox', delay=(0.75, 0.35, 0.3), rev=('hall', 0.4))
    s.bus_fx('harp', rev=('room', 0.3))
    s.bus_fx('pad', chorus=3, rev=('hall', 0.4))
    render2(s, -16.0, wow=(3, 1), drive=1.6, out_dir='ost2/out')


if __name__ == '__main__':
    for name in sys.argv[1:] or ['t29', 't30', 't31', 't32', 't33', 't34']:
        globals()[name]()
