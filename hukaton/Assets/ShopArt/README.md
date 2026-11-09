# ShopArt — финальный арт лавки (v1)

Художественный слой лавки по пакету `output/shop-art-pack-v1`: модели, текстуры, материалы, префабы и отдельная сцена предпросмотра.
Слой подключён к игре через функциональные префабы `Assets/ShopBlockout/Prefabs` (см. `Assets/ShopBlockout/README.md`); здесь лежат только визуальные ассеты.

## Как посмотреть
Открыть `Assets/ShopArt/Scenes/ShopArtPreview.unity`. В `Preview Cameras` четыре камеры (включать по одной):
A — место продавца (0,05; 1,60; −0,10) Euler 12/0/0 FOV 75; B — витрина (0,40; 1,60; 1,695) Euler 23/90/0 FOV 80; вид от входа; подсобка.
Свет сцены (`Preview Lights`) и задник за дверью (`SA_PreviewBackdrop`) существуют только для превью.
Сцена рендерится тем же рендерером проекта (PC_Renderer: 640×360 + дизеринг).

## Состав
- `Models/` — 61 FBX (Blender 5.0, 1 unit = 1 м, Forward −Z / Up Y, Apply Transform, без анимаций/камер/света). Материалы не импортируются (Material Creation Mode: None), меши подключены в префабах напрямую.
- `Textures/` — 21 PNG: Point filter, без сжатия, mipmaps вкл.; тайлы пола/потолка/балок/внешних стен — Repeat, атласы — Clamp; у альфа-атласов Alpha Is Transparency + Preserve Coverage.
- `Materials/` — 20 материалов URP/Lit: Metallic 0, Smoothness 0, блики и отражения выключены. `*_Cutout` — Alpha Clip 0,5. Эмиссия только у ламп, свечей, экрана CRT и задника превью.
- `Prefabs/` — по префабу на ассет (`SA_*`), разложены по `Architecture / Furniture / Decor / Stock / Preview`. Коллайдеров нет: это визуальные префабы, физика и interaction targets остаются в существующих функциональных сборках.
- `Prefabs/SA_ShopSet.prefab` — собранная лавка (архитектура, мебель и весь декор) в координатах Shop. Товар на витрине и задник в неё не входят.

Исходник: `output/shop-art-final-v1/Source/ShopArt.blend` (редактируемые меши, не триангулированы; FBX триангулируются при экспорте).

## Стиль и форма
Простая PSX-геометрия по CARIMARA с намеренной кривизной: стены вспучены только наружу (до 8,5 см, проход не уменьшается), балки провисают, стойки и косяки завалены, столешница из трёх досок разной длины/толщины, полки прогнуты, крышка люка покороблена, мелкие предметы кособокие. Рабочие поверхности (середина столешницы, площадки витрины, пол) остаются ровными. Детали (доски, руны, ржавчина, трещины подсобки, клавиши, вентиляция) нарисованы в текстурах; часть текстур взята прямо с утверждённых листов ассетов и концептов и пересэмплирована в низкое разрешение.

