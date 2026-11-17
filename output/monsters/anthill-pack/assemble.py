"""Deterministic padding, packing and verification; never redraws source artwork."""
from pathlib import Path
from PIL import Image, ImageDraw
import numpy as np
import json, hashlib, re, uuid, importlib.util, os

ROOT = Path(__file__).resolve().parents[3]
PACK = Path(__file__).resolve().parent
STATES = ['front-default','back-default','front-aggro','front-hurt','front-attack','front-death','back-death']
SUFFIX = ['DefaultFront','DefaultBack','AggroFront','HurtFront','AttackFront','DeathFront','DeathBack']
SPECS = {
 'AntWorker': ('Муравей-работяга',1.3),
 'AntPlug': ('Муравей-пробка',1.6),
 'WingedAnt': ('Крылатый муравей',2.2),
 'BubbleLarva': ('Личинка-пузырь',1.0),
 'AntQueen': ('Матка',2.8),
}
W=1800
H=2000
GROUND=1500
PIVOT=[.5,(H-GROUND)/H]
def dump(p,d): p.write_text(json.dumps(d,ensure_ascii=False,indent=2)+'\n')
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def rel(p): return str(p.relative_to(ROOT))
template=(ROOT/'hukaton/Assets/Content/Enemies/AntMachineGunCrew/AntMachineGunCrewAtlas.png.meta').read_text()
spec=importlib.util.spec_from_file_location('density',ROOT/'output/sprite-density/tool/resample.py')
density=importlib.util.module_from_spec(spec);spec.loader.exec_module(density)
reports=[]
for enemy,(name,height) in SPECS.items():
 folder=PACK/enemy; masters=folder/'masters';masters.mkdir(exist_ok=True)
 atlas=Image.new('RGBA',(W*3,H*3));cells=[];checks=[]
 for i,state in enumerate(STATES):
  src=folder/'raw'/f'{state}.png';im=Image.open(src);assert im.mode=='RGBA'
  a=np.asarray(im)[:,:,3];bbox=Image.fromarray((a>127).astype('uint8')).getbbox();assert bbox
  assert not any(np.any(e>127) for e in [a[0],a[-1],a[:,0],a[:,-1]])
  offset=((W-im.width)//2,GROUND-bbox[3]);assert min(offset)>=0
  assert offset[0]+im.width<=W and offset[1]+im.height<=H
  master=Image.new('RGBA',(W,H));master.paste(im,offset)
  dest=masters/f'{state}.png';master.save(dest)
  assert np.array_equal(np.asarray(master.crop((*offset,offset[0]+im.width,offset[1]+im.height))),np.asarray(im))
  atlas.paste(master,((i%3)*W,(i//3)*H))
  cells.append(dict(name=enemy+'_'+SUFFIX[i],state=state,master=rel(dest),column=i%3,row=i//3,
                    rect_top_left=[(i%3)*W,(i//3)*H,W,H],source=rel(src),source_size=list(im.size),
                    source_offset=list(offset),visible_bbox=list(bbox),source_sha256=sha(src)))
  checks.append(dict(state=state,rgba=True,transparent_pixels=int((a==0).sum()),
                     opaque_pixels=int((a==255).sum()),visible_edge_pixels=0,source_rgba_preserved=True))
 master_atlas=folder/'atlas.png';atlas.save(master_atlas)
 reopened=Image.open(master_atlas)
 for c in cells:
  x,y,w,h=c['rect_top_left'];assert np.array_equal(np.asarray(reopened.crop((x,y,x+w,y+h))),np.asarray(Image.open(ROOT/c['master'])))
 for k in [7,8]:assert np.max(np.asarray(reopened.crop(((k%3)*W,(k//3)*H,(k%3+1)*W,(k//3+1)*H)))[:,:,3])==0
 # Runtime size is an explicitly provisional art assumption, no gameplay asset is created.
 body_pixels=cells[0]['visible_bbox'][3]-cells[0]['visible_bbox'][1];ppu=body_pixels/height
 original=ROOT/'output/sprite-density/originals/Enemies'/enemy/(enemy+'Atlas.png');original.parent.mkdir(parents=True,exist_ok=True)
 import shutil
 shutil.copyfile(master_atlas,original)
 meta=re.sub(r'^guid: .*', 'guid: '+uuid.uuid4().hex,template,flags=re.M)
 meta=re.sub(r'^(  spritePixelsToUnits: ).*',lambda m:m.group(1)+str(ppu),meta,flags=re.M)
 snippets=[]
 ids=[]
 for i,c in enumerate(cells):
  sid=uuid.uuid4().hex;iid=100000+i;ids.append(iid)
  s=density.SPRITE_TMPL.format(name=c['name'],sid=sid,iid=iid)
  s=s.replace('        x: 0','        x: '+str((i%3)*W)).replace('        y: 0','        y: '+str(3*H-(i//3+1)*H))
  s=s.replace('        width: 1','        width: '+str(W)).replace('        height: 1','        height: '+str(H))
  s=s.replace('pivot: {x: 0.5, y: 0.5}',f'pivot: {{x: {PIVOT[0]}, y: {PIVOT[1]}}}')
  snippets.append(s)
 before,tail=meta.split('    sprites:\n',1);tail=tail[re.search(r'^    outline: \[\]',tail,re.M).start():]
 meta=before+'    sprites:\n'+''.join(snippets)+tail
 meta=re.sub(r'    nameFileIdTable:[\s\S]*?(?=\n  [^ ])','    nameFileIdTable:\n'+''.join(f"      {c['name']}: {i}\n" for c,i in zip(cells,ids)).rstrip(),meta)
 Path(str(original)+'.meta').write_text(meta)
 entry=dict(png=str(original.relative_to(ROOT/'output/sprite-density')),density=100,world_scale=1,
            out='Enemies/'+enemy,note=f'Anthill art only; provisional visible height {height}m; no gameplay prefab.',margin_px=2)
 staging=folder/'density';staging.mkdir(exist_ok=True)
 old=os.getcwd();os.chdir(ROOT/'output/sprite-density')
 try: report=density.process(entry,str(staging))
 finally:os.chdir(old)
 assert report['packing']=='grid';assert report['new_ppu']==100;assert len(report['sprites'])==7
 dump(folder/'density-report.json',report);reports.append((enemy,entry,report))
 runtime=Image.open(staging/(enemy+'Atlas.png'));occupied=np.zeros((runtime.height,runtime.width),bool)
 runtime_masters=folder/'runtime-masters';runtime_masters.mkdir(exist_ok=True)
 for s in report['sprites']:
  x,y,w,h=s['rect'];top=runtime.height-y-h
  cell=runtime.crop((x,top,x+w,top+h));cell.save(runtime_masters/(s['name']+'.png'))
  assert not occupied[top:top+h,x:x+w].any();occupied[top:top+h,x:x+w]=True
 assert not np.asarray(runtime)[:,:,3][~occupied].any()
 assert set(np.unique(np.asarray(runtime)[:,:,3]))=={0,255}
 dest=ROOT/'hukaton/Assets/Content/Enemies'/enemy;dest.mkdir(exist_ok=True)
 shutil.copyfile(staging/(enemy+'Atlas.png'),dest/(enemy+'Atlas.png'))
 manifest=dict(character=enemy,display_name=name,type='aggressive',temperament_status='working_assumption_for_combat_concept',
  physical_height_m=height,size_provided_by_user=False,size_reference='Provisional art scale including default silhouette; no gameplay tuning.',
  nominal_world_scale=1,gameplay_implemented=False,health=None,damage=None,loot_assigned=False,
  master_atlas=rel(master_atlas),master_atlas_sha256=sha(master_atlas),master_cell_size=[W,H],master_columns=3,master_rows=3,
  master_ground_anchor=[W/2,GROUND],master_pivot=PIVOT,master_pixels_per_unit=ppu,master_cells=cells,
  atlas=enemy+'Atlas.png',width=runtime.width,height=runtime.height,cell_width=report['cell'][0],cell_height=report['cell'][1],
  columns=report['cols'],rows=report['rows'],pixelsPerUnit=100,pivot=report['sprites'][0]['pivot'],cells=report['sprites'],
  unity=dict(atlas=enemy+'Atlas.png',pixels_per_unit=100,world_density=100,world_scale=1,sprites=report['sprites'],import_validation='pending_actual_editor'),
  generation_mode='built-in image_gen; transparent_background=true; separate state calls',prompts=rel(PACK/'prompts.json'),
  user_art_approval='concepts_approved_generated_sprites_not_yet_reviewed',unity_import_validation='pending_actual_editor',
  source_padding='Whole source RGBA pasted unchanged; integer offsets align alpha>=128 bottom to common ground; no per-state scaling, crop or rotation.',
  notes=['Seven full-body states; rear only for default and death.','Two unused master cells fully transparent.','No gameplay, loot, prefab or scene added.'])
 dump(dest/'manifest.json',manifest);dump(folder/'manifest.json',manifest)
 dump(folder/'verification.json',dict(source_checks=checks,master_atlas_exact_rgba=True,empty_cells_transparent=True,
  runtime_equal_cells=True,runtime_no_overlap=True,runtime_hard_alpha=True,dimensions=list(runtime.size),
  runtime_atlas_sha256=sha(dest/(enemy+'Atlas.png')),actual_editor_validation='pending'))
 # Presentation previews only; never imported as textures.
 preview=Image.new('RGB',(1200,840),(232,225,210));draw=ImageDraw.Draw(preview)
 for i,s in enumerate(report['sprites']):
  im=Image.open(runtime_masters/(s['name']+'.png'));im.thumbnail((370,235),Image.Resampling.NEAREST)
  x=(i%3)*400+(400-im.width)//2;y=(i//3)*280+25
  preview.paste(im,(x,y),im);draw.text(((i%3)*400+15,(i//3)*280+260),s['name'],fill=(35,30,25))
 preview.save(folder/'states-preview.png')
 thumb=Image.open(masters/'front-default.png');thumb.thumbnail((480,480),Image.Resampling.LANCZOS);thumb.save(folder/'registry.png')
 print(enemy,'master',list(atlas.size),'runtime',list(runtime.size),'cell',report['cell'],'PPU',report['new_ppu'])
cfgpath=ROOT/'output/sprite-density/config.json';cfg=json.loads(cfgpath.read_text())
for enemy,entry,report in reports:
 cfg=[x for x in cfg if x['png']!=entry['png']];cfg.append(entry)
dump(cfgpath,cfg)
dump(PACK/'build-report.json',[dict(enemy=e,entry=en,report=r) for e,en,r in reports])
