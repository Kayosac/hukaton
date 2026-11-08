using System.IO;
using System.Linq;
using Hakaton.Trading.Unity;
using Hakaton.Character;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Object = UnityEngine.Object;

namespace Hakaton.Trading.Tests
{
    /// <summary>Checks the saved authoring contract without running gameplay or a scene builder.</summary>
    public sealed class TradingSceneTests
    {
        const string ScenePath = "Assets/Trading/Scenes/TradingPrototype.unity";
        Scene scene;
        bool openedHere;
        TradingPrototype controller;
        TradingSceneBindings world;

        [SetUp]
        public void OpenAuthoredScene()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath), Is.Not.Null,
                "The trading prototype must be delivered as a saved scene.");
            scene = SceneManager.GetSceneByPath(ScenePath);
            openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var controllers = Components<TradingPrototype>();
            Assert.That(controllers, Has.Length.EqualTo(1));
            controller = controllers[0];
            world = controller.World;
        }

        [TearDown]
        public void CloseOnlySceneOpenedByTest()
        {
            if (openedHere && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }

        [Test]
        public void SavedSceneHasCompleteReferencesBeforePlay()
        {
            Assert.That(EditorApplication.isPlaying, Is.False);
            Assert.That(controller.Session, Is.Null, "Gameplay must not be needed to create the shop.");
            Assert.That(world, Is.Not.Null);
            Assert.That(world.ValidateReferences(), Is.True);
            Assert.That(controller.hud, Is.Not.Null);
            Assert.That(controller.sound, Is.Not.Null);
            Assert.That(controller.placeSound, Is.Not.Null);
            Assert.That(controller.saleSound, Is.Not.Null);
            Assert.That(Components<Renderer>().Length, Is.GreaterThan(20),
                "The shop, counter and inventory props must already exist in Edit mode.");
            foreach (var reference in new Object[] { world, controller.hud, controller.sound })
                Assert.That(((Component)reference).gameObject.scene, Is.EqualTo(scene));
        }

        [Test]
        public void SavedSceneContainsOneCameraListenerAndUiInputSystem()
        {
            Assert.That(Components<Camera>(), Has.Length.EqualTo(1));
            Assert.That(Components<AudioListener>(), Has.Length.EqualTo(1));
            Assert.That(Components<EventSystem>(), Has.Length.EqualTo(1));
            Assert.That(world.ViewCamera.GetComponent<AudioListener>(), Is.Not.Null);
            Assert.That(controller.hud.GetComponent<Canvas>(), Is.Not.Null);
            Assert.That(controller.hud.GetComponent<GraphicRaycaster>(), Is.Not.Null);
            Assert.That(controller.hud.GetComponent<CanvasScaler>(), Is.Not.Null);
            var input = Components<EventSystem>()[0].GetComponent<InputSystemUIInputModule>();
            Assert.That(input, Is.Not.Null);
            Assert.That(input.actionsAsset, Is.Not.Null);
            Assert.That(AssetDatabase.Contains(input.actionsAsset), Is.True,
                "UI input actions must survive a scene reload as an imported asset.");
            foreach (var required in new[] {
                ("point", input.point), ("leftClick", input.leftClick), ("move", input.move),
                ("submit", input.submit), ("cancel", input.cancel) })
            {
                InputActionReference reference = required.Item2;
                Assert.That(reference, Is.Not.Null, "Missing UI action: " + required.Item1);
                Assert.That(AssetDatabase.Contains(reference), Is.True,
                    "UI action must be a saved asset/subasset: " + required.Item1);
                Assert.That(reference.action, Is.Not.Null, "Unresolved UI action: " + required.Item1);
                Assert.That(reference.action.actionMap.asset, Is.SameAs(input.actionsAsset),
                    "UI action must belong to the assigned actions asset: " + required.Item1);
            }
        }

        [Test]
        public void ReusableAssembliesRemainConnectedToPrefabs()
        {
            AssertPrefab(world.PlayerRoot.gameObject, "Shopkeeper.prefab");
            AssertPrefab(world.Customer, "Customer.prefab");
            AssertPrefab(world.CounterPose.gameObject, "Counter.prefab");
            AssertPrefab(world.DoorCollider.gameObject, "ShopDoor.prefab");
            AssertPrefab(controller.hud.gameObject, "TradingHUD.prefab");
            for (int i = 0; i < world.SlotColliders.Length; i++)
            {
                AssertPrefab(world.SlotColliders[i].gameObject, "DisplaySlot.prefab");
                Assert.That(world.SlotAnchors[i].parent, Is.EqualTo(world.SlotColliders[i].transform.parent),
                    "Each pad and item anchor must belong to the same reusable slot instance.");
            }
            foreach (var loot in world.LootColliders)
                AssertPrefab(loot.gameObject, "FreshLootPickup.prefab");
        }

        [Test]
        public void ItemsMaterialsAndAudioAreSavedAssets()
        {
            foreach (var prefab in world.ItemPrefabs)
            {
                Assert.That(PrefabUtility.IsPartOfPrefabAsset(prefab), Is.True);
                Assert.That(AssetDatabase.GetAssetPath(prefab), Does.EndWith(".prefab"));
                Assert.That(prefab.GetComponent<Renderer>(), Is.Not.Null);
                Assert.That(prefab.GetComponentsInChildren<Collider>(true).All(c => c.enabled && c.isTrigger), Is.True,
                    "Displayed items must be raycast targets without obstructing player movement.");
            }
            Assert.That(world.HeldItem.GetComponentsInChildren<Collider>(true).All(c => !c.enabled), Is.True,
                "The held item must not obstruct aiming or movement.");
            foreach (var material in world.ItemMaterials)
                Assert.That(AssetDatabase.GetAssetPath(material), Does.EndWith(".mat"));
            foreach (var renderer in Components<Renderer>())
                foreach (var material in renderer.sharedMaterials)
                    Assert.That(AssetDatabase.Contains(material), Is.True,
                        renderer.name + " must reference a material asset.");
            Assert.That(AssetDatabase.GetAssetPath(controller.placeSound), Does.EndWith(".wav"));
            Assert.That(AssetDatabase.GetAssetPath(controller.saleSound), Does.EndWith(".wav"));
        }

        [Test]
        public void HudHasAllEditableControlsAndSerializedReferences()
        {
            var hud = controller.hud;
            Assert.That(hud.categoryButtons, Has.Length.EqualTo(6));
            Assert.That(hud.priceButtons, Has.Length.EqualTo(5));
            Assert.That(hud.contextPanel, Is.Not.Null);
            Assert.That(hud.staminaFill, Is.Not.Null);
            var serialized = new SerializedObject(hud);
            var property = serialized.GetIterator();
            while (property.NextVisible(true))
                if (property.propertyType == SerializedPropertyType.ObjectReference)
                    Assert.That(property.objectReferenceValue, Is.Not.Null,
                        "Unassigned HUD field: " + property.propertyPath);
            foreach (var button in hud.GetComponentsInChildren<Button>(true))
                Assert.That(button.targetGraphic, Is.Not.Null, button.name + " needs an authored target graphic.");
        }

        [Test]
        public void ShopkeeperUsesIndependentCharacterPrefabVariant()
        {
            var shopkeeper = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Trading/Prefabs/Shopkeeper.prefab");
            Assert.That(PrefabUtility.GetPrefabAssetType(shopkeeper), Is.EqualTo(PrefabAssetType.Variant));
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Prefabs/FirstPersonPlayer.prefab");
            Assert.That(player, Is.Not.Null);
            Assert.That(player.GetComponentInChildren<TradingPrototype>(), Is.Null);
            Assert.That(player.GetComponentInChildren<TradingSceneBindings>(), Is.Null);
            var motor = player.GetComponent<FirstPersonMotor>();
            Assert.That(motor, Is.Not.Null);
            Assert.That(motor.control, Is.Not.Null);
            Assert.That(motor.input, Is.Not.Null);
            Assert.That(motor.stamina, Is.Not.Null);
            Assert.That(motor.view.viewCamera.transform.parent, Is.EqualTo(motor.view.pitchPivot));
            Assert.That(world.Movement.gameObject, Is.EqualTo(world.PlayerRoot.gameObject));
            Assert.That(world.Control, Is.SameAs(world.Movement.control));
            Assert.That(world.Stamina, Is.SameAs(world.Movement.stamina));
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Character/Scenes/MovementPlayground.unity"), Is.Not.Null);
        }

        [Test]
        public void InteractionTargetsAndMinimalHudAreAuthored()
        {
            foreach (var slot in world.SlotColliders)
                Assert.That(slot.GetComponentInParent<TradingInteractionTarget>().kind, Is.EqualTo(TradingTargetKind.DisplaySlot));
            Assert.That(world.DoorCollider.GetComponentInParent<TradingInteractionTarget>().kind, Is.EqualTo(TradingTargetKind.Door));
            foreach (var loot in world.LootColliders)
                Assert.That(loot.GetComponentInParent<TradingInteractionTarget>().kind, Is.EqualTo(TradingTargetKind.Loot));
            var hud = controller.hud;
            Assert.That(hud.interactionPanel.gameObject.activeSelf, Is.False);
            foreach (var text in hud.GetComponentsInChildren<TMP_Text>(true))
            {
                Assert.That(text.text.ToLowerInvariant(), Does.Not.Contain("wasd"));
                Assert.That(text.text.ToLowerInvariant(), Does.Not.Contain("колесо"));
                Assert.That(text.text.ToLowerInvariant(), Does.Not.Contain("осматриваться"));
            }
            Assert.That(hud.layoutRoot.Find("Header"), Is.Null);
            Assert.That(hud.layoutRoot.Find("Slot 1 label"), Is.Null);
        }

        [Test]
        public void SpriteCustomersHaveCompleteAppearancesAndAnAuthoredRoute()
        {
            var presenter = world.Customers;
            Assert.That(presenter, Is.Not.Null);
            Assert.That(presenter.actor.gameObject, Is.EqualTo(world.Customer));
            Assert.That(presenter.appearances.Length, Is.GreaterThanOrEqualTo(2));
            var actor = presenter.actor;
            Assert.That(actor.viewer, Is.EqualTo(world.ViewCamera.transform));
            Assert.That(actor.route.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(actor.route.All(p => p && !p.IsChildOf(actor.transform)), Is.True);
            Assert.That(actor.GetComponentsInChildren<MeshRenderer>(), Is.Empty,
                "Customers must be sprites rather than the old cube proxy.");
            Assert.That(AssetDatabase.GetAssetPath(actor.spriteRenderer.sharedMaterial),
                Is.EqualTo("Assets/Rendering/Materials/HandDrawnSprite.mat"));
            Assert.That(actor.spriteRenderer.sharedMaterial.HasProperty("_SpriteUVRect"), Is.True);
            foreach (var appearance in presenter.appearances)
            {
                Assert.That(AssetDatabase.Contains(appearance), Is.True);
                Assert.That(appearance.IsComplete, Is.True);
                foreach (var sprite in new[] { appearance.neutral, appearance.back, appearance.displeased, appearance.happy, appearance.angry })
                {
                    Assert.That(sprite.rect.size, Is.EqualTo(new Vector2(1199, 1312)));
                    Assert.That(sprite.pivot, Is.EqualTo(new Vector2(599.5f, 0)));
                    Assert.That(sprite.pixelsPerUnit, Is.EqualTo(100));
                    Assert.That(sprite.texture.width, Is.EqualTo(3597));
                    Assert.That(sprite.texture.height, Is.EqualTo(2624));
                }
            }
        }

        [Test]
        public void SceneAndPrefabsHaveNoMissingScriptsOrMigrationDependency()
        {
            foreach (var transform in Components<Transform>())
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject), Is.Zero,
                    "Missing script on scene object " + transform.name);
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Trading/Prefabs" }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                    Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject), Is.Zero,
                        "Missing script in prefab " + prefab.name + "/" + transform.name);
            }
            foreach (var relative in new[] {
                "Trading/Runtime/TradingWorld.cs", "Trading/Editor/TradingAssetMigration.cs",
                "Trading/Editor/TradingHudAuthoring.cs" })
                Assert.That(File.Exists(Path.Combine(Application.dataPath, relative)), Is.False,
                    "Remove the temporary/runtime builder after saving assets: " + relative);
        }

        T[] Components<T>() where T : Component => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

        static void AssertPrefab(GameObject instance, string expectedFile)
        {
            Assert.That(PrefabUtility.GetPrefabInstanceStatus(instance), Is.EqualTo(PrefabInstanceStatus.Connected), instance.name);
            var path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance);
            Assert.That(path, Does.EndWith("/" + expectedFile), instance.name + " must preserve its reusable prefab link.");
        }
    }
}
