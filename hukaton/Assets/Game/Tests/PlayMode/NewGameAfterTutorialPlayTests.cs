#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Game.Tests
{
    /// <summary>
    /// Balance stage 10: a new game started «after the tutorial» gets 100 coins in the wallet and 100 revenue, as if the
    /// lessons had been sold; the goals chain still begins with goal 1, and the first revenue goal counts the 100.
    /// In the real scene; the save goes to a temporary folder of the test.
    /// </summary>
    public sealed class NewGameAfterTutorialPlayTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";

        [UnitySetUp]
        public IEnumerator LoadTheGameWithAFreshSave()
        {
            GameSaveFolder.UseTemporaryForTests(nameof(NewGameAfterTutorialPlayTests));
            MainMenu.SkipNextLaunch = true;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return WaitFor(() => Object.FindAnyObjectByType<GameLoop>()?.Ready == true, 5);
        }

        [UnityTearDown]
        public IEnumerator RestoreThePlayersSave()
        {
            SceneManager.SetActiveScene(SceneManager.CreateScene("After tutorial test teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            GameSaveFolder.EndTemporaryForTests();
        }

        [UnityTest]
        public IEnumerator ANewGameAfterTheTutorialHasAHundredCoinsAndAHundredRevenue()
        {
            var first = Object.FindAnyObjectByType<GameLoop>();
            Assert.That(first.tutorialEarnings, Is.EqualTo(100), "the scene's value");
            Assert.That(first.tutorialRevenue, Is.EqualTo(100), "the scene's value");
            MainMenu.SkipNextLaunch = true;
            first.StartOver(NewGameStart.AfterTutorial);
            GameLoop game = null;
            yield return WaitFor(() => (game = Object.FindAnyObjectByType<GameLoop>()) && game != first && game.Ready, 20);
            Assert.That(game && game != first && game.Ready, Is.True, "the scene came back as a new game");
            Assert.That(game.FreshStart && !game.LoadedFromSave, Is.True);
            Assert.That(game.tutorial.Active, Is.False, "no tutorial");
            Assert.That(game.wallet.Wallet.Balance, Is.EqualTo(100), "the wallet");
            Assert.That(game.trade.Session.GrossRevenue, Is.EqualTo(100), "the revenue");
            Assert.That(game.trade.Session.SalesCount, Is.Zero, "no sale invented");
            Assert.That(game.CurrentGoal, Is.EqualTo(MainGoal.GoblinCard), "the chain begins with goal 1");
            yield return null; // saved in LateUpdate
            game.SaveNow();
            Assert.That(GameSaveStore.Load(game.SavePath, game.ValidateSave, out var save, out var warning), Is.True, warning);
            Assert.That(save.shop.grossRevenue, Is.EqualTo(100), "the revenue is in the save");

            // Goals 1 and 2 done: the first revenue goal counts the hundred.
            Assert.That(game.CompleteActiveGoal(), Is.True);
            Assert.That(game.CompleteActiveGoal(), Is.True);
            yield return null;
            Assert.That(game.CurrentGoal, Is.EqualTo(MainGoal.FirstRevenue));
            Assert.That(game.objectiveLabel.text, Is.EqualTo("Выручка: 100/404"));
        }

        static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
#endif
