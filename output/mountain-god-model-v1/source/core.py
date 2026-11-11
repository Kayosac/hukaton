"""MG01 shared geometry core (Blender bpy 5.x, run headless with `python3`).

Coordinate system (Blender, working scale 1:100 -> 1 Blender metre = 100 game metres):
  +X = canonical screen right, -Y = FRONT (towards the canonical viewer), +Z = up, ground z = 0.
  Pivot (0,0,0) = ground, centre of the S01-S03 support triangle.
FBX export later maps Blender -Y front -> Unity +Z front (Forward -Z, Up Y, bake space transform).

Everything is built as plain python lists (Shell = closed manifold piece) and only turned into
bpy objects at the end, so builders stay pure and testable.
"""
import math, random
from mathutils import Vector, Matrix, Quaternion

V = Vector


# ---------------------------------------------------------------- shells
class Shell:
    """One closed manifold piece: verts (Vector list) + faces (index tuples, CCW = outward)."""

    def __init__(self, name='shell', group=None, mat='body'):
        self.name, self.group, self.mat = name, group, mat
        self.verts, self.faces = [], []

    def v(self, p):
        self.verts.append(V(p))
        return len(self.verts) - 1

    def f(self, *ids):
        self.faces.append(tuple(ids))

    def extend(self, other):
        off = len(self.verts)
        self.verts += [V(p) for p in other.verts]
        self.faces += [tuple(i + off for i in fc) for fc in other.faces]
        return self

    def transformed(self, M):
        s = Shell(self.name, self.group, self.mat)
        s.verts = [M @ p for p in self.verts]
        s.faces = list(self.faces)
        return s

    def tri_count(self):
        return sum(len(f) - 2 for f in self.faces)


# ---------------------------------------------------------------- curves
def catmull_rom(pts, samples_per_seg=3):
    """Uniform Catmull-Rom through pts. Returns list of (point, seg_index, local_t).
    Every control point is hit exactly (local_t == 0 at it); last point appended."""
    P = [V(p) for p in pts]
    ext = [P[0] + (P[0] - P[1])] + P + [P[-1] + (P[-1] - P[-2])]
    out = []
    for i in range(len(P) - 1):
        p0, p1, p2, p3 = ext[i], ext[i + 1], ext[i + 2], ext[i + 3]
        n = samples_per_seg[i] if isinstance(samples_per_seg, (list, tuple)) else samples_per_seg
        for k in range(n):
            t = k / n
            t2, t3 = t * t, t * t * t
            q = 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3)
            out.append((q, i, t))
    out.append((P[-1], len(P) - 2, 1.0))
    return out


def lerp_param(vals, seg, t):
    """Interpolate a per-control-point value list at (seg, t). Values may be floats or tuples."""
    a, b = vals[seg], vals[min(seg + 1, len(vals) - 1)]
    s = t * t * (3 - 2 * t)  # smoothstep keeps rings near joints tight
    if isinstance(a, (tuple, list)):
        return tuple(x + (y - x) * s for x, y in zip(a, b))
    return a + (b - a) * s


def transport_frames(points, up_hint=V((0, 0, 1))):
    """Rotation-minimising frames. Returns list of (T, N, B)."""
    n = len(points)
    T = []
    for i in range(n):
        a = points[max(i - 1, 0)]
        b = points[min(i + 1, n - 1)]
        d = (b - a)
        T.append(d.normalized() if d.length > 1e-9 else V((0, 0, 1)))
    up = V(up_hint)
    N0 = up - T[0] * up.dot(T[0])
    if N0.length < 1e-6:
        N0 = V((1, 0, 0)) - T[0] * T[0].x
    N0.normalize()
    frames = [(T[0], N0, T[0].cross(N0))]
    for i in range(1, n):
        Tp, Np, Bp = frames[-1]
        q = Tp.rotation_difference(T[i])
        N = q @ Np
        N = (N - T[i] * N.dot(T[i])).normalized()
        frames.append((T[i], N, T[i].cross(N)))
    return frames


