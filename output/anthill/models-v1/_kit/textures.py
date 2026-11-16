"""Anthill («Муравейник») shared texture library — pixel-painted, point-sampled, PSX like ForestArt.
Run: python3 textures.py -> tex/*.png + tex/regions.json + tex/overview.png"""
import json, math, os
import numpy as np, cv2
from PIL import Image, ImageDraw
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'tex'); os.makedirs(OUT, exist_ok=True)

def hexes(*h): return np.array([[int(x[i:i+2],16) for i in (1,3,5)] for x in h], np.float32)
# palettes dark->light, from the anthill concept sheets (a bit deeper than the lit concept renders)
P_EARTH = hexes('#3f2420','#4f2c26','#5e352d','#6c3e33','#7b483a','#8c5443')
P_STRATA= hexes('#8a5a3c','#9b6a44','#a8794d')          # ochre strata streaks
P_OLIVE = hexes('#5c5032','#6d6038','#7d6f40')          # dull olive patches
P_FLOOR = hexes('#3a231f','#482b25','#55332b','#623c31','#704537','#7d4f3e')
P_ROOT  = hexes('#3c2621','#4c302a','#5c3a31','#6c4538','#7c5040','#8d5d48')
P_WOOD  = hexes('#2f211b','#432f25','#55392c','#664534','#76523d','#886048')
P_CLOTH = hexes('#3e1d1e','#4f2526','#5f2e2e','#6f3737','#804342')
P_ROPE  = hexes('#4f3a2c','#665039','#7d6447','#937856','#a98d68')
P_EGG   = hexes('#8a7660','#a08a70','#b29b7f','#c1aa8c','#cfb99a','#dcc8a8')
P_SEED  = hexes('#5a3b25','#6f4a2d','#845a35','#99693d','#ad7a48')
P_SHELL = hexes('#3f241b','#52301f','#653c26','#784830','#8e5a3b')
P_SHELLIN=hexes('#7a4c30','#8f5c3a','#a26c45','#b37b51')
P_PAPER = hexes('#5e4430','#74553a','#8a6744','#9e7850','#b0895c')
P_LEAF  = hexes('#3f3a20','#51492a','#625833','#74683c','#867846','#988851')
P_DRYLEAF=[hexes('#6a3b22','#844a2a','#9c5c33','#b46f3f'), hexes('#5d2e24','#74392b','#8a4633','#a0553d'), hexes('#544b2a','#685d33','#7d703d','#928449')]
P_RESIN = hexes('#5a3018','#74401e','#8e5224','#a8662c','#c07c38','#d39448')
P_IRON  = hexes('#2e1f19','#40291f','#533426','#66412d','#7a5038')
P_CAPM  = hexes('#2c3a33','#38483f','#45584c','#53685a','#627867')   # mint-teal tall cap
P_CAPY  = hexes('#5a4a26','#6e5c2e','#837037','#988442','#ad984e')   # yellow-olive cluster cap
P_GILL  = hexes('#6f8f6a','#8fb184','#acd09c','#c6e8b2','#def7c8')   # glowing gills
P_STEM  = hexes('#5a3f33','#6e4f40','#835f4d','#97705b','#a98168')
P_SUB   = hexes('#2a1a17','#36221d','#432a23','#51332a','#5f3d31')
P_MYC   = hexes('#c89a72','#e1b88d','#f2d3aa')

def pnoise(h,w,cells,seed,octaves=4,aniso=(1,1)):
    r=np.random.default_rng(seed); acc=np.zeros((h,w),np.float32); amp=1.; tot=0
    for o in range(octaves):
        cy=max(1,int(cells*aniso[1]*2**o)); cx=max(1,int(cells*aniso[0]*2**o))
        g=np.tile(r.random((cy,cx)).astype(np.float32),(3,3))
        acc+=cv2.resize(g,(w*3,h*3),interpolation=cv2.INTER_CUBIC)[h:2*h,w:2*w]*amp; tot+=amp; amp*=.5
    acc/=tot; return (acc-acc.min())/(acc.max()-acc.min()+1e-6)
