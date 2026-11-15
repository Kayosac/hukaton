"""«хаPAY» — фонушка внутриигрового аукциона (сайт начала 2000-х в ПК подсобки).

Как MIDI-музыка на сайтах 2000-х: дешёвые General-MIDI-инструменты, всё
синтезировано кодом (numpy), без сэмплов, CC0.
  GM-пианино, орган, квадратный лид, слэп-бас, драм-машина GM, «choir aahs»,
  «orchestra hit» один раз за петлю, звонок «продано».
До мажор-ловушка: навязчивый мотив, хроматические ноты мимо (ля-бемоль над фа,
ми-бемоль, си над си-бемолем), минорная субдоминанта и ♭VI–♭VII.

4/4, 120 bpm, такт 2 с, 32 такта = 64 с, бесшовная петля.
  1–8   A:  C Am F G | C E7 F Fm — мотив квадратным лидом, пианино «3-3-2», слэп
  9–16  A': то же, мотив играет орган; на 16 такте звонок «продано»
  17–24 B:  Dm7 G C Am | Dm7 G A♭ B♭ — хор «аа» и лид, пианино арпеджио,
            оркестровый удар на A♭ / B♭ / (C 25 такта)
  25–32 A'': мотив квадратом + пианино в октаву, сбивка в 32 такте в начало

Варианты:  python3 track_hapay.py A  — честная сайт-фонушка
           python3 track_hapay.py B  — «заброшенный сайт»: детюн, VHS-плёнка,
                                       редкие заикания, призрачная вкладка
"""
import sys
import numpy as np
from synth import *
from synth2 import *

VAR = (sys.argv[1] if len(sys.argv) > 1 else "A").upper()
WEIRD = VAR == "B"

BPM = 120
BEAT = 60 / BPM
E8 = BEAT / 2
BAR = 4 * BEAT
NBARS = 32
LOOP = NBARS * BAR

CH = {"C": (36, [0, 4, 7]), "Am": (45, [0, 3, 7]), "F": (41, [0, 4, 7]), "G": (43, [0, 4, 7]),
      "E7": (40, [0, 4, 7, 10]), "Fm": (41, [0, 3, 7]), "Dm7": (38, [0, 3, 7, 10]),
      "Ab": (44, [0, 4, 7]), "Bb": (46, [0, 4, 7])}
A = ["C", "Am", "F", "G", "C", "E7", "F", "Fm"]
B = ["Dm7", "G", "C", "Am", "Dm7", "G", "Ab", "Bb"]
chords = A + A + B + A

# hook, in eighths: (pos, len, midi[, cents])  — cents = deliberately out-of-tune note
HOOK = [
    [(0, 1, 76), (1, 1, 79), (2, 2, 84), (4, 1, 83), (5, 1, 79), (6, 2, 76)],
    [(0, 1, 72), (1, 1, 76), (2, 2, 81), (4, 1, 79), (5, 1, 76), (6, 2, 72)],
    [(0, 1, 77), (1, 1, 81), (2, 1, 84), (3, 1, 81), (4, 1, 80, -30), (5, 1, 79), (6, 1, 77), (7, 1, 74)],
    [(0, 3, 79), (3, 1, 74), (4, 2, 71), (6, 1, 74), (7, 1, 75)],
    [(0, 1, 76), (1, 1, 79), (2, 2, 84), (4, 1, 83), (5, 1, 79), (6, 2, 76)],
    [(0, 1, 76), (1, 1, 80), (2, 2, 83), (4, 1, 86), (5, 1, 83), (6, 2, 80)],
    [(0, 1, 81), (1, 1, 79), (2, 1, 77), (3, 1, 76), (4, 2, 77), (6, 1, 81), (7, 1, 84, -25)],
    [(0, 2, 80), (2, 2, 79), (4, 3, 77), (7, 1, 74)],
]
BMEL = [
    [(0, 2, 77), (2, 1, 76), (3, 1, 74), (4, 4, 72)],
    [(0, 2, 71), (2, 1, 72), (3, 1, 74), (4, 2, 79), (6, 2, 77)],
    [(0, 2, 76), (2, 2, 72), (4, 2, 67), (6, 2, 72)],
    [(0, 3, 76), (3, 1, 74), (4, 4, 72)],
    [(0, 2, 77), (2, 1, 76), (3, 1, 74), (4, 2, 72), (6, 2, 74)],
    [(0, 2, 79), (2, 2, 74), (4, 4, 71)],
    [(0, 2, 72), (2, 2, 75), (4, 4, 80)],
    [(0, 2, 77), (2, 2, 74), (4, 3, 81), (7, 1, 83, -20)],
]

