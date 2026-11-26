#if UNITY_EDITOR
using System.Collections;
using Hakaton.Trading;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Game.Tests
{
    /// <summary>
    /// Balance stage 6, the end of the demo: the stranger comes once, as the last visitor of the shift that closes with 1616
    /// revenue — however it closes; then the goblin has his word and «Конец демо» comes up once; the game goes on.
    /// </summary>
    public sealed class DemoFinalePlayTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";
        public enum Closing { Player, TimeUp, BulkBuyer }

        [UnitySetUp]
        public IEnumerator LoadTheGameWithAFreshSave()
        {
            GameSaveFolder.UseTemporaryForTests(nameof(DemoFinalePlayTests));
            MainMenu.SkipNextLaunch = true;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return WaitFor(() => Object.FindAnyObjectByType<GameLoop>()?.Ready == true, 5);
        }

        [UnityTearDown]
        public IEnumerator RestoreThePlayersSave()
        {
            SceneManager.SetActiveScene(SceneManager.CreateScene("Finale test teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            GameSaveFolder.EndTemporaryForTests();
        }

        static GameLoop Game() => Object.FindAnyObjectByType<GameLoop>();

        static IEnumerator OpenShift(GameLoop game, long revenue, bool bulkBuyerFirst = false)
        {
            var session = game.trade.Session;
            session.CompleteRevenueGoal(revenue);
            Assert.That(session.GrossRevenue, Is.EqualTo(revenue));
            session.StockEmptySlotsRandomly();
            Assert.That(session.DisplayCount, Is.GreaterThan(0));
            session.ForceBulkBuyerNext = bulkBuyerFirst;
            Assert.That(session.Open(), Is.True);
            yield return null;
            yield return WaitFor(() => !game.trade.ViewTransitioning, 3);
        }

        static IEnumerator CloseShift(GameLoop game, Closing how)
        {
            var session = game.trade.Session;
            switch (how)
            {
                case Closing.Player:
                    session.RequestClose(); // E at the counter, the clock's cross
                    break;
                case Closing.TimeUp:
                    yield return WaitFor(() => session.CurrentCustomer != null, 10);
                    session.Tick(session.RemainingSeconds + 1);
                    if (session.CurrentCustomer != null && !session.CurrentCustomer.IsTalker) session.Refuse(session.CurrentCustomer.Id);
                    break;
                case Closing.BulkBuyer:
                    yield return WaitFor(() => session.CurrentCustomer != null, 10);
                    Assert.That(session.CurrentCustomer.IsBulkBuyer, Is.True);
                    Assert.That(session.AcceptBulkOffer(session.CurrentCustomer.Id), Is.True);
                    Assert.That(session.DisplayCount, Is.Zero);
                    break;
            }
        }

        /// <summary>The stranger walks in and says everything; the shift ends after him.</summary>
        static IEnumerator ReadTheStranger(GameLoop game)
        {
            var session = game.trade.Session;
            yield return WaitFor(() => session.CurrentCustomer != null && session.CurrentCustomer.IsTalker, 20);
            Assert.That(session.CurrentCustomer != null && session.CurrentCustomer.IsTalker, Is.True, "The stranger is the last visitor.");
            yield return null;
            Assert.That(game.StrangerVisited, Is.True);
            float end = Time.realtimeSinceStartup + 30;
            while (session.IsOpen && Time.realtimeSinceStartup < end)
            {
                if (session.CurrentCustomer != null && session.CurrentCustomer.IsTalker) session.ReadOnTalker(session.CurrentCustomer.Id);
                yield return null;
            }
            Assert.That(session.IsOpen, Is.False, "The shift ends after him.");
        }

        /// <summary>The goblin poofs in with his word; every line closes on E.</summary>
        static IEnumerator HearTheGoblin(GameLoop game)
        {
            yield return WaitFor(() => game.LectureActive, 15);
            Assert.That(game.LectureActive, Is.True, "The goblin has his word after the stranger.");
            Assert.That(game.FinaleShown, Is.False, "The title waits for him.");
            float end = Time.realtimeSinceStartup + 30;
            while (game.LectureActive && Time.realtimeSinceStartup < end)
            {
                game.lecture.Continue();
                yield return null;
            }
            Assert.That(game.LectureActive, Is.False);
        }

        [UnityTest]
        public IEnumerator TheStrangerEndsTheDemoHoweverTheShiftCloses([Values] Closing how)
        {
            var game = Game();
            game.SkipTutorial();
            yield return null;
            Assert.That(game.strangerRevenue, Is.EqualTo(1616));
            Assert.That(game.EndlessVisited, Is.False, "No endless outing is needed any more.");
            yield return OpenShift(game, 1616, how == Closing.BulkBuyer);
            yield return CloseShift(game, how);
            yield return ReadTheStranger(game);
            yield return HearTheGoblin(game);
            yield return WaitFor(() => game.FinaleShown, 10);
            Assert.That(game.FinaleShown, Is.True, "«Конец демо» comes once the goblin is gone.");
            Assert.That((game.finaleTitle, game.finaleCaption), Is.EqualTo(("Конец демо", "Спасибо за игру · лавка остаётся открытой")));
            yield return WaitFor(() => game.fade.titleGroup.alpha > .5f, 3);
            Assert.That((game.fade.title.text, game.fade.subtitle.text), Is.EqualTo((game.finaleTitle, game.finaleCaption)), "The title is on screen.");
            Assert.That(game.fade.titleGroup.alpha, Is.GreaterThan(.5f));
            yield return new WaitForSecondsRealtime(2.5f);
            Assert.That(game.fade.title.text, Is.EqualTo(game.finaleTitle), "It holds about four seconds: no banner over it.");
            yield return WaitFor(() => game.fade.titleGroup.alpha <= 0, 8);
            // The game goes on: the last revenue goal.
            Assert.That(game.trade.Session.GrossRevenue, Is.GreaterThanOrEqualTo(1616));
            Assert.That(game.CurrentGoal, Is.Not.EqualTo(MainGoal.WorkShift));
            Assert.That(GameSaveStore.Load(game.SavePath, game.ValidateSave, out var save, out var warning), Is.True, warning);
            Assert.That((save.strangerVisited, save.goals.finaleShown), Is.EqualTo((true, true)));

            if (how != Closing.Player) yield break;
            // Once per game: the next closing with the revenue brings neither him nor the title.
            yield return OpenShift(game, 2000);
            yield return CloseShift(game, Closing.Player);
            Assert.That(game.trade.Session.IsOpen, Is.False, "Closed at once: no last visitor.");
            yield return WaitFor(() => game.LectureActive || game.FinalePending, 3);
            Assert.That(game.LectureActive || game.FinalePending, Is.False);
        }

        [UnityTest]
        public IEnumerator OneCoinShortHeDoesNotCome()
        {
            var game = Game();
            game.SkipTutorial();
            yield return null;
            yield return OpenShift(game, 1615);
            yield return CloseShift(game, Closing.Player);
            Assert.That(game.trade.Session.IsOpen, Is.False, "The shop closes at once.");
            yield return WaitFor(() => game.LectureActive || game.FinalePending, 3);
            Assert.That((game.StrangerVisited, game.LectureActive, game.FinalePending, game.FinaleShown), Is.EqualTo((false, false, false, false)));
            Assert.That(game.trade.Session.LastMessage, Is.EqualTo(TradingSession.ClosedNotice));
        }

        [UnityTest]
        public IEnumerator AnOlderSaveWithTheStrangerGetsTheTitleWithoutASecondVisit()
        {
            var game = Game();
            game.SkipTutorial();
            yield return null;
            game.trade.Session.CompleteRevenueGoal(1700);
            var older = game.Capture();
            older.strangerVisited = true;
            older.godMet = true;
            older.goals = null; // saved before the end of the demo existed
            Assert.That(game.Restore(older), Is.True);
            Assert.That(game.StrangerVisited, Is.True);
            Assert.That(game.FinaleShown, Is.False);
            yield return null;
            yield return OpenShift(game, 1700);
            yield return CloseShift(game, Closing.Player);
            Assert.That(game.trade.Session.IsOpen, Is.False, "No second visit: the shop closes at once.");
            yield return WaitFor(() => game.FinaleShown, 10);
            Assert.That(game.FinaleShown, Is.True, "The title comes at the end of the first shift closed with his revenue.");
            Assert.That(game.LectureActive, Is.False, "Without the goblin's word again.");
        }

        static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
#endif
