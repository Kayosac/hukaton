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
        public IEnumerator ADormantPropWakesWhenTouchedAndRestsAgain()
        {
            var crate = Crate(Spot(6, -2));
            yield return Settle(crate.body);
            // As the chunk builder leaves it: still scenery.
            crate.body.isKinematic = true;
            crate.dormant = true;
            yield return new WaitForFixedUpdate();
            var start = crate.body.position;
            int awake = PropPhysics.AwakeCount;
            Assert.That(PropPhysics.Burst(start + Vector3.left * .6f, .9f, 4), Is.GreaterThanOrEqualTo(1), "the burst wakes it and throws it");
            Assert.That(crate.body.isKinematic, Is.False);
            Assert.That(crate.Stirred, Is.True);
            Assert.That(PropPhysics.AwakeCount, Is.EqualTo(awake + 1));
            yield return new WaitForSeconds(.4f);
            Assert.That(Vector3.Distance(start, crate.body.position), Is.GreaterThan(.15f), "it moved");
            // Lying still for a while, it is still scenery again where it lies.
            float until = Time.time + 8;
            while (Time.time < until && !crate.body.isKinematic) yield return null;
            Assert.That(crate.body.isKinematic, Is.True, "at rest it is still again");
            Assert.That(PropPhysics.AwakeCount, Is.LessThanOrEqualTo(awake));
            Assert.That(PropPhysics.Ready(crate.body), Is.True, "and wakes again when pushed");
        }

        [UnityTest]
        public IEnumerator AHardBlowBreaksAPotIntoPiecesWithCoinsAndALightOneDoesNot()
        {
            var crate = Crate(Spot(6, 1));
            yield return Settle(crate.body);
            var pot = Object.Instantiate(AssetDatabase.LoadAssetAtPath<BreakableKind>("Assets/Game/Data/Breakables/Clay pot.asset"));
            Assert.That(pot.shards.Length, Is.GreaterThan(0));
            pot.coinChance = 1;
            var breakable = crate.gameObject.AddComponent<Breakable>();
            breakable.kind = pot;
            Vector3 at = crate.body.worldCenterOfMass;
            // A soft nudge only moves it.
            Assert.That(PropPhysics.Nudge(crate.body, at, Vector3.forward, pot.hitSpeed * .5f), Is.True);
            Assert.That(breakable.Broken, Is.False);
            yield return Settle(crate.body);
            int coins = Object.FindObjectsByType<WorldCoin>(FindObjectsSortMode.None).Length;
            // A heavy blow breaks it: pieces fly, a coin or three fall out, the thing is gone.
            Assert.That(PropPhysics.Nudge(crate.body, at, Vector3.forward, pot.hitSpeed * 2), Is.True);
            Assert.That(breakable.Broken, Is.True);
            Assert.That(crate.gameObject.activeSelf, Is.False);
            yield return null;
            Assert.That(PropDebris.LiveCount, Is.InRange(pot.shardCount.x, PropDebris.MaxPieces));
            Assert.That(Object.FindObjectsByType<WorldCoin>(FindObjectsSortMode.None).Length - coins, Is.InRange(pot.coins.x, pot.coins.y));
            // Whole again for the next outing.
            crate.ResetForOuting();
            Assert.That(breakable.Broken, Is.False);
            Assert.That(crate.gameObject.activeSelf, Is.True);
            Object.Destroy(pot);
        }

        [UnityTest]
        public IEnumerator ABrokenHiveNowAndThenLetsOutBeesThatGoForThePlayer()
        {
            var hive = Object.Instantiate(AssetDatabase.LoadAssetAtPath<BreakableKind>("Assets/Game/Data/Breakables/Beehive.asset"));
            Assert.That(hive.release, Is.Not.Null);
            Assert.That(hive.releaseChance, Is.InRange(.05f, .95f), "now and then, not always");
            for (int round = 0; round < 2; round++)
            {
                bool out_ = round == 0;
                hive.releaseChance = out_ ? 1 : 0;
                var crate = Crate(Spot(6, round * 2 - 1));
                yield return Settle(crate.body);
                var breakable = crate.gameObject.AddComponent<Breakable>();
                breakable.kind = hive;
                int before = world.creatures.Count;
                Assert.That(PropPhysics.Nudge(crate.body, crate.body.worldCenterOfMass, Vector3.forward, hive.hitSpeed * 2), Is.True);
                Assert.That(breakable.Broken, Is.True);
                yield return null;
                if (!out_)
                {
                    Assert.That(world.creatures.Count, Is.EqualTo(before), "an empty hive");
                    continue;
                }
                Assert.That(world.creatures.Count, Is.EqualTo(before + 1), "a swarm comes out");
                var bees = world.creatures[world.creatures.Count - 1];
                // Straight for the player: chasing, or already aiming its sting when the player is that close.
                Assert.That(new[] { CreatureState.Chase, CreatureState.Aim, CreatureState.Windup, CreatureState.Strike }, Does.Contain(bees.State), "and goes for the player at once");
                Assert.That(Vector3.Distance(bees.Position, crate.transform.position), Is.LessThan(3), "out of the hive");
                // Gone with its place: it leaves the world's list by itself.
                Object.Destroy(bees.gameObject);
                yield return null;
                Assert.That(world.creatures.Count, Is.EqualTo(before));
            }
            Object.Destroy(hive);
        }

        /// <summary>A record to look at (Logs/world-plans/break-*.png): a pot and a crate breaking, not a pass/fail on looks.</summary>
        [UnityTest]
        public IEnumerator PhotographBreaking()
        {
            string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "world-plans");
            System.IO.Directory.CreateDirectory(dir);
            var cam = world.viewCamera;
            foreach (var (name, side) in new[] { ("Clay pot", -.7f), ("Crate", .7f) })
            {
                var thing = Crate(Spot(3.2f, side));
                yield return Settle(thing.body);
                var kind = AssetDatabase.LoadAssetAtPath<BreakableKind>($"Assets/Game/Data/Breakables/{name}.asset");
                thing.gameObject.AddComponent<Breakable>().kind = kind;
                if (name == "Clay pot") thing.transform.localScale *= .6f;
                yield return Settle(thing.body);
                var eye = cam.transform.position;
                var look = thing.body.worldCenterOfMass;
                yield return Photo(cam, dir, $"break-{name}-0", eye, look);
                PropPhysics.Nudge(thing.body, look, world.motor.transform.forward, kind.hitSpeed * 2);
                for (int f = 0; f < 4; f++) yield return null;
                yield return Photo(cam, dir, $"break-{name}-1", eye, look);
                yield return new WaitForSeconds(.35f);
                yield return Photo(cam, dir, $"break-{name}-2", eye, look);
                yield return new WaitForSeconds(1.2f);
                yield return Photo(cam, dir, $"break-{name}-3", eye, look);
            }
        }

        static IEnumerator Photo(Camera cam, string dir, string name, Vector3 eye, Vector3 look)
        {
            yield return new WaitForEndOfFrame();
            var saved = (cam.transform.position, cam.transform.rotation);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            var rt = new RenderTexture(960, 540, 24);
            var previous = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = previous;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
            cam.transform.SetPositionAndRotation(saved.Item1, saved.Item2);
        }

        [UnityTest]
        public IEnumerator PiecesNeverPileUpPastTheCap()
        {
            var kind = AssetDatabase.LoadAssetAtPath<BreakableKind>("Assets/Game/Data/Breakables/Crate.asset");
            var spot = Spot(5, 0);
            for (int i = 0; i < 12; i++)
            {
                PropDebris.Shatter(world, kind, new Bounds(spot + Vector3.up * .5f, Vector3.one * .6f), Vector3.forward, Vector3.zero);
                yield return null;
            }
            Assert.That(PropDebris.LiveCount, Is.LessThanOrEqualTo(PropDebris.MaxPieces));
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
