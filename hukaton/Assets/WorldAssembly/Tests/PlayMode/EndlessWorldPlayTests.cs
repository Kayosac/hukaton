#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// The endless world inside the real game scene: an outing builds a world around the start overlook, creatures can
    /// walk between chunks, chunks unload behind the player and come back as they were left. Also photographs the
    /// start from the player's eyes into Logs/world-plans.
    /// </summary>
    public sealed class EndlessWorldPlayTests
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
            SceneManager.SetActiveScene(SceneManager.CreateScene("Endless world teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            if (Directory.Exists(saveFolder))
                foreach (var file in Directory.GetFiles(saveFolder)) File.Delete(file);
            foreach (var save in playerSaves) File.WriteAllBytes(save.Key, save.Value);
        }

        [UnityTest]
        public IEnumerator CreaturesCanWalkFromTheOverlookAcrossRingZero()
        {
            Assert.That(streamer.Active, Is.True);
            Assert.That(streamer.LoadedCount, Is.GreaterThan(40));
            var start = world.motor.transform.position;
            Assert.That(NavMesh.SamplePosition(start + Vector3.down * 3, out var from, 6, NavMesh.AllAreas), Is.True, "navigation under the overlook");
            var path = new NavMeshPath();
            var failures = new List<string>();
            int checkedChunks = 0;
            foreach (var view in Object.FindObjectsByType<ChunkView>(FindObjectsSortMode.None))
            {
                var coord = streamer.CoordAt(view.transform.position);
                if (streamer.Map.Chunk(coord).Ring != 0 || view.shapeId == Landmarks.StartHill.PartId(1, 1)) continue;
                var targets = view.findSockets.Concat(view.creatureSockets).Where(s => s).ToList();
                if (targets.Count == 0) continue;
                checkedChunks++;
                bool any = false;
                foreach (var target in targets)
                {
                    if (!NavMesh.SamplePosition(target.position, out var to, 1.5f, NavMesh.AllAreas)) continue;
                    NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path);
                    if (path.status == NavMeshPathStatus.PathComplete) { any = true; break; }
                }
                if (!any) failures.Add($"{view.name}");
            }
            Debug.Log($"[World] ring 0: {checkedChunks} chunks checked, unreachable for creatures: {string.Join(", ", failures)}");
            var creatures = world.creatures.Where(c => c && c.gameObject.activeInHierarchy).ToList();
            Assert.That(creatures.Count, Is.GreaterThan(0), "creatures appear around the start");
            var lost = creatures.Where(c => !c.navigator.isOnNavMesh).Select(c => c.name).ToList();
            Assert.That(lost, Is.Empty, "every creature stands on the navigation: " + string.Join(", ", lost));
            Assert.That(checkedChunks, Is.GreaterThan(10));
            Assert.That(failures.Count, Is.LessThanOrEqualTo(checkedChunks / 10), string.Join("\n", failures));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ChunksComeBackAsTheyWereLeft()
        {
            // Take one find near the start.
            var loot = world.finds.Where(l => l && l.Available).OrderBy(l => Vector3.Distance(l.transform.position, world.motor.transform.position)).First();
            var place = loot.transform.position;
            Assert.That(loot.PickUp(world.viewCamera.transform), Is.True);
            yield return new WaitForSeconds(.5f);
            int lootValue = world.LootValue;
            Assert.That(lootValue, Is.GreaterThan(0));

            // Walk (teleport) far away until that chunk unloads, then come back.
            var home = world.motor.transform.position;
            var coord = streamer.CoordAt(place);
            world.motor.Teleport(home + new Vector3(300, 0, 0), Quaternion.identity);
            yield return WaitFor(() => Object.FindObjectsByType<ChunkView>(FindObjectsSortMode.None).All(v => streamer.CoordAt(v.transform.position).X > coord.X + 4), 10);
            Assert.That(Object.FindObjectsByType<ChunkView>(FindObjectsSortMode.None).Any(v => streamer.CoordAt(v.transform.position).Equals(coord)), Is.False, "the chunk unloaded");
            world.motor.Teleport(home, Quaternion.identity);
            yield return WaitFor(() => Object.FindObjectsByType<ChunkView>(FindObjectsSortMode.None).Any(v => streamer.CoordAt(v.transform.position).Equals(coord)), 10);
            yield return null;
            Assert.That(world.finds.Any(l => l && l.Available && Vector3.Distance(l.transform.position, place) < .5f), Is.False, "the taken find stays taken");
            Assert.That(world.LootValue, Is.EqualTo(lootValue));
        }

        [UnityTest]
        public IEnumerator PhotographTheStart()
        {
            yield return new WaitForSeconds(1.5f);
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "world-plans");
            Directory.CreateDirectory(dir);
            var cam = world.viewCamera;
            var start = world.motor.transform.position;
            foreach (var (label, yaw) in new[] { ("north", 0f), ("east", 90f), ("south", 180f), ("west", 270f) })
            {
                world.motor.Teleport(start, Quaternion.Euler(0, yaw, 0));
                yield return null;
                yield return null;
                var rt = new RenderTexture(1280, 720, 24);
                var previous = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = previous;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, $"play-{label}.png"), tex.EncodeToPNG());
                Object.Destroy(tex);
                rt.Release();
            }
        }

        [UnityTest]
        public IEnumerator PhotographTheWorld()
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "world-plans");
            Directory.CreateDirectory(dir);
            var wanted = new Dictionary<string, System.Func<ChunkView, bool>>
            {
                ["maze"] = v => v.shapeId.StartsWith("maze"),
                ["canyon"] = v => v.shapeId.StartsWith("canyon"),
                ["ramp"] = v => v.variantId == "ramp",
                ["cliff"] = v => v.shapeId == "cliff-edge" && v.variantId == "cliff",
                ["bridge"] = v => v.variantId == "bridge",
                ["island"] = v => v.variantId == "island",
                ["summit"] = v => v.shapeId.StartsWith("cliff") && v.transform.position.y > streamer.root.position.y + 3,
                ["gap"] = v => v.variantId == "gap",
                ["camp"] = v => v.variantId == "camp",
                ["ruins"] = v => v.variantId == "ruins",
                ["gully"] = v => v.variantId == "gully",
                ["tower"] = v => v.variantId == "tower",
                ["watchhill"] = v => v.shapeId == "watchhill-11",
                ["crater"] = v => v.shapeId == "crater-11",
                ["fort"] = v => v.shapeId == "fort-10",
            };
            var home = world.motor.transform.position;
            for (int step = 0; step < 12 && wanted.Count > 0; step++)
            {
                var spot = home + new Vector3(step * 40, 0, step * 25);
                world.motor.Teleport(spot, Quaternion.identity);
                for (int f = 0; f < 50; f++) yield return null;
                foreach (var key in wanted.Keys.ToList())
                {
                    var view = Object.FindObjectsByType<ChunkView>(FindObjectsSortMode.None).FirstOrDefault(wanted[key]);
                    if (!view) continue;
                    var centre = view.transform.position;
                    var eye = centre + new Vector3(-11, 7, -11);
                    yield return Shot(dir, "play-" + key, eye, centre + Vector3.up);
                    wanted.Remove(key);
                }
            }
            // From the player's own eyes, out in the first and second rings.
            int walk = 0;
            foreach (var (offset, look) in new[] { (new Vector3(56, 0, 8), Vector3.right), (new Vector3(-10, 0, 64), Vector3.forward), (new Vector3(-60, 0, -40), new Vector3(-1, 0, -.4f)), (new Vector3(90, 0, 70), new Vector3(1, 0, .6f)) })
            {
                var spot = home + offset;
                world.motor.Teleport(spot + Vector3.up * 20, Quaternion.identity);
                for (int f = 0; f < 50; f++) yield return null;
                // The ground under the spot, not the player falling onto it.
                var hits = Physics.RaycastAll(spot + Vector3.up * 40, Vector3.down, 80, ~(1 << 4), QueryTriggerInteraction.Ignore)
                    .Where(h => !h.collider.transform.IsChildOf(world.motor.transform)).OrderBy(h => h.distance).ToList();
                if (hits.Count == 0) continue;
                var eye = hits[0].point + Vector3.up * 1.7f;
                yield return Shot(dir, "play-walk-" + ++walk, eye, eye + look.normalized * 10 - Vector3.up * .6f);
            }
            // Far away, looking back at the start: the beacon and mountains through the fog.
            var back = home + new Vector3(140, 0, 0);
            world.motor.Teleport(back + Vector3.up * 20, Quaternion.identity);
            for (int f = 0; f < 60; f++) yield return null;
            var under = Physics.RaycastAll(back + Vector3.up * 40, Vector3.down, 80, ~(1 << 4), QueryTriggerInteraction.Ignore)
                .Where(h => !h.collider.transform.IsChildOf(world.motor.transform)).OrderBy(h => h.distance).ToList();
            var backEye = (under.Count > 0 ? under[0].point : back) + Vector3.up * 1.7f;
            yield return Shot(dir, "play-far-back", backEye, home + Vector3.up * 6);
            Debug.Log("[World] photographed; not found: " + string.Join(", ", wanted.Keys));
        }

        IEnumerator Shot(string dir, string name, Vector3 eye, Vector3 target)
        {
            // Stand where the camera will be, so streaming and far silhouettes follow it as in play.
            world.motor.Teleport(eye - Vector3.up * 1.6f, Quaternion.identity);
            // Let the chunks round the new spot stream in, as they would ahead of a walking player.
            for (int f = 0; f < 45; f++) yield return null;
            world.motor.Teleport(eye - Vector3.up * 1.6f, Quaternion.identity);
            yield return null;
            var cam = world.viewCamera;
            var saved = (cam.transform.position, cam.transform.rotation);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            var rt = new RenderTexture(1280, 720, 24);
            var previous = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = previous;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
            cam.transform.SetPositionAndRotation(saved.Item1, saved.Item2);
        }

        static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
#endif
