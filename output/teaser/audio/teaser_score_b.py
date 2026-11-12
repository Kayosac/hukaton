"""PENUS teaser «А хозяин не придёт?» — score B: new music written for this trailer.

Same world as «Кривая тропа» (E phrygian, crooked meters, flute, kalimba, music box, choir, retrowave pads, VHS),
new material: the sigh motif E–F–E–B. A music box whispers it while the coin comes out of the dark; slow-motion
choir and flute carry the card through the upside-down forest into the shop; the three faces are struck by low
chords falling Em → C → B, the mouth is a rising drum roll that stutters and is cut dead by the blow that kills the
dog (black: its death cry, the fang torn out, then the knock of the fang on the counter); the question
waits over a sub hum and a speeding heartbeat. v4: no drop on the logo. The heartbeat slows, the music box sighs
the motif while the logo turns out of the dark (its last note on the glint), and the theme comes in for good —
choir, kalimba, the 5/8 pulse, the flute singing the motif over Em F | Em C B7 Em | F Em C B7 — and is cut dead on
the bar line where Em would come back, while the picture glitches more and more and finally melts (glitch_plan.py). Effects and the glitch are in teaser_common.
Run: python3 teaser_score_b.py  ->  PENUS-Teaser-25s-v6-audio.wav (48 kHz stereo, about -14 LUFS)
"""
from teaser_common import *  # noqa: F401,F403

rng = np.random.default_rng(7700)


def hz(m, cents=0.0):
    return mtof(m + cents / 100)


def env(n, a, r):
    e = np.ones(n)
    na, nr = max(1, int(a * SR)), max(1, int(r * SR))
    e[:na] = np.linspace(0, 1, na)
    e[-nr:] *= np.linspace(1, 0, nr)
    return e


music, drums, box, lead = Bus(), Bus(), Bus(), Bus()
MOTIF = [76, 77, 76, 71]                         # E5 F5 E5 B4: the sigh

# ---------------------------------------------------------------- 0–2: the coin out of the dark
dr = accordion(hz(28), 2.3, rng, nh=10, wet=0.4)
dr = filt(dr * np.clip(np.arange(len(dr)) / SR / 1.6, 0, 1), "lp", 190)
music.add(dr, 0.05, gain=0.95)
for i, (t, m) in enumerate(zip((0.30, 0.62, 0.94, 1.26), [x + 12 for x in MOTIF])):
    x = bitcrush(toy_piano(hz(m, -40 + rng.uniform(-8, 8)), 0.32, rng), 9000, 6)
    box.add(filt(x, "lp", 3000), t, pan=-0.3 + 0.2 * i, gain=0.55)
x = reverse(kalimba(hz(83), 0.5, rng)); music.add(x, 1.62 - len(x) / SR, pan=0.4, gain=0.6)
for m, p in ((100, -0.4), (107, 0.3)):
    music.add(fm_bell(hz(m, -15), 1.0, rng), 1.62, pan=p, gain=0.35)

# ---------------------------------------------------------------- 2–5: slow motion, forest -> shop
ch = stereo(choir([52 - 0.3, 59 - 0.3, 64 - 0.3, 66 - 0.3], 3.2, rng))
ch *= env(len(ch), 0.6, 0.4)[:, None]
music.add(np.stack([filt(ch[:, c], "lp", 2200) for c in range(2)], 1), 1.95, gain=0.9)
flute = [(2.05, 0.72, 71), (2.80, 0.38, 72), (3.20, 0.38, 71), (3.60, 0.58, 69), (4.20, 0.38, 67), (4.60, 0.42, 65)]
for st, x in legato([(t, d, m - 0.25) for t, d, m in flute], rng, kind="flute", glide=0.09, vib_cents=26, vib_rate=4.2):
    music.add(x, st, pan=-0.1, gain=0.95)
for k in range(15):                                # half-time kalimba, Em then Cmaj7 on the shop cut
    t = 2.0 + k * 0.2
    root, tones = (52, [0, 7, 3, 7]) if t < 3.6 else (48, [0, 7, 4, 11])
    music.add(kalimba(hz(root + 12 + tones[k % 4], rng.uniform(-12, 12)), 0.4, rng), t, pan=0.35 if k % 2 else -0.3, gain=0.42)
