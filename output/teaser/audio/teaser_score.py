"""PENUS teaser «А хозяин не придёт?» — music and sound, 18.000 s, scored to the 30 fps edit.

Music is new, written for this edit with the «Кривая тропа» v2 instruments (synth/ = copies of output/music/v2/src):
detuned flute + kalimba + drone in 7/8 enter on the cut to the upside-down forest (2.0 s); the flute holds one
note across the cut into the shop (3.6 s); the three faces (5/6/7 s) get an 808 and a saw bass stepping down
with the shop bell (0, -2, -4 semitones); under the mouth only a low drone and the growl remain; the tape stops
into the wooden knock (9.4 s). After the logo a faint choir «behind the wall» sits under the real door bell.
Sound effects are the game's own (hukaton/Assets/Audio/Sfx), placed and treated per the storyboard.
Run: python3 teaser_score.py  ->  PENUS-Teaser-18s-v1-audio.wav (48 kHz stereo, about -14 LUFS)
"""
import os, sys
import numpy as np
from scipy.io import wavfile
from scipy.signal import fftconvolve, resample_poly

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "synth"))  # copies of output/music/v2/src (Кривая тропа v2 instruments)
from synth import *   # noqa: E402,F401  SR=44100, kalimba, accordion, legato, filt, mtof, ...
from synth2 import *  # noqa: E402,F401  supersaw, saw_bass, kick808, gated_snare, choir, static_burst, ...

SFX = os.path.normpath(os.path.join(HERE, "..", "..", "..", "hukaton", "Assets", "Audio", "Sfx"))
DUR = 18.0
N = int((DUR + 4.0) * SR)
rng = np.random.default_rng(20261112)

E = 0.2          # eighth note, s (7/8 = 2+2+3, as in «Кривая тропа»)
T0 = 2.0         # music grid starts on the cut into the forest; every cut sits on an eighth


class Bus:
    def __init__(self):
        self.x = np.zeros((N, 2))

    def add(self, x, t, pan=0.0, gain=1.0):
        x = np.asarray(x, dtype=float)
        if x.ndim == 1:
            l = np.cos((pan + 1) * np.pi / 4)
            r = np.sin((pan + 1) * np.pi / 4)
            x = np.stack([x * l, x * r], 1)
        a = int(round(t * SR))
        if a < 0:
            x = x[-a:]
            a = 0
        b = min(N, a + len(x))
        if b > a:
            self.x[a:b] += x[: b - a] * gain


def load(name):
    sr, x = wavfile.read(os.path.join(SFX, name))
    x = x.astype(float) / (32768.0 if x.dtype == np.int16 else 1.0)
    if x.ndim == 2:
        x = x.mean(1)
    if sr != SR:
        x = resample_poly(x, SR, sr)
    return x


def pitch(x, semitones):
    """Resample: lower is longer, like a slowed tape."""
    k = 2 ** (semitones / 12)
    idx = np.arange(0, len(x) - 1, k)
    return np.interp(idx, np.arange(len(x)), x)


def fade(x, a=0.005, r=0.01):
    x = x.copy()
    na, nr = int(a * SR), int(r * SR)
    if na: x[:na] *= np.linspace(0, 1, na)
    if nr: x[-nr:] *= np.linspace(1, 0, nr)
    return x


def tail(x, seconds, decay, lp=5000, wet=0.6):
    n = int(seconds * SR)
    t = np.arange(n) / SR
    ir = filt(rng.standard_normal(n), "lp", lp) * np.exp(-t / decay)
    ir /= np.sqrt(np.sum(ir ** 2))
    y = fftconvolve(np.concatenate([x, np.zeros(n)]), ir)[: len(x) + n]
    out = np.concatenate([x, np.zeros(n)]) * (1 - wet) + y * wet
    return out


def loop_to(x, seconds, xf=0.08):
    """Tile a short loop to a length with crossfades."""
    n = int(seconds * SR)
    out = np.zeros(n + len(x))
    step = len(x) - int(xf * SR)
    pos = 0
    while pos < n:
        out[pos: pos + len(x)] += fade(x, xf, xf)
        pos += step
    return out[:n]


def window(x, t0, t1, ramp=0.01):
    """Hard edit of a bus to [t0, t1) with tiny ramps (dry cuts)."""
    g = np.zeros(N)
    a, b = int(t0 * SR), int(t1 * SR)
    g[a:b] = 1
    r = int(ramp * SR)
    if r:
        g[a:a + r] = np.linspace(0, 1, r)
        g[b - r:b] = np.linspace(1, 0, r)
    return x * g[:, None]


