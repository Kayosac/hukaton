"""Composition engine on top of synth.py: chords, scales, motif melodies, arps, basslines,
drum grids, arrangement sections, per-layer auto-balance, per-bus FX, seamless loops."""
import numpy as np
from synth import *

NOTE = {'C': 0, 'C#': 1, 'Db': 1, 'D': 2, 'D#': 3, 'Eb': 3, 'E': 4, 'F': 5, 'F#': 6, 'Gb': 6, 'G': 7,
        'G#': 8, 'Ab': 8, 'A': 9, 'A#': 10, 'Bb': 10, 'B': 11}
QUAL = {'': [0, 4, 7], 'M': [0, 4, 7], 'm': [0, 3, 7], '7': [0, 4, 7, 10], 'M7': [0, 4, 7, 11], 'm7': [0, 3, 7, 10],
        'm9': [0, 3, 7, 10, 14], 'M9': [0, 4, 7, 11, 14], 'sus2': [0, 2, 7], 'sus4': [0, 5, 7], 'dim': [0, 3, 6],
        'm7b5': [0, 3, 6, 10], 'aug': [0, 4, 8], '5': [0, 7], 'add9': [0, 4, 7, 14], 'madd9': [0, 3, 7, 14],
        'm6': [0, 3, 7, 9], '6': [0, 4, 7, 9], '7sus4': [0, 5, 7, 10], 'M7#11': [0, 4, 7, 11, 18], '9': [0, 4, 7, 10, 14],
        'mM7': [0, 3, 7, 11], 'dim7': [0, 3, 6, 9], '7b9': [0, 4, 7, 10, 13], 'm11': [0, 3, 7, 10, 14, 17]}
MODES = {'ionian': [0, 2, 4, 5, 7, 9, 11], 'dorian': [0, 2, 3, 5, 7, 9, 10], 'phrygian': [0, 1, 3, 5, 7, 8, 10],
         'lydian': [0, 2, 4, 6, 7, 9, 11], 'mixolydian': [0, 2, 4, 5, 7, 9, 10], 'aeolian': [0, 2, 3, 5, 7, 8, 10],
         'harmonic': [0, 2, 3, 5, 7, 8, 11], 'penta': [0, 3, 5, 7, 10], 'mpenta': [0, 2, 4, 7, 9],
         'whole': [0, 2, 4, 6, 8, 10], 'octatonic': [0, 1, 3, 4, 6, 7, 9, 10], 'hijaz': [0, 1, 4, 5, 7, 8, 10]}


class Chord:
    def __init__(self, s):
        r = s[:2] if len(s) > 1 and s[1] in '#b' else s[:1]
        self.name = s
        self.root = NOTE[r]
        q = s[len(r):]
        bass = None
        if '/' in q:
            q, b = q.split('/')
            bass = NOTE[b]
        self.iv = QUAL[q]
        self.bass_pc = self.root if bass is None else bass
        self.pcs = sorted({(self.root + i) % 12 for i in self.iv})

    def voicing(self, center=60, n=4):
        """Close voicing of n chord tones (taken from the interval stack) centred near `center`."""
        iv = self.iv[:n] if len(self.iv) >= n else (self.iv + [i + 12 for i in self.iv])[:n]
        base = self.root + 12 * round((center - 6 - self.root) / 12)
        notes = sorted(base + i for i in iv)
        # pull the stack around the centre
        while np.mean(notes) - center > 6:
            notes = sorted(notes[:-1] + [notes[-1] - 12])
        while center - np.mean(notes) > 6:
            notes = sorted([notes[0] + 12] + notes[1:])
        return notes

    def bass(self, octave=2):
        return 12 * (octave + 1) + self.bass_pc

    def tones(self, lo, hi):
        return [m for m in range(lo, hi + 1) if m % 12 in self.pcs]

    def nearest(self, m, maxd=12):
        c = [x for x in self.tones(m - maxd, m + maxd)]
        return min(c, key=lambda x: (abs(x - m), x)) if c else m


