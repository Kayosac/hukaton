# Rebuilds enemy-sounds.html from the current enemy prefabs: python3 output/enemy-sounds/build.py
import os,re,json,subprocess,base64,io,functools,html,hashlib
from PIL import Image
HERE=os.path.dirname(os.path.abspath(__file__))
R=os.path.abspath(os.path.join(HERE,'..','..')); A=R+'/hukaton/Assets'
guid2path={}
o=subprocess.run(['grep','-rH','--include=*.meta','^guid:',A],capture_output=True,text=True).stdout
for line in o.splitlines():
    p,_,g=line.rpartition(':guid: '); guid2path[g.strip()]=p[:-5]
CUE='56466cc688e8d449385595f6364b3480'
def txt(p): return open(p,errors='ignore').read()
@functools.lru_cache(None)
def is_cue(p): return bool(p) and p.endswith('.asset') and CUE in txt(p)[:2000]
def field(t,k):
    m=re.search(r'\n  '+k+r': (.*)',t); return m.group(1).strip() if m else None
def yname(s):
    if s is None: return None
    s=s.strip()
    if s.startswith('"'): return json.loads(s)
    return s
def cue(p):
    t=txt(p)
    clips=[guid2path[g] for g in re.findall(r'fileID: 8300000, guid: ([0-9a-f]{32}), type: 3',t) if g in guid2path]
    return dict(id=os.path.basename(p)[:-6],clips=clips,volume=field(t,'volume'),pitch=field(t,'pitch'))
def scan(p,chain=(),depth=0):
    out=[];cls=None;last=None
    for l in txt(p).split('\n'):
        m=re.match(r'^  m_EditorClassIdentifier: (.*)',l)
        if m: cls=m.group(1).split('::')[-1].split('.')[-1]
        mk=re.match(r'^  (\w+):\s*$',l)
        if mk: last=mk.group(1)
        for m in re.finditer(r'(?:(\w+): )?\{fileID: (-?\d+), guid: ([0-9a-f]{32}), type: 2\}',l):
            key=m.group(1) or last
            tp=guid2path.get(m.group(3))
            if not tp: continue
            if is_cue(tp): out.append(dict(owner=cls,field=key,chain=chain,cue=cue(tp)))
            elif tp.endswith('.asset') and depth<2 and os.path.getsize(tp)<300000 and 'MonoBehaviour' in txt(tp)[:300]:
                t=txt(tp); nm=yname(field(t,'displayName')) or os.path.basename(tp)[:-6]
                out+=scan(tp,chain+((nm,os.path.basename(os.path.dirname(tp))),),depth+1)
    return out

