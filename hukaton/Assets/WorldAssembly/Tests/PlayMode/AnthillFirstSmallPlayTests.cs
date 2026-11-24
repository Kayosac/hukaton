#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hakaton.Dungeons.Unity;
using Hakaton.Expeditions;
using Hakaton.Game;
using Hakaton.WorldAssembly.Unity;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.WorldAssembly.Tests
{
    /// <summary>
    /// The anthill in a new game: small (one level, no Queen) until its prize chest is opened; opening it is noted in the
    /// game's save, and the anthill already laid stays as it was.
    /// </summary>
    public sealed class AnthillFirstSmallPlayTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";
        readonly Dictionary<string, byte[]> playerSaves = new Dictionary<string, byte[]>();
        string saveFolder;
        GameLoop game;
        ExpeditionWorld world;
        WorldStreamer streamer;

        [UnitySetUp]
        public IEnumerator DepartIntoANewWorld()
        {
            saveFolder = Path.Combine(Application.persistentDataPath, "Hakaton");
            playerSaves.Clear();
            if (Directory.Exists(saveFolder))
                foreach (var file in Directory.GetFiles(saveFolder)) { playerSaves[file] = File.ReadAllBytes(file); File.Delete(file); }
            MainMenu.SkipNextLaunch = true;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return WaitFor(() => Object.FindAnyObjectByType<GameLoop>()?.Ready == true, 5);
            game = Object.FindAnyObjectByType<GameLoop>();
            world = Object.FindAnyObjectByType<ExpeditionWorld>();
            streamer = Object.FindAnyObjectByType<WorldStreamer>();
            streamer.fixedSeed = 2;
            game.SkipTutorial();
            Assert.That(game.TryDepart(), Is.True);
            yield return WaitFor(() => world.Running, 3);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator RestoreThePlayersSave()
        {
            Invulnerable(false);
            SceneManager.SetActiveScene(SceneManager.CreateScene("Anthill teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            if (Directory.Exists(saveFolder))
                foreach (var file in Directory.GetFiles(saveFolder)) File.Delete(file);
            foreach (var save in playerSaves) File.WriteAllBytes(save.Key, save.Value);
            DungeonProgress.Restore(false);
        }

        [UnityTest]
        public IEnumerator TheFirstAnthillIsSmallAndItsPrizeIsNotedButItStaysAsLeft()
        {
            var site = streamer.Dungeons;
            Invulnerable(true);
            Assert.That(DungeonProgress.AnthillPrizeOpened, Is.False, "a new game has opened no anthill prize");
            Assert.That(game.Capture().anthillPrizeOpened, Is.False);
            DungeonEntrance entrance = null;
            yield return GoIn(e => entrance = e);
            var dungeon = site.Current;
            Debug.Log($"[Anthill] first: seed {dungeon.Plan.Seed} large={dungeon.Plan.Large} levels={dungeon.Plan.Levels}");
            Assert.That(dungeon.Plan.Large, Is.False, "the first anthill is the small one");
            Assert.That(dungeon.Plan.Levels, Is.EqualTo(1));
            Assert.That(site.Creatures.Any(c => c.name == "AntQueen"), Is.False, "no Queen");
            var junction = dungeon.Plan.Rooms.First(r => r.Shape.Role == Hakaton.Dungeons.RoomRole.Junction);
            var workersAtJunction = site.Creatures.Count(c => c.name == "AntWorker" && Within(dungeon, junction, c.transform.position));
            Debug.Log($"[Anthill] workers at the junction: {workersAtJunction}");
            Assert.That(workersAtJunction, Is.InRange(1, 2), "one or two workers at the junction");

            var prize = site.Chests.Single(c => c.tier && c.tier.displayName == "Сундук муравейника");
            world.motor.Teleport(prize.transform.position - prize.transform.forward * -2.2f + Vector3.up * .1f, Quaternion.LookRotation(Flat(prize.transform.position - world.motor.transform.position)));
            yield return new WaitForSeconds(.3f);
            Assert.That(prize.Open(), Is.True);
            yield return null; yield return null;
            Assert.That(DungeonProgress.AnthillPrizeOpened, Is.True, "the first prize chest is noted");
            Assert.That(game.Capture().anthillPrizeOpened, Is.True, "and goes into the save");

            // Up and in again: the same small anthill, as it was left.
            yield return GoOut(entrance);
            for (float s = 0; s <= 6.6f && !site.Underground; s += .5f) { Stand(entrance.passage, s, 0); yield return null; yield return null; }
            Assert.That(site.Current, Is.SameAs(dungeon), "the anthill is kept as it was left");
            Assert.That(site.Current.Plan.Large, Is.False, "and stays small");
            Assert.That(prize.Opened, Is.True);
        }

        IEnumerator GoIn(System.Action<DungeonEntrance> found)
        {
            var site = streamer.Dungeons;
            string note = streamer.GoToNearestDungeon();
            Debug.Log("[Anthill] " + note);
            Assert.That(note, Does.StartWith("Иду"), note);
            DungeonEntrance entrance = null;
            yield return WaitFor(() => (entrance = DungeonEntrance.All.FirstOrDefault(e => e && (e.outside.position - world.motor.transform.position).sqrMagnitude < 4)) != null, 8);
            Assert.That(entrance, Is.Not.Null, "the player stands before a hill's mouth");
            found(entrance);
            // The cheat's flight is over once the screen is light again: only then does walking into the passage count.
            yield return new WaitForSeconds(1f);
            for (float s = 0; s <= 6.6f && !site.Underground; s += .5f) { Stand(entrance.passage, s, 0); yield return null; yield return null; }
            Assert.That(site.Underground, Is.True);
            for (float s = 6; s >= -1; s -= .5f) { Stand(site.Current.Passage, s, 180); yield return null; yield return null; }
            yield return new WaitForSeconds(.3f);
        }

        IEnumerator GoOut(DungeonEntrance entrance)
        {
            var site = streamer.Dungeons;
            var dungeon = site.Current;
            for (float s = 0; s <= 6.6f && site.Underground; s += .5f) { Stand(dungeon.Passage, s, 0); yield return null; yield return null; }
            Assert.That(site.Underground, Is.False, "the passage's dark end leads back up");
            yield return WaitFor(() => entrance && entrance.passage, 6);
            for (float s = 6; s >= -2; s -= .5f) { Stand(entrance.passage, s, 180); yield return null; yield return null; }
            yield return new WaitForSeconds(.5f);
        }

        static bool Within(DungeonBuilder dungeon, Hakaton.Dungeons.PlacedRoom room, Vector3 p)
        {
            var local = dungeon.Built.InverseTransformPoint(p);
            const float S = Hakaton.Dungeons.DungeonPlan.CellSize;
            var cell = new Hakaton.Dungeons.Cell(Mathf.RoundToInt(local.x / S), Mathf.RoundToInt(local.z / S));
            return room.Cells.Contains(cell);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > 1e-4f ? v : Vector3.forward; }

        static void Invulnerable(bool on)
        {
            var cheats = Object.FindAnyObjectByType<CheatPowers>();
            if (cheats) cheats.SetInvulnerable(on);
        }

        void Stand(Transform passage, float s, float yaw)
        {
            var at = passage.TransformPoint(new Vector3(0, .1f, s));
            world.motor.Teleport(at, passage.rotation * Quaternion.Euler(0, yaw, 0));
        }

        static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
        }
    }
}
#endif
