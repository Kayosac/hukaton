"""Goblin ceiling rig foley v2: everything synthesized from scratch, no samples.
Physical-ish models: modal metal/wood impacts, stick-slip friction creaks, a ratchet (drum + pawl + gear teeth),
freewheel gear whir, brake squeal, rope twang, short room. 44.1 kHz mono 16-bit wav -> ./out"""
import os, sys, wave
import numpy as np
from scipy import signal

SR = 44100
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'out')
os.makedirs(OUT, exist_ok=True)

def n(s): return int(round(s * SR))
def t(s): return np.arange(n(s)) / SR
def sos(kind, f, o=2): return signal.butter(o, f, kind, fs=SR, output='sos')
def hp(x, f, o=2): return signal.sosfilt(sos('hp', f, o), x)
def lp(x, f, o=2): return signal.sosfilt(sos('lp', f, o), x)
def bp(x, lo, hi, o=2): return signal.sosfilt(sos('bp', [lo, hi], o), x)

def reson(x, f, q):
    b, a = signal.iirpeak(f, q, fs=SR)
    return signal.lfilter(b, a, x)

def place(buf, s, at, g=1.0):
    s = np.array(s, dtype=float)
    fo = min(len(s), n(.02))
    if fo > 1: s[-fo:] *= np.linspace(1, 0, fo) ** 2      # no click where a layer ends
    i = max(0, n(at)); j = min(len(buf), i + len(s))
    if j > i: buf[i:j] += s[:j - i] * g

def modal(freqs, decays, amps, dur, rng, detune=.01):
    tt = t(dur); y = np.zeros_like(tt)
    for f, d, a in zip(freqs, decays, amps):
        f *= 1 + rng.uniform(-detune, detune)
        ph = rng.uniform(0, 2 * np.pi)
        y += a * np.sin(2 * np.pi * f * tt + ph) * np.exp(-tt / d)
    return y

def strike(dur, lo, hi, decay, rng):
    """Contact noise of an impact (the 'tick' before the modes ring)."""
    x = rng.standard_normal(n(dur)) * np.exp(-t(dur) / decay)
    return bp(x, lo, min(hi, SR / 2 - 100), 2)

def metal_hit(rng, f0=1650, size=1.0, dur=.35, bright=1.0):
    r = [1, 1.62, 2.41, 3.37, 4.53, 5.86]
    fr = [f0 * k for k in r]
    dec = [.09 * size, .06 * size, .045 * size, .03 * size, .02 * size, .014 * size]
    am = [1, .7, .55 * bright, .4 * bright, .25 * bright, .15 * bright]
    y = modal(fr, dec, am, dur, rng, .02)
    place(y, strike(.012, 2500, 12000, .002, rng), 0, .6)
    return y

def wood_hit(rng, f0=420, dur=.25, damp=1.0, body=1.0):
    fr = [f0, f0 * 2.31, f0 * 3.9, f0 * 5.6]
    dec = [.05 / damp, .028 / damp, .016 / damp, .01 / damp]
    am = [1, .55, .3, .15]
    y = modal(fr, dec, am, dur, rng, .03)
    place(y, strike(.02, 300, 5000, .004, rng), 0, .9)
    y += body * thump(rng, 95, 60, .06, dur)
    return y

def thump(rng, f0, f1, decay, dur):
    tt = t(dur)
    f = f1 + (f0 - f1) * np.exp(-tt / .03)
    ph = 2 * np.pi * np.cumsum(f) / SR
    return np.sin(ph) * np.exp(-tt / decay) * (1 - np.exp(-tt / .0015))

def stick_slip(dur, rate_from, rate_to, formants, rng, rough=.25, env=None, q=9):
    """Friction creak: a jittered pulse train (stick-slip events) through resonances of the material."""
    tt = t(dur)
    rate = rate_from * (rate_to / rate_from) ** (tt / dur) if rate_from > 0 else np.full_like(tt, rate_to)
    rate *= 1 + rough * lp(rng.standard_normal(len(tt)), 12, 1) * 1.2
    rate = np.clip(rate, 15, 4000)
    ph = np.cumsum(rate) / SR
    pulses = np.zeros_like(tt)
    idx = np.nonzero(np.diff(np.floor(ph)) > 0)[0]
    pulses[idx] = 1 + rough * rng.standard_normal(len(idx))
    y = np.zeros_like(tt)
    for f, g in formants:
        y += g * reson(pulses, f, q)
    y += .15 * bp(rng.standard_normal(len(tt)), 1500, 7000) * lp(np.abs(pulses), 30, 1) * 3
    if env is not None: y *= env
    return y

