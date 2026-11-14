"""Rebuild the eight technical diagrams; map SVGs are editable source masters."""
from pathlib import Path
import math,subprocess,shutil,os
from reportlab.graphics.shapes import Drawing,Rect,Line,Polygon,PolyLine,Circle,String,Path as GraphicPath
from reportlab.graphics import renderPDF,renderSVG
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont

HERE=Path(__file__).resolve().parents[1];ROOT=HERE.parents[2];OUT=HERE/'figures';TMP=ROOT/'tmp/old-apiary-docs'
TMP.mkdir(parents=True,exist_ok=True)
FONT=next(p for p in [Path(os.environ.get('DOC_FONT','/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf')),Path.home()/'.cache/codex-runtimes/codex-primary-runtime/dependencies/native/poppler/poppler/fonts/DejaVuSans.ttf'] if p.is_file())
PDFTOPPM=shutil.which('pdftoppm') or str(Path.home()/'.cache/codex-runtimes/codex-primary-runtime/dependencies/bin/override/pdftoppm')
pdfmetrics.registerFont(TTFont('Doc',str(FONT)))
from reportlab.lib.colors import HexColor
C={k:HexColor(v)for k,v in {'ink':'#24302f','muted':'#596c68','green':'#dfe9dd','water':'#b6cfdf','blue':'#36759e','earth':'#eae2d2','orange':'#ad5d30','purple':'#73568e','red':'#a44742','line':'#94a6a0','white':'#ffffff'}.items()}

def new(w=720,h=360):
 d=Drawing(w,h);d.add(Rect(0,0,w,h,fillColor=C['white'],strokeColor=None));return d
def line(d,x1,y1,x2,y2,key='line',width=1,dash=None):d.add(Line(x1,y1,x2,y2,strokeColor=C[key],strokeWidth=width,strokeDashArray=dash))
def text(d,x,y,s,size=12,key='ink',anchor='start'):
 d.add(String(x,y,s,fontName='Doc',fontSize=size,fillColor=C[key],textAnchor=anchor))
def wrap(d,x,y,s,w,size=11,key='ink',leading=16):
 words=s.split();lines=[];row=''
 for z in words:
  test=(row+' '+z).strip()
  if pdfmetrics.stringWidth(test,'Doc',size)>w and row:lines.append(row);row=z
  else:row=test
 if row:lines.append(row)
 for i,s in enumerate(lines):text(d,x,y-i*leading,s,size,key)
 return y-len(lines)*leading
def box(d,x,y,w,h,title,sub='',fill='green'):
 d.add(Rect(x,y,w,h,fillColor=C[fill],strokeColor=C['line'],strokeWidth=.8))
 text(d,x+10,y+h-21,title,12)
 if sub:wrap(d,x+10,y+h-39,sub,w-20,10.5,leading=14)
def arrow(d,x1,y1,x2,y2,key='ink',width=1.3,dash=None):
 line(d,x1,y1,x2,y2,key,width,dash);a=math.atan2(y2-y1,x2-x1);l=7
 d.add(PolyLine([x2-l*math.cos(a-.5),y2-l*math.sin(a-.5),x2,y2,x2-l*math.cos(a+.5),y2-l*math.sin(a+.5)],strokeColor=C[key],strokeWidth=width,fillColor=None))
def dimension(d,x1,y1,x2,y2,label):
 line(d,x1,y1,x2,y2,'muted',.8)
 if abs(x1-x2)<.1:
  line(d,x1-4,y1,x1+4,y1,'muted');line(d,x2-4,y2,x2+4,y2,'muted');text(d,x1+8,(y1+y2)/2,label,11)
 else:
  line(d,x1,y1-4,x1,y1+4,'muted');line(d,x2,y2-4,x2,y2+4,'muted');text(d,(x1+x2)/2,y1+8,label,11,anchor='middle')
def person(d,x,y,scale=1):
 d.add(Circle(x,y+15*scale,3*scale,fillColor=C['ink'],strokeColor=None));line(d,x,y+12*scale,x,y+5*scale,'ink',1.7);line(d,x,y+5*scale,x-4*scale,y,'ink',1.4);line(d,x,y+5*scale,x+4*scale,y,'ink',1.4)
