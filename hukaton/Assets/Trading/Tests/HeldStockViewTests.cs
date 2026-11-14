using System.Collections.Generic;
using Hakaton.Trading.Unity;
using NUnit.Framework;
using UnityEngine;

namespace Hakaton.Trading.Tests
{
    /// <summary>The hand in the shop: up only near the display, with hysteresis, sliding in and out of view.</summary>
    public sealed class HeldStockViewTests
    {
        [Test]
        public void DistanceIsAcrossTheFloorToTheBox()
        {
            var slot = new Bounds(new Vector3(0, 1, 0), new Vector3(1, .2f, 1));
            Assert.That(HeldStockView.HorizontalDistance(new Vector3(0, 5, 0), slot), Is.EqualTo(0), "Height is ignored.");
            Assert.That(HeldStockView.HorizontalDistance(new Vector3(2.5f, 1.6f, 0), slot), Is.EqualTo(2).Within(1e-5f));
            Assert.That(HeldStockView.HorizontalDistance(new Vector3(3.5f, 0, -4.5f), slot), Is.EqualTo(5).Within(1e-5f));
        }

        [Test]
        public void ComesNearWithinShowAndLeavesOnlyPastHide()
        {
            const float show = 1.4f, hide = 1.7f;
            Assert.That(HeldStockView.NextNear(false, 1.55f, show, hide), Is.False, "Between the two edges it does not come up…");
            Assert.That(HeldStockView.NextNear(false, 1.3f, show, hide), Is.True);
            Assert.That(HeldStockView.NextNear(true, 1.65f, show, hide), Is.True, "…and once up it stays up there.");
            Assert.That(HeldStockView.NextNear(true, 1.8f, show, hide), Is.False);
            Assert.That(HeldStockView.NextNear(true, 1.5f, show, 1), Is.False, "Hide closer than show acts as show.");
        }

        [Test]
        public void HandIsDownAtTheCounter()
        {
            // In the shop scene the counter pose is 1.76 m from the display, the eye standing there 1.9 m.
            var view = new GameObject("Hand").AddComponent<HeldStockView>();
            try
            {
                Assert.That(view.hideBeyond, Is.GreaterThan(view.showWithin));
                foreach (bool wasNear in new[] { false, true })
                    Assert.That(HeldStockView.NextNear(wasNear, 1.76f, view.showWithin, view.hideBeyond), Is.False);
            }
            finally { Object.DestroyImmediate(view.gameObject); }
        }

        [Test]
        public void SlidesInAboutAQuarterSecondEitherWay()
        {
            float raise = 0;
            for (int frame = 0; frame < 8; frame++) raise = HeldStockView.Slide(raise, true, 1 / 60f, .28f);
            Assert.That(raise, Is.GreaterThan(0).And.LessThan(1), "Not a pop.");
            for (int frame = 0; frame < 20; frame++) raise = HeldStockView.Slide(raise, true, 1 / 60f, .28f);
            Assert.That(raise, Is.EqualTo(1));
            raise = HeldStockView.Slide(raise, false, .14f, .28f);
            Assert.That(raise, Is.EqualTo(.5f).Within(1e-5f));
            Assert.That(HeldStockView.Slide(raise, false, 1, .28f), Is.EqualTo(0));
            Assert.That(HeldStockView.Ease(0), Is.EqualTo(0));
            Assert.That(HeldStockView.Ease(1), Is.EqualTo(1));
            Assert.That(HeldStockView.Ease(.5f), Is.GreaterThan(.5f), "Quick off the bottom edge, settling into the corner.");
        }

        [Test]
        public void ModelDropsWhollyBelowTheView()
        {
            // A box framed in the lower-right corner of a 60-degree view.
            var points = new List<Vector3>();
            for (int corner = 0; corner < 8; corner++)
                points.Add(new Vector3((corner & 1) == 0 ? -.1f : .1f, (corner & 2) == 0 ? -.1f : .1f, (corner & 4) == 0 ? -.1f : .1f));
            float tanV = Mathf.Tan(30 * Mathf.Deg2Rad), tanH = tanV * 16 / 9f;
            HeldStockView.Fit(points, tanH, tanV, new Vector2(.32f, .46f), new Vector2(.025f, .015f), out float scale, out var centre, out _);
            float drop = HeldStockView.DropBelowView(points, scale, centre.y, tanV);
            Assert.That(drop, Is.GreaterThan(0));
            foreach (var p in points)
            {
                float w = 1 + scale * p.z;
                float viewportY = (centre.y - drop + scale * p.y) / (w * 2 * tanV) + .5f;
                Assert.That(viewportY, Is.LessThan(0), "Every point is past the bottom edge when down.");
            }
        }
    }
}
