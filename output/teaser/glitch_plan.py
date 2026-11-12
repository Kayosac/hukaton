"""Shared plan of the end glitch of the PENUS teaser, used by the picture (teaser_glitch.py) and the score
(audio/teaser_score_b.py), so every broken frame has its broken sound. Frames are teaser frames at 30 fps.

The logo holds clean for a while; from START single small glitches creep in, they come more often and get heavier,
from MELT the whole picture (and the music) starts to melt, and at CUT — exactly on a bar line of the music —
picture and sound end dead. The frames after CUT are black and silent."""
import random

FPS = 30
TOTAL = 760            # 25.33 s
START = 570            # 19.0 s: the first small glitch
MELT = 702            # 23.4 s: everything starts to melt
CUT = 750              # 25.0 s: the bar line; black and silence from here


def plan():
    """[(frame, kind, strength 0..1)]; a frame may carry a glitch and the melt."""
    rng = random.Random(25011)
    out = []
    for f in range(START, CUT):
        x = (f - START) / (CUT - START)                    # 0..1 over the glitching part
        if f >= MELT:
            out.append((f, "melt", (f - MELT) / (CUT - 1 - MELT)))
        if rng.random() < 0.06 + 0.6 * x ** 2.2:
            if x < 0.3:
                kind = "tick"
            elif x < 0.55:
                kind = rng.choice(["tick", "tick", "light", "bands"])
            elif x < 0.75:
                kind = rng.choice(["light", "bands", "freeze", "blocks"])
            else:
                kind = rng.choice(["bands", "freeze", "blocks", "dark", "heavy", "shred"])
            out.append((f, kind, x))
    return out


PLAN = plan()
