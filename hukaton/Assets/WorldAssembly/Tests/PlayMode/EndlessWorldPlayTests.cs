#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                Debug.Log($"[World] {c.name} at {c.transform.position} (chunk local {c.transform.position - centre}), navigation {(found ? (nearest.position - c.transform.position).ToString() : "none")}, state {c.State}");
            }
            foreach (var c in group)
                // Bodies push each other a little as a group lands: within reach of the navigation, not off in a wall.
                Assert.That(NavMesh.SamplePosition(c.transform.position, out _, 1.5f, NavMesh.AllAreas), Is.True, $"{c.name} at {c.transform.position - centre} stands on the navigation");
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
            Assert.That(Vector3.Distance(prop.body.position, before), Is.GreaterThan(.3f), "a loose prop flies when pushed");

            // Far away and back: the chunks come back burnt and with the empty place of the tree.
            for (int f = 0; f < 240 && streamer.fire.Burning > 0; f++) yield return null;
            var away = start + new Vector3(260, 0, 260);
            world.motor.Teleport(away + Vector3.up * 30, Quaternion.identity);
            yield return WaitFor(() => !streamer.IsLoaded(coord) && !streamer.IsLoaded(treeCoord), 10);
            Assert.That(streamer.IsLoaded(coord), Is.False, "the burnt chunk unloads");
            world.motor.Teleport(start, Quaternion.identity);
            yield return WaitFor(() => streamer.IsLoaded(coord) && streamer.IsLoaded(treeCoord), 10);
            for (int f = 0; f < 5; f++) yield return null;
            ChunkView At(ChunkCoord c) => Object.FindObjectsByType<ChunkView>(FindObjectsSortMode.None).First(v => streamer.CoordAt(v.transform.position).Equals(c));
            var again = At(coord).burnables[grassIndex];
            Assert.That(streamer.fire.Touched(again), Is.True, "the grass is still burnt");
            Assert.That(again.GetComponentsInChildren<Renderer>().All(r => !r.enabled), Is.True, "burnt to ash");
            Assert.That(At(treeCoord).trees[treeIndex].gameObject.activeSelf, Is.False, "the awakened tree's place stays empty");
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
