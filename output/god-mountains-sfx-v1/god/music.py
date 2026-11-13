"""Music for the god's mountain: D minor, 60 bpm, 16 chords of 8 s (128 s), loops seamlessly.
Two stems on the same clock:
  gm_music_air   — what fades in from the round pocket on: low drone, string pad, a soft choir line, a slow harp.
  gm_music_might — what joins when he rises: low brass swells, timpani and a great drum, a deep unison choir.
"""
import sys, os
import numpy as np
from lib import *

OUT = sys.argv[1] if len(sys.argv) > 1 else "out"
os.makedirs(OUT, exist_ok=True)
BAR = 4.0
CH = 8.0
L = 16 * CH


def hz(m):
    return 440 * 2 ** ((m - 69) / 12)


# chord: (bass, [voicing], choir top)
D, Bb, F, C, Gm, A, Asus = ([38, 50, 53, 57, 62]), ([34, 50, 53, 58, 62]), ([41, 48, 53, 57, 60]), ([36, 48, 52, 55, 60]), \
    ([31, 50, 55, 58, 62]), ([33, 49, 52, 57, 61]), ([33, 50, 52, 57, 62])
PROG = [(D, 69), (Bb, 70), (F, 69), (C, 67), (D, 65), (Gm, 67), (Bb, 65), (Asus, 64),
        (Bb, 65), (F, 69), (Gm, 70), (D, 69), (Bb, 74), (C, 72), (D, 69), (A, 69)]


def supersaw(f, dur, voices=7, spread=.012, cut=1800):
    t = t_axis(dur)
    x = np.zeros(len(t))
    for k in range(voices):
        d = 1 + spread * (k - (voices - 1) / 2) / ((voices - 1) / 2)
        ph = rng.uniform() + f * d * t
        x += 2 * (ph % 1) - 1
    return lp(x / voices, cut, 2)


def choir(f, dur, voices=6, vowel=(650, 1080, 2650), vib=5.2):
    t = t_axis(dur)
    x = np.zeros(len(t))
    for k in range(voices):
        v = 1 + .006 * np.sin(2 * np.pi * (vib + rng.uniform(-.6, .6)) * t + rng.uniform(0, 6))
        v *= 1 + .004 * (k - voices / 2) / voices
        ph = np.cumsum(f * v) / SR + rng.uniform()
        x += (2 * (ph % 1) - 1)
    x /= voices
    y = sum(g * bp(x, c * .85, c * 1.18, 2) for c, g in zip(vowel, (1, .6, .25)))
    y += .2 * lp(x, 400)
    breath = bp(white(dur), 1500, 5000) * .015
    return y + breath


def harp(f, dur=4.0):
    """Karplus-Strong pluck."""
    n = secs(dur)
    p = int(SR / f)
    buf = rng.uniform(-1, 1, p)
    out = np.zeros(n + p)
    for k in range(0, n, p):
        out[k:k + p] = buf
        buf = .5 * (buf + np.roll(buf, -1)) * .996
    return lp(out[:n], 3000) * adsr(n, .002, .3)


def brass(f, dur, swell=True):
    t = t_axis(dur)
    a = smooth(t / (dur * .55)) * (1 - smooth((t - dur * .7) / (dur * .3))) if swell else adsr(len(t), .3, 1.5)
    x = np.zeros(len(t))
    for d in (.997, 1.0, 1.004):
        ph = np.cumsum(np.full(len(t), f * d)) / SR + rng.uniform()
        x += 2 * (ph % 1) - 1
    x = sweep_lp(x / 3, lambda s: 180 + 900 * float(np.interp(s, t[::441], a[::441])))
    return np.tanh(x * 1.5) * a


def timpani(f=55, dur=3.0, vel=1.0):
    t = t_axis(dur)
    fp = f * (1 + .06 * np.exp(-t / .05))
    body = np.sin(2 * np.pi * np.cumsum(fp) / SR) * np.exp(-t / .9)
    body += .4 * np.sin(2 * np.pi * np.cumsum(fp * 1.5) / SR) * np.exp(-t / .5)
    hit = lp(white(dur), 1800) * np.exp(-t / .03) * .5
    return (body + hit) * vel


