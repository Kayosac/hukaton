using System;
using Hakaton.Character;
using Hakaton.Expeditions.Cards;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Moon gravity for a few seconds, by the same rule as the card «Лунная гравитация» (<see cref="MoonGravityEffect"/>):
    /// its gravity multiplier on the player's motor, its HUD badge and its burst.
    /// </summary>
    [CreateAssetMenu(menuName = "Hakaton/Expedition/Oddity effect: moon gravity")]
    public sealed class MoonGravityOddityEffect : OddityEffect
    {
        [Tooltip("The card's effect asset (Game/Data/Effects/Moon gravity): multiplier, badge and burst come from it.")]
        public MoonGravityEffect card;
        [Min(.5f)] public float seconds = 10;

        protected override bool Happen(ExpeditionWorld world, Oddity source)
        {
            var motor = world.motor;
            if (!motor || !card) return false;
            motor.ApplyModifier(MotorModifier.Gravity, card.gravityMultiplier, seconds);
            PlayerStatusBadge.Show(world, card.badge);
            world.Spawn(card.playerVfx, motor.transform.position + Vector3.up * .05f, Quaternion.identity, card.playerTint, card.playerVfxScale);
            if (world.feel) world.feel.PunchFov(-14);
            return true;
        }
    }
}
