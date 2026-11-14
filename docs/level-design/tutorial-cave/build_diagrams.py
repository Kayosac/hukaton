"""Deterministic design drawings; no Unity assets are authored by this script."""
from pathlib import Path
import json, math, html, subprocess

ROOT=Path(__file__).resolve().parent
DATA=json.loads((ROOT/'layout.json').read_text())
OUT=ROOT/'figures'; OUT.mkdir(exist_ok=True)
C={'bg':'#eeeae0','floor':'#faf6e9','ink':'#303b37','muted':'#627069','edge':'#777c6d','line':'#b8bbab','water':'#b3cdd2','blue':'#3b7587','fire':'#a65e39','green':'#66764a','route':'#a5623b'}

def esc(t):return html.escape(str(t))
def doc(w,h):
 return [f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}"><title>Пещера под корнями — схема v0.2</title><rect width="100%" height="100%" fill="{C["bg"]}"/><g font-family="Arial, Helvetica, sans-serif" fill="{C["ink"]}">']
def text(s,x,y,t,size=22,fill=None,weight=400,anchor='start'):
 s.append(f'<text x="{x:.1f}" y="{y:.1f}" font-size="{size}" font-weight="{weight}" text-anchor="{anchor}" fill="{fill or C["ink"]}">{esc(t)}</text>')
def line(s,a,b,color=None,width=2,dash=None):
 s.append(f'<line x1="{a[0]:.2f}" y1="{a[1]:.2f}" x2="{b[0]:.2f}" y2="{b[1]:.2f}" stroke="{color or C["edge"]}" stroke-width="{width}"'+(f' stroke-dasharray="{dash}"' if dash else '')+'/>')
def poly(s,pts,fill,stroke=None,width=2,close=True,dash=None):
 tag='polygon' if close else 'polyline';p=' '.join(f'{x:.2f},{y:.2f}'for x,y in pts)
 s.append(f'<{tag} points="{p}" fill="{fill}" stroke="{stroke or "none"}" stroke-width="{width}" stroke-linejoin="round"'+(f' stroke-dasharray="{dash}"' if dash else '')+'/>')
def circle(s,p,r,fill,stroke=None,width=2,dash=None):
 s.append(f'<circle cx="{p[0]:.2f}" cy="{p[1]:.2f}" r="{r}" fill="{fill}" stroke="{stroke or "none"}" stroke-width="{width}"'+(f' stroke-dasharray="{dash}"' if dash else '')+'/>')
def arrow(s,a,b,color=None,width=2.5):
 color=color or C['route'];line(s,a,b,color,width);t=math.atan2(b[1]-a[1],b[0]-a[0]);l=11
 poly(s,[(b[0]-l*math.cos(t-.5),b[1]-l*math.sin(t-.5)),b,(b[0]-l*math.cos(t+.5),b[1]-l*math.sin(t+.5))],'none',color,width,False)
def badge(s,p,n):
 circle(s,p,20,C['ink']);text(s,p[0],p[1]+8,n,23,C['floor'],500,'middle')
def save(s,name):
 (OUT/(name+'.svg')).write_text(''.join(s)+'</g></svg>')


s=doc(1800,1120)
text(s,65,65,'ПЕЩЕРА ПОД КОРНЯМИ',40,weight=500)
text(s,67,108,'v0.2 · больше ходьбы, больше изгибов · 10.11.2026',22,C['muted'])
text(s,67,145,'165 м вместо 74 м. Следующая преграда скрыта до последнего подхода.',24)
line(s,(65,169),(1735,169),C['line'])
def P(x,z):return (65+x*18,985-z*18)
poly(s,[P(*p)for p in DATA['floorPolygon']],C['floor'],C['edge'],4)
poly(s,[P(*p)for p in DATA['waterPolygon']],C['water'],C['blue'],2)
for z in [32,33.5,35,36.5,38]:
 for x in [39,42,45]:line(s,P(x,z),P(x+1.5,z+.25),'#88b0bb',1.7)
text(s,*P(43,41.3),'10 м · вода',21,C['blue'],anchor='middle')
line(s,P(38,40.4),P(48,40.4),C['blue'],1.5)
for x in [38,48]:line(s,P(x,40),P(x,40.8),C['blue'],1.5)
poly(s,[P(*p)for p in DATA['route']],'none',C['route'],3.5,False,'9 7')
for i,(a,b)in enumerate(zip(DATA['route'],DATA['route'][1:])):
 if math.dist(a,b)>5:
  pa=P(a[0]+(b[0]-a[0])*.35,a[1]+(b[1]-a[1])*.35);pb=P(a[0]+(b[0]-a[0])*.62,a[1]+(b[1]-a[1])*.62);arrow(s,pa,pb,width=2.5)
