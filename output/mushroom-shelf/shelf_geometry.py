"""Bracket-fungus shelf («гриб-полка», трутовик) for the Mushroom trampoline card.

Geometry is built in UNITY space: x along the wall, y up, z out of the wall; the origin is the point where the card
hit the wall (the hinge), the wall surface is z = 0 and the back edge is sunk 0.15 m into it.

Look (matches the floor trampoline mushroom: same red cap / tan gills / cream spots materials, chunky low-poly facets):
  * a lopsided, lobed half-disc with a small bite out of the margin;
  * a level plateau on top (the landing surface) made of rolled growth zones that step down a little toward the margin;
  * a cream, ruffled lip that droops unevenly (more on one side);
  * flat irregular cream spot plates on top, like the floor mushroom's spots;
  * a tan underside with radial gill ridges, thick at the wall, thin at the lip; the tips thin out into the wall.

build() returns (vertices, normals, submeshes): submesh 0 = top (cap material), 1 = underside + hidden back
(gills material), 2 = lip + spots (spots material). collider() returns the convex hull of the visible body
(spots are 1–1.5 cm plates and stay outside it), at most 255 triangles.
"""
import math

import numpy as np
from scipy.spatial import ConvexHull

SUNK = 0.15          # back edge depth inside the wall, m
TOP = 0.06           # plateau height over the hinge, m
ZONE_EDGES = [0.0, 0.2, 0.36, 0.5, 0.62, 0.72, 0.81, 0.885, 0.94]   # growth zones (denser toward the margin)
RIM_START = 0.94     # cream margin starts here on top
RING_EDGES = (1, 3, 5, 7)   # zone edges drawn as thin tan growth rings (gills material)
RING_HALF = 0.013    # half width of a ring line, in rho
N_THETA = 32         # segments around the half-disc (even: the gill ridges alternate)
JOIN = 0.0           # height where top and bottom meet at the side tips


def outline_scale(t):
    """Lobes and one small bite along the margin (t in [0, pi]); faded at the tips so nothing folds back."""
    lobes = 0.06 * math.sin(3 * t + 0.5) + 0.03 * math.sin(5 * t + 2.0) + 0.02 * math.sin(9 * t + 1.1)
    bite = -0.045 * math.exp(-((t - 2.25) / 0.1) ** 2)
    return 1 + (lobes + bite) * max(0.0, math.sin(t)) ** 0.5


def half_width(t):
    return 0.915 + 0.05 * math.cos(t)          # the +x side is a bit wider: lopsided


def outline(t):
    """Margin point (x, z) at angle t; t = 0 is the +x end on the wall, t = pi the -x end."""
    f = outline_scale(t)
    return half_width(t) * math.cos(t) * f, 0.99 * math.sin(t) * f


def taper(t):
    """Thickness share along the margin: the shelf thins out where its sides run into the wall."""
    return max(0.0, math.sin(t)) ** 0.45


def droop(t):
    """The lip hangs lower on the -x side than on the +x side."""
    return 0.08 - 0.035 * math.cos(t) + 0.02 * math.sin(3 * t + 0.4)


def ruffle(t):
    """Up-and-down waves along the lip."""
    return 0.02 * math.sin(7 * t + 1.0) + 0.008 * math.sin(13 * t + 0.3)


def lip_thickness(t):
    return 0.075 + 0.018 * math.sin(4 * t + 0.3)


def back_bottom(t):
    return -0.33 - 0.05 * math.sin(3 * t + 1.2) - 0.02 * math.sin(7 * t)


def zones(rho, t):
    """Rolled growth zones: a flat-topped roll per zone with a sharp groove between, each outer zone a bit lower."""
    for k in range(len(ZONE_EDGES) - 1):
        a, b = ZONE_EDGES[k], ZONE_EDGES[k + 1]
        if a <= rho < b:
            u = (rho - a) / (b - a)
            return 0.02 * (1 - abs(2 * u - 1) ** 3) - 0.004 * k
    return -0.004 * (len(ZONE_EDGES) - 1)


def lip_weight(rho):
    return max(0.0, (rho - 0.78) / 0.22) ** 2


def top_height(t, rho, with_zones=True):
    y = TOP - droop(t) * lip_weight(rho) + ruffle(t) * lip_weight(rho)
    if with_zones:
        y += zones(min(rho, RIM_START - 1e-6), t) * (0.4 + 0.6 * taper(t))
    y += 0.005 * math.sin(3.1 * t + 7 * rho) * math.sin(2.3 * rho + t)   # a slight unevenness
    tp = taper(t)
    return y * tp + JOIN * (1 - tp)


