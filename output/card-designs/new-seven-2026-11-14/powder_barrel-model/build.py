"""Пороховая бочка (карта powder_barrel): кривая бочка в стиле болотных пропсов (кит sk.py, общие текстуры
M_Swamp_Planks / M_Swamp_Props). Клёпки разной высоты и ширины с щелями, вздутое неровное брюхо, три кривых
железных обруча (один сполз), утопленная крышка из торцевого дерева, фитиль-верёвка из пробки, грубо
намалёванный череп спереди, пороховые подтёки. Плюс выпуклая коллизия (12 граней) отдельным объектом.
python3 build_barrel.py [norender]"""
import sys, os, math, random
_HERE = os.path.dirname(os.path.abspath(__file__))
# кит болотных моделей: рядом ('kit') или в репозитории (output/biomes/rotten-swamp/models-v1/_kit)
for _k in (os.path.join(_HERE, 'kit'), os.path.join(_HERE, '..', '..', '..', 'biomes', 'rotten-swamp', 'models-v1', '_kit')):
    if os.path.isdir(_k):
        sys.path.insert(0, _k)
        break
import sk
from mathutils import Vector, Matrix

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'out')
H = 0.84            # высота клёпок
R_END, R_MID = 0.25, 0.305
N = 11              # клёпок
rnd = random.Random(7)
SCALE = 1.15        # вся бочка (после ревью в мире: тело ~0,97 м — по пояс игроку с глазами на 1,6 м)
SKULL = .23         # размер черепа (крупнее — середина плоской краски уходит под клёпки)
BONE = 'fungus_stem' # бледная «кость» черепа и костей (lily_petal почти белый — в мире светился, как лампа)

# кривизна всей бочки: верх уехал вбок, брюхо неровное
TOP_SHIFT = Vector((0.035, -0.02, 0))
stave_r = [rnd.uniform(.965, 1.04) for _ in range(N)]
stave_h = [H + rnd.uniform(-.025, .03) for _ in range(N)]
stave_gap = [rnd.uniform(.012, .03) for _ in range(N)]     # доля угла под щель
stave_off = [rnd.uniform(-.06, .06) for _ in range(N)]     # сдвиг шва по углу


def prof(z):
    """Радиус бочки на высоте z (0..H): вздутие, чуть несимметричное (низ толще)."""
    t = max(0.0, min(1.0, z / H))
    return R_END + (R_MID - R_END) * math.sin(math.pi * t) ** .8 * (1 + .05 * (1 - t))


def axis(z):
    """Ось бочки кривая: смещение центра сечения на высоте z."""
    t = z / H
    return TOP_SHIFT * (t * t) + Vector((0.008 * math.sin(t * 5.1), 0.01 * math.sin(t * 3.3 + 1), 0))


def stave_angle(i):
    return 2 * math.pi * (i + stave_off[i]) / N


def surf(a, z, k=1.0, out=0.0):
    """Точка на поверхности бочки: угол a, высота z, множитель радиуса k, выступ out (м)."""
    r = prof(z) * k + out
    return axis(z) + Vector((math.cos(a) * r, math.sin(a) * r, z))


