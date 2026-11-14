"""Author v0.2 and its explicit floor boundary, without dependencies or Unity edits."""
from pathlib import Path
import json,math
P=Path(__file__).resolve().parent
d=json.loads((P/'layout.json').read_text())
d.update(version='0.2',bounds=[0,0,64,43],targetMinutes=None)
d['route']=[[8,5],[8,12],[16,12],[16,19],[10,19],[10,29],[10,35],[21,35],[21,28],[29,28],[29,35],[34,35],[38,35],[48,35],[56,35],[56,27],[48,27],[48,20],[57,20],[57,12],[54,12],[54,3],[49,3],[47,6]]
d['rooms']=[
 {'id':1,'name':'Пробуждение и первый зигзаг','center':[8,5],'floor':2,'ceiling':5,'cue':'За входом два закрытых поворота. Корней отсюда не видно.','action':'Два броска ЛКМ → R; затем прогулка к первому уроку.'},
 {'id':2,'name':'Корневой затвор','center':[10,29],'floor':2,'ceiling':5.2,'cue':'Корни раскрываются только на последнем прямом подходе.','action':'Огонь → пройти. За корнями снова каменный поворот.'},
 {'id':3,'name':'Сухой серпантин','center':[25,28],'floor':2,'ceiling':5,'cue':'Ход сначала уводит в сторону, затем вниз. Вода за каменной стеной.','action':'Пандус −2 м; последний поворот открывает водный зал.'},
 {'id':4,'name':'Скрытая переправа','center':[43,35],'floor':0,'ceiling':9,'cue':'Вода впервые видна только с ближнего подхода.','action':'Лёд → заморозить перемычку 10 × 8 м → перейти.'},
 {'id':5,'name':'Петляющий ход с эхом','center':[52,20],'floor':0,'ceiling':3,'cue':'Чередование левых и правых поворотов. Куст скрыт.','action':'Пройти короткие прямые, полого подняться на +1 м.'},
 {'id':6,'name':'Грот партнёра','center':[44,7],'floor':1,'ceiling':7,'cue':'Куст раскрывается лишь после последнего каменного угла.','action':'Удар → беготня ≈5 с → семь реплик → затемнение в лавку.'}
]
d['waterPolygon']=[[38,31],[48,31],[48,39],[38,39]]
d['rootGate']=[[8.1,29],[11.9,29]]
d['grantZones']=[
 {'id':'F','card':'Огонь','center':[10,26.5],'radius':3.5,'requires':'all_instructions_discarded; actual approach corridor'},
 {'id':'I','card':'Лёд','center':[35,35],'radius':3.5,'requires':'roots_passed; actual near-shore approach'},
 {'id':'S','card':'Удар','center':[49,4],'radius':3.5,'requires':'far_shore_reached; final grotto approach'}
]
d['bush']=[44,7];d['shores']=[{'id':'A','position':[36,0.05,35]},{'id':'B','position':[50,0.05,35]}]
d['ramp']={'from':[21,2,28],'to':[29,0,28],'minimumWidth':3.5}
d.pop('upperShelfGuard',None)
d['section']=[[0,2],[50.4,2],[58.4,0],[110,0],[118,1],[147,1]]
d['sectionNote']='Floor profile follows route distance. Exact lengths recalculated from route. No early overlook of water.'
d['corridorWidth']=3.5
d['wallPolicy']='Occluding rock connects floor to ceiling. No low walls, windows, gaps above walls, or climbable props exposing future lessons.'
d['revealTargets']=[{'id':'roots','point':[10,29],'mustBeHiddenFrom':[[8,5],[16,12],[16,19]]}, {'id':'water','point':[38,35],'mustBeHiddenFrom':[[10,29],[10,31],[21,35],[25,28]]}, {'id':'bush','point':[44,7],'mustBeHiddenFrom':[[48,35],[50,35],[48,27],[48,20],[57,20]]}]
d['revisionBrief']='User: longer and more bends, slightly more walking, next obstacle not visible ahead. v0.2 replaces early water overlook with dry enclosed switchbacks.'