FIELD={
 ('WorldCreature','alertCue'):('Заметил игрока','Переход в тревогу. Пугливые играют его же при бегстве, чуть выше тоном.'),
 ('WorldCreature','threatCue'):('Угроза / рык','Сорвался в агрессию; дальше рычит в погоне, не чаще раза в 1.3 с.'),
 ('WorldCreature','windupCue'):('Замах','Подготовка к атаке — телеграф перед ударом или выстрелом.'),
 ('WorldCreature','strikeCue'):('Удар','Сам момент атаки (укус, удар, рывок).'),
 ('WorldCreature','hurtCue'):('Получил урон','Попадание по врагу. Тот же звук, тише и выше, — стук по оболочке; ниже — когда тонет.'),
 ('WorldCreature','deathCue'):('Смерть','Гибель (и конец утопания в трясине).'),
 ('WorldCreature','stepCue'):('Шаги','При движении, тихо, только если игрок ближе 18 м.'),
 ('WorldCreature','sound'):('Отпочкование','Из тела вырываются новые существа (отросток тролля, рои пчёл у мутанта) при потере здоровья.'),
 ('WorldCreature','bodyLoop'):('Постоянный звук тела','Луп, пока враг жив и игрок рядом; спокойный — тише, во льду — молчит.'),
 ('EnemyProjectileProfile','fireCue'):('Выстрел','Враг выпускает снаряд «{p}».'),
 ('EnemyProjectileProfile','impactCue'):('Попадание снаряда','Снаряд «{p}» во что-то врезался.'),
 ('EnemyProjectileProfile','shotDownCue'):('Снаряд сбит','Игрок сбил снаряд «{p}» картой.'),
 ('EnemyProjectileProfile','flightLoop'):('Полёт снаряда','Луп, пока снаряд «{p}» летит.'),
 ('PlayerStatusDefinition','startCue'):('Дебафф на игроке','Наложил на игрока «{s}» — звук начала эффекта.'),
 ('PlayerStatusDefinition','tickCue'):('Тик дебаффа','Каждый тик «{s}» на игроке.'),
 ('PlayerStatusDefinition','endCue'):('Дебафф кончился','«{s}» прошёл по таймеру.'),
 ('PlayerStatusDefinition','removedCue'):('Дебафф снят','«{s}» сняли действием.'),
 ('ChestMimic','breath'):('Дыхание','Тихий луп вблизи — выдаёт мимика, только если подойти близко.'),
 ('ChestMimic','warnCue'):('Предупреждение','Вздрагивает перед укусом; тише и выше — когда его потревожили.'),
 ('ChestMimic','biteCue'):('Укус','Кусает игрока, открывшего сундук.'),
 ('ChestMimic','hopCue'):('Прыжок','Убегает скачками.'),
 ('ChestMimic','rattleCue'):('Звон монет','Монеты гремят внутри, пока он скачет.'),
 ('ChestMimic','hurtCue'):('Получил урон','Попадание по мимику.'),
 ('ChestMimic','deathCue'):('Смерть','Гибель мимика.'),
}
ORDER=['bodyLoop','alertCue','threatCue','windupCue','strikeCue','fireCue','flightLoop','impactCue','shotDownCue','startCue','tickCue','endCue','removedCue','hurtCue','deathCue','sound','stepCue','breath','warnCue','biteCue','hopCue','rattleCue']
GROUP={'WorldCreature':'Тело','ChestMimic':'Тело','EnemyProjectileProfile':'Снаряд','PlayerStatusDefinition':'Дебафф на игроке'}

audio={}
def enc(path):
    if path in audio: return audio[path]
    mp3=subprocess.run(['ffmpeg','-v','error','-i',path,'-ac','1','-b:a','96k','-f','mp3','-'],capture_output=True).stdout
    audio[path]='a%d'%len(audio); data_uri[audio[path]]='data:audio/mpeg;base64,'+base64.b64encode(mp3).decode()
    dur[audio[path]]=float(subprocess.run(['ffprobe','-v','error','-show_entries','format=duration','-of','csv=p=0',path],capture_output=True,text=True).stdout.strip() or 0)
    return audio[path]
data_uri={};dur={}

def sprite_png(folder,prefabname):
    d=A+'/Content/Enemies/'+folder
    atlas=[f for f in os.listdir(d) if f.endswith('Atlas.png')][0]
    if prefabname=='Budding troll bud':
        im=Image.open(d+'/BuddingTroll_small_offshoot.png').convert('RGBA')
    else:
        meta=txt(d+'/'+atlas+'.meta'); im=Image.open(d+'/'+atlas).convert('RGBA')
        m=re.search(r'name: (\w*DefaultFront)\n(?:.*\n){0,6}?\s*rect:\n\s*serializedVersion: 2\n\s*x: ([\d.]+)\n\s*y: ([\d.]+)\n\s*width: ([\d.]+)\n\s*height: ([\d.]+)',meta)
        x,y,w,h=[int(float(v)) for v in m.groups()[1:]]
        H=im.size[1]; im=im.crop((x,H-y-h,x+w,H-y))
    bb=im.getbbox(); im=im.crop(bb)
    b=io.BytesIO(); im.save(b,'PNG'); return 'data:image/png;base64,'+base64.b64encode(b.getvalue()).decode(), im.size

enemies=[]
for n in sorted(os.listdir(A+'/Game/Prefabs')):
    if not n.endswith('.prefab'): continue
    p=A+'/Game/Prefabs/'+n
    if 'alertCue:' not in txt(p): continue
    name=n[:-7]
    folder='BuddingTroll' if name.startswith('Budding troll') else ''.join(w.capitalize() for w in name.split())
    man=json.load(open(A+'/Content/Enemies/%s/manifest.json'%folder))
    t=txt(p)
    entries=scan(p)
    enemies.append(dict(prefab=name,folder=folder,ru=man.get('display_name',name)+(' — отросток' if name=='Budding troll bud' else ''),type=man.get('type',''),entries=entries,sprite=sprite_png(folder,name)))
