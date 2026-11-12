using Hakaton.Character;
using Hakaton.Expeditions;
using Hakaton.Feel;
using Hakaton.Trading.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Game.Tests
{
    /// <summary>Playtest: one stamina widget — the arc of pips under the aim — in the shop HUD and in the outing HUD.</summary>
    public sealed class StaminaPipsHudTests
    {
        const string PipsPath = "Assets/Game/Prefabs/UI/Stamina pips.prefab";

        static GameObject Load(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(asset, Is.Not.Null, path);
            return asset;
        }

        [Test]
        public void ThePipsPrefabHasCrispSpritesAndATemplatePip()
        {
            var pips = Load(PipsPath).GetComponent<StaminaPips>();
            Assert.That(pips, Is.Not.Null);
            Assert.That(pips.visibility, Is.Not.Null);
            Assert.That(pips.pipTemplate, Is.Not.Null);
            Assert.That(pips.pipTemplate.transform.parent, Is.SameAs(pips.transform));
            Assert.That(pips.pipTemplate.raycastTarget, Is.False);
            Assert.That(pips.visibility.blocksRaycasts, Is.False);
            foreach (var sprite in new[] { pips.fullPip, pips.emptyPip, pips.tiredPip })
            {
                Assert.That(sprite, Is.Not.Null);
                Assert.That(sprite.texture.filterMode, Is.EqualTo(FilterMode.Point), sprite.name + ": hard pixels");
                Assert.That(sprite.texture.mipmapCount, Is.EqualTo(1), sprite.name);
            }
            Assert.That(pips.fullPip, Is.Not.SameAs(pips.emptyPip));
            Assert.That(pips.tiredPip, Is.Not.SameAs(pips.emptyPip));
        }

        [Test]
        public void BothHudsShowTheSameArcUnderTheAimInsideTheirLayout()
        {
            var pipsAsset = Load(PipsPath);
            var outing = Load("Assets/Game/Prefabs/UI/Expedition HUD.prefab").GetComponent<ExpeditionHud>();
            var shop = Load("Assets/Trading/Prefabs/TradingHUD.prefab").GetComponent<TradingHud>();
            Check(outing.staminaPips, outing.layout, pipsAsset);
            Check(shop.staminaPips, shop.shopLayout, pipsAsset);
            Assert.That(outing.transform.Find("Stamina"), Is.Null, "The old bar is gone.");
            Assert.That(shop.transform.Find("Layout — 1280 x 800/Stamina"), Is.Null, "The old bar is gone.");
        }

        static void Check(StaminaPips pips, ReferenceUiScale layout, GameObject asset)
        {
            Assert.That(pips, Is.Not.Null);
            Assert.That(layout, Is.Not.Null);
            Assert.That(pips.transform.parent, Is.SameAs(layout.transform), "Inside the 736 × 368 layout.");
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(pips.gameObject), Is.SameAs(asset), "One shared widget.");
            var rect = (RectTransform)pips.transform;
            Assert.That((rect.anchorMin, rect.anchorMax), Is.EqualTo((new Vector2(.5f, .5f), new Vector2(.5f, .5f))), "Anchored on the aim point.");
            Assert.That(rect.anchoredPosition.x, Is.Zero);
            Assert.That(rect.anchoredPosition.y, Is.LessThan(-13.8f - 3), "Below the charge ring (27.6 units across).");
            Assert.That(rect.anchoredPosition.y % 1, Is.Zero, "Whole layout units.");
        }
    }
}
