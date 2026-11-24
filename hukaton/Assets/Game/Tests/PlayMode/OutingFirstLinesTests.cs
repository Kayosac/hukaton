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
    /// The outings' two once-per-game lines: at an anthill's mouth and at the first hit taken (not in the cave prologue).
    /// Each comes once, the marks are in the save. In the real scene; the player's own save files are moved aside for the
    /// run and put back afterwards.
    /// </summary>
    public sealed class OutingFirstLinesTests
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
            MainMenu.SkipNextLaunch = true;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return WaitFor(() => Object.FindAnyObjectByType<GameLoop>()?.Ready == true, 5);
        }

        [UnityTearDown]
        public IEnumerator RestoreThePlayersSave()
        {
            SceneManager.SetActiveScene(SceneManager.CreateScene("Outing first lines test teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            if (Directory.Exists(saveFolder))
                foreach (var file in Directory.GetFiles(saveFolder)) File.Delete(file);
            foreach (var save in playerSaves) File.WriteAllBytes(save.Key, save.Value);
        }

        [UnityTest]
        public IEnumerator EachLineComesOncePerGame()
        {
            var game = Object.FindAnyObjectByType<GameLoop>();
            var world = game.expedition;

            // A hit in the cave prologue says nothing.
            yield return WaitFor(() => world.InPrologue && world.Running, 20);
            Assert.That(world.InPrologue && world.Running, Is.True, "A fresh game opens in the cave prologue.");
            world.vitals.Immortal = false;
            Assert.That(world.vitals.Hurt(1, Vector3.zero, null, true), Is.True);
            Assert.That(world.Notice, Is.Not.EqualTo(ExpeditionWorld.FirstHurtLine), "The prologue's hits do not count.");
            Assert.That(world.Firsts.firstHurtLineShown, Is.False);
            Assert.That(world.NoteAnthillApproached(), Is.False, "No anthill line in the cave.");

            game.SkipTutorial();
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Shop));

            // First outing: the first hit and the first anthill bring their lines, as the outing's notices.
            yield return Depart(game);
            Assert.That(world.vitals.Hurt(1, Vector3.zero, null, true), Is.True);
            Assert.That(world.Notice, Is.EqualTo("Упадёшь — половина добычи останется в лесу."));
            Assert.That(Time.time, Is.LessThan(world.NoticeUntil), "The line is on screen.");
            Assert.That(world.NoteAnthillApproached(), Is.True);
            Assert.That(world.Notice, Is.EqualTo("Муравейник. Внутри темно и бьют больно. „Пора домой“ работает и там."));
            world.Announce("другое", 1);
            yield return new WaitForSeconds(.8f); // past the protection after the hit
            Assert.That(world.vitals.Hurt(1, Vector3.zero, null, true), Is.True);
            Assert.That(world.NoteAnthillApproached(), Is.False);
            Assert.That(world.Notice, Is.EqualTo("другое"), "Neither line comes twice.");
            yield return ComeHome(game);
            yield return null; // the game loop saves the change in its LateUpdate
            Assert.That(GameSaveStore.Load(game.SavePath, game.ValidateSave, out var save, out var warning), Is.True, warning);
            Assert.That(save.outingFirsts != null && save.outingFirsts.firstHurtLineShown && save.outingFirsts.anthillLineShown, Is.True,
                "Both marks are in the save.");

            // The next outing: still nothing.
            yield return Depart(game);
            world.Announce("другое", 1);
            Assert.That(world.vitals.Hurt(1, Vector3.zero, null, true), Is.True);
            Assert.That(world.NoteAnthillApproached(), Is.False);
            Assert.That(world.Notice, Is.EqualTo("другое"));
            yield return ComeHome(game);
        }

        static IEnumerator Depart(GameLoop game)
        {
            var world = game.expedition;
            if (world.readyWorld) world.NextPlace = OutingPlace.Ready;
            Assert.That(game.TryDepart(), Is.True, "The shop can leave on an outing.");
            yield return WaitFor(() => world.Running, 5);
            Assert.That(world.Running, Is.True);
            world.vitals.Immortal = false;
        }

        // A defeat by the kill height, the way the smoke test leaves: back in the shop.
        static IEnumerator ComeHome(GameLoop game)
        {
            var world = game.expedition;
            world.vitals.Hurt(world.vitals.HalfHearts, Vector3.zero, null, true);
            yield return WaitFor(() => game.Phase == GamePhase.Shop && !world.Returning, 10);
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Shop));
        }

        static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
#endif
