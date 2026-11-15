using System.Linq;
using Hakaton.Guide;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Computer.Tests
{
    /// <summary>Гвоздик's «Склад» part: its own tour, keys the guide can light, texts saved in the prefab.</summary>
    public sealed class PcGuideTests
    {
        const string PcPath = "Assets/Computer/Prefabs/BackroomPC.prefab";

        static void AssertStorageTour(GuideTour tour, string where)
        {
            Assert.That(tour, Is.Not.Null, where);
            Assert.That(tour.id, Is.EqualTo(PcGuide.Prefix + "storage"), where);
            Assert.That(tour.steps, Is.Not.Empty, where);
            foreach (var step in tour.steps)
            {
                Assert.That(step, Is.Not.Null, where);
                Assert.That(PcGuide.KnowsStorageKey(step.key), Is.True, $"{where}: the guide cannot light «{step.key}»");
                Assert.That(step.text, Is.Not.Null.And.Not.Empty, $"{where}: «{step.key}» says nothing");
                Assert.That(step.kind, Is.EqualTo(GuideStepKind.Read), $"{where}: «{step.key}»");
            }
            var keys = tour.steps.Select(s => s.key).ToArray();
            Assert.That(keys, Is.Unique, where);
            Assert.That(keys.First(), Is.EqualTo(PcGuide.StorageWindowKey), where);
            Assert.That(keys.Last(), Is.EqualTo("storage-close"), where);
            Assert.That(keys, Does.Contain("storage-empty").And.Contain("storage-list"), where + ": an empty and a full storeroom");
        }

        [Test]
        public void EveryStorageSpotIsAPartTheWindowKnows()
        {
            var parts = new[] { "icon", "totals", "sort", "header", "list", "empty", "pending", "status", "close" };
            foreach (var pair in PcGuide.StorageSpots)
                Assert.That(parts, Does.Contain(pair.Value), $"«{pair.Key}» lights «{pair.Value}»");
            Assert.That(PcGuide.KnowsStorageKey(PcGuide.StorageIconKey), Is.True);
            Assert.That(PcGuide.KnowsStorageKey("counts"), Is.False, "the cards' keys are not the storeroom's");
        }

        [Test]
        public void TheStorageTourHasItsOwnIdAndKnownKeysByDefault()
        {
            var go = new GameObject("guide");
            try { AssertStorageTour(go.AddComponent<PcGuide>().storageTour, "code defaults"); }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ThePrefabSavesTheStorageTourAndTheDesktopIconStep()
        {
            var prefab = PrefabUtility.LoadPrefabContents(PcPath);
            try
            {
                var guide = prefab.GetComponentInChildren<PcGuide>(true);
                Assert.That(guide, Is.Not.Null);
                Assert.That(guide.computer, Is.Not.Null);
                Assert.That(guide.computer.storage, Is.Not.Null, "«Склад» must be built into the PC");
                AssertStorageTour(guide.storageTour, "BackroomPC.prefab");

                var ids = new[] { guide.desktopTour.id, guide.cardsTour.id, guide.upgradesTour.id, guide.storageTour.id };
                Assert.That(ids, Is.Unique, "a new part needs a new id to be shown to those who saw the rest");
                Assert.That(ids.All(id => id.StartsWith(PcGuide.Prefix)), Is.True, "the replay forgets the PC's tours by prefix");

                var desktop = guide.desktopTour.steps.Select(s => s.key).ToList();
                int icon = desktop.IndexOf(PcGuide.StorageIconKey);
                Assert.That(icon, Is.GreaterThan(desktop.IndexOf("icons")), "the «Склад» icon comes after the icons");
                Assert.That(desktop.Last(), Is.EqualTo("open-cards"), "the desktop part still ends by opening «Карты»");
                Assert.That(guide.desktopTour.steps[icon].text, Is.Not.Empty);
                Assert.That(guide.desktopTour.steps[icon].kind, Is.EqualTo(GuideStepKind.Read));
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }
    }
}