for v in (48, 55, 59, 64):
    pad = supersaw(hz(v, rng.uniform(-3, 3)), 1.45, rng, maxf=2600)
    pad *= env(len(pad), 0.35, 0.1)[:, None]
    music.add(np.stack([filt(pad[:, c], "lp", 1400) for c in range(2)], 1), 3.6, gain=0.45)
for t, gn in ((2.0, 0.8), (2.8, 0.5), (3.6, 0.95), (4.4, 0.55)):
    drums.add(frame_drum(rng, 70), t, gain=gn)
x = reverse(crash(rng, 0.9)); drums.add(x, 5.0 - len(x) / SR + 0.01, pan=0.15, gain=0.75)

# ---------------------------------------------------------------- 5–8: three faces struck by falling chords, 8–9.4 the mouth
CH = {5.0: [40, 47, 52, 55], 6.0: [36, 43, 48, 52], 7.0: [35, 42, 47, 51], 8.0: [29, 36, 41, 45]}
for t, notes in CH.items():
    for v in notes:
        st = supersaw(hz(v, rng.uniform(-4, 4)), 0.9, rng, maxf=3000)
        st *= (np.exp(-np.arange(len(st)) / SR / 0.32))[:, None]
        music.add(np.stack([filt(st[:, c], "lp", 1800) for c in range(2)], 1), t, gain=0.7)
    drums.add(kick808(rng, 160, 42, 0.45), t, gain=1.05)
    drums.add(frame_drum(rng, 58), t, gain=0.9)
for t0, root in ((5.0, 40), (6.0, 36), (7.0, 35)):     # 5/8 pulse inside each face: 2+3
    for i in range(5):
        retro_x = saw_bass(hz(root, rng.uniform(-4, 4)), 0.16, rng)
        music.add(retro_x, t0 + i * 0.2, gain=0.8 if i in (0, 2) else 0.5)
        drums.add(hat(rng, 0.02), t0 + i * 0.2 + 0.1, pan=0.45, gain=0.22)
        if i in (0, 2):
            drums.add(woodblock(rng, 900 + rng.uniform(-30, 30)), t0 + i * 0.2, pan=-0.35, gain=0.35)
    drums.add(gated_snare(rng), t0 + 0.4, pan=0.05, gain=0.75)
for i, m in enumerate([x + 12 for x in MOTIF[:3]]):
    x = bitcrush(toy_piano(hz(m, -40), 0.22, rng), 9000, 6)
    box.add(filt(x, "lp", 3200), 7.45 + i * 0.16, pan=0.5, gain=0.35)

dr = accordion(hz(29), 1.6, rng, nh=10, wet=0.5); music.add(filt(dr, "lp", 300), 8.0, gain=2.0)
t = 8.0; k = 0
while t < 9.32:                                   # the roll speeds up into the knock
    step = 0.1 if t < 8.7 else (0.05 if t < 9.1 else 0.033)
    drums.add(snare(rng, 200, 0.06, 0.8), t, pan=0.05 * ((k % 2) * 2 - 1), gain=0.25 + 0.6 * (t - 8.0) / 1.4)
    t += step; k += 1
n = int(1.4 * SR); tt = np.arange(n) / SR
riser = filt(rng.standard_normal(n), "bp", (500, 6000)) * (tt / tt[-1]) ** 2 * 0.3 \
    + np.sin(2 * np.pi * np.cumsum(hz(41) * 2 ** (tt / tt[-1] * 3)) / SR) * (tt / tt[-1]) ** 2 * 0.12
music.add(riser, 8.0, gain=1.0)

mix_music = music.x + drums.x + 0.8 * box.x
mix_music = mix_music + 0.25 * reverb(mix_music, rng, 2.4, 0.7)
mix_music = stutter(mix_music, 9.12, 0.045, 4)
mix_music = window(mix_music, 0.0, 9.4, ramp=0.002)       # cut dead by the blow (black, 9.4–10.0)