def bottom_height(t, rho, ridged=False):
    lip = top_height(t, 1.0, False) - lip_thickness(t) * taper(t)
    back = back_bottom(t)
    y = lip + (back - lip) * (1 - rho) - 0.03 * math.sin(math.pi * rho)
    tp = taper(t)
    y = y * tp + JOIN * (1 - tp)
    if ridged:
        # gill ridges: every other sample is pushed up into a groove, fading at the lip and at the wall
        y += 0.035 * min(1.0, rho * 3) * min(1.0, (1 - rho) * 8) * tp
    return y


def plan_point(t, rho):
    """Plan position (x, z) at angle t and margin share rho; the rings wobble a little (crooked growth)."""
    x, z = outline(t)
    r = rho + 0.02 * math.sin(5 * t + 1.3) * math.sin(math.pi * min(1.0, rho))
    return np.array([x, -SUNK + (z + SUNK) * r])


def outward_plan(t):
    x, z = outline(t)
    dt = 1e-3
    x1, z1 = outline(min(math.pi, t + dt))
    x0, z0 = outline(max(0.0, t - dt))
    out = np.array([z1 - z0, -(x1 - x0)])
    if np.dot(out, np.array([x, z])) < 0:
        out = -out
    return out / max(1e-9, np.linalg.norm(out))


def top_bands():
    """Rows of the red top, band by band (a zone between rings), and the rows of each tan ring line."""
    bands, rings = [], []
    for k in range(len(ZONE_EDGES) - 1):
        a, b = ZONE_EDGES[k], ZONE_EDGES[k + 1]
        lo = a + RING_HALF if k in RING_EDGES else a
        hi = b - RING_HALF if (k + 1) in RING_EDGES else b
        bands.append([lo + (hi - lo) * j / 3 for j in range(4)])
    for k in RING_EDGES:
        e = ZONE_EDGES[k]
        rings.append([e - RING_HALF, e, e + RING_HALF])
    # merge bands that touch (no ring between them) into continuous strips
    strips = [bands[0]]
    for band in bands[1:]:
        if abs(band[0] - strips[-1][-1]) < 1e-9:
            strips[-1] = strips[-1] + band[1:]
        else:
            strips.append(band)
    return strips, rings


class Builder:
    def __init__(self):
        self.v = []
        self.tris = []

    def add(self, p):
        self.v.append(np.asarray(p, dtype=np.float64))
        return len(self.v) - 1

    def grid(self, rows, outward):
        """Quads between consecutive rows of vertex indices, one winding for the whole grid: the one whose Unity
        normal cross(b - a, c - a) agrees with outward(centroid) by area-weighted vote."""
        tris, votes = [], 0.0
        for r in range(len(rows) - 1):
            a_row, b_row = rows[r], rows[r + 1]
            for i in range(len(a_row) - 1):
                q = [a_row[i], a_row[i + 1], b_row[i + 1], b_row[i]]
                for tri in ((q[0], q[1], q[2]), (q[0], q[2], q[3])):
                    if len({tri[0], tri[1], tri[2]}) < 3:
                        continue
                    pa, pb, pc = (self.v[k] for k in tri)
                    fn = np.cross(pb - pa, pc - pa)
                    if np.linalg.norm(fn) < 1e-12:
                        continue
                    votes += np.dot(fn, outward((pa + pb + pc) / 3))
                    tris.append(tri)
        if votes < 0:
            tris = [(a, c, b) for (a, b, c) in tris]
        self.tris += tris

    def fan(self, centre, ring, up):
        tris = []
        for i in range(len(ring)):
            tri = (centre, ring[i], ring[(i + 1) % len(ring)])
            pa, pb, pc = (self.v[k] for k in tri)
            if np.dot(np.cross(pb - pa, pc - pa), up) < 0:
                tri = (tri[0], tri[2], tri[1])
            tris.append(tri)
        self.tris += tris


SPOTS = [  # (t, rho, radius, sides, turn)
    (1.18, 0.42, 0.085, 6, 0.3), (1.95, 0.6, 0.065, 5, 1.1), (1.58, 0.24, 0.055, 6, 2.0),
    (2.55, 0.47, 0.095, 7, 0.7), (0.72, 0.64, 0.06, 5, 2.6), (2.2, 0.78, 0.045, 5, 0.2), (0.95, 0.85, 0.04, 5, 1.7),
]


