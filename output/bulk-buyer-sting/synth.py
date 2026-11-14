"""Bulk buyer («Скупщик») arrival sting: a swell in, two dark stabs (da-DUM, a tritone apart), a low gong and a
cascade of coins. Mono, 44.1 kHz, 16-bit. Run: python3 synth.py <out.wav>"""
import sys
import numpy as np
from scipy import signal
from scipy.io import wavfile

SR = 44100
rng = np.random.default_rng(23)
DUR = 2.6
N = int(DUR * SR)
t = np.arange(N) / SR
out = np.zeros(N)


def lp(x, f, order=2):
    b, a = signal.butter(order, min(f, SR * .45) / (SR / 2), 'low')
    return signal.lfilter(b, a, x)


def hp(x, f, order=2):
    b, a = signal.butter(order, f / (SR / 2), 'high')
    return signal.lfilter(b, a, x)


def place(x, at, gain=1.0):
    i = int(at * SR)
    n = min(len(x), N - i)
    out[i:i + n] += x[:n] * gain


def saw(f, n, phase=0.0):
    """Band-limited saw (additive, harmonics under 9 kHz): no aliasing hiss."""
    s = np.arange(n) / SR
    x = np.zeros(n)
    for k in range(1, int(9000 / f) + 1):
        x += np.sin(2 * np.pi * k * (f * s + phase)) / k
    return x * 2 / np.pi


# 1. Swell in: rising filtered noise + a gliding sine, cut off right at the first stab.
sw_n = int(.42 * SR)
st = np.arange(sw_n) / SR
noise = rng.standard_normal(sw_n)
swell = np.zeros(sw_n)
for k, (f0, f1) in enumerate([(400, 3200), (900, 6000)]):
    b = hp(lp(noise, f1), f0)
    swell += b * (.6 if k == 0 else .35)
env = (st / st[-1]) ** 2.6
gl = np.sin(2 * np.pi * np.cumsum(220 * 2 ** (st / st[-1] * 1.5)) / SR)
place((swell * .5 + gl * .25) * env, 0.0, .7)

# 2. Two stabs: detuned saws, minor colour, lowpass closing down. D3 short, then Ab2 long (a tritone under).
def stab(root, dur, cutoff0, cutoff1, gain):
    n = int(dur * SR)
    s = np.arange(n) / SR
    chord = [1, 1.189, 1.498, 2.0]  # root, minor third, fifth, octave
    x = np.zeros(n)
    for r in chord:
        for det in (-.006, .0, .007):
            x += saw(root * r * (1 + det), n, rng.random())
    x /= 12
    # closing filter: a smooth crossfade from a bright to a dark lowpassed copy (no block switching clicks)
    mix = np.exp(-s / (dur * .25))
    y = lp(x, cutoff0) * mix + lp(x, cutoff1) * (1 - mix)
    e = np.minimum(1, s / .008) * np.exp(-s / (dur * .35))
    e[-int(.03 * SR):] *= np.linspace(1, 0, int(.03 * SR))
    sub = np.sin(2 * np.pi * root / 2 * s) * np.exp(-s / (dur * .4))
    return (y + .5 * sub) * e * gain


place(stab(146.83, .26, 5000, 900, 1.6), .42)
place(stab(103.83, 1.7, 4200, 300, 1.25), .68)

# 3. Low gong under the second stab: inharmonic partials, slow beating.
g_n = int(1.9 * SR)
gs = np.arange(g_n) / SR
gong = np.zeros(g_n)
for ratio, amp, dec in [(1, 1, .9), (2.32, .5, .6), (4.25, .3, .4), (6.63, .18, .25), (9.38, .1, .15)]:
    f = 51.9 * ratio
    gong += amp * np.sin(2 * np.pi * f * gs + .3 * np.sin(2 * np.pi * 1.7 * gs)) * np.exp(-gs / dec)
gong *= np.minimum(1, gs / .004)
place(gong, .68, .45)

# 4. Coin crash at the big stab + a cascade of coin pings thinning out.
cr_n = int(.35 * SR)
crash = hp(rng.standard_normal(cr_n), 3500) * np.exp(-np.arange(cr_n) / SR / .07)
place(crash, .68, .22)

def coin(f, dur=.12):
    n = int(dur * SR)
    s = np.arange(n) / SR
    x = (np.sin(2 * np.pi * f * s) + .5 * np.sin(2 * np.pi * f * 2.76 * s) + .25 * np.sin(2 * np.pi * f * 5.4 * s))
    return x * np.exp(-s / (dur * .25)) * np.minimum(1, s / .001)

at = .7
while at < 1.9:
    place(coin(rng.uniform(2600, 5200)), at, rng.uniform(.06, .14) * (1.9 - at) / 1.2 + .03)
    at += rng.exponential(.035 + (at - .7) * .06)

# 5. Weird tape wobble over the tail (weirdcore): a slight pitch warble by a modulated delay read.
wob = .0016 * (1 - np.cos(2 * np.pi * .9 * t)) * np.clip((t - .7) / .5, 0, 1)
idx = np.arange(N) - wob * SR
out = np.interp(idx, np.arange(N), out)

# Tail fade and normalisation to -1 dBFS.
fade = int(.25 * SR)
out[-fade:] *= np.linspace(1, 0, fade) ** 2
out = hp(out, 35)
out *= 10 ** (-1 / 20) / np.max(np.abs(out))
wavfile.write(sys.argv[1] if len(sys.argv) > 1 else 'bulk_buyer_arrive.wav', SR, (out * 32767).astype(np.int16))
