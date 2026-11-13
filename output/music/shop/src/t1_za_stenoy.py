"""1 - «Лавка за стеной»: dark music-box lullaby, slow 6/8 with one limping 7/8 bar,
E minor + phrygian F. Music box, kalimba, tape pad, drone, wall clock as the pulse,
CRT whine, a 'radio from the back room' and a slowed choir."""
import numpy as np
from synth import *

E8 = 60 / (56 * 3)          # eighth note, dotted quarter = 56
rng = np.random.RandomState(11)

# chord slots: (name, pad voicing, bass root, arp tones, melody [(midi|None, eighths)])
S = [
    ("Em",    [52, 55, 59, 62], 40, [64, 67, 71, 76], [(71,3),(67,1),(69,1),(71,1),(76,4),(74,1),(71,1)]),
    ("Fmaj7", [53, 57, 60, 64], 41, [65, 69, 72, 76], [(72,3),(69,2),(65,1),(64,4),(None,2)]),
    ("Em",    [52, 55, 59, 64], 40, [64, 67, 71, 79], [(67,2),(71,1),(76,3),(79,2),(78,1),(76,3)]),
    ("Cmaj7", [48, 55, 59, 64], 36, [60, 64, 67, 71], [(76,3),(71,3),(72,2),(67,1),(64,3)]),
    ("Am",    [45, 57, 60, 64], 45, [57, 60, 64, 69], [(69,2),(72,1),(76,3),(74,2),(72,1),(71,3)]),
    ("F",     [53, 57, 60, 65], 41, [65, 69, 72, 77], [(72,2),(77,1),(76,3),(72,3),(69,3)]),
    ("B",     [47, 54, 59, 63], 35, [59, 63, 66, 71], [(71,3),(75,3),(78,2),(75,1),(71,3)]),
    ("Em",    [52, 55, 59, 64], 40, [64, 67, 71, 76], [(76,6),(None,3),(64,3)]),
]
slots = []
for p in range(2):
    for i, s in enumerate(S):
        length = 12 + (1 if i == 7 else 0)   # last bar of each pass limps in 7/8
        slots.append((p, i, s, length))

total_e = sum(s[3] for s in slots)
lp_ = Loop(total_e * E8, seed=5)
print("loop", lp_.sec)

