"""Small offline synth for Hakaton music sketches.

Everything here is synthesized from scratch with numpy/scipy (no samples),
so the result is free to ship (CC0, same as the game's synthesized SFX).
Loops are rendered circularly: note tails, reverb and tape wobble wrap
around the loop point, so the file loops without a click or a gap.
"""
import numpy as np
from scipy.signal import butter, sosfilt
from scipy.io import wavfile

SR = 44100
TAIL = 4.0  # seconds of tail rendered past the loop end and folded back


def mtof(m):
    return 440.0 * 2 ** ((m - 69) / 12.0)


def _t(dur):
    return np.arange(int(dur * SR)) / SR


def env_adsr(n, a=0.005, d=0.1, s=0.7, r=0.05, hold=None):
    """hold = seconds until release starts (default: n - r)."""
    t = np.arange(n) / SR
    total = n / SR
    if hold is None:
        hold = max(total - r, a)
    e = np.where(t < a, t / max(a, 1e-6), s + (1 - s) * np.exp(-(t - a) / max(d, 1e-6)))
    rel = np.clip((t - hold) / max(r, 1e-6), 0, 1)
    return e * (1 - rel)


def noise(n, rng):
    return rng.standard_normal(n)


def filt(x, kind, f, order=2):
    if kind == "bp":
        sos = butter(order, [f[0] / (SR / 2), min(f[1] / (SR / 2), 0.99)], btype="band", output="sos")
    else:
        sos = butter(order, min(f / (SR / 2), 0.99), btype=kind, output="sos")
    return sosfilt(sos, x)


def phase_from_freq(freq):
    return 2 * np.pi * np.cumsum(freq) / SR


# ---------------------------------------------------------------- instruments

def pluck(f, dur, rng, bright=1.0, pos=0.18, decay=3.0, nh=28):
    """Plucked string (balalaika / gusli): additive harmonics, highs die first."""
    n = int((dur + 0.8) * SR)
    t = np.arange(n) / SR
    out = np.zeros(n)
    B = 0.0004  # slight stiffness -> faint metallic inharmonicity
    for k in range(1, nh + 1):
        fk = f * k * np.sqrt(1 + B * k * k)
        if fk > SR * 0.45:
            break
        amp = abs(np.sin(np.pi * k * pos)) / k ** (1.1 / bright)
        out += amp * np.sin(2 * np.pi * fk * t + rng.uniform(0, 6.28)) * np.exp(-t * decay * (1 + 0.35 * k))
    click = filt(noise(int(0.006 * SR), rng), "hp", 2500) * 0.08
    out[: len(click)] += click
    stop = np.clip(1 - (t - dur) / 0.08, 0, 1)  # damp after note length
    return out * stop * 0.35


def kalimba(f, dur, rng):
    n = int((dur + 1.5) * SR)
    t = np.arange(n) / SR
    parts = [(1.0, 1.0, 2.2), (2.01, 0.04, 6), (5.43, 0.22, 14), (13.3, 0.07, 30)]
    out = np.zeros(n)
    for r, a, dcy in parts:
        if f * r < SR * 0.45:
            out += a * np.sin(2 * np.pi * f * r * t) * np.exp(-t * dcy)
    click = filt(noise(int(0.004 * SR), rng), "hp", 3000) * 0.15
    out[: len(click)] += click
    att = np.clip(t / 0.002, 0, 1)
    return out * att * 0.45


def toy_piano(f, dur, rng):
    """Struck metal rods: free-bar partials, a bit out of tune by nature."""
    n = int((dur + 1.2) * SR)
    t = np.arange(n) / SR
    parts = [(1.0, 1.0, 3.2), (2.0, 0.35, 5), (2.76, 0.25, 9), (5.40, 0.12, 16), (8.93, 0.05, 28)]
    out = np.zeros(n)
    for r, a, dcy in parts:
        if f * r < SR * 0.45:
            out += a * np.sin(2 * np.pi * f * r * t + rng.uniform(0, 6.28)) * np.exp(-t * dcy)
    hit = filt(noise(int(0.01 * SR), rng), "bp", (1500, 6000)) * 0.12
    out[: len(hit)] += hit
    return out * np.clip(t / 0.001, 0, 1) * 0.35


