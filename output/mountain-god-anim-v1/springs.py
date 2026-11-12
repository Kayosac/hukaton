"""Hat and beard on springs (the same model as the Unity GodSpringBones component): every spring bone's tail is a
point mass pulled toward where the animation puts it, damped, held at the bone's length from its head, nudged by a slow
wind. The bone turns from its animated direction toward the simulated tail. A child follows its parent's swing.
Units: god units (100 m); stiffness in 1/s^2 from a natural period; the wind is a fraction of the bone length."""
import math
from mathutils import Vector, Quaternion, Matrix
from mathutils import noise

# bone: (period s, damping ratio, wind as a fraction of length per s^2)
SPRINGS = [("hat_00", 1.7, .32, .10), ("hat_01", 2.3, .2, .22), ("beard_00", 2.5, .26, .12)]
SUB = 4


class Springs:
    def __init__(self, arm):
        self.arm = arm
        self.state = {}
        self.t = 0.0

    def step(self, dt):
        arm = self.arm
        W = arm.matrix_world
        PB = arm.pose.bones
        self.t += dt
        offsets = {}
        for name, period, zeta, wind in SPRINGS:
            pb = PB[name]
            m_anim = W @ pb.matrix  # animated, with the parent's animated pose
            par = pb.parent.name if pb.parent else None
            q_par = offsets.get(par, Quaternion())
            piv = None
            if par in offsets:
                # the parent swung: this bone's animated frame swings with it about the parent's head
                piv = W @ PB[par].head
            head = m_anim.translation if piv is None else piv + q_par @ (m_anim.translation - piv)
            length = pb.length
            want_dir = (q_par @ (m_anim.to_quaternion() @ Vector((0, 1, 0)))).normalized()
            target = head + want_dir * length
            st = self.state.get(name)
            if st is None:
                st = self.state[name] = {"p": target.copy(), "v": Vector()}
            w = 2 * math.pi / period
            k, c = w * w, 2 * zeta * w
            h = dt / SUB
            for _ in range(SUB):
                t = self.t
                wv = Vector((noise.noise(Vector((t * .23, 1.7, 0))), noise.noise(Vector((t * .19, 5.1, 0))),
                             .35 * noise.noise(Vector((t * .17, 9.3, 0))))) * wind * length
                a = (target - st["p"]) * k - st["v"] * c + wv
                st["v"] += a * h
                st["p"] += st["v"] * h
                d = st["p"] - head
                if d.length < 1e-6:
                    d = want_dir.copy()
                n = d.normalized()
                st["p"] = head + n * length
                st["v"] -= n * st["v"].dot(n)
            q = want_dir.rotation_difference((st["p"] - head).normalized())
            offsets[name] = q @ q_par
        # write in hierarchy order: the parent's new pose must be evaluated before a child is converted to local
        want = {}
        for name, *_ in SPRINGS:
            pb = PB[name]
            m_anim = W @ pb.matrix
            par = pb.parent.name if pb.parent else None
            if par in offsets:
                piv = W @ PB[par].head
                head = piv + offsets[par] @ (m_anim.translation - piv)
            else:
                head = m_anim.translation
            rot = (offsets[name] @ m_anim.to_quaternion()).to_matrix().to_4x4()
            want[name] = Matrix.Translation(head) @ rot @ Matrix.Diagonal(m_anim.to_scale()).to_4x4()
        for name, *_ in SPRINGS:
            PB[name].matrix = W.inverted() @ want[name]
            self.update()
        return offsets

    def update(self):
        import bpy
        bpy.context.view_layer.update()
