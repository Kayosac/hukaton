using Hakaton.Guide;
using NUnit.Framework;
using UnityEngine;

namespace Hakaton.Guide.Tests
{
    public sealed class GuideProgressTests
    {
        [Test]
        public void AFinishedTourIsSavedAndStaysFinishedAfterLoading()
        {
            var progress = new GuideProgress();
            progress.SetStep("pc.desktop", 3);
            progress.Finish("pc.desktop");
            var loaded = new GuideProgress();
            loaded.Restore(JsonUtility.FromJson<GuideSave>(JsonUtility.ToJson(progress.Save())));
            Assert.IsTrue(loaded.IsDone("pc.desktop"));
            Assert.AreEqual(0, loaded.StepOf("pc.desktop"));
        }

        [Test]
        public void AHalfShownTourCarriesOnFromTheStepItStoppedAt()
        {
            var progress = new GuideProgress();
            progress.SetStep("brazier", 4);
            var loaded = new GuideProgress();
            loaded.Restore(JsonUtility.FromJson<GuideSave>(JsonUtility.ToJson(progress.Save())));
            Assert.IsFalse(loaded.IsDone("brazier"));
            Assert.AreEqual(4, loaded.StepOf("brazier"));
        }

        [Test]
        public void ASaveFromBeforeTheGuidesShowsEveryTour()
        {
            var progress = new GuideProgress();
            progress.Finish("brazier");
            progress.Restore(null);
            Assert.IsFalse(progress.IsDone("brazier"));
        }

        [Test]
        public void ForgettingByPrefixBringsBackOnlyThoseTours()
        {
            var progress = new GuideProgress();
            progress.Finish("pc.desktop");
            progress.Finish("pc.cards");
            progress.Finish("brazier");
            int changes = 0;
            progress.Changed += () => changes++;
            progress.Forget("pc.");
            Assert.IsFalse(progress.IsDone("pc.desktop"));
            Assert.IsFalse(progress.IsDone("pc.cards"));
            Assert.IsTrue(progress.IsDone("brazier"));
            Assert.AreEqual(1, changes);
            progress.Forget();
            Assert.IsFalse(progress.IsDone("brazier"));
        }

        /// <summary>Balance stage 10: an older record is handed to the upgrade once, a current one is not.</summary>
        [Test]
        public void AnOlderRecordIsUpgradedOnLoadAndANewOneIsNot()
        {
            var progress = new GuideProgress();
            progress.Finish("pc.cards");
            var save = progress.Save();
            Assert.AreEqual(GuideProgress.CurrentVersion, save.version, "saved in the current layout");
            int calls = 0;
            var loaded = new GuideProgress();
            loaded.Restore(JsonUtility.FromJson<GuideSave>(JsonUtility.ToJson(save)), (p, v) => calls++);
            Assert.AreEqual(0, calls, "a current record is taken as it is");
            var older = JsonUtility.FromJson<GuideSave>("{\"done\":[\"pc.cards\"],\"started\":[],\"steps\":[]}");
            Assert.AreEqual(0, older.version, "a record from before the parts reads as version 0");
            loaded.Restore(older, (p, v) => { calls++; Assert.AreEqual(0, v); p.Finish("pc.merge"); });
            Assert.AreEqual(1, calls);
            Assert.IsTrue(loaded.IsDone("pc.merge"));
            loaded.Restore(null, (p, v) => calls++);
            Assert.AreEqual(1, calls, "no record at all: nothing to upgrade");
        }

        [Test]
        public void AFinishedTourDoesNotRecordSteps()
        {
            var progress = new GuideProgress();
            progress.Finish("pc.upgrades");
            progress.SetStep("pc.upgrades", 2);
            Assert.AreEqual(0, progress.StepOf("pc.upgrades"));
        }
    }
}
