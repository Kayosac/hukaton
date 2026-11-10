using System.Linq;
using NUnit.Framework;

namespace Hakaton.Decks.Tests
{
    /// <summary>Board, «Туториал», 01–03: the cave's hand — instruction cards once, spells that always come back.</summary>
    public sealed class PrologueTests
    {
        static readonly CardSpec Fire = new CardSpec("fire", "Огненный плевок", 100);
        static readonly CardSpec Note = new CardSpec("note_throw", "Бросок", 0, mergeable: false);

        static Expedition Cave(out DeckWorkshop workshop)
        {
            workshop = new DeckWorkshop(new DeckRules(startingCoins: 0), new[] { Fire }, 1);
            Assert.That(workshop.DepartPrologue(), Is.Null);
            return workshop.Current;
        }

        [Test]
        public void PrologueStartsEmptyAndNeverOffersReturn()
        {
            var cave = Cave(out var workshop);
            Assert.That(cave.Prologue, Is.True);
            Assert.That(workshop.IsAway, Is.True);
            Assert.That(cave.Deck.Hand, Is.Empty);
            var note = cave.GrantToHand(Note);
            cave.Release(note.Id);
            Assert.That(cave.Deck.Hand, Is.Empty, "an empty hand in the cave is not «Пора домой»");
            Assert.That(cave.Deck.HasReturn, Is.False);
        }

        [Test]
        public void GoblinHandsOverOnlyTheReturnCard()
        {
            var cave = Cave(out var workshop);
            cave.GrantToHand(Fire);
            cave.GrantToHand(Fire);
            var card = cave.GrantReturn();
            Assert.That(card.IsReturn, Is.True);
            Assert.That(cave.Deck.Hand, Is.EqualTo(new[] { card }), "the spells are gone, «Пора домой» is all that is left");
            Assert.That(cave.ReturnHome(), Is.True);
            Assert.That(workshop.IsAway, Is.False);
        }

        [Test]
        public void SpellComesBackAfterEveryThrow()
        {
            var cave = Cave(out _);
            var fire = cave.GrantToHand(Fire);
            for (int i = 0; i < 5; i++)
            {
                Assert.That(cave.Release(fire.Id), Is.Not.Null);
                Assert.That(cave.Deck.Held, Does.Contain(fire));
                Assert.That(cave.Settle(fire.Id), Is.True);
                Assert.That(cave.Deck.Hand, Does.Contain(fire));
            }
        }

        [Test]
        public void SpellReturnsToItsOwnPlace()
        {
            var cave = Cave(out _);
            var a = cave.GrantToHand(Fire);
            var b = cave.GrantToHand(Fire);
            var c = cave.GrantToHand(Fire);
            cave.Release(a.Id);
            cave.Settle(a.Id);
            Assert.That(cave.Deck.Hand.Select(x => x.Id), Is.EqualTo(new[] { a.Id, b.Id, c.Id }));
        }

        [Test]
        public void InstructionIsUsedOnce()
        {
            var cave = Cave(out _);
            var note = cave.GrantToHand(Note);
            cave.Release(note.Id);
            Assert.That(cave.Deck.Held, Is.Empty);
            Assert.That(cave.Settle(note.Id), Is.False);
            Assert.That(cave.Deck.Hand, Is.Empty);
        }

        [Test]
        public void DiscardTakesInstructionsAndKeepsSpells()
        {
            var cave = Cave(out _);
            var fire = cave.GrantToHand(Fire);
            var note = cave.GrantToHand(Note);
            var gone = cave.DiscardHand();
            Assert.That(gone.Select(c => c.Id), Is.EquivalentTo(new[] { note.Id }));
            Assert.That(cave.Deck.Hand.Select(c => c.Id), Is.EquivalentTo(new[] { fire.Id }));
        }

        [Test]
        public void EndingThePrologueOwnsNothingAndCountsNoOuting()
        {
            var cave = Cave(out var workshop);
            int owned = workshop.Owned.Count, journeys = workshop.Journeys;
            cave.GrantToHand(Fire);
            cave.Defeat();
            Assert.That(workshop.IsAway, Is.False);
            Assert.That(workshop.Owned.Count, Is.EqualTo(owned));
            Assert.That(workshop.Journeys, Is.EqualTo(journeys));
            Assert.That(workshop.Depart(1), Is.Null, "a normal outing is possible again");
            Assert.That(workshop.Current.Prologue, Is.False);
        }
    }
}
