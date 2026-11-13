"""Synthesized one-shot and loop sounds for the player debuffs (none of these existed in the project)."""
import sys, wave, numpy as np
OUT = sys.argv[1]
SR = 44100
rng = np.random.default_rng(11)

def t(seconds): return np.arange(int(SR * seconds)) / SR
def env(n, attack=.005, release=.2):
    x = np.ones(n); a = int(SR * attack); r = int(SR * release)
    if a: x[:a] = np.linspace(0, 1, a)
    if r: x[-r:] *= np.linspace(1, 0, r) ** 2
    return x
def lowpass(x, cutoff):
    a = np.exp(-2 * np.pi * cutoff / SR); y = np.zeros_like(x); s = 0.0
    for i, v in enumerate(x): s = (1 - a) * v + a * s; y[i] = s
    return y
def highpass(x, cutoff): return x - lowpass(x, cutoff)
def save(name, x, peak=.85):
    x = x / (np.max(np.abs(x)) + 1e-9) * peak
    with wave.open(f"{OUT}/{name}.wav", 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((x * 32767).astype(np.int16).tobytes())
def sweep(f0, f1, seconds):
    f = np.geomspace(f0, f1, int(SR * seconds)); return np.sin(2 * np.pi * np.cumsum(f) / SR)

# Poison tick: a small "blup" bubble.
n = t(.2); x = sweep(260, 720, .2) * np.exp(-n * 22) + lowpass(rng.normal(0, .3, n.size), 1500) * np.exp(-n * 40)
save("debuff_poison_tick", x * env(n.size, .002, .05), .7)

# Steam hiss (Burn put out).
n = t(.75); x = highpass(rng.normal(0, 1, n.size), 2500) * np.exp(-n * 4.5)
save("debuff_hiss", x * env(n.size, .01, .3), .6)

# Ringing in the ears (Deafen): a loopable high sine with a slow wobble, 2 s (whole cycles).
n = t(2.0); x = np.sin(2 * np.pi * 3800 * n + .6 * np.sin(2 * np.pi * .5 * n)) + .25 * np.sin(2 * np.pi * 5700 * n)
save("debuff_tinnitus_loop", x, .5)

# Thump (Deafen and Blind start): a dull blow, like being hit over the head.
n = t(.6); x = np.sin(2 * np.pi * 52 * n) * np.exp(-n * 7) + lowpass(rng.normal(0, 1, n.size), 400) * np.exp(-n * 30) * 1.6
save("debuff_thump", x * env(n.size, .002, .2))

# Rattle (Crooked hands throw): a few dry clicks.
n = t(.3); x = np.zeros(n.size)
for k in range(7):
    at = int(SR * (.012 + k * .038 + rng.uniform(-.006, .006))); m = int(SR * .02)
    x[at:at + m] += highpass(rng.normal(0, 1, m), 1800) * np.exp(-np.arange(m) / SR * 220) * (1 - k * .1)
save("debuff_rattle", x, .6)

# Riffle (Shuffle): cards flicking through the fingers.
n = t(.45); x = np.zeros(n.size)
for k in range(16):
    at = int(SR * (.01 + k * .026 * (1 + k * .02))); m = int(SR * .015)
    if at + m >= n.size: break
    x[at:at + m] += lowpass(highpass(rng.normal(0, 1, m), 900), 5000) * np.exp(-np.arange(m) / SR * 300)
save("debuff_riffle", x, .6)

# Alarm drone (Mark): two detuned saws with a tremolo, swelling then fading.
n = t(1.4)
saw = lambda f: 2 * ((n * f) % 1) - 1
x = lowpass(saw(110) + saw(113.5) + .5 * saw(165), 1800) * (.7 + .3 * np.sin(2 * np.pi * 7 * n))
save("debuff_alarm", x * env(n.size, .25, .6), .65)

# Wobble (Warp): a sine with a strong wow, like a stretched tape.
n = t(1.6); f = 220 * (1 + .18 * np.sin(2 * np.pi * 1.3 * n) + .06 * np.sin(2 * np.pi * 5.1 * n))
x = np.sin(2 * np.pi * np.cumsum(f) / SR) + .35 * np.sin(2 * np.pi * np.cumsum(f * 1.5) / SR)
save("debuff_wobble", x * env(n.size, .08, .7), .6)

# Rot: a wet, low squelch.
n = t(.7); x = sweep(150, 45, .7) * np.exp(-n * 5) + lowpass(rng.normal(0, 1, n.size), 600) * (np.exp(-n * 8) * (1 + .6 * np.sin(2 * np.pi * 23 * n)))
save("debuff_rot", x * env(n.size, .004, .25))

# Glass crack (Fragile): a sharp high snap with a short ring.
n = t(.5); x = highpass(rng.normal(0, 1, n.size), 3000) * np.exp(-n * 30) + .5 * np.sin(2 * np.pi * 2900 * n) * np.exp(-n * 9) + .3 * np.sin(2 * np.pi * 4300 * n) * np.exp(-n * 12)
save("debuff_crack", x * env(n.size, .001, .2), .7)
print("ok")
