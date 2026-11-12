using System.Collections.Generic;
using System.Linq;
using Hakaton.Trading.Unity;
using NUnit.Framework;

namespace Hakaton.Trading.Tests
{
    public sealed class StockSelectionTests
    {
        static readonly ItemDefinition Cap = new ItemDefinition("cap", "Шапка", "Трофеи", 50);
        static readonly ItemDefinition Fang = new ItemDefinition("fang", "Клык", "Трофеи", 80);
        static readonly ItemDefinition Herb = new ItemDefinition("herb", "Трава", "Растения", 40);
        static readonly ItemDefinition Horn = new ItemDefinition("horn", "Рог", "Трофеи", 100);

        /// <summary>Stock in exactly this order, one copy at a time (as outings and taking goods back mix it).</summary>
        static TradingSession Stock(params ItemDefinition[] order)
        {
            var session = new TradingSession(new TradeSettings { SlotCount = 8 });
            foreach (var definition in order) session.SeedInventory(definition, 1);
            return session;
        }

        static string Kind(TradingSession session, int index) => session.Inventory[index].Definition.Id;

        /// <summary>Places the item in hand on the next free slot, as one click (or one hold tick) does.</summary>
        static string Place(TradingSession session, ref int selected)
        {
            int slot = session.Slots.ToList().IndexOf(null);
            string placed = Kind(session, selected);
            Assert.That(session.PlaceItem(selected, slot), Is.True);
            selected = StockSelection.AfterPlacing(session.Inventory, selected, placed);
            return placed;
        }

        [Test]
        public void PlacingKeepsTheSameKindInHandWhileCopiesRemain()
        {
            var session = Stock(Cap, Fang, Cap, Herb, Cap, Fang);
            int selected = 0;
            var placed = new List<string>();
            while (session.Inventory.Count > 0) placed.Add(Place(session, ref selected));
            Assert.That(placed, Is.EqualTo(new[] { "cap", "cap", "cap", "fang", "fang", "herb" }));
        }

        [Test]
        public void SameKindIsFoundAcrossTheEndOfTheStock()
        {
            var session = Stock(Fang, Cap, Herb, Cap);
            int selected = 3;
            Assert.That(Place(session, ref selected), Is.EqualTo("cap"));
            Assert.That(selected, Is.EqualTo(1));
            Assert.That(Kind(session, selected), Is.EqualTo("cap"));
        }

        [Test]
        public void ExhaustedKindHandsOverToTheNextKind()
        {
            var session = Stock(Cap, Fang, Herb, Horn);
            int selected = 2;
            Assert.That(Place(session, ref selected), Is.EqualTo("herb"));
            Assert.That(Kind(session, selected), Is.EqualTo("horn"));

            session = Stock(Cap, Fang, Herb);
            selected = 2;
            Place(session, ref selected);
            Assert.That(selected, Is.EqualTo(0), "The last kind wraps to the first.");
        }

        [Test]
        public void ExhaustedKindHandsOverToTheKindTheWheelWouldShowNext()
        {
            // A copy of an earlier kind sits after the placed one: the hand still moves on to the next kind.
            var session = Stock(Fang, Cap, Fang, Herb);
            int expected = StockSelection.StepKind(session.Inventory, 1, 1);
            Assert.That(Kind(session, expected), Is.EqualTo("herb"));
            int selected = 1;
            Place(session, ref selected);
            Assert.That(Kind(session, selected), Is.EqualTo("herb"));
            Assert.That(selected, Is.EqualTo(2));
        }

        [Test]
        public void WheelStepsBetweenKindsInOrderOfFirstAppearance()
        {
            var session = Stock(Cap, Cap, Cap, Fang, Herb, Fang);
            var inventory = session.Inventory;
            Assert.That(StockSelection.StepKind(inventory, 0, 1), Is.EqualTo(3));
            Assert.That(StockSelection.StepKind(inventory, 3, 1), Is.EqualTo(4));
            Assert.That(StockSelection.StepKind(inventory, 4, 1), Is.EqualTo(0));
            Assert.That(StockSelection.StepKind(inventory, 0, -1), Is.EqualTo(4));
            Assert.That(StockSelection.StepKind(inventory, 4, -1), Is.EqualTo(3));
            Assert.That(StockSelection.StepKind(inventory, 3, -1), Is.EqualTo(0));
            // From a later copy, the step is still by kind and lands on the first copy.
            Assert.That(StockSelection.StepKind(inventory, 2, 1), Is.EqualTo(3));
            Assert.That(StockSelection.StepKind(inventory, 5, 1), Is.EqualTo(4));
            Assert.That(StockSelection.StepKind(inventory, 5, -1), Is.EqualTo(0));
        }

        [Test]
        public void SingleKindAndEmptyStockAreSafe()
        {
            var single = Stock(Cap, Cap);
            Assert.That(StockSelection.StepKind(single.Inventory, 1, 1), Is.EqualTo(0));
            Assert.That(StockSelection.StepKind(single.Inventory, 1, -1), Is.EqualTo(0));
            Assert.That(StockSelection.StepKind(single.Inventory, 1, 0), Is.EqualTo(1));

            var empty = Stock();
            Assert.That(StockSelection.StepKind(empty.Inventory, 0, 1), Is.EqualTo(0));
            Assert.That(StockSelection.AfterPlacing(empty.Inventory, 0, "cap"), Is.EqualTo(0));

            var last = Stock(Cap);
            int selected = 0;
            Place(last, ref selected);
            Assert.That(selected, Is.EqualTo(0));
            Assert.That(last.Inventory, Is.Empty);
        }
    }
}