def build():
    ts = np.linspace(0, math.pi, N_THETA + 1)

    # ---- top (cap material): rho 0 .. RIM_START, in strips between the ring lines
    top = Builder()
    strips, rings = top_bands()
    for strip in strips:
        rows = []
        for rho in strip:
            row = []
            for t in ts:
                x, z = plan_point(t, rho)
                row.append(top.add([x, top_height(t, rho), z]))
            rows.append(row)
        top.grid(rows, lambda c: np.array([0, 1, 0]))

    # ---- lip (spots material): cream margin on top, then the rolled edge down to the underside
    rim = Builder()
    rim_rows = []
    for rho in (RIM_START, 0.97, 1.0):
        row = []
        for t in ts:
            x, z = plan_point(t, rho)
            row.append(rim.add([x, top_height(t, rho), z]))
        rim_rows.append(row)
    for ang in (55, 15, -25, -65):
        row = []
        for t in ts:
            x, z = outline(t)
            out = outward_plan(t)
            ytop, ybot = top_height(t, 1.0), bottom_height(t, 1.0)
            mid, half = (ytop + ybot) / 2, (ytop - ybot) / 2
            bulge = half * 0.9 * taper(t)
            a = math.radians(ang)
            row.append(rim.add([x + out[0] * bulge * math.cos(a), mid + half * math.sin(a), z + out[1] * bulge * math.cos(a)]))
        rim_rows.append(row)
    row = []
    for t in ts:
        x, z = plan_point(t, 1.0)
        row.append(rim.add([x, bottom_height(t, 1.0), z]))
    rim_rows.append(row)
    rim.grid(rim_rows, lambda c: np.array([c[0], 0.6 if c[1] > -0.02 else -0.4, c[2] + 0.1]))

    # ---- spots (spots material): flat irregular plates lying on the top, a little raised, with a short skirt
    rng = np.random.default_rng(11)
    for (t, rho, radius, sides, turn) in SPOTS:
        cx, cz = plan_point(t, rho)
        base = top_height(t, rho)
        lift = 0.011 + 0.004 * rng.random()
        tilt = rng.normal(0, 0.06, 2)           # each plate leans a little: crooked
        centre = rim.add([cx, base + lift, cz])
        ring, skirt = [], []
        for s in range(sides):
            a = 2 * math.pi * s / sides + turn + rng.normal(0, 0.18)
            r = radius * (0.75 + 0.45 * rng.random())
            px, pz = cx + r * math.cos(a), cz + r * math.sin(a) * 0.85
            py = base + lift * 0.7 + tilt[0] * (px - cx) + tilt[1] * (pz - cz)
            ring.append(rim.add([px, py, pz]))
            skirt.append(rim.add([px, base - 0.012, pz]))
        rim.fan(centre, ring, np.array([0, 1, 0]))
        rim.grid([ring + ring[:1], skirt + skirt[:1]], lambda c, cx=cx, cz=cz: np.array([c[0] - cx, 0, c[2] - cz]))

    # ---- underside (gills material): rho 1 .. 0 with radial ridges, then the back face hidden in the wall
    under = Builder()
    # the tan growth-ring lines on top belong to the gills material too
    for ring in rings:
        rows = []
        for rho in ring:
            row = []
            for t in ts:
                x, z = plan_point(t, rho)
                row.append(under.add([x, top_height(t, rho), z]))
            rows.append(row)
        under.grid(rows, lambda c: np.array([0, 1, 0]))
    u_rows = []
    for rho in np.linspace(1.0, 0.0, 9):
        row = []
        for i, t in enumerate(ts):
            x, z = plan_point(t, rho)
            row.append(under.add([x, bottom_height(t, rho, ridged=(i % 2 == 1)), z]))
        u_rows.append(row)
    under.grid(u_rows, lambda c: np.array([0, -1, 0]))
    back_low, back_high = [], []
    for t in ts:
        x, z = plan_point(t, 0.0)
        back_low.append(under.add([x, bottom_height(t, 0.0), z]))
        back_high.append(under.add([x, top_height(t, 0.0), z]))
    under.grid([back_low, back_high], lambda c: np.array([0, 0, -1]))

    vertices, submeshes, off = [], [], 0
    for part in (top, under, rim):
        vertices += part.v
        submeshes.append(np.array(part.tris) + off)
        off += len(part.v)
    vertices, submeshes = compact(np.array(vertices), submeshes)
    return smooth_normals(vertices, submeshes)


def compact(vertices, submeshes):
    """Drops unused vertices and merges exact duplicates inside each submesh (keeps contiguous ranges)."""
    new_v, new_subs = [], []
    for tris in submeshes:
        remap, local = {}, []
        for idx in tris.reshape(-1):
            key = tuple(np.round(vertices[idx], 6))
            if key not in remap:
                remap[key] = len(new_v)
                new_v.append(vertices[idx])
            local.append(remap[key])
        arr = np.array(local).reshape(-1, 3)
        new_subs.append(np.array([tri for tri in arr if len(set(tri.tolist())) == 3]))
    return np.array(new_v), new_subs


CREASE = (50, 22, 45)   # auto-smooth angle per submesh: soft zone rolls on top, faceted gill ridges, soft lip