def diamond(d,x,y,key='orange'):
 d.add(Polygon([x,y+5,x+5,y,x,y-5,x-5,y],fillColor=C[key],strokeColor=None))
def finish(d,name):
 renderPDF.drawToFile(d,str(TMP/(name+'.pdf')))
 renderSVG.drawToFile(d,str(OUT/(name+'.svg')))
 svg=OUT/(name+'.svg');svg.write_text(svg.read_text().replace('font-family: Doc','font-family: DejaVu Sans, Arial, sans-serif'))
 subprocess.run([PDFTOPPM,'-singlefile','-r','220','-png',str(TMP/(name+'.pdf')),str(OUT/name)],check=True,stdout=subprocess.DEVNULL)

# Hydrology: two sources, one receiving basin, no implied current simulation.
d=new(720,370)
box(d,25,263,210,78,'Родник 07 · +9 м','Пасечный ручей 1,5–2 м')
box(d,455,263,240,78,'Северная река · +10 м','Русло 4–6 м, верхний разрыв')
box(d,250,130,220,88,'Озеро · 0 м','Остров 17; отдельная переправа W03',fill='water')
box(d,250,20,220,65,'Южный сток · −1 м','М5 сохраняет пешую связь',fill='water')
arrow(d,130,263,284,218,'blue',2);arrow(d,570,263,436,218,'blue',2);arrow(d,360,130,360,85,'blue',2)
text(d,77,220,'М1, М4; прыжок П02',11);text(d,467,220,'М2, М3; лёд П11',11)
wrap(d,25,95,'Три отдельные зоны: W01 у ручья, W02 на средней реке, W03 у острова.',190,11)
wrap(d,505,95,'Стрелки показывают сток. Физическое течение и проводимость воды не вводятся.',190,11)
finish(d,'hydrology')

# Route graph is deliberately schematic, while the map is spatial.
d=new(720,460)
nodes={1:(355,42,'01 старт'),2:(135,83,'02 повозка'),3:(48,149,'03 поляна'),4:(48,235,'04 пасека'),5:(140,305,'05 сарай'),6:(50,365,'06 могилы'),7:(190,414,'07 родник'),9:(296,319,'09 развилка'),8:(300,217,'08 арка'),20:(190,143,'20 корни'),18:(355,105,'18 берег'),19:(530,50,'19 / М5'),16:(654,137,'16 причал'),15:(609,219,'15 излучина'),21:(446,231,'21 каньон'),14:(645,311,'14 привал'),13:(590,398,'13 двор тролля'),10:(348,410,'10 логово'),'M2':(460,355,'М2')}
edges=[(1,2),(1,19),(2,3),(3,4),(4,5),(5,6),(6,7),(7,9),(5,9),(9,8),(8,20),(20,2),(3,20),(9,'M2'),('M2',14),('M2',13),(9,10),(13,14),(14,15),(15,21),(21,8),(15,16),(16,19),(20,18),(18,19)]
for a,b in edges:
 x1,y1,_=nodes[a];x2,y2,_=nodes[b];line(d,x1,y1,x2,y2,'line',2)
for i,(x,y,label)in nodes.items():
 if i in (10,13):diamond(d,x,y,'red')
 else:d.add(Circle(x,y,5,fillColor=C['ink'],strokeColor=None))
 text(d,x,y+11,label,10.5,anchor='middle')
text(d,212,322,'М1',9,'muted');text(d,528,237,'М3',9,'muted');text(d,238,187,'М4',9,'muted')
wrap(d,388,174,'Цели с картами: 12 → 11; 17; 22. На графе — все 18 пеших точек.',200,10.5,'purple')
text(d,22,18,'Топология: расстояния условны. Красные ромбы — необязательные опасные места.',10,'muted')
finish(d,'route-graph')

