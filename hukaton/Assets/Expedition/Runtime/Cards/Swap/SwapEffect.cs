using System.Collections.Generic;
using Hakaton.Character;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Подмена» (тьма, ранг 5/5). On a real hit of the card into a loose, movable, non-living object with μ ≤ 2 (a
    /// non-kinematic Rigidbody that is not a creature or an ally; the Stone cube qualifies) within 12 m (upgraded 18 m)
    /// of the player, the player and the object swap places — if the player's capsule fits where the object was. Each
    /// keeps ITS OWN velocity (owner's decision): they exchange positions, not motion. Otherwise a miss.
    /// The object's bottom goes to the player's feet; the player's feet to the object's bottom.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// </summary>
    public sealed class SwapEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (I / II / III)")]
        [Tooltip("Range, m (board: 12 → 18).")]
        public LevelFloat range = new LevelFloat(12, 20, 30);
        [Tooltip("Heaviest object it swaps (μ).")]
        [Min(0)] public float maxMass = 2;

        [Header("Placement")]
        [Tooltip("Furthest the player may be shifted from the object's place to fit, m.")]
        [Min(0)] public float maxShift = .6f;
        [Tooltip("Furthest the object may be shifted from the player's place to fit, m.")]
        [Min(0)] public float objectShift = .8f;

        [Header("Presentation")]
        public VfxBurst swapVfx;
        public SfxCue swapSound;
        public Color tint = new Color(.55f, .3f, .85f);
        [Min(0)] public float fovPunch = 45;

        /// <summary>Why a swap cannot happen.</summary>
        public enum Refusal { None, NoObject, Living, Fixed, TooHeavy, TooFar, NoRoom }

        public float Range(int level) => range.At(level);

        /// <summary>The board's conditions on the hit object (room for the player is checked separately).</summary>
        public Refusal Check(Rigidbody body, Vector3 playerCentre, int level)
        {
            if (!body) return Refusal.NoObject;
            if (body.GetComponentInParent<WorldCreature>() || body.GetComponentInParent<ICombatant>() != null) return Refusal.Living;
            if (body.isKinematic) return Refusal.Fixed;
            if (body.mass > maxMass + .001f) return Refusal.TooHeavy;
            if (Vector3.Distance(playerCentre, body.worldCenterOfMass) > Range(level) + .001f) return Refusal.TooFar;
            return Refusal.None;
        }

        public static string Explain(Refusal refusal)
        {
            switch (refusal)
            {
                case Refusal.NoObject: return "Подмена: нужен подвижный предмет";
                case Refusal.Living: return "Подмена: живое не подменить";
                case Refusal.Fixed: return "Подмена: предмет закреплён";
                case Refusal.TooHeavy: return "Подмена: слишком тяжело";
                case Refusal.TooFar: return "Подмена: слишком далеко";
                case Refusal.NoRoom: return "Подмена: там не поместиться";
                default: return "";
            }
        }

        public override void Apply(in ImpactContext c)
        {
            var motor = c.Motor;
            var body = c.Body;
            var refusal = c.Creature || c.HitCombatant != null ? Refusal.Living : Check(body, c.PlayerCentre, c.Level);
            Vector3 playerFeet = TeleportEffect.Feet(motor);
            Vector3 arrival = default;
            if (refusal == Refusal.None && !TryFindPlayerPlace(motor, body, out arrival)) refusal = Refusal.NoRoom;
            if (refusal != Refusal.None)
            {
                Feedback(c, false);
                c.World.Announce(Explain(refusal), 1.6f);
                return;
            }

            Vector3 objectFrom = body.worldCenterOfMass;
            Vector3 objectTo = ObjectPlace(body, playerFeet, motor);
            // Exchange positions only: the motor keeps the player's velocity, the body keeps its own.
            TeleportEffect.MoveFeetTo(motor, arrival);
            Vector3 shift = objectTo - BottomCentre(Bounds(body));
            Vector3 pose = body.position + shift;
            body.position = pose;
            body.transform.position = pose;
            body.WakeUp();

            var world = c.World;
            world.Spawn(swapVfx, objectFrom, Quaternion.identity, tint);
            world.Spawn(swapVfx, objectFrom + shift, Quaternion.identity, tint);
            world.Play(swapSound, TeleportEffect.CapsuleCentre(motor));
            if (world.feel) { world.feel.PunchFov(fovPunch); world.feel.AddTrauma(.12f); }
            Feedback(c, true);
        }

        bool TryFindPlayerPlace(FirstPersonMotor motor, Rigidbody body, out Vector3 feet)
        {
            var controller = motor.characterController;
            var settings = PlayerCapsulePlacement.Settings.For(controller.radius + controller.skinWidth, controller.height);
            settings.maxShift = maxShift;
            var colliders = new HashSet<Collider>(body.GetComponentsInChildren<Collider>());
            // The object leaves its place: its own colliders do not block the player.
            return PlayerCapsulePlacement.TryFind(BottomCentre(Bounds(body)), Vector3.up, settings, out feet, colliders.Contains);
        }

        /// <summary>Where the object's bottom goes: the player's feet, nudged if it does not fit there.</summary>
        Vector3 ObjectPlace(Rigidbody body, Vector3 playerFeet, FirstPersonMotor motor)
        {
            var bounds = Bounds(body);
            var own = new HashSet<Collider>(body.GetComponentsInChildren<Collider>());
            Vector3 extents = bounds.extents * .96f;
            Vector3 best = playerFeet + Vector3.up * .02f;
            float step = .1f;
            for (float d = 0; d <= objectShift + 1e-4f; d += step)
                foreach (var direction in Directions)
                {
                    Vector3 bottom = best + direction * d;
                    var hits = Physics.OverlapBox(bottom + Vector3.up * bounds.extents.y, extents, Quaternion.identity, ~(1 << 2), QueryTriggerInteraction.Ignore);
                    bool blocked = false;
                    foreach (var hit in hits) if (!own.Contains(hit)) { blocked = true; break; }
                    if (!blocked) return bottom;
                    if (d == 0) break;
                }
            return best;
        }

        static readonly Vector3[] Directions =
        {
            Vector3.up, Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
            (Vector3.up + Vector3.forward).normalized, (Vector3.up + Vector3.back).normalized,
            (Vector3.up + Vector3.left).normalized, (Vector3.up + Vector3.right).normalized
        };

        static Bounds Bounds(Rigidbody body)
        {
            bool any = false;
            var bounds = new Bounds(body.worldCenterOfMass, Vector3.zero);
            foreach (var part in body.GetComponentsInChildren<Collider>())
            {
                if (!part.enabled || part.isTrigger || part.attachedRigidbody != body) continue;
                if (!any) { bounds = part.bounds; any = true; }
                else bounds.Encapsulate(part.bounds);
            }
            return bounds;
        }

        static Vector3 BottomCentre(Bounds bounds) => new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            label = "Дальность";
            values = range;
            unit = " м";
            return true;
        }
    }
}
