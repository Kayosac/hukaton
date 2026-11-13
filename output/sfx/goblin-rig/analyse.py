import sys, wave, numpy as np
def load(p):
    w = wave.open(p); n = w.getnframes(); sr = w.getframerate(); ch = w.getnchannels(); sw = w.getsampwidth()
    x = np.frombuffer(w.readframes(n), dtype=np.int16 if sw == 2 else np.int32).astype(float)
    x = x.reshape(-1, ch).mean(1) / (32768 if sw == 2 else 2**31)
    return x, sr, ch, sw
for p in sys.argv[1:]:
    x, sr, ch, sw = load(p)
    peak = np.abs(x).max(); rms = np.sqrt((x**2).mean())
    hop = int(sr * .02); env = [np.sqrt((x[i:i+hop]**2).mean()) for i in range(0, len(x) - hop, hop)]
    env = np.array(env); 
    def db(v): return 20*np.log10(max(v,1e-9))
    # active region rms
    act = env[env > peak*.05]
    spec = np.abs(np.fft.rfft(x * np.hanning(len(x)))); f = np.fft.rfftfreq(len(x), 1/sr)
    cent = (spec*f).sum()/spec.sum()
    bands = [(0,150),(150,500),(500,2000),(2000,6000),(6000,22050)]
    tot = (spec**2).sum(); bs = ' '.join(f'{a}-{b}:{100*(spec[(f>=a)&(f<b)]**2).sum()/tot:.0f}%' for a,b in bands)
    print(f'{p.split("/")[-1]}: {len(x)/sr:.2f}s sr={sr} ch={ch} bits={sw*8} peak={db(peak):.1f}dB rms={db(rms):.1f} activeRms={db(np.sqrt((act**2).mean()) if len(act) else 0):.1f} centroid={cent:.0f}Hz {bs}')
    # coarse envelope in 50ms steps (dB)
    e2 = env[::max(1,len(env)//40)]
    print('   env:', ' '.join(f'{db(v):.0f}' for v in e2))
