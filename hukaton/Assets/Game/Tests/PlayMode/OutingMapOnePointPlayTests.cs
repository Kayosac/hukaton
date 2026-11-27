#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using System.Reflection;
using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Game.Tests
{
    /// <summary>
    /// Balance stage 10, the map at the door: while it holds one point, the door opens straight on that point's card
    /// («Идти» / «Остаться») and «Остаться» closes the map; with two points it opens as before, waiting for a click.
    /// In the real scene; the save goes to a temporary folder of the test.
    /// </summary>
    public sealed class OutingMapOnePointPlayTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";

        [UnitySetUp]
        public IEnumerator LoadTheGameWithAFreshSave()
        {
            GameSaveFolder.UseTemporaryForTests(nameof(OutingMapOnePointPlayTests));
            MainMenu.SkipNextLaunch = true;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return WaitFor(() => Object.FindAnyObjectByType<GameLoop>()?.Ready == true, 5);
        }

        [UnityTearDown]
        public IEnumerator RestoreThePlayersSave()
        {
            SceneManager.SetActiveScene(SceneManager.CreateScene("Outing map test teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            GameSaveFolder.EndTemporaryForTests();
        }

        [UnityTest]
        public IEnumerator OnePointOpensItsCardAtOnceAndTwoWaitForAClick()
        {
            var game = Object.FindAnyObjectByType<GameLoop>();
            game.SkipTutorial();
            var map = game.outingMap;
            Assert.That(map, Is.Not.Null);
            Assert.That(map.card, Is.Not.Null);
            yield return Frames(3);

            // The door, before the revenue goal: the old apiary only.
            typeof(GameLoop).GetMethod("TryDepartFromDoor", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, null);
            yield return null;
            Assert.That(map.IsOpen, Is.True, "the door opens the map");
            Assert.That(map.OpenedSites.Count, Is.EqualTo(1));
            Assert.That(map.CardOpen && map.OpenedOnTheOnlyPoint, Is.True, "its card is up without a click");
            Assert.That(map.card.title.text, Is.EqualTo(game.expedition.PlaceName(OutingPlace.Ready)));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Shop), "nothing departs by itself");
            yield return Frames(5);
            map.card.stayButton.onClick.Invoke();
            yield return null;
            Assert.That(map.IsOpen, Is.False, "«Остаться» closes the map");
            Assert.That(map.CardOpen, Is.False);
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Shop));
            yield return new WaitForSecondsRealtime(map.closeSeconds + .1f);

            // Two points: as before — the map waits for a click on one of them.
            var second = map.sites.First(s => s.parentId == map.OpenedSites[0].id);
            map.Open(s => s.id == map.OpenedSites[0].id || s.id == second.id, p => p.ToString(), _ => { });
            yield return null;
            Assert.That(map.IsOpen, Is.True);
            Assert.That(map.OpenedSites.Count, Is.EqualTo(2));
            Assert.That(map.CardOpen || map.OpenedOnTheOnlyPoint, Is.False, "two points: no card until a click");
            Assert.That(map.Opened(second.id).seen, Is.False, "the new point is new (it blinks until clicked)");
            map.Close();
            yield return null;
        }

        static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
#endif