# ---------------------------------------------------------------- tube
def tube(name, pts, radii, sides=8, samples=3, up_hint=(0, 0, 1), roll=None, jitter=0.06,
         seed=0, cap_start=True, cap_end=True, tip_end=False, phase=0.0, group=None, mat='body',
         extra_rings=None, ring_jitter=0.0):
    """Low-poly tube along a Catmull-Rom spline through `pts`.

    radii : per control point float or (r_normal, r_binormal) ellipse radii.
    roll  : per control point degrees, rotates the cross-section around the tangent.
    samples: rings per segment (int or per-segment list). A ring always sits on each control
             point -> put control points on joints (elbow/knee/wrist) for rig-friendly loops.
    extra_rings: optional {seg_index: [t,...]} additional ring parameters (e.g. both sides of a joint).
    jitter: radial noise fraction for the crooked hand-made look (deterministic via seed).
    ring_jitter: per-ring scale/offset noise (organic lumps); fades to 0 at both end rings.
    tip_end: close the end into a single point (fingers, toes, branch tips).
    Returns (Shell, info) where info has the sampled centreline and per-ring frames.
    """
    rnd = random.Random(seed)
    P = [V(p) for p in pts]
    samp = catmull_rom(P, samples)
    if extra_rings:
        # additional rings at given (segment, t) params, e.g. both sides of a joint
        for seg, ts in extra_rings.items():
            for t in ts:
                samp.append((_eval_cr(P, seg, t), seg, t))
        samp.sort(key=lambda x: x[1] + x[2])
    cps = [s[0] for s in samp]
    frames = transport_frames(cps, V(up_hint))
    rad = [r if isinstance(r, (tuple, list)) else (r, r) for r in radii]
    rolls = roll if roll is not None else [0.0] * len(P)
    sh = Shell(name, group, mat)
    rings = []
    nring = len(samp)
    for idx, ((c, seg, t), (T, N, B)) in enumerate(zip(samp, frames)):
        rx, ry = lerp_param(rad, seg, t)
        if ring_jitter and 0 < idx < nring - 1:
            fade = min(idx, nring - 1 - idx) / 2.0
            k = ring_jitter * min(1.0, fade)
            sc_ = 1.0 + rnd.uniform(-k, k)
            rx, ry = rx * sc_ * (1 + rnd.uniform(-k, k) * 0.6), ry * sc_
            c = c + (N * rnd.uniform(-1, 1) + B * rnd.uniform(-1, 1)) * (k * 0.45 * (rx + ry) / 2)
        rl = math.radians(lerp_param(rolls, seg, t))
        cr, sr = math.cos(rl), math.sin(rl)
        N2, B2 = N * cr + B * sr, -N * sr + B * cr
        ring = []
        for k in range(sides):
            a = phase + 2 * math.pi * k / sides
            j = 1.0 + (rnd.uniform(-jitter, jitter) if jitter else 0.0)
            p = c + (N2 * (math.cos(a) * rx) + B2 * (math.sin(a) * ry)) * j
            ring.append(sh.v(p))
        rings.append(ring)
    n = sides
    last = len(rings) - 1
    for i in range(last):
        a, b = rings[i], rings[i + 1]
        for k in range(n):
            k2 = (k + 1) % n
            sh.f(a[k], a[k2], b[k2], b[k])
    if cap_start:
        sh.f(*reversed(rings[0]))
    if tip_end:
        T, N, B = frames[-1]
        rx, ry = rad[-1]
        tip = sh.v(cps[-1] + T * max(rx, ry) * 0.9)
        r = rings[-1]
        for k in range(n):
            sh.f(r[k], r[(k + 1) % n], tip)
    elif cap_end:
        sh.f(*rings[-1])
    info = {'centre': cps, 'frames': frames, 'rings': rings, 'samples': samp}
    return sh, info


