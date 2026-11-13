"""Outing soundtrack v2, weird tracks 21-24 (the «Странное» direction)."""
import sys
import numpy as np
from engine2 import *


def pa(s, mix, lo=380, hi=3800, room='hall', wet=0.45):
    x = cbp(mix, lo, hi, 2) * 1.3 + 0.1 * mix
    ir = make_ir(np.random.RandomState(7), t60={'hall': 3.0, 'cave': 5.0, 'room': 1.2}[room], damp=3000)
    return x + wet * creverb(x, ir)


def chime(rng, notes=(77, 73, 70), gap=0.45):
    out = np.zeros(int((gap * len(notes) + 3.0) * SR))
    for k, m in enumerate(notes):
        y = glass(m, 0.25, rng, a=0.004, r=2.5)
        p = int(k * gap * SR)
        out[p:p + len(y)] += y[:len(out) - p]
    return out


def babble(rng, secs, base=50):
    """Distant announcer: vowel-ish syllables with no words."""
    parts = []
    t = 0.0
    words = list(WORDS.keys())
    while t < secs:
        y = vox(words[rng.randint(len(words))], base + rng.randint(-2, 3), rng, speed=rng.uniform(1.6, 2.4), breath=0.05)
        parts.append(y)
        parts.append(np.zeros(int(rng.uniform(0.02, 0.12) * SR)))
        t += (len(y) + len(parts[-1])) / SR
    return np.concatenate(parts)[:int(secs * SR)]


def t21():
    """«Зал ожидания»: bossa from the station loudspeakers. 84 bpm, F major with a dark bII. Nylon guitar, vibes, rim clave,
    announcement chimes and a wordless announcer, all through the PA."""
    s = Song2("21_zal_ozhidaniya", 84, 4, 24, seed=121)
    ch = prog([('FM7', 1), ('GbM7', 1), ('Gm7', 1), ('C9', 1), ('Am7', 1), ('D7b9', 1), ('Gm9', 1), ('Db7', 1)], s.bars)
    s.sections([(8, {'gtr', 'bass', 'perc', 'ann'}), (16, {'gtr', 'bass', 'perc', 'vib', 'ann'})])
    nylon = lambda ns, d, r: sum(pluck(m, d, r, tau=1.4, pos=0.2, bright=0.8, damp=True) for m in ns)
    s.chords('gtr', nylon, " ".join(["{c} . . {c} . . {c} . . . {c} . . {c} . .".format(c=c.name) for c in ch[:8]]), center=60, n=4, legato=0.8, vel=0.8)
    s.bassline('bass', ch, [(0, 0, 1.4), (1.5, 7, .4), (2, 7, 1.4), (3.5, 0, .4)], inst=lambda m, d, r: pluck(m, d, r, tau=0.8, pos=0.3, bright=0.6, damp=True), octave=2)
    s.kit('perc', {'i': ["i..i..i...i..i..", "..i..i..i...i..."], 'x': "xxxxxxxxxxxxxxxx"}, vel=0.6)
    s.riff('vib', 'vibes', "a4 - - - c5 - - - e5 - - - d5 - c5 - | bb4 - - - - - - - f4 - - - . . . . | d5 - - - bb4 - - - g4 - a4 - bb4 - - - | "
                           "e5 - - - d5 - - - c5 - - - . . . . | c5 - - - e5 - - - g5 - - - e5 - - - | f#5 - - - eb5 - - - c5 - - - a4 - - - | "
                           "a4 - - - bb4 - - - d5 - - - c5 - - - | b4 - - - ab4 - - - f4 - - - . . . .", vel=0.9)
    for b in (0, 12):
        s.hit('ann', chime(s.rng), b * 4, vel=1.0)
    for b in (2, 14):
        s.hit('ann', babble(s.rng, 5.0), b * 4 + 0.5, vel=0.7)
    for k, v in dict(gtr=-19, bass=-20, perc=-25, vib=-17, ann=-19).items():
        s.role(k, v)
    s.bus_fx('vib', rev=('room', 0.3))
    s.bus_fx('gtr', rev=('room', 0.25))
    render2(s, -19.5, comp=False, wow=(12, 2.5), out_dir='ost2/out', crackle_level=0.004, post=lambda s_, m: pa(s_, m))


