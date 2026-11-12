"""Animation preview of the MG01 test rig: 5 short clips, FK + IK on the 224-bone test armature.
Renders two passes per frame: A = shaded model (Cycles or Workbench), B = bones only (Workbench, transparent).
Run: python3 -I anim_preview.py MODE OUTDIR [clip,clip..] [step]
MODE: preview (workbench, fast) | final (cycles)"""
import sys, os, math, json
sys.path.insert(0, '/home/claude/work/mg')
import bpy
from mathutils import Vector as V, Quaternion as Q
import bl

MODE, OUT = sys.argv[1], sys.argv[2]
ONLY = sys.argv[3].split(',') if len(sys.argv) > 3 and sys.argv[3] != 'all' else None
STEP = int(sys.argv[4]) if len(sys.argv) > 4 else 1
W, H = (440, 500) if MODE == 'final' else (330, 375)
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath='/home/claude/work/anim/anim_base.blend')
sc = bpy.context.scene
vl = bpy.context.view_layer
arm = bpy.data.objects['MG01_TestRig']
G = json.load(open('/home/claude/work/out/v3c/MG01_rig_guides.json'))
off = V((-0.288, 2.463, 0.0))
for o in bpy.data.objects:
    if o.name.startswith('RG') or o.type == 'CURVE':
        o.hide_render = True
arm.hide_render = True
body_objs = [bpy.data.objects[n] for n in ('MG01_Body', 'MG01_Cloth', 'MG01_Leaves')]
bviz = bpy.data.objects['MG01_BoneViz']

# ---------------------------------------------------------------- look (same as presentation renders)
w = bpy.data.worlds.new('sky'); sc.world = w; w.use_nodes = True
bg = w.node_tree.nodes['Background']
bg.inputs[0].default_value = bl.hex_rgb('#B3A8DA'); bg.inputs[1].default_value = 0.9
sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN'))
sun.data.energy = 2.6; sun.data.angle = math.radians(8); sun.data.color = (1.0, 0.97, 0.94)
sun.rotation_euler = (math.radians(52), math.radians(-18), math.radians(-28))
sc.collection.objects.link(sun)
bpy.ops.mesh.primitive_circle_add(vertices=64, radius=30, fill_type='NGON', location=(0, 0, 0))
gnd = bpy.context.active_object
gm = bpy.data.materials.new('ground'); gm.use_nodes = True
gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = bl.hex_rgb('#6E5F86')
gm.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 1.0
gm.diffuse_color = bl.hex_rgb('#6E5F86')
gnd.data.materials.append(gm)
for m in bpy.data.materials:
    if m.use_nodes:
        for n in m.node_tree.nodes:
            if n.type == 'BSDF_PRINCIPLED':
                n.inputs['Roughness'].default_value = 1.0
                if 'Specular IOR Level' in n.inputs:
                    n.inputs['Specular IOR Level'].default_value = 0.0
model_objs = body_objs + [gnd]
sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage = W, H, 100
sc.view_settings.view_transform = 'Standard'
sc.view_settings.look = 'None'
sc.cycles.device = 'CPU'
sc.cycles.samples = 14
sc.cycles.use_denoising = True
sc.cycles.max_bounces = 3
CAMS = {
    'q3': dict(loc=(off.x - 24, off.y - 31, 9.5), look=(off.x + 0.6, off.y, 10.4), lens=49),
    'front': dict(loc=(off.x + 2.0, off.y - 38, 8.0), look=(off.x + 0.3, off.y, 9.4), lens=50),
    'q3r': dict(loc=(off.x + 25, off.y - 30, 9.5), look=(off.x + 0.4, off.y, 10.4), lens=49),
    'slam': dict(loc=(off.x - 12, off.y - 31, 7.5), look=(off.x - 0.2, off.y - 1.0, 9.6), lens=43),
    'head': dict(loc=(off.x + 5.5, off.y - 15.5, 17.0), look=(off.x + 1.6, off.y, 15.6), lens=50),
}

