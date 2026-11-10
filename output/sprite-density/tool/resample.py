"""Resample world sprites to a uniform texel density (texels per world metre).

For every texture: read sprite rects/pivots/PPU from its .meta, keep a common
pivot-aligned trimmed frame for all equally sized sprites, resample with an
exact affine box so world size and pivot are unchanged, enforce a >=1 px dark
outer contour, pack into the smallest power-of-two atlas (<= MAX) and write
PNG + patched .meta (names/internalIDs untouched).
"""
import json, math, re, sys, os
import numpy as np
from PIL import Image

MAX = 2048

def parse_meta(text):
    ppu = float(re.search(r'^  spritePixelsToUnits: (\S+)', text, re.M).group(1))
    mode = int(re.search(r'^  spriteMode: (\d+)', text, re.M).group(1))
    sprites = []
    if mode == 2:
        block = text.split('  spriteSheet:')[1]
        for m in re.finditer(r'    - serializedVersion: 2\n      name: (.*)\n      rect:\n        serializedVersion: 2\n        x: (\S+)\n        y: (\S+)\n        width: (\S+)\n        height: (\S+)\n      alignment: (\d+)\n      pivot: \{x: (\S+), y: (\S+)\}', block):
            n, x, y, w, h, al, px, py = m.groups()
            sprites.append(dict(name=n, x=float(x), y=float(y), w=float(w), h=float(h), alignment=int(al), px=float(px), py=float(py)))
    else:
        al = int(re.search(r'^  alignment: (\d+)', text, re.M).group(1))
        pv = re.search(r'^  spritePivot: \{x: (\S+), y: (\S+)\}', text, re.M)
        sprites.append(dict(name=None, x=0, y=0, w=None, h=None, alignment=al, px=float(pv.group(1)), py=float(pv.group(2))))
    return ppu, mode, sprites

ALIGN = {0:(.5,.5),1:(0,1),2:(.5,1),3:(1,1),4:(0,.5),5:(1,.5),6:(0,0),7:(.5,0),8:(1,0)}

def pivot_of(s):
    return (s['px'], s['py']) if s['alignment'] == 9 else ALIGN[s['alignment']]

def lum(rgb):
    return rgb[..., 0] * .299 + rgb[..., 1] * .587 + rgb[..., 2] * .114

def premul_resize(img, size, box):
    a = np.asarray(img).astype(np.float32) / 255
    a[..., :3] *= a[..., 3:4]
    chans = [Image.fromarray(a[..., i]).resize(size, Image.LANCZOS, box=box) for i in range(4)]
    out = np.stack([np.asarray(c) for c in chans], -1)
    return np.clip(out, 0, 1)

def resample_cell(src, box, size, outline_lum):
    """src: RGBA uint8 array of the source cell (top-left origin). box in source px."""
    k = int(math.ceil(max(0, -box[0], -box[1], box[2] - src.shape[1], box[3] - src.shape[0]))) + 2
    src = np.pad(src, ((k, k), (k, k), (0, 0)))
    box = (box[0] + k, box[1] + k, box[2] + k, box[3] + k)
    img = Image.fromarray(src)
    out = premul_resize(img, size, box)
    alpha = out[..., 3]
    solid = alpha >= .5
    rgb = np.where(alpha[..., None] > 1e-4, out[..., :3] / np.maximum(alpha[..., None], 1e-4), 0)
    rgb = np.clip(rgb, 0, 1)
    # dark-coverage of the source footprint of each output pixel
    s = src.astype(np.float32) / 255
    dark = ((lum(s[..., :3]) <= outline_lum + .06) & (s[..., 3] > .5)).astype(np.float32)
    cov = np.asarray(Image.fromarray(dark).resize(size, Image.BOX, box=box))
    # outer boundary of the hard silhouette (4-neighbourhood)
    pad = np.pad(solid, 1)
    edge = solid & ~(pad[:-2, 1:-1] & pad[2:, 1:-1] & pad[1:-1, :-2] & pad[1:-1, 2:])
    fix = edge & (cov > .08) & (lum(rgb) > outline_lum + .08)
    rgb[fix] = OUTLINE_RGB
    res = np.zeros(out.shape, np.uint8)
    res[..., :3] = np.round(rgb * 255)
    res[..., 3] = np.where(solid, 255, 0)
    res[~solid, :3] = 0
    return res, int(fix.sum()), int(edge.sum())

