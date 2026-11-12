using System;
using UnityEngine;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// The god's hat and beard on springs, after the Animator. Each spring bone's tail is a point mass pulled toward
    /// where the clip puts it, damped, held at the bone's length from its head and nudged by a slow wind; the bone then
    /// turns from its animated direction toward that point. Bones are listed parents first: a child is read after its
    /// parent has swung, so it follows the swing. Stiffness comes from a natural period, so the feel does not depend on
    /// the god's scale. Same model as the Blender preview (output/mountain-god-anim-v1/springs.py).
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(500)]
    public sealed class GodSpringBones : MonoBehaviour
    {
        [Serializable]
        public sealed class Spring
        {
            public Transform bone;
            [Tooltip("Natural swing period, seconds: bigger is floppier.")]
            [Min(.1f)] public float period = 2f;
            [Tooltip("0 swings for ever, 1 stops without overshoot.")]
            [Range(0, 1)] public float damping = .25f;
            [Tooltip("Wind push, in bone lengths per second squared.")]
            [Min(0)] public float wind = .12f;
            [Tooltip("The bone's tail in its own space (the rig's bones point along +Y).")]
            public Vector3 tailLocal = Vector3.up;

            [NonSerialized] public Vector3 position, velocity;
            [NonSerialized] public bool started;
            [NonSerialized] public Quaternion written, animated;
        }

        public Spring[] springs = Array.Empty<Spring>();
        [Range(1, 8)] public int substeps = 4;
        [Tooltip("A frame longer than this restarts the springs (loading, teleport).")]
        [Min(.02f)] public float maxStep = .25f;

        float time;

        void OnEnable()
        {
            foreach (var s in springs)
                if (s != null) s.started = false;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            bool restart = dt > maxStep;
            time += dt;
            foreach (var s in springs)
            {
                if (s?.bone == null) continue;
                // A bone the Animator does not drive keeps what was written last frame: give it its animated pose back.
                if (s.started && s.bone.localRotation == s.written) s.bone.localRotation = s.animated;
                s.animated = s.bone.localRotation;

                Vector3 head = s.bone.position;
                Vector3 tail = s.bone.TransformPoint(s.tailLocal);
                Vector3 want = tail - head;
                float length = want.magnitude;
                if (length < 1e-5f) continue;
                Vector3 dir = want / length;
                if (!s.started || restart)
                {
                    s.position = tail;
                    s.velocity = Vector3.zero;
                    s.started = true;
                }
                float w = 2 * Mathf.PI / s.period;
                float k = w * w, c = 2 * s.damping * w;
                float h = Mathf.Min(dt, maxStep) / substeps;
                for (int i = 0; i < substeps; i++)
                {
                    float t = time - (substeps - 1 - i) * h;
                    var gust = new Vector3(Mathf.PerlinNoise(t * .23f, 1.7f) - .5f, .35f * (Mathf.PerlinNoise(t * .17f, 9.3f) - .5f),
                                           Mathf.PerlinNoise(t * .19f, 5.1f) - .5f) * (2 * s.wind * length);
                    Vector3 a = (tail - s.position) * k - s.velocity * c + gust;
                    s.velocity += a * h;
                    s.position += s.velocity * h;
                    Vector3 d = s.position - head;
                    Vector3 n = d.sqrMagnitude > 1e-10f ? d.normalized : dir;
                    s.position = head + n * length;
                    s.velocity -= n * Vector3.Dot(s.velocity, n);
                }
                s.bone.rotation = Quaternion.FromToRotation(dir, (s.position - head).normalized) * s.bone.rotation;
                s.written = s.bone.localRotation;
            }
        }
    }
}
