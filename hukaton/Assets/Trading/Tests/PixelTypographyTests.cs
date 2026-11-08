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

        [Test]
        public void SavedHudUsesIntegerPixelScaleAndConfiguredTmpLabels()
        {
            var hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Trading/Prefabs/TradingHUD.prefab");
            Assert.That(hud.GetComponentsInChildren<Text>(true), Is.Empty);
            Assert.That(hud.GetComponent<Canvas>().pixelPerfect, Is.True);
            Assert.That(hud.GetComponent<Canvas>().renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            var scaler = hud.GetComponent<CanvasScaler>();
            Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ConstantPixelSize));
            Assert.That(scaler.scaleFactor, Is.EqualTo(1));
            var labels = hud.GetComponentsInChildren<TMP_Text>(true);
            Assert.That(labels.Length, Is.GreaterThan(0));
            foreach (var label in labels)
            {
                Assert.That(label.font, Is.Not.Null, label.name);
                Assert.That(label.enableAutoSizing, Is.False, label.name);
                Assert.That(label.fontSize % label.font.faceInfo.pointSize, Is.Zero, label.name);
                Assert.That(label.fontSharedMaterial, Is.EqualTo(label.font.material), label.name);
                Assert.That(label.rectTransform.localScale, Is.EqualTo(Vector3.one), label.name);
            }
        }
    }
}