def set_cam(k):
    c = CAMS[k]
    bl.make_camera(sc, c['loc'], look_at=c['look'], lens=c['lens'], name='cam')

# ---------------------------------------------------------------- pose helpers
bones = arm.data.bones
pbs = arm.pose.bones
acc = {}
def chain(key):
    return [j['name'] for j in G['chains'][key][:-1] if j['name'] in bones]
def bdir(b):
    bo = bones[b]
    return (bo.tail_local - bo.head_local).normalized()
def rotw(b, axis, deg):
    """rotate bone b about an armature-space axis (FK, relative to the parent's posed frame)"""
    if b not in bones or abs(deg) < 1e-6:
        return
    la = bones[b].matrix_local.to_3x3().inverted() @ V(axis)
    if la.length < 1e-8:
        return
    acc[b] = Q(la.normalized(), math.radians(deg)) @ acc.get(b, Q())
Z = V((0, 0, 1)); X = V((1, 0, 0)); Y = V((0, 1, 0))
def lift_axis(b):
    a = bdir(b).cross(Z)
    return a.normalized() if a.length > 0.2 else bdir(b).cross(Y).normalized()
def lift(b, deg):
    rotw(b, lift_axis(b), deg)
def yaw(b, deg):
    rotw(b, Z, deg)
def twist(b, deg):
    rotw(b, bdir(b), deg)
CURL = {}
for key, ch in G['chains'].items():
    if '_f' in key or 'thumb' in key or key.endswith('_toe'):
        p = [V(j['pos']) for j in ch]
        a = (p[1] - p[0]).cross(p[-1] - p[-2])
        if a.length < 1e-5:
            a = (p[1] - p[0]).cross(Z)
        CURL[key] = (a.normalized(), [j['name'] for j in ch[:-1]])
def curl(hand, deg, thumb_scale=0.6):
    for key, (ax, names) in CURL.items():
        if key.startswith(hand + '_'):
            s = thumb_scale if 'thumb' in key else 1.0
            for i, n in enumerate(names):
                rotw(n, ax, deg * s * (0.8 + 0.25 * i))

# IK for the front support hand (guides: IK_S02_contact) - target empty drives S02 shoulder/elbow/forearm
S02_WRIST = V(G['chains']['S02'][-1]['pos'])
ik_t = bpy.data.objects.new('IK_S02', None); sc.collection.objects.link(ik_t)
ik_t.location = S02_WRIST
ikc = pbs['S02_seg03'].constraints.new('IK')
ikc.target = ik_t; ikc.chain_count = 3; ikc.use_stretch = False; ikc.iterations = 200
for n in ('S02_shoulder', 'S02_elbow', 'S02_seg03'):
    pbs[n].ik_stretch = 0.0

def apply_pose():
    for pb in pbs:
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = acc.get(pb.name, Q())
    vl.update()

def ease(x):
    x = min(1, max(0, x)); return x * x * (3 - 2 * x)
def seg(t, a, b):
    return ease((t - a) / (b - a))
S = math.sin; PI2 = 2 * math.pi

# ---------------------------------------------------------------- clips  (t in [0,1), all loop-friendly)
def clip_idle(t):
    ph = PI2 * t
    lift('Chest', 0); rotw('Chest', X, 1.0 * S(ph)); rotw('Spine', X, 0.8 * S(ph))
    for c in ('C01_seg00', 'C02_seg00', 'A03_seg00'):
        rotw(c, X, -1.8 * S(ph))
    rotw('Neck', Z, 5 * S(ph)); rotw('Neck', X, 2.5 * S(2 * ph))
    rotw('hat_00', X, 4 * S(ph - 0.9)); rotw('hat_01', X, 7 * S(ph - 1.4)); yaw('hat_01', 6 * S(ph - 1.2))
    rotw('beard_00', X, 3 * S(ph - 0.7)); yaw('beard_00', 4 * S(ph - 1.0))
    for i, a in enumerate(('A01', 'A02', 'A03', 'A04')):
        ch = chain(a); k = i * 1.3
        lift(ch[1], 5 * S(ph + k)); lift(ch[2], 7 * S(ph + k + 0.7))
        curl(a, 12 + 12 * S(ph + k + 1.2))
    for l, k in (('L03', 0), ('L04', 2.0)):
        ch = chain(l)
        lift(ch[-2], 6 * S(ph + k)); lift(ch[-1], 9 * S(ph + k + 0.6))
        for n in chain(l + '_toe') + chain(l + '_foot')[:1]:
            rotw(n, CURL[l + '_toe'][0], 12 * S(ph + k + 1.2))
    rotw('L01_knee', X, 5 * S(ph)); rotw('L02_seg03', X, 4 * S(ph + 1.5))
    for h in ('S01', 'S02', 'S03'):
        curl(h, 4 * S(ph + 0.5))

