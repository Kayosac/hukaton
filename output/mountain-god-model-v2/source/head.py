"""MG01 head module: main head + neck stub, beard, wizard hat, and the 'face fragments' melted
into the giant limbs. Pure data: every builder returns core.Shell objects (closed, manifold,
outward faces) plus decal anchors and joint positions. No bpy objects are created here
(bmesh is used only as a convex-hull helper).

Style (guide, section "Simplifying the main face"): big wedge nose, tiny dark eye/mouth strokes
PAINTED (returned as decal anchors), beard of 2-3 big planes, hat = uneven low-poly cone with a few
kinks. No wrinkles, hair strands, teeth, eyeballs.

Coordinates: Blender metres at 1:100, +X canon right, -Y front, +Z up (see core.py).
"""
import math, random
import bmesh
from mathutils import Vector as V, Matrix
import core
import layout as L

# Reference head size the profile numbers below are authored for (layout.HEAD['size']).
_REF = V((1.35, 1.15, 1.75))

# Head cross-section rings in head-local coords (x right, -y = face direction, z up), authored
# for _REF. Each ring: (z_front, z_back, fw, fy, cw, cy, sw, sy, bw, by) -> 8 verts:
#   front-right(fw,fy) cheek-right(cw,cy) side-right(sw,sy) back-right(bw,by) + mirror.
# The ring is slanted: front verts at z_front, back verts at z_back (jaw line rises to the nape).
_HEAD_RINGS = [
    (-1.00, -0.46, 0.20, -0.46, 0.40, -0.28, 0.44, 0.06, 0.27, 0.30),   # jaw underside / chin
    (-0.86, -0.34, 0.23, -0.50, 0.47, -0.30, 0.51, 0.08, 0.31, 0.38),   # chin front / jaw corner
    (-0.70, -0.22, 0.25, -0.51, 0.51, -0.31, 0.55, 0.10, 0.33, 0.44),   # mouth line
    (-0.36, 0.00, 0.28, -0.53, 0.58, -0.31, 0.60, 0.12, 0.36, 0.52),    # nose base
    (-0.08, 0.12, 0.29, -0.52, 0.60, -0.33, 0.61, 0.13, 0.37, 0.54),    # cheekbones (slight planes)
    (0.21, 0.26, 0.30, -0.49, 0.57, -0.32, 0.60, 0.14, 0.38, 0.55),     # eye band (slightly recessed)
    (0.40, 0.44, 0.30, -0.56, 0.57, -0.37, 0.59, 0.14, 0.38, 0.54),     # brow ridge (under the brim)
    (0.55, 0.56, 0.27, -0.50, 0.52, -0.32, 0.55, 0.13, 0.36, 0.50),     # temples (inside the hat)
    (0.70, 0.69, 0.20, -0.38, 0.41, -0.24, 0.47, 0.12, 0.31, 0.42),     # upper skull (inside the hat)
]
_HEAD_APEX = (0.0, 0.05, 0.90)


# ============================================================== small helpers
def _rot(yaw=0.0, pitch=0.0, roll=0.0):
    """3x3 rotation. yaw>0 turns the face (-Y) towards +X; pitch<0 tips the top backwards (+Y)
    (chin forward); roll<0 leans the top towards -X."""
    return (Matrix.Rotation(math.radians(yaw), 3, 'Z') @ Matrix.Rotation(math.radians(roll), 3, 'Y')
            @ Matrix.Rotation(math.radians(pitch), 3, 'X'))


def _slide(p, dy):
    """Move p along the canon camera ray by dy in Y (keeps its canon-front-view pixel)."""
    p = V(p)
    d0 = p.y - L.CAM.y
    return L.CAM + (p - L.CAM) * ((d0 + dy) / d0)


def _signed_volume(sh):
    vol = 0.0
    for f in sh.faces:
        a = sh.verts[f[0]]
        for i in range(1, len(f) - 1):
            vol += a.dot(sh.verts[f[i]].cross(sh.verts[f[i + 1]]))
    return vol / 6.0


def _orient(sh):
    """Faces are built consistently; flip all of them if the shell turned out inside-out."""
    if _signed_volume(sh) < 0:
        sh.faces = [tuple(reversed(f)) for f in sh.faces]
    return sh