def build():
    pl = sk.Mesh('Planks')
    pr = sk.Mesh('Props')
    T = 1.6
    th = 0.024
    seg = 6
    # ---------------- клёпки
    for i in range(N):
        a0 = stave_angle(i) + stave_gap[i]
        a1 = stave_angle((i + 1) % N) + (2 * math.pi if i == N - 1 else 0)
        a1 -= stave_gap[i] * .4
        k = stave_r[i]
        hh = stave_h[i]
        z0 = -rnd.uniform(0, .012)
        uo = rnd.random() * 3
        rows = []
        for s in range(seg + 1):
            z = z0 + (hh - z0) * s / seg
            # клёпка чуть выгнута наружу посередине своей ширины (не идеальный цилиндр)
            am = (a0 + a1) / 2
            pa, pm, pb = surf(a0, z, k), surf(am, z, k, .006), surf(a1, z, k)
            rows.append((pa, pm, pb, z))
        for s in range(seg):
            (pa, pm, pb, z), (qa, qm, qb, z2) = rows[s], rows[s + 1]
            w = (pb - pa).length
            for (p0, p1, q0, q1, u0, u1) in ((pa, pm, qa, qm, 0, w / 2), (pm, pb, qm, qb, w / 2, w)):
                pl.quad(p0, p1, q1, q0, (uo + u0 / T, z / T), (uo + u1 / T, z / T), (uo + u1 / T, z2 / T), (uo + u0 / T, z2 / T))
                # внутренняя сторона (видна под крышкой и в щелях)
                c0 = axis(z); c1 = axis(z2)
                def inn(p, c):
                    d = p - c; d.z = 0
                    return p - d.normalized() * th
                pl.quad(inn(q0, c1), inn(q1, c1), inn(p1, c0), inn(p0, c0), (uo, z2 / T), (uo + .05, z2 / T), (uo + .05, z / T), (uo, z / T))
            # бока клёпки (в щели)
            for (p, q) in ((pa, qa), (pb, qb)):
                c0 = axis(z); c1 = axis(z2)
                d0 = p - c0; d0.z = 0; d1 = q - c1; d1.z = 0
                pi_, qi = p - d0.normalized() * th, q - d1.normalized() * th
                if p is pa:
                    pl.quad(pi_, p, q, qi, (0, z / T), (th / T, z / T), (th / T, z2 / T), (0, z2 / T))
                else:
                    pl.quad(p, pi_, qi, q, (0, z / T), (th / T, z / T), (th / T, z2 / T), (0, z2 / T))
        # торец клёпки сверху
        pa, pm, pb, z = rows[-1]
        c = axis(z)
        def inn2(p):
            d = p - c; d.z = 0
            return p - d.normalized() * th
        pl.quad(pa, inn2(pa), inn2(pm), pm, (0, 0), (0, th / T), (.1, th / T), (.1, 0))
        pl.quad(pm, inn2(pm), inn2(pb), pb, (.1, 0), (.1, th / T), (.2, th / T), (.2, 0))

    # ---------------- дно и крышка (торцевое дерево), крышка утоплена и перекошена
    def disc(zc, tilt, region, up=True, k=.93):
        ring = []
        for j in range(N + 3):
            a = 2 * math.pi * j / (N + 3) + .2
            z = zc + tilt.x * math.cos(a) + tilt.y * math.sin(a)
            ring.append(surf(a, z, k * rnd.uniform(.985, 1.01), -0.018))
        pr.cap(ring if up else list(reversed(ring)), region=region)
        return ring
    disc(0.03, Vector((0, 0, 0)), 'end_grain_grey', up=False)
    disc(0.032, Vector((0, 0, 0)), 'end_grain_grey', up=True)
    lid_z = min(stave_h) - .05
    lid_tilt = Vector((.018, -.012, 0))
    lid = disc(lid_z, lid_tilt, 'end_grain_0', up=True)
    disc(lid_z - .03, lid_tilt, 'end_grain_1', up=False)

    # ---------------- обручи: плоская полоса железа по профилю, кривые, один сполз и перекошен
    iu0, iv0, iu1, iv1 = sk.Mesh.region('iron')
    hoops = [(.09, .055, Vector((0.0, 0.0, 0))), (.36, .045, Vector((.018, .01, 0))), (H - .1, .05, Vector((-.012, .02, 0)))]
    for hz, hw, tilt in hoops:
        n = 22
        ring_lo, ring_hi, ring_lo_in, ring_hi_in = [], [], [], []
        for j in range(n):
            a = 2 * math.pi * j / n
            z = hz + tilt.x * math.cos(a) + tilt.y * math.sin(a) + rnd.uniform(-.004, .004)
            wob = rnd.uniform(-.003, .004)
            ring_lo.append(surf(a, z - hw / 2, 1.04, .012 + wob))
            ring_hi.append(surf(a, z + hw / 2, 1.04, .012 + wob))
            ring_lo_in.append(surf(a, z - hw / 2, 1.04, .0))
            ring_hi_in.append(surf(a, z + hw / 2, 1.04, .0))
        circ = 2 * math.pi * prof(hz) * 1.04
        for j in range(n):
            j2 = (j + 1) % n
            u0 = iu0 + (iu1 - iu0) * ((j / n * circ / .5) % 1)
            u1 = u0 + (iu1 - iu0) * (circ / n / .5)
            pr.quad(ring_lo[j], ring_lo[j2], ring_hi[j2], ring_hi[j], (u0, iv0), (u1, iv0), (u1, iv1), (u0, iv1))
            pr.quad(ring_hi[j], ring_hi[j2], ring_hi_in[j2], ring_hi_in[j], (u0, iv1), (u1, iv1), (u1, iv1 - .004), (u0, iv1 - .004))
            pr.quad(ring_lo_in[j], ring_lo_in[j2], ring_lo[j2], ring_lo[j], (u0, iv0), (u1, iv0), (u1, iv0 + .004), (u0, iv0 + .004))

    # ---------------- пробка и фитиль (верёвка), конец фитиля — точка для искр
    ca, sa = math.cos(.9), math.sin(.9)
    lc = sum((Vector(p) for p in lid), Vector()) / len(lid)
    bung = lc + Vector((ca * .1, sa * .1, 0))
    # пробка — маленький кривой цилиндр из торцевого дерева (атласный регион через cap)
    br = [bung + Vector((math.cos(2 * math.pi * j / 7) * .035, math.sin(2 * math.pi * j / 7) * .035, 0)) for j in range(7)]
    bt = [p + Vector((0, 0, .03 + .004 * (j % 2))) for j, p in enumerate(br)]
    su0, sv0, su1, sv1 = sk.Mesh.region('small_wood')
    for j in range(7):
        j2 = (j + 1) % 7
        pr.quad(br[j], br[j2], bt[j2], bt[j], (su0 + j * .01, sv0), (su0 + j * .01 + .01, sv0), (su0 + j * .01 + .01, sv0 + .01), (su0 + j * .01, sv0 + .01))
    pr.cap(bt, region='end_grain_1')
    ru0, rv0, ru1, rv1 = sk.Mesh.region('rope')
    top = bung + Vector((0, 0, .03))
    path = [top, top + Vector((.01, 0, .07)), top + Vector((.045, -.02, .13)), top + Vector((.095, -.03, .15)), top + Vector((.13, -.025, .12))]
    fuse_tip = path[-1]
    sides = 5
    rings = []
    for i, p in enumerate(path):
        t = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
        up = Vector((1, 0, 0)) if abs(t.z) > .9 else Vector((0, 0, 1))
        x = (up - t * up.dot(t)).normalized(); y = t.cross(x)
        r = .011 if i < len(path) - 1 else .009
        rings.append([p + x * math.cos(2 * math.pi * k / sides) * r + y * math.sin(2 * math.pi * k / sides) * r for k in range(sides)])
    for i in range(len(rings) - 1):
        for k in range(sides):
            k2 = (k + 1) % sides
            v0 = rv0 + (rv1 - rv0) * k / sides; v1 = rv0 + (rv1 - rv0) * (k + 1) / sides
            u0 = ru0 + (ru1 - ru0) * i / 4; u1 = ru0 + (ru1 - ru0) * (i + 1) / 4
            pr.quad(rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k], (u0, v0), (u0, v1), (u1, v1), (u1, v0))
    pr.cap(rings[-1], region='tar')     # обугленный кончик

    # ---------------- череп спереди (−Y), грубо намалёван светлым, глазницы и нос — тёмные
    def paint(poly2d, ac, zc, scale, region, out):
        """Плоская «краска» на бочке: poly2d в (дуга м, высота м) около угла ac, высоты zc."""
        # Лицом наружу: обход против часовой (если смотреть на бочку). Глазницы, нос, зубы, челюсть и кости были
        # заданы по часовой — в Unity (отсечение задних граней) их не было видно, череп читался светлым пятном.
        area = sum(x0 * y1 - x1 * y0 for (x0, y0), (x1, y1) in zip(poly2d, poly2d[1:] + poly2d[:1]))
        if area < 0:
            poly2d = list(reversed(poly2d))
        pts = []
        for (x, y) in poly2d:
            z = zc + y * scale
            a = ac + x * scale / prof(z)
            pts.append(surf(a, z, 1.0 * stave_r_at(a), out))
        u0, v0, u1, v1 = sk.Mesh.region(region)
        xs = [p[0] for p in poly2d]; ys = [p[1] for p in poly2d]
        uvs = [(u0 + (x - min(xs)) / (max(xs) - min(xs) + 1e-6) * (u1 - u0), v0 + (y - min(ys)) / (max(ys) - min(ys) + 1e-6) * (v1 - v0)) for x, y in poly2d]
        pr.face(pts, uvs)

    front = -math.pi / 2 - .05
    zc = .55
    skull = []
    for j in range(13):
        a = 2 * math.pi * j / 13
        rr = 1 + .08 * math.sin(a * 3 + 1)
        skull.append((math.cos(a) * .5 * rr, math.sin(a) * .45 * rr + .12))
    # челюсть — трапеция снизу
    jaw = [(-.24, -.2), (.26, -.22), (.2, -.5), (-.18, -.48)]
    # Краска черепа — бледная «кость» (BONE), а не серая тряпка (rag): в свете мира тряпка сливалась с клёпками.
    paint(skull, front, zc, SKULL, BONE, .0045)
    paint(jaw, front, zc, SKULL, BONE, .0045)
    paint([(-.33, .18), (-.08, .2), (-.12, -.04), (-.3, -.02)], front, zc, SKULL, 'tar', .0065)
    paint([(.08, .22), (.31, .16), (.29, -.05), (.1, -.03)], front, zc, SKULL, 'tar', .0065)
    paint([(-.04, -.08), (.05, -.09), (.0, -.19)], front, zc, SKULL, 'tar', .0065)
    for x in (-.12, .0, .12):
        paint([(x - .02, -.24), (x + .02, -.24), (x + .02, -.44), (x - .02, -.44)], front, zc, SKULL, 'tar', .0065)
    # скрещённые кости под черепом
    for sgn in (-1, 1):
        paint([(-.55 * sgn, -.62), (-.45 * sgn, -.7), (.55 * sgn, -.98), (.45 * sgn, -.9)], front, zc, SKULL, BONE, .0045)

    # ---------------- пороховые подтёки (тёмные пятна у пробки и внизу)
    for (ac, z0, sc) in ((1.2, .2, .1), (2.6, .12, .08), (-0.5, .74, .07), (3.6, .62, .06)):
        blob = []
        for j in range(8):
            a = 2 * math.pi * j / 8
            r = rnd.uniform(.6, 1.0)
            blob.append((math.cos(a) * r * .8, math.sin(a) * r * (1.4 if j in (5, 6) else 1)))
        paint(blob, ac, z0, sc, 'tar', .004)

    objs = [pl.to_object('PB_Planks'), pr.to_object('PB_Props')]

    # ---------------- коллизия: выпуклый 12-гранник по профилю (4 пояса)
    col = sk.Mesh('Props')
    zs = [0, .22, .58, max(stave_h)]
    rings = [[surf(2 * math.pi * j / 12, z, 1.02, .0) for j in range(12)] for z in zs]
    for i in range(len(rings) - 1):
        for j in range(12):
            j2 = (j + 1) % 12
            col.quad(rings[i][j], rings[i][j2], rings[i + 1][j2], rings[i + 1][j], (0, 0), (0, 0), (0, 0), (0, 0))
    col.cap(rings[0], flip=True); col.cap(rings[-1])
    collider = col.to_object('PB_Collider')
    return objs, collider, fuse_tip


