#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Hakaton.Expeditions.Cards;
using Hakaton.Expeditions.Events;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Expeditions.Tests.PlayMode
{
    /// <summary>
    /// The awakened tree goes wherever the player goes (playtest), in the beast brawl lab
    /// (Assets/Expedition/Scenes/BeastBrawlLab.unity: uneven ground with baked navigation and a running outing):
    /// it follows the player off a ledge with no navigation on it, and burrows to come up behind a player who teleported away.
    /// </summary>
    public sealed class TreeAllyPlayTests
    {
        const string AllyPath = "Assets/Game/Prefabs/Cards/TreeAwakening/Awakened tree.prefab";
        // Open ground of the lab, away from its showcase brawls (removed anyway).
        static readonly Vector3 Spot = new Vector3(30, 0, -75);

        ExpeditionWorld world;
        readonly List<GameObject> made = new List<GameObject>();

        [UnitySetUp]
        public IEnumerator LoadLab()
        {
            Time.timeScale = 1;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Expedition/Scenes/BeastBrawlLab.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            world = Object.FindFirstObjectByType<ExpeditionWorld>();
            Assert.That(world, Is.Not.Null);
            float until = Time.time + 3;
            while (!world.Running && Time.time < until) yield return null;
            Assert.That(world.Running, "the lab opens an outing by itself");
            // No creatures around: the tree only follows.
            foreach (var showcase in Object.FindObjectsByType<BeastBrawl>(FindObjectsSortMode.None)) Object.Destroy(showcase.gameObject);
            made.Clear();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            foreach (var thing in made) if (thing) Object.Destroy(thing);
            yield return null;
        }

        static Vector3 OnGround(Vector3 at)
        {
            if (Physics.Raycast(at + Vector3.up * 10, Vector3.down, out var hit, 20, ~(1 << 2), QueryTriggerInteraction.Ignore)) at.y = hit.point.y;
            return at;
        }

        static float Flat(Vector3 v) { v.y = 0; return v.magnitude; }

        Vector3 PlayerFeet => world.motor.transform.position;

        /// <summary>A tree comes alive at <paramref name="at"/> as the card makes one.</summary>
        TreeAlly Awaken(Vector3 at)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<TreeAlly>(AllyPath);
            Assert.That(prefab, Is.Not.Null);
            Vector3 look = PlayerFeet - at;
            look.y = 0;
            var ally = world.SpawnForOuting(prefab, at, look.sqrMagnitude > .01f ? Quaternion.LookRotation(look) : Quaternion.identity);
            ally.Configure(12, 2, 2.5f, 2.5f, .8f, 3);
            ally.Rise(null, at);
            world.Allies.Add(ally);
            made.Add(ally.gameObject);
            return ally;
        }

        /// <summary>The player walks along the ground at 4 m/s (falling off whatever ends under the feet).</summary>
        IEnumerator Walk(Vector3 direction, float distance, Trace trace)
        {
            direction.y = 0;
            direction.Normalize();
            float walked = 0;
            while (walked < distance)
            {
                float step = Mathf.Min(4 * Time.deltaTime, distance - walked);
                world.motor.TeleportKeepingVelocity(PlayerFeet + direction * step);
                walked += step;
                trace.Sample();
                yield return null;
            }
        }

        static IEnumerator Watch(float seconds, Trace trace)
        {
            float until = Time.time + seconds;
            while (Time.time < until) { trace.Sample(); yield return null; }
        }

        /// <summary>What the tree and the player did, every 0.25 s: goes into failure messages and the log.</summary>
        sealed class Trace
        {
            readonly TreeAllyPlayTests owner;
            readonly TreeAlly tree;
            readonly StringBuilder lines = new StringBuilder();
            readonly float started = Time.time;
            float next;

            public Trace(TreeAllyPlayTests owner, TreeAlly tree) { this.owner = owner; this.tree = tree; }

            public void Sample(string note = null)
            {
                if (note == null && Time.time < next) return;
                next = Time.time + .25f;
                if (!tree) { lines.AppendLine($"{Time.time - started:0.00}s tree gone"); return; }
                var motor = owner.world.motor;
                Vector3 player = motor.transform.position;
                lines.Append($"{Time.time - started:0.00}s ");
                if (note != null) lines.Append("[" + note + "] ");
                lines.AppendLine($"{tree.DebugPathing} | player ({player.x:0.0},{player.y:0.0},{player.z:0.0}) g={(motor.IsGrounded ? 1 : 0)} " +
                                 $"d={Flat(tree.Position - player):0.0} dy={player.y - tree.Position.y:0.0} burrows={tree.Burrows}/{tree.LastBurrowReason}");
            }

            public override string ToString() => "\nTree trace (every 0.25 s):\n" + lines;

            /// <summary>Logs the trace when <paramref name="ok"/> is false and returns it for the assertion messages.</summary>
            public string Report(bool ok, string test)
            {
                Sample("end");
                if (!ok) Debug.Log($"{test} failed.{this}");
                return ToString();
            }
        }

        [UnityTest]
        public IEnumerator TheTreeFollowsThePlayerOffALedge()
        {
            // A 2 m block with no navigation on top: up there the tree can only follow the player's trail.
            Vector3 ground = OnGround(Spot);
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "Test ledge (2 m)";
            made.Add(block);
            block.transform.position = ground + Vector3.up * .5f;
            block.transform.localScale = new Vector3(8, 3, 8);
            Physics.SyncTransforms();
            Vector3 top = ground + Vector3.up * 2;
            world.motor.Teleport(top + new Vector3(0, .05f, .5f), Quaternion.LookRotation(Vector3.forward));
            yield return null;
            var tree = Awaken(top + new Vector3(0, 0, -1.8f));
            var trace = new Trace(this, tree);
            trace.Sample("awakened");
            yield return Watch(1.5f, trace);
            Assert.That(tree.Alive, "it climbed out of the ground" + trace);

            // Off the edge (3.5 m ahead) and on along the ground below.
            trace.Sample("player walks");
            yield return Walk(Vector3.forward, 10, trace);
            trace.Sample("player stops");
            Assert.That(PlayerFeet.y, Is.LessThan(top.y - 1), "the player dropped off the ledge" + trace);

            float until = Time.time + 12;
            while (Time.time < until)
            {
                trace.Sample();
                if (tree.Grounded && Flat(tree.Position - PlayerFeet) <= tree.followDistance + 1 && Mathf.Abs(tree.Position.y - PlayerFeet.y) < 1) break;
                yield return null;
            }
            bool ok = tree.Burrows == 0 && tree.Alive && Mathf.Abs(tree.Position.y - PlayerFeet.y) < 1
                && Flat(tree.Position - PlayerFeet) <= tree.followDistance + 1;
            string report = trace.Report(ok, nameof(TheTreeFollowsThePlayerOffALedge));
            Assert.That(tree.Burrows, Is.Zero, $"walking off a ledge needs no burrowing (it burrowed: {tree.LastBurrowReason}){report}");
            Assert.That(Mathf.Abs(tree.Position.y - PlayerFeet.y), Is.LessThan(1), "it came down the ledge after the player" + report);
            Assert.That(Flat(tree.Position - PlayerFeet), Is.LessThanOrEqualTo(tree.followDistance + 1), "and caught up" + report);
            Assert.That(tree.Alive, "alive" + report);
        }

        [UnityTest]
        public IEnumerator TheTreeBurrowsAndComesUpBehindAPlayerWhoTeleportedAway()
        {
            Vector3 start = OnGround(Spot + Vector3.forward * 5);
            world.motor.Teleport(start + Vector3.up * .05f, Quaternion.LookRotation(Vector3.forward));
            yield return null;
            Vector3 at = OnGround(start - Vector3.forward * 2.5f);
            if (NavMesh.SamplePosition(at, out var hit, 2, NavMesh.AllAreas)) at = hit.position;
            var tree = Awaken(at);
            var trace = new Trace(this, tree);
            trace.Sample("awakened");
            yield return Watch(1.5f, trace);
            Assert.That(tree.Alive, "alive" + trace);
            Assert.That(tree.Burrows, Is.Zero, "beside the player it just stands" + trace);

            // As the Teleport card does: 40 m in one frame, keeping the view.
            Vector3 far = OnGround(start + Vector3.forward * 40);
            world.motor.TeleportKeepingVelocity(far + Vector3.up * .05f);
            trace.Sample("player teleported");

            float until = Time.time + 8;
            while (Time.time < until && (tree.Burrows == 0 || !tree.Alive || tree.State == TreeAllyState.Awakening))
            {
                trace.Sample();
                yield return null;
            }
            float distance = Flat(tree.Position - PlayerFeet);
            bool ok = tree.Burrows == 1 && tree.Alive && distance >= 1.45f && distance <= 6.05f
                && Mathf.Abs(tree.Position.y - PlayerFeet.y) < 1.5f && Flat(tree.Position - at) > 30;
            string report = trace.Report(ok, nameof(TheTreeBurrowsAndComesUpBehindAPlayerWhoTeleportedAway));
            Assert.That(tree.Burrows, Is.EqualTo(1), "it burrowed once" + report);
            Assert.That(tree.LastBurrowReason == BurrowReason.TooFar || tree.LastBurrowReason == BurrowReason.Gap,
                "because the player went where it cannot walk in time: " + tree.LastBurrowReason + report);
            Assert.That(tree.Alive, "and came up again" + report);
            Assert.That(distance, Is.InRange(1.45f, 6.05f), "not on top of the player, close behind" + report);
            Assert.That(Mathf.Abs(tree.Position.y - PlayerFeet.y), Is.LessThan(1.5f), "on the player's level" + report);
            Assert.That(Flat(tree.Position - at), Is.GreaterThan(30), "near the player, not where it went under" + report);
        }
    }
}
#endif
