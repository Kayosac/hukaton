using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace Hakaton.Trading.Tests
{
    public sealed class PixelTypographyTests
    {
        const string Root = "Assets/Content/UI/Typography/";
        [TestCase("Ithaca", 16)]
        [TestCase("PixeloidSans", 9)]
        public void SavedFontPreservesPixelEdgesAndAlphabet(string name, int baseSize)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root + "Resources/Fonts/" + name + " Pixel.asset");
            Assert.That(font, Is.Not.Null);
            Assert.That(font.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Static));
            Assert.That(font.atlasRenderMode, Is.EqualTo(GlyphRenderMode.RASTER));
            Assert.That(font.faceInfo.pointSize, Is.EqualTo(baseSize));
            Assert.That(font.material.shader.name, Is.EqualTo("TextMeshPro/Mobile/Bitmap"));
            Assert.That(font.fallbackFontAssetTable, Is.Empty);
            Assert.That(font.material.mainTexture, Is.EqualTo(font.atlasTexture));
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyzАБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюяІіЇїЄєҐґЎў0123456789";
            foreach (char c in alphabet) Assert.That(font.HasCharacter(c), Is.True, name + " missing " + c);
            foreach (var atlas in font.atlasTextures)
            {
                Assert.That(atlas.filterMode, Is.EqualTo(FilterMode.Point));
                Assert.That(atlas.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
                Assert.That(atlas.mipmapCount, Is.EqualTo(1));
                Assert.That(atlas.GetPixels32().All(c => c.a == 0 || c.a == 255), Is.True, "Antialiasing in atlas");
            }
        }

        // Shop HUD handoff (2026-11-10): the approved mockups set Ithaca 24 (goals title) and 18 (item card)
        // inside the 736 × 368 reference layout, which is magnified to the window as a whole.
        // Trading handoff (same day): request buttons 32 → 24 (75%), small actions 32 → 17 (52.5%), hotkeys 12 → 6.3.
        static bool ApprovedMockupSize(TMP_Text label) => label.GetComponentInParent<Hakaton.Feel.ReferenceUiScale>(true)
            && ((label.font.faceInfo.pointSize == 16 && (label.fontSize == 24 || label.fontSize == 18 || label.fontSize == 17))
                || (label.font.faceInfo.pointSize == 9 && Mathf.Approximately(label.fontSize, 6.3f)));

        [Test]
        public void ShopHudUsesHandoffSprites()
        {
            var hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Trading/Prefabs/TradingHUD.prefab").GetComponent<Hakaton.Trading.Unity.TradingHud>();
            Assert.That(hud.shopLayout, Is.Not.Null);
            // The aim point is a small white square, an even whole number of screen pixels.
            Assert.That(hud.reticleDot.sprite.name, Is.EqualTo("px_white"));
            Assert.That(hud.reticleIdle, Is.EqualTo(Color.white));
            Assert.That(hud.reticleFocused, Is.EqualTo(Color.white));
            Assert.That(hud.reticleDot.rectTransform.pivot, Is.EqualTo(new Vector2(.5f, .5f)));
            var square = hud.reticleDot.GetComponent<Hakaton.Feel.PixelSquare>();
            Assert.That(square != null, "Sized to an even whole number of screen pixels");
            Assert.That(square.layout, Is.SameAs(hud.shopLayout));
            Assert.That(Hakaton.Feel.PixelSquare.SidePixels(1080 / 414f, square.pixelsPerStep), Is.EqualTo(6), "6 px at 1080p");
            Assert.That(hud.selectedItemPanel.GetComponent<Image>().sprite.name, Is.EqualTo("ItemDescriptionPaper"));
            Assert.That(hud.selectedItemPanel.localEulerAngles.z, Is.EqualTo(12).Within(.01f));
            Assert.That(hud.selectedItemTitle.transform.IsChildOf(hud.selectedItemPanel), Is.True);
            Assert.That(hud.selectedItemDetail.transform.IsChildOf(hud.selectedItemPanel), Is.True);
            Assert.That(hud.contextPanel.GetComponent<Image>(), Is.Null, "The prompt has no plate");
            var coin = hud.shopLayout.GetComponentInChildren<Hakaton.Feel.UiSpriteFlipbook>(true);
            Assert.That(coin.frames.Select(f => f.name), Is.EqualTo(new[] { "Coin_Y_000", "Coin_Y_060", "Coin_Y_120", "Coin_Y_180", "Coin_Y_240", "Coin_Y_300" }));
            Assert.That(Hakaton.Trading.Unity.TradingHud.FormatMoney(1250), Is.EqualTo("1 250"));
        }

        [Test]
        public void TradingConversationFollowsHandoff()
        {
            var hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Trading/Prefabs/TradingHUD.prefab").GetComponent<Hakaton.Trading.Unity.TradingHud>();
            Assert.That(hud.clockFill.type, Is.EqualTo(Image.Type.Filled));
            Assert.That(hud.clockFill.fillMethod, Is.EqualTo(Image.FillMethod.Radial360));
            Assert.That(hud.clockFill.fillClockwise, Is.False, "The remaining-time edge moves clockwise");
            Assert.That(hud.clockRestingFace.transform.IsChildOf(hud.clockBody.transform), Is.False, "The face is never faded with the clock");
            Assert.That(hud.clockClose.transform.IsChildOf(hud.clockBody.transform), Is.True);
            Assert.That(hud.endedClockAlpha, Is.EqualTo(.38f).Within(.001f));
            Assert.That(Hakaton.Trading.Unity.TradingHud.ClockFill(30, 120), Is.EqualTo(.25f).Within(.0001f));
            Assert.That(Hakaton.Trading.Unity.TradingHud.ClockFill(-5, 120), Is.Zero);
            Assert.That(hud.speech.fontSize, Is.EqualTo(24));
            Assert.That(hud.priceButtons.Length, Is.EqualTo(5));
            foreach (var button in hud.priceButtons.Append(hud.accept).Append(hud.refuseDeal)
                         .Append(hud.acceptOffer).Append(hud.declineOffer))
                Assert.That(button.transform.localScale, Is.EqualTo(Vector3.one), button.name + ": sized by density, never scaled");
            Assert.That(hud.acceptOffer.transform.IsChildOf(hud.offerActions.transform), Is.True);
            Assert.That(hud.declineOffer.transform.IsChildOf(hud.offerActions.transform), Is.True);
            Assert.That(hud.conversation.GetComponentsInChildren<TMP_Text>(true).Any(t => t.text.Contains("Ориентир")), Is.False);
        }

        [Test]
        public void SavedHudUsesIntegerPixelScaleAndConfiguredTmpLabels()
        {
            var hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Trading/Prefabs/TradingHUD.prefab");
            Assert.That(hud.GetComponentsInChildren<Text>(true), Is.Empty);
            Assert.That(hud.GetComponent<Canvas>().pixelPerfect, Is.True);
            Assert.That(hud.GetComponent<Canvas>().renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            var scaler = hud.GetComponent<CanvasScaler>();
            // Full HD layout, magnified with the window (×2 on 4K): the one UI size standard.
            Assert.That(Hakaton.Feel.UiReference.Follows(scaler), Is.True);
            var labels = hud.GetComponentsInChildren<TMP_Text>(true);
            Assert.That(labels.Length, Is.GreaterThan(0));
            foreach (var label in labels)
            {
                Assert.That(label.font, Is.Not.Null, label.name);
                Assert.That(label.enableAutoSizing, Is.False, label.name);
                if (!ApprovedMockupSize(label))
                    Assert.That(label.fontSize % label.font.faceInfo.pointSize, Is.Zero, label.name);
                Assert.That(label.fontSharedMaterial, Is.EqualTo(label.font.material), label.name);
                Assert.That(label.rectTransform.localScale, Is.EqualTo(Vector3.one), label.name);
            }
        }
    }
}
