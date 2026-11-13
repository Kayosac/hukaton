"""Goblin ceiling rig foley. Builds the rig-specific sounds from the existing backroom library
(hatch_open.wav creak and clunk, winch_rattle.wav pawl clicks, rope_pull.wav fibre squeak) plus
synthesized layers. Output: 44.1 kHz mono 16-bit wav in ./out (copied into Assets/Audio/Sfx/backroom).
Run: python3 synth.py"""
import os, wave
import numpy as np
from scipy import signal

SR = 44100
LIB = os.path.expanduser('~/mnt/hukaton/worktrees/world/hukaton/Assets/Audio/Sfx/backroom')
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'out')
os.makedirs(OUT, exist_ok=True)


def load(name):
    w = wave.open(os.path.join(LIB, name))
    assert w.getframerate() == SR and w.getsampwidth() == 2
    x = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(float) / 32768
    return x.reshape(-1, w.getnchannels()).mean(1)


def save(name, x, peak_db=-1.0, drive=0.0):
    x = np.asarray(x, float)
    if drive > 0:  # soft saturation: denser body, transient tamed
        x = np.tanh(x / np.abs(x).max() * (1 + drive))
    x = hp(x, 28, 2)
    x *= 10 ** (peak_db / 20) / max(1e-9, np.abs(x).max())
    loud = np.nonzero(np.abs(x) > 10 ** (-66 / 20))[0]
    x = x[:loud[-1] + n(.01)] if len(loud) else x
    fade = int(.004 * SR)
    x[-fade:] *= np.linspace(1, 0, fade)
    data = (np.clip(x, -1, 1) * 32767).astype(np.int16)
    with wave.open(os.path.join(OUT, name), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(data.tobytes())
    rms = 20 * np.log10(np.sqrt((x[np.abs(x) > 1e-4] ** 2).mean()) + 1e-9)
    print(f'{name}: {len(x) / SR:.2f}s peak {peak_db} dB, rms {rms:.1f} dB')


def add(*xs):
    out = np.zeros(max(len(v) for v in xs))
    for v in xs: out[:len(v)] += v
    return out


def t(sec): return np.arange(n(sec)) / SR
def n(sec): return int(round(sec * SR))
def hp(x, f, o=2): return signal.sosfilt(signal.butter(o, f, 'hp', fs=SR, output='sos'), x)
def lp(x, f, o=2): return signal.sosfilt(signal.butter(o, f, 'lp', fs=SR, output='sos'), x)
def bp(x, lo, hi, o=2): return signal.sosfilt(signal.butter(o, [lo, hi], 'bp', fs=SR, output='sos'), x)


def reson(x, f, q):
    b, a = signal.iirpeak(f, q, fs=SR)
    return signal.lfilter(b, a, x)


def pitch(x, ratio):
    """Varispeed (tape-style): ratio > 1 is higher and shorter."""
    idx = np.arange(0, len(x) - 1, ratio)
    return np.interp(idx, np.arange(len(x)), x)


def cut(x, a, b, fin=.002, fout=.03):
    s = x[n(a):n(b)].copy()
    s[:n(fin)] *= np.linspace(0, 1, n(fin))
    s[-n(fout):] *= np.linspace(1, 0, n(fout)) ** 2
    return s


def place(buf, s, at, gain=1.0):
    i = n(at)
    if i >= len(buf): return
    m = min(len(s), len(buf) - i)
    buf[i:i + m] += s[:m] * gain


def room(x, size=.22, mix=.14, seed=0):
    """Small wooden backroom: short dark noise tail."""
    r = np.random.default_rng(seed)
    ir = r.standard_normal(n(size)) * np.exp(-t(size) / (size / 5.5))
    ir = lp(ir, 3200)
    ir[:n(.006)] = 0
    wet = signal.fftconvolve(x, ir)[:len(x) + n(size)]
    dry = np.concatenate([x, np.zeros(len(wet) - len(x))])
    return dry + wet / np.abs(wet).max() * np.abs(x).max() * mix


def modal(freqs, decays, amps, dur, jitter=0.0, seed=0):
    r = np.random.default_rng(seed)
    tt = t(dur); y = np.zeros_like(tt)
    for f, d, a in zip(freqs, decays, amps):
        f *= 1 + jitter * r.uniform(-1, 1)
        y += a * np.sin(2 * np.pi * f * tt + r.uniform(0, 6.28)) * np.exp(-tt / d)
    return y


def click(dur, lo, hi, decay, seed):
    r = np.random.default_rng(seed)
    return bp(r.standard_normal(n(dur)), lo, hi) * np.exp(-t(dur) / decay)


def wood_clack(f0, seed, dur=.18):
    """Board-on-board clack: a few inharmonic plank modes and a dry splintery transient."""
    r = np.random.default_rng(seed)
    m = modal([f0, f0 * 2.31, f0 * 3.93, f0 * 6.1], [.035, .02, .011, .005], [1, .7, .45, .25], dur, .04, seed)
    return add(m, click(.03, 700, 5000, .004, seed + 1) * 1.2) * (1 + .1 * r.standard_normal())


def thump(f0, f1, decay, dur):
    tt = t(dur)
    f = f1 + (f0 - f1) * np.exp(-tt / (decay * .6))
    ph = 2 * np.pi * np.cumsum(f) / SR
    return np.sin(ph) * np.exp(-tt / decay) * (1 - np.exp(-tt / .0015))


def creak(dur, f_start, f_end, body=(620, 1150, 2350), seed=0, roughness=.18, wobble=4.0):
    """Stick-slip friction: an impulse train with a jittery, gliding period through wooden body resonances."""
    r = np.random.default_rng(seed)
    y = np.zeros(n(dur)); pos = 0.0
    tt = 0.0
    while pos < len(y) - 1:
        k = pos / len(y)
        f = f_start * (f_end / f_start) ** k
        f *= 1 + .12 * np.sin(2 * np.pi * wobble * tt + seed)
        period = SR / f * (1 + roughness * r.standard_normal())
        y[int(pos)] += r.uniform(.5, 1.0)
        pos += max(8, period); tt = pos / SR
    out = sum(reson(y, b * r.uniform(.94, 1.06), 9) * g for b, g in zip(body, (1, .7, .45)))
    env = np.sin(np.pi * np.linspace(0, 1, len(y))) ** .6
    return out * env


# ---- source material from the project library -----------------------------------------------
hatch = load('hatch_open.wav')
winch = load('winch_rattle.wav')
rope = load('rope_pull.wav')
hinge_creak = cut(hatch, .10, .50, .004, .08)          # the hatch's own wooden hinge creak
heavy_clunk = cut(hatch, .525, .90, .0015, .12)        # its heavy wooden clunk
light_clunk = cut(hatch, .108, .20, .001, .05)         # the small knock at the start of the creak
pawl = [cut(winch, a - .004, a + .045, .001, .02) for a in (.19, .31, .365, .645, .70, 1.035)]
fibre = cut(rope, .10, .42, .004, .06)                 # rope-fibre squeak of the pull


# ---- handle knock: the wooden pull handle snapping back against the frame ------------------
def handle_knock(seed, double):
    r = np.random.default_rng(seed)
    y = np.zeros(n(.32))
    f0 = r.uniform(470, 560)
    def knock(g, s):
        k = modal([f0, f0 * 2.71, f0 * 5.2, f0 * .52], [.045, .018, .008, .03], [1, .55, .25, .35], .2, .03, s)
        k = add(k, click(.02, 1500, 6000, .003, s) * .9)
        return k * g
    place(y, knock(1, seed), 0)
    if double: place(y, knock(.32, seed + 7) * .9, .052 + r.uniform(0, .01))
    place(y, pitch(fibre, 1.35)[:n(.12)] * .18, .004)
    return room(y, .16, .10, seed)


save('rig_handle_knock_01.wav', handle_knock(11, True), -3)
save('rig_handle_knock_02.wav', handle_knock(12, False), -3)


# ---- hatch unlatch: iron latch flips, the hatch hinge begins to creak ----------------------
def unlatch():
    y = np.zeros(n(.55))
    latch = add(modal([2300, 3870, 6150, 1240], [.012, .008, .004, .02], [1, .6, .35, .4], .1, .02, 3), click(.02, 2500, 9000, .002, 3))
    place(y, latch, 0, .8)
    place(y, latch * .45, .028)                           # the latch tongue rattles once
    place(y, pitch(hinge_creak, 1.12), .02, 1.1)          # hinge creak from hatch_open.wav, a touch faster
    place(y, creak(.26, 210, 150, seed=4) * .25, .05)
    return room(y, .2, .12, 3)


save('rig_hatch_unlatch.wav', unlatch(), -2)


# ---- hatch knocks: the hatch hits its open stop on the overshoot and each bounce ------------
def hatch_knock(seed, ratio, sub):
    r = np.random.default_rng(seed)
    y = np.zeros(n(.5))
    place(y, pitch(heavy_clunk, ratio), 0)
    place(y, thump(sub * 1.9, sub, .07, .3) * .45, .001)
    place(y, pitch(light_clunk, ratio * 1.5) * .4, .035 + r.uniform(0, .015))   # hinge pin rattle
    place(y, wood_clack(sub * 3.6, seed) * 1.1, 0)
    place(y, creak(.12, 260, 190, seed=seed) * .1, .05)
    return room(y, .22, .12, seed)


save('rig_hatch_knock_01.wav', hatch_knock(21, 1.0, 62), -1, 1.0)
save('rig_hatch_knock_02.wav', hatch_knock(22, 1.09, 70), -1, 1.0)
save('rig_hatch_knock_03.wav', hatch_knock(23, .93, 56), -1, 1.0)


# ---- hatch slam: the hatch swings shut against the ceiling frame and the latch catches -----
def slam():
    y = np.zeros(n(1.0))
    place(y, pitch(heavy_clunk, .9), 0, 1.0)
    place(y, thump(105, 40, .11, .5) * .75, 0)
    place(y, click(.05, 300, 2600, .012, 31) * .5, 0)
    place(y, wood_clack(230, 34) * 1.6, .001)                             # board-on-board crack
    latch = add(modal([2150, 3620, 5800], [.01, .007, .004], [1, .55, .3], .08, .02, 32), click(.015, 2500, 9000, .002, 32))
    place(y, latch * .35, .018)
    place(y, pitch(light_clunk, 1.3) * .45, .085)                                 # the hatch jumps once in the frame
    place(y, pitch(light_clunk, 1.6) * .22, .15)
    r = np.random.default_rng(33)                                                 # dust and grit from the ceiling
    grit = np.zeros(n(.5))
    for _ in range(26):
        place(grit, click(.006, 2500, 8000, .001, int(r.integers(1e6))) * r.uniform(.2, 1), r.uniform(0, .45) ** 1.6)
    place(y, grit * .05, .02)
    return room(y, .3, .16, 31)


save('rig_hatch_slam.wav', slam(), -1, 2.2)


# ---- cable creak: the two ropes take the goblin's weight over the wooden pulleys ------------
def cable_creak(seed, dur, events):
    r = np.random.default_rng(seed)
    y = np.zeros(n(dur + .3))
    for at, length, f0, f1, g in events:
        c = creak(length, f0, f1, body=(r.uniform(480, 640), r.uniform(980, 1250), r.uniform(1900, 2500)),
                  seed=int(r.integers(1e6)), roughness=.25, wobble=r.uniform(3, 7))
        place(y, c * g, at)
    place(y, pitch(fibre, .82) * .22, events[0][0])                              # fibres stretching
    # faint continuous rope rub on the pulley
    rub = bp(r.standard_normal(n(dur)), 700, 2600) * (0.5 + .5 * np.sin(2 * np.pi * 9 * t(dur)) ** 2)
    rub *= np.sin(np.pi * np.linspace(0, 1, n(dur))) ** 1.5
    place(y, rub * .025, 0)
    return room(y, .2, .12, seed)


save('rig_cable_creak_01.wav', cable_creak(41, 1.15, [(.0, .34, 180, 120, 1.0), (.42, .22, 240, 200, .6), (.78, .3, 150, 105, .8)]), -4)
save('rig_cable_creak_02.wav', cable_creak(42, 1.15, [(.05, .26, 210, 160, .8), (.36, .4, 165, 120, 1.0), (.88, .2, 260, 230, .5)]), -4)


# ---- winch unwind: lowering — pawl lifted, drum freewheels against its brake and slows ------
def unwind(seed, dur=1.15):
    r = np.random.default_rng(seed)
    total = dur + .25
    y = np.zeros(n(total))
    tt = t(dur); speed = (1 - tt / dur) ** 2                                       # same curve as the descent
    # pawl skipping over the gear teeth: rate follows the drum speed
    rate0 = 46.0; ph = 0.0; k = 0
    for i in range(len(tt)):
        ph += rate0 * speed[i] / SR
        if ph >= 1:
            ph -= 1; k += 1
            g = .35 + .65 * speed[i]
            place(y, pitch(pawl[int(r.integers(len(pawl)))], r.uniform(1.05, 1.3)) * g * .55, tt[i] + r.uniform(0, .004))
    # wooden drum rumble and brake friction
    noise = r.standard_normal(len(tt))
    rumble = bp(noise, 55, 220) * (speed ** .8) * 1.6
    rumble += lp(noise, 90) * speed * .8
    brake = bp(r.standard_normal(len(tt)), 900, 2600) * (.2 + .8 * speed) * .12
    brake = reson(brake, 1450 * 1.0, 6) * .5 + brake
    drum = (rumble + brake)
    place(y, drum * .45, 0)
    # the pulley axle squeal rising as it brakes
    sq = creak(.45, 300, 420, body=(880, 1600, 3100), seed=seed + 1, roughness=.08, wobble=2) * .08
    place(y, sq, dur - .5)
    return room(y, .24, .1, seed)


save('rig_winch_unwind.wav', unwind(51), -1.5)


# ---- winch stop: the brake catches, the pawl drops in, the ropes twang under the load -------
def stop(seed, heavy):
    r = np.random.default_rng(seed)
    y = np.zeros(n(.75))
    place(y, pitch(pawl[5], .78) * 1.0, 0)                                         # pawl drops into the gear
    place(y, pitch(pawl[2], .7) * .5, .012)
    wood = modal([118, 231, 410, 690], [.09, .05, .03, .015], [1, .5, .3, .2], .4, .04, seed)
    place(y, wood * (.9 if heavy else .6), .004)
    place(y, wood_clack(300 if heavy else 340, seed + 5) * .7, .002)                                   # wooden drum brake thunk
    place(y, thump(95 if heavy else 120, 45, .09, .4) * (.7 if heavy else .45), .002)
    tw = t(.45)                                                                    # rope twang under the jerk
    f = 74 if heavy else 92
    twang = (np.sin(2 * np.pi * f * tw) + .4 * np.sin(2 * np.pi * f * 2.02 * tw) + .2 * np.sin(2 * np.pi * f * 3.05 * tw)) \
        * np.exp(-tw / .12) * (1 + .3 * np.sin(2 * np.pi * 6 * tw))
    twang = lp(twang + bp(r.standard_normal(len(tw)), 400, 1800) * np.exp(-tw / .05) * .3, 1800)
    place(y, twang * .35, .01)
    place(y, creak(.22, 170, 120, seed=seed + 3) * .35, .06)
    if heavy:                                                                      # the landing bounce settles once more
        place(y, wood * .35, .105)
        place(y, pitch(pawl[1], .8) * .35, .11)
    else:
        place(y, pitch(heavy_clunk, 1.25) * .55, .03)
        place(y, wood_clack(260, seed) * .8, .03)                              # the body knocks the hatch frame
    return room(y, .24, .13, seed)


save('rig_winch_stop_01.wav', stop(61, True), -1, 1.2)
save('rig_winch_stop_02.wav', stop(62, False), -1, 1.2)
