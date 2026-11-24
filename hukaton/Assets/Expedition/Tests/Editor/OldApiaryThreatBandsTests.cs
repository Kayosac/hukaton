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

        // The heavy ones (sinister tree, bee mutant, troll, dwarf robot) live only in the far band, exempt or not:
        // the first loop (up to 55 m) has nothing a starting deck cannot handle (balance stage 1, LivingTree_20 moved out).
        [Test]
        public void HeavyCreaturesLiveOnlyInTheFarBand()
        {
            var apiary = Apiary();
            var rule = Rule(apiary);
            float far = rule.bands[rule.bands.Count - 1].radius;
            var failures = new List<string>();
            int heavy = 0;
            foreach (var creature in Creatures(apiary))
            {
                if (rule.TierOf(creature) < 4) continue;
                heavy++;
                var places = new List<Transform> { creature.transform };
                places.AddRange(creature.spawnPoints);
                foreach (var place in places)
                    if (place && rule.Distance(place.position) < far)
                        failures.Add($"{place.name}: {rule.Distance(place.position):0.0} m from the departure, closer than {far} m");
            }
            Assert.That(heavy, Is.GreaterThan(0), "heavy creatures to check");
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
            var tree = Creatures(apiary).First(c => c.name == "LivingTree_20");
            Assert.That(rule.exempt.Contains(tree), Is.False, "the moved tree follows the band rule like any other copy");
        }

        // ---- the first threat and the first easy kill (balance stage 1) ----

        const string FirstThreat = "Bush_x4";
        const string FirstKill = "RatPumpkin_x4";

        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        static List<Vector3> SpawnPlaces(WorldCreature creature)
        {
            var places = new List<Vector3>();
            if (creature.spawnPoints != null) foreach (var point in creature.spawnPoints) if (point) places.Add(point.position);
            if (places.Count == 0) places.Add(creature.transform.position);
            return places;
        }

        static bool Hostile(WorldCreature creature) => creature.temperament == Temperament.Aggressive || creature.temperament == Temperament.Territorial;

        // How far from its spawn point a creature can notice the player: a lurker stands still in its cover (it walks
        // back once more than 0.8 m away) but sees all round; any other creature wanders its territory first.
        static float NoticeRadius(WorldCreature creature, bool withWander)
            => creature.sightRange + (creature.ambush.enabled ? .8f : withWander ? creature.territoryRadius : 0);

        // Every pumpkin rat keeps 20 m and more from the first threat at every pair of spawn points, and no spot is
        // close enough for both to notice (let alone spring at) the player at once.
        [Test]
        public void PumpkinRatsKeepClearOfTheFirstThreat()
        {
            var apiary = Apiary();
            var creatures = Creatures(apiary);
            var bush = creatures.First(c => c.name == FirstThreat);
            var kill = creatures.First(c => c.name == FirstKill);
            Assert.That(bush.ambush.enabled && kill.ambush.enabled, "both lie in wait");
            var rats = creatures.Where(c => c.displayName == kill.displayName).ToList();
            Assert.That(rats.Count, Is.GreaterThan(1), "pumpkin rats to check");
            var failures = new List<string>();
            foreach (var rat in rats)
                foreach (var b in SpawnPlaces(bush))
                    foreach (var r in SpawnPlaces(rat))
                    {
                        float distance = Vector2.Distance(Flat(b), Flat(r));
                        float springs = Mathf.Min(bush.ambush.pounceRange, bush.sightRange) + Mathf.Min(rat.ambush.pounceRange, rat.sightRange) + 1.6f;
                        float notices = NoticeRadius(bush, true) + NoticeRadius(rat, true);
                        if (distance < 20) failures.Add($"{rat.name}: {distance:0.0} m from {FirstThreat}, closer than 20 m");
                        if (springs >= distance) failures.Add($"{rat.name}: ambushes reach {springs:0.0} m, points {distance:0.0} m apart");
                        if (notices >= distance) failures.Add($"{rat.name}: both notice within {notices:0.0} m, points {distance:0.0} m apart");
                    }
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        // The first easy kill (a guaranteed trophy card) stays on the first loop, in the 30–55 m band.
        [Test]
        public void FirstKillLivesOnTheFirstLoop()
        {
            var apiary = Apiary();
            var rule = Rule(apiary);
            var kill = Creatures(apiary).First(c => c.name == FirstKill);
            var places = SpawnPlaces(kill);
            places.Add(kill.transform.position);
            foreach (var place in places)
                Assert.That(rule.Distance(place), Is.InRange(30f, 55f), FirstKill + " in the 30–55 m band");
        }

        // At the first threat (15 m round its points) at most two hostile creatures can notice the player at once,
        // even counting how far the others wander from their points. Across the first loop's band (30–55 m) a pumpkin
        // rat is never the third to notice the player (by sight from any spawn point). The crowd of a bee swarm, a
        // clown and a goblin dog past the arch (about 55 m, north-east) is left as the level design has it.
        [Test]
        public void AtMostTwoHostilesNoticeThePlayerOnTheFirstLoop()
        {
            var apiary = Apiary();
            var rule = Rule(apiary);
            var creatures = Creatures(apiary);
            var hostiles = creatures.Where(Hostile).ToList();
            var bush = creatures.First(c => c.name == FirstThreat);
            var kill = creatures.First(c => c.name == FirstKill);
            var bushPlaces = SpawnPlaces(bush);
            Vector3 centre = rule.transform.position;
            var failures = new List<string>();
            int nearSpots = 0, bandSpots = 0;
            for (int x = -55; x <= 55; x++)
                for (int z = -55; z <= 55; z++)
                {
                    var spot = centre + new Vector3(x, 0, z);
                    float distance = rule.Distance(spot);
                    bool band = distance >= 30 && distance <= 55;
                    bool nearThreat = bushPlaces.Any(p => Vector2.Distance(Flat(p), Flat(spot)) <= 15);
                    if (!band && !nearThreat) continue;
                    if (nearThreat) nearSpots++;
                    if (band) bandSpots++;
                    var seeing = hostiles.Where(c => SpawnPlaces(c).Any(p => Vector2.Distance(Flat(p), Flat(spot)) <= NoticeRadius(c, nearThreat))).ToList();
                    if (seeing.Count <= 2) continue;
                    if (!nearThreat && !seeing.Any(c => c.displayName == kill.displayName)) continue;
                    if (failures.Count < 20)
                    {
                        var local = apiary.transform.InverseTransformPoint(spot);
                        failures.Add($"({local.x:0};{local.z:0}), {distance:0} m from the departure: {string.Join(", ", seeing.Select(c => c.name))}");
                    }
                }
            Assert.That(nearSpots, Is.GreaterThan(100), "spots round the first threat");
            Assert.That(bandSpots, Is.GreaterThan(1000), "spots in the band");
            Assert.That(failures, Is.Empty, "more than two hostiles notice the player at\n" + string.Join("\n", failures));
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