def stave_r_at(a):
    a = a % (2 * math.pi)
    for i in range(N):
        a0 = stave_angle(i) % (2 * math.pi)
        a1 = stave_angle((i + 1) % N) % (2 * math.pi)
        if (a0 <= a < a1) or (a1 < a0 and (a >= a0 or a < a1)):
            return stave_r[i]
    return 1.0


def main(render=True):
    sk.reset()
    objs, collider, tip = build()
    for o in objs + [collider]:
        o.data.transform(Matrix.Scale(SCALE, 4))
    tip = tip * SCALE
    d = sk.ground(objs)
    collider.data.transform(Matrix.Translation(d))
    tip = tip + d
    os.makedirs(OUT, exist_ok=True)
    sk.export_fbx(objs + [collider], OUT + '/SM_PowderBarrel.fbx')
    lo, hi = sk.bounds(objs)
    print('size', [round(float(hi[i] - lo[i]), 3) for i in range(3)], 'tris', sk.tris(objs), 'fuse tip (blender)', [round(c, 3) for c in tip],
          'unity', [round(tip.x, 3), round(tip.z, 3), round(tip.y, 3)])
    if render:
        tiles = []
        for az, el in ((-20, 15), (60, 25), (160, 40)):
            p = OUT + '/r_%d_%d.png' % (az, el)
            sk.render(objs, p, az=az, el=el, size=(420, 420), samples=10)
            tiles.append(p)
        sk.sheet(tiles, OUT + '/card.jpg', cols=3)


if __name__ == '__main__':
    main(render='norender' not in sys.argv)
