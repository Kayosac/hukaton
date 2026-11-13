"""2 - «Тёплый ЭЛТ»: 90s lobby / shareware lounge muzak. 4/4, 82 bpm, D major with
extended chords. FM electric piano with chorus, fretless-style gliding bass, brushes and
shaker, vibraphone tune. Weirdcore touch: the first pass sounds like it comes through
a wall/old TV speaker and opens up; a slowed 'lobby chime' on pass three."""
import numpy as np
from synth import *

BPM = 82
Q = 60 / BPM
BAR = 4 * Q
rng = np.random.RandomState(22)

CH = [  # ep voicing, bass root
    ([59, 62, 66, 69], 43),   # Gmaj9
    ([57, 61, 64, 68], 42),   # F#m7(add9-ish)
    ([55, 59, 62, 66], 40),   # Em9
    ([55, 59, 61, 66], 45),   # A13
    ([54, 57, 61, 64], 38),   # Dmaj9
    ([57, 61, 62, 66], 47),   # Bm9
    ([55, 59, 62, 66], 40),   # Em9
    ([55, 58, 61, 64], 45),   # A7b9
]
MEL = [
    [(0, 78, 1.5), (1.5, 76, .5), (2, 74, 1), (3, 69, 1)],
    [(0, 73, 2.5), (2.5, 71, .5), (3, 69, 1)],
    [(0, 71, .5), (.5, 74, .5), (1, 78, 1.5), (2.5, 76, 1.5)],
    [(0, 73, 1), (1, 71, 1), (2, 66, 2)],
    [(0, 69, 1.5), (1.5, 73, .5), (2, 76, 1), (3, 81, 1)],
    [(0, 78, 2), (2, 76, .5), (2.5, 74, .5), (3, 73, 1)],
    [(0, 71, 1), (1, 74, 1), (2, 79, 1.5), (3.5, 78, .5)],
    [(0, 76, 1), (1, 73, 1), (2, 70, 1), (3, 69, 1)],
]
PASSES = 3
lp_ = Loop(PASSES * 8 * BAR, seed=7)
SW = 0.62   # swung offbeat position


def sw(beat):
    """swing offbeat eighths"""
    b = np.floor(beat)
    fr = beat - b
    return b + (SW if abs(fr - 0.5) < 1e-6 else fr)


prev_note = None
kicks = []
for p in range(PASSES):
    for i, (ep, root) in enumerate(CH):
        bar = p * 8 + i
        t0 = bar * BAR
        nxt_root = CH[(i + 1) % 8][1]
        # electric piano comping
        hits = [(0, 1.45, 0.8), (2.5, 1.3, 0.6)] if p != 1 else [(0, 0.9, 0.75), (1.5, 0.45, 0.45), (2.5, 1.3, 0.6)]
        if i == 7:
            hits = [(0, 1.45, 0.8), (2.5, 0.5, 0.6), (3.5, 0.45, 0.5)]
        for b, d, v in hits:
            for k, m in enumerate(ep):
                lp_.add("ep", fm_epiano(m, d * Q, rng, vel=v), t0 + sw(b) * Q + k * 0.006 + rng.uniform(0, 0.008),
                        pan=-0.35 + 0.23 * k, gain=0.14 * v)
        # bass: root, fifth, octave, chromatic approach
        line = [(0, root, 1.4), (1.5, root + 7, 0.45), (2, root + 12, 0.9), (3, nxt_root - 1 if i % 2 else root + 10, 0.85)]
        for b, m, d in line:
            lp_.add("bass", bass(m, d * Q, rng, cutoff=700, a=0.01, r=0.07, saw=0.25, glide_from=(m - 1) if b == 0 else None),
                    t0 + sw(b) * Q, gain=0.5)
        # drums
        for b in (0, 2):
            lp_.add("drums", kick(rng, punch=0.4, tau=0.22, f0=90, f1=48), t0 + b * Q, gain=0.5)
            kicks.append(t0 + b * Q)
        for b in (1, 3):
            lp_.add("drums", brush(rng, 0.22), t0 + b * Q - 0.015, pan=0.1, gain=0.20)
        for b in np.arange(0, 4, 0.5):
            acc = 1.0 if b % 1 == 0 else 0.6
            lp_.add("shk", shaker(rng, 0.045), t0 + sw(b) * Q + rng.uniform(-0.004, 0.004), pan=0.5, gain=0.07 * acc)
        if i % 2 == 1:
            lp_.add("drums", woodblock(rng, 1250), t0 + sw(3.5) * Q, pan=-0.5, gain=0.06)
        # vibes tune on passes 2 and 3
        if p >= 1:
            for b, m, d in MEL[i]:
                mm = m + (12 if (p == 2 and i in (4, 5)) else 0)
                lp_.add("vib", vibes(mm, d * Q * 0.95, rng), t0 + sw(b) * Q, pan=0.25, gain=0.17)

# slowed lobby chime at the top of pass 3 (glass tones, big hall)
for k, m in enumerate([81, 78, 74, 69]):
    lp_.add("chime", glass(m, 0.9, rng, a=0.02, r=2.5), 16 * BAR - 1.6 * Q + k * 0.55, pan=0.0, gain=0.22)

L = lp_.L
B = lp_.buses
ir = make_ir(rng, t60=1.9, damp=4500)
ir_big = make_ir(rng, t60=5.0, damp=3000)
ep = stereo_chorus(B["ep"], rng, depth_ms=3.5, rate=0.8, mix=0.55)
ep = ep + 0.35 * creverb(ep, ir)
vib = B["vib"] + 0.35 * creverb(B["vib"], ir)
vib = cdelay(vib, Q * 0.75, fb=0.3, mix=0.18, cutoff=2500)
drums = B["drums"] + 0.15 * creverb(B["drums"], ir)
shk = B["shk"] + 0.2 * creverb(B["shk"], ir)
chime = B["chime"] + 1.2 * creverb(B["chime"], ir_big)
duck = ducker(L, kicks, depth=0.12, release=0.2)

levels(ep=2.8 * ep, bass=0.5 * B["bass"], drums=0.8 * drums, shk=3.0 * shk, vib=1.3 * vib, chime=0.25 * chime)
mix = 2.8 * ep * duck + 0.5 * B["bass"] + 0.8 * drums + 3.0 * shk + 1.3 * vib + 0.25 * chime

# 'through the TV speaker': bars 1-6 muffled, opening during bars 7-8
n = np.arange(L)
g = np.ones(L)
a, b = int(6 * BAR * SR), int(8 * BAR * SR)
g[:a] = 0.0
g[a:b] = np.linspace(0, 1, b - a) ** 1.5
k = int(0.3 * SR)
g[-k:] = np.linspace(1, 0, k)   # snap back to muffled across the loop seam
muff = cbp(mix, 250, 2400, 2) * 1.6
muff = muff + 0.3 * creverb(muff, ir)
mix = mix * g[:, None] + muff * (1 - g[:, None])

mix = mix + crackle(L, rng, rate=3, level=0.012) + hiss(L, rng, 0.004)
mix = tape_wow(mix, rng, depth_cents=6, flutter_cents=3.0)
out = master(mix, target_rms_db=-19.5, drive=1.3)
write("out/2_teply_elt.wav", out)
report("2_teply_elt", out)
