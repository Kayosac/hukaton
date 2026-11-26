using System.Collections.Generic;
using Hakaton.Expeditions;
using Hakaton.Loot;
using NUnit.Framework;
using UnityEditor;

namespace Hakaton.EnemyDebug.Tests
{
    /// <summary>
    /// Combat balance of the demo (stage 8): what a kill pays, in the items' base prices. The hard «sponges» pay more,
    /// the safe small fry less. Each table is rolled by its own code (<see cref="LootTable.Roll"/>) many times over.
    /// </summary>
    public class LootBalanceTests
    {
        const int Rolls = 200000;

        static double Average(string prefab)
        {
            var creature = AssetDatabase.LoadAssetAtPath<WorldCreature>($"Assets/Game/Prefabs/{prefab}.prefab");
            Assert.That(creature, Is.Not.Null, prefab);
            var random = new System.Random(20261126);
            var dropped = new List<LootItem>();
            long coins = 0;
            for (int i = 0; i < Rolls; i++)
            {
                dropped.Clear();
                creature.drops.Roll(() => (float)random.NextDouble(), dropped);
                foreach (var item in dropped) coins += item.price;
            }
            return (double)coins / Rolls;
        }

        // Sponges: big, dangerous, and now worth it.
        [TestCase("Sinister living tree", 251)]
        [TestCase("Enraged bee mutant", 225)]
        [TestCase("Giant vile toad", 230)]
        [TestCase("Swamp mutant", 198)]
        [TestCase("Raging fly agaric", 206)]
        [TestCase("AntPlug", 260)]
        [TestCase("AntQueen", 315)]
        public void AKillPaysAboutItsRisk(string prefab, int coins)
        {
            double average = Average(prefab);
            UnityEngine.Debug.Log($"[stage-8] loot {prefab}: {average:0.0} per kill (target {coins})");
            Assert.That(average, Is.EqualTo(coins).Within(coins * .05), $"{prefab}: the average kill pays {coins} ± 5 %");
        }
    }
}
