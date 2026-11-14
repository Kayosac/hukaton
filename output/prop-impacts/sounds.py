"""Knocks of loose things (PropImpacts): synthesized modal impacts, 2 variants per make.
Run from the repo root: python3 output/prop-impacts/sounds.py  -> hukaton/Assets/Audio/Sfx/props/*.wav"""
import numpy as np, wave, os
SR = 44100
OUT = os.path.join(os.path.dirname(__file__), '..', '..', 'hukaton', 'Assets', 'Audio', 'Sfx', 'props')

def env(n, decay): return np.exp(-np.arange(n) / SR * decay)
def lowpass(x, hz):
    a = np.exp(-2 * np.pi * hz / SR); y = np.zeros_like(x); s = 0.0
    for i, v in enumerate(x): s = a * s + (1 - a) * v; y[i] = s
    return y
def modal(seconds, partials, rng):
    n = int(SR * seconds); t = np.arange(n) / SR; y = np.zeros(n)
    for hz, amp, decay in partials:
        y += amp * np.sin(2 * np.pi * hz * t + rng.uniform(0, 6.28)) * env(n, decay)
    return y
def click(seconds, hz, decay, rng, gain=1.0):
    n = int(SR * seconds); return lowpass(rng.standard_normal(n), hz) * env(n, decay) * gain
def mix(*parts):
    n = max(len(p) for p in parts); y = np.zeros(n)
    for p in parts: y[:len(p)] += p
    return y
def save(name, y):
    y = y / max(1e-9, np.abs(y).max()) * .8
    fade = int(SR * .004); y[-fade:] *= np.linspace(1, 0, fade)
    with wave.open(os.path.join(OUT, name + '.wav'), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes((y * 32767).astype('<i2').tobytes())

def wood(seed, base):
    r = np.random.default_rng(seed)
    body = modal(.16, [(base, 1, 38), (base * 2.37, .55, 55), (base * 3.9, .3, 80), (base * 5.6, .15, 110)], r)
    return mix(body, click(.03, 2600, 260, r, 1.6), modal(.1, [(base * .5, .5, 45)], r))
def clay(seed, base):
    r = np.random.default_rng(seed)
    ring = modal(.12, [(base, 1, 70), (base * 1.58, .7, 90), (base * 2.71, .45, 120), (base * 4.2, .25, 160)], r)
    return mix(ring, click(.012, 7000, 600, r, 1.2), modal(.06, [(base * .31, .35, 90)], r))
def stone(seed, base):
    r = np.random.default_rng(seed)
    tock = modal(.07, [(base, 1, 120), (base * 1.83, .6, 150), (base * 2.9, .3, 200)], r)
    return mix(tock, click(.03, 3200, 220, r, 2.2), modal(.09, [(base * .22, .7, 60)], r))
def metal(seed, base):
    r = np.random.default_rng(seed)
    ring = modal(.3, [(base, 1, 18), (base * 2.76, .6, 24), (base * 5.4, .4, 32), (base * 8.9, .22, 45), (base * 1.31, .3, 60)], r)
    return mix(ring, click(.008, 9000, 800, r, 1.0), modal(.05, [(base * .4, .4, 100)], r))
def soft(seed, base):
    r = np.random.default_rng(seed)
    return mix(modal(.14, [(base, 1, 42), (base * 1.5, .3, 70)], r), click(.09, 600, 60, r, 2.5))

os.makedirs(OUT, exist_ok=True)
save('prop_wood_01', wood(11, 205)); save('prop_wood_02', wood(12, 168)); save('prop_wood_03', wood(13, 246))
save('prop_clay_01', clay(21, 1180)); save('prop_clay_02', clay(22, 960))
save('prop_stone_01', stone(31, 640)); save('prop_stone_02', stone(32, 520))
save('prop_metal_01', metal(41, 1450)); save('prop_metal_02', metal(42, 1120))
save('prop_soft_01', soft(51, 92)); save('prop_soft_02', soft(52, 78))

# Breaking (Breakable): a clay pot bursting, a crate splitting.
def burst_clay(seed):
    r = np.random.default_rng(seed); n = int(SR * .55); y = np.zeros(n)
    y[:int(SR*.09)] += stone(seed, 380)[:int(SR*.09)] * .9                      # the crack
    for k in range(int(r.integers(14, 20))):                                  # shards ringing as they land
        at = int(SR * (.01 + r.exponential(.07)));
        if at >= n - 4000: continue
        piece = clay(seed * 10 + k, r.uniform(1400, 3600)) * r.uniform(.15, .55)
        y[at:at+len(piece)] += piece[:n-at]
    y += click(.55, 1800, 9, r, .25)[:n]                                     # grit
    return y
def burst_wood(seed):
    r = np.random.default_rng(seed); n = int(SR * .6); y = np.zeros(n)
    snap = mix(click(.06, 5000, 70, r, 2.6), modal(.25, [(150, 1, 18), (310, .6, 26), (720, .35, 40)], r))
    y[:len(snap)] += snap[:n]
    for k in range(int(r.integers(5, 8))):                                    # planks clattering down
        at = int(SR * (.06 + r.uniform(0, .35)))
        piece = wood(seed * 10 + k, r.uniform(180, 330)) * r.uniform(.25, .6)
        y[at:at+len(piece)] += piece[:n-at]
    return y
save('prop_break_clay_01', burst_clay(61)); save('prop_break_clay_02', burst_clay(62))
save('prop_break_wood_01', burst_wood(71)); save('prop_break_wood_02', burst_wood(72))
