"""Shared plan of the end glitch of the PENUS teaser (v4), used by the picture (teaser_glitch.py) and the score
(audio/teaser_score_b.py) so every broken frame has its broken sound. Frames are teaser frames at 30 fps."""
FPS = 30
TOTAL = 660            # 22.0 s
CUT = 656              # first frame of the hard black; picture and sound end dead here (21.867 s)
PLAN = [               # (frame, kind)
    (644, "light"), (645, "light"), (646, "bands"), (647, "freeze"), (648, "blocks"), (649, "dark"),
    (650, "heavy"), (651, "freeze"), (652, "blocks"), (653, "heavy"), (654, "freeze"), (655, "shred"),
]
