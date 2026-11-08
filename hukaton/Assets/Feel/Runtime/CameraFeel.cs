using UnityEngine;

namespace Hakaton.Feel
{
    /// <summary>
    /// Additive camera feedback consumed by the first-person view: trauma shake,
    /// directional recoil kicks and field-of-view punches. Owns no transform itself,
    /// so it never fights the view's own smoothing.
    /// </summary>
    public sealed class CameraFeel : MonoBehaviour
    {
        [Header("Shake")]
        [Tooltip("Maximum positional shake at full trauma, metres.")]
        [Min(0)] public float maxShakeOffset = .06f;
        [Tooltip("Maximum rotational shake at full trauma, degrees (pitch, yaw, roll).")]
        public Vector3 maxShakeAngles = new Vector3(2.4f, 2.4f, 3.5f);
        [Min(.1f)] public float shakeFrequency = 22;
        [Tooltip("Trauma lost per second.")]
        [Min(.1f)] public float traumaDecay = 1.8f;
        [Header("Kick")]
        [Min(.1f)] public float kickFrequency = 6;
        [Range(0, 2)] public float kickDamping = .55f;
        [Header("Field of view")]
        [Min(.1f)] public float fovFrequency = 4.5f;
        [Range(0, 2)] public float fovDamping = .6f;
        [Header("Accessibility")]
        [Range(0, 1)] public float intensity = 1;

        public Vector3 PositionOffset { get; private set; }
        public Quaternion RotationOffset { get; private set; } = Quaternion.identity;
        public float FovOffset => fov.Value * intensity;
        public float Trauma => trauma;

        float trauma, sustainedTrauma, seed;
        SpringVector3 kick;
        SpringFloat fov;

        void Awake() { seed = Random.value * 100; }

        /// <summary>Adds shake trauma (0..1). Shake strength grows with trauma squared.</summary>
        public void AddTrauma(float amount) => trauma = Mathf.Clamp01(trauma + amount);

        /// <summary>Holds trauma at least at this level while called each frame (e.g. a charging boar nearby).</summary>
        public void Sustain(float amount) => sustainedTrauma = Mathf.Max(sustainedTrauma, amount);

        /// <summary>Recoil impulse in degrees per second: x = pitch up (negative), y = yaw, z = roll.</summary>
        public void Kick(Vector3 angularVelocity) => kick.Kick(angularVelocity);

        /// <summary>Instant FOV velocity; positive widens.</summary>
        public void PunchFov(float velocity) => fov.Kick(velocity);

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0) return;
            trauma = Mathf.Max(sustainedTrauma, Mathf.Max(0, trauma - traumaDecay * dt));
            sustainedTrauma = 0;
            float shake = trauma * trauma * intensity;
            float t = Time.unscaledTime * shakeFrequency;
            Vector3 noise = new Vector3(Noise(seed, t), Noise(seed + 17, t), Noise(seed + 41, t));
            Vector3 angles = Vector3.Scale(maxShakeAngles, noise) * shake;
            PositionOffset = new Vector3(Noise(seed + 61, t), Noise(seed + 79, t), 0) * maxShakeOffset * shake;
            kick.Step(Vector3.zero, kickFrequency, kickDamping, dt);
            fov.Step(0, fovFrequency, fovDamping, dt);
            RotationOffset = Quaternion.Euler((angles + kick.Value) * intensity);
        }

        static float Noise(float offset, float time) => Mathf.PerlinNoise(offset, time) * 2 - 1;

        public void ResetFeel()
        {
            trauma = sustainedTrauma = 0;
            kick.Snap(Vector3.zero);
            fov.Snap(0);
            PositionOffset = Vector3.zero;
            RotationOffset = Quaternion.identity;
        }
    }
}