m = Mix(LOOP, seed=1404 if not WEIRD else 6606)
rng = m.rng
DET = 18 if WEIRD else 5  # random per-note detune, cents


def crooked(midi, cents=0.0, amount=None):
    a = DET if amount is None else amount
    return mtof(midi + (cents + rng.uniform(-a, a)) / 100)


def cheap(x, rate=22050, bits=12):
    """Sample-player cheapness of a 2000s sound card."""
    if WEIRD:
        rate, bits = min(rate, 16000), min(bits, 9)
    return bitcrush(x, rate, bits)


# ---------------------------------------------------------------- GM instruments

def gm_piano(f, dur, rng, vel=1.0):
    """'Acoustic Grand Piano' of a cheap wavetable: two strings, highs die first, damper on release."""
    n = int((dur + 0.5) * SR)
    t = np.arange(n) / SR
    out = np.zeros(n)
    Bk = 0.00025
    for k in range(1, 13):
        fk = f * k * np.sqrt(1 + Bk * k * k)
        if fk > 9000:
            break
        amp = abs(np.sin(np.pi * k * 0.13)) / k ** 0.9
        dec = np.exp(-t * (1.1 + 0.9 * k) * (f / 400) ** 0.3)
        for det in (-1.2, 1.4):
            out += amp * 0.5 * np.sin(2 * np.pi * fk * 2 ** (det / 1200) * t + rng.uniform(0, 6.28)) * dec
    ham = filt(noise(int(0.012 * SR), rng), "bp", (400, 3500)) * np.exp(-np.arange(int(0.012 * SR)) / (0.003 * SR))
    out[: len(ham)] += ham * 0.25
    damp = np.clip(1 - (t - dur) / 0.12, 0, 1)
    out = out * np.clip(t / 0.002, 0, 1) * damp * 0.16 * vel
    return cheap(filt(out, "lp", 6500))


def gm_organ(f, dur, rng):
    """'Drawbar Organ': 16' 8' 4' 2 2/3' 2', light vibrato, key click."""
    n = int((dur + 0.06) * SR)
    t = np.arange(n) / SR
    vib = 1 + (2 ** (7 / 1200) - 1) * np.sin(2 * np.pi * 6.3 * t + rng.uniform(0, 6))
    ph = phase_from_freq(np.full(n, f) * vib)
    x = 0.55 * np.sin(0.5 * ph) + 1.0 * np.sin(ph) + 0.55 * np.sin(2 * ph) + 0.3 * np.sin(3 * ph) + 0.22 * np.sin(4 * ph)
    click = filt(noise(int(0.004 * SR), rng), "hp", 3000) * 0.15
    x[: len(click)] += click
    e = env_adsr(n, a=0.006, d=0.05, s=1.0, r=0.04, hold=dur)
    return cheap(x * e * 0.07)


def gm_square(f, dur, rng, bend=0.0):
    """'Lead 1 (square)': soft-edged pulse, delayed vibrato, optional sagging pitch bend (cents)."""
    n = int((dur + 0.05) * SR)
    t = np.arange(n) / SR
    vib = 9 * np.sin(2 * np.pi * 5.5 * t) * np.clip((t - 0.2) / 0.15, 0, 1)
    bnd = bend * np.clip((t - dur * 0.45) / (dur * 0.55), 0, 1) ** 1.5
    freq = f * 2 ** ((vib + bnd) / 1200)
    ph = phase_from_freq(freq)
    x = sum(np.sin(k * ph) / k for k in range(1, 14, 2) if f * k < 9000)
    x += 0.15 * np.sin(2 * ph)
    e = env_adsr(n, a=0.004, d=0.12, s=0.75, r=0.04, hold=dur)
    return cheap(filt(x * e * 0.09, "lp", 3200))


