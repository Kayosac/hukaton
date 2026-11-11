"""Rig guides for the future skeleton (no armature is delivered with the model).

Writes joint chains (world positions, Blender metres at working scale 1:100) to JSON and creates a hidden
'MG01_RigGuides' collection of empties + poly-curves in the .blend. A separate test (rigtest.py) builds a
throw-away armature from these guides to prove the mesh deforms cleanly.
"""
import json
import bpy
from mathutils import Vector as V
import layout as L


def _v(p):
    return [round(float(c), 4) for c in p]


def limb_chain(lb):
    """Every control point of the limb centre-line, root first. Joint names where defined."""
    names = {i: n for n, i in lb['joints'].items()}
    out = []
    for i, p in enumerate(lb['pts']):
        out.append(dict(name=f"{lb['id']}_{names.get(i, 'seg%02d' % i)}", pos=_v(p)))
    return out


def collect(head_joints=None, hand_joints=None, extra=None):
    """Build the guide dictionary. hand_joints: {hid: {chain: [Vector...]}} from hands.py builders."""
    T = L.TORSO['pts']
    g = dict(
        units='Blender metres at working scale 1:100 (x100 = proposed in-game metres)',
        axes='Blender: +X canon screen-right, -Y front, +Z up. FBX export: Forward -Z, Up Y -> Unity +Z front, +Y up.',
        pivot=[0, 0, 0],
        chains={},
        parents={},
        ik_targets={},
        notes='Generic rig proposal: one chain per arm (9) and leg (4); support hands S01-S03 get 3 independent '
              'contact targets; cradle hands follow the pelvis. Fingers may be merged into simplified chains.',
    )
    g['chains']['ROOT'] = [dict(name='Root', pos=[0, 0, 0])]
    g['chains']['SPINE'] = [dict(name='Pelvis', pos=_v(T[1])), dict(name='Spine', pos=_v(T[2])),
                            dict(name='Chest', pos=_v(T[3])), dict(name='Neck', pos=_v(T[4])),
                            dict(name='Head', pos=_v(T[5]))]
    g['parents']['SPINE'] = 'Root'
    g['chains']['HUB'] = [dict(name='PelvisLow_00', pos=_v(L.HUB['pts'][0])), dict(name='PelvisLow_01', pos=_v(L.HUB['pts'][-1]))]
    g['parents']['HUB'] = 'Pelvis'
    attach = {'C01': 'Chest', 'C02': 'Chest', 'A01': 'Chest', 'A02': 'Chest', 'A03': 'Spine', 'A04': 'Neck',
              'S01': 'PelvisLow_01', 'S02': 'PelvisLow_01', 'S03': 'PelvisLow_01',
              'L01': 'Pelvis', 'L02': 'Pelvis', 'L03': 'Pelvis', 'L04': 'Pelvis'}
    for lb in L.LIMBS:
        g['chains'][lb['id']] = limb_chain(lb)
        g['parents'][lb['id']] = attach[lb['id']]
    if head_joints:
        for k, pts in head_joints.items():
            g['chains']['HEAD_' + k] = [dict(name=f'{k}_{i:02d}', pos=_v(p)) for i, p in enumerate(pts)]
            g['parents']['HEAD_' + k] = 'Head' if k != 'neck' else 'Neck'
    if hand_joints:
        for hid, chains in hand_joints.items():
            for cname, pts in chains.items():
                key = f'{hid}_{cname}'
                g['chains'][key] = [dict(name=f'{key}_{i:02d}', pos=_v(p)) for i, p in enumerate(pts)]
                if hid.startswith('L'):
                    g['parents'][key] = f"{hid}_ankle" if cname == 'foot' else f"{hid}_foot_00"
                else:
                    g['parents'][key] = f"{hid}_wrist" if cname == 'wrist' else f"{hid}_wrist_00"
    for sid, d in L.SUPPORT_HANDS.items():
        c = sum(d['tips'], V()) / 5
        g['ik_targets'][sid] = dict(name=f'IK_{sid}_contact', pos=_v((c.x, c.y, 0.0)),
                                    note='ground contact target, adjust to terrain')
    if extra:
        g.update(extra)
    return g


def write_json(g, path):
    with open(path, 'w') as f:
        json.dump(g, f, indent=1, ensure_ascii=False)


def build_guides(g, collection_name='MG01_RigGuides'):
    col = bpy.data.collections.get(collection_name) or bpy.data.collections.new(collection_name)
    if col.name not in bpy.context.scene.collection.children:
        bpy.context.scene.collection.children.link(col)
    for key, chain in g['chains'].items():
        for j in chain:
            e = bpy.data.objects.new('RG_' + j['name'], None)
            e.empty_display_type = 'SPHERE'
            e.empty_display_size = 0.12
            e.location = j['pos']
            col.objects.link(e)
        if len(chain) > 1:
            cu = bpy.data.curves.new('RGC_' + key, 'CURVE')
            cu.dimensions = '3D'
            sp = cu.splines.new('POLY')
            sp.points.add(len(chain) - 1)
            for i, j in enumerate(chain):
                sp.points[i].co = (*j['pos'], 1)
            ob = bpy.data.objects.new('RGC_' + key, cu)
            col.objects.link(ob)
    col.hide_render = True
    return col
