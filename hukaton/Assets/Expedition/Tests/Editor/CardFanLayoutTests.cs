using System.Collections.Generic;
using System.Linq;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>
    /// Playtest: the whole hand (ten cards at most, the code ready for twenty) is fanned out at once. The authored fan
    /// of Player.prefab keeps every card on screen on 16:9 and 4:3: about 60% or more of each resting card is visible
    /// (the bottom edge always cuts the fan), no card leaves the screen sideways, the raised selected card stays
    /// almost whole and the resting fan leaves the HUD's card counter in the bottom-right corner free.
    /// </summary>
    public sealed class CardFanLayoutTests
    {
        const string PlayerPath = "Assets/Game/Prefabs/Player.prefab";
        const string HudPath = "Assets/Game/Prefabs/UI/Expedition HUD.prefab";

        static CardHandView Hand() => AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath).GetComponentInChildren<CardHandView>(true);

        [Test]
        public void PlayerHandHasASlotPerCardOfTheBiggestHandAndATemplateForMore()
        {
            var hand = Hand();
            var config = AssetDatabase.LoadAssetAtPath<DeckPrototypeConfig>("Assets/Game/Data/Cards.asset");
            Assert.That(config.maximumHand, Is.LessThanOrEqualTo(CardFanLayout.MaxCards));
            Assert.That(hand.slots.Length, Is.GreaterThanOrEqualTo(config.maximumHand), "Every card of a full hand has an authored slot.");
            Assert.That(hand.slots.All(slot => slot && slot.transform.parent == hand.transform), Is.True);
            Assert.That(hand.slots.Distinct().Count(), Is.EqualTo(hand.slots.Length));
            Assert.That(hand.slotTemplate, Is.Not.Null, "Bigger hands clone the template.");
            var camera = hand.transform.parent.GetComponent<Camera>();
            Assert.That(camera, Is.Not.Null, "The fan lives in camera space.");
            Assert.That(hand.fitFieldOfView, Is.EqualTo(camera.fieldOfView), "The fan is fitted to the camera's resting FOV.");
        }

        [TestCase(16, 9)]
        [TestCase(4, 3)]
        public void FanIsSymmetricOrderedFlatAndSqueezesAsTheHandGrows(int width, int height)
        {
            var hand = Hand();
            float aspect = width / (float)height;
            float previousGap = float.MaxValue, previousSpread = 0;
            for (int n = 1; n <= CardFanLayout.MaxCards; n++)
            {
                var fan = hand.Fan(n, aspect);
                Assert.That(fan.Count, Is.EqualTo(n));
                for (int i = 0; i < n; i++)
                {
                    Vector2 a = fan.Offset(i), b = fan.Offset(n - 1 - i);
                    Assert.That(a.x, Is.EqualTo(-b.x).Within(1e-5f), $"{n} cards: card {i} mirrors card {n - 1 - i}");
                    Assert.That(a.y, Is.EqualTo(b.y).Within(1e-5f));
                    Assert.That(fan.Roll(i), Is.EqualTo(-fan.Roll(n - 1 - i)).Within(1e-4f));
                    Assert.That(a.y, Is.LessThanOrEqualTo(1e-6f), "No card rises above the top of the arc.");
                    Assert.That(-a.y, Is.LessThanOrEqualTo(hand.maxSink + 1e-5f), "The arc flattens: the outer cards sink little.");
                    if (i > 0) Assert.That(a.x, Is.GreaterThan(fan.Offset(i - 1).x), $"{n} cards: left to right");
                }
                if (n == 1) continue;
                float gap = 2 * fan.HalfSpread / (n - 1);
                Assert.That(gap, Is.LessThanOrEqualTo(previousGap + 1e-5f), $"{n} cards squeeze closer than {n - 1}");
                Assert.That(fan.HalfSpread, Is.GreaterThanOrEqualTo(previousSpread - 1e-5f), $"{n} cards spread no narrower than {n - 1}");
                previousGap = gap;
                previousSpread = fan.HalfSpread;
            }
        }

        [TestCase(16, 9)]
        [TestCase(4, 3)]
        public void EveryCardOfEveryHandStaysOnScreen(int width, int height)
        {
            var hand = Hand();
            float aspect = width / (float)height;
            float counterEdge = CounterLeftEdge(aspect);
            for (int n = 1; n <= CardFanLayout.MaxCards; n++)
            {
                var fan = hand.Fan(n, aspect);
                for (int i = 0; i < n; i++)
                {
                    var rest = Visible(hand, fan, i, false, aspect);
                    Assert.That(rest.fraction, Is.GreaterThanOrEqualTo(.58f), $"{n} cards, card {i} at rest on {width}:{height}");
                    Assert.That(rest.edge, Is.LessThanOrEqualTo(1), $"{n} cards, card {i} leaves the screen sideways");
                    Assert.That(rest.edge, Is.LessThan(counterEdge), $"{n} cards, card {i} runs under the card counter");
                    var raised = Visible(hand, fan, i, true, aspect);
                    Assert.That(raised.fraction, Is.GreaterThanOrEqualTo(.8f), $"{n} cards, card {i} selected on {width}:{height}");
                    Assert.That(raised.edge, Is.LessThanOrEqualTo(1), $"{n} cards, selected card {i} leaves the screen sideways");
                }
            }
        }

        /// <summary>
        /// Share of the card inside the view at its depth, and its outermost corner as a share of the half-screen
        /// width. The card stays parallel to the screen (only rolled), so its depth is uniform.
        /// </summary>
        static (float fraction, float edge) Visible(CardHandView hand, CardFanLayout fan, int index, bool selected, float aspect)
        {
            Vector3 centre = hand.Target(fan, index, selected, 0, false);
            float scale = selected ? hand.selectedScale : 1;
            Vector2 half = new Vector2(hand.cardWidth, hand.cardWidth * CardHandView.CardAspect) * scale * .5f;
            // The view rolls the card by -Roll around the camera's forward axis.
            var rotation = Quaternion.Euler(0, 0, -fan.Roll(index));
            var corners = new List<Vector2>();
            foreach (var corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
            {
                Vector3 p = rotation * new Vector3(corner.x * half.x, corner.y * half.y, 0);
                corners.Add(new Vector2(centre.x + p.x, centre.y + p.y));
            }
            float halfHeight = centre.z * Mathf.Tan(hand.fitFieldOfView * .5f * Mathf.Deg2Rad);
            float halfWidth = halfHeight * aspect;
            var inside = Clip(corners, -halfWidth, halfWidth, -halfHeight, halfHeight);
            float edge = corners.Max(c => Mathf.Abs(c.x)) / halfWidth;
            return (Area(inside) / (4 * half.x * half.y), edge);
        }

        /// <summary>Left edge of the right-anchored card counter as a share of the half-screen width.</summary>
        static float CounterLeftEdge(float aspect)
        {
            var hud = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath).GetComponent<ExpeditionHud>();
            var counter = (RectTransform)hud.cardCounter.transform.parent;
            Assert.That((counter.anchorMin.x, counter.anchorMax.x), Is.EqualTo((1f, 1f)), "The counter sits in the bottom-right corner.");
            var layout = hud.GetComponentInChildren<ReferenceUiScale>(true);
            // ReferenceUiScale fits the height, limited by the width: the layout is never narrower than its reference.
            float layoutHalf = Mathf.Max(layout.referenceWidth, layout.referenceHeight * aspect) * .5f;
            float left = counter.anchoredPosition.x - counter.pivot.x * counter.sizeDelta.x;
            return (layoutHalf + left) / layoutHalf;
        }

        static List<Vector2> Clip(List<Vector2> polygon, float xMin, float xMax, float yMin, float yMax)
        {
            polygon = ClipEdge(polygon, p => p.x >= xMin, (a, b) => Lerp(a, b, (xMin - a.x) / (b.x - a.x)));
            polygon = ClipEdge(polygon, p => p.x <= xMax, (a, b) => Lerp(a, b, (xMax - a.x) / (b.x - a.x)));
            polygon = ClipEdge(polygon, p => p.y >= yMin, (a, b) => Lerp(a, b, (yMin - a.y) / (b.y - a.y)));
            return ClipEdge(polygon, p => p.y <= yMax, (a, b) => Lerp(a, b, (yMax - a.y) / (b.y - a.y)));
        }

        static Vector2 Lerp(Vector2 a, Vector2 b, float t) => a + (b - a) * t;

        static List<Vector2> ClipEdge(List<Vector2> polygon, System.Func<Vector2, bool> inside, System.Func<Vector2, Vector2, Vector2> cross)
        {
            var result = new List<Vector2>();
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 from = polygon[i], to = polygon[(i + 1) % polygon.Count];
                if (inside(to))
                {
                    if (!inside(from)) result.Add(cross(from, to));
                    result.Add(to);
                }
                else if (inside(from)) result.Add(cross(from, to));
            }
            return result;
        }

        static float Area(List<Vector2> polygon)
        {
            float twice = 0;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                twice += a.x * b.y - b.x * a.y;
            }
            return Mathf.Abs(twice) * .5f;
        }
    }
}
