using System.Collections.Generic;
using System.Linq;
using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEngine;

namespace Hakaton.Game.Tests
{
    public sealed class OutingMapTests
    {
        static OutingMap NewMap(out GameObject owner)
        {
            owner = new GameObject("map");
            var map = owner.AddComponent<OutingMap>();
            map.sites = new[]
            {
                new OutingMap.Site { id = "root", place = OutingPlace.Ready, unlock = OutingMapUnlock.Always, home = new Vector2(.2f, .3f) },
                new OutingMap.Site { id = "child", place = OutingPlace.Endless, parentId = "root", unlock = OutingMapUnlock.RevenueGoal },
                new OutingMap.Site { id = "grandchild", place = OutingPlace.Endless, parentId = "child", unlock = OutingMapUnlock.RevenueGoal },
            };
            map.Restore(new OutingMapSave { seed = 1234 });
            return map;
        }

        [Test]
        public void NewPointsGrowOnlyWhenUnlockedAndOnlyFromOpenedParents()
        {
            var map = NewMap(out var owner);
            bool goal = false;
            int changes = 0;
            map.Changed += () => changes++;
            Assert.That(map.Grow(s => s.unlock == OutingMapUnlock.Always || goal), Is.EqualTo(new[] { "root" }));
            Assert.That(map.Opened("root").x, Is.EqualTo(.2f).Within(1e-5));
            Assert.That(map.Opened("root").seen, Is.False, "A point appears new: it burns until clicked.");
            Assert.That(map.Grow(s => s.unlock == OutingMapUnlock.Always || goal), Is.Empty);
            goal = true;
            Assert.That(map.Grow(s => true), Is.EqualTo(new[] { "child", "grandchild" }), "A chain opens parent first.");
            Assert.That(changes, Is.EqualTo(2));
            Object.DestroyImmediate(owner);
        }

        [Test]
        public void NewPointIsNearItsParentInsideThePaperAndAwayFromOthers()
        {
            var rules = OutingMapLayout.Rules.Default(4f / 3f);
            for (int seed = 0; seed < 200; seed++)
            {
                var parent = new Vector2(.2f, .3f);
                var taken = new List<Vector2> { parent, new Vector2(.45f, .35f) };
                var p = OutingMapLayout.PlaceNear(parent, taken, seed, rules);
                Assert.That(rules.bounds.Contains(p), Is.True, "seed " + seed);
                float step = OutingMapLayout.Distance(p, parent, rules.aspect);
                Assert.That(step, Is.InRange(rules.minStep - 1e-4f, rules.maxStep + 1e-4f), "seed " + seed);
                Assert.That(taken.Min(t => OutingMapLayout.Distance(p, t, rules.aspect)), Is.GreaterThanOrEqualTo(rules.clearance - 1e-4f), "seed " + seed);
            }
        }

        [Test]
        public void TheSameSaveGrowsTheSameMapAndKeepsWhatWasSeen()
        {
            var a = NewMap(out var ownerA);
            a.Grow(s => true);
            a.Opened("root").seen = true;
            var save = JsonUtility.FromJson<OutingMapSave>(JsonUtility.ToJson(a.Save()));
            var b = NewMap(out var ownerB);
            b.Restore(save);
            Assert.That(b.OpenedSites.Select(s => s.id), Is.EqualTo(new[] { "root", "child", "grandchild" }));
            Assert.That(b.Opened("root").seen, Is.True);
            Assert.That(b.Opened("child").x, Is.EqualTo(a.Opened("child").x));
            // A new map from the same seed places the child the same way.
            var c = NewMap(out var ownerC);
            c.Grow(s => true);
            Assert.That(c.Opened("child").x, Is.EqualTo(a.Opened("child").x));
            Assert.That(c.Opened("child").y, Is.EqualTo(a.Opened("child").y));
            Object.DestroyImmediate(ownerA); Object.DestroyImmediate(ownerB); Object.DestroyImmediate(ownerC);
        }

        [Test]
        public void OldSavesWithoutAMapAndUnknownPointsAreTolerated()
        {
            var map = NewMap(out var owner);
            map.Restore(null);
            Assert.That(map.OpenedSites, Is.Empty);
            var save = new OutingMapSave { seed = 1, sites = { new OutingMapSiteSave { id = "gone" }, new OutingMapSiteSave { id = "root", seen = true } } };
            map.Restore(save);
            Assert.That(map.OpenedSites.Select(s => s.id), Is.EqualTo(new[] { "root" }));
            Object.DestroyImmediate(owner);
        }

        [Test]
        public void RoutesAreDashedFromParentToChildAndStopShortOfTheIcons()
        {
            var from = new Vector2(0, 0);
            var to = new Vector2(400, 0);
            var dashes = OutingMapLayout.DashPoints(from, to, 30, 74, 7);
            Assert.That(dashes.Count, Is.InRange(7, 12));
            Assert.That(Vector2.Distance(dashes[0], from), Is.GreaterThanOrEqualTo(70));
            Assert.That(Vector2.Distance(dashes[dashes.Count - 1], to), Is.GreaterThanOrEqualTo(70));
            Assert.That(OutingMapLayout.DashPoints(from, new Vector2(100, 0), 30, 74, 7), Is.Empty);
        }
    }
}