class Scale:
    def __init__(self, tonic, mode):
        self.tonic = NOTE[tonic] if isinstance(tonic, str) else tonic
        self.pcs = sorted({(self.tonic + i) % 12 for i in MODES[mode]})
        self.notes = [m for m in range(0, 128) if m % 12 in self.pcs]

    def snap(self, m):
        return min(self.notes, key=lambda x: (abs(x - m), x))

    def step(self, m, k):
        i = self.notes.index(self.snap(m))
        return self.notes[int(np.clip(i + k, 0, len(self.notes) - 1))]


def prog(spec, bars):
    """spec: list of (chordname, nbars) → list of Chord per bar, cycled to `bars`."""
    seq = []
    for name, n in spec:
        seq += [Chord(name)] * n
    return [seq[i % len(seq)] for i in range(bars)]


# ---------------------------------------------------------------- extra instruments (mono)
def flute(m, dur, rng, breath=0.18, vib=0.006, a=0.08):
    f = mtof(m) * cents(rng.uniform(-4, 4))
    nh = int(dur * SR)
    e = adsr(nh, a, 0.3, 0.85, 0.22)
    n = len(e)
    t = tvec(n)
    v = 1 + vib * np.sin(2 * np.pi * rng.uniform(4.6, 5.6) * t) * np.clip((t - 0.25) / 0.4, 0, 1)
    fr = f * v
    y = sine_f(fr) + 0.22 * sine_f(fr * 2) + 0.06 * sine_f(fr * 3)
    y += breath * bp(rng.randn(n), f * 0.9, min(f * 3.0, 15000)) * (0.4 + 0.6 * np.exp(-t / 0.12))
    return y * e


def harp(m, dur, rng):
    return pluck(m, dur, rng, tau=1.6, pos=0.1, bright=1.25)


def marimba(m, dur, rng, tau=None):
    f = mtof(m) * cents(rng.uniform(-3, 3))
    tau = tau or 0.45 * (440 / f) ** 0.35
    n = int(tau * 5 * SR)
    t = tvec(n)
    y = np.sin(2 * np.pi * f * t) * np.exp(-t / tau)
    y += 0.25 * np.sin(2 * np.pi * f * 3.93 * t) * np.exp(-t / (tau * 0.25))
    y += 0.08 * np.sin(2 * np.pi * f * 9.2 * t) * np.exp(-t / 0.01)
    y[:30] *= np.linspace(0, 1, 30)
    return fade_tail(y)


def glock(m, dur, rng):
    f = mtof(m) * cents(rng.uniform(-3, 3))
    n = int(2.6 * SR)
    t = tvec(n)
    y = np.sin(2 * np.pi * f * t) * np.exp(-t / 0.9)
    y += 0.35 * np.sin(2 * np.pi * f * 2.76 * t) * np.exp(-t / 0.3)
    y += 0.2 * np.sin(2 * np.pi * f * 5.4 * t) * np.exp(-t / 0.12)
    y[:15] *= np.linspace(0, 1, 15)
    return fade_tail(y)


def _sweep(x, lo, hi, env):
    return lp(x, lo, 2) * (1 - env) + lp(x, hi, 2) * env


def brass(m, dur, rng, flo=500, fhi=2600, a=0.08, r=0.3):
    f = mtof(m)
    nh = int(dur * SR)
    e = adsr(nh, a, 0.5, 0.8, r)
    n = len(e)
    t = tvec(n)
    y = sum(saw_f(const(f * cents(c), n), rng=rng) for c in (-7, 0, 6)) / 3
    fe = np.clip(t / (a * 2.5), 0, 1) * np.exp(-np.maximum(t - a * 2.5, 0) / 0.8) * 0.7 + 0.15
    return _sweep(y, flo, fhi, fe) * e


def lead(m, dur, rng, cutoff=3200, vib=0.007, glide_from=None, a=0.015, sq=0.5):
    f = mtof(m)
    nh = int(dur * SR)
    e = adsr(nh, a, 0.3, 0.8, 0.2)
    n = len(e)
    t = tvec(n)
    v = 1 + vib * np.sin(2 * np.pi * 5.4 * t) * np.clip((t - 0.2) / 0.3, 0, 1)
    fr = f * v
    if glide_from is not None:
        fr = fr * 2 ** ((glide_from - m) / 12 * np.exp(-t / 0.05))
    y = (1 - sq) * saw_f(fr, rng=rng) + sq * pulse_f(fr, 0.5, rng=rng)
    return lp(y, cutoff, 2) * e


