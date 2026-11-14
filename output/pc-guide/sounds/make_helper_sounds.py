"""The PC helper «Гвоздик»'s voice (mono 16-bit 44.1 kHz): four short metallic bleep-mumbles and a pop-up sound.
Original synthesis. Run: python3 make_helper_sounds.py <out dir>"""
import sys, wave
import numpy as np
SR = 44100; OUT = sys.argv[1] if len(sys.argv) > 1 else '.'; rng = np.random.default_rng(23)
def t(sec): return np.arange(int(SR * sec)) / SR
def env(n, a, d): x = np.ones(n); ai = int(SR * a); x[:ai] = np.linspace(0, 1, ai) if ai else 1; x *= np.exp(-np.arange(n) / (SR * d)); return x
def save(name, x):
    x = x / max(1e-6, np.max(np.abs(x))) * .85
    w = wave.open(f"{OUT}/{name}.wav", 'wb'); w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
    w.writeframes((x * 32767).astype('<i2').tobytes()); w.close()
def place(buf, x, at):
    i = int(SR * at); n = min(len(x), len(buf) - i); buf[i:i + n] += x[:n]
def syllable(f0, f1, dur, ring):
    """A short pitched blip that slides f0 -> f1, a squarish body (8-bit) plus a metallic ring (the nail)."""
    tt = t(dur); f = np.linspace(f0, f1, len(tt)); ph = np.cumsum(2 * np.pi * f / SR)
    body = np.tanh(3 * np.sin(ph)) * .55 + np.sin(2 * ph) * .15
    metal = np.sin(ph * ring) * .12 * np.exp(-tt * 30)
    return (body + metal) * env(len(tt), .003, dur * .45)
# four mumbles: 3-5 syllables of pitch-sliding blips, like a tiny voice talking fast
patterns = [
    [(620, 760, .06), (540, 480, .05), (700, 820, .07)],
    [(500, 640, .05), (680, 600, .05), (560, 520, .05), (740, 880, .08)],
    [(760, 640, .06), (600, 700, .05), (820, 760, .07)],
    [(560, 620, .04), (640, 560, .05), (720, 800, .05), (520, 470, .05), (660, 780, .07)],
]
for i, pat in enumerate(patterns):
    total = sum(d for *_, d in pat) + .06 * len(pat) + .1
    b = np.zeros(int(SR * total)); at = 0
    for f0, f1, d in pat:
        place(b, syllable(f0, f1, d, 4.7 + rng.random()), at); at += d + rng.uniform(.015, .035)
    save(f'pc_helper_talk_{i + 1:02d}', b)
# pop-up: a springy upward boing with a rusty creak at the start
b = np.zeros(int(SR * .32))
cr = t(.06); place(b, np.sign(np.sin(2 * np.pi * 95 * cr)) * (rng.random(len(cr)) < .5) * env(len(cr), .002, .02) * .25, 0)
tt = t(.28); f = 300 + 700 * (1 - np.exp(-tt * 18)) + 40 * np.sin(2 * np.pi * 26 * tt) * np.exp(-tt * 6)
ph = np.cumsum(2 * np.pi * f / SR); place(b, (np.sin(ph) + .25 * np.sin(3 * ph)) * env(len(tt), .003, .09), .025)
save('pc_helper_pop', b)
print('ok')