def clip_slam(t):
    up = seg(t, 0.05, 0.42) * (1 - seg(t, 0.58, 0.68))
    fist = seg(t, 0.12, 0.40) * (1 - seg(t, 0.62, 0.70))
    ik_t.location = S02_WRIST + V((0.0, -0.9, 2.6)) * up
    lift('S02_wrist_00', 22 * up)
    curl('S02', 55 * fist - 6 * seg(t, 0.66, 0.70) * (1 - seg(t, 0.80, 0.95)))
    # anticipation / impact on the rest of the body
    hit = seg(t, 0.66, 0.70) * (1 - seg(t, 0.70, 0.95))
    rotw('Chest', X, 4 * up - 3 * hit); rotw('Neck', X, -6 * up + 5 * hit)
    rotw('hat_01', X, -6 * up + 12 * hit); rotw('beard_00', X, 4 * hit)
    for a in ('A01', 'A02', 'A03', 'A04'):
        ch = chain(a)
        lift(ch[1], 7 * up - 6 * hit); curl(a, 30 * up + 10 * hit)
    for l in ('L03', 'L04'):
        lift(chain(l)[-1], 10 * hit)

def clip_arms(t):
    ph = PI2 * t
    # A04 + A01: big waves, opposite phase; A02/A03: grab-and-release
    ch = chain('A04')
    lift(ch[1], 22 * S(ph)); yaw(ch[1], 12 * S(ph + 0.6)); lift(ch[2], 25 * S(ph + 0.8))
    twist(ch[-1], 25 * S(2 * ph)); curl('A04', 25 + 25 * S(2 * ph + 1))
    ch = chain('A01')
    lift(ch[1], 14 * S(ph + math.pi)); yaw(ch[2], 16 * S(ph + math.pi + 0.5)); lift(ch[3], 18 * S(ph + math.pi + 0.9))
    curl('A01', 30 + 30 * S(2 * ph + 2))
    ch = chain('A02')
    lift(ch[1], 10 * S(ph + 1)); lift(ch[2], 30 * S(ph + 1.6)); curl('A02', 35 + 35 * S(ph + 2.4))
    ch = chain('A03')
    lift(ch[1], -8 * S(ph + 2)); yaw(ch[1], 14 * S(ph + 2)); lift(ch[2], 22 * S(ph + 2.6)); curl('A03', 30 + 30 * S(ph + 3.2))
    rotw('Neck', Z, 6 * S(ph)); rotw('hat_01', X, 5 * S(ph - 1))

def clip_head(t):
    ph = PI2 * t
    turn = 20 * S(ph); nod = 9 * S(2 * ph + 0.4)
    rotw('Neck', Z, turn * 0.6); rotw('Neck', X, nod * 0.5)
    rotw('head_00', Z, turn * 0.4); rotw('head_00', X, nod * 0.5)
    # hat and beard follow with lag and overshoot (secondary motion)
    yaw('hat_00', 10 * S(ph - 0.9)); rotw('hat_00', X, 6 * S(2 * ph - 0.6))
    yaw('hat_01', 18 * S(ph - 1.5)); rotw('hat_01', X, 12 * S(2 * ph - 1.1))
    yaw('beard_00', 12 * S(ph - 1.0)); rotw('beard_00', X, 8 * S(2 * ph - 0.9))
    rotw('Chest', Z, 3 * S(ph - 0.3))
    for a in ('A01', 'A04'):
        curl(a, 15 + 10 * S(ph))

