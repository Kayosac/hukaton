using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>
    /// Peaceful creatures that can fight back stand up for their own kind (WorldCreature.Kin.cs): who does, who counts
    /// as kin and when an attack is noticed. The live behaviour is checked in the EnemyDebug play tests.
    /// </summary>
    public sealed class KinDefenceTests
    {
        const float Range = 12, Hearing = 4;

        static WorldCreature Prefab(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/" + name + ".prefab");
            Assert.That(prefab, Is.Not.Null, name);
            var creature = prefab.GetComponent<WorldCreature>();
            Assert.That(creature, Is.Not.Null, name);
            return creature;
        }

        // ---------------- noticing ----------------

        [Test]
        public void BeyondTheWitnessRangeNothingIsNoticed()
        {
            Assert.That(WorldCreature.NoticesAttack(12.5f, Range, Hearing, true, 1, true), Is.False);
            Assert.That(WorldCreature.NoticesAttack(12.5f, Range, Hearing, false, 1, true), Is.False);
        }

        [Test]
        public void CloseByTheCryIsHeardFromBehindAndThroughCover()
        {
            Assert.That(WorldCreature.NoticesAttack(3.5f, Range, Hearing, true, -1, false), Is.True);
        }

        [Test]
        public void FurtherAwayTheAttackHasToBeInClearView()
        {
            Assert.That(WorldCreature.NoticesAttack(8, Range, Hearing, true, 1, true), Is.True, "in front, in clear view");
            Assert.That(WorldCreature.NoticesAttack(8, Range, Hearing, true, 0, true), Is.True, "to the side");
            Assert.That(WorldCreature.NoticesAttack(8, Range, Hearing, true, 1, false), Is.False, "behind a rock or in fog");
            Assert.That(WorldCreature.NoticesAttack(8, Range, Hearing, true, -.6f, true), Is.False, "a calm one does not look behind itself");
            Assert.That(WorldCreature.NoticesAttack(8, Range, Hearing, false, -.6f, true), Is.True, "one already up looks all round");
        }

        // ---------------- who stands up for whom ----------------

        [Test]
        public void OnlyPeacefulCreaturesThatCanFightBackStandUpForKin()
        {
            foreach (var name in new[] { "Rooster baseball", "Teen leshy skater", "Zombie beekeeper" })
                Assert.That(Prefab(name).StandsUpForKin, Is.True, name + ": peaceful, strikes or shoots back");
            foreach (var name in new[] { "Purple mushroom", "Kind little worm", "Snail frog mount" })
                Assert.That(Prefab(name).StandsUpForKin, Is.False, name + ": timid, it flees");
            foreach (var name in new[] { "Goblin dog", "Budding troll" })
                Assert.That(Prefab(name).StandsUpForKin, Is.False, name + ": not peaceful, it has its own reasons to fight");
        }

        [Test]
        public void KinIsTheSameKindOfCreature()
        {
            var rooster = Prefab("Rooster baseball");
            var leshy = Prefab("Teen leshy skater");
            Assert.That(rooster.IsKinOf(rooster), Is.True);
            Assert.That(rooster.IsKinOf(leshy), Is.False);
            Assert.That(leshy.IsKinOf(rooster), Is.False);
            Assert.That(rooster.IsKinOf(null), Is.False);

            var copy = new GameObject("Rooster copy").AddComponent<WorldCreature>();
            try
            {
                copy.displayName = rooster.displayName;
                Assert.That(copy.IsKinOf(rooster) && rooster.IsKinOf(copy), Is.True, "another rooster");
                copy.displayName = leshy.displayName;
                Assert.That(copy.IsKinOf(rooster), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(copy.gameObject);
            }
        }
    }
}
