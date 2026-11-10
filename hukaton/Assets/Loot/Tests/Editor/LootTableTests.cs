using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Hakaton.Loot.Tests
{
    /// <summary>Drop table rules with scripted random values.</summary>
    public sealed class LootTableTests
    {
        readonly List<UnityEngine.Object> made = new List<UnityEngine.Object>();

        [TearDown]
        public void Clean()
        {
            foreach (var asset in made) UnityEngine.Object.DestroyImmediate(asset);
            made.Clear();
        }

        LootItem Item(string id)
        {
            var item = ScriptableObject.CreateInstance<LootItem>();
            item.id = id;
            made.Add(item);
            return item;
        }

        static Func<float> Values(params float[] values)
        {
            int next = 0;
            return () => values[Mathf.Min(next++, values.Length - 1)];
        }

        static LootDrop Drop(LootItem item, float chance, int min = 1, int max = 1) =>
            new LootDrop { item = item, chance = chance, min = min, max = max };

        [Test]
        public void EveryRowRollsOnItsOwn()
        {
            LootItem fang = Item("fang"), pelt = Item("pelt");
            var table = new LootTable { atLeastOne = false, entries = new[] { Drop(fang, .5f), Drop(pelt, .5f) } };
            var dropped = new List<LootItem>();
            table.Roll(Values(.2f, .8f), dropped);
            Assert.That(dropped, Is.EqualTo(new[] { fang }), "0.2 < 0.5 drops the fang, 0.8 skips the pelt.");
        }

        [Test]
        public void CertainRowsDropWithoutUsingRandom()
        {
            var fang = Item("fang");
            var table = new LootTable { entries = new[] { Drop(fang, 1) } };
            var dropped = new List<LootItem>();
            table.Roll(() => throw new AssertionException("A certain single copy needs no roll."), dropped);
            Assert.That(dropped, Is.EqualTo(new[] { fang }));
        }

        [Test]
        public void CountIsInclusiveBetweenMinAndMax()
        {
            var seed = Item("seed");
            var table = new LootTable { entries = new[] { Drop(seed, 1, 2, 4) } };
            var low = new List<LootItem>();
            table.Roll(Values(0f), low);
            var high = new List<LootItem>();
            table.Roll(Values(.999f), high);
            var top = new List<LootItem>();
            table.Roll(Values(1f), top);
            Assert.That(low.Count, Is.EqualTo(2));
            Assert.That(high.Count, Is.EqualTo(4));
            Assert.That(top.Count, Is.EqualTo(4), "Random.value can return exactly 1.");
        }

        [Test]
        public void AtLeastOnePicksARowWeightedByChanceWhenNothingDropped()
        {
            LootItem common = Item("common"), rare = Item("rare");
            var table = new LootTable { atLeastOne = true, entries = new[] { Drop(common, .3f), Drop(rare, .1f) } };
            var dropped = new List<LootItem>();
            // Both rows miss (.9, .9), then .8 of the total weight .4 = .32 lands past common (.3) on rare.
            table.Roll(Values(.9f, .9f, .8f), dropped);
            Assert.That(dropped, Is.EqualTo(new[] { rare }));
            dropped.Clear();
            table.Roll(Values(.9f, .9f, .5f), dropped);
            Assert.That(dropped, Is.EqualTo(new[] { common }));
        }

        [Test]
        public void WithoutAtLeastOneAMissIsEmpty()
        {
            var table = new LootTable { atLeastOne = false, entries = new[] { Drop(Item("a"), .3f) } };
            var dropped = new List<LootItem>();
            table.Roll(Values(.9f), dropped);
            Assert.That(dropped, Is.Empty);
        }

        [Test]
        public void EmptyRowsAndZeroChanceAreIgnored()
        {
            var item = Item("a");
            var table = new LootTable { atLeastOne = false, entries = new[] { null, Drop(null, 1), Drop(item, 0) } };
            var dropped = new List<LootItem>();
            table.Roll(Values(0f), dropped);
            Assert.That(dropped, Is.Empty);
            Assert.That(new LootTable().IsEmpty, Is.True);
        }
    }
}
