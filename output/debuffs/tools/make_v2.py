"""v2: a universal look and sound for Slow (no substance), and a proper stun sound for Deafen."""
import sys, wave, numpy as np
from PIL import Image
ART, SFX = sys.argv[1], sys.argv[2]
W, H = 384, 216
yy, xx = np.mgrid[0:H, 0:W]

def noise(scale, seed):
    r = np.random.default_rng(seed)
    g = r.random((int(H / scale) + 2, int(W / scale) + 2))
    big = np.array(Image.fromarray((g * 255).astype(np.uint8)).resize((W + 2 * scale, H + 2 * scale), Image.BILINEAR), float) / 255
    return big[:H, :W]

# Slow: a few heavy, wobbly frames closing in from the screen edges, like time thickening around the player.
edge = np.minimum(np.minimum(xx, W - 1 - xx) / W, np.minimum(yy, H - 1 - yy) / W)  # distance to the nearest edge, in screen widths
wobble = (noise(20, 11) - .5) * .018 + (noise(6, 12) - .5) * .006
d = edge + wobble
a = np.zeros((H, W))
for lo, hi, alpha in [(-1, .028, 1.0), (.05, .063, .75), (.088, .097, .5), (.122, .128, .3)]:
    a[(d >= lo) & (d < hi)] = alpha
img = np.zeros((H, W, 4), np.uint8)
img[..., :3] = 255
img[..., 3] = (a * 255).astype(np.uint8)
Image.fromarray(img, 'RGBA').save(f"{ART}/DebuffSlow.png")

SR = 44100
rng = np.random.default_rng(21)
def t(seconds): return np.arange(int(SR * seconds)) / SR
def lowpass(x, cutoff):
    a = np.exp(-2 * np.pi * cutoff / SR); y = np.zeros_like(x); s = 0.0
    for i, v in enumerate(x): s = (1 - a) * v + a * s; y[i] = s
    return y
def save(name, x, peak=.85):
    x = x / (np.max(np.abs(x)) + 1e-9) * peak
    with wave.open(f"{SFX}/{name}.wav", 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((x * 32767).astype(np.int16).tobytes())

# Slow: everything winding down — a tone sliding down like a tape running out of power, with a slowing wobble.
n = t(.9)
f = 320 * np.exp(-n * 1.9)
phase = np.cumsum(f) / SR
tone = np.sin(2 * np.pi * phase) + .4 * np.sign(np.sin(2 * np.pi * phase * .5))
wob = 1 + .25 * np.sin(2 * np.pi * np.cumsum(9 * np.exp(-n * 2.5)) / SR)
x = lowpass(tone * wob, 1400) * np.minimum(1, n / .02) * np.exp(-n * 1.6)
save("debuff_slow", x, .7)

# Stun (Deafen): a concussive blow — a low boom and crack, then a loud high ring that beats and fades.
n = t(1.6)
boom = np.sin(2 * np.pi * np.cumsum(np.geomspace(130, 38, n.size)) / SR) * np.exp(-n * 6)
crack = lowpass(rng.normal(0, 1, n.size), 3500) * np.exp(-n * 45)
ring = (np.sin(2 * np.pi * 2650 * n) + np.sin(2 * np.pi * 2683 * n) + .4 * np.sin(2 * np.pi * 5310 * n)) * np.exp(-n * 1.9)
ring *= np.minimum(1, n / .03)
x = boom * 1.3 + crack * 1.1 + ring * .55
save("debuff_stun", x, .9)
print("ok")