def slap(f, dur, rng, pop=False):
    """'Slap Bass 1': thumb thwack / finger pop, fast-closing brightness."""
    n = int((dur + 0.06) * SR)
    t = np.arange(n) / SR
    ph = phase_from_freq(np.full(n, f) * (1 + (0.02 if pop else 0.01) * np.exp(-t / 0.01)))
    x = np.zeros(n)
    for k in range(1, 18):
        if f * k > 6000:
            break
        bright = np.exp(-t * (6 + k * (2.2 if pop else 3.4)))
        x += (1 / k) * np.sin(k * ph) * (0.25 + 0.75 * bright)
    x += 0.5 * np.sin(ph)
    tw = filt(noise(int(0.006 * SR), rng), "bp", (700, 4000)) * (0.25 if pop else 0.15)
    x[: len(tw)] += tw
    e = env_adsr(n, a=0.002, d=0.25, s=0.45, r=0.035, hold=dur)
    return cheap(filt(x * e * 0.17, "lp", 3000 if pop else 2200))


def gm_kick(rng):
    return cheap(filt(kick808(rng, f0=140, f1=52, dec=0.16), "lp", 2500) * 0.9)


def gm_snare(rng, vel=1.0):
    return cheap(filt(snare(rng, tone=185, dec=0.09, snappy=0.75), "lp", 7500) * vel)


def gm_hat(rng, open_=False):
    return cheap(hat(rng, dec=0.16 if open_ else 0.03, f=7500))


def gm_tom(rng, f=150):
    n = int(0.4 * SR)
    t = np.arange(n) / SR
    freq = f * (1 + 0.5 * np.exp(-t / 0.03))
    x = np.sin(phase_from_freq(freq)) * np.exp(-t / 0.15)
    x[: int(0.004 * SR)] += filt(noise(int(0.004 * SR), rng), "hp", 1500) * 0.2
    return cheap(x * 0.4)


def orch_hit(root, rng):
    """'Orchestra Hit': brass+strings stack + timpani, crushed hard like the classic sample."""
    n = int(0.7 * SR)
    t = np.arange(n) / SR
    x = np.zeros(n)
    for iv in (0, 7, 12, 16, 19, 24):
        fr = mtof(root + iv + rng.uniform(-10, 10) / 100) * (1 - 0.03 * np.exp(-t / 0.05))
        x += saw_add(fr, maxf=6000) * (1.0 if iv < 19 else 0.6)
    x += 2.5 * np.sin(phase_from_freq(mtof(root - 12) * (1 + 0.3 * np.exp(-t / 0.04)))) * np.exp(-t / 0.2)
    x += filt(noise(n, rng), "bp", (800, 5000)) * np.exp(-t / 0.03) * 0.8
    e = np.clip(t / 0.003, 0, 1) * np.exp(-t / 0.18)
    return filt(bitcrush(x * e * 0.05, 11025, 8), "lp", 5000)


def desk_bell(rng):
    n = int(1.6 * SR)
    t = np.arange(n) / SR
    f = mtof(98 + rng.uniform(-10, 10) / 100)
    x = np.sin(2 * np.pi * f * t + 1.2 * np.exp(-t / 0.15) * np.sin(2 * np.pi * f * 2.76 * t))
    x += 0.35 * np.sin(2 * np.pi * f * 2.02 * t) * np.exp(-t / 0.3)
    return x * np.exp(-t / 0.6) * np.clip(t / 0.001, 0, 1) * 0.05


def voice(c, lo=55, hi=71):
    root, iv = CH[c]
    out = []
    for v in iv:
        p = root + v
        while p < lo:
            p += 12
        while p > hi:
            p -= 12
        out.append(p)
    return sorted(set(out))


def bass_root(c):
    r = CH[c][0]
    while r > 44:
        r -= 12
    while r < 33:
        r += 12
    return r


