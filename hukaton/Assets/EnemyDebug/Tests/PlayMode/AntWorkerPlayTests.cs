#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.EnemyDebug.Tests.PlayMode
{
    public class AntWorkerPlayTests
    {
        EnemyDebugSandbox sandbox;
        readonly List<WorldLoot> madeLoot = new List<WorldLoot>();

        [UnitySetUp]
        public IEnumerator LoadSavedScene()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/EnemyDebug/Scenes/EnemyDebug.unity", new LoadSceneParameters(LoadSceneMode.Single));
            sandbox = Object.FindFirstObjectByType<EnemyDebugSandbox>();
            yield return Wait(.3f);
            Assert.That(sandbox.world.Running, Is.True);
            Assert.That(sandbox.Previews.Any(p => p.source && p.source.name == "AntWorker"), Is.True);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (sandbox) sandbox.ClearLive();
            foreach (var loot in madeLoot) if (loot) Object.Destroy(loot.gameObject);
            madeLoot.Clear();
            yield return null;
        }

        static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }
        static IEnumerator Until(System.Func<bool> ready, float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until && !ready()) yield return null;
        }

        EnemyDebugPreview Preview() => sandbox.Previews.First(p => p.source && p.source.name == "AntWorker");

        WorldCreature SpawnWorker()
        {
            var preview = Preview();
            Vector3 at = preview.transform.position + Vector3.back * 3;
            Assert.That(NavMesh.SamplePosition(at, out var hit, 3, NavMesh.AllAreas), Is.True);
            var worker = Object.Instantiate(preview.source, hit.position, preview.transform.rotation, sandbox.liveRoot);
            worker.world = sandbox.world;
            worker.ResetForOuting(new System.Random(17));
            sandbox.world.creatures.Clear();
            sandbox.world.creatures.Add(worker);
            return worker;
        }

        void Stand(Vector3 at)
        {
            sandbox.motor.Teleport(at + Vector3.up * .05f, Quaternion.identity);
            Physics.SyncTransforms();
        }

        [UnityTest]
        public IEnumerator TerritorialWarningLeadsToTwoHeartHitAndConfiguredDeafenChance()
        {
            var worker = SpawnWorker();
            Stand(worker.Position + Vector3.back * 6);
            yield return Until(() => worker.State == CreatureState.Threat, 2);
            Assert.That(worker.State, Is.EqualTo(CreatureState.Threat), "The worker warns inside 7 m before attacking");
            Assert.That(sandbox.ReceivedDamage, Is.Zero);
            Stand(worker.Position + Vector3.back * 4);
            yield return Until(() => sandbox.ReceivedDamage > 0, 9);
            Assert.That(sandbox.ReceivedDamage, Is.EqualTo(4), "One pickaxe hit takes two player hearts");

            int applied = StatusInflict.ApplyAll(worker.strikeStatuses, sandbox.world.statuses, () => .49f);
            Assert.That(applied, Is.EqualTo(1));
            Assert.That(sandbox.world.statuses.Has(PlayerStatusKind.Deafen), Is.True);
            Assert.That(sandbox.world.statuses.Remaining(PlayerStatusKind.Deafen), Is.InRange(4.5f, 5f));
            Assert.That(sandbox.world.statuses.Strength(PlayerStatusKind.Deafen, 0), Is.EqualTo(600));
            sandbox.world.statuses.Remove(PlayerStatusKind.Deafen);
            applied = StatusInflict.ApplyAll(worker.strikeStatuses, sandbox.world.statuses, () => .5f);
            Assert.That(applied, Is.Zero, "The configured chance is exactly 50 per cent");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator MissUsesTheLongPickaxeRecovery()
        {
            var worker = SpawnWorker();
            Stand(worker.Position + Vector3.back * 1.5f);
            yield return Until(() => worker.State == CreatureState.Windup, 5);
            Assert.That(worker.State, Is.EqualTo(CreatureState.Windup));
            Stand(worker.Position + Vector3.right * 6);
            yield return Until(() => worker.State == CreatureState.Recover, 2);
            Assert.That(worker.State, Is.EqualTo(CreatureState.Recover));
            Assert.That(sandbox.ReceivedDamage, Is.Zero, "The player left the fixed strike direction before contact");
            float started = Time.time;
            yield return Until(() => worker.State != CreatureState.Recover, 2.5f);
            float elapsed = Time.time - started;
            Assert.That(elapsed, Is.InRange(1.35f, 1.8f), $"Miss recovery must be about 1.5 s, was {elapsed}");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator WorkerSpawnsAsAConfiguredPatrolSquad()
        {
            var preview = Preview();
            Stand(preview.transform.position + Vector3.back * 30);
            var first = sandbox.Spawn(preview);
            Assert.That(first.Squad, Is.Not.Null);
            Assert.That(first.Squad.Members.Count, Is.EqualTo(sandbox.squadSize));
            Assert.That(first.Squad.Route.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(first.Squad.Settings.flank, Is.True);
            foreach (var member in first.Squad.Members)
            {
                Assert.That(member.navigator.isOnNavMesh, Is.True);
                Assert.That(member.Health, Is.EqualTo(4));
            }
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator DeathShowsDeathPoseAndDropsAcceptedLoot()
        {
            var before = Object.FindObjectsByType<WorldLoot>(FindObjectsSortMode.None).ToList();
            var worker = SpawnWorker();
            Stand(worker.Position + Vector3.back * 20);
            worker.Damage(worker.maximumHealth, Vector3.zero, worker.Centre, 0);
            yield return Wait(.6f);
            Assert.That(worker.Alive, Is.False);
            Assert.That(worker.view.Pose, Is.EqualTo(CreaturePose.Death));
            yield return Wait(worker.corpseSeconds + 1f);
            Assert.That(worker.gameObject.activeSelf, Is.False);
            madeLoot.AddRange(Object.FindObjectsByType<WorldLoot>(FindObjectsSortMode.None).Except(before));
            Assert.That(madeLoot.Count, Is.GreaterThanOrEqualTo(1), "atLeastOne guarantees a real world drop");
            var accepted = new[] { "ant_worker_pickaxe", "ant_worker_hardhat", "ant_worker_mitten" };
            foreach (var loot in madeLoot)
            {
                Assert.That(loot.item, Is.Not.Null);
                Assert.That(accepted, Contains.Item(loot.item.id));
                Assert.That(loot.item.icon, Is.Not.Null);
            }
            LogAssert.NoUnexpectedReceived();
        }
    }
}
#endif
