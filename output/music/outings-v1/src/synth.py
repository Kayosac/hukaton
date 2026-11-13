"""Small synth toolkit for Hakaton shop music.
Everything renders into a circular stereo buffer, so loops are seamless:
note tails, reverb and tape wobble wrap around the end of the loop."""
import numpy as np
from scipy.signal import butter, sosfilt
from scipy.io import wavfile

SR = 44100


def mtof(m):
    return 440.0 * 2.0 ** ((np.asarray(m, dtype=float) - 69.0) / 12.0)


def cents(c):
    return 2.0 ** (c / 1200.0)


def tvec(n):
    return np.arange(n) / SR


# ---------------------------------------------------------------- filters
def _sos(kind, f, order=2):
    nyq = SR / 2
    if kind == "bp":
        lo, hi = f
        return butter(order, [max(lo, 10) / nyq, min(hi, nyq * 0.95) / nyq], btype="band", output="sos")
    return butter(order, min(f, nyq * 0.95) / nyq, btype=kind, output="sos")


def lp(x, f, order=2):
    return sosfilt(_sos("low", f, order), x, axis=0)


def hp(x, f, order=2):
    return sosfilt(_sos("high", f, order), x, axis=0)


def bp(x, lo, hi, order=2):
    return sosfilt(_sos("bp", (lo, hi), order), x, axis=0)


def cfilt(x, sos):
    """Filter a loop as if it repeated forever (steady state)."""
    y = sosfilt(sos, np.concatenate([x, x, x], 0), axis=0)
    return y[2 * len(x):]


def clp(x, f, order=2):
    return cfilt(x, _sos("low", f, order))


def chp(x, f, order=2):
    return cfilt(x, _sos("high", f, order))


def cbp(x, lo, hi, order=2):
    return cfilt(x, _sos("bp", (lo, hi), order))


# ---------------------------------------------------------------- oscillators
def phase(freq, ph0=0.0):
    return (ph0 + np.cumsum(freq) / SR) % 1.0


def sine_f(freq, ph0=0.0):
    return np.sin(2 * np.pi * phase(freq, ph0))


def _blep(ph, dt):
    y = np.zeros_like(ph)
    m = ph < dt
    x = ph[m] / dt[m]
    y[m] = x + x - x * x - 1
    m = ph > 1 - dt
    x = (ph[m] - 1) / dt[m]
    y[m] = x * x + x + x + 1
    return y


def saw_f(freq, ph0=None, rng=None):
    if ph0 is None:
        ph0 = (rng or np.random).rand()
    dt = np.clip(freq / SR, 1e-9, 0.5)
    ph = phase(freq, ph0)
    return 2 * ph - 1 - _blep(ph, dt)


def pulse_f(freq, width=0.5, ph0=None, rng=None):
    if ph0 is None:
        ph0 = (rng or np.random).rand()
    dt = np.clip(freq / SR, 1e-9, 0.5)
    ph = phase(freq, ph0)
    ph2 = (ph + width) % 1.0
    a = 2 * ph - 1 - _blep(ph, dt)
    b = 2 * ph2 - 1 - _blep(ph2, dt)
    return 0.5 * (a - b)


def const(f, n):
    return np.full(n, float(f))


# ---------------------------------------------------------------- envelopes
def adsr(n_hold, a, d, s, r):
    """n_hold samples of gate, then release r seconds. Returns envelope."""
    na, nd, nr = int(a * SR) + 1, int(d * SR) + 1, int(r * SR) + 1
    n = n_hold + nr
    e = np.empty(n)
    t = np.arange(n)
    e[:] = s
    att = t < na
    e[att] = t[att] / na
    dec = (t >= na) & (t < na + nd)
    e[dec] = s + (1 - s) * np.exp(-4.0 * (t[dec] - na) / nd)
    # release from the level at gate-off
    g = min(n_hold, n - 1)
    lvl = e[g - 1] if g > 0 else 0.0
    rel = t >= n_hold
    e[rel] = lvl * np.exp(-5.0 * (t[rel] - n_hold) / nr)
    return e


def perc(n, a, tau):
    t = tvec(n)
    e = np.exp(-t / tau)
    na = max(1, int(a * SR))
    e[:na] *= np.linspace(0, 1, na)
    return e


