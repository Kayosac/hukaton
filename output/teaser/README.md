# PENUS — teaser 22 s «А хозяин не придёт?»

- `PENUS-Teaser-22s-v5.mp4` — 1920 × 1080, 30 fps, 660 frames, H.264 + AAC 48 kHz, 22.000 s, about −14 LUFS.
- Picture: rendered in Unity from `hukaton/Assets/Teaser/Scenes/PenusTeaser.unity` (see `hukaton/Assets/Teaser/README.md`), then the end glitch from `teaser_glitch.py`.
- Sound: score B (`audio/teaser_score_b.py`), the trailer music in the world of «Кривая тропа»: E phrygian sigh motif E–F–E–B on a music box, slow-motion choir and flute, falling chords on the three faces, a stuttering drum roll cut dead by the blow that kills the dog, a heartbeat under the question; then, with no drop, the theme comes in under the logo and plays to the end. `audio/teaser_common.py` holds the game's own effects from `hukaton/Assets/Audio/Sfx` placed on the edit, the eye-match cut sound (a soft low "dum-tok": air pulled in, a low tom and a short wooden knock, a step lower on each cut), the kill and the tearing in the black, and the end glitch. Instruments in `audio/synth/` are copies of `output/music/v2/src`. (Score A and the logo drop of v3 are in the history at commit 489bd7b3.)
- `glitch_plan.py` is the one list of broken frames: the picture and the sound break on the same frames and both end dead on frame 656 (21.867 s); the last four frames are black and silent.

| Time | Picture | Sound |
| --- | --- | --- |
| 0.0–2.0 | the coin comes out of the dark as a crescent, spins, glints; the camera drifts back | the coin rings an octave too low, a whoosh on the spin, a chime on the glint, the music box sighs |
| 2.0–3.6 | a dense forest upside down, the camera rolls and pushes in; the card spins and rolls in the middle into the depth, slow motion | choir and flute enter on the cut, slowed paper rustle |
| 3.6–5.0 | the same card over the counter, the camera moves level from behind the counter towards the door | the music carries across the cut |
| 5.0–8.0 | customer, goblin dog, card dealer looking into the lens, cut on their eyes | the match-cut "dum-tok" at 0 / −2 / −4 semitones, chords falling Em → C → B, 5/8 pulse, rope creak |
| 8.0–9.4 | the open mouth, pushing in on the cracked fang | growl, a drum roll speeds up and stutters |
| 9.4–10.0 | black | the blow, the dog's death cry, the body drops, wet tearing, the root cracks, the fang pops free |
| 10.0–11.0 | the fang on the counter's red pad, exactly where it was in the jaw; the camera pulls back | dry knock on wood, then the quiet shop |
| 11.0–15.0 | the customer looks at the fang, then at us: «А хозяин не придёт?» | clock ticks, a short mumble, a sub hum and a speeding heartbeat |
| 15.0–21.47 | PENUS comes out of the dark through a sliding mask, one full turn easing to rest, a sparkle at 16.36; the camera pulls back | the heartbeat lets go, the music box sighs the motif on the turn (B on the sparkle), then the theme: choir, kalimba, 5/8 pulse, the flute on the motif over Em F Em C B7 Em F, growing |
| 21.47–21.87 | glitches: torn bands, colour split, frozen frames, broken blocks, a dark flicker, the picture shreds | the music crushes, stutters, drops out and buzzes on the same frames |
| 21.87–22.0 | black | silence |

## Rebuild

```
python3 audio/teaser_score_b.py      # -> audio/PENUS-Teaser-22s-v5-audio.wav
python3 teaser_glitch.py             # breaks frames 644–655 of hukaton/Logs/teaser-frames, blacks out 656–659
ffmpeg -y -framerate 30 -i ../../hukaton/Logs/teaser-frames/frame_%04d.png -i audio/PENUS-Teaser-22s-v5-audio.wav \
  -c:v libx264 -preset veryfast -crf 16 -pix_fmt yuv420p -c:a aac -b:a 192k -shortest -movflags +faststart \
  PENUS-Teaser-22s-v5.mp4
```

`teaser_glitch.py` keeps the clean frames it breaks in `Logs/teaser-frames-clean` and takes them again after a new render, so it can be run more than once. The MP4 and WAV are not committed (media in `output/` stay out of git).
