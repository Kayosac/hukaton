using System;
using Hakaton.Character;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>Hearts in half steps (board v0.1: 4 hearts, 0.75 s protection after a hit, falls by reference speed).</summary>
    public sealed class PlayerVitals : MonoBehaviour
    {
        public ExpeditionSettings settings;
        public FirstPersonMotor motor;
        public ExpeditionWorld world;

        public int HalfHearts { get; private set; }
        public int MaximumHalfHearts => settings.maximumHalfHearts;
        public int PreviousHalfHearts { get; private set; }
        public float LastChangeTime { get; private set; } = float.NegativeInfinity;
        public bool LastChangeWasDamage { get; private set; }
        public bool Invulnerable => Time.time < invulnerableUntil;
        public bool Critical => HalfHearts > 0 && HalfHearts <= 2;
        /// <summary>Signed change in half-hearts; negative is damage.</summary>
        public event Action<int> Changed;
        float invulnerableUntil;

        void Awake() { Restore(); motor.Landed += OnLanded; }
        void OnDestroy() { if (motor) motor.Landed -= OnLanded; }

        public void Restore()
        {
            HalfHearts = PreviousHalfHearts = settings.maximumHalfHearts;
            invulnerableUntil = 0;
            LastChangeTime = float.NegativeInfinity;
            LastChangeWasDamage = false;
        }

        public bool Heal(int halfHearts)
        {
            int healed = Mathf.Min(settings.maximumHalfHearts, HalfHearts + Mathf.Max(0, halfHearts));
            if (healed == HalfHearts) return false;
            PreviousHalfHearts = HalfHearts;
            HalfHearts = healed;
            LastChangeTime = Time.time;
            LastChangeWasDamage = false;
            if (world)
            {
                world.Play(world.cues ? world.cues.playerHeal : null);
                if (world.feel) world.feel.PunchFov(-12);
            }
            Changed?.Invoke(HalfHearts - PreviousHalfHearts);
            return true;
        }

        /// <summary>One hit. <paramref name="source"/> (world position) aims the camera kick away from the attacker.</summary>
        public bool Hurt(int halfHearts, Vector3 knockback, Vector3? source)
        {
            if (halfHearts <= 0 || !world.Running || HalfHearts == 0 || Invulnerable) return false;
            PreviousHalfHearts = HalfHearts;
            HalfHearts = Mathf.Max(0, HalfHearts - halfHearts);
            invulnerableUntil = Time.time + settings.hurtInvulnerability;
            LastChangeTime = Time.time;
            LastChangeWasDamage = true;
            motor.ApplyNormalizedImpulse(knockback);
            if (world.feel)
            {
                world.feel.AddTrauma(.35f + .12f * halfHearts);
                Vector3 local = source.HasValue
                    ? world.viewCamera.transform.InverseTransformDirection(source.Value - world.viewCamera.transform.position)
                    : Vector3.forward;
                // Snap the view away from the hit: pitch up, roll and yaw against its side.
                world.feel.Kick(new Vector3(-90, -Mathf.Sign(local.x) * 60, Mathf.Sign(local.x) * 80));
            }
            if (world.time) world.time.HitStop(.06f);
            world.Play(world.cues ? world.cues.playerHurt : null);
            Changed?.Invoke(HalfHearts - PreviousHalfHearts);
            if (HalfHearts == 0) world.ReturnToShop(true);
            return true;
        }

        void OnLanded(float speed)
        {
            if (!world || !world.Running) return;
            float reference = motor.ToReferenceSpeed(speed);
            int damage = ExpeditionEffectRules.ImpactDamage(reference, settings.fallDamageSpeed, settings.fallDamageStep);
            if (reference > 7)
            {
                world.Play(world.cues ? world.cues.landHeavy : null, null, Mathf.Clamp01(reference / 14));
                world.Spawn(world.vfx ? world.vfx.landingDust : null, motor.transform.position, Quaternion.identity, new Color(.8f, .72f, .58f), Mathf.Clamp(reference / 9, .6f, 1.4f));
                if (world.feel) world.feel.AddTrauma(Mathf.Clamp01((reference - 7) / 12));
            }
            if (damage > 0) Hurt(damage, Vector3.zero, null);
        }
    }
}
