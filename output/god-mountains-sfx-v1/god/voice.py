"""The god's voice: RHVoice (aleksandr-hq) lowered and slowed by resampling (formants go down with the pitch, like a
much bigger throat), a sub-octave under it, a slow chorus, a rock rumble that follows the words, a big hall."""
import subprocess, sys, os
import numpy as np, librosa
from lib import *

LINES = [
    "А. Вот ты где. Маленький. Шумный.",
    "Ты шёл через мою гору. Каждый твой шаг щекотал мне ладонь.",
    "Внизу ты торгуешь. Монеты звенят до самых облаков, я слышу.",
    "Тысяча монет. Для тебя гора. Для меня пылинка на ногте.",
    "Но ты дошёл. Значит, твоя дверь теперь откроется и в другие места.",
    "Иди. Торгуй. Бросай свои карты в камни мира.",
]
DOWN = .76          # resample factor: pitch and formants down ~4.75 semitones, pace x0.76
OUT = sys.argv[1] if len(sys.argv) > 1 else "out"
os.makedirs(OUT, exist_ok=True)


def tts(text, path):
    subprocess.run(["RHVoice-test", "-p", "aleksandr-hq", "-t", "72", "-r", "108", "-o", path],
                   input=text.encode(), check=True, stderr=subprocess.DEVNULL)
    y, sr = librosa.load(path, sr=None)
    return y, sr


def god_voice(y, sr):
    main = librosa.resample(y, orig_sr=sr * DOWN, target_sr=SR)
    main = hp(main, 45)
    sub = librosa.effects.pitch_shift(main, sr=SR, n_steps=-12, bins_per_octave=12)
    sub = lp(sub, 380, 2)
    def det(k, ms):
        z = librosa.resample(main, orig_sr=SR * k, target_sr=SR)[:len(main)]
        z = np.concatenate([np.zeros(secs(ms / 1000)), z])[:len(main)]
        return np.pad(z, (0, len(main) - len(z)))
    cl, cr = det(1.007, 19), det(.993, 29)
    body = main + .55 * lp(main, 170) + .5 * sub
    x = np.stack([body + .3 * cl, body + .3 * cr], 1)
    x = lp(x, 6200, 2)
    # the mountain resonates under the words
    e = lp(np.abs(main), 5, 1)
    e = e / (e.max() + 1e-9)
    rum = lp(brown(len(main) / SR + .01)[:len(main)], 110, 2) * e * .45
    x += widen(rum, 17)
    x = np.tanh(x / (np.abs(x).max() + 1e-9) * 1.6)
    x = np.concatenate([np.zeros((secs(.12), 2)), x])
    x = reverb(x, wet=.32, rt=4.2, dark=2600, pre=.06)
    return master(trim_tail(x, -55), -1.5)


for i, line in enumerate(LINES):
    y, sr = tts(line, f"/tmp/l{i}.wav")
    v = god_voice(y, sr)
    write(f"{OUT}/gm_god_line{i + 1}.wav", v)
    f0, vo, _ = librosa.pyin(v[:, 0], fmin=30, fmax=200, sr=SR, frame_length=4096)
    print(i + 1, f"{len(v) / SR:.1f}s", "f0", round(float(np.nanmedian(f0)), 1))
