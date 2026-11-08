#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using Hakaton.Character;
using Hakaton.ShopBlockout;
using Hakaton.Trading.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Trading.Tests.PlayMode
{
    public sealed class ShopBlockoutPlayModeTests
    {
        TradingPrototype trade;
        TradingSceneBindings world;
        SpriteNpcActor actor;
        Mouse mouse, previousMouse;
        InputSettings.EditorInputBehaviorInPlayMode previousInputBehavior;
        InputSettings.BackgroundBehavior previousBackgroundBehavior;
        GameObject obstruction;
        CursorLockMode previousCursorLock;
        bool previousCursorVisible;

        [UnitySetUp]
        public IEnumerator LoadSavedShop()
        {
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            previousMouse = Mouse.current;
            previousInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            mouse = InputSystem.AddDevice<Mouse>();
            yield return SceneManager.LoadSceneAsync("Assets/ShopBlockout/Scenes/ShopBlockout.unity", LoadSceneMode.Single);
            trade = SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<TradingPrototype>(true)).Single();
            world = trade.World;
            actor = world.Customers.actor;
            actor.walkSpeed = 10;
            actor.reactionDuration = .2f;
            actor.turnDuration = .15f;
            world.ShopView.categoryReadSeconds = 0;
            trade.services.goblin.hatchSeconds = .04f;
            trade.services.goblin.lowerSeconds = .08f;
            yield return null;
            Assert.That(trade.enabled, Is.True);
            Assert.That(trade.Session, Is.Not.Null);
            Assert.That(trade.services.workshop.EmbeddedOpen, Is.False);
        }

        [UnityTearDown]
        public IEnumerator RestoreInputAndCloseOwnedModals()
        {
            if (trade)
            {
                trade.Session?.Close();
                if (trade.services) trade.services.Close();
                if (world && world.ShopView && world.ShopView.modal) world.ShopView.modal.enabled = false;
            }
            if (obstruction) UnityEngine.Object.Destroy(obstruction);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
            InputSystem.settings.editorInputBehaviorInPlayMode = previousInputBehavior;
            InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
            yield return null;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }

        [UnityTest]
        public IEnumerator CategoryVisitUsesPhysicalDisplayThenSaleFundsGoblinCardPurchase()
        {
            var item = StartVisit(true);
            int beforeGold = trade.Session.Gold;
            float beforeTime = trade.Session.RemainingSeconds;
            yield return WaitFor(() => trade.SelectingFromDisplay && trade.ActionsReady);
            Assert.That(world.ShopView.modal.CurrentPose, Is.SameAs(world.ShopView.displayCamera));
            Assert.That(trade.Session.RemainingSeconds, Is.LessThan(beforeTime), "The timer must continue during the camera transition and selection.");
            var displayed = OnlyDisplayedItem();
            ClickPhysicalItem(displayed);
            Assert.That(trade.Session.CurrentDeal, Is.Not.Null, "A real camera ray and left click must select the displayed object.");
            Assert.That(trade.Session.CurrentDeal.Item, Is.SameAs(item));
            yield return WaitFor(() => trade.ActionsReady && !trade.SelectingFromDisplay);
            Assert.That(world.ShopView.modal.CurrentPose, Is.SameAs(world.ShopView.counterCamera));
            yield return WaitFor(() => At(displayed, world.DealItemAnchor));
            SellAtRequestedReferencePrice();
            Assert.That(trade.Session.Gold, Is.EqualTo(beforeGold + item.Definition.RecommendedPrice));
            Assert.That(actor.CurrentExpression, Is.EqualTo(NpcExpression.Happy));
            yield return WaitFor(() => !world.ShopView.modal.Active);
            Assert.That(trade.Session.IsOpen, Is.False);

            var services = trade.services;
            Assert.That(services.TryInteract(ShopServiceKind.GoblinRope), Is.True);
            yield return WaitFor(() => services.goblin.IsDeployed);
            Assert.That(services.TryInteract(ShopServiceKind.GoblinRope), Is.True);
            var workshop = services.workshop;
            Assert.That(workshop.Market, Is.True);
            Assert.That(workshop.Model.Coins, Is.EqualTo(trade.Session.Gold));
            int copies = workshop.Model.Owned.Count;
            int cost = workshop.config.Find(workshop.SelectedDefinitionId).purchasePrice;
            int beforePurchase = trade.Session.Gold;
            Assert.That(beforePurchase, Is.GreaterThanOrEqualTo(cost));
            workshop.equipButton.onClick.Invoke();
            Assert.That(workshop.Model.Owned.Count, Is.EqualTo(copies + 1));
            Assert.That(trade.Session.Gold, Is.EqualTo(beforePurchase - cost));
            Assert.That(workshop.Model.Coins, Is.EqualTo(trade.Session.Gold), "Trading and cards must debit the same wallet.");
            workshop.embeddedCloseButton.onClick.Invoke();
            Assert.That(world.Control.HasModalOwner, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RefusalReturnsTheSameUnsoldInstanceFromCounterToItsSlot()
        {
            var item = StartVisit(false);
            int beforeGold = trade.Session.Gold;
            yield return WaitFor(() => trade.ActionsReady);
            var displayed = OnlyDisplayedItem();
            yield return WaitFor(() => At(displayed, world.DealItemAnchor));
            trade.RefuseCustomer(trade.Session.CurrentCustomer.Id);
            Assert.That(trade.Session.Slots[0], Is.SameAs(item));
            Assert.That(trade.Session.Gold, Is.EqualTo(beforeGold));
            Assert.That(actor.CurrentExpression, Is.EqualTo(NpcExpression.Angry));
            yield return WaitFor(() => At(displayed, world.SlotAnchors[0]));
            Assert.That(OnlyDisplayedItem(), Is.SameAs(displayed));
            trade.ToggleShop();
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ClosingDuringDisplayBlinkRestoresFreeControlAndKeepsStock()
        {
            var item = StartVisit(true);
            yield return WaitFor(() => trade.SelectingFromDisplay && trade.ViewTransitioning);
            trade.Session.Tick(trade.Session.RemainingSeconds + 1);
            Assert.That(trade.Session.IsOpen, Is.True);
            Assert.That(trade.Session.CurrentCustomer, Is.Not.Null, "An active category conversation survives the timer reaching zero.");
            trade.ToggleShop();
            Assert.That(trade.Session.IsOpen, Is.False);
            Assert.That(trade.Session.Slots[0], Is.SameAs(item));
            yield return WaitFor(() => !world.ShopView.modal.Active);
            Assert.That(world.Control.HasModalOwner, Is.False);
            Assert.That(world.ShopView.modal.blinkOverlay.alpha, Is.Zero);
            Assert.That(world.ShopView.modal.blinkOverlay.blocksRaycasts, Is.False);
            Assert.That(world.ViewCamera.fieldOfView, Is.EqualTo(75).Within(.01f));
            Assert.That(world.PlayerController.enabled, Is.True);
            Assert.That(world.Movement.enabled, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ZeroTimerStillAllowsCategorySelectionAndFinishingTheExistingDeal()
        {
            var item = StartVisit(true);
            int before = trade.Session.Gold;
            yield return WaitFor(() => trade.SelectingFromDisplay && trade.ActionsReady);
            long customer = trade.Session.CurrentCustomer.Id;
            trade.Session.Tick(trade.Session.RemainingSeconds + 1);
            trade.SelectCustomerItem(0, customer);
            Assert.That(trade.Session.CurrentDeal, Is.Not.Null);
            yield return WaitFor(() => trade.ActionsReady && !trade.SelectingFromDisplay);
            SellAtRequestedReferencePrice();
            Assert.That(trade.Session.Gold, Is.EqualTo(before + item.Definition.RecommendedPrice));
            Assert.That(trade.Session.IsOpen, Is.False);
            Assert.That(trade.Session.CurrentCustomer, Is.Null);
            yield return WaitFor(() => !actor.IsBusy && !world.ShopView.modal.Active);
            Assert.That(trade.Session.CurrentCustomer, Is.Null, "No subsequent visitor may start after the timer expires.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator EveryStockSlotIsVisibleAndClickableFromTheDisplayView()
        {
            for (int i=0;i<6;i++) Assert.That(trade.Session.PlaceItem(0,i), Is.True);
            yield return null;
            var camera=world.ViewCamera;
            camera.transform.SetPositionAndRotation(world.ShopView.displayCamera.position,world.ShopView.displayCamera.rotation);
            camera.fieldOfView=world.ShopView.displayFov;
            Physics.SyncTransforms();
            foreach (Transform item in world.SoldItemsRoot)
            {
                Vector3 screen=camera.WorldToScreenPoint(item.GetComponentInChildren<Renderer>().bounds.center);
                Assert.That(screen.z, Is.GreaterThan(0),item.name);
                Assert.That(camera.pixelRect.Contains(screen), Is.True,item.name);
                Assert.That(trade.hud.ContainsBlockingPanel(screen), Is.False,item.name);
                Assert.That(Physics.Raycast(camera.ScreenPointToRay(screen),out var hit,4,~(1<<2),QueryTriggerInteraction.Collide),Is.True,item.name);
                Assert.That(hit.collider.transform.IsChildOf(item),Is.True,$"{item.name} is obscured by {hit.collider.name}");
            }
        }

        [UnityTest]
        public IEnumerator OutOfReachOrOccludedDisplayedItemCannotRemainAnInteractionTarget()
        {
            Assert.That(trade.Session.PlaceItem(0, 0), Is.True);
            yield return null;
            var displayed = OnlyDisplayedItem();
            Vector3 center = displayed.GetComponentInChildren<Renderer>().bounds.center;
            var cameraTransform = world.ViewCamera.transform;
            Vector3 originalPosition = cameraTransform.position;
            Quaternion originalRotation = cameraTransform.rotation;
            var target = world.SlotColliders[0].GetComponentInParent<TradingInteractionTarget>();
            float originalReach = target.reach;
            cameraTransform.SetPositionAndRotation(center + Vector3.left * .65f, Quaternion.LookRotation(Vector3.right));
            Physics.SyncTransforms();
            var ray = world.ViewCamera.ViewportPointToRay(new Vector3(.5f, .5f));
            Assert.That(Physics.Raycast(ray, out var hit, 5, ~(1 << 2), QueryTriggerInteraction.Collide), Is.True);
            Assert.That(hit.collider.transform.IsChildOf(displayed.transform), Is.True, "The test must first hit the physical stock object.");
            target.reach = .05f;
            trade.SendMessage("UpdateHover", SendMessageOptions.RequireReceiver);
            Assert.That(trade.HoveredSlot, Is.EqualTo(-1));
            Assert.That(trade.ContextPrompt, Is.Empty);
            target.reach = 1.5f;
            trade.SendMessage("UpdateHover", SendMessageOptions.RequireReceiver);
            Assert.That(trade.HoveredSlot, Is.Zero);

            // Disposable collision data verifies first-hit occlusion without editing the authored room.
            obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstruction.transform.position = cameraTransform.position + Vector3.right * .25f;
            obstruction.transform.localScale = Vector3.one * .12f;
            Physics.SyncTransforms();
            trade.SendMessage("UpdateHover", SendMessageOptions.RequireReceiver);
            Assert.That(trade.HoveredSlot, Is.EqualTo(-1));
            Assert.That(trade.ContextPrompt, Is.Empty);
            target.reach = originalReach;
            cameraTransform.SetPositionAndRotation(originalPosition, originalRotation);
            LogAssert.NoUnexpectedReceived();
        }

        ItemInstance StartVisit(bool category)
        {
            Assert.That(trade.Session.PlaceItem(0, 0), Is.True);
            var item = trade.Session.Slots[0];
            Assert.That(trade.Session.Open(), Is.True);
            // Replace the automatic visitor synchronously, before presentation sees it, with explicit test terms.
            Assert.That(trade.Session.Refuse(trade.Session.CurrentCustomer.Id), Is.True);
            Assert.That(trade.Session.AdvanceCustomer(new CustomerTerms {
                Name = "Проверка лавки", RequestedSlot = category ? -1 : 0,
                RequestedCategory = category ? item.Definition.Category : null,
                AsksPrice = true, OpeningRatio = .8f, MaxPrice = item.Definition.RecommendedPrice * 2, Patience = 5
            }), Is.True);
            return item;
        }

        Transform OnlyDisplayedItem()
        {
            Assert.That(world.SoldItemsRoot.childCount, Is.EqualTo(1), "The fixture places one stock instance.");
            return world.SoldItemsRoot.GetChild(0);
        }

        void ClickPhysicalItem(Transform item)
        {
            Physics.SyncTransforms();
            Vector3 screen = world.ViewCamera.WorldToScreenPoint(item.GetComponentInChildren<Renderer>().bounds.center);
            Assert.That(screen.z, Is.GreaterThan(0));
            Assert.That(world.ViewCamera.pixelRect.Contains(screen), Is.True, "The selected stock must be visible from the authored B pose.");
            Assert.That(trade.hud.ContainsBlockingPanel(screen), Is.False, "The selection UI must leave the physical assortment clickable.");
            mouse.MakeCurrent();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screen, buttons = 1 });
            InputSystem.Update();
            trade.SendMessage("UpdateDisplaySelection", SendMessageOptions.RequireReceiver);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screen });
            InputSystem.Update();
        }

        void SellAtRequestedReferencePrice()
        {
            var deal = trade.Session.CurrentDeal;
            Assert.That(deal, Is.Not.Null);
            Assert.That(trade.ActionsReady, Is.True);
            trade.OfferPrice(deal.Id, deal.Revision, 0);
            Assert.That(trade.Session.LastDeparture.Outcome, Is.EqualTo(CustomerOutcome.Purchased));
        }

        static bool At(Transform item, Transform anchor) => item && Vector3.Distance(item.position, anchor.position) < .002f;

        static IEnumerator WaitFor(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + 8;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, "Timed out waiting for the saved shop integration.");
        }
    }
}
#endif
