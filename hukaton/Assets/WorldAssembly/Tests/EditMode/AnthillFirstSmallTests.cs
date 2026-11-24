using System.Linq;
using Hakaton.Dungeons;
using Hakaton.Dungeons.Unity;
using Hakaton.Expeditions;
using Hakaton.WorldAssembly.Unity;
using NUnit.Framework;
using UnityEditor;

namespace Hakaton.WorldAssembly.Tests
{
    /// <summary>
    /// Until the player has opened an anthill's prize chest every anthill the world lays is the small one (one level, no
    /// Queen); after it the kit's chance of a large one (0.4) holds.
    /// </summary>
    public sealed class AnthillFirstSmallTests
    {
        const string KitPath = "Assets/Dungeons/Anthill/Anthill Kit.asset";
        const int Seeds = 200;

        [Test]
        public void EveryAnthillIsSmallUntilAPrizeChestIsOpened()
        {
            var kit = AssetDatabase.LoadAssetAtPath<DungeonKit>(KitPath);
            Assert.That(kit, Is.Not.Null);
            Assert.That(kit.largeChance, Is.EqualTo(.4f).Within(1e-5), "the usual chance of a large anthill is kept");
            bool was = DungeonProgress.AnthillPrizeOpened;
            try
            {
                DungeonProgress.Restore(false);
                Assert.That(DungeonSite.LargeAllowed, Is.False);
                for (int seed = 1; seed <= Seeds; seed++)
                {
                    var plan = DungeonGenerator.Generate(seed, kit.Recipe(DungeonSite.LargeAllowed));
                    Assert.That(plan.Large, Is.False, $"seed {seed}: large before the first prize");
                    Assert.That(plan.Levels, Is.EqualTo(1), $"seed {seed}: one level");
                    Assert.That(plan.Rooms.Any(r => r.Shape.Role == RoomRole.Queen), Is.False, $"seed {seed}: no Queen");
                }

                DungeonProgress.NoteAnthillPrizeOpened();
                Assert.That(DungeonSite.LargeAllowed, Is.True);
                int large = 0;
                for (int seed = 1; seed <= Seeds; seed++)
                {
                    var plan = DungeonGenerator.Generate(seed, kit.Recipe(DungeonSite.LargeAllowed));
                    if (!plan.Large) continue;
                    large++;
                    Assert.That(plan.Rooms.Count(r => r.Shape.Role == RoomRole.Queen), Is.EqualTo(1), $"seed {seed}: a large one has its Queen");
                }
                UnityEngine.Debug.Log($"[Anthill] after the first prize: {large} large of {Seeds}");
                Assert.That(large, Is.InRange(Seeds * .3, Seeds * .5), "about four in ten are large");
            }
            finally { DungeonProgress.Restore(was); }
        }

        [Test]
        public void TheSmallRuleChangesOnlyTheSize()
        {
            // A small anthill laid before the first prize is the same as the small one of that seed after it: only the
            // seeds that would have been large are made small.
            var kit = AssetDatabase.LoadAssetAtPath<DungeonKit>(KitPath);
            for (int seed = 1; seed <= 60; seed++)
            {
                var usual = DungeonGenerator.Generate(seed, kit.Recipe());
                if (usual.Large) continue;
                var first = DungeonGenerator.Generate(seed, kit.Recipe(false));
                Assert.That(first.Cells.Keys.OrderBy(c => c.X).ThenBy(c => c.Y), Is.EqualTo(usual.Cells.Keys.OrderBy(c => c.X).ThenBy(c => c.Y)), $"seed {seed}");
                Assert.That(first.Rooms.Select(r => r.Shape.Id), Is.EqualTo(usual.Rooms.Select(r => r.Shape.Id)), $"seed {seed}");
            }
        }
    }
}
