#if UNITY_EDITOR
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Game.Tests
{
    /// <summary>
    /// The real game in a test saves into the test's temporary folder, never into the player's own one
    /// (<see cref="GameSaveFolder.PlayerFolder"/>): the path it uses, and the file it actually writes, are checked.
    /// </summary>
    public sealed class SaveFolderIsolationPlayTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";
        string folder;

        [UnitySetUp]
        public IEnumerator LoadTheGameOnATemporarySave()
        {
            folder = GameSaveFolder.UseTemporaryForTests(nameof(SaveFolderIsolationPlayTests));
            MainMenu.SkipNextLaunch = true;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            float end = Time.realtimeSinceStartup + 5;
            while (!(Object.FindAnyObjectByType<GameLoop>()?.Ready ?? false) && Time.realtimeSinceStartup < end) yield return null;
        }

        [UnityTearDown]
        public IEnumerator UnloadAndDropTheTemporarySave()
        {
            SceneManager.SetActiveScene(SceneManager.CreateScene("Save folder test teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            GameSaveFolder.EndTemporaryForTests();
        }

        [UnityTest]
        public IEnumerator TheGameUnderATestSavesOnlyIntoTheTestsTemporaryFolder()
        {
            var game = Object.FindAnyObjectByType<GameLoop>();
            Assert.That(game, Is.Not.Null);
            Assert.That(game.Ready, Is.True);
            string player = Path.GetFullPath(GameSaveFolder.PlayerFolder);
            string current = Path.GetFullPath(GameSaveFolder.Current);
            Assert.That(GameSaveFolder.IsRedirected, Is.True);
            Assert.That(current, Is.EqualTo(Path.GetFullPath(folder)));
            Assert.That(current, Does.StartWith(Path.GetFullPath(GameSaveFolder.TemporaryRoot)));
            Assert.That(current, Does.Not.StartWith(player), "The test's folder is not inside the player's one.");
            string save = Path.GetFullPath(game.SavePath);
            Assert.That(Path.GetDirectoryName(save), Is.EqualTo(current), "The game reads and writes the save in the test's folder.");
            Assert.That(save, Does.Not.StartWith(player));
            Assert.That(game.LoadedFromSave, Is.False, "Nothing of the player's was loaded: the folder starts empty.");

            game.SkipTutorial();
            Assert.That(game.SaveNow(), Is.True);
            Assert.That(File.Exists(save), Is.True, "The save is written where the game says it is.");
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }
    }
}
#endif
