"""Sounds for the PC mini-game «Закрой окно» (mono 16-bit 44.1 kHz, peak-normalized like make_sounds.py).

Run without arguments: writes WAVs (and .meta files with fresh guids, only if missing) into
hukaton/Assets/Audio/Sfx/backroom/. Optional: python3 make_popup_sounds.py <out dir>  (WAVs only).
  pc_popup_spawn_0/1/2  a popup appears (short, soft, will play very often)
  pc_popup_close        a popup closed (dry pop)
  pc_popup_fake         the fake X was clicked (mocking 'boo-wong' + splash)
  pc_popup_win          beat the attack (chiptune jingle)
  pc_bsod               blue screen (tube crackle, low hum, alarm beeps)
"""
import os, sys, wave, uuid
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
DEFAULT_OUT = os.path.join(ROOT, 'hukaton', 'Assets', 'Audio', 'Sfx', 'backroom')
OUT = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_OUT
WRITE_META = len(sys.argv) <= 1
META_SRC = os.path.join(DEFAULT_OUT, 'pc_click.wav.meta')
SR = 44100; rng = np.random.default_rng(23)


def t(sec): return np.arange(int(SR * sec)) / SR


def env(n, a, d):
    x = np.ones(n); ai = int(SR * a)
    if ai: x[:ai] = np.linspace(0, 1, ai)
    x *= np.exp(-np.arange(n) / (SR * d)); return x


def fade_out(x, sec=.01):
    n = min(len(x), int(SR * sec)); x[-n:] *= np.linspace(1, 0, n); return x


def lowpass(x, k):
    y = np.zeros_like(x); s = 0.0
    for i, v in enumerate(x): s += k * (v - s); y[i] = s
    return y


def crush(x, hold=4, bits=6):
    """Sample-and-hold plus bit reduction: the old-sound-card grit."""
    y = np.repeat(x[::hold], hold)[:len(x)]; q = 2 ** (bits - 1); return np.round(y * q) / q


def tone(freqs, wave_='sine'):
    ph = np.cumsum(2 * np.pi * np.asarray(freqs) / SR)
    if wave_ == 'sine': return np.sin(ph)
    if wave_ == 'square': return np.sign(np.sin(ph))
    if wave_ == 'pulse': return np.where((ph / (2 * np.pi)) % 1 < .25, 1.0, -1.0)
    if wave_ == 'tri': return 2 / np.pi * np.arcsin(np.sin(ph))
    if wave_ == 'saw': return 2 * ((ph / (2 * np.pi)) % 1) - 1


def place(buf, x, at):
    i = int(SR * at); n = min(len(x), len(buf) - i); buf[i:i + n] += x[:n]


def save(name, x, peak=.89):
    x = fade_out(x.copy(), .004)
    x = x / max(1e-6, np.max(np.abs(x))) * peak
    p = os.path.join(OUT, name + '.wav')
    w = wave.open(p, 'wb'); w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
    w.writeframes((x * 32767).astype('<i2').tobytes()); w.close()
    if WRITE_META and not os.path.exists(p + '.meta'):
        src = open(META_SRC).read().split('\n'); src[1] = 'guid: ' + uuid.uuid4().hex
        open(p + '.meta', 'w').write('\n'.join(src))


# ---- spawn: a tiny glassy 'dzin' that bends at the end, three flavours (0.15–0.22 s)
def spawn(f0, f1, ratio, dur, bend, seed):
    r = np.random.default_rng(seed); tt = t(dur); n = len(tt)
    gl = f0 + (f1 - f0) * (1 - np.exp(-tt / .012))            # quick upward chirp into the note
    gl *= 1 - bend * np.clip((tt - dur * .55) / (dur * .45), 0, 1) ** 2   # crooked droop at the tail
    mod = np.sin(np.cumsum(2 * np.pi * gl * ratio / SR)) * 1.2 * np.exp(-tt / .03)
    car = np.sin(np.cumsum(2 * np.pi * gl / SR) + mod)
    x = car * env(n, .004, dur * .2)
    x += .35 * np.sin(np.cumsum(2 * np.pi * gl * 2.01 / SR)) * env(n, .002, dur * .12)
    x += .12 * lowpass(r.uniform(-1, 1, n), .5) * env(n, .0005, .004)
    return lowpass(crush(x, 3, 8), .55)


