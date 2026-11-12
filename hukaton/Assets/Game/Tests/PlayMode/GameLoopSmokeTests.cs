#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hakaton.Computer;
using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Game.Tests
{
    /// <summary>
    /// The whole loop in the real scene: shop → departure → a thrown card → defeat → back in the shop.
    /// The player's own save files are moved aside for the run and put back afterwards.
    /// </summary>
    public sealed class GameLoopSmokeTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";
        readonly Dictionary<string, byte[]> playerSaves = new Dictionary<string, byte[]>();
        string saveFolder;

        [UnitySetUp]
        public IEnumerator LoadTheGameWithAFreshSave()
        {
            saveFolder = Path.Combine(Application.persistentDataPath, "Hakaton");
            playerSaves.Clear();
            if (Directory.Exists(saveFolder))
                foreach (var file in Directory.GetFiles(saveFolder))
                {
                    playerSaves[file] = File.ReadAllBytes(file);
                    File.Delete(file);
                }
            // The game itself, without the main menu lying over it.
            MainMenu.SkipNextLaunch = true;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return WaitFor(() => Object.FindAnyObjectByType<GameLoop>()?.Ready == true, 5);
        }

        [UnityTearDown]
        public IEnumerator RestoreThePlayersSave()
        {
            // Unload the game first so nothing writes a save after the files are restored.
            SceneManager.SetActiveScene(SceneManager.CreateScene("Smoke test teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            if (Directory.Exists(saveFolder))
                foreach (var file in Directory.GetFiles(saveFolder)) File.Delete(file);
            foreach (var save in playerSaves) File.WriteAllBytes(save.Key, save.Value);
        }

        [UnityTest]
        public IEnumerator CollectionAndMoneyCheatsUseTheSharedEconomyAndPersist()
        {
            var game = Object.FindAnyObjectByType<GameLoop>();
            var menu = Object.FindAnyObjectByType<CheatMenu>();
            Assert.That(menu, Is.Not.Null);
            Assert.That(menu.game, Is.SameAs(game));
            Assert.That(menu.UnlockCollection(), Is.False, "The cave is still an outing.");
            int initialCoins = game.wallet.Wallet.Balance;
            Assert.That(menu.AddThousandCoins(), Is.True, "Money can be granted during the cave too.");
            game.SkipTutorial();
            var model = game.workshop.Model;
            var selected = model.Selected().Select(c => c.Id).ToArray();
            long revenue = game.trade.Session.GrossRevenue;
            Assert.That(menu.UnlockCollection(), Is.True);
            Assert.That(model.Owned.Select(c => c.Spec.Id).Distinct(), Is.EquivalentTo(model.Catalog.Select(c => c.Id)));
            Assert.That(model.HandSize, Is.EqualTo(model.Rules.MaxHand));
            Assert.That(model.DeckAtMaximum, Is.True);
            Assert.That(model.Owned.Count, Is.LessThanOrEqualTo(model.CollectionLimit));
            Assert.That(model.Selected().Select(c => c.Id), Is.EqualTo(selected));
            int count = model.Owned.Count;
            Assert.That(menu.UnlockCollection(), Is.True);
            Assert.That(model.Owned.Count, Is.EqualTo(count));
            Assert.That(menu.AddThousandCoins(), Is.True);
            Assert.That(game.wallet.Wallet.Balance, Is.EqualTo(initialCoins + 2000));
            Assert.That(model.Coins, Is.EqualTo(initialCoins + 2000));
            Assert.That(game.trade.Session.GrossRevenue, Is.EqualTo(revenue), "Cheat money is not sales revenue.");

            Assert.That(GameSaveStore.Load(game.SavePath, game.ValidateSave, out var save, out var warning), Is.True, warning);
            Assert.That(save.coins, Is.EqualTo(initialCoins + 2000));
            Assert.That(game.Restore(save), Is.True);
            Assert.That((model.Owned.Count, model.HandSize, model.DeckAtMaximum), Is.EqualTo((count, model.Rules.MaxHand, true)));

            menu.SetOpen(true);
            Assert.That(game.player.control.HasModalOwner, Is.True);
            yield return null; // Exercise the expanded, scrollable menu in the real Game View.
            menu.SetOpen(false);
            Assert.That(game.player.control.HasModalOwner, Is.False);

            game.wallet.Wallet.TryRestoreBalance(int.MaxValue - 500);
            Assert.That(menu.AddThousandCoins(), Is.False);
            Assert.That(game.wallet.Wallet.Balance, Is.EqualTo(int.MaxValue - 500), "Overflow leaves the balance intact.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ActiveGoalCheatUnlocksOutingChoiceAndPersistsWithoutAwardingCoins()
        {
            var game = Object.FindAnyObjectByType<GameLoop>();
            var menu = Object.FindAnyObjectByType<CheatMenu>();
            Assert.That(menu, Is.Not.Null);
            Assert.That(game.CanCompleteActiveGoal, Is.False, "The cave tutorial has its own skip action.");
            Assert.That(menu.CompleteActiveGoal(), Is.False);
            game.SkipTutorial();
            Assert.That(game.CanCompleteActiveGoal, Is.True);
            Assert.That(game.PlaceChoiceUnlocked, Is.False);
            int coins = game.wallet.Wallet.Balance;
            int sales = game.trade.Session.SalesCount;
            var stock = game.trade.Session.Inventory.Select(item => item.Id).ToArray();
            var beforeGoal = game.Capture();

            menu.SetOpen(true);
            yield return null;
            Assert.That(game.goalsBlock.activeSelf, Is.True, "The unfinished revenue goal is listed.");
            Assert.That(game.objectiveLabel.text, Does.Contain("/" + game.revenueGoal));
            Assert.That(menu.CompleteActiveGoal(), Is.True);
            Assert.That(game.GoalReached, Is.True);
            Assert.That(game.PlaceChoiceUnlocked, Is.True);
            Assert.That(game.CanCompleteActiveGoal, Is.False);
            Assert.That(game.goalsBlock.activeSelf, Is.False, "A completed goal leaves the list.");
            Assert.That(menu.CompleteActiveGoal(), Is.False);
            Assert.That(game.wallet.Wallet.Balance, Is.EqualTo(coins));
            Assert.That(game.trade.Session.SalesCount, Is.EqualTo(sales));
            Assert.That(game.trade.Session.Inventory.Select(item => item.Id), Is.EqualTo(stock));
            yield return null; // The normal game loop celebrates and saves the reached goal.
            Assert.That(game.Capture().goalCelebrated, Is.True);
            Assert.That(GameSaveStore.Load(game.SavePath, game.ValidateSave, out var save, out var warning), Is.True, warning);
            Assert.That(save.shop.grossRevenue, Is.EqualTo(game.revenueGoal));
            Assert.That(save.goalCelebrated, Is.True);
            Assert.That(game.Restore(save), Is.True);
            Assert.That(game.PlaceChoiceUnlocked, Is.True);
            Assert.That(game.CanCompleteActiveGoal, Is.False);
            Assert.That(game.goalsBlock.activeSelf, Is.False);
            // An older save below the goal lists it again.
            Assert.That(game.Restore(beforeGoal), Is.True);
            Assert.That(game.GoalReached, Is.False);
            Assert.That(game.goalsBlock.activeSelf, Is.True);
            menu.SetOpen(false);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator CheatCardsOverflowTheHandAndRemainVisibleSelectableAndThrowable()
        {
            var game = Object.FindAnyObjectByType<GameLoop>();
            var menu = Object.FindAnyObjectByType<CheatMenu>();
            game.SkipTutorial();
            var world = game.expedition;
            // This checks hand bindings and gameplay, without rendering the full expedition world.
            world.viewCamera.enabled = false;
            Assert.That(game.TryDepart(), Is.True);
            yield return WaitFor(() => world.Running, 3);
            var model = world.workshop.Model;
            var trip = model.Current;
            var original = trip.Deck.Hand.Select(card => card.Id).ToArray();
            int stock = trip.Deck.StockCount;
            int capacity = trip.Deck.Capacity;
            int owned = model.Owned.Count;
            var definition = world.workshop.config.cards.First(card => card && card.id == "fire");

            menu.SetOpen(true);
            for (int i = 0; i < 100; i++)
                Assert.That(menu.GrantCard(definition, 1), Is.Not.Null, "No slot limit for cheat cards.");
            Assert.That(trip.Deck.Hand.Count, Is.EqualTo(original.Length + 100));
            Assert.That(trip.Deck.Hand.Select(card => card.Id).Distinct().Count(), Is.EqualTo(trip.Deck.Hand.Count));
            Assert.That(original, Is.SubsetOf(trip.Deck.Hand.Select(card => card.Id)));
            Assert.That(trip.Deck.StockCount, Is.EqualTo(stock));
            Assert.That(trip.Deck.Capacity, Is.EqualTo(capacity), "Normal draw capacity stays unchanged.");
            Assert.That(model.Owned.Count, Is.EqualTo(owned), "Cheat copies stay temporary.");

            foreach (int index in new[] { trip.Deck.Hand.Count - 1, 0, world.hand.slots.Length, trip.Deck.Hand.Count / 2 })
            {
                world.thrower.Select(index);
                var selected = trip.Deck.Hand[index];
                yield return null;
                yield return null; // Let the hand's LateUpdate follow the selection.
                Assert.That(world.thrower.Selected, Is.SameAs(selected));
                Assert.That(world.hand.slots.Count(view => view.Visible), Is.EqualTo(world.hand.slots.Length));
                Assert.That(world.hand.Take(selected.Id, out _, out var view), Is.True,
                    "Every selected copy must have a physical hand view, including overflow cards.");
                Assert.That(view.Front, Is.SameAs(world.Artwork(selected)));
                world.hand.Deal(); // Restore the view consumed by the pose check.
            }

            menu.SetOpen(false);
            var thrown = world.thrower.Selected;
            world.thrower.Preparation.Begin();
            Assert.That(world.thrower.Release(), Is.Not.Null);
            Assert.That(trip.Deck.Hand.Any(card => card.Id == thrown.Id), Is.False);
            Assert.That(trip.Deck.Hand.Count, Is.EqualTo(original.Length + 99));
            yield return null;
            world.thrower.Discard();
            yield return null;
            Assert.That(trip.Deck.Hand.Count, Is.LessThanOrEqualTo(capacity), "Discard returns to normal dealing.");
            Assert.That(world.hand.slots.Count(view => view.Visible), Is.EqualTo(trip.Deck.Hand.Count));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator DepartThrowACardAndComeBackHomeAfterDefeat()
        {
            var game = Object.FindAnyObjectByType<GameLoop>();
            Assert.That(game.Ready, Is.True, "The authored scene wires every system the loop needs.");
            Assert.That(game.cave && game.cave.Active, Is.True, "A fresh game opens in the cave prologue.");
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Expedition));
            game.SkipTutorial();
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Shop));
            Assert.That(game.tutorial.Active, Is.False);

            Assert.That(game.TryDepart(), Is.True, "A fresh game can leave on an outing.");
            var world = Object.FindAnyObjectByType<ExpeditionWorld>();
            yield return WaitFor(() => world.Running, 3);
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Expedition));

            var thrower = world.thrower;
            thrower.Select(0);
            Assert.That(thrower.Selected, Is.Not.Null, "The hand is dealt on departure.");
            thrower.Preparation.Begin();
            thrower.Preparation.Tick(world.settings.chargeSeconds * .6f, false, 0, 0, world.settings.chargeSeconds, world.settings.gesturePixels);
            Assert.That(thrower.Release(), Is.Not.Null, "Releasing a prepared throw launches a card.");
            yield return new WaitForSeconds(1);

            world.vitals.Hurt(world.vitals.HalfHearts, Vector3.zero, null);
            yield return WaitFor(() => game.Phase == GamePhase.Shop && !world.Returning, 10);
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Shop), "Defeat brings the player back to the shop.");
            Assert.That(world.workshop.Model.IsAway, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PcMusicStartsAfterTheBootAndFadesOutWhenSwitchedOff()
        {
            var game = Object.FindAnyObjectByType<GameLoop>();
            game.SkipTutorial();
            var pc = Object.FindAnyObjectByType<PcComputer>();
            Assert.That(pc, Is.Not.Null);
            var music = pc.music;
            Assert.That(music && music.clip, Is.True, "The PC prefab wires its music source and clip.");
            Assert.That((music.loop, music.playOnAwake, music.spatialBlend), Is.EqualTo((true, false, 0f)));
            Assert.That(pc.musicVolume, Is.InRange(.01f, .3f), "The PC music is meant to be very quiet.");

            Assert.That(pc.Open(), Is.True);
            bool playedDuringBoot = false;
            float end = Time.realtimeSinceStartup + 8;
            while (!pc.Running && Time.realtimeSinceStartup < end)
            {
                playedDuringBoot |= music.isPlaying && music.volume > 0;
                yield return null;
            }
            Assert.That(pc.Running, Is.True, "The PC boots up.");
            Assert.That(playedDuringBoot, Is.False, "No music while the PC is still booting.");
            yield return null;
            Assert.That(music.isPlaying, Is.True, "The music starts once the desktop is up.");
            Assert.That(music.volume, Is.LessThan(pc.musicVolume), "…and fades in rather than jumping to full volume.");
            yield return new WaitForSecondsRealtime(pc.musicFadeInSeconds + .3f);
            Assert.That(music.volume, Is.EqualTo(pc.musicVolume).Within(.001f));

            pc.Close();
            yield return WaitFor(() => !pc.IsOpen, 5);
            Assert.That(pc.IsOpen, Is.False);
            Assert.That((music.isPlaying, music.volume), Is.EqualTo((false, 0f)), "Switching off fades the music out and pauses it.");
            float pausedAt = music.time;
            Assert.That(pausedAt, Is.GreaterThan(0));

            Assert.That(pc.Open(), Is.True, "A short wake this time.");
            yield return WaitFor(() => pc.Running, 5);
            yield return null;
            Assert.That(music.isPlaying, Is.True);
            Assert.That(music.time, Is.GreaterThanOrEqualTo(pausedAt), "After a short wake the music carries on where it stopped.");
            pc.CloseImmediately();
            Assert.That(music.isPlaying, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
#endif
