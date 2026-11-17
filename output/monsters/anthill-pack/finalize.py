from pathlib import Path
from PIL import Image,ImageDraw
import json,hashlib,re
ROOT=Path(__file__).resolve().parents[3];PACK=Path(__file__).resolve().parent
names={'AntWorker':'Муравей-работяга','AntPlug':'Муравей-пробка','AntMachineGunCrew':'Муравьиный пулемётный расчёт','WingedAnt':'Крылатый муравей','BubbleLarva':'Личинка-пузырь','AntQueen':'Матка'}
def dump(p,d):p.write_text(json.dumps(d,ensure_ascii=False,indent=2)+'\n')
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
t=json.loads((PACK/'kayospace-readback.json').read_text());regpath=ROOT/'docs/enemies/kayospace-registry.json';reg=json.loads(regpath.read_text())
affected=[]
for enemy,name in names.items():
 row=next(r for r in t['rows'] if r['values']['Враг']==name);attachment=row['images']['Спрайт врага'][0]
 dest=ROOT/'hukaton/Assets/Content/Enemies'/enemy;out=PACK/enemy
 rec=next((r for r in reg['rows'] if r['enemy']==enemy),None)
 if rec is None:
  rec=dict(enemy=enemy,name=name,row_index=len(reg['rows']));reg['rows'].append(rec)
 rec.update(behavior=row['values']['Описание поведения'],combat_pattern=row['values']['Описание паттерна боя'],category='Агрессивный',
            gameplay_implemented=False,health=None,damage_half_hearts=None,health_text='Не определено',damage_text=row['values']['Урон (сердца)'],
            loot_text=row['values']['Добыча'],manifest=str((dest/'manifest.json').relative_to(ROOT)),atlas=str((dest/(enemy+'Atlas.png')).relative_to(ROOT)))
 rec.setdefault('kayospace',{}).update(table_id=t['table'],row_id=row['id'],sprite_asset_id=attachment['asset'],
   sprite_column_type='url',sprite_cell_value=None,image_attachment=attachment,row_height=row['height'],
   image_attachment_readback_verified=True,image_retrieved_and_visually_inspected=True)
 if enemy!='AntMachineGunCrew':
  registry=out/'registry.png';assert sha(registry)==attachment['asset']
  rec.update(sprite=str(registry.relative_to(ROOT)),sprite_sha256=sha(registry),sprite_size=[attachment['w'],attachment['h']],loot=[],loot_assigned=False,prefab=None)
  m=json.loads((dest/'manifest.json').read_text());m['registry']={'table':t['table'],'row':row['id'],'attachment':attachment['asset'],'verified':True,'retrieved_and_visually_inspected':True}
  m['brief']=row['values']['Описание поведения'];m['combat_concept']=row['values']['Описание паттерна боя']
  for p in [dest/'manifest.json',out/'manifest.json']:dump(p,m)
  # Keep future density resampling compatible with Unity-generated GUIDs and sprite IDs.
  actual=(dest/(enemy+'Atlas.png.meta')).read_text();src=ROOT/'output/sprite-density/originals/Enemies'/enemy/(enemy+'Atlas.png.meta');old=src.read_text()
  old=re.sub(r'^guid: .*',re.search(r'^guid: .*',actual,re.M).group(),old,flags=re.M)
  amap={}
  for block in re.split(r'(?=^    - serializedVersion: 2\n      name:)',actual,flags=re.M)[1:]:
   n=re.search(r'^      name: (.*)',block,re.M).group(1);sid=re.search(r'^      spriteID: (.*)',block,re.M).group(1);iid=re.search(r'^      internalID: (.*)',block,re.M).group(1);amap[n]=(sid,iid)
  parts=re.split(r'(?=^    - serializedVersion: 2\n      name:)',old,flags=re.M)
  for i in range(1,len(parts)):
   n=re.search(r'^      name: (.*)',parts[i],re.M).group(1);sid,iid=amap[n]
   parts[i]=re.sub(r'^      spriteID: .*','      spriteID: '+sid,parts[i],flags=re.M)
   parts[i]=re.sub(r'^      internalID: .*','      internalID: '+iid,parts[i],flags=re.M)
  old=''.join(parts)
  old=re.sub(r'    nameFileIdTable:[\s\S]*?(?=\n  [^ ])','    nameFileIdTable:\n'+''.join(f'      {n}: {i}\n' for n,(_,i) in amap.items()).rstrip(),old)
  src.write_text(old)
  # Strict asset-folder scope.
  assert {p.name for p in dest.iterdir() if p.suffix!='.meta'}=={enemy+'Atlas.png','manifest.json','unity-verification.json'}
 affected.append(dict(enemy=enemy,row=row['id'],attachment=attachment['asset'],attachment_retrieved=True,visual_identity_verified=True))
reg['enemy_count']=len(reg['rows']);reg['updated_on']='2026-11-16';reg['table_columns']=t['columns']
reg['anthill_pack_update']={'date':'2026-11-16','table':'tb_hakaton-enemies','five_rows_added_one_updated':True,'rows':affected,
 'columns_and_unrelated_rows_preserved':True,'row_height':172,'all_six_rows_read_back':True,'all_six_images_retrieved_and_visually_inspected':True,
 'table_layout_screenshot_review':False,'concept_only':True,'gameplay_verified':False,'source':'output/monsters/anthill-pack/kayospace-readback.json'}
