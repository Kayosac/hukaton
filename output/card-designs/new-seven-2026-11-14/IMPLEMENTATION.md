# Семь новых карт — реализация в игре (ветка swamp/biome, worktree worktrees/swamp)

Пользователь 14.11.2026 дал «делай»: реализовать карты из таблицы KayoSpace `tb_hakaton-cards` (строки с пометкой
«[Новая карта — пока только дизайн, в игре нет.]»). Числа, ранг и цену брать ИЗ ТАБЛИЦЫ (пользователь их правит там),
а не из этого файла. Работать по одной карте, по порядку. Арт уже нарисован и утверждён (все семь, включая Веер).

## Порядок и арт (папка output/card-designs/new-seven-2026-11-14/)

| # | Карта | id | Стихия · тип | Арт (утверждён) |
|---|---|---|---|---|
| 1 | Струя | water_jet | Вода · АТАКА | block-01/water_jet.png |
| 2 | Камнепад | rockfall | Земля · АТАКА | block-01/rockfall.png |
| 3 | Сглаз | evil_eye | Тьма · АТАКА | block-01/evil_eye.png |
| 4 | Вороньё | crows | Звери · АТАКА | block-02/crows.png |
| 5 | Пороховая бочка | powder_barrel | Огонь · ЛОВУШКА | block-02/powder_barrel.png |
| 6 | Бумеранг | boomerang | Бой · УРОН | block-02/boomerang.png |
| 7 | Веер | fan | Бой · УРОН | block-03/fan.png |

## Решения пользователя по механике

- Струя: ранит, слегка отбрасывает, делает цель мокрой (существующий WetStatus — Молния ×2, Лёд заковывает), горящего зверя тушит.
- Камнепад: через 0,5 с после попадания на зону падают камни; ранят всех существ в радиусе и игрока, если он там; мелких зверей оглушают. Радиус зоны показывается кольцом при прицеливании и контуром после броска (общее правило для карт с зоной).
- Сглаз: попадание вешает чёрную метку; через 2 с она лопается и наносит большой урон. Зверь умер раньше — метка пропадает. Эхо повторяет метку.
- Вороньё: три вороны слетаются на зверя и клюют (урон в секунду, 4 с); пока клюют, зверь отвлечён и НЕ нападает.
- Пороховая бочка: ставит бочку на землю, стоит до конца вылазки. Взрывается ТОЛЬКО от огня: карта Огонь, горящий зверь рядом, горящая смола, пламя от Сквозняка (и любой другой источник поджига игры). Не от удара, не по таймеру. Взрыв ранит, поджигает и отбрасывает всех в радиусе, включая игрока. Бочка — видимый твёрдый объект с коллайдером.
- Бумеранг: карта летит по дуге и возвращается, раня всех на пути туда и обратно (одного зверя — не чаще раза на каждом отрезке). Ловится САМА, если долетела до игрока — тогда снова в руке; не долетела (упёрлась в стену и т. п.) — в сброс.
- Веер: один бросок выпускает 3/5/7 карт веером (угол 40°); каждая ранит при попадании; тратится одна копия.
  Решено при реализации (не пользователем, можно пересмотреть): Эхо, повторяя Веер, бьёт в своей точке сразу всеми
  картами веера уровня Эха — 3 / 5 / 14 HP, ровно как Веер в упор (одна карта, 1 / 1 / 2, сделала бы Эхо — ранг 5, 450
  монет — бесполезным; веер из точки эха бьёт в ту же стену, куда эхо попало). Сравнимо со Сглазом III (14). Строка ПК —
  «Карт в веере 3 · 5 · 7»: растёт на каждом уровне и решает, скольких заденет веер; урон каждой меняется только на III.

## Правила проекта (обязательные)

- Git: worktree на Маке, его .git указывает на чужой путь. Перед git-командами:
  `cd $HOME/mnt/hukaton/worktrees/swamp && export GIT_DIR=$HOME/mnt/hukaton/.git/worktrees/swamp GIT_WORK_TREE=$PWD`.
  Checkout разреженный (sparse): из output/ видна только часть. Коммитить только свои пути (никаких add -A / commit -a /
  stash / reset / checkout чужого). Коммит после каждой карты, сообщение по-русски, с хвостом
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Не трогать неотслеживаемое `hukaton/Assets/SwampArt/Editor/`.
  Работать ТОЛЬКО в worktrees/swamp — не в main и не в других worktree, не слать команды чужим редакторам.
