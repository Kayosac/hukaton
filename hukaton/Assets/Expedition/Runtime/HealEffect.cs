using System.Collections.Generic;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    public sealed class HealEffect : CardEffect
    {
        public override void Apply(in ImpactContext c)
        {
            bool meaningful = c.PlayerWithin(c.Radius) && c.World.vitals.Heal(c.Power);
            Feedback(c, meaningful);
        }
    }
}
