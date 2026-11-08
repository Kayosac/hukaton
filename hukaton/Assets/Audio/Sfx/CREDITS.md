# Credits and licenses

## Kenney audio packs (CC0 1.0)

Some of these sounds use, layer or reprocess recordings from these Kenney (www.kenney.nl) packs:

| Pack | Used for (examples) | Obtained from (GitHub mirror) |
|---|---|---|
| **Casino Audio** | card slides, places, shoves, fan and shuffle (cards/*, impact_paper, ui_confirm, transition_whoosh) | `ismetcabbar9-oss/kenney-audio-assets` ("Audio (295 files)/Casino sounds") |
| **Impact Sounds** | footsteps (grass/wood), punch/soft/wood/glass/plank impacts | `Boyquotes/kenney-impact-sounds-for-godot` |
| **RPG Audio** | book place, cloth, creaks, coins, leather, latch, door close, knife slice, chop, footsteps | `Boyquotes/kenney-rpg-audio-for-godot` |
| **Sci-Fi Sounds** | low_frequency_explosion, force_field, thruster_fire, computer_noise (as low-passed layers) | `Boyquotes/kenney-sci-fi-sounds-for-godot` |
| **UI Audio** (UI SFX Set) | click1, click3, rollover2 | `Calinou/kenney-ui-audio` |
| **Interface Sounds** | tick_001 | `Calinou/kenney-interface-sounds` |

Before any file was used, the LICENSE file in each of these repositories was checked. Each one states
**Creative Commons Zero v1.0 Universal (CC0 1.0), public-domain dedication**:
https://creativecommons.org/publicdomain/zero/1.0/

> "This content is free to use in personal, educational and commercial projects.
> Support us by crediting Kenney or www.kenney.nl (this is not mandatory)" (Kenney license text)

Crediting Kenney is optional, but it is appreciated.

The `manifest.json` `source` field lists the exact original Kenney file or files used for each cue.

## Synthesized material

Everything else was synthesized from scratch with Python (numpy/scipy) for this library: noise sweeps, modal coins and bells, FM and glockenspiel chimes, formant creature voices, crackle, ambience beds, the fanfare and the sting. The same applies to the synthesized layers inside the hybrid cues. This material contains no third-party audio. It is released under the same terms, CC0 1.0 (public domain), so you can use, modify and ship it without restriction.

## Inspected but not used

- Kenney Digital Audio, Jingles and Voiceover Fighter (CC0, same mirrors). They were checked, but no file from them is in this library.
- The mirror `Boyquotes/kenney-voiceover-for-godot` was **skipped** because its LICENSE file is GPL-3.0, not CC0.