# ---------------------------------------------------------------- 9.4–15: the question waits
quiet = Bus()
n = int(5.3 * SR); tt = np.arange(n) / SR
hum = np.sin(2 * np.pi * 41.2 * tt) * (0.5 + 0.5 * np.sin(2 * np.pi * 0.23 * tt)) * env(n, 1.5, 0.3) * 0.12
quiet.add(hum, 9.6, gain=1.0)
hb = load("player/heartbeat_low.wav")
for t, gn in ((12.95, 0.45), (13.75, 0.55), (14.35, 0.65), (14.78, 0.75)):
    quiet.add(hb, t, gain=gn)
for i, m in enumerate((88, 89)):
    x = bitcrush(toy_piano(hz(m, -45), 0.5, rng), 8000, 6)
    quiet.add(filt(x, "lp", 2500), 13.2 + i * 0.55, pan=0.6, gain=0.18)
quiet_x = window(quiet.x, 9.4, 14.92, ramp=0.01)

# ---------------------------------------------------------------- 15–21.87: the theme comes in under the logo
theme, tdrums, tbox = Bus(), Bus(), Bus()
for t, gn in ((15.10, 0.7), (15.62, 0.5), (16.30, 0.35)):            # the heartbeat lets go
    tbox.add(hb, t, gain=gn)
n = int(2.4 * SR); tt = np.arange(n) / SR
theme.add(np.sin(2 * np.pi * 41.2 * tt) * env(n, 0.05, 1.2) * 0.12, 14.9, gain=1.0)   # the hum hands over to the bass
for t, m in ((15.15, 88), (15.45, 89), (15.75, 88), (16.36, 83)):    # the sigh on the turning logo, B on the glint
    x = bitcrush(toy_piano(hz(m, -40 + rng.uniform(-6, 6)), 0.5, rng), 9000, 6)
    tbox.add(filt(x, "lp", 3200), t, pan=0.25 if m != 83 else -0.15, gain=0.6)
for m, p in ((100, -0.4), (107, 0.35), (95, 0.1)):
    theme.add(fm_bell(hz(m, -15), 1.2, rng), 16.36, pan=p, gain=0.3)

BARS = [(15.0, "Em"), (16.0, "F"), (17.0, "Em"), (18.0, "C"), (19.0, "B7"), (20.0, "Em"),
        (21.0, "F"), (22.0, "Em"), (23.0, "C"), (24.0, "B7")]                   # cut dead on the bar line at 25.0
