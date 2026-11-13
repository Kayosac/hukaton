using System;
using Hakaton.Character;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Hearts in half steps (board v0.1: 4 hearts, 0.75 s protection after a hit, falls by reference speed).
    /// The player as a fighter (<see cref="ICombatant"/>): damage units are half hearts.
    /// </summary>
    public sealed class PlayerVitals : MonoBehaviour, ICombatant
    {
        public ExpeditionSettings settings;
        public FirstPersonMotor motor;
        public ExpeditionWorld world;

        public int HalfHearts { get; private set; }
        public int MaximumHalfHearts => settings.maximumHalfHearts;
        public int PreviousHalfHearts { get; private set; }
        public float LastChangeTime { get; private set; } = float.NegativeInfinity;
        public bool LastChangeWasDamage { get; private set; }
        public bool Invulnerable => Immortal || Time.time < invulnerableUntil;
        /// <summary>Cheat (F1 menu): no hit, fall or drowning takes hearts.</summary>
        public bool Immortal { get; set; }
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

        // ---------- the player as a fighter ----------

        public Faction Faction => Faction.Player;
        public bool Alive => HalfHearts > 0;
        public Vector3 Position => motor.transform.position;
        public Vector3 Centre => motor.transform.TransformPoint(motor.characterController.center);
        public Transform Root => motor.transform;

        /// <summary>A hit from a creature, an ally or a card effect: <see cref="DamageInfo.Amount"/> half hearts.</summary>
        public bool TakeDamage(in DamageInfo damage) => Hurt(damage.Amount, damage.Impulse, damage.Source, damage.PiercesGuard);

        /// <summary>
        /// One hit of <paramref name="halfHearts"/> (1 = ½ heart). <paramref name="knockback"/> is a normalized impulse;
        /// <paramref name="source"/> (world position) aims the camera kick away from the attacker. Ignored while
        /// protected after the previous hit (unless <paramref name="piercesGuard"/>: the next blow of the same attack)
        /// and outside an outing.
        /// </summary>
        public bool Hurt(int halfHearts, Vector3 knockback, Vector3? source, bool piercesGuard = false)
        {
            if (halfHearts <= 0 || !world.Running || HalfHearts == 0 || (Invulnerable && !(piercesGuard && !Immortal))) return false;
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

        /// <summary>Instant death (deep water): hearts empty at once, ignoring protection. The caller runs the defeat.</summary>
        public bool Drain()
        {
            if (!world.Running || HalfHearts == 0 || Immortal) return false;
            PreviousHalfHearts = HalfHearts;
            HalfHearts = 0;
            LastChangeTime = Time.time;
            LastChangeWasDamage = true;
            Changed?.Invoke(HalfHearts - PreviousHalfHearts);
            return true;
        }

        /// <summary>
        /// Takes hearts down to <paramref name="keepHalfHearts"/> at once (a curse): never kills, ignores the protection
        /// after a hit, shakes the view. False when the player already has no more than that.
        /// </summary>
        public bool DrainTo(int keepHalfHearts)
        {
            keepHalfHearts = Mathf.Max(1, keepHalfHearts);
            if (!world.Running || Immortal || HalfHearts <= keepHalfHearts) return false;
            PreviousHalfHearts = HalfHearts;
            HalfHearts = keepHalfHearts;
            LastChangeTime = Time.time;
            LastChangeWasDamage = true;
            if (world.feel)
            {
                world.feel.AddTrauma(.6f);
                world.feel.Kick(new Vector3(-120, 0, 90));
                world.feel.PunchFov(14);
            }
            if (world.time) world.time.HitStop(.08f);
            Changed?.Invoke(HalfHearts - PreviousHalfHearts);
            return true;
        }

        void OnLanded(float speed)
        {
            if (!world || !world.Running) return;
            // A card surface (the mushroom cap) may take the landing over: no fall damage, maybe a bounce.
            var surface = motor.GroundCollider ? motor.GroundCollider.GetComponentInParent<ILandingSurface>() : null;
            if (surface != null && surface.AbsorbLanding(motor, speed)) return;
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
