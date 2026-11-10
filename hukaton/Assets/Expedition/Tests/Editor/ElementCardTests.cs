using System.Collections.Generic;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions.Cards;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>Rules of cluster B's cards: tar puddle, rain cloud, lightning, fog, lure, attraction (board: tmp/new-cards/cards.md).</summary>
    public sealed class ElementCardTests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void DestroyCreated()
        {
            foreach (var item in created) if (item) Object.DestroyImmediate(item);
            created.Clear();
        }

        // ---------- tar: states ----------

        [Test]
        public void TarGoesFromSlowingToBurningToBurntOut()
        {
            var tar = new TarPuddleState(30);
            Assert.That(tar.Phase, Is.EqualTo(TarPhase.Tar));
            Assert.That(tar.Slows, Is.True);
            Assert.That(tar.Burns, Is.False, "Unlit tar does no damage.");
            Assert.That(tar.Advance(1000), Is.False, "Unlit tar never runs out: it stays until the end of the outing.");
            Assert.That(tar.Slows, Is.True);

            Assert.That(tar.Ignite(), Is.True);
            Assert.That(tar.Burns, Is.True);
            Assert.That(tar.Slows, Is.False, "No slowdown in the fire.");
            Assert.That(tar.Advance(29.5f), Is.False);
            Assert.That(tar.BurnRemaining, Is.EqualTo(.5f).Within(1e-3f));
            Assert.That(tar.Advance(.5f), Is.True, "It burns for exactly 30 s.");
            Assert.That(tar.Phase, Is.EqualTo(TarPhase.BurntOut));
            Assert.That(tar.Gone, Is.True);
            Assert.That(tar.Ignite(), Is.False, "Burnt-out tar is gone for good.");
            Assert.That(tar.Slows || tar.Burns, Is.False);
        }

        [Test]
        public void LightingBurningTarAgainIsIgnoredAndKeepsTheTimer()
        {
            var tar = new TarPuddleState(30);
            tar.Ignite();
            tar.Advance(20);
            Assert.That(tar.Ignite(), Is.False);
            Assert.That(tar.BurnRemaining, Is.EqualTo(10).Within(1e-3f), "A second fire does not restart the 30 s.");
        }

        [Test]
        public void RainWashesTarAwayLitOrNot()
        {
            var unlit = new TarPuddleState(30);
            Assert.That(unlit.Wash(), Is.True);
            Assert.That(unlit.Phase, Is.EqualTo(TarPhase.Washed));
            Assert.That(unlit.Slows, Is.False, "Washed tar no longer slows.");
            Assert.That(unlit.Ignite(), Is.False, "Washed tar is no target for fire.");

            var burning = new TarPuddleState(30);
            burning.Ignite();
            burning.Advance(10);
            Assert.That(burning.Wash(), Is.True, "Rain puts burning tar out without waiting for the 30 s.");
            Assert.That(burning.Burns, Is.False);
            Assert.That(burning.Gone, Is.True);
            Assert.That(burning.Wash(), Is.False, "Nothing is left to wash.");
        }

        // ---------- tar: damage by time in the fire ----------

        [Test]
        public void TarFireHurtsOneUnitPerSecondSpentInIt()
        {
            var fire = new TarFireExposure();
            var fighter = new object();
            int frame = 0;
            Assert.That(fire.Expose(fighter, frame++, .5f), Is.EqualTo(0), "No instant hit on stepping in.");
            Assert.That(fire.Expose(fighter, frame++, .5f), Is.EqualTo(1), "The first unit after a full second.");
            int total = 1;
            for (int i = 0; i < 290; i++) total += fire.Expose(fighter, frame++, .1f);
            Assert.That(total, Is.EqualTo(30), "30 s in the fire at 1 unit/s: 30 units (15 hearts for the player).");
        }

        [Test]
        public void CrossingTheEdgeGivesNoBurstOfRepeatedHits()
        {
            var fire = new TarFireExposure();
            var fighter = new object();
            int frame = 0, total = 0;
            for (int i = 0; i < 20; i++)
            {
                for (int k = 0; k < 3; k++) total += fire.Expose(fighter, frame++, .1f);
                frame += 5; // stepped out for a few frames
            }
            Assert.That(total, Is.EqualTo(6), "6 s in the fire in all → 6 units, however often the edge is crossed.");
        }

        [Test]
        public void OverlappingPuddlesDoNotStack()
        {
            var fire = new TarFireExposure();
            var fighter = new object();
            int total = 0;
            for (int frame = 0; frame < 100; frame++)
            {
                total += fire.Expose(fighter, frame, .1f); // puddle A
                total += fire.Expose(fighter, frame, .1f); // puddle B, same frame
            }
            Assert.That(total, Is.EqualTo(10), "10 s in two overlapping fires is still 10 units.");
        }

        [Test]
        public void EveryFighterBurnsOnItsOwnClock()
        {
            var fire = new TarFireExposure();
            object player = new object(), creature = new object();
            int playerTotal = 0, creatureTotal = 0;
            for (int frame = 0; frame < 50; frame++)
            {
                playerTotal += fire.Expose(player, frame, .1f);
                if (frame >= 20) creatureTotal += fire.Expose(creature, frame, .1f);
            }
            Assert.That(playerTotal, Is.EqualTo(5));
            Assert.That(creatureTotal, Is.EqualTo(3));
            fire.Clear();
            Assert.That(fire.Pending(player), Is.EqualTo(0));
        }

        // ---------- rain ----------

        [Test]
        public void RainReachesEveryPuddleItTouches()
        {
            Assert.That(RainRules.CirclesOverlap(Vector3.zero, 3, new Vector3(4.9f, 0, 0), 2), Is.True,
                "A puddle only partly under the rain is reached (and removed whole).");
            Assert.That(RainRules.CirclesOverlap(Vector3.zero, 3, new Vector3(1, 0, 1), 2), Is.True);
            Assert.That(RainRules.CirclesOverlap(Vector3.zero, 3, new Vector3(3, 0, 4), 2), Is.False, "Edges just touching: not reached.");
            Assert.That(RainRules.CirclesOverlap(Vector3.zero, 3, new Vector3(6, 0, 0), 2), Is.False);
        }

        [Test]
        public void RainZoneWashesOnlyZonesItReaches()
        {
            var rain = Make("Rain", Vector3.zero).AddComponent<RainCloudZone>();
            rain.radius = 3;
            rain.height = 4.5f;
            rain.depth = 1;
            var near = Make("Tar near", new Vector3(4.5f, 0, 0)).AddComponent<TarPuddleZone>();
            near.radius = 2;
            var far = Make("Tar far", new Vector3(6, 0, 0)).AddComponent<TarPuddleZone>();
            far.radius = 2;
            var high = Make("Tar on a cliff", new Vector3(1, 12, 0)).AddComponent<TarPuddleZone>();
            high.radius = 2;
            Assert.That(rain.Reaches(near), Is.True);
            Assert.That(rain.Reaches(far), Is.False);
            Assert.That(rain.Reaches(high), Is.False, "Far above the cloud.");
            Assert.That(rain.Reaches(rain), Is.False);
        }

        // ---------- lightning ----------

        sealed class Fighter : ICombatant
        {
            public readonly string name;
            public bool wet;
            public Faction faction = Faction.Wild;
            public Vector3 at;
            public Fighter(string name, float x, bool wet, float z = 0) { this.name = name; at = new Vector3(x, 0, z); this.wet = wet; }
            public Faction Faction => faction;
            public bool Alive => true;
            public Vector3 Position => at;
            public Vector3 Centre => at + Vector3.up;
            public Transform Root => null;
            public bool TakeDamage(in DamageInfo damage) => true;
            public override string ToString() => name;
        }

        static List<Fighter> Chain(Fighter first, IReadOnlyList<Fighter> all, System.Func<Fighter, Fighter, bool> sight = null)
            => LightningChain.Build(first, all, 3, 4, f => f.wet, f => f.Centre, sight ?? ((a, b) => true));

        [Test]
        public void ADryTargetIsStruckAlone()
        {
            var first = new Fighter("dry wolf", 0, false);
            var wet = new Fighter("wet boar", 1, true);
            Assert.That(Chain(first, new[] { first, wet }), Is.EqualTo(new[] { first }));
        }

        [Test]
        public void TheChainJumpsOnlyToWetTargets()
        {
            var first = new Fighter("first", 0, true);
            var dry = new Fighter("dry", 1, false);
            var wet = new Fighter("wet", 3, true);
            Assert.That(Chain(first, new[] { first, dry, wet }), Is.EqualTo(new[] { first, wet }));
        }

        [Test]
        public void EachJumpIsAtMostFourMetres()
        {
            var first = new Fighter("first", 0, true);
            var edge = new Fighter("exactly 4 m", 4, true);
            var beyond = new Fighter("8.1 m", 8.1f, true);
            Assert.That(Chain(first, new[] { first, edge, beyond }), Is.EqualTo(new[] { first, edge }),
                "4.1 m from the last target is out of reach.");
            var far = new Fighter("4.1 m", 4.1f, true);
            Assert.That(Chain(first, new[] { first, far }), Is.EqualTo(new[] { first }));
        }

        [Test]
        public void TheChainIsLinearAndPicksTheNearestFromTheLastTarget()
        {
            var first = new Fighter("first", 0, true);
            var b = new Fighter("b", 3.9f, true);
            var c = new Fighter("c", 7.5f, true);
            var side = new Fighter("side", 0, true, 3.95f);
            Assert.That(Chain(first, new[] { c, side, b, first }), Is.EqualTo(new[] { first, b, c }),
                "From b the nearest is c (3.6 m), although c is 7.5 m from the first target.");
        }

        [Test]
        public void TheChainNeedsDirectSight()
        {
            var first = new Fighter("first", 0, true);
            var behindRock = new Fighter("behind a rock", 1, true);
            var open = new Fighter("in the open", 3, true);
            bool Sight(Fighter a, Fighter b) => b != behindRock && a != behindRock;
            Assert.That(Chain(first, new[] { first, behindRock, open }, Sight), Is.EqualTo(new[] { first, open }));
            Assert.That(Chain(first, new[] { first, behindRock }, Sight), Is.EqualTo(new[] { first }));
        }

        [Test]
        public void NoTargetIsHitTwiceAndTheChainStopsAtThree()
        {
            var a = new Fighter("a", 0, true);
            var b = new Fighter("b", 1, true);
            Assert.That(Chain(a, new[] { a, b }), Is.EqualTo(new[] { a, b }), "No jump back to a target already struck.");
            var crowd = new[] { a, b, new Fighter("c", 2, true), new Fighter("d", 3, true), new Fighter("e", .5f, true, .5f) };
            var chain = Chain(a, crowd);
            Assert.That(chain.Count, Is.EqualTo(3), "Three targets in all, the first included.");
            Assert.That(new HashSet<Fighter>(chain).Count, Is.EqualTo(3));
        }

        [Test]
        public void TheWetPlayerAndAlliesAreTargetsToo()
        {
            var first = new Fighter("wolf", 0, true);
            var player = new Fighter("player", 2, true) { faction = Faction.Player };
            var ally = new Fighter("tree", 3.5f, true) { faction = Faction.Player };
            Assert.That(Chain(first, new[] { first, ally, player }), Is.EqualTo(new[] { first, player, ally }));
        }

        [Test]
        public void WetTargetsTakeDoubleDamage()
        {
            Assert.That(LightningChain.Damage(3, false, 2), Is.EqualTo(3));
            Assert.That(LightningChain.Damage(3, true, 2), Is.EqualTo(6));
            Assert.That(LightningChain.Damage(4, true, 2), Is.EqualTo(8));
        }

        FakeCardZone FakeZone(CardZoneKind kind, Vector3 position, float radius)
        {
            var zone = ScriptableObject.CreateInstance<FakeCardZone>();
            zone.kind = kind;
            zone.position = position;
            zone.radius = radius;
            created.Add(zone);
            return zone;
        }

        [Test]
        public void LightningOnAFighterInTarLightsThatPuddle()
        {
            // Owner, 09.11 evening: a strike on a fighter standing in tar (or touching it) sets the puddle alight.
            var zones = new CardZoneRegistry();
            var tar = FakeZone(CardZoneKind.TarPuddle, Vector3.zero, 2);
            var otherTar = FakeZone(CardZoneKind.TarPuddle, new Vector3(10, 0, 0), 2);
            var fog = FakeZone(CardZoneKind.Fog, Vector3.zero, 3);
            zones.Add(tar);
            zones.Add(otherTar);
            zones.Add(fog);

            Assert.That(LightningTar.IgniteUnder(zones, new Vector3(1, 0, .5f), .3f), Is.EqualTo(1), "Feet in the tar.");
            Assert.That((tar.ignitions, otherTar.ignitions, fog.ignitions), Is.EqualTo((1, 0, 0)),
                "Only the puddle under the struck fighter; other kinds of zones are not lit by this rule.");
            Assert.That(LightningTar.IgniteUnder(zones, new Vector3(2.2f, 0, 0), .3f), Is.EqualTo(1), "Touching the edge counts.");
            Assert.That(LightningTar.IgniteUnder(zones, new Vector3(3, 0, 0), .3f), Is.Zero, "A step beyond the edge: nothing.");
            Assert.That(LightningTar.IgniteUnder(zones, new Vector3(0, 3, 0), .3f), Is.Zero, "High above the tar (on a branch, mid-jump): nothing.");
            Assert.That(tar.ignitions, Is.EqualTo(2));
            Assert.That(LightningTar.IgniteUnder(null, Vector3.zero, .3f), Is.Zero);
        }

        [Test]
        public void LightningAssetLightsTarUnderTheStruckFighter()
        {
            var lightning = Effect<LightningEffect>("Lightning");
            Assert.That(lightning.tarTouchMargin, Is.GreaterThan(0).And.LessThanOrEqualTo(.5f),
                "A fighter touching the puddle's edge counts, one a step away does not.");
        }

        // ---------- fog ----------

        [Test]
        public void FogHidesEverySightLineThroughIt()
        {
            Vector3 foot = Vector3.zero;
            Assert.That(FogRules.SegmentCrossesCylinder(new Vector3(-10, 1.5f, 0), new Vector3(10, 1.5f, 0), foot, 3, 3.5f), Is.True, "Straight through.");
            Assert.That(FogRules.SegmentCrossesCylinder(new Vector3(-10, 1.5f, 3.5f), new Vector3(10, 1.5f, 3.5f), foot, 3, 3.5f), Is.False, "Beside it.");
            Assert.That(FogRules.SegmentCrossesCylinder(new Vector3(-10, 5, 0), new Vector3(10, 5, 0), foot, 3, 3.5f), Is.False, "Over it.");
            Assert.That(FogRules.SegmentCrossesCylinder(new Vector3(0, 1, 0), new Vector3(10, 1, 8), foot, 3, 3.5f), Is.True, "Looking out from inside.");
            Assert.That(FogRules.SegmentCrossesCylinder(new Vector3(10, 1, 8), new Vector3(1, 1, -1), foot, 3, 3.5f), Is.True, "Looking at someone inside.");
            Assert.That(FogRules.SegmentCrossesCylinder(new Vector3(-6, 6, 0), new Vector3(6, -1, 0), foot, 3, 3.5f), Is.True, "Down through the top.");
            Assert.That(FogRules.SegmentCrossesCylinder(new Vector3(-10, 1, 0), new Vector3(-4, 1, 0), foot, 3, 3.5f), Is.False, "Stops short of it.");
        }

        // ---------- the card assets ----------

        static T Effect<T>(string name) where T : Object => AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Data/Effects/" + name + ".asset");

        [Test]
        public void ElementCardAssetsCarryTheBoardsNumbersAndTheirPrefabs()
        {
            var tar = Effect<TarPuddleEffect>("Tar puddle");
            Assert.That(tar, Is.Not.Null);
            Assert.That((tar.radius.i, tar.radius.ii, tar.radius.iii, tar.speedMultiplier, tar.burnSeconds, tar.burnDamagePerSecond), Is.EqualTo((2f, 3f, 4f, .5f, 30f, 1f)));
            Assert.That(tar.puddlePrefab, Is.Not.Null);

            var rain = Effect<RainCloudEffect>("Rain cloud");
            Assert.That((rain.durationSeconds, rain.radius.i, rain.radius.ii, rain.radius.iii, rain.wetLingerSeconds), Is.EqualTo((15f, 3f, 4.5f, 6f, 30f)));
            Assert.That(rain.cloudPrefab, Is.Not.Null);

            var lightning = Effect<LightningEffect>("Lightning");
            Assert.That((lightning.damage.i, lightning.damage.ii, lightning.damage.iii, lightning.wetDamageMultiplier, lightning.chainTargets, lightning.chainRange),
                Is.EqualTo((3, 5, 8, 2f, 3, 4f)));
            Assert.That(lightning.strikeVfx, Is.Not.Null);
            Assert.That(lightning.arcPrefab, Is.Not.Null);
            var lightningCard = AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Game/Data/lightning.asset");
            Assert.That(lightningCard.purchasePrice, Is.EqualTo(150), "Confirmed: a copy costs 150 coins.");

            var fog = Effect<FogEffect>("Fog");
            Assert.That((fog.duration.i, fog.duration.ii, fog.duration.iii, fog.radius), Is.EqualTo((8f, 14f, 24f, 3f)));
            Assert.That(fog.fogPrefab, Is.Not.Null);

            var lure = Effect<LureEffect>("Lure");
            Assert.That((lure.duration.i, lure.duration.ii, lure.duration.iii, lure.radius), Is.EqualTo((6f, 12f, 20f, 10f)));
            Assert.That(lure.noisePrefab, Is.Not.Null);

            var attraction = Effect<AttractionEffect>("Attraction");
            Assert.That((attraction.impulse.i, attraction.impulse.ii, attraction.impulse.iii, attraction.radius), Is.EqualTo((6f, 9f, 12f, 3f)));
        }

        [Test]
        public void WorkshopShowsEachCardsMainNumber()
        {
            void Check(ICardStatLine line, string label, float i, float ii, float iii)
            {
                Assert.That(line.TryGetStat(out var l, out var v, out _), Is.True);
                Assert.That((l, v.i, v.ii, v.iii), Is.EqualTo((label, i, ii, iii)));
            }
            Check(Effect<TarPuddleEffect>("Tar puddle"), "Радиус", 2, 3, 4);
            Check(Effect<RainCloudEffect>("Rain cloud"), "Радиус", 3, 4.5f, 6);
            Check(Effect<LightningEffect>("Lightning"), "Урон", 3, 5, 8);
            Check(Effect<FogEffect>("Fog"), "Длительность", 8, 14, 24);
            Check(Effect<LureEffect>("Lure"), "Звук", 6, 12, 20);
            Check(Effect<AttractionEffect>("Attraction"), "Импульс", 6, 9, 12);
        }

        GameObject Make(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            created.Add(go);
            return go;
        }
    }
}