def _hull(name, pts, group=None, mat='body'):
    """Closed convex shell around pts (bmesh convex hull), outward faces, unused points dropped."""
    bm = bmesh.new()
    for p in pts:
        bm.verts.new(V(p))
    bm.verts.ensure_lookup_table()
    res = bmesh.ops.convex_hull(bm, input=bm.verts[:], use_existing_faces=False)
    junk = list({g for g in res['geom_interior'] + res['geom_unused'] if isinstance(g, bmesh.types.BMVert)})
    junk += [v for v in bm.verts if not v.link_faces and v not in junk]
    if junk:
        bmesh.ops.delete(bm, geom=junk, context='VERTS')
    # merge coplanar triangles into flat facets (fewer, broader planes; same tri count)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(1.0), verts=bm.verts[:], edges=bm.edges[:])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.verts.index_update()
    sh = core.Shell(name, group, mat)
    sh.verts = [v.co.copy() for v in bm.verts]
    sh.faces = [tuple(v.index for v in f.verts) for f in bm.faces]
    bm.free()
    return _orient(sh)


def _loft(name, rings, group=None, mat='body', bottom='fan', top='fan'):
    """Closed shell through rings of equal vertex count (ordered CCW when seen from the end the
    loft travels towards). bottom/top: 'fan' (centroid fan), 'ngon', or a point (apex)."""
    sh = core.Shell(name, group, mat)
    ids = [[sh.v(p) for p in r] for r in rings]
    n = len(rings[0])
    for a, b in zip(ids[:-1], ids[1:]):
        for k in range(n):
            k2 = (k + 1) % n
            sh.f(a[k], a[k2], b[k2], b[k])

    def cap(ring, top_end, how):
        if how == 'ngon':
            sh.f(*(ring if top_end else list(reversed(ring))))
            return
        c = sh.v(V(how) if not isinstance(how, str) else sum((sh.verts[i] for i in ring), V()) / n)
        for k in range(n):
            k2 = (k + 1) % n
            sh.f(*((ring[k], ring[k2], c) if top_end else (ring[k2], ring[k], c)))

    cap(ids[0], False, bottom)
    cap(ids[-1], True, top)
    return _orient(sh)


def _path_rings(pts, radii, sides, up_hint, rolls=None, rnd=None, jitter=0.0, phase=0.0, squash=None):
    """Rings around an explicit polyline (no spline): ring i is centred on pts[i], perpendicular to
    the averaged tangent, rotation-minimising frames. radii: float or (rx, ry)."""
    P = [V(p) for p in pts]
    frames = core.transport_frames(P, V(up_hint))
    rings = []
    for i, (c, (T, N, B)) in enumerate(zip(P, frames)):
        r = radii[i]
        rx, ry = (r, r) if not isinstance(r, (tuple, list)) else r
        rl = math.radians(rolls[i]) if rolls else 0.0
        N2, B2 = N * math.cos(rl) + B * math.sin(rl), -N * math.sin(rl) + B * math.cos(rl)
        ring = []
        for k in range(sides):
            a = phase + 2 * math.pi * k / sides
            j = 1.0 + (rnd.uniform(-jitter, jitter) if (rnd and jitter) else 0.0)
            ring.append(c + (N2 * math.cos(a) * rx + B2 * math.sin(a) * ry) * j)
        rings.append(ring)
    return rings, frames


def ray_hit(shells, origin, direction):
    """Nearest hit of a ray with a list of shells -> (point, normal, shell_name) or None."""
    from mathutils.geometry import intersect_ray_tri
    o, d = V(origin), V(direction).normalized()
    best = None
    for sh in shells:
        for f in sh.faces:
            for i in range(1, len(f) - 1):
                a, b, c = sh.verts[f[0]], sh.verts[f[i]], sh.verts[f[i + 1]]
                hit = intersect_ray_tri(a, b, c, d, o, True)
                if hit is None:
                    continue
                t = (hit - o).dot(d)
                if t > 1e-6 and (best is None or t < best[0]):
                    n = (b - a).cross(c - a).normalized()
                    best = (t, hit, n, sh.name)
    return None if best is None else (best[1], best[2], best[3])


# ============================================================== torso estimate
_CHEST = None