# Vertical ledges with absolute and relative heights.
d=new(720,360)
ground=55;scale=26
d.add(Polygon([25,ground,215,ground,215,ground+2.8*scale,410,ground+2.8*scale,410,ground+6.8*scale,690,ground+6.8*scale,690,25,25,25],fillColor=C['earth'],strokeColor=C['muted'],strokeWidth=1))
person(d,120,ground,1.2);diamond(d,340,ground+2.8*scale+12);diamond(d,592,ground+6.8*scale+12)
text(d,80,35,'Подход ≈ +3,2 м',11);text(d,268,ground+2.8*scale+26,'12 · +6 м',12);text(d,520,ground+6.8*scale+28,'11 · +10 м',12)
dimension(d,235,ground,235,ground+2.8*scale,'2,8 м');dimension(d,430,ground+2.8*scale,430,ground+6.8*scale,'4 м')
arrow(d,142,ground+20,190,ground+95,'purple',2);arrow(d,355,ground+2.8*scale+24,395,ground+6.8*scale+38,'purple',2)
wrap(d,38,320,'П08: базовый Импульс из покоя ≈3,26 м. Кромка на 2,8 м оставляет запас.',270,11)
wrap(d,383,320,'П09: улучшенный Импульс / Верёвка / Поток. Прыжок + базовый Импульс тоже допускается.',300,11)
text(d,290,103,'Полка 4 × 5 м',10,'muted');text(d,512,181,'Карниз ≥4 × 6 м',10,'muted')
text(d,290,73,'Лут в глубине, вне подбора снизу',10,'muted')
finish(d,'section-ledges')

# Ridge and moon example, clearly marked as ideal limit rather than guaranteed path.
d=new(720,390)
baseline=45;s=18
pts=[35,baseline+9*s,205,baseline+9*s,205,baseline+5*s,370,baseline+5*s,370,baseline+14*s,690,baseline+14*s,690,20,35,20]
d.add(Polygon(pts,fillColor=C['earth'],strokeColor=C['muted'],strokeWidth=1))
person(d,150,baseline+9*s,1.2);diamond(d,575,baseline+14*s+13)
d.add(Rect(270,baseline+5*s+1,34,5,fillColor=C['purple'],strokeColor=None));text(d,250,baseline+5*s-18,'Гриб · +5 м',11)
text(d,53,baseline+9*s+24,'07 · +9 м',12);text(d,588,baseline+14*s+28,'22 · +14 м',12)
dimension(d,221,baseline+5*s,221,baseline+9*s,'4 м');dimension(d,390,baseline+5*s,390,baseline+14*s,'9 м')
arrow(d,190,baseline+9*s+12,284,baseline+5*s+15,'ink',1.5)
p=GraphicPath(strokeColor=C['purple'],strokeWidth=2,fillColor=None,strokeDashArray=[5,3]);p.moveTo(286,baseline+5*s+12);p.curveTo(320,385,480,385,560,baseline+14*s+16);d.add(p)
wrap(d,34,368,'Гриб → отскок → попадание Луны',290,12)
wrap(d,35,95,'С террасы 07: Поток, Верёвка или Лёгкость → Импульс. Из ниши есть пеший выход.',195,10.5)
wrap(d,420,119,'3,2 м обычного отскока → до 12,8 м в идеальном пределе. Задержка применения быстро уменьшает высоту.',260,10.5)
wrap(d,420,50,'Технический эксперимент; не обязательная цепочка.',260,10,'red',leading=13)
finish(d,'section-ridge')