def synth_pluck(m, dur, rng, lo=300, hi=5000, decay=0.18, saw=True):
    f = mtof(m)
    n = int((dur + 0.4) * SR)
    t = tvec(n)
    y = saw_f(const(f, n), rng=rng) if saw else pulse_f(const(f, n), 0.3, rng=rng)
    y += 0.5 * saw_f(const(f * cents(9), n), rng=rng)
    env = np.exp(-t / decay)
    amp = np.exp(-t / (decay * 2.2))
    amp[:20] *= np.linspace(0, 1, 20)
    return fade_tail(_sweep(y, lo, hi, env) * amp)


def reed(m, dur, rng, cutoff=1400, width=0.18, a=0.05):
    f = mtof(m)
    nh = int(dur * SR)
    e = adsr(nh, a, 0.2, 0.85, 0.1)
    n = len(e)
    t = tvec(n)
    v = 1 + 0.005 * np.sin(2 * np.pi * 5.0 * t) * np.clip((t - 0.2) / 0.3, 0, 1)
    y = pulse_f(f * v, width, rng=rng)
    y = lp(hp(y, 150), cutoff, 2) + 0.05 * bp(rng.randn(n), f, f * 4)
    return y * e


def pizz(m, dur, rng):
    return pluck(m, min(dur, 0.3), rng, tau=0.28, pos=0.3, bright=1.1)


def organ(m, dur, rng, bars=(1, .7, .4, .25, .15)):
    f = mtof(m)
    nh = int(dur * SR)
    e = adsr(nh, 0.05, 0.1, 1.0, 0.25)
    n = len(e)
    t = tvec(n)
    les = 1 + 0.002 * np.sin(2 * np.pi * 6.2 * t)
    y = sum(g * sine_f(f * h * les) for g, h in zip(bars, (1, 2, 3, 4, 0.5)))
    return y * e * (1 + 0.12 * np.sin(2 * np.pi * 6.2 * t))


def strings(m, dur, rng, cutoff=1800, a=0.6, r=1.0):
    y = supersaw(m, dur, rng, cutoff=cutoff, a=a, r=r, voices=4, spread=8)
    t = tvec(len(y))
    return y * (1 + 0.04 * np.sin(2 * np.pi * 5.2 * t))


def taiko(rng, f0=95, f1=48, tau=0.55):
    n = int(tau * 4 * SR)
    t = tvec(n)
    fr = f1 + (f0 - f1) * np.exp(-t / 0.05)
    y = sine_f(fr) * np.exp(-t / tau) + 0.5 * lp(rng.randn(n), 600) * np.exp(-t / 0.06)
    return fade_tail(y)


def tom(rng, f0=170, f1=95, tau=0.25):
    n = int(tau * 4 * SR)
    t = tvec(n)
    fr = f1 + (f0 - f1) * np.exp(-t / 0.04)
    return fade_tail(sine_f(fr) * np.exp(-t / tau) + 0.2 * bp(rng.randn(n), 200, 2000) * np.exp(-t / 0.03))


def clap(rng):
    n = int(0.35 * SR)
    t = tvec(n)
    y = np.zeros(n)
    for k, d in enumerate([0, 0.011, 0.022]):
        i = int(d * SR)
        seg = bp(rng.randn(n - i), 900, 5000) * np.exp(-tvec(n - i) / (0.012 if k < 2 else 0.12))
        y[i:] += seg
    return fade_tail(y)


def gated_snare(rng, hold=0.22):
    y = snare(rng, tau=0.12, tone=190, toneamt=0.6)
    n = int((hold + 0.03) * SR)
    tail = bp(rng.randn(n), 600, 7000) * 0.5
    tail[-int(0.03 * SR):] *= np.linspace(1, 0, int(0.03 * SR))
    out = np.zeros(max(len(y), n))
    out[:len(y)] += y
    out[:n] += tail * np.minimum(tvec(n) / 0.005, 1)
    return out


def open_hat(rng):
    return hat(rng, tau=0.18, lo=6500)