# mimic
mp=A+'/Expedition/Prefabs/Chests/Mimics/Mimic 2 Common.prefab'
mi=Image.open('/dev/stdin' if False else R+'/output/mimic/mimic-seq3.png').convert('RGBA').crop((72,66,250,236))
b=io.BytesIO(); mi.save(b,'PNG')
enemies.append(dict(prefab='Mimic (все 5 тиров)',folder='',ru='Мимик-сундук',type='3D, не спрайт',entries=scan(mp),sprite=('data:image/png;base64,'+base64.b64encode(b.getvalue()).decode(),mi.size),photo=True))

# usage counts per cue
use={}
for e in enemies:
    for c in {x['cue']['id'] for x in e['entries']}: use.setdefault(c,set()).add(e['ru'])
TYPES={'aggressive':'агрессивный','peaceful':'мирный','timid':'пугливый','territorial':'территориальный'}
out=[]
for e in enemies:
    rows=[]
    for x in e['entries']:
        key=(x['owner'],x['field'])
        if key not in FIELD: 
            rows.append(dict(group='?',title=x['field'],desc=x['owner'],cue=x['cue'])); continue
        title,desc=FIELD[key]
        pn=next((c[0] for c in x['chain'] if c[1]=='Projectiles'),'')
        sn=next((c[0] for c in x['chain'] if c[1]=='Statuses'),'')
        if x['owner']=='PlayerStatusDefinition' and not sn: sn=x['chain'][-1][0] if x['chain'] else ''
        desc=desc.format(p=pn,s=sn)
        if x['owner']=='PlayerStatusDefinition' and pn: desc+=' (через снаряд «%s»)'%pn
        rows.append(dict(group=GROUP.get(x['owner'],x['owner']),order=ORDER.index(x['field']) if x['field'] in ORDER else 99,title=title,desc=desc,cue=x['cue']))
    # dedupe identical
    seen=set();rr=[]
    for r in sorted(rows,key=lambda r:(['Тело','Снаряд','Дебафф на игроке'].index(r['group']) if r['group'] in ['Тело','Снаряд','Дебафф на игроке'] else 9,r.get('order',99))):
        k=(r['title'],r['desc'],r['cue']['id'])
        if k in seen: continue
        seen.add(k); rr.append(r)
    out.append(dict(ru=e['ru'],prefab=e['prefab'],type=TYPES.get(e['type'],e['type']),img=e['sprite'][0],w=e['sprite'][1][0],h=e['sprite'][1][1],photo=e.get('photo',False),
        sounds=[dict(group=r['group'],title=r['title'],desc=r['desc'],cue=r['cue']['id'],shared=len(use[r['cue']['id']]),vol=float(r['cue']['volume'] or 1),pitch=[float(v) for v in re.findall(r'[\d.]+',r['cue']['pitch'] or '1 1')][:2],
                     clips=[dict(a=enc(c),file=os.path.relpath(c,A)) for c in r['cue']['clips']]) for r in rr]))
PRJ={'Beer mug':'пивная кружка','Honey glob':'медовый плевок','Beer mug':'пивная кружка','Blood clot':'сгусток крови','Web':'паутина','Toad slime':'жабья слизь','Agaric spit':'плевок мухомора','Baseball':'бейсбольный мяч'}
for e in out:
    if ' — ' in e['ru'] and not e['ru'].endswith('отросток'): e['ru']=e['ru'].split(' — ')[0]
    for s in e['sounds']:
        for k,v in PRJ.items(): s['desc']=s['desc'].replace('«%s»'%k,'«%s»'%v)
d=dict(enemies=out,audio=data_uri,dur=dur,total=len(enemies))
t=open(os.path.join(HERE,'template.html')).read()
open(os.path.join(HERE,'enemy-sounds.html'),'w').write(t.replace('__DATA__',json.dumps(d,ensure_ascii=False)))
print(len(out),'enemies',len(data_uri),'clips', sum(len(v) for v in data_uri.values())//1024,'KB')
for e in out: print(e['ru'],e['type'],len(e['sounds']),[s['cue'] for s in e['sounds']])