def play_line(line, bar0, inst, shift=0, gain=1.0, pan=0.0, bus="lead", legato=0.9, **kw):
    for k, bar in enumerate(line):
        for nt in bar:
            pos, ln, mi = nt[:3]
            cents = nt[3] if len(nt) > 3 else 0
            x = inst(crooked(mi + shift, cents), ln * E8 * legato, rng, **kw)
            m.add(x, (bar0 + k) * BAR + pos * E8, pan=pan, gain=gain, jitter=0.003, bus=bus)


# ---------------------------------------------------------------- arrangement
for bi, c in enumerate(chords):
    t0 = bi * BAR
    sec = "B" if 16 <= bi < 24 else "A"
    vs = voice(c)
    root = bass_root(c)
    third = CH[c][1][1]

    # piano: 3-3-2 bounce in A, rolling arpeggio in B
    if sec == "A":
        for pos, ln, g in ((0, 1.4, 0.9), (3, 1.4, 0.75), (6, 1.4, 0.8)):
            for k, p in enumerate(vs):
                m.add(gm_piano(crooked(p), ln * E8, rng, vel=g), t0 + pos * E8 + k * 0.006, pan=-0.35 + 0.2 * k,
                      gain=1.0, jitter=0.003, bus="keys")
    else:
        arp = [p + 12 for p in vs]
        seq = (arp + arp[::-1])[:8] if len(arp) >= 4 else (arp + [arp[0] + 12] + arp[::-1] + [arp[0]])[:8]
        for i in range(8):
            m.add(gm_piano(crooked(seq[i % len(seq)]), E8 * 0.95, rng, vel=0.7 if i % 2 else 0.85),
                  t0 + i * E8, pan=0.3 if i % 2 else -0.1, gain=1.0, jitter=0.003, bus="keys")

    # slap bass: octave-popping disco line
    pat = [(0, 1.4, 0, False), (1.5, 0.5, 12, True), (3, 0.9, 7, False), (4, 1.4, 0, False),
           (5.5, 0.5, 12, True), (6, 0.9, 7, False), (7, 0.8, 10 if third == 4 else 12, True)]
    if bi % 4 == 3:  # walk-up on every 4th bar
        pat = pat[:5] + [(6, 0.9, 5, False), (7, 0.8, 7, False)]
    for pos, ln, iv, pop in pat:
        m.add(slap(crooked(root + iv), ln * E8, rng, pop=pop), t0 + pos * E8, pan=0.0, gain=1.0, jitter=0.002,
              bus="bass")

    # drum machine
    fill = bi in (15, 23, 31)
    kicks = [0, 3, 4] if not fill else [0, 3]
    for p in kicks:
        m.add(gm_kick(rng), t0 + p * E8, gain=0.85 if p != 3 else 0.6, bus="drums")
    for p in (2, 6):
        if fill and p == 6:
            continue
        m.add(gm_snare(rng), t0 + p * E8, pan=0.05, gain=0.75, jitter=0.002, bus="drums")
    for i in range(8):
        if fill and i >= 6:
            continue
        op = (i == 7 and bi % 2 == 1)
        m.add(gm_hat(rng, op), t0 + i * E8, pan=0.35, gain=(0.5 if op else (0.55 if i % 2 else 0.35)), jitter=0.003,
              bus="drums")
    if fill:  # soft tom/snare fill over the last two beats
        if bi == 31:
            for j, f in enumerate((210, 180, 150, 120)):
                m.add(gm_tom(rng, f), t0 + (6 + j * 0.5) * E8, pan=0.4 - 0.25 * j, gain=0.8, bus="drums")
        else:
            for j in range(4):
                m.add(gm_snare(rng, 0.45 + 0.12 * j), t0 + (6 + j * 0.5) * E8, pan=0.05, gain=0.7, bus="drums")

    # organ: sustained comping chords in B and A''
    if bi >= 16:
        for p in voice(c, 52, 67):
            m.add(gm_organ(crooked(p), BAR * 0.96, rng), t0, pan=-0.2, gain=0.55 if sec == "A" else 0.75,
                  bus="organ")

