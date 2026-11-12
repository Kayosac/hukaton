using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Hakaton.Computer.Tests
{
    public sealed class PcMergeShowTests
    {
        static PcMergeFx Settings()
        {
            var fx = ScriptableObject.CreateInstance<PcMergeFx>();
            fx.ResetToDefaults();
            return fx;
        }

        [Test]
        public void EachResultLevelPicksTheHighestEntryAtOrBelowIt()
        {
            var fx = Settings();
            try
            {
                Assert.That(fx.For(2).resultLevel, Is.EqualTo(2));
                Assert.That(fx.For(3).resultLevel, Is.EqualTo(3));
                Assert.That(fx.For(7).resultLevel, Is.EqualTo(3), "above the last entry: the biggest show");
                Assert.That(fx.For(1).resultLevel, Is.EqualTo(2), "below every entry: the smallest show");
                Assert.That(PcMergeFx.Pick(new PcMergeFx.Level[0], 2), Is.Null);
                Assert.That(PcMergeFx.Pick(null, 2), Is.Null);
                Assert.That(PcMergeFx.Pick(new[] { null, PcMergeFx.Level.Defaults(3) }, 2).resultLevel, Is.EqualTo(3));
            }
            finally { Object.DestroyImmediate(fx); }
        }

        [Test]
        public void LevelThreeIsTheBiggerShow()
        {
            PcMergeFx.Level two = PcMergeFx.Level.Defaults(2), three = PcMergeFx.Level.Defaults(3);
            Assert.That(three.Timeline.End, Is.GreaterThan(two.Timeline.End));
            Assert.That(three.Timeline.GrindSeconds, Is.GreaterThan(two.Timeline.GrindSeconds));
            Assert.That(three.jitterPixels.y, Is.GreaterThan(two.jitterPixels.y));
            Assert.That(three.windowShakePixels, Is.GreaterThan(two.windowShakePixels));
            Assert.That(three.impactFlash, Is.GreaterThan(two.impactFlash));
            Assert.That(three.markHopPixels, Is.GreaterThan(two.markHopPixels));
            Assert.That(two.Timeline.End, Is.InRange(1f, 1.7f), "a merge reads in about a second and a half");
            Assert.That(three.Timeline.End, Is.InRange(1f, 1.7f));
        }

        [Test]
        public void TheImpactIsAPunchNotAGlare()
        {
            foreach (var level in new[] { PcMergeFx.Level.Defaults(2), PcMergeFx.Level.Defaults(3) })
            {
                Assert.That(level.crtFlashSeconds, Is.LessThanOrEqualTo(.1f), "the tube's white lasts two or three frames");
                Assert.That(level.impactFlash, Is.LessThanOrEqualTo(.15f), "the card carries the flash, not the whole screen");
                Assert.That(level.holdSeconds + level.cardFlashFadeSeconds, Is.LessThanOrEqualTo(.25f), "the card is white only briefly");
                Assert.That(level.spotlight, Is.InRange(.5f, .85f), "the window darkens so the cards and the effects pop");
                Assert.That(level.spotlightInSeconds, Is.LessThanOrEqualTo(level.flySeconds), "dark before the cards meet");
            }
            Assert.That(PcMergeFx.Level.Defaults(3).spotlight, Is.GreaterThan(PcMergeFx.Level.Defaults(2).spotlight));
        }

        [Test]
        public void TheBeatsFollowEachOther()
        {
            var timeline = new PcMergeTimeline(.2f, .4f, .06f, .5f);
            Assert.That(timeline.At(PcMergeFx.Beat.Fly), Is.EqualTo(0));
            Assert.That(timeline.At(PcMergeFx.Beat.Grind), Is.EqualTo(.2f).Within(1e-5f));
            Assert.That(timeline.At(PcMergeFx.Beat.Impact), Is.EqualTo(.6f).Within(1e-5f));
            Assert.That(timeline.At(PcMergeFx.Beat.Reveal), Is.EqualTo(.66f).Within(1e-5f));
            Assert.That(timeline.End, Is.EqualTo(1.16f).Within(1e-5f));
            Assert.That(timeline.GrindSeconds, Is.EqualTo(.4f).Within(1e-5f));

            var negative = new PcMergeTimeline(-1, -1, -1, -1);
            Assert.That(negative.End, Is.EqualTo(0), "negative durations count as none");
        }

        [Test]
        public void TheDashWindsUpThenLandsExactlyOnTheTarget()
        {
            Vector2 from = new Vector2(30, 20), to = new Vector2(0, 0);
            Assert.That(PcMergeMath.Fly(from, to, 0, 6, 1.5f, 8), Is.EqualTo(from));
            Assert.That(PcMergeMath.Fly(from, to, 1, 6, 1.5f, 8), Is.EqualTo(to));
            Assert.That(PcMergeMath.Fly(from, to, 2, 6, 1.5f, 8), Is.EqualTo(to), "clamped past the end");

            var windUp = PcMergeMath.Fly(from, to, PcMergeMath.WindUp, 6, 1.5f, 8);
            Assert.That(Vector2.Distance(windUp, to), Is.EqualTo(Vector2.Distance(from, to) + 6).Within(.01f), "pulls back before the dash");

            bool overshot = false;
            for (float u = PcMergeMath.WindUp; u < 1; u += .01f)
                overshot |= Vector2.Dot(PcMergeMath.Fly(from, to, u, 6, 1.5f, 0) - to, from - to) < -.5f;
            Assert.That(overshot, Is.True, "the dash overshoots the target before settling");

            Assert.That(PcMergeMath.Fly(to, to, 1, 6, 1.5f, 8), Is.EqualTo(to), "a drop right on the target still lands");
        }

        [Test]
        public void JitterIsWholePixelsWithinItsAmplitude()
        {
            var seen = new HashSet<Vector2Int>();
            for (int tick = 0; tick < 300; tick++)
            {
                var j = PcMergeMath.Jitter(1234, tick, 3);
                Assert.That(Mathf.Abs(j.x), Is.LessThanOrEqualTo(3));
                Assert.That(Mathf.Abs(j.y), Is.LessThanOrEqualTo(3));
                Assert.That(PcMergeMath.Jitter(1234, tick, 3), Is.EqualTo(j), "deterministic");
                seen.Add(j);
            }
            Assert.That(seen.Count, Is.GreaterThan(20), "it really shakes");
            Assert.That(PcMergeMath.Jitter(1234, 5, 0), Is.EqualTo(Vector2Int.zero));
            Assert.That(PcMergeMath.Jitter(-77, -5, 1.4f).x, Is.InRange(-1, 1), "any seed and tick");
        }

        [Test]
        public void TheWindowShakeFadesOutAndStops()
        {
            for (float t = 0; t < .3f; t += .01f)
            {
                var s = PcMergeMath.Shake(9, t, 4, .3f);
                Assert.That(Mathf.Abs(s.x), Is.LessThanOrEqualTo(4));
                Assert.That(Mathf.Abs(s.y), Is.LessThanOrEqualTo(4));
            }
            Assert.That(PcMergeMath.Shake(9, .3f, 4, .3f), Is.EqualTo(Vector2Int.zero));
            Assert.That(PcMergeMath.Shake(9, -.1f, 4, .3f), Is.EqualTo(Vector2Int.zero));
            Assert.That(PcMergeMath.Shake(9, .29f, 4, .3f).sqrMagnitude, Is.LessThanOrEqualTo(2), "almost still at the end");
        }
    }
}
