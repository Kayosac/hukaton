# Music

`PcMenuMusic.wav` — the backroom PC's quiet menu loop («Рабочий стол», 57.6 s, seamless). Synthesized from scratch with Python (numpy/scipy) for this game, no third-party audio, CC0 1.0 like `../Sfx`. The "phone earpiece" sound (mono, 300–3400 Hz, small-speaker resonance, 8-bit mu-law) is baked into the file, so it sounds the same in WebGL, where Unity's audio filters do not work. Sources: `output/music/pc2/src/` (`track_pc2.py`, then `phone.py`).
