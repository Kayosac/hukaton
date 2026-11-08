using System.Linq;
using Hakaton.Character;
using Hakaton.ShopBlockout;
using Hakaton.Trading.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Hakaton.Trading.Tests
{
    /// <summary>Checks the saved shop integration rather than invoking an authoring tool.</summary>
    public sealed class ShopBlockoutIntegrationTests
    {
        const string ScenePath = "Assets/ShopBlockout/Scenes/ShopBlockout.unity";
        Scene scene;
        bool openedHere;
        TradingPrototype trade;
        TradingSceneBindings world;

        [SetUp]
        public void OpenSavedShop()
        {
            scene = SceneManager.GetSceneByPath(ScenePath);
            openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            trade = Components<TradingPrototype>().Single();
            world = trade.World;
        }

        [TearDown]
        public void CloseOnlySceneOpenedByFixture()
        {
            if (openedHere && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }

        [Test]
        public void ShopUsesSavedCounterDisplayAndCameraReferencesBeforePlay()
        {
            Assert.That(trade.Session, Is.Null);
            Assert.That(world.ValidateReferences(), Is.True);
            Assert.That(world.ShopView, Is.Not.Null);
            Assert.That(world.ShopView.IsConfigured, Is.True);
            Assert.That(world.ShopView.modal.viewCamera, Is.SameAs(world.ViewCamera));
            Assert.That(world.ShopView.modal.control, Is.SameAs(world.Control));
            Assert.That(world.ShopView.modal.blinkOverlay, Is.Not.Null);
            Assert.That(Vector3.Distance(world.ShopView.counterCamera.position, world.ShopView.displayCamera.position), Is.GreaterThan(.5f));
            Assert.That(world.DealItemAnchor, Is.Not.Null);
            Assert.That(world.openFromDisplaySlots, Is.False, "Only the counter opens the compact shop.");
            Assert.That(world.SlotAnchors.Length, Is.EqualTo(6));
            Assert.That(world.SlotAnchors.Distinct().Count(), Is.EqualTo(6));
            Assert.That(world.SlotColliders.Distinct().Count(), Is.EqualTo(6));
            GameObject counter = PrefabUtility.GetOutermostPrefabInstanceRoot(world.DealItemAnchor.gameObject);
            Assert.That(counter, Is.Not.Null);
            GameObject display = PrefabUtility.GetOutermostPrefabInstanceRoot(world.SlotAnchors[0].gameObject);
            Assert.That(display, Is.Not.Null);
            Assert.That(display, Is.Not.SameAs(counter), "The assortment belongs to the separate customer-side display.");
            foreach (var anchor in world.SlotAnchors)
            {
                Assert.That(anchor.IsChildOf(display.transform), Is.True);
                Assert.That(anchor.IsChildOf(counter.transform), Is.False);
                Assert.That(Vector3.Distance(anchor.position, world.DealItemAnchor.position), Is.GreaterThan(.3f));
            }
            foreach (var root in new[] { world.PlayerRoot.gameObject, world.Customers.actor.gameObject, trade.hud.gameObject })
                Assert.That(PrefabUtility.IsPartOfPrefabInstance(root), Is.True, root.name + " must retain its prefab connection.");
        }

        [Test]
        public void CustomerAndBackroomServicesShareAuthoredAssetsAndOneWallet()
        {
            var actor = world.Customers.actor;
            Assert.That(actor.viewer, Is.SameAs(world.ViewCamera.transform));
            Assert.That(actor.route.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(actor.route.All(point => point && point.gameObject.scene == scene && !point.IsChildOf(actor.transform)), Is.True);
            Assert.That(Vector3.Distance(actor.route[0].position, actor.route[actor.route.Length - 1].position), Is.GreaterThan(.5f));
            Assert.That(world.Customers.appearances.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(world.Customers.appearances.All(appearance => appearance && appearance.IsComplete && AssetDatabase.Contains(appearance)), Is.True);
            Assert.That(AssetDatabase.GetAssetPath(actor.spriteRenderer.sharedMaterial), Is.EqualTo("Assets/Rendering/Materials/HandDrawnSprite.mat"));

            Assert.That(trade.walletHost, Is.Not.Null);
            Assert.That(trade.services, Is.Not.Null);
            var workshop = trade.services.workshop;
            Assert.That(workshop, Is.Not.Null);
            Assert.That(workshop.walletHost, Is.SameAs(trade.walletHost));
            Assert.That(workshop.control, Is.SameAs(world.Control));
            Assert.That(workshop.embeddedMode, Is.True);
            Assert.That(workshop.embeddedVisibility, Is.Not.Null);
            Assert.That(workshop.embeddedCloseButton, Is.Not.Null);
            var goblin = trade.services.goblin;
            Assert.That(goblin, Is.Not.Null);
            Assert.That(goblin.goblinRoot, Is.Not.Null);
            Assert.That(goblin.stowedPose, Is.Not.Null);
            Assert.That(goblin.deployedPose, Is.Not.Null);
            Assert.That(goblin.hatchHinge, Is.Not.Null);
            Assert.That(goblin.pullHandle, Is.Not.Null);
            foreach (var cable in new[] { goblin.cableMeshes, goblin.cableTops, goblin.cableHarnesses })
                Assert.That(cable != null && cable.Length == 2 && cable.All(point => point), Is.True);
            foreach (var kind in new[] { ShopServiceKind.Computer, ShopServiceKind.GoblinRope, ShopServiceKind.Goblin })
                Assert.That(Components<ShopInteractionTarget>().Any(target => target.kind == kind), Is.True,
                    "Missing authored interaction for " + kind);
        }

        [Test]
        public void IntegratedSceneHasOneCameraListenerAndEventSystemAndNoMissingScripts()
        {
            Assert.That(Components<Camera>(), Has.Length.EqualTo(1));
            Assert.That(Components<AudioListener>(), Has.Length.EqualTo(1));
            Assert.That(Components<EventSystem>(), Has.Length.EqualTo(1));
            Assert.That(Components<FirstPersonModalView>(), Has.Length.EqualTo(1));
            foreach (var transform in Components<Transform>())
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject), Is.Zero,
                    "Missing script on " + transform.name);
        }

        T[] Components<T>() where T : Component => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    }
}
