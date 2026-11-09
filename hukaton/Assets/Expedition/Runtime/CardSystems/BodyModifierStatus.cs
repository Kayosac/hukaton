using Hakaton.Character;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Card modifiers on a physical body that is not the player (creature, ally, prop, stone cube, loot) — Lightness:
    /// received impulses ×N and a falling-speed limit. Added on demand; same refresh rule as the player's
    /// (<see cref="TimedModifier"/>). The player's own modifiers live in <see cref="FirstPersonMotor.ApplyModifier"/>.
    /// Impulses go through <see cref="CardPhysics.Push"/> so the multiplier applies.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BodyModifierStatus : MonoBehaviour, ICardStatus
    {
        public Rigidbody body;
        TimedModifier impulseScale, fallLimit;
        bool tracked;

        /// <summary>Received-impulse multiplier now (1 when none).</summary>
        public float ImpulseScale => impulseScale.Current(Time.time, 1);
        /// <summary>Falling-speed limit now in m/s (∞ when none). Bodies fall at g 9.81, so design numbers apply as they are.</summary>
        public float FallSpeedLimit => fallLimit.Current(Time.time, float.PositiveInfinity);

        public static BodyModifierStatus On(Rigidbody body, ExpeditionWorld world)
        {
            if (!body) return null;
            var status = body.GetComponent<BodyModifierStatus>();
            if (!status) status = body.gameObject.AddComponent<BodyModifierStatus>();
            status.body = body;
            if (world && !status.tracked) { status.tracked = true; world.TrackStatus(status); }
            return status;
        }

        /// <summary>The body's current impulse multiplier (1 without a status).</summary>
        public static float ImpulseScaleOf(Rigidbody body)
        {
            var status = body ? body.GetComponent<BodyModifierStatus>() : null;
            return status ? status.ImpulseScale : 1;
        }

        public void ApplyImpulseScale(float multiplier, float seconds) => impulseScale.Apply(multiplier, seconds, Time.time);
        public void ApplyFallSpeedLimit(float speed, float seconds) => fallLimit.Apply(speed, seconds, Time.time);

        public void ClearStatus()
        {
            impulseScale.Clear();
            fallLimit.Clear();
        }

        void FixedUpdate()
        {
            if (!body || body.isKinematic) return;
            float limit = FallSpeedLimit;
            var velocity = body.linearVelocity;
            if (velocity.y < -limit) body.linearVelocity = new Vector3(velocity.x, -limit, velocity.z);
        }
    }

    public static class CardPhysics
    {
        /// <summary>
        /// Pushes a physical body (creature, prop, cube, loot): ForceMode.Impulse, so Δv = I / mass (Rigidbody mass is
        /// the board's μ), times the body's Lightness multiplier. Kinematic and missing bodies are ignored.
        /// </summary>
        public static void Push(Rigidbody body, Vector3 impulse)
        {
            if (!body || body.isKinematic) return;
            body.AddForce(impulse * BodyModifierStatus.ImpulseScaleOf(body), ForceMode.Impulse);
        }
    }
}
