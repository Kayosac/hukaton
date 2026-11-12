from PIL import Image, ImageDraw, ImageFilter, ImageChops
from pathlib import Path
import json

root = Path(__file__).resolve().parents[1]
source = Image.open(root/'masters/default_front.png').convert('RGBA')
crop_origin = (560, 140)
regions = [(20, 48, 121, 136), (134, 22, 200, 112), (82, 211, 165, 246)]
mask = Image.new('L', (240, 280))
draw = ImageDraw.Draw(mask)
for rect in regions:
    draw.rounded_rectangle(rect, radius=5, fill=255)
# Feather inwards so the mask has no influence beyond the explicitly recorded regions.
mask = ImageChops.multiply(mask, mask.filter(ImageFilter.GaussianBlur(2)))
mask.save(root/'expression-work/mask.png')
fullmask = Image.new('L', source.size)
fullmask.paste(mask, crop_origin)
reports = []
for name in ['displeased','happy','enraged']:
    patch = Image.open(root/f'expression-work/{name}-registered.png').convert('RGBA')
    # Transfer generated INK only; preserve the source skin rather than generated
    # lighting, which can otherwise make rectangular tinted seams.
    skin = source.crop((560,140,800,420)).convert('RGB')
    original = skin.copy()
    def luminosity(rgb): return .299*rgb[0]+.587*rgb[1]+.114*rgb[2]
    old_ink=original.convert('L').point(lambda v:255 if v<165 else 0).filter(ImageFilter.MaxFilter(7))
    for y in range(280):
        for x in range(240):
            if not mask.getpixel((x,y)): continue
            old=original.getpixel((x,y))
            if old_ink.getpixel((x,y)):
                candidates=[]
                for radius in range(1,28):
                    for dx,dy in [(radius,0),(-radius,0),(0,radius),(0,-radius),(radius,radius),(-radius,-radius),(radius,-radius),(-radius,radius)]:
                        xx,yy=x+dx,y+dy
                        if 0<=xx<240 and 0<=yy<280:
                            c=original.getpixel((xx,yy))
                            if not old_ink.getpixel((xx,yy)) and c[0]>190 and c[1]>110 and c[2]<c[1] and luminosity(c)>170:
                                candidates.append(c)
                    if len(candidates)>=4: break
                if candidates: skin.putpixel((x,y),tuple(round(sum(c[k] for c in candidates)/len(candidates)) for k in range(3)))
            c=patch.getpixel((x,y))[:3]
            ink=max(0,min(1,(130-luminosity(c))/100))
            if ink:
                bg=skin.getpixel((x,y))
                skin.putpixel((x,y),tuple(round(bg[k]*(1-ink)+(27,24,18)[k]*ink) for k in range(3)))
    layer = source.copy()
    layer.paste(skin, crop_origin)
    result = Image.composite(layer, source, fullmask)
    result.putalpha(source.getchannel('A'))
    path = root/f'masters/{name}_front.png'
    result.save(path)
    saved = Image.open(path).convert('RGBA')
    diff = ImageChops.difference(saved, source)
    changed = diff.convert('RGB').point(lambda v: 255 if v else 0).convert('L').point(lambda v: 255 if v else 0)
    outside = ImageChops.multiply(changed, fullmask.point(lambda v: 0 if v else 255))
    alpha_diff = ImageChops.difference(saved.getchannel('A'),source.getchannel('A'))
    assert outside.getbbox() is None and alpha_diff.getbbox() is None
    reports.append({'file':str(path.relative_to(root)), 'size':list(saved.size), 'changed_pixels_outside_masks':0, 'changed_alpha_pixels':0, 'changed_pixels_inside_masks':sum(v>0 for v in changed.getdata())})
json.dump({'crop':[560,140,240,280], 'mask_regions_crop_local':regions, 'mask':'expression-work/mask.png','variants':reports},open(root/'expression-work/verification.json','w'),indent=2)
# Internal full-body and face QA previews.
board = Image.new('RGB',(1217*4,1293),'#e8dfd0')
for i,name in enumerate(['default_front','displeased_front','happy_front','enraged_front']):
    im=Image.open(root/f'masters/{name}.png')
    board.paste(im,(i*1217,0),im)
board.resize((1460,388),Image.Resampling.LANCZOS).save(root/'expression-work/full-review.png')
faces=Image.new('RGB',(960,280),'#e8dfd0')
for i,name in enumerate(['default_front','displeased_front','happy_front','enraged_front']):
    im=Image.open(root/f'masters/{name}.png').crop((560,140,800,420))
    faces.paste(im,(i*240,0),im)
faces.save(root/'expression-work/composite-review.png')
print(json.dumps(reports,indent=2))
