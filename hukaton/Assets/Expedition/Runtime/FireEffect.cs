using System.Collections.Generic;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    public sealed class FireEffect : CardEffect
    {
        [Min(0)] public float burnVisualSeconds = 1.6f;

        public override void Apply(in ImpactContext c)
        {
            bool meaningful = false;
            if (c.Creature) { c.Creature.Damage(c.Power, c.Direction * 1.5f, c.Point, burnVisualSeconds); meaningful = true; }
            var thicket = c.Obstacle ? c.Obstacle : c.World.ObstacleNear(c.Point, c.Radius, ObstacleKind.Thorns);
            if (thicket && thicket.React(CardAction.Fire, c.Point)) meaningful = true;
            Feedback(c, meaningful);
        }
    }
}
