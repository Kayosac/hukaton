"""Extra voices and effects for v2: retrowave synths and weirdcore/internetcore textures.
All synthesized; all effects are circular so the loop stays seamless."""
import numpy as np
from scipy.signal import butter, sosfilt
from synth import SR, mtof, env_adsr, filt, phase_from_freq


def saw_add(freq, maxf=5000, nh=40, phase0=0.0):
    """Band-limited saw from an instantaneous-frequency array."""
    ph = phase_from_freq(freq) + phase0
    f0 = float(np.max(freq))
    out = np.zeros(len(freq))
    for k in range(1, nh + 1):
        if f0 * k > maxf:
            break
        out += np.sin(k * ph) / k
    return out


def supersaw(f, dur, rng, voices=5, spread=14, maxf=4000):
    n = int((dur + 0.6) * SR)
    t = np.arange(n) / SR
    out = np.zeros((n, 2))
    for v in range(voices):
        det = (v - (voices - 1) / 2) / ((voices - 1) / 2) * spread
        drift = 3 * np.sin(2 * np.pi * rng.uniform(0.1, 0.3) * t + rng.uniform(0, 6))
        x = saw_add(np.full(n, f) * 2 ** ((det + drift) / 1200), maxf=maxf, phase0=rng.uniform(0, 6.28))
        pan = (v / (voices - 1)) * 2 - 1
        out[:, 0] += x * np.cos((pan * 0.8 + 1) * np.pi / 4)
        out[:, 1] += x * np.sin((pan * 0.8 + 1) * np.pi / 4)
    e = env_adsr(n, a=0.4, d=0.5, s=0.9, r=0.55, hold=dur)
    return out * e[:, None] * 0.08


def saw_bass(f, dur, rng):
    n = int((dur + 0.05) * SR)
    t = np.arange(n) / SR
    x = saw_add(np.full(n, f), maxf=2500)
    x = x + 0.6 * np.sin(phase_from_freq(np.full(n, f / 2)))  # sub
    fe = np.exp(-t / 0.06)
    x = filt(x, "lp", 380) * (1 - fe) + filt(x, "lp", 1600) * fe
    return x * env_adsr(n, a=0.003, d=0.12, s=0.55, r=0.03, hold=dur) * 0.32


def kick808(rng, f0=150, f1=48, dec=0.35):
    t = np.arange(int(0.7 * SR)) / SR
    freq = f1 + (f0 - f1) * np.exp(-t / 0.025)
    x = np.tanh(1.8 * np.sin(phase_from_freq(freq)) * np.exp(-t / dec))
    return x * 0.6


def gated_snare(rng, gate=0.24):
    """80s gated-reverb snare: bright body + dense noise room chopped off hard."""
    n = int(0.4 * SR)
    t = np.arange(n) / SR
    body = filt(rng.standard_normal(n), "bp", (900, 8000)) * np.exp(-t / 0.08)
    body += np.sin(2 * np.pi * 200 * t) * np.exp(-t / 0.04) * 0.8
    room = filt(rng.standard_normal(n), "bp", (500, 6000)) * 0.55 * np.clip(t / 0.01, 0, 1)
    g = np.clip((gate - t) / 0.015, 0, 1)
    return (body + room * g) * 0.32


def choir(midis, dur, rng, vowel_from=(730, 1090, 2440), vowel_to=(300, 870, 2240)):
    """Slowed 'ahh -> ooh' formant choir pad (the internet 'slowed + reverb' feel)."""
    n = int((dur + 1.2) * SR)
    t = np.arange(n) / SR
    src = np.zeros(n)
    for m in midis:
        for v in range(3):
            vib = 12 * np.sin(2 * np.pi * (4.6 + v * 0.4) * t + rng.uniform(0, 6))
            freq = mtof(m + (rng.uniform(-8, 8) + vib) / 100) * np.ones(n)
            src += saw_add(freq, maxf=6000, phase0=rng.uniform(0, 6))
    mix = np.clip(t / dur, 0, 1)
    out = np.zeros(n)
    # two static vowel filters, cross-faded -> vowel morph
    for vw, w in ((vowel_from, 1 - mix), (vowel_to, mix)):
        y = np.zeros(n)
        for i, fc in enumerate(vw):
            y += filt(src, "bp", (fc * 0.88, fc * 1.12)) * (1.0, 0.5, 0.25)[i]
        out += y * w
    e = env_adsr(n, a=0.9, d=0.5, s=0.95, r=1.0, hold=dur)
    return out * e * 0.05


def fm_bell(f, dur, rng, ratio=3.5, index=2.2):
    n = int((dur + 2.5) * SR)
    t = np.arange(n) / SR
    mod = np.sin(2 * np.pi * f * ratio * t) * index * np.exp(-t / 0.6)
    x = np.sin(2 * np.pi * f * t + mod) * np.exp(-t / 1.4)
    return x * np.clip(t / 0.004, 0, 1) * 0.18


def beep(f, dur):
    t = np.arange(int(dur * SR)) / SR
    return np.sign(np.sin(2 * np.pi * f * t)) * 0.05 * np.clip(np.minimum(t, dur - t) / 0.004, 0, 1)