def bubble(rng):
    n = int(0.12 * SR)
    t = tvec(n)
    f0 = rng.uniform(300, 900)
    fr = f0 * (1 + 2.5 * t / t[-1])
    return fade_tail(sine_f(fr) * np.exp(-t / 0.03) * np.minimum(t / 0.003, 1))


def croak(rng, f=None):
    f = f or rng.uniform(90, 160)
    n = int(0.25 * SR)
    t = tvec(n)
    am = 0.5 + 0.5 * np.sign(np.sin(2 * np.pi * 38 * t))
    y = np.sin(2 * np.pi * f * t + 3 * np.sin(2 * np.pi * f * 2 * t)) * am * np.exp(-t / 0.08)
    return fade_tail(lp(y, 1500))


def cricket(rng):
    n = int(0.35 * SR)
    t = tvec(n)
    f = rng.uniform(4200, 5200)
    gate = (np.sin(2 * np.pi * 28 * t) > 0.3).astype(float)
    return fade_tail(np.sin(2 * np.pi * f * t) * gate * np.exp(-t / 0.2))


def modem(rng, dur=0.9):
    n = int(dur * SR)
    y = np.zeros(n)
    i = 0
    while i < n:
        k = int(rng.uniform(0.01, 0.06) * SR)
        f = rng.choice([1200, 2200, 980, 1650, 2400, 1070, 1270])
        seg = np.sin(2 * np.pi * f * tvec(min(k, n - i)))
        y[i:i + len(seg)] = seg
        i += k
    y = y * 0.7 + 0.3 * bp(rng.randn(n), 800, 3500)
    return fade_tail(y)


def buzz_layer(L, rng, f=190, level=1.0, n_bees=3):
    """Bees: a few saw buzzes drifting in pitch, AM flutter and pan (whole-cycle LFOs)."""
    n = np.arange(L)
    sec = L / SR
    out = np.zeros((L, 2))
    for b in range(n_bees):
        cyc = rng.randint(3, 9)
        drift = 1 + 0.03 * np.sin(2 * np.pi * cyc * n / L + rng.uniform(0, 6))
        fr = f * rng.uniform(0.85, 1.2) * drift
        cycles_total = np.cumsum(fr) / SR
        # force whole number of cycles over the loop for a seamless phase
        cycles_total *= round(cycles_total[-1]) / cycles_total[-1]
        ph = cycles_total % 1
        y = 2 * ph - 1
        y = bp(np.concatenate([y, y]), 300, 2500)[L:]
        amc = round(rng.uniform(1, 4) * sec)
        am = (0.5 + 0.5 * np.sin(2 * np.pi * amc * n / L + rng.uniform(0, 6))) ** 3
        pan = np.sin(2 * np.pi * rng.randint(1, 4) * n / L + rng.uniform(0, 6))
        th = (pan + 1) * np.pi / 4
        out[:, 0] += y * am * np.cos(th)
        out[:, 1] += y * am * np.sin(th)
    return out * level


def glitch(x, rng, times, slice_sec=0.06, repeats=6):
    """Stutter: repeat a short slice at given times (circular)."""
    L = len(x)
    y = x.copy()
    k = int(slice_sec * SR)
    for t in times:
        p = int(t * SR) % L
        sl = x[(p + np.arange(k)) % L]
        for r in range(repeats):
            idx = (p + r * k + np.arange(k)) % L
            y[idx] = sl * (0.9 ** r)
    return y


INST = dict(music_box=music_box, kalimba=kalimba, toy=toy_piano, glass=glass, ep=fm_epiano, vibes=vibes,
            pad=supersaw, pluck=pluck, tuba=tuba, accordion=accordion, bass=bass, choir=choir, flute=flute,
            harp=harp, marimba=marimba, glock=glock, brass=brass, lead=lead, spluck=synth_pluck, reed=reed,
            pizz=pizz, organ=organ, strings=strings)

