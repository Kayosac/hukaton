"""5 - «Торг»: dusty lo-fi boom-bap. 4/4, 84 bpm, swung 16ths, A minor jazz turnaround.
Nylon-string plucked chords ducked by the kick, sub bass, crunchy drums with coin clinks
as accents, an ocarina-like whistle tune, and chopped wordless vocal stabs on pass 3."""
import numpy as np
from synth import *

BPM = 84
Q = 60 / BPM
BAR = 4 * Q
rng = np.random.RandomState(55)

CH = [
    ([57, 60, 64, 67, 71], 33),   # Am9
    ([53, 57, 60, 64], 38),       # Dm9
    ([53, 59, 64], 31),           # G13 (rootless)
    ([52, 55, 59, 62], 36),       # Cmaj9
    ([53, 57, 60, 64], 29),       # Fmaj7
    ([53, 57, 59, 62], 35),       # Bm7b5
    ([52, 56, 62, 65], 28),       # E7b9
    ([52, 56, 60, 62], 28),       # E7#5
]
MEL = [
    [(0.5, 76, .5), (1, 79, 1), (2.5, 76, .5), (3, 74, 1)],
    [(0, 72, 2), (2.5, 69, .5), (3, 72, .75)],
    [(0, 71, 1.5), (2, 74, .5), (2.5, 76, 1.5)],
    [(0, 79, 2.5), (3, 76, 1)],
    [(0.5, 77, .5), (1, 76, .5), (1.5, 72, 1.5), (3, 69, 1)],
    [(0, 74, 1), (1, 77, 1), (2, 74, .5), (2.5, 71, 1.5)],
    [(0, 68, 1), (1, 71, 1), (2, 74, 1), (3, 77, 1)],
    [(0, 76, 3)],
]
PASSES = 3
lp_ = Loop(PASSES * 8 * BAR, seed=15)
SW16 = 0.58


def s16(beat):
    """swing on 16ths: the 2nd and 4th 16th of each beat are pushed late"""
    b = np.floor(beat)
    fr = round((beat - b) * 4) / 4
    if abs(fr - 0.25) < 1e-6:
        fr = SW16 * 0.5
    elif abs(fr - 0.75) < 1e-6:
        fr = 0.5 + SW16 * 0.5
    else:
        fr = beat - b
    return b + fr


def whistle(m, dur):
    f = mtof(m)
    nh = int(dur * SR)
    e = adsr(nh, 0.04, 0.3, 0.8, 0.18)
    n = len(e)
    t = tvec(n)
    vib = 1 + 0.006 * np.sin(2 * np.pi * 5.0 * t) * np.clip(t / 0.4, 0, 1)
    fr = f * vib * 2 ** (-0.6 / 12 * np.exp(-t / 0.03))
    y = sine_f(fr) + 0.08 * sine_f(fr * 2) + 0.04 * sine_f(fr * 3)
    y += bp(rng.randn(n), f * 0.8, f * 1.6) * 0.12
    return y * e


