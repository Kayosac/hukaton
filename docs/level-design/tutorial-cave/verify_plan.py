"""Geometric design checks only; deliberately does not claim Unity validation."""
from pathlib import Path
from collections import deque
import math,json,xml.etree.ElementTree as ET
from PIL import Image
P=Path(__file__).resolve().parent;d=json.loads((P/'layout.json').read_text());poly=d['floorPolygon']
def inside(pt):
 x,z=pt;c=False
 for a,b in zip(poly,poly[1:]+poly[:1]):
  if(a[1]>z)!=(b[1]>z)and x<(b[0]-a[0])*(z-a[1])/(b[1]-a[1])+a[0]:c=not c
 return c
def sees(a,b):
 n=max(1,math.ceil(math.dist(a,b)*10))
 return all(inside([a[0]+(b[0]-a[0])*i/n,a[1]+(b[1]-a[1])*i/n])for i in range(n+1))
samples=[];dist=0
for a,b in zip(d['route'],d['route'][1:]):
 length=math.dist(a,b);n=math.ceil(length*5)
 for i in range(n):
  t=i/n;samples.append((dist+t*length,[a[0]+t*(b[0]-a[0]),a[1]+t*(b[1]-a[1])]))
 dist+=length
assert all(inside(a)for _,a in samples)
assert all(inside([44+5*math.cos(i/360*2*math.pi),7+5*math.sin(i/360*2*math.pi)])for i in range(360))
visibility=[]
for t in d['revealTargets']:
 assert all(not sees(a,t['point'])for a in t['mustBeHiddenFrom'])
 st,eye=next((st,a)for st,a in samples if sees(a,t['point']))
 assert math.dist(eye,t['point'])<12
 visibility.append({'target':t['id'],'required_views_blocked':True,'first_centerline_view_at_route_m':round(st,2),'eye_xz':eye,'distance_to_target_m':round(math.dist(eye,t['point']),2)})
walk={(i,j)for i in range(257)for j in range(173)if inside([i*.25,j*.25])}
def path(roots=False,water=False):
 def blocked(a):
  x,z=a[0]*.25,a[1]*.25
  return (roots and 8<=x<=12 and 28.5<=z<=29.5)or(water and 38<=x<=48 and 31<=z<=39)
 q=deque([(32,20)]);seen=set(q)
 while q:
  a=q.popleft()
  if a==(188,24):return True
  for n in [(a[0]+1,a[1]),(a[0]-1,a[1]),(a[0],a[1]+1),(a[0],a[1]-1)]:
   if n in walk and n not in seen and not blocked(n):seen.add(n);q.append(n)
 return False
topology={'open_path':path(),'roots_block_path':not path(roots=True),'water_blocks_path':not path(water=True)}
assert all(topology.values())
for name,size in [('cave-plan',(1800,1120)),('cave-section',(1800,630))]:
 ET.parse(P/'figures'/f'{name}.svg');im=Image.open(P/'figures'/f'{name}.png');assert im.size==size;im.verify()
report={'version':'0.2','route_length_m':round(dist,2),'route_samples_inside_floor':len(samples),'clear_goblin_circle_inside_floor':True,'visibility':visibility,'visibility_scope':'2D rays to target center / near water edge from centerline. Full silhouettes, corridor edges, camera height and jump require Unity checks.','planar_topology_025m':topology,'png_dimensions_correct':True,'svg_parse_valid':True,'unity_editor_validation':False,'unity_playmode_validation':False,'status':'design-proposal'}
(P/'verification.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n');print(json.dumps(report,ensure_ascii=False))
