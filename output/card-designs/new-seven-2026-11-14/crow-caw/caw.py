"""Synthesized crow caws for the «Вороньё» card (no recordings in the project).

A caw = short noisy "k" onset + a harsh voiced "aaa" whose pitch rises a touch and then falls,
with period-doubling roughness (the raspy crow quality), nasal formants and a noisy breath layer.
"""
import numpy as np
from scipy.signal import butter, sosfilt
from scipy.io import wavfile

SR = 44100
rng = np.random.default_rng(7)


def bandpass(x, lo, hi, order=2):
    sos = butter(order, [lo, hi], btype="band", fs=SR, output="sos")
    return sosfilt(sos, x)


def formant(x, centre, width):
    return bandpass(x, max(60, centre - width / 2), centre + width / 2, 2)


def caw(duration=0.38, f_start=620, f_peak=700, f_end=470, rough=0.55, breath=0.35, seed=0):
    r = np.random.default_rng(seed)
    n = int(duration * SR)
    t = np.arange(n) / SR
    u = t / duration
    # Pitch contour: quick rise to the peak in the first 25 %, then a sagging fall.
    f0 = np.where(u < .25, f_start + (f_peak - f_start) * (u / .25),
                  f_peak + (f_end - f_peak) * np.clip((u - .25) / .75, 0, 1) ** 1.4)
    # Slow vibrato + per-period jitter.
    f0 = f0 * (1 + .015 * np.sin(2 * np.pi * 9 * t)) * (1 + .02 * r.standard_normal(n).cumsum() / np.sqrt(np.arange(1, n + 1)))
    phase = np.cumsum(f0) / SR
    # Glottal-ish pulse: sharp sawtooth with a soft corner.
    saw = 2 * (phase % 1) - 1
    pulse = np.tanh(3 * saw) - .4 * saw
    # Period doubling: every other cycle louder -> raspy subharmonic.
    cycle = np.floor(phase).astype(int)
    pulse *= np.where(cycle % 2 == 0, 1.0, 1 - rough)
    # Amplitude roughness at ~70 Hz.
    pulse *= 1 + .35 * np.sin(2 * np.pi * 68 * t + r.uniform(0, 6))
    # Breath noise gated by the pulses.
    noise = r.standard_normal(n) * (0.5 + 0.5 * (phase % 1 < .3))
    src = pulse + breath * noise
    # Nasal "aw" formants.
    voiced = (1.0 * formant(src, 1150, 500) + .8 * formant(src, 1750, 600)
              + .45 * formant(src, 2800, 900) + .25 * formant(src, 600, 300))
    voiced = np.tanh(1.3 * voiced / (np.abs(voiced).max() + 1e-9))  # a bit of throat grit
    env = np.minimum(1, t / .012) * np.clip((duration - t) / .09, 0, 1) ** 1.3
    env *= 1 - .25 * u  # tires a little toward the end
    out = voiced * env
    # "k" onset: 14 ms of bright noise just before the voice settles.
    k = int(.014 * SR)
    burst = bandpass(r.standard_normal(k), 1800, 5200, 3) * np.hanning(k) * .9
    out[:k] += burst
    return out


def silence(seconds):
    return np.zeros(int(seconds * SR))


def save(name, x):
    x = x / (np.abs(x).max() + 1e-9) * .89
    tail = int(.03 * SR)
    x = np.concatenate([x, np.zeros(tail)])
    wavfile.write(name, SR, (x * 32767).astype(np.int16))


variants = {
    "crow_caw_1.wav": caw(.40, 610, 700, 470, .55, .14, seed=1),
    "crow_caw_2.wav": caw(.30, 680, 760, 540, .6, .16, seed=2),
    "crow_caw_3.wav": np.concatenate([caw(.22, 650, 720, 560, .5, .14, seed=3), silence(.07),
                                      caw(.26, 630, 690, 500, .6, .16, seed=4)]),
    "crow_caw_4.wav": caw(.48, 560, 640, 410, .65, .2, seed=5),
}
for name, x in variants.items():
    save(name, x)
    print(name, round(len(x) / SR, 2), "s")
