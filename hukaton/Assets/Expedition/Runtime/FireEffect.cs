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
        [Tooltip("Tar and the branch bridge catch fire when hit directly or when the hit point is this close to them.")]
        [Min(0)] public float igniteReach = .4f;

        public override void Apply(in ImpactContext c)
        {
            bool meaningful = false;
            if (c.Creature) { c.Creature.Damage(c.Power, c.Direction * 1.5f, c.Point, burnVisualSeconds); meaningful = true; }
            else if (c.TargetAs<ICardCreature>() is ICardCreature other && other.Alive
                     && other.TakeDamage(new DamageInfo(c.Power, c.Direction * 1.5f, c.Point, c.ThrowOrigin, DamageKind.Fire)))
                meaningful = true;
            var thicket = c.Obstacle ? c.Obstacle : c.World.ObstacleNear(c.Point, c.Radius, ObstacleKind.Thorns);
            if (thicket && thicket.React(CardAction.Fire, c.Point)) meaningful = true;
            // Ice: an encased creature thaws, ice on water melts around the hit.
            if (IceEncasement.MeltAround(c.World, c.Target, c.Point, c.Radius)) meaningful = true;
            if (c.World.ice && c.World.ice.Melt(c.Point, c.Radius)) meaningful = true;
            // Card-made things that burn (IIgnitable): tar puddle, branch bridge.
            if (c.World.Ignite(c.Target, c.Point, igniteReach, IgnitionSource.Fire) > 0) meaningful = true;
            Feedback(c, meaningful);
        }
    }
}
