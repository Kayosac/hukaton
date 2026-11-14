using Hakaton.Loot;
using Hakaton.Trading.Unity;
using NUnit.Framework;
using UnityEngine;

namespace Hakaton.Trading.Tests
{
    /// <summary>The close look at a displayed item: its framing, zoom and spin arithmetic, and what can be looked at.</summary>
    public sealed class DisplayItemInspectorTests
    {
        static readonly float Tan60 = Mathf.Tan(30 * Mathf.Deg2Rad);

        [Test]
        public void TheFittedItemSpansTheAskedShareOfTheViewAtAnyDistanceAndSize()
        {
            foreach (float depth in new[] { .12f, .3f, .5f })
                foreach (float radius in new[] { .05f, .2f, 1.5f })
                {
                    float scale = InspectionFraming.FitScale(radius, depth, Tan60, .52f);
                    float viewHeight = 2 * depth * Tan60;
                    Assert.That(2 * radius * scale / viewHeight, Is.EqualTo(.52f).Within(1e-4f));
                    Assert.That(radius * scale / depth, Is.EqualTo(InspectionFraming.RadiusRatio(Tan60, .52f)).Within(1e-4f));
                }
            Assert.That(InspectionFraming.FitScale(0, .5f, Tan60, .5f), Is.EqualTo(1), "A degenerate item keeps its size.");
        }

        [Test]
        public void TheItemIsHeldAtThePreferredDistanceUnlessSomethingInViewIsCloser()
        {
            float ratio = InspectionFraming.RadiusRatio(Tan60, .52f);
            Assert.That(InspectionFraming.HoldDepth(.5f, 10, 1.3f, ratio, .1f), Is.EqualTo(.5f), "Open view: the preferred distance.");
            float near = InspectionFraming.HoldDepth(.5f, .4f, 1.3f, ratio, .1f);
            Assert.That(near, Is.LessThan(.5f));
            Assert.That(InspectionFraming.BackdropDepth(near, 1.3f, ratio), Is.LessThan(.4f),
                "Standing against the shelf, the backdrop still stands in front of it.");
            Assert.That(InspectionFraming.HoldDepth(.5f, .01f, 1.3f, ratio, .1f), Is.EqualTo(.1f), "Never nearer than the minimum.");
        }

        [Test]
        public void TheBackdropStandsBehindTheItemsFarSideAtTheFarthestZoom()
        {
            float ratio = InspectionFraming.RadiusRatio(Tan60, .52f);
            float depth = .3f;
            float farSide = depth * 1.3f + ratio * depth;
            Assert.That(InspectionFraming.BackdropDepth(depth, 1.3f, ratio), Is.GreaterThan(farSide));
        }

        [Test]
        public void TheWheelBringsTheItemCloserWithinItsLimits()
        {
            float zoom = 1;
            zoom = InspectionFraming.StepZoom(zoom, 120, .9f, .62f, 1.3f);
            Assert.That(zoom, Is.EqualTo(.9f).Within(1e-5f), "Up: closer.");
            zoom = InspectionFraming.StepZoom(zoom, -1, .9f, .62f, 1.3f);
            Assert.That(zoom, Is.EqualTo(1).Within(1e-5f), "Down: back again.");
            Assert.That(InspectionFraming.StepZoom(1, 0, .9f, .62f, 1.3f), Is.EqualTo(1), "No scroll, no change.");
            for (int i = 0; i < 30; i++) zoom = InspectionFraming.StepZoom(zoom, 1, .9f, .62f, 1.3f);
            Assert.That(zoom, Is.EqualTo(.62f));
            for (int i = 0; i < 30; i++) zoom = InspectionFraming.StepZoom(zoom, -1, .9f, .62f, 1.3f);
            Assert.That(zoom, Is.EqualTo(1.3f));
        }

        [Test]
        public void TheNearestZoomKeepsTheItemPastTheNearPlane()
        {
            float ratio = InspectionFraming.RadiusRatio(Tan60, .52f);
            float depth = .1f, nearPlane = .03f;
            float nearest = InspectionFraming.NearestZoom(.62f, ratio, (nearPlane + .01f) / depth);
            Assert.That(depth * nearest - ratio * depth, Is.GreaterThanOrEqualTo(nearPlane + .01f - 1e-5f));
            Assert.That(InspectionFraming.NearestZoom(.62f, ratio, .001f), Is.EqualTo(.62f), "Far from the near plane: the asked limit.");
        }

