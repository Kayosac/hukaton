"""PENUS teaser «А хозяин не придёт?» — score A: music with the «Кривая тропа» v2 instruments, scored to the cuts.

Detuned flute + kalimba + drone in 7/8 enter on the cut into the upside-down forest (2.0 s); the flute holds one
note across the cut into the shop (3.6 s); the three faces (5/6/7 s) get an 808 and a saw bass stepping down with
the shop bell (0, -2, -4 semitones); under the mouth only a low drone and the growl remain; the tape stops into the
wooden knock (9.4 s). The logo lands on a drop (teaser_common.drop), then a faint choir «behind the wall» sits under
the real door bell. Effects are the game's own (teaser_common.effects).
Run: python3 teaser_score.py  ->  PENUS-Teaser-18s-v3-audio-A.wav (48 kHz stereo, about -14 LUFS)
"""
from teaser_common import *  # noqa: F401,F403

rng = np.random.default_rng(20261112)
E = 0.2          # eighth note, s (7/8 = 2+2+3, as in «Кривая тропа»)
T0 = 2.0         # music grid starts on the cut into the forest; every cut sits on an eighth


def crooked(midi, cents=12):
    return mtof(midi + rng.uniform(-cents, cents) / 100)


def g(eighth):
    return T0 + eighth * E


music, drums, retro, weird = Bus(), Bus(), Bus(), Bus()

# ======================================================== MUSIC 2.0 – 9.4 s
# harmony follows the picture: forest/shelves Em, faces Em -> D -> C, mouth F (phrygian, wrong)
def chord_at(t):
    if t < 6.0: return 40, [0, 3, 7]      # Em
    if t < 7.0: return 38, [0, 4, 7]      # D
    if t < 8.0: return 36, [0, 4, 7]      # C
    return 41, [0, 4, 7]                   # F

# drone (accordion) with a breathing level, the low thread of the whole dream
for t0, t1, root in ((1.85, 6.0, 40), (6.0, 7.0, 38), (7.0, 8.0, 36), (8.0, 9.6, 29)):
    d = t1 - t0 + 0.4
    for m, gn in ((root, 0.9), (root + 7, 0.4)):
        x = accordion(crooked(m, 6), d, rng, nh=14, wet=0.5)
        t = np.arange(len(x)) / SR
        x = filt(x * np.clip(t / 0.35, 0, 1) * (0.8 + 0.2 * np.sin(2 * np.pi * 0.37 * t)), "lp", 700 if root > 30 else 380)
        music.add(x, t0, pan=-0.05, gain=gn * (1.6 if root > 30 else 2.3))

# kalimba ostinato in 7/8 (2+2+3) from the forest cut to the mouth
for e in range(0, 30):
    t = g(e)
    root, tones = chord_at(t)
    pos = e % 7
    pat = [0, 7, 12, 7, 12 + tones[1], 12, 7]
    midi = root + 24 + pat[pos]
    acc = 1.0 if pos in (0, 2, 4) else 0.6
    music.add(kalimba(crooked(midi, 14), E, rng), t + rng.uniform(-.004, .004), pan=0.3 if pos % 2 else -0.25,
              gain=0.5 * acc)

# frame drum + wooden boxes, folk pulse until the faces take over
for bar in range(3):
    t = g(bar * 7)
    music.add(frame_drum(rng, 82), t, gain=0.7)
    music.add(frame_drum(rng, 96), t + 4 * E, pan=0.1, gain=0.4)
    for k, (o, f) in enumerate([(0, 820), (2, 1040), (4, 700)]):
        if t + o * E < 5.0:
            music.add(woodblock(rng, f + rng.uniform(-25, 25)), t + o * E, pan=-0.4 + 0.4 * k, gain=0.42)

# the detuned flute: theme bars 4–7 of «Кривая тропа», one long E held across the forest -> shop cut
theme = [(0, 3, 71), (3, 2, 69), (5, 2, 67), (7, 6, 64), (13, 2, 62),       # 2.0 .. 4.8
         (15, 2, 64), (17, 3, 67),                                           # face 1
         (20, 2, 69), (22, 3, 67),                                           # face 2
         (25, 5, 71)]                                                        # face 3, hangs into the mouth