# Loot slot section.
d=new(720,350)
floor=105;unit=68
line(d,25,floor,695,floor,'muted',2)
wallx=290;roofy=floor+.65*unit
d.add(Rect(wallx,roofy,320,130,fillColor=C['earth'],strokeColor=C['muted'],strokeWidth=1))
person(d,210,floor,4.8);diamond(d,wallx+4.2*unit,floor+12)
text(d,wallx+4.2*unit,floor-21,'Предмет',11,anchor='middle')
text(d,wallx+1.5*unit,floor-21,'Попадание',11,anchor='middle')
d.add(Circle(wallx+1.5*unit,floor+3,4,fillColor=C['purple'],strokeColor=None))
arrow(d,wallx+4.2*unit-7,floor+22,wallx+1.5*unit+7,floor+22,'purple',2)
dimension(d,wallx+1.5*unit,floor-49,wallx+4.2*unit,floor-49,'2,7 м < радиуса 3 м')
dimension(d,wallx-16,floor,wallx-16,roofy,'0,65 м')
wrap(d,30,315,'П17: низкий проход для предмета, тело игрока не помещается.',255,12)
wrap(d,355,315,'Ровный канал шириной ≈0,9 м. Глубина предмета в пробном варианте 4,2 м.',325,11)
wrap(d,30,45,'Нужна проверка стены при подборе (R01). Низкий бросок проверяется с отступа 4–5 м перед входом.',650,11)
finish(d,'section-slot')

d=new(720,260)
box(d,20,125,195,100,'До применения','Колючки закрыты. Обход доступен. Предмет за преградой.',fill='earth')
box(d,262,125,195,100,'В текущей вылазке','Огонь потрачен. Проём открыт. Можно вернуться позже.')
box(d,504,125,195,100,'Следующая вылазка','Исходная преграда и находка восстановлены.',fill='earth')
arrow(d,215,176,262,176,'orange',2);arrow(d,457,176,504,176,'muted',2)
text(d,238,110,'Огонь',11,anchor='middle');text(d,480,110,'Возврат / поражение',10,anchor='middle')
wrap(d,28,72,'Карта меняет путь до конца вылазки. Знание маршрута и новые экземпляры карт сохраняются через лавку.',665,12)
finish(d,'card-state-cycle')

d=new(720,390)
box(d,20,286,205,75,'Исходная рука','Огонь · Удар · Лечение')
box(d,260,286,205,75,'После Огня в П03','Удар · Лечение · Импульс')
box(d,500,286,200,75,'После Удара по грибу','Лечение · Импульс · Лёд')
arrow(d,225,325,260,325,'orange');arrow(d,465,325,500,325,'ink')
box(d,260,146,205,78,'П04 как следующая цель','Импульс в текущей руке уже имеет близкое полезное применение.',fill='earth')
arrow(d,600,286,465,215,'purple')
box(d,20,30,300,75,'Если пришла другая карта','Продолжить пешую петлю; цель выбирается по руке.')
box(d,390,30,310,75,'Нельзя считать этот добор гарантией','Пример объясняет решение, а не задаёт обязательный сценарий.',fill='earth')
arrow(d,360,146,170,105,'muted');arrow(d,365,146,545,105,'muted')
finish(d,'hand-example')

d=new(720,390)
d.add(Rect(215,75,290,255,fillColor=C['green'],strokeColor=C['line'],strokeWidth=1))
text(d,360,350,'13 · арена 18 × 20 м',13,anchor='middle')
d.add(Circle(360,220,12,fillColor=C['red'],strokeColor=None));text(d,360,244,'Тролль',11,anchor='middle')
d.add(Circle(284,153,30,fillColor=C['earth'],strokeColor=C['orange'],strokeWidth=1));text(d,280,151,'Смола',10,anchor='middle')
d.add(Circle(432,260,27,fillColor=C['water'],strokeColor=C['blue'],strokeWidth=1));text(d,432,260,'Дождь',10,anchor='middle')
d.add(Rect(530,92,25,238,fillColor=C['earth'],strokeColor=C['muted'],strokeWidth=1))
line(d,597,70,597,337,'muted',4)
arrow(d,230,310,160,310,'ink',2);text(d,143,306,'К М2',11,anchor='end')
arrow(d,435,86,435,38,'ink',2);text(d,435,20,'К привалу 14',11,anchor='middle')
wrap(d,28,238,'Разнесённые выходы. Сухие позиции для отхода от мокрой цепи.',160,11)
wrap(d,570,260,'Внешний обход за непрозрачной стенкой.',135,11)
wrap(d,25,85,'Пример подготовки, не фиксированные зоны спавна карт. Дождь рядом со смолой может разрушить план.',170,10.5)
finish(d,'combat-arena')
print('8 technical diagrams exported')
