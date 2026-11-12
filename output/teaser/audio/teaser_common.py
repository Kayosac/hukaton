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
    amb_clip(shop_amb, 9.4, 14.9, 10.0, 0.8)

    bell = load("shop/door_bell.wav")
    for t, st, gn in ((5.0, 0, 0.62), (6.0, -2, 0.62), (7.0, -4, 0.66)):
        b = filt(pitch(bell, st), "lp", 5200 - 700 * (-st))
        w = 1 + 0.004 * np.sin(2 * np.pi * 5.5 * np.arange(len(b)) / SR)
        b = np.interp(np.cumsum(w) - w[0], np.arange(len(b)), b)
        sfx.add(fade(b[: int(0.98 * SR)], 0.001, 0.12), t, pan=0.1, gain=gn)
    sfx.add(fade(load("backroom/rope_pull.wav")[: int(0.45 * SR)], 0.002, 0.1), 7.08, pan=0.35, gain=0.32)

    growl = filt(np.concatenate([pitch(load("creatures/wolf_growl.wav"), -3), pitch(load("creatures/wolf_growl.wav"), -4)]), "hp", 70)
    g = Bus(); g.add(growl[: int(1.4 * SR)], 8.0, gain=0.85)
    sfx.x += window(g.x, 0, 9.4, ramp=0.002)
    sfx.add(load("shop/place_item_01.wav"), 9.4, gain=1.25)

    tick = load("shop/clock_tick.wav")
    for k, t in enumerate((10.0, 11.0, 12.0, 13.0, 14.0)):
        sfx.add(pitch(tick, 0 if k % 2 == 0 else -3), t, pan=-0.35, gain=0.75)
    sfx.add(fade(filt(load("voices/peasant_hmm.wav"), "lp", 1500), 0.01, 0.08), 12.08, pan=0.05, gain=0.95)

    return sfx.x + amb.x


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
