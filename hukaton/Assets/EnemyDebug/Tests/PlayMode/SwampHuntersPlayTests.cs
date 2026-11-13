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

        [UnityTest]
        public IEnumerator ToadSpitsFromAfarAndYanksWithItsTongue()
        {
            var toad = Spawn("Giant vile toad");
            // Far: it spits slime.
            yield return Stand(toad, 10);
            bool spat = false;
            float until = Time.time + 8;
            while (Time.time < until && !spat) { yield return null; spat = EnemyProjectile.Active.Any(p => p && p.Shooter == toad); }
            Assert.That(spat, Is.True, "it spits slime from afar");
            // Within the tongue's reach: it pulls the player in, and the slam follows.
            yield return Wait(.5f);
            yield return Stand(toad, 5);
            float start = Vector3.Distance(sandbox.motor.transform.position, toad.Position);
            float closest = start;
            int before = sandbox.ReceivedDamage;
            until = Time.time + 6;
            while (Time.time < until && sandbox.ReceivedDamage < before + 3)
            {
                yield return null;
                closest = Mathf.Min(closest, Vector3.Distance(sandbox.motor.transform.position, toad.Position));
            }
            Assert.That(closest, Is.LessThan(start - 1.5f), "the tongue yanks the player in");
            Assert.That(sandbox.ReceivedDamage, Is.GreaterThanOrEqualTo(before + 3), "the slam takes one and a half hearts");
        }

        [UnityTest]
        public IEnumerator FlyAgaricStandsAndSpitsAFan()
        {
            var agaric = Spawn("Raging fly agaric");
            Assert.That(agaric.fireVulnerability, Is.Not.Null, "it fears fire");
            var start = agaric.Position;
            yield return Stand(agaric, 8);
            int most = 0;
            float until = Time.time + 6;
            while (Time.time < until && most < 3)
            {
                yield return null;
                most = Mathf.Max(most, EnemyProjectile.Active.Count(p => p && p.Shooter == agaric));
            }
            Assert.That(most, Is.GreaterThanOrEqualTo(3), "a fan of three spits");
            Assert.That(Vector3.Distance(agaric.Position, start), Is.LessThan(2), "rooted: it hardly moves");
        }

        [UnityTest]
        public IEnumerator MosquitoSwarmFliesInBitesAndFallsToOneHit()
        {
            var swarm = Spawn("Mosquito swarm");
            Assert.That(swarm.flying, Is.True);
            Assert.That(swarm.fireVulnerability, Is.Not.Null, "it fears fire");
            yield return Stand(swarm, 8);
            float until = Time.time + 6;
            while (Time.time < until && sandbox.ReceivedDamage < 1) yield return null;
            Assert.That(sandbox.ReceivedDamage, Is.GreaterThanOrEqualTo(1), "it bites for half a heart");
            swarm.Damage(1, Vector3.zero, swarm.Centre, 0);
            yield return null;
            Assert.That(swarm.Alive, Is.False, "one hit swats it");
        }
    }
}
#endif