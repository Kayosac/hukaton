using Hakaton.Computer;
using NUnit.Framework;

namespace Hakaton.Computer.Tests
{
    public sealed class PcNailFearTests
    {
        [Test]
        public void FirstSceneWaitsForTheStranger()
        {
            var save = new PcNailFearSave();
            Assert.AreEqual(-1, PcNailFearRules.Due(save, false, true, 10, 4));
            Assert.AreEqual(0, PcNailFearRules.Due(save, true, false, 0, 4));
        }

        [Test]
        public void SecondSceneNeedsHapayAndFourOutingsAfterTheFirst()
        {
            var save = new PcNailFearSave { shown = 1, journeysAtLast = 5 };
            Assert.AreEqual(-1, PcNailFearRules.Due(save, true, false, 20, 4), "no хаPAY yet");
            Assert.AreEqual(-1, PcNailFearRules.Due(save, true, true, 8, 4), "only three outings");
            Assert.AreEqual(1, PcNailFearRules.Due(save, true, true, 9, 4));
        }

        [Test]
        public void EachSceneOnlyOnce()
        {
            Assert.AreEqual(-1, PcNailFearRules.Due(new PcNailFearSave { shown = 2, journeysAtLast = 0 }, true, true, 99, 4));
        }

        [Test]
        public void OneInSixRoll()
        {
            Assert.IsTrue(PcNailFearRules.Comes(false, 1f / 6f, .1));
            Assert.IsFalse(PcNailFearRules.Comes(false, 1f / 6f, .2));
            Assert.IsTrue(PcNailFearRules.Comes(true, 0, .99));
        }
    }
}