def crooked(midi, cents=12):
    return mtof(midi + rng.uniform(-cents, cents) / 100)


def g(eighth):
    return T0 + eighth * E


music, drums, retro, weird = Bus(), Bus(), Bus(), Bus()
sfx, amb, final = Bus(), Bus(), Bus()

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
wet = np.stack([fftconvolve(mix_music[:, c], filt(rng.standard_normal(int(2.6 * SR)), "lp", 5000)
                            * np.exp(-np.arange(int(2.6 * SR)) / SR / 0.8))[:N] for c in range(2)], 1)
wet *= np.sqrt(np.sum(mix_music ** 2)) / (np.sqrt(np.sum(wet ** 2)) + 1e-9)
mix_music = mix_music + 0.28 * wet + weird.x
mix_music = tape_stop(mix_music, 9.08, 9.4)          # the tape runs out of power...
mix_music = window(mix_music, 1.9, 9.4, ramp=0.004)   # ...and the knock cuts it dead

# ======================================================== logo: a choir behind the wall, very far away
ch = choir([48 - 0.4, 55 - 0.4, 59 - 0.4, 64 - 0.4], 3.2, rng)
ch = np.asarray(ch)
if ch.ndim == 1:
    ch = np.stack([ch, ch], 1)
tt = np.arange(ch.shape[0]) / SR
ch = ch * np.clip(tt / 1.2, 0, 1)[:, None]
wall = Bus(); wall.add(ch, 15.05, gain=1.0)
wall_x = np.stack([filt(wall.x[:, c], "lp", 420) for c in range(2)], 1)
wall_x = window(wall_x, 15.0, 17.8, ramp=0.004)

# ======================================================== SOUND EFFECTS (the game's own)
coin = load("shop/coin_single.wav")
low = tail(pitch(coin, -12), 2.4, 0.9, lp=4200, wet=0.55)           # the tiny coin rings like a bell
mid = tail(pitch(coin, -5), 1.6, 0.6, lp=7000, wet=0.5)             # keeps mid overtones for phone speakers
sfx.add(fade(low, 0.002, 0.3), 0.0, gain=0.95)
sfx.add(fade(mid, 0.002, 0.3), 0.0, pan=0.15, gain=0.32)

breath = filt(load("creatures/mimic_breath_loop.wav"), "lp", 1800)
sfx.add(fade(breath[: int(1.6 * SR)], 0.4, 0.2), 0.4, pan=-0.2, gain=0.16)

flutter = loop_to(load("cards/card_flutter_loop.wav"), 3.0)          # paper, continuous across the cut
tt = np.arange(len(flutter)) / SR
pan_lr = np.clip(-0.85 + 1.7 * tt / 3.0, -1, 1)
flutter = fade(flutter, 0.12, 0.25)
fl = np.stack([flutter * np.cos((pan_lr + 1) * np.pi / 4), flutter * np.sin((pan_lr + 1) * np.pi / 4)], 1)
sfx.add(fl, 2.0, gain=0.55)

forest = load("ambience/forest_loop.wav")
shop_amb = load("ambience/shop_loop.wav")
def amb_clip(src, t0, t1, offset, gain):
    seg = src[int(offset * SR): int((offset + t1 - t0) * SR)]
    amb.add(fade(seg, 0.01, 0.01), t0, gain=gain)
amb_clip(forest, 2.0, 3.6, 3.0, 0.28)
amb_clip(shop_amb, 3.6, 6.0, 1.0, 0.5)
amb_clip(forest, 6.0, 7.0, 9.0, 0.28)
amb_clip(shop_amb, 7.0, 8.0, 6.0, 0.5)
amb_clip(forest, 8.0, 9.4, 12.0, 0.25)
amb_clip(shop_amb, 9.4, 15.0, 10.0, 0.8)
amb_clip(shop_amb, 16.25, 17.8, 20.0, 0.7)

bell = load("shop/door_bell.wav")
for t, st, gn in ((5.0, 0, 0.62), (6.0, -2, 0.62), (7.0, -4, 0.66)):
    b = pitch(bell, st)
    b = filt(b, "lp", 5200 - 700 * (-st))                            # a worn recording, each a bit darker
    w = 1 + 0.004 * np.sin(2 * np.pi * 5.5 * np.arange(len(b)) / SR)
    b = np.interp(np.cumsum(w) - w[0], np.arange(len(b)), b)
    sfx.add(fade(b[: int(0.98 * SR)], 0.001, 0.12), t, pan=0.1, gain=gn)