# hook: A square, A' organ, A'' square + piano octave
play_line(HOOK, 0, gm_square, gain=0.95, pan=-0.1)
play_line(HOOK, 8, gm_organ, shift=-12, gain=1.5, pan=0.15, bus="organ")
play_line(HOOK, 24, gm_square, gain=0.85, pan=-0.1)
play_line(HOOK, 24, gm_piano, shift=12, gain=0.45, pan=0.3, bus="keys", legato=1.0)

# B: square melody, choir 'aahs' pad
bend_bars = {19: -90, 21: -60} if WEIRD else {}
for k, bar in enumerate(BMEL):
    for nt in bar:
        pos, ln, mi = nt[:3]
        cents = nt[3] if len(nt) > 3 else 0
        bend = bend_bars.get(16 + k, 0) if nt is bar[-1] else 0
        m.add(gm_square(crooked(mi, cents), ln * E8 * 0.92, rng, bend=bend), (16 + k) * BAR + pos * E8, pan=-0.1,
              gain=0.85, jitter=0.003, bus="lead")
for bi in range(16, 24):
    mids = voice(chords[bi], 57, 72)[:3]
    m.add(choir(mids, BAR * 1.0, rng, vowel_from=(730, 1090, 2440), vowel_to=(650, 1080, 2650)), bi * BAR,
          gain=0.9, bus="pad")

# orchestra hits: once per loop, the ♭VI ♭VII I cadence
for bi, r in ((22, 56), (23, 58), (24, 60)):
    m.add(orch_hit(r, rng), bi * BAR, pan=0.0, gain=0.8 if bi < 24 else 0.65, bus="hit")

# 'sold!' desk bell at the end of A'
m.add(desk_bell(rng), 15 * BAR + 6 * E8, pan=0.45, gain=1.0, bus="weird")
if WEIRD:
    m.add(desk_bell(rng), 15 * BAR + 6.5 * E8, pan=-0.45, gain=0.55, bus="weird")  # a second, detuned ding

# ---------------------------------------------------------------- the 'strange' layer
# muffled drone behind the wall under B (dark-fantasy undertow, barely there)
for bi in range(16, 24):
    r = CH[chords[bi]][0]
    n = int((BAR + 0.5) * SR)
    t = np.arange(n) / SR
    x = (np.sin(2 * np.pi * mtof(r) * t) + 0.5 * np.sin(2 * np.pi * mtof(r + 7) * t)) \
        * np.clip(t / 0.4, 0, 1) * np.clip((BAR + 0.4 - t) / 0.4, 0, 1)
    m.add(filt(x, "lp", 250) * 0.022, bi * BAR, gain=1.0, bus="weird")

if WEIRD:
    # a ghost 'second tab': the hook on a toy piano, a quarter-tone flat, far away, under B
    for k, bar in enumerate(HOOK[:4] + HOOK[4:]):
        for nt in bar:
            pos, ln, mi = nt[:3]
            x = filt(bitcrush(toy_piano(mtof(mi - 0.45 + rng.uniform(-0.1, 0.1)), ln * E8, rng), 8000, 6), "lp", 2200)
            m.add(x, (16 + k) * BAR + pos * E8 + 0.03, pan=0.6, gain=0.28, bus="ghost")
    # dial-up behind the wall + a static breath
    m.add(filt(modem(rng, 2.6), "lp", 1600), 20 * BAR + 0.3, pan=-0.7, gain=0.45, bus="ghost")
    m.add(static_burst(rng, 1.2), 11 * BAR + 0.5, pan=0.6, gain=0.35, bus="ghost")
    # hard-drive clicks
    for at in np.sort(rng.uniform(0, LOOP, 10)):
        for j in range(rng.integers(2, 6)):
            nn = int(0.004 * SR)
            tick = filt(rng.standard_normal(nn), "bp", (1800, 6000)) * np.exp(-np.arange(nn) / (0.0008 * SR))
            m.add(tick, at + j * rng.uniform(0.02, 0.05), pan=0.3, gain=0.1, bus="ghost")

