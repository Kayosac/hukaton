"""Engine v2: breakbeats, chops, scratches, vocal shouts, acid, reese, guitars, harpsichord, ringtone,
tape stops / rewinds / glitches, compressors. Hand-written riffs via a compact step notation."""
import numpy as np
import numba
from engine import *

TWO_PI = 2 * np.pi


# ---------------------------------------------------------------- numba DSP
@numba.njit(cache=True)
def svf(x, fc, q, mode):
    """TPT state-variable filter with per-sample cutoff. mode 0=LP 1=BP 2=HP."""
    n = len(x)
    y = np.empty(n)
    ic1 = 0.0
    ic2 = 0.0
    k = 1.0 / q
    for i in range(n):
        f = fc[i]
        if f < 20.0:
            f = 20.0
        if f > 18000.0:
            f = 18000.0
        g = np.tan(np.pi * f / 44100.0)
        a1 = 1.0 / (1.0 + g * (g + k))
        a2 = g * a1
        a3 = g * a2
        v3 = x[i] - ic2
        v1 = a1 * ic1 + a2 * v3
        v2 = ic2 + a2 * ic1 + a3 * v3
        ic1 = 2.0 * v1 - ic1
        ic2 = 2.0 * v2 - ic2
        if mode == 0:
            y[i] = v2
        elif mode == 1:
            y[i] = v1
        else:
            y[i] = x[i] - k * v1 - v2
    return y


@numba.njit(cache=True)
def _env_follow(a, att, rel):
    n = len(a)
    e = np.empty(n)
    s = 0.0
    for i in range(n):
        v = a[i]
        c = att if v > s else rel
        s = c * s + (1 - c) * v
        e[i] = s
    return e


def compress(x, thr_db=-18, ratio=4, att=0.005, rel=0.12, makeup_db=0.0):
    """Feed-forward stereo-linked compressor (runs the loop twice so the start is settled)."""
    mono = np.abs(x).max(1)
    a = np.exp(-1 / (att * SR))
    r = np.exp(-1 / (rel * SR))
    env = _env_follow(np.concatenate([mono, mono]), a, r)[len(mono):]
    db = 20 * np.log10(env + 1e-9)
    over = np.maximum(db - thr_db, 0)
    gain = 10 ** ((-over * (1 - 1 / ratio) + makeup_db) / 20)
    return x * gain[:, None]


