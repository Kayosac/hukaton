#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hakaton.Decks;
using Hakaton.Expeditions;
using Hakaton.Loot;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Game.Tests
{
    /// <summary>
    /// The game's first drowning keeps the whole outing's loot, the second costs half as any defeat; the cave prologue's
    /// dips do not spend the forgiveness, and the mark is in the save. In the real scene; the save goes to a temporary folder of the test
    /// (GameSaveFolder): the player's own files are never touched.
    /// </summary>
    public sealed class FirstDrowningTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";

        [UnitySetUp]
        public IEnumerator LoadTheGameWithAFreshSave()
        {
            // The save lives in a temporary folder of this test: the player's own files are never read or touched.
            GameSaveFolder.UseTemporaryForTests(nameof(FirstDrowningTests));
            MainMenu.SkipNextLaunch = true;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return WaitFor(() => Object.FindAnyObjectByType<GameLoop>()?.Ready == true, 5);
        }

        [UnityTearDown]
        public IEnumerator RestoreThePlayersSave()
        {
            SceneManager.SetActiveScene(SceneManager.CreateScene("First drowning test teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            GameSaveFolder.EndTemporaryForTests();
        }

        [UnityTest]
        public IEnumerator FirstDrowningKeepsEverythingTheSecondCostsHalf()
        {
            var game = Object.FindAnyObjectByType<GameLoop>();
            var world = game.expedition;

            // The cave prologue: its water only puts the player back on the shore and spends nothing.
            Assert.That(game.cave && game.cave.Active, Is.True, "A fresh game opens in the cave prologue.");
            yield return WaitFor(() => world.InPrologue && world.Running, 20);
            Assert.That(world.InPrologue && world.Running, Is.True, $"The cave loads and the prologue runs (in prologue: {world.InPrologue}, running: {world.Running}).");
            world.vitals.Immortal = false;
            world.Drown();
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.That(world.Returning, Is.False, "The prologue has no death.");
            Assert.That(world.InPrologue, Is.True);
            Assert.That(world.Firsts.drownForgiven, Is.False, "A dip in the cave is not the first drowning.");

            game.SkipTutorial();
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Shop));
            var items = world.lootCatalog.items.Where(x => x).Take(3).ToArray();
            Assert.That(items.Length, Is.EqualTo(3));

            // First drowning: three different finds and ten coins, all of them come home.
            var delivered = new List<LootSave>();
            void Delivered(IReadOnlyList<LootSave> loot, bool defeated) { delivered.Clear(); delivered.AddRange(loot); }
            world.workshop.OutingCompleted += Delivered;
            int balance = world.workshop.Model.Wallet.Balance;
            yield return DepartCollectAndDrown(game, items, 10);
            Assert.That(world.LastOutingForgiven, Is.True);
            Assert.That(world.Firsts.drownForgiven, Is.True);
            Assert.That(delivered.Sum(x => x.count), Is.EqualTo(3), "Every find of the first drowning comes home.");
            Assert.That(world.workshop.Model.Wallet.Balance - balance, Is.EqualTo(10), "And every coin.");
            Assert.That(world.LastOutingNotice, Is.EqualTo("Вы утонули — глубокую воду лучше заморозить. На первый раз добыча цела."));
            yield return FinishTheLecture(game);
            Assert.That(game.LastNotice, Does.StartWith(world.LastOutingNotice), "The shop shows the world's line.");
            yield return null; // the game loop saves the change in its LateUpdate
            Assert.That(GameSaveStore.Load(game.SavePath, game.ValidateSave, out var save, out var warning), Is.True, warning);
            Assert.That(save.outingFirsts != null && save.outingFirsts.drownForgiven, Is.True, "The forgiveness is spent in the save.");

            // Second drowning: the usual defeat — two of three finds, five of ten coins.
            balance = world.workshop.Model.Wallet.Balance;
            yield return DepartCollectAndDrown(game, items, 10);
            world.workshop.OutingCompleted -= Delivered;
            Assert.That(world.LastOutingForgiven, Is.False);
            Assert.That(delivered.Sum(x => x.count), Is.EqualTo(2));
            Assert.That(world.workshop.Model.Wallet.Balance - balance, Is.EqualTo(5));
            Assert.That(world.LastOutingNotice, Is.EqualTo("Вы утонули — глубокую воду лучше заморозить. Домой пришло 2 из 3 вещей и 5 из 10 монет."));
            Assert.That(game.lecture.Active, Is.False, "The goblin says his word once.");
            Assert.That(game.LastNotice, Does.StartWith(world.LastOutingNotice));
        }

        static IEnumerator DepartCollectAndDrown(GameLoop game, LootItem[] items, int coins)
        {
            var world = game.expedition;
            if (world.readyWorld) world.NextPlace = OutingPlace.Ready;
            Assert.That(game.TryDepart(), Is.True, "The shop can leave on an outing.");
            yield return WaitFor(() => world.Running, 5);
            Assert.That(world.Running, Is.True);
            foreach (var item in items) world.Collect(item);
            world.CollectCoins(coins, Color.yellow);
            world.vitals.Immortal = false;
            world.Drown();
            Assert.That(world.Returning, Is.True, "Deep water drowns at once.");
            yield return WaitFor(() => game.Phase == GamePhase.Shop && !world.Returning, 10);
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Shop));
        }

        static IEnumerator FinishTheLecture(GameLoop game)
        {
            var lecture = game.lecture;
            yield return WaitFor(() => lecture.Active && lecture.Line == 0, 5);
            float end = Time.realtimeSinceStartup + 10;
            while (lecture.Active && Time.realtimeSinceStartup < end)
            {
                lecture.Continue();
                yield return null;
            }
            Assert.That(lecture.Active, Is.False);
        }

        static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
#endif
