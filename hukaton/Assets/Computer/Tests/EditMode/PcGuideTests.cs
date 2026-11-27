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

        /// <summary>Balance stage 10: the PC's tours in parts as saved in the prefab.</summary>
        [Test]
        public void ThePcGuideComesInShortPartsAndTheMergeIsItsOwn()
        {
            var prefab = PrefabUtility.LoadPrefabContents(PcPath);
            try
            {
                var guide = prefab.GetComponentInChildren<PcGuide>(true);
                string[] Keys(GuideTour t) => t.steps.Select(s => s.key).ToArray();
                Assert.That(Keys(guide.desktopTour), Is.EqualTo(new[] { "hello", "icons", PcGuide.StorageIconKey }));
                Assert.That(Keys(guide.cardsTour), Is.EqualTo(new[] { "counts", "faded", "toggle", "toggle-back", "hover", "sort", "status", "start", "close" }));
                Assert.That(guide.mergeTour.id, Is.EqualTo("pc.merge"));
                Assert.That(Keys(guide.mergeTour), Is.EqualTo(new[] { "merge" }));
                Assert.That(guide.mergeTour.steps[0].kind, Is.EqualTo(GuideStepKind.Watch));
                Assert.That(Keys(guide.upgradesTour), Is.EqualTo(new[] { "rows", "bar", "price", "tray", "buy" }));
                var start = guide.cardsTour.steps.First(s => s.key == "start").text;
                Assert.That(start, Does.StartWith("Через «Пуск» открываются те же программы."), "away from the icons it names them");
                foreach (var tour in new[] { guide.desktopTour, guide.cardsTour, guide.mergeTour, guide.upgradesTour })
                    foreach (var step in tour.steps)
                        Assert.That(step.key, Is.Not.EqualTo("deck").And.Not.EqualTo("deck-row"), "no step points at «Размер колоды»");
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }

        /// <summary>Balance stage 10: records from before the parts — a whole tour seen, one left half-way, none.</summary>
        [Test]
        public void OlderRecordsKeepWhatWasSeenOfTheParts()
        {
            GuideProgress Old(string json)
            {
                var progress = new GuideProgress();
                progress.Restore(JsonUtility.FromJson<GuideSave>(json), PcGuide.UpgradeOlderRecord);
                return progress;
            }
            // Seen whole (or skipped with Esc, which marked them all).
            var whole = Old("{\"done\":[\"pc.cards\",\"pc.desktop\",\"pc.storage\",\"pc.upgrades\"]}");
            foreach (var id in new[] { "pc.desktop", "pc.cards", "pc.merge", "pc.upgrades", "pc.storage" })
                Assert.That(whole.IsDone(id), Is.True, id);
            // Left half-way: the desktop at «tray» (its three steps are behind), «Карты» at «sort» (the merge is behind).
            var half = Old("{\"done\":[],\"started\":[\"pc.desktop\",\"pc.cards\"],\"steps\":[4,6]}");
            Assert.That(half.IsDone("pc.desktop"), Is.True, "the desktop part's steps had gone by");
            Assert.That(half.IsDone("pc.merge"), Is.True, "the merge had gone by");
            Assert.That(half.IsDone("pc.cards"), Is.False);
            Assert.That(half.StepOf("pc.cards"), Is.EqualTo(5), "on at «sort», one place up without the merge");
            // Left before the merge: it is still to come; the desktop at «icons» carries on there.
            var early = Old("{\"done\":[],\"started\":[\"pc.desktop\",\"pc.cards\"],\"steps\":[1,4]}");
            Assert.That(early.IsDone("pc.merge") || early.IsDone("pc.desktop") || early.IsDone("pc.cards"), Is.False);
            Assert.That(early.StepOf("pc.desktop"), Is.EqualTo(1));
            Assert.That(early.StepOf("pc.cards"), Is.EqualTo(4), "the merge was the step at 4: «hover» stands there now");
            // Nothing seen.
            var none = Old("{\"done\":[\"brazier\"]}");
            Assert.That(none.IsDone("pc.desktop") || none.IsDone("pc.cards") || none.IsDone("pc.merge"), Is.False);
            Assert.That(none.IsDone("brazier"), Is.True);
            // A current record with «Карты» seen but not the merge stays so.
            var current = new GuideProgress();
            current.Finish("pc.cards");
            var again = new GuideProgress();
            again.Restore(JsonUtility.FromJson<GuideSave>(JsonUtility.ToJson(current.Save())), PcGuide.UpgradeOlderRecord);
            Assert.That(again.IsDone("pc.merge"), Is.False, "the merge comes when two copies do");
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

                var ids = new[] { guide.desktopTour.id, guide.cardsTour.id, guide.mergeTour.id, guide.upgradesTour.id, guide.storageTour.id };
                Assert.That(ids, Is.Unique, "a new part needs a new id to be shown to those who saw the rest");
                Assert.That(ids.All(id => id.StartsWith(PcGuide.Prefix)), Is.True, "the replay forgets the PC's tours by prefix");

                var desktop = guide.desktopTour.steps.Select(s => s.key).ToList();
                int icon = desktop.IndexOf(PcGuide.StorageIconKey);
                Assert.That(icon, Is.GreaterThan(desktop.IndexOf("icons")), "the «Склад» icon comes after the icons");
                Assert.That(desktop.Count, Is.LessThanOrEqualTo(3), "balance stage 10: the desktop part is a short look, three steps at most");
                Assert.That(guide.desktopTour.steps[icon].text, Is.Not.Empty);
                Assert.That(guide.desktopTour.steps[icon].kind, Is.EqualTo(GuideStepKind.Read));
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }
    }
}
