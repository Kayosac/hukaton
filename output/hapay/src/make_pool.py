"""Writes hukaton/Assets/Hapay/Data/Hapay pool.asset (first version of the хаPAY collectors).
Run from hukaton/Assets with /tmp/hapay_guids.json (script and sprite guids). After the first run the asset is
edited in Unity; this file only records how the first version was made."""
import json, os, uuid
g = json.load(open('/tmp/hapay_guids.json'))
ids = set(f[:-6] for f in os.listdir('Loot/Items') if f.endswith('.asset'))
# avatars: 0 goblin, 1 hood, 2 top hat, 3 antlers
C = [
("beekeeper","пасечник_на_пенсии",3,"собираю память о пчёлах. они не вернутся",[("beekeeper_hat",1),("beekeeper_smoker",1),("honey_jar",1),("honeycomb_chunk",3)]),
("troll","xX_ТрОлЛь_Xx",0,"собираю себя обратно",[("troll_bud",1),("troll_extra_hand",1),("troll_sinew",1)]),
("grandma","бабка_с_болота",1,"для варенья. не спрашивайте",[("mosquito_proboscis",12)]),
("connoisseur","ГИПЕР_ЦЕНИТЕЛЬ",2,"деньги не вопрос. вопрос — кто вы",[("hypercube",1),("diamond_apple",1),("golden_jug",1),("silver_ruby_goblet",1),("buddha_statuette",1),("mantel_clock",1),("magnifying_glass",1)]),
("skater","skate_or_die_1998",3,"верните мне 98-й",[("leshy_skateboard",1),("leshy_hoodie",1),("leshy_cigarette",1),("rooster_cap",1)]),
("roosters","КОЛЛЕКЦИЯ_ПЕТУХОВ",2,"кукарекаю по пятницам",[("rooster_bat",1),("rooster_cap",1),("rooster_comb",2)]),
("mechanic","механик_ищет_запчасти",0,"пульт нажимать не буду. честно",[("dwarf_scrap_claw",1),("dwarf_saucepan_helmet",1),("dwarf_beer_mug",1),("dwarf_control_panel",1)]),
("agaric","мухоморная_принцесса",1,"они смотрят. мне нравится",[("agaric_cap",2),("agaric_crooked_tooth",1),("agaric_angry_eye",1),("fly_agaric_eyes",1)]),
("blood","кровь_и_точка",1,"это для науки. для какой — не скажу",[("mosquito_blood_sac",3),("leech_blood_gland",2),("leech_sucker",1)]),
("mantis","богомол_фан_клуб",2,"молимся вместе",[("mantis_sickle",2),("mantis_wing_cover",2)]),
("toad","жабий_король",2,"поцелуй меня. нет, не надо",[("toad_sticky_tongue",1),("toad_wart_hide",1),("frog_foot",2)]),
("spider","паутина_эстет",3,"вяжу свитер",[("spider_silk",4),("spider_moss_carapace",1)]),
("dungbeetle","навозник_навсегда",0,"катаю. катаюсь",[("dung_beetle_dung_ball",2),("dung_beetle_chitin_leg",3)]),
("drowned","утопленница_ищет_своё",1,"второй сапог тоже мой",[("drowned_swimsuit",1),("drowned_waterlogged_boot",2)]),
("ghost","простыня_с_дырками",1,"холодно. очень",[("ghost_bedsheet",3)]),
("forester","лесник_из_дупла",3,"лес помнит",[("tree_bark_mask",1),("tree_embedded_axe",1),("bush_root_heart",2)]),
("circus","цирк_уехал",2,"клоуны остались",[("clown_mask_shard",5),("strange_teddy",1)]),
("pumpkin","тыквенный_вечер",0,"к празднику. к какому — неважно",[("pumpkin_jaw",3),("rat_king",1)]),
("mushroomer","грибник_2003",3,"не ешьте шляпки. я съел",[("hare_foot",3),("agaric_cap",1),("fly_agaric_eyes",1)]),
("sweettooth","сладкоежка_666",0,"мёд — это жизнь. соты тоже",[("honeycomb_chunk",5),("honey_jar",1)]),
("nostalgia","ностальгия_точка_ру",2,"раньше было лучше. наверное",[("ipod_earbuds",1),("film_camera",1),("juice_carton",1),("baby_bottle",1)]),
("grandchest","бабушкин_сундук",1,"бабушка просила вернуть",[("kerosene_lamp",1),("straw_doll",1),("battered_case",1),("mantel_clock",1)]),
("champion","чемпион_района",0,"я был лучшим. в 7 «Б»",[("school_trophy",2),("rooster_bat",1)]),
("gardener","тёмный_садовник",3,"сад растёт ночью",[("living_bonsai",1),("fly_agaric_eyes",1),("bush_root_heart",1),("snail_shell",2)]),
("poop","какашка_коллекционер",0,"без комментариев",[("poop",9)]),
("mutants","мутант_мутанту_друг",1,"дружба крепкая",[("mutant_stinger",1),("mutant_wing",1),("swamp_mutant_peg_leg",1),("swamp_mutant_wart",1)]),
("dogs","собачник_злой",0,"у меня есть собака. теперь две",[("boar_tusk",2),("wolf_pelt",1)]),
("snail","улитка_медленная",3,"не торопитесь",[("snail_shell",6)]),
("ratking","КОРОЛЬ_КРЫС",2,"нас много",[("rat_king",2),("pumpkin_jaw",1),("hare_foot",1)]),
("student","вечный_студент",2,"диплом пишу девятый год",[("magnifying_glass",1),("mantel_clock",1),("school_trophy",1),("buddha_statuette",1),("film_camera",1),("kerosene_lamp",1),("straw_doll",1),("juice_carton",1),("strange_teddy",1)]),
("delmon_debt","Delmon_где_деньги",1,"Delmon, ты мне должен. пусть платит лавка",[("honey_jar",2),("golden_jug",1),("silver_ruby_goblet",1)]),
]
bad = [w for c in C for w, _ in c[4] if w not in ids]
assert not bad, bad
def y(s): return json.dumps(s, ensure_ascii=True)
def coll(i, nick, av, quote, wants, ind='  '):
    out = f"{ind}- id: {i}\n{ind}  nick: {y(nick)}\n{ind}  avatar: {av}\n{ind}  quote: {y(quote)}\n{ind}  wants:\n"
    for w, n in wants: out += f"{ind}  - item: {w}\n{ind}    count: {n}\n"
    return out