DRUM = dict(k=lambda r: kick(r), K=lambda r: kick(r, punch=1.2), s=lambda r: snare(r), S=lambda r: gated_snare(r),
            h=lambda r: hat(r), o=lambda r: open_hat(r), t=lambda r: taiko(r), T=lambda r: taiko(r, f0=75, f1=40, tau=0.8),
            m=lambda r: tom(r), n=lambda r: tom(r, 240, 140, 0.2), c=lambda r: clap(r), b=lambda r: brush(r),
            w=lambda r: woodblock(r, 1100), W=lambda r: woodblock(r, 800), x=lambda r: shaker(r), q=lambda r: clock_tick(r),
            Q=lambda r: clock_tick(r, False), z=lambda r: coin(r), r=lambda r: woodblock(r, 1700))


# ---------------------------------------------------------------- song
class Song:
    def __init__(self, name, bpm, bpb, bars, seed=1, swing=0.0):
        self.name, self.bpm, self.bpb, self.bars = name, bpm, bpb, bars
        self.Q = 60.0 / bpm
        self.loop = Loop(bars * bpb * self.Q, seed=seed)
        self.L = self.loop.L
        self.rng = np.random.RandomState(seed)
        self.swing = swing
        self.secs = [(bars, None)]
        self.roles = {}
        self.fx = {}

    # time
    def sw(self, beat):
        if not self.swing:
            return beat
        b = np.floor(beat)
        fr = beat - b
        if abs(fr - 0.5) < 1e-6:
            return b + 0.5 + self.swing
        if abs(fr - 0.25) < 1e-6:
            return b + 0.25 + self.swing / 2
        if abs(fr - 0.75) < 1e-6:
            return b + 0.75 + self.swing / 2
        return beat

    def t(self, beat):
        return self.sw(beat) * self.Q

    # arrangement
    def sections(self, spec):
        """spec: list of (nbars, set_of_layers)."""
        assert sum(s[0] for s in spec) == self.bars, (sum(s[0] for s in spec), self.bars)
        self.secs = spec

    def on(self, layer, bar):
        b = 0
        for n, layers in self.secs:
            if b <= bar % self.bars < b + n:
                return layers is None or layer in layers
            b += n
        return False

    def role(self, layer, db):
        self.roles[layer] = db

    # notes
    def note(self, layer, inst, m, beat, dur, vel=1.0, pan=0.0, jitter=0.0, **kw):
        bar = int(beat // self.bpb)
        if not self.on(layer, bar):
            return
        f = INST[inst] if isinstance(inst, str) else inst
        sig = f(m, dur * self.Q, self.rng, **kw)
        self.loop.add(layer, sig, self.t(beat) + (self.rng.uniform(-jitter, jitter) if jitter else 0), pan=pan, gain=vel)

    def hit(self, layer, sig, beat, vel=1.0, pan=0.0):
        bar = int(beat // self.bpb)
        if not self.on(layer, bar):
            return
        self.loop.add(layer, sig, self.t(beat), pan=pan, gain=vel)

    # patterns
    def pads(self, layer, chords, inst='pad', center=60, n=4, vel=1.0, spread=0.6, every=1, legato=1.0, **kw):
        for bar in range(0, self.bars, every):
            c = chords[bar]
            for k, m in enumerate(c.voicing(center, n)):
                self.note(layer, inst, m, bar * self.bpb, self.bpb * every * legato, vel, pan=spread * (k / max(1, n - 1) * 2 - 1), **kw)

    def drums(self, layer, pattern, steps=None, vel=None, pans=None, jitter=0.004, every_bar=None):
        """pattern: {key: 'x..x' per bar (or list of strings cycled per bar)}; char = drum key (DRUM) or '.'.
        Upper/lower case are different drums; '-' = rest. vel per key."""
        vel = vel or {}
        pans = pans or {}
        for bar in range(self.bars):
            for key, pat in pattern.items():
                p = pat[bar % len(pat)] if isinstance(pat, list) else pat
                st = steps or len(p)
                for i, ch in enumerate(p):
                    if ch in '.-':
                        continue
                    beat = bar * self.bpb + i * self.bpb / st
                    if not self.on(layer, bar):
                        continue
                    sig = DRUM[ch](self.rng)
                    v = vel.get(ch, vel.get(key, 1.0)) * self.rng.uniform(0.85, 1.05)
                    self.loop.add(layer, sig, self.t(beat) + self.rng.uniform(-jitter, jitter), pan=pans.get(ch, 0), gain=v)

    def bassline(self, layer, chords, pattern, inst='bass', octave=2, vel=1.0, **kw):
        """pattern: list of (beat, interval, dur) where interval is semitones above the chord's bass or 'n' = next root."""
        for bar in range(self.bars):
            c = chords[bar]
            nxt = chords[(bar + 1) % self.bars]
            for beat, iv, dur in pattern:
                m = (nxt.bass(octave) - 1 if iv == 'n' else c.bass(octave) + iv)
                self.note(layer, inst, m, bar * self.bpb + beat, dur, vel, **kw)

    def arp(self, layer, chords, inst, pattern, step, lo=60, span=2, vel=1.0, pan=0.0, accent=None, octave_up_every=None, **kw):
        """pattern: indices into the chord's tones from `lo` upwards; one index per step (None = rest)."""
        n_steps = int(round(self.bpb / step))
        for bar in range(self.bars):
            tones = chords[bar].tones(lo, lo + 12 * span)
            for i in range(n_steps):
                idx = pattern[(bar * n_steps + i) % len(pattern)]
                if idx is None:
                    continue
                m = tones[idx % len(tones)]
                v = vel * (accent[i % len(accent)] if accent else 1.0)
                p = pan if not isinstance(pan, (list, tuple)) else pan[i % len(pan)]
                self.note(layer, inst, m, bar * self.bpb + i * step, step * 0.95, v, pan=p, **kw)

    def melody(self, layer, chords, scale, inst, lo, hi, phrase_bars=2, plan='AABA', start=0, phrases=None,
               durs=(0.5, 1, 1, 1.5, 2), weights=None, end_rest=1.0, steps=(-2, -1, -1, 1, 1, 2, 0, 3, -3),
               vel=1.0, pan=0.0, legato=0.95, seed=None, chord_snap=1.0, **kw):
        rng = np.random.RandomState(seed if seed is not None else self.rng.randint(1 << 30))
        pb = phrase_bars * self.bpb
        motifs, contours = {}, {}
        for letter in sorted(set(plan)):
            on, t = [0.0], 0.0
            w = np.array(weights or [1] * len(durs), float)
            while True:
                t += rng.choice(durs, p=w / w.sum())
                if t >= pb - end_rest:
                    break
                on.append(t)
            rh = [(o, (on[i + 1] if i + 1 < len(on) else pb - end_rest + rng.uniform(0, end_rest * 0.5)) - o) for i, o in enumerate(on)]
            motifs[letter] = rh
            contours[letter] = [0] + list(rng.choice(steps, len(rh) - 1))
        n_ph = phrases or (self.bars - start) // phrase_bars
        prev = scale.snap((lo + hi) // 2)
        for p in range(n_ph):
            letter = plan[p % len(plan)]
            base_beat = start * self.bpb + p * pb
            last_phrase = (p % len(plan) == len(plan) - 1)
            m = None
            for i, (o, d) in enumerate(motifs[letter]):
                beat = base_beat + o
                c = chords[int(beat // self.bpb) % self.bars]
                if i == 0:
                    m = c.nearest(prev, 7)
                else:
                    m = scale.step(m, int(contours[letter][i]))
                strong = (o % 1 == 0) or d >= 1
                if strong and rng.rand() < chord_snap:
                    m = c.nearest(m, 2)
                if last_phrase and i == len(motifs[letter]) - 1:
                    m = min(c.tones(m - 7, m + 7), key=lambda x: (x % 12 != c.root, abs(x - m)))
                while m > hi:
                    m -= 12
                while m < lo:
                    m += 12
                self.note(layer, inst, m, beat, d * legato, vel * rng.uniform(0.85, 1.0), pan=pan, **kw)
                prev = m

    # mix
    def bus_fx(self, layer, **fx):
        self.fx[layer] = fx

    def render(self, extras=None, target_rms=-20.0, wow=(7, 2), master_lp=None, hiss_level=0.003, crackle_level=0.0,
               out_dir='ost/out', drive=1.25, muffle=None, post=None):
        rng = self.rng
        L = self.L
        irs = {}

        def ir(kind):
            if kind not in irs:
                spec = {'room': (1.3, 4000), 'hall': (3.5, 3000), 'cave': (5.5, 2200), 'plate': (2.2, 6000), 'huge': (8.0, 2000)}[kind]
                irs[kind] = make_ir(np.random.RandomState(hash(kind) % 1000), t60=spec[0], damp=spec[1])
            return irs[kind]

        layers = {}
        for name, buf in self.loop.buses.items():
            x = buf
            fx = self.fx.get(name, {})
            if 'hp' in fx:
                x = chp(x, fx['hp'])
            if 'lp' in fx:
                x = clp(x, fx['lp'])
            if 'bp' in fx:
                x = cbp(x, *fx['bp'])
            if 'chorus' in fx:
                x = stereo_chorus(x, rng, depth_ms=fx['chorus'], rate=0.5, mix=0.5)
            if 'delay' in fx:
                d, fb, mix = fx['delay']
                x = cdelay(x, d * self.Q, fb=fb, mix=mix, cutoff=fx.get('delay_lp', 2800))
            if 'rev' in fx:
                kind, amt = fx['rev']
                x = x * fx.get('dry', 1.0) + amt * creverb(x, ir(kind))
            if 'duck' in fx:
                times, depth = fx['duck']
                x = x * ducker(L, times, depth=depth, release=0.2)
            layers[name] = x
        if extras:
            for name, x in extras(self, layers).items():
                layers[name] = x
        # auto-balance: each layer's 'active' RMS to its role level
        def active_rms(x):
            blk = int(0.05 * SR)
            nb = len(x) // blk
            e = (x[:nb * blk] ** 2).mean(1).reshape(nb, blk).mean(1)
            thr = e.max() * 1e-3
            act = e[e > thr]
            return np.sqrt(act.mean()) if len(act) else 1e-9
        mix = np.zeros((L, 2))
        report = []
        for name, x in layers.items():
            db = self.roles.get(name, -24)
            g = 10 ** (db / 20) / max(active_rms(x), 1e-9)
            mix += x * g
            report.append(f"{name}{db:+.0f}")
        if master_lp:
            mix = clp(mix, master_lp, 1)
        if muffle:
            # 'behind the wall': sections band-limited and roomy, with short ramps (circular)
            g = np.zeros(L)
            ramp = int(0.6 * SR)
            for b0, b1 in muffle:
                a, b = int(self.t(b0 * self.bpb) * SR), int(self.t(b1 * self.bpb) * SR)
                idx = np.arange(a, b) % L
                g[idx] = 1
            k = np.hanning(2 * ramp + 1)
            g = np.real(np.fft.irfft(np.fft.rfft(g) * np.fft.rfft(np.pad(k / k.sum(), (0, L - len(k)))), L))
            g = np.roll(g, -ramp).clip(0, 1)[:, None]
            wall = cbp(mix, 180, 900, 2) * 1.1
            wall = 0.5 * wall + 0.6 * creverb(wall, ir("room"))
            mix = mix * (1 - g) + wall * g
        if post:
            mix = post(self, mix)
        if hiss_level:
            mix += hiss(L, rng, hiss_level)
        if crackle_level:
            mix += crackle(L, rng, rate=4, level=crackle_level)
        if wow:
            mix = tape_wow(mix, rng, depth_cents=wow[0], flutter_cents=wow[1])
        out = master(mix, target_rms_db=target_rms, drive=drive)
        import os
        os.makedirs(out_dir, exist_ok=True)
        path = f"{out_dir}/{self.name}.wav"
        write(path, out)
        rms = 20 * np.log10(np.sqrt((out ** 2).mean()))
        pk = 20 * np.log10(np.abs(out).max())
        print(f"{self.name}: {L/SR:.1f}s rms {rms:.1f} peak {pk:.1f} | " + " ".join(report), flush=True)
        return out

    def kick_times(self, pattern_steps, steps):
        """Beat times (seconds) of a kick pattern, for ducking."""
        out = []
        for bar in range(self.bars):
            p = pattern_steps[bar % len(pattern_steps)] if isinstance(pattern_steps, list) else pattern_steps
            for i, ch in enumerate(p):
                if ch not in '.-':
                    out.append(self.t(bar * self.bpb + i * self.bpb / steps))
        return out