def outline_colour(cells):
    """Median colour of the source's outer contour band (darkest third of boundary pixels)."""
    vals = []
    for c in cells:
        a = c[..., 3] > 127
        pad = np.pad(a, 1)
        edge = a & ~(pad[:-2, 1:-1] & pad[2:, 1:-1] & pad[1:-1, :-2] & pad[1:-1, 2:])
        vals.append(c[edge][:, :3])
    v = np.concatenate(vals).astype(np.float32) / 255
    l = lum(v)
    v = v[l <= np.quantile(l, .5)]
    return np.median(v, 0)

def pow2(n):
    p = 1
    while p < n: p *= 2
    return p

def layout(n, cw, ch):
    best = None
    for cols in range(1, n + 1):
        rows = math.ceil(n / cols)
        W, H = pow2(cols * cw), pow2(rows * ch)
        if W > MAX or H > MAX: continue
        key = (W * H, max(W, H) / min(W, H), cols * cw * rows * ch)
        if best is None or key < best[0]: best = (key, cols, rows, W, H)
    if best is None: raise SystemExit(f'cells {cw}x{ch} x{n} do not fit {MAX}')
    return best[1:]

def shelf_pack(sizes):
    """First-fit-decreasing shelves (rows or columns) into the smallest pow2 canvas <= MAX."""
    cands = sorted({(1 << a, 1 << b) for a in range(5, 12) for b in range(5, 12)}, key=lambda t: (t[0] * t[1], max(t) / min(t)))
    for W, H in cands:
        for vertical in (False, True):
            # work in (along, across): shelves run across `along` axis
            dims = [(h, w) if vertical else (w, h) for w, h in sizes]
            A, B = (H, W) if vertical else (W, H)
            order = sorted(range(len(dims)), key=lambda i: -dims[i][1])
            shelves = []  # [offset_across, thickness, used_along]
            pos = [None] * len(dims); ok = True
            for i in order:
                l, t = dims[i]
                for sh in shelves:
                    if t <= sh[1] and sh[2] + l <= A:
                        pos[i] = (sh[2], sh[0]); sh[2] += l; break
                else:
                    off = sum(sh[1] for sh in shelves)
                    if l > A or off + t > B: ok = False; break
                    shelves.append([off, t, l]); pos[i] = (0, off)
            if ok:
                return W, H, [(q[1], q[0]) if vertical else q for q in pos]
    raise SystemExit('does not fit')

def extents(cell, pxs, pys):
    ys, xs = np.nonzero(cell[..., 3] >= 32)
    if not len(xs): return 0, 1, 0, 1
    return pxs - xs.min(), xs.max() + 1 - pxs, pys - ys.min(), ys.max() + 1 - pys

def fmt(v):
    s = repr(float(v)); return s[:-2] if s.endswith('.0') else s

SPRITE_TMPL = """    - serializedVersion: 2
      name: {name}
      rect:
        serializedVersion: 2
        x: 0
        y: 0
        width: 1
        height: 1
      alignment: 9
      pivot: {{x: 0.5, y: 0.5}}
      border: {{x: 0, y: 0, z: 0, w: 0}}
      customData: 
      outline: []
      physicsShape: []
      tessellationDetail: -1
      bones: []
      spriteID: {sid}
      internalID: {iid}
      vertices: []
      indices: 
      edges: []
      weights: []
"""

