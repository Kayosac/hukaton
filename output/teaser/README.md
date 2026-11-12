# PENUS — teaser 18 s «А хозяин не придёт?»

- `PENUS-Teaser-18s-v2.mp4` — 1920 × 1080, 30 fps, 540 frames, H.264 + AAC 48 kHz, 18.000 s, about −14 LUFS. v2: the coin is the 3D model built from the HUD coin sprites (`output/coin-model`); v1 had the 42 px HUD flipbook.
- Picture: rendered in Unity from `hukaton/Assets/Teaser/Scenes/PenusTeaser.unity` (see `hukaton/Assets/Teaser/README.md`). Only existing game art is used.
- Sound: `audio/teaser_score.py` writes `audio/PENUS-Teaser-18s-v1-audio.wav`. The music is new, scored to the cuts with the «Кривая тропа» v2 instruments (`audio/synth/` are copies of `output/music/v2/src`); the effects are the game's own from `hukaton/Assets/Audio/Sfx`.

| Time | Picture | Sound |
| --- | --- | --- |
| 0.0–2.0 | black, then the tired coin (3D model) slowly turning | the coin rings an octave too low, faint breathing |
| 2.0–3.6 | the forest upside down, the card flies through | detuned flute, kalimba and drone in 7/8 enter on the cut, paper rustle |
| 3.6–5.0 | the same card past the shop shelves | the flute holds its note across the cut, warm pad |
| 5.0–8.0 | customer, goblin dog, card dealer on the rig | shop bell at 0 / −2 / −4 semitones, 808 and saw bass stepping down, rope creak |
| 8.0–9.4 | the dog's open mouth | low drone and growl, the tape stops |
| 9.4–11.0 | the fang alone on the counter | dry knock on wood, then the quiet shop |
| 11.0–15.0 | the customer looks at the fang, then at us: «А хозяин не придёт?» | clock ticks, a short mumble |
| 15.0–18.0 | PENUS, the star glints | far choir behind the wall, the real door bell at 16.3, a door and one board, cut at 17.8 |

## Rebuild

```
python3 audio/teaser_score.py
ffmpeg -y -framerate 30 -i ../../hukaton/Logs/teaser-frames/frame_%04d.png -i audio/PENUS-Teaser-18s-v1-audio.wav \
  -map 0:v -map 1:a -c:v libx264 -preset veryfast -crf 16 -pix_fmt yuv420p -movflags +faststart \
  -c:a aac -b:a 192k -t 18 PENUS-Teaser-18s-v2.mp4
```

The MP4 and WAV are not committed (media in `output/` stay out of git).
