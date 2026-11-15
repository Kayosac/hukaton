# Мини-игра «Закрой окно» — арт и звуки

- `real-v1/` — утверждённые баннеры рекламы (реалистичные, «как в 2000-х»): генератор `make_ads.py`, исходные «стоковые» фото `photo_*.png`, превью `preview_all.png`. Пишет `Assets/Computer/Art/Popups/pc_ad_<slug>_0/1.png` (180×150, по 2 кадра).
- `cartoon-v0/` — первый, отклонённый мультяшный вариант. Его `make_popup_art.py` всё ещё единственный источник рамок `xp_window_ad_*`, ложного крестика `xp_close_fake*`, иконок `xp_icon_warning/error/question` и мордочки `pc_bsod_face`, но он же перезаписывает `pc_ad_*` старыми картинками 80×60 — перед запуском отключите в нём часть с рекламой.
- `make_popup_sounds.py` — звуки `Audio/Sfx/backroom/pc_popup_*.wav` и `pc_bsod.wav`.
