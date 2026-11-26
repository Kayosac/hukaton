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
    /// The endless world's two once-per-game lines (balance stage 7): past the calm start into ring 1 (232 m) and after
    /// the first mimic bite. Each comes once, not in the cave prologue, the marks are in the save; a line that comes
    /// while another holds the screen waits for it. In the real scene; the save goes to a temporary folder of the test.
    /// </summary>
    public sealed class WorldFirstLinesTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";

        [UnitySetUp]
        public IEnumerator LoadTheGameWithAFreshSave()
        {
            GameSaveFolder.UseTemporaryForTests(nameof(WorldFirstLinesTests));
            MainMenu.SkipNextLaunch = true;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return WaitFor(() => Object.FindAnyObjectByType<GameLoop>()?.Ready == true, 5);
        }

        [UnityTearDown]
        public IEnumerator RestoreThePlayersSave()
        {
            SceneManager.SetActiveScene(SceneManager.CreateScene("World first lines test teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            GameSaveFolder.EndTemporaryForTests();
        }

        [UnityTest]
        public IEnumerator RingOneAndTheFirstMimicBiteSpeakOncePerGame()
        {
            var game = Object.FindAnyObjectByType<GameLoop>();
            var world = game.expedition;

            yield return WaitFor(() => world.InPrologue && world.Running, 20);
            Assert.That(world.InPrologue && world.Running, Is.True, "A fresh game opens in the cave prologue.");
            Assert.That(world.NoteMimicBite(), Is.False, "No mimic line in the prologue.");
            game.SkipTutorial();
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Shop));

            // First outing into the endless world.
            yield return Depart(game);
            var centre = world.OutingWorld as IOutingCentre;
            Assert.That(centre, Is.Not.Null, "the endless world has a centre");
            Assert.That(centre.WilderFrom, Is.EqualTo(232).Within(.5f), "ring 1 begins 232 m from the centre");
            Assert.That(centre.TryGetCentre(out var middle), Is.True);
            var start = world.motor.transform.position;
            yield return Frames(3);
            Assert.That(world.Notice, Is.Not.EqualTo(ExpeditionWorld.WilderRingLine), "Nothing at the start.");
            yield return GoOut(world, middle, 225);
            Assert.That(world.Notice, Is.Not.EqualTo(ExpeditionWorld.WilderRingLine), "Nothing in ring 0.");
            yield return GoOut(world, middle, 240);
            Assert.That(world.Notice, Is.EqualTo("Дальше звери злее, а добыча богаче."), "Past the calm start.");
            Assert.That(world.Firsts.wilderRingLineShown, Is.True);
            world.motor.Teleport(start, Quaternion.identity);
            yield return new WaitForSeconds(ExpeditionWorld.FirstLineSeconds + .1f);

            // The first mimic bite is also the first hit: the hit's line first, then the mimic's.
            Assert.That(world.vitals.Hurt(1, Vector3.zero, null, true), Is.True);
            Assert.That(world.NoteMimicBite(), Is.True);
            Assert.That(world.Notice, Is.EqualTo(ExpeditionWorld.FirstHurtLine), "The hit's line holds its time.");
            Assert.That(world.NoteMimicBite(), Is.False, "Once, even while it waits.");
            yield return new WaitForSeconds(ExpeditionWorld.FirstLineSeconds + .2f);
            Assert.That(world.Notice, Is.EqualTo("Сундук с зубами. Заморозь или брось карту в закрытый."));
            Assert.That(world.Firsts.mimicLineShown, Is.True);
            yield return new WaitForSeconds(ExpeditionWorld.FirstLineSeconds + .1f);
            world.Announce("другое", 1);
            Assert.That(world.NoteMimicBite(), Is.False);
            yield return GoOut(world, middle, 240);
            Assert.That(world.Notice, Is.EqualTo("другое"), "Neither line comes twice.");
            world.motor.Teleport(start, Quaternion.identity);
            yield return ComeHome(game);
            yield return null; // the game loop saves the change in its LateUpdate
            Assert.That(GameSaveStore.Load(game.SavePath, game.ValidateSave, out var save, out var warning), Is.True, warning);
            Assert.That(save.outingFirsts != null && save.outingFirsts.wilderRingLineShown && save.outingFirsts.mimicLineShown, Is.True,
                "Both marks are in the save.");

            // The next outing: still nothing.
            yield return Depart(game);
            Assert.That(world.Firsts.wilderRingLineShown && world.Firsts.mimicLineShown, Is.True);
            world.Announce("другое", 1);
            Assert.That(world.NoteMimicBite(), Is.False);
            Assert.That((world.OutingWorld as IOutingCentre).TryGetCentre(out middle), Is.True);
            yield return GoOut(world, middle, 240);
            Assert.That(world.Notice, Is.EqualTo("другое"));
            yield return ComeHome(game);
        }

        // Stands the player this far from the centre (the line is told on the next frame); not long enough to fall far.
        static IEnumerator GoOut(ExpeditionWorld world, Vector3 middle, float metres)
        {
            world.motor.Teleport(new Vector3(middle.x + metres, world.motor.transform.position.y, middle.z), Quaternion.identity);
            yield return Frames(3);
            Assert.That(world.DistanceFromCentre, Is.EqualTo(metres).Within(3f), "where the player stands");
        }

        static IEnumerator Depart(GameLoop game)
        {
            var world = game.expedition;
            world.NextPlace = OutingPlace.Endless;
            Assert.That(game.TryDepart(), Is.True, "The shop can leave on an outing.");
            yield return WaitFor(() => world.Running, 10);
            Assert.That(world.Running, Is.True);
            world.vitals.Immortal = false;
        }

        // A defeat, the way the smoke test leaves: back in the shop.
        static IEnumerator ComeHome(GameLoop game)
        {
            var world = game.expedition;
            world.vitals.Hurt(world.vitals.HalfHearts, Vector3.zero, null, true);
            yield return WaitFor(() => game.Phase == GamePhase.Shop && !world.Returning, 10);
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Shop));
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