def warp(img,dx,dy):
    h,w=img.shape[:2]; X,Y=np.meshgrid(np.arange(w,dtype=np.float32),np.arange(h,dtype=np.float32))
    return cv2.remap(img,(X+dx)%w,(Y+dy)%h,cv2.INTER_LINEAR,borderMode=cv2.BORDER_WRAP)
def quant(v,pal): return pal[np.clip((v*len(pal)).astype(int),0,len(pal)-1)]
def bm(base,over,mask): return np.where(mask[...,None],over,base)
def up2(img): return cv2.resize(img,(img.shape[1]*2,img.shape[0]*2),interpolation=cv2.INTER_NEAREST)
def save(name,rgb,alpha=None):
    rgb=np.clip(rgb,0,255).astype(np.uint8)
    if alpha is not None: Image.fromarray(np.dstack([rgb,(np.clip(alpha,0,1)*255).astype(np.uint8)]),'RGBA').save(os.path.join(OUT,name))
    else: Image.fromarray(rgb,'RGB').save(os.path.join(OUT,name))
def mask_q(v,cover): return v>np.quantile(v,1-cover)

# ------------------------------------------------ tiling
def earth(size=128,seed=3):
    """Clay wall: faceted red-brown blotches, wavy near-horizontal ochre strata (along U), olive patches."""
    h=w=size
    v=pnoise(h,w,4,seed,4,aniso=(1.5,.8)); dx=(pnoise(h,w,2,seed+1,2)-.5)*8; v=warp(v,dx,dx*.3)
    img=quant(v*.98,P_EARTH)
    s=pnoise(h,w,2,seed+2,3,aniso=(.35,3.5)); s=warp(s,0,(pnoise(h,w,2,seed+3,2)-.5)*14)
    strata=(np.abs(s-.5)<.035)&(pnoise(h,w,6,seed+4,2)>.5)
    img=bm(img,quant(pnoise(h,w,8,seed+5,2)*.6,P_STRATA),strata)
    ol=mask_q(pnoise(h,w,5,seed+6,3),.035); img=bm(img,quant(pnoise(h,w,8,seed+7,2)*.6,P_OLIVE),ol)
    r=np.random.default_rng(seed)
    for _ in range(50):
        x=r.integers(0,w); y=r.integers(0,h); L=r.integers(2,7)
        for k in range(L): img[(y+k//3)%h,(x+k)%w]=P_EARTH[0]
    return up2(img)
def floor(size=128,seed=13):
    h=w=size
    v=pnoise(h,w,5,seed,4); dx=(pnoise(h,w,2,seed+1,2)-.5)*6; v=warp(v,dx,-dx)
    img=quant(v*.98,P_FLOOR)
    worn=mask_q(pnoise(h,w,3,seed+2,3),.18); img=bm(img,quant(pnoise(h,w,8,seed+3,2)*.4+.55,P_FLOOR),worn)
    
    r=np.random.default_rng(seed)
    for _ in range(120):
        x=r.integers(0,w); y=r.integers(0,h); img[y,x]=P_FLOOR[0] if r.random()<.7 else P_FLOOR[-1]
    return up2(img)
def root(size=128,seed=23):
    """Root bark, grain along V; long ochre/olive streaks."""
    h=w=size
    g=pnoise(h,w,2,seed,4,aniso=(5,.5)); g=warp(g,(pnoise(h,w,2,seed+1,2)-.5)*8,0)
    img=quant(g*.75+.2,P_ROOT)
    crack=np.abs(pnoise(h,w,2,seed+2,3,aniso=(6,.4))-.5)<.05; img=bm(img,P_ROOT[0],crack)
    st=mask_q(pnoise(h,w,3,seed+3,3,aniso=(4,.3)),.12); img=bm(img,quant(pnoise(h,w,8,seed+4,2)*.99,P_STRATA[:2]),st)
    ol=mask_q(pnoise(h,w,4,seed+5,3,aniso=(3,.5)),.10); img=bm(img,P_OLIVE[1],ol)
    return up2(img)
def wood(size=128,seed=33,boards=5):
    """Beams/boards, grain along V, dark seams, olive damp patches."""
    h=w=size; img=np.zeros((h,w,3),np.float32); r=np.random.default_rng(seed)
    e=np.concatenate([[0],np.sort(r.choice(np.arange(6,w-6),boards-1,replace=False)),[w]])
    grain=pnoise(h,w,2,seed+1,3,aniso=(6,.4))
    for i in range(len(e)-1):
        a,b=e[i],e[i+1]; img[:,a:b]=quant(np.clip(grain[:,a:b]*.55+.3+r.uniform(-.15,.15),0,.99),P_WOOD[1:])
        img[:,a]=P_WOOD[0]
        if r.random()<.6:
            ky=r.integers(4,h-4); cv2.circle(img,(int((a+b)/2),int(ky)),2,P_WOOD[0].tolist(),-1)
    ol=mask_q(pnoise(h,w,3,seed+5,3),.10); img=bm(img,quant(pnoise(h,w,8,seed+6,2)*.99,P_OLIVE),ol)
    return up2(img)
def cloth(size=64,seed=43):
    h=w=size; v=pnoise(h,w,3,seed,3); img=quant(v*.9+.05,P_CLOTH)
    yy,xx=np.mgrid[0:h,0:w]; weave=((xx+yy)%4==0); img=bm(img,img*0.9,weave)
    fold=np.abs(pnoise(h,w,2,seed+1,2,aniso=(.5,2))-.5)<.04; img=bm(img,P_CLOTH[0],fold)
    return up2(img)

# ------------------------------------------------ Props atlas 512 (painted at 256 then up2)
REG={}; A=np.zeros((256,256,3),np.float32); E=np.zeros((256,256),np.float32)
def put(name,x,y,w,h,img,em=None):
    A[y:y+h,x:x+w]=img
    if em is not None: E[y:y+h,x:x+w]=em
    REG[name]=[x*2,y*2,w*2,h*2]
def fill(w,h,pal,seed,cells=4,aniso=(1,1),bias=0.):
    return quant(np.clip(pnoise(h,w,cells,seed,3,aniso)*.9+.05+bias,0,.99),pal)
def strands(w,h,pal,seed,horizontal=True):
    img=fill(w,h,pal,seed,3,(4,.5) if not horizontal else (.5,4))
    for i in range(0,h if horizontal else w,3):
        if horizontal: img[i]=pal[0]
        else: img[:,i]=pal[0]
    return img
def end_grain(s,seed):
    yy,xx=np.mgrid[0:s,0:s]-s/2+.5; d=np.sqrt(xx**2+yy**2)/(s/2)
    img=quant(np.clip((np.sin(d*14+pnoise(s,s,3,seed,2)*3)*.5+.5)*.5+.35,0,.99),P_WOOD[2:]); img[d>.9]=P_WOOD[1]; return img
put('end_grain',0,0,32,32,end_grain(32,1))
put('root_end',32,0,32,32,quant(np.clip(end_grain(32,2)[...,0]/255,0,.99),P_ROOT[2:]) if False else end_grain(32,2)*np.array([1.05,.85,.8]))
put('rope',64,0,64,16,strands(64,16,P_ROPE,3,horizontal=False)[:, ::1])
# rope with diagonal twist
rp=fill(64,16,P_ROPE,4,3,(3,.5)); yy,xx=np.mgrid[0:16,0:64]; rp=bm(rp,P_ROPE[0],((xx+yy)%6==0)); put('rope',64,0,64,16,rp)
put('iron',64,16,32,16,fill(32,16,P_IRON,5,4))
put('dark',96,16,32,16,np.tile(np.array([24,14,12],np.float32),(16,32,1)))
put('cloth',128,0,64,32,np.tile(cloth(16,6),(1,2,1)))
put('egg',192,0,64,32,fill(64,32,P_EGG,7,3,(.8,1.2),.05))
sd=fill(64,32,P_SEED,8,3,(4,.5)); 
for i in range(0,64,9): sd[:,i]=P_SEED[0]
put('seed',0,32,64,32,sd)
put('shell_out',64,32,64,32,fill(64,32,P_SHELL,9,4,(1,2)))
put('shell_in',128,32,64,32,fill(64,32,P_SHELLIN,10,3))
pp=fill(64,32,P_PAPER,11,4); fl=np.abs(pnoise(32,64,2,12,2,(.5,2))-.5)<.05; pp=bm(pp,P_PAPER[0],fl); put('paper',192,32,64,32,pp)
# leaves (opaque, vein down the middle of the region along V)
def leaf(w,h,pal,seed):
    img=fill(w,h,pal,seed,3,(1,1)); img[:,w//2-1:w//2+1]=pal[-1]*0.96
    for k in range(4,h,8):
        for t in range(w//2-1):
            yy=min(h-1,k+t//3)
            img[yy,w//2-2-t]=pal[-2]; img[yy,w//2+1+t]=pal[-2]
    return img
put('leaf_green',0,64,32,64,leaf(32,64,P_LEAF,13))
put('leaf_dry_0',32,64,32,64,leaf(32,64,P_DRYLEAF[0],14))
put('leaf_dry_1',64,64,32,64,leaf(32,64,P_DRYLEAF[1],15))
put('leaf_dry_2',96,64,32,64,leaf(32,64,P_DRYLEAF[2],16))
rs=fill(64,64,P_RESIN,17,3,(.5,3),.05); put('resin',128,64,64,64,rs)
put('substrate',192,64,64,64,(lambda s: bm(s,quant(pnoise(64,64,6,19,2)*.99,P_MYC),np.abs(pnoise(64,64,3,20,3)-.5)<.03))(fill(64,64,P_SUB,18,5)),
    em=(np.abs(pnoise(64,64,3,20,3)-.5)<.03)*0.55)
cm=fill(64,32,P_CAPM,21,3); sp=pnoise(32,64,10,22,1)>.86; cm=bm(cm,hexes('#b7a46a')[0],sp); put('cap_mint',0,128,64,32,cm)
put('cap_yellow',64,128,64,32,fill(64,32,P_CAPY,23,3))
g=fill(64,32,P_GILL,24,3,(4,.5),.15)
for i in range(0,64,4): g[:,i]=P_GILL[1]
put('gill_glow',128,128,64,32,g,em=np.ones((32,64))*.9)
put('stem',192,128,64,32,fill(64,32,P_STEM,25,3,(.6,2)))
put('stem_glow',192,160,64,32,quant(np.clip(np.linspace(0,1,32)[:,None]*np.ones((1,64))*.9,0,.99),np.concatenate([P_STEM[2:],P_GILL[1:4]])),em=np.clip(np.linspace(-.2,1,32)[:,None]*np.ones((1,64)),0,1)*.6)
put('earth_props',0,160,64,64,earth(64,26)[::2,::2]); put('wood_props',64,160,64,64,wood(64,27)[::2,::2])
put('root_props',128,160,64,64,root(64,28)[::2,::2])

P_GILLY=hexes('#8a8a48','#a9a752','#c6c25e','#ddd86e','#efe98a')
gy=fill(64,32,P_GILLY,29,3,(4,.5),.15)
for i in range(0,64,4): gy[:,i]=P_GILLY[1]
put('gill_glow_yellow',0,224,64,32,gy,em=np.ones((32,64))*.9)

def main():
    save('T_Ant_Earth.png',earth()); save('T_Ant_Floor.png',floor()); save('T_Ant_Root.png',root()); save('T_Ant_Wood.png',wood()); save('T_Ant_Cloth.png',cloth())
    save('T_Ant_Props.png',up2(A)); save('T_Ant_Props_Emission.png',up2(np.repeat(E[...,None],3,2)*255))
    json.dump({'T_Ant_Props.png':{'size':512,'regions':REG}},open(os.path.join(OUT,'regions.json'),'w'),indent=1)
    ims=[Image.open(os.path.join(OUT,n)).convert('RGB').resize((256,256),Image.NEAREST) for n in ['T_Ant_Earth.png','T_Ant_Floor.png','T_Ant_Root.png','T_Ant_Wood.png','T_Ant_Cloth.png']]
    ov=Image.new('RGB',(256*5+512*2,512),(30,30,30))
    for i,im in enumerate(ims): ov.paste(im,(i*256,0))
    pa=Image.open(os.path.join(OUT,'T_Ant_Props.png')).convert('RGB'); ov.paste(pa,(1280,0)); ov.paste(Image.open(os.path.join(OUT,'T_Ant_Props_Emission.png')).convert('RGB'),(1792,0))
    d=ImageDraw.Draw(pa)
    ov.save(os.path.join(OUT,'overview.png'))
main()