kicks = []
for p in range(PASSES):
    for i, (ch, root) in enumerate(CH):
        bar = p * 8 + i
        t0 = bar * BAR
        # drums
        kpat = [0, 1.75, 2.5] if i % 2 == 0 else [0, 0.75, 2.5, 3.25]
        for b in kpat:
            lp_.add("drums", kick(rng, punch=0.8, tau=0.28, f0=115, f1=47), t0 + s16(b) * Q, gain=0.85)
            kicks.append(t0 + s16(b) * Q)
        for b in (1, 3):
            lp_.add("drums", snare(rng, tau=0.14, tone=185, lo=700, hi=6000, toneamt=0.7), t0 + b * Q + 0.012, pan=0.05, gain=0.42)
        for k in range(16):
            b = k / 4
            if k % 2 == 1 and rng.rand() < 0.55:
                continue
            g = 0.10 if k % 4 == 0 else (0.07 if k % 2 == 0 else 0.035)
            lp_.add("hats", hat(rng, tau=0.03 if k % 8 else 0.06), t0 + s16(b) * Q + rng.uniform(-0.004, 0.004), pan=0.35, gain=g)
        if i % 4 == 3:
            lp_.add("coins", coin(rng), t0 + s16(3.75) * Q, pan=-0.4, gain=0.11)
            lp_.add("coins", coin(rng), t0 + s16(3.75) * Q + 0.07, pan=0.4, gain=0.07)
        if i == 7 and p == 1:
            lp_.add("drums", snare(rng, tau=0.1, tone=200, toneamt=0.5), t0 + s16(3.5) * Q, gain=0.2)
            lp_.add("drums", snare(rng, tau=0.1, tone=200, toneamt=0.5), t0 + s16(3.75) * Q, gain=0.28)
        # plucked chords: strum down, then a short muted chop
        for b, d, g, up in [(0, 2.2, 1.0, False), (2.5, 1.1, 0.7, True), (3.75, 0.2, 0.4, False)]:
            tones = ch[::-1] if up else ch
            for k, m in enumerate(tones):
                lp_.add("keys", pluck(m, d * Q, rng, tau=1.4, pos=0.22, bright=0.7, damp=True),
                        t0 + s16(b) * Q + k * 0.018, pan=-0.3 + 0.15 * k, gain=0.11 * g)
        # sub bass
        line = [(0, root, 1.4), (1.75, root, 0.25), (2.5, root + 12 if i % 2 else root + 7, 0.6), (3.25, root, 0.5)]
        for b, m, d in line:
            lp_.add("bass", bass(m, d * Q, rng, cutoff=180, a=0.006, r=0.06, saw=0.18, glide_from=m + 12 if b == 0 and i % 4 == 0 else None),
                    t0 + s16(b) * Q, gain=0.6)
        # whistle tune on passes 2 and 3
        if p >= 1:
            for b, m, d in MEL[i]:
                lp_.add("lead", whistle(m + (12 if p == 2 and i < 4 else 0), d * Q * 0.92), t0 + s16(b) * Q, pan=0.2, gain=0.16)
        # chopped vocal stabs on pass 3
        if p == 2:
            for b in (1.5, 3.25):
                top = ch[-1] + 12
                v = choir(top, Q * 0.3, rng, vowel="a" if i % 2 else "o", a=0.01, r=0.08, voices=2)
                lp_.add("chop", v, t0 + s16(b) * Q, pan=-0.45 if b < 2 else 0.45, gain=0.5)

L = lp_.L
B = lp_.buses
ir = make_ir(rng, t60=1.2, damp=3500)
duck = ducker(L, kicks, depth=0.45, release=0.18)

drums = B["drums"]
idx = (np.arange(L) // 3) * 3          # crunch: sample-and-hold at 1/3 rate
drums = clp(drums[idx], 9000) * 0.6 + drums * 0.4
drums = drums + 0.1 * creverb(drums, ir)
hats = clp(B["hats"], 9000)
keys = stereo_chorus(B["keys"], rng, depth_ms=2.5, rate=0.5, mix=0.4)
keys = (keys + 0.4 * creverb(keys, ir)) * duck
lead = B["lead"] + 0.4 * creverb(B["lead"], ir)
lead = cdelay(lead, Q * 0.75, fb=0.3, mix=0.2, cutoff=2000)
chop = cdelay(B["chop"] + 0.3 * creverb(B["chop"], ir), Q * 0.5, fb=0.25, mix=0.25, cutoff=2500)
coins = B["coins"] + 0.4 * creverb(B["coins"], ir)
bassb = B["bass"] * (0.6 + 0.4 * duck)

G = dict(drums=drums, hats=5.0 * hats, keys=2.8 * keys, bass=0.8 * bassb, lead=1.4 * lead, chop=2.5 * chop, coins=2.0 * coins)
levels(**G)
mix = sum(G.values())
mix = clp(mix, 7500, 1)
mix = mix + crackle(L, rng, rate=5, level=0.012) + hiss(L, rng, 0.004, cutoff=7000)
mix = tape_wow(mix, rng, depth_cents=10, flutter_cents=2.5)
out = master(mix, target_rms_db=-19.0, drive=1.5)
write("out/5_torg.wav", out)
report("5_torg", out)
