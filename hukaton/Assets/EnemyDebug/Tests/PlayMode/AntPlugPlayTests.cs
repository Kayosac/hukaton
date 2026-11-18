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
    public class AntPlugPlayTests
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
            Assert.That(sandbox.Previews.Any(p => p.source && p.source.name == "AntPlug"), Is.True);
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
        EnemyDebugPreview Preview() => sandbox.Previews.First(p => p.source && p.source.name == "AntPlug");

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
        public IEnumerator AggressiveHeadbuttTakesThreeHeartsAndAlwaysDeafens()
        {
            var worker = SpawnWorker();
            Stand(worker.Position + Vector3.back * 4);
            yield return Wait(.35f);
            Assert.That(worker.State, Is.Not.EqualTo(CreatureState.Threat), "Aggressive plug attacks without a territorial warning");
            yield return Until(() => sandbox.ReceivedDamage > 0, 9);
            Assert.That(sandbox.ReceivedDamage, Is.EqualTo(6));
            Assert.That(sandbox.world.statuses.Has(PlayerStatusKind.Deafen), Is.True);
            Assert.That(sandbox.world.statuses.Remaining(PlayerStatusKind.Deafen), Is.InRange(4.5f, 5f));
            Assert.That(sandbox.world.statuses.Strength(PlayerStatusKind.Deafen, 0), Is.EqualTo(600));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator MissUsesTheLongHeadbuttRecovery()
        {
            var worker = SpawnWorker();
            Stand(worker.Position + Vector3.back * 1.4f);
            yield return Until(() => worker.State == CreatureState.Windup, 4);
            Assert.That(worker.State, Is.EqualTo(CreatureState.Windup));
            Stand(worker.Position + Vector3.right * 6);
            yield return Until(() => worker.State == CreatureState.Recover, 2.5f);
            Assert.That(worker.State, Is.EqualTo(CreatureState.Recover));
            Assert.That(sandbox.ReceivedDamage, Is.Zero);
            float started = Time.time;
            yield return Until(() => worker.State != CreatureState.Recover, 3);
            float elapsed = Time.time - started;
            Assert.That(elapsed, Is.InRange(2.05f, 2.5f), $"Miss recovery must be about 2.2 s, was {elapsed}");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator SolitaryDeathUsesIndependentDropChanceAndShowsDeathPose()
        {
            var worker = sandbox.Spawn(Preview());
            Assert.That(sandbox.LiveCreatures.Count, Is.EqualTo(1));
            Assert.That(worker.Squad, Is.Null);
            var rolled = new List<Hakaton.Loot.LootItem>();
            worker.drops.Roll(() => .79f, rolled);
            Assert.That(rolled.Count, Is.EqualTo(1));
            rolled.Clear();
            worker.drops.Roll(() => .8f, rolled);
            Assert.That(rolled, Is.Empty, "The independent drop chance is exactly 80 per cent");

            var before = Object.FindObjectsByType<WorldLoot>(FindObjectsSortMode.None).ToList();
            Stand(worker.Position + Vector3.back * 20);
            UnityEngine.Random.InitState(1);
            worker.Damage(worker.maximumHealth, Vector3.zero, worker.Centre, 0);
            yield return Wait(.6f);
            Assert.That(worker.Alive, Is.False);
            Assert.That(worker.view.Pose, Is.EqualTo(CreaturePose.Death));
            yield return Wait(worker.corpseSeconds + 1f);
            Assert.That(worker.gameObject.activeSelf, Is.False);
            madeLoot.AddRange(Object.FindObjectsByType<WorldLoot>(FindObjectsSortMode.None).Except(before));
            Assert.That(madeLoot.Count, Is.EqualTo(1));
            Assert.That(madeLoot[0].item.id, Is.EqualTo("ant_plug_dead_head"));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
#endif