## Размеры и pivots (ключевое)
Все корни мебели совпадают с измеренными root из `scene-survey.json` — при размещении в Shop ставить в те же координаты:
Counter (0,025;0;0,675), DisplayCase (1,475;0;1,695), ComputerStation (−0,995;0;−1,94), CurtainedDoorway (0,05;0;−0,60), EntranceDoor (0,05;0;2,40), CeilingRig (1,045;0;−1,71), RoomShell (0;0;0).
- Прилавок: верх средней доски 0,990, коврик `Counter_TradingPad` верх 0,998; боковые доски 0,982–0,984.
- Витрина: `Display_SlotPad_1…6` — отдельные объекты в точных центрах (X −0,035; Y 0,766/1,116/1,466; Z ∓0,31), верх 0,7835/1,1335/1,4835. Фриз под карнизом не ниже 1,725 (не закрывает верхний ряд с камеры B).
- ПК: CRT 0,65×0,48×0,40 (центр (0;1,15;−0,12), повёрнут на 4°), системный блок 0,60×0,10×0,40, клавиатура-клин; экран — отдельный `PC_Screen` (эмиссия).
- Штора: просвет между подвязанными полотнами ≥ 0,67 м, полотна двусторонние, без коллайдеров.
- Дверь: рама 1,24×2,34, чистый проём 1,04×2,22 сохранён (низ перемычки не опускается, косяки завалены наружу); створка — `Door_Hinge` на кромке петли (−0,58;0;2,455 в Shop) → `Door_Leaf` (центр −0,58;1,07;2,98).
- Подвес (пустой): `Rig_HatchHinge` (−0,36;2,65;0 локально, закрыто 0°, открыто Z −105°) → `Rig_HatchPanel`; тросы `Rig_Cable_L/R` — 6-гранные меши длиной 2 м с pivot в центре, scale Y 0,88 (как цилиндры блокаута, `cableMeshLength = 2` менять не нужно); петли `Rig_Harness_L/R`; `Rig_PullRopeRoot` → `Rig_PullRope`, `Rig_PullHandle` (0,16×0,07×0,07, Y 1,29). В `SA_ShopSet` люк сохранён открытым (как в исходной сцене).
- Потолок: проём под люком исправлен — центрирован под коробом X 0,705…1,385, Z −2,06…−1,36, закрытая крышка 0,72×0,74 перекрывает его на 0,02 со всех сторон (в блокауте проём X 0,74…1,58, Z −2,18…−1,42 был смещён). При интеграции согласовать коллайдеры потолка.
- Товар (`SA_Stock_*`): pivot — низ по центру, все в рекомендуемом объёме X≤0,28 Y≤0,23 Z≤0,39.

## Живые детали
- `SA_Candle`: меш-огонёк `Candle_Flame` выключен, вместо него `Flame` — вариант `Assets/Game/Prefabs/Effects/Retro/Candle flame.prefab` (FireTorch из Retro Arsenal без дыма, искр и звука).
- `SA_Mug`: меш `Mug_Steam` выключен, вместо него `Steam` — `Mug steam.prefab` (SmokeTinyWhite: мелкие полупрозрачные клубы, проявление и угасание, лёгкий шум).
- `T_Props`: со циферблата часов стёрты нарисованные стрелки (деления и центральная ось остались) — стрелки теперь отдельные и идут (`Assets/ShopBlockout/Prefabs/ClockHands.prefab`).

## Подключение к игре
`SA_RoomShell`, `SA_EntranceDoor` и декор комнаты вложены в `RoomShell.prefab`; `SA_Counter`, `SA_DisplayCase`, `SA_ComputerStation`, `SA_CurtainedDoorway`, `SA_CeilingRig` — в одноимённые функциональные префабы вместе со своим декором; `SA_Stock_*` — в `Assets/Game/Prefabs/Stock *.prefab`; свет превью перенесён в `ShopLighting.prefab`. Коллайдеры и ссылки геймплея остались прежними, коллайдеры потолка совпадают с новым проёмом люка. `SA_ShopSet` и `ShopArtPreview` остаются эталоном арта: в игре расстановку правят в функциональных префабах.