def fade_tail(x, sec=0.01):
    k = min(len(x), int(sec * SR))
    if k > 1:
        x[-k:] *= np.linspace(1, 0, k)
    return x


# ---------------------------------------------------------------- instruments (mono)
def music_box(m, dur, rng, detune=8):
    f = mtof(m) * cents(rng.uniform(-detune, detune))
    tau = 2.2 * (523 / f) ** 0.5
    n = int((tau * 4 + 0.05) * SR)
    t = tvec(n)
    y = np.sin(2 * np.pi * f * t) * np.exp(-t / tau)
    y += 0.30 * np.sin(2 * np.pi * f * 2.0 * t) * np.exp(-t / (tau * 0.35))
    y += 0.14 * np.sin(2 * np.pi * f * 5.43 * t) * np.exp(-t / 0.05)
    y += 0.06 * np.sin(2 * np.pi * f * 3.02 * t) * np.exp(-t / (tau * 0.2))
    click = hp(rng.randn(int(0.004 * SR)), 4000) * 0.15
    y[: len(click)] += click
    y[:40] *= np.linspace(0, 1, 40)
    return fade_tail(y)


def kalimba(m, dur, rng, bright=1.0):
    f = mtof(m) * cents(rng.uniform(-5, 5))
    tau = 0.9 * (440 / f) ** 0.4
    n = int((tau * 4.5) * SR)
    t = tvec(n)
    y = np.sin(2 * np.pi * f * t) * np.exp(-t / tau)
    y += 0.35 * bright * np.sin(2 * np.pi * f * 5.95 * t) * np.exp(-t / 0.045)
    y += 0.12 * np.sin(2 * np.pi * f * 3.0 * t) * np.exp(-t / (tau * 0.25))
    thump = lp(rng.randn(int(0.02 * SR)), 900) * np.exp(-tvec(int(0.02 * SR)) / 0.004) * 0.6
    y[: len(thump)] += thump
    y[:30] *= np.linspace(0, 1, 30)
    return fade_tail(y)


def toy_piano(m, dur, rng):
    f = mtof(m) * cents(rng.uniform(-14, 14))
    tau = 1.0 * (523 / f) ** 0.5
    n = int((tau * 4 + 0.05) * SR)
    t = tvec(n)
    y = np.sin(2 * np.pi * f * t) * np.exp(-t / tau)
    y += 0.5 * np.sin(2 * np.pi * f * 2.76 * t + 0.3) * np.exp(-t / (tau * 0.3))
    y += 0.3 * np.sin(2 * np.pi * f * 5.40 * t) * np.exp(-t / (tau * 0.12))
    y += 0.15 * np.sin(2 * np.pi * f * 8.93 * t) * np.exp(-t / 0.03)
    y[:25] *= np.linspace(0, 1, 25)
    return fade_tail(y)


def glass(m, dur, rng, a=0.5, r=3.0):
    f = mtof(m) * cents(rng.uniform(-4, 4))
    nh = int(dur * SR)
    e = adsr(nh, a, 0.6, 0.75, r)
    n = len(e)
    vib = 1 + 0.0025 * np.sin(2 * np.pi * rng.uniform(4.2, 5.4) * tvec(n))
    fr = f * vib
    y = sine_f(fr) + 0.18 * sine_f(fr * 2.0) + 0.06 * sine_f(fr * 3.0) + 0.04 * sine_f(fr * 4.16)
    return y * e


def fm_epiano(m, dur, rng, vel=0.8):
    f = mtof(m) * cents(rng.uniform(-3, 3))
    nh = int(dur * SR)
    e = adsr(nh, 0.002, 1.6, 0.25, 0.45) * np.exp(-tvec(nh + int(0.45 * SR) + 1) / 3.0)
    n = len(e)
    t = tvec(n)
    idx = (0.6 + 2.4 * vel) * np.exp(-t / 0.35) + 0.25
    mod = np.sin(2 * np.pi * f * t)
    y = np.sin(2 * np.pi * f * t + idx * mod)
    tine = np.sin(2 * np.pi * f * 14.0 * t) * np.exp(-t / 0.018) * 0.35 * vel
    y = (y + tine) * e
    trem = 1 + 0.18 * np.sin(2 * np.pi * 4.8 * t + rng.uniform(0, 6.28))
    return y * trem


