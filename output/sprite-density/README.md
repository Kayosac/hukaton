# Единая плотность пикселей world-спрайтов (128 текселей на метр)

Все спрайты мира через HandDrawnSprite приведены к 128 текселям на мировой метр; сетка шейдера (`HandDrawnSprite.mat`, `_PixelsPerUnit`) тоже 128.

- `tool/resample.py config.json <out>` — пересборка: режет спрайты по rect из .meta, держит общий кадр с точным пивотом, ресемплинг с премультипликацией (Lanczos), жёсткая альфа, тёмный внешний контур минимум 1 px, упаковка в минимальный pow2 ≤ 2048 (сетка; если не влезает — полки, как у тролля), патч .meta без смены имён и internalID.
- `config.json` — что и с каким мировым масштабом пересобирается (`world_scale` = WorldCreature.size / масштаб Transform; у куста `trim: false`, т.к. CaveDrawnProp меряет его по bounds; куст переведён в Multiple с тем же fileID 21300000).
- `build/report.json` — итог: размеры, PPU, rect'ы, сколько пикселей контура дорисовано.
- `unity-density-check.txt` — что реально импортировал Unity (размеры, PPU, ID, bounds; 0 битых ссылок в 2703 полях префабов).
- `enemy-debug-shots/` — плеймод EnemyDebug, все 15 мобов в ряд.
- `originals/` — исходные PNG и .meta до пересборки (не в git; есть в истории коммитов).
