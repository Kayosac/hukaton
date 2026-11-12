"""Final fit: chamfered 12-gon with per-vertex radii, refined from fit.json and written back to fit.json."""
import numpy as np, json, time
from PIL import Image, ImageDraw
from scipy import ndimage, optimize
from scipy.spatial import ConvexHull
DS=2
src=open('fit.py').read().split("N=12")[0].replace("DS=4","DS=2")
exec(src)
N=12; CZ=34.0; CR=46.0
f=json.load(open('fit.json'))
rho=np.radians(f['rho_deg'])
ang=np.pi/2+np.pi/N+rho+np.arange(N)*2*np.pi/N
shape=M['000'][0].shape
def raster(pts):
    img=Image.new('L',(shape[1],shape[0]),0); ImageDraw.Draw(img).polygon([tuple(p) for p in pts],fill=1)
    return np.asarray(img).astype(bool)
def geom(radii,T,theta,cx,cy,back):
    V=np.stack([radii*np.cos(ang),radii*np.sin(ang)],1)
    k1=1-CR/radii.mean()
    def proj(P,z):
        X=P[:,0]*np.cos(theta)+z*np.sin(theta); return np.stack([cx+X, cy-P[:,1]],1)
    pts=np.concatenate([proj(V,T/2-CZ),proj(V,-(T/2-CZ)),proj(V*k1,T/2),proj(V*k1,-T/2)])/DS
    h=pts[ConvexHull(pts).vertices]
    sil=raster(h)
    face=raster(proj(V,-(T/2-CZ) if back else (T/2-CZ))/DS)
    return sil, sil&~face
def iou(a,b): return (a&b).sum()/max(1,(a|b).sum())
def unpack(p): return p[:N], p[N], p[N+1], p[N+2], p[N+3], p[N+4], p[N+5], p[N+6]
def loss(p):
    radii,T,a0,x0,y0,a1,x1,y1=unpack(p)
    s0,b0=geom(radii,T,a0,x0,y0,False); s1,b1=geom(radii,T,a1,x1,y1,True)
    return -(iou(s0,M['000'][0])+iou(s1,M['180'][0])+0.25*(iou(b0,M['000'][1])+iou(b1,M['180'][1])))
p0=np.concatenate([np.array(f['radii_px']),[f['T_px'],np.radians(f['theta000_deg']),f['c000'][0],f['c000'][1],np.radians(f['theta180_deg']),f['c180'][0],f['c180'][1]]])
t=time.time(); print('start',-loss(p0))
best=p0; bl=loss(p0)
for it in range(40):
    r=optimize.minimize(loss,best,method='Powell',options={'maxiter':4,'xtol':1e-2,'ftol':1e-6})
    if r.fun<bl: best,bl=r.x,r.fun
    print('pass',it,-bl,round(time.time()-t))
    if time.time()-t>150: break
radii,T,a0,x0,y0,a1,x1,y1=unpack(best)
s0,b0=geom(radii,T,a0,x0,y0,False); s1,b1=geom(radii,T,a1,x1,y1,True)
res=dict(N=N,rho_deg=float(np.degrees(rho)),radii_px=[float(v) for v in radii],T_px=float(T),chamfer_r=CR,chamfer_z=CZ,
  theta000_deg=float(np.degrees(a0)),c000=[float(x0),float(y0)],theta180_deg=float(np.degrees(a1)),c180=[float(x1),float(y1)],
  iou_sil000=float(iou(s0,M['000'][0])),iou_sil180=float(iou(s1,M['180'][0])),iou_band000=float(iou(b0,M['000'][1])),iou_band180=float(iou(b1,M['180'][1])))
print(json.dumps({k:v for k,v in res.items() if k!='radii_px'},indent=1)); json.dump(res,open('fit.json','w'),indent=1)