def clip_legs(t):
    ph = PI2 * t
    for l, k in (('L03', 0.0), ('L04', math.pi)):
        ch = chain(l)
        lift(ch[-3], 16 * S(ph + k)); lift(ch[-2], 28 * S(ph + k + 0.6)); lift(ch[-1], 24 * S(ph + k + 1.1))
        for n in chain(l + '_toe') + chain(l + '_foot'):
            rotw(n, CURL[l + '_toe'][0], 26 * S(ph + k + 1.6))
    rotw('L01_knee', X, 22 * S(ph)); rotw('L01_seg03', X, 14 * S(ph + 0.6))
    for n in chain('L01_foot'):
        rotw(n, X, 14 * S(ph + 1.2))
    rotw('L02_seg03', X, 20 * S(ph + 1.5)); rotw('L02_knee', Y, 8 * S(ph + 1.0))
    for n in chain('L02_foot'):
        rotw(n, X, 14 * S(ph + 2.2))
    curl('C01', 10 + 8 * S(ph)); curl('C02', 10 + 8 * S(ph + 1))

CLIPS = [
    ('idle', clip_idle, 40, 'q3', 'Idle: дыхание, руки, шляпа с запаздыванием'),
    ('slam', clip_slam, 40, 'slam', 'Опорная рука S02 на IK: подъём, кулак, удар'),
    ('arms', clip_arms, 40, 'q3r', 'Верхние руки A01–A04: взмахи и хваты пальцами'),
    ('head', clip_head, 36, 'head', 'Голова: поворот и кивок, шляпа и борода догоняют'),
    ('legs', clip_legs, 40, 'q3', 'Ноги L01–L04: колени, стопы, пальцы; C-руки сжимают'),
]

def render_frame(path_a, path_b):
    for o in model_objs:
        o.hide_render = False
    bviz.hide_render = True
    sun.hide_render = False
    if MODE == 'final':
        sc.render.engine = 'CYCLES'
        sc.render.film_transparent = False
    else:
        sc.render.engine = 'BLENDER_WORKBENCH'
        sc.render.film_transparent = False
        sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'TEXTURE'
        sc.display.shading.show_shadows = True; sc.display.shading.show_cavity = False
        sc.display.shading.background_type = 'WORLD'
    sc.render.filepath = path_a
    bpy.ops.render.render(write_still=True)
    for o in model_objs:
        o.hide_render = True
    bviz.hide_render = False
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.render.film_transparent = True
    sh = sc.display.shading
    sh.light = 'STUDIO'; sh.color_type = 'MATERIAL'; sh.show_shadows = False
    sh.show_cavity = False; sh.show_object_outline = True; sh.object_outline_color = (0.08, 0.06, 0.15)
    sc.render.filepath = path_b
    bpy.ops.render.render(write_still=True)

meta = []
for name, fn, n, cam, title in CLIPS:
    if ONLY and name not in ONLY:
        continue
    set_cam(cam)
    ikc.influence = 1.0 if name == 'slam' else 0.0
    FR = os.environ.get('FRAMES')
    fl = [int(x.split(':')[1]) for x in FR.split(',') if x.split(':')[0] == name] if FR else range(0, n, STEP)
    for f in fl:
        acc.clear(); ik_t.location = S02_WRIST
        fn(f / n)
        apply_pose()
        pa = os.path.join(OUT, f'{name}_{f:03d}_A.png'); pb_ = os.path.join(OUT, f'{name}_{f:03d}_B.png')
        render_frame(pa, pb_)
        print('frame', name, f, flush=True)
    meta.append(dict(name=name, frames=n, title=title))
json.dump(dict(clips=meta, step=STEP, bones=len(bones)), open(os.path.join(OUT, 'meta.json'), 'w'), ensure_ascii=False)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'anim_scene.blend'))
