# MG01 "Бог в собственных руках" — shared notes for module agents

Project: Unity hackathon game "Hakaton". Asset MG01: a mountain-sized multi-limbed god sitting in his own
giant hands (9 arms, 4 legs, 10 painted open eyes, wizard hat). Concept pack (read-only):
  /home/claude/pack/mountain-god-art-pack-v1/  (README.md, 02-documentation/MG01-Concept-and-Modeling-Guide-RU.md,
  03-specification/MG01-specification.md, 01-concepts/*.png — 00-canonical.png is the priority reference)
Grid crops of the canon with pixel coordinates: /home/claude/work/ref/grid_*.png, crop_mid.png, crop_low.png

Style: PSX-like LOW-POLY, flat-shaded broad facets, big simple masses, slightly crooked/irregular (the
user wants forms themselves a bit crooked/chaotic, not regular CAD shapes). Blunt boxy fingers like the
canon. Model silhouette/large depth; PAINT small details (lids, mouths, nails). No tiny bevels.

Tech: Python 3.13 + `bpy` 5.3 module (run scripts with `python3 -I script.py`; headless; Workbench
render works, EEVEE is very slow — avoid). 2 CPU cores shared with other agents: keep renders small.
Coordinates: Blender metres at working scale 1:100 (model ~18 m tall). +X = canonical screen right,
-Y = FRONT (towards the canon viewer), +Z up, ground z=0.
Shared code (READ-ONLY, do not edit): /home/claude/work/mg/core.py (Shell, tube, blob, wedge,
frame_matrix), bl.py (shells_to_object, cameras, workbench render, contact_sheet), layout.py (targets:
SUPPORT_HANDS, CRADLE_HANDS, SMALL_HANDS, FEET, HEAD, TORSO, LIMBS, EYES/FACES/BRANCHES canon px; P()/G()
unproject canon pixels). Layout numbers may still be tuned by the lead — never hardcode them, import layout.
Blocking scene for context: /home/claude/work/out/blocking/blocking.blend (+ overlay.png, turntable.png).

Every builder returns pure data (core.Shell objects: closed, manifold, outward faces) — the lead joins
everything and runs an EXACT boolean union into one body mesh, so shells MAY overlap/penetrate each
other (e.g. fingers sunk 20-30% into the palm) but each shell itself must be closed (no holes,
no non-manifold edges, no zero-area faces, no self-intersection inside one shell).
Rig-readiness (a skeleton will be added later): put ring loops at joints (wrist, MCP, PIP, DIP,
elbow/knee/ankle), keep 2+ rings around each bending joint, and return the joint chain positions so
the lead can write rig guides. Set Shell.group to the limb ID (e.g. 'S01') so vertex groups map to
limbs; Shell.mat in {'body','cloth','leaves','hat'}.
Triangle counts are counted after triangulation (core.Shell.tri_count()).