start=[[3,2],[6,0.5],[11,1],[13,4],[12,8],[9,9],[5,8],[3,5]]
grove=[[36,6],[38,2],[43,0],[48.5,1],[50.5,5],[50,10],[48,13],[42,14],[38,11]]
banks=[[34.5,32.5,38,37.5],[48,32.5,51.5,37.5]]
def inside(pt,poly):
 x,z=pt;c=False
 for a,b in zip(poly,poly[1:]+poly[:1]):
  if (a[1]>z)!=(b[1]>z) and x<(b[0]-a[0])*(z-a[1])/(b[1]-a[1])+a[0]:c=not c
 return c
def distance(p,a,b):
 dx=b[0]-a[0];dz=b[1]-a[1];t=max(0,min(1,((p[0]-a[0])*dx+(p[1]-a[1])*dz)/(dx*dx+dz*dz)))
 return math.hypot(p[0]-a[0]-t*dx,p[1]-a[1]-t*dz)
def traversable(p):
 if inside(p,start) or inside(p,grove) or inside(p,d['waterPolygon']):return True
 if any(a<=p[0]<=c and b<=p[1]<=e for a,b,c,e in banks):return True
 # One shallow dry recess on the outside of the middle walking passage.
 if 57<=p[0]<=60 and 18.5<=p[1]<=21.5:return True
 return any(distance(p,a,b)<=1.75 for a,b in zip(d['route'],d['route'][1:]))

# Raster union at 20 cm, followed by a conservative outline simplification.
step=.2
cells={(i,j)for i in range(320)for j in range(215)if traversable(((i+.5)*step,(j+.5)*step))}
edges={}
for i,j in cells:
 for nei,a,b in [((i,j-1),(i,j),(i+1,j)),((i+1,j),(i+1,j),(i+1,j+1)),((i,j+1),(i+1,j+1),(i,j+1)),((i-1,j),(i,j+1),(i,j))]:
  if nei not in cells:edges[a]=b
loops=[]
while edges:
 startpt=next(iter(edges));v=startpt;loop=[]
 while True:
  loop.append([round(v[0]*step,2),round(v[1]*step,2)]);v=edges.pop(v)
  if v==startpt:break
 loops.append(loop)
assert len(loops)==1, f'Unexpected disconnected floor or hole: {len(loops)}'
def simplify(points,eps):
 if len(points)<3:return points
 a,b=points[0],points[-1]
 if a==b:return points
 values=[distance(p,a,b)for p in points[1:-1]];m=max(values,default=0)
 if m>eps:
  i=values.index(m)+1;return simplify(points[:i+1],eps)[:-1]+simplify(points[i:],eps)
 return [a,b]
pts=loops[0];mid=len(pts)//2
d['floorPolygon']=simplify(pts[:mid+1],.12)[:-1]+simplify(pts[mid:]+[pts[0]],.12)[:-1]
d['routeLengthMetres']=round(sum(math.dist(a,b)for a,b in zip(d['route'],d['route'][1:])),2)
d['implementationNotes']=[
 'Keep agreed three lessons and script. Added length comes from walking passages, not new tasks.',
 'All blocking walls must be full height. Check line of sight from both edges of each corridor and while jumping.',
 'Card grants require actual approach and completed prior obstacle; XZ distance alone can trigger through a folded corridor wall.',
 'Nearest safe shore A/B requires implementation; current prologueShore is a single reference.',
 'Freeze the whole 10x8m plug with one WorldObstacle, since Freeze disables the whole barrier.',
 'Flat 10m-diameter goblin performance clearance; all dimensions await Unity playtesting.'
]
(P/'layout.json').write_text(json.dumps(d,ensure_ascii=False,indent=2)+'\n')
print('Route',d['routeLengthMetres'],'m; boundary',len(d['floorPolygon']),'vertices')