def process(entry, out_dir):
    global OUTLINE_RGB
    png, density = entry['png'], entry['density']   # texels per world metre wanted
    scale = entry.get('world_scale', 1.0)            # nominal transform scale of the renderer in the world
    meta = open(png + '.meta').read()
    ppu, mode, sprites = parse_meta(meta)
    im = np.asarray(Image.open(png).convert('RGBA'))
    TH, TW = im.shape[:2]
    if mode != 2:
        sprites[0].update(x=0, y=0, w=TW, h=TH, name=entry.get('sprite_name'))
    new_ppu = density * scale
    f = new_ppu / ppu
    cells = []
    for s in sprites:
        x, y, w, h = int(s['x']), int(s['y']), int(s['w']), int(s['h'])
        top = TH - y - h
        cells.append(im[top:top + h, x:x + w])
    OUTLINE_RGB = outline_colour(cells)
    olum = float(lum(OUTLINE_RGB))
    results = [None] * len(sprites); stats = [None] * len(sprites)
    packing = 'grid'
    if not entry.get('trim', True):
        # keep the exact source frame (code sizes this sprite from its bounds)
        s = sprites[0]; w, h = s['w'], s['h']
        n_w, n_h = int(round(w * f)), int(round(h * f))
        new_ppu = n_h * ppu / h; f = new_ppu / ppu
        res, fixed, edge = resample_cell(cells[0], (0, 0, w, h), (n_w, n_h), olum)
        px, py = pivot_of(s)
        results[0] = dict(img=res, w=n_w, h=n_h, pivot=(px, py)); stats[0] = dict(outline_fixed=fixed, edge=edge)
    else:
        def run(groups):
            for idx in groups:
                w, h = sprites[idx[0]]['w'], sprites[idx[0]]['h']
                P = [(pivot_of(sprites[i])[0] * w, (1 - pivot_of(sprites[i])[1]) * h) for i in idx]
                E = [extents(cells[i], *pp) for i, pp in zip(idx, P)]
                m = 1.0 / f
                L, R, U, D = (max(e[k] for e in E) + m for k in range(4))
                nl, nr, nu, nd = (math.ceil(v * f) for v in (L, R, U, D))
                for i, (pxs, pys) in zip(idx, P):
                    box = (pxs - nl / f, pys - nu / f, pxs + nr / f, pys + nd / f)
                    res, fixed, edge = resample_cell(cells[i], box, (nl + nr, nu + nd), olum)
                    results[i] = dict(img=res, w=nl + nr, h=nu + nd, pivot=(nl / (nl + nr), nd / (nu + nd)))
                    stats[i] = dict(outline_fixed=fixed, edge=edge)
        groups = {}
        for i, s in enumerate(sprites): groups.setdefault((s['w'], s['h']), []).append(i)
        run(list(groups.values()))
    cw = max(r['w'] for r in results); ch = max(r['h'] for r in results)
    n = len(results)
    if mode != 2 and not entry.get('to_multiple'):
        cols, rows, W, H = 1, 1, pow2(cw), pow2(ch)
        pos = [(0, 0)]
    else:
        try:
            cols, rows, W, H = layout(n, cw, ch)
            pos = [((k % cols) * cw, (k // cols) * ch) for k in range(n)]
        except SystemExit:
            # equal grid does not fit: trim every sprite on its own and shelf-pack
            packing = 'shelf'; cols = rows = None
            run([[i] for i in range(n)])
            W, H, pos = shelf_pack([(r['w'], r['h']) for r in results])
    if W > MAX or H > MAX: raise SystemExit(f'{png}: {W}x{H} > {MAX}')
    atlas = np.zeros((H, W, 4), np.uint8)
    rects = []
    for r, (x0, y0) in zip(results, pos):
        atlas[y0:y0 + r['h'], x0:x0 + r['w']] = r['img']
        rects.append(dict(x=x0, y=H - y0 - r['h'], w=r['w'], h=r['h'], pivot=r['pivot']))
    os.makedirs(out_dir, exist_ok=True)
    name = os.path.basename(png)
    Image.fromarray(atlas, 'RGBA').save(os.path.join(out_dir, name), optimize=True)
    meta2 = re.sub(r'^(  spritePixelsToUnits: )\S+', lambda mm: mm.group(1) + fmt(round(new_ppu, 6)), meta, flags=re.M)
    meta2 = re.sub(r'^(  maxTextureSize: )\d+', r'\g<1>2048', meta2, flags=re.M)
    meta2 = re.sub(r'^(    maxTextureSize: )\d+', r'\g<1>2048', meta2, flags=re.M)
    meta2 = re.sub(r'^(    textureCompression: )\d+', r'\g<1>0', meta2, flags=re.M)
    if mode != 2 and entry.get('to_multiple'):
        nm, iid = entry['sprite_name'], entry['internal_id']
        meta2 = re.sub(r'^(  spriteMode: )\d+', r'\g<1>2', meta2, flags=re.M)
        sid = re.search(r'^    spriteID: (\S+)', meta2, re.M).group(1)
        meta2 = meta2.replace('    sprites: []\n', '    sprites:\n' + SPRITE_TMPL.format(name=nm, sid=sid, iid=iid), 1)
        meta2 = meta2.replace('    nameFileIdTable: {}', f'    nameFileIdTable:\n      {nm}: {iid}', 1)
        meta2 = re.sub(r'^  internalIDToNameTable: \[\]', '  internalIDToNameTable: []', meta2, flags=re.M)
        mode = 2
    if mode == 2:
        it = iter(rects)
        def repl(mm):
            r = next(it)
            return (f"    - serializedVersion: 2\n      name: {mm.group(1)}\n      rect:\n        serializedVersion: 2\n"
                    f"        x: {r['x']}\n        y: {r['y']}\n        width: {r['w']}\n        height: {r['h']}\n"
                    f"      alignment: 9\n      pivot: {{x: {fmt(round(r['pivot'][0], 7))}, y: {fmt(round(r['pivot'][1], 7))}}}")
        head, sheet = meta2.split('  spriteSheet:', 1)
        sheet, cnt = re.subn(r'    - serializedVersion: 2\n      name: (.*)\n      rect:\n        serializedVersion: 2\n        x: \S+\n        y: \S+\n        width: \S+\n        height: \S+\n      alignment: \d+\n      pivot: \{x: \S+, y: \S+\}', repl, sheet)
        assert cnt == len(rects), (png, cnt, len(rects))
        meta2 = head + '  spriteSheet:' + sheet
        p0 = rects[0]['pivot']
    else:
        r = results[0]
        p0 = (r['pivot'][0] * r['w'] / W, (H - r['h'] + r['pivot'][1] * r['h']) / H)
        rects[0]['pivot'] = p0
    meta2 = re.sub(r'^(  alignment: )\d+', r'\g<1>9', meta2, flags=re.M, count=1)
    meta2 = re.sub(r'^(  spritePivot: )\{x: \S+, y: \S+\}', lambda mm: mm.group(1) + f"{{x: {fmt(round(p0[0],7))}, y: {fmt(round(p0[1],7))}}}", meta2, flags=re.M)
    open(os.path.join(out_dir, name + '.meta'), 'w').write(meta2)
    return dict(png=png, old_size=[TW, TH], old_ppu=ppu, new_ppu=new_ppu, factor=f, world_scale=scale, density=density,
                texels_per_world_m=new_ppu / scale, atlas=[W, H], cell=[cw, ch], cols=cols, rows=rows, packing=packing,
                outline_rgb=[round(float(v), 4) for v in OUTLINE_RGB],
                sprites=[dict(name=s['name'], rect=[r['x'], r['y'], r['w'], r['h']], pivot=[round(r['pivot'][0], 7), round(r['pivot'][1], 7)], **st)
                         for s, r, st in zip(sprites, rects, stats)])

if __name__ == '__main__':
    cfg = json.load(open(sys.argv[1]))
    report = [process(e, os.path.join(sys.argv[2], e['out'])) for e in cfg]
    json.dump(report, open(os.path.join(sys.argv[2], 'report.json'), 'w'), indent=1, ensure_ascii=False)
    for r in report:
        print(os.path.basename(r['png']), r['old_size'], '->', r['atlas'], 'cell', r['cell'], r['packing'], r['cols'], r['rows'], 'ppu', round(r['old_ppu'],2), '->', round(r['new_ppu'],4), 'tex/m', round(r['texels_per_world_m'],2),
              'outline_fixed', sum(s['outline_fixed'] for s in r['sprites']))
