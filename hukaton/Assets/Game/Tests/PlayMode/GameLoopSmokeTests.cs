#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
        public IEnumerator DepartThrowACardAndComeBackHomeAfterDefeat()
        {
            var game = Object.FindAnyObjectByType<GameLoop>();
            Assert.That(game.Ready, Is.True, "The authored scene wires every system the loop needs.");
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Shop));

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