t_e = 0
for p, i, (name, pad, root, arp, mel), length in slots:
    t0 = t_e * E8
    dur = length * E8
    # pad: warm supersaw, long attack
    for m in pad:
        lp_.add("pad", supersaw(m, dur * 0.95, rng, cutoff=1100 if p == 0 else 1500, a=1.4, r=2.2, spread=14),
                t0, pan=rng.uniform(-0.5, 0.5), gain=0.11)
    # bass: soft sine on the downbeat and the second dotted quarter of bar 2
    lp_.add("bass", bass(root, dur * 0.6, rng, cutoff=250, a=0.05, r=0.6, saw=0.15), t0, gain=0.42)
    lp_.add("bass", bass(root + 7, E8 * 2.5, rng, cutoff=250, a=0.04, r=0.4, saw=0.1), t0 + 9 * E8, gain=0.22)
    # melody on music box, an octave up
    te = 0
    for k, (m, d) in enumerate(mel):
        if m is not None:
            lp_.add("box", music_box(m + 12, d * E8, rng), t0 + te * E8, pan=0.15, gain=0.30)
            if p == 1 and d >= 3:
                # second pass: tremolo-like repeat and a soft octave below
                lp_.add("box", music_box(m + 12, E8, rng), t0 + (te + 1.5) * E8, pan=-0.2, gain=0.12)
                lp_.add("box", music_box(m, d * E8, rng), t0 + te * E8 + 0.012, pan=-0.3, gain=0.12)
        te += d
    # kalimba arpeggio in eighths
    pat = [0, 1, 2, 1, 3, 2, 0, 2, 1, 3, 2, 1]
    for k in range(12):
        if p == 0 and k % 3 != 0:
            continue
        m = arp[pat[k]] - 12
        lp_.add("kal", kalimba(m, E8, rng, bright=0.6), t0 + k * E8 + rng.uniform(0, 0.012),
                pan=0.45 * np.sin(k), gain=0.17 if p == 1 else 0.20)
    # wall clock: tick-tock on every dotted quarter
    for k in range(0, length, 3):
        lp_.add("clock", clock_tick(rng, high=((t_e + k) // 3) % 2 == 0), t0 + k * E8, pan=-0.55, gain=0.09)
    # radio from the back room: same theme on a cheap lead, band-limited, on slots 3-6 of pass 2
    if p == 1 and 2 <= i <= 5:
        te = 0
        for m, d in mel:
            if m is not None:
                n = int(d * E8 * SR)
                env = adsr(n, 0.03, 0.2, 0.7, 0.15)
                fr = mtof(m - 12) * (1 + 0.004 * np.sin(2 * np.pi * 5 * tvec(len(env))))
                sig = pulse_f(fr, 0.3, rng=rng) * env
                lp_.add("radio", sig, t0 + te * E8, pan=0.6, gain=0.5)
            te += d
    # slowed choir 'u' under the last four slots
    if p == 1 and i >= 4:
        for m in pad[1:]:
            lp_.add("choir", choir(m + 12, dur * 0.9, rng, vowel="u", a=2.0, r=3.0), t0, pan=rng.uniform(-0.7, 0.7), gain=0.16)
    t_e += length

L = lp_.L
B = lp_.buses
ir_room = make_ir(rng, t60=1.6, damp=3500, width=0.9)
ir_hall = make_ir(rng, t60=4.5, damp=2500, width=1.0)

box = B["box"] + 0.55 * creverb(B["box"], ir_hall)
kal = B["kal"] + 0.4 * creverb(B["kal"], ir_room)
pad = stereo_chorus(B["pad"], rng, depth_ms=4, rate=0.35, mix=0.5)
pad = pad + 0.4 * creverb(pad, ir_hall)
radio = cbp(B["radio"], 350, 2200, 2)
radio = radio + 0.2 * cbp(rng.randn(L, 2), 800, 5000) * 0.01 * (np.abs(radio).max(1, keepdims=True) > 0.001)
radio = 0.7 * creverb(radio, ir_room) + 0.3 * radio
choirb = B["choir"] + 0.8 * creverb(B["choir"], ir_hall)
clock = B["clock"] + 0.3 * creverb(B["clock"], ir_room)

# drone: E1 + B1 with slow beating, plus a dusty noise bed
n = np.arange(L)
drone = np.zeros((L, 2))
for m, g, c in [(28, 0.5, 3), (35, 0.25, 5), (40, 0.12, 7)]:
    f = mtof(m)
    cyc = round(f * L / SR)
    for ch, det in [(0, 0), (1, 1)]:
        drone[:, ch] += g * np.sin(2 * np.pi * (cyc + det) * n / L)
drone *= (0.75 + 0.25 * np.sin(2 * np.pi * 2 * n / L))[:, None]
drone = clp(drone, 300) * 0.16

# CRT whine (15.7 kHz), very quiet, whole cycles so it loops
cyc = round(15734 * L / SR)
crt = np.sin(2 * np.pi * cyc * n / L)[:, None] * np.array([[0.0035, 0.003]])

levels(box=0.9*box, kal=0.8*kal, pad=2.4*pad, bass=0.9*B["bass"], drone=drone, clock=5*clock, radio=0.55*radio, choir=2.5*choirb)
mix = (0.9 * box + 0.8 * kal + 2.4 * pad + 0.9 * B["bass"] + drone + 5.0 * clock
       + 0.55 * radio + 2.5 * choirb + crt + hiss(L, rng, 0.0035))
mix = tape_wow(mix, rng, depth_cents=9, flutter_cents=2.0)
out = master(mix, target_rms_db=-19.5, drive=1.2)
write("out/1_lavka_za_stenoy.wav", out)
report("1_lavka_za_stenoy", out)
