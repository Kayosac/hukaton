"""4 - «Пыльные склянки»: beatless liminal ambient. D minor with a phrygian Eb.
Low drone and 'wind across bottle necks', a slow wordless choir changing chord every
12 s, sparse glass-harmonica tones in a big hall, rare bottle clinks, reversed swells,
and a faint music-box phrase from another room."""
import numpy as np
from synth import *

rng = np.random.RandomState(44)
SEG = 12.0
CH = [
    ([50, 57, 64, 65], "o"),   # Dm9
    ([46, 53, 57, 62], "u"),   # Bbmaj7
    ([43, 50, 57, 58], "o"),   # Gm9
    ([45, 53, 57, 62], "a"),   # Dm/A
    ([51, 55, 58, 62], "u"),   # Ebmaj7 (phrygian shade)
    ([45, 52, 57, 62], "o"),   # Asus4 -> back to Dm
]
lp_ = Loop(len(CH) * SEG, seed=3)
L = lp_.L
PENTA = [62, 64, 65, 69, 72, 74, 76, 77, 81]
CHORD_SAFE = {0: [62, 64, 65, 69, 72, 76], 1: [62, 65, 69, 70, 74, 77], 2: [62, 65, 69, 70, 74, 79],
              3: [62, 64, 65, 69, 72, 74], 4: [62, 67, 70, 74, 75, 79], 5: [62, 64, 67, 69, 74, 76]}

for i, (tones, vow) in enumerate(CH):
    t0 = i * SEG
    for m in tones:
        lp_.add("choir", choir(m + 12, SEG * 0.85, rng, vowel=vow, a=3.0, r=4.5, voices=4), t0, pan=rng.uniform(-0.8, 0.8), gain=0.30)
    lp_.add("sub", bass(tones[0] - 12, SEG * 0.8, rng, cutoff=200, a=2.0, r=3.0, saw=0.1), t0, gain=0.25)
    # sparse glass tones from the chord-safe set
    t = t0 + rng.uniform(0.5, 2.0)
    while t < t0 + SEG - 1.0:
        m = rng.choice(CHORD_SAFE[i]) + (12 if rng.rand() < 0.3 else 0)
        d = rng.uniform(0.8, 2.6)
        lp_.add("glass", glass(m, d, rng, a=rng.uniform(0.15, 0.6), r=2.5), t, pan=rng.uniform(-0.9, 0.9), gain=0.16)
        t += rng.uniform(1.6, 3.6)
    # rare bottle clink
    if i % 2 == 0:
        lp_.add("clink", coin(rng, base=rng.uniform(1700, 2500)), t0 + rng.uniform(3, 9), pan=rng.uniform(-0.6, 0.6), gain=0.10)

# reversed swells into chord changes 2 and 5
ir_big = make_ir(rng, t60=6.0, damp=2500, width=1.0)
for k, at in [(0, 2 * SEG), (1, 5 * SEG)]:
    note = glass(CHORD_SAFE[(int(at / SEG)) % 6][2] + 12, 0.6, rng, a=0.01, r=1.5)
    st = np.zeros((int(5 * SR), 2))
    st[:len(note)] += note[:, None]
    wet = np.zeros_like(st)
    for c in range(2):
        wet[:, c] = np.convolve(st[:, c], ir_big[:len(st), c])[:len(st)]
    rev = wet[::-1] * 0.7
    lp_.add("swell", rev, at - len(rev) / SR + 0.05)

# music box phrase from another room (quote of the shop lullaby), around 40 s
phrase = [(71, 3), (67, 1), (69, 1), (71, 1), (76, 4), (74, 1), (71, 1)]
t = 3 * SEG + 3.0
E8 = 60 / (56 * 3)
for m, d in phrase:
    lp_.add("room", music_box(m + 12, d * E8, rng), t, pan=0.7, gain=0.25)
    t += d * E8

B = lp_.buses
n = np.arange(L)
ir = make_ir(rng, t60=3.5, damp=3000)

# drone: D1 + A1 with slow beating, plus bottle-wind noise (band moves slowly)
drone = np.zeros((L, 2))
for m, g in [(26, 0.5), (33, 0.3), (38, 0.12)]:
    cyc = round(mtof(m) * L / SR)
    drone[:, 0] += g * np.sin(2 * np.pi * cyc * n / L)
    drone[:, 1] += g * np.sin(2 * np.pi * (cyc + 1) * n / L)
drone = clp(drone, 250) * (0.8 + 0.2 * np.sin(2 * np.pi * 3 * n / L))[:, None]
wind = np.zeros((L, 2))
for fc, cyc, ph in [(520, 2, 0.0), (790, 3, 1.3), (1180, 5, 2.1)]:
    band = cbp(rng.randn(L, 2), fc * 0.93, fc * 1.07, 2)
    wind += band * (0.5 + 0.5 * np.sin(2 * np.pi * cyc * n / L + ph))[:, None] ** 2
wind *= 0.6

choirb = B["choir"] + 0.9 * creverb(B["choir"], ir_big)
glassb = B["glass"] + 0.8 * creverb(B["glass"], ir_big)
glassb = cdelay(glassb, 0.61, fb=0.4, mix=0.25, cutoff=2200)
clink = B["clink"] + 0.9 * creverb(B["clink"], ir)
room = cbp(B["room"], 300, 1800, 2)
room = 0.4 * room + 0.9 * creverb(room, ir)
swell = B["swell"]
sub = B["sub"]

G = dict(choir=choirb, glass=glassb, drone=0.2 * drone, wind=1.4 * wind, clink=1.6 * clink, room=0.15 * room, swell=0.12 * swell, sub=0.6 * sub)
levels(**G)
mix = sum(G.values())
mix = mix + hiss(L, rng, 0.003)
mix = tape_wow(mix, rng, depth_cents=7, flutter_cents=1.0)
out = master(mix, target_rms_db=-20.5, drive=1.1)
write("out/4_pylnye_sklyanki.wav", out)
report("4_pylnye_sklyanki", out)