def chest_front_layout(x, z):
    """Front (min Y) of the layout.TORSO tube at (x, z) or None (above/below/beside it). Sections are
    treated as horizontal ellipses with radii (rx along X, ry along Y) - i.e. the tube built with
    up_hint=(1,0,0)."""
    global _CHEST
    if _CHEST is None:
        samp = core.catmull_rom(L.TORSO['pts'], 8)
        rad = [r if isinstance(r, (tuple, list)) else (r, r) for r in L.TORSO['radii']]
        _CHEST = [(c, core.lerp_param(rad, s, t)) for c, s, t in samp]
    for (c0, r0), (c1, r1) in zip(_CHEST[:-1], _CHEST[1:]):
        if (c0.z - z) * (c1.z - z) <= 0 and abs(c1.z - c0.z) > 1e-6:
            t = (z - c0.z) / (c1.z - c0.z)
            c = c0 + (c1 - c0) * t
            rx, ry = r0[0] + (r1[0] - r0[0]) * t, r0[1] + (r1[1] - r0[1]) * t
            u = (x - c.x) / rx
            if abs(u) >= 1.0:
                return None
            return c.y - ry * math.sqrt(1.0 - u * u) * 1.06   # +6%: tube jitter
    return None


# ============================================================== main head
def build_head(centre, size, chin, neck_top, neck_radius, hat_brim, hat_tip, seed=0,
               push=0.4, yaw=12.0, pitch=-5.0, roll=-6.0, beard_tip=None, chest_front=None):
    """Main head of the old god: long calm moai-like head + neck stub, beard, hat.

    centre/size/chin/hat_brim/hat_tip: layout.HEAD; neck_top/neck_radius: last point/radius of
    layout.TORSO. size = full extents (width X, depth Y, height Z).
    push: the layout head sits too far back (its face would be behind the chest front), so the
          whole head/hat group is slid `push` m towards the viewer ALONG THE CANON CAMERA RAYS
          (front-view pixels unchanged).
    yaw/pitch/roll: head orientation (deg). The canon face is turned slightly towards +X (nose right
          of the face centre), top leaning to -X, chin a little forward.
    beard_tip: world point; default ~layout.P(680, 345, y) with y just in front of the chest.
    chest_front: optional callable (x, z) -> front Y of the real torso surface (or None); default
          estimates it from layout.TORSO (ellipse sections, rx along X). The beard is draped on it.
    Returns dict(head, beard, hat, decals, joints, info).
    """
    rnd = random.Random(seed)
    if isinstance(neck_radius, (tuple, list)):
        neck_radius = max(neck_radius)
    sx, sy, sz = (V(size)[i] / _REF[i] for i in range(3))
    sx *= 1.08
    C = _slide(centre, -push)
    R = _rot(yaw, pitch, roll)
    Rh, Fh, Uh = R @ V((1, 0, 0)), R @ V((0, -1, 0)), R @ V((0, 0, 1))  # right, face dir, up

    # The layout chin (canon px 650,212) sits only ~0.25 m under the painted mouth and is hidden by the
    # beard, so it is not used to stretch the face; the authored long-face proportions win.
    low_k = 1.0
    def W(x, y, z, scale_low=True):
        """head-local (authored for _REF) -> world"""
        zz = z * (low_k if (scale_low and z < 0) else 1.0)
        return C + R @ V((x * sx, y * sy, zz * sz))

    # ---------------------------------------------------------- head shell
    rings = []
    for (zf, zb, fw, fy, cw, cy, sw, sy_, bw, by) in _HEAD_RINGS:
        pts2 = [(fw, fy), (cw, cy), (sw, sy_), (bw, by), (-bw, by), (-sw, sy_), (-cw, cy), (-fw, fy)]
        ring = []
        for (x, y) in pts2:
            t = (y - fy) / (by - fy)
            z = zf + (zb - zf) * t
            j = 1.0 + rnd.uniform(-0.035, 0.035)
            jz = rnd.uniform(-0.025, 0.025)
            ring.append(W(x * j, y * (1.0 + rnd.uniform(-0.02, 0.02)), z + jz))
        rings.append(ring)
    head = _loft('HEAD_skull', rings, group='HEAD', mat='body', bottom='fan', top=W(*_HEAD_APEX))

    # ---------------------------------------------------------- nose wedge (overlaps the face)
    nose_pts = [W(-0.08, -0.42, 0.31), W(0.08, -0.42, 0.31),        # bridge root, buried in the brow
                W(0.0, -0.64, 0.24),                                  # ridge top (between the eyes)
                W(0.0, -0.95, -0.45),                                 # tip, drooping
                W(-0.17, -0.83, -0.42), W(0.18, -0.84, -0.41),        # wings
                W(-0.22, -0.45, -0.34), W(0.22, -0.45, -0.33),        # buried base (higher than the tip)
                W(0.0, -0.60, -0.36)]                                 # under the tip (short underside)
    nose = _hull('HEAD_nose', nose_pts, group='HEAD', mat='body')
    # tiny ear wedges under the brim (mostly hidden by the hat and the side beard)
    ears = [_hull('HEAD_ear_' + ('r' if sg > 0 else 'l'), [
        W(sg * 0.50, 0.02, 0.18), W(sg * 0.50, 0.24, 0.16), W(sg * 0.50, 0.04, -0.16), W(sg * 0.50, 0.22, -0.14),
        W(sg * 0.70, 0.16, 0.14), W(sg * 0.68, 0.22, -0.04), W(sg * 0.64, 0.10, -0.12)],
        group='HEAD', mat='body') for sg in (-1, 1)]

    # ---------------------------------------------------------- neck stub
    head_base = W(0.0, 0.16, -0.50, scale_low=False)
    nt = V(neck_top)
    n_low = nt - V((0, 0, 0.55))                  # deep inside the torso's neck cone
    n_top = head_base + Uh * 0.30                 # deep inside the skull
    n_mid = n_low + (n_top - n_low) * 0.5
    neck, _ = core.tube('HEAD_neck', [n_low, n_mid, n_top],
                        [neck_radius * 1.2, neck_radius * 1.12, neck_radius * 1.25],
                        sides=8, samples=1, up_hint=tuple(Fh), jitter=0.04, seed=seed + 5,
                        group='HEAD', mat='body')

    # ---------------------------------------------------------- beard (3 big faceted masses)
    # The beard grows from the jaw and lies over the chest: its back is draped onto the chest front
    # (layout torso estimate, or the caller's chest_front(x, z) -> y), its front keeps the thickness.
    cf = chest_front if chest_front is not None else chest_front_layout

    def drape(p, gap, max_back=0.45):
        y = cf(p.x, p.z)
        if y is None:
            return p
        y -= gap
        return V((p.x, max(min(y, p.y + max_back), p.y - 0.6), p.z))

    if beard_tip is None:
        # canon pixel ~(680, 345), pulled 8 px inwards so it hangs over the right chest; depth from
        # the chest front a little inside the tip (the layout torso is narrow at that height)
        px, py = 672, 343
        y = W(0, -0.5, -0.9).y - 0.25
        for _ in range(3):
            q = L.P(px, py, y)
            yc = cf(q.x - 0.25, q.z)
            if yc is None:
                break
            y = max(min(yc - 0.12, y + 0.45), y - 0.6)
        beard_tip = L.P(px, py, y)
    BT = V(beard_tip)
    jr = lambda k=0.03: V((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))) * k
    fwd = V((0, -1, 0))
    # centre mass: broad plank from the whole jaw (just under the mouth) to the long point
    bm_l = drape(W(-0.48, -0.40, -1.62), 0.06)
    bm_r = drape(W(0.52, -0.40, -1.58), 0.06)
    tp_l = drape(W(-0.30, -0.6, -2.0) * 0.55 + BT * 0.45, 0.07)
    tp_r = drape(W(0.45, -0.6, -2.0) * 0.5 + BT * 0.5, 0.07)
    beard_c = _hull('BEARD_centre', [
        W(-0.36, -0.40, -0.80), W(0.36, -0.40, -0.80),               # root under the mouth (buried)
        W(-0.46, -0.06, -1.06), W(0.46, -0.06, -1.06),               # root under the jaw (buried)
        W(-0.44, -0.66, -0.88) + jr(), W(0.46, -0.66, -0.90) + jr(),  # front top corners
        W(0.03, -0.80, -0.90),                                        # front ridge top
        bm_l, bm_r,                                                   # back middle, on the chest
        bm_l + fwd * 0.40 + jr(), bm_r + fwd * 0.40 + jr(),           # front middle (thickness)
        (bm_l + bm_r) / 2 + fwd * 0.52 + V((0.04, 0, 0.08)),          # ridge middle
        tp_l, tp_r, tp_l + fwd * 0.26, tp_r + fwd * 0.24,              # taper
        BT], group='HEAD', mat='body')

    # side masses: mustache/cheek beard from beside the nose, flaring out, shorter points on the chest
    def side(sg, tip):
        mid_b = drape(W(sg * 0.74, -0.12, -1.12), 0.05, 0.3)
        mid_f = drape(W(sg * 0.84, -0.42, -0.95), 0.05 + 0.3, 0.3)
        t = drape(W(*tip), 0.06, 0.35)
        return _hull('BEARD_' + ('right' if sg > 0 else 'left'), [
            W(sg * 0.24, -0.46, -0.46), W(sg * 0.52, -0.24, -0.30),       # root beside the nose (buried)
            W(sg * 0.34, -0.40, -0.95), W(sg * 0.54, -0.08, -0.98),       # root on the jaw (buried)
            W(sg * 0.26, -0.67, -0.52) + jr(), W(sg * 0.62, -0.50, -0.34) + jr(),  # upper surface (cheek)
            W(sg * 0.40, -0.77, -1.05) + jr(), mid_f + jr(),               # broad middle
            mid_b,                                                         # back edge
            t + jr(0.03), t + fwd * 0.12 + V((-sg * 0.08, 0, 0.18))], group='HEAD', mat='body')
    beard_l = side(-1, (-0.86, -0.58, -1.88))
    beard_r = side(1, (0.90, -0.56, -1.80))

    # ---------------------------------------------------------- hat
    hat_shells, hat_joints, hat_info = _build_hat(C, R, sx, sz, hat_brim, hat_tip, push, rnd)

    # ---------------------------------------------------------- decals (painted strokes)
    face_shells = [head]
    decals = []
    for kind, (x, z), sz_ in (('eye_closed', (-0.27, 0.14), 0.24), ('eye_closed', (0.27, 0.14), 0.24),
                              ('mouth', (0.0, -0.69), 0.38)):
        p0 = W(x, -2.0, z)
        hit = ray_hit(face_shells, p0, Fh * -1.0)
        if hit is None:
            continue
        pos, nrm, _ = hit
        up = (Uh - nrm * Uh.dot(nrm)).normalized()
        decals.append(dict(kind=kind, pos=pos, normal=nrm, up=up, size=sz_ * sx))

    joints = {
        'neck': [nt.copy(), head_base],
        'head': [head_base, W(*_HEAD_APEX)],
        'hat': hat_joints,
        'beard': [W(0.0, -0.45, -0.92), BT],
    }
    info = dict(centre=C, R=R, face_dir=Fh, up=Uh, push=push, **hat_info)
    return dict(head=[head, nose, neck] + ears, beard=[beard_c, beard_l, beard_r], hat=hat_shells,
                decals=decals, joints=joints, info=info)


