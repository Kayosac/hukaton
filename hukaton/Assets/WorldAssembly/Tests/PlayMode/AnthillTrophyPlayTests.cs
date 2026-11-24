#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Dungeons.Unity;
using Hakaton.Expeditions;
using Hakaton.Expeditions.Cards;
using Hakaton.Game;
using Hakaton.Loot;
using Hakaton.SpecialItems;
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
    /// The anthill's rewards in the real game (pg_anthill-loot-cards): its own curios in its chests, its trophy cards
    /// coming underground at the level of their source, «Вербовка» turning ants to the player's side (they follow, fight
    /// the wild ones, never turn on the player) and «Эхолот» lighting enemies through the walls. Photographs into
    /// Logs/anthill-trophies.
    /// </summary>
    public sealed class AnthillTrophyPlayTests
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
            shots = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "anthill-trophies");
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
            SceneManager.SetActiveScene(SceneManager.CreateScene("Anthill trophies teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            if (Directory.Exists(saveFolder))
                foreach (var file in Directory.GetFiles(saveFolder)) File.Delete(file);
            foreach (var save in playerSaves) File.WriteAllBytes(save.Key, save.Value);
        }

        [UnityTest]
        public IEnumerator TheAnthillPaysInItsOwnCuriosAndCards()
        {
            var site = streamer.Dungeons;
            Invulnerable(true);
            string note = streamer.GoToNearestDungeon();
            Assert.That(note, Does.StartWith("Иду"), note);
            DungeonEntrance entrance = null;
            yield return WaitFor(() => (entrance = DungeonEntrance.All.FirstOrDefault(e => e && (e.outside.position - world.motor.transform.position).sqrMagnitude < 4)) != null, 8);
            Assert.That(entrance, Is.Not.Null);
            yield return new WaitForSeconds(1f);
            Assert.That(world.trophies.Underground, Is.False, "above ground the trophies are the usual ones");

            // Above ground «Эхолот» works too (nothing to find may be there: then it says so).
            var config = world.workshop.config;
            var sonarCard = config.trophyCards.First(c => c.id == "sonar");
            var recruitCard = config.trophyCards.First(c => c.id == "recruitment");
            Assert.That(config.cards, Has.No.Member(sonarCard), "the goblin never sells the dungeon's cards");
            Assert.That(config.Find("sonar"), Is.SameAs(sonarCard));
            // Thrown for real: it goes off as it leaves the hand, the wave starts from the player.
            var sonarCopy = world.workshop.Model.Current.GrantToHand(sonarCard.ToSpec(), 1, 1);
            var eye = world.viewCamera.transform;
            var thrown = world.Launch(sonarCopy, eye.position + eye.forward * .4f, eye.forward * 18, null, null);
            yield return new WaitForSeconds(.3f);
            Assert.That(thrown == null, Is.True, "the card is gone at once, not flying");
            var waves = Object.FindObjectsByType<SonarWave>(FindObjectsSortMode.None);
            Assert.That(waves.Length, Is.GreaterThan(0), "the wave runs out above ground");
            Assert.That(Vector3.Distance(Flat(waves[0].transform.position), Flat(world.motor.transform.position)), Is.LessThan(1.5f), "from the player");
            yield return Shot("01-sonar-above");

            for (float s = 0; s <= 6.6f && !site.Underground; s += .5f)
            {
                Stand(entrance.passage, s, 0);
                yield return null; yield return null;
            }
            Assert.That(site.Underground, Is.True);
            var dungeon = site.Current;
            for (float s = 6; s >= -1; s -= .5f)
            {
                Stand(dungeon.Passage, s, 180);
                yield return null; yield return null;
            }
            yield return null;
            Assert.That(world.trophies.Underground, Is.True, "underground the dungeon's cards come too");

            // ---- its own curios in its chests ----
            var chests = site.Chests;
            var prize = chests.First(c => c.tier && c.tier.displayName == "Сундук муравейника");
            Assert.That(prize.tier.rank, Is.EqualTo(5));
            Assert.That(prize.tier.bonus.entries.Select(e => e.item.id), Is.EquivalentTo(new[] { "ant_amber_fly", "ant_royal_jelly", "ant_fossil_queen_egg", "ant_inverted_farm", "ant_button_drum" }), "the prize chest can hold all five anthill curios");
            var kit = dungeon.kit;
            Assert.That(kit.stashChest.GetComponent<TreasureChest>().tier.bonus.entries.Select(e => e.item.id), Has.Member("ant_inverted_farm"));
            Assert.That(kit.sideChest.GetComponent<TreasureChest>().tier.bonus.entries.Select(e => e.item.id), Has.Member("ant_button_drum"));
            var pools = Resources.FindObjectsOfTypeAll<LootPool>();
            foreach (var pool in pools)
                Assert.That(pool.entries.Any(e => e.item && e.item.id.StartsWith("ant_")), Is.False, $"{pool.name}: anthill curios never lie about the world");
            yield return Curios(dungeon);

            // ---- trophy cards underground: the dungeon's own, at the source's level ----
            CardCopy arrived = null;
            void Got(CardCopy copy) => arrived = copy;
            world.trophies.Arrived += Got;
            world.trophies.AlwaysDungeon = true;
            Assert.That(world.trophies.Give(world.motor.transform.position + world.motor.transform.forward * 3, 1, 3), Is.True);
            yield return WaitFor(() => arrived != null, 4);
            world.trophies.Arrived -= Got;
            world.trophies.AlwaysDungeon = false;
            Assert.That(arrived, Is.Not.Null, "a card flew into the hand");
            Assert.That(new[] { "sonar", "recruitment" }, Has.Member(arrived.Spec.Id));
            Assert.That(arrived.Level, Is.EqualTo(3), "a card from the Queen or the prize chest is level III");
            Assert.That(TrophyCardRules.LevelByHealth(4, 8, 15), Is.EqualTo(1));
            Assert.That(TrophyCardRules.LevelByHealth(12, 8, 15), Is.EqualTo(2));
            Assert.That(TrophyCardRules.LevelByHealth(20, 8, 15), Is.EqualTo(3));

            // ---- «Вербовка» ----
            var ants = site.Creatures.Where(c => c && c.Alive && c.name == "AntWorker").ToList();
            Assert.That(ants.Count, Is.GreaterThanOrEqualTo(2), "two workers to test with");
            var recruit = ants[0];
            var wild = ants[1];
            var queen = site.Creatures.FirstOrDefault(c => c && c.name == "AntQueen");
            Vector3 spot = Clear(world.motor.transform.position, 8);
            Vector3 forward = Flat(spot - world.motor.transform.position).normalized;
            if (forward.sqrMagnitude < .1f) forward = world.motor.transform.forward;
            var start = world.motor.transform.position;
            Put(recruit, start + forward * 2.5f);
            yield return new WaitForSeconds(.3f);
            Throw(recruitCard, 1, recruit.Position);
            yield return null;
            Assert.That(recruit.Recruited, Is.True, "the worker went over");
            Assert.That(recruit.Faction, Is.EqualTo(Faction.Player));
            var recruitment = (RecruitmentEffect)recruitCard.effectAsset;
            Assert.That(recruitment.Recruitable(recruit), Is.True);
            if (queen) Assert.That(recruitment.Recruitable(queen), Is.False, "the Queen is never recruited");
            world.motor.Teleport(start, Quaternion.LookRotation(recruit.Position + Vector3.up * .3f - (start + Vector3.up * 1.6f)));
            yield return new WaitForSeconds(.6f);
            yield return Shot("10-recruited");

            // It follows the player.
            var away = Clear(start, 10);
            world.motor.Teleport(away + Vector3.up * .1f, Quaternion.LookRotation(Flat(start - away).sqrMagnitude > .01f ? Flat(start - away) : Vector3.forward));
            yield return WaitFor(() => Flat(recruit.Position - world.motor.transform.position).magnitude < 4.5f, 8);
            Assert.That(Flat(recruit.Position - world.motor.transform.position).magnitude, Is.LessThan(4.5f), "the recruit caught up with the player");
            yield return Shot("11-follows");

            // A wild worker comes for the player: the recruit fights it, and never the player.
            int wildBefore = wild.Health;
            Put(wild, world.motor.transform.position + Flat(world.motor.transform.forward).normalized * 4);
            wild.JoinFight(world.Player);
            bool turned = false, fought = false;
            float until = Time.time + 12;
            while (Time.time < until && wild.Alive)
            {
                if ((recruit.State == CreatureState.Strike || recruit.State == CreatureState.Windup) && ReferenceEquals(recruit.Target, world.Player)) turned = true;
                if (recruit.RecruitFoe == wild) fought = true;
                yield return null;
            }
            yield return Shot("12-fights");
            Assert.That(turned, Is.False, "a recruit never winds up at the player");
            Assert.That(fought, Is.True, "the recruit went for the wild worker");
            Assert.That(!wild.Alive || wild.Health < wildBefore, Is.True, $"the wild worker was hurt ({wild.Health}/{wildBefore})");
            Assert.That(recruit.Recruited, Is.True, "still on the player's side");

            // ---- «Эхолот» underground: an enemy behind a wall shows through it ----
            var hidden = site.Creatures.FirstOrDefault(c => c && c.Alive && !c.Recruited && c.name == "AntWorker" && c != recruit);
            Vector3 feet = world.motor.transform.position, look = Vector3.zero, behind = Vector3.zero;
            bool walled = false;
            // From where the player stands, or from where one of the garrison stands.
            foreach (var from in new[] { feet }.Concat(site.Creatures.Where(c => c && c.Alive).Select(c => c.Position)))
                if (NavMesh.SamplePosition(from, out var standing, 1, NavMesh.AllAreas) && BehindWall(standing.position, out look, out behind))
                {
                    feet = standing.position;
                    walled = true;
                    break;
                }
            if (hidden && walled)
            {
                Put(hidden, behind);
                world.motor.Teleport(feet, Quaternion.LookRotation(look));
                yield return new WaitForSeconds(.3f);
                Throw(sonarCard, 1, world.motor.transform.position);
                yield return new WaitForSeconds(.15f);
                yield return Shot("20-sonar-wave");
                yield return new WaitForSeconds(.6f);
                var mark = SonarMark.On(hidden);
                Assert.That(mark, Is.Not.Null, "the worker behind the wall is lit");
                Assert.That(mark.Shown && mark.Copies > 0, Is.True);
                yield return Shot("21-sonar-through-wall");
            }
            else Debug.Log("[Anthill] no wall to hide a worker behind here");

            // ---- «Эхолот» everywhere around: enemies light up, recruits do not ----
            var lit = site.Creatures.Where(c => c && c.Alive && !c.Recruited && c.isActiveAndEnabled).ToList();
            Throw(sonarCard, 2, world.motor.transform.position);
            yield return new WaitForSeconds(1.6f);
            var marks = Object.FindObjectsByType<SonarMark>(FindObjectsSortMode.None);
            Debug.Log($"[Anthill] sonar: {marks.Length} marks, {lit.Count} wild creatures awake");
            Assert.That(marks.Length, Is.GreaterThan(0), "the wave found enemies");
            Assert.That(marks.Any(m => m.Target == recruit), Is.False, "the recruit is not an enemy");
            Assert.That(marks.Where(m => m.Shown).All(m => m.Copies > 0), Is.True, "every lit enemy has its silhouette");
            float reach = ((SonarEffect)sonarCard.effectAsset).radius.At(2);
            Assert.That(marks.All(m => Vector3.Distance(m.Target.transform.position, world.motor.transform.position) <= reach + 12), Is.True);
            yield return Shot("22-sonar-around");
            yield return new WaitForSeconds(11);
            Assert.That(Object.FindObjectsByType<SonarMark>(FindObjectsSortMode.None).Length, Is.EqualTo(0), "the silhouettes go out after their time");
            Invulnerable(false);
        }

        [UnityTest]
        public IEnumerator TheAntCompassSmellsAnthillsThenStashesThenTheWayOut()
        {
            var site = streamer.Dungeons;
            Invulnerable(true);
            var host = Object.FindAnyObjectByType<SpecialItemsHost>();
            var hand = Object.FindAnyObjectByType<SpecialItemsHand>();
            var compass = host.Find("ant_compass");
            Assert.That(compass, Is.Not.Null, "the compass is in the catalogue");
            Assert.That(compass.price, Is.EqualTo(800));
            Assert.That(compass.unlock, Is.EqualTo(SpecialItemUnlock.AfterQueen));
            Assert.That(game.QueenSlain, Is.False);
            Assert.That(host.IsUnlocked(compass), Is.False, "not for sale before the Queen falls");
            host.Grant(compass);
            var tool = (CompassTool)hand.ToolFor(compass);
            Assert.That(tool, Is.Not.Null, "its tool is on the player");

            // Above ground: toward the nearest anthill, from a few chunks away.
            yield return new WaitForSeconds(.5f);
            hand.Take(compass);
            yield return new WaitForSeconds(.6f);
            Assert.That(tool.IsOut, Is.True);
            Assert.That(tool.Target.Kind, Is.EqualTo(CompassTargetKind.Entrance), "above ground it smells an anthill");
            var target = tool.Target.Point;
            var feet = world.motor.transform.position;
            // Facing the anthill the ant points straight up; turned away, it points the other way round.
            world.motor.Teleport(feet, Quaternion.LookRotation(Flat(target - feet)));
            yield return new WaitForSeconds(1.5f);
            Debug.Log($"[Anthill] compass facing the anthill: {tool.Heading:0}° ({Flat(target - feet).magnitude:0} m)");
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(0, tool.Heading)), Is.LessThan(30), "facing it, the ant looks straight ahead");
            yield return Shot("40-compass-facing");
            world.motor.Teleport(feet, Quaternion.LookRotation(Quaternion.Euler(0, 90, 0) * Flat(target - feet)));
            yield return new WaitForSeconds(1.5f);
            Assert.That(Mathf.DeltaAngle(0, tool.Heading), Is.InRange(-120, -60), "anthill on the left: the ant turns left");
            yield return Shot("41-compass-left");

            // Down in the anthill: toward the stashes, then — every stash opened — toward the way up.
            string note = streamer.GoToNearestDungeon();
            Assert.That(note, Does.StartWith("Иду"), note);
            DungeonEntrance entrance = null;
            yield return WaitFor(() => (entrance = DungeonEntrance.All.FirstOrDefault(e => e && (e.outside.position - world.motor.transform.position).sqrMagnitude < 4)) != null, 8);
            yield return new WaitForSeconds(.5f);
            Assert.That(Flat(tool.Target.Point - entrance.passage.position).magnitude, Is.LessThan(1f), "by the hill it smells this hill's mouth");
            for (float s = 0; s <= 6.6f && !site.Underground; s += .5f) { Stand(entrance.passage, s, 0); yield return null; yield return null; }
            for (float s = 6; s >= -1; s -= .5f) { Stand(site.Current.Passage, s, 180); yield return null; yield return null; }
            yield return new WaitForSeconds(.6f);
            if (!tool.IsOut) hand.Take(compass);
            yield return new WaitForSeconds(.6f);
            var stashes = site.Chests.Where(c => c.name == "Anthill Stash Chest").ToList();
            Assert.That(stashes.Count, Is.GreaterThan(0));
            Assert.That(tool.Target.Kind, Is.EqualTo(CompassTargetKind.Stash), "below it smells a stash");
            var nearest = stashes.OrderBy(c => (c.transform.position - world.motor.transform.position).sqrMagnitude).First();
            Assert.That((tool.Target.Point - nearest.transform.position).magnitude, Is.LessThan(.1f), "the nearest one");
            yield return Shot("42-compass-stash");
            foreach (var chest in stashes) chest.Open();
            yield return new WaitForSeconds(.6f);
            Assert.That(tool.Target.Kind, Is.EqualTo(CompassTargetKind.WayOut), "every stash opened: toward the way up");

            // The first slain Queen puts it on the goblin's table.
            var queen = site.Creatures.FirstOrDefault(c => c && c.name == "AntQueen");
            if (queen)
            {
                queen.Damage(queen.maximumHealth * 2, Vector3.zero, queen.Centre, 0);
                yield return new WaitForSeconds(.3f);
                Assert.That(game.QueenSlain, Is.True, "the Queen's death is noted");
                Assert.That(host.IsUnlocked(compass), Is.True, "the compass is on sale now");
            }
            else Debug.Log("[Anthill] small anthill: no Queen to slay here");
            Invulnerable(false);
        }

        /// <summary>Puts each anthill curio on the floor in front of the player (as found: twice its shelf size) and photographs them.</summary>
        IEnumerator Curios(DungeonBuilder dungeon)
        {
            var ids = new[] { "ant_button_drum", "ant_inverted_farm", "ant_amber_fly", "ant_royal_jelly", "ant_fossil_queen_egg" };
            var catalog = world.lootCatalog;
            var placed = new List<GameObject>();
            var eye = world.motor.transform;
            Vector3 forward = Flat(eye.forward).normalized, side = Vector3.Cross(Vector3.up, forward);
            Vector3 floor = eye.position + forward * 2.2f;
            if (Physics.Raycast(floor + Vector3.up, Vector3.down, out var hit, 4, ~(1 << 2), QueryTriggerInteraction.Ignore)) floor = hit.point;
            for (int i = 0; i < ids.Length; i++)
            {
                var item = catalog ? catalog.items.FirstOrDefault(x => x && x.id == ids[i]) : null;
                Assert.That(item, Is.Not.Null, ids[i] + " is in the catalog");
                Assert.That(item.category, Is.EqualTo("Диковины"));
                Assert.That(item.model, Is.Not.Null);
                var o = Object.Instantiate(item.model, floor + side * ((i - 2) * .55f), Quaternion.LookRotation(-forward));
                o.transform.localScale = Vector3.one * 2;
                placed.Add(o);
            }
            world.motor.Teleport(eye.position, Quaternion.LookRotation(floor + Vector3.up * .2f - (eye.position + Vector3.up * 1.6f)));
            yield return new WaitForSeconds(.4f);
            yield return Shot("05-curios");
            foreach (var o in placed) Object.Destroy(o);
        }

        void Throw(CardDefinition definition, int level, Vector3 at)
        {
            var trip = world.workshop.Model.Current;
            var copy = trip.GrantToHand(definition.ToSpec(), level, 1);
            world.ApplyImpact(copy, definition, null, at + Vector3.up * .05f, Vector3.up, Vector3.down, world.motor.transform.position);
            trip.Withdraw(copy.Id);
        }

        /// <summary>
        /// A spot on the dungeon's navigation just behind a wall the player faces: the eye ray hits rock within a few
        /// metres, the spot lies a few metres beyond it on about the same floor and cannot be seen.
        /// </summary>
        static bool BehindWall(Vector3 feet, out Vector3 direction, out Vector3 spot)
        {
            Vector3 eye = feet + Vector3.up * 1.5f;
            for (int i = 0; i < 24; i++)
            {
                direction = Quaternion.Euler(0, i * 15, 0) * Vector3.forward;
                if (!Physics.Raycast(eye, direction, out var hit, 9, ~(1 << 2), QueryTriggerInteraction.Ignore) || hit.distance < 1.5f
                    || hit.rigidbody) continue;
                for (float beyond = 1.5f; beyond <= 9; beyond += 1)
                {
                    if (!NavMesh.SamplePosition(feet + direction * (hit.distance + beyond), out var nav, 1.2f, NavMesh.AllAreas)) continue;
                    if (Mathf.Abs(nav.position.y - feet.y) > 2.5f) continue;
                    if (!Physics.Linecast(eye, nav.position + Vector3.up * .8f, ~(1 << 2), QueryTriggerInteraction.Ignore)) continue;
                    spot = nav.position;
                    return true;
                }
            }
            direction = spot = Vector3.zero;
            return false;
        }

        static void Put(WorldCreature creature, Vector3 at)
        {
            if (NavMesh.SamplePosition(at, out var nav, 3, NavMesh.AllAreas)) at = nav.position;
            creature.body.position = at;
            creature.transform.position = at;
            if (creature.navigator.isOnNavMesh) creature.navigator.Warp(at);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

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
