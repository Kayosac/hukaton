"""Deterministic design drawings; no Unity assets are authored by this script."""
from pathlib import Path
import json, math, html, subprocess

ROOT=Path(__file__).resolve().parent
DATA=json.loads((ROOT/'layout.json').read_text())
OUT=ROOT/'figures'; OUT.mkdir(exist_ok=True)
C={'bg':'#eeeae0','floor':'#faf6e9','ink':'#303b37','muted':'#627069','edge':'#777c6d','line':'#b8bbab','water':'#b3cdd2','blue':'#3b7587','fire':'#a65e39','green':'#66764a','route':'#a5623b'}

def esc(t):return html.escape(str(t))
def doc(w,h):
 return [f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}"><title>Пещера под корнями — предложение v0.1</title><rect width="100%" height="100%" fill="{C["bg"]}"/><g font-family="Arial, Helvetica, sans-serif" fill="{C["ink"]}">']
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
text(s,67,108,'План уровня · предложение v0.1 · 10.11.2026',22,C['muted'])
text(s,67,145,'Один путь, шесть пространств. Инструкции → Огонь → Лёд → Удар → гоблин → лавка.',23)
line(s,(65,169),(1735,169),C['line'])
def P(x,z):return (65+x*20,985-z*20)
floor=[P(*p) for p in DATA['floorPolygon']]
poly(s,floor,C['floor'],C['edge'],5)
poly(s,[P(*p) for p in DATA['waterPolygon']],C['water'],C['blue'],2)
for z in [25,26.5,28,29.5,31]:
 for x in [34,37,40]:line(s,P(x,z),P(x+1.5,z+.25),'#88b0bb',1.7)
text(s,*P(38,34),'10 м · вода от стены до стены',21,C['blue'],anchor='middle')
line(s,P(33,33.3),P(43,33.3),C['blue'],1.5)
for x in [33,43]:line(s,P(x,32.8),P(x,33.8),C['blue'],1.5)
poly(s,[P(*p) for p in DATA['route']],'none',C['route'],4,False,'10 8')
for i in [2,5,8,9,11,13,15]:
 a,b=DATA['route'][i:i+2]; pa=P(a[0]+(b[0]-a[0])*.42,a[1]+(b[1]-a[1])*.42);pb=P(a[0]+(b[0]-a[0])*.72,a[1]+(b[1]-a[1])*.72);arrow(s,pa,pb,width=3)
for i in range(6):
 x=7.8+i*.85;line(s,P(x,17.5),P(x+.25,18.5),C['fire'],7)
line(s,P(7.8,18),P(12.2,18),C['fire'],7)
line(s,P(12.2,18),P(18,18),C['edge'],1.5)
text(s,*P(18.5,18.4),'корни до свода',21)
text(s,*P(18.5,17),'один бросок Огня',19,C['muted'])
for x in [24,25.2,26.4,27.6,28.8]:line(s,P(x,26.8),P(x,29.2),C['line'],2)
text(s,*P(25,23),'пандус −2 м',20,C['muted'],anchor='middle')
line(s,P(18,24.5),P(23,24.5),C['edge'],8)
line(s,P(23,24.5),P(26,25.5),C['edge'],8)
text(s,*P(25.5,13.5),'КАМЕННЫЙ МАССИВ',20,C['muted'],anchor='middle')
text(s,*P(25.5,11.8),'закрывает обзор и срезы',19,C['muted'],anchor='middle')
for zone,key,col in zip(DATA['grantZones'],['О','Л','У'],[C['fire'],C['blue'],C['green']]):
 p=P(*zone['center']);circle(s,p,zone['radius']*20,'none',col,1.7,'5 6')
 poly(s,[(p[0],p[1]-16),(p[0]+16,p[1]),(p[0],p[1]+16),(p[0]-16,p[1])],col)
 text(s,p[0],p[1]+7,key,19,C['floor'],500,'middle')
for shore in DATA['shores']:
 x,_,z=shore['position'];p=P(x,z);poly(s,[(p[0]-8,p[1]-8),(p[0]+8,p[1]-8),(p[0]+8,p[1]+8),(p[0]-8,p[1]+8)],C['blue'])
 text(s,p[0],p[1]+32,shore['id'],18,C['blue'],anchor='middle')