def _build_hat(C, R, sx, sz, hat_brim, hat_tip, push, rnd):
    """Crooked 6-sided cone (4 rings + drooping tip bending to +X) + wide uneven brim.
    The cone is centred on the skull (layout hat_brim only gives the brim height: in the canon it is
    the front-left brim area, centring the hat on it would leave the right side of the skull bare)."""
    Uh = R @ V((0, 0, 1))
    # hat axis: head axis tipped slightly forward and to the left (canon brim: left side lower)
    Hup = (_rot(0, 6.0, -3.0) @ Uh).normalized()
    Hr = (V((1, 0, 0)) - Hup * Hup.x).normalized()
    Hf = Hup.cross(Hr)  # = -Y-ish (front)
    brim_w = _slide(hat_brim, -push)
    # brim centre on the head axis at the brim height (just above the brow ridge)
    brim_h = max((brim_w - C).dot(Uh), 0.30 * sz) + 0.07 * sz
    O = C + Uh * brim_h + R @ V((-0.04, 0.03, 0)) * sx
    T = _slide(hat_tip, -push)

    # ---- brim: 18-gon disc, inner ring under the cone, outer ring drooping, uneven thickness
    nb = 18
    r_in, r_out = 0.74 * sx, 1.13 * sx
    top_c, bot_c = O + Hup * 0.10, O - Hup * 0.04
    it, ib, ot, ob = [], [], [], []
    for k in range(nb):
        a = 2 * math.pi * k / nb + 0.1
        d = Hr * math.cos(a) + Hf * math.sin(a)       # k=0 right, increasing -> front -> left -> back
        left = max(0.0, -math.cos(a))                   # bigger, lower on the left like the canon
        ro = r_out * (1.0 + 0.10 * left + rnd.uniform(-0.06, 0.06))
        droop = -0.10 - 0.08 * left + rnd.uniform(-0.04, 0.04)
        thick = 0.07 + rnd.uniform(0.0, 0.07)
        it.append(O + d * r_in + Hup * 0.07)
        ib.append(O + d * r_in - Hup * 0.06)
        ot.append(O + d * ro + Hup * (droop + thick * 0.5))
        ob.append(O + d * ro + Hup * (droop - thick * 0.5))
    brim = core.Shell('HAT_brim', 'HAT', 'hat')
    I_t = [brim.v(p) for p in it]; I_b = [brim.v(p) for p in ib]
    O_t = [brim.v(p) for p in ot]; O_b = [brim.v(p) for p in ob]
    ct, cb = brim.v(top_c), brim.v(bot_c)
    for k in range(nb):
        k2 = (k + 1) % nb
        brim.f(ct, I_t[k2], I_t[k])
        brim.f(I_t[k], I_t[k2], O_t[k2], O_t[k])
        brim.f(O_t[k], O_t[k2], O_b[k2], O_b[k])
        brim.f(O_b[k], O_b[k2], I_b[k2], I_b[k])
        brim.f(cb, I_b[k], I_b[k2])
    _orient(brim)

    # ---- cone: fat body rising along the hat axis with small kinks, then a hook curling over to +X
    #      and drooping to the tip (canon). Radii shrink fast only in the hook.
    B0 = O - Hup * 0.05                            # base ring sunk into the brim (no slit)
    TT = T - B0
    over = max(TT.dot(Hr), 0.6)                    # how far right the tip is
    back = TT.dot(Hf)                              # tip depth offset (layout: a bit behind)
    h = 0.93 * sz
    path = [B0,
            B0 + Hup * (0.55 * h),
            B0 + Hup * (1.00 * h) + Hr * 0.12 - Hf * 0.05,
            B0 + Hup * (1.30 * h) + Hr * (0.22 * over) + Hf * (0.12 * back),
            B0 + Hup * (1.45 * h) + Hr * (0.48 * over) + Hf * (0.40 * back),
            B0 + Hup * (1.36 * h) + Hr * (0.72 * over) + Hf * (0.70 * back),
            T + Hup * (0.30 * h) - Hr * 0.05]
    radii = [(0.82 * sx, 0.76 * sx), (0.68 * sx, 0.63 * sx), (0.52 * sx, 0.48 * sx), 0.38 * sx, 0.27 * sx,
             0.17 * sx, 0.09 * sx]
    rings, frames = _path_rings(path, radii, 6, up_hint=Hf, rolls=[0, 8, -5, 10, 3, -6, 0], rnd=rnd, jitter=0.06, phase=0.3)
    cone = _loft('HAT_cone', rings, group='HAT', mat='hat', bottom='fan', top=T)
    return [cone, brim], [B0, path[3], T], dict(hat_origin=O, hat_up=Hup)


