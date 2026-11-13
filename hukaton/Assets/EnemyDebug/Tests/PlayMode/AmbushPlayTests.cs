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
    /// <summary>The mantis man in the debug scene: it lies in wait, springs at a close target and strikes with both sickles.</summary>
    public class AmbushPlayTests
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

        IEnumerator Stand(WorldCreature mantis, float distance)
        {
            var at = mantis.Position + mantis.transform.forward * distance;
            if (NavMesh.SamplePosition(at, out var hit, 2, NavMesh.AllAreas)) at = hit.position;
            sandbox.motor.Teleport(at + Vector3.up * .1f, Quaternion.LookRotation(mantis.Position - at));
            yield return null;
        }

        [UnityTest]
        public IEnumerator LiesInWaitThenSpringsAndStrikesTwice()
        {
            var preview = sandbox.Previews.FirstOrDefault(p => p.source && p.source.name == "Mantis man");
            Assert.That(preview, Is.Not.Null, "the debug scene shows the mantis man");
            Assert.That(preview.source.ambush.enabled, Is.True);
            var mantis = sandbox.Spawn(preview);
            var start = mantis.Position;

            // In plain sight but out of its reach: it keeps still and does not give itself away.
            yield return Stand(mantis, 10);
            yield return Wait(3);
            Assert.That(mantis.Lurking, Is.True, $"still lying in wait at 10 m: {mantis.State}");
            Assert.That(Vector3.Distance(mantis.Position, start), Is.LessThan(.8f), "an ambusher does not wander");
            Assert.That(sandbox.ReceivedDamage, Is.Zero);

            // Close: it springs out and both sickles land, the second one through the first one's protection.
            yield return Stand(mantis, 4.5f);
            float until = Time.time + 4;
            while (Time.time < until && sandbox.ReceivedDamage < 4) yield return null;
            Assert.That(mantis.Lurking, Is.False, "it sprang out");
            Assert.That(sandbox.ReceivedDamage, Is.GreaterThanOrEqualTo(4), "two sickles, a heart each");
        }
    }
}
#endif