def t22():
    """«Автоответчик»: lounge on an answering-machine cassette. 76 bpm, Eb. Beep, a dialled number, EP, muted horn,
    brushes; dropouts, a rewind and a tape stop at the loop's end."""
    s = Song2("22_avtootvetchik", 76, 4, 20, seed=122, swing=0.12)
    ch = prog([('EbM9', 1), ('Cm9', 1), ('Fm9', 1), ('Bb9', 1), ('Gm7', 1), ('C7b9', 1), ('Fm9', 1), ('Bb7sus4', 1)], s.bars)
    s.pads('ep', ch, 'ep', center=62, n=4, legato=0.55, vel=0.8)
    for bar in range(s.bars):
        s.note('ep', 'ep', ch[bar].voicing(62, 4)[-1], bar * 4 + 2.5, 1.2, 0.5)
    s.bassline('bass', ch, [(0, 0, .9), (1, 7, .9), (2, 12, .9), (3, 'n', .9)], octave=2, cutoff=500, saw=0.15)
    s.kit('drums', {'u': ["....u.......u..."], 'x': "x.x.x.x.x.x.x.x.", 'k': "k.......k......."}, vel=0.7)
    horn = lambda m, d, r: lead(m, d, r, cutoff=1500, sq=0.3, vib=0.006, a=0.04)
    s.riff('horn', horn, "g4 - - bb4 - c5 - - eb5 - - - d5 - c5 - | bb4 - - - g4 - - - . . . . . . . . | "
                         "c5 - - eb5 - f5 - - g5 - - - f5 - eb5 - | d5 - - - bb4 - - - . . . . . . . .", start_bar=4, vel=0.9)
    s.hit('fx', np.sin(TWO_PI * 1000 * tvec(int(0.45 * SR))) * 0.5, 0.0, vel=1.0)
    for k, d in enumerate('5550173'):
        s.hit('fx', dtmf(d, 0.1), 1.0 + k * 0.4, vel=0.6)
    for k, v in dict(ep=-19, bass=-20, drums=-24, horn=-17, fx=-20).items():
        s.role(k, v)
    s.bus_fx('ep', chorus=4, rev=('room', 0.3))
    s.bus_fx('horn', rev=('room', 0.35))

    def cassette(s_, mix):
        x = cbp(mix, 90, 7000, 1)
        x = drive(x * 1.3, 1.5)
        x = dropout(x, s_.rng, list(s_.rng.uniform(4, s_.L / SR - 4, 9)), dur=0.15)
        x = rewind(x, s_.t(10 * 4 + 1), s_.Q * 1.2)
        x = tape_stop(x, s_.t(19 * 4 + 2), 1.8 * s_.Q, tail=0.02)
        return x
    render2(s, -19.0, comp=False, wow=(18, 4), out_dir='ost2/out', crackle_level=0.006, post=cassette)


def t23():
    """«Канал 0»: channel surfing. 100 bpm; six 4-bar channels — weather muzak, a church, a cartoon polka, a dark
    harpsichord, a breakbeat jingle, the weather again — with static and clicks between, all through a TV speaker."""
    s = Song2("23_kanal_0", 100, 4, 24, seed=123)
    s.sections([(4, {'w_ep', 'w_bass', 'w_vib'}), (4, {'c_choir', 'c_org'}), (4, {'t_toy', 't_tuba'}), (4, {'h_harp'}),
                (4, {'j_brk', 'j_chip', 'j_bass'}), (4, {'w_ep', 'w_bass', 'w_vib'})])
    wch = prog([('CM9', 1), ('Am9', 1), ('Dm9', 1), ('G9', 1)], s.bars)
    s.pads('w_ep', wch, 'ep', center=62, n=4, legato=0.6, vel=0.8)
    s.bassline('w_bass', wch, [(0, 0, 1.4), (2, 7, 1.4)], octave=2, cutoff=400, saw=0.15)
    s.riff('w_vib', 'vibes', "e5 - - - g5 - - - b5 - a5 - g5 - - - | c5 - - - e5 - - - g5 - - - . . . . | f5 - - - a5 - - - c6 - b5 - a5 - - - | d5 - - - - - - - . . . . . . . .", vel=0.8)
    cch = prog([('Cm', 1), ('Ab', 1), ('Fm', 1), ('G', 1)], s.bars)
    s.pads('c_choir', cch, 'choir', center=60, n=4, vowel='a', a=0.6, r=1.5, voices=4)
    s.pads('c_org', cch, 'organ', center=48, n=3, legato=0.98)
    s.riff('t_toy', 'toy', "c5 . e5 . g5 . e5 . f5 . d5 . b4 . g4 . | c5 . e5 . g5 . c6 . b5 . g5 . f5 . d5 .", vel=0.9)
    s.riff('t_tuba', lambda m, d, r: tuba(m, d, r, slide=2), "c2 . . . g1 . . . g1 . . . d2 . . . | c2 . . . g1 . . . c2 . . . g1 . . .")
    s.riff('h_harp', 'harpsichord', "a4 c5 e5 a5 e5 c5 a4 c5 d5 f5 a5 d6 a5 f5 d5 f5 | g#4 b4 e5 g#5 e5 b4 g#4 b4 a4 c5 e5 a5 e5 c5 a4 e4")
    brk = s.make_break({'k': "k.....k.k.......", 's': "....s.......s...", 'h': "h.h.h.h.h.h.h.h."}, 1)
    s.chop('j_brk', brk, [straight(0)])
    s.riff('j_chip', lambda m, d, r: chip(m, d, r, width=0.25, vib=0.01), "f5 . a5 . c6 - a5 . bb5 . g5 . e5 - c5 . | f5 . a5 . c6 - d6 . c6 - a5 - f5 - - -", vel=0.6)
    s.riff('j_bass', 'sub', "f1 . . f1 . . f2 . c1 . . c1 . . c2 . ")
    for k, v in dict(w_ep=-19, w_bass=-20, w_vib=-17, c_choir=-18, c_org=-21, t_toy=-17, t_tuba=-19, h_harp=-16,
                     j_brk=-16, j_chip=-18, j_bass=-19).items():
        s.role(k, v)
    s.bus_fx('c_choir', rev=('cave', 0.7))
    s.bus_fx('c_org', rev=('cave', 0.5))
    s.bus_fx('h_harp', rev=('room', 0.3))

    def tv(s_, mix):
        L = s_.L
        rng = s_.rng
        for b in range(0, s_.bars, 4):
            t0 = s_.t(b * 4)
            n = int(rng.uniform(0.2, 0.35) * SR)
            st = static(n, rng) * 0.1
            st[:50] += 0.25
            idx = (int((t0 - 0.12) * SR) + np.arange(n)) % L
            mix[idx] = mix[idx] * 0.15 + st[:, None]
        x = cbp(mix, 160, 7000, 2)
        x = crush(x, 10, 2)
        hum = np.sin(TWO_PI * round(15734 * L / SR) * np.arange(L) / L)[:, None] * 0.002
        return x + hum
    render2(s, -18.5, comp=True, wow=(6, 2), out_dir='ost2/out', post=tv)


