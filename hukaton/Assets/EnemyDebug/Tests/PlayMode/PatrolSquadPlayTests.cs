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
    /// <summary>Squad creatures (giant dung beetles) in the debug scene: they walk their route together and fight together.</summary>
    public class PatrolSquadPlayTests
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

        EnemyDebugPreview Beetle()
        {
            var preview = sandbox.Previews.FirstOrDefault(p => p.source && p.source.name == "Giant dung beetle");
            Assert.That(preview, Is.Not.Null, "the debug scene shows the giant dung beetle");
            Assert.That(preview.source.patrol.enabled, Is.True, "the beetle comes in squads");
            return preview;
        }

        /// <summary>The navigation point furthest from <paramref name="from"/>: out of the squad's sight and hearing.</summary>
        static Vector3 FarFrom(Vector3 from)
        {
            var mesh = NavMesh.CalculateTriangulation();
            return mesh.vertices.OrderByDescending(v => (v - from).sqrMagnitude).First();
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }

        [UnityTest]
        public IEnumerator SquadWalksItsRouteTogether()
        {
            var preview = Beetle();
            var origin = preview.transform.position;
            sandbox.motor.Teleport(FarFrom(origin) + Vector3.up * .1f, Quaternion.identity);
            yield return Wait(.2f);
            var first = sandbox.Spawn(preview);
            var squad = first.Squad;
            Assert.That(squad, Is.Not.Null);
            Assert.That(squad.Members.Count, Is.EqualTo(sandbox.squadSize));
            Assert.That(squad.Route.Count, Is.GreaterThanOrEqualTo(2));
            var start = squad.Members.Select(m => m.Position).ToArray();
            int point = squad.NextPoint;
            bool passedPoint = false;
            float until = Time.time + 25;
            while (Time.time < until && !passedPoint)
            {
                yield return null;
                if (squad.NextPoint != point) passedPoint = true;
            }
            Assert.That(passedPoint, Is.True, "the squad reaches a route point, stops and walks on");
            for (int i = 0; i < squad.Members.Count; i++)
                Assert.That(Flat(squad.Members[i].Position, start[i]), Is.GreaterThan(1.5f), $"member {i} walks with the squad");
            foreach (var member in squad.Members)
            {
                Assert.That(member.OnPatrol, Is.True, $"{member.name} stays calm with nobody around: {member.State}");
                Assert.That(Flat(member.Position, squad.Leader.Position), Is.LessThan(sandbox.squadSize * squad.Settings.spacing + 3), "nobody wanders off");
            }
        }

        [UnityTest]
        public IEnumerator WholeSquadAttacksTogether()
        {
            var preview = Beetle();
            var origin = preview.transform.position;
            sandbox.motor.Teleport(FarFrom(origin) + Vector3.up * .1f, Quaternion.identity);
            yield return Wait(.2f);
            var squad = sandbox.Spawn(preview).Squad;
            yield return Wait(1f);
            // The player comes close to one of them; the others are further, some behind it.
            var leader = squad.Leader;
            var near = leader.Position + leader.transform.forward * 4;
            if (NavMesh.SamplePosition(near, out var hit, 3, NavMesh.AllAreas)) near = hit.position;
            sandbox.motor.Teleport(near + Vector3.up * .1f, Quaternion.LookRotation(leader.Position - near));
            float until = Time.time + 8;
            while (Time.time < until && squad.Members.Any(m => m.OnPatrol)) yield return null;
            foreach (var member in squad.Members)
                Assert.That(member.OnPatrol, Is.False, $"{member.name} turned on the player with the squad: {member.State}");
            until = Time.time + 15;
            while (Time.time < until && sandbox.ReceivedDamage == 0) yield return null;
            Assert.That(sandbox.ReceivedDamage, Is.GreaterThan(0), "the spear thrust lands");
            Assert.That(sandbox.ReceivedDamage, Is.GreaterThanOrEqualTo(3), "a thrust takes one and a half hearts (3 halves)");
        }
    }
}
#endif