def tuba(f, dur, rng, scoop=-0.35, nh=14, glide_to=None):
    """Brass bass with a lazy pitch scoop into each note (the 'crooked' tuba)."""
    n = int((dur + 0.15) * SR)
    t = np.arange(n) / SR
    cents = scoop * 100 * np.exp(-t / 0.035)
    if glide_to is not None:  # sad trombone-ish slide over the whole note
        cents = cents + np.clip(t / dur, 0, 1) ** 1.5 * 1200 * np.log2(glide_to / f)
    freq = f * 2 ** (cents / 1200)
    ph = phase_from_freq(freq)
    e = env_adsr(n, a=0.025, d=0.15, s=0.65, r=0.1, hold=dur)
    out = np.zeros(n)
    for k in range(1, nh + 1):
        if f * k > 4000:
            break
        bright = np.clip(e * 1.3, 0, 1) ** (0.5 * k)  # brighter when loud
        out += (1 / k ** 1.3) * bright * np.sin(k * ph)
    return out * e * 0.5


def accordion(f, dur, rng, nh=24, wet=1.0):
    """Two detuned reeds (musette) -> natural wheezy beating."""
    n = int((dur + 0.12) * SR)
    t = np.arange(n) / SR
    out = np.zeros(n)
    for det in (-9 * wet, +9 * wet, 0):
        fr = f * 2 ** (det / 1200)
        p0 = rng.uniform(0, 6.28)
        for k in range(1, nh + 1):
            fk = fr * k
            if fk > 7000:
                break
            amp = (1 / k) / (1 + (fk / 2200) ** 2)
            out += amp * np.sin(2 * np.pi * fk * t + p0 * k)
    e = env_adsr(n, a=0.03, d=0.2, s=0.85, r=0.07, hold=dur)
    return out * e * 0.18


def pulse(f, dur, rng, duty=0.25, vib=0.0):
    """Naive (aliased on purpose) chip pulse lead."""
    n = int((dur + 0.05) * SR)
    t = np.arange(n) / SR
    freq = f * 2 ** ((vib * np.sin(2 * np.pi * 6 * t) * np.clip((t - 0.12) / 0.1, 0, 1)) / 1200)
    ph = np.cumsum(freq) / SR
    x = np.where((ph % 1.0) < duty, 1.0, -1.0)
    x -= x.mean()
    e = env_adsr(n, a=0.002, d=0.08, s=0.6, r=0.03, hold=dur)
    return x * e * 0.12


def legato(notes, rng, kind="flute", glide=0.045, vib_cents=14, vib_rate=5.2, gain=1.0):
    """Render a whole phrase as ONE continuous breath with portamento between notes.

    notes: list of (start_sec, dur_sec, midi or None). Rests split phrases.
    Returns list of (start_sec, mono_array).
    """
    out = []
    phrase = []
    for nt in notes + [(None, None, None)]:
        if nt[2] is None:
            if phrase:
                out.append(_render_phrase(phrase, rng, kind, glide, vib_cents, vib_rate, gain))
            phrase = []
        else:
            if phrase and nt[0] - (phrase[-1][0] + phrase[-1][1]) > 0.02:
                out.append(_render_phrase(phrase, rng, kind, glide, vib_cents, vib_rate, gain))
                phrase = []
            phrase.append(nt)
    return out