        [Test]
        public void TheSpinFollowsTheMouseSmoothlyAndCoastsAfterAFlick()
        {
            const float dt = 1 / 60f;
            var spin = Vector2.zero;
            spin = InspectionFraming.Spin(spin, new Vector2(2, 0), dt, 24, 4.5f, 900);
            Assert.That(spin.x, Is.GreaterThan(0).And.LessThan(120), "Catches up with the mouse, not at once.");
            for (int i = 0; i < 60; i++) spin = InspectionFraming.Spin(spin, new Vector2(2, 0), dt, 24, 4.5f, 900);
            Assert.That(spin.x, Is.EqualTo(120).Within(1), "A steady drag: the item turns with the mouse.");
            var flicked = InspectionFraming.Spin(spin, Vector2.zero, dt, 24, 4.5f, 900);
            Assert.That(flicked.x, Is.GreaterThan(spin.x * .9f), "Let go: it keeps turning a little.");
            for (int i = 0; i < 240; i++) flicked = InspectionFraming.Spin(flicked, Vector2.zero, dt, 24, 4.5f, 900);
            Assert.That(flicked, Is.EqualTo(Vector2.zero), "And comes to rest.");
            Assert.That(InspectionFraming.Spin(Vector2.zero, new Vector2(500, 0), dt, 1000, 4.5f, 900).magnitude, Is.LessThanOrEqualTo(900.001f));
            Assert.That(InspectionFraming.Spin(new Vector2(5, 5), Vector2.one, 0, 24, 4.5f, 900), Is.EqualTo(new Vector2(5, 5)));
        }

        [Test]
        public void TheEasesRunFromStartToEnd()
        {
            Assert.That(InspectionFraming.Smoother(0), Is.EqualTo(0));
            Assert.That(InspectionFraming.Smoother(1), Is.EqualTo(1));
            Assert.That(InspectionFraming.Smoother(.5f), Is.EqualTo(.5f).Within(1e-5f));
            Assert.That(InspectionFraming.BackOut(0, 1.1f), Is.EqualTo(0).Within(1e-5f));
            Assert.That(InspectionFraming.BackOut(1, 1.1f), Is.EqualTo(1).Within(1e-5f));
            float peak = 0;
            for (float t = 0; t <= 1; t += .01f) peak = Mathf.Max(peak, InspectionFraming.BackOut(t, 1.1f));
            Assert.That(peak, Is.GreaterThan(1).And.LessThan(1.1f), "A little past its mark, then settles.");
        }

        [Test]
        public void ModelsAndDrawingsCanBeLookedAtButNotEmptyThings()
        {
            var model = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var texture = new Texture2D(4, 4);
            var drawing = new GameObject("drawing");
            var picture = new GameObject("picture").AddComponent<SpriteRenderer>();
            picture.transform.SetParent(drawing.transform, false);
            picture.sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(.5f, 0), 4);
            drawing.AddComponent<LootSpriteView>().spriteRenderer = picture;
            var blank = new GameObject("blank drawing");
            blank.AddComponent<SpriteRenderer>();
            var empty = new GameObject("empty");
            try
            {
                Assert.That(DisplayItemInspector.IsInspectable(model), Is.True);
                Assert.That(DisplayItemInspector.IsDrawing(model), Is.False);
                Assert.That(DisplayItemInspector.IsInspectable(drawing), Is.True);
                Assert.That(DisplayItemInspector.IsDrawing(drawing), Is.True);
                Assert.That(DisplayItemInspector.IsInspectable(blank), Is.False, "A sprite renderer without a picture.");
                Assert.That(DisplayItemInspector.IsInspectable(empty), Is.False);
                Assert.That(DisplayItemInspector.IsInspectable(null), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(picture.sprite);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(model);
                Object.DestroyImmediate(drawing);
                Object.DestroyImmediate(blank);
                Object.DestroyImmediate(empty);
            }
        }
    }
}
