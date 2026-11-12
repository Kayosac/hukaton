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
    /// The goblin's word after the first drowning, in the real scene: out to the ready-made location, into deep water,
    /// and he is waiting in the shop; his lines close one by one, he leaves in a poof, and a second drowning brings only
    /// the notice. The player's own save files are moved aside for the run and put back afterwards.
    /// </summary>
    public sealed class DrownLectureTests
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
            SceneManager.SetActiveScene(SceneManager.CreateScene("Drown lecture test teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            if (Directory.Exists(saveFolder))
                foreach (var file in Directory.GetFiles(saveFolder)) File.Delete(file);
            foreach (var save in playerSaves) File.WriteAllBytes(save.Key, save.Value);
        }

        [UnityTest]
        public IEnumerator FirstDrowningBringsTheGoblinOnceAndHeLeavesInAPoof()
        {
            var game = Object.FindAnyObjectByType<GameLoop>();
            var lecture = game.lecture;
            Assert.That(lecture, Is.Not.Null, "The scene wires the drowning lecture to the game loop.");
            Assert.That(lecture.goblin, Is.Not.Null);
            Assert.That(lecture.spot, Is.Not.Null);
            Assert.That(lecture.bubble, Is.SameAs(game.tutorial.bubble), "Everyone speaks on the same plate.");
            Assert.That(lecture.goblin, Is.Not.SameAs(game.tutorial.goblin), "He is not the shop tutorial's goblin.");
            Assert.That(lecture.goblin.gameObject.activeSelf, Is.False, "He waits off stage.");
            Assert.That(game.drownLecture, Is.Not.Empty);
            game.SkipTutorial();
            Assert.That(game.DrownLectureGiven, Is.False);
            var world = game.expedition;

            yield return DepartAndDrown(game);
            Assert.That(world.LastOutingDrowned, Is.True);
            Assert.That(lecture.Active, Is.True, "The goblin waits in the shop after the first drowning.");
            Assert.That(lecture.goblin.gameObject.activeSelf, Is.True);
            Assert.That(Vector3.Distance(lecture.goblin.transform.position, lecture.spot.position), Is.LessThan(.01f));
            Assert.That(game.player.control.HasModalOwner, Is.True, "The player stands still while he talks.");
            Assert.That(game.LastNotice, Is.Empty, "The return notice waits until he is gone.");
            Assert.That(game.TryDepart(), Is.False, "The door stays shut while he talks.");

            yield return WaitFor(() => lecture.Line == 0, 5);
            Assert.That(lecture.Line, Is.EqualTo(0), "The first line comes as the picture returns.");
            Assert.That(lecture.bubble.Visible, Is.True);

            int lastLine = -1;
            float end = Time.realtimeSinceStartup + 10;
            while (lecture.Active && Time.realtimeSinceStartup < end)
            {
                lastLine = Mathf.Max(lastLine, lecture.Line);
                lecture.Continue(); // the first press only finishes a line that is still typing
                yield return null;
            }
            Assert.That(lecture.Active, Is.False, "Every line closes on E.");
            Assert.That(lastLine, Is.EqualTo(game.drownLecture.Length - 1), "Each line was on the plate.");
            Assert.That(lecture.goblin.gameObject.activeSelf, Is.False, "He leaves in a poof.");
            Assert.That(lecture.bubble.Visible, Is.False);
            Assert.That(game.player.control.HasModalOwner, Is.False, "The shop is the player's again.");
            Assert.That(game.DrownLectureGiven, Is.True);
            Assert.That(game.LastNotice, Does.Contain("поражения"), "The return notice follows him.");
            yield return null; // the game loop saves the change in its LateUpdate
            Assert.That(GameSaveStore.Load(game.SavePath, game.ValidateSave, out var save, out var warning), Is.True, warning);
            Assert.That(save.drownLectureGiven, Is.True);

            yield return DepartAndDrown(game);
            Assert.That(world.LastOutingDrowned, Is.True);
            Assert.That(lecture.Active, Is.False, "He says it once.");
            Assert.That(lecture.goblin.gameObject.activeSelf, Is.False);
            Assert.That(game.player.control.HasModalOwner, Is.False);
            Assert.That(game.LastNotice, Does.Contain("поражения"));
            // Errors and exceptions still fail the test on their own; plain logs (the apiary's shadow-atlas note) do not.
        }

        /// <summary>Out to the ready-made location (the place every outing goes to before the revenue goal), straight into deep water.</summary>
        static IEnumerator DepartAndDrown(GameLoop game)
        {
            var world = game.expedition;
            if (world.readyWorld) world.NextPlace = OutingPlace.Ready;
            Assert.That(game.TryDepart(), Is.True, "The shop can leave on an outing.");
            yield return WaitFor(() => world.Running, 5);
            Assert.That(world.Running, Is.True);
            world.vitals.Immortal = false;
            world.Drown();
            Assert.That(world.Returning, Is.True, "Deep water drowns at once.");
            yield return WaitFor(() => game.Phase == GamePhase.Shop && !world.Returning, 10);
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Shop), "Drowning brings the player home.");
        }

        static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
#endif