line(s,P(8.1,29),P(11.9,29),C['fire'],7)
for x in [8.4,9.2,10,10.8,11.6]:line(s,P(x,28.6),P(x+.2,29.4),C['fire'],5)
text(s,*P(5,31),'корни',20,C['fire'])
for x in [22,23.4,24.8,26.2,27.6]:line(s,P(x,26.8),P(x,29.2),C['line'],1.8)
text(s,*P(25,24.1),'сухой спуск −2 м',19,C['muted'],anchor='middle')
text(s,*P(27,16),'ГЛУХИЕ ПОВОРОТЫ',23,C['muted'],anchor='middle')
text(s,*P(27,14),'сплошной камень до свода',20,C['muted'],anchor='middle')
text(s,*P(27,12),'цель не видна через соседний ход',18,C['muted'],anchor='middle')
for zone,key,col in zip(DATA['grantZones'],['О','Л','У'],[C['fire'],C['blue'],C['green']]):
 p=P(*zone['center']);circle(s,p,zone['radius']*18,'none',col,1.5,'5 6')
 poly(s,[(p[0],p[1]-14),(p[0]+14,p[1]),(p[0],p[1]+14),(p[0]-14,p[1])],col);text(s,p[0],p[1]+6,key,17,C['floor'],500,'middle')
for shore in DATA['shores']:
 x,_,z=shore['position'];p=P(x,z);poly(s,[(p[0]-6,p[1]-6),(p[0]+6,p[1]-6),(p[0]+6,p[1]+6),(p[0]-6,p[1]+6)],C['blue']);text(s,p[0],p[1]+29,shore['id'],17,C['blue'],anchor='middle')
circle(s,P(44,7),90,'none',C['line'],1.8,'7 5');circle(s,P(44,7),30.6,'none',C['green'],1.8,'4 4')
for x,z,r in [(44,7,14),(43.4,6.7,12),(44.5,6.6,13),(44.2,7.6,11)]:circle(s,P(x,z),r,'#899467',C['green'],1.5)
text(s,*P(44,1.5),'куст · площадка Ø10 м',19,anchor='middle')
text(s,*P(61,20),'ниша',17,C['muted'])
for n,p in {1:(5.5,4.5),2:(7.5,28),3:(25,31.5),4:(43,37.3),5:(52.5,23),6:(39,7)}.items():badge(s,P(*p),n)
# Designer-only sight reveal marks; these do not represent gameplay pickups.
for p,label in [((10,19),'П1'),((29,35),'П2'),((54,3),'П3')]:
 q=P(*p);circle(s,q,7,C['floor'],C['ink'],2);text(s,q[0]-16,q[1]-16,label,17,C['ink'])
text(s,*P(8,-.6),'СТАРТ',20,C['muted'],anchor='middle')
text(s,*P(1,41),'ВИД СВЕРХУ',18,C['muted']);arrow(s,P(1,35),P(1,38),C['ink'],2);text(s,*P(1,39),'Z',17,anchor='middle')
line(s,P(1,33),P(6,33),C['ink'],3)
for x in [1,6]:line(s,P(x,32.6),P(x,33.4),C['ink'],2)
text(s,*P(3.5,31.5),'5 м',18,anchor='middle')
line(s,(1280,205),(1280,990),C['line'])
notes=[
 ('Первый зигзаг',['До корней теперь есть прогулка.','Повороты прячут первый урок.','Инструкции проходят как раньше.']),
 ('Корневой затвор',['Корни видны лишь с подхода П1.','Огонь открывает проход.','Дальше снова глухой поворот.']),
 ('Сухой серпантин',['Несколько смен направления.','Спуск на 2 м закрыт камнем.','Ни воды, ни следующей карты.']),
 ('Скрытая переправа',['П2 → зал и вода раскрываются.','Лёд появляется у ближнего берега.','Один бросок → переход 10 м.']),
 ('Петляющий ход с эхом',['После воды — ещё цепочка углов.','Шуршание слышно, куста не видно.','Небольшая сухая ниша по пути.']),
 ('Грот партнёра',['П3 → куст впервые перед игроком.','Удар, беготня и семь реплик.','Затемнение → прежний урок в лавке.'])]
for i,(title,lines)in enumerate(notes):
 y=235+i*124;badge(s,(1320,y-7),i+1);text(s,1352,y,title,24,weight=500)
 for j,t in enumerate(lines):text(s,1310,y+33+j*24,t,20,C['muted'])