av = [g['hapay_avatar_goblin.png'], g['hapay_avatar_hood.png'], g['hapay_avatar_tophat.png'], g['hapay_avatar_antlers.png']]
s = f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {g['HapayPool.cs']}, type: 3}}
  m_Name: Hapay pool
  m_EditorClassIdentifier: Hakaton.Hapay::Hakaton.Hapay.HapayPool
  collections:
""" + ''.join(coll(*c) for c in C) + "  avatars:\n" + ''.join(f"  - {{fileID: 21300000, guid: {a}, type: 3}}\n" for a in av) + f"""  firstLot:
    id: first
    nick: {y('верный_покупатель')}
    avatar: 2
    quote: {y('Delmon? давно не виделись. беру что есть, как раньше')}
    wants: []
  firstLotMaxKinds: 3
  lotsOnSite: 4
  noRepeat: 5
  shortestMinutes: 8
  longestMinutes: 25
  bigSetPieces: 9
  baseMultiplier: 1.3
  perPiece: 0.04
  perRarity: 0.05
  maxMultiplier: 1.6
  roundTo: 5
"""
open('Hapay/Data/Hapay pool.asset', 'w').write(s)
if not os.path.exists('Hapay/Data/Hapay pool.asset.meta'):
    open('Hapay/Data/Hapay pool.asset.meta', 'w').write(f"fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\nNativeFormatImporter:\n  externalObjects: {{}}\n  mainObjectFileID: 11400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")
print(len(C), 'collections')
