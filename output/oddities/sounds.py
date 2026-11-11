"""Weirdcore sounds for the oddities, synthesized (numpy): a warped payphone ring and TV hiss (seamless loops),
the voice in the receiver, the knock and picture on the TV, and a far-off call that draws the forest's creatures.
Everything is a bit wrong on purpose: wobbling pitch, bit-crush, drop-outs, little glitches."""
import os, wave, numpy as np

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'out', 'sfx')
os.makedirs(OUT, exist_ok=True)
SR = 22050
rng = np.random.default_rng(91)

def save(name, x, peak=.85):
    x = np.asarray(x, float)
    x = x / max(1e-6, np.max(np.abs(x))) * peak
    with wave.open(os.path.join(OUT, name), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((x * 32767).astype(np.int16).tobytes())

def onepole(x, a):
    y = np.zeros_like(x); s = 0.
    for i in range(len(x)): s += a * (x[i] - s); y[i] = s
    return y

def crush(x, bits=6, hold=3):
    q = 2 ** bits
    y = np.round(x * q) / q
    return np.repeat(y[::hold], hold)[:len(x)]

def glitch(x, count, length=(.02, .06)):
    """Stutters: copy a short slice over the next few slices (a skipping tape)."""
    x = x.copy()
    for _ in range(count):
        n = int(rng.uniform(*length) * SR); s = rng.integers(0, len(x) - 4 * n)
        for k in range(1, rng.integers(2, 4)): x[s + k * n:s + (k + 1) * n] = x[s:s + n]
    return x

def loop_fade(x, seconds=.03):
    f = int(seconds * SR); r = np.linspace(0, 1, f)
    x = x.copy(); x[:f] = x[:f] * r + x[-f:] * (1 - r); return x[:-f]

t_of = lambda s: np.arange(int(s * SR)) / SR

# --- payphone ring: two-bell trill, 1.1 s on, 0.9 s off, twice; pitch sags and wobbles, bit-crushed ---
T = 4.0; t = t_of(T)
gate = (((t % 2.0) < 1.1)).astype(float)
gate = onepole(gate, .02)
wob = 1 + .03 * np.sin(2 * np.pi * .7 * t) + .015 * np.sin(2 * np.pi * 5.3 * t) - .04 * (t / T)
trill = (np.sin(2 * np.pi * 20 * t) > 0).astype(float)
bell = (np.sin(2 * np.pi * 1180 * wob * t) * trill + .8 * np.sin(2 * np.pi * 1490 * wob * t) * (1 - trill))
bell += .3 * np.sin(2 * np.pi * 2620 * wob * t) * trill
ring = bell * gate * (1 + .3 * rng.standard_normal(len(t)) * .1)
ring = crush(ring, 5, 3)
ring = glitch(ring, 5)
ring += .05 * onepole(rng.standard_normal(len(t)), .3)          # line hiss
save('oddity_phone_ring_loop.wav', loop_fade(ring), .7)

# --- TV hiss: snow noise, 50 Hz hum, slow flutter, glitch chirps ---
T = 3.0; t = t_of(T)
snow = rng.standard_normal(len(t))
hiss = snow - onepole(snow, .25)
hum = .25 * np.sign(np.sin(2 * np.pi * 50 * t)) * .5 + .15 * np.sin(2 * np.pi * 100 * t)
flutter = 1 + .35 * np.sin(2 * np.pi * .9 * t) * np.sin(2 * np.pi * 7 * t)
chirp = np.zeros(len(t))
for s in rng.uniform(0, T - .2, 4):
    a, n = int(s * SR), int(.08 * SR)
    f = np.linspace(rng.uniform(600, 2400), rng.uniform(200, 3200), n)
    chirp[a:a + n] += .5 * np.sin(2 * np.pi * np.cumsum(f) / SR) * np.hanning(n)
tvh = crush(hiss * .6 * flutter + hum * .5 + chirp, 7, 2)
tvh = glitch(tvh, 6, (.01, .04))
save('oddity_tv_hiss_loop.wav', loop_fade(tvh), .6)

# --- the voice in the receiver: formant gibberish, slowed and reversed in places, breath, line noise ---
T = 1.7; t = t_of(T)
f0 = 110 + 25 * np.sin(2 * np.pi * 1.3 * t) + 18 * np.sin(2 * np.pi * 4.1 * t)
phase = np.cumsum(f0) / SR
src = (phase % 1) * 2 - 1
voice = np.zeros(len(t))
vowels = [(700, 1200), (300, 2300), (500, 900), (400, 1900)]
seg = len(t) // 7
for k in range(7):
    f1, f2 = vowels[rng.integers(0, 4)]
    s = slice(k * seg, (k + 1) * seg)
    for f in (f1, f2):
        tt = t[s]
        voice[s] += np.sin(2 * np.pi * f * tt) * np.abs(src[s]) * np.hanning(seg)
voice[int(.6 * SR):int(1.0 * SR)] = voice[int(.6 * SR):int(1.0 * SR)][::-1]
breath = onepole(rng.standard_normal(len(t)), .2) * np.clip(np.sin(np.pi * t / T), 0, 1) ** 2
answer = crush(voice * .8 + breath * .5, 5, 4)
answer = onepole(answer, .45) - onepole(answer, .03)          # telephone band
answer = glitch(answer, 4)
click = np.zeros(len(t)); click[:300] = np.hanning(600)[:300] * rng.standard_normal(300)
save('oddity_phone_voice.wav', answer + click * .8)

# --- TV knock: a hollow thump, a burst of static, a wrong little jingle ---
T = 1.4; t = t_of(T)
thump = np.sin(2 * np.pi * (140 * np.exp(-t * 18) + 55) * t) * np.exp(-t * 14)
burst = rng.standard_normal(len(t)) * np.exp(-((t - .18) / .12) ** 2)
notes = [784, 740, 659, 523]
jingle = np.zeros(len(t))
for i, f in enumerate(notes):
    a = int((.45 + i * .17) * SR); n = int(.16 * SR)
    tt = np.arange(n) / SR
    jingle[a:a + n] += np.sign(np.sin(2 * np.pi * f * (1 - .03 * i) * tt)) * np.exp(-tt * 9) * .5
knock = crush(thump * 1.4 + burst * .6 + jingle, 6, 2)
save('oddity_tv_knock.wav', glitch(knock, 2))

# --- the far call that draws the creatures: a low detuned horn with a broken echo ---
T = 2.6; t = t_of(T)
env = np.clip(t / .3, 0, 1) * np.exp(-np.maximum(0, t - 1.2) * 2.5)
horn = sum(np.sign(np.sin(2 * np.pi * f * (1 + .02 * np.sin(2 * np.pi * 3 * t)) * t)) for f in (98, 103.5, 146))
horn = onepole(horn * env, .12)
echo = np.zeros(len(t)); d = int(.37 * SR); echo[d:] = horn[:-d] * .5
call = crush(horn + echo, 6, 3)
save('oddity_call.wav', glitch(call, 3))
print('sfx ok', sorted(os.listdir(OUT)))