def taiko(dur=4.0):
    t = t_axis(dur)
    body = np.sin(2 * np.pi * np.cumsum(48 + 40 * np.exp(-t / .08)) / SR) * np.exp(-t / .7)
    skin = lp(white(dur), 900) * np.exp(-t / .06)
    return np.tanh((body * 1.4 + skin * .6) * 1.5)


def air():
    b = np.zeros((secs(L + 8), 2))
    t_all = 0
    for i, (ch, top) in enumerate(PROG):
        at = i * CH
        bass, notes = ch[0], ch[1:]
        # drone and pad
        dr = (np.sin(2 * np.pi * hz(bass) * t_axis(CH + 3)) + .4 * np.sin(2 * np.pi * hz(bass + 12) * t_axis(CH + 3)))
        place(b, widen(dr * adsr(len(dr), 2.0, 3.0, 1.2), 23), at, .25)
        for k, m in enumerate(notes):
            s = supersaw(hz(m), CH + 3, cut=1400 + 300 * k) * adsr(secs(CH + 3), 2.2, 3.0, 1.3)
            place(b, pan(s, -.6 + .4 * k), at, .09)
        # choir line (enters on the second time round the first half, and stays)
        if i >= 2:
            c = choir(hz(top), CH + 2.5) * adsr(secs(CH + 2.5), 2.5, 2.5, 1.4)
            place(b, widen(c, 14), at, .22)
        # harp: a slow rising arpeggio, one note a beat
        arp = sorted(notes + [notes[0] + 12, notes[1] + 12, notes[2] + 12])
        for j in range(8):
            m = arp[j % len(arp)] + 12
            place(b, pan(harp(hz(m), 3.0), rng.uniform(-.5, .5)), at + j * 1.0, .10 * (.8 + .4 * rng.uniform()))
    return b


def might():
    b = np.zeros((secs(L + 8), 2))
    for i, (ch, top) in enumerate(PROG):
        at = i * CH
        bass, notes = ch[0], ch[1:]
        # brass swells on root and fifth, two octaves
        for m, g in ((bass + 12, .3), (bass + 19, .2), (bass + 24, .14)):
            place(b, widen(brass(hz(m), CH + .5), 9), at, g)
        # deep unison choir on the root, "oh"
        c = choir(hz(bass + 12), CH + 2, voices=8, vowel=(450, 800, 2600)) * adsr(secs(CH + 2), 1.5, 2.5, 1.4)
        c += choir(hz(bass + 24), CH + 2, voices=6, vowel=(450, 800, 2600)) * adsr(secs(CH + 2), 1.5, 2.5, 1.4) * .7
        place(b, widen(c, 18), at, .2)
        # timpani on 1 and 3, a roll into every second chord, the great drum on each chord
        for beat in (0, 2, 4, 6):
            place(b, widen(timpani(hz(bass + 12 if bass + 12 < 50 else bass), 3, .8 if beat % 4 == 0 else .5), 3), at + beat, .45)
        if i % 2 == 1:
            for r in range(16):
                place(b, widen(timpani(hz(38), 1.0, .15 + .5 * r / 16), 3), at + CH - 2 + r * .125, .35)
        place(b, widen(taiko(), 7), at, .7)
    return b


def finish(x, name):
    # wrap the tail past the loop point onto the start: seamless loop
    x = reverb(x, wet=.38, rt=5.0, dark=3000, pre=.03)
    n = secs(L)
    tail = x[n:]
    x = x[:n].copy()
    x[:len(tail)] += tail[:n]
    x = bass_harmonics(x, .15)
    x = master(x, -1.5)
    return x


for name, fn in (("gm_music_air", air), ("gm_music_might", might)):
    x = finish(fn(), name)
    write(f"/tmp/{name}.wav", x)
    import subprocess
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", f"/tmp/{name}.wav", "-c:a", "libvorbis", "-q:a", "5",
                    f"{OUT}/{name}.ogg"], check=True)
    print(name, f"{len(x) / SR:.0f}s")
