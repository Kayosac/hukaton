"""Shared sound for the PENUS teaser score: helpers, the game's sound effects placed on the edit, the end glitch.
Score: teaser_score_b.py (the trailer music kept from v3, extended to the 22 s v4 edit)."""
import os, sys
import numpy as np
from scipy.io import wavfile
from scipy.signal import fftconvolve, resample_poly

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "synth"))  # copies of output/music/v2/src (Кривая тропа v2 instruments)
from synth import *   # noqa: E402,F401,F403
from synth2 import *  # noqa: E402,F401,F403

SFX = os.path.normpath(os.path.join(HERE, "..", "..", "..", "hukaton", "Assets", "Audio", "Sfx"))
sys.path.insert(0, os.path.join(HERE, ".."))
import glitch_plan  # noqa: E402
DUR = glitch_plan.TOTAL / glitch_plan.FPS          # 22.0 s
CUT = glitch_plan.CUT / glitch_plan.FPS            # 21.867 s: the sound ends dead with the picture
N = int((DUR + 4.0) * SR)


class Bus:
    def __init__(self):
        self.x = np.zeros((N, 2))

    def add(self, x, t, pan=0.0, gain=1.0):
        x = np.asarray(x, dtype=float)
        if x.ndim == 1:
            x = np.stack([x * np.cos((pan + 1) * np.pi / 4), x * np.sin((pan + 1) * np.pi / 4)], 1)
        a = int(round(t * SR))
        if a < 0:
            x = x[-a:]; a = 0
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
    k = 2 ** (semitones / 12)
    return np.interp(np.arange(0, len(x) - 1, k), np.arange(len(x)), x)


def fade(x, a=0.005, r=0.01):
    x = np.array(x, dtype=float)
    na, nr = int(a * SR), int(r * SR)
    if na: x[:na] *= np.linspace(0, 1, na)[(slice(None),) + (None,) * (x.ndim - 1)]
    if nr: x[-nr:] *= np.linspace(1, 0, nr)[(slice(None),) + (None,) * (x.ndim - 1)]
    return x


def tail(x, seconds, decay, rng, lp=5000, wet=0.6):
    n = int(seconds * SR)
    t = np.arange(n) / SR
    ir = filt(rng.standard_normal(n), "lp", lp) * np.exp(-t / decay)
    ir /= np.sqrt(np.sum(ir ** 2))
    y = fftconvolve(np.concatenate([x, np.zeros(n)]), ir)[: len(x) + n]
    return np.concatenate([x, np.zeros(n)]) * (1 - wet) + y * wet


def loop_to(x, seconds, xf=0.08):
    n = int(seconds * SR)
    out = np.zeros(n + len(x))
    step = len(x) - int(xf * SR)
    pos = 0
    while pos < n:
        out[pos: pos + len(x)] += fade(x, xf, xf)
        pos += step
    return out[:n]


def window(x, t0, t1, ramp=0.01):
    g = np.zeros(N)
    a, b = int(t0 * SR), int(t1 * SR)
    g[a:b] = 1
    r = int(ramp * SR)
    if r:
        g[a:a + r] = np.linspace(0, 1, r)
        g[b - r:b] = np.linspace(1, 0, r)
    return x * g[:, None]


def reverb(x, rng, seconds=2.6, decay=0.8, lp=5000):
    ir = filt(rng.standard_normal(int(seconds * SR)), "lp", lp) * np.exp(-np.arange(int(seconds * SR)) / SR / decay)
    wet = np.stack([fftconvolve(x[:, c], ir)[:N] for c in range(2)], 1)
    return wet * np.sqrt(np.sum(x ** 2)) / (np.sqrt(np.sum(wet ** 2)) + 1e-9)


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


def tape_hiss(rng):
    t = np.arange(N) / SR
    hiss = np.stack([filt(rng.standard_normal(N), "bp", (2000, 12000)) for _ in range(2)], 1) * 0.0035
    hum = (np.sin(2 * np.pi * 50 * t) + 0.5 * np.sin(2 * np.pi * 100 * t)) * 0.0018
    return window(hiss + hum[:, None], 0.0, CUT, ramp=0.0)


def stereo(x):
    x = np.asarray(x, dtype=float)
    return np.stack([x, x], 1) if x.ndim == 1 else x