- Unity: открытый редактор этого worktree управляется через `hukaton/Logs/bridge/inbox/<имя>.cmd` (refresh, play, stop,
  open <scene>, tests EditMode|PlayMode [filter], shot <name> [superSize], invoke <Type.Method> [arg], timescale <x>);
  ответ — `hukaton/Logs/bridge/<имя>.out`, компиляция — compile.log, консоль — console.log, состояние — state.txt.
  Имена своих .cmd начинать с `nc-`. Подробности — Assets/Dev/Editor/EditorBridge.cs. После правок кода — refresh и
  проверка compile.log без ошибок.
- Новая карта = значение в `CardAction` (Assets/Decks/Runtime/CardDefinition.cs; НЕ перенумеровывать, продолжать с 36),
  `Assets/Game/Data/<id>.asset` (CardDefinition: displayName, action, artwork, accent, effectAsset, короткое description
  по-русски, purchasePrice и rank из таблицы, uses 1/1/1), запись в `Assets/Game/Data/Cards.asset` (список cards),
  эффект `Assets/Expedition/Runtime/Cards/<Name>/<Name>Effect.cs` (наследник CardEffect; числа уровней I/II/III — LevelInt/
  LevelFloat в ассете эффекта; ICardStatLine где уместно), ассет `Assets/Game/Data/Effects/<Name>.asset`, префабы —
  `Assets/Game/Prefabs/Cards/<Name>/`, EditMode-тесты правил — `Assets/Expedition/Tests/Editor/<Name>Tests.cs`.
  Образцы: Lightning, Bubble, Scarecrow, Discord, TarPuddle, Lure, Echo, Marauder, StrikeEffect, FireEffect.
  Проверить, нет ли других реестров/switch'ей по CardAction или по списку карт (тексты, магазин гоблина, Эхо, тесты
  каталога вроде CardFoundationTests) — новую карту добавить и туда.
- Арт: скопировать PNG в `Assets/Decks/Art/<id>.png`, импорт как у соседних карт (Sprite Single, PPU 1, без сжатия,
  Max Size 2048 — образец `discord.png.meta`, guid новый). Картинку — в ячейку «Изображение» таблицы KayoSpace
  (инструмент kayospace write_table / add_image_file, если доступен; иначе сообщить).
- Визуальные эффекты — по максимуму из пака Retro Arsenal (уже в проекте), не самодельные. Звуки — из проекта.
  Никаких эмодзи. В мире никаких невидимых коллайдеров.
- Правила AGENTS.md про Unity: всё настроено в ассетах/префабах, без генерации в рантайме; одноразовые
  скрипты-сборщики удалить после использования.
- Проверка: компиляция, EditMode-тесты карты и смежные, затем вживую в сцене `Assets/EnemyDebug/Scenes/EnemyDebug.unity`
  (play + shot) — скриншот работы карты. Не заявлять проверок, которых не было.
- После реализации: в таблице KayoSpace снять из описания пометку «[Новая карта — пока только дизайн, в игре нет.]»,
  обновить статус ниже.

## Где регистрировать новую карту (найдено на Струе)

- Обязательно: `CardAction` (CardDefinition.cs), `Assets/Game/Data/<id>.asset`, строка в `Cards.asset` (cards),
  эффект в `Assets/Expedition/Runtime/Cards/<Name>/<Name>Effect.cs`, namespace `Hakaton.Expeditions.Cards`,
  обязательно `ICardStatLine` (CardFoundationTests требует строку числа у всех карт с action >= 10, кроме Телепорта).
- Других реестров нет: ПК берёт число из ICardStatLine (DeckPrototype.cs, switch только для старых карт), гоблин
  берёт карты из Cards.asset по рангу, Эхо повторяет всё, кроме `neverRepeat` (EchoEffect) и Телепорта.
  CardCatalogueTests проверяет только исходные 22 карты; новые — общими правилами (арт, эффект, описание, уникальный action).
- Живая проверка: в EnemyDebug в руке только «Удар» (EnemyDebugStrikeCard требует StrikeEffect), простого способа
  выдать другую карту нет. Струю проверяли временным статическим зондом через `invoke` (эффект.Apply по живой копии), зонд удалён.