def vibes(m, dur, rng):
    f = mtof(m)
    n = int((dur + 2.5) * SR)
    t = tvec(n)
    e = np.exp(-t / 1.8)
    e[: int(dur * SR) :] *= 1
    rel = int(dur * SR)
    e[rel:] *= np.exp(-tvec(n - rel) / 0.35)
    y = np.sin(2 * np.pi * f * t) + 0.22 * np.sin(2 * np.pi * f * 4.0 * t) * np.exp(-t / 0.25)
    y += 0.06 * np.sin(2 * np.pi * f * 10.0 * t) * np.exp(-t / 0.05)
    trem = 1 - 0.32 * (0.5 + 0.5 * np.sin(2 * np.pi * 5.2 * t))
    y = y * e * trem
    y[:20] *= np.linspace(0, 1, 20)
    return y


def supersaw(m, dur, rng, cutoff=1400, a=1.0, r=2.0, voices=5, spread=12):
    f = mtof(m)
    nh = int(dur * SR)
    e = adsr(nh, a, 1.0, 0.85, r)
    n = len(e)
    y = np.zeros(n)
    for k in range(voices):
        c = spread * (k - (voices - 1) / 2) / ((voices - 1) / 2 or 1)
        drift = 1 + 0.0015 * np.sin(2 * np.pi * rng.uniform(0.1, 0.3) * tvec(n) + rng.uniform(0, 6))
        y += saw_f(const(f * cents(c), n) * drift, rng=rng)
    y = lp(y / voices, cutoff, 2)
    return y * e


def pluck(m, dur, rng, tau=0.9, pos=0.18, bright=1.0, damp=None):
    """Additive plucked string: balalaika / guitar-ish, exact tuning."""
    f = mtof(m) * cents(rng.uniform(-6, 6))
    total = dur if damp else tau * 4
    n = int((total + 0.06) * SR)
    t = tvec(n)
    y = np.zeros(n)
    kmax = int(min(40, (SR * 0.45) / f))
    for k in range(1, kmax + 1):
        amp = abs(np.sin(np.pi * k * pos)) / k ** (1.3 - 0.3 * bright)
        tk = tau / (1 + 0.35 * (k - 1) / bright)
        y += amp * np.sin(2 * np.pi * f * k * t * (1 + 0.0004 * k * k)) * np.exp(-t / tk)
    y[:15] *= np.linspace(0, 1, 15)
    if damp:
        nd = int(dur * SR)
        if nd < n:
            y[nd:] *= np.exp(-tvec(n - nd) / 0.03)
    return fade_tail(y)


def tuba(m, dur, rng, slide=2.0, bright=700):
    f = mtof(m) * cents(rng.uniform(-10, 10))
    nh = int(dur * SR)
    e = adsr(nh, 0.03, 0.2, 0.7, 0.12)
    n = len(e)
    t = tvec(n)
    glide = f * 2 ** (-slide / 12 * np.exp(-t / 0.05))
    y = saw_f(glide, rng=rng) * 0.6 + pulse_f(glide, 0.3, rng=rng) * 0.4
    y = lp(y, bright, 2)
    y += 0.4 * sine_f(glide)
    breath = bp(rng.randn(n), 200, 900) * 0.03
    return (y + breath) * e


def accordion(m, dur, rng, a=0.04, r=0.12, cutoff=2600):
    f = mtof(m)
    nh = int(dur * SR)
    e = adsr(nh, a, 0.1, 0.9, r)
    n = len(e)
    t = tvec(n)
    bel = 1 + 0.06 * np.sin(2 * np.pi * 3.1 * t)
    y = pulse_f(const(f * cents(-9), n), 0.42, rng=rng) + pulse_f(const(f * cents(9), n), 0.38, rng=rng)
    y += 0.5 * saw_f(const(f * 2 * cents(3), n), rng=rng)
    y = lp(y, cutoff, 2)
    y = hp(y, 180)
    return 0.4 * y * e * bel


