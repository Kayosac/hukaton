using System.Collections.Generic;
using System.Linq;
using Hakaton.Character;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Economy;
using Hakaton.Economy.Unity;
using Hakaton.Expeditions;
using Hakaton.ShopBlockout;
using Hakaton.Trading.Unity;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace Hakaton.Game.Tests
{
    /// <summary>Inspects the saved game as authored, without invoking an authoring script or entering Play.</summary>
    public sealed class UnifiedGameSceneTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";
        const string TypographyPath = "Assets/Content/UI/Typography/Resources/Fonts/";
        static readonly string[] StockIds = { "common_find", "special_find", "hare_foot", "boar_tusk", "wolf_pelt" };
        static readonly int[] StockPrices = { 20, 40, 60, 120, 180 };
        Scene scene;
        bool openedHere;
        GameLoop game;

        [SetUp]
        public void OpenTheSavedGameAdditively()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath), Is.Not.Null, "The unified game must be saved before validation.");
            scene = SceneManager.GetSceneByPath(ScenePath);
            openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            game = Components<GameLoop>().Single();
        }

        [TearDown]
        public void CloseOnlyTheSceneOpenedByThisFixture()
        {
            if (openedHere && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }

        [Test]
        public void SavedSceneHasOnePlayerCameraInputAndEconomyWithNoMissingScripts()
        {
            Assert.That(Components<FirstPersonMotor>(), Has.Length.EqualTo(1));
            Assert.That(Components<FirstPersonInput>(), Has.Length.EqualTo(1));
            Assert.That(Components<PlayerControlGate>(), Has.Length.EqualTo(1));
            Assert.That(Components<PlayerStamina>(), Has.Length.EqualTo(1));
            Assert.That(Components<CharacterController>(), Has.Length.EqualTo(1));
            Assert.That(Components<Camera>(), Has.Length.EqualTo(1));
            Assert.That(Components<AudioListener>(), Has.Length.EqualTo(1));
            Assert.That(Components<EventSystem>(), Has.Length.EqualTo(1));
            Assert.That(Components<CoinWalletHost>(), Has.Length.EqualTo(1));
            Assert.That(Components<TradingPrototype>(), Has.Length.EqualTo(1));
            Assert.That(Components<DeckPrototype>(), Has.Length.EqualTo(1));
            Assert.That(Components<ExpeditionWorld>(), Has.Length.EqualTo(1));
            foreach (var transform in Components<Transform>())
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject), Is.Zero, transform.name);
            Assert.That(game.trade.Session, Is.Null, "Edit mode inspects authored dependencies, not runtime construction.");
            Assert.That(game.workshop.Model, Is.Null);
            Assert.That(EditorBuildSettings.scenes.First(item => item.enabled).path, Is.EqualTo(ScenePath));
        }

        [Test]
        public void BothPhasesReferenceTheSameAuthoredPlayerWalletAndCollection()
        {
            Assert.That(game.trade, Is.Not.Null);
            Assert.That(game.workshop, Is.Not.Null);
            Assert.That(game.expedition, Is.Not.Null);
            Assert.That(game.services, Is.Not.Null);
            Assert.That(game.player, Is.Not.Null);
            Assert.That(game.wallet, Is.Not.Null);
            Assert.That(game.shopGeometry, Is.Not.Null);
            Assert.That(game.expeditionGeometry, Is.Not.Null);
            Assert.That(game.shopGeometry, Is.Not.SameAs(game.expeditionGeometry));
            var shop = game.trade.World;
            var world = game.expedition;
            Assert.That(shop.ValidateReferences(), Is.True);
            Assert.That(game.trade.externalExpeditions, Is.True);
            Assert.That(shop.requireTestExpedition, Is.False);
            Assert.That(shop.LootColliders, Is.Empty, "The main game must not leave the test-supplies shortcut enabled.");
            Assert.That(game.trade.walletHost, Is.SameAs(game.wallet));
            Assert.That(game.workshop.walletHost, Is.SameAs(game.wallet));
            Assert.That(game.services, Is.SameAs(game.trade.services));
            Assert.That(game.services.workshop, Is.SameAs(game.workshop));
            Assert.That(world.workshop, Is.SameAs(game.workshop));
            Assert.That(game.workshop.embeddedMode, Is.True);
            Assert.That(game.workshop.persistProgress, Is.False, "Only the unified save owns this economy.");
            Assert.That(game.workshop.embeddedVisibility, Is.Not.Null);
            Assert.That(game.workshop.embeddedCloseButton, Is.Not.Null);
            Assert.That(game.workshop.control, Is.SameAs(game.player.control));
            Assert.That(world.motor, Is.SameAs(game.player));
            Assert.That(shop.Movement, Is.SameAs(game.player));
            Assert.That(shop.PlayerRoot, Is.SameAs(game.player.transform));
            Assert.That(shop.PlayerController, Is.SameAs(game.player.characterController));
            Assert.That(shop.Control, Is.SameAs(game.player.control));
            Assert.That(world.control, Is.SameAs(game.player.control));
            Assert.That(shop.Stamina, Is.SameAs(game.player.stamina));
            Assert.That(world.viewCamera, Is.SameAs(shop.ViewCamera));
            Assert.That(world.thrower, Is.SameAs(game.player.GetComponent<CardThrower>()));
            Assert.That(world.thrower.world, Is.SameAs(world));
            Assert.That(world.thrower.control, Is.SameAs(game.player.control));
            Assert.That(world.thrower.input, Is.SameAs(game.player.input));
            Assert.That(world.thrower.trajectory, Is.Not.Null);
            Assert.That(world.vitals, Is.SameAs(game.player.GetComponent<PlayerVitals>()));
            Assert.That(world.vitals.motor, Is.SameAs(game.player));
            Assert.That(world.vitals.world, Is.SameAs(world));
            Assert.That(world.vitals.settings, Is.SameAs(world.settings));
            Assert.That(world.hand.world, Is.SameAs(world));
            Assert.That(world.hand.transform.IsChildOf(shop.ViewCamera.transform), Is.True);
            Assert.That(world.hand.cards, Has.Length.EqualTo(5));
            Assert.That(world.hand.cards.All(card => card), Is.True);
            Assert.That(world.hud.world, Is.SameAs(world));
            Assert.That(world.hud.heartFills, Has.Length.EqualTo(4));
            Assert.That(world.settings.maximumHalfHearts, Is.EqualTo(8));
            Assert.That(AssetDatabase.Contains(world.settings), Is.True);
            Assert.That(world.shopPose, Is.SameAs(shop.DoorInside));
            Assert.That(world.departurePose, Is.Not.Null);
            Assert.That(world.spawnedObjects, Is.Not.Null);
            Assert.That(world.projectilePrefab && world.impactPrefab, Is.True);
            Assert.That(AssetDatabase.Contains(world.projectilePrefab), Is.True);
            Assert.That(AssetDatabase.Contains(world.impactPrefab), Is.True);
            foreach (var root in new[] { game.shopGeometry.transform, game.expeditionGeometry.transform })
            {
                Assert.That(game.player.transform.IsChildOf(root), Is.False, "Phase visibility must not deactivate the shared player.");
                Assert.That(game.transform.IsChildOf(root), Is.False, "The coordinator must survive either phase switch.");
                Assert.That(game.workshop.transform.IsChildOf(root), Is.False, "The shared collection must survive either phase switch.");
            }
            Assert.That(game.shopHud, Is.SameAs(game.trade.hud.GetComponent<Canvas>()));
            Assert.That(game.progressCanvas, Is.Not.Null);
            Assert.That(game.objectiveLabel, Is.Not.Null);
            Assert.That(game.resultLabel, Is.Not.Null);
            Assert.That(game.objectiveLabel.transform.IsChildOf(game.progressCanvas.transform), Is.True);
            Assert.That(game.resultLabel.transform.IsChildOf(game.progressCanvas.transform), Is.True);
        }

        [Test]
        public void ReusablePlayerShopItemsCustomersAndWorldRemainPrefabInstances()
        {
            var shop = game.trade.World;
            var world = game.expedition;
            Assert.That(PrefabUtility.GetPrefabAssetType(game.player.gameObject), Is.EqualTo(PrefabAssetType.Variant));
            Assert.That(HasPrefabSource(game.player.gameObject, "Assets/Character/Prefabs/FirstPersonPlayer.prefab"), Is.True,
                "The unified player must retain the reusable character base through its variant chain.");
            foreach (var obj in new[] {
                game.player.gameObject, shop.Customers.actor.gameObject, game.services.goblin.gameObject,
                game.trade.hud.gameObject, game.workshop.gameObject, world.hud.gameObject, game.progressCanvas.gameObject,
                shop.DealItemAnchor.gameObject
            }) AssertPrefab(obj);
            Assert.That(shop.SlotAnchors, Has.Length.EqualTo(6));
            foreach (var slot in shop.SlotAnchors) AssertPrefab(slot.gameObject);
            Assert.That(shop.SlotAnchors.Select(slot => PrefabUtility.GetNearestPrefabInstanceRoot(slot.gameObject)).Distinct().Count(), Is.EqualTo(6),
                "Six editable display-slot instances are required.");
            Assert.That(PrefabUtility.GetOutermostPrefabInstanceRoot(shop.DealItemAnchor.gameObject),
                Is.Not.SameAs(PrefabUtility.GetOutermostPrefabInstanceRoot(shop.SlotAnchors[0].gameObject)));
            foreach (var prefab in shop.ItemPrefabs)
            {
                Assert.That(AssetDatabase.Contains(prefab), Is.True);
                Assert.That(PrefabUtility.IsPartOfPrefabAsset(prefab), Is.True);
            }
            foreach (var material in shop.ItemMaterials) Assert.That(AssetDatabase.Contains(material), Is.True);
            foreach (var loot in world.finds) AssertPrefab(loot.gameObject);
            foreach (var obstacle in world.obstacles) AssertPrefab(obstacle.gameObject);
            foreach (var creature in world.creatures)
            {
                AssertPrefab(creature.gameObject);
                Assert.That(PrefabUtility.IsPartOfPrefabAsset(creature.trophyPrefab), Is.True);
            }
            foreach (var target in Components<ShopInteractionTarget>()) AssertPrefab(target.gameObject);
            Assert.That(shop.Customers.appearances.All(appearance => appearance && appearance.IsComplete && AssetDatabase.Contains(appearance)), Is.True);
        }

        [Test]
        public void StockCatalogAndFiniteOutingSourcesMatchThe640CoinPreset()
        {
            var catalog = game.trade.startingStock;
            Assert.That(catalog, Has.Length.EqualTo(5));
            Assert.That(catalog.Select(entry => entry.id).Distinct().Count(), Is.EqualTo(5));
            Assert.That(catalog.Select(entry => entry.id), Is.EquivalentTo(StockIds));
            foreach (var entry in catalog)
            {
                int index = System.Array.IndexOf(StockIds, entry.id);
                Assert.That(entry.recommendedPrice, Is.EqualTo(StockPrices[index]), entry.id);
                Assert.That(entry.count, Is.Zero, "The first outing supplies all starting stock.");
                Assert.That(entry.category, Is.Not.Null.And.Not.Empty);
            }
            Assert.That(game.trade.World.ItemPrefabs, Has.Length.EqualTo(5));
            Assert.That(game.trade.World.ItemMaterials, Has.Length.EqualTo(5));
            var world = game.expedition;
            Assert.That(world.finds, Has.Length.EqualTo(6));
            Assert.That(world.finds.Distinct().Count(), Is.EqualTo(6));
            Assert.That(world.finds.Count(item => item.itemId == "common_find"), Is.EqualTo(4));
            Assert.That(world.finds.Count(item => item.itemId == "special_find"), Is.EqualTo(2));
            foreach (var find in world.finds)
            {
                Assert.That(find.world, Is.SameAs(world));
                Assert.That(find.guaranteed, Is.True, "The preset defines a finite guaranteed supply.");
                Assert.That(find.value, Is.EqualTo(catalog.Single(entry => entry.id == find.itemId).recommendedPrice));
                Assert.That(find.GetComponent<Collider>(), Is.Not.Null);
                if (find.spawnPoints != null)
                    foreach (var point in find.spawnPoints) Assert.That(point && point.gameObject.scene == scene, Is.True);
            }
            foreach (var unregistered in Components<WorldLoot>().Except(world.finds))
                Assert.That(unregistered.gameObject.activeSelf, Is.False, "Unregistered leftovers must not create extra supplies.");
            Assert.That(world.creatures, Has.Length.EqualTo(5));
            Assert.That(world.creatures.Distinct().Count(), Is.EqualTo(5));
            Assert.That(world.creatures.Count(creature => creature.temperament == Temperament.Timid), Is.EqualTo(3));
            Assert.That(world.creatures.Count(creature => creature.temperament == Temperament.Territorial), Is.EqualTo(1));
            Assert.That(world.creatures.Count(creature => creature.temperament == Temperament.Aggressive), Is.EqualTo(1));
            foreach (var creature in world.creatures)
            {
                Assert.That(creature.world, Is.SameAs(world));
                Assert.That(creature.body && creature.navigator && creature.visual, Is.True);
                Assert.That(creature.spawnPoints, Is.Not.Null.And.Not.Empty);
                foreach (var point in creature.spawnPoints) Assert.That(point && point.gameObject.scene == scene, Is.True);
                var expectedId = creature.temperament == Temperament.Timid ? "hare_foot"
                    : creature.temperament == Temperament.Territorial ? "boar_tusk" : "wolf_pelt";
                Assert.That(creature.trophyPrefab.itemId, Is.EqualTo(expectedId));
                Assert.That(creature.trophyPrefab.value, Is.EqualTo(catalog.Single(entry => entry.id == expectedId).recommendedPrice));
            }
            Assert.That(world.finds.Sum(item => item.value) + world.creatures.Sum(creature => creature.trophyPrefab.value), Is.EqualTo(640));
        }

        [Test]
        public void StartingEconomyDecksAndTradeTuningMatchTheConceptPreset()
        {
            Assert.That(game.revenueGoal, Is.EqualTo(1000));
            Assert.That(game.wallet.startingBalance, Is.Zero);
            var config = game.workshop.config;
            Assert.That(AssetDatabase.Contains(config), Is.True);
            Assert.That(config.startingCoins, Is.Zero);
            Assert.That(config.deckLimit, Is.EqualTo(12));
            Assert.That(config.initialHand, Is.EqualTo(3));
            Assert.That(config.maximumHand, Is.EqualTo(5));
            Assert.That(config.maximumCardLevel, Is.EqualTo(2));
            Assert.That(config.handUpgradePrice, Is.EqualTo(200));
            Assert.That(config.cards, Has.Length.EqualTo(6));
            Assert.That(config.cards.All(card => card && AssetDatabase.Contains(card) && card.artwork), Is.True);
            var model = config.CreateWorkshop(new CoinWallet());
            Assert.That(model.Coins, Is.Zero);
            Assert.That(model.Selected(DeckKind.Exploration), Has.Count.EqualTo(8));
            Assert.That(model.Selected(DeckKind.Combat), Has.Count.EqualTo(7));
            Assert.That(model.Owned, Has.Count.EqualTo(15));
            Assert.That(model.CanDepart(), Is.Null);
            var expectedCopies = new Dictionary<string, int> {
                { "impulse", 3 }, { "ice", 3 }, { "fire", 2 }, { "strike", 5 }, { "heavy_strike", 0 }, { "heal", 2 }
            };
            foreach (var pair in expectedCopies)
                Assert.That(model.Owned.Count(copy => copy.Spec.Id == pair.Key), Is.EqualTo(pair.Value), pair.Key);
            Assert.That(game.trade.shiftSeconds, Is.EqualTo(120));
            Assert.That(game.trade.minOpeningRatio, Is.EqualTo(.75f));
            Assert.That(game.trade.maxOpeningRatio, Is.EqualTo(.9f));
            Assert.That(game.trade.minCeilingRatio, Is.EqualTo(1));
            Assert.That(game.trade.maxCeilingRatio, Is.EqualTo(1.35f));
            Assert.That(game.trade.minOfferCeilingRatio, Is.EqualTo(.95f));
            Assert.That(game.trade.maxOfferCeilingRatio, Is.EqualTo(1.2f));
            Assert.That(game.trade.asksPriceProbability, Is.EqualTo(.7f));
            Assert.That(game.trade.usePresetPatienceCosts, Is.True);
        }

        [Test]
        public void AllSavedUiUsesApprovedBitmapFontsAndAnIntegerPixelCanvas()
        {
            Assert.That(Components<Text>(), Is.Empty);
            var ithaca = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TypographyPath + "Ithaca Pixel.asset");
            var pixeloid = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TypographyPath + "PixeloidSans Pixel.asset");
            Assert.That(ithaca && pixeloid, Is.True);
            foreach (var font in new[] { ithaca, pixeloid })
            {
                Assert.That(font.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Static));
                Assert.That(font.atlasRenderMode, Is.EqualTo(GlyphRenderMode.RASTER));
                foreach (char letter in "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyzАБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя0123456789")
                    Assert.That(font.HasCharacter(letter), Is.True, font.name + " is missing " + letter);
                foreach (var texture in font.atlasTextures)
                {
                    Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Point));
                    Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
                    Assert.That(texture.mipmapCount, Is.EqualTo(1));
                }
            }
            foreach (var canvas in Components<Canvas>().Where(canvas => canvas.isRootCanvas))
            {
                Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay), canvas.name);
                Assert.That(canvas.pixelPerfect, Is.True, canvas.name);
                var scaler = canvas.GetComponent<CanvasScaler>();
                Assert.That(scaler, Is.Not.Null, canvas.name);
                Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ConstantPixelSize), canvas.name);
                Assert.That(scaler.scaleFactor, Is.GreaterThanOrEqualTo(1));
                Assert.That(scaler.scaleFactor, Is.EqualTo(Mathf.Round(scaler.scaleFactor)));
            }
            var labels = Components<TMP_Text>();
            Assert.That(labels, Is.Not.Empty);
            foreach (var label in labels)
            {
                Assert.That(label.font == ithaca || label.font == pixeloid, Is.True, label.name);
                Assert.That(label.enableAutoSizing, Is.False, label.name);
                Assert.That(label.fontSize % (label.font == ithaca ? 16 : 9), Is.Zero, label.name);
                Assert.That(label.rectTransform.localScale, Is.EqualTo(Vector3.one), label.name);
                Assert.That(label.fontSharedMaterial, Is.Not.Null, label.name);
                Assert.That(AssetDatabase.Contains(label.fontSharedMaterial), Is.True, label.name);
                Assert.That(label.fontSharedMaterial.shader.name, Is.EqualTo("TextMeshPro/Mobile/Bitmap"), label.name);
                Assert.That(label.fontSharedMaterial.mainTexture, Is.SameAs(label.font.atlasTexture), label.name);
            }
        }

        static void AssertPrefab(GameObject item)
        {
            Assert.That(item, Is.Not.Null);
            Assert.That(PrefabUtility.IsPartOfPrefabInstance(item), Is.True, item.name + " must retain its editable prefab connection.");
            Assert.That(PrefabUtility.GetPrefabInstanceStatus(item), Is.EqualTo(PrefabInstanceStatus.Connected), item.name);
        }

        static bool HasPrefabSource(GameObject instance, string path)
        {
            var visited = new HashSet<int>();
            for (var source = instance; source && visited.Add(source.GetInstanceID()); source = PrefabUtility.GetCorrespondingObjectFromSource(source))
                if (AssetDatabase.GetAssetPath(source) == path) return true;
            return false;
        }

        T[] Components<T>() where T : Component => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    }
}
