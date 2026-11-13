#if UNITY_EDITOR
using System.Collections;
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
    /// <summary>Swamp enemies that hunt with a shot and a bite, checked in the debug scene.</summary>
    public class SwampHuntersPlayTests
    {
        EnemyDebugSandbox sandbox;

        [UnitySetUp]
        public IEnumerator LoadSavedScene()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/EnemyDebug/Scenes/EnemyDebug.unity", new LoadSceneParameters(LoadSceneMode.Single));
            sandbox = Object.FindFirstObjectByType<EnemyDebugSandbox>();
            yield return Wait(.3f);
            Assert.That(sandbox.world.Running, Is.True);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (sandbox) sandbox.ClearLive();
            yield return null;
        }

        static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }

        WorldCreature Spawn(string prefab)
        {
            var preview = sandbox.Previews.FirstOrDefault(p => p.source && p.source.name == prefab);
            Assert.That(preview, Is.Not.Null, $"the debug scene shows {prefab}");
            return sandbox.Spawn(preview);
        }

        IEnumerator Stand(WorldCreature creature, float distance)
        {
            var dir = new Vector3(0, 0, 10) - creature.Position;
            dir.y = 0;
            var at = creature.Position + dir.normalized * distance;
            if (NavMesh.SamplePosition(at, out var hit, 2, NavMesh.AllAreas)) at = hit.position;
            sandbox.motor.Teleport(at + Vector3.up * .1f, Quaternion.LookRotation(creature.Position - at));
            yield return null;
        }

        [UnityTest]
        public IEnumerator SpiderWebSlowsThenItBites()
        {
            var spider = Spawn("Giant swamp spider");
            Assert.That(sandbox.world.statuses, Is.Not.Null, "the debug scene carries the player's statuses");
            yield return Stand(spider, 9);
            float until = Time.time + 6;
            while (Time.time < until && !sandbox.world.statuses.Has(PlayerStatusKind.Slow)) yield return null;
            Assert.That(sandbox.world.statuses.Has(PlayerStatusKind.Slow), Is.True, "a web that lands slows the player (the Slow debuff)");
            Assert.That(sandbox.ReceivedDamage, Is.GreaterThanOrEqualTo(1), "the web stings for half a heart");
            int afterWeb = sandbox.ReceivedDamage;
            until = Time.time + 6;
            while (Time.time < until && sandbox.ReceivedDamage < afterWeb + 2) yield return null;
            Assert.That(sandbox.ReceivedDamage, Is.GreaterThanOrEqualTo(afterWeb + 2), "it runs in and bites for a heart");
        }
    }
}
#endif
