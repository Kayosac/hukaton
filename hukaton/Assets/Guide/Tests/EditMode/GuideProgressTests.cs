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