def _eval_cr(P, seg, t):
    ext = [P[0] + (P[0] - P[1])] + P + [P[-1] + (P[-1] - P[-2])]
    p0, p1, p2, p3 = ext[seg], ext[seg + 1], ext[seg + 2], ext[seg + 3]
    t2, t3 = t * t, t * t * t
    return 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3)


# ---------------------------------------------------------------- simple solids
def blob(name, centre, radii, rings=5, sides=8, jitter=0.07, seed=0, rot=None, group=None, mat='body'):
    """Low-poly ellipsoid (UV-sphere topology with pole vertices)."""
    rnd = random.Random(seed)
    c = V(centre)
    R = rot.to_matrix() if isinstance(rot, Quaternion) else (rot if rot is not None else Matrix.Identity(3))
    sh = Shell(name, group, mat)
    top = sh.v(c + R @ V((0, 0, radii[2])))
    rows = []
    for i in range(1, rings):
        th = math.pi * i / rings
        row = []
        for k in range(sides):
            ph = 2 * math.pi * k / sides + (0.5 * math.pi / sides if i % 2 else 0)
            j = 1 + rnd.uniform(-jitter, jitter)
            p = V((math.sin(th) * math.cos(ph) * radii[0], math.sin(th) * math.sin(ph) * radii[1], math.cos(th) * radii[2])) * j
            row.append(sh.v(c + R @ p))
        rows.append(row)
    bot = sh.v(c + R @ V((0, 0, -radii[2])))
    n = sides
    for k in range(n):
        sh.f(top, rows[0][k], rows[0][(k + 1) % n])
    for i in range(len(rows) - 1):
        a, b = rows[i], rows[i + 1]
        for k in range(n):
            sh.f(a[k], b[k], b[(k + 1) % n], a[(k + 1) % n])
    for k in range(n):
        sh.f(bot, rows[-1][(k + 1) % n], rows[-1][k])
    return sh


def wedge(name, base_centre, out_normal, down, width, height, depth, group=None, mat='body'):
    """Triangular prism 'nose' wedge sticking out of a surface.
    base_centre sits ON the surface (buried 30% inside), out_normal = surface normal,
    down = direction the nose points along the surface (nose bridge -> tip)."""
    n = V(out_normal).normalized()
    d = (V(down) - n * V(down).dot(n)).normalized()
    s = d.cross(n).normalized()
    c = V(base_centre) - n * depth * 0.35
    top = c - d * height * 0.5
    bot = c + d * height * 0.5
    sh = Shell(name, group, mat)
    # bridge line (top) is narrow, tip (bottom) protrudes most
    a = sh.v(top - s * width * 0.18)
    b = sh.v(top + s * width * 0.18)
    tip_out = sh.v(top + n * depth * 0.55)
    e = sh.v(bot - s * width * 0.5)
    f = sh.v(bot + s * width * 0.5)
    g = sh.v(bot + n * depth * 1.0 + d * height * 0.08)
    # faces (outward)
    sh.f(a, b, tip_out)           # top cap
    sh.f(e, g, f)                 # bottom cap
    sh.f(a, tip_out, g, e)        # left side
    sh.f(b, f, g, tip_out)        # right side
    sh.f(a, e, f, b)              # back (buried)
    return sh


# ---------------------------------------------------------------- helpers
def frame_matrix(origin, fwd, up):
    """4x4 matrix with local +Y = fwd, local +Z = up (orthonormalised), local +X = fwd x up."""
    f = V(fwd).normalized()
    u = (V(up) - f * V(up).dot(f)).normalized()
    x = f.cross(u)
    M = Matrix((
        (x.x, f.x, u.x, origin[0]),
        (x.y, f.y, u.y, origin[1]),
        (x.z, f.z, u.z, origin[2]),
        (0, 0, 0, 1)))
    return M


def shells_tri_count(shells):
    return sum(s.tri_count() for s in shells)
