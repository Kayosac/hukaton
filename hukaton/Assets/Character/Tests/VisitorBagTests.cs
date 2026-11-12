using System.Collections.Generic;
using System.Linq;
using Hakaton.Character;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Character.Tests
{
    public sealed class VisitorBagTests
    {
        const string RosterPath = "Assets/Content/NPCs/Visitor roster.asset";
        readonly List<NpcAppearance> created = new List<NpcAppearance>();

        NpcAppearance[] Visitors(int count)
        {
            var list = new NpcAppearance[count];
            for (int i = 0; i < count; i++)
            {
                list[i] = ScriptableObject.CreateInstance<NpcAppearance>();
                list[i].name = "V" + i;
                created.Add(list[i]);
            }
            return list;
        }

        [TearDown]
        public void Clean()
        {
            foreach (var visitor in created) if (visitor) Object.DestroyImmediate(visitor);
            created.Clear();
        }

        static List<NpcAppearance> Draw(VisitorBag bag, int count)
        {
            var drawn = new List<NpcAppearance>();
            for (int i = 0; i < count; i++)
            {
                var visitor = bag.Peek();
                bag.Take(visitor);
                drawn.Add(visitor);
            }
            return drawn;
        }

        [Test]
        public void EveryoneComesOncePerRoundAndNeverTwiceInARow()
        {
            var roster = Visitors(9);
            var random = new System.Random(3);
            var bag = new VisitorBag(() => roster, max => random.Next(max));
            var drawn = Draw(bag, 9 * 40);
            for (int round = 0; round < 40; round++)
                Assert.That(drawn.Skip(round * 9).Take(9).Distinct().Count(), Is.EqualTo(9), "round " + round);
            for (int i = 1; i < drawn.Count; i++) Assert.That(drawn[i], Is.Not.SameAs(drawn[i - 1]), "position " + i);
        }

        [Test]
        public void OrderChangesBetweenRounds()
        {
            var roster = Visitors(9);
            var random = new System.Random(5);
            var bag = new VisitorBag(() => roster, max => random.Next(max));
            var drawn = Draw(bag, 9 * 6);
            var rounds = Enumerable.Range(0, 6).Select(r => string.Join(",", drawn.Skip(r * 9).Take(9).Select(v => v.name))).ToList();
            Assert.That(rounds.Distinct().Count(), Is.GreaterThan(1));
        }

        [Test]
        public void AddedVisitorJoinsTheNextRound()
        {
            var roster = new List<NpcAppearance>(Visitors(3));
            var bag = new VisitorBag(() => roster, max => 0);
            Draw(bag, 2);
            var newcomer = Visitors(1)[0];
            roster.Add(newcomer);
            var next = Draw(bag, 1 + 4);
            Assert.That(next.Take(1), Has.No.Member(newcomer), "the current round finishes first");
            Assert.That(next.Skip(1), Has.Member(newcomer));
        }

        [Test]
        public void PeekDoesNotUseTheVisitorUp()
        {
            var bag = new VisitorBag(() => Visitors(4), max => 0);
            var first = bag.Peek();
            Assert.That(bag.Peek(), Is.SameAs(first));
            Assert.That(bag.Remaining, Is.EqualTo(4));
        }

        [Test]
        public void EveryNpcAppearanceIsInTheShopRoster()
        {
            var roster = AssetDatabase.LoadAssetAtPath<VisitorRoster>(RosterPath);
            Assert.That(roster, Is.Not.Null, RosterPath);
            var missing = AssetDatabase.FindAssets("t:NpcAppearance", new[] { "Assets/Content/NPCs" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<NpcAppearance>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(appearance => !roster.visitors.Contains(appearance)).Select(appearance => appearance.name).ToArray();
            Assert.That(missing, Is.Empty, "Add these to " + RosterPath);
            Assert.That(roster.visitors.All(v => v && v.IsComplete), "every visitor needs all five sprites");
        }

        /// <summary>Playtest: the rare bulk buyer («Скупщик») has his own look and never joins the shuffled round.</summary>
        [Test]
        public void TheBulkBuyerHasHisOwnCompleteLookOutsideTheRound()
        {
            var roster = AssetDatabase.LoadAssetAtPath<VisitorRoster>(RosterPath);
            Assert.That(roster.bulkBuyer, Is.Not.Null, "Assign Bulk Buyer on " + RosterPath);
            Assert.That(roster.bulkBuyer.IsComplete, Is.True, "the bulk buyer needs all five sprites");
            Assert.That(roster.visitors, Has.No.Member(roster.bulkBuyer));
            Assert.That(AssetDatabase.GetAssetPath(roster.bulkBuyer), Does.Not.StartWith("Assets/Content/NPCs/"),
                "Content/NPCs holds the regular visitors only");
        }
    }
}
