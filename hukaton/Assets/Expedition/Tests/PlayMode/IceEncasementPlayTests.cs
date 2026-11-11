#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Expeditions.Tests.PlayMode
{
    /// <summary>The ice shell rules on a stand-in body, then on a real chest mimic in its lab scene.</summary>
    public sealed class IceEncasementPlayTests
    {
        public sealed class IceFakeFreezable : MonoBehaviour, IFreezable
        {
            public float frozenFor;
            public int thaws;
            public void Freeze(float seconds) => frozenFor = Mathf.Max(frozenFor, seconds);
            public void Thaw() { frozenFor = 0; thaws++; }
        }

        GameObject worldObject, bodyObject;
        ExpeditionWorld world;
        IceFakeFreezable body;

        [SetUp]
        public void SetUp()
        {
            // Inactive: a bare world must not wake (it has no workshop); the shell only needs it to track statuses.
            worldObject = new GameObject("World");
            worldObject.SetActive(false);
            world = worldObject.AddComponent<ExpeditionWorld>();
            bodyObject = new GameObject("Body");
            body = bodyObject.AddComponent<IceFakeFreezable>();
        }

        [TearDown]
        public void TearDown()
        {
            if (bodyObject) Object.DestroyImmediate(bodyObject);
            if (worldObject) Object.DestroyImmediate(worldObject);
        }

        [Test]
        public void OnlyAPhysicalHitDoublesAgainstTheShell()
        {
            Assert.That(IceEncasement.DamageAgainstShell(3, DamageKind.Hit), Is.EqualTo(6));
            Assert.That(IceEncasement.DamageAgainstShell(3, DamageKind.Fire), Is.EqualTo(3));
            Assert.That(IceEncasement.DamageAgainstShell(3, DamageKind.Lightning), Is.EqualTo(3));
        }

        [Test]
        public void EncasingFreezesWithoutATimerAndOnlyOnce()
        {
            Assert.That(IceEncasement.TryEncase(world, body), Is.True);
            Assert.That(IceEncasement.IsEncased(body), Is.True);
            Assert.That(body.frozenFor, Is.GreaterThanOrEqualTo(IceEncasement.PermanentSeconds));
            Assert.That(IceEncasement.TryEncase(world, body), Is.False, "Already encased.");
        }

        [Test]
        public void PhysicalHitShattersAndLandsDouble()
        {
            IceEncasement.TryEncase(world, body);
            int dealt = IceEncasement.ResolveHit(body, 4, DamageKind.Hit, Vector3.forward, Vector3.zero, 20);
            Assert.That(dealt, Is.EqualTo(8));
            Assert.That(IceEncasement.IsEncased(body), Is.False);
            Assert.That(body.thaws, Is.EqualTo(1));
            Assert.That(IceEncasement.ResolveHit(body, 4, DamageKind.Hit, Vector3.forward, Vector3.zero, 20), Is.EqualTo(4), "No shell, no bonus.");
        }

        [Test]
        public void FireStartsTheMelt()
        {
            IceEncasement.TryEncase(world, body);
            int dealt = IceEncasement.ResolveHit(body, 2, DamageKind.Fire, Vector3.zero, Vector3.zero, 20);
            Assert.That(dealt, Is.EqualTo(2));
            var shell = bodyObject.GetComponent<IceEncasement>();
            Assert.That(shell.Melting, Is.True);
            Assert.That(IceEncasement.TryEncase(world, body), Is.True, "Ice on a melting shell freezes it over again.");
            Assert.That(shell.Melting, Is.False);
        }

        [Test]
        public void LightningPassesUnlessItKills()
        {
            IceEncasement.TryEncase(world, body);
            IceEncasement.ResolveHit(body, 2, DamageKind.Lightning, Vector3.zero, Vector3.zero, 20);
            Assert.That(IceEncasement.IsEncased(body), Is.True);
            IceEncasement.ResolveHit(body, 5, DamageKind.Lightning, Vector3.zero, Vector3.zero, 5);
            Assert.That(IceEncasement.IsEncased(body), Is.False, "A killing blow breaks the shell.");
        }

        [Test]
        public void FireNearbyMeltsAndOutingEndFrees()
        {
            IceEncasement.TryEncase(world, body);
            Assert.That(IceEncasement.MeltAround(world, null, bodyObject.transform.position + Vector3.right, 2), Is.True);
            IceEncasement.TryEncase(world, body);
            bodyObject.GetComponent<IceEncasement>().ClearStatus();
            Assert.That(IceEncasement.IsEncased(body), Is.False);
            Assert.That(body.frozenFor, Is.EqualTo(0));
        }

        // ---------------- on a real mimic ----------------

        static IEnumerator Lab()
        {
            Time.timeScale = 1;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/ChestArt/Scenes/MimicLab.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var lab = Object.FindFirstObjectByType<ExpeditionWorld>();
            float until = Time.time + 3;
            while (lab && !lab.Running && Time.time < until) yield return null;
        }

        static IEnumerator Seconds(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }

        [UnityTest]
        public IEnumerator EncasedMimicWearsAShellAndOpensSafely()
        {
            yield return Lab();
            var lab = Object.FindFirstObjectByType<ExpeditionWorld>();
            var mimic = Object.FindObjectsByType<ChestMimic>(FindObjectsSortMode.None).First(m => m.chest.tier.rank == 2);
            lab.motor.Teleport(mimic.transform.position - mimic.transform.forward * 1.8f, Quaternion.LookRotation(mimic.transform.forward));
            yield return null;
            Assert.That(IceEncasement.TryEncase(lab, mimic), Is.True);
            var shell = mimic.GetComponent<IceEncasement>();
            Assert.That(shell.Shell, Is.Not.Null, "the kit gives the shell a mesh (Expedition Effects → Ice Shell Kit)");
            yield return Seconds(1);
            Assert.That(mimic.Frozen, Is.True, "an ice shell has no timer");
            Assert.That(mimic.chest.Open(), Is.True);
            yield return Seconds(2);
            Assert.That(mimic.Bites, Is.Zero);
            Assert.That(IceEncasement.IsEncased(mimic), Is.False, "opening cracks the shell off");
        }

        [UnityTest]
        public IEnumerator StrikeOnAnEncasedMimicShattersForDoubleDamage()
        {
            yield return Lab();
            var lab = Object.FindFirstObjectByType<ExpeditionWorld>();
            var mimic = Object.FindObjectsByType<ChestMimic>(FindObjectsSortMode.None).OrderByDescending(m => m.Health).First();
            lab.motor.Teleport(mimic.transform.position - mimic.transform.forward * 6f, Quaternion.LookRotation(mimic.transform.forward));
            yield return null;
            IceEncasement.TryEncase(lab, mimic);
            int before = mimic.Health;
            Assume.That(before, Is.GreaterThan(2));
            mimic.TakeDamage(new DamageInfo(1, Vector3.forward, mimic.Centre));
            Assert.That(before - mimic.Health, Is.EqualTo(2));
            Assert.That(IceEncasement.IsEncased(mimic), Is.False);
            Assert.That(mimic.Frozen, Is.False, "free once the shell is gone");
            yield return null;
            Assert.That(Object.FindObjectsByType<IceShard>(FindObjectsSortMode.None).Length, Is.GreaterThan(0), "shards fly");
        }
    }
}
#endif
