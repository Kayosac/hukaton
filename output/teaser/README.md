# PENUS — teaser 18 s «А хозяин не придёт?»

- `PENUS-Teaser-18s-v3-A.mp4` and `PENUS-Teaser-18s-v3-B.mp4` — 1920 × 1080, 30 fps, 540 frames, H.264 + AAC 48 kHz, 18.000 s, about −15 LUFS. Same picture, two scores:
  - **A** — the «Кривая тропа» v2 instruments scored to the cuts (`audio/teaser_score.py`), now with a drop on the logo.
  - **B** — new music written for the trailer in the same world (`audio/teaser_score_b.py`): E phrygian sigh motif E–F–E–B on a music box, slow-motion choir and flute, falling chords on the three faces, a stuttering drum roll into the knock, a heartbeat under the question, the motif in full on the drop.
- v3 picture: the coin comes out of the dark as a crescent, spins fast and flashes while the camera drifts back; the card spins in the middle of the picture and drifts into the depth in slow motion through a dense upside-down rolling forest and through the shop from behind the counter towards the door; front-view peasant, goblin dog and card dealer; the fang on the counter's red pad; the logo grows.
- Picture: rendered in Unity from `hukaton/Assets/Teaser/Scenes/PenusTeaser.unity` (see `hukaton/Assets/Teaser/README.md`).
- Sound: `audio/teaser_common.py` holds the shared parts (the game's own effects from `hukaton/Assets/Audio/Sfx` placed on the edit, and the logo drop: a rising pull, a hair of silence, then a sub boom with the game's heavy impact). Instruments in `audio/synth/` are copies of `output/music/v2/src`.

| Time | Picture | Sound |
| --- | --- | --- |
| 0.0–2.0 | the coin comes out of the dark as a crescent, spins, glints; the camera drifts back | the coin rings an octave too low, a whoosh on the spin, a chime on the glint |
| 2.0–3.6 | a dense forest upside down, the camera rolls and pushes in; the card spins in the middle into the depth, slow motion | music enters on the cut, slowed paper rustle |
| 3.6–5.0 | the same card over the counter, the camera moves level from behind the counter towards the door | the music carries across the cut |
| 5.0–8.0 | customer, goblin dog, card dealer on the rig (front views) | shop bell at 0 / −2 / −4 semitones, 808 and saw bass stepping down, rope creak |
| 8.0–9.4 | the dog's fangs, very close | growl; A: low drone, the tape stops; B: a drum roll speeds up and stutters |
| 9.4–11.0 | the fang alone on the counter's red pad | dry knock on wood, then the quiet shop |
| 11.0–15.0 | the customer looks at the fang, then at us: «А хозяин не придёт?» | clock ticks, a short mumble |
| 15.0–18.0 | PENUS grows, the star glints | the drop (pull, silence, hit), then quiet: the real door bell at 16.3, a door and one board, cut at 17.8 |

## Rebuild

```
python3 audio/teaser_score.py     # A
python3 audio/teaser_score_b.py   # B
ffmpeg -y -framerate 30 -i ../../hukaton/Logs/teaser-frames/frame_%04d.png -i audio/PENUS-Teaser-18s-v3-audio-A.wav \
  -map 0:v -map 1:a -c:v libx264 -preset veryfast -crf 16 -pix_fmt yuv420p -movflags +faststart \
  -c:a aac -b:a 192k -t 18 PENUS-Teaser-18s-v3-A.mp4
```

The MP4 and WAV are not committed (media in `output/` stay out of git).
