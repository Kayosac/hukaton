# Music

`MainMenuMusic.wav` — the main menu track («Кривая тропа», B v2: dark fantasy in 7/8 with retrowave and weirdcore, 67.2 s, stereo). Plays once, no loop. Synthesized from scratch with Python (numpy/scipy) for this game, CC0 1.0. Sources: `output/music/v2/src/` (`track_b2.py`).

`PcMenuMusic.wav` — the backroom PC's quiet menu loop («Рабочий стол», 57.6 s, seamless). Synthesized from scratch with Python (numpy/scipy) for this game, no third-party audio, CC0 1.0 like `../Sfx`. The "phone earpiece" sound (mono, 300–3400 Hz, small-speaker resonance, 8-bit mu-law) is baked into the file, so it sounds the same in WebGL, where Unity's audio filters do not work. Sources: `output/music/pc2/src/` (`track_pc2.py`, then `phone.py`).

`ShopMusic.wav` — the shop's background loop («Лавка за стеной», 69.3 s, seamless): a dark music-box lullaby in slow 6/8 with one limping 7/8 bar, the same E minor + phrygian F as «Кривая тропа»; music box, kalimba, tape pad, drone, a wall clock as the pulse, a "radio from the backroom" and a slowed choir. Plays very quietly in the shop only after the tutorial (`ShopMusic` in the game scene). Synthesized from scratch with Python (numpy/scipy) for this game, CC0 1.0. Sources: `output/music/shop/src/` (`t1_za_stenoy.py`; the four other candidates are there too).
