using NUnit.Framework;

namespace Hakaton.Game.Tests
{
    /// <summary>«Счётчик FPS»: real frames over real seconds, read out once per window.</summary>
    public sealed class FpsCounterTests
    {
        [Test]
        public void TheAverageIsFramesOverSecondsRounded()
        {
            Assert.That(FpsCounter.Average(30, .5f), Is.EqualTo(60));
            Assert.That(FpsCounter.Average(72, .5f), Is.EqualTo(144));
            Assert.That(FpsCounter.Average(29, .5f), Is.EqualTo(58));
            Assert.That(FpsCounter.Average(1, 1f / 30), Is.EqualTo(30));
            Assert.That(FpsCounter.Average(0, .5f), Is.Zero, "An empty window");
            Assert.That(FpsCounter.Average(10, 0), Is.Zero, "No time has passed");
        }

        [Test]
        public void ASteadyFrameRateIsReadOutOncePerWindow()
        {
            int frames = 0, readings = 0;
            float seconds = 0;
            for (int i = 0; i < 600; i++) // ten seconds at 60 FPS, half-second windows
                if (FpsCounter.Sample(ref frames, ref seconds, 1f / 60, .5f, out int fps))
                {
                    readings++;
                    Assert.That(fps, Is.EqualTo(60));
                }
            Assert.That(readings, Is.InRange(19, 20));
        }

        [Test]
        public void AnUnevenWindowIsAveragedNotSampled()
        {
            // Half a second at 30 FPS, then 90 FPS: 60 frames in the one-second window, whatever the last frame took.
            int frames = 0, readings = 0, reading = -1;
            float seconds = 0;
            for (int i = 0; i < 70; i++)
                if (FpsCounter.Sample(ref frames, ref seconds, i < 15 ? 1f / 30 : 1f / 90, 1, out int fps))
                {
                    readings++;
                    reading = fps;
                }
            Assert.That(readings, Is.EqualTo(1));
            Assert.That(reading, Is.EqualTo(60));
        }

        [Test]
        public void AHitchLongerThanTheWindowIsReadOutAtOnceAndStartsANewWindow()
        {
            int frames = 0;
            float seconds = 0;
            Assert.That(FpsCounter.Sample(ref frames, ref seconds, 1, .5f, out int fps), Is.True);
            Assert.That(fps, Is.EqualTo(1));
            Assert.That(frames, Is.Zero);
            Assert.That(seconds, Is.Zero);
            Assert.That(FpsCounter.Sample(ref frames, ref seconds, -1, .5f, out _), Is.False, "A negative delta adds no time");
            Assert.That(seconds, Is.Zero);
        }

        [Test]
        public void TheReadoutIsCachedPerValue()
        {
            Assert.That(FpsCounter.Readout(60), Is.EqualTo("60 FPS"));
            Assert.That(FpsCounter.Readout(144), Is.SameAs(FpsCounter.Readout(144)), "Steady readings allocate nothing");
            Assert.That(FpsCounter.Readout(-3), Is.EqualTo("0 FPS"));
            Assert.That(FpsCounter.Readout(1500), Is.EqualTo("1500 FPS"));
        }
    }
}
