using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Implemented by a card's effect asset whose one throw releases several cards at once («Веер»). The throw takes one
    /// use of the one copy as usual (<see cref="Hakaton.Decks.Expedition.Release"/>, events fire once); then
    /// <see cref="ExpeditionWorld.Launch"/> asks for the velocity of every card of the volley — from the throw already
    /// bent by Crooked hands, so the whole volley is bent — and launches each as an ordinary <see cref="CardProjectile"/>
    /// (its own ballistic flight, contact and impact), all showing the same copy. The copy's attempt settles once, when
    /// the last of them is gone (<see cref="CardVolley"/>). The aiming preview draws every card's path
    /// (<see cref="CardThrower"/>).
    /// </summary>
    public interface ICardVolley
    {
        /// <summary>
        /// Fills <paramref name="velocities"/> with the launch velocity of each card of one throw and returns how many
        /// (1 … the buffer's length). <paramref name="handRolls"/> (may be null) gets how far each card leans, degrees
        /// around the view axis, as the fan opens out of the hand. <paramref name="centre"/> is the card flying exactly
        /// along the throw.
        /// </summary>
        int Volley(in CardFlightLaunch launch, Vector3[] velocities, float[] handRolls, out int centre);

        /// <summary>The volley has left the hand (<paramref name="cards"/> are in flight): its sound and swing.</summary>
        void Launched(ExpeditionWorld world, IReadOnlyList<CardProjectile> cards);
    }

    /// <summary>
    /// The cards of one volley. The attempt of the thrown copy (<see cref="Hakaton.Decks.Expedition.Settle"/>) is over
    /// once, when every card of it is gone: <see cref="Finish"/> is true for the last one only, and never twice for the
    /// same card. Pure counting (EditMode-tested).
    /// </summary>
    public sealed class CardVolley
    {
        readonly List<CardProjectile> cards = new List<CardProjectile>();
        readonly HashSet<object> finished = new HashSet<object>();

        /// <param name="size">How many cards the volley has.</param>
        public CardVolley(int size) { Size = Mathf.Max(1, size); }

        /// <summary>How many cards the volley has.</summary>
        public int Size { get; }
        /// <summary>The volley's cards in flight order (left to right).</summary>
        public IReadOnlyList<CardProjectile> Cards => cards;
        /// <summary>Every card is gone and the copy's attempt has settled.</summary>
        public bool Settled { get; private set; }

        internal void Add(CardProjectile card)
        {
            if (card && !cards.Contains(card)) cards.Add(card);
        }

        /// <summary>One card of the volley is gone (hit, miss, fizzle). True exactly once: for the last of its cards.</summary>
        public bool Finish(object card)
        {
            if (Settled || card == null || !finished.Add(card) || finished.Count < Size) return false;
            Settled = true;
            return true;
        }
    }
}