def t24():
    """«Мелодия звонка»: a dark baroque tune as a 2000s polyphonic ringtone with a phone vibrating, then a lo-fi break joins,
    and at last the same tune on a real harpsichord with a choir. 96 bpm, D minor."""
    s = Song2("24_melodiya_zvonka", 96, 4, 32, seed=124)
    s.sections([(8, {'ring', 'vibr'}), (16, {'ring', 'vibr', 'brk', 'sub'}), (8, {'harp', 'brk', 'sub', 'choir'})])
    tune = ("d5 . a4 . f4 . a4 . d5 . e5 . f5 . e5 . | d5 . a4 . f4 . a4 . c#5 . a4 . e4 . a4 . | "
            "d5 . a4 . f4 . a4 . d5 . e5 . f5 . g5 . | a5 . g5 . f5 . e5 . d5 - - - . . . . ")
    bassl = "d3 - - - - - - - a2 - - - - - - - | a2 - - - - - - - c#3 - - - - - - - | bb2 - - - - - - - g2 - - - - - - - | a2 - - - - - - - d3 - - - - - - - "
    s.riff('ring', 'ringtone', tune, vel=0.6)
    s.riff('ring', 'ringtone', bassl, vel=0.4)
    s.riff('harp', 'harpsichord', tune, vel=1.0)
    s.riff('harp', 'harpsichord', bassl, transpose=-12, vel=0.7)
    for b in range(0, 24, 4):
        n = int(1.2 * SR)
        t = tvec(n)
        buzz = np.sign(np.sin(TWO_PI * 150 * t)) * (np.sin(TWO_PI * 1.6 * t) > 0) * 0.4
        s.hit('vibr', bp(buzz, 100, 2000), b * 4 + 2, vel=0.8)
    brk = s.make_break({'k': ["k.......k.k.....", "k.....k...k....."], 's': "....s.......s...", 'h': "h.h.h.h.h.h.h.h."}, 2, swing=0.1)
    brk = crush(brk, 8, 4)
    s.chop('brk', brk, [straight(0), straight(1)])
    s.riff('sub', 'sub', "d2 - - - - - - - a1 - - - - - - - | a1 - - - - - - - c#2 - - - - - - - | bb1 - - - - - - - g1 - - - - - - - | a1 - - - - - - - d2 - - - - - - - ")
    s.pads('choir', prog([('Dm', 1), ('A', 1), ('Bb', 1), ('A', 1)], s.bars), 'choir', center=60, n=3, vowel='a', a=0.8, r=1.5)
    for k, v in dict(ring=-17, vibr=-26, brk=-17, sub=-19, harp=-16, choir=-21).items():
        s.role(k, v)
    s.bus_fx('ring', bp=(450, 4500), rev=('room', 0.2))
    s.bus_fx('harp', rev=('hall', 0.4))
    s.bus_fx('choir', rev=('cave', 0.6))
    s.bus_fx('brk', lp=6000)
    render2(s, -17.5, wow=(5, 1.5), out_dir='ost2/out')


if __name__ == '__main__':
    for name in sys.argv[1:] or ['t21', 't22', 't23', 't24']:
        globals()[name]()