## Ассеты (треугольники — после триангуляции; всего 4065)
| Группа | Префаб | Tris | Габарит X×Y×Z, м | Материалы |
|---|---|---|---|---|
| Архитектура | `SA_RoomShell` | 1042 | 3.80×3.01×5.20 | M_Room_Ceiling, M_Room_Exterior, M_Room_Floor, M_Room_Timber, M_Room_WallsBack, M_Room_WallsFront |
| Декор | `SA_Banner_Diamond` | 2 | 0.19×0.53×0.01 | M_PropsCutout |
| Декор | `SA_Banner_Eye` | 2 | 0.26×0.73×0.01 | M_PropsCutout |
| Декор | `SA_Banner_Olive` | 2 | 0.18×0.54×0.01 | M_PropsCutout |
| Декор | `SA_BerryBunch` | 8 | 0.14×0.30×0.14 | M_PropsCutout |
| Декор | `SA_BootPlanter` | 34 | 0.19×0.30×0.15 | M_Props, M_PropsCutout |
| Декор | `SA_BottleGreen` | 36 | 0.06×0.18×0.08 | M_Props |
| Декор | `SA_Candle` | 40 | 0.09×0.19×0.11 | M_Props, M_PropsCutout |
| Декор | `SA_CeramicTeapot` | 110 | 0.30×0.21×0.17 | M_Props |
| Декор | `SA_ClayJar` | 28 | 0.12×0.17×0.12 | M_Props |
| Декор | `SA_CoinBowl` | 44 | 0.15×0.07×0.12 | M_Props |
| Декор | `SA_CreamJar` | 44 | 0.09×0.12×0.08 | M_Props |
| Декор | `SA_DiceBox` | 10 | 0.09×0.09×0.09 | M_Props |
| Декор | `SA_Garland_Door` | 12 | 1.10×0.35×0.03 | M_PropsCutout |
| Декор | `SA_Garland_Long` | 12 | 1.50×0.48×0.03 | M_PropsCutout |
| Декор | `SA_GemPillow` | 80 | 0.27×0.18×0.24 | M_Props, M_PropsCutout |
| Декор | `SA_HerbBundleA` | 8 | 0.13×0.25×0.13 | M_PropsCutout |
| Декор | `SA_HerbBundleB` | 8 | 0.12×0.22×0.12 | M_PropsCutout |
| Декор | `SA_HerbGarland` | 52 | 0.90×0.35×0.12 | M_Props, M_PropsCutout |
| Декор | `SA_HerbSpray_Olive` | 2 | 0.22×0.48×0.00 | M_PropsCutout |
| Декор | `SA_HerbSpray_Red` | 2 | 0.22×0.48×0.00 | M_PropsCutout |
| Декор | `SA_HerbSprigFlat` | 2 | 0.20×0.00×0.07 | M_PropsCutout |
| Декор | `SA_HexLantern` | 92 | 0.23×0.85×0.24 | M_Props, M_PropsCutout |
| Декор | `SA_HornVessel` | 52 | 0.35×0.15×0.12 | M_Props |
| Декор | `SA_KeyRing` | 2 | 0.10×0.16×0.00 | M_PropsCutout |
| Декор | `SA_Mug` | 104 | 0.16×0.27×0.11 | M_Props, M_PropsCutout |
| Декор | `SA_Pennants_Stick` | 2 | 0.57×0.27×0.00 | M_PropsCutout |
| Декор | `SA_Poster_Berry` | 2 | 0.12×0.16×0.00 | M_PropsCutout |
| Декор | `SA_Poster_Eye` | 2 | 0.21×0.20×0.00 | M_PropsCutout |
| Декор | `SA_Poster_Flower` | 2 | 0.19×0.23×0.00 | M_PropsCutout |
| Декор | `SA_Poster_Leaf` | 2 | 0.17×0.22×0.00 | M_PropsCutout |
| Декор | `SA_Poster_Leaf2` | 2 | 0.13×0.16×0.00 | M_PropsCutout |
| Декор | `SA_Poster_Star` | 2 | 0.12×0.12×0.00 | M_PropsCutout |
| Декор | `SA_Poster_Sun` | 2 | 0.13×0.13×0.00 | M_PropsCutout |
| Декор | `SA_Poster_Trees` | 2 | 0.20×0.24×0.00 | M_PropsCutout |
| Декор | `SA_Poster_Trees2` | 2 | 0.17×0.21×0.00 | M_PropsCutout |
| Декор | `SA_Pouch` | 6 | 0.18×0.32×0.01 | M_PropsCutout |
| Декор | `SA_RedParcel` | 20 | 0.18×0.15×0.16 | M_Props, M_PropsCutout |
| Декор | `SA_Rug_Diamond` | 2 | 1.30×0.00×0.75 | M_PropsCutout |
| Декор | `SA_Rug_Oval` | 2 | 1.00×0.00×0.60 | M_PropsCutout |
| Декор | `SA_Scales` | 88 | 0.45×0.39×0.16 | M_Props |
| Декор | `SA_Spool` | 20 | 0.07×0.07×0.06 | M_Props |
| Декор | `SA_Spoons` | 2 | 0.10×0.22×0.00 | M_PropsCutout |
| Декор | `SA_StripedFlag` | 2 | 0.13×0.30×0.00 | M_PropsCutout |
| Декор | `SA_TeapotLantern` | 110 | 0.26×0.85×0.16 | M_Props, M_PropsCutout |
| Декор | `SA_VaseSprig` | 40 | 0.14×0.30×0.16 | M_Props, M_PropsCutout |
| Декор | `SA_WallClock` | 22 | 0.31×0.31×0.08 | M_Props |
| Декор | `SA_WallShelf` | 14 | 0.61×0.16×0.19 | M_Props |
| Декор | `SA_WrappedBook` | 14 | 0.25×0.08×0.17 | M_Props, M_PropsCutout |
| Мебель и станции | `SA_CeilingRig` | 262 | 0.95×2.00×0.92 | M_CeilingRig |
| Мебель и станции | `SA_ComputerStation` | 194 | 1.12×1.40×0.67 | M_ComputerScreen, M_ComputerStation, M_ComputerStation_Cutout |
| Мебель и станции | `SA_Counter` | 222 | 1.75×1.00×0.60 | M_Counter |
| Мебель и станции | `SA_CurtainedDoorway` | 204 | 1.13×2.24×0.22 | M_CurtainedDoorway, M_CurtainedDoorway_Cutout |
| Мебель и станции | `SA_DisplayCase` | 412 | 0.50×1.89×1.33 | M_DisplayCase |
| Мебель и станции | `SA_EntranceDoor` | 384 | 1.38×2.44×1.23 | M_EntranceDoor, M_EntranceDoor_Cutout |
| Только для превью | `SA_PreviewBackdrop` | 4 | 2.80×3.25×2.00 | M_PreviewBackdrop |
| Товар | `SA_Stock_boar_tusk` | 33 | 0.26×0.11×0.07 | M_Stock |
| Товар | `SA_Stock_common_find` | 54 | 0.19×0.16×0.20 | M_Stock |
| Товар | `SA_Stock_hare_foot` | 30 | 0.07×0.09×0.30 | M_Stock |
| Товар | `SA_Stock_special_find` | 46 | 0.17×0.16×0.16 | M_Stock |
| Товар | `SA_Stock_wolf_pelt` | 28 | 0.27×0.14×0.15 | M_Stock |

## Текстуры
| Файл | Размер | Эмиссия |
|---|---|---|
| `T_CeilingRig` | 256×256 | — |
| `T_ComputerScreen` | 64×64 | да |
| `T_ComputerStation` | 256×256 | — |
| `T_Counter` | 256×256 | — |
| `T_CurtainedDoorway` | 256×256 | — |
| `T_DisplayCase` | 256×512 | — |
| `T_EntranceDoor` | 256×256 | — |
| `T_PreviewBackdrop` | 256×256 | да |
| `T_Props` | 512×512 | да |
| `T_PropsCutout` | 512×512 | да |
| `T_Room_Ceiling` | 256×256 | — |
| `T_Room_Exterior` | 256×256 | — |
| `T_Room_Floor` | 256×256 | — |
| `T_Room_Timber` | 128×256 | — |
| `T_Room_WallsBack` | 512×512 | — |
| `T_Room_WallsFront` | 512×512 | — |
| `T_Stock` | 256×256 | — |