dump(regpath,reg);dump(PACK/'registry-verification.json',reg['anthill_pack_update'])
overview=Image.new('RGB',(1500,1000),(232,225,210));draw=ImageDraw.Draw(overview)
for i,(enemy,name) in enumerate(names.items()):
 source=(PACK/enemy/'raw/front-default.png') if enemy!='AntMachineGunCrew' else PACK/'references/AntMachineGunCrew.png'
 im=Image.open(source).convert('RGBA');mask=im.getchannel('A').point(lambda v:255 if v>=128 else 0);bbox=mask.getbbox()
 # Cropping transparent margins is for this display-only overview, never for masters/atlases.
 im=im.crop(bbox);im.thumbnail((440,420),Image.Resampling.LANCZOS);x=(i%3)*500+(500-im.width)//2;y=(i//3)*500+450-im.height
 overview.paste(im,(x,y),im);draw.text(((i%3)*500+20,(i//3)*500+470),enemy,fill=(35,30,25))
overview.save(PACK/'overview.png')
readme='''# Муравейник — пакет спрайтов\n\nСоздано 16.11.2026: пять новых врагов по семь состояний (35 PNG-мастеров). Существующий пулемётный расчёт сохранён без перерисовки и повторно проверен. Всего шесть типов, 42 импортированных спрайта.\n\n![Шесть врагов](overview.png)\n\n## Атласы\n\n| Враг | Unity PNG | Ячейка | Атлас | Рабочая высота обычного силуэта |\n|---|---|---|---|---|\n'''
for enemy,name in names.items():
 m=json.loads((ROOT/'hukaton/Assets/Content/Enemies'/enemy/'manifest.json').read_text())
 readme+=f"| {name} | [{enemy}Atlas.png](../../../hukaton/Assets/Content/Enemies/{enemy}/{enemy}Atlas.png) | {m['cell_width']} × {m['cell_height']} | {m['width']} × {m['height']} | {m['physical_height_m']} м |\n"
readme+='''\nРазмеры и агрессивный характер — рабочие художественные допущения, не заданный игровой баланс. У новых врагов PPU 100, номинальный scale 1, общая плотность 100 texels/m.\n\n## Состав и сборка\n\nУ каждого: default front/back, aggression front, hurt front, attack front, death front/back. У смерти видимые глаза — чёрные X; на задней стороне головы глаза не рисовались. Агрессия содержит один красный восклицательный знак и короткие красные штрихи.\n\nПолноразмерные атласы новых врагов: `<Enemy>/atlas.png`, 5400 × 6000 RGBA; ячейки 1800 × 2000, 3 × 3, последние две пусты. Мастера в `<Enemy>/masters/`, исходные результаты генерации в `<Enemy>/raw/`. Сохранены все исходные RGBA-пиксели, использовано только прозрачное дополнение с целочисленными смещениями. Сетка и ground anchor заданы до упаковки; отдельные позы не масштабировались и не обрезались.\n\nUnity-текстуры получены штатным `output/sprite-density/tool/resample.py`: premultiplied resampling, hard alpha и защита тёмного контура. Это отдельные уменьшенные производные, а не замена мастеров. Их ячейки сохранены для проверки в `<Enemy>/runtime-masters/`. Оригиналы для повторного пересчёта и метаданные с актуальными Unity GUID/fileID лежат в `output/sprite-density/originals/Enemies/<Enemy>/`; записи добавлены в общий config.\n\n## Проверки\n\n- Все 35 исходников имеют RGBA и настоящие прозрачные области; непрозрачные пиксели не касаются границы исходного холста.\n- Все занятые ячейки мастер-атласов точно совпадают с мастер-PNG по RGBA; пустые ячейки прозрачны.\n- Runtime-атласы имеют равные ячейки, alpha 0/255, прозрачные неиспользованные области и размеры power-of-two не более 2048.\n- В настоящем Unity Editor 6000.3.21f1 проверены шесть атласов и 42 спрайта: Sprite/Multiple, PPU 100 у пакета муравьёв, общие pivot и FullRect bounds внутри каждого врага, NPOT None, no mipmaps, Uncompressed, Bilinear, Max Size 2048. Ячейки заданы явно по фиксированной сетке, без Automatic/Trim.\n- Последовательная смена спрайтов не меняет transform, PPU и границы ячейки. Выполнен GPU-рендер состояний и сравнение в одноразовом preview сохранённой EnemyDebug; сцены проекта не сохранялись и не изменялись. Использован материал Sprites/Default для проверки изображений, это не игровой тест HandDrawnSprite.\n- Временный Editor-скрипт и его `.meta` удалены из Assets. Исходник проверки оставлен здесь для аудита.\n\n![Сравнение в Unity](unity-scale-comparison.png)\n\n## KayoSpace\n\nОбновлена [существующая таблица врагов](kayospace://board/tb_hakaton-enemies): добавлены пять строк и обновлён концепт существующего расчёта. Все шесть строк повторно прочитаны, все шесть PNG-вложений получены через get_image и визуально проверены. Остальные строки, ширины колонок и высота 172 px сохранены. Вёрстка всей таблицы отдельным скриншотом не проверялась. ID и источники записаны в `docs/enemies/kayospace-registry.json`.\n\n## Границы результата\n\nЭто художественные ассеты и подтверждённый импорт. Gameplay, здоровье, урон, добыча, спавн, призыв, VFX кислотной лужи и отдельные projectile-спрайты не создавались. Поведение в таблице отмечено как концепт. Play-mode игровой тест не выполнялся. Изображения созданы после одобрения концептов; отдельное пользовательское одобрение каждого итогового рисунка ещё не получено.\n\n[Точные промпты](prompts.json) — встроенный imagegen, `transparent_background: true`. Актуальные стилевые примеры просмотрены в KayoSpace: расчёт, жук с копьём, петух-бейсболист и робот дварфов. Старые `output/shop-visitors` и `output/monsters/style-current.json` в этом checkout отсутствовали; использованы актуальные Assets и native-вложения таблицы.\n'''
(PACK/'README.md').write_text(readme)
print('Registry updated, source GUID/fileIDs synchronized, README and overview saved.')
