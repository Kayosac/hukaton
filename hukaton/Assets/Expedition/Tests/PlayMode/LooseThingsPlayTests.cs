using System.Collections;
using Hakaton.Decks.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>Loose things answer what happens next to them: a blow, a landing, a card (PropPhysics).</summary>
    public sealed class LooseThingsPlayTests
    {
        ExpeditionWorld world;

        [UnitySetUp]
        public IEnumerator OpenTheLab()
        {
            Time.timeScale = 1;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/ChestArt/Scenes/MimicLab.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            world = Object.FindFirstObjectByType<ExpeditionWorld>();
            Assert.That(world, Is.Not.Null);
            float until = Time.time + 3;
            while (!world.Running && Time.time < until) yield return null;
            Assert.That(world.Running, "the lab opens an outing by itself");
            Assert.That(ExpeditionWorld.Active, Is.SameAs(world));
        }

        PhysicsProp Crate(Vector3 at)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<PhysicsProp>("Assets/Expedition/Prefabs/Movable crate.prefab");
            Assert.That(prefab, Is.Not.Null);
            return Object.Instantiate(prefab, at, Quaternion.identity, world.spawnedObjects);
        }

        static IEnumerator Settle(Rigidbody body)
        {
            float until = Time.time + 3;
            yield return new WaitForFixedUpdate();
            while (Time.time < until && body.linearVelocity.sqrMagnitude > .0004f) yield return new WaitForFixedUpdate();
        }

        Vector3 Spot(float ahead, float side)
        {
            var player = world.motor.transform;
            Vector3 at = player.position + player.forward * ahead + player.right * side + Vector3.up * 2;
            return Physics.Raycast(at, Vector3.down, out var hit, 8, ~((1 << 2) | (1 << 4)), QueryTriggerInteraction.Ignore) ? hit.point + Vector3.up * .4f : at;
        }

        [UnityTest]
        public IEnumerator ABurstThrowsLooseThingsButNotCreatures()
        {
            var crate = Crate(Spot(6, 0));
            yield return Settle(crate.body);
            var start = crate.body.position;
            int moved = PropPhysics.Burst(start + Vector3.left * .6f, 2, 4);
            Assert.That(moved, Is.GreaterThanOrEqualTo(1));
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(crate.body.linearVelocity.magnitude, Is.GreaterThan(1), "the crate is thrown");
            Assert.That(crate.body.linearVelocity.x, Is.GreaterThan(0), "away from the centre");
            Assert.That(PropPhysics.IsLoose(crate.body), Is.True);
            foreach (var creature in Object.FindObjectsByType<WorldCreature>(FindObjectsSortMode.None))
                if (creature.body) Assert.That(PropPhysics.IsLoose(creature.body), Is.False, "creatures answer for themselves");
        }

        [UnityTest]
        public IEnumerator AStrikingCardKnocksTheThingItHitsAndAUtilityCardDoesNot()
        {
            var crate = Crate(Spot(6, 2));
            yield return Settle(crate.body);
            var collider = crate.GetComponentInChildren<Collider>();
            var strike = AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Game/Data/strike.asset");
            Assert.That(strike, Is.Not.Null);
            Assert.That(((CardEffect)strike.effectAsset).BodyNudge, Is.GreaterThan(0));
            Vector3 flight = world.motor.transform.forward * 18;
            world.ApplyImpact(null, strike, collider, crate.body.worldCenterOfMass, -flight.normalized, flight);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(Vector3.Dot(crate.body.linearVelocity, flight.normalized), Is.GreaterThan(1), "knocked along the card's flight");

            yield return Settle(crate.body);
            var heal = AssetDatabase.LoadAssetAtPath<CardEffect>("Assets/Game/Data/Effects/Heal.asset");
            if (heal) Assert.That(heal.BodyNudge, Is.EqualTo(0), "a card that does not strike does not knock");
        }

        [UnityTest]
        public IEnumerator AHardLandingScattersWhatLiesRoundTheFeet()
        {
            var crate = Crate(Spot(1.1f, 0));
            yield return Settle(crate.body);
            var start = crate.body.position;
            // A long drop onto the spot next to the crate.
            world.motor.Teleport(world.motor.transform.position + Vector3.up * 5, world.motor.transform.rotation);
            float until = Time.time + 4;
            yield return null;
            while (Time.time < until && !world.motor.IsGrounded) yield return null;
            Assert.That(world.motor.IsGrounded, Is.True, "landed");
            yield return new WaitForSeconds(.3f);
            Assert.That(Vector3.Distance(start, crate.body.position), Is.GreaterThan(.15f), "the crate jumped away");
        }
    }
}