line(s,(65,1025),(1735,1025),C['line']);line(s,(70,1060),(115,1060),C['route'],3.5,'8 5');text(s,130,1067,'маршрут 165 м',20)
circle(s,(375,1060),7,C['floor'],C['ink'],2);text(s,395,1067,'П1–П3: последние повороты перед целью',20)
text(s,1735,1067,'Схема · обзор и размеры проверить в Unity',19,C['muted'],anchor='end')
save(s,'cave-plan')

# Proportional unfolded profile: walking passages are visibly longer than lessons.
stations=[0]
for a,b in zip(DATA['route'],DATA['route'][1:]):stations.append(stations[-1]+math.dist(a,b))
length=stations[-1];root=stations[5];near=stations[12];far=stations[13]
ramp_start=stations[8];ramp_end=stations[9];up_start=stations[18];up_end=stations[19]
DATA['section']=[[0,2],[ramp_start,2],[ramp_end,0],[up_start,0],[up_end,1],[length,1]]
(ROOT/'layout.json').write_text(json.dumps(DATA,ensure_ascii=False,indent=2)+'\n')
s=doc(1800,630);text(s,65,65,'БОЛЬШЕ ПУТИ МЕЖДУ УРОКАМИ',36,weight=500)
text(s,65,107,'v0.2 · развёрнутый профиль по длине маршрута · повороты распрямлены · 0 м = вода',22,C['muted'])
def X(dist):return 80+dist/length*1640
def Y(height):return 450-height*30
profile=[(X(a),Y(b))for a,b in DATA['section']]
# Cut water pit out of the ground, keep the floor profile readable.
profile=[(X(0),Y(2)),(X(ramp_start),Y(2)),(X(ramp_end),Y(0)),(X(near),Y(0)),(X(near),Y(-3)),(X(far),Y(-3)),(X(far),Y(0)),(X(up_start),Y(0)),(X(up_end),Y(1)),(X(length),Y(1))]
poly(s,profile+[(1720,570),(80,570)],'#d4cebd',C['edge'],2)
ceiling=[(X(0),Y(5)),(X(ramp_start),Y(5)),(X(ramp_end),Y(3)),(X(stations[10]),Y(3)),(X(stations[11]),Y(9)),(X(stations[14]),Y(9)),(X(stations[15]),Y(3)),(X(up_end),Y(4)),(X(length-6),Y(7)),(X(length),Y(7))]
poly(s,ceiling+[(1720,135),(80,135)],'#d4cebd',C['edge'],2)
poly(s,[(X(near),Y(0)),(X(far),Y(0)),(X(far),Y(-3)),(X(near),Y(-3))],C['water']);line(s,(X(near),Y(0)),(X(far),Y(0)),C['blue'],6)
text(s,(X(near)+X(far))/2,488,'10 м',21,C['blue'],anchor='middle');text(s,(X(near)+X(far))/2,518,'лёд',20,C['blue'],anchor='middle')
line(s,(X(root),Y(5)),(X(root),Y(2)),C['fire'],11)
for yy in [320,343,366]:line(s,(X(root)-15,yy-7),(X(root)+15,yy+7),C['fire'],4)
for i,pos in enumerate([0,root,(ramp_start+ramp_end)/2,(near+far)/2,stations[17],length]):badge(s,(max(105,min(1695,X(pos))),210),i+1)
text(s,X(15),425,'пол +2 м',20,C['muted'],anchor='middle');text(s,X(110),490,'пол 0 м',20,C['muted'],anchor='middle');text(s,X(length-5),467,'+1 м',20,C['muted'],anchor='middle')
text(s,X((ramp_start+ramp_end)/2),378,'−2 м',20,C['muted'],anchor='middle')
for a,b in [(0,root),(root,near),(far,length)]:
 line(s,(X(a),588),(X(b),588),C['edge'],1.5)
 text(s,(X(a)+X(b))/2,616,f'{b-a:.0f} м ходьбы',21,anchor='middle')
save(s,'cave-section')
(ROOT/'drawing-checks.json').write_text(json.dumps({'version':'0.2','routeLengthMetres':round(length,2),'previousRouteLengthMetres':73.56,'lengthMultiplier':round(length/73.56,2),'rooms':6,'waterLengthMetres':10,'waterWidthMetres':8,'unityPlaytested':False,'status':'SVG authored; PNG visual review recorded separately'},indent=2))
print('SVG diagrams written; route:',round(length,2),'m; roots',round(root,2),'water',round(near,2),round(far,2))
