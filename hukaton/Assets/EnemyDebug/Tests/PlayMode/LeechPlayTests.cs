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
    /// <summary>
    /// The giant swamp leech in the debug scene: it waits under the surface, comes up when the player is near, spits
    /// blood from afar and sucks blood up close, healing itself.
    /// </summary>
    public class LeechPlayTests
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

        IEnumerator Stand(WorldCreature leech, float distance)
        {
            var dir = new Vector3(0, 0, 10) - leech.Position;
            dir.y = 0;
            var at = leech.Position + dir.normalized * distance;
            if (NavMesh.SamplePosition(at, out var hit, 2, NavMesh.AllAreas)) at = hit.position;
            sandbox.motor.Teleport(at + Vector3.up * .1f, Quaternion.LookRotation(leech.Position - at));
            yield return null;
        }

        [UnityTest]
        public IEnumerator WaitsUnderThenSpitsAndSucksBlood()
        {
            var preview = sandbox.Previews.FirstOrDefault(p => p.source && p.source.name == "Giant swamp leech");
            Assert.That(preview, Is.Not.Null, "the debug scene shows the leech");
            var leech = sandbox.Spawn(preview);
            yield return Stand(leech, 14);
            yield return Wait(1.5f);
            Assert.That(leech.Lurking, Is.True, $"waits under the surface: {leech.State}");
            Assert.That(leech.Passable, Is.True, "nothing to bump into while it is under");
            Assert.That(leech.Position.y, Is.GreaterThan(-.5f), "it stays on the bottom");

            // Within reach: it comes up and spits blood clots.
            yield return Stand(leech, 6.5f);
            bool spat = false;
            float until = Time.time + 5;
            while (Time.time < until && !spat)
            {
                yield return null;
                spat = EnemyProjectile.Active.Any(p => p && p.Shooter == leech);
            }
            Assert.That(leech.Lurking, Is.False, "it came up");
            Assert.That(leech.Passable, Is.False, "up, it has a body again");
            Assert.That(spat, Is.True, "it spits a blood clot from afar");

            // Hurt, it latches on up close and drinks its health back.
            leech.Damage(4, Vector3.zero, leech.Centre, 0);
            int hurt = leech.Health;
            yield return Wait(.6f);
            yield return Stand(leech, 2);
            until = Time.time + 6;
            while (Time.time < until && !leech.Holding) yield return null;
            Assert.That(leech.Holding, Is.True, $"it latches on: {leech.State}");
            until = Time.time + 3;
            while (Time.time < until && leech.Holding) yield return null;
            Assert.That(leech.Health, Is.GreaterThan(hurt), "sucking blood heals it");
        }
    }
}
#endif
