# The shift's last seconds: a loud wooden clock tick (the tock is the same clip pitched down in ShopFeel).
# python3 tick.py -> shift_countdown_tick.wav (copied to hukaton/Assets/Audio/Sfx/shop/)
import numpy as np
from scipy.signal import butter, sosfilt
from scipy.io import wavfile
sr = 44100
n = int(.2 * sr)
t = np.arange(n) / sr
rng = np.random.default_rng(14)

def band(x, lo, hi, order=4):
    return sosfilt(butter(order, [lo, hi], btype='band', fs=sr, output='sos'), x)

def click(at, partials, decay, level):
    out = np.zeros(n)
    i0 = int(at * sr)
    tt = t[: n - i0]
    burst = rng.standard_normal(len(tt)) * np.exp(-tt / .0012)
    out[i0:] += band(burst, 1800, 7500) * level * 1.4
    for f, a in partials:
        out[i0:] += a * level * np.sin(2 * np.pi * f * tt + rng.uniform(0, 6.28)) * np.exp(-tt / decay)
    return out

sig = np.zeros(n)
# the escapement: two hard clicks 7 ms apart
sig += click(0.0, [(1930, .9), (3270, .55), (4810, .35), (6420, .2)], .018, 1.0)
sig += click(0.007, [(1710, .7), (2890, .4), (4380, .25)], .014, .7)
# wooden case: low-mid resonance and a short body thock for weight
body = rng.standard_normal(n) * np.exp(-t / .004)
sig += band(body, 330, 520, 2) * 2.2 * np.exp(-t / .07)
sig += band(body, 820, 1100, 2) * 1.2 * np.exp(-t / .05)
sig += .55 * np.sin(2 * np.pi * 170 * t) * np.exp(-t / .045) * (1 - np.exp(-t / .0008))
# a faint metallic ring of the spring, so it reads as a clock and not a knock
sig += .06 * np.sin(2 * np.pi * 2637 * t) * np.exp(-t / .09)
fade = np.ones(n); fade[-int(.02 * sr):] = np.linspace(1, 0, int(.02 * sr))
sig *= fade
sig /= np.max(np.abs(sig))
sig *= 10 ** (-1 / 20)
wavfile.write('shift_countdown_tick.wav', sr, (sig * 32767).astype(np.int16))
