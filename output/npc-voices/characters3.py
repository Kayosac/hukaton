"""The bulk buyer («Скупщик»): a seasoned fence of stolen goods. Low, raspy, sly voice; his signature is coins
(counted, poured, flicked) and the sack he hauls the whole shelf away in."""
from synth import *

S = syllable


def coin(f=3300, dur=.22, gain=1.0):
    """One coin ringing: inharmonic, fast decay, a tiny click on the hit."""
    hit = bandpass(noise(.006), 6000, 2) * np.exp(-900 * t_of(.006)) * .6
    ring = metal(f, dur, partials=(1, 2.41, 3.87, 5.6), decay=16, gains=(1, .5, .3, .15), detune=.004)
    return mix((hit, 0, 1), (ring, 0, .7)) * gain


def count_coins(n=4, gap=.11, base=3200):
    """Coins put down one by one, each a little different."""
    return mix(*[(coin(base * (1 + .06 * rng.uniform(-1, 1)), .2), i * gap * (1 + .15 * rng.uniform(-1, 1)), .55 + .2 * rng.random())
                 for i in range(n)])


def pour(dur=.4):
    """A handful of coins poured onto the counter."""
    times = np.sort(rng.uniform(0, dur * .75, 14))
    return mix(*[(coin(rng.uniform(2700, 4200), .18), float(tt), .25 + .3 * rng.random()) for tt in times])


def flick():
    """A coin flicked up with the thumb: the ping, then it lands back in the palm."""
    return mix((coin(3900, .35), 0, .8), (thud(160, .05), .3, .3), (bandpass(noise(.02), 2500, 3) * np.exp(-200 * t_of(.02)), .3, .7))


def sack(dur=.35):
    """A heavy sack dumped on the counter: a thud with cloth rustle."""
    rustle = bandpass(noise(dur), 1400, 1.2) * np.interp(t_of(dur), [0, .03, dur], [0, 1, 0]) * .35
    return mix((thud(65, .3), .02, 1), (rustle, 0, 1), (count_coins(3, .03, 2900) * .4, .04, 1))


def tsk():
    """The tongue click of a man who has seen better goods."""
    return bandpass(noise(.012), 3200, 5) * np.exp(-450 * t_of(.012)) * 1.3


def snort():
    n = noise(.12)
    return lowpass(bandpass(n, 700, 1.5), 1500) * np.sin(np.pi * t_of(.12) / .12) ** 2 * .6


def fence():
    T = .93; R = dict(tract=T, breath=.22, jitter=.012, vib=(4, .015), rough=.18)
    say = lambda c, d, v, **k: S(contour(*c), d, v, **{**R, **k})
    heh = lambda f: say(((0, f), (1, f * .9)), .075, 'e', onset='h', a=.004, r=.035, breath=.35)
    return {
        # Sack on the counter, then a sly drawn-out «э-эй» — business is here.
        'arrive': mix((sack(), 0, .9),
                      (cat(say(((0, 118), (1, 128)), .12, 'e', onset='h'), silence(.02),
                           say(((0, 136), (.4, 150), (1, 122)), .3, 'a', a=.02, r=.12)), .22, 1)),
        # Weighs it: a low hum, coins counted under it, a click of the tongue.
        'hmm': mix((hum(contour((0, 120), (.4, 112), (1, 128)), .5, T, breath=.18, vib=(3.5, .015)), 0, 1),
                   (count_coins(4, .1), .08, .55), (tsk(), .6, .9)),
        # A raspy chuckle and the coins poured out.
        'happy': mix((cat(*[cat(heh(f), silence(.035)) for f in (150, 142, 136, 128)]), 0, 1),
                     (pour(.45), .12, .5), (flick(), .5, .5)),
        # A click, a snort, a grumbled «н-на» down, and the sack slapped shut.
        'angry': mix((tsk(), 0, 1), (snort(), .06, .8),
                     (drive(cat(say(((0, 140), (1, 130)), .09, 'e', onset='t'), silence(.03),
                                say(((0, 135), (.3, 150), (1, 95)), .3, 'a', rough=.4, breath=.25)), 2.2), .2, 1),
                     (thud(80, .2), .62, .6)),
    }


CHARACTERS3 = [
    ('bulk_buyer', 'BulkBuyer', 'Скупщик', 'Низкий хриплый хитрый голос; мешок на прилавок, пересчёт и звон монет, цоканье языком.', fence),
]


def fence_talk():
    """His explanation, line by line: four short raspy babbled phrases (one per line on screen), a coin or a tongue
    click here and there. Varied so consecutive lines never sound alike."""
    T = .93; R = dict(tract=T, breath=.22, jitter=.012, vib=(4, .015), rough=.18)
    say = lambda f0, f1, d, v, onset=None, **k: S(contour((0, f0), (1, f1)), d, v, onset=onset, **{**R, 'a': .008, 'r': .04, **k})
    gap = lambda d=.035: silence(d)
    # «Я скупщик…» — matter-of-fact, falling.
    a = mix((cat(say(130, 138, .09, 'a', 'h'), gap(), say(142, 132, .1, 'u', 'k'), gap(),
                 say(136, 124, .11, 'i', 'sh'), gap(.05), say(128, 112, .18, 'a', 't')), 0, 1), (coin(3500, .2), .62, .35))
    # «Плачу три четверти…» — a little haggling lilt.
    b = mix((cat(say(126, 134, .08, 'a', 'p'), gap(), say(138, 146, .09, 'u', 'sh'), gap(),
                 say(150, 140, .09, 'i', 't'), gap(), say(140, 128, .1, 'e', 'h'), gap(.05), say(132, 118, .16, 'o')), 0, 1),
            (count_coins(2, .09), .5, .35))
    # «Не торгуюсь…» — flat and firm, then a click of the tongue.
    c = mix((cat(say(124, 122, .08, 'e', 'h'), gap(), say(126, 124, .1, 'o', 't'), gap(), say(124, 120, .1, 'u', 'k'), gap(.06),
                 say(122, 110, .16, 'a', 'h')), 0, 1), (tsk(), .66, .8))
    # The offer: «По рукам?» — sly, rising at the end.
    d = mix((cat(say(128, 132, .09, 'o', 'p'), gap(), say(134, 128, .09, 'u', 'h'), gap(.04), say(136, 162, .2, 'a', 'k', a=.02, r=.08)), 0, 1),
            (flick(), .1, .3))
    return [a, b, c, d]
