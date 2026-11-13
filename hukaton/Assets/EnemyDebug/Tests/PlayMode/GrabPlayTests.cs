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
    /// <summary>The swamp mutant in the debug scene: its strike grabs the player, holds and squeezes; a hit breaks the hold.</summary>
    public class GrabPlayTests
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

        IEnumerator Grabbed(WorldCreature mutant)
        {
            var at = mutant.Position + mutant.transform.forward * 2.2f;
            if (NavMesh.SamplePosition(at, out var hit, 2, NavMesh.AllAreas)) at = hit.position;
            sandbox.motor.Teleport(at + Vector3.up * .1f, Quaternion.LookRotation(mutant.Position - at));
            float until = Time.time + 6;
            while (Time.time < until && !mutant.Holding) yield return null;
            Assert.That(mutant.Holding, Is.True, $"the lunge grabs the player: {mutant.State}");
        }

        WorldCreature Spawn()
        {
            var preview = sandbox.Previews.FirstOrDefault(p => p.source && p.source.name == "Swamp mutant");
            Assert.That(preview, Is.Not.Null, "the debug scene shows the swamp mutant");
            Assert.That(preview.source.grab.enabled, Is.True);
            return sandbox.Spawn(preview);
        }

        [UnityTest]
        public IEnumerator GrabHoldsAndSqueezesThenLetsGo()
        {
            var mutant = Spawn();
            yield return Grabbed(mutant);
            int atGrab = sandbox.ReceivedDamage;
            Assert.That(atGrab, Is.GreaterThanOrEqualTo(1), "the grab itself takes half a heart");
            yield return Wait(.3f);
            Assert.That(sandbox.motor.MoveSpeedScale, Is.EqualTo(0).Within(.001f), "held: the player cannot walk");
            float until = Time.time + 3;
            while (Time.time < until && mutant.Holding) yield return null;
            Assert.That(mutant.Holding, Is.False, "it lets go after the hold");
            Assert.That(sandbox.ReceivedDamage - atGrab, Is.GreaterThanOrEqualTo(2), "two squeezes of half a heart while held");
            yield return Wait(.3f);
            Assert.That(sandbox.motor.MoveSpeedScale, Is.GreaterThan(.5f), "free to walk again");
        }

        [UnityTest]
        public IEnumerator HitBreaksTheHold()
        {
            var mutant = Spawn();
            yield return Grabbed(mutant);
            yield return Wait(.2f);
            mutant.Damage(1, Vector3.zero, mutant.Centre, 0);
            yield return null;
            yield return null;
            Assert.That(mutant.Holding, Is.False, "a hit makes it let go");
            yield return Wait(.3f);
            Assert.That(sandbox.motor.MoveSpeedScale, Is.GreaterThan(.5f), "free to walk again");
        }
    }
}
#endif
