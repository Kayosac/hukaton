import numpy as np, cv2, glob, json
from PIL import Image, ImageDraw
S='/home/claude/ref/swamp/sheets/'
out={}
rows=[]
for p in sorted(glob.glob(S+'*.png')):
    im=np.array(Image.open(p).convert('RGB')).astype(np.float32)
    bg=np.median(np.concatenate([im[:20].reshape(-1,3),im[-20:].reshape(-1,3)]),0)
    m=np.linalg.norm(im-bg,axis=2)>28
    px=im[m][::7]
    K=10
    _,lab,c=cv2.kmeans(px,K,None,(cv2.TERM_CRITERIA_EPS+cv2.TERM_CRITERIA_MAX_ITER,30,0.5),3,cv2.KMEANS_PP_CENTERS)
    cnt=np.bincount(lab.ravel(),minlength=K)
    order=np.argsort(c.sum(1))
    name=p.split('/')[-1][:-4]
    out[name]=[('#%02x%02x%02x'%tuple(int(v) for v in c[i]),int(cnt[i])) for i in order]
    rows.append((name,[c[i] for i in order]))
json.dump(out,open('palettes.json','w'),indent=0)
W=900;H=28*len(rows)
img=Image.new('RGB',(W,H),(30,30,30));d=ImageDraw.Draw(img)
for r,(n,cs) in enumerate(rows):
    d.text((4,r*28+8),n[:22],fill=(220,220,220))
    for i,c in enumerate(cs):
        d.rectangle([200+i*68,r*28+2,200+i*68+64,r*28+26],fill=tuple(int(v) for v in c))
img.save('palettes.png')