# ---------------------------------------------------------------- mix
L = m.L
t = np.arange(L) / SR
drums = m.fold("drums")
bass = m.fold("bass")
keys = m.fold("keys")
organ = circ_chorus(m.fold("organ"), LOOP, base=0.006, depth=0.002, cycles=96)  # rotary-ish
lead = m.fold("lead")
lead = lead + 0.3 * circ_delay(lead, 3 * E8, fb=0.3, taps=3)
pad = circ_chorus(m.fold("pad"), LOOP, cycles=23)
hit = m.fold("hit")
weird = m.fold("weird")

G = dict(drums=0.45, bass=0.75, keys=1.1, organ=0.6, lead=1.1, pad=1.3, hit=1.0)
drums, bass, keys, organ, lead, pad, hit = (G[k] * v for k, v in
    (("drums", drums), ("bass", bass), ("keys", keys), ("organ", organ), ("lead", lead), ("pad", pad), ("hit", hit)))
dry = drums + bass + keys + organ + lead + pad + hit
wet = circ_reverb(0.6 * drums + keys + organ + lead + pad + hit, rng, length=2.0, decay=0.45, lp=5000)  # GM 'Reverb 40' room
weird_wet = circ_reverb(weird, rng, length=4.0, decay=1.4, lp=4000, predelay=0.03)
mix = dry + 0.22 * wet + 0.5 * weird + 0.5 * weird_wet
if "-v" in sys.argv:  # bus balance (dB relative to the dry sum)
    ref = np.sqrt(np.mean(dry ** 2))
    for nm, b in (("drums", drums), ("bass", bass), ("keys", keys), ("organ", organ),
                  ("lead", lead), ("pad", pad), ("hit", hit), ("weird", 0.5 * weird + 0.5 * weird_wet),
                  ("reverb", 0.22 * wet)):
        print(f"  {nm:7s} {20 * np.log10(np.sqrt(np.mean(b ** 2)) / ref + 1e-9):6.1f} dB")

if WEIRD:
    ghost = m.fold("ghost")
    ghost_wet = circ_reverb(ghost, rng, length=5.0, decay=1.8, lp=3500, predelay=0.05)
    mix = mix + 0.35 * ghost + 0.6 * ghost_wet

# room tone / tape
hiss = np.stack([filt(rng.standard_normal(L), "bp", (2500, 11000)) for _ in range(2)], 1) * (0.006 if WEIRD else 0.0018)
hum = (np.sin(2 * np.pi * 50 * t) + 0.4 * np.sin(2 * np.pi * 100 * t)) * (0.0025 if WEIRD else 0.0008)
mix = mix + hiss + hum[:, None]

if WEIRD:
    # rare glitch: the 'sound card' skips — a 16th repeats (only twice per loop)
    for at in (11 * BAR + 6 * E8, 27 * BAR + 5 * E8):
        mix = stutter(mix, at, E8 / 2, 3, decay=0.97)
    # one 'tape catch': pitch sags and recovers inside a beat, length preserved
    a, n = int((19 * BAR + 2 * E8) * SR), int(0.6 * SR)
    u = np.arange(n) / n
    sp = 1 - 0.06 * np.sin(2 * np.pi * u)
    pos = a + np.cumsum(sp) - sp[0]
    for ch in range(2):
        mix[a:a + n, ch] = np.interp(pos, np.arange(L), mix[:, ch])
    mix = dropouts(mix, rng, [7.3, 29.1, 45.6, 58.2], LOOP)
    mix = circ_eq(mix, hp=40, lp=7000)
    mix = circ_wow(mix, LOOP, cycles=(5, 13, 47), cents=(14, 7, 2.5))
else:
    mix = circ_eq(mix, hp=35, lp=11000)
    mix = circ_wow(mix, LOOP, cycles=(7, 37), cents=(3, 1))

mix = master(mix, target_rms_db=-22.4, drive=1.25)
name = "haPAY_A_fonushka" if not WEIRD else "haPAY_B_zabroshenny"
write(f"out/{name}.wav", mix)
print(name, LOOP)