CHORDS = {"Em": [40, 47, 52, 55, 59], "F": [41, 48, 53, 57, 64], "C": [36, 43, 48, 52, 59], "B7": [35, 42, 47, 51, 57]}
for k, (t0, name) in enumerate(BARS):
    notes = CHORDS[name]
    lift = k / (len(BARS) - 1)                                       # everything grows towards the cut
    ch = stereo(choir([v + 12 for v in notes[2:]], 1.05, rng))
    theme.add(np.stack([filt(ch[:, c], "lp", 2400) for c in range(2)], 1), t0 - (0.25 if k else 0.0), gain=0.75 + 0.35 * lift)
    if t0 >= 21.0:                                                   # the choir opens up an octave for the second phrase
        hi = stereo(choir([v + 24 for v in notes[2:4]], 1.05, rng))
        theme.add(np.stack([filt(hi[:, c], "lp", 3200) for c in range(2)], 1), t0 - 0.25, gain=0.35 + 0.3 * lift)
    if t0 >= 16.0:                                                   # kalimba in eighths
        tones = [notes[2], notes[3], notes[4], notes[3], notes[4] + 5 if name != "B7" else notes[4] + 6]
        for i in range(5):
            theme.add(kalimba(hz(tones[i] + 12, rng.uniform(-10, 10)), 0.4, rng), t0 + i * 0.2,
                      pan=0.35 if i % 2 else -0.3, gain=0.34 + 0.12 * lift)
        tdrums.add(frame_drum(rng, 70), t0, gain=0.55 + 0.3 * lift)
    if t0 >= 17.0:                                                   # the pulse: 5/8 as 2+3
        for v in notes[:4]:
            pad = supersaw(hz(v + 12, rng.uniform(-3, 3)), 1.02, rng, maxf=2400)
            pad *= env(len(pad), 0.06, 0.08)[:, None]
            theme.add(np.stack([filt(pad[:, c], "lp", 1300 + 900 * lift) for c in range(2)], 1), t0, gain=0.22 + 0.2 * lift)
        for i in range(5):
            theme.add(saw_bass(hz(notes[0], rng.uniform(-4, 4)), 0.16, rng), t0 + i * 0.2, gain=(0.7 if i in (0, 2) else 0.42) * (0.8 + 0.3 * lift))
            tdrums.add(hat(rng, 0.02), t0 + i * 0.2 + 0.1, pan=0.45, gain=0.16 + 0.1 * lift)
        tdrums.add(kick808(rng, 150, 44, 0.4), t0, gain=0.75 + 0.3 * lift)
        tdrums.add(kick808(rng, 150, 44, 0.3), t0 + 0.4, gain=0.5 + 0.3 * lift)
        tdrums.add(woodblock(rng, 900 + rng.uniform(-30, 30)), t0 + 0.4, pan=-0.35, gain=0.25)
    if t0 >= 19.0:
        tdrums.add(gated_snare(rng), t0 + 0.4, pan=0.05, gain=0.45 + 0.25 * lift)
    if t0 >= 22.0:                                                   # sixteenth hats push the last bars
        for i in range(10):
            tdrums.add(hat(rng, 0.012), t0 + i * 0.1 + 0.05, pan=-0.4, gain=0.08 + 0.06 * lift)
flute = [(17.0, 0.58, 76), (17.6, 0.38, 77), (18.0, 0.58, 76), (18.6, 0.38, 71), (19.0, 0.38, 72), (19.4, 0.18, 71),
         (19.6, 0.38, 69), (20.0, 0.58, 71), (20.6, 0.38, 67),
         (21.0, 0.38, 77), (21.4, 0.58, 76), (22.0, 0.58, 79), (22.6, 0.38, 77), (23.0, 0.58, 76), (23.6, 0.38, 72),
         (24.0, 0.38, 71), (24.4, 0.18, 69), (24.6, 0.4, 75)]                       # ends on D#, the bar before Em
for st_, x in legato([(t, d, m - 0.25) for t, d, m in flute], rng, kind="flute", glide=0.07, vib_cents=22, vib_rate=4.6):
    theme.add(x, st_, pan=-0.1, gain=0.95)
for st_, x in legato([(t, d, m - 12) for t, d, m in flute if t >= 19.0], rng, kind="saw", glide=0.05, vib_cents=8, vib_rate=5.5):
    theme.add(filt(x, "lp", 2600), st_, pan=0.12, gain=0.32)
for at, dur in ((17.0, 0.9), (21.0, 1.1), (25.0, 1.4)):             # reverse swells into the pulse, the second phrase, the cut
    x = reverse(crash(rng, dur)); tdrums.add(x, at - len(x) / SR + 0.01, pan=0.15, gain=0.55)

theme_x = theme.x + tdrums.x + 0.8 * tbox.x
theme_x = theme_x + 0.22 * reverb(theme_x, rng, 2.2, 0.65)
theme_x = window(theme_x, 14.88, DUR, ramp=0.01)
theme_x = wow(theme_x, 15.0, CUT, cents=7)

# ---------------------------------------------------------------- mix
dream = effects(rng)
dream = wow(mix_music + quiet_x + dream, 0.0, 15.0, cents=12)
mix = glitch(dream + theme_x + tape_hiss(rng), rng)
out = master_write(mix, os.path.join(HERE, "PENUS-Teaser-25s-v6-audio.wav"), rms_db=-17.2)
print("B v6 ok, peak", float(np.max(np.abs(out))), "samples", len(out))
