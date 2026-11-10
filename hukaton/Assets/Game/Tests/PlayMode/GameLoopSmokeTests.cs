#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
#endif
