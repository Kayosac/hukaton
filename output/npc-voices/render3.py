import json, os
from synth import finish, save, SR
from characters3 import CHARACTERS3
from characters import CUES
os.makedirs('wav', exist_ok=True)
info = {}
for key, npc, name, desc, fn in CHARACTERS3:
    cues = fn()
    for cue, _ in CUES:
        x = finish(cues[cue]); save(f'wav/{key}_{cue}.wav', x); info[f'{key}_{cue}'] = round(len(x) / SR, 2)
print(json.dumps(info))
from characters3 import fence_talk
for i, x in enumerate(fence_talk()):
    x = finish(x); save(f'wav/bulk_buyer_talk{i + 1}.wav', x); info[f'bulk_buyer_talk{i + 1}'] = round(len(x) / SR, 2)
print(json.dumps(info))