# ---------------------------------------------------------------- the game's sound effects on the v3 edit
def effects(rng):
    """Effects under the tape (0–15 s). Nothing plays over the logo: the music carries it to the end."""
    sfx, amb = Bus(), Bus()
    coin = load("shop/coin_single.wav")
    sfx.add(fade(tail(pitch(coin, -12), 2.4, 0.9, rng, lp=4200, wet=0.55), 0.002, 0.3), 0.0, gain=0.95)
    sfx.add(fade(tail(pitch(coin, -5), 1.6, 0.6, rng, lp=7000, wet=0.5), 0.002, 0.3), 0.0, pan=0.15, gain=0.32)
    breath = filt(load("creatures/mimic_breath_loop.wav"), "lp", 1800)
    sfx.add(fade(breath[: int(1.3 * SR)], 0.3, 0.2), 0.15, pan=-0.2, gain=0.14)
    sfx.add(filt(load("ui/transition_whoosh.wav"), "lp", 3500), 0.92, pan=0.1, gain=0.45)        # the fast spin
    sfx.add(tail(load("impacts/heal_chime.wav"), 1.2, 0.5, rng, lp=9000, wet=0.4), 1.62, pan=-0.35, gain=0.6)  # the glint

    flutter = loop_to(pitch(load("cards/card_flutter_loop.wav"), -7), 3.0, xf=0.12)  # slow-motion paper
    tt = np.arange(len(flutter)) / SR
    swirl = 0.6 * np.sin(2 * np.pi * (0.15 + 0.35 * tt / 3.0) * tt)                     # the spin pans it around
    flutter = fade(flutter, 0.25, 0.25)
    sfx.add(np.stack([flutter * np.cos((swirl + 1) * np.pi / 4), flutter * np.sin((swirl + 1) * np.pi / 4)], 1), 2.0, gain=0.5)

    forest, shop_amb = load("ambience/forest_loop.wav"), load("ambience/shop_loop.wav")
    def amb_clip(src, t0, t1, offset, gain):
        amb.add(fade(src[int(offset * SR): int((offset + t1 - t0) * SR)], 0.01, 0.01), t0, gain=gain)
    amb_clip(forest, 2.0, 3.6, 3.0, 0.28)
    amb_clip(shop_amb, 3.6, 6.0, 1.0, 0.5)
    amb_clip(forest, 6.0, 7.0, 9.0, 0.28)
    amb_clip(shop_amb, 7.0, 8.0, 6.0, 0.5)
    amb_clip(forest, 8.0, 9.4, 12.0, 0.25)
    amb_clip(shop_amb, 10.0, 14.9, 10.6, 0.8)

    for t, st in ((5.0, 0), (6.0, -2), (7.0, -4)):               # the eye-matched cuts
        air, hit = cut_thock(rng, st)
        sfx.add(air, t - len(air) / SR, gain=1.0)
        sfx.add(tail(hit, 0.6, 0.18, rng, lp=3000, wet=0.25), t, gain=0.65)
    sfx.add(fade(load("backroom/rope_pull.wav")[: int(0.45 * SR)], 0.002, 0.1), 7.08, pan=0.35, gain=0.32)

    growl = filt(np.concatenate([pitch(load("creatures/wolf_growl.wav"), -3), pitch(load("creatures/wolf_growl.wav"), -4)]), "hp", 70)
    g = Bus(); g.add(growl[: int(1.4 * SR)], 8.0, gain=0.85)
    sfx.x += window(g.x, 0, 9.4, ramp=0.002)
    sfx.x += kill_and_tear(rng)                                    # black, 9.4–10.0
    sfx.add(load("shop/place_item_01.wav"), 10.0, gain=1.25)       # the fang lands on the counter

    tick = load("shop/clock_tick.wav")
    for t, st in ((11.0, -3), (12.0, 0), (13.0, -3), (14.0, 0)):
        sfx.add(pitch(tick, st), t, pan=-0.35, gain=0.75)
    sfx.add(fade(filt(load("voices/peasant_hmm.wav"), "lp", 1500), 0.01, 0.08), 12.08, pan=0.05, gain=0.95)

    return sfx.x + amb.x


# ---------------------------------------------------------------- the eye-matched cuts and the black before the fang
def cut_thock(rng, st=0.0):
    """A soft, deep "dum-tok" for a match cut: air pulled in, then a low tom with a short wooden knock. No ring."""
    k = 2 ** (st / 12)
    n = int(0.6 * SR); t = np.arange(n) / SR
    f = (60 + 75 * np.exp(-t / 0.03)) * k
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.15)
    body = np.tanh(body * 1.8) / np.tanh(1.8)
    knock = np.sin(2 * np.pi * 420 * k * t) * np.exp(-t / 0.016) * 0.42 \
        + np.sin(2 * np.pi * 1130 * k * t) * np.exp(-t / 0.006) * 0.16
    click = filt(rng.standard_normal(n), "bp", (900, 3000)) * np.exp(-t / 0.003) * 0.3
    hit = fade(body * 0.85 + knock + click, 0.0005, 0.05)
    m = int(0.15 * SR); tm = np.arange(m) / SR
    air = filt(rng.standard_normal(m), "bp", (250, 2200)) * (tm / tm[-1]) ** 2.6 * 0.18
    return fade(air, 0.01, 0.002), hit


