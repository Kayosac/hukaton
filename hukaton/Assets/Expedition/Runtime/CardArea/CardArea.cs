using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A card that acts over an area around where it lands (impulse, heal, rain, lure…). Implemented by its effect
    /// asset. <see cref="CardAreaDisplay"/> asks it for the radius to draw the ring on the ground: while the throw is
    /// charged (at the point where the card will land) and, for cards that leave no outlined zone, as a short flash
    /// where it landed.
    /// </summary>
    public interface ICardArea
    {
        /// <summary>
        /// Radius of the area, m, for a copy of <paramref name="level"/> landing on <paramref name="target"/> (null:
        /// nothing hit yet). False when the card would have no area there (ice away from water).
        /// </summary>
        bool TryGetArea(CardDefinition definition, int level, Collider target, out float radius);

        /// <summary>
        /// The card leaves a zone that outlines itself while it lasts (<see cref="ICardAreaOutline"/>): the landing
        /// is not flashed, the zone's outline takes over.
        /// </summary>
        bool LeavesOutlinedZone { get; }
    }

    /// <summary>
    /// A card zone whose reach is not obvious from its look (rain on the ground, fog, an updraft's foot, the lure's
    /// hearing): <see cref="CardAreaDisplay"/> outlines it on the ground while this returns true.
    /// </summary>
    public interface ICardAreaOutline
    {
        bool TryGetOutline(out Vector3 centre, out float radius);
    }
}
