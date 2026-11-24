using System;
using System.Collections.Generic;
using System.Linq;
using Hakaton.Decks;
using Hakaton.Expeditions;
using NUnit.Framework;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>
    /// A defeat keeps ceil(half) of the whole bag: single copies drawn at random from the outing's seed, none twice.
    /// </summary>
    public sealed class DefeatLootTests
    {
        const float Loss = .5f;

        static IEnumerable<TestCaseData> Bags()
        {
            yield return new TestCaseData(new[] { 1 }, 1).SetName("Одна вещь — домой одна");
            yield return new TestCaseData(new[] { 1, 1, 1 }, 2).SetName("Три разные — домой две");
            yield return new TestCaseData(new[] { 3 }, 2).SetName("Три одинаковые — домой две");
            yield return new TestCaseData(new[] { 2, 1 }, 2).SetName("Две и одна — домой две");
            yield return new TestCaseData(new[] { 1, 1, 1, 1, 1 }, 3).SetName("Пять разных — домой три");
        }

        [TestCaseSource(nameof(Bags))]
        public void KeepsCeilOfHalfTheWholeBag(int[] counts, int expected)
        {
            for (int seed = 0; seed < 50; seed++)
            {
                var kept = DefeatLoot.Keep(counts, Loss, DefeatLoot.RandomFor(seed));
                Assert.That(kept.Sum(), Is.EqualTo(expected), $"seed {seed}");
                for (int i = 0; i < counts.Length; i++)
                    Assert.That(kept[i], Is.InRange(0, counts[i]), $"entry {i} never keeps more copies than it had (seed {seed})");
            }
        }

        [Test]
        public void CountsRoundUpOverTheWholeBag()
        {
            Assert.That(Enumerable.Range(0, 7).Select(n => DefeatLoot.KeptCount(n, Loss)), Is.EqualTo(new[] { 0, 1, 1, 2, 2, 3, 3 }));
            Assert.That(DefeatLoot.KeptCount(10, .3f), Is.EqualTo(7), "No rounding error turns 7 into 8.");
            Assert.That(DefeatLoot.KeptCount(4, 0), Is.EqualTo(4));
            Assert.That(DefeatLoot.KeptCount(4, 1), Is.EqualTo(0));
            Assert.That(DefeatLoot.KeptCoins(7, Loss), Is.EqualTo(3), "Coins still round down.");
            Assert.That(DefeatLoot.KeptCoins(0, Loss), Is.EqualTo(0));
        }

        [Test]
        public void NoCopyIsDrawnTwice()
        {
            // Five copies of one find and nothing else: three come home, never more than five exist.
            for (int seed = 0; seed < 200; seed++)
            {
                var kept = DefeatLoot.Keep(new[] { 1, 1, 1, 1, 1 }, Loss, DefeatLoot.RandomFor(seed));
                Assert.That(kept.All(k => k == 0 || k == 1), Is.True, $"a single copy is kept at most once (seed {seed})");
                Assert.That(kept.Count(k => k == 1), Is.EqualTo(3));
            }
        }

        [Test]
        public void EachCopyHasTheSameChance()
        {
            // [2, 1]: two of three copies come home; each copy stays with chance 2/3, so the pair keeps 4/3 on average.
            int runs = 6000;
            double pair = 0, single = 0;
            for (int seed = 0; seed < runs; seed++)
            {
                var kept = DefeatLoot.Keep(new[] { 2, 1 }, Loss, new Random(seed));
                pair += kept[0];
                single += kept[1];
            }
            Assert.That(pair / runs, Is.EqualTo(4 / 3.0).Within(.05));
            Assert.That(single / runs, Is.EqualTo(2 / 3.0).Within(.05));
        }

        [Test]
        public void TheSameSeedKeepsTheSameThingsAndOtherSeedsDiffer()
        {
            var bag = Bag(("a", 1), ("b", 1), ("c", 1), ("d", 1), ("e", 1));
            var first = Ids(DefeatLoot.Keep(bag, Loss, DefeatLoot.RandomFor(42)));
            Assert.That(Ids(DefeatLoot.Keep(bag, Loss, DefeatLoot.RandomFor(42))), Is.EqualTo(first), "One seed, one result.");
            var seen = new HashSet<string>();
            for (int seed = 0; seed < 20; seed++) seen.Add(Ids(DefeatLoot.Keep(bag, Loss, DefeatLoot.RandomFor(seed))));
            Assert.That(seen.Count, Is.GreaterThan(1), "Different seeds keep different things.");
        }

        [Test]
        public void TheKeptBagKeepsEntriesInOrderAndDropsEmptyOnes()
        {
            var bag = Bag(("a", 2), ("b", 1), ("c", 3));
            var kept = DefeatLoot.Keep(bag, Loss, DefeatLoot.RandomFor(7));
            Assert.That(kept.Sum(x => x.count), Is.EqualTo(3));
            Assert.That(kept.All(x => x.count > 0), Is.True);
            var order = kept.Select(x => x.id).ToList();
            Assert.That(order, Is.EqualTo(order.OrderBy(x => x).ToList()), "Kept entries stay in the bag's order.");
            foreach (var entry in kept)
            {
                var source = bag.Single(x => x.id == entry.id);
                Assert.That(entry.count, Is.LessThanOrEqualTo(source.count));
                Assert.That((entry.name, entry.value), Is.EqualTo((source.name, source.value)));
            }
            Assert.That(DefeatLoot.Keep(new List<LootSave>(), Loss, DefeatLoot.RandomFor(1)), Is.Empty);
        }

        static List<LootSave> Bag(params (string id, int count)[] entries)
            => entries.Select(e => new LootSave { id = e.id, name = e.id.ToUpperInvariant(), count = e.count, value = 10 }).ToList();

        static string Ids(List<LootSave> bag) => string.Join(",", bag.Select(x => x.id + "×" + x.count));
    }
}