def modem(rng, dur=2.6):
    """Generic dial-up-style handshake: tone, warbling FSK, scrambled noise. Telephone band."""
    n = int(dur * SR)
    t = np.arange(n) / SR
    x = np.zeros(n)
    a = int(0.45 * SR)
    x[:a] = np.sin(2 * np.pi * 2100 * t[:a])  # answer tone
    b = int(1.2 * SR)
    sym = (np.floor(t[a:b] * 300) % 2) * 2 - 1
    sym = sym * np.sign(np.sin(2 * np.pi * 7.3 * t[a:b]))
    x[a:b] = np.sin(phase_from_freq(1650 + 200 * sym))  # FSK warble
    c = int(1.6 * SR)
    x[b:c] = np.sin(2 * np.pi * 1200 * t[b:c] + 3 * np.sin(2 * np.pi * 600 * t[b:c]))
    x[c:] = rng.standard_normal(n - c) * (0.6 + 0.4 * np.sign(np.sin(2 * np.pi * 45 * t[c:])))
    x = filt(x, "bp", (300, 3400), order=3)
    gaps = np.clip(np.sin(2 * np.pi * 2.1 * t) * 5 + 4, 0, 1)
    return x * gaps * np.clip(np.minimum(t, dur - t) / 0.02, 0, 1) * 0.07


def static_burst(rng, dur=1.5):
    n = int(dur * SR)
    t = np.arange(n) / SR
    x = filt(rng.standard_normal(n), "bp", (600, 9000))
    crack = (rng.random(n) < 0.002) * rng.standard_normal(n) * 6
    env = np.sin(np.pi * t / dur) ** 2
    return (x + crack) * env * 0.08


def reverse(x):
    return x[::-1].copy()


# ---------------------------------------------------------------- circular effects

def circ_delay(x, d, fb=0.42, taps=5, pingpong=True, lp=3500):
    """Ping-pong echo by summing rolled copies (wraps across the loop)."""
    out = np.zeros_like(x)
    src = filt(x.mean(1), "lp", lp)
    for k in range(1, taps + 1):
        y = np.roll(src, int(k * d * SR)) * fb ** k
        ch = (k % 2) if pingpong else None
        if ch is None:
            out += y[:, None]
        else:
            out[:, ch] += y
    return out


def circ_chorus(x, loop_sec, base=0.012, depth=0.003, cycles=31):
    L = x.shape[0]
    t = np.arange(L) / SR
    out = np.zeros_like(x)
    for ch, ph in ((0, 0.0), (1, np.pi)):
        dly = base + depth * np.sin(2 * np.pi * cycles / loop_sec * t + ph)
        p = (np.arange(L) - dly * SR) % L
        xe = np.concatenate([x[:, ch], x[:1, ch]])
        out[:, ch] = 0.6 * x[:, ch] + 0.6 * np.interp(p, np.arange(L + 1), xe)
    return out


def circ_lp(x, fc):
    L = x.shape[0]
    f = np.fft.rfftfreq(L, 1 / SR)
    g = 1 / np.sqrt(1 + (f / fc) ** 4)
    return np.stack([np.fft.irfft(np.fft.rfft(x[:, c]) * g, n=L) for c in range(2)], 1)


def window_curve(L, spans, ramp=0.25):
    """1 inside each (start_sec, end_sec) span, 0 outside, with smooth ramps."""
    t = np.arange(L) / SR
    w = np.zeros(L)
    for a, b in spans:
        w = np.maximum(w, np.clip((t - a) / ramp + 1, 0, 1) * np.clip((b - t) / ramp + 1, 0, 1))
    return w


def duck(L, hits, depth=0.55, rel=0.18):
    t = np.arange(L) / SR
    g = np.ones(L)
    for h in hits:
        dt = (t - h) % (L / SR)
        g = np.minimum(g, 1 - depth * np.exp(-dt / rel) * (dt < 1.0))
    return g


def tape_stop(x, start, end):
    """Playback speed falls 1 -> 0 between start and end (seconds), silence till end."""
    a, b = int(start * SR), int(end * SR)
    n = b - a
    u = np.arange(n) / n
    speed = (1 - u) ** 1.6
    pos = a + np.cumsum(speed)
    y = x.copy()
    fade = np.clip((1 - u) / 0.15, 0, 1)
    for ch in range(2):
        y[a:b, ch] = np.interp(pos, np.arange(len(x)), x[:, ch]) * fade
    return y


def stutter(x, at, slice_sec, repeats, decay=0.85):
    a = int(at * SR)
    s = int(slice_sec * SR)
    piece = x[a: a + s].copy()
    edge = np.clip(np.minimum(np.arange(s), s - 1 - np.arange(s)) / 60, 0, 1)[:, None]
    y = x.copy()
    for r in range(1, repeats + 1):
        p = a + r * s
        if p + s > len(y):
            break
        y[p: p + s] = piece * edge * decay ** r
    return y


def dropouts(x, rng, times, loop_sec):
    """VHS dropouts: short dips with the highs gone."""
    dull = circ_lp(x, 1200) * 0.45
    w = np.zeros(x.shape[0])
    t = np.arange(x.shape[0]) / SR
    for c in times:
        d = rng.uniform(0.06, 0.16)
        w = np.maximum(w, np.exp(-((t - c) / d) ** 2))
    return x * (1 - w[:, None]) + dull * w[:, None]