circle(s,P(44,8.5),100,'none',C['line'],2,'8 6')
circle(s,P(44,8.5),34,'none',C['green'],2,'4 4')
for x,z,r in [(44,8.5,15),(43.4,8.2,13),(44.5,8.1,14),(44.2,9.1,12)]:circle(s,P(x,z),r,'#899467',C['green'],1.5)
text(s,*P(44,3.1),'куст · световой колодец',20,anchor='middle')
text(s,*P(44,1.7),'свободная площадка Ø10 м',18,C['muted'],anchor='middle')
text(s,*P(54,17.1),'ниша',18,C['muted'],anchor='middle')
badge_positions={1:(5.5,5.3),2:(6.4,19),3:(19,30.5),4:(38,29.4),5:(52.9,24.7),6:(40,11.3)}
for n,p in badge_positions.items():badge(s,P(*p),n)
text(s,*P(7,1.2),'СТАРТ',20,C['muted'],anchor='middle')
text(s,*P(1,36.5),'ВИД СВЕРХУ',18,C['muted'])
arrow(s,P(1,31.5),P(1,34),C['ink'],2);text(s,*P(1,35),'Z',17,anchor='middle')
line(s,P(1,29.7),P(6,29.7),C['ink'],3)
for x in [1,6]:line(s,P(x,29.3),P(x,30.1),C['ink'],2)
text(s,*P(3.5,28.5),'5 м',18,anchor='middle')
line(s,(1280,205),(1280,975),C['line'])
notes=[
 ('Пробуждение',['Тесно, сухо, безопасно.','Три инструкции уже в руке.','Два броска → R.']),
 ('Корневой затвор',['Первый понятный барьер.','Огонь открывает проход.','За корнями меняется масштаб.']),
 ('Уступ над водой',['Высокий зал раскрывается сразу.','Дальше видно светлый берег.','Спуск без обязательного прыжка.']),
 ('Ледяная переправа',['Лёд выдаётся на сухом берегу.','Один бросок → весь участок замёрз.','Падение → ближайший берег A/B.']),
 ('Ход с эхом',['Поворот прячет финальный грот.','Короткая ниша для любопытства.','Шуршание направляет к кусту.']),
 ('Непрошеный партнёр',['Удар по кусту → гоблин.','≈5 с беготни → семь реплик.','E → затемнение → лавка.'])]
for i,(title,lines) in enumerate(notes):
 y=235+i*124;badge(s,(1320,y-7),i+1);text(s,1352,y,title,24,weight=500)
 for j,t in enumerate(lines):text(s,1310,y+33+j*24,t,20,C['muted'])
line(s,(65,1025),(1735,1025),C['line'])
line(s,(70,1062),(115,1062),C['route'],4,'8 5');text(s,130,1069,'путь',20)
circle(s,(260,1062),14,'none',C['blue'],1.5,'4 3');text(s,285,1069,'зона выдачи карты · R 3,5 м',20)
text(s,710,1069,'О / Л / У — Огонь / Лёд / Удар',20)
text(s,1735,1069,'Размеры стартовые · Unity-плейтест впереди',19,C['muted'],anchor='end')
save(s,'cave-plan')

s=doc(1800,630)
text(s,65,65,'КАК МЕНЯЕТСЯ ПРОСТРАНСТВО',36,weight=500)
text(s,65,107,'Развёрнутый профиль · горизонтальные расстояния условны · 0 м = поверхность воды',22,C['muted'])
X=[100,370,620,850,1090,1340,1610]
# Continuous floor, exaggerated water pit, and ceiling silhouette.
ground=[(75,390),(620,390),(810,450),(890,450),(890,545),(1115,545),(1115,450),(1340,450),(1450,420),(1720,420)]
poly(s,ground+[(1720,570),(75,570)],'#d4cebd',C['edge'],2)
poly(s,[(75,300),(370,294),(495,290),(620,180),(850,180),(1120,180),(1260,360),(1360,360),(1470,240),(1720,240),(1720,135),(75,135)],'#d4cebd',C['edge'],2)
poly(s,[(890,450),(1115,450),(1115,545),(890,545)],C['water'],None)
line(s,(890,448),(1115,448),C['blue'],7)
text(s,1002,490,'10 м',23,C['blue'],anchor='middle')
text(s,1002,522,'вода → лёд',21,C['blue'],anchor='middle')
line(s,(370,294),(370,390),C['fire'],14)
for yy in [310,335,360,380]:line(s,(352,yy-8),(388,yy+8),C['fire'],5)
for a,b in [((120,374),(340,374)),((405,374),(590,374)),((650,385),(790,428)),((1125,433),(1320,433)),((1380,432),(1550,404))]:arrow(s,a,b)
circle(s,(1610,401),18,C['green']);circle(s,(1590,407),15,C['green']);circle(s,(1627,405),16,C['green'])
for i,x in enumerate([150,370,620,1000,1320,1610]):
 badge(s,(x,192 if i in [0,1,4] else 285),i+1)
for x,t,y in [(150,'+2 м',427),(620,'+2 м',427),(1190,'0 м',486),(1610,'+1 м',459)]:text(s,x,y,t,22,C['muted'],anchor='middle')
for x,t in [(150,'защищённый карман'),(410,'тесный проход'),(740,'высокий зал'),(1260,'снова теснее'),(1580,'тёплый грот')]:text(s,x,604,t,21,anchor='middle')
text(s,850,220,'свод +9 м',22,C['muted'],anchor='middle')
text(s,690,347,'пандус 7 м',20,C['muted'])
save(s,'cave-section')

length=sum(math.dist(a,b) for a,b in zip(DATA['route'],DATA['route'][1:]))
(ROOT/'drawing-checks.json').write_text(json.dumps({'routeLengthMetres':round(length,2),'rooms':len(DATA['rooms']),'waterLengthMetres':10,'waterWidthMetres':8,'grantZones':3,'unityPlaytested':False,'status':'SVG authored; PNG visual review recorded separately'},indent=2))
print('SVG diagrams written; route length:',round(length,2),'m')