def bass(m, dur, rng, cutoff=420, a=0.006, r=0.08, saw=0.35, glide_from=None):
    f = mtof(m)
    nh = int(dur * SR)
    e = adsr(nh, a, 0.4, 0.75, r)
    n = len(e)
    if glide_from is not None:
        f0 = mtof(glide_from)
        fr = f + (f0 - f) * np.exp(-tvec(n) / 0.04)
    else:
        fr = const(f, n)
    y = sine_f(fr) + saw * lp(saw_f(fr, rng=rng), cutoff)
    return y * e


def choir(m, dur, rng, vowel="a", a=1.5, r=2.5, voices=3):
    forms = {"a": [(800, 1.0), (1150, 0.5), (2900, 0.12)],
             "o": [(450, 1.0), (800, 0.45), (2830, 0.08)],
             "u": [(325, 1.0), (700, 0.25), (2530, 0.05)],
             "e": [(400, 1.0), (1700, 0.35), (2600, 0.1)]}[vowel]
    f = mtof(m)
    nh = int(dur * SR)
    e = adsr(nh, a, 1.0, 0.9, r)
    n = len(e)
    t = tvec(n)
    src = np.zeros(n)
    for k in range(voices):
        vib = 1 + 0.004 * np.sin(2 * np.pi * rng.uniform(4.5, 5.8) * t + rng.uniform(0, 6))
        src += saw_f(f * cents(rng.uniform(-10, 10)) * vib, rng=rng)
    y = np.zeros(n)
    for fc, g in forms:
        y += g * bp(src, fc * 0.85, fc * 1.15, 2)
    y += 0.02 * hp(rng.randn(n), 3000)
    return y * e / voices


# ---------------------------------------------------------------- percussion (mono)
def kick(rng, punch=1.0, tau=0.32, f0=110, f1=44):
    n = int((tau * 4) * SR)
    t = tvec(n)
    fr = f1 + (f0 - f1) * np.exp(-t / 0.035)
    y = sine_f(fr) * np.exp(-t / tau)
    click = lp(rng.randn(200), 3000) * np.exp(-tvec(200) / 0.001) * 0.3 * punch
    y[:200] += click
    return fade_tail(y)


def snare(rng, tau=0.16, tone=190, lo=900, hi=7000, toneamt=0.5):
    n = int(tau * 5 * SR)
    t = tvec(n)
    y = bp(rng.randn(n), lo, hi) * np.exp(-t / tau)
    y += toneamt * np.sin(2 * np.pi * tone * t) * np.exp(-t / 0.05)
    return fade_tail(y)


def hat(rng, tau=0.035, lo=7000):
    n = int(tau * 6 * SR)
    y = hp(rng.randn(n), lo, 2) * np.exp(-tvec(n) / tau)
    return fade_tail(y)


def brush(rng, dur=0.18):
    n = int(dur * 2 * SR)
    t = tvec(n)
    e = np.minimum(t / 0.03, 1) * np.exp(-np.maximum(t - 0.03, 0) / (dur * 0.6))
    return fade_tail(bp(rng.randn(n), 1800, 9000) * e)


def shaker(rng, tau=0.05):
    n = int(tau * 5 * SR)
    t = tvec(n)
    e = np.minimum(t / 0.012, 1) * np.exp(-t / tau)
    return fade_tail(hp(rng.randn(n), 5500) * e)


def woodblock(rng, f=900):
    n = int(0.12 * SR)
    t = tvec(n)
    y = np.sin(2 * np.pi * f * t) * np.exp(-t / 0.02) + 0.5 * np.sin(2 * np.pi * f * 1.62 * t) * np.exp(-t / 0.012)
    return fade_tail(y)


def clock_tick(rng, high=True):
    n = int(0.05 * SR)
    t = tvec(n)
    f = 2400 if high else 1750
    y = np.sin(2 * np.pi * f * t) * np.exp(-t / 0.006)
    y += 0.6 * bp(rng.randn(n), 1500, 6000) * np.exp(-t / 0.0025)
    y += 0.3 * np.sin(2 * np.pi * 420 * t) * np.exp(-t / 0.01)
    return fade_tail(y)


