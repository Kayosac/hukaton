"""Check the document package. Does not execute Unity or simulate gameplay."""
from pathlib import Path
import json,re,hashlib
from collections import defaultdict,deque
import pdfplumber
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
REPO=ROOT.parents[2]
PDF=REPO/'output/pdf/OldApiary-Level-Design-RU.pdf'
def read(name):return json.loads((ROOT/'data'/name).read_text())
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
text=(ROOT/'LEVEL_DESIGN.md').read_text()
plan=read('level-plan.json');cards=read('card-catalog.json');loot=read('loot-plan.json');tests=read('test-cases.json')
checks={}
def check(name,condition,detail):
 checks[name]={'passed':bool(condition),'detail':detail}
check('unique_places',sorted(p['id'] for p in plan['places'])==list(range(1,23)),len(plan['places']))
check('unique_barriers',sorted(b['id'] for b in plan['barriers'])==list(range(1,19)),len(plan['barriers']))
check('place_passports',len(re.findall(r'^## \d{2} ',text,re.M))==22,22)
check('barrier_passports',len(re.findall(r'^## П\d{2} ',text,re.M))==18,18)
check('chapters',len(re.findall(r'^# \d+ ',text,re.M))==20,20)
check('ordinary_cards',len(cards)==22 and len({c['id'] for c in cards})==22,len(cards))
check('future_acceptance_tests',len(tests)==44,{'count':len(tests),'execution':'not run; acceptance plan only'})
check('bridges',len(plan['bridges'])==5,5)
graph=defaultdict(set)
for _,a,b in plan['roads']:graph[a].add(b);graph[b].add(a)
seen={1};queue=deque([1])
while queue:
 for nxt in graph[queue.popleft()]:
  if nxt not in seen:seen.add(nxt);queue.append(nxt)
missing_nodes=sorted(set(range(1,23))-seen)
check('walking_connectivity',len(seen)==18 and missing_nodes==[11,12,17,22],{'reachable':sorted(seen),'additional_card_goals':missing_nodes,'scope':'graph topology, not collision/NavMesh'})
missing=[]
for _,path in re.findall(r'\[([^\]]+)\]\(([^)]+)\)',text):
 if not re.match(r'\w+://',path) and not (ROOT/path).exists():missing.append(path)
check('source_and_figure_links',not missing,missing)
figures=[]
for path in sorted((ROOT/'figures').glob('*.png')):
 with Image.open(path) as im:
  im.verify()
 with Image.open(path) as im:figures.append({'file':path.name,'size':list(im.size),'svg':path.with_suffix('.svg').exists()})
check('figures',len(figures)==13 and all(f['svg'] for f in figures),figures)
values=[p['value'] for p in loot]
check('proposed_loot',len(loot)==19 and sum(values)==580,{'count':len(loot),'common_20':values.count(20),'rare_40':values.count(40),'total':sum(values),'status':'proposed; excludes creature trophies'})
pages=[];outside=[];empty=[]
with pdfplumber.open(PDF) as pdf:
 for i,page in enumerate(pdf.pages,1):
  words=page.extract_text() or ''
  bad=[c['text'] for c in page.chars if c['x0']<-.5 or c['x1']>page.width+.5 or c['top']<-.5 or c['bottom']>page.height+.5]
  if bad:outside.append({'page':i,'characters':bad})
  if len(words)<75 and not page.images:empty.append(i)
  pages.append({'page':i,'size_pt':[round(page.width,2),round(page.height,2)],'text_characters':len(words),'images':len(page.images)})
check('pdf_text_inside_pages',not outside,outside)
check('pdf_no_empty_pages',not empty,empty)
check('pdf_map_plates',sum(p['size_pt'][0]>650 for p in pages)==5,[p['page'] for p in pages if p['size_pt'][0]>650])
report={'version':plan['version'],'checked':'2026-11-09','scope':'Documentation, source links, graph topology, arithmetic and PDF geometry. No game assets changed by these checks.','checks':checks,'pdf':{'path':str(PDF.relative_to(REPO)),'page_count':len(pages),'sha256':sha(PDF),'pages':pages},'source_sha256':sha(ROOT/'LEVEL_DESIGN.md'),'visual_review':{'status':'pending final render inspection'},'unity_validation':{'executed':False,'reason':'Documentation-only task; proposed level is not an authored or tested Unity scene.'},'playtests':{'executed':False,'target_total_familiarization_minutes':[20,30]}}
(ROOT/'data/validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
print(json.dumps({'checks':len(checks),'failed':[k for k,v in checks.items() if not v['passed']],'pages':len(pages),'walking_points':len(seen),'figures':len(figures)},ensure_ascii=False))
if any(not v['passed'] for v in checks.values()):raise SystemExit(1)
