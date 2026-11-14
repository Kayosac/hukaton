"""Проверка петли: длительность, пик, RMS, RMS по тактам, стык конец→начало."""
import sys
import numpy as np
from scipy_lite import wavfile

for p in sys.argv[1:]:
    sr, x = wavfile.read(p)
    x = x.astype(float) / 32768
    L = len(x)
    db = lambda v: 20 * np.log10(max(v, 1e-12))
    rms = np.sqrt(np.mean(x ** 2))
    bar = int(2.0 * sr)
    bars = [db(np.sqrt(np.mean(x[i:i + bar] ** 2))) for i in range(0, L - bar + 1, bar)]
    d = np.abs(np.diff(x, axis=0)).max(1)
    seam = np.abs(x[0] - x[-1]).max()
    w = int(0.005 * sr)
    tail, head = x[-w:], x[:w]
    print(f"{p}: {L / sr:.3f} c, {sr} Гц, пик {db(np.abs(x).max()):.2f} dBFS, RMS {db(rms):.2f} dB")
    print(f"  RMS по тактам: мин {min(bars):.1f}, макс {max(bars):.1f}, разброс {max(bars) - min(bars):.1f} dB")
    print(f"  стык: скачок {seam:.4f} (обычный шаг: медиана {np.median(d):.4f}, 99.9% {np.percentile(d, 99.9):.4f}, макс {d.max():.4f})")
    print(f"  RMS последних/первых 5 мс: {db(np.sqrt(np.mean(tail ** 2))):.1f} / {db(np.sqrt(np.mean(head ** 2))):.1f} dB")
    # the seam against every other bar line: steps within ±1 ms and the first 5 ms
    k = int(0.001 * sr)
    other = [np.abs(np.diff(x[b - k:b + k], axis=0)).max() for b in range(bar, L - bar + 1, bar)]
    heads = [db(np.sqrt(np.mean(x[b:b + w] ** 2))) for b in range(bar, L - bar + 1, bar)]
    xx = np.concatenate([x[-k:], x[:k]])
    print(f"  стык ±1 мс: макс шаг {np.abs(np.diff(xx, axis=0)).max():.4f}; на других тактовых чертах: "
          f"медиана {np.median(other):.4f}, макс {max(other):.4f}; первые 5 мс других тактов: {np.median(heads):.1f} dB")
    # crest per 50 ms window — looks for sudden loud events
    win = int(0.05 * sr)
    r = np.sqrt(np.mean(x[: L // win * win].reshape(-1, win, 2) ** 2, axis=(1, 2)))
    print(f"  самое громкое окно 50 мс: {db(r.max()):.1f} dB на {np.argmax(r) * 0.05:.2f} c")
    print("  такты:", " ".join(f"{b:.0f}" for b in bars))
