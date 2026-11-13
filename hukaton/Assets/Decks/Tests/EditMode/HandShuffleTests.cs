using System;
using System.Linq;
using NUnit.Framework;

namespace Hakaton.Decks.Tests
{
    /// <summary>The Shuffle debuff (board «Дебаффы»): the hand's order is mixed up; nothing leaves, enters or is blocked.</summary>
    public sealed class HandShuffleTests
    {
        static readonly CardSpec[] Specs = { new CardSpec("wind", "Ветер", 30), new CardSpec("water", "Вода", 40), new CardSpec("fire", "Огонь", 50) };

        static Expedition Trip()
        {
            var w = new DeckWorkshop(new DeckRules(12, 3, 5, 3, 60, 1000), Specs, 3);
            w.Depart(7);
            return w.Current;
        }

        [Test]
        public void ShuffleChangesTheOrderButKeepsTheSameCards()
        {
            var trip = Trip();
            var random = new Random(3);
            for (int round = 0; round < 20; round++)
            {
                var before = trip.Deck.Hand.Select(c => c.Id).ToArray();
                Assert.That(trip.ShuffleHand(random), Is.True);
                var after = trip.Deck.Hand.Select(c => c.Id).ToArray();
                Assert.That(after, Is.Not.EqualTo(before), "The order always changes.");
                Assert.That(after.OrderBy(i => i), Is.EqualTo(before.OrderBy(i => i)), "The same cards stay in the hand.");
            }
        }

        [Test]
        public void NothingToShuffleWithFewerThanTwoCards()
        {
            var trip = Trip();
            while (trip.Deck.Hand.Count > 1) trip.Release(trip.Deck.Hand[0].Id);
            Assert.That(trip.ShuffleHand(new Random(1)), Is.False);
        }
    }
}
