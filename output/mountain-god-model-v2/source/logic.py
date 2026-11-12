"""Limb-logic audit: every limb must start on the body and end in a hand/foot, and must not merge with
other limbs along the way. Reports every contact between shells of different groups with where it
happens along each limb (metres from the limb root).
Run: python3 -I logic.py [out.json]
"""
import sys, os, json, math
sys.path.insert(0, '/home/claude/work/mg')
import bpy
from mathutils import Vector as V
from mathutils.bvhtree import BVHTree
import core, bl, layout as L

BODY_GROUPS = {'CORE', 'HEAD'}
# intentional contacts between different limbs: cradle palms hold the thighs
ALLOWED = {('C01', 'L01'), ('C02', 'L02')}
GAP = 0.12  # closer than this (without intersecting) also counts as a contact


def limb_centre(lid):
    lb = next(l for l in L.LIMBS if l['id'] == lid)
    pts = [s[0] for s in core.catmull_rom(lb['pts'], 6)]
    acc = [0.0]
    for a, b in zip(pts, pts[1:]):
        acc.append(acc[-1] + (b - a).length)
    return pts, acc


END_PARTS = ('palm', 'thumb', 'wrist', 'foot', 'toe', '_f0', '_f1', '_f2', '_f3', '_f4', 'heel', 'sole')


def along(lid, p, shell_name=''):
    """(metres from root, total length) of the closest centre-line point; hands/feet count as the end."""
    if lid in BODY_GROUPS or not any(l['id'] == lid for l in L.LIMBS):
        return None, None
    pts, acc = limb_centre(lid)
    if any(k in shell_name for k in END_PARTS) and shell_name != lid:
        return acc[-1], acc[-1]
    i = min(range(len(pts)), key=lambda k: (pts[k] - p).length)
    return acc[i], acc[-1]


def audit(shells):
    trees, boxes = [], []
    for s in shells:
        vs = [tuple(v) for v in s.verts]
        trees.append(BVHTree.FromPolygons(vs, [list(f) for f in s.faces]))
        mn = V((min(v.x for v in s.verts), min(v.y for v in s.verts), min(v.z for v in s.verts)))
        mx = V((max(v.x for v in s.verts), max(v.y for v in s.verts), max(v.z for v in s.verts)))
        boxes.append((mn, mx))
    out = []
    n = len(shells)
    for i in range(n):
        gi = shells[i].group or shells[i].name
        for j in range(i + 1, n):
            gj = shells[j].group or shells[j].name
            if gi == gj:
                continue
            (a0, a1), (b0, b1) = boxes[i], boxes[j]
            if any(a1[k] + GAP < b0[k] or b1[k] + GAP < a0[k] for k in range(3)):
                continue
            pairs = trees[i].overlap(trees[j])
            kind, pts = None, []
            if pairs:
                kind = 'intersect'
                for fi, fj in pairs[:200]:
                    f = shells[i].faces[fi]
                    pts.append(sum((shells[i].verts[k] for k in f), V()) / len(f))
            else:
                small, big = (i, j) if len(shells[i].verts) < len(shells[j].verts) else (j, i)
                for v in shells[small].verts:
                    hit = trees[big].find_nearest(v, GAP)
                    if hit[0] is not None:
                        kind = 'near'
                        pts.append(V(v))
            if not kind:
                continue
            c = sum(pts, V()) / len(pts)
            sa, la = along(gi, c, shells[i].name)
            sb, lb_ = along(gj, c, shells[j].name)
            out.append(dict(a=gi, a_shell=shells[i].name, b=gj, b_shell=shells[j].name, kind=kind,
                            where=[round(x, 2) for x in c], a_from_root=None if sa is None else round(sa, 1),
                            a_len=None if la is None else round(la, 1), b_from_root=None if sb is None else round(sb, 1),
                            b_len=None if lb_ is None else round(lb_, 1), n=len(pts)))
    return out


def judge(c):
    """Classify a contact: ok (root on body / own grip) or PROBLEM."""
    a, b = c['a'], c['b']
    pair = tuple(sorted((a, b)))
    if pair in ALLOWED:
        return 'ok-grip'
    # the cradle hands hold the pelvis/hub and may brush the other thigh near the crotch
    for hand, other in ((a, b), (b, a)):
        sh = c['a_shell'] if hand == a else c['b_shell']
        if hand in ('C01', 'C02') and sh != hand and (other in BODY_GROUPS or other.startswith('L')):
            return 'ok-grip'
    body = a in BODY_GROUPS or b in BODY_GROUPS
    if body:
        limb = b if a in BODY_GROUPS else a
        s = c['b_from_root'] if a in BODY_GROUPS else c['a_from_root']
        if limb in BODY_GROUPS:
            return 'ok-body'
        if s is None:
            return 'ok-body'
        return 'ok-root' if s <= 3.0 else 'PROBLEM: limb touches the body far from its root'
    # two limbs: allowed only if BOTH are at their roots (e.g. supports meeting inside the pelvis mass)
    if c['a_from_root'] is not None and c['b_from_root'] is not None and c['a_from_root'] <= 1.0 and c['b_from_root'] <= 1.0:
        return 'ok-shared-root'
    return 'PROBLEM: limbs merge'


if __name__ == '__main__':
    out = sys.argv[1] if len(sys.argv) > 1 else '/home/claude/work/out/logic.json'
    sys.argv = [sys.argv[0], '/home/claude/work/out/_logic_tmp']
    import assemble
    bl.reset()
    shells, ctx = assemble.build_shells()
    res = audit(shells)
    for c in res:
        c['verdict'] = judge(c)
    probs = [c for c in res if c['verdict'].startswith('PROBLEM')]
    json.dump(res, open(out, 'w'), indent=1)
    for c in sorted(res, key=lambda c: c['verdict']):
        print(f"{c['verdict']:<46} {c['a']:>5}/{c['a_shell']:<14} {c['b']:>5}/{c['b_shell']:<14} {c['kind']:<9} at {c['where']} "
              f"root-dist a={c['a_from_root']}/{c['a_len']} b={c['b_from_root']}/{c['b_len']}")
    print('PROBLEMS', len(probs))
