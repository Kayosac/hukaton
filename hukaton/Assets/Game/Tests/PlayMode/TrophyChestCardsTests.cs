#if UNITY_EDITOR
using System.Collections;
using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Game.Tests
{
    /// <summary>
    /// Balance stage 9: an opened chest of rank 1 or 2 — the main way to taste new cards — leaves a trophy card in 60 and
    /// 70 % of cases (was 40 and 55 %); ranks 3–5 keep 75 / 90 / 100 %. Read from the saved game scene, the code's
    /// default the same; 10 000 chests of each rank rolled as <see cref="TrophyCards.ChestOpened"/> rolls them.
    /// The save goes to a temporary folder of the test (GameSaveFolder): the player's own files are never touched.
    /// </summary>
    public sealed class TrophyChestCardsTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";
        const int Chests = 10000;

        [UnitySetUp]
        public IEnumerator LoadTheGameWithAFreshSave()
        {
            GameSaveFolder.UseTemporaryForTests(nameof(TrophyChestCardsTests));
            MainMenu.SkipNextLaunch = true;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            float until = Time.realtimeSinceStartup + 5;
            while (Object.FindAnyObjectByType<GameLoop>()?.Ready != true && Time.realtimeSinceStartup < until) yield return null;
        }

        [UnityTearDown]
        public IEnumerator RestoreThePlayersSave()
        {
            SceneManager.SetActiveScene(SceneManager.CreateScene("Trophy chest cards teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            GameSaveFolder.EndTemporaryForTests();
        }

        [UnityTest]
        public IEnumerator LowRankChestsGiveACardMoreOften()
        {
            var world = Object.FindAnyObjectByType<ExpeditionWorld>(FindObjectsInactive.Include);
            Assert.That(world, Is.Not.Null);
            var trophies = world.trophies;
            Assert.That(trophies, Is.Not.Null);
            Assert.That(trophies.chestsByRank, Is.EqualTo(new[] { .6f, .7f, .75f, .9f, 1f }).Within(1e-5f), "The scene's chances by chest rank 1–5.");
            var probe = new GameObject("Trophy defaults").AddComponent<TrophyCards>();
            Assert.That(probe.chestsByRank, Is.EqualTo(trophies.chestsByRank).Within(1e-5f), "The code's default matches the scene.");
            Object.Destroy(probe.gameObject);

            var random = new System.Random(20261127);
            var expected = new[] { .6, .7, .75, .9, 1 };
            for (int rank = 1; rank <= 5; rank++)
            {
                int cards = 0;
                float chance = trophies.ChestChance(rank);
                for (int i = 0; i < Chests; i++) if (random.NextDouble() < chance) cards++;
                double share = cards / (double)Chests;
                Debug.Log($"[Trophies] chest rank {rank}: a card from {cards} of {Chests} chests ({share:P1})");
                Assert.That(share, Is.EqualTo(expected[rank - 1]).Within(.02), $"Rank {rank}: a card from {share:P1} of {Chests} chests.");
            }
            Assert.That(trophies.ChestChance(0), Is.EqualTo(trophies.ChestChance(1)), "Below rank 1 counts as rank 1.");
            Assert.That(trophies.ChestChance(9), Is.EqualTo(trophies.ChestChance(5)), "Above rank 5 counts as rank 5.");
            Assert.That((trophies.richChestRank, trophies.richChestCardRank, trophies.startingCollection), Is.EqualTo((5, 3, 8)),
                "The rich chest and the first-card guarantee are untouched.");
            yield return null;
        }
    }
}
#endif
