using System.Collections.Generic;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    public sealed class FreezeEffect : CardEffect
    {
        public override void Apply(in ImpactContext c)
        {
            bool meaningful = false;
            if (c.Creature) { c.Creature.Freeze(c.Power); meaningful = true; }
            var water = c.Obstacle ? c.Obstacle : c.World.ObstacleNear(c.Point, c.Radius, ObstacleKind.Lake);
            if (water && water.React(CardAction.Freeze, c.Point)) meaningful = true;
            Feedback(c, meaningful);
        }
    }
}
