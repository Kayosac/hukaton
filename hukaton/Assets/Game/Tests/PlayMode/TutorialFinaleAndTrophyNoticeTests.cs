#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hakaton.Decks;
using Hakaton.Expeditions;
using Hakaton.Tutorial;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Game.Tests
{
    /// <summary>
    /// Balance, stage 1: the goblin's finale warns that forest cards are one-use and «Пора домой» comes when they run
    /// out, right before he leaves; saves made in the middle of the finale still read on correctly. Trophy cards: the
    /// first kill is sure while the collection holds at most eight cards, and the notice says the card lasts the outing.
    /// The save goes to a temporary folder of the test (GameSaveFolder): the player's own files are never touched.
    /// </summary>
    public sealed class TutorialFinaleAndTrophyNoticeTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";
        const string ReturnCardLine = "Там не пещера. Каждая карта — один раз. Последней придёт „Пора домой“: зажми и держи, пока не унесёт. В глубокую воду не суйся: я за тобой нырять не буду.";
        const string LeavingLine = "А у меня дела поважнее.";

        [UnitySetUp]
        public IEnumerator LoadTheGameWithAFreshSave()
        {
            // The save lives in a temporary folder of this test: the player's own files are never read or touched.
            GameSaveFolder.UseTemporaryForTests(nameof(TutorialFinaleAndTrophyNoticeTests));
            MainMenu.SkipNextLaunch = true;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return WaitFor(() => Object.FindAnyObjectByType<GameLoop>()?.Ready == true, 5);
        }

        [UnityTearDown]
        public IEnumerator RestoreThePlayersSave()
        {
            SceneManager.SetActiveScene(SceneManager.CreateScene("Finale and trophy notice teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            GameSaveFolder.EndTemporaryForTests();
        }

        [UnityTest]
        public IEnumerator TheFinaleWarnsAboutTheReturnCardJustBeforeTheGoblinLeaves()
        {
            var game = Object.FindAnyObjectByType<GameLoop>();
            var tutorial = game.tutorial;
            var finale = tutorial.finale;
            Assert.That(finale.Length, Is.EqualTo(4));
            Assert.That(finale[2], Is.EqualTo(ReturnCardLine), "The warning is the last word before the goodbye.");
            Assert.That(finale[3], Is.EqualTo(LeavingLine), "The goblin still leaves on his own line.");
            var probe = new GameObject("Finale defaults").AddComponent<ShopTutorial>();
            Assert.That(probe.finale, Is.EqualTo(finale), "The code's default matches the scene.");
            Object.Destroy(probe.gameObject);

            game.SkipTutorial();
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Shop));

            // An old save made before the line was added, at «А у меня дела поважнее.» (two lines read): the player now
            // reads the new warning, then the goodbye — nothing skipped, nothing read twice.
            Assert.That(game.Restore(Saved(game, ShopTutorialStage.Finale, 2)), Is.True);
            yield return Shows(tutorial, ReturnCardLine);
            yield return ReadOn(tutorial);
            yield return Shows(tutorial, LeavingLine);
            Assert.That(tutorial.Save().linesRead, Is.EqualTo(3), "A save now keeps the goodbye as the line to show.");
            yield return ReadOn(tutorial);
            Assert.That(tutorial.Active, Is.False, "The goodbye ends the tutorial: the player is free.");

            // A save past the end of the lines (any future shortening) finishes the finale instead of failing.
            Assert.That(game.Restore(Saved(game, ShopTutorialStage.Finale, 9)), Is.True);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(tutorial.Active, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator TrophyCardsComeSureForSmallCollectionsAndLastTheOuting()
        {
            var game = Object.FindAnyObjectByType<GameLoop>();
            var world = Object.FindAnyObjectByType<ExpeditionWorld>(FindObjectsInactive.Include);
            var trophies = world.trophies;
            Assert.That(trophies.startingCollection, Is.EqualTo(8));
            Assert.That(trophies.notice, Is.EqualTo("Трофейная карта: «{0}» — до конца вылазки"));
            var probe = new GameObject("Trophy defaults").AddComponent<TrophyCards>();
            Assert.That(probe.startingCollection, Is.EqualTo(trophies.startingCollection), "The code's default matches the scene.");
            Assert.That(probe.notice, Is.EqualTo(trophies.notice), "The code's default matches the scene.");
            Object.Destroy(probe.gameObject);

            game.SkipTutorial();
            int owned = game.workshop.Model.Owned.Count;
            Assert.That(game.TryDepart(), Is.True);
            yield return WaitFor(() => world.Running, 3);
            Assert.That(world.Running, Is.True);
            Assert.That(trophies.Luck.Starting, Is.EqualTo(owned <= 8), "The first kill is sure while the collection holds at most eight cards.");

            var definition = world.workshop.config.cards.First(c => c && !c.dummy && c.artwork);
            CardCopy arrived = null;
            void Got(CardCopy copy) => arrived = copy;
            trophies.Arrived += Got;
            trophies.Send(definition, world.motor.transform.position + world.motor.transform.forward * 3, 1);
            yield return WaitFor(() => arrived != null, 4);
            trophies.Arrived -= Got;
            Assert.That(arrived, Is.Not.Null, "The card flew into the hand.");
            Assert.That(world.Notice, Is.EqualTo($"Трофейная карта: «{definition.displayName}» — до конца вылазки"));
        }

        static GameSave Saved(GameLoop game, ShopTutorialStage stage, int linesRead)
        {
            var save = game.Capture();
            save.tutorial = new TutorialSave { stage = (int)stage, linesRead = linesRead, stockGiven = true };
            return save;
        }

        static IEnumerator Shows(ShopTutorial tutorial, string line)
        {
            var text = tutorial.bubble.bubble.Dialogue;
            yield return WaitFor(() => tutorial.bubble.Visible && text.text == line, 2);
            Assert.That(tutorial.bubble.Visible, Is.True);
            Assert.That(text.text, Is.EqualTo(line));
        }

        /// <summary>As E or a click: the first press finishes the typing, the next one reads on.</summary>
        static IEnumerator ReadOn(ShopTutorial tutorial)
        {
            int before = tutorial.Save().linesRead;
            var stage = tutorial.Stage;
            for (int i = 0; i < 6 && tutorial.Stage == stage && tutorial.Save().linesRead == before; i++)
            {
                tutorial.Continue();
                yield return null;
                yield return null;
            }
        }

        static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
        }
    }
}
#endif