def adsr(dur, a, r, shape=1.0):
    tt = t(dur)
    e = np.minimum(1, tt / max(a, 1e-4)) * np.minimum(1, (dur - tt) / max(r, 1e-4))
    return np.clip(e, 0, 1) ** shape

ROOM_IR = None
def room(x, mix=.13, size=.28, seed=7):
    global ROOM_IR
    rng = np.random.default_rng(seed)
    ir = rng.standard_normal(n(size)) * np.exp(-t(size) / (size / 5.5))
    ir = lp(hp(ir, 180), 5200)
    ir[: n(.006)] = 0
    ir /= np.sqrt(np.sum(ir ** 2))
    wet = signal.fftconvolve(x, ir)
    dry = np.concatenate([x, np.zeros(len(wet) - len(x))])
    return dry + mix * wet

def finish(name, x, peak_db=-1.0, drive=0.0, tail=True):
    x = hp(x, 35, 2)
    if tail: x = room(x)
    if drive > 0: x = np.tanh(x / np.max(np.abs(x)) * (1 + drive)) 
    # trim trailing silence
    a = np.abs(x); thr = 10 ** (-60 / 20) * a.max()
    last = np.nonzero(a > thr)[0][-1] + n(.02)
    x = x[: min(len(x), last)]
    fo = min(len(x), n(.015)); x[-fo:] *= np.linspace(1, 0, fo)
    x = x / np.max(np.abs(x)) * 10 ** (peak_db / 20)
    with wave.open(os.path.join(OUT, name), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((x * 32767).astype('<i2').tobytes())
    print(f'{name}: {len(x)/SR:.2f}s')

# ------------------------------------------------------------------------------------------------
# ROPE: the player's tug — grab (palm on fibres), the rope stretching taut over the pulley, the
# pulley's iron axle squeak and a deep thunk as the rig's trip catches.
def rope_tug(seed):
    rng = np.random.default_rng(seed)
    D = .9; y = np.zeros(n(D))
    place(y, .25 * bp(rng.standard_normal(n(.08)), 1200, 6000) * adsr(.08, .005, .07), 0)       # palm grabs fibres
    env = adsr(.42, .12, .12, 1.4)
    place(y, stick_slip(.42, 55, 140, [(420, 1), (980, .7), (1850, .35), (3100, .15)], rng, .3, env), .03, .9)  # rope stretch creak
    fib = bp(rng.standard_normal(n(.4)), 2500, 9000) * adsr(.4, .15, .2) * (.6 + .4 * np.sin(2 * np.pi * 23 * t(.4)))
    place(y, fib, .05, .22)                                                                         # fibres grinding
    place(y, stick_slip(.22, 700, 1100, [(1300, 1), (2650, .5), (4100, .25)], rng, .15, adsr(.22, .05, .1), q=14), .12, .28)  # pulley axle
    place(y, wood_hit(rng, 260, .3, 1.6, .8), .36, .35)                                             # trip catches, rig thunk
    place(y, metal_hit(rng, 1250, .5, .2, .4), .365, .12)                                           # latch tongue
    return y

# Handle springs back and knocks the frame (two variants)
def handle_knock(seed, bounces):
    rng = np.random.default_rng(seed)
    y = np.zeros(n(.6)); at = 0; g = 1.0
    for k in range(bounces):
        place(y, wood_hit(rng, 610 + rng.uniform(-40, 40), .22, 1.6, .5), at, g)
        place(y, .2 * strike(.03, 600, 3000, .006, rng), at, g)
        at += .085 * (.62 ** k) + rng.uniform(0, .01); g *= .45
    # rope swinging slap
    place(y, .3 * bp(rng.standard_normal(n(.12)), 400, 2500) * adsr(.12, .01, .1), .02)
    return y

# ------------------------------------------------------------------------------------------------
# HATCH
def hatch_unlatch(seed):
    rng = np.random.default_rng(seed)
    y = np.zeros(n(.8))
    place(y, metal_hit(rng, 2350, .5, .25, .8), 0, .5)                # bolt slides out: click
    place(y, .4 * bp(rng.standard_normal(n(.05)), 2000, 8000) * adsr(.05, .002, .045), .004)  # bolt scrape
    place(y, metal_hit(rng, 1780, .4, .2, .6), .055, .15)           # bolt hits its keeper
    env = adsr(.55, .06, .2, 1.2)
    place(y, stick_slip(.55, 70, 210, [(560, 1), (1220, .8), (2450, .4), (3900, .2)], rng, .25, env, q=16), .07, 1.6)  # iron hinge groan
    return y

def hatch_knock(seed, f0, sub):
    rng = np.random.default_rng(seed)
    y = np.zeros(n(.5))
    place(y, wood_hit(rng, f0, .45, .8, 2.0), 0)
    place(y, thump(rng, sub * 1.6, sub, .09, .4), 0, .9)
    pass             # hinge iron rings
    dust = bp(rng.standard_normal(n(.35)), 3000, 9000) * (rng.random(n(.35)) < .02) * np.exp(-t(.35) / .12)
    place(y, dust, .02, .5)
    return y

def hatch_slam(seed):
    rng = np.random.default_rng(seed)
    y = np.zeros(n(1.2))
    whoosh = lp(rng.standard_normal(n(.16)), 700) * np.linspace(0, 1, n(.16)) ** 2
    place(y, whoosh, 0, .35)
    hit = .16
    place(y, thump(rng, 90, 48, .16, .6), hit, 1.3)
    place(y, wood_hit(rng, 175, .6, .6, 2.2), hit, 1.0)
    place(y, wood_hit(rng, 390, .4, .9, .3), hit + .003, .6)
    place(y, metal_hit(rng, 2100, .5, .25, .6), hit + .012, .2)       # bolt snaps home
    dust = hp(rng.standard_normal(n(.7)), 2500) * (rng.random(n(.7)) < .012) * np.exp(-t(.7) / .3)
    place(y, dust, hit + .05, .9)                                      # dust and grit trickle
    return y

# Cables creak under the goblin's weight (two variants, 1.15 s)
def cable_creak(seed, events):
    rng = np.random.default_rng(seed)
    D = 1.25; y = np.zeros(n(D))
    for at, dur, r0, r1, g in events:
        env = adsr(dur, dur * .3, dur * .4, 1.3)
        place(y, stick_slip(dur, r0, r1, [(330, 1), (760, .7), (1500, .35), (2700, .15)], rng, .25, env, q=16), at, g)
    rub = bp(rng.standard_normal(n(D)), 900, 4000) * (.5 + .5 * np.sin(2 * np.pi * 3.1 * t(D))) * adsr(D, .2, .3)
    y += .015 * rub
    return y

# ------------------------------------------------------------------------------------------------
# MECHANISM — the ceiling winch: an iron drum on a wooden frame, a ratchet wheel with a heavy pawl.
def pawl_clack(rng, g=1.0, heavy=1.0):
    d = .18
    y = metal_hit(rng, 1500 + rng.uniform(-90, 90), .55 * heavy, d, 1.1)
    y += .7 * wood_hit(rng, 330 + rng.uniform(-25, 25), d, 1.4, .0)    # frame takes the knock
    y += .9 * heavy * thump(rng, 120, 85, .035, d)                      # drum
    return g * y

def winch_ratchet(seed, dur=1.15):
    """Hauling him up: the crank turns in strokes, the pawl clacks over each tooth, the drum groans, rope strains."""
    rng = np.random.default_rng(seed)
    y = np.zeros(n(dur + .3))
    # clack times: strokes of ~3 teeth, slight hesitations between strokes (effort)
    at = .015; k = 0
    while at < dur - .02:
        stroke_pos = k % 4
        place(y, pawl_clack(rng, .55 if stroke_pos == 0 else .3 + .08 * rng.random(), .8), at)
        at += (.16 if stroke_pos < 3 else .26) * rng.uniform(.9, 1.12); k += 1
    tt = t(dur)
    # drum + gear rumble modulated by the tooth rate
    rumble = lp(rng.standard_normal(len(tt)), 260, 3) * (.6 + .4 * np.sin(2 * np.pi * 13 * tt)) * adsr(dur, .05, .08)
    place(y, rumble, 0, .8)
    # gear mesh hum (tonal, wobbling with the crank)
    f = 190 * (1 + .04 * np.sin(2 * np.pi * 3.2 * tt))
    hum = (np.sin(2 * np.pi * np.cumsum(f) / SR) + .5 * np.sin(4 * np.pi * np.cumsum(f) / SR)) * adsr(dur, .06, .1)
    place(y, bp(hum, 150, 900), 0, .12)
    # rope straining on the drum
    place(y, stick_slip(dur, 45, 70, [(300, 1), (690, .6), (1400, .3)], rng, .45, adsr(dur, .15, .2, 1.2), q=8), 0, .5)
    # crank axle squeak each stroke
    for s in np.arange(.1, dur - .1, .36):
        place(y, stick_slip(.12, 900, 1300, [(1400, 1), (2900, .4)], rng, .2, adsr(.12, .02, .07), q=16), s + rng.uniform(0, .03), .22)
    return y

def winch_unwind(seed, dur=1.15):
    """Lowering: the pawl is lifted, the drum freewheels against its brake — a whirring gear train that spins up,
    then the brake shoe drags it down to a crawl (matches the descent curve)."""
    rng = np.random.default_rng(seed)
    tt = t(dur)
    u = tt / dur
    speed = np.sin(np.pi * np.clip(u * 1.15, 0, 1)) ** .7 * (1 - .65 * np.clip((u - .55) / .45, 0, 1))
    speed = np.maximum(speed, .05)
    teeth_rate = 5 + 22 * speed                                        # gear teeth ticks per second
    ph = np.cumsum(teeth_rate) / SR
    y = np.zeros(n(dur + .3))
    idx = np.nonzero(np.diff(np.floor(ph)) > 0)[0]
    for i in idx:
        s = i / SR
        g = .15 + .25 * speed[i]
        place(y, g * metal_hit(rng, 2600 + rng.uniform(-200, 200), .25, .08, .9), s)  # light freewheel ticks
    # whirring gear whine following the speed
    f = 120 + 520 * speed
    whine = np.sin(2 * np.pi * np.cumsum(f) / SR) + .4 * np.sin(2 * np.pi * np.cumsum(2.02 * f) / SR)
    place(y, bp(whine, 100, 2200) * speed ** 1.2, 0, .18)
    # drum rumble / rope paying out
    place(y, lp(rng.standard_normal(len(tt)), 300, 3) * speed, 0, .7)
    place(y, bp(rng.standard_normal(len(tt)), 1200, 5000) * speed ** 2 * (.6 + .4 * rng.random(len(tt))), 0, .12)
    # brake shoe drags in the second half: squeal (stick-slip) rising in pitch as it bites
    b0 = .5
    bd = dur - b0
    benv = adsr(bd, .12, .08, 1.0) * np.linspace(.4, 1, n(bd))
    place(y, stick_slip(bd, 520, 880, [(900, 1), (1750, .6), (3300, .3)], rng, .12, benv, q=18), b0, .5)
    return y

def winch_stop(seed, heavy):
    """The pawl drops and the brake catches: a big iron clank, the drum thuds, the ropes twang under the load;
    'heavy' = arrival down (weight bounces once), else = he bumps up into the hatch frame."""
    rng = np.random.default_rng(seed)
    y = np.zeros(n(1.1))
    place(y, pawl_clack(rng, .6, 1.1), 0)
    place(y, metal_hit(rng, 900, 1.4, .5, .6), .006, .4)               # ratchet wheel rings
    place(y, thump(rng, 80, 50, .14, .5), .004, 1.0)
    # rope twang: low plucked string with sagging pitch
    tt = t(.8)
    f = 98 * (1 + .03 * np.exp(-tt / .05))
    tw = np.zeros_like(tt)
    for h, a in ((1, 1), (2, .5), (3, .3), (4, .15)):
        tw += a * np.sin(2 * np.pi * h * np.cumsum(f) / SR) * np.exp(-tt / (.32 / h))
    place(y, tw * (1 + .2 * np.sin(2 * np.pi * 5.5 * tt)), .01, .5)
    if heavy:
        pass
    else:
        place(y, wood_hit(rng, 190, .45, .7, 2.0), .03, .9)             # goblin's rig bumps the hatch frame
    return y

if __name__ == '__main__':
    finish('rig_rope_tug.wav', rope_tug(1), -1.0)
    finish('rig_handle_knock_01.wav', handle_knock(11, 1), -2)
    finish('rig_handle_knock_02.wav', handle_knock(12, 1), -2)
    finish('rig_hatch_unlatch.wav', hatch_unlatch(21), -1.5)
    finish('rig_hatch_knock_01.wav', hatch_knock(31, 210, 68), -1, .6)
    finish('rig_hatch_knock_02.wav', hatch_knock(32, 232, 74), -1, .6)
    finish('rig_hatch_knock_03.wav', hatch_knock(33, 196, 62), -1, .6)
    finish('rig_hatch_slam.wav', hatch_slam(41), -.8, 1.2)
    finish('rig_cable_creak_01.wav', cable_creak(51, [(.0, .38, 60, 120, 1.0), (.45, .25, 90, 140, .6), (.8, .35, 55, 95, .85)]), -3)
    finish('rig_cable_creak_02.wav', cable_creak(52, [(.05, .3, 75, 130, .8), (.38, .42, 50, 100, 1.0), (.9, .25, 100, 150, .5)]), -3)
    finish('rig_winch_ratchet.wav', winch_ratchet(61), -1, .5)
    finish('rig_winch_unwind.wav', winch_unwind(71), -1.2)
    finish('rig_winch_stop_01.wav', winch_stop(81, True), -.8, .8)
    finish('rig_winch_stop_02.wav', winch_stop(82, False), -.8, .8)