- Готовые API: мокрота `WetStatus.Soak(body, world, seconds)`, значок игроку `PlayerStatusBadge.Show(world, world.vfx.wetBadge)`;
  тушение зверя `WorldCreature.Douse()`, смолы `TarPuddleZone.Extinguish()`; урон `WorldCreature.Damage(amount, impulse, point, burnSeconds)`.
- Карта с задержкой/зоной (найдено на Камнепаде): эффект ставит префаб-зону `CardZoneBehaviour` + `ICardAreaOutline`
  (контур рисуется, пока `TryGetOutline` true) и отвечает `LeavesOutlinedZone => true`; кольцо при прицеливании —
  `ICardArea.TryGetArea`. Образцы: Fog, Lure, Rockfall.
- Урон по площади: перебор `world.Combatants(list)` (игрок, звери, союзники, мимики) и `fighter.TakeDamage(DamageInfo)`;
  игроку то же число в половинках сердца. Горящая смола и цепная молния бьют всех без разбора сторон.
- «Мелкий» зверь = масса `body.mass` (μ) не больше 1 (масса игрока); оглушение — `WorldCreature.Stagger(seconds, from)`.
- В EnemyDebug у отладочного игрока НЕТ `CardAreaDisplay` (кольца/контуры не видны) — для проверки зонд временно
  добавляет его с `ringPrefab` из Player.prefab. Команда `timescale` не держится: HitStop возвращает 1 после удара,
  поэтому кадр «до обвала» снимать командой `shot` в том же опросе, что и `invoke` (оба .cmd положить разом).

- Карта с отложенным уроном по существу (найдено на Сглазе): объект-метка через `world.SpawnForOuting` (уберётся в конце
  вылазки), каждый кадр догоняет `ICombatant.Position/Centre` цели (рост = 2 × (Centre.y − Position.y), учитывает
  Кривое зеркало); цель умерла/уничтожена — `Combat.IsAlive(target) && target.Root` ложно, метка пропадает. Типа урона
  «тьма» нет: взят `DamageKind.Other` (не колет ледяную глыбу ×2, как `Hit`). WorldCreature НЕ реализует ICardCreature
  (только мимик) — проверять `is WorldCreature || is ICardCreature`. Живого зверя в EnemyDebug зонд создаёт сам
  (Instantiate префаба + `world = …`, `ResetForOuting(null)`, `world.creatures.Add`); агрессивный сразу бежит на игрока —
  для кадра его замораживать (`Freeze`). Две команды `shot` ближе ~0,3 с друг к другу — второй кадр теряется.

- Отвлечение зверя (найдено на Вороньё): интерфейс `IDistractable` (CardSystems/Distraction.cs) у `WorldCreature`
  (WorldCreature.Distraction.cs) и `ChestMimic`. Источник продлевает `Distract(seconds)` каждый шаг и снимает
  `EndDistraction()`, уходя. Отвлечённый зверь в Update после заморозки/оглушения уходит в Recover (атака, выстрел,
  захват прерываются), стоит и дёргается; пугливый бегает как обычно, подманенный Манком идёт к шуму. Мимик не кусает
  (удирает). Манок/Чучело/Раздор не тронуты. Готовых спрайтов птиц и звука карканья в проекте нет: вороны нарисованы
  PIL-скриптом (64×48, PPU 100 = сетка), карканье — frog_croak с pitch 1.45–1.7, клевок — wolf_snap с pitch 1.6–1.9.

- Поджиг (найдено на Пороховой бочке): общая система — `ICardZone` + `IIgnitable` в `world.Zones`; поджигают
  `world.Ignite(collider, point, reach, source)` (Огонь, Молния), `world.Ignite(feet, height, radius, reach)` (горящий
  уязвимый к огню или осмолённый зверь, CreatureFire), `Zones.Ignite(...)` (вспышка газа GasVent, вспыхнувшее Чучело,
  взрыв бочки), Сквозняк — всем `IIgnitable` по ветру. Горящая смола раньше соседей НЕ поджигала — теперь поджигает
  (`TarPuddleZone.SpreadFire`, раз в 0,25 с, досягаемость 0,3 м). Обычный (не уязвимый) горящий зверь касанием ничего
  не поджигает — бочка сама проверяет горящих зверей вплотную. Мокрота у горючих зон раньше была только «дождь
  смывает/гасит» (`IWashable`); добавлен `ISoakable` (Струя мочит), бочка мокнет через общий `WetStatus` (30 с).
