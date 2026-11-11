# Единая плотность пикселей world-спрайтов

Сетка шейдера HandDrawnSprite (`Rendering/Materials/HandDrawnSprite.mat`, `_PixelsPerUnit`) = 100 пикселей на метр мира; все мобы, их снаряды и куст в пещере собраны ровно под неё (1 тексель = 1 клетка сетки). Гоблин-торговец (оба вида) — исключение по решению пользователя: материал `HandDrawnSprite Goblin.mat` с сеткой 200 и текстуры под 200.

- `tool/resample.py config.json build` — пересборка из `originals/`: режет спрайты по rect из .meta, держит общий кадр с точным пивотом, ресемплинг с премультипликацией (Lanczos), жёсткая альфа, тёмный внешний контур минимум 1 px, упаковка в минимальный pow2 ≤ 2048 (сетка; если не влезает — полки), патч .meta без смены имён и internalID. Готовое копируется из `build/` в Assets.
- `config.json` — `density` (тексели на метр) и `world_scale` (WorldCreature.size / масштаб Transform). У куста `trim: false` (CaveDrawnProp меряет его по bounds) и перевод в Multiple с тем же fileID 21300000. Круглый гоблин собран под масштаб 3 (пещера), за прилавком он в масштабе 1.
- `build/report.json` — итог: размеры, PPU, rect'ы, сколько пикселей контура дорисовано.
- `unity-density-check.txt`, `enemy-debug-shots/` — проверка первой версии (128) в Unity и EnemyDebug.
- `originals/` — исходные PNG и .meta до пересборки (не в git; есть в истории коммитов).
- Если сетку поменяют: выставить `density` в config.json (гоблинам — вдвое больше), перезапустить, скопировать, обновить Unity.
