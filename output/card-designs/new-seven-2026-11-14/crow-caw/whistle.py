"""Seamless looping whistle of a spinning boomerang card («Бумеранг»): an airy tone whose loudness and pitch
pulse with the spin ("whup-whup-whup"), over band-limited rushing air. The loop is exactly 1 s: every periodic
part has a whole number of cycles in it and the noise is cross-faded across the seam."""
import numpy as np
from scipy.signal import butter, sosfilt
from scipy.io import wavfile

SR = 44100
LOOP = 1.0
SPIN = 9.0           # whups per second (whole number of cycles per loop)
FADE = .15
r = np.random.default_rng(11)

n = int(LOOP * SR)
total = n + int(FADE * SR)
t = np.arange(total) / SR

spin = .5 - .5 * np.cos(2 * np.pi * SPIN * t)          # 0..1 once per half-turn
whup = spin ** 1.8
f = 1650 * (1 + .07 * (spin - .5))                       # pitch leans up as the edge cuts the air
phase = 2 * np.pi * np.cumsum(f) / SR
tone = np.sin(phase) + .18 * np.sin(2 * phase + .4) + .06 * np.sin(3 * phase)


def band(x, lo, hi):
    return sosfilt(butter(2, [lo, hi], btype="band", fs=SR, output="sos"), x)


air = band(r.standard_normal(total), 900, 4800)
air /= np.abs(air).max()
hiss = band(r.standard_normal(total), 4000, 9000)
hiss /= np.abs(hiss).max()

x = .55 * tone * (.15 + .85 * whup) + .5 * air * (.25 + .75 * whup) + .12 * hiss * whup
# Cross-fade the tail over the head so the loop has no seam (tonal parts are already periodic).
k = total - n
ramp = np.linspace(0, 1, k)
loop = x[:n].copy()
loop[:k] = x[:k] * ramp + x[n:] * (1 - ramp)
loop = loop / np.abs(loop).max() * .85
wavfile.write("boomerang_whistle.wav", SR, (loop * 32767).astype(np.int16))
print(len(loop) / SR, "s")