- Модели: болотный кит Blender (`output/biomes/rotten-swamp/models-v1/_kit/sk.py`) работает в облачном контейнере с
  `pip install bpy==5.3.0` (на Маке/VM Blender нет); папка `_kit/tex` слишком глубоко для device_stage_files —
  копировать во временную папку выше. Бочка: `powder_barrel-model/build.py` (+ card.jpg).
- EnemyDebug лечит игрока сразу после урона; полученный урон виден в HUD («Получено урона»).

- Свой полёт карты (найдено на Бумеранге): интерфейс `ICustomFlight` у эффекта (Expedition/Runtime/CardCustomFlight.cs).
  `CardProjectile.Launch` спрашивает `BeginFlight` и дальше каждый FixedUpdate отдаёт шаг объекту `ICardFlightPath`
  (двигает карту `MoveTo`, кончает полёт `Bounce` — удар/отскок/падение, `Catch` — поймали, `Drop` — время вышло;
  `Resolve` для карты со своим полётом тоже ведёт в `Bounce`, без втыкания). Превью у прицела (`CardThrower.UpdatePreview`)
  берёт путь из `ICustomFlight.PreviewPath` — те же правила, что у полёта. `MayComeBack` → `Expedition.Release(id, true)`:
  карта висит в `Held` даже без использований (в её место ничего не добирается); `Expedition.Catch(id)` — назад в руку с
  возвратом использования, `Settle` без поимки — в сброс. Для Веера (несколько снарядов за бросок) хук лучше ставить в
  `ExpeditionWorld.Launch` (там рождаются снаряды), полёт каждого остаётся своим.
- EnemyDebug теперь умеет бросать любую карту: клавиша T (поле `nextCardKey` у EnemyDebugStrikeCard) меняет карту в руке на
  следующую из каталога сессии `EnemyDebug/Data/Isolated Session.asset` (`Expedition.Withdraw` + `GrantToHand`). Чтобы
  проверить новую карту — добавить её определение в этот список. В прологе карта бесконечна (UsesLeft = MaxValue), поэтому
  Бумеранг там возвращается в руку и после удара о стену; разница «поймали / в сброс» проверена EditMode-тестами колоды.
- Команды через мост: после stop изменённый в этот момент .cs может не перекомпилироваться (compile.log со старым
  временем) — проверять время FINISHED и при необходимости поменять файл ещё раз и refresh.

- Несколько снарядов за бросок (найдено на Веере): интерфейс `ICardVolley` у эффекта (Expedition/Runtime/CardVolley.cs).
  `CardThrower.Release` как обычно один раз выпускает копию (`Expedition.Release`, `Thrown`/`AnyThrown`, память Эха), затем
  `ExpeditionWorld.Launch` видит `ICardVolley` и запускает `Volley(...)` обычных `CardProjectile` той же копии (каждый со
  своей баллистикой и ударом; у первого мига — свой наклон в руке `handRolls`). Скорости веера считаются от уже отклонённого
  кривыми руками броска — отклоняется весь веер. Общий `CardVolley`: `CardProjectile.OnDestroy` зовёт `Settle` только для
  последней карты залпа (`Finish` true ровно раз) — копия с использованиями (пролог/EnemyDebug) не возвращается в руку,
  пока летит хоть одна карта. Снаряд карты без коллайдеров, слой «Cards» вне маски — карты залпа друг друга не сбивают;
  вражеский снаряд, задетый несколькими картами, сбивается один раз (`EnemyProjectile.ended`). Возвращает `Launch`
  среднюю карту, все — `projectile.Volley.Cards` (сокет в стене — `Resolve` для всех). Флаттер-петля — только у средней
  и крайних (`JoinVolley(..., громкость)`), иначе 7 петель съедают пул SfxPlayer.
- Превью залпа: `CardThrower` рисует путь каждой карты; боковые линии — копии авторской `trajectory` (Instantiate при
  первом прицеливании залпом, `volleySideWidth/Alpha`). Эхо отличает повтор по копии: `c.Copy.Spec.Id` («echo») не равен
  `c.Definition.id` (`FanRules.IsEcho`).
