using System.Collections.Generic;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Разряд молнии» (воздух*, ранг 2/5, 150 монет). Strikes only the fighter hit directly (no area): 3 damage
    /// (upgraded 4). A wet target takes double and passes the strike on (<see cref="LightningChain"/>): to the nearest
    /// wet fighter not hit yet within 4 m in direct sight, up to 3 targets in all, either side (the wet player and allies
    /// too). Wet is not used up. A strike in tar (or within <see cref="igniteReach"/> of it) sets the puddle alight, and so
    /// does a strike on any fighter standing in tar or touching it (<see cref="LightningTar"/>, owner 09.11); the
    /// strike's damage still goes to the struck fighters only.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// </summary>
    public sealed class LightningEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (I / II / III)")]
        [Tooltip("Direct damage units (board*: 3 → 4).")]
        public LevelInt damage = new LevelInt(3, 5, 8);
        [Tooltip("Damage multiplier for a wet target (confirmed ×2).")]
        [Min(0)] public float wetDamageMultiplier = 2;
        [Tooltip("Targets in the chain including the first (board*).")]
        [Min(0)] public int chainTargets = 3;
        [Tooltip("Longest jump between wet targets, m (board*).")]
        [Min(0)] public float chainRange = 4;
        [Tooltip("Tar catches fire when the strike lands this close to it, m.")]
        [Min(0)] public float igniteReach = .4f;
        [Tooltip("A struck fighter whose feet are in a tar puddle or this close to it sets the puddle alight, m.")]
        [Min(0)] public float tarTouchMargin = .3f;

        [Header("Hit")]
        [Tooltip("Small shove along each jump (impulse units; the player gets it as a normalized impulse).")]
        [Min(0)] public float knockback = 1.2f;

        [Header("Look")]
        [Tooltip("Bolt from the sky with a short flash (Retro Arsenal), on the first target or the impact point.")]
        public VfxBurst strikeVfx;
        [Min(0)] public float strikeScale = 1;
        [Tooltip("Crooked arc along each jump of the chain.")]
        public LightningArc arcPrefab;
        [Tooltip("Sparks on every fighter the chain reaches.")]
        public VfxBurst sparkVfx;
        public Color tint = new Color(.75f, .9f, 1f);
        public SfxCue jumpSound;

        static readonly List<ICombatant> Fighters = new List<ICombatant>();
        static readonly RaycastHit[] SightHits = new RaycastHit[16];

        public override float BodyNudge => 2;
        // The bolt makes things jump and twitch.
        public override float BodyLift => 1.6f;
        public override float BodySpin => 16;

        static readonly Collider[] Near = new Collider[48];
        static readonly List<Rigidbody> Zapped = new List<Rigidbody>();

        /// <summary>
        /// The crack jumps on to up to <paramref name="count"/> loose things within 6 m of the hit, nearest first: each
        /// sparks and leaps.
        /// </summary>
        void Crackle(ExpeditionWorld world, Vector3 at, int count, float speed, CardBlow blow)
        {
            int n = Physics.OverlapSphereNonAlloc(at, 6, Near, ~(1 << 4), QueryTriggerInteraction.Ignore);
            Zapped.Clear();
            for (int i = 0; i < n; i++)
            {
                var body = Near[i] ? Near[i].attachedRigidbody : null;
                Near[i] = null;
                if (!body || Zapped.Contains(body) || !PropPhysics.IsLoose(body) || !PropPhysics.Movable(body)) continue;
                if ((body.worldCenterOfMass - at).sqrMagnitude < 4) continue;
                Zapped.Add(body);
            }
            Zapped.Sort((a, b) => (a.worldCenterOfMass - at).sqrMagnitude.CompareTo((b.worldCenterOfMass - at).sqrMagnitude));
            for (int i = 0; i < Zapped.Count && i < count; i++)
            {
                var body = Zapped[i];
                if (!body) continue;
                var spot = body.worldCenterOfMass;
                world.Spawn(sparkVfx ? sparkVfx : strikeVfx, spot, Quaternion.identity, tint, strikeScale * .45f);
                PropPhysics.Nudge(body, spot, Vector3.up + Random.insideUnitSphere * .5f, speed, 2.5f, 1.5f, 18, blow);
            }
            Zapped.Clear();
        }

        public override void Apply(in ImpactContext c)
        {
            var world = c.World;
            bool meaningful = world.Ignite(c.Target, c.Point, igniteReach, IgnitionSource.Lightning) > 0;
            var first = c.HitCombatant;
            bool hitFighter = Combat.IsAlive(first);
            world.Spawn(strikeVfx, hitFighter ? first.Position : c.Point, Quaternion.identity, tint, strikeScale);
            // The bolt's crack makes loose things jump; upgraded, it runs on from thing to thing round the hit.
            float boost = PropPhysics.LevelBoost(c.Level);
            PropPhysics.Burst(c.Point, 2 * Mathf.Sqrt(boost), 3 * boost, 1.5f, null, 2.5f, 16, c.Blow);
            if (c.Level >= 2) Crackle(world, c.Point, c.Level >= 3 ? 5 : 2, 3 * boost, c.Blow);
            if (hitFighter)
            {
                Fighters.Clear();
                world.Combatants(Fighters);
                // Who is wet is decided at the moment of the strike, before anyone is hurt.
                var chain = LightningChain.Build(first, Fighters, chainTargets, chainRange, IsWet, f => f.Centre, InSight);
                var wet = new bool[chain.Count];
                for (int i = 0; i < chain.Count; i++) wet[i] = IsWet(chain[i]);
                int baseDamage = c.At(damage);
                for (int i = 0; i < chain.Count; i++)
                {
                    var target = chain[i];
                    Vector3 from = i == 0 ? target.Centre + Vector3.up * 6 : chain[i - 1].Centre;
                    Vector3 push = (target.Centre - from).normalized * knockback + Vector3.up * knockback * .4f;
                    if (i > 0)
                    {
                        LightningArc.Spawn(arcPrefab, from, target.Centre, world.spawnedObjects);
                        world.Spawn(sparkVfx, target.Centre, Quaternion.identity, tint);
                        world.Play(jumpSound, target.Centre, 1, 1 + i * .08f);
                    }
                    // Standing in tar (or touching it): the strike lights the puddle under the fighter's feet.
                    LightningTar.IgniteUnder(world.Zones, target.Position, tarTouchMargin);
                    target.TakeDamage(new DamageInfo(LightningChain.Damage(baseDamage, wet[i], wetDamageMultiplier), push, target.Centre, from,
                        DamageKind.Lightning));
                }
                meaningful = true;
            }
            Feedback(c, meaningful);
        }

        static bool IsWet(ICombatant fighter) => fighter.Root && WetStatus.IsWet(fighter.Root);

        /// <summary>Nothing but fighters stands between the two (rocks, trees and walls block a jump).</summary>
        static bool InSight(ICombatant a, ICombatant b)
        {
            Vector3 from = a.Centre, to = b.Centre, line = to - from;
            float distance = line.magnitude;
            if (distance < .01f) return true;
            int count = Physics.RaycastNonAlloc(from, line / distance, SightHits, distance, ~(1 << 2), QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = SightHits[i].collider;
                if (!hit || Combat.Of(hit) != null || hit.GetComponentInParent<CardProjectile>()) continue;
                if (a.Root && hit.transform.IsChildOf(a.Root) || b.Root && hit.transform.IsChildOf(b.Root)) continue;
                return false;
            }
            return true;
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            label = "Урон";
            values = damage;
            unit = "";
            return true;
        }
    }
}