save('pc_popup_spawn_0', spawn(1250, 1660, 1.5, .17, .08, 1))
save('pc_popup_spawn_1', spawn(990, 1480, 2.0, .19, .12, 2))
save('pc_popup_spawn_2', spawn(1480, 1180, 3.01, .16, .06, 3))

# ---- close: dry pop (0.08 s)
b = np.zeros(int(SR * .08)); tt = t(.06)
fr = 180 + 900 * np.exp(-tt / .006)
place(b, np.sin(np.cumsum(2 * np.pi * fr / SR)) * env(len(tt), .0005, .012), 0)
ck = t(.012); place(b, lowpass(rng.uniform(-1, 1, len(ck)), .7) * env(len(ck), .0002, .002) * .6, 0)
save('pc_popup_close', b)

# ---- fake: splash + a mocking falling 'boo-wong' with wobble (0.4 s)
b = np.zeros(int(SR * .4))
sp = t(.09); place(b, crush(rng.uniform(-1, 1, len(sp)), 6, 4) * env(len(sp), .0005, .025) * .55, 0)
zap = t(.05); place(b, tone(2400 - 30000 * zap, 'square') * env(len(zap), .0005, .012) * .35, 0)
tt = t(.36)
fr = (330 * np.exp(-tt * 2.2) + 95) * (1 + .06 * np.sin(2 * np.pi * 9 * tt))
w = tone(fr, 'saw') * .6 + tone(fr * .5, 'square') * .4
w = lowpass(w, .18) * env(len(tt), .01, .16)
place(b, crush(w, 2, 7) * 1.1, .03)
save('pc_popup_fake', b)

# ---- win: a bright chiptune jingle (~1 s)
b = np.zeros(int(SR * 1.05))
def note(at, hz, dur, vol=1.0, wav='pulse', vib=0.0):
    tt = t(dur); fr = hz * (1 + vib * np.sin(2 * np.pi * 6 * tt) * np.clip((tt - .08) / .1, 0, 1))
    x = tone(fr, wav) * env(len(tt), .003, dur * .6) * vol; place(b, fade_out(x, .01), at)
C5, E5, G5, C6, D6, E6, G4 = 523.25, 659.25, 783.99, 1046.5, 1174.7, 1318.5, 392.0
step = .075
for i, hz in enumerate((G4, C5, E5, G5, E5, G5)):
    note(i * step, hz, step * 1.4, .5)
note(6 * step, C6, .5, .55, vib=.012)
note(6 * step, E5, .5, .25, 'square')
note(6 * step + .18, E6, .32, .3, vib=.015)
for i, hz in enumerate((130.8, 196.0, 261.6)):
    note(i * step * 2, hz, step * 2.2, .55, 'tri')
note(6 * step, 130.8, .55, .6, 'tri')
for i in range(8):
    hh = t(.03); place(b, rng.uniform(-1, 1, len(hh)) * env(len(hh), .0005, .006) * (.25 if i % 2 else .15), i * step)
save('pc_popup_win', b)

# ---- bsod: zap, tube crackle, mains hum, three alarm beeps (1.5 s)
b = np.zeros(int(SR * 1.5))
zp = t(.12); place(b, np.sin(2 * np.pi * (60 + 500 * np.exp(-zp * 40)) * zp) * env(len(zp), .001, .05) * 1.0, 0)
tt = t(1.5)
hum = (np.sin(2 * np.pi * 50 * tt) + .5 * np.sin(2 * np.pi * 100 * tt) + .3 * np.sign(np.sin(2 * np.pi * 150 * tt)) * .4)
hum *= np.clip(tt / .05, 0, 1) * np.clip((1.5 - tt) / .3, 0, 1) * .35
place(b, hum, 0)
cr = rng.uniform(-1, 1, len(tt)) * (rng.random(len(tt)) < .015)
cr = lowpass(cr, .6) * (1.2 * np.exp(-tt / .4) + .25)
place(b, cr * 1.4, 0)
st = lowpass(rng.uniform(-1, 1, len(tt)), .25) * .08 * (.35 + .65 * np.exp(-tt / .3)); place(b, st, 0)
for i in range(3):
    bt = t(.13); x = tone(np.full(len(bt), 1975.0), 'square') * .5 + np.sin(2 * np.pi * 2963 * bt) * .2
    place(b, lowpass(x, .5) * env(len(bt), .003, .2) * np.clip((.13 - bt) / .01, 0, 1) * .5, .25 + i * .3)
save('pc_bsod', b)
print('ok')
