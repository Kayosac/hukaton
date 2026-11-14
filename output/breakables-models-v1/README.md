# Осколки ломаемых предметов и новый ящик — модели v1

15.11.2026. Модели в Blender (`bpy` 5.3), стиль и атласы — лесные пропсы (ForestArt). Статус: в проекте, ждёт одобрения
арта пользователем; Play-проверки не запускались.

## В Unity

- `hukaton/Assets/ForestArt/Models/Props/SM_PR17_CrateBound.fbx` — ящик с двумя железными полосами вместо заглушки
  (куб + две плиты) в `Expedition/Prefabs/Movable crate.prefab`. 0,9 м, 588 треугольников, `PR17_Crate_COL` — бокс.
  Пивот — низ по центру; в префабе модель сдвинута на −0,45 м, BoxCollider по `_COL`.
- `hukaton/Assets/ForestArt/Models/Debris/SM_DB0N_*.fbx` — наборы кусков, у каждого куска свой `_COL`.
- `hukaton/Assets/Expedition/Prefabs/Debris/DB0N *.prefab` — префабы кусков: меш, выпуклый MeshCollider из `_COL`,
  Rigidbody, IceShard (лежит и тает).
- `Game/Data/Breakables/{Clay pot, Bottle, Crate, Lantern}.asset` указывают на новые куски; включён флаг
  `piecesAtOwnSize`: куски не масштабируются по размеру разбитого и сначала выпадает весь набор по разу.
- Тыква и улей пока не ломаются: их префабы кусков готовы, вида (`BreakableKind`) у них нет.

## Наборы

| Набор | Куски | Треугольники | Материалы |
|---|---|---|---|
| DB01 Clay (горшок, кувшин) | Rim, Base, WallLong, WallMid, WallSmall | 48–92 | M_Forest_Props |
| DB02 Glass (бутылки) | Neck, Bottom, WallA, WallB (+ `_Brown`, `_Blue` в FBX) | 44–116 | M_Forest_Props |
| DB03 Crate | PlankShort, PlankSplintered, PlankNail, Corner | 60–112 | M_Forest_Props |
| DB04 Lantern | Cage (с ручкой), Lid, Glass | 28–176 | M_Forest_Props, M_Forest_PropsCutout |
| DB05 Pumpkin | SliceTall, SliceWide, SliceSmall, SliceBottom, Top | 48–120 | M_Pumpkin, M_Forest_Props |
| DB06 Beehive | WallBoard, Frame, Lid, CombChunk | 68–152 | M_Forest_Special, M_Forest_Props |

Выше бюджета 120: каркас фонаря (176, ручка с верёвкой) и рамка с сотами (152).

## Соглашения

1 единица = 1 м; в Blender Z вверх и перед в −Y, в Unity Y вверх и перед +Z. Пивот куска — центр куска. Коллизия —
отдельный объект `<кусок>_COL`, выпуклый, не больше 255 треугольников.

## Здесь

По папке на набор: FBX, `.blend`, `build.py`, `card.jpg` (рендеры), `manifest.json`. `_kit/` — общие хелперы
(`bk.py`, `sk.py`), бриф для субагентов и рабочие скрипты.