# ============================================================== face fragments on the limbs
def face_fragment(fid, pos, normal, up, size, seed=0, host_radius=None):
    """A face melted into a limb surface: nose wedge + brow ridge (+ chin/cheek plane for the big
    profile face F01), sunk ~35% into the host so the union fuses them. Eyes/mouth are decals.

    pos: point ON the host surface (face centre ~ nose middle); normal: host normal; up: forehead
    direction along the surface (usually along the limb axis); size: face height (1.2-2.5 m).
    host_radius: optional local radius of the host tube around `up`: pieces and decals are wrapped
    onto that cylinder so wide brows do not float off a curved limb.
    Returns dict(shells=[...], decals=[...]); shells have group None (lead assigns the host limb).
    """
    rnd = random.Random(seed * 31 + sum(map(ord, fid)))
    n = V(normal).normalized()
    u = (V(up) - n * V(up).dot(n)).normalized()
    s = u.cross(n)  # viewer's right when looking at the face
    H = float(size)
    p0 = V(pos)

    def X(a, b, c):
        """face-local (a across, b up, c out of the surface; units of H) -> world, wrapped."""
        a, b, c = a * H, b * H, c * H
        if host_radius:
            Rr = host_radius
            th = a / Rr
            return p0 + u * b + (s * math.sin(th) + n * math.cos(th)) * (Rr + c) - n * Rr
        return p0 + s * a + u * b + n * c

    def nrm_at(a):
        if host_radius:
            th = a * H / host_radius
            return (s * math.sin(th) + n * math.cos(th)).normalized()
        return n

    j = lambda k=0.025: rnd.uniform(-k, k)
    big = fid == 'F01'
    k = 1.15 if big else 1.0          # F01: bigger, more profiled features
    bury = -0.12                      # buried depth (~35% of the nose wedge is under the surface)
    # nose wedge: narrow bridge growing out of the brow, broad drooping tip, flat underside
    pr = (0.27 if big else 0.21)
    nose = _hull(f'{fid}_nose', [
        X(-0.07, 0.30, bury), X(0.07, 0.30, bury),                 # bridge root (buried in the brow)
        X(0.0 + j(0.01), 0.25, 0.035),                              # bridge top (just out of the surface)
        X(0.0, -0.21 + j(), pr),                                    # tip
        X(-0.15 * k + j(0.01), -0.25, pr * 0.55), X(0.15 * k + j(0.01), -0.25, pr * 0.55),   # wings
        X(-0.20 * k, -0.27, bury), X(0.20 * k, -0.27, bury),       # buried base
        X(0.0, -0.31, bury)], group=None, mat='body')
    shells = [nose]
    # brow: low shelf over the eyes; the forehead side is flush with the surface, the ends taper
    # into it, only the middle overhangs the painted eyes a little
    bw = 0.40 * k
    brow = _hull(f'{fid}_brow', [
        X(-bw, 0.44 + j(), bury * 0.6), X(bw, 0.42 + j(), bury * 0.6),        # forehead line (buried)
        X(-bw, 0.24, bury * 0.6), X(bw, 0.25, bury * 0.6),                    # under-brow line (buried)
        X(-0.20 * k, 0.29 + j(0.01), 0.055 * k), X(0.21 * k, 0.28 + j(0.01), 0.05 * k),  # brow line
        X(0.0, 0.30, 0.045 * k),
        X(-0.34 * k, 0.31, -0.005), X(0.34 * k, 0.30, -0.005)],              # brow ends, flush
        group=None, mat='body')
    shells.append(brow)
    # low cheek planes beside the nose: flush at their edges, they just swell the face area a little
    for sg in ((1,) if big else (-1, 1)):
        shells.append(_hull(f'{fid}_cheek_' + ('r' if sg > 0 else 'l'), [
            X(sg * 0.10, 0.10, bury * 0.5), X(sg * 0.44, 0.08, bury * 0.5),
            X(sg * 0.10, -0.40, bury * 0.5), X(sg * 0.40, -0.40, bury * 0.5),
            X(sg * 0.22, 0.0, 0.035 + j(0.01)), X(sg * 0.30, -0.20, 0.04), X(sg * 0.18, -0.26, 0.03)],
            group=None, mat='body'))
    if big:
        # profile face: a flat chin plane under the mouth, rising out of the surface like the nose
        shells.append(_hull(f'{fid}_chin', [
            X(-0.30, -0.46, bury), X(0.30, -0.46, bury), X(-0.16, -0.86, bury), X(0.16, -0.86, bury),
            X(-0.17, -0.50, 0.03), X(0.17, -0.50, 0.03),                  # under the mouth, low
            X(0.0 + j(0.01), -0.70, 0.11), X(-0.10, -0.62, 0.07), X(0.10, -0.63, 0.07)],  # chin point
            group=None, mat='body'))
    # decals on the host surface
    decals = []
    for kind, (a, b), sz_ in (('eye_closed', (-0.20, 0.17), 0.20), ('eye_closed', (0.20, 0.17), 0.20),
                              ('mouth', (0.0, -0.39), 0.26)):
        nn = nrm_at(a)
        decals.append(dict(kind=kind, pos=X(a, b, 0.0), normal=nn, up=(u - nn * u.dot(nn)).normalized(), size=sz_ * H))
    return dict(shells=shells, decals=decals)


def all_shells(res):
    return res['head'] + res['beard'] + res['hat']
