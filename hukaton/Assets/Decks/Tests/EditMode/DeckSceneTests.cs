using System.Linq;
using Hakaton.Decks.Unity;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Decks.Tests
{
    public sealed class DeckSceneTests
    {
        const string Root="Assets/Decks/";
        [Test] public void SceneIsAuthoredAndBoundWithoutAnyBuilder()
        {
            var setup=EditorSceneManager.GetSceneManagerSetup();
            try {
                var scene=EditorSceneManager.OpenScene(Root+"Scenes/DeckPrototype.unity");
                var all=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
                foreach(var t in all)Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject),Is.Zero,t.name);
                var deck=all.Select(t=>t.GetComponent<DeckPrototype>()).Single(c=>c!=null);
                Assert.That(PrefabUtility.IsPartOfPrefabInstance(deck),Is.True);
                var optionalEmbeddedFields=new[]{nameof(DeckPrototype.walletHost),nameof(DeckPrototype.embeddedVisibility),nameof(DeckPrototype.embeddedCloseButton)};
                foreach(var field in typeof(DeckPrototype).GetFields().Where(f=>typeof(Object).IsAssignableFrom(f.FieldType)
                    && !optionalEmbeddedFields.Contains(f.Name)))
                    Assert.That((Object)field.GetValue(deck),Is.Not.Null,field.Name);
                Assert.That(deck.collectionViews.Length,Is.EqualTo(6));
                Assert.That(deck.deckRows.Length,Is.EqualTo(12));
                Assert.That(all.Any(t=>t.name.Contains("Builder")),Is.False);
            } finally {if(setup.Any(s=>s.isLoaded && s.isActive))EditorSceneManager.RestoreSceneManagerSetup(setup);}
        }
        [Test] public void EntireUiUsesSavedPixelFontsAndIntegerSizes()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"Prefabs/DeckWorkshop.prefab");
            Assert.That(prefab,Is.Not.Null);
            var canvas=prefab.GetComponent<Canvas>();Assert.That(canvas.pixelPerfect,Is.True);Assert.That(canvas.renderMode,Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(prefab.GetComponent<CanvasScaler>().uiScaleMode,Is.EqualTo(CanvasScaler.ScaleMode.ConstantPixelSize));
            Assert.That(prefab.GetComponent<CanvasScaler>().scaleFactor,Is.EqualTo(1));
            foreach(var label in prefab.GetComponentsInChildren<TMP_Text>(true)) {
                Assert.That(label.font,Is.Not.Null,label.name);Assert.That(label.enableAutoSizing,Is.False,label.name);
                Assert.That(label.fontSize%label.font.faceInfo.pointSize,Is.Zero,label.name);
                Assert.That(label.fontSharedMaterial,Is.EqualTo(label.font.material),label.name);
                Assert.That(label.rectTransform.localScale,Is.EqualTo(Vector3.one),label.name);
            }
        }
        [Test] public void CatalogueAndPrototypeLimitsAreExplicitAssets()
        {
            var config=AssetDatabase.LoadAssetAtPath<DeckPrototypeConfig>(Root+"Data/Prototype.asset");
            Assert.That(config.cards.Length,Is.EqualTo(6));Assert.That(config.returnArtwork,Is.Not.Null);
            Assert.That(config.deckLimit,Is.EqualTo(12));Assert.That(config.initialHand,Is.EqualTo(3));Assert.That(config.maximumHand,Is.EqualTo(5));
            foreach(var card in config.cards)Assert.That(card.artwork,Is.Not.Null,card.name);
            Assert.That(config.cards.Select(c=>c.id).Distinct().Count(),Is.EqualTo(6));
        }
    }
}
