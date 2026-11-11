using System;
using Hakaton.Character;
using Hakaton.Expeditions.Cards;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>A few half hearts back (<see cref="PlayerVitals.Heal"/>).</summary>
    [CreateAssetMenu(menuName = "Hakaton/Expedition/Oddity effect: heal")]
    public sealed class HealOddityEffect : OddityEffect
    {
        [Min(1)] public int halfHearts = 3;

        public override bool CanHappen(ExpeditionWorld world)
            => base.CanHappen(world) && world.vitals && world.vitals.HalfHearts < world.vitals.MaximumHalfHearts;

        protected override bool Happen(ExpeditionWorld world, Oddity source) => world.vitals && world.vitals.Heal(halfHearts);
    }
}