def _render_phrase(ph, rng, kind, glide, vib_cents, vib_rate, gain):
    t0 = ph[0][0]
    end = ph[-1][0] + ph[-1][1]
    n = int((end - t0 + 0.25) * SR)
    t = np.arange(n) / SR
    pitch = np.full(n, float(ph[0][2]))
    amp = np.ones(n)
    for i, (s, d, m) in enumerate(ph):
        a = int((s - t0) * SR)
        pitch[a:] = m
        if i > 0:  # glide from previous pitch
            g = int(glide * SR)
            prev = ph[i - 1][2]
            seg = np.linspace(0, 1, g) ** 0.7
            pitch[a: a + g] = prev + (m - prev) * seg[: len(pitch[a: a + g])]
            dip = 1 - 0.35 * np.exp(-((np.arange(g) - g * 0.3) / (g * 0.25)) ** 2)
            amp[a: a + g] *= dip[: len(amp[a: a + g])]
    # delayed vibrato per note + slow 'wobbly player' drift
    vib = np.zeros(n)
    for s, d, m in ph:
        a = int((s - t0) * SR)
        b = min(n, int((s - t0 + d) * SR))
        tt = np.arange(b - a) / SR
        vib[a:b] = vib_cents * np.sin(2 * np.pi * vib_rate * tt) * np.clip((tt - 0.18) / 0.25, 0, 1)
    drift = 6 * np.sin(2 * np.pi * 0.37 * t + rng.uniform(0, 6))
    freq = mtof(pitch + (vib + drift) / 100)
    phs = phase_from_freq(freq)
    if kind == "flute":
        x = np.sin(phs) + 0.18 * np.sin(2 * phs) + 0.06 * np.sin(3 * phs)
        br = filt(noise(n, rng), "bp", (1200, 5000)) * 0.06
        x = x + br * (0.6 + 0.4 * np.sin(phs))
        g0 = 0.22
    elif kind == "clarinet":
        x = sum((1 / k ** 1.2) * np.sin(k * phs) for k in (1, 3, 5, 7, 9, 11)) + 0.12 * np.sin(2 * phs)
        x = filt(x, "lp", 3500)
        g0 = 0.16
    elif kind == "kazoo":
        x = sum((1 / k) * np.sin(k * phs) for k in range(1, 16))
        buzz = filt(noise(n, rng), "bp", (800, 3000)) * 0.15
        x = filt(x * (1 + 0.3 * np.sin(phs * 0.5)) + buzz, "bp", (300, 3800))
        g0 = 0.16
    elif kind == "saw":  # retrowave lead: saw + a bit of square, warm low-pass
        x = sum((1 / k) * np.sin(k * phs) for k in range(1, 22))
        x += 0.35 * sum((1 / k) * np.sin(k * phs * 1.0029) for k in range(1, 22, 2))
        x = filt(x, "lp", 2600)
        g0 = 0.13
    else:  # whistle
        x = np.sin(phs)
        g0 = 0.2
    e = env_adsr(n, a=0.05, d=0.3, s=0.9, r=0.12, hold=end - t0)
    return (t0, x * amp * e * g0 * gain)


# ---------------------------------------------------------------- percussion

def kick(rng, f0=110, f1=45, dec=0.28):
    t = _t(0.5)
    freq = f1 + (f0 - f1) * np.exp(-t / 0.03)
    x = np.sin(phase_from_freq(freq)) * np.exp(-t / dec)
    x[: int(0.003 * SR)] += filt(noise(int(0.003 * SR), rng), "hp", 2000) * 0.3
    return x * 0.8


def snare(rng, tone=190, dec=0.12, snappy=0.7):
    t = _t(0.35)
    nz = filt(noise(len(t), rng), "bp", (1200, 7000)) * np.exp(-t / dec) * snappy
    tn = np.sin(2 * np.pi * tone * t) * np.exp(-t / 0.05)
    return (nz + tn * 0.6) * 0.45


def hat(rng, dec=0.035, f=7000):
    t = _t(0.25)
    return filt(noise(len(t), rng), "hp", f) * np.exp(-t / dec) * 0.25


def shaker(rng, dec=0.05):
    t = _t(0.15)
    e = np.clip(t / 0.012, 0, 1) * np.exp(-t / dec)
    return filt(noise(len(t), rng), "bp", (4000, 11000)) * e * 0.22


def woodblock(rng, f=900):
    t = _t(0.15)
    x = np.sin(2 * np.pi * f * t) * np.exp(-t / 0.025) + 0.4 * np.sin(2 * np.pi * f * 2.45 * t) * np.exp(-t / 0.012)
    return x * 0.35


def frame_drum(rng, f=85):
    t = _t(0.6)
    freq = f * (1 + 0.25 * np.exp(-t / 0.02))
    x = np.sin(phase_from_freq(freq)) * np.exp(-t / 0.22)
    x += filt(noise(len(t), rng), "lp", 900) * np.exp(-t / 0.04) * 0.5
    return x * 0.5


def cowbell(rng):
    t = _t(0.4)
    x = np.sign(np.sin(2 * np.pi * 562 * t)) + np.sign(np.sin(2 * np.pi * 845 * t))
    x = filt(x, "bp", (500, 3000)) * np.exp(-t / 0.09)
    return x * 0.12


def crash(rng, dec=0.9):
    t = _t(2.5)
    return filt(noise(len(t), rng), "hp", 3500) * np.exp(-t / dec) * 0.18


