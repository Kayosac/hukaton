using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Implemented by a card's effect asset whose card does not fly the ordinary ballistic way to its first contact
    /// (<see cref="CardFlight"/>): «Бумеранг» goes out and comes back. <see cref="CardProjectile"/> asks it once at the
    /// launch for the flight that then moves the card every physics step; the aiming preview asks it for the same path,
    /// so the preview never lies. The thrower asks it whether the throw may bring the card back to the hand
    /// (<see cref="Expedition.Release(int, bool)"/>). A card that throws several projectiles at once (Веер) would hook
    /// in at <see cref="ExpeditionWorld.Launch"/> instead — the flight of each projectile stays its own.
    /// </summary>
    public interface ICustomFlight
    {
        /// <summary>The throw may bring the card back to the hand by itself (caught on the way back).</summary>
        bool MayComeBack { get; }

        /// <summary>The flight of one thrown card, from its launch. Never null.</summary>
        ICardFlightPath BeginFlight(in CardFlightLaunch launch);

        /// <summary>
        /// The same path for the aiming preview: fills <paramref name="points"/> from the launch point and returns how
        /// many were written (at least 1). Stops where the real card would stop (a solid obstacle, the catch).
        /// </summary>
        int PreviewPath(in CardFlightLaunch launch, Vector3[] points);
    }

    /// <summary>Where and how a card leaves the hand (after Crooked hands bent it): the start of its flight.</summary>
    public readonly struct CardFlightLaunch
    {
        public readonly ExpeditionWorld World;
        public readonly CardCopy Copy;
        public readonly CardDefinition Definition;
        public readonly Vector3 Origin, Velocity;

        public CardFlightLaunch(ExpeditionWorld world, CardCopy copy, CardDefinition definition, Vector3 origin, Vector3 velocity)
        {
            World = world; Copy = copy; Definition = definition; Origin = origin; Velocity = velocity;
        }

        /// <summary>The copy's level: 1, 2 or 3.</summary>
        public int Level => Copy != null ? Copy.Level : 1;
    }

    /// <summary>
    /// One card's own flight: moves the <see cref="CardProjectile"/> every physics step and ends it — on a contact
    /// (<see cref="CardProjectile.Resolve"/>, <see cref="CardProjectile.Bounce"/>), caught (<see cref="CardProjectile.Catch"/>)
    /// or dropped (<see cref="CardProjectile.Drop"/>).
    /// </summary>
    public interface ICardFlightPath
    {
        void Step(CardProjectile card, float dt);
        /// <summary>How much faster than an ordinary card it spins in its plane.</summary>
        float SpinMultiplier { get; }
        /// <summary>The sound looped while it flies instead of the ordinary flutter (null: the flutter).</summary>
        SfxCue FlightLoop { get; }
    }
}
