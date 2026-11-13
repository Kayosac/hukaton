"""Small DSP kit for the god's sounds, voice and music (numpy/scipy). Everything is float stereo at SR."""
import numpy as np
from scipy import signal
import soundfile as sf

SR = 44100
rng = np.random.default_rng(7)


def secs(n):
    return int(round(n * SR))


def t_axis(dur):
    return np.arange(secs(dur)) / SR


def white(dur):
    return rng.standard_normal(secs(dur))


def brown(dur):
    x = np.cumsum(rng.standard_normal(secs(dur)))
    x = hp(x, 12)
    return x / (np.abs(x).max() + 1e-9)


def _sos(kind, f, order=2):
    if kind == "band":
        lo, hi = f
        return signal.butter(order, [max(lo, 5) / (SR / 2), min(hi, SR / 2 - 100) / (SR / 2)], btype="band", output="sos")
    return signal.butter(order, min(f, SR / 2 - 100) / (SR / 2), btype=kind, output="sos")


def lp(x, f, order=2):
    return signal.sosfilt(_sos("low", f, order), x, axis=0)


def hp(x, f, order=2):
    return signal.sosfilt(_sos("high", f, order), x, axis=0)


def bp(x, lo, hi, order=2):
    return signal.sosfilt(_sos("band", (lo, hi), order), x, axis=0)


def sweep_lp(x, f_of_t, block=256):
    """Low-pass with a cutoff that moves (f_of_t: array per sample or callable of seconds)."""
    n = len(x)
    out = np.zeros_like(x)
    zi = None
    for i in range(0, n, block):
        f = f_of_t(i / SR) if callable(f_of_t) else f_of_t[min(i, n - 1)]
        sos = _sos("low", max(20, f), 2)
        if zi is None:
            zi = np.zeros((sos.shape[0], 2))
        out[i:i + block], zi = signal.sosfilt(sos, x[i:i + block], zi=zi)
    return out


def sweep_bp(x, c_of_t, q=1.2, block=256):
    n = len(x)
    out = np.zeros_like(x)
    zi = None
    for i in range(0, n, block):
        c = c_of_t(i / SR) if callable(c_of_t) else c_of_t[min(i, n - 1)]
        c = max(30, c)
        sos = _sos("band", (c / (1 + .5 / q), c * (1 + .5 / q)), 1)
        if zi is None:
            zi = np.zeros((sos.shape[0], 2))
        out[i:i + block], zi = signal.sosfilt(sos, x[i:i + block], zi=zi)
    return out


def smooth(u):
    u = np.clip(u, 0, 1)
    return u * u * (3 - 2 * u)


def env_points(dur, pts):
    """Piecewise-smooth envelope through (time, value) points."""
    t = t_axis(dur)
    ts = [p[0] for p in pts]
    vs = [p[1] for p in pts]
    e = np.interp(t, ts, vs)
    return lp(e, 30, 1)


def adsr(n, a, r, curve=2.0):
    e = np.ones(n)
    na, nr = secs(a), secs(r)
    if na:
        e[:na] = (np.arange(na) / na) ** curve
    if nr:
        e[-nr:] *= (1 - np.arange(nr) / nr) ** curve
    return e


