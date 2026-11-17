#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hakaton.Dungeons;
using Hakaton.Dungeons.Unity;
using Hakaton.Expeditions;
using Hakaton.Game;
using Hakaton.WorldAssembly.Unity;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.WorldAssembly.Tests
{
    /// <summary>
    /// The anthill in the real game: the cheat finds a mound, walking into its throat takes the player down into a
    /// dungeon built under it, the way out under the light slot brings the player back to the mouth, and going in again
    /// finds the same dungeon. Photographs the way in and some chambers into Logs/dungeon-play.
    /// </summary>
    public sealed class DungeonPlayTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";
        readonly Dictionary<string, byte[]> playerSaves = new Dictionary<string, byte[]>();
        string saveFolder, shots;
        GameLoop game;
        ExpeditionWorld world;
        WorldStreamer streamer;

        [UnitySetUp]
        public IEnumerator DepartIntoANewWorld()
        {
            saveFolder = Path.Combine(Application.persistentDataPath, "Hakaton");
            shots = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "dungeon-play");
            Directory.CreateDirectory(shots);
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
            SceneManager.SetActiveScene(SceneManager.CreateScene("Dungeon teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            if (Directory.Exists(saveFolder))
                foreach (var file in Directory.GetFiles(saveFolder)) File.Delete(file);
            foreach (var save in playerSaves) File.WriteAllBytes(save.Key, save.Value);
        }

        [UnityTest]
        public IEnumerator TheAnthillIsEnteredLeftAndFoundAsLeft()
        {
            var site = streamer.Dungeons;
            string note = streamer.GoToNearestDungeon();
            Debug.Log("[Dungeon] " + note);
            Assert.That(note, Does.StartWith("Иду"), note);
            DungeonEntrance entrance = null;
            yield return WaitFor(() => (entrance = Object.FindObjectsByType<DungeonEntrance>(FindObjectsSortMode.None).FirstOrDefault(e => (e.outside.position - world.motor.transform.position).sqrMagnitude < 4)) != null, 8);
            Assert.That(entrance, Is.Not.Null, "the player stands at a mound's mouth");
            yield return new WaitForSeconds(1.5f);
            yield return Shot("00-mound");

            // Walk into the throat.
            world.motor.Teleport(entrance.inside.position + Vector3.up * .1f, entrance.inside.rotation);
            yield return WaitFor(() => site.Underground, 4);
            Assert.That(site.Underground, Is.True, "stepping into the throat takes the player down");
            yield return new WaitForSeconds(3.5f);
            var dungeon = site.Current;
            var plan = dungeon.Plan;
            Debug.Log($"[Dungeon] seed {plan.Seed} large={plan.Large} levels={plan.Levels} rooms={plan.Rooms.Count} cells={plan.Cells.Count}");
            Assert.That(NavMesh.SamplePosition(dungeon.Arrival.position, out _, 1.5f, NavMesh.AllAreas), Is.True, "navigation where the player arrives");
            Assert.That((world.motor.transform.position - dungeon.Arrival.position).magnitude, Is.LessThan(1.5f));
            var air = streamer.GetComponent<WorldAtmosphere>();
            Debug.Log($"[Dungeon] air: burrow {air.Strength(WorldAtmosphere.Burrow):0.00}, fog {RenderSettings.fogColor} x{RenderSettings.fogDensity:0.0000}, ambient {RenderSettings.ambientMode} {RenderSettings.ambientLight}");
            yield return Shot("01-arrival");
            // Diagnostics of the light underground.
            var probe = RenderSettings.ambientProbe;
            Debug.Log($"[Dungeon] probe L0 r{probe[0, 0]:0.000} g{probe[1, 0]:0.000} b{probe[2, 0]:0.000}; sun {RenderSettings.sun?.intensity}; volumes: " +
                string.Join(", ", Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None).Where(v => v.isActiveAndEnabled).Select(v => $"{v.name} w{v.weight:0.00} {(v.sharedProfile ? string.Join("/", v.sharedProfile.components.Where(c => c.active).Select(c => c.name)) : "")}")));
            var anyRenderer = dungeon.Built.GetComponentInChildren<MeshRenderer>();
            Debug.Log($"[Dungeon] renderer {anyRenderer.name} probes {anyRenderer.lightProbeUsage} mat {anyRenderer.sharedMaterial.shader.name}");
            var vols = Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None).Where(v => v.enabled).ToList();
            foreach (var v in vols) v.enabled = false;
            yield return null;
            yield return Shot("01b-no-volumes");
            foreach (var v in vols) v.enabled = true;
            var was = RenderSettings.ambientLight;
            RenderSettings.ambientLight = Color.white; DynamicGI.UpdateEnvironment();
            yield return null; yield return null;
            yield return Shot("01c-white-ambient");
            RenderSettings.ambientLight = was; DynamicGI.UpdateEnvironment();
            var culling = dungeon.GetComponent<DungeonCulling>();
            Assert.That(culling.Shown, Is.LessThan(culling.Total), "far cells are not drawn");

            // A look into every chamber from its doorway.
            int i = 2;
            foreach (var room in plan.Rooms)
            {
                var door = room.Doors[room.UsedDoors.FirstOrDefault()];
                var at = dungeon.Built.TransformPoint(DungeonGeometry.Center(door.cell, room.Floor) - DungeonGeometry.Offset(door.side) * 1.2f);
                var center = Vector3.zero; foreach (var c in room.Cells) center += DungeonGeometry.Center(c, room.Floor); center /= room.Cells.Count;
                var look = dungeon.Built.TransformPoint(center) - at; look.y = 0;
                world.motor.Teleport(at + Vector3.up * .1f, Quaternion.LookRotation(look));
                yield return new WaitForSeconds(.6f);
                yield return Shot($"{i++:00}-{room.Shape.Role}");
            }

            // Back up the way out.
            world.motor.Teleport(dungeon.Arrival.position, dungeon.Arrival.rotation);
            yield return new WaitForSeconds(.5f);
            world.motor.Teleport(dungeon.WayOut + Vector3.up * .1f, dungeon.Arrival.rotation);
            yield return WaitFor(() => !site.Underground, 4);
            Assert.That(site.Underground, Is.False, "the light slot takes the player back up");
            yield return new WaitForSeconds(1f);
            var coord = streamer.CoordAt(world.motor.transform.position);
            yield return WaitFor(() => (entrance = Object.FindObjectsByType<DungeonEntrance>(FindObjectsSortMode.None).FirstOrDefault(e => (e.outside.position - world.motor.transform.position).sqrMagnitude < 2.25f)) != null, 6);
            Assert.That(entrance, Is.Not.Null, "out at the mound's mouth (its chunk loaded again)");
            yield return Shot("99-back-out");

            // In again: the same dungeon.
            world.motor.Teleport(entrance.inside.position + Vector3.up * .1f, entrance.inside.rotation);
            yield return WaitFor(() => site.Underground, 4);
            Assert.That(site.Current, Is.SameAs(dungeon), "the dungeon is kept as it was left");
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(shots, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
        }

        static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
        }
    }
}
#endif
