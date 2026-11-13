"""3 - «Кривой вальс»: a tipsy oom-pa-pa waltz. 3/4, 126 bpm, D major with
'missed' chords. Tuba sliding into every note, plucked balalaika on 2 and 3,
out-of-tune toy piano, musette accordion; ends with the tuba sliding off and a boing."""
import numpy as np
from synth import *

BPM = 126
Q = 60 / BPM
BAR = 3 * Q
rng = np.random.RandomState(33)

CHORDS = {"D": ([62, 66, 69], 38, 33), "A7": ([61, 64, 67], 33, 40), "D7": ([60, 62, 66], 38, 33),
          "G": ([62, 67, 71], 31, 38), "Gm": ([62, 67, 70], 31, 38), "Bb": ([62, 65, 70], 34, 41),
          "Eb": ([63, 67, 70], 39, 34)}
PROG = ["D", "D", "A7", "A7", "A7", "A7", "D", "D", "D", "D7", "G", "Gm", "D", "A7", "D", "D"]
MEL = [
    [(0, 66, 2), (2, 69, 1)], [(0, 74, 2), (2, 73, 1)], [(0, 73, 1), (1, 76, 1), (2, 79, 1)],
    [(0, 76, 2), (2, 73, 1)], [(0, 69, 1), (1, 71, 1), (2, 73, 1)], [(0, 76, 1.5), (1.5, 74, .5), (2, 73, 1)],
    [(0, 74, 2), (2, 78, 1)], [(0, 81, 3)], [(0, 81, 1), (1, 78, 1), (2, 74, 1)], [(0, 72, 2), (2, 74, 1)],
    [(0, 71, 1), (1, 74, 1), (2, 79, 1)], [(0, 82, 2), (2, 79, 1)], [(0, 78, 2), (2, 74, 1)],
    [(0, 76, 1), (1, 73, 1), (2, 69, 1)], [(0, 74, 2), (2, 69, 1)], [(0, 62, 3)],
]
PASSES = 3
lp_ = Loop(PASSES * 16 * BAR, seed=9)


DSCALE = {2, 4, 6, 7, 9, 11, 1}


def third_below(m):
    steps, x = 0, m
    while steps < 2:
        x -= 1
        if x % 12 in DSCALE:
            steps += 1
    return x


def j():
    return rng.uniform(-0.012, 0.018)    # drunk timing


for p in range(PASSES):
    for i in range(16):
        bar = p * 16 + i
        t0 = bar * BAR
        ch = PROG[i]
        if p == 1 and i == 4:
            ch = "Bb"        # the musician misses
        if p == 2 and i == 11:
            ch = "Eb"
        tones, root, fifth = CHORDS[ch]
        last = (p == PASSES - 1 and i == 15)
        # tuba on 1
        if not last:
            m = root if i % 2 == 0 else fifth
            lp_.add("tuba", tuba(m, Q * 0.85, rng, slide=2.5 + rng.uniform(0, 1.5)), t0 + j(), pan=-0.1, gain=0.55)
        else:
            n = int(BAR * 1.1 * SR)
            tt = tvec(n)
            fr = mtof(38) * 2 ** (-14 / 12 * (tt / tt[-1]) ** 1.6)
            e = adsr(n - int(0.12 * SR), 0.03, 0.2, 0.8, 0.12)[:n]
            sig = lp(saw_f(fr, rng=rng) * 0.6 + pulse_f(fr, 0.3, rng=rng) * 0.4, 600) + 0.4 * sine_f(fr)
            lp_.add("tuba", sig * e, t0, pan=-0.1, gain=0.55)
            lp_.add("fx", boing(rng, 140, 520, 0.5), t0 + 2.55 * Q, pan=0.3, gain=0.35)
        # balalaika plucks on 2 and 3 (strummed)
        for b in (1, 2):
            if last:
                break
            for k, m in enumerate(tones):
                lp_.add("bal", pluck(m, Q * 0.42, rng, tau=0.5, pos=0.12, bright=1.2, damp=True),
                        t0 + b * Q + k * 0.011 + j(), pan=0.35, gain=0.13)
            lp_.add("perc", woodblock(rng, 1500 if b == 1 else 1350), t0 + b * Q + j(), pan=0.6, gain=0.035)
        # melody
        for b, m, d in MEL[i]:
            st = t0 + b * Q + j()
            if p in (0, 2):
                lp_.add("toy", toy_piano(m + 12, d * Q, rng), st, pan=0.15, gain=0.24)
                if d >= 2:   # toy piano re-strikes long notes, a bit late
                    lp_.add("toy", toy_piano(m + 12, Q, rng), st + Q * 1.05, pan=0.15, gain=0.10)
            if p in (1, 2):
                mm = m if p == 1 else third_below(m)
                lp_.add("acc", accordion(mm, d * Q * 0.92, rng), st, pan=-0.3, gain=0.42)
        # accordion bass-chord 'pa-pa' in pass 2 (on top of balalaika)
        if p == 1:
            for b in (1, 2):
                for m in tones:
                    lp_.add("acc", accordion(m - 12, Q * 0.4, rng, a=0.01, r=0.05, cutoff=1800), t0 + b * Q, pan=-0.5, gain=0.12)

L = lp_.L
B = lp_.buses
ir = make_ir(rng, t60=1.3, damp=4000)
toy = B["toy"] + 0.35 * creverb(B["toy"], ir)
acc = B["acc"] + 0.25 * creverb(B["acc"], ir)
bal = B["bal"] + 0.25 * creverb(B["bal"], ir)
tub = B["tuba"] + 0.12 * creverb(B["tuba"], ir)
perc = B["perc"] + 0.2 * creverb(B["perc"], ir)
fx = B["fx"] + 0.4 * creverb(B["fx"], ir)

levels(tuba=tub, bal=1.6 * bal, toy=0.8 * toy, acc=acc, perc=4 * perc, fx=fx)
mix = tub + 1.6 * bal + 0.8 * toy + acc + 4 * perc + fx
mix = mix + hiss(L, rng, 0.003) + crackle(L, rng, rate=2, level=0.01)
mix = tape_wow(mix, rng, depth_cents=12, flutter_cents=3.5)
out = master(mix, target_rms_db=-19.5, drive=1.3)
write("out/3_krivoy_vals.wav", out)
report("3_krivoy_vals", out)