def place(buf, x, at, gain=1.0):
    """Add mono or stereo x into stereo buf at seconds `at`."""
    i = secs(at)
    if i >= len(buf):
        return
    x = x.copy()
    f = min(len(x) // 2, secs(.02))
    ramp = np.linspace(0, 1, f)
    if x.ndim == 1:
        x[:f] *= ramp; x[-f:] *= ramp[::-1]
    else:
        x[:f] *= ramp[:, None]; x[-f:] *= ramp[::-1, None]
    if x.ndim == 1:
        x = np.stack([x, x], 1)
    j = min(len(buf), i + len(x))
    buf[i:j] += x[:j - i] * gain


def pan(x, p):
    """p -1..1 (constant power)."""
    a = (p + 1) * np.pi / 4
    return np.stack([x * np.cos(a), x * np.sin(a)], 1)


def widen(x, ms=11):
    """Mono to a wide stereo (Haas + decorrelation)."""
    d = secs(ms / 1000)
    r = np.concatenate([np.zeros(d), x[:-d]]) if d else x
    return np.stack([x * .9 + .1 * r, r * .9 + .1 * x], 1)


def boom(dur=6.0, f0=55, f1=24, drop=1.2, drive=2.0, click=.4):
    x = _boom(dur, f0, f1, drop, drive, click)
    return x * adsr(len(x), 0, dur * .4, 1.5)


def _boom(dur, f0, f1, drop, drive, click):
    """A huge low impact: falling sine body, soft drive, a dark noise transient."""
    t = t_axis(dur)
    f = f1 + (f0 - f1) * np.exp(-t / drop)
    ph = 2 * np.pi * np.cumsum(f) / SR
    body = np.sin(ph) * np.exp(-t / (dur * .28))
    body = np.tanh(body * drive) / np.tanh(drive)
    n = lp(white(dur), 900) * np.exp(-t / .09) * click
    rum = lp(brown(dur), 90) * np.exp(-t / (dur * .35)) * .8
    x = body + n + rum
    return x / (np.abs(x).max() + 1e-9)


def grind(dur, density_env, lo=90, hi=700, grain=(0.02, 0.09)):
    """Rock grinding on rock: many short band-passed noise grains whose density follows density_env (0..1)."""
    n = secs(dur)
    out = np.zeros(n)
    t = 0.0
    while t < dur:
        k = density_env[min(n - 1, secs(t))]
        t += (0.004 + 0.06 * (1 - k)) * rng.uniform(.5, 1.5)
        if k < .02:
            continue
        g = rng.uniform(*grain)
        gn = secs(g)
        c = rng.uniform(lo, hi)
        x = bp(rng.standard_normal(gn), c * .7, c * 1.4)
        x *= np.hanning(gn) * k * rng.uniform(.3, 1.0)
        place_m(out, x, t)
    return out / (np.abs(out).max() + 1e-9)


def place_m(buf, x, at):
    i = secs(at)
    j = min(len(buf), i + len(x))
    if i < len(buf):
        buf[i:j] += x[:j - i]


def groan(dur, f_path, bright=500, rough=.4):
    """A slow giant groan/creak: a rough low saw gliding along f_path (array of Hz per sample), formant-filtered."""
    f = f_path * (1 + rough * .02 * lp(rng.standard_normal(len(f_path)), 8))
    ph = np.cumsum(f) / SR
    saw = 2 * (ph % 1) - 1
    x = lp(saw, bright, 2) + .4 * bp(saw, bright * .6, bright * 1.6)
    return x / (np.abs(x).max() + 1e-9)


def whoosh(dur, env, c_path, q=.8):
    """Air pushed by something enormous: band-passed noise with a moving centre."""
    x = sweep_bp(white(dur), c_path, q=q)
    x = x * env
    return x / (np.abs(x).max() + 1e-9)


def hall_ir(rt=4.5, pre=.03, dark=3500, sr=SR, seed=3):
    r = np.random.default_rng(seed)
    n = int(rt * sr)
    t = np.arange(n) / sr
    decay = np.exp(-6.9 * t / rt)
    ir = r.standard_normal((n, 2)) * decay[:, None]
    # darker as it decays
    ir = np.stack([sweep_lp(ir[:, c], lambda s: dark * np.exp(-s / (rt * .5)) + 300) for c in range(2)], 1)
    p = int(pre * sr)
    ir = np.concatenate([np.zeros((p, 2)), ir])
    return ir / np.sqrt((ir ** 2).sum(0, keepdims=True))


def reverb(x, wet=.35, rt=4.5, dark=3500, pre=.03):
    if x.ndim == 1:
        x = np.stack([x, x], 1)
    ir = hall_ir(rt, pre, dark)
    w = np.stack([signal.fftconvolve(x[:, c], ir[:, c]) for c in range(2)], 1)
    dry = np.concatenate([x, np.zeros((len(w) - len(x), 2))])
    w *= np.sqrt((dry ** 2).mean() / ((w ** 2).mean() + 1e-12))
    return dry * (1 - wet) + w * wet


def trim_tail(x, thresh_db=-60):
    a = np.abs(x).max(1) if x.ndim == 2 else np.abs(x)
    th = a.max() * 10 ** (thresh_db / 20)
    idx = np.where(a > th)[0]
    end = idx[-1] + secs(.05) if len(idx) else len(x)
    x = x[:end].copy()
    f = min(len(x), secs(.3))
    x[-f:] *= np.linspace(1, 0, f)[:, None] if x.ndim == 2 else np.linspace(1, 0, f)
    return x


def bass_harmonics(x, amount=.3, f=110):
    """Lets the sub be heard on small speakers: saturated low end, its overtones only."""
    lo = lp(x, f, 2)
    lo = lo / (np.abs(lo).max() + 1e-9)
    return x + amount * np.abs(x).max() * hp(np.tanh(lo * 5), f * .9, 2)


def master(x, peak=-1.0):
    x = x / (np.abs(x).max() + 1e-9)
    x = np.tanh(x * 1.3) / np.tanh(1.3)
    return x * 10 ** (peak / 20) / (np.abs(x).max() + 1e-9)


def write(path, x, sr=SR):
    sf.write(path, np.asarray(x, np.float32), sr, subtype="PCM_16")