def coin(rng, base=None):
    base = base or rng.uniform(2600, 3800)
    n = int(0.6 * SR)
    t = tvec(n)
    y = np.zeros(n)
    for r, a, tau in [(1, 1, 0.25), (1.51, 0.6, 0.18), (2.43, 0.5, 0.12), (3.9, 0.3, 0.07)]:
        y += a * np.sin(2 * np.pi * base * r * cents(rng.uniform(-20, 20)) * t) * np.exp(-t / tau)
    y[:20] *= np.linspace(0, 1, 20)
    return fade_tail(y)


def boing(rng, f0=120, f1=420, dur=0.45):
    n = int(dur * SR)
    t = tvec(n)
    fr = f0 + (f1 - f0) * (1 - np.exp(-t / 0.08)) + 30 * np.sin(2 * np.pi * 14 * t) * np.exp(-t / 0.15)
    return fade_tail(sine_f(fr) * np.exp(-t / 0.18))


# ---------------------------------------------------------------- loop buffer
class Loop:
    def __init__(self, seconds, seed=1):
        self.L = int(round(seconds * SR))
        self.sec = self.L / SR
        self.buses = {}
        self.rng = np.random.RandomState(seed)

    def bus(self, name):
        if name not in self.buses:
            self.buses[name] = np.zeros((self.L, 2))
        return self.buses[name]

    def add(self, name, sig, t, pan=0.0, gain=1.0):
        buf = self.bus(name)
        sig = np.asarray(sig) * gain
        if sig.ndim == 1:
            th = (np.clip(pan, -1, 1) + 1) * np.pi / 4
            sig = np.stack([sig * np.cos(th), sig * np.sin(th)], 1) * np.sqrt(2)
        pos = int(round(t * SR)) % self.L
        i, n = 0, len(sig)
        while i < n:
            k = min(n - i, self.L - pos)
            buf[pos:pos + k] += sig[i:i + k]
            i += k
            pos = 0

    def add_full(self, name, stereo):
        self.bus(name)[:] += stereo


# ---------------------------------------------------------------- effects on loops (circular)
def make_ir(rng, t60=2.2, pre=0.015, damp=4000, width=1.0, early=True):
    n = int((t60 + pre) * SR)
    t = tvec(n)
    ir = np.zeros((n, 2))
    env = np.exp(-6.9 * t / t60)
    for c in range(2):
        nz = rng.randn(n) * env
        bright = lp(nz, damp, 1)
        dark = lp(nz, damp * 0.25, 1)
        mix = np.clip(t / t60, 0, 1)
        ir[:, c] = bright * (1 - mix) + dark * mix
    k = int(pre * SR)
    ir = np.roll(ir, k, axis=0)
    ir[:k] = 0
    if early:
        for d, g in [(0.011, 0.5), (0.017, 0.35), (0.023, 0.3), (0.031, 0.22)]:
            ir[int(d * SR), 0] += g * rng.uniform(0.6, 1)
            ir[int((d + 0.003) * SR), 1] += g * rng.uniform(0.6, 1)
    m = ir.mean(1, keepdims=True)
    ir = m + (ir - m) * width
    ir /= np.sqrt((ir ** 2).sum() / 2)
    return ir


def creverb(x, ir):
    L = len(x)
    out = np.zeros_like(x)
    irw = np.zeros((L, 2))
    n = len(ir)
    # wrap the IR if it is longer than the loop
    for s in range(0, n, L):
        seg = ir[s:s + L]
        irw[:len(seg)] += seg
    for c in range(2):
        out[:, c] = np.fft.irfft(np.fft.rfft(x[:, c]) * np.fft.rfft(irw[:, c]), L)
    return out


def cdelay(x, sec, fb=0.35, mix=0.3, cutoff=3000, pingpong=True, taps=6):
    d = int(sec * SR)
    wet = clp(x, cutoff, 1)
    y = np.zeros_like(x)
    for k in range(1, taps + 1):
        tap = np.roll(wet, d * k, axis=0) * (fb ** (k - 1))
        if pingpong and k % 2:
            tap = tap[:, ::-1]
        y += tap
    return x + mix * y


