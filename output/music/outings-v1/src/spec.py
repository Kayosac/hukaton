import sys, numpy as np, matplotlib
matplotlib.use('Agg'); import matplotlib.pyplot as plt
from scipy.io import wavfile
from scipy.signal import spectrogram
fs = sys.argv[1:]
fig, ax = plt.subplots(len(fs), 1, figsize=(14, 2.6*len(fs)))
ax = np.atleast_1d(ax)
for a, f in zip(ax, fs):
    sr, x = wavfile.read(f); x = x.mean(1)/32768
    fr, t, S = spectrogram(x, sr, nperseg=4096, noverlap=3072)
    a.pcolormesh(t, fr, 10*np.log10(S+1e-12), vmin=-115, vmax=-45, shading='auto')
    a.set_ylim(30, 16000); a.set_yscale('log'); a.set_title(f.split('/')[-1], fontsize=9)
    r = np.sqrt(np.convolve(x**2, np.ones(sr//2)/(sr//2), 'same'))
    a2 = a.twinx(); a2.plot(np.arange(len(x))/sr, 20*np.log10(r+1e-9), 'w', lw=0.8); a2.set_ylim(-45, -5)
plt.tight_layout(); plt.savefig('out/spec.png', dpi=60)
