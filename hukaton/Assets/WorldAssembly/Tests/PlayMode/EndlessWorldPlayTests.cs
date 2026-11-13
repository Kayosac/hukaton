#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions;
using Hakaton.Expeditions.Cards;
using Hakaton.Game;
using Hakaton.OldApiary;
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
            // The game itself, without the main menu lying over it.
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
                // Ring 0 as far as the chunks loaded round the overlook reach: further out, the way may run through
                // chunks that load only as the player comes closer.
                if (coord.X * coord.X + coord.Y * coord.Y > 3 * 3 || view.shapeId == Landmarks.StartHill.PartId(1, 1)) continue;
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

        /// <summary>
        /// Walks the player in a straight line at a sprint across the world and reports what streaming costs: chunk
        /// loads and unloads, frame times, draw calls. Not a pass/fail on speed (the editor is slower than a build), but
        /// a record to compare against: Logs/world-plans/streaming.txt.
        /// </summary>
        [UnityTest]
        public IEnumerator MeasureStreamingWhileWalking()
        {
            yield return new WaitForSeconds(1);
            streamer.LoadTimes = streamer.UnloadTimes = default;
            var start = world.motor.transform.position;
            var frames = new List<float>();
            long batches = 0, tris = 0, samples = 0;
            const float speed = 6.5f;
            float travelled = 0;
            while (travelled < 400)
            {
                float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
                travelled += speed * dt;
                var spot = start + new Vector3(travelled * .8f, 0, travelled * .6f);
                world.motor.Teleport(spot + Vector3.up * 30, Quaternion.identity);
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000);
                batches += UnityEditor.UnityStats.batches;
                tris += UnityEditor.UnityStats.triangles;
                samples++;
            }
            frames.Sort();
            string report = $"loads {streamer.LoadTimes}\nunloads {streamer.UnloadTimes}\n" +
                $"frames {frames.Count}: median {frames[frames.Count / 2]:0.0} ms, 95% {frames[(int)(frames.Count * .95f)]:0.0} ms, worst {frames[frames.Count - 1]:0.0} ms\n" +
                $"batches avg {batches / Mathf.Max(1, samples)}, triangles avg {tris / Mathf.Max(1, samples)}\nloaded now {streamer.LoadedCount}";
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "Logs", "world-plans"));
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "Logs", "world-plans", "streaming.txt"), report);
            Assert.That(streamer.LoadTimes.Count, Is.GreaterThan(10));
        }

        [UnityTest]
        public IEnumerator CheatsMakeThePlayerInvulnerableAndLetHimFly()
        {
            var powers = Object.FindAnyObjectByType<CheatPowers>();
            Assert.That(powers, Is.Not.Null, "cheat powers come with the game loop");
            var vitals = world.vitals;
            int hearts = vitals.HalfHearts;
            powers.SetInvulnerable(true);
            Assert.That(vitals.Hurt(99, Vector3.zero, null), Is.False, "no damage");
            Assert.That(vitals.Drain(), Is.False, "no drowning");
            Assert.That(vitals.HalfHearts, Is.EqualTo(hearts));
            powers.SetInvulnerable(false);

            powers.SetFlying(true);
            yield return null;
            yield return null;
            Assert.That(world.motor.ExternalOwner, Is.SameAs(powers), "flight drives the body");
            Assert.That(world.motor.characterController.enabled, Is.False, "and flies through everything");
            powers.SetFlying(false);
            yield return null;
            Assert.That(world.motor.ExternallyControlled, Is.False);
            Assert.That(world.motor.characterController.enabled, Is.True);
        }

        [UnityTest]
        public IEnumerator TheStartIsALookoutAndCampfiresCrackle()
        {
            var silhouettes = streamer.GetComponent<FarSilhouettes>();
            yield return WaitFor(() => silhouettes.Revealed, 2);
            Assert.That(silhouettes.Revealed, Is.True, "the top of the start hill shows far landmarks");
            Assert.That(silhouettes.MarkerRange, Is.GreaterThan(silhouettes.farRadius));
            yield return null;
            Assert.That(world.viewCamera.farClipPlane, Is.GreaterThanOrEqualTo(silhouettes.MarkerRange), "the camera sees as far as the markers");
            Assert.That(streamer.RoadsPainted, Is.GreaterThan(0), "roads lead away from the start");

            // The nearest camp: its fire loops once the player stands by it.
            ChunkCoord camp = default;
            bool found = false;
            for (int r = 1; r < 12 && !found; r++)
                for (int x = -r; x <= r && !found; x++)
                    for (int y = -r; y <= r && !found; y++)
                    {
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) != r) continue;
                        var plan = streamer.Map.Chunk(x, y);
                        if (plan.Variant.Id == "camp") { camp = new ChunkCoord(x, y); found = true; }
                    }
            Assert.That(found, Is.True, "a camp within 12 chunks");
            var origin = streamer.root ? streamer.root.position : streamer.transform.position;
            float size = streamer.Map.Rules.ChunkSize;
            world.motor.Teleport(origin + new Vector3(camp.X * size, streamer.Map.Chunk(camp).BaseLevel * streamer.Map.Rules.PlateauHeight + 2, camp.Y * size), Quaternion.identity);
            AmbientSpot fire = null;
            yield return WaitFor(() => (fire = Object.FindObjectsByType<AmbientSpot>(FindObjectsSortMode.None).FirstOrDefault(a => a.cue && streamer.CoordAt(a.transform.position).Equals(camp))) != null, 10);
            Assert.That(fire, Is.Not.Null, "the camp has a place that sounds");
            world.motor.Teleport(fire.transform.position + new Vector3(1.5f, 1, 0), Quaternion.identity);
            yield return new WaitForSeconds(.3f);
            Assert.That(world.sfx.pool.Any(a => a && a.isPlaying && a.loop && fire.cue.clips.Contains(a.clip)), Is.True, "the fire crackles");
        }

        [UnityTest]
        public IEnumerator GroupsSpawnTogetherNoticeThePlayerAndComeBack()
        {
            Object.FindAnyObjectByType<CheatPowers>().SetInvulnerable(true);
            // The nearest chunk with a group of three or more, out of the calm first ring.
            ChunkCoord den = default;
            int expected = 0;
            for (int r = 4; r < 14 && expected == 0; r++)
                for (int x = -r; x <= r && expected == 0; x++)
                    for (int y = -r; y <= r && expected == 0; y++)
                    {
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) != r) continue;
                        var plan = streamer.Map.Chunk(x, y);
                        int count = plan.Spawns.Sum(g => g.Count);
                        if (count >= 3 && plan.Spawns.Any(g => g.Count >= 2)) { den = new ChunkCoord(x, y); expected = count; }
                    }
            Assert.That(expected, Is.GreaterThan(0), "a group within 14 chunks");
            var origin = streamer.root ? streamer.root.position : streamer.transform.position;
            float size = streamer.Map.Rules.ChunkSize;
            var centre = origin + new Vector3(den.X * size, streamer.Map.Chunk(den).BaseLevel * streamer.Map.Rules.PlateauHeight, den.Y * size);
            // Stand two chunks off, out of their sight, while they load.
            world.motor.Teleport(centre + new Vector3(2 * size, 6, 0), Quaternion.identity);
            System.Func<List<WorldCreature>> members = () => Object.FindObjectsByType<ChunkView>(FindObjectsSortMode.None)
                .Where(v => streamer.CoordAt(v.transform.position).Equals(den))
                .SelectMany(v => v.GetComponentsInChildren<WorldCreature>()).Where(c => c.Alive).ToList();
            yield return WaitFor(() => members().Count > 0, 10);
            yield return new WaitForSeconds(.5f);
            var group = members();
            Debug.Log($"[World] group at {den}: {string.Join(", ", streamer.Map.Chunk(den).Spawns)} → {group.Count} creatures");
            Assert.That(group.Count, Is.EqualTo(expected), "the whole group came");
            foreach (var c in group)
            {
                bool found = NavMesh.SamplePosition(c.transform.position, out var nearest, 6, NavMesh.AllAreas);
                var on = Physics.RaycastAll(c.transform.position + Vector3.up * .5f, Vector3.down, 6, ~(1 << 4), QueryTriggerInteraction.Ignore)
                    .Where(h => !h.collider.transform.IsChildOf(c.transform)).OrderBy(h => h.distance).Select(h => $"{h.collider.transform.parent?.name}/{h.collider.name}").FirstOrDefault();
                Debug.Log($"[World] {c.name} at {c.transform.position} (chunk local {c.transform.position - centre}), navigation {(found ? (nearest.position - c.transform.position).ToString() : "none")}, state {c.State}, on {on}");
            }
            foreach (var c in group)
                // Bodies push each other a little as a group lands, and pumpkins hop: within reach of the navigation (a hop
                // above it at most), not off in a wall or up in a tree.
                Assert.That(NavMesh.SamplePosition(c.transform.position, out _, 2.5f, NavMesh.AllAreas), Is.True, $"{c.name} at {c.transform.position - centre} stands on the navigation");
            float spread = group.SelectMany(a => group.Where(b => b != a).Select(b => Vector3.Distance(a.transform.position, b.transform.position))).Min();
            Assert.That(spread, Is.GreaterThan(.5f), "not on one spot");

            // From the next chunk over (flat ground at the same height), they can come for the player across the cut.
            var denPlan = streamer.Map.Chunk(den);
            var side = Sides.All.First(sd =>
            {
                var n = streamer.Map.Chunk(den.Step(sd));
                bool flat = n.Shape.Kind == ShapeKind.Plain || n.Shape.Kind == ShapeKind.PlateauTop || n.Shape.Kind == ShapeKind.Landmark;
                return flat && n.BaseLevel == denPlan.BaseLevel;
            });
            var next = den.Step(side);
            // Three metres past the cut, by the middle of the edge where the chunks are joined (not on whatever stands in
            // the middle of that chunk).
            var near = centre + new Vector3((next.X - den.X) * (size * .5f + 3), 0, (next.Y - den.Y) * (size * .5f + 3));
            Assert.That(NavMesh.SamplePosition(near + Vector3.up, out var ground, 3, NavMesh.AllAreas), Is.True);
            var path = new NavMeshPath();
            bool across = false;
            foreach (var c in group)
            {
                if (!NavMesh.SamplePosition(c.transform.position, out var from, 1.5f, NavMesh.AllAreas)) continue;
                NavMesh.CalculatePath(from.position, ground.position, NavMesh.AllAreas, path);
                Debug.Log($"[World] {c.name} → next chunk: {path.status}, corners {path.corners.Length}, end {(path.corners.Length > 0 ? path.corners[path.corners.Length - 1] - centre : Vector3.zero)}");
                across |= path.status == NavMeshPathStatus.PathComplete;
            }
            Debug.Log($"[World] group in {denPlan.Shape.Id}/{denPlan.Variant.Id} r{denPlan.Rotation} can reach {streamer.Map.Chunk(next)} ({side}, target {ground.position - centre}): {across}");
            world.motor.Teleport(group[0].transform.position + new Vector3(3.5f, 1.5f, 0), Quaternion.identity);
            yield return new WaitForSeconds(4);
            var calm = new[] { CreatureState.Idle, CreatureState.Wander, CreatureState.Graze };
            Assert.That(group.Any(c => c && !calm.Contains(c.State)), Is.True, "someone noticed the player: " + string.Join(", ", group.Where(c => c).Select(c => c.State)));

            // Far away and back: the group is still there (whoever was not killed).
            int alive = members().Count;
            world.motor.Teleport(centre + new Vector3(320, 10, 0), Quaternion.identity);
            yield return WaitFor(() => members().Count == 0, 10);
            Assert.That(members().Count, Is.EqualTo(0), "unloaded with its chunk");
            world.motor.Teleport(centre + new Vector3(2 * size, 6, 0), Quaternion.identity);
            yield return WaitFor(() => members().Count > 0, 10);
            yield return null;
            Assert.That(members().Count, Is.EqualTo(alive), "the same group comes back");
            Assert.That(across, Is.True, "the group reaches into the next chunk");
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
                ["beeyard"] = v => v.shapeId == "beeyard-11",
                ["village"] = v => v.shapeId == "village-01",
                ["stone-forest"] = v => v.shapeId == "stone-forest-10",
                ["giant-log"] = v => v.shapeId == "giant-log-01",
                ["apiary"] = v => v.variantId == "apiary",
                ["road"] = v => v.transform.Find("Road") && v.shapeId == "plain",
                ["falls"] = v => v.variantId == "falls",
                ["mesa"] = v => v.shapeId == "mesa-01",
                ["sinkhole"] = v => v.shapeId == "sinkhole-10",
                ["swamp"] = v => v.shapeId == "swamp-11",
                ["lake"] = v => v.shapeId == "lake-11",
                ["forest"] = v => v.variantId == TileShapes.Forest,
                ["forest-glade"] = v => v.variantId == TileShapes.ForestGlade,
                ["forest-heart"] = v => v.variantId == TileShapes.ForestHeart,
            };
            // The air of each place: settles at once for the photos.
            var air = streamer.GetComponent<WorldAtmosphere>();
            if (air) air.settle = .05f;
            // Not over water: the player standing there would drown and end the outing.
            var moods = new List<string> { WorldAtmosphere.Rain, WorldAtmosphere.Storm, WorldAtmosphere.Swamp, WorldAtmosphere.High };
            var home = world.motor.transform.position;
            for (int step = 0; step < 20 && wanted.Count > 0; step++)
            {
                var spot = home + new Vector3(step * 40, 0, step * 25);
                world.motor.Teleport(spot, Quaternion.identity);
                for (int f = 0; f < 50; f++) yield return null;
                if (!streamer.Active) { Debug.Log($"[World] photo walk: the outing ended at step {step}"); break; }
                foreach (var key in wanted.Keys.ToList())
                {
                    var view = Object.FindObjectsByType<ChunkView>(FindObjectsSortMode.None).FirstOrDefault(wanted[key]);
                    if (!view) continue;
                    var centre = view.transform.position;
                    if (key.StartsWith("forest"))
                    {
                        // Over the crowns, then down on a lane looking along it to where the creatures wait (taken
                        // first: the view may be reused for another chunk while the camera is up there).
                        var from = view.findSockets.FirstOrDefault(x => x);
                        var to = view.creatureSockets.FirstOrDefault(x => x);
                        var inEye = from ? from.position + Vector3.up * 1.6f : centre;
                        var inTarget = to ? to.position + Vector3.up * (key == "forest-heart" ? 3 : 1) : centre;
                        // The player waits on the lane meanwhile: a fall from up there would end the outing.
                        yield return Shot(dir, "play-" + key, centre + new Vector3(-16, 24, -16), centre, inEye);
                        if (from && to) yield return Shot(dir, "play-" + key + "-in", inEye, inTarget);
                        wanted.Remove(key);
                        continue;
                    }
                    var eye = centre + new Vector3(-11, 7, -11);
                    yield return Shot(dir, "play-" + key, eye, centre + Vector3.up);
                    wanted.Remove(key);
                }
                if (air)
                    foreach (var mood in moods.ToList())
                    {
                        if (air.Strength(mood) < .6f) continue;
                        var at = world.motor.transform.position + Vector3.up * 1.7f;
                        yield return Shot(dir, "play-air-" + mood, at, at + new Vector3(1, -.08f, .6f) * 10);
                        var weather = streamer.transform.Find("Weather");
                        int alive = 0;
                        if (weather)
                            foreach (Transform effect in weather)
                                if (effect.name.StartsWith(mood + " "))
                                    foreach (var ps in effect.GetComponentsInChildren<ParticleSystem>()) alive += ps.particleCount;
                        Debug.Log($"[World] air {mood}: strength {air.Strength(mood):0.00}, fog {RenderSettings.fogDensity:0.0000}, particles {alive}");
                        moods.Remove(mood);
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
            // Where the player came down, if nothing was found under the spot (the ground may still be streaming in).
            var backEye = (under.Count > 0 ? under[0].point : world.motor.transform.position) + Vector3.up * 1.7f;
            yield return Shot(dir, "play-far-back", backEye, home + Vector3.up * 6);
            Debug.Log("[World] photographed; not found: " + string.Join(", ", wanted.Keys.Concat(moods.Select(m => "air " + m))));
        }

        [UnityTest]
        public IEnumerator ChunksBurnAwakenAndRollLikeTheOldApiary()
        {
            Assert.That(streamer.fire, Is.Not.Null, "the endless world runs fire");
            var air = streamer.GetComponent<WorldAtmosphere>();
            Assert.That(air.postProcess, Is.Not.Null, "the static location's post-processing");
            Assert.That(air.postProcess.enabled && air.postProcess.isGlobal && air.postProcess.sharedProfile, Is.True, "post-processing on during the outing");
            var views = Object.FindObjectsByType<ChunkView>(FindObjectsSortMode.None);
            int burnables = views.Sum(v => v.burnables.Length), trees = views.Sum(v => v.trees.Length), props = views.Sum(v => v.props.Length);
            Debug.Log($"[World] loaded chunks: {burnables} burnables, {trees} trees to awaken, {props} loose props");
            Assert.That(burnables, Is.GreaterThan(300), "grass, bushes and wood burn");
            Assert.That(trees, Is.GreaterThan(20), "trees can be awakened");
            Assert.That(props, Is.GreaterThan(5), "loose things can be pushed");
            Assert.That(views.SelectMany(v => v.trees).All(t => t && t.GetComponentInChildren<Collider>()), Is.True, "a card can hit every awakenable tree");
            yield return null;

            // Fire takes the grass of a chunk by the start and spreads.
            var start = world.motor.transform.position;
            var view = views.Where(v => v.burnables.Count(b => b && b.style == BurnStyle.Ash && b.isActiveAndEnabled) > 6)
                .OrderBy(v => Vector3.Distance(v.transform.position, start)).First();
            var coord = streamer.CoordAt(view.transform.position);
            int grassIndex = System.Array.FindIndex(view.burnables, b => b && b.style == BurnStyle.Ash && b.isActiveAndEnabled);
            var grass = view.burnables[grassIndex];
            Assert.That(world.Ignite(null, grass.Bounds.center, .5f, IgnitionSource.Fire), Is.GreaterThan(0), $"Fire lights the grass of {view.name}");
            yield return WaitFor(() => streamer.fire.Lit > 1, 6);
            Assert.That(streamer.fire.Lit, Is.GreaterThan(1), "flames spread to the neighbours");
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "world-plans");
            Directory.CreateDirectory(dir);
            var flame = grass.Bounds.center;
            yield return Shot(dir, "play-fire", flame + new Vector3(-4.5f, 2.2f, -4.5f), flame, start + Vector3.up * 1.6f);

            // A tree comes alive (as the card hides it), a loose prop is kicked away.
            int treeIndex = System.Array.FindIndex(view.trees, t => t && t.Standing);
            var treeView = treeIndex >= 0 ? view : views.First(v => v.trees.Any(t => t && t.Standing));
            if (treeIndex < 0) treeIndex = System.Array.FindIndex(treeView.trees, t => t && t.Standing);
            var treeCoord = streamer.CoordAt(treeView.transform.position);
            world.HideForOuting(treeView.trees[treeIndex].gameObject);
            var prop = views.SelectMany(v => v.props).First(p => p && p.body && !p.body.isKinematic);
            var before = prop.body.position;
            prop.body.AddForce(Vector3.up * 4 + Vector3.right * 3, ForceMode.VelocityChange);
            for (int f = 0; f < 20; f++) yield return null;
            var touching = Physics.OverlapBox(prop.body.worldCenterOfMass, Vector3.one * .3f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)
                .Where(c => !c.transform.IsChildOf(prop.transform)).Select(c => $"{c.transform.parent?.name}/{c.name}");
            Debug.Log($"[World] pushed {prop.name} at {before} in {streamer.CoordAt(before)}: moved {Vector3.Distance(prop.body.position, before):0.000} m, kinematic {prop.body.isKinematic}, sleeping {prop.body.IsSleeping()}, touching {string.Join(", ", touching)}");
            Assert.That(Vector3.Distance(prop.body.position, before), Is.GreaterThan(.3f), "a loose prop flies when pushed");

            // Far away and back: the chunks come back burnt and with the empty place of the tree.
            for (int f = 0; f < 240 && streamer.fire.Burning > 0; f++) yield return null;
            var away = start + new Vector3(260, 0, 260);
            world.motor.Teleport(away + Vector3.up * 30, Quaternion.identity);
            yield return WaitFor(() => !streamer.IsLoaded(coord) && !streamer.IsLoaded(treeCoord), 10);
            Assert.That(streamer.IsLoaded(coord), Is.False, "the burnt chunk unloads");
            world.motor.Teleport(start, Quaternion.identity);
            yield return WaitFor(() => streamer.IsLoaded(coord) && streamer.IsLoaded(treeCoord), 20);
            Assert.That(streamer.IsLoaded(coord) && streamer.IsLoaded(treeCoord), Is.True, $"the chunks {coord} and {treeCoord} come back");
            for (int f = 0; f < 5; f++) yield return null;
            ChunkView At(ChunkCoord c) => Object.FindObjectsByType<ChunkView>(FindObjectsSortMode.None).First(v => streamer.CoordAt(v.transform.position).Equals(c));
            var again = At(coord).burnables[grassIndex];
            Assert.That(streamer.fire.Touched(again), Is.True, "the grass is still burnt");
            Assert.That(again.GetComponentsInChildren<Renderer>().All(r => !r.enabled), Is.True, "burnt to ash");
            Assert.That(At(treeCoord).trees[treeIndex].gameObject.activeSelf, Is.False, "the awakened tree's place stays empty");
        }

        [UnityTest]
        public IEnumerator AWokenTreeLeavesItsPlaceInTheForestWallOpen()
        {
            // The nearest cell of a giant forest.
            var map = streamer.Map;
            ChunkPlan cell = null;
            for (int x = -30; x <= 30; x++)
                for (int y = -30; y <= 30; y++)
                {
                    var p = map.Chunk(x, y);
                    if (!p.Forest || p.MazeHeart) continue;
                    if (cell == null || x * x + y * y < cell.Coord.X * cell.Coord.X + cell.Coord.Y * cell.Coord.Y) cell = p;
                }
            Assert.That(cell, Is.Not.Null, "a giant forest within 480 m");
            float size = map.Rules.ChunkSize;
            var centre = streamer.root.position + new Vector3(cell.Coord.X * size, cell.BaseLevel * map.Rules.PlateauHeight, cell.Coord.Y * size);
            world.motor.Teleport(centre + Vector3.up * 3, Quaternion.identity);
            yield return WaitFor(() => streamer.IsLoaded(cell.Coord), 10);
            for (int f = 0; f < 30; f++) yield return null;
            var view = Object.FindObjectsByType<ChunkView>(FindObjectsSortMode.None).First(v => streamer.CoordAt(v.transform.position).Equals(cell.Coord));

            // A giant tree of the wall: it stops a body as it looks, with the trunks and thorns round it that are its;
            // woken, it takes them along and nothing is left where it stood.
            var tree = view.trees.First(t => t && t.Standing && t.transform.parent && t.transform.parent.name == "Giant trees");
            var piece = tree.GetComponentsInChildren<Collider>().Where(c => !c.isTrigger).ToArray();
            var spot = tree.transform.position + Vector3.up * 1.2f;
            bool Blocked() => Physics.OverlapSphere(spot, .3f, ~(1 << 4), QueryTriggerInteraction.Ignore)
                .Any(c => c.transform.IsChildOf(view.transform) && !c.GetComponent<TerrainCollider>() && c.transform.parent != view.transform);
            Assert.That(Blocked(), Is.True, $"the wall where {tree.name} stands");
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "world-plans");
            Directory.CreateDirectory(dir);
            var toward = Flat(tree.transform.position - centre);
            var eye = tree.transform.position - toward.normalized * 7 + Vector3.up * 1.7f;
            yield return Shot(dir, "play-tree-before", eye, tree.transform.position + Vector3.up * 2.5f, centre + Vector3.up * 1.7f);
            world.HideForOuting(tree.gameObject);
            yield return null;
            Assert.That(Blocked(), Is.False, "the woken tree took its piece of the wall with it");
            Assert.That(piece.All(c => !c.enabled || !c.gameObject.activeInHierarchy), Is.True);
            yield return Shot(dir, "play-tree-after", eye, tree.transform.position + Vector3.up * 2.5f, centre + Vector3.up * 1.7f);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        [UnityTest]
        public IEnumerator TheGreatTreeHasALookoutHollowTrunksLetTheLaneThroughFireOpensTheDeadfall()
        {
            var map = streamer.Map;
            float size = map.Rules.ChunkSize;
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "world-plans");
            Directory.CreateDirectory(dir);
            ChunkPlan Nearest(string variant)
            {
                ChunkPlan best = null;
                for (int x = -30; x <= 30; x++)
                    for (int y = -30; y <= 30; y++)
                    {
                        var p = map.Chunk(x, y);
                        if (p.Variant.Id != variant || p.Shape.Kind != ShapeKind.Maze) continue;
                        if (best == null || x * x + y * y < best.Coord.X * best.Coord.X + best.Coord.Y * best.Coord.Y) best = p;
                    }
                return best;
            }
            Vector3 Centre(ChunkPlan p) => streamer.root.position + new Vector3(p.Coord.X * size, p.BaseLevel * map.Rules.PlateauHeight, p.Coord.Y * size);
            ChunkView ViewAt(ChunkCoord c) => Object.FindObjectsByType<ChunkView>(FindObjectsSortMode.None).First(v => streamer.CoordAt(v.transform.position).Equals(c));
            bool Walk(Vector3 from, Vector3 to, float reach = 1.5f)
            {
                if (!NavMesh.SamplePosition(from, out var a, reach, NavMesh.AllAreas) || !NavMesh.SamplePosition(to, out var b, reach, NavMesh.AllAreas)) return false;
                var path = new NavMeshPath();
                return NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            }

            // The heart: a stair of shelf fungi up the great tree to its lookout, walked from the ring below.
            var heart = Nearest(TileShapes.ForestHeart);
            Assert.That(heart, Is.Not.Null, "a forest's heart within 480 m");
            world.motor.Teleport(Centre(heart) + new Vector3(4.6f, 3, 0), Quaternion.identity);
            yield return WaitFor(() => streamer.IsLoaded(heart.Coord), 20);
            Assert.That(streamer.IsLoaded(heart.Coord), Is.True, $"{heart} loads");
            for (int f = 0; f < 40; f++) yield return null;
            var view = ViewAt(heart.Coord);
            Assert.That(view.lookouts.Length, Is.EqualTo(1), "a lookout on the great tree");
            var lookout = view.lookouts[0].transform.position;
            Assert.That(lookout.y - view.transform.position.y, Is.GreaterThan(6), "high up the tree");
            var ring = view.creatureSockets[0].position;
            Assert.That(Walk(ring, lookout), Is.True, "the stair leads up from the ring");
            yield return Shot(dir, "play-heart-stair", ring + Vector3.up * 1.7f + (ring - Centre(heart)).normalized * .4f, lookout - Vector3.up * 2, ring + Vector3.up * 1.7f);
            var outward = Flat(lookout - Centre(heart)).normalized;
            yield return Shot(dir, "play-heart-lookout", lookout + Vector3.up * 1.7f, lookout + Vector3.up * 1.2f + outward * 10, ring + Vector3.up * 1.7f);

            // A hollow trunk: the lane runs on through it.
            var hollow = Nearest(TileShapes.ForestHollow);
            Assert.That(hollow, Is.Not.Null, "a hollow trunk within 480 m");
            world.motor.Teleport(Centre(hollow) + Vector3.up * 3, Quaternion.identity);
            yield return WaitFor(() => streamer.IsLoaded(hollow.Coord), 20);
            Assert.That(streamer.IsLoaded(hollow.Coord), Is.True, $"{hollow} loads");
            for (int f = 0; f < 40; f++) yield return null;
            view = ViewAt(hollow.Coord);
            var log = view.GetComponentsInChildren<Transform>().First(t => t.name == "Hollow trunk");
            Assert.That(log.GetComponentsInChildren<MeshCollider>().Length, Is.GreaterThan(0), "the trunk is solid as it looks");
            float half = log.lossyScale.z * 2.86f / 2;
            var endA = log.position + log.forward * (half + 1.2f) + Vector3.up;
            var endB = log.position - log.forward * (half + 1.2f) + Vector3.up;
            Assert.That(Walk(endA, endB), Is.True, "through the hollow trunk");
            yield return Shot(dir, "play-hollow-trunk", endB + log.forward * -2.5f + Vector3.up * .8f, log.position + Vector3.up * 1.4f, endB + Vector3.up * .7f);

            // Deadfall closes a blind alcove with a reward behind it; fire opens it.
            var hoard = Nearest(TileShapes.ForestDeadfall);
            Assert.That(hoard, Is.Not.Null, "deadfall within 480 m");
            world.motor.Teleport(Centre(hoard) + Vector3.up * 3, Quaternion.identity);
            yield return WaitFor(() => streamer.IsLoaded(hoard.Coord), 20);
            Assert.That(streamer.IsLoaded(hoard.Coord), Is.True, $"{hoard} loads");
            for (int f = 0; f < 40; f++) yield return null;
            view = ViewAt(hoard.Coord);
            var deadfall = view.obstacles.First(o => o && o.name == "Deadfall");
            Assert.That(deadfall.Resolved, Is.False);
            Assert.That(view.pocketSocket.GetComponentInChildren<WorldLoot>(), Is.Not.Null, "a reward lies behind the deadfall");
            Assert.That(Walk(view.creatureSockets[0].position, view.pocketSocket.position), Is.False, "the deadfall shuts the alcove");
            var front = deadfall.transform.position - deadfall.transform.forward * 4 + Vector3.up * 1.7f;
            yield return Shot(dir, "play-deadfall-before", front, deadfall.transform.position + Vector3.up * 1.2f, view.creatureSockets[0].position + Vector3.up * 1.7f);
            Assert.That(deadfall.React(CardAction.Fire, deadfall.transform.position), Is.True, "fire takes the deadfall");
            yield return WaitFor(() => !deadfall.barrier.enabled, 5);
            Assert.That(deadfall.barrier.enabled, Is.False, "the way to the reward is open");
            for (int f = 0; f < 60; f++) yield return null;
            yield return Shot(dir, "play-deadfall-after", front, deadfall.transform.position + Vector3.up * 1.2f, view.creatureSockets[0].position + Vector3.up * 1.7f);
        }

        [UnityTest]
        public IEnumerator TheAirThickensUnderTheGiantTreesAndClearsAfterTheOuting()
        {
            var air = streamer.GetComponent<WorldAtmosphere>();
            Assert.That(air, Is.Not.Null, "the streamer carries the world's atmosphere");
            air.settle = .05f;
            for (int f = 0; f < 10; f++) yield return null;
            float startFog = RenderSettings.fogDensity;
            Assert.That(Shader.GetGlobalFloat("_WorldBiomeStrength"), Is.GreaterThan(0), "plants take the colour of the ground's patches");
            // The nearest cell of a giant forest that is not its heart.
            var map = streamer.Map;
            ChunkPlan cell = null;
            for (int x = -30; x <= 30; x++)
                for (int y = -30; y <= 30; y++)
                {
                    var p = map.Chunk(x, y);
                    if (!p.Forest || p.MazeHeart) continue;
                    if (cell == null || x * x + y * y < cell.Coord.X * cell.Coord.X + cell.Coord.Y * cell.Coord.Y) cell = p;
                }
            Assert.That(cell, Is.Not.Null, "a giant forest within 480 m");
            var centre = streamer.root.position + new Vector3(cell.Coord.X * map.Rules.ChunkSize, cell.BaseLevel * map.Rules.PlateauHeight + 3, cell.Coord.Y * map.Rules.ChunkSize);
            world.motor.Teleport(centre, Quaternion.identity);
            for (int f = 0; f < 45; f++) yield return null;
            world.motor.Teleport(centre, Quaternion.identity);
            for (int f = 0; f < 60; f++) yield return null;
            Assert.That(air.Strength(WorldAtmosphere.Forest), Is.GreaterThan(.8f), $"forest air at {cell}");
            float forestFog = RenderSettings.fogDensity;
            Assert.That(forestFog, Is.GreaterThan(startFog * 1.2f), "thicker fog under the giant trees than at the start");
            // The outing ends (as on the way home): the scene's own air and colours come back.
            streamer.EndOuting();
            yield return null;
            yield return null;
            Assert.That(Shader.GetGlobalFloat("_WorldBiomeStrength"), Is.EqualTo(0f), "plants back to their own colour at home");
            Assert.That(RenderSettings.fogDensity, Is.LessThan(forestFog / 1.2f), "the scene's own fog back at home");
        }

        [UnityTest]
        public IEnumerator ChestsAndSmallEventsStandInTheWorldAndComeBackAsLeft()
        {
            Object.FindAnyObjectByType<CheatPowers>().SetInvulnerable(true);
            var map = streamer.Map;
            float size = map.Rules.ChunkSize;
            int seed = streamer.Seed;
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "world-plans");
            Directory.CreateDirectory(dir);
            List<ChunkPlan> Nearest(System.Func<ChunkPlan, bool> wanted)
            {
                var found = new List<ChunkPlan>();
                for (int x = -30; x <= 30; x++)
                    for (int y = -30; y <= 30; y++)
                    {
                        var p = map.Chunk(x, y);
                        if (wanted(p)) found.Add(p);
                    }
                return found.OrderBy(p => p.Coord.X * p.Coord.X + p.Coord.Y * p.Coord.Y).ToList();
            }
            Vector3 Centre(ChunkPlan p) => streamer.root.position + new Vector3(p.Coord.X * size, p.BaseLevel * map.Rules.PlateauHeight, p.Coord.Y * size);
            ChunkView ViewAt(ChunkCoord c) => Object.FindObjectsByType<ChunkView>(FindObjectsSortMode.None).First(v => streamer.CoordAt(v.transform.position).Equals(c));
            IEnumerator Visit(ChunkPlan p)
            {
                world.motor.Teleport(Centre(p) + Vector3.up * 3, Quaternion.identity);
                yield return WaitFor(() => streamer.IsLoaded(p.Coord), 10);
                for (int f = 0; f < 40; f++) yield return null;
            }
            IEnumerator Away(ChunkPlan p)
            {
                // Far enough that the chunk unloads, on ground the guarantee keeps walkable.
                world.motor.Teleport(Centre(p) + new Vector3(12 * size, 6, 0), Quaternion.identity);
                yield return WaitFor(() => !streamer.IsLoaded(p.Coord), 10);
                Assert.That(streamer.IsLoaded(p.Coord), Is.False, $"{p} unloads");
            }
            Vector3 Front(Transform t, float distance) => t.position + t.forward * distance + Vector3.up * 1.6f;

            // A ringing payphone: E uses it for good, and it stays silent when its chunk comes back.
            var phonePlan = Nearest(p => p.Event == WorldEvents.Payphone).FirstOrDefault();
            Assert.That(phonePlan, Is.Not.Null, "a payphone within 480 m");
            yield return Visit(phonePlan);
            var phone = ViewAt(phonePlan.Coord).GetComponentInChildren<Oddity>();
            Assert.That(phone, Is.Not.Null, $"the payphone stands on {phonePlan}");
            Assert.That(phone.kind, Is.EqualTo(Oddity.Kind.Payphone));
            Assert.That(phone.CanInteract, Is.True, "it rings for the player");
            yield return Shot(dir, "play-event-payphone", Front(phone.transform, 3.5f), phone.transform.position + Vector3.up * 1.3f);
            Assert.That(phone.Interact(), Is.True, "E answers it");
            yield return WaitFor(() => phone.Capture() == Oddity.State.Used, 10);
            Assert.That(phone.Capture(), Is.EqualTo(Oddity.State.Used));
            yield return Away(phonePlan);
            yield return Visit(phonePlan);
            phone = ViewAt(phonePlan.Coord).GetComponentInChildren<Oddity>();
            Assert.That(phone.Capture(), Is.EqualTo(Oddity.State.Used), "the answered payphone is silent when its chunk comes back");
            Assert.That(phone.CanInteract, Is.False);

            // A hissing television.
            var tvPlan = Nearest(p => p.Event == WorldEvents.Television).FirstOrDefault();
            Assert.That(tvPlan, Is.Not.Null, "a television within 480 m");
            yield return Visit(tvPlan);
            var tv = ViewAt(tvPlan.Coord).GetComponentInChildren<Oddity>();
            Assert.That(tv, Is.Not.Null, $"the television stands on {tvPlan}");
            Assert.That(tv.kind, Is.EqualTo(Oddity.Kind.Television));
            yield return Shot(dir, "play-event-television", Front(tv.transform, 3.5f), tv.transform.position + Vector3.up * .9f);

            // A beast brawl: two packs on the navigation; the fight ends and the survivors come back as they were.
            var brawlPlan = Nearest(p => p.Event == WorldEvents.Brawl).FirstOrDefault();
            Assert.That(brawlPlan, Is.Not.Null, "a brawl within 480 m");
            // Two chunks off: loaded, but out of the packs' sight, so the fight has not started yet.
            world.motor.Teleport(Centre(brawlPlan) + new Vector3(2 * size, 6, 0), Quaternion.identity);
            yield return WaitFor(() => streamer.IsLoaded(brawlPlan.Coord), 10);
            for (int f = 0; f < 40; f++) yield return null;
            var brawl = ViewAt(brawlPlan.Coord).GetComponentInChildren<Hakaton.Expeditions.Events.BeastBrawl>();
            Assert.That(brawl, Is.Not.Null, $"the brawl is on {brawlPlan}");
            Assert.That(brawl.Spawned, Is.True);
            var beasts = brawl.Beasts.ToList();
            Assert.That(beasts.Count, Is.GreaterThanOrEqualTo(3), "two packs");
            foreach (var beast in beasts)
                Assert.That(NavMesh.SamplePosition(beast.transform.position, out _, 1.5f, NavMesh.AllAreas), Is.True, $"{beast.name} stands on the navigation");
            Assert.That(brawl.Current, Is.EqualTo(Hakaton.Expeditions.Events.BeastBrawl.Phase.NotStarted), "nothing starts out of sight");
            // Ringside, 9 m off on the navigation; a calm pair is set on each other as a Манок would.
            var away = Flat(brawl.Centre - Centre(brawlPlan)).sqrMagnitude > 1 ? Flat(Centre(brawlPlan) - brawl.Centre).normalized : Vector3.forward;
            var ringside = brawl.Centre + away * 9;
            if (NavMesh.SamplePosition(ringside, out var side, 4, NavMesh.AllAreas)) ringside = side.position;
            ringside += Vector3.up * 1.7f;
            world.motor.Teleport(ringside - Vector3.up * 1.6f, Quaternion.identity);
            for (int f = 0; f < 10; f++) yield return null;
            if (brawl.Current == Hakaton.Expeditions.Events.BeastBrawl.Phase.NotStarted) brawl.Begin();
            for (int f = 0; f < 120; f++) yield return null;
            // Framed on the fighters themselves, 6 m off on the ringside's side.
            var fighters = brawl.Beasts.Where(b => b && b.Alive).ToList();
            var thick = fighters.Count > 0 ? fighters.Aggregate(Vector3.zero, (sum, b) => sum + b.transform.position) / fighters.Count : brawl.Centre;
            var eye = thick + Flat(ringside - thick).normalized * 6 + Vector3.up * 1.8f;
            yield return Shot(dir, "play-event-brawl", eye, thick + Vector3.up * .5f, ringside);
            yield return WaitFor(() => brawl.Current == Hakaton.Expeditions.Events.BeastBrawl.Phase.Over, 50);
            Assert.That(brawl.Current, Is.EqualTo(Hakaton.Expeditions.Events.BeastBrawl.Phase.Over), $"the fight ends ({brawl.FightSeconds:0} s)");
            int alive = brawl.AliveIn(0) + brawl.AliveIn(1);
            Debug.Log($"[World] brawl on {brawlPlan} ({brawl.variant}): {beasts.Count} beasts, {alive} left after {brawl.FightSeconds:0} s, started by {brawl.StartedBy}");
            Assert.That(brawl.FightSeconds, Is.GreaterThan(5), "a real fight, not one called off at once");
            Assert.That(alive, Is.LessThan(beasts.Count), "someone fell");
            yield return Away(brawlPlan);
            yield return Visit(brawlPlan);
            brawl = ViewAt(brawlPlan.Coord).GetComponentInChildren<Hakaton.Expeditions.Events.BeastBrawl>();
            Assert.That(brawl.Current, Is.EqualTo(Hakaton.Expeditions.Events.BeastBrawl.Phase.Over), "the fight stays over");
            Assert.That(brawl.AliveIn(0) + brawl.AliveIn(1), Is.EqualTo(alive), "the fallen stay fallen");

            // Chests: an ordinary one opens and stays open; a mimic on guard comes back on guard.
            ChestSpawnTable Table(ChunkPlan p) => streamer.content.ChestTable(p.Ring);
            TreasureChest Rolled(ChunkPlan p) => WorldRules.IsStartArea(p.Coord) || !Table(p) ? null
                : Table(p).Roll(Hash.Unit(seed, Salt.ChestPresence, p.Coord.X, p.Coord.Y), Hash.Unit(seed, Salt.ChestTier, p.Coord.X, p.Coord.Y));
            // References die with their chunk as the player moves on: keep the plans, look the chests up again.
            TreasureChest plain = null, mimicChest = null;
            ChunkPlan plainPlan = null, mimicPlan = null;
            var rolls = Nearest(p => Rolled(p) != null).Take(80).ToList();
            Debug.Log($"[World] chest rolls: {rolls.Count(p => !Rolled(p).GetComponent<ChestMimic>())} chests, {rolls.Count(p => Rolled(p).GetComponent<ChestMimic>())} mimics among the {rolls.Count} nearest");
            foreach (var p in rolls)
            {
                bool isMimic = Rolled(p).GetComponent<ChestMimic>();
                if (isMimic ? mimicPlan != null : plainPlan != null) continue;
                yield return Visit(p);
                var chest = ViewAt(p.Coord).GetComponentInChildren<TreasureChest>();
                if (!chest) continue;
                if (isMimic) { mimicChest = chest; mimicPlan = p; } else { plain = chest; plainPlan = p; }
                if (plainPlan != null && mimicPlan != null) break;
            }
            Assert.That(plainPlan, Is.Not.Null, "an ordinary chest among the nearest rolls");
            yield return Visit(plainPlan);
            plain = ViewAt(plainPlan.Coord).GetComponentInChildren<TreasureChest>();
            // A chest's lock is on its -Z side.
            yield return Shot(dir, "play-chest", Front(plain.transform, -2.6f), plain.transform.position + Vector3.up * .4f);
            Assert.That(plain.Interact(), Is.True, "E opens it");
            for (int f = 0; f < 60; f++) yield return null;
            yield return Away(plainPlan);
            yield return Visit(plainPlan);
            Assert.That(ViewAt(plainPlan.Coord).GetComponentInChildren<TreasureChest>().Opened, Is.True, "an opened chest stays open");
            if (mimicPlan != null)
            {
                yield return Visit(mimicPlan);
                mimicChest = ViewAt(mimicPlan.Coord).GetComponentInChildren<TreasureChest>();
                var mimic = mimicChest.GetComponent<ChestMimic>();
                yield return Shot(dir, "play-chest-mimic", Front(mimicChest.transform, -2.6f), mimicChest.transform.position + Vector3.up * .4f);
                Assert.That(mimic.Current, Is.EqualTo(ChestMimic.State.Closed));
                // Opened unfrozen: it bites (the player cannot be hurt here) and runs; then it plays chest on guard.
                world.motor.Teleport(mimicChest.transform.position - mimicChest.transform.forward * 1.6f, Quaternion.identity);
                yield return null;
                mimicChest.Interact();
                yield return WaitFor(() => mimic.Current == ChestMimic.State.Alert && !mimic.Busy, 25);
                Assert.That(mimic.Current, Is.EqualTo(ChestMimic.State.Alert), "on guard after the bite");
                yield return Away(mimicPlan);
                yield return Visit(mimicPlan);
                mimic = ViewAt(mimicPlan.Coord).GetComponentInChildren<ChestMimic>();
                Assert.That(mimic.Current, Is.EqualTo(ChestMimic.State.Alert), "the mimic comes back on guard");
            }
            else Debug.Log("[World] no mimic among the nearest chest rolls");
        }

        [UnityTest]
        public IEnumerator RainFrontsComeOverWetEverybodyPutFireOutAndThunder()
        {
            var rain = streamer.GetComponent<WorldRain>();
            var air = streamer.GetComponent<WorldAtmosphere>();
            Assert.That(rain, Is.Not.Null, "the world's rain goes with its air");
            air.settle = .3f;
            Assert.That(rain.RainsAt(world.motor.transform.position), Is.False, "no rain at the start");
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "world-plans");
            Directory.CreateDirectory(dir);
            // A thunderstorm over dry ground the player can stand on, that holds for a while; dry there a little earlier.
            var map = streamer.Map;
            var rules = map.Rules;
            ChunkPlan cell = null;
            float when = -1;
            for (float t = 60; t < 3600 && cell == null; t += 10)
                for (int x = -17; x <= 17 && cell == null; x++)
                    for (int y = -17; y <= 17 && cell == null; y++)
                    {
                        var plan = map.Chunk(x, y);
                        double d = System.Math.Sqrt(x * x + y * y) * rules.ChunkSize;
                        if (d < 150 || d > 270 || plan.BaseLevel >= 3) continue;
                        if (plan.Shape.Kind != ShapeKind.Plain && plan.Shape.Kind != ShapeKind.PlateauTop || plan.Variant.Id == "pond") continue;
                        double cx = x * rules.ChunkSize, cy = y * rules.ChunkSize;
                        bool holds = true;
                        for (float k = 0; k <= 50 && holds; k += 10) holds = WorldWeather.At(rules, streamer.Seed, cx, cy, t + k).Storm > .7f;
                        if (!holds || WorldWeather.At(rules, streamer.Seed, cx, cy, t - 30).Rain > 0) continue;
                        cell = plan;
                        when = t;
                    }
            Assert.That(cell, Is.Not.Null, "a thunderstorm within an hour");
            var (wx, wy) = WorldWeather.WindOf(streamer.Seed, rules.Weather);
            var upwind = -new Vector3((float)wx, 0, (float)wy).normalized;
            var centre = streamer.root.position + new Vector3(cell.Coord.X * rules.ChunkSize, cell.BaseLevel * rules.PlateauHeight + 3, cell.Coord.Y * rules.ChunkSize);
            Debug.Log($"[World] storm over {cell} at {when:0} s; wind {wx:0.0}, {wy:0.0} m/s");

            // Before it: still dry, the front comes on the wind.
            rain.SetClock(when - 30);
            world.motor.Teleport(centre, Quaternion.identity);
            for (int f = 0; f < 45; f++) yield return null;
            world.motor.Teleport(centre, Quaternion.identity);
            for (int f = 0; f < 30; f++) yield return null;
            // In the open, away from trunks and rocks, to see the sky.
            var feet = ClearSpot(world.motor.transform.position);
            world.motor.Teleport(feet, Quaternion.identity);
            for (int f = 0; f < 10; f++) yield return null;
            var eye = feet + Vector3.up * 1.7f;
            Assert.That(rain.RainsAt(feet), Is.False, "dry before the front");
            Assert.That(rain.Clouds, Is.GreaterThan(5), "the front's clouds are in the sky");
            var look = Open(eye, upwind);
            yield return Shot(dir, "play-rain-front-coming", eye, eye + look * 40 + Vector3.up * 8, feet + Vector3.up * 1.6f);
            Debug.Log($"[World] front coming: {rain.Clouds} clouds, {rain.Veils} rain veils, gloom {rain.Gloom:0.00}");

            // Under it: everybody out in the open gets wet, the air darkens, the rain is heard, lightning strikes.
            rain.SetClock(when + 2);
            float until = Time.time + 5;
            while (Time.time < until) yield return null;
            Assert.That(rain.RainsAt(feet), Is.True, "it rains on the player now");
            Assert.That(WetStatus.IsWet(world.Player.Root), Is.True, "the player is wet");
            Assert.That(rain.Falling, Is.GreaterThan(.5f), "rain falls round the camera");
            Assert.That(air.Strength(WorldAtmosphere.Rain), Is.GreaterThan(.6f), "rainy air");
            Assert.That(air.Strength(WorldAtmosphere.Storm), Is.GreaterThan(.5f), "stormy air");
            var weather = streamer.content;
            Assert.That(world.sfx.pool.Any(s => s && s.isPlaying && s.loop && (weather.rainLoop.clips.Contains(s.clip) || weather.downpourLoop.clips.Contains(s.clip))),
                Is.True, "the rain is heard");
            look = Open(eye, -upwind);
            yield return Shot(dir, "play-rain-storm", eye, eye + look * 30 + Vector3.up * 4, feet + Vector3.up * 1.6f);
            look = Open(eye, upwind);
            yield return Shot(dir, "play-rain-storm-sky", eye, eye + look * 10 + Vector3.up * 9, feet + Vector3.up * 1.6f);
            int strikes = rain.Strikes;
            yield return WaitFor(() => rain.Strikes > strikes, 30);
            Assert.That(rain.Strikes, Is.GreaterThan(strikes), "lightning strikes in a thunderstorm");
            Debug.Log($"[World] under the storm: {rain.Clouds} clouds, {rain.Veils} veils, {rain.Strikes} strikes, fog {RenderSettings.fogDensity:0.0000}");

            // A burning creature out in the rain goes out.
            rain.SetClock(when + 10);
            var creature = world.ActiveCreatures.FirstOrDefault(c => c && c.Alive && rain.RainsAt(c.Centre) && !rain.Sheltered(c.Centre, c.transform));
            if (creature)
            {
                int doused = rain.Doused;
                creature.Damage(0, Vector3.zero, creature.Centre, 8, DamageKind.Fire);
                if (creature.Burning)
                {
                    yield return WaitFor(() => !creature.Burning, 2);
                    Assert.That(creature.Burning, Is.False, $"{creature.name} put out by the rain");
                    Assert.That(rain.Doused, Is.GreaterThan(doused));
                    Assert.That(WetStatus.IsWet(creature.Root), Is.True, $"{creature.name} wet");
                }
                else Debug.Log($"[World] {creature.name} does not burn");
            }
            else Debug.Log("[World] no creature out in the rain");

            // Grass set alight in the rain goes out too.
            var grass = Object.FindObjectsByType<ChunkView>(FindObjectsSortMode.None)
                .SelectMany(v => v.burnables)
                .Where(b => b && b.isActiveAndEnabled && b.style == BurnStyle.Ash)
                .Select(b => (b, top: new Vector3(b.Bounds.center.x, b.Bounds.max.y + .05f, b.Bounds.center.z)))
                .Where(x => rain.RainsAt(x.top) && !rain.Sheltered(x.top, x.b.transform))
                .OrderBy(x => Vector3.Distance(x.b.transform.position, feet))
                .Select(x => x.b).FirstOrDefault();
            Assert.That(grass, Is.Not.Null, "grass out in the rain");
            Assert.That(world.Ignite(null, grass.Bounds.center, .5f, IgnitionSource.Fire), Is.GreaterThan(0), "fire takes the grass for a moment");
            Assert.That(streamer.fire.Burning, Is.GreaterThan(0));
            yield return WaitFor(() => streamer.fire.Burning == 0, 3);
            Assert.That(streamer.fire.Burning, Is.EqualTo(0), "the rain puts the fire out");

            // Home: the sky clears.
            streamer.EndOuting();
            yield return null;
            yield return null;
            Assert.That(rain.Clouds, Is.EqualTo(0), "no clouds left after the outing");
        }

        // ---------- what you see is what you hit: the rope and the fire ----------

        static readonly Dictionary<string, System.Func<Collider, bool>> RopeKinds = new Dictionary<string, System.Func<Collider, bool>>
        {
            ["branch"] = c => c.name.EndsWith("_Bark") && c.transform.parent && c.transform.parent.name.StartsWith("FA_TR"),
            ["crown"] = c => c.name.EndsWith("_Crown"),
            ["rock"] = c => c.name.StartsWith("RK"),
            ["wood"] = c => c.name.StartsWith("DW"),
            ["building"] = c => c.name.StartsWith("ST") || c.name.StartsWith("SP"),
        };

        /// <summary>Inside a closed shape: its surface is above the point, and nothing of it seen looking up from inside.</summary>
        static bool Inside(Vector3 point) => InsideOf(point) != null;

        static readonly Dictionary<Mesh, bool> closedMeshes = new Dictionary<Mesh, bool>();

        /// <summary>A closed shape (every edge shared by two faces, seams welded): only those have an inside.</summary>
        static bool Closed(Mesh mesh)
        {
            if (closedMeshes.TryGetValue(mesh, out bool closed)) return closed;
            var v = mesh.vertices;
            var t = mesh.triangles;
            var id = new int[v.Length];
            var welded = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < v.Length; i++)
            {
                var key = Vector3Int.RoundToInt(v[i] * 1000);
                if (!welded.TryGetValue(key, out int k)) welded[key] = k = welded.Count;
                id[i] = k;
            }
            var edges = new Dictionary<(int, int), int>();
            for (int i = 0; i + 2 < t.Length; i += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = id[t[i + e]], b = id[t[i + (e + 1) % 3]];
                    var edge = a < b ? (a, b) : (b, a);
                    edges.TryGetValue(edge, out int n);
                    edges[edge] = n + 1;
                }
            closed = edges.Values.All(n => n % 2 == 0);
            closedMeshes[mesh] = closed;
            return closed;
        }

        /// <summary>Under the ground's surface by more than <paramref name="margin"/> (the ground is an open sheet: no inside to test).</summary>
        static Collider Buried(Vector3 point, float margin)
        {
            foreach (var hit in Physics.RaycastAll(point + Vector3.up * 2, Vector3.down, 2, CardFlight.CollisionMask & ~(1 << 4), QueryTriggerInteraction.Ignore))
                if (hit.collider.name == "Ground" && hit.point.y > point.y + margin) return hit.collider;
            return null;
        }

        static Collider InsideOf(Vector3 point)
        {
            foreach (var c in Physics.OverlapSphere(point, 4, CardFlight.CollisionMask & ~(1 << 4), QueryTriggerInteraction.Ignore))
            {
                if (!c.bounds.Contains(point) || c is CharacterController) continue;
                if (!(c is MeshCollider mesh) || mesh.convex) { if ((c.ClosestPoint(point) - point).sqrMagnitude < 1e-6f) return c; continue; }
                if (!Closed(mesh.sharedMesh)) continue;
                var top = new Vector3(point.x, c.bounds.max.y + 1, point.z);
                if (top.y <= point.y) continue;
                if (!c.Raycast(new Ray(top, Vector3.down), out var down, top.y - point.y)) continue;
                if (!c.Raycast(new Ray(point, Vector3.up), out _, top.y - point.y)) return c;
            }
            return null;
        }

        ChunkPlan NearestPlan(System.Func<ChunkPlan, bool> wanted, int reach = 25)
        {
            ChunkPlan best = null;
            for (int x = -reach; x <= reach; x++)
                for (int y = -reach; y <= reach; y++)
                {
                    var p = streamer.Map.Chunk(x, y);
                    if (!wanted(p)) continue;
                    if (best == null || x * x + y * y < best.Coord.X * best.Coord.X + best.Coord.Y * best.Coord.Y) best = p;
                }
            return best;
        }

        IEnumerator Visit(ChunkPlan plan)
        {
            float size = streamer.Map.Rules.ChunkSize;
            var centre = streamer.root.position + new Vector3(plan.Coord.X * size, plan.BaseLevel * streamer.Map.Rules.PlateauHeight + 3, plan.Coord.Y * size);
            world.motor.Teleport(centre, Quaternion.identity);
            yield return WaitFor(() => streamer.IsLoaded(plan.Coord), 20);
            for (int f = 0; f < 40; f++) yield return null;
        }

        ChunkView LoadedView(ChunkCoord c) => Object.FindObjectsByType<ChunkView>(FindObjectsSortMode.None).FirstOrDefault(v => streamer.CoordAt(v.transform.position).Equals(c));

        /// <summary>
        /// A card thrown from 3 m off a point of the drawn surface of <paramref name="target"/>, as the player throws: the
        /// sweep of the card's own radius. Returns whether it struck that surface there (not something else first).
        /// </summary>
        bool Throw(Collider target, System.Func<Vector3, Vector3, bool> surface, System.Random rng, out RaycastHit hit, out Vector3 eye)
        {
            hit = default;
            eye = default;
            var mesh = ((MeshCollider)target).sharedMesh;
            var v = mesh.vertices;
            var t = mesh.triangles;
            var m = target.transform.localToWorldMatrix;
            for (int attempt = 0; attempt < 60; attempt++)
            {
                int k = rng.Next(t.Length / 3) * 3;
                Vector3 a = m.MultiplyPoint3x4(v[t[k]]), b = m.MultiplyPoint3x4(v[t[k + 1]]), c = m.MultiplyPoint3x4(v[t[k + 2]]);
                var normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude < 1e-6f) continue;
                normal.Normalize();
                var point = (a + b + c) / 3;
                if (!surface(point, normal)) continue;
                eye = point + normal * 3 + Vector3.up * .4f;
                if (Physics.CheckSphere(eye, .4f, CardFlight.CollisionMask, QueryTriggerInteraction.Ignore) || Inside(eye)) continue;
                var direction = (point - eye).normalized;
                if (!CardFlight.Sweep(eye, point + direction * .6f, world.settings.cardRadius, out hit)) continue;
                if (hit.collider == target && Vector3.Distance(hit.point, point) < .35f) return true;
            }
            return false;
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator TheRopeHoldsOnWhatYouSeeAllOverTheWorld()
        {
            Object.FindAnyObjectByType<CheatPowers>().SetInvulnerable(true);
            var rope = world.workshop.config.Find("rope");
            Assert.That(rope, Is.Not.Null, "the rope card");
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "world-plans");
            Directory.CreateDirectory(dir);
            var places = new List<ChunkPlan>
            {
                NearestPlan(p => p.Forest && !p.MazeHeart),
                NearestPlan(p => p.Shape.Kind == ShapeKind.Cliff),
                NearestPlan(p => (p.Shape.Kind == ShapeKind.Plain || p.Shape.Kind == ShapeKind.PlateauTop) && (p.Variant.Id == "camp" || p.Variant.Id == "farm" || p.Variant.Id == "ruins")),
                NearestPlan(p => (p.Shape.Kind == ShapeKind.Plain || p.Shape.Kind == ShapeKind.PlateauTop) && (p.Variant.Id == "boulders" || p.Variant.Id == "grove")),
                NearestPlan(p => p.Shape.Kind == ShapeKind.Maze && !p.Forest),
            };
            var held = new Dictionary<string, int>();
            var failures = new List<string>();
            int hung = 0, photos = 0;
            var rng = new System.Random(7);
            var hangOn = typeof(RopeLine).GetMethod("Grab", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var place in places.Where(p => p != null))
            {
                yield return Visit(place);
                var view = LoadedView(place.Coord);
                if (!view) { failures.Add($"{place}: did not load"); continue; }
                var ground = view.transform.Find("Ground");
                var solids = view.GetComponentsInChildren<MeshCollider>().Where(c => c.enabled && !c.isTrigger && !c.convex && c.gameObject.layer != 4 && (!ground || !c.transform.IsChildOf(ground))).ToList();
                foreach (var kind in RopeKinds)
                {
                    var candidates = solids.Where(kind.Value).OrderBy(_ => rng.Next()).Take(6).ToList();
                    int here = 0;
                    foreach (var target in candidates)
                    {
                        if (here >= 2) break;
                        float baseY = target.transform.parent ? target.transform.parent.position.y : target.bounds.min.y;
                        // Branches: high up and away from the trunk's axis; elsewhere any face that looks out or up.
                        System.Func<Vector3, Vector3, bool> surface = kind.Key == "branch"
                            ? (p, n) => p.y - baseY > 2.4f && Vector2.Distance(new Vector2(p.x, p.z), new Vector2(target.transform.position.x, target.transform.position.z)) > 1.2f * target.transform.lossyScale.x && n.y > -.3f
                            : (System.Func<Vector3, Vector3, bool>)((p, n) => n.y > -.2f && p.y - baseY > .4f);
                        if (!Throw(target, surface, rng, out var hit, out var eye)) continue;
                        world.ApplyImpact(null, rope, hit.collider, hit.point, hit.normal, (hit.point - eye).normalized * 18, eye);
                        var line = RopeLine.All.LastOrDefault();
                        string what = $"{kind.Key} {target.transform.parent?.name}/{target.name} in {place}";
                        if (!line || !line.Attached || line.Anchor != hit.collider) { failures.Add($"{what}: the rope did not hold where the card struck"); continue; }
                        yield return new WaitForSeconds(2f);
                        if (!line || !line.Attached) { failures.Add($"{what}: the rope let go"); continue; }
                        for (int i = 1; i < line.PointCount; i++)
                        {
                            var into = InsideOf(line.Point(i)) ?? Buried(line.Point(i), line.thickness);
                            if (!into) continue;
                            failures.Add($"{what}: piece {i} of {line.PointCount} of the cord went into {into.transform.parent?.name}/{into.name} at {line.Point(i)}");
                            break;
                        }
                        held.TryGetValue(kind.Key, out int count);
                        held[kind.Key] = count + 1;
                        if (photos < 4 && (kind.Key == "branch" || kind.Key == "rock" || kind.Key == "crown"))
                        {
                            photos++;
                            yield return Shot(dir, $"play-rope-{photos}-{kind.Key}", eye + Vector3.up * .5f, Vector3.Lerp(line.AnchorPoint, line.EndPoint, .4f), world.motor.transform.position + Vector3.up * 1.6f);
                        }
                        // Hang on it, where the hands reach the cord from the ground below its end.
                        if (Physics.Raycast(line.EndPoint + Vector3.up * .3f, Vector3.down, out var under, 6, CardFlight.CollisionMask & ~(1 << 4), QueryTriggerInteraction.Ignore))
                        {
                            var feet = under.point + Vector3.up * .05f;
                            var controller = world.motor.characterController;
                            bool room = !Physics.CheckCapsule(feet + Vector3.up * (controller.radius + .1f), feet + Vector3.up * (controller.height - controller.radius), controller.radius - .02f, CardFlight.CollisionMask & ~(1 << 4), QueryTriggerInteraction.Ignore);
                            if (room)
                            {
                                world.motor.Teleport(feet, Quaternion.identity);
                                yield return null;
                                hangOn.Invoke(line, new object[] { line.Length * .7f });
                                if (RopeLine.Holding(world.motor) == line)
                                {
                                    yield return new WaitForSeconds(2.5f);
                                    var body = world.motor.transform.position;
                                    bool stillOn = RopeLine.Holding(world.motor) == line;
                                    if (Inside(body + Vector3.up * .9f)) failures.Add($"{what}: hanging, the body went into something at {body}");
                                    else if (stillOn && Vector3.Distance(line.AnchorPoint, body) > line.Length + 2.5f) failures.Add($"{what}: hanging {Vector3.Distance(line.AnchorPoint, body):0.0} m off a {line.Length:0.0} m rope");
                                    else hung++;
                                    RopeLine.ReleasePlayer(world.motor);
                                    yield return new WaitForSeconds(1f);
                                    if (Inside(world.motor.transform.position + Vector3.up * .9f)) failures.Add($"{what}: let go, the body stuck in something");
                                }
                            }
                        }
                        line.Detach();
                        here++;
                    }
                }
            }
            Debug.Log($"[World] rope: held on {string.Join(", ", held.Select(h => $"{h.Key} ×{h.Value}"))}; hung {hung} times; {failures.Count} failures\n" + string.Join("\n", failures));
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
            foreach (var kind in new[] { "branch", "crown", "rock", "wood" })
                Assert.That(held.ContainsKey(kind), Is.True, $"a rope held on a {kind}");
            Assert.That(hung, Is.GreaterThanOrEqualTo(3), "the player hung on ropes");
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator FireTakesWhatBurnsAndOpensOnlyWhatItHits()
        {
            // Dry weather for this: rain puts fire out.
            var rain = streamer.GetComponent<WorldRain>();
            if (rain) rain.enabled = false;
            Object.FindAnyObjectByType<CheatPowers>().SetInvulnerable(true);
            var fire = world.workshop.config.Find("fire");
            Assert.That(fire, Is.Not.Null, "the fire card");
            var rng = new System.Random(3);

            // Everything that burns catches when a card of fire is thrown at it the way the player throws.
            var tried = new Dictionary<string, (int lit, int thrown)>();
            foreach (var place in new[]
                     {
                         NearestPlan(p => (p.Shape.Kind == ShapeKind.Plain || p.Shape.Kind == ShapeKind.PlateauTop) && (p.Variant.Id == "camp" || p.Variant.Id == "farm")),
                         NearestPlan(p => (p.Shape.Kind == ShapeKind.Plain || p.Shape.Kind == ShapeKind.PlateauTop) && p.Variant.Id == "grove"),
                         NearestPlan(p => p.Forest && !p.MazeHeart),
                     }.Where(p => p != null))
            {
                yield return Visit(place);
                var view = LoadedView(place.Coord);
                if (!view) continue;
                foreach (var group in view.burnables.Where(b => b && b.isActiveAndEnabled).GroupBy(b => b.name).ToList())
                {
                    var b = group.OrderBy(_ => rng.Next()).First();
                    tried.TryGetValue(group.Key, out var st);
                    if (st.thrown >= 2) continue;
                    var look = b.Bounds;
                    bool thrown = false;
                    for (int k = 0; k < 8 && !thrown; k++)
                    {
                        float a = k * Mathf.PI / 4;
                        var eye = look.center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (look.extents.magnitude + 3) + Vector3.up * 1.5f;
                        if (Physics.CheckSphere(eye, .3f, CardFlight.CollisionMask, QueryTriggerInteraction.Ignore) || Inside(eye)) continue;
                        var aim = look.center;
                        if (!CardFlight.Sweep(eye, aim + (aim - eye).normalized * 2, world.settings.cardRadius, out var hit)) continue;
                        // Struck it, or the ground or whatever it stands in right by it (grass has nothing to strike).
                        if (Vector3.Distance(hit.point, look.ClosestPoint(hit.point)) > .3f) continue;
                        world.ApplyImpact(null, fire, hit.collider, hit.point, hit.normal, (aim - eye).normalized * 18, eye);
                        thrown = true;
                    }
                    if (!thrown) continue;
                    yield return null;
                    bool lit = streamer.fire.Touched(b);
                    tried[group.Key] = (st.lit + (lit ? 1 : 0), st.thrown + 1);
                }
            }
            Debug.Log("[World] fire: " + string.Join(", ", tried.Select(x => $"{x.Key} {x.Value.lit}/{x.Value.thrown}")));
            Assert.That(tried.Count, Is.GreaterThanOrEqualTo(5), "fire thrown at grass, bushes, trees, wood");
            var cold = tried.Where(x => x.Value.lit < x.Value.thrown).Select(x => $"{x.Key} {x.Value.lit}/{x.Value.thrown}").ToList();
            Assert.That(cold, Is.Empty, "did not catch: " + string.Join(", ", cold));

            // Fire on the wall of a forest cell chars it and opens nothing; fire on the deadfall opens the alcove, with no word about it.
            var hoard = NearestPlan(p => p.Variant.Id == TileShapes.ForestDeadfall);
            Assert.That(hoard, Is.Not.Null, "a deadfall within 400 m");
            yield return Visit(hoard);
            var cell = LoadedView(hoard.Coord);
            var deadfall = cell.obstacles.First(o => o && o.name == "Deadfall");
            var wall = cell.GetComponentsInChildren<Collider>().Where(c => c.name.EndsWith("_Thicket") && !c.GetComponentInParent<WorldObstacle>())
                .OrderByDescending(c => Vector3.Distance(c.bounds.center, deadfall.transform.position)).FirstOrDefault();
            Assert.That(wall, Is.Not.Null, "thorns in the forest wall");
            var resolvedBefore = Object.FindObjectsByType<WorldObstacle>(FindObjectsSortMode.None).Where(o => o.Resolved).ToList();
            var wallEye = wall.bounds.center + Vector3.up * 3 + (deadfall.transform.position - wall.bounds.center).normalized * 2;
            world.ApplyImpact(null, fire, wall, wall.ClosestPointOnBounds(wallEye), Vector3.up, (wall.bounds.center - wallEye).normalized * 18, wallEye);
            yield return new WaitForSeconds(.5f);
            var opened = Object.FindObjectsByType<WorldObstacle>(FindObjectsSortMode.None).Where(o => o.Resolved && !resolvedBefore.Contains(o)).ToList();
            Assert.That(opened, Is.Empty, "fire on a wall thicket opened " + string.Join(", ", opened.Select(o => o.name)));
            Assert.That(deadfall.Resolved, Is.False, "the deadfall stands");
            var front = deadfall.transform.position - deadfall.transform.forward * 3 + Vector3.up * 1.4f;
            Assert.That(CardFlight.Sweep(front, deadfall.transform.position + Vector3.up * .8f, world.settings.cardRadius, out var onPile), Is.True, "the deadfall is in front");
            world.ApplyImpact(null, fire, onPile.collider, onPile.point, onPile.normal, (onPile.point - front).normalized * 18, front);
            Assert.That(deadfall.Resolved, Is.True, "fire takes the deadfall");
            Assert.That(world.Notice ?? "", Does.Not.Contain("выгорел"), "no word that the way is open");
            yield return new WaitForSeconds(2f);
            var controller = world.motor.characterController;
            foreach (float across in new[] { -.8f, 0, .8f })
            {
                var at = deadfall.transform.position + deadfall.transform.right * across;
                Assert.That(Physics.Raycast(at + Vector3.up * 3, Vector3.down, out var floor, 6, CardFlight.CollisionMask & ~(1 << 4), QueryTriggerInteraction.Ignore), Is.True);
                var feet = floor.point + Vector3.up * .3f;
                var blocking = Physics.OverlapCapsule(feet + Vector3.up * controller.radius, feet + Vector3.up * (controller.height - controller.radius), controller.radius - .02f, CardFlight.CollisionMask & ~(1 << 4), QueryTriggerInteraction.Ignore)
                    .Where(c => c.transform.IsChildOf(cell.transform) && c.name != "Ground").Select(c => c.name).ToList();
                Assert.That(blocking, Is.Empty, $"burnt, the alcove's mouth is open ({across:0.0} m across)");
            }
        }

        /// <summary>The nearest ground round <paramref name="near"/> with nothing standing within 4 m up to 10 m high.</summary>
        static Vector3 ClearSpot(Vector3 near)
        {
            int mask = ~((1 << 2) | (1 << 4));
            var best = near;
            float bestDistance = float.MaxValue;
            for (int x = -4; x <= 4; x++)
                for (int z = -4; z <= 4; z++)
                {
                    var over = near + new Vector3(x * 3, 15, z * 3);
                    if (!Physics.Raycast(over, Vector3.down, out var ground, 30, mask, QueryTriggerInteraction.Ignore)) continue;
                    var feet = ground.point;
                    var hits = Physics.OverlapCapsule(feet + Vector3.up * 5.5f, feet + Vector3.up * 10, 4, mask, QueryTriggerInteraction.Ignore);
                    if (hits.Any(h => !h.attachedRigidbody || h.attachedRigidbody.isKinematic)) continue;
                    float d = Vector3.Distance(feet, near);
                    if (d < bestDistance) { bestDistance = d; best = feet; }
                }
            return best;
        }

        /// <summary>
        /// Of the directions round <paramref name="toward"/> (looking a little up), the one that sees furthest before
        /// anything drawn is in the way (tree crowns have no colliders: their boxes count).
        /// </summary>
        static Vector3 Open(Vector3 eye, Vector3 toward)
        {
            var drawn = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .Where(r => r.enabled && r.gameObject.activeInHierarchy && !r.GetComponentInParent<WorldRain>() && !r.transform.IsChildOf(Camera.main ? Camera.main.transform : null))
                .Select(r => r.bounds).ToList();
            var best = toward;
            float far = -1;
            foreach (float angle in new[] { 0f, 25, -25, 50, -50, 80, -80, 120, -120 })
            {
                var dir = Quaternion.Euler(0, angle, 0) * toward;
                var ray = new Ray(eye, (dir + Vector3.up * .2f).normalized);
                float reach = 80;
                foreach (var box in drawn)
                    if (!box.Contains(eye) && box.IntersectRay(ray, out float d) && d > .5f) reach = Mathf.Min(reach, d);
                // Straight ahead counts a little more: the front comes from there.
                reach *= 1 - Mathf.Abs(angle) / 600;
                if (reach > far) { far = reach; best = dir; }
            }
            return best;
        }

        IEnumerator Shot(string dir, string name, Vector3 eye, Vector3 target, Vector3? stand = null)
        {
            // Stand where the camera will be (or under it), so streaming and far silhouettes follow it as in play.
            var feet = (stand ?? eye) - Vector3.up * 1.6f;
            world.motor.Teleport(feet, Quaternion.identity);
            // Let the chunks round the new spot stream in, as they would ahead of a walking player.
            for (int f = 0; f < 45; f++) yield return null;
            world.motor.Teleport(feet, Quaternion.identity);
            yield return null;
            var cam = world.viewCamera;
            // World Resolution hands sprites and other transparents to its presentation camera; off for the shot, so
            // this camera draws them itself.
            var resolution = cam.GetComponents<MonoBehaviour>().FirstOrDefault(m => m && m.enabled && m.GetType().Name == "WorldResolution");
            if (resolution) resolution.enabled = false;
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
            if (resolution) resolution.enabled = true;
        }

        /// <summary>
        /// Rotten swamp pieces in the real world (Kayospace «Биом: Гнилая топь»): water wets whoever wades in it, a gas
        /// vent set off by Fire throws the player and is spent, a bog swallows a creature pushed in and holds the player:
        /// slow sinking, a lunge lifts, sunk to the head is drowned.
        /// </summary>
        [UnityTest]
        public IEnumerator TheSwampWetsFlaresSwallowsAndDrownsSlowly()
        {
            // Water near the start with a shallow rim and a deep middle.
            WorldObstacle lake = null;
            Vector3 shallow = default, deep = default;
            float shallowLevel = 0, deepLevel = 0, deepDepth = 0;
            var start = world.motor.transform.position;
            foreach (var o in world.obstacles.Where(o => o && o.kind == ObstacleKind.Lake && o.HasSurface)
                         .OrderBy(o => Vector3.Distance(o.SurfaceBounds.center, start)))
            {
                var box = o.SurfaceBounds;
                bool gotShallow = false, gotDeep = false;
                for (float x = box.min.x; x <= box.max.x && !(gotShallow && gotDeep); x += .5f)
                    for (float z = box.min.z; z <= box.max.z && !(gotShallow && gotDeep); z += .5f)
                    {
                        var p = new Vector3(x, box.max.y + 1, z);
                        if (!o.SurfaceAt(p, out float level)) continue;
                        float depth = o.DepthAt(new Vector3(x, level, z), level);
                        if (!gotShallow && depth > .25f && depth < .6f) { shallow = new Vector3(x, level, z); shallowLevel = level; gotShallow = true; }
                        // Deep, and well away from the next chunk's water (its own drowning would not be a bog).
                        if (!gotDeep && depth > 1.45f && depth < 10 &&
                            !world.obstacles.Any(other => other && other != o && other.kind == ObstacleKind.Lake && other.DistanceTo(new Vector3(x, level, z)) < 3))
                        { deep = new Vector3(x, level, z); deepLevel = level; deepDepth = depth; gotDeep = true; }
                    }
                if (gotShallow && gotDeep) { lake = o; break; }
            }
            Assert.That(lake, Is.Not.Null, $"water with a shallow rim and a deep middle near the start ({world.obstacles.Count(o => o && o.kind == ObstacleKind.Lake)} lakes)");

            // Wading wets the player.
            Assert.That(WetStatus.IsWet(world.motor), Is.False, "dry at the start");
            world.motor.Teleport(shallow + Vector3.up * .3f, Quaternion.identity);
            yield return WaitFor(() => WetStatus.IsWet(world.motor), 3);
            Assert.That(WetStatus.IsWet(world.motor), Is.True, "standing in water makes the player wet");
            Assert.That(world.Running, Is.True, "shallow water does not drown");

            // A gas vent by the player flares at a spark: the player is thrown up, the vent is spent. (Thrown off the
            // start hill the player would fall hard: not what is tested here.)
            world.vitals.Immortal = true;
            world.motor.Teleport(start, Quaternion.identity);
            for (int f = 0; f < 10; f++) yield return null;
            var ventObject = new GameObject("Test gas vent");
            ventObject.transform.position = world.motor.transform.position + world.motor.transform.forward * 1.5f;
            var vent = ventObject.AddComponent<GasVent>();
            vent.radius = 1.2f;
            vent.height = 2.5f;
            vent.OnSpawned(world);
            Assert.That(vent.Full, Is.True, "a fresh vent is full of gas");
            Assert.That(world.Ignite(null, vent.transform.position + Vector3.up * .5f, .5f, IgnitionSource.Fire), Is.GreaterThan(0), "Fire sets the gas off");
            yield return null;
            yield return null;
            Assert.That(world.motor.Velocity.y, Is.GreaterThan(1), "the flash throws the player up");
            Assert.That(vent.Full, Is.False, "the vent is spent");
            Assert.That(world.Ignite(null, vent.transform.position + Vector3.up * .5f, .5f, IgnitionSource.Fire), Is.EqualTo(0), "a spent vent does not flare again");
            Object.Destroy(ventObject);
            yield return WaitFor(() => world.motor.IsGrounded, 4);
            world.vitals.Immortal = false;
            Assert.That(world.Running, Is.True, "still out after the flash");

            // The water becomes a bog: a creature pushed in goes under.
            var bog = lake.gameObject.AddComponent<BogMire>();
            // A river is shallower than a real bog: the head goes under before the feet find the bed.
            bog.drownDepth = Mathf.Min(bog.drownDepth, deepDepth - .4f);
            var creature = world.creatures.Where(c => c && c.Alive && c.gameObject.activeInHierarchy)
                .OrderBy(c => Vector3.Distance(c.Position, deep)).FirstOrDefault();
            Assert.That(creature, Is.Not.Null, "a creature to push in");
            creature.body.position = deep + Vector3.down * .5f;
            creature.transform.position = creature.body.position;
            yield return WaitFor(() => !creature.Alive, 2);
            Assert.That(creature.Alive, Is.False, "the bog takes a creature pushed in");
            yield return WaitFor(() => !creature.gameObject.activeSelf, bog.creatureSinkSeconds + 2);
            Assert.That(creature.gameObject.activeSelf, Is.False, "it goes under");
            Assert.That(world.Running, Is.True, "still out before stepping into the bog");

            // The player: held, slowed, sinking slowly; a lunge lifts; at the head, drowned.
            // Just under the surface, so nothing over the water (a bridge) catches the player first.
            world.motor.Teleport(deep + Vector3.down * .1f, Quaternion.identity);
            yield return WaitFor(() => bog.HoldingPlayer, 3);
            var feet = world.motor.transform.position;
            Assert.That(bog.HoldingPlayer, Is.True, $"the bog holds the player (running {world.Running}, feet {feet}, level {deepLevel}, in {lake.InWater(feet)}, external {world.motor.ExternallyControlled})");
            yield return new WaitForSeconds(1.5f);
            Assert.That(world.Running, Is.True, "the bog does not drown at once");
            Assert.That(world.motor.MoveSpeedScale, Is.LessThan(.5f), "walking in the bog is slow");
            float sunk = bog.SunkDepth(world.motor.transform.position);
            Assert.That(sunk, Is.InRange(.05f, 1f), "sinking slowly");
            Assert.That(bog.LungeNow(), Is.True, "a lunge");
            yield return new WaitForSeconds(.2f);
            Assert.That(bog.SunkDepth(world.motor.transform.position), Is.LessThan(sunk), "the lunge lifts the player");
            // The lunge also carries towards the edge: back into the middle, and this time no struggling.
            world.motor.Teleport(deep + Vector3.down * .1f, Quaternion.identity);
            float stepIn = Time.time;
            yield return WaitFor(() => !world.Running, 25);
            Assert.That(world.Running, Is.False, $"sunk to the head, drowned (sunk {bog.SunkDepth(world.motor.transform.position):0.00} of {bog.drownDepth:0.00} m, held {bog.HoldingPlayer})");
            Assert.That(Time.time - stepIn, Is.GreaterThan(3), "slowly, not at once");
        }

        [UnityTest]
        public IEnumerator MergedDressingComesApartWhenItBurnsOrWakes()
        {
            // The still dressing of a chunk draws as one mesh per material; its own renderers are kept from drawing.
            var views = Object.FindObjectsByType<ChunkView>(FindObjectsSortMode.None);
            var look = views.Select(v => v.GetComponentInChildren<MergedLook>()).FirstOrDefault(l => l && l.sources.Length > 0);
            Assert.That(look, Is.Not.Null, "a loaded chunk with merged dressing");
            Assert.That(look.sources.Where(r => r).All(r => r.forceRenderingOff), Is.True, "merged pieces do not draw themselves");

            // Fire: the burning piece draws by its own renderers and is cut out of the merged mesh.
            var piece = views.SelectMany(v => v.burnables).Where(b => b && b.GetComponent<MergedPiece>())
                .OrderBy(b => Vector3.Distance(b.transform.position, world.motor.transform.position)).FirstOrDefault();
            Assert.That(piece, Is.Not.Null, "a merged burnable near the start");
            var merged = piece.GetComponent<MergedPiece>();
            int source = merged.sources[0];
            int slice = System.Array.IndexOf(merged.look.sliceSource, source);
            var part = merged.look.parts[merged.look.slicePart[slice]];
            Assert.That(streamer.fire.Ignite(piece, piece.transform.position), Is.True, "it catches");
            yield return null;
            Assert.That(merged.look.sources[source].forceRenderingOff, Is.False, "the burning piece draws itself");
            var v0 = part.sharedMesh.vertices;
            int start = merged.look.sliceStart[slice], count = merged.look.sliceCount[slice];
            Assert.That(Enumerable.Range(start, count).All(i => v0[i] == v0[start]), Is.True, "and is gone from the merged mesh");

            // An awakened tree: hidden for the outing, gone from the merged mesh too.
            var tree = views.SelectMany(v => v.trees).FirstOrDefault(t => t && t.Standing && t.GetComponent<MergedPiece>());
            if (tree)
            {
                var tp = tree.GetComponent<MergedPiece>();
                world.HideForOuting(tree.gameObject);
                int ts = System.Array.IndexOf(tp.look.sliceSource, tp.sources[0]);
                var tv = tp.look.parts[tp.look.slicePart[ts]].sharedMesh.vertices;
                int tStart = tp.look.sliceStart[ts];
                Assert.That(Enumerable.Range(tStart, tp.look.sliceCount[ts]).All(i => tv[i] == tv[tStart]), Is.True, "an awakened tree is gone from the merged mesh");
            }
        }

        [UnityTest]
        public IEnumerator TheSwampHasFogBanksWispsAndItsOwnLook()
        {
            // Into the heart of the nearest swamp.
            var map = streamer.Map;
            ChunkCoord heart = default;
            int best = -1;
            for (int x = -14; x <= 14; x++)
                for (int y = -14; y <= 14; y++)
                {
                    if (!map.Biomes.IsSwamp(new ChunkCoord(x, y))) continue;
                    int around = 0;
                    for (int i = -2; i <= 2; i++) for (int j = -2; j <= 2; j++) if (map.Biomes.IsSwamp(new ChunkCoord(x + i, y + j))) around++;
                    int score = around * 100 - (x * x + y * y);
                    if (score > best) { best = score; heart = new ChunkCoord(x, y); }
                }
            var origin = streamer.root ? streamer.root.position : streamer.transform.position;
            float size = map.Rules.ChunkSize;
            world.vitals.Immortal = true;
            world.motor.Teleport(origin + new Vector3(heart.X * size, map.Chunk(heart).BaseLevel * map.Rules.PlateauHeight + 1.5f, heart.Y * size), Quaternion.identity);
            yield return WaitFor(() => streamer.IsLoaded(heart), 10);
            var air = streamer.GetComponent<WorldAtmosphere>();
            var life = streamer.GetComponent<SwampLife>();
            Assert.That(life, Is.Not.Null, "the swamp's life runs with the air");
            yield return WaitFor(() => air.Strength(WorldAtmosphere.Swamp) > .8f && life.Banks >= 3, 12);
            Assert.That(air.Strength(WorldAtmosphere.Swamp), Is.GreaterThan(.8f), "swamp air");
            Assert.That(life.Banks, Is.GreaterThanOrEqualTo(3), "fog banks wander round the player");
            var look = streamer.GetComponentsInChildren<UnityEngine.Rendering.Volume>().FirstOrDefault(v => v.name == "Swamp look");
            Assert.That(look, Is.Not.Null, "the swamp's colour grading");
            Assert.That(look.weight, Is.GreaterThan(.8f));

            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "world-plans");
            var bank = life.BankCentre(0);
            var away = new Vector3(1, 0, .4f).normalized;
            yield return Shot(dir, "play-swamp-look", world.motor.transform.position + Vector3.up * 1.6f, world.motor.transform.position + Vector3.up * 1.2f + away * 10);
            yield return Shot(dir, "play-swamp-fog-outside", bank + away * 17 + Vector3.up * 2.4f, bank + Vector3.up * .8f);
            // The eternal flame, if one burns near enough: seen from a way off.
            ChunkCoord flame = default;
            bool burning = false;
            for (int x = -20; x <= 20 && !burning; x++)
                for (int y = -20; y <= 20 && !burning; y++)
                    if (map.Chunk(x, y).Variant.Id == "eternal-flame") { flame = new ChunkCoord(x, y); burning = true; }
            if (burning)
            {
                var at = origin + new Vector3(flame.X * size, map.Chunk(flame).BaseLevel * map.Rules.PlateauHeight, flame.Y * size);
                yield return Shot(dir, "play-swamp-eternal-flame", at + new Vector3(-22, 4, -14), at + Vector3.up * 2);
                yield return Shot(dir, "play-swamp-eternal-flame-near", at + new Vector3(-5, 1.9f, -3.5f), at + Vector3.up * 1.5f);
                var beacon = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.name == "Beacon light");
                Assert.That(beacon, Is.Not.Null, "the eternal flame's light");
                Debug.Log($"[World] eternal flame at {flame}: light {beacon.transform.position}, chunk centre {at}, fire systems {beacon.transform.parent.GetComponentsInChildren<ParticleSystem>().Count(p => p.isPlaying)} playing");
                yield return Shot(dir, "play-swamp-eternal-flame-light", beacon.transform.position + new Vector3(-4, .5f, -3), beacon.transform.position + Vector3.down * .8f);
            }
            // Walk into a fog bank: the world goes white.
            bank = life.ThickestBank();
            world.motor.Teleport(bank + Vector3.up * 1.2f, Quaternion.identity);
            yield return WaitFor(() => air.Strength(WorldAtmosphere.Mist) > .5f, 6);
            Assert.That(air.Strength(WorldAtmosphere.Mist), Is.GreaterThan(.5f), "inside a fog bank the air is thick");
            yield return Shot(dir, "play-swamp-fog-inside", world.motor.transform.position + Vector3.up * 1.6f, world.motor.transform.position + Vector3.up * 1.4f + away * 10, world.motor.transform.position + Vector3.up * 1.6f);


            // A will-o'-the-wisp drifts off towards where it leads as the player comes near.
            WillOWisp wisp = null;
            yield return WaitFor(() => (wisp = Object.FindObjectsByType<WillOWisp>(FindObjectsSortMode.None).FirstOrDefault()) != null, 2);
            if (wisp)
            {
                var goal = wisp.Goal;
                float before = Vector2.Distance(new Vector2(wisp.transform.position.x, wisp.transform.position.z), new Vector2(goal.x, goal.z));
                world.motor.Teleport(wisp.transform.position + (wisp.transform.position - goal).normalized * 3 + Vector3.up * .5f, Quaternion.identity);
                yield return new WaitForSeconds(2.5f);
                if (wisp)
                {
                    float after = Vector2.Distance(new Vector2(wisp.transform.position.x, wisp.transform.position.z), new Vector2(goal.x, goal.z));
                    Assert.That(after, Is.LessThan(before - 1), "the wisp drifts towards its goal");
                }
            }
            else Debug.Log("[World] no wisp in the loaded swamp to follow");
        }

        [UnityTest]
        public IEnumerator ASunkenChestFloatsUpWhenPulledAndPaysRichly()
        {
            var content = streamer.content;
            float chance = content.sunkenChance;
            content.sunkenChance = 1;
            try
            {
                // The nearest chunk whose water hides a chest.
                var map = streamer.Map;
                ChunkCoord spot = default;
                bool found = false;
                for (int r = 1; r < 18 && !found; r++)
                    for (int x = -r; x <= r && !found; x++)
                        for (int y = -r; y <= r && !found; y++)
                        {
                            if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) != r) continue;
                            var plan = map.Chunk(x, y);
                            var prefab = streamer.catalog.Find(plan.Shape.Id, plan.Variant.Id, ChunkCatalog.Land(plan.Biome, plan.Zone));
                            if (prefab && prefab.hasSunkenSpot && content.ChestTable(plan.Ring)) { spot = new ChunkCoord(x, y); found = true; }
                        }
                Assert.That(found, Is.True, "deep water within 18 chunks");
                var origin = streamer.root ? streamer.root.position : streamer.transform.position;
                float size = map.Rules.ChunkSize;
                world.vitals.Immortal = true;
                world.motor.Teleport(origin + new Vector3(spot.X * size + 7, map.Chunk(spot).BaseLevel * map.Rules.PlateauHeight + 8, spot.Y * size + 7), Quaternion.identity);
                yield return WaitFor(() => streamer.IsLoaded(spot), 10);
                SunkenChest sunken = null;
                yield return WaitFor(() => (sunken = Object.FindObjectsByType<SunkenChest>(FindObjectsSortMode.None).FirstOrDefault(s => streamer.CoordAt(s.transform.position).Equals(spot))) != null, 3);
                Assert.That(sunken, Is.Not.Null, $"a chest on the bed at {spot} ({map.Chunk(spot)})");
                var chest = sunken.GetComponent<TreasureChest>();
                Assert.That(chest.transform.position.y, Is.LessThan(sunken.level - .5f), "under water");
                Assert.That(chest.CoinMultiplier, Is.GreaterThan(1.5f), "rich");
                for (int f = 0; f < 30; f++) yield return null;
                // «Притяжение» tears it out of the silt: it floats up.
                CardPhysics.Push(chest.GetComponent<Rigidbody>(), Vector3.up * 8);
                yield return new WaitForSeconds(2.5f);
                Assert.That(sunken.Afloat, Is.True, "loosened");
                Assert.That(chest.GetComponent<Rigidbody>().worldCenterOfMass.y, Is.GreaterThan(sunken.level - .8f), "floats up to the surface");
                Assert.That(chest.Open(), Is.True, "and opens");
            }
            finally { content.sunkenChance = chance; }
        }

        /// <summary>
        /// What the swamp costs to draw against the forest: batches, set-pass calls, triangles and shadow casters from
        /// the player's eyes, four ways round the heart of the nearest swamp and round a forest spot as far out
        /// (Logs/world-plans/render-cost.txt). A measurement, run by name.
        /// </summary>
        [UnityTest, Explicit("a measurement, run by name")]
        public IEnumerator RenderCostOfTheSwampAgainstTheForest()
        {
            var map = streamer.Map;
            var (heart, forest) = SwampHeartAndForestSpot(map);
            var origin = streamer.root ? streamer.root.position : streamer.transform.position;
            float size = map.Rules.ChunkSize;
            var report = new System.Text.StringBuilder();
            var batches = global::Unity.Profiling.ProfilerRecorder.StartNew(global::Unity.Profiling.ProfilerCategory.Render, "Batches Count");
            var setPass = global::Unity.Profiling.ProfilerRecorder.StartNew(global::Unity.Profiling.ProfilerCategory.Render, "SetPass Calls Count");
            var draws = global::Unity.Profiling.ProfilerRecorder.StartNew(global::Unity.Profiling.ProfilerCategory.Render, "Draw Calls Count");
            var tris = global::Unity.Profiling.ProfilerRecorder.StartNew(global::Unity.Profiling.ProfilerCategory.Render, "Triangles Count");
            var casters = global::Unity.Profiling.ProfilerRecorder.StartNew(global::Unity.Profiling.ProfilerCategory.Render, "Shadow Casters Count");
            world.vitals.Immortal = true;
            foreach (var (label, at) in new[] { ("swamp", heart), ("forest", forest) })
            {
                var plan = map.Chunk(at);
                var spot = origin + new Vector3(at.X * size, plan.BaseLevel * map.Rules.PlateauHeight + 1.5f, at.Y * size);
                long b = 0, sp = 0, d = 0, t = 0, sc = 0;
                float ms = 0;
                int frames = 0;
                for (int k = 0; k < 4; k++)
                {
                    world.motor.Teleport(spot, Quaternion.Euler(0, k * 90, 0));
                    yield return WaitFor(() => streamer.IsLoaded(at), 10);
                    for (int f = 0; f < 40; f++) yield return null;
                    for (int f = 0; f < 30; f++)
                    {
                        float before = Time.realtimeSinceStartup;
                        yield return null;
                        ms += (Time.realtimeSinceStartup - before) * 1000;
                        b += batches.LastValue; sp += setPass.LastValue; d += draws.LastValue; t += tris.LastValue; sc += casters.LastValue;
                        frames++;
                    }
                }
                report.AppendLine($"{label} {at} ({plan}): batches {b / frames}, set-pass {sp / frames}, draw calls {d / frames}, triangles {t / frames}, shadow casters {sc / frames}, frame {ms / frames:0.0} ms, chunks loaded {streamer.LoadedCount}");
            }
            batches.Dispose(); setPass.Dispose(); draws.Dispose(); tris.Dispose(); casters.Dispose();
            var file = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "world-plans", "render-cost.txt");
            File.WriteAllText(file, report.ToString());
            Debug.Log("[World] render cost\n" + report);
        }

        /// <summary>
        /// The heart of the nearest swamp (the most swamp round it within 14 chunks) and a plain forest chunk as far from
        /// the start with no swamp within three chunks: where the render measurements stand.
        /// </summary>
        static (ChunkCoord heart, ChunkCoord forest) SwampHeartAndForestSpot(WorldMap map)
        {
            ChunkCoord heart = default, forest = default;
            int best = -1;
            for (int x = -14; x <= 14; x++)
                for (int y = -14; y <= 14; y++)
                {
                    if (!map.Biomes.IsSwamp(new ChunkCoord(x, y))) continue;
                    int around = 0;
                    for (int i = -2; i <= 2; i++) for (int j = -2; j <= 2; j++) if (map.Biomes.IsSwamp(new ChunkCoord(x + i, y + j))) around++;
                    int score = around * 100 - (x * x + y * y);
                    if (score > best) { best = score; heart = new ChunkCoord(x, y); }
                }
            Assert.That(best, Is.GreaterThan(0), "a swamp within 14 chunks");
            float ring = Mathf.Sqrt(heart.X * heart.X + heart.Y * heart.Y);
            best = int.MaxValue;
            for (int x = -14; x <= 14; x++)
                for (int y = -14; y <= 14; y++)
                {
                    var c = new ChunkCoord(x, y);
                    bool clear = true;
                    for (int i = -3; i <= 3 && clear; i++) for (int j = -3; j <= 3 && clear; j++) clear = !map.Biomes.IsSwamp(new ChunkCoord(x + i, y + j));
                    if (!clear || map.Chunk(c).Shape.Kind != ShapeKind.Plain) continue;
                    int score = (int)(Mathf.Abs(Mathf.Sqrt(x * x + y * y) - ring) * 100);
                    if (score < best) { best = score; forest = c; }
                }
            return (heart, forest);
        }

        /// <summary>
        /// Where the swamp's and the forest's draws and frame time go: one spot, four ways round, measured with everything
        /// on and with one thing changed at a time (the sun's shadows off, screen-space AO and its depth-normals prepass
        /// off, fewer shadow cascades, no shadows from the far merged dressing, particles paused, the presentation camera
        /// drawing nothing but the upscaled image); plus what the
        /// visible renderers are (shader, keywords, queue, property block, shadow casting, SRP Batcher compatibility) and
        /// which particle systems are alive. Logs/world-plans/render-passes.txt. A measurement, run by name.
        /// </summary>
        [UnityTest, Explicit("a measurement, run by name")]
        public IEnumerator RenderPassesOfTheSwampAndTheForest()
        {
            var map = streamer.Map;
            var (heart, forest) = SwampHeartAndForestSpot(map);
            var origin = streamer.root ? streamer.root.position : streamer.transform.position;
            float size = map.Rules.ChunkSize;
            var report = new System.Text.StringBuilder();
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            var suns = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.type == LightType.Directional && l.shadows != LightShadows.None).ToList();
            var sunShadows = suns.Select(l => l.shadows).ToList();
            var ao = RendererFeatures().Where(f => f.GetType().Name.Contains("AmbientOcclusion")).ToList();
            var aoWas = ao.Select(FeatureActive).ToList();
            var cascadesProperty = pipeline.GetType().GetProperty("shadowCascadeCount");
            var distanceProperty = pipeline.GetType().GetProperty("shadowDistance");
            int cascadesWere = (int)cascadesProperty.GetValue(pipeline);
            float distanceWas = (float)distanceProperty.GetValue(pipeline);
            report.AppendLine($"pipeline {pipeline.name}, quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}, shadow cascades {cascadesWere}, shadow distance {distanceWas} m, " +
                              $"shadow-casting directional lights {suns.Count}, AO features {ao.Count}");
            var farCasters = new List<(Renderer r, UnityEngine.Rendering.ShadowCastingMode mode)>();
            System.Action<bool> farShadows = on =>
            {
                if (!on && farCasters.Count == 0)
                    foreach (var look in Object.FindObjectsByType<MergedLook>(FindObjectsSortMode.None))
                    {
                        var far = look.transform.Find("Far");
                        if (far) foreach (var r in far.GetComponentsInChildren<Renderer>(true)) if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) farCasters.Add((r, r.shadowCastingMode));
                    }
                foreach (var (r, mode) in farCasters) if (r) r.shadowCastingMode = on ? mode : UnityEngine.Rendering.ShadowCastingMode.Off;
                if (on) farCasters.Clear();
            };
            var particleRenderers = new List<ParticleSystemRenderer>();
            System.Action<bool> particles = on =>
            {
                if (!on && particleRenderers.Count == 0)
                    foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                    {
                        var pr = ps.GetComponent<ParticleSystemRenderer>();
                        if (!ps.isPlaying || !pr || !pr.enabled) continue;
                        ps.Pause(false);
                        pr.enabled = false;
                        particleRenderers.Add(pr);
                    }
                if (on)
                {
                    foreach (var pr in particleRenderers) if (pr) { pr.enabled = true; pr.GetComponent<ParticleSystem>().Play(false); }
                    particleRenderers.Clear();
                }
            };
            // The presentation camera (fixed-resolution world): with nothing to cull it draws only the upscaled image.
            var outputMasks = new List<(Camera camera, int mask)>();
            System.Action<bool> output = on =>
            {
                foreach (var (camera, mask) in outputMasks) if (camera) camera.cullingMask = mask;
                outputMasks.Clear();
                if (!on)
                    foreach (var camera in Camera.allCameras)
                        if (camera.name.StartsWith("World presentation")) { outputMasks.Add((camera, camera.cullingMask)); camera.cullingMask = 0; }
            };
            System.Action<bool, bool, int, float, bool, bool> apply = (shadows, ssao, cascades, distance, far, effects) =>
            {
                for (int i = 0; i < suns.Count; i++) suns[i].shadows = shadows ? sunShadows[i] : LightShadows.None;
                foreach (var feature in ao) SetFeatureActive(feature, ssao);
                cascadesProperty.SetValue(pipeline, cascades);
                distanceProperty.SetValue(pipeline, distance);
                farShadows(far);
                particles(effects);
            };
            var configs = new (string name, bool shadows, bool ssao, int cascades, float distance, bool far, bool effects)[]
            {
                ("all", true, true, cascadesWere, distanceWas, true, true),
                ("no sun shadows", false, true, cascadesWere, distanceWas, true, true),
                ("no SSAO", true, false, cascadesWere, distanceWas, true, true),
                ("2 cascades", true, true, 2, distanceWas, true, true),
                ("far no shadows", true, true, cascadesWere, distanceWas, false, true),
                ("no particles", true, true, cascadesWere, distanceWas, true, false),
                ("no output camera", true, true, cascadesWere, distanceWas, true, true),
            };
            var batches = global::Unity.Profiling.ProfilerRecorder.StartNew(global::Unity.Profiling.ProfilerCategory.Render, "Batches Count");
            var setPass = global::Unity.Profiling.ProfilerRecorder.StartNew(global::Unity.Profiling.ProfilerCategory.Render, "SetPass Calls Count");
            var draws = global::Unity.Profiling.ProfilerRecorder.StartNew(global::Unity.Profiling.ProfilerCategory.Render, "Draw Calls Count");
            var casters = global::Unity.Profiling.ProfilerRecorder.StartNew(global::Unity.Profiling.ProfilerCategory.Render, "Shadow Casters Count");
            var timings = new FrameTiming[1];
            // Which cameras draw each frame: a visible Scene view (or gizmos) adds its own draws to every counter.
            var cameraFrames = new Dictionary<string, int>();
            System.Action<UnityEngine.Rendering.ScriptableRenderContext, Camera> countCamera = (_, cam) =>
            {
                string key = cam.cameraType + " " + cam.name;
                cameraFrames[key] = cameraFrames.TryGetValue(key, out var n) ? n + 1 : 1;
            };
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += countCamera;
            int countedFrames = Time.frameCount;
            // Gizmos in the Game view draw an icon per light, audio source and particle system, each its own set-pass
            // call: measured with them off so the numbers are the game's own.
            var gizmoFields = new List<(Object view, FieldInfo field, bool was)>();
            foreach (var view in Resources.FindObjectsOfTypeAll(typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView")))
                for (var t = view.GetType(); t != null; t = t.BaseType)
                {
                    var field = t.GetField("m_Gizmos", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (field == null || field.FieldType != typeof(bool)) continue;
                    gizmoFields.Add((view, field, (bool)field.GetValue(view)));
                    field.SetValue(view, false);
                    break;
                }
            report.AppendLine($"game views {gizmoFields.Count}, gizmos were on in {gizmoFields.Count(g => g.was)}: measured with gizmos off");
            world.vitals.Immortal = true;
            try
            {
                foreach (var (label, at) in new[] { ("swamp", heart), ("forest", forest) })
                {
                    var plan = map.Chunk(at);
                    var spot = origin + new Vector3(at.X * size, plan.BaseLevel * map.Rules.PlateauHeight + 1.5f, at.Y * size);
                    var sums = new double[configs.Length, 8];
                    int frames = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        world.motor.Teleport(spot, Quaternion.Euler(0, k * 90, 0));
                        yield return WaitFor(() => streamer.IsLoaded(at), 10);
                        for (int f = 0; f < 40; f++) yield return null;
                        if (k == 0) report.Append(Census($"{label} {at} ({plan}) looking north"));
                        for (int c = 0; c < configs.Length; c++)
                        {
                            var o = configs[c];
                            apply(o.shadows, o.ssao, o.cascades, o.distance, o.far, o.effects);
                            output(o.name != "no output camera");
                            for (int f = 0; f < 5; f++) yield return null;
                            for (int f = 0; f < 20; f++)
                            {
                                float before = Time.realtimeSinceStartup;
                                yield return null;
                                FrameTimingManager.CaptureFrameTimings();
                                FrameTimingManager.GetLatestTimings(1, timings);
                                sums[c, 0] += batches.LastValue; sums[c, 1] += setPass.LastValue; sums[c, 2] += draws.LastValue; sums[c, 3] += casters.LastValue;
                                sums[c, 4] += (Time.realtimeSinceStartup - before) * 1000;
                                sums[c, 5] += timings[0].cpuRenderThreadFrameTime;
                                sums[c, 6] += timings[0].gpuFrameTime;
                                sums[c, 7] += timings[0].cpuMainThreadFrameTime - timings[0].cpuMainThreadPresentWaitTime;
                            }
                        }
                        apply(true, true, cascadesWere, distanceWas, true, true);
                        output(true);
                        frames += 20;
                    }
                    report.AppendLine($"== {label} {at}: averages over 4 directions (frame = editor wall clock; render thread and GPU from FrameTimingManager, 0 when it gives nothing)");
                    for (int c = 0; c < configs.Length; c++)
                        report.AppendLine($"  {configs[c].name,-17} batches {sums[c, 0] / frames,5:0}, set-pass {sums[c, 1] / frames,4:0}, draw calls {sums[c, 2] / frames,5:0}, " +
                                          $"shadow casters {sums[c, 3] / frames,5:0}, frame {sums[c, 4] / frames,5:0.0} ms, main thread {sums[c, 7] / frames,5:0.0} ms, render thread {sums[c, 5] / frames,5:0.0} ms, GPU {sums[c, 6] / frames,5:0.0} ms");
                }
            }
            finally
            {
                for (int i = 0; i < suns.Count; i++) if (suns[i]) suns[i].shadows = sunShadows[i];
                for (int i = 0; i < ao.Count; i++) SetFeatureActive(ao[i], aoWas[i]);
                cascadesProperty.SetValue(pipeline, cascadesWere);
                distanceProperty.SetValue(pipeline, distanceWas);
                farShadows(true);
                particles(true);
                output(true);
                UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= countCamera;
                foreach (var (view, field, was) in gizmoFields) if (view) field.SetValue(view, was);
                batches.Dispose(); setPass.Dispose(); draws.Dispose(); casters.Dispose();
            }
            countedFrames = Mathf.Max(1, Time.frameCount - countedFrames);
            report.AppendLine($"cameras per frame: {string.Join(", ", cameraFrames.OrderByDescending(c => c.Value).Select(c => $"{c.Key} {c.Value / (float)countedFrames:0.00}"))}");
            report.AppendLine($"scene views open {UnityEditor.SceneView.sceneViews.Count}");
            var file = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "world-plans", "render-passes.txt");
            File.WriteAllText(file, report.ToString());
            Debug.Log("[World] render passes\n" + report);
        }

        /// <summary>What the renderers visible right now are drawn with, grouped (one line per shader variant).</summary>
        static string Census(string title)
        {
            var groups = new Dictionary<string, (int draws, int renderers, int casters, int blocks)>();
            var types = new Dictionary<string, int>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || !r.isVisible || r.forceRenderingOff) continue;
                types[r.GetType().Name] = types.TryGetValue(r.GetType().Name, out var n) ? n + 1 : 1;
                bool block = r.HasPropertyBlock();
                bool casts = r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off;
                foreach (var m in r.sharedMaterials.Distinct())
                {
                    if (!m || !m.shader) continue;
                    var key = $"{m.shader.name} q{m.renderQueue} [{string.Join(" ", m.shaderKeywords.OrderBy(w => w))}] srp:{SrpBatcherCode(m.shader)}";
                    groups.TryGetValue(key, out var g);
                    groups[key] = (g.draws + 1, g.renderers + 1, g.casters + (casts ? 1 : 0), g.blocks + (block ? 1 : 0));
                }
            }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"== census, {title}: renderers {string.Join(", ", types.OrderByDescending(t => t.Value).Select(t => $"{t.Key} {t.Value}"))}");
            sb.AppendLine("  (material slots, of them casting shadows, of them with a property block; srp:0 = SRP Batcher compatible)");
            foreach (var g in groups.OrderByDescending(g => g.Value.draws))
                sb.AppendLine($"  {g.Value.draws,5} {g.Value.casters,5} {g.Value.blocks,5}  {g.Key}");
            var systems = Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Where(p => p.gameObject.activeInHierarchy).ToList();
            sb.AppendLine($"  particle systems active {systems.Count}, playing {systems.Count(p => p.isPlaying)}, alive particles {systems.Sum(p => p.particleCount)}; by top object (systems, visible, casting):");
            foreach (var g in systems.GroupBy(p => TopBelowChunk(p.transform)).OrderByDescending(g => g.Count()).Take(12))
            {
                var rs = g.Select(p => p.GetComponent<ParticleSystemRenderer>()).Where(r => r).ToList();
                sb.AppendLine($"    {g.Count(),4} {rs.Count(r => r.isVisible),4} {rs.Count(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off),4}  {g.Key}");
            }
            return sb.ToString();
        }

        /// <summary>Where a part sits: up to three names from just under its chunk (or the scene root) down towards it.</summary>
        static string TopBelowChunk(Transform t)
        {
            var names = new List<string>();
            for (var x = t; x && !x.GetComponent<ChunkView>(); x = x.parent) names.Insert(0, Regex.Replace(x.name, @"\s*\(\d+\)$|\(Clone\)", ""));
            return (t.GetComponentInParent<ChunkView>() ? "chunk/" : "") + string.Join("/", names.Take(3));
        }

        static readonly Dictionary<Shader, int> SrpCodes = new Dictionary<Shader, int>();

        /// <summary>The editor's own SRP Batcher verdict on a shader (0 = compatible), as its inspector shows it.</summary>
        static int SrpBatcherCode(Shader shader)
        {
            if (SrpCodes.TryGetValue(shader, out var code)) return code;
            const BindingFlags any = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var util = typeof(UnityEditor.ShaderUtil);
            var active = util.GetMethod("GetShaderActiveSubshaderIndex", any);
            var check = util.GetMethod("GetSRPBatcherCompatibilityCode", any);
            int sub = active != null ? (int)active.Invoke(null, new object[] { shader }) : 0;
            code = check != null ? (int)check.Invoke(null, new object[] { shader, sub }) : -1;
            return SrpCodes[shader] = code;
        }

        /// <summary>The renderer features of the current pipeline's renderers (URP keeps them behind its asset).</summary>
        static List<Object> RendererFeatures()
        {
            var list = new List<Object>();
            var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (!asset) return list;
            var field = asset.GetType().GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field?.GetValue(asset) is System.Array datas)
                foreach (var data in datas)
                    if (data != null && data.GetType().GetProperty("rendererFeatures")?.GetValue(data) is System.Collections.IList features)
                        foreach (var feature in features)
                            if (feature is Object o && o && !list.Contains(o)) list.Add(o);
            return list;
        }

        static bool FeatureActive(Object feature) => (bool)feature.GetType().GetProperty("isActive").GetValue(feature);

        static void SetFeatureActive(Object feature, bool on) => feature.GetType().GetMethod("SetActive").Invoke(feature, new object[] { on });

        static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
#endif
