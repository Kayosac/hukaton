using System.Collections.Generic;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    public sealed class StrikeEffect : CardEffect
    {
        [Tooltip("Small shove along the throw so hits read physically.")]
        [Min(0)] public float nudge = 1.2f;

        public override void Apply(in ImpactContext c)
        {
            bool meaningful = c.Creature && c.Creature.Alive;
            if (meaningful) c.Creature.Damage(c.Power, Flat(c.Direction) * nudge, c.Point, 0);
            Feedback(c, meaningful);
        }

        internal static Vector3 Flat(Vector3 direction)
        {
            direction.y = 0;
            return direction.sqrMagnitude > .001f ? direction.normalized : Vector3.forward;
        }
    }
}