notes = [(g(e), ln * E * 0.97, m - 0.3) for e, ln, m in theme]               # ~30 cents flat: «расстроенная»
for st, x in legato(notes, rng, kind="flute", glide=0.06, vib_cents=22, vib_rate=4.6):
    music.add(x, st, pan=-0.1, gain=1.0)
for st, x in legato(notes, rng, kind="flute", glide=0.06, vib_cents=18, vib_rate=4.3):
    music.add(filt(x, "lp", 2400), st + 3 * E, pan=0.6, gain=0.2)

# shop: warm supersaw pad enters on the cut (3.6 s), breathing filter, carries through the faces
for t0, t1, (root, tones) in ((3.6, 6.0, (40, [0, 3, 7])), (6.0, 7.0, (38, [0, 4, 7])), (7.0, 8.0, (36, [0, 4, 7]))):
    for v in (root + 12 + tones[0], root + 12 + tones[2], root + 24 + tones[1]):
        pad = supersaw(crooked(v, 3), t1 - t0 + 0.05, rng, maxf=3200)
        tt = np.arange(pad.shape[0]) / SR
        lfo = 0.5 + 0.5 * np.sin(2 * np.pi * tt / 2.8 - np.pi / 2)
        dark = np.stack([filt(pad[:, i], "lp", 650) for i in range(2)], 1)
        bright = np.stack([filt(pad[:, i], "lp", 2200) for i in range(2)], 1)
        y = dark * (1 - lfo[:, None]) + bright * lfo[:, None]
        y *= np.clip(tt / (0.25 if t0 == 3.6 else 0.02), 0, 1)[:, None]
        retro.add(y, t0, gain=0.55)

# the three faces: reversed crash sucks into the first look, then 808 + saw bass stepping down with the bells
x = reverse(crash(rng, 0.8))
drums.add(x, 5.0 - len(x) / SR + 0.02, pan=0.2, gain=0.55)
for t, root in ((5.0, 40), (6.0, 38), (7.0, 36)):
    drums.add(kick808(rng), t, gain=1.0)
    for i in range(5):
        retro.add(saw_bass(crooked(root, 4), E * 0.8, rng), t + i * E, gain=0.95 if i == 0 else 0.55)
    drums.add(gated_snare(rng), t + 3 * E, pan=0.05, gain=0.7)
    for i in range(10):
        drums.add(hat(rng, 0.02), t + i * E / 2, pan=0.45, gain=0.18 + 0.14 * (i % 2))

# a dry radio blip on the first cut into the dream
weird.add(static_burst(rng, 0.18), 1.98, pan=-0.3, gain=0.5)

# ======================================================== mix the music, tape stop into the knock
mix_music = 0.82 * (music.x + drums.x + 0.85 * retro.x)
mix_music = mix_music + 0.28 * reverb(mix_music, rng) + weird.x
mix_music = tape_stop(mix_music, 9.08, 9.4)          # the tape runs out of power...
mix_music = window(mix_music, 1.9, 9.4, ramp=0.004)   # ...and the knock cuts it dead


# ======================================================== logo: the drop, then a choir behind the wall, very far away
ch = stereo(choir([48 - 0.4, 55 - 0.4, 59 - 0.4, 64 - 0.4], 2.8, rng))
tt = np.arange(ch.shape[0]) / SR
ch = ch * np.clip(tt / 0.9, 0, 1)[:, None]
wall = Bus(); wall.add(ch, 15.3, gain=1.0)
wall_x = window(np.stack([filt(wall.x[:, c], "lp", 420) for c in range(2)], 1), 15.0, 17.8, ramp=0.004)

dream, door = effects(rng)
dream = wow(mix_music + dream, 0.0, 15.0, cents=11)
mix = dream + drop(rng) + wall_x * 0.55 + door + tape_hiss(rng)
out = master_write(mix, os.path.join(HERE, "PENUS-Teaser-18s-v3-audio-A.wav"), rms_db=-18.3)
print("A ok, peak", float(np.max(np.abs(out))))