def crush(x, bits=8, hold=1):
    y = x
    if hold > 1:
        idx = (np.arange(len(x)) // hold) * hold
        y = y[idx]
    q = 2 ** (bits - 1)
    return np.round(y * q) / q


def drive(x, amt=2.0):
    return np.tanh(x * amt) / np.tanh(amt)


# ---------------------------------------------------------------- drum kit v2 (mono)
_METAL = np.array([205.3, 304.4, 369.6, 522.7, 540.0, 800.0])


def _metal(n, scale=1.0, rng=None):
    t = tvec(n)
    y = np.zeros(n)
    for f in _METAL * scale:
        y += np.sign(np.sin(TWO_PI * f * t + (rng.uniform(0, 6) if rng is not None else 0)))
    return y / len(_METAL)


def kick2(rng, punch=1.0, tau=0.28, f0=165, f1=48, drv=1.6):
    n = int(tau * 4 * SR)
    t = tvec(n)
    fr = f1 + (f0 - f1) * np.exp(-t / 0.028)
    y = sine_f(fr) * np.exp(-t / tau)
    click = hp(rng.randn(300), 1500) * np.exp(-tvec(300) / 0.0015) * 0.5 * punch
    y[:300] += click
    return fade_tail(drive(y * 1.2, drv))


def k808(rng, f=48, tau=0.9, drv=1.4):
    n = int(tau * 3.5 * SR)
    t = tvec(n)
    fr = f + f * 1.6 * np.exp(-t / 0.02)
    return fade_tail(drive(sine_f(fr) * np.exp(-t / tau), drv))


def snare2(rng, tone=185, tau=0.17, crack=1.0, body=0.8):
    n = int(tau * 5 * SR)
    t = tvec(n)
    y = body * (np.sin(TWO_PI * tone * t) + 0.5 * np.sin(TWO_PI * tone * 1.63 * t)) * np.exp(-t / 0.06)
    y += hp(rng.randn(n), 900) * np.exp(-t / tau) * 0.9
    y += crack * bp(rng.randn(n), 3000, 9000) * np.exp(-t / 0.012)
    return fade_tail(drive(y, 1.5))


def hat2(rng, tau=0.04, open_=False):
    tau = 0.28 if open_ else tau
    n = int(tau * 5 * SR)
    t = tvec(n)
    y = hp(_metal(n, 1.0, rng) + 0.4 * rng.randn(n), 7000, 2) * np.exp(-t / tau)
    y[:20] *= np.linspace(0, 1, 20)
    return fade_tail(y)


def ride(rng):
    n = int(1.6 * SR)
    t = tvec(n)
    y = bp(_metal(n, 2.3, rng), 4000, 12000) * np.exp(-t / 0.7) + 0.3 * hp(rng.randn(n), 9000) * np.exp(-t / 0.05)
    return fade_tail(y)


def crash(rng):
    n = int(2.5 * SR)
    t = tvec(n)
    y = hp(_metal(n, 1.7, rng) * 0.5 + rng.randn(n), 3000) * np.exp(-t / 0.9)
    y[:40] *= np.linspace(0, 1, 40)
    return fade_tail(y)


def cowbell(rng):
    n = int(0.4 * SR)
    t = tvec(n)
    y = (np.sign(np.sin(TWO_PI * 540 * t)) + np.sign(np.sin(TWO_PI * 800 * t))) * 0.5
    y = bp(y, 400, 2500) * (0.7 * np.exp(-t / 0.02) + 0.3 * np.exp(-t / 0.18))
    return fade_tail(y)


def tamb(rng):
    n = int(0.25 * SR)
    t = tvec(n)
    jingle = hp(rng.randn(n), 6000) * (0.6 + 0.4 * np.sin(TWO_PI * 60 * t)) * np.exp(-t / 0.07)
    return fade_tail(jingle)


def rim(rng):
    n = int(0.08 * SR)
    t = tvec(n)
    return fade_tail(np.sin(TWO_PI * 1700 * t) * np.exp(-t / 0.008) + 0.6 * bp(rng.randn(n), 2000, 6000) * np.exp(-t / 0.004))


KIT = {
    'k': lambda r: kick2(r), 'K': lambda r: kick2(r, punch=1.4), 'b': lambda r: k808(r), 'B': lambda r: k808(r, f=41, tau=1.3),
    's': lambda r: snare2(r), 'S': lambda r: snare2(r, crack=1.3), 'g': lambda r: snare2(r, tau=0.08, crack=0.3, body=0.4),
    'h': lambda r: hat2(r), 'o': lambda r: hat2(r, open_=True), 'r': lambda r: ride(r), 'C': lambda r: crash(r),
    'c': lambda r: clap(r), 'w': lambda r: cowbell(r), 't': lambda r: tamb(r), 'm': lambda r: tom(r), 'n': lambda r: tom(r, 240, 140, 0.2),
    'i': lambda r: rim(r), 'z': lambda r: coin(r), 'q': lambda r: clock_tick(r), 'x': lambda r: shaker(r),
    'T': lambda r: taiko(r), 'u': lambda r: brush(r, 0.2),
}
VEL = {'k': .9, 'K': 1.0, 'b': 1.0, 'B': 1.0, 's': .85, 'S': 1.0, 'g': .28, 'h': .45, 'o': .5, 'r': .4, 'C': .7, 'c': .8,
       'w': .6, 't': .5, 'm': .8, 'n': .7, 'i': .6, 'z': .5, 'q': .6, 'x': .4, 'T': 1.0, 'u': .6}


# ---------------------------------------------------------------- instruments v2 (mono)
def acid(m, dur, rng, cut=400, env=2600, res=6.0, decay=0.18, accent=False, slide_from=None, sq=False, drv=2.0):
    f = mtof(m)
    n = int((dur + 0.05) * SR)
    t = tvec(n)
    fr = np.full(n, f)
    if slide_from is not None:
        fr = f * 2 ** ((slide_from - m) / 12 * np.exp(-t / 0.035))
    x = pulse_f(fr, 0.5, rng=rng) if sq else saw_f(fr, rng=rng)
    e = env * (1.6 if accent else 1.0)
    fc = cut + e * np.exp(-t / decay)
    y = svf(x.astype(np.float64), fc.astype(np.float64), res, 0)
    amp = np.ones(n)
    amp[:40] = np.linspace(0, 1, 40)
    k = int(0.01 * SR)
    amp[-k:] = np.linspace(1, 0, k)
    return drive(y * (1.4 if accent else 1.0), drv) * amp


def reese(m, dur, rng, cutoff=700, a=0.01, r=0.1):
    f = mtof(m)
    nh = int(dur * SR)
    e = adsr(nh, a, 0.2, 0.9, r)
    n = len(e)
    t = tvec(n)
    x = saw_f(const(f * cents(-14), n), rng=rng) + saw_f(const(f * cents(14), n), rng=rng) + 0.8 * sine_f(const(f / 2, n))
    fc = cutoff * (1 + 0.5 * np.sin(TWO_PI * 0.7 * t))
    y = svf(x, fc, 1.2, 0)
    return drive(y * 0.8, 1.8) * e


def sub(m, dur, rng, a=0.005, r=0.06):
    f = mtof(m)
    nh = int(dur * SR)
    e = adsr(nh, a, 0.3, 0.9, r)
    return drive(sine_f(const(f, len(e))) * 1.2, 1.3) * e


def guitar(m, dur, rng, mute=False, fifth=True, octave=True, drv=14.0, cab=4200):
    """Distorted power-chord guitar (drop-tuned riffs)."""
    notes = [m] + ([m + 7] if fifth else []) + ([m + 12] if octave else [])
    d = min(dur, 0.14) if mute else dur
    x = sum(pluck(k, d, rng, tau=2.5, pos=0.15, bright=1.3, damp=True) for k in notes)
    x = x[:max(len(x), 10)]
    if mute:
        x = lp(x, 1400)
    y = drive(x * drv, 1.0)
    y = bp(y, 90, cab, 2)
    y = lp(y, cab * 1.1, 2)
    return y * 0.5


def wah_guitar(chord, dur, rng, f0=450, f1=2200, decay=0.12):
    """Funky muted clean strum through an envelope wah."""
    x = sum(pluck(m, min(dur, 0.18), rng, tau=0.6, pos=0.2, bright=1.2, damp=True) for m in chord)
    n = len(x)
    t = tvec(n)
    fc = f0 + (f1 - f0) * np.exp(-t / decay) * np.minimum(t / 0.01, 1)
    return svf(x, fc, 4.0, 1) * 2.0


def harpsichord(m, dur, rng):
    y = pluck(m, dur, rng, tau=1.0, pos=0.07, bright=1.5)
    y2 = pluck(m + 12, dur, rng, tau=0.7, pos=0.07, bright=1.5)
    n = min(len(y), len(y2))
    out = y[:n] + 0.45 * y2[:n]
    out[:200] += 0.15 * hp(rng.randn(200), 3000) * np.exp(-tvec(200) / 0.002)
    return out


def slap(m, dur, rng, pop=False):
    y = pluck(m, dur, rng, tau=0.7 if not pop else 0.4, pos=0.05 if pop else 0.12, bright=1.6 if pop else 1.0, damp=True)
    n = len(y)
    t = tvec(n)
    f = mtof(m)
    y += (0.8 if not pop else 0.3) * np.sin(TWO_PI * f * t) * np.exp(-t / 0.15)
    return drive(y, 1.6)


def stab(chord_notes, dur, rng, bright=3200):
    """Brass section stab: scoop up, fast filter envelope."""
    out = None
    for m in chord_notes:
        f = mtof(m)
        n = int((dur + 0.15) * SR)
        t = tvec(n)
        fr = f * 2 ** (-0.8 / 12 * np.exp(-t / 0.03))
        x = sum(saw_f(fr * cents(c), rng=rng) for c in (-8, 0, 7)) / 3
        fc = 500 + bright * np.exp(-t / 0.12)
        y = svf(x, fc, 1.0, 0)
        e = adsr(int(dur * SR), 0.01, 0.15, 0.6, 0.12)[:n]
        y = y[:len(e)] * e
        out = y if out is None else out[:len(y)] + y[:len(out)]
    return drive(out, 1.4)


def ringtone(m, dur, rng):
    n = int(dur * SR)
    t = tvec(n)
    y = np.sign(np.sin(TWO_PI * mtof(m) * t)) * 0.5
    k = min(200, n // 4)
    y[:k] *= np.linspace(0, 1, k)
    y[-k:] *= np.linspace(1, 0, k)
    return y


def chip(m, dur, rng, width=0.25, vib=0.0):
    n = int(dur * SR)
    t = tvec(n)
    fr = mtof(m) * (1 + vib * np.sin(TWO_PI * 6 * t) * np.clip((t - 0.15) / 0.2, 0, 1))
    ph = np.cumsum(fr) / SR % 1
    y = np.where(ph < width, 1.0, -1.0) * 0.5
    e = np.ones(n)
    k = min(150, n // 4)
    e[:k] = np.linspace(0, 1, k)
    e[-k:] = np.linspace(1, 0, k)
    return y * e


def hammond(m, dur, rng):
    y = organ(m, dur, rng, bars=(1, .8, .6, .3, .5))
    n = len(y)
    perc = 0.4 * np.sin(TWO_PI * mtof(m) * 3 * tvec(n)) * np.exp(-tvec(n) / 0.15)
    return drive((y + perc) * 0.7, 1.8)


# ---------------------------------------------------------------- voice-ish shouts (formants)
FORM = {'a': (800, 1150, 2900), 'e': (450, 1900, 2600), 'i': (280, 2250, 2950), 'o': (450, 800, 2830),
        'u': (325, 700, 2530), 'h': (600, 1500, 2600), 'y': (280, 2250, 2950)}
WORDS = {'hey': [('h', .05), ('e', .14), ('i', .1)], 'yeah': [('y', .05), ('e', .08), ('a', .22)],
         'oh': [('o', .3)], 'ha': [('h', .04), ('a', .16)], 'uh': [('u', .16)], 'go': [('g', .02), ('o', .22)],
         'ah': [('a', .35)], 'woo': [('u', .1), ('o', .3)], 'yo': [('y', .05), ('o', .22)], 'hi': [('h', .04), ('a', .08), ('i', .12)]}


def vox(word, m, rng, speed=1.0, glide=0.0, breath=0.15, grit=0.0):
    segs = WORDS[word]
    total = sum(d for _, d in segs) / speed + 0.04
    n = int(total * SR)
    t = tvec(n)
    f = mtof(m) * 2 ** (glide / 12 * np.clip(t / total, 0, 1))
    f = f * (1 + 0.012 * np.sin(TWO_PI * 5.5 * t))
    src = saw_f(f, rng=rng)
    if grit:
        src = drive(src * (1 + grit * 4), 1 + grit * 3)
    noise = rng.randn(n)
    F = np.zeros((3, n))
    voiced = np.ones(n)
    pos = 0
    prev = None
    for ph, d in segs:
        k = min(int(d / speed * SR), n - pos)
        if k <= 0:
            break
        tgt = FORM['h' if ph in 'gk' else ph]
        st = FORM[prev] if prev else tgt
        for j in range(3):
            F[j, pos:pos + k] = np.linspace(st[j], tgt[j], k)
        if ph in 'hgk':
            voiced[pos:pos + k] = 0.15
        pos += k
        prev = 'h' if ph in 'gk' else ph
    F[:, pos:] = F[:, max(pos - 1, 0)][:, None]
    exc = src * voiced + noise * (1 - voiced) * 1.5 + breath * noise
    y = np.zeros(n)
    for j, g in enumerate((1.0, 0.6, 0.25)):
        y += g * svf(exc, F[j], 6.0, 1)
    e = np.ones(n)
    a = int(0.008 * SR)
    e[:a] = np.linspace(0, 1, a)
    r = int(0.04 * SR)
    e[-r:] = np.linspace(1, 0, r)
    return y * e


def scratch(src, dur, kind='baby', rate=4.0, depth=0.12, fader=True):
    """Turntable scratch over `src` (mono). Position moves forward/back; amplitude follows platter speed."""
    n = int(dur * SR)
    t = tvec(n)
    if kind == 'baby':
        pos = depth * (1 - np.cos(TWO_PI * rate * t)) / 2
        gate = np.ones(n)
    elif kind == 'chirp':
        pos = depth * (1 - np.cos(TWO_PI * rate * t)) / 2
        gate = (np.sin(TWO_PI * rate * t) > 0).astype(float) if fader else np.ones(n)
    elif kind == 'transform':
        pos = depth * (1 - np.cos(TWO_PI * rate * 0.5 * t)) / 2
        gate = (np.sin(TWO_PI * rate * 4 * t) > 0).astype(float)
    else:  # scribble
        pos = depth * 0.3 * (1 + np.sin(TWO_PI * rate * 4 * t)) / 2 + depth * 0.5 * t / dur
        gate = np.ones(n)
    vel = np.gradient(pos) * SR
    idx = np.clip(pos * SR, 0, len(src) - 2)
    i0 = idx.astype(int)
    fr = idx - i0
    y = src[i0] * (1 - fr) + src[i0 + 1] * fr
    amp = np.clip(np.abs(vel) / (depth * rate * 1.5 + 1e-9), 0, 1.2)
    k = int(0.004 * SR)
    g = np.convolve(gate, np.ones(k) / k, 'same')
    y = y * amp * g
    return hp(y, 120)


# ---------------------------------------------------------------- fx on the full mix (circular)
def tape_stop(x, t0, dur, tail=0.15):
    """From t0 the tape slows to a halt over `dur` seconds; silence until t0+dur+tail, then the loop carries on."""
    L = len(x)
    n = int(dur * SR)
    tt = np.arange(n) / n
    rate = (1 - tt) ** 1.6
    pos = int(t0 * SR) + np.cumsum(rate)
    i0 = np.floor(pos).astype(int)
    fr = pos - i0
    seg = x[i0 % L] * (1 - fr)[:, None] + x[(i0 + 1) % L] * fr[:, None]
    seg = seg * np.linspace(1, 0.2, n)[:, None]
    y = x.copy()
    idx = (int(t0 * SR) + np.arange(n)) % L
    y[idx] = seg
    k = int(tail * SR)
    idx2 = (int(t0 * SR) + n + np.arange(k)) % L
    y[idx2] *= np.linspace(0, 1, k)[:, None] ** 2
    return y


def rewind(x, t0, dur, speed=7.0):
    L = len(x)
    n = int(dur * SR)
    pos = int(t0 * SR) - np.arange(n) * speed
    i0 = np.floor(pos).astype(int) % L
    seg = x[i0]
    seg = cbp(np.vstack([seg, seg]), 300, 5000)[n:] if n > 10 else seg
    seg *= 0.6
    y = x.copy()
    idx = (int(t0 * SR) + np.arange(n)) % L
    k = int(0.01 * SR)
    seg[:k] *= np.linspace(0, 1, k)[:, None]
    seg[-k:] *= np.linspace(1, 0, k)[:, None]
    y[idx] = seg
    return y


def dropout(x, rng, times, dur=0.12):
    y = x.copy()
    L = len(x)
    for t in times:
        n = int(dur * rng.uniform(0.5, 1.5) * SR)
        idx = (int(t * SR) + np.arange(n)) % L
        env = 1 - np.hanning(n) * rng.uniform(0.7, 1.0)
        y[idx] = y[idx] * env[:, None]
    return y


def dtmf(digit, dur=0.12):
    tab = {'1': (697, 1209), '2': (697, 1336), '3': (697, 1477), '4': (770, 1209), '5': (770, 1336), '6': (770, 1477),
           '7': (852, 1209), '8': (852, 1336), '9': (852, 1477), '*': (941, 1209), '0': (941, 1336), '#': (941, 1477)}
    a, b = tab[digit]
    n = int(dur * SR)
    t = tvec(n)
    y = 0.5 * (np.sin(TWO_PI * a * t) + np.sin(TWO_PI * b * t))
    k = 100
    y[:k] *= np.linspace(0, 1, k)
    y[-k:] *= np.linspace(1, 0, k)
    return y


def static(n, rng):
    return bp(rng.randn(n), 800, 9000) + 0.3 * rng.randn(n)


def riser(dur, rng, f0=300, f1=7000):
    n = int(dur * SR)
    t = tvec(n) / dur
    fc = f0 * (f1 / f0) ** t
    y = svf(rng.randn(n), fc, 3.0, 1) * t ** 2
    return y


def impact(rng):
    n = int(2.0 * SR)
    t = tvec(n)
    y = sine_f(40 + 80 * np.exp(-t / 0.05)) * np.exp(-t / 0.6) + 0.4 * crash(rng)[:n] * 0.6
    return fade_tail(drive(y, 1.5))


def siren(dur, rng, f=600, depth=300, rate=2.0):
    n = int(dur * SR)
    t = tvec(n)
    fr = f + depth * np.sin(TWO_PI * rate * t)
    y = saw_f(fr, rng=rng)
    return lp(y, 3000) * adsr(n, 0.05, 0.1, 1, 0.1)[:n]


# ---------------------------------------------------------------- song v2
NOTE_RE = {}


def parse_note(tok):
    """'e2', 'f#3', 'bb1' → midi."""
    t = tok.lower()
    name = t[0].upper()
    i = 1
    if i < len(t) and t[i] in '#b' and not t[i:].lstrip('#b').startswith('-') and len(t) > 2:
        name += t[i]
        i += 1
    octv = int(t[i:])
    return 12 * (octv + 1) + NOTE[name]


class Song2(Song):
    def riff(self, layer, inst, text, step=0.25, start_bar=0, end_bar=None, vel=1.0, legato=0.92, pan=0.0, transpose=0, every=None, **kw):
        """Step notation: whitespace-separated tokens, '|' ignored; note ('e2', 'f#3'), optional suffix '!' accent '?' ghost '~' slide;
        '-' holds the previous note one more step; '.' rest. The pattern repeats from start_bar to end_bar."""
        toks = [x for x in text.replace('|', ' ').split()]
        steps_total = int(round(((end_bar or self.bars) - start_bar) * self.bpb / step))
        evs = []
        i = 0
        while i < steps_total:
            tok = toks[i % len(toks)]
            if tok in ('.', '-'):
                i += 1
                continue
            acc = 1.25 if '!' in tok else (0.45 if '?' in tok else 1.0)
            slide = '~' in tok
            m = parse_note(tok.strip('!?~')) + transpose
            j = i + 1
            while j < steps_total and toks[j % len(toks)] == '-':
                j += 1
            evs.append((i, j - i, m, acc, slide))
            i = j
        prev_m = None
        for (i, d, m, acc, slide) in evs:
            beat = start_bar * self.bpb + i * step
            extra = {}
            if slide and prev_m is not None and inst in ('acid',):
                extra['slide_from'] = prev_m
            if inst == 'acid' and acc > 1:
                extra['accent'] = True
            f = {'acid': acid, 'reese': reese, 'sub': sub, 'guitar': guitar, 'harpsichord': harpsichord, 'slap': slap,
                 'ringtone': ringtone, 'chip': chip, 'hammond': hammond}.get(inst, inst) if isinstance(inst, str) else inst
            if f is guitar and '?' in toks[i % len(toks)]:
                extra['mute'] = True
                acc = 0.9
            if f is slap and m >= 40:
                extra['pop'] = True
            self.note(layer, f, m, beat, d * step * legato, vel * acc, pan=pan, **{**kw, **extra})
            prev_m = m

    def chords(self, layer, inst_fn, text, step=0.25, start_bar=0, end_bar=None, center=60, n=3, vel=1.0, legato=0.9, pan=0.0):
        """Chord-stab notation: tokens are chord names ('Em', 'F#m7') or '.'/'-'; inst_fn(notes, dur, rng)."""
        toks = text.replace('|', ' ').split()
        steps_total = int(round(((end_bar or self.bars) - start_bar) * self.bpb / step))
        i = 0
        while i < steps_total:
            tok = toks[i % len(toks)]
            if tok in ('.', '-'):
                i += 1
                continue
            j = i + 1
            while j < steps_total and toks[j % len(toks)] == '-':
                j += 1
            beat = start_bar * self.bpb + i * step
            bar = int(beat // self.bpb)
            if self.on(layer, bar):
                notes = Chord(tok).voicing(center, n)
                sig = inst_fn(notes, (j - i) * step * legato * self.Q, self.rng)
                self.loop.add(layer, sig, self.t(beat), pan=pan, gain=vel)
            i = j

    def kit(self, layer, pattern, steps=16, jitter=0.003, vel=1.0, pans=None, humanize=0.1):
        """Drum grid with KIT chars; pattern: {lane: str or [str per bar]}."""
        pans = pans or {}
        for bar in range(self.bars):
            if not self.on(layer, bar):
                continue
            for lane, pat in pattern.items():
                p = pat[bar % len(pat)] if isinstance(pat, list) else pat
                st = len(p)
                for i, ch in enumerate(p):
                    if ch in '.-':
                        continue
                    beat = bar * self.bpb + i * self.bpb / st
                    v = VEL.get(ch, .7) * vel * self.rng.uniform(1 - humanize, 1 + humanize * 0.3)
                    self.loop.add(layer, KIT[ch](self.rng), self.t(beat) + self.rng.uniform(-jitter, jitter), pan=pans.get(ch, 0), gain=v)

    def make_break(self, pattern, bars=2, swing=0.0, crunch=True, room=0.18, seed=0):
        """Render a source break (stereo) of `bars` bars at the song tempo from a KIT grid."""
        rng = np.random.RandomState(1000 + seed)
        bl = self.bpb * self.Q
        L = int(round(bars * bl * SR))
        out = np.zeros((L + int(SR), 2))
        for b in range(bars):
            for lane, pat in pattern.items():
                p = pat[b % len(pat)] if isinstance(pat, list) else pat
                st = len(p)
                for i, ch in enumerate(p):
                    if ch in '.-':
                        continue
                    pos = i / st
                    frac = pos * self.bpb - np.floor(pos * self.bpb)
                    if swing and abs(frac - 0.25) < 1e-6:
                        pos += swing / 4 / self.bpb
                    if swing and abs(frac - 0.75) < 1e-6:
                        pos += swing / 4 / self.bpb
                    t = (b + pos) * bl + rng.uniform(-0.003, 0.003)
                    sig = KIT[ch](rng) * VEL.get(ch, .7) * rng.uniform(0.9, 1.05)
                    p0 = int(max(t, 0) * SR)
                    pan = {'h': .25, 'o': .25, 'r': -.3}.get(ch, 0)
                    th = (pan + 1) * np.pi / 4
                    seg = sig[:len(out) - p0]
                    out[p0:p0 + len(seg), 0] += seg * np.cos(th) * 1.41
                    out[p0:p0 + len(seg), 1] += seg * np.sin(th) * 1.41
        # wrap the tail
        tail = out[L:]
        out = out[:L]
        out[:len(tail)] += tail[:L]
        if room:
            ir = make_ir(rng, t60=0.6, damp=5000, width=0.6)
            out = out + room * creverb(out, ir)
        if crunch:
            out = crush(drive(out * 1.4, 1.6), bits=12, hold=2)
            out = clp(out, 11000)
        out = compress(out, -14, 4, 0.002, 0.08, 4)
        return out

    def chop(self, layer, brk, seqs, steps_per_bar=16, start_bar=0, end_bar=None, vel=1.0, fade=0.002):
        """Place 16th slices of a break by index per bar. seqs: list (cycled per bar) of token lists/strings:
        int = slice, 'rN' = reversed slice N, 'sN' = slice N stuttered as two 32nds, '.' = silence, '-' = let the previous slice run on."""
        nsl = int(round(len(brk) / (self.bpb * self.Q * SR) * steps_per_bar))
        sl_len = len(brk) / nsl
        stepsec = self.bpb * self.Q / steps_per_bar
        fl = int(fade * SR)
        end_bar = end_bar or self.bars
        for bar in range(start_bar, end_bar):
            if not self.on(layer, bar):
                continue
            seq = seqs[(bar - start_bar) % len(seqs)]
            toks = seq.split() if isinstance(seq, str) else seq
            i = 0
            while i < len(toks):
                tok = str(toks[i])
                if tok in ('.', '-'):
                    i += 1
                    continue
                run = 1
                while i + run < len(toks) and str(toks[i + run]) == '-':
                    run += 1
                rev = tok.startswith('r')
                stut = tok.startswith('s')
                idx = int(tok.lstrip('rs')) % nsl
                a = int(idx * sl_len)
                n = int(stepsec * SR * run) + fl
                if stut:
                    half = int(stepsec * SR / 2)
                    piece = brk[a:a + half]
                    seg = np.concatenate([piece, piece] + [brk[a:a + half]] * (run * 2 - 2))[:n]
                else:
                    seg = np.take(brk, np.arange(a, a + n) % len(brk), axis=0)
                    if rev:
                        seg = seg[::-1]
                seg = seg.copy()
                k = min(fl, len(seg) // 2)
                if k > 1:
                    seg[:k] *= np.linspace(0, 1, k)[:, None]
                    seg[-k:] *= np.linspace(1, 0, k)[:, None]
                self.loop.add(layer, seg, self.t(bar * self.bpb) + i * stepsec, gain=vel)
                i += run


def straight(bar_in_break, steps=16):
    return [bar_in_break * steps + i for i in range(steps)]


def render2(s, target_rms=-17.0, comp=True, post=None, **kw):
    """Song.render with a glue compressor on the master; louder targets for the breakbeat tracks."""
    def post2(s_, mix):
        if post:
            mix = post(s_, mix)
        if comp:
            mix = compress(mix, -20, 3.0, 0.004, 0.15, 3)
        return mix
    return s.render(target_rms=target_rms, post=post2, **kw)
