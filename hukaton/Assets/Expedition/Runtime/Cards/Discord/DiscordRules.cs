using System.Collections.Generic;
using Hakaton.Expeditions.Events;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// Plain rules of «Раздор» (<see cref="DiscordEffect"/>, <see cref="DiscordBrawl"/>): who joins the free-for-all,
    /// who fights whom, when it is over. No scene objects, so EditMode tests can check them.
    /// </summary>
    public static class DiscordRules
    {
        /// <summary>What the card does to one beast in its radius.</summary>
        public enum Entry
        {
            /// <summary>Joins the free-for-all.</summary>
            Join,
            /// <summary>Left alone: dead, or already fighting for something else.</summary>
            Skip,
            /// <summary>
            /// A beast of a «Звериная разборка» that has not started yet: the card starts that event's own fight
            /// (pack against pack) instead of pulling the beast out of it.
            /// </summary>
            StartEvent,
        }

        /// <summary>
        /// Decision for a beast in the radius. <paramref name="beastBrawl"/> is the phase of the «Звериная разборка» the
        /// beast belongs to, or null for an ordinary beast. Beasts of an event under way stay in it (its referee runs
        /// them, two referees would fight over the same beast); the survivors of an event that is over are ordinary beasts
        /// again and join. A beast already brawling for another «Раздор» stays in that fight.
        /// </summary>
        public static Entry Decide(bool alive, bool brawling, BeastBrawl.Phase? beastBrawl)
        {
            if (!alive) return Entry.Skip;
            if (beastBrawl == BeastBrawl.Phase.NotStarted) return Entry.StartEvent;
            if (beastBrawl == BeastBrawl.Phase.Fighting) return Entry.Skip;
            return brawling ? Entry.Skip : Entry.Join;
        }

        /// <summary>A fight needs at least two beasts; fewer only bristle.</summary>
        public static bool Gathers(int beasts) => beasts >= 2;

        /// <summary>Over when one fighter (or none) is left, or after <paramref name="maximumSeconds"/>.</summary>
        public static bool Over(int fightersLeft, float seconds, float maximumSeconds) => fightersLeft <= 1 || seconds >= maximumSeconds;

        /// <summary>In the card's radius: measured on the ground plane, so a flyer above the spot counts.</summary>
        public static bool Within(Vector3 centre, Vector3 point, float radius)
        {
            Vector3 d = point - centre;
            d.y = 0;
            return d.sqrMagnitude <= radius * radius;
        }

        /// <summary>
        /// Index of the nearest other fighter to <paramref name="self"/> (flat distance) among those
        /// <paramref name="fighting"/>, or -1 when there is none.
        /// </summary>
        public static int Nearest(IReadOnlyList<Vector3> positions, IReadOnlyList<bool> fighting, int self)
        {
            int best = -1;
            float nearest = float.MaxValue;
            for (int i = 0; i < positions.Count; i++)
            {
                if (i == self || !fighting[i]) continue;
                Vector3 d = positions[i] - positions[self];
                d.y = 0;
                float distance = d.sqrMagnitude;
                if (distance < nearest) { nearest = distance; best = i; }
            }
            return best;
        }

        /// <summary>A little loyal to the one it fights: only a rival nearer by more than <paramref name="loyalty"/> takes over.</summary>
        public static bool KeepsRival(float toCurrent, float toNearest, float loyalty) => toNearest + loyalty > toCurrent;
    }
}