- Живой бросок из руки в EnemyDebug без мыши: временный редакторный зонд (своя asmdef со ссылками на Hakaton.EnemyDebug,
  Expedition, Character, Decks) — `EnemyDebugStrikeCard.level` + `Next()` до нужной карты, `thrower.Begin()`,
  `Preparation.Tick(...)`, `thrower.Release()`. `timescale` не держится (TimeFeel — единственный хозяин масштаба времени),
  замедлять через `world.time.SlowMotion(сек, масштаб)`. Редактор в фоне даёт ~3 кадра/с — кадры полёта снимать
  `ScreenCapture.CaptureScreenshot` из `EditorApplication.update` по `Time.time` броска, а не командой `shot`.
  Команды моста писать в `inbox/<имя>.tmp` и `mv` в `.cmd` в той же папке: `mv` из /tmp — копия, мост может прочитать
  пустой файл («FAILED unknown command:»). Карты летят по баллистике: целиться выше груди (на 6 м — примерно на 1,15 м).
- Isolated Session (EnemyDebug) теперь содержит все новые карты: strike, boomerang, water_jet, rockfall, evil_eye, crows,
  powder_barrel, fan — каждую проверили броском из руки по толпе псов, ошибок в консоли нет.

## Статус

| Карта | Статус |
|---|---|
| Струя | готово (adb4640f0): WaterJetEffect, тесты WaterJetTests 7/7, вживую в EnemyDebug — checks/water_jet-*.png; таблица KayoSpace — за пользователем |
| Камнепад | готово (a6122059b): RockfallEffect + зона RockfallZone, тесты RockfallTests 8/8, вживую в EnemyDebug — checks/rockfall-*.png; таблица KayoSpace — за пользователем |
| Сглаз | готово (efbc46f62): EvilEyeEffect + метка EvilEyeMark, тесты EvilEyeTests 9/9, вживую в EnemyDebug (две метки через Эхо на замороженном звере, взрыв, промах) — checks/evil_eye-*.png; таблица KayoSpace — за пользователем |
| Вороньё | готово (870336b98): CrowsEffect + стая CrowFlock, отвлечение IDistractable (WorldCreature, ChestMimic), тесты CrowsTests 14/14, вживую в EnemyDebug (болотный мутант L2: 4 с без урона игроку, 16→8 HP, потом снова нападает; пёс L3 — смерть, вороны разлетаются; промах) — checks/crows-*.png; таблица KayoSpace — за пользователем |
| Пороховая бочка | готово (57ff0cd31): PowderBarrelEffect + бочка PowderBarrel (зона IIgnitable/IWashable/ISoakable, Rigidbody), своя кривая модель SM_PowderBarrel (болотный кит), тесты PowderBarrelTests 12/12, вживую в EnemyDebug (удар и Камнепад не взрывают; Огонь — фитиль 0,4 с и взрыв: звери −5 HP, горят, отброшены; цепь из двух бочек; мокрая не горит; игрок рядом −5 и отброшен) — checks/powder_barrel-*.png; таблица KayoSpace — за пользователем |
| Бумеранг | готово (301b7c7a4): BoomerangEffect + свой полёт BoomerangFlight/BoomerangRules через новый ICustomFlight, поимка → та же копия в руке (Expedition.Catch), тесты BoomerangTests 15/15, вживую в EnemyDebug (три пса в ряд по −2 HP, возврат и поимка через ~1,5 с, удар о куб — отскок и падение, превью дуги) — checks/boomerang-*.png; EnemyDebug: T — сменить карту; таблица KayoSpace — за пользователем |
| Веер | готово (90f07418f): FanEffect + залп ICardVolley/CardVolley (3/5/7 карт в 40°, урон каждой 1/1/2, одна копия, Settle раз после последней карты), превью всех путей, тесты FanTests 9/9, вживую в EnemyDebug броском из руки (III по толпе из 5 псов в 6 м — 7 из 7 карт в цель, все пятеро ранены; в упор жаба 20 → 6 HP, вся пачка; кривые руки сдвинули весь веер) — checks/fan-*.png; таблица KayoSpace — за пользователем |