def tape_wow(x, rng, depth_cents=8, flutter_cents=2.5):
    """Circular time-varying delay; LFOs have whole cycles per loop."""
    L = len(x)
    sec = L / SR
    n = np.arange(L)
    d = np.zeros(L)
    for f, c in [(0.37, depth_cents), (0.83, depth_cents * 0.5), (6.3, flutter_cents)]:
        cyc = max(1, round(f * sec))
        fr = cyc / sec
        A = (c / 1200 * np.log(2)) * SR / (2 * np.pi * fr)
        d += A * np.sin(2 * np.pi * cyc * n / L + rng.uniform(0, 6.28))
    pos = (n - d - d.min()) % L
    i0 = np.floor(pos).astype(int)
    fr = pos - i0
    i1 = (i0 + 1) % L
    return x[i0] * (1 - fr)[:, None] + x[i1] * fr[:, None]


def tape_stop_free_lfo(L, cyc, phase=0.0):
    n = np.arange(L)
    return np.sin(2 * np.pi * cyc * n / L + phase)


def hiss(L, rng, level=0.004, cutoff=9000):
    nz = rng.randn(L, 2)
    return clp(chp(nz, 1500), cutoff) * level


def crackle(L, rng, rate=6.0, level=0.15):
    y = np.zeros((L, 2))
    k = int(rate * L / SR)
    pos = rng.randint(0, L, k)
    amp = rng.exponential(1, k) * level
    for c in range(2):
        np.add.at(y[:, c], pos, amp * rng.choice([-1, 1], k))
    y = chp(y, 1200)
    y += clp(chp(rng.randn(L, 2), 300), 3000) * level * 0.03
    return y


def stereo_chorus(x, rng, depth_ms=3.0, rate=0.6, mix=0.5):
    L = len(x)
    sec = L / SR
    cyc = max(1, round(rate * sec))
    n = np.arange(L)
    out = np.zeros_like(x)
    for c, ph in [(0, 0.0), (1, np.pi / 2)]:
        d = (depth_ms / 1000 * SR) * (1.2 + np.sin(2 * np.pi * cyc * n / L + ph))
        pos = (n - d) % L
        i0 = np.floor(pos).astype(int)
        fr = pos - i0
        w = x[i0, c] * (1 - fr) + x[(i0 + 1) % L, c] * fr
        out[:, c] = x[:, c] * (1 - mix) + w * mix
    return out


def ducker(L, times, depth=0.5, release=0.25):
    """Sidechain-style gain curve from kick times (circular)."""
    g = np.ones(L)
    n = int(release * 4 * SR)
    curve = 1 - depth * np.exp(-tvec(n) / release)
    for t in times:
        p = int(round(t * SR)) % L
        idx = (p + np.arange(n)) % L
        g[idx] = np.minimum(g[idx], curve)
    return g[:, None]


# ---------------------------------------------------------------- master
def master(x, target_rms_db=-19.0, drive=1.3, ceiling_db=-1.2, hp_hz=28):
    x = chp(x, hp_hz, 2)
    x = np.tanh(x * drive) / drive
    rms = np.sqrt((x ** 2).mean())
    x *= 10 ** (target_rms_db / 20) / rms
    ceil = 10 ** (ceiling_db / 20)
    t = ceil * 0.8
    a = np.abs(x)
    over = a > t
    x[over] = np.sign(x[over]) * (t + (ceil - t) * np.tanh((a[over] - t) / (ceil - t)))
    pk = np.abs(x).max()
    if pk > ceil:
        x *= ceil / pk
    return x


def write(path, x):
    wavfile.write(path, SR, (np.clip(x, -1, 1) * 32767).astype(np.int16))


def report(name, x):
    rms = 20 * np.log10(np.sqrt((x ** 2).mean()))
    pk = 20 * np.log10(np.abs(x).max())
    seam = np.abs(x[0] - x[-1]).max()
    step = np.abs(np.diff(x, axis=0)).max()
    print(f"{name}: {len(x)/SR:.2f}s rms {rms:.1f} dB peak {pk:.1f} dB seam-jump {seam:.4f} (max step {step:.4f})")


def levels(**layers):
    tot = sum(layers.values())
    r = lambda x: 20 * np.log10(np.sqrt((x ** 2).mean()) + 1e-12)
    print("  layers:", ", ".join(f"{k} {r(v)-r(tot):+.1f}" for k, v in layers.items()))
