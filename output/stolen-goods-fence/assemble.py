from PIL import Image
from pathlib import Path
import json, hashlib, re, uuid, shutil

root=Path(__file__).resolve().parent
repo=root.parents[1]
names=['default_front','back','displeased_front','happy_front','enraged_front']
unity_names=['DefaultFront','Back','DispleasedFront','HappyFront','EnragedFront']
W,H=1224,1296
atlas=Image.new('RGBA',(3*W,2*H))
cells=[]
for i,name in enumerate(names):
    im=Image.open(root/f'masters/{name}.png')
    assert im.mode=='RGBA' and im.size==(W,H)
    x,y=i%3*W,i//3*H
    atlas.paste(im,(x,y))
    cells.append({'name':name,'file':f'masters/{name}.png','column':i%3,'row':i//3,'rect_top_left':[x,y,W,H],'source_offset_px':[8,3] if name=='back' else [0,0]})
atlas.save(root/'atlas.png')
saved=Image.open(root/'atlas.png')
checks=[]
for c in cells:
    x,y,w,h=c['rect_top_left'];im=Image.open(root/c['file'])
    assert saved.crop((x,y,x+w,y+h)).tobytes()==im.tobytes()
    a=im.getchannel('A');bbox=a.point(lambda v:255 if v>=32 else 0).getbbox()
    assert bbox[0]>0 and bbox[1]>0 and bbox[2]<W and bbox[3]<H
    checks.append({'name':c['name'],'exact_RGBA_match':True,'visible_bounds_alpha_32':bbox,'alpha_extrema':a.getextrema(),'sha256':hashlib.sha256((root/c['file']).read_bytes()).hexdigest()})
assert saved.crop((2*W,H,3*W,2*H)).getchannel('A').getbbox() is None
json.dump({'atlas_size':saved.size,'mode':saved.mode,'cells':checks,'empty_cell_fully_transparent':True,'expression_verification':'expression-work/verification.json'},open(root/'verification.json','w'),indent=2)
pivot=[602/W,(H-1285)/H]
manifest={'character':'Скупщик краденого — матёрый торгаш','id':'StolenGoodsFence','status':'generated_for_user_review','created_on':'2026-11-12','user_refinement':'сделай его матерым торгашем','generator':'built-in image_gen','style_reference':'../shop-visitors/09-peasant-line-details.png','prompts':['prompts.json','expression-work/prompts.json'],'default_front':'masters/default_front.png','shared_back':'masters/back.png','variants':{n:f'masters/{n}_front.png' for n in ['displeased','happy','enraged']},'source_normalization':'canvas.json','atlas':{'file':'atlas.png','status':'built_pixel_verified','size':[3*W,2*H],'cell_size':[W,H],'columns':3,'rows':2,'origin':'top-left','offset':[0,0],'padding':[0,0],'cells':cells,'empty_cell':[2,1],'sha256':hashlib.sha256((root/'atlas.png').read_bytes()).hexdigest()},'master_pivot_normalized_bottom_left':pivot,'master_pixels_per_unit':100,'ground_anchor_top_left':[602,1285],'nominal_customer_world_scale':0.14285715,'physical_height_m_approx':1.8,'expression_verification':'expression-work/verification.json','verification':'verification.json','unity':{'import_validated':False,'play_mode_validated':False,'gameplay_roster_integration':False}}
json.dump(manifest,open(root/'manifest.json','w'),indent=2,ensure_ascii=False)
# Full sprites on light and dark backgrounds for visual inspection, separate from texture.
preview=Image.new('RGB',(5*300,2*318))
for row,bg in enumerate(['#e9dfd0','#292d31']):
    for i,name in enumerate(names):
        tile=Image.new('RGBA',(W,H),bg);tile.alpha_composite(Image.open(root/f'masters/{name}.png'))
        preview.paste(tile.convert('RGB').resize((300,318),Image.Resampling.LANCZOS),(i*300,row*318))
preview.save(root/'preview.png')
# Create unique source import metadata outside Unity, then the standard density tool consumes it.
orig=repo/'output/sprite-density/originals/NPCs/StolenGoodsFence'
orig.mkdir(parents=True,exist_ok=True)
shutil.copy2(root/'atlas.png',orig/'StolenGoodsFenceAtlas.png')
meta=(repo/'hukaton/Assets/Content/NPCs/Blacksmith/BlacksmithAtlas.png.meta').read_text()
meta=re.sub(r'^guid: .*','guid: '+uuid.uuid4().hex,meta,flags=re.M)
meta=meta.replace('Blacksmith_','StolenGoodsFence_')
meta=re.sub(r'^(  spritePixelsToUnits: ).*',r'\g<1>100',meta,flags=re.M)
meta=re.sub(r'^(  spritePivot: ).*',lambda m:m[1]+f'{{x: {pivot[0]}, y: {pivot[1]}}}',meta,flags=re.M)
for i,n in enumerate(unity_names):
    name='StolenGoodsFence_'+n
    pattern=r'(    - serializedVersion: 2\n      name: '+name+r'\n      rect:\n        serializedVersion: 2\n)        x: \S+\n        y: \S+\n        width: \S+\n        height: \S+\n      alignment: \d+\n      pivot: \{x: \S+, y: \S+\}'
    rect=f'        x: {i%3*W}\n        y: {(1-i//3)*H}\n        width: {W}\n        height: {H}\n      alignment: 9\n      pivot: {{x: {pivot[0]}, y: {pivot[1]}}}'
    meta,count=re.subn(pattern,lambda m:m[1]+rect,meta);assert count==1
    old=re.search(r'      '+name+r': (-?\d+)',meta)[1]
    meta=meta.replace(old,str(21300001+i))
meta=re.sub(r'(      spriteID: )\S+',lambda m:m[1]+uuid.uuid4().hex,meta)
(orig/'StolenGoodsFenceAtlas.png.meta').write_text(meta)
entry={'png':'originals/NPCs/StolenGoodsFence/StolenGoodsFenceAtlas.png','density':200,'world_scale':0.14285715,'out':'NPCs/StolenGoodsFence','note':'Customer art; 2x common grid, ShopCustomer nominal scale 1/7; roster not modified','ground_at_pivot':True}
config=repo/'output/sprite-density/config.json'
data=json.loads(config.read_text());assert not any(e['out']==entry['out'] for e in data)
data.append(entry);config.write_text(json.dumps(data,indent=1,ensure_ascii=False)+'\n')
json.dump([entry],open(root/'density-config.json','w'),indent=2)
print('Master atlas verified:',saved.size)