def kill_and_tear(rng):
    """The black between the jaw and the counter: the goblin dog is struck and dies, then the fang is torn out."""
    k = Bus()
    k.add(fade(filt(pitch(load("impacts/strike_hit_01.wav"), -3), "lp", 5000), 0.001, 0.05), 9.40, gain=0.85)
    k.add(fade(pitch(load("impacts/heavy_hit_02.wav"), -5)[: int(0.35 * SR)], 0.001, 0.1), 9.40, gain=0.45)
    cry = filt(pitch(load("creatures/creature_death.wav"), -3), "lp", 4500)
    k.add(fade(cry[: int(0.5 * SR)], 0.003, 0.14), 9.42, pan=0.05, gain=0.95)
    k.add(fade(pitch(load("creatures/body_thud.wav"), -2)[: int(0.3 * SR)], 0.001, 0.08), 9.62, gain=0.6)
    n = int(0.29 * SR); t = np.arange(n) / SR
    dens = (t / t[-1]) ** 1.5                                       # wet tearing gets denser
    tear = np.zeros(n); i = 0
    while i < n:
        g = int(SR * rng.uniform(0.002, 0.006))
        grain = filt(rng.standard_normal(g), "bp", (rng.uniform(450, 900), rng.uniform(1800, 3800))) * np.hanning(g)
        e = min(n, i + g); tear[i:e] += grain[: e - i] * rng.uniform(0.5, 1.0)
        i += max(30, int(SR * (0.013 - 0.010 * dens[i]) * rng.uniform(0.5, 1.5)))
    tear *= 0.3 + 0.7 * dens
    squelch = filt(rng.standard_normal(n), "bp", (220, 900)) * (0.5 + 0.5 * np.sin(2 * np.pi * 23 * t)) * dens * 0.3
    jitter = filt(rng.standard_normal(n), "lp", 25); jitter /= np.max(np.abs(jitter)) + 1e-9
    strain = filt(np.sign(np.sin(2 * np.pi * np.cumsum(85 + 45 * t / t[-1] + 12 * jitter) / SR)), "lp", 650) * dens * 0.07
    k.add(fade(tear + squelch + strain, 0.02, 0.01), 9.66, gain=1.7)
    k.add(fade(filt(pitch(load("creatures/wolf_snap.wav"), -6), "lp", 4000)[: int(0.22 * SR)], 0.001, 0.05), 9.90, gain=0.8)
    m = int(0.06 * SR); tm = np.arange(m) / SR
    k.add(np.sin(2 * np.pi * np.cumsum(70 + 260 * np.exp(-tm / 0.02)) / SR) * np.exp(-tm / 0.025), 9.93, gain=0.5)
    return window(k.x, 9.4, 10.0, ramp=0.002)


# ---------------------------------------------------------------- the end glitch, frame by frame with the picture
def glitch(x, rng):
    """Breaks the sound on the frames of glitch_plan.PLAN and cuts it dead at CUT."""
    y = x.copy()
    slot = SR // glitch_plan.FPS
    for f, kind in glitch_plan.PLAN:
        a = int(round(f / glitch_plan.FPS * SR)); b = a + slot
        seg = y[a:b].copy()
        if kind == "light":
            seg = np.stack([bitcrush(seg[:, c], 9000, 7) for c in range(2)], 1)
        elif kind == "bands":
            half = seg[: slot // 2]
            seg = np.concatenate([half, half])[:slot]
            seg = np.stack([bitcrush(seg[:, c], 6000, 6) for c in range(2)], 1)
        elif kind == "freeze":
            seg = y[a - slot:a].copy()
        elif kind == "blocks":
            seg = np.stack([bitcrush(seg[:, c], 3500, 5) for c in range(2)], 1)
            seg += stereo(static_burst(rng, (slot + 2) / SR)[:slot]) * 0.25
        elif kind == "dark":
            seg *= 0.0
            seg[:40] += 0.3 * np.linspace(1, 0, 40)[:, None]
        elif kind in ("heavy", "shred"):
            n = slot // (6 if kind == "heavy" else 12)
            piece = seg[:n]
            seg = np.concatenate([piece * (0.9 ** k) for k in range(slot // n + 1)])[:slot]
            seg = np.stack([bitcrush(seg[:, c], 2500 if kind == "heavy" else 1800, 4) for c in range(2)], 1)
            seg += stereo(static_burst(rng, (slot + 2) / SR)[:slot]) * (0.35 if kind == "heavy" else 0.5)
        y[a:b] = seg
    y[int(round(CUT * SR)):] = 0.0
    return y


def master_write(mix, path, rms_db=-19.5):
    mix = mix[: int(DUR * SR)].copy()
    mix[int(round(CUT * SR)):] = 0.0
    rms = np.sqrt(np.mean(mix[: int(CUT * SR)] ** 2))
    mix = mix * 10 ** (rms_db / 20) / rms
    mix = np.tanh(mix * 1.25) / np.tanh(1.25)
    pk = np.max(np.abs(mix))
    if pk > 0.89:
        mix *= 0.89 / pk
    out48 = resample_poly(mix, 160, 147, axis=0)
    wavfile.write(path, 48000, (np.clip(out48, -1, 1) * 32767).astype(np.int16))
    return mix
