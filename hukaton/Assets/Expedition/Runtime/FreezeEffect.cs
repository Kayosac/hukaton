using System.Collections.Generic;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    public sealed class FreezeEffect : CardEffect
    {
        [Tooltip("Board: a wet creature freezes this many times longer (2 → 4 s, upgraded 3 → 6 s). Not over ×2 from several clouds.")]
        [Min(1)] public float wetMultiplier = 2;

        /// <summary>Freeze duration: the card's power in seconds, multiplied for a wet target (checked at the moment of the hit).</summary>
        public static float FreezeSeconds(float power, bool wet, float wetMultiplier) => wet ? power * wetMultiplier : power;

        public override void Apply(in ImpactContext c)
        {
            bool meaningful = false;
            if (c.Creature) { c.Creature.Freeze(FreezeSeconds(c.Power, WetStatus.IsWet(c.Creature), wetMultiplier)); meaningful = true; }
            var water = c.Obstacle ? c.Obstacle : c.World.ObstacleNear(c.Point, c.Radius, ObstacleKind.Lake);
            if (water && water.React(CardAction.Freeze, c.Point)) meaningful = true;
            Feedback(c, meaningful);
        }
    }
}
