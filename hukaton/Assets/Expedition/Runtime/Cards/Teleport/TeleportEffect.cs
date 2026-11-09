using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Телепорт» (тьма*, ранг 5/5). Moves the player to the point of impact (first contact of the card) into the nearest free capsule position on the
    /// side of the contact, keeping the full world velocity and every running effect. No upgrade (upgradable = false).
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// STUB from the foundation pass: only the impact feedback so far.
    /// </summary>
    public sealed class TeleportEffect : CardEffect
    {
        public override void Apply(in ImpactContext c)
        {
            // TODO(Teleport): find a free capsule pose next to c.Point on the c.Normal side without passing through walls;
            // c.Motor.TeleportKeepingVelocity(position) (it also lets go of a rope).
            Feedback(c, false);
            c.World.Announce(c.Definition.displayName + ": эффект в разработке", 1.5f);
        }
    }
}
