from PIL import Image, ImageDraw, ImageFont, ImageFilter
import io, math, sys
U='/mnt/user-data/uploads/hukaton/hukaton/Assets/'
ITH=ImageFont.truetype(U+'Content/UI/Typography/Fonts/Ithaca.ttf',16)
PIX=ImageFont.truetype(U+'Content/UI/Typography/Fonts/PixeloidSans.ttf',9)
SRC='/tmp/claude-tool-results/7757504b-1edd-5733-9eac-d14630a30b87/mcp-kayospace-blob-1794699017865-avtkua-2468a3ef.png'
W,H=180,150

def text(d,xy,s,font,fill,outline=None,shadow=None,anchor='la'):
    d.fontmode='1'
    x,y=xy
    if shadow: d.text((x+shadow[0],y+shadow[1]),s,font=font,fill=shadow[2],anchor=anchor)
    if outline:
        for dx in (-1,0,1):
            for dy in (-1,0,1):
                if dx or dy: d.text((x+dx,y+dy),s,font=font,fill=outline,anchor=anchor)
    d.text((x,y),s,font=font,fill=fill,anchor=anchor)

def photo(h):
    im=Image.open(SRC).convert('RGB')
    # cut the white studio background
    im=im.crop((40,10,500,512))
    sc=h/im.height
    im=im.resize((max(1,round(im.width*sc)),h),Image.LANCZOS)
    # cheap jpeg of the era
    b=io.BytesIO(); im.save(b,'JPEG',quality=38); im=Image.open(b).convert('RGB')
    px=im.load(); a=Image.new('L',im.size,0); pa=a.load()
    for y in range(im.height):
        for x in range(im.width):
            r,g,bb=px[x,y]
            pa[x,y]=0 if (r>232 and g>232 and bb>232) else 255
    im=im.convert('RGBA'); im.putalpha(a)
    return im

def burst(d,cx,cy,r1,r2,n,fill,outline):
    pts=[]
    for i in range(n*2):
        r=r1 if i%2==0 else r2
        t=i*math.pi/n-math.pi/2
        pts.append((round(cx+r*math.cos(t)),round(cy+r*math.sin(t))))
    d.polygon(pts,fill=fill,outline=outline)

def frame(k):
    im=Image.new('RGBA',(W,H))
    d=ImageDraw.Draw(im)
    # loud vertical gradient red -> orange -> yellow, with rays
    for y in range(H):
        t=y/(H-1)
        c=(255,int(30+190*t),int(20*t)) if k==0 else (255,int(200-150*t),int(20+30*(1-t)))
        d.line([(0,y),(W,y)],fill=c)
    cx,cy=56,96
    for i in range(16):
        a0=i*2*math.pi/16+(0 if k==0 else math.pi/16)
        if i%2: continue
        a1=a0+2*math.pi/16
        d.polygon([(cx,cy),(cx+300*math.cos(a0),cy+300*math.sin(a0)),(cx+300*math.cos(a1),cy+300*math.sin(a1))],fill=(255,240,90) if k==0 else (255,255,170))
    # headline band
    d.rectangle([0,0,W-1,21],fill=(200,0,0) if k==0 else (20,20,160))
    text(d,(W//2,2),'ПОЗДРАВЛЯЕМ!!!',ITH,(255,236,0) if k==0 else (255,255,255),outline=(0,0,0),anchor='ma')
    d.rectangle([0,22,W-1,33],fill=(0,0,0))
    text(d,(W//2,23),'Вы 1 000 000-й посетитель лавки!',PIX,(255,255,255),anchor='ma')
    # stock photo
    p=photo(110)
    im.alpha_composite(p,(-10,H-110))
    d=ImageDraw.Draw(im)
    # starburst prize
    burst(d,133,64,30,22,14,(255,250,40) if k==0 else (255,60,60),(120,0,0))
    text(d,(133,48),'ПРИЗ',PIX,(200,0,0) if k==0 else (255,255,255),anchor='ma')
    text(d,(133,57),'5000',ITH,(0,0,0) if k==0 else (255,240,0),anchor='ma')
    text(d,(133,71),'монет!',PIX,(200,0,0) if k==0 else (255,255,255),anchor='ma')
    # fake button
    bx0,by0,bx1,by1=92,104,175,121
    d.rectangle([bx0,by0,bx1,by1],fill=(20,150,20),outline=(0,60,0))
    d.line([(bx0+1,by0+1),(bx1-1,by0+1)],fill=(120,230,120))
    d.line([(bx0+1,by1-1),(bx1-1,by1-1)],fill=(0,90,0))
    text(d,((bx0+bx1)//2+1,by0+4),'ЗАБРАТЬ >>',PIX,(255,255,0) if k==0 else (255,255,255),outline=(0,50,0),anchor='ma')
    # hand-drawn red arrow pointing to the button, blinking
    if k==0:
        d.polygon([(84,110),(90,113),(84,116)],fill=(255,255,255),outline=(150,0,0))
        d.line([(76,113),(84,113)],fill=(150,0,0),width=2)
    # fine print
    text(d,(W-2,H-24),'*акция действует',PIX,(90,40,0),anchor='ra')
    text(d,(W-2,H-14),'до вчерашнего дня',PIX,(90,40,0),anchor='ra')
    return im

for k in (0,1):
    frame(k).save(f'ad_prize_real_{k}.png')
# preview: inside the red ad window, on the desktop, x3
win=Image.open(U+'Computer/Art/Popups/xp_window_ad_red.png').convert('RGBA')
cl=Image.open(U+'Computer/Art/Ui/xp_close.png').convert('RGBA')
def nine(src,w,h,L=8,B=4,R=8,T=26):
    out=Image.new('RGBA',(w,h)); sw,sh=src.size
    xs=[(0,L,0,L),(L,sw-R,L,w-R),(sw-R,sw,w-R,w)]; ys=[(0,T,0,T),(T,sh-B,T,h-B),(sh-B,sh,h-B,h)]
    for sx0,sx1,dx0,dx1 in xs:
        for sy0,sy1,dy0,dy1 in ys:
            if dx1>dx0 and dy1>dy0: out.paste(src.crop((sx0,sy0,sx1,sy1)).resize((dx1-dx0,dy1-dy0),Image.NEAREST),(dx0,dy0))
    return out
sheet=Image.new('RGBA',(2*(W+8+40)+20,H+30+40),(40,40,48,255))
desk=Image.open(U+'Computer/Art/Ui/pc_wallpaper.png').convert('RGBA').resize((sheet.width,sheet.height),Image.NEAREST)
sheet.alpha_composite(desk)
dd=ImageDraw.Draw(sheet)
for k in (0,1):
    w=nine(win,W+8,H+30)
    ImageDraw.Draw(w).fontmode='1'
    text(ImageDraw.Draw(w),(6,6),'Вы выиграли!!!',ITH,(255,255,255),shadow=(1,1,(80,0,0)))
    w.alpha_composite(cl,(W+8-26,3))
    w.alpha_composite(frame(k),(4,26))
    sheet.alpha_composite(w,(20+k*(W+8+40),20))
sheet=sheet.resize((sheet.width*3,sheet.height*3),Image.NEAREST)
sheet.save('preview_real.png')