def boing(rng, f0=700, f1=160, dur=0.45):
    """Cartoon spring: falling pitch with a widening wobble."""
    t = _t(dur)
    freq = f1 + (f0 - f1) * np.exp(-t / (dur * 0.35))
    freq = freq * (1 + 0.06 * np.sin(2 * np.pi * 18 * t) * (t / dur))
    x = np.sin(phase_from_freq(freq)) + 0.3 * np.sin(2 * phase_from_freq(freq))
    return x * np.exp(-t / (dur * 0.4)) * np.clip(t / 0.003, 0, 1) * 0.25


def bitcrush(x, rate=11025, bits=7):
    step = max(1, int(SR / rate))
    y = np.repeat(x[::step], step)[: len(x)]
    q = 2 ** (bits - 1)
    return np.round(y * q) / q


# ---------------------------------------------------------------- mixing

class Mix:
    def __init__(self, loop_sec, seed):
        self.L = int(round(loop_sec * SR))
        self.buf = {}
        self.rng = np.random.default_rng(seed)

    def bus(self, name):
        if name not in self.buf:
            self.buf[name] = np.zeros((self.L + int(TAIL * SR), 2))
        return self.buf[name]

    def add(self, x, start, pan=0.0, gain=1.0, bus="main", jitter=0.0):
        if jitter:
            start += self.rng.uniform(-jitter, jitter)
        b = self.bus(bus)
        L = self.L
        a = int(round(start * SR)) % L
        l = np.cos((pan + 1) * np.pi / 4) * gain
        r = np.sin((pan + 1) * np.pi / 4) * gain
        pos = 0
        while pos < len(x):  # wrap around the loop point as many times as needed
            seg = x[pos: pos + (L - a)]
            b[a: a + len(seg), 0] += seg * l
            b[a: a + len(seg), 1] += seg * r
            pos += len(seg)
            a = 0

    def fold(self, name):
        return self.buf[name][: self.L].copy()


def circ_reverb(x, rng, length=2.2, decay=0.55, lp=6000, predelay=0.012):
    """Circular convolution with a decaying-noise IR (wraps across the loop point)."""
    L = x.shape[0]
    n = int(length * SR)
    t = np.arange(n) / SR
    out = np.zeros_like(x)
    for ch in range(2):
        ir = filt(rng.standard_normal(n), "lp", lp) * np.exp(-t / decay)
        ir[: int(predelay * SR)] = 0
        ir /= np.sqrt(np.sum(ir ** 2))
        irp = np.zeros(L)
        irp[:n] = ir
        out[:, ch] = np.real(np.fft.ifft(np.fft.fft(x[:, ch]) * np.fft.fft(irp)))
    return out


def circ_eq(x, hp=35, lp=None, tilt_db=0.0):
    L = x.shape[0]
    f = np.fft.rfftfreq(L, 1 / SR)
    g = 1 / np.sqrt(1 + (hp / np.maximum(f, 1e-3)) ** 4)
    if lp:
        g *= 1 / np.sqrt(1 + (f / lp) ** 4)
    if tilt_db:
        g *= 10 ** (tilt_db / 20 * np.log2(np.maximum(f, 20) / 1000) / 4)
    return np.stack([np.fft.irfft(np.fft.rfft(x[:, c]) * g, n=L) for c in range(2)], 1)


def circ_wow(x, loop_sec, cycles=(11, 3), cents=(9, 5)):
    """Tape wow/flutter with an integer number of cycles per loop -> seamless."""
    L = x.shape[0]
    t = np.arange(L) / SR
    off = np.zeros(L)
    for c, ct in zip(cycles, cents):
        f = c / loop_sec
        dev = (2 ** (ct / 1200) - 1)
        off += dev / (2 * np.pi * f) * np.sin(2 * np.pi * f * t)
    pos = (t + off) * SR
    idx = np.arange(L)
    out = np.zeros_like(x)
    for ch in range(2):
        xe = np.concatenate([x[:, ch], x[:2, ch]])
        p = pos % L
        out[:, ch] = np.interp(p, np.arange(L + 2), xe)
    return out


def master(x, target_rms_db=-17.0, drive=1.3):
    rms = np.sqrt(np.mean(x ** 2))
    x = x * (10 ** (target_rms_db / 20) / rms)
    x = np.tanh(x * drive) / np.tanh(drive)
    peak = np.max(np.abs(x))
    if peak > 0.89:
        x *= 0.89 / peak
    return x


def write(path, x):
    wavfile.write(path, SR, (np.clip(x, -1, 1) * 32767).astype(np.int16))
