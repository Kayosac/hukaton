using System;
using Hakaton.Character;
using Hakaton.Expeditions.Cards;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>Every creature within the radius hears it and comes to look at the player (<see cref="WorldCreature.Investigate"/>).</summary>
    [CreateAssetMenu(menuName = "Hakaton/Expedition/Oddity effect: lure creatures")]
    public sealed class LureOddityEffect : OddityEffect
    {
        [Min(1)] public float radius = 25;
        [Tooltip("How long they keep coming to look, s.")]
        [Min(1)] public float seconds = 15;

        public int CountInReach(ExpeditionWorld world)
        {
            if (!world || !world.motor) return 0;
            int count = 0;
            Vector3 player = world.motor.transform.position;
            foreach (var creature in world.ActiveCreatures)
                if (creature && creature.isActiveAndEnabled && creature.Alive && Vector3.Distance(creature.transform.position, player) <= radius) count++;
            return count;
        }

        public override bool CanHappen(ExpeditionWorld world) => base.CanHappen(world) && CountInReach(world) > 0;

        protected override bool Happen(ExpeditionWorld world, Oddity source)
        {
            Vector3 player = world.motor.transform.position;
            int coming = 0;
            foreach (var creature in world.ActiveCreatures)
                if (creature && creature.isActiveAndEnabled && creature.Alive && Vector3.Distance(creature.transform.position, player) <= radius
                    && creature.Investigate(player, seconds)) coming++;
            return coming > 0;
        }
    }
}
