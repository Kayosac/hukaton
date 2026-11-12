using System.Collections.Generic;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    public sealed class FreezeEffect : CardEffect, ICardArea
    {
        [Tooltip("A dry creature freezes for the card's power in seconds. A WET creature is encased in ice instead: frozen until fire melts it or a physical hit shatters it.")]
        [Min(1)] public float wetMultiplier = 2;
        [Tooltip("The ice that forms on water: spawned at the first freeze; a freeze next to a sheet grows that sheet.")]
        public IcePatch icePrefab;
        [Tooltip("Radius of ice on water by card level I / II / III, as a multiple of the card's effect radius.")]
        public Vector3 waterRadiusByLevel = new Vector3(1f, 1.3f, 1.65f);

        /// <summary>Freeze duration: the card's power in seconds, multiplied for a wet target (checked at the moment of the hit).</summary>
        public static float FreezeSeconds(float power, bool wet, float wetMultiplier) => wet ? power * wetMultiplier : power;

        public override void Apply(in ImpactContext c)
        {
            bool meaningful = false;
            if (c.Creature && c.Creature.Alive) meaningful = FreezeBody(c, c.Creature, c.Creature);
            else if (c.TargetAs<ICardCreature>() is ICardCreature other && other.Alive)
                meaningful = FreezeBody(c, other, other.Root);

            // Water: ice forms in a radius around the hit, whatever lakes and chunks it spans.
            float radius = c.Radius * WaterRadiusScale(c.Level);
            if (IcePatch.FreezeAt(c.World, icePrefab, c.Point, radius)) meaningful = true;
            Feedback(c, meaningful);
        }

        bool FreezeBody(in ImpactContext c, IFreezable creature, Component root)
        {
            bool wet = root && WetStatus.IsWet(root);
            if (wet && IceEncasement.TryEncase(c.World, root)) return true;
            if (IceEncasement.IsEncased(root)) return true;
            creature.Freeze(FreezeSeconds(c.Power, wet, wetMultiplier));
            return true;
        }

        float WaterRadiusScale(int level) => level <= 1 ? waterRadiusByLevel.x : level == 2 ? waterRadiusByLevel.y : waterRadiusByLevel.z;

        // An area only on water: elsewhere Ice freezes the one creature it hits.
        bool ICardArea.TryGetArea(CardDefinition definition, int level, Collider target, out float radius)
        {
            radius = definition.effectRadius * WaterRadiusScale(level);
            var obstacle = target ? target.GetComponentInParent<WorldObstacle>() : null;
            return obstacle && obstacle.kind == ObstacleKind.Lake;
        }

        bool ICardArea.LeavesOutlinedZone => false;
    }
}
