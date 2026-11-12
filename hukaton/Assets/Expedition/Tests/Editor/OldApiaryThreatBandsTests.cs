using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>
    /// The Old Apiary gets harder away from the start: every creature that is not a thematic inhabitant of a point of
    /// interest respects the threat bands (<see cref="OutingThreatBands"/> on the departure) at its home and at both
    /// spawn points, and the points of interest keep their inhabitants where the level design put them.
    /// </summary>
    public sealed class OldApiaryThreatBandsTests
    {
        const string PrefabPath = "Assets/OldApiary/Prefabs/Old Apiary.prefab";
        const string DepartureName = "Departure (01 deck)";

        // Points of interest: creature root position (x, z) under "Creatures". Moving one on purpose means updating this.
        static readonly Dictionary<string, Vector2> PointsOfInterest = new Dictionary<string, Vector2>
        {
            { "BeeMutant_06", new Vector2(18f, 95f) },
            { "BeeSwarm_04", new Vector2(13f, 63f) },
            { "Beekeeper_04", new Vector2(16.9167f, 58.9167f) },
            { "Bush_15", new Vector2(122.4f, 53.6f) },
            { "ClownEmoji_17", new Vector2(99f, 34f) },
            { "DwarfRobot_22", new Vector2(65f, 113f) },
            { "LeshySkater_21", new Vector2(92f, 59f) },
            { "LivingTree_20", new Vector2(47f, 39f) },
            { "Mushroom_03", new Vector2(15.5f, 38.5f) },
            { "RatPumpkin_05", new Vector2(35f, 73.5f) },
            { "Rooster_09", new Vector2(57f, 80f) },
            { "SnailFrog_07", new Vector2(42f, 104f) },
            { "TerritorialDog_10", new Vector2(76f, 103.4f) },
            { "Troll_13", new Vector2(125.4f, 93f) },
            { "Worm_02", new Vector2(36.1667f, 20f) },
        };

        static GameObject Apiary()
        {
            var apiary = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(apiary, Is.Not.Null, PrefabPath);
            return apiary;
        }

        static OutingThreatBands Rule(GameObject apiary)
        {
            var rules = apiary.GetComponentsInChildren<OutingThreatBands>(true);
            Assert.That(rules.Length, Is.EqualTo(1), "one threat-band rule in the Old Apiary");
            Assert.That(rules[0].name, Is.EqualTo(DepartureName), "the rule sits on the departure");
            return rules[0];
        }

        static List<WorldCreature> Creatures(GameObject apiary)
        {
            var group = apiary.transform.Find("Creatures");
            Assert.That(group, Is.Not.Null, "Creatures");
            var creatures = new List<WorldCreature>();
            foreach (Transform child in group)
                if (child.TryGetComponent(out WorldCreature creature)) creatures.Add(creature);
            return creatures;
        }

        [Test]
        public void BandsCapTheTierByDistance()
        {
            var bands = new List<OutingThreatBands.Band>
            {
                new OutingThreatBands.Band { radius = 30, maxTier = 1 },
                new OutingThreatBands.Band { radius = 55, maxTier = 2 },
                new OutingThreatBands.Band { radius = 80, maxTier = 3 },
            };
            Assert.That(OutingThreatBands.Ordered(bands));
            Assert.That(OutingThreatBands.MaxTierAt(bands, 0), Is.EqualTo(1));
            Assert.That(OutingThreatBands.MaxTierAt(bands, 29.9f), Is.EqualTo(1));
            Assert.That(OutingThreatBands.MaxTierAt(bands, 30), Is.EqualTo(2));
            Assert.That(OutingThreatBands.MaxTierAt(bands, 79.9f), Is.EqualTo(3));
            Assert.That(OutingThreatBands.MaxTierAt(bands, 80), Is.EqualTo(int.MaxValue));
            Assert.That(OutingThreatBands.MaxTierAt(null, 10), Is.EqualTo(int.MaxValue));

            bands.Add(new OutingThreatBands.Band { radius = 70, maxTier = 4 });
            Assert.That(OutingThreatBands.Ordered(bands), Is.False, "radius falls");
            bands[3] = new OutingThreatBands.Band { radius = 100, maxTier = 2 };
            Assert.That(OutingThreatBands.Ordered(bands), Is.False, "tier falls");
        }

        [Test]
        public void CreaturesAreWeakerCloserToTheStart()
        {
            var apiary = Apiary();
            var rule = Rule(apiary);
            Assert.That(OutingThreatBands.Ordered(rule.bands), "bands grow outward");
            var creatures = Creatures(apiary);
            var failures = new List<string>();
            int checkedCount = 0;
            foreach (var creature in creatures)
            {
                int tier = rule.TierOf(creature);
                if (tier < 0) { failures.Add(creature.name + ": its kind has no tier"); continue; }
                if (rule.exempt.Contains(creature)) continue;
                checkedCount++;
                Assert.That(creature.spawnPoints, Is.Not.Null.And.Not.Empty, creature.name + ": spawn points");
                var places = new List<Transform> { creature.transform };
                places.AddRange(creature.spawnPoints);
                foreach (var place in places)
                {
                    if (!place) { failures.Add(creature.name + ": missing spawn point"); continue; }
                    float distance = rule.Distance(place.position);
                    int allowed = rule.MaxTierAt(distance);
                    if (tier > allowed)
                        failures.Add($"{place.name}: tier {tier} at {distance:0.0} m, the band allows {allowed}");
                }
            }
            Assert.That(checkedCount, Is.GreaterThan(0), "copies to check");
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        [Test]
        public void PointsOfInterestKeepTheirInhabitants()
        {
            var apiary = Apiary();
            var rule = Rule(apiary);
            var creatures = Creatures(apiary);
            Assert.That(rule.exempt.Count, Is.EqualTo(PointsOfInterest.Count), "exempt are exactly the points-of-interest inhabitants");
            foreach (var pair in PointsOfInterest)
            {
                var creature = creatures.FirstOrDefault(c => c.name == pair.Key);
                Assert.That(creature, Is.Not.Null, pair.Key);
                Assert.That(rule.exempt.Contains(creature), pair.Key + " is exempt");
                Vector3 at = creature.transform.localPosition;
                Assert.That(Vector2.Distance(new Vector2(at.x, at.z), pair.Value), Is.LessThan(.01f), pair.Key + " stays at its point of interest");
            }
        }
    }
}
