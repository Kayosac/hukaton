#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Dungeons.Unity;
using Hakaton.Expeditions;
using Hakaton.Expeditions.Cards;
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
    /// A recruited worker in the anthill's entrance chamber catches up with the player standing behind the timber prop
    /// (AN14_TimberProp, in every anthill): its way goes round the prop, and its body does not stick on it
    /// (<see cref="PropFootprint"/>).
    /// </summary>
    public sealed class AnthillTimberPropPlayTests
    {
        const string ScenePath = "Assets/Game/Scenes/Hakaton.unity";
        GameLoop game;
        ExpeditionWorld world;
        WorldStreamer streamer;

        [UnitySetUp]
        public IEnumerator DepartIntoANewWorld()
        {
            // The save lives in a temporary folder of this test: the player's own files are never read or touched.
            GameSaveFolder.UseTemporaryForTests(nameof(AnthillTimberPropPlayTests));
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
            SceneManager.SetActiveScene(SceneManager.CreateScene("Anthill timber prop teardown"));
            var gameScene = SceneManager.GetSceneByPath(ScenePath);
            if (gameScene.isLoaded) yield return SceneManager.UnloadSceneAsync(gameScene);
            GameSaveFolder.EndTemporaryForTests();
        }

        [UnityTest]
        public IEnumerator ARecruitCatchesUpRoundTheTimberProp()
        {
            var site = streamer.Dungeons;
            var cheats = Object.FindAnyObjectByType<CheatPowers>();
            if (cheats) cheats.SetInvulnerable(true);
            string note = streamer.GoToNearestDungeon();
            Assert.That(note, Does.StartWith("Иду"), note);
            DungeonEntrance entrance = null;
            yield return WaitFor(() => (entrance = DungeonEntrance.All.FirstOrDefault(e => e && (e.outside.position - world.motor.transform.position).sqrMagnitude < 4)) != null, 8);
            Assert.That(entrance, Is.Not.Null);
            yield return new WaitForSeconds(1f);
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

            // The timber prop of the entrance chamber, in the chamber's frame.
            var chamber = dungeon.Chambers[dungeon.Plan.Entrance.Index];
            var timber = chamber.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.StartsWith("AN14_TimberProp"));
            Assert.That(timber, Is.Not.Null, "the entrance chamber has its timber prop");
            var frame = chamber.transform;
            var colliders = timber.GetComponentsInChildren<Collider>().Where(c => !c.isTrigger).ToArray();
            Assert.That(colliders, Is.Not.Empty);
            Bounds box = default;
            bool first = true;
            foreach (var col in colliders)
                foreach (var corner in Corners(col.bounds))
                {
                    var local = frame.InverseTransformPoint(corner);
                    if (first) { box = new Bounds(local, Vector3.zero); first = false; } else box.Encapsulate(local);
                }

            // The worker on one side of it along its length, the player on the other: the straight way runs through it.
            Assert.That(NavMesh.SamplePosition(frame.TransformPoint(new Vector3(box.center.x, 0, box.min.z - 2.5f)), out var from, 1.2f, NavMesh.AllAreas), Is.True, "room before the timber prop");
            Assert.That(NavMesh.SamplePosition(frame.TransformPoint(new Vector3(box.center.x, 0, box.max.z + 2.5f)), out var to, 1.2f, NavMesh.AllAreas), Is.True, "room behind the timber prop");
            Vector3 lift = Vector3.up * .8f;
            Assert.That(Physics.Linecast(from.position + lift, to.position + lift, out var through, ~(1 << 2), QueryTriggerInteraction.Ignore) && through.collider.transform.IsChildOf(timber), Is.True,
                "the timber prop stands between the worker and the player");

            // The garrison round here out of the way, then «Вербовка» on a worker.
            var ants = site.Creatures.Where(c => c && c.Alive && c.name == "AntWorker").ToList();
            Assert.That(ants, Is.Not.Empty, "a worker to recruit");
            var recruit = ants.OrderBy(c => (c.Position - from.position).sqrMagnitude).First();
            foreach (var other in site.Creatures)
                if (other && other.Alive && other != recruit && (other.Position - from.position).sqrMagnitude < 30 * 30)
                    other.Damage(other.maximumHealth * 2, Vector3.zero, other.Centre, 0);
            world.motor.Teleport(from.position + (from.position - to.position).normalized * 2 + Vector3.up * .1f, Quaternion.LookRotation(Flat(from.position - to.position)));
            Put(recruit, from.position);
            yield return new WaitForSeconds(.5f);
            var recruitCard = world.workshop.config.trophyCards.First(c => c.id == "recruitment");
            Throw(recruitCard, 1, recruit.Position);
            yield return null;
            Assert.That(recruit.Recruited, Is.True, "the worker went over");
            yield return new WaitForSeconds(.5f);

            // The player steps behind the timber prop, facing back: the recruit follows round it.
            Put(recruit, from.position);
            world.motor.Teleport(to.position + Vector3.up * .1f, Quaternion.LookRotation(Flat(from.position - to.position)));
            float followDistance = ((RecruitmentEffect)recruitCard.effectAsset).orders.followDistance;
            Debug.Log($"[Anthill timber] worker {Flat(to.position - from.position).magnitude:F1} m from the player across the prop, follows at {followDistance} m");
            yield return WaitFor(() => Flat(recruit.Position - world.motor.transform.position).magnitude < 4.5f, 12);
            var at = frame.InverseTransformPoint(recruit.Position);
            Assert.That(Flat(recruit.Position - world.motor.transform.position).magnitude, Is.LessThan(4.5f),
                $"the recruit caught up with the player (it stands at {at:F2} in the chamber, the prop spans z {box.min.z:F2}..{box.max.z:F2})");
            Assert.That(recruit.Recruited, Is.True, "still on the player's side");
            if (cheats) cheats.SetInvulnerable(false);
        }

        static Vector3[] Corners(Bounds b)
        {
            var corners = new Vector3[8];
            for (int c = 0; c < 8; c++)
                corners[c] = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
            return corners;
        }

        void Throw(CardDefinition definition, int level, Vector3 at)
        {
            var trip = world.workshop.Model.Current;
            var copy = trip.GrantToHand(definition.ToSpec(), level, 1);
            world.ApplyImpact(copy, definition, null, at + Vector3.up * .05f, Vector3.up, Vector3.down, world.motor.transform.position);
            trip.Withdraw(copy.Id);
        }

        static void Put(WorldCreature creature, Vector3 at)
        {
            if (NavMesh.SamplePosition(at, out var nav, 3, NavMesh.AllAreas)) at = nav.position;
            creature.body.position = at;
            creature.transform.position = at;
            if (creature.navigator.isOnNavMesh) creature.navigator.Warp(at);
        }

        void Stand(Transform passage, float s, float yaw)
        {
            var at = passage.TransformPoint(new Vector3(0, .1f, s));
            world.motor.Teleport(at, passage.rotation * Quaternion.Euler(0, yaw, 0));
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
        }
    }
}
#endif
