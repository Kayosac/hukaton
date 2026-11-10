using System.Linq;
using Hakaton.Expeditions.Cards;
using Hakaton.Feel;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>The expedition HUD as approved in the 2026-11-10 UI handoff.</summary>
    public sealed class ExpeditionHudTests
    {
        static ExpeditionHud Hud => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Expedition HUD.prefab").GetComponent<ExpeditionHud>();

        [Test]
        public void HeartsSpinAsDrawnFramesAndFillFromTheLeft()
        {
            var hud = Hud;
            Assert.That(hud.heartFills.Length, Is.EqualTo(4));
            foreach (var fill in hud.heartFills)
            {
                Assert.That(fill.type, Is.EqualTo(Image.Type.Filled));
                Assert.That(fill.fillMethod, Is.EqualTo(Image.FillMethod.Horizontal));
                Assert.That(fill.color, Is.EqualTo(Color.white), "The drawn red is never tinted");
                Assert.That(fill.GetComponent<UiSpriteFlipbook>().frames.Length, Is.EqualTo(6));
                var empty = fill.transform.parent.Find("Empty").GetComponent<UiSpriteFlipbook>();
                Assert.That(empty.frames.Length, Is.EqualTo(6));
                Assert.That(empty.frameSeconds, Is.EqualTo(fill.GetComponent<UiSpriteFlipbook>().frameSeconds));
            }
        }

        [Test]
        public void CounterIsWhiteTiltedAndNothingShowsTheOldStack()
        {
            var hud = Hud;
            Assert.That(hud.cardCounter.color, Is.EqualTo(Color.white));
            Assert.That(hud.cardCounter.fontSize, Is.EqualTo(32));
            Assert.That(Mathf.DeltaAngle(hud.cardCounter.transform.parent.localEulerAngles.z, -8), Is.EqualTo(0).Within(.01f));
            Assert.That(hud.cardCounter.textWrappingMode, Is.EqualTo(TextWrappingModes.NoWrap));
            Assert.That(hud.GetComponentsInChildren<TMP_Text>(true).Any(t => t.text.Contains("КОЛОДА") || t.text.Contains("Сумка")), Is.False);
            var hand = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Player.prefab").GetComponentInChildren<CardHandView>(true);
            Assert.That(hand.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("Other deck")), Is.False);
        }

        [Test]
        public void SpinArrowIsTailShaftAndHeadAroundTheAimPoint()
        {
            var hud = Hud;
            Assert.That(new[] { hud.spinTail, hud.spinShaft, hud.spinHead }.Select(i => i.sprite.name),
                Is.EqualTo(new[] { "ArrowTail", "ArrowShaft", "ArrowHead" }));
            foreach (var part in new[] { hud.spinTail, hud.spinShaft, hud.spinHead })
            {
                Assert.That(part.transform.parent, Is.EqualTo(hud.spinPointer));
                Assert.That(part.rectTransform.pivot, Is.EqualTo(new Vector2(0, .5f)));
                Assert.That(part.transform.localScale, Is.EqualTo(Vector3.one));
            }
            Assert.That(hud.spinShaft.transform.GetSiblingIndex(), Is.LessThan(hud.spinHead.transform.GetSiblingIndex()));
            Assert.That(hud.reticle.sprite.name, Is.EqualTo("CrosshairX"));
        }

        [TestCase("Assets/Game/Prefabs/Cards/MoonGravity/Moon gravity badge.prefab", "EffectMoon")]
        [TestCase("Assets/Game/Prefabs/Cards/Lightness/Lightness badge.prefab", "EffectFeather")]
        [TestCase("Assets/Game/Prefabs/Cards/RainCloud/Wet badge.prefab", "EffectDroplet")]
        public void EffectBadgeTurnsItsRingAndSymbolOppositeWays(string path, string symbol)
        {
            var badge = AssetDatabase.LoadAssetAtPath<PlayerStatusBadge>(path);
            Assert.That(badge.rings.Length, Is.EqualTo(2));
            var far = badge.rings[0].GetComponent<UiSpriteFlipbook>().frames.Select(f => f.name).ToArray();
            var near = badge.rings[1].GetComponent<UiSpriteFlipbook>().frames.Select(f => f.name).ToArray();
            Assert.That(far, Is.EqualTo(Enumerable.Range(0, 6).Select(i => $"EffectRingFar_Y_{i * 60:000}")));
            Assert.That(near, Is.EqualTo(Enumerable.Range(0, 6).Select(i => $"EffectRingNear_Y_{i * 60:000}")));
            Assert.That(badge.icon.GetComponent<UiSpriteFlipbook>().frames.Select(f => f.name),
                Is.EqualTo(new[] { 0, 5, 4, 3, 2, 1 }.Select(i => $"{symbol}_Y_{i * 60:000}")));
            // Far half, symbol, near half.
            Assert.That(badge.rings[0].transform.GetSiblingIndex(), Is.LessThan(badge.icon.transform.GetSiblingIndex()));
            Assert.That(badge.icon.transform.GetSiblingIndex(), Is.LessThan(badge.rings[1].transform.GetSiblingIndex()));
            foreach (var ring in badge.rings) Assert.That(ring.type, Is.EqualTo(Image.Type.Filled));
        }
    }
}
