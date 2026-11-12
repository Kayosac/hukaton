"""First pass: regular chamfer-less 12-gon fitted to Coin_Y_000 / Coin_Y_180 (kept for reference; writes fit_v1.json).
fit_silhouette_chamfer.py reuses its loading and mask code."""
import numpy as np, json, os
from PIL import Image, ImageDraw
from scipy import ndimage, optimize
im=np.asarray(Image.open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'..','..','hukaton','Assets','Content','UI','ShopHUD','HangoverCoinAtlas.png')).convert('RGBA')).astype(float)
cells={'000':im[0:1254,0:1254],'180':im[1254:2508,0:1254]}
DS=4  # work at 1/4 res for speed
def masks(c):
    a=c[...,3]>128
    r,g,b=c[...,0],c[...,1],c[...,2]
    brown=(r<125)&(g<75)&(r>40)&(r>g*1.35)&a
    lab,n=ndimage.label(brown)
    # keep components touching the silhouette border
    border=a & ~ndimage.binary_erosion(a,iterations=3)
    keep=np.zeros_like(brown)
    for i in range(1,n+1):
        comp=lab==i
        if comp.sum()>2000 and (comp&border).sum()>50: keep|=comp
    keep=ndimage.binary_closing(keep,iterations=6)&a
    return a[::DS,::DS], keep[::DS,::DS]
M={k:masks(c) for k,c in cells.items()}
for k,(a,b) in M.items(): print(k,'sil',a.sum(),'band',b.sum())
Image.fromarray((np.stack([M['000'][0],M['000'][1],M['000'][1]],-1)*255).astype(np.uint8)).save('band000.png')
Image.fromarray((np.stack([M['180'][0],M['180'][1],M['180'][1]],-1)*255).astype(np.uint8)).save('band180.png')

N=12
def poly(apo, rho):
    R=apo/np.cos(np.pi/N)
    ang=np.pi/2+np.pi/N+rho+np.arange(N)*2*np.pi/N   # rho=0 -> flat top
    return np.stack([R*np.cos(ang),R*np.sin(ang)],1)
def raster(pts,shape):
    img=Image.new('L',(shape[1],shape[0]),0); ImageDraw.Draw(img).polygon([tuple(p) for p in pts],fill=1)
    return np.asarray(img).astype(bool)
def hull(pts):
    from scipy.spatial import ConvexHull
    h=ConvexHull(pts); return pts[h.vertices]
def render(apo,rho,T,theta,cx,cy,back=False,shape=None,s=1.0):
    shape=shape or M["000"][0].shape
    P=poly(apo,rho)
    zf, zb = T/2, -T/2
    def proj(x,y,z):
        X=x*np.cos(theta)+z*np.sin(theta)
        return np.stack([cx+X, cy-y],1)
    F=proj(P[:,0],P[:,1],np.full(N,zf)); B=proj(P[:,0],P[:,1],np.full(N,zb))
    sil=raster(hull(np.concatenate([F,B])),shape)
    face=raster(B if back else F,shape)
    return sil, sil&~face
def iou(a,b): return (a&b).sum()/max(1,(a|b).sum())
def loss(p):
    apo,rho,T,th0,cx0,cy0,th1,cx1,cy1=p
    s0,b0=render(apo,rho,T,th0,cx0,cy0)
    s1,b1=render(apo,rho,T,th1,cx1,cy1,back=True)
    return -(iou(s0,M['000'][0])+iou(s1,M['180'][0])+0.6*iou(b0,M['000'][1])+0.6*iou(b1,M['180'][1]))
q=1254/DS
x0=[1153/2/DS,0.0,180/DS,np.radians(-32),620/DS,631/DS,np.radians(180+32),618/DS,631/DS]
best=None
for rho0 in (0.0,0.05,-0.05):
    for T0 in (150/DS,200/DS,260/DS):
        x=list(x0); x[1]=rho0; x[2]=T0
        r=optimize.minimize(loss,x,method='Nelder-Mead',options={'maxiter':900,'xatol':1e-3,'fatol':1e-5})
        if best is None or r.fun<best.fun: best=r
        print('start',rho0,T0,'->',round(-r.fun,4))
p=best.x
apo,rho,T,th0,cx0,cy0,th1,cx1,cy1=p
s0,b0=render(*p[:6]); s1,b1=render(apo,rho,T,th1,cx1,cy1,back=True)
res=dict(apothem_px=apo*DS,diameter_px=2*apo*DS,rho_deg=float(np.degrees(rho)),T_px=T*DS,T_over_D=T/(2*apo),
         theta000_deg=float(np.degrees(th0)),c000=[cx0*DS,cy0*DS],theta180_deg=float(np.degrees(th1)),c180=[cx1*DS,cy1*DS],
         iou_sil000=iou(s0,M['000'][0]),iou_sil180=iou(s1,M['180'][0]),iou_band000=iou(b0,M['000'][1]),iou_band180=iou(b1,M['180'][1]))
print(json.dumps(res,indent=1,default=float)); json.dump(res,open('fit_v1.json','w'),indent=1,default=float)
# overlay
def ov(c,s,b,name):
    a,bm=M[name]
    img=np.zeros(a.shape+(3,),np.uint8)
    img[a]=[90,90,90]; img[bm]=[150,90,40]
    e=s^ndimage.binary_erosion(s); img[e]=[255,0,0]
    eb=b^ndimage.binary_erosion(b); img[eb]=[0,255,255]
    return img
Image.fromarray(np.concatenate([ov(None,s0,b0,'000'),ov(None,s1,b1,'180')],1)).save('fitov.png')
