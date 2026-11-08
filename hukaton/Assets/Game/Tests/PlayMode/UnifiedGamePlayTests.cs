#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hakaton.Character;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions;
using Hakaton.ShopBlockout;
using Hakaton.Trading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Game.Tests
{
    public sealed class UnifiedGamePlayTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";
        GameLoop game;
        ExpeditionWorld world;
        CursorLockMode previousCursorLock;
        bool previousCursorVisible;
        Scene loadedScene;
        readonly List<string> ownedSavePaths=new List<string>();

        [UnitySetUp]
        public IEnumerator LoadAuthoredGameWithoutReadingOrWritingPlayerProgress()
        {
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            // sceneLoaded runs after Awake and before Start. Disabling persistence after
            // yielding LoadSceneAsync would allow Start to read the real user's save.
            UnityAction<Scene,LoadSceneMode> preventPersistence = (scene,mode) => {
                if(scene.path != ScenePath)return;
                loadedScene=scene;
                game=scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<GameLoop>(true)).Single();
                game.persistProgress=false;
                game.workshop.persistProgress=false;
            };
            SceneManager.sceneLoaded+=preventPersistence;
            try { yield return SceneManager.LoadSceneAsync(ScenePath,LoadSceneMode.Single); }
            finally { SceneManager.sceneLoaded-=preventPersistence; }
            Assert.That(game,Is.Not.Null);
            yield return WaitFor(()=>game.Ready,"GameLoop.Start must bind the saved systems.");
            world=game.expedition;
            var actor=game.trade.World.Customers.actor;
            actor.walkSpeed=10;
            actor.reactionDuration=.12f;
            actor.turnDuration=.12f;
            game.trade.World.ShopView.categoryReadSeconds=0;
            game.services.goblin.hatchSeconds=.04f;
            game.services.goblin.lowerSeconds=.08f;
            Assert.That(game.wallet.Wallet.Balance,Is.Zero,"A test must start fresh, never load the player's balance.");
            Assert.That(game.trade.Session.TotalStockCount,Is.Zero);
            Assert.That(game.workshop.Model.Owned.Count,Is.EqualTo(15));
            Assert.That(game.Phase,Is.EqualTo(GamePhase.Shop));
        }

        [UnityTearDown]
        public IEnumerator ReleaseTestOwnedState()
        {
            if(game)
            {
                game.persistProgress=false;
                game.workshop.persistProgress=false;
                game.services.Close();
                game.trade.Session.Close();
                if(game.trade.World.ShopView.modal)game.trade.World.ShopView.modal.enabled=false;
                if(world && world.control)world.control.SetBlocked(world,false);
            }
            yield return null;
            Cursor.lockState=previousCursorLock;
            Cursor.visible=previousCursorVisible;
            // Only files registered under this test's unique save name are removed.
            foreach(string path in ownedSavePaths)
                foreach(string suffix in new[]{"",".bak",".tmp"})
                    if(File.Exists(path+suffix))File.Delete(path+suffix);
            ownedSavePaths.Clear();
        }

        [UnityTest]
        public IEnumerator OneAuthoredPlayerChangesPhaseAndEmptyDeckCannotConsumeAnOuting()
        {
            AssertOnePlayer();
            var cards=game.workshop.Model;
            var exploration=cards.Selected(DeckKind.Exploration).Select(c=>c.Id).ToArray();
            foreach(int id in exploration)Assert.That(cards.Toggle(id),Is.Null);
            Assert.That(game.TryDepart(),Is.False);
            Assert.That(game.trade.Session.ExpeditionId,Is.Zero);
            Assert.That(cards.Journeys,Is.Zero);
            Assert.That(game.Phase,Is.EqualTo(GamePhase.Shop));
            Assert.That(game.LastNotice,Does.Contain("исследования"));
            foreach(int id in exploration)Assert.That(cards.Toggle(id),Is.Null);
            yield return Depart();
            AssertOnePlayer();
            Assert.That(game.player,Is.SameAs(world.motor));
            Assert.That(game.player.control,Is.SameAs(game.trade.World.Control));
            Assert.That(world.control,Is.SameAs(game.workshop.control));
            Assert.That(game.player.walkSpeed,Is.EqualTo(game.worldWalkSpeed));
            Assert.That(game.TryDepart(),Is.False,"One physical outing must own one trading token.");
            yield return ReturnWithCard();
            AssertOnePlayer();
            Assert.That(game.player.walkSpeed,Is.EqualTo(game.shopWalkSpeed));
            Assert.That(Vector3.Distance(game.player.transform.position,world.shopPose.position),Is.LessThan(.4f));
            Assert.That(game.workshop.EmbeddedOpen,Is.False);
            Assert.That(world.control.HasModalOwner,Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PhysicalLootReturnsToShopSalesFundGoblinPurchaseAndNextOutingKeepsTheCopy()
        {
            var cards=game.workshop.Model;
            int initialCopies=cards.Owned.Count;
            yield return Depart();
            yield return Capture("unified-expedition");
            var finds=world.finds.Where(f=>f.gameObject.activeInHierarchy).ToArray();
            Assert.That(finds.Length,Is.EqualTo(6),"Four common and two guaranteed special finds belong to this preset.");
            int expectedRevenue=finds.Sum(f=>f.value);
            foreach(var find in finds)
            {
                Assert.That(find.PickUp(),Is.True);
                Assert.That(find.PickUp(),Is.False,"A picked scene finding cannot pay twice.");
            }
            Assert.That(world.Loot.Sum(x=>x.count),Is.EqualTo(6));
            yield return ReturnWithCard();
            Assert.That(game.trade.Session.Inventory.Count,Is.EqualTo(6));
            Assert.That(cards.Goods,Is.Empty,"There is only one authoritative inventory in the unified game.");
            Assert.That(game.trade.Session.RemainingSeconds,Is.EqualTo(game.trade.shiftSeconds).Within(.01f));
            Assert.That(cards.Owned.Count,Is.EqualTo(initialCopies));
            for(int i=0;i<finds.Length;i++)yield return SellInventoryItemAtReferencePrice(i==0);
            Assert.That(game.trade.Session.TotalStockCount,Is.Zero);
            Assert.That(game.wallet.Wallet.Balance,Is.EqualTo(expectedRevenue));
            Assert.That(game.trade.Session.GrossRevenue,Is.EqualTo(expectedRevenue));
            Assert.That(cards.Coins,Is.EqualTo(expectedRevenue));

            Assert.That(game.services.TryInteract(ShopServiceKind.GoblinRope),Is.True);
            yield return WaitFor(()=>game.services.goblin.IsDeployed,"The authored goblin must finish its descent.");
            Assert.That(game.services.TryInteract(ShopServiceKind.GoblinRope),Is.True);
            Assert.That(game.workshop.Market,Is.True);
            int cost=game.workshop.config.Find(game.workshop.SelectedDefinitionId).purchasePrice;
            Assert.That(expectedRevenue,Is.GreaterThanOrEqualTo(cost));
            game.workshop.equipButton.onClick.Invoke();
            var purchased=cards.Owned.Single(c=>c.Id==cards.Owned.Max(x=>x.Id));
            Assert.That(cards.Owned.Count,Is.EqualTo(initialCopies+1));
            Assert.That(cards.IsSelected(purchased.Id),Is.False);
            Assert.That(game.trade.Session.Gold,Is.EqualTo(expectedRevenue-cost));
            Assert.That(game.wallet.Wallet.Balance,Is.EqualTo(cards.Coins));
            Assert.That(game.trade.Session.GrossRevenue,Is.EqualTo(expectedRevenue),"Spending does not reverse earned revenue.");
            game.workshop.embeddedCloseButton.onClick.Invoke();
            Assert.That(world.control.HasModalOwner,Is.False);
            Assert.That(cards.Toggle(purchased.Id),Is.Null);
            yield return Capture("unified-shop-returned");
            yield return Depart();
            Assert.That(cards.Find(purchased.Id),Is.Not.Null);
            Assert.That(cards.Current.Get(purchased.Spec.Kind).Total,Is.EqualTo(cards.Selected(purchased.Spec.Kind).Count));
            Assert.That(world.finds.All(f=>!f.Taken),Is.True);
            yield return ReturnWithCard();
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator AuthoredNavigationAndBaseImpulseReachTheLedgeWithoutUnsafeFallDamage()
        {
            yield return Depart();
            Assert.That(world.creatures.Length,Is.EqualTo(5));
            foreach(var creature in world.creatures)
                Assert.That(creature.navigator.isOnNavMesh,Is.True,creature.name+" must start on the newly authored navigation surface.");
            Assert.That(game.player.gravity,Is.EqualTo(9.81f).Within(.001f));
            Assert.That(world.settings.useCardEffectValues,Is.True);
            var impulse=game.workshop.config.cards.Single(c=>c.action==CardAction.Wind);
            var copy=game.workshop.Model.Owned.First(c=>c.Spec.Id==impulse.id && c.Level==1);
            Assert.That(impulse.ImpulseAtLevel(copy.Level),Is.EqualTo(8).Within(.001f));
            Physics.SyncTransforms();
            Assert.That(Physics.Raycast(new Vector3(0,8,0),Vector3.down,out var ground,20,~(1<<2),QueryTriggerInteraction.Ignore),Is.True);
            Assert.That(Vector3.Dot(ground.normal,Vector3.up),Is.GreaterThan(.99f),"The reference launch must use level ground.");
            var ledge=game.expeditionGeometry.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Wind ledge").GetComponent<Collider>();
            Assert.That(ledge,Is.Not.Null);
            float ledgeHeight=ledge.bounds.max.y-ledge.bounds.min.y;
            Assert.That(ledgeHeight,Is.EqualTo(2.8f).Within(.05f));
            // Block manual movement only. The real motor still integrates the impulse,
            // gravity, contact and landing damage; no test replaces its physics.
            world.control.SetBlocked(game,true);
            try
            {
                game.player.Teleport(ground.point+Vector3.up*.03f,Quaternion.identity);
                yield return WaitFor(()=>game.player.IsGrounded,"The reference launch must first settle on its authored ground collider.");
                float baseY=game.player.transform.position.y;
                int hearts=world.vitals.HalfHearts;
                world.ApplyImpact(copy,impulse,ground.collider,game.player.transform.position,Vector3.up);
                Assert.That(game.player.VerticalSpeed,Is.EqualTo(8).Within(.01f));
                float apex=baseY;
                bool airborne=false,landed=false;
                float simulationDeadline=Time.time+3;
                float wallDeadline=Time.realtimeSinceStartup+12;
                while(Time.time<simulationDeadline && Time.realtimeSinceStartup<wallDeadline)
                {
                    yield return null;
                    apex=Mathf.Max(apex,game.player.transform.position.y);
                    airborne|=!game.player.IsGrounded && game.player.transform.position.y>baseY+.05f;
                    if(airborne && game.player.IsGrounded){landed=true;break;}
                }
                float rise=apex-baseY;
                Assert.That(airborne,Is.True,"The physical impulse must lift the CharacterController off the ground.");
                Assert.That(landed,Is.True,"An 8 m/s vertical impulse must land again within three simulated seconds.");
                Assert.That(rise,Is.EqualTo(3.3f).Within(.5f),"Sample the actual apex each frame, not a position after a fixed wait.");
                Assert.That(ledgeHeight,Is.LessThanOrEqualTo(rise),"The saved 2.8 m ledge must fit below the base card's measured lift.");
                Assert.That(world.vitals.HalfHearts,Is.EqualTo(hearts),"Landing at the same elevation after a base impulse must remain below the fall-damage threshold.");
                Assert.That(Mathf.Abs(game.player.transform.position.y-baseY),Is.LessThan(.1f));
            }
            finally { world.control.SetBlocked(game,false); }
            yield return ReturnWithCard();
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator EmptyReturnCannotRestoreAnExpiredShiftOrDuplicateExistingStock()
        {
            yield return Depart();
            Assert.That(world.finds.First(f=>f.gameObject.activeInHierarchy).PickUp(),Is.True);
            yield return ReturnWithCard();
            var session=game.trade.Session;
            Assert.That(session.PlaceItem(0,0),Is.True);
            Assert.That(session.Open(),Is.True);
            session.Tick(session.RemainingSeconds+1);
            session.Close();game.trade.RefreshPresentation();
            Assert.That(session.RemainingSeconds,Is.Zero);
            long originalId=session.Slots[0].Id;
            yield return Depart();
            Assert.That(world.Loot,Is.Empty);
            yield return ReturnWithCard();
            Assert.That(session.RemainingSeconds,Is.Zero);
            Assert.That(session.TotalStockCount,Is.EqualTo(1));
            Assert.That(session.Slots[0].Id,Is.EqualTo(originalId));
            Assert.That(session.Open(),Is.False,"Fresh delivered loot is required to renew an expired shift.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator DefeatRetainsConfiguredLootOnceAndKeepsCardsAndCommonControl()
        {
            var cards=game.workshop.Model;
            var cardProgress=cards.Owned.Select(c=>$"{c.Id}:{c.Spec.Id}:{c.Level}").ToArray();
            int initialCopies=cards.Owned.Count;
            yield return Depart();
            long outing=game.trade.Session.ExpeditionId;
            var common=world.finds.Where(f=>f.itemId=="common_find" && f.gameObject.activeInHierarchy).ToArray();
            Assert.That(common.Length,Is.EqualTo(4));
            foreach(var find in common)Assert.That(find.PickUp(),Is.True);
            int expected=Mathf.FloorToInt(common.Length*(1-world.settings.defeatLootLoss));
            Assert.That(world.vitals.Hurt(100,Vector3.zero),Is.True);
            Assert.That(world.Returning,Is.True);
            Assert.That(game.TryDepart(),Is.False);
            yield return WaitFor(()=>game.Phase==GamePhase.Shop && !world.Returning,"Defeat must finish the real fade and return callback.");
            Assert.That(game.trade.Session.TotalStockCount,Is.EqualTo(expected));
            Assert.That(cards.Owned.Count,Is.EqualTo(initialCopies));
            Assert.That(cards.Owned.Select(c=>$"{c.Id}:{c.Spec.Id}:{c.Level}"),Is.EqualTo(cardProgress));
            Assert.That(world.vitals.HalfHearts,Is.EqualTo(world.settings.maximumHalfHearts));
            Assert.That(world.control.HasModalOwner,Is.False);
            world.ReturnToShop(true);
            game.workshop.CompleteOuting("Duplicate callback",new[]{new LootSave{id="common_find",name="Находка",count=expected,value=20}},true);
            Assert.That(game.trade.CompleteExternalExpedition(outing,new[]{new StockDelivery("common_find",expected)}),Is.False);
            Assert.That(game.trade.Session.TotalStockCount,Is.EqualTo(expected),"One outgoing token can only deliver once.");
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator GoalCelebrationWaitsForAVisibleClosedShopAndFreePlayContinues()
        {
            var seed=game.Capture();seed.shop.grossRevenue=game.revenueGoal;seed.shop.salesCount=10;seed.goalCelebrated=false;
            Assert.That(game.Restore(seed),Is.True);
            Assert.That(game.workshop.OpenEmbedded(),Is.True);
            yield return null;
            Assert.That(game.Capture().goalCelebrated,Is.False,"Do not consume the congratulations behind the workshop.");
            game.workshop.CloseEmbedded();
            game.trade.Session.SeedInventory(game.trade.FindStock("common_find"),1);
            Assert.That(game.trade.Session.PlaceItem(0,0),Is.True);
            Assert.That(game.trade.Session.Open(),Is.True);
            yield return null;
            Assert.That(game.Capture().goalCelebrated,Is.False,"The conversation hides the progress canvas.");
            game.trade.ToggleShop();
            yield return WaitFor(()=>game.Capture().goalCelebrated,"Congratulate once the shop is closed.");
            Assert.That(game.progressCanvas.enabled,Is.True);
            Assert.That(game.LastNotice,Does.Contain("Цель достигнута"));
            if(game.player.control.HasModalOwner)Assert.That(game.TryDepart(),Is.False,"The counter camera must release its control claim before departure.");
            yield return WaitFor(()=>!game.player.control.HasModalOwner && !game.trade.ViewTransitioning,"Closing trade must finish the camera transition.");
            yield return Depart();
            yield return ReturnWithCard();
            Assert.That(game.Capture().goalCelebrated,Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator WholeGameSnapshotRestoresCopiesSlotsTimerAndWalletWithoutReducingCompletedGoal()
        {
            var seed=game.Capture();
            seed.coins=seed.cards.coins=1200;
            seed.shop.grossRevenue=1000;seed.shop.salesCount=5;seed.shop.remainingSeconds=61.25f;
            Assert.That(game.Restore(seed),Is.True);
            Assert.That(game.revenueGoal,Is.EqualTo(1000));Assert.That(game.GoalReached,Is.True);
            var cards=game.workshop.Model;
            var definition=game.workshop.config.cards.First(c=>c.action==CardAction.HeavyStrike);
            Assert.That(cards.Buy(definition.id,out var bought),Is.Null);
            Assert.That(cards.Toggle(bought.Id),Is.Null);
            Assert.That(cards.Upgrade(bought.Id),Is.Null);
            Assert.That(cards.ImproveHand(DeckKind.Combat),Is.Null);
            var stock=game.trade.FindStock("common_find");
            game.trade.Session.SeedInventory(stock,2);
            Assert.That(game.trade.Session.PlaceItem(0,2),Is.True);
            yield return null;
            Assert.That(game.GoalReached,Is.True);
            Assert.That(game.trade.Session.GrossRevenue,Is.EqualTo(1000));
            Assert.That(game.wallet.Wallet.Balance,Is.LessThan(1200));
            var saved=game.Capture();
            string expected=JsonUtility.ToJson(saved);
            Assert.That(cards.Toggle(bought.Id),Is.Null);
            Assert.That(cards.Buy(definition.id,out _),Is.Null);
            Assert.That(game.trade.Session.RemoveItem(2),Is.True);
            Assert.That(game.Restore(saved),Is.True);
            Assert.That(JsonUtility.ToJson(game.Capture()),Is.EqualTo(expected));
            Assert.That(game.Restore(saved),Is.True);
            Assert.That(JsonUtility.ToJson(game.Capture()),Is.EqualTo(expected),"Repeat restore must not credit the wallet or duplicate instances.");
            Assert.That(game.trade.Session.Slots[2],Is.Not.Null);
            Assert.That(cards.Find(bought.Id).Level,Is.EqualTo(2));
            Assert.That(cards.IsSelected(bought.Id),Is.True);
            Assert.That(cards.HandSize(DeckKind.Combat),Is.EqualTo(4));
            Assert.That(game.trade.Session.RemainingSeconds,Is.EqualTo(61.25f));
            Assert.That(game.GoalReached,Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator SaveNowAndSceneReloadRestoreTheWholeGameFromAnIsolatedFile()
        {
            string uniqueFile="playtest-"+Guid.NewGuid().ToString("N")+".json";
            game.saveFile=uniqueFile;
            string uniquePath=game.SavePath;
            ownedSavePaths.Add(uniquePath);
            Assert.That(File.Exists(uniquePath),Is.False,"The test owns a fresh unique save file.");
            game.persistProgress=true;
            var seed=game.Capture();
            seed.coins=seed.cards.coins=900;
            seed.shop.grossRevenue=1111;seed.shop.salesCount=7;seed.shop.remainingSeconds=64.75f;
            Assert.That(game.Restore(seed),Is.True);
            var cards=game.workshop.Model;
            string definitionId=game.workshop.config.cards.Single(c=>c.action==CardAction.HeavyStrike).id;
            Assert.That(cards.Buy(definitionId,out var copy),Is.Null);
            Assert.That(cards.Toggle(copy.Id),Is.Null);
            Assert.That(cards.Upgrade(copy.Id),Is.Null);
            Assert.That(cards.ImproveHand(DeckKind.Combat),Is.Null);
            game.trade.Session.SeedInventory(game.trade.FindStock("common_find"),2);
            game.trade.Session.SeedInventory(game.trade.FindStock("wolf_pelt"),1);
            Assert.That(game.trade.Session.PlaceItem(0,4),Is.True);
            yield return null;
            var expected=game.Capture();
            string expectedJson=JsonUtility.ToJson(expected);
            Assert.That(game.SaveNow(),Is.True);
            Assert.That(File.Exists(uniquePath),Is.True);
            Assert.That(game.wallet.Wallet.Balance,Is.LessThan(900));
            // The outgoing scene cannot overwrite this checkpoint during unload.
            game.persistProgress=false;
            UnityAction<Scene,LoadSceneMode> loadIsolatedProgress=(scene,mode)=>{
                if(scene.path!=ScenePath)return;
                loadedScene=scene;
                game=scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<GameLoop>(true)).Single();
                game.saveFile=uniqueFile;
                game.persistProgress=true;
                game.workshop.persistProgress=false;
            };
            SceneManager.sceneLoaded+=loadIsolatedProgress;
            try { yield return SceneManager.LoadSceneAsync(ScenePath,LoadSceneMode.Single); }
            finally { SceneManager.sceneLoaded-=loadIsolatedProgress; }
            yield return WaitFor(()=>game.Ready,"The reloaded scene must restore the isolated file in Start.");
            world=game.expedition;
            Assert.That(game.SavePath,Is.EqualTo(uniquePath));
            Assert.That(JsonUtility.ToJson(game.Capture()),Is.EqualTo(expectedJson));
            Assert.That(game.wallet.Wallet.Balance,Is.EqualTo(expected.coins));
            Assert.That(game.workshop.Model.Coins,Is.EqualTo(expected.coins));
            Assert.That(game.trade.Session.Gold,Is.EqualTo(expected.coins));
            Assert.That(game.trade.Session.Inventory.Count,Is.EqualTo(2));
            Assert.That(game.trade.Session.Slots[4].Definition.Id,Is.EqualTo("common_find"));
            Assert.That(game.trade.Session.GrossRevenue,Is.EqualTo(1111));
            Assert.That(game.workshop.Model.HandSize(DeckKind.Combat),Is.EqualTo(4));
            Assert.That(game.workshop.Model.Find(copy.Id).Level,Is.EqualTo(2));
            Assert.That(game.workshop.Model.IsSelected(copy.Id),Is.True);
            Assert.That(game.GoalReached,Is.True);
            Assert.That(game.Phase,Is.EqualTo(GamePhase.Shop));
            AssertOnePlayer();
            game.persistProgress=false;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RestoreCannotPartiallyOverwriteAnActiveConversationOrOuting()
        {
            var candidate=game.Capture();candidate.coins=candidate.cards.coins=700;
            var session=game.trade.Session;
            session.SeedInventory(game.trade.FindStock("common_find"));session.PlaceItem(0,0);session.Open();
            string before=JsonUtility.ToJson(game.Capture());
            Assert.That(game.Restore(candidate),Is.False);
            Assert.That(JsonUtility.ToJson(game.Capture()),Is.EqualTo(before));
            session.Close();game.trade.RefreshPresentation();
            yield return Depart();
            int copies=game.workshop.Model.Owned.Count;long outing=session.ExpeditionId;
            Assert.That(game.Restore(candidate),Is.False);
            Assert.That(session.ExpeditionId,Is.EqualTo(outing));Assert.That(game.wallet.Wallet.Balance,Is.Zero);
            Assert.That(game.workshop.Model.Owned.Count,Is.EqualTo(copies));Assert.That(world.Running,Is.True);
            yield return ReturnWithCard();
            LogAssert.NoUnexpectedReceived();
        }

        IEnumerator Depart()
        {
            Assert.That(game.TryDepart(),Is.True,game.LastNotice);
            var visibleCards=world.hand.cards.Where(card=>card.gameObject.activeInHierarchy).ToArray();
            Assert.That(visibleCards.Length,Is.EqualTo(game.workshop.Model.Current.Active.Hand.Count));
            foreach(var card in visibleCards)
                Assert.That(card.transform.localPosition.z,Is.GreaterThanOrEqualTo(.6f),
                    "Cards must be placed ahead of the camera immediately, before the first animation frame.");
            yield return null;
            Assert.That(world.Running,Is.True);Assert.That(game.Phase,Is.EqualTo(GamePhase.Expedition));
            Assert.That(game.shopGeometry.activeSelf,Is.False);Assert.That(game.expeditionGeometry.activeSelf,Is.True);
            Assert.That(game.trade.enabled,Is.False);Assert.That(game.shopHud.enabled,Is.False);
            Assert.That(game.workshop.presentation.enabled,Is.False);Assert.That(world.hud.canvas.enabled,Is.True);
            Assert.That(world.control.HasModalOwner,Is.False);
        }
        IEnumerator ReturnWithCard()
        {
            var trip=game.workshop.Model.Current;
            if(trip.ActiveKind!=DeckKind.Exploration)world.thrower.SwitchDeck();
            int attempts=0;
            while(!trip.Get(DeckKind.Exploration).HasReturn && attempts++<8)
            {
                world.thrower.Discard();
                yield return new WaitForSeconds(world.settings.discardCooldown+.04f);
            }
            Assert.That(trip.Get(DeckKind.Exploration).HasReturn,Is.True);
            world.thrower.Select(0);
            Assert.That(world.thrower.Selected.IsReturn,Is.True);
            world.thrower.Begin();
            Assert.That(world.Returning,Is.True);
            yield return WaitFor(()=>game.Phase==GamePhase.Shop && !world.Returning,"Return card must deliver loot and restore the saved shop phase.");
            Assert.That(game.workshop.Model.IsAway,Is.False);Assert.That(game.workshop.Busy,Is.False);
            Assert.That(game.trade.Session.ExpeditionId,Is.Zero);Assert.That(game.shopGeometry.activeSelf,Is.True);
            Assert.That(game.expeditionGeometry.activeSelf,Is.False);Assert.That(game.trade.enabled,Is.True);
            Assert.That(world.hud.canvas.enabled,Is.False);Assert.That(game.workshop.EmbeddedOpen,Is.False);
        }
        IEnumerator SellInventoryItemAtReferencePrice(bool capture)
        {
            var trade=game.trade;var session=trade.Session;
            Assert.That(session.PlaceItem(0,0),Is.True);
            var item=session.Slots[0];
            Assert.That(session.Open(),Is.True);
            Assert.That(session.Refuse(session.CurrentCustomer.Id),Is.True);
            Assert.That(session.AdvanceCustomer(new CustomerTerms {
                RequestedSlot=0,AsksPrice=true,MaxPrice=item.Definition.RecommendedPrice,Patience=4
            }),Is.True);
            yield return WaitFor(()=>trade.ActionsReady && !trade.SelectingFromDisplay,"The real NPC must reach the counter before a price is accepted.");
            if(capture)yield return Capture("unified-trade");
            var deal=session.CurrentDeal;Assert.That(deal,Is.Not.Null);
            int coins=session.Gold;long revenue=session.GrossRevenue;
            trade.OfferPrice(deal.Id,deal.Revision,0);
            Assert.That(session.Gold,Is.EqualTo(coins+item.Definition.RecommendedPrice));
            Assert.That(session.GrossRevenue,Is.EqualTo(revenue+item.Definition.RecommendedPrice));
            Assert.That(session.LastSoldItem.Id,Is.EqualTo(item.Id));
            yield return WaitFor(()=>!trade.World.ShopView.modal.Active && !trade.World.Customers.actor.IsBusy,"The sold customer's exit must release the counter and modal camera.");
            Assert.That(session.IsOpen,Is.False);
        }
        void AssertOnePlayer()
        {
            var roots=loadedScene.GetRootGameObjects();
            Assert.That(roots.SelectMany(r=>r.GetComponentsInChildren<FirstPersonMotor>(true)).Count(),Is.EqualTo(1));
            Assert.That(roots.SelectMany(r=>r.GetComponentsInChildren<PlayerControlGate>(true)).Count(),Is.EqualTo(1));
            Assert.That(roots.SelectMany(r=>r.GetComponentsInChildren<Camera>(true)).Count(c=>c.isActiveAndEnabled),Is.EqualTo(1));
            Assert.That(roots.SelectMany(r=>r.GetComponentsInChildren<AudioListener>(true)).Count(c=>c.isActiveAndEnabled),Is.EqualTo(1));
        }
        static IEnumerator WaitFor(Func<bool> condition,string reason)
        {
            float deadline=Time.realtimeSinceStartup+12;
            while(!condition() && Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(condition(),Is.True,reason);
        }
        static IEnumerator Capture(string name)
        {
            if(Application.isBatchMode)yield break;
            yield return new WaitForEndOfFrame();
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../output/game-integration"));
            Directory.CreateDirectory(directory);
            ScreenCapture.CaptureScreenshot(Path.Combine(directory,name+".png"));
            yield return null;
        }
    }
}
#endif
