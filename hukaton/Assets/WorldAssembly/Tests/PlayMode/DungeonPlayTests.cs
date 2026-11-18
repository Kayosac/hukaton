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
    /// The anthill in the real game: the cheat finds a hill, walking into its passage darkens the screen and at the dark
    /// end the player is in the dungeon's own passage, walking out of it into the light; the same way back up; going in
    /// again finds the same dungeon. Photographs the way and the chambers into Logs/dungeon-play.
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
        public IEnumerator TheAnthillIsWalkedIntoLeftAndFoundAsLeft()
        {
            var site = streamer.Dungeons;
            // The anthill's garrison fights back; the photographer must live to the end.
            world.vitals.Immortal = true;
            string note = streamer.GoToNearestDungeon();
            Debug.Log("[Dungeon] " + note);
            Assert.That(note, Does.StartWith("Иду"), note);
            DungeonEntrance entrance = null;
            yield return WaitFor(() => (entrance = DungeonEntrance.All.FirstOrDefault(e => e && (e.outside.position - world.motor.transform.position).sqrMagnitude < 4)) != null, 8);
            Assert.That(entrance, Is.Not.Null, "the player stands before a hill's mouth");
            yield return new WaitForSeconds(1.5f);
            yield return Shot("00-hill");
            var away = entrance.passage.TransformPoint(new Vector3(5, 0, -13));
            world.motor.Teleport(away + Vector3.up * .3f, Quaternion.LookRotation(Flat(entrance.passage.TransformPoint(new Vector3(0, 0, 4)) - away)));
            yield return new WaitForSeconds(.8f);
            yield return Shot("00b-hill-afar");
            Assert.That(Blocked(entrance.passage), Is.Null, "nothing of the world stands in the passage");

            // Walk in: the screen darkens step by step, at the dark end the player is in the dungeon's passage.
            float last = -1;
            for (float s = 0; s <= 6.6f && !site.Underground; s += .5f)
            {
                Stand(entrance.passage, s, 0);
                yield return null; yield return null;
                if (s > 1.5f && s < 6) Assert.That(site.Darkness, Is.GreaterThan(last), $"darker at {s} m in");
                last = site.Darkness;
                if (Mathf.Approximately(s, 3.5f)) yield return Shot("01-walking-in");
            }
            Assert.That(site.Underground, Is.True, "the dark end of the hill's passage leads down");
            var dungeon = site.Current;
            var plan = dungeon.Plan;
            Debug.Log($"[Dungeon] seed {plan.Seed} large={plan.Large} levels={plan.Levels} rooms={plan.Rooms.Count} cells={plan.Cells.Count}");
            var local = dungeon.Passage.InverseTransformPoint(world.motor.transform.position);
            Assert.That(local.z, Is.EqualTo(site.darkAt - .25f).Within(.3f), "as deep in the dungeon's passage");
            Assert.That(Vector3.Dot(world.motor.transform.forward, dungeon.Passage.forward), Is.LessThan(-.9f), "facing its mouth");
            Assert.That(site.Darkness, Is.GreaterThan(.9f), "still dark");
            // Walk out of it into the entrance chamber: lighter with every step.
            last = 2;
            for (float s = 6; s >= -1; s -= .5f)
            {
                Stand(dungeon.Passage, s, 180);
                yield return null; yield return null;
                if (s > 1.5f && s < 6) Assert.That(site.Darkness, Is.LessThan(last), $"lighter at {s} m");
                last = site.Darkness;
                if (Mathf.Approximately(s, 3.5f)) yield return Shot("02-walking-out-below");
            }
            Assert.That(site.Darkness, Is.EqualTo(0).Within(.01f), "light in the entrance chamber");
            var garrison = site.Creatures;
            Debug.Log("[Dungeon] garrison: " + string.Join(", ", garrison.GroupBy(c => c.name).Select(g => g.Key + " x" + g.Count())));
            Assert.That(garrison.Length, Is.GreaterThan(3), "the anthill is inhabited");
            Assert.That(garrison.All(c => world.creatures.Contains(c)), Is.True, "its creatures take part in the outing");
            Assert.That(garrison.All(c => c.navigator && c.navigator.isOnNavMesh), Is.True, "every one stands on the dungeon's navigation");
            if (plan.Large) Assert.That(garrison.Count(c => c.name == "AntQueen"), Is.EqualTo(1), "the queen waits at the bottom");
            Assert.That(garrison.Any(c => c.name == "AntQueen" || c.name == "AntWorker"), Is.True);
            var chests = site.Chests;
            Debug.Log("[Dungeon] chests: " + string.Join(", ", chests.Select(c => c.name)));
            Assert.That(chests.Count(c => c.tier && c.tier.displayName == "Сундук муравейника"), Is.EqualTo(1), "one prize chest");
            yield return Hazards(site, dungeon);
            Assert.That(site.Underground, Is.True);
            yield return new WaitForSeconds(2f);
            yield return Shot("03-entrance-chamber");
            Stand(dungeon.Passage, -2.5f, 0);
            yield return new WaitForSeconds(.5f);
            Assert.That(site.Darkness, Is.EqualTo(0).Within(.01f));
            yield return Shot("03b-way-out-from-the-chamber");
            Assert.That(NavMesh.SamplePosition(dungeon.Passage.position, out _, 2f, NavMesh.AllAreas), Is.True, "navigation at the passage's mouth");
            var culling = dungeon.GetComponent<DungeonCulling>();
            Assert.That(culling.Shown, Is.LessThan(culling.Total), "far cells are not drawn");

            // A look into every chamber from its doorway.
            int i = 4;
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

            if (plan.Large)
            {
                // The way up after the queen: dark while she lives, daylight down the shaft once she is dead.
                Assert.That(dungeon.Exit, Is.Not.Null, "the queen's chamber has a way up");
                var queen = site.Creatures.First(c => c.name == "AntQueen");
                var under = dungeon.Exit.position;
                // Into the chamber from the shaft: towards where the queen sat.
                var boss = dungeon.Markers.First(m => m.name == "Spawn Boss").at.position - under; boss.y = 0;
                var back = -boss.normalized;
                world.motor.Teleport(under - back * 1.5f + Vector3.up * .1f, Quaternion.LookRotation(back));
                yield return new WaitForSeconds(.4f);
                Assert.That(site.Underground, Is.True, "no way up while the queen lives");
                Assert.That(dungeon.ExitOpen, Is.False);
                queen.Damage(queen.maximumHealth * 3, Vector3.zero, queen.Centre, 0);
                yield return new WaitForSeconds(queen.corpseSeconds + 1f);
                world.motor.Teleport(under - back * 3.5f + Vector3.up * .1f, Quaternion.LookRotation(back + Vector3.up * .9f));
                yield return new WaitForSeconds(.5f);
                Assert.That(dungeon.ExitOpen, Is.True, "the queen is dead: daylight comes down the shaft");
                // The prize under the way up: a rare curio is always in it.
                var prize = site.Chests.First(c => c.tier.displayName == "Сундук муравейника");
                var lootBefore = Object.FindObjectsByType<WorldLoot>(FindObjectsSortMode.None).ToList();
                // Opened from afar, so the player does not pick the loot up before it is counted.
                Stand(dungeon.Passage, -1, 180);
                Assert.That(prize.Open(), Is.True);
                yield return new WaitForSeconds(3f);
                var poured = Object.FindObjectsByType<WorldLoot>(FindObjectsSortMode.None).Where(l => !lootBefore.Contains(l) && l.item).ToList();
                Debug.Log("[Dungeon] prize: " + string.Join(", ", poured.Select(l => l.item.id + "/" + l.item.rarity)));
                Assert.That(poured.Any(l => l.item.category == "Диковины" && l.item.rarity >= Hakaton.Loot.LootRarity.Rare), Is.True, "a rare curio in the prize chest");
                yield return Shot("10a-prize");
                yield return Shot("10-way-up-after-the-queen");
                for (float r = 3.4f; r >= 0 && site.Underground; r -= .4f)
                {
                    world.motor.Teleport(under - back * r + Vector3.up * .1f, Quaternion.LookRotation(back));
                    yield return null; yield return null;
                    if (Mathf.Abs(r - 1.8f) < .21f) yield return Shot("11-into-the-light");
                }
                Assert.That(site.Underground, Is.False, "the shaft leads up");
            }
            else
            {
                // Back up: into the passage again, out of the hill's mouth.
                for (float s = 0; s <= 6.6f && site.Underground; s += .5f)
                {
                    Stand(dungeon.Passage, s, 0);
                    yield return null; yield return null;
                }
                Assert.That(site.Underground, Is.False, "the passage's dark end leads back up");
            }
            Assert.That(world.creatures.Any(c => c && c.transform.IsChildOf(dungeon.transform)), Is.False, "the dungeon's creatures sleep while the player is up");
            yield return WaitFor(() => (entrance = DungeonEntrance.All.FirstOrDefault(e => e && e.passage && Mathf.Abs(e.passage.InverseTransformPoint(world.motor.transform.position).x) < 2.5f && (e.passage.position - world.motor.transform.position).magnitude < 9)) != null, 6);
            Assert.That(entrance, Is.Not.Null, "in the hill's passage (its chunk loaded again)");
            for (float s = 6; s >= -2; s -= .5f)
            {
                Stand(entrance.passage, s, 180);
                yield return null; yield return null;
                if (Mathf.Approximately(s, 3.5f)) yield return Shot("98-walking-out-above");
            }
            Assert.That(site.Darkness, Is.EqualTo(0).Within(.01f), "daylight again before the hill");
            yield return new WaitForSeconds(1f);
            yield return Shot("99-back-out");

            // In again: the same dungeon.
            for (float s = 0; s <= 6.6f && !site.Underground; s += .5f)
            {
                Stand(entrance.passage, s, 0);
                yield return null; yield return null;
            }
            Assert.That(site.Current, Is.SameAs(dungeon), "the dungeon is kept as it was left");
            Assert.That(site.Creatures.Length, Is.GreaterThan(0));
            Assert.That(site.Creatures.All(c => world.creatures.Contains(c)), Is.True, "and its creatures are awake again");
            world.vitals.Immortal = false;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        /// <summary>Floor modifiers of this dungeon: acid poisons whoever stands in it, collapses bring the ceiling down near the player.</summary>
        IEnumerator Hazards(DungeonSite site, DungeonBuilder dungeon)
        {
            var hazards = dungeon.GetComponent<DungeonHazards>();
            Assert.That(hazards, Is.Not.Null);
            Debug.Log("[Dungeon] floor modifiers: " + string.Join(", ", hazards.Levels) + $"; acid puddles {hazards.AcidPuddles}");
            // Both kinds get looked at: a level without one is given collapses for the test.
            if (!hazards.Levels.Contains(DungeonHazards.Modifier.Collapse) && hazards.Levels.Contains(DungeonHazards.Modifier.None))
                hazards.Force(hazards.Levels.ToList().IndexOf(DungeonHazards.Modifier.None), DungeonHazards.Modifier.Collapse);
            var root = dungeon.transform.Find("Hazards");
            var plan = dungeon.Plan;
            for (int level = 0; level < hazards.Levels.Count; level++)
            {
                var modifier = hazards.Levels[level];
                if (modifier == DungeonHazards.Modifier.Acid)
                {
                    var puddle = root.Cast<Transform>().FirstOrDefault(t => t.name.StartsWith("AN44") && hazards.LevelOf(dungeon.Built.InverseTransformPoint(t.position).y) == level);
                    Assert.That(puddle, Is.Not.Null, "an acid level has puddles");
                    var view = Clear(puddle.position, 3f);
                    world.motor.Teleport(view + Vector3.up * .1f, Quaternion.LookRotation((puddle.position - view).normalized + Vector3.down * .5f));
                    yield return new WaitForSeconds(.6f);
                    yield return Shot($"20-acid-level{level}");
                    world.vitals.Immortal = false;
                    world.motor.Teleport(puddle.position + Vector3.up * .1f, Quaternion.identity);
                    yield return new WaitForSeconds(.4f);
                    Assert.That(world.statuses.Has(PlayerStatusKind.Poison), Is.True, "standing in acid poisons");
                    world.vitals.Immortal = true;
                }
                else if (modifier == DungeonHazards.Modifier.Collapse)
                {
                    var heap = root.Cast<Transform>().FirstOrDefault(t => t.name.StartsWith("AN42") && hazards.LevelOf(dungeon.Built.InverseTransformPoint(t.position).y) == level);
                    if (heap)
                    {
                        var view = Clear(heap.position + heap.forward * .8f, 3f);
                        world.motor.Teleport(view + Vector3.up * .1f, Quaternion.LookRotation((heap.position - view).normalized + Vector3.down * .2f));
                        yield return new WaitForSeconds(.6f);
                        yield return Shot($"21-rubble-level{level}");
                    }
                    // Somewhere on that level: a corridor, if it has one.
                    var cell = plan.CorridorCells.Where(c => plan.Cells[c].Kind == CellKind.Corridor && hazards.LevelOf(plan.Cells[c].Floor) == level).DefaultIfEmpty(plan.Cells.First(kv => kv.Value.Kind == CellKind.Room && hazards.LevelOf(kv.Value.Floor) == level).Key).First();
                    var at = dungeon.Built.TransformPoint(DungeonGeometry.Center(cell, plan.Cells[cell].Floor));
                    int falls = hazards.Falls;
                    float until = Time.time + 16;
                    while (Time.time < until && hazards.Falls == falls)
                    {
                        world.motor.Teleport(at + Vector3.up * .1f, world.motor.transform.rotation);
                        yield return new WaitForSeconds(.5f);
                    }
                    Assert.That(hazards.Falls, Is.GreaterThan(falls), "the ceiling comes down on a collapse level");
                    yield return new WaitForSeconds(.5f);
                    yield return Shot($"22-collapse-level{level}");
                    yield return new WaitForSeconds(1.5f);
                    Assert.That(root.Cast<Transform>().Any(t => t.name.StartsWith("Fallen")), Is.True, "fallen chunks lie on the floor");
                }
            }
        }

        /// <summary>A spot up to <paramref name="far"/> m from <paramref name="from"/> with nothing solid between (to look back at it).</summary>
        static Vector3 Clear(Vector3 from, float far)
        {
            var best = from; float bestRoom = 0;
            for (int i = 0; i < 12; i++)
            {
                var dir = Quaternion.Euler(0, i * 30, 0) * Vector3.forward;
                float room = Physics.Raycast(from + Vector3.up * 1.2f, dir, out var hit, far, ~0, QueryTriggerInteraction.Ignore) ? hit.distance - .5f : far;
                if (room > bestRoom && NavMesh.SamplePosition(from + dir * room, out var nav, 1f, NavMesh.AllAreas)) { bestRoom = room; best = nav.position; }
            }
            return best;
        }

        /// <summary>Puts the player <paramref name="s"/> m into a passage, facing in (0) or out (180).</summary>
        void Stand(Transform passage, float s, float yaw)
        {
            var at = passage.TransformPoint(new Vector3(0, .1f, s));
            world.motor.Teleport(at, passage.rotation * Quaternion.Euler(0, yaw, 0));
        }

        /// <summary>The first collider of the world (not the passage's own modules nor the hill) inside the passage, or null.</summary>
        static Collider Blocked(Transform passage)
        {
            var own = passage.GetComponentInParent<DungeonEntrance>().transform;
            var centre = passage.TransformPoint(new Vector3(0, 1.6f, 3.8f));
            foreach (var col in Physics.OverlapBox(centre, new Vector3(1.3f, 1.2f, 3.4f), passage.rotation, ~0, QueryTriggerInteraction.Ignore))
                if (!col.transform.IsChildOf(own) && !col.GetComponentInParent<CharacterController>()) return col;
            return null;
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