def smooth_normals(vertices, submeshes, weld_angle=45):
    """Auto-smooth: every face corner averages only the neighbouring faces within <crease> degrees of its face, so
    the knife-thin tips, the gill ridges and the spot edges stay crisp. Corners with equal normals share a vertex.
    Then vertices on the same spot across submeshes (cream margin into the red top, lip into the underside) are
    welded when their normals are close. Returns (vertices, normals, submeshes), contiguous ranges per submesh."""
    out_v, out_n, out_subs = [], [], []
    for sub, tris in enumerate(submeshes):
        cos_crease = math.cos(math.radians(CREASE[min(sub, len(CREASE) - 1)]))
        a, b, c = vertices[tris[:, 0]], vertices[tris[:, 1]], vertices[tris[:, 2]]
        fn = np.cross(b - a, c - a)
        unit = fn / np.maximum(1e-12, np.linalg.norm(fn, axis=1))[:, None]
        faces_of = {}
        for f, tri in enumerate(tris):
            for i in tri:
                faces_of.setdefault(int(i), []).append(f)
        key_to_new, new_tris = {}, []
        for f, tri in enumerate(tris):
            corners = []
            for i in tri:
                acc = np.zeros(3)
                for g in faces_of[int(i)]:
                    if np.dot(unit[g], unit[f]) >= cos_crease:
                        acc += fn[g]
                nrm = acc / max(1e-12, np.linalg.norm(acc))
                key = (int(i),) + tuple(np.round(nrm, 3))
                if key not in key_to_new:
                    key_to_new[key] = len(out_v)
                    out_v.append(vertices[i])
                    out_n.append(nrm)
                corners.append(key_to_new[key])
            new_tris.append(corners)
        out_subs.append(np.array(new_tris))
    out_v, out_n = np.array(out_v), np.array(out_n)
    keys = {}
    for i, p in enumerate(np.round(out_v, 5)):
        keys.setdefault(tuple(p), []).append(i)
    cos_weld = math.cos(math.radians(weld_angle))
    welded = out_n.copy()
    for group in keys.values():
        if len(group) < 2:
            continue
        for i in group:
            acc = np.zeros(3)
            for j in group:
                if np.dot(out_n[i], out_n[j]) >= cos_weld:
                    acc += out_n[j]
            welded[i] = acc / max(1e-12, np.linalg.norm(acc))
    return out_v, welded, out_subs


def collider_points(theta_step=2, tips=5):
    """The visible body sampled for the hull: the zone tops, the lip's outermost bulge, the underside, the back."""
    pts = []
    every = np.linspace(0, math.pi, N_THETA + 1)
    ts = np.unique(np.concatenate([every[:tips], every[-tips:], every[::theta_step], [math.pi]]))
    for t in ts:
        for rho in (0.0, 0.45, 0.74, 0.88, 1.0):
            x, z = plan_point(t, rho)
            if rho < RIM_START:
                y = max(top_height(t, r) for r in np.linspace(max(0, rho - 0.14), min(RIM_START, rho + 0.1), 13))
            else:
                y = max(top_height(t, r) for r in np.linspace(RIM_START, 1.0, 5))
            pts.append([x, y, z])
        for rho in (1.0, 0.5, 0.0):
            x, z = plan_point(t, rho)
            pts.append([x, bottom_height(t, rho) - 0.002, z])
        x, z = outline(t)
        out = outward_plan(t)
        ytop, ybot = top_height(t, 1.0), bottom_height(t, 1.0)
        bulge = (ytop - ybot) / 2 * 0.9 * taper(t)
        pts.append([x + out[0] * bulge, (ytop + ybot) / 2, z + out[1] * bulge])
    return np.array(pts)


def collider(theta_step=2, tips=5, max_triangles=255):
    """Convex hull of the body with outward Unity winding; coarsens the sampling until it fits in 255 triangles."""
    for step in range(theta_step, 8):
        pts = collider_points(step, tips)
        hv = pts[ConvexHull(pts).vertices]
        hull = ConvexHull(hv)
        if len(hull.simplices) <= max_triangles:
            break
    centre = hv.mean(axis=0)
    tris = []
    for s in hull.simplices:
        a, b, c = hv[s[0]], hv[s[1]], hv[s[2]]
        if np.dot(np.cross(b - a, c - a), (a + b + c) / 3 - centre) < 0:
            s = [s[0], s[2], s[1]]
        tris.append(list(s))
    tris = np.array(tris)
    a, b, c = hv[tris[:, 0]], hv[tris[:, 1]], hv[tris[:, 2]]
    fn = np.cross(b - a, c - a)
    n = np.zeros_like(hv)
    for k in range(3):
        np.add.at(n, tris[:, k], fn)
    n /= np.maximum(1e-12, np.linalg.norm(n, axis=1))[:, None]
    return hv, n, tris


if __name__ == "__main__":
    v, n, subs = build()
    print("vertices", len(v), "triangles", [len(s) for s in subs], "bbox", v.min(0).round(3), v.max(0).round(3))
    cv, cn, ct = collider()
    print("collider", len(cv), "vertices", len(ct), "triangles")
