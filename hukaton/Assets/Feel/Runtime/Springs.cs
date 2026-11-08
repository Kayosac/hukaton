using UnityEngine;

namespace Hakaton.Feel
{
    /// <summary>
    /// Damped harmonic springs for presentation (hands, cards, UI). Frequency is in Hz,
    /// damping is a ratio: 1 = critically damped, below 1 overshoots and wobbles.
    /// Stable for any frame time thanks to sub-stepping.
    /// </summary>
    public struct SpringFloat
    {
        public float Value;
        public float Velocity;

        public SpringFloat(float value) { Value = value; Velocity = 0; }

        public float Step(float target, float frequency, float damping, float deltaTime)
        {
            float omega = 2f * Mathf.PI * Mathf.Max(.01f, frequency);
            int steps = Mathf.Max(1, Mathf.CeilToInt(deltaTime / (1f / 240f)));
            float dt = deltaTime / steps;
            for (int i = 0; i < steps; i++)
            {
                float acceleration = omega * omega * (target - Value) - 2f * damping * omega * Velocity;
                Velocity += acceleration * dt;
                Value += Velocity * dt;
            }
            return Value;
        }

        public void Snap(float value) { Value = value; Velocity = 0; }
        public void Kick(float velocity) { Velocity += velocity; }
    }

    public struct SpringVector3
    {
        public Vector3 Value;
        public Vector3 Velocity;

        public SpringVector3(Vector3 value) { Value = value; Velocity = Vector3.zero; }

        public Vector3 Step(Vector3 target, float frequency, float damping, float deltaTime)
        {
            float omega = 2f * Mathf.PI * Mathf.Max(.01f, frequency);
            int steps = Mathf.Max(1, Mathf.CeilToInt(deltaTime / (1f / 240f)));
            float dt = deltaTime / steps;
            for (int i = 0; i < steps; i++)
            {
                Vector3 acceleration = omega * omega * (target - Value) - 2f * damping * omega * Velocity;
                Velocity += acceleration * dt;
                Value += Velocity * dt;
            }
            return Value;
        }

        public void Snap(Vector3 value) { Value = value; Velocity = Vector3.zero; }
        public void Kick(Vector3 velocity) { Velocity += velocity; }
    }

    public static class Ease
    {
        public static float OutCubic(float t) { t = Mathf.Clamp01(t); return 1 - (1 - t) * (1 - t) * (1 - t); }
        public static float InCubic(float t) { t = Mathf.Clamp01(t); return t * t * t; }
        public static float OutBack(float t, float overshoot = 1.70158f)
        {
            t = Mathf.Clamp01(t) - 1;
            return 1 + t * t * ((overshoot + 1) * t + overshoot);
        }
        public static float InOutSine(float t) => -(Mathf.Cos(Mathf.PI * Mathf.Clamp01(t)) - 1) / 2;
        /// <summary>Frame-rate independent exponential approach: 1 - e^(-rate·dt).</summary>
        public static float Damp(float rate, float deltaTime) => 1 - Mathf.Exp(-rate * deltaTime);
    }
}
