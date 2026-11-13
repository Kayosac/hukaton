import numpy as np, scipy.signal as sg, scipy.io.wavfile as wf
SR=44100
rng=np.random.default_rng(7)
def noise(n): return rng.standard_normal(n)
def bp(x,lo,hi,order=2): b,a=sg.butter(order,[lo/(SR/2),hi/(SR/2)],'band'); return sg.lfilter(b,a,x)
def lp(x,f,order=2): b,a=sg.butter(order,f/(SR/2),'low'); return sg.lfilter(b,a,x)
def hp(x,f,order=2): b,a=sg.butter(order,f/(SR/2),'high'); return sg.lfilter(b,a,x)
def sweep_bp(x,f0,f1,q=1.2,blocks=200):
    # time-varying band-pass by overlapping blocks
    n=len(x); out=np.zeros(n); win=n//blocks*2; hop=win//2; w=np.hanning(win)
    for i in range(0,n-win,hop):
        t=i/n; f=f0*(f1/f0)**t
        seg=x[i:i+win]*w
        lo=max(20,f/q); hi=min(SR/2-100,f*q)
        out[i:i+win]+=bp(seg,lo,hi)
    return out
def env(n,a,peak,r):
    t=np.linspace(0,1,n); e=np.where(t<peak,(t/peak)**a,((1-t)/(1-peak))**r); return e
def reverb(x,size=.35,decay=2.2,mix=.35):
    n=int(SR*decay); ir=noise(n)*np.exp(-np.linspace(0,8,n)); ir=lp(ir,1800); ir/=np.abs(ir).sum()**.5*40
    y=sg.fftconvolve(x,ir)[:len(x)+n//3]
    out=np.zeros(len(y)); out[:len(x)]+=x*(1-mix); out+=y*mix
    return out
def norm(x,peak=.9): return x/np.max(np.abs(x))*peak
def save(name,x): wf.write(f'/tmp/gmsfx/{name}.wav',SR,(np.clip(x,-1,1)*32767).astype(np.int16)); print(name, round(len(x)/SR,2),'s')

# ---- breath of the mountain: inhale 3 s, exhale 4 s, one seamless 7 s cycle
ni,ne=int(3*SR),int(4*SR)
inh=sweep_bp(noise(ni),110,320,1.5)*env(ni,1.6,.82,.6)
inh+=lp(noise(ni),70)*6*env(ni,2,.85,.5)            # chest rumble
exh=sweep_bp(noise(ne),340,95,1.6)*env(ne,.7,.12,1.6)
exh+=lp(noise(ne),60)*8*env(ne,.8,.15,1.4)
# grit in the exhale: sparse little clicks of falling crumbs
grit=np.zeros(ne)
for _ in range(70):
    p=int(rng.uniform(.1,.8)*ne); L=int(SR*rng.uniform(.004,.012))
    if p+L<ne: grit[p:p+L]+=hp(noise(L),1500)*np.hanning(L)*rng.uniform(.05,.25)
exh+=grit*env(ne,.8,.25,1.2)
cyc=np.concatenate([inh*0.8,exh])
# the cavern: a dark room tail, folded back onto the start so the loop is seamless
wet=reverb(cyc,decay=2.6,mix=.4)
tail=wet[len(cyc):]; loop=wet[:len(cyc)].copy(); loop[:len(tail)]+=tail
loop=lp(loop,2200)
save('gm_breath',norm(loop,.85))
# muffled (behind the stone frame): dark and far
m=lp(lp(loop,260),260)
save('gm_breath_muffled',norm(m,.6))

# ---- wind in the gap: 10 s seamless loop
n=10*SR
w=bp(noise(n+SR),180,900)+.5*bp(noise(n+SR),900,2600)
lfo=.55+.45*np.sin(np.linspace(0,2*np.pi*3,n+SR)+1)*(.7+.3*np.sin(np.linspace(0,2*np.pi*7,n+SR)))
w*=lfo
x=w[:n].copy(); fade=np.linspace(0,1,SR); x[:SR]=x[:SR]*fade+w[n:n+SR]*(1-fade)
save('gm_wind',norm(x,.7))

# ---- stone groan: grinding slab, 2.6 s
n=int(2.6*SR); t=np.arange(n)/SR
f=55+12*np.sin(2*np.pi*.7*t)+rng.standard_normal(n).cumsum()/SR*3
ph=2*np.pi*np.cumsum(f)/SR
saw=(ph/(2*np.pi))%1*2-1
fric=(np.sin(2*np.pi*13*t)>0.2)*1.0
g=lp(saw,400)*.6*(.6+.4*fric)+bp(noise(n),80,600)*.8*(.5+.5*fric)
g+= hp(noise(n),2000)*.05*fric
g*=env(n,.6,.3,1.1)
save('gm_groan',norm(reverb(g,decay=2.0,mix=.45),.85))

# ---- deep thud of the mountain (the throat squeezes)
n=int(3*SR); t=np.arange(n)/SR
th=np.sin(2*np.pi*(38-10*t)*t)*np.exp(-t*1.4)+lp(noise(n),120)*np.exp(-t*2.2)*.8
save('gm_thud',norm(reverb(th,decay=2.4,mix=.35),.9))
