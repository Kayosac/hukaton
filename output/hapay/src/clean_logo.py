from PIL import Image; import numpy as np
src=np.array(Image.open('logo_v3_src.png').convert('RGB')).astype(float)
H,W,_=src.shape
pal=np.array([[187,186,186],[15,15,15],[160,67,48],[69,97,143],[175,129,51],[97,110,64],[100,62,86],[228,186,82],[117,84,32]],float)
full=((src.reshape(-1,3)[:,None]-pal[None])**2).sum(2).argmin(1).reshape(H,W)
best=None
for cell in np.arange(5.0,5.8,0.02):
  for ox in np.arange(0,cell,1):
    # score: fraction of cell pixels agreeing with majority (sample a stripe)
    pass
cell=1536/288
nw,nh=288,192
out=np.zeros((nh,nw),int)
for j in range(nh):
  for i in range(nw):
    y0=int(j*cell+1);y1=int((j+1)*cell-1);x0=int(i*cell+1);x1=int((i+1)*cell-1)
    out[j,i]=np.bincount(full[y0:y1,x0:x1].ravel(),minlength=len(pal)).argmax()
# remove isolated single pixels (noise) except outline
from collections import Counter
o2=out.copy()
for j in range(1,nh-1):
  for i in range(1,nw-1):
    nb=[out[j-1,i],out[j+1,i],out[j,i-1],out[j,i+1]]
    if all(n!=out[j,i] for n in nb):
      o2[j,i]=Counter(nb).most_common(1)[0][0]
out=o2
# background -> transparent via flood fill from border on label 0
from collections import deque
bg=np.zeros_like(out,bool);q=deque()
for j in range(nh):
  for i in (0,nw-1): q.append((j,i))
for i in range(nw):
  for j in (0,nh-1): q.append((j,i))
while q:
  j,i=q.popleft()
  if 0<=j<nh and 0<=i<nw and not bg[j,i] and out[j,i]==0:
    bg[j,i]=True; q.extend([(j+1,i),(j-1,i),(j,i+1),(j,i-1)])
rgba=np.zeros((nh,nw,4),np.uint8); rgba[...,:3]=pal[out]; bg=(out==0); rgba[...,3]=np.where(bg,0,255)
ys,xs=np.where(~bg); rgba=rgba[ys.min()-2:ys.max()+3, xs.min()-2:xs.max()+3]
Image.fromarray(rgba).save('hapay_logo.png')
big=Image.fromarray(rgba).resize((rgba.shape[1]*4,rgba.shape[0]*4),Image.NEAREST); big.save('hapay_logo_x4.png')
# preview on grey and on dark
for name,c in (('prev_light',(220,220,220)),('prev_dark',(40,30,35))):
  b=Image.new('RGBA',big.size,c+(255,)); b.alpha_composite(big); b.convert('RGB').save(name+'.png')
print(rgba.shape)
