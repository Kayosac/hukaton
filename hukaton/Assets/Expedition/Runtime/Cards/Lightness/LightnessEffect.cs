using System.Collections.Generic;
using Hakaton.Character;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Лёгкость» (воздух, ранг 3/5). A burst of radius 2.5 m at the point of impact puts Lightness on every body in it
    /// — the player, creatures, objects, loot — for 6 s (upgraded 9 s): received impulses ×1.5, falling speed limited to
    /// 8 m/s. Same refresh rule as every timed modifier (longer of the two, never multiplied). Independent of Moon
    /// gravity: the Moon changes only the player's g, Lightness only impulses and the falling limit, so both simply act
    /// together without multiplying any bonus. The player gets a HUD badge; other bodies a light aura while it lasts.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// </summary>
    public sealed class LightnessEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (I / II / III)")]
        [Tooltip("Duration, s (board: 6 → 9).")]
        public LevelFloat duration = new LevelFloat(6, 12, 20);
        [Tooltip("Burst radius, m.")]
        [Min(0)] public float radius = 2.5f;
        [Tooltip("Received impulses multiplier (same on both levels).")]
        [Min(0)] public float impulseMultiplier = 1.5f;
        [Tooltip("Falling speed limit, design m/s (same on both levels).")]
        [Min(0)] public float fallSpeedLimit = 8;

        [Header("Presentation")]
        [Tooltip("HUD badge while the player is light.")]
        public PlayerStatusBadge badge;
        [Tooltip("Burst over the whole radius (scaled to it).")]
        public VfxBurst burstVfx;
        [Tooltip("Radius the burst prefab is authored for, m.")]
        [Min(.1f)] public float burstAuthoredRadius = 2.5f;
        [Tooltip("Light aura that follows every other body while it is light.")]
        public VfxBurst bodyAura;
        public Color tint = new Color(.7f, .92f, 1f);

        static readonly Dictionary<Rigidbody, VfxBurst> Auras = new Dictionary<Rigidbody, VfxBurst>();
        static readonly List<Rigidbody> DeadAuras = new List<Rigidbody>();

        public float Duration(int level) => duration.At(level);

        /// <summary>Lightness on the player's motor (separate so it can be tested on a bare motor).</summary>
        public void ApplyTo(FirstPersonMotor motor, int level)
        {
            if (!motor) return;
            float seconds = Duration(level);
            motor.ApplyModifier(MotorModifier.ReceivedImpulse, impulseMultiplier, seconds);
            motor.ApplyFallSpeedLimit(fallSpeedLimit, seconds);
        }

        public override void Apply(in ImpactContext c)
        {
            var world = c.World;
            float seconds = Duration(c.Level);
            int affected = 0;
            if (c.PlayerWithin(radius))
            {
                ApplyTo(c.Motor, c.Level);
                PlayerStatusBadge.Show(world, badge);
                affected++;
            }
            var seen = new HashSet<Rigidbody>();
            foreach (var hit in Physics.OverlapSphere(c.Point, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                var body = hit.attachedRigidbody;
                if (!body || body.isKinematic || !seen.Add(body)) continue;
                var creature = body.GetComponent<WorldCreature>();
                if (creature) creature.ApplyLightness(impulseMultiplier, fallSpeedLimit, seconds);
                else
                {
                    var status = BodyModifierStatus.On(body, world);
                    status.ApplyImpulseScale(impulseMultiplier, seconds);
                    status.ApplyFallSpeedLimit(fallSpeedLimit, seconds);
                }
                ShowAura(world, body, seconds);
                affected++;
            }
            world.Spawn(burstVfx, c.Point + c.Normal * .1f, Quaternion.identity, tint, radius / burstAuthoredRadius);
            Feedback(c, affected > 0);
        }

        void ShowAura(ExpeditionWorld world, Rigidbody body, float seconds)
        {
            if (!bodyAura) return;
            DeadAuras.Clear();
            foreach (var pair in Auras) if (!pair.Key || !pair.Value) DeadAuras.Add(pair.Key);
            foreach (var dead in DeadAuras) Auras.Remove(dead);
            if (!Auras.TryGetValue(body, out var aura) || !aura)
            {
                aura = world.Spawn(bodyAura, body.worldCenterOfMass, Quaternion.identity, tint);
                if (!aura) return;
                Auras[body] = aura;
            }
            aura.Follow(body.transform, body.worldCenterOfMass - body.transform.position);
            aura.HoldFor(seconds);
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            label = "Длительность";
            values = duration;
            unit = " с";
            return true;
        }
    }
}
