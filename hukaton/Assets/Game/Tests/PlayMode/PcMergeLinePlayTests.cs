#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Hakaton.Computer;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Game.Tests
{
    /// <summary>
    /// Balance stage 9, «Окно слияния показывает цену»: in the PC's «Карты», a copy with a partner shows in the window's
    /// status line — before it is dropped — what the merge gives and takes, with the card's own numbers: damage
    /// (Удар), seconds (Лёд), metres (Ветвь-мост). The line of every card fits the status bar at its own size (Pixeloid 9).
    /// The save goes to a temporary folder of the test (GameSaveFolder): the player's own files are never touched.
    /// </summary>
    public sealed class PcMergeLinePlayTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";

        [UnitySetUp]
        public IEnumerator LoadTheGameWithAFreshSave()
        {
            GameSaveFolder.UseTemporaryForTests(nameof(PcMergeLinePlayTests));
            MainMenu.SkipNextLaunch = true;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return WaitFor(() => Object.FindAnyObjectByType<GameLoop>()?.Ready == true, 5);
        }

        [UnityTearDown]
        public IEnumerator RestoreThePlayersSave()
        {
            var pc = Object.FindAnyObjectByType<PcComputer>();
            if (pc) pc.CloseImmediately();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Merge line teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            GameSaveFolder.EndTemporaryForTests();
        }

        [UnityTest]
        public IEnumerator TheCardsWindowShowsWhatAMergeGivesAndTakes()
        {
            var game = Object.FindAnyObjectByType<GameLoop>();
            game.SkipTutorial();
            var pc = Object.FindAnyObjectByType<PcComputer>();
            Assert.That(pc, Is.Not.Null);
            var guide = Object.FindAnyObjectByType<PcGuide>(FindObjectsInactive.Include);
            if (guide && guide.book)
                foreach (var tour in new[] { guide.desktopTour, guide.cardsTour, guide.mergeTour, guide.upgradesTour, guide.hapayTour, guide.storageTour })
                    guide.book.Progress.Finish(tour.id);

            var model = pc.workshop.Model;
            foreach (var id in new[] { "strike", "ice", "ice", "branch_bridge", "branch_bridge" })
                Assert.That(model.Purchase(id, 0, out _), Is.Null, id);

            Assert.That(pc.Open(), Is.True);
            yield return WaitFor(() => pc.Running, 8);
            Assert.That(pc.Running, Is.True, "The PC boots up.");
            pc.desktop.Open(pc.cards.window);
            yield return null;
            yield return null;
            var cards = pc.cards;
            Assert.That(cards.window.IsOpen, Is.True);

            var expected = new (string id, string line)[]
            {
                ("strike", "Отпустите на другую «Удар»: 2 броска × 3 урона → 1 бросок × 5 урона"),
                ("ice", "Отпустите на другую «Лёд»: 2 броска × 3 с → 1 бросок × 5 с"),
                ("branch_bridge", "Отпустите на другую «Ветвь-мост»: 2 броска × 5 м → 1 бросок × 8 м"),
            };
            foreach (var (id, line) in expected)
            {
                var copy = model.Owned.First(c => c.Spec.Id == id);
                Assert.That(model.MergePartners(copy).Any(), Is.True, id + " has a partner");
                Assert.That(cards.MergeStatus(copy), Is.EqualTo(line), id);
            }

            // Every card's line, at I and at II, fits the status bar without shrinking the type (no wrapping there).
            var status = cards.window.status;
            Assert.That(status.enableAutoSizing, Is.False);
            float room = status.rectTransform.rect.width;
            string widest = null;
            float widestWidth = 0;
            foreach (var definition in pc.workshop.config.cards)
                for (int level = 1; level < pc.workshop.config.maximumCardLevel; level++)
                {
                    string text = $"Отпустите на другую «{definition.displayName}»: " + DeckPrototype.MergeLine(definition, level, pc.workshop.config.maximumCardLevel);
                    float width = status.GetPreferredValues(text).x;
                    if (width > widestWidth) { widestWidth = width; widest = text; }
                }
            Debug.Log($"[Merge] status bar {room:0} px, the widest line {widestWidth:0} px: {widest}");
            Assert.That(widestWidth, Is.LessThanOrEqualTo(room), "The widest merge line fits the status bar: " + widest);
            Assert.That(status.fontSize, Is.EqualTo(9).Or.EqualTo(18), "Pixeloid at its own size.");
            pc.CloseImmediately();
        }

        static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
#endif