sfx.add(fade(load("backroom/rope_pull.wav")[: int(0.45 * SR)], 0.002, 0.1), 7.08, pan=0.35, gain=0.32)

growl = pitch(load("creatures/wolf_growl.wav"), -3)
gr = np.concatenate([growl, pitch(load("creatures/wolf_growl.wav"), -4)])
gr = filt(gr, "hp", 70)
sfx.add(gr[: int(1.4 * SR)], 8.0, gain=0.85)                          # cut by the knock (window below)

knock = load("shop/place_item_01.wav")
sfx_knock = Bus(); sfx_knock.add(knock, 9.4, gain=1.25)

tick = load("shop/clock_tick.wav")
for k, t in enumerate((10.0, 11.0, 12.0, 13.0, 14.0)):
    sfx.add(pitch(tick, 0 if k % 2 == 0 else -3), t, pan=-0.35, gain=0.75)

hmm = filt(load("voices/peasant_hmm.wav"), "lp", 1500)
sfx.add(fade(hmm, 0.01, 0.08), 12.08, pan=0.05, gain=0.95)

# the real door: natural bell, then the door and one board under someone we never see
final.add(bell, 16.3, pan=0.25, gain=0.95)
final.add(pitch(load("player/footstep_creaky_03.wav"), -5), 16.92, pan=0.3, gain=0.5)
final.add(load("player/footstep_creaky_01.wav"), 17.42, pan=0.15, gain=0.62)

# ======================================================== edit the effects to the cuts
# the growl must die exactly on the knock; other effects before 9.4 are short anyway
sfx_x = sfx.x.copy()
cut = int(9.4 * SR)
gw = Bus(); gw.add(gr[: int(1.4 * SR)], 8.0, gain=0.85)
sfx_x[cut:] -= gw.x[cut:]
sfx_x += sfx_knock.x

# ======================================================== tape: wow + hiss on the dream, clean real door at the end
def wow(x, t0, t1, cents=10, rate=0.9, flutter_c=3, flutter_r=7.3):
    y = x.copy()
    a, b = int(t0 * SR), int(t1 * SR)
    seg = x[a:b]
    t = np.arange(len(seg)) / SR
    dev = (2 ** (cents / 1200) - 1) / (2 * np.pi * rate) * np.sin(2 * np.pi * rate * t) \
        + (2 ** (flutter_c / 1200) - 1) / (2 * np.pi * flutter_r) * np.sin(2 * np.pi * flutter_r * t)
    pos = np.clip((t + dev) * SR, 0, len(seg) - 1)
    for c in range(2):
        y[a:b, c] = np.interp(pos, np.arange(len(seg)), seg[:, c])
    return y

dream = mix_music + sfx_x + amb.x
dream = wow(dream, 0.0, 15.0, cents=11)
mix = dream + wall_x * 0.55 + final.x

t = np.arange(N) / SR
hiss = np.stack([filt(rng.standard_normal(N), "bp", (2000, 12000)) for _ in range(2)], 1) * 0.0035
hum = (np.sin(2 * np.pi * 50 * t) + 0.5 * np.sin(2 * np.pi * 100 * t)) * 0.0018
mix += window(hiss + hum[:, None], 0.0, 17.8, ramp=0.004)
mix = window(mix, 0.0, 17.8, ramp=0.003)                    # dead cut with the creak, 0.2 s of silence
mix = mix[: int(DUR * SR)]

# ======================================================== master
rms = np.sqrt(np.mean(mix[: int(17.8 * SR)] ** 2))
mix *= 10 ** (-19.5 / 20) / rms
mix = np.tanh(mix * 1.25) / np.tanh(1.25)
pk = np.max(np.abs(mix))
if pk > 0.89:
    mix *= 0.89 / pk
out48 = resample_poly(mix, 160, 147, axis=0)
wavfile.write(os.path.join(HERE, "PENUS-Teaser-18s-v1-audio.wav"), 48000, (np.clip(out48, -1, 1) * 32767).astype(np.int16))
print("ok", len(out48) / 48000, "s, peak", float(np.max(np.abs(mix))), "rms dB", 20 * np.log10(np.sqrt(np.mean(mix ** 2))))
