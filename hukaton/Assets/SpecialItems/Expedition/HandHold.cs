using System;
using Hakaton.Expeditions;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.SpecialItems
{
    /// <summary>
    /// How a special item sits in the hand, bottom left (the phone, the player): its way in out of the bottom-left
    /// corner, turning clockwise; the lag of the view's turns and of jumps and falls (a spring, so it overshoots and
    /// settles); a landing's knock and the step, which quickens and grows into a run. Whole reference pixels; the turn
    /// is free. The item's tool says how far along the way in it is and adds its own hops and shakes.
    /// </summary>
    [Serializable]
    public sealed class HandHold
    {
        [Tooltip("Where the hand rests (bottom-left anchored, reference pixels).")]
        public Vector2 shownPosition = new Vector2(253, -170);
        [Tooltip("Where the hand comes from, relative to its place: out of the bottom-left corner…")]
        public Vector2 enterOffset = new Vector2(-380, -560);
        [Tooltip("…turned this far anticipating the clockwise turn it makes on the way in, degrees.")]
        public float enterAngle = 42;

        [Header("Physics in the hand")]
        [Tooltip("Turning the view leaves the item behind: reference pixels per degree per second, and the limit.")]
        public float lookLag = .3f;
        public float maxLookLag = 70;
        [Tooltip("It also tilts against a turn: degrees per degree per second, and the limit.")]
        public float lookTilt = .025f;
        public float maxLookTilt = 7;
        [Tooltip("Up and down: rising leaves it low, falling lifts it (reference pixels per m/s, and the limit).")]
        public float fallLag = 7;
        public float maxFallLag = 70;
        [Tooltip("A landing knocks it down: reference pixels per second per m/s of the landing.")]
        public float landKick = 45;
        [Min(.1f)] public float lagFrequency = 4.2f;
        [Range(0, 2)] public float lagDamping = .42f;
        [Tooltip("The step: side sway and dip (reference pixels) walking and running.")]
        public Vector2 walkBob = new Vector2(7, 10);
        public Vector2 runBob = new Vector2(13, 18);
        [Tooltip("Steps per second walking and running (one sway left-right is two steps).")]
        public float walkStepRate = 3.4f, runStepRate = 5.2f;
        [Tooltip("The step's roll, degrees, walking and running.")]
        public float walkBobTilt = 1.6f, runBobTilt = 3.8f;

        /// <summary>A small shake the tool sets and clears itself (a refusal, a press).</summary>
        [NonSerialized] public Vector2 shake;

        SpringVector3 lag, hop;
        SpringFloat stepAmount, running;
        float stepPhase, lastYaw, lastPitch;
        bool lookKnown;
        ExpeditionWorld world;
        Action<float> landed;

        /// <summary>The item is being taken out: everything at rest, landings start to knock it.</summary>
        public void Begin(ExpeditionWorld outing)
        {
            End();
            lag = default;
            hop = default;
            stepAmount = default;
            running = default;
            shake = Vector2.zero;
            lookKnown = false;
            world = outing;
            landed ??= speed => lag.Kick(new Vector3(0, -speed * landKick, 0));
            if (world && world.motor) world.motor.Landed += landed;
        }

        /// <summary>Put away: landings no longer reach it.</summary>
        public void End()
        {
            if (world && world.motor && landed != null) world.motor.Landed -= landed;
            world = null;
        }

        /// <summary>A hop up (reference pixels per second), e.g. the phone's lid snapping open.</summary>
        public void Hop(float speed) => hop.Kick(new Vector3(0, speed, 0));

        /// <summary>
        /// Poses <paramref name="hand"/> (its pivot at the wrist) <paramref name="slide"/> of the way in (0 away,
        /// 1 in place); <paramref name="dt"/> 0 only places it, nothing moves on.
        /// </summary>
        public void Apply(RectTransform hand, float slide, float dt)
        {
            if (!hand) return;
            var target = Vector3.zero;
            if (dt > 0 && world && world.viewCamera && world.motor)
            {
                var euler = world.viewCamera.transform.eulerAngles;
                if (lookKnown)
                {
                    float yawRate = Mathf.DeltaAngle(lastYaw, euler.y) / dt, pitchRate = Mathf.DeltaAngle(lastPitch, euler.x) / dt;
                    target.x = Mathf.Clamp(-yawRate * lookLag, -maxLookLag, maxLookLag);
                    target.y = Mathf.Clamp(pitchRate * lookLag, -maxLookLag, maxLookLag);
                    target.z = Mathf.Clamp(yawRate * lookTilt, -maxLookTilt, maxLookTilt);
                }
                lastYaw = euler.y;
                lastPitch = euler.x;
                lookKnown = true;
                float vertical = world.motor.Velocity.y;
                target.y += Mathf.Clamp(-vertical * fallLag, -maxFallLag, maxFallLag);
                // The step: only on the ground, quicker and bigger at a run.
                bool walking = world.motor.IsGrounded && world.motor.Speed01 > .05f;
                stepAmount.Step(walking ? Mathf.Clamp01(world.motor.Speed01 * 1.5f) : 0, 6, .9f, dt);
                running.Step(world.motor.IsSprinting ? 1 : 0, 5, .9f, dt);
                float rate = Mathf.Lerp(walkStepRate, runStepRate, running.Value);
                if (walking) stepPhase += dt * rate * Mathf.PI;
            }
            if (dt > 0)
            {
                lag.Step(target, lagFrequency, lagDamping, dt);
                hop.Step(Vector3.zero, 9, .35f, dt);
            }
            var amplitude = Vector2.Lerp(walkBob, runBob, running.Value) * stepAmount.Value;
            float tiltAmount = Mathf.Lerp(walkBobTilt, runBobTilt, running.Value) * stepAmount.Value;
            // A figure of eight: one sway per two steps, a dip on every step.
            var step = new Vector2(Mathf.Sin(stepPhase) * amplitude.x, -Mathf.Abs(Mathf.Cos(stepPhase)) * amplitude.y + amplitude.y * .5f);
            float roll = Mathf.Sin(stepPhase) * tiltAmount;

            float k = EaseOutBack(Mathf.Clamp01(slide));
            var way = Vector2.LerpUnclamped(enterOffset, Vector2.zero, k);
            float wayAngle = Mathf.LerpUnclamped(enterAngle, 0, k);
            var position = shownPosition + way + new Vector2(lag.Value.x, lag.Value.y) + new Vector2(0, hop.Value.y) + step + shake;
            hand.anchoredPosition = new Vector2(Mathf.Round(position.x), Mathf.Round(position.y));
            hand.localRotation = Quaternion.Euler(0, 0, wayAngle + lag.Value.z + roll);
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.2f, c3 = c1 + 1;
            float u = t - 1;
            return 1 + c3 * u * u * u + c1 * u * u;
        }
    }
}
