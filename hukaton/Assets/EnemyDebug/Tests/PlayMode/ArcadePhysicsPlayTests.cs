#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.EnemyDebug.Tests.PlayMode
{
    /// <summary>
    /// Cards throw loose things about like an arcade, not like the real world: high, far and spinning, farther at a
    /// higher level; a thing thrown into a creature hurts it; Impulse and Draft hurl the creatures themselves.
    /// </summary>
    public class ArcadePhysicsPlayTests
    {
        EnemyDebugSandbox sandbox;
        ExpeditionWorld world;
        readonly List<GameObject> made = new List<GameObject>();
        // An empty part of the arena, away from the creature showcase.
        static readonly Vector3 Spot = new Vector3(50, 0, -30);

        [UnitySetUp]
        public IEnumerator LoadSavedScene()
        {
            Time.timeScale = 1;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/EnemyDebug/Scenes/EnemyDebug.unity", new LoadSceneParameters(LoadSceneMode.Single));
            sandbox = Object.FindFirstObjectByType<EnemyDebugSandbox>();
            yield return Wait(.3f);
            world = sandbox.world;
            Assert.That(world.Running, Is.True);
            sandbox.motor.Teleport(Spot + new Vector3(0, .05f, -8), Quaternion.identity);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            foreach (var go in made) if (go) Object.Destroy(go);
            made.Clear();
            if (sandbox) sandbox.ClearLive();
            yield return null;
        }

        static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }

        static DeckPrototypeConfig Config => AssetDatabase.LoadAssetAtPath<DeckPrototypeConfig>("Assets/Game/Data/Cards.asset");

        CardCopy Copy(string id, int level) => new Expedition(new CardCopy[0], 3, 1).GrantToHand(Config.Find(id).ToSpec(), level);

        WorldCreature Beast(string name, Vector3 at)
        {
            var preview = sandbox.Previews.FirstOrDefault(p => p.source && p.source.name == name);
            Assert.That(preview, Is.Not.Null, name);
            var creature = sandbox.Spawn(preview);
            creature.Resume(creature.maximumHealth, at);
            return creature;
        }

        PhysicsProp Crate(Vector3 at)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<PhysicsProp>("Assets/Expedition/Prefabs/Movable crate.prefab");
            var crate = Object.Instantiate(prefab, at + Vector3.up * .5f, Quaternion.identity);
            made.Add(crate.gameObject);
            return crate;
        }

        static IEnumerator Settle(Rigidbody body)
        {
            float until = Time.time + 3;
            yield return new WaitForFixedUpdate();
            while (Time.time < until && body.linearVelocity.sqrMagnitude > .0004f) yield return new WaitForFixedUpdate();
        }

        [UnityTest]
        public IEnumerator AStrikeThrowsACrateHighAndSpinningAndAnUpgradedOneFarther()
        {
            var definition = Config.Find("strike");
            var speeds = new float[4];
            foreach (int level in new[] { 1, 3 })
            {
                var crate = Crate(Spot + new Vector3(level * 3, 0, 0));
                yield return Settle(crate.body);
                var collider = crate.GetComponentInChildren<Collider>();
                var flight = Vector3.forward * 18;
                world.ApplyImpact(Copy("strike", level), definition, collider, crate.body.worldCenterOfMass, -flight.normalized, flight);
                yield return new WaitForFixedUpdate();
                speeds[level] = crate.body.linearVelocity.magnitude;
                Assert.That(crate.body.linearVelocity.y, Is.GreaterThan(1), $"level {level}: up into the air");
                Assert.That(Vector3.Dot(crate.body.linearVelocity, flight.normalized), Is.GreaterThan(1), $"level {level}: along the card's flight");
                Assert.That(crate.body.angularVelocity.magnitude, Is.GreaterThan(3), $"level {level}: spinning");
            }
            Assert.That(speeds[1], Is.GreaterThan(5), "a Strike throws a crate");
            Assert.That(speeds[3], Is.GreaterThan(speeds[1] * 2), "an upgraded Strike far harder");
        }

        [UnityTest]
        public IEnumerator AThrownCrateHurtsTheCreatureItHits()
        {
            var dog = Beast("Goblin dog", Spot);
            yield return Wait(.3f);
            dog.Freeze(6);
            var target = dog.Centre;
            var crate = Crate(target + new Vector3(-2.6f, -.2f, 0));
            yield return new WaitForFixedUpdate();
            int before = dog.Health;
            Assert.That(PropPhysics.Nudge(crate.body, crate.body.worldCenterOfMass, target + Vector3.up * .2f - crate.body.worldCenterOfMass, 4, 2.6f, 0, 0), Is.True);
            float until = Time.time + 2;
            while (Time.time < until && dog.Health == before) yield return new WaitForFixedUpdate();
            Assert.That(dog.Health, Is.LessThan(before), "the crate hurts it");
        }

        [UnityTest]
        public IEnumerator ImpulseHurlsACreatureHighAndAway()
        {
            var dog = Beast("Goblin dog", Spot);
            yield return Wait(.3f);
            var at = dog.Position + new Vector3(-1.2f, 0, 0);
            world.ApplyImpact(Copy("impulse", 1), Config.Find("impulse"), null, at, Vector3.up, Vector3.down * 10);
            yield return new WaitForFixedUpdate();
            var v = dog.body.linearVelocity;
            Assert.That(v.y, Is.GreaterThan(3), "up off its feet");
            Assert.That(v.x, Is.GreaterThan(3), "and away from the blast");
        }

        /// <summary>A record to look at (Logs/world-plans/arcade-*.png): a pile of crates under each card, level I and III.</summary>
        [UnityTest]
        public IEnumerator PhotographCardsOnAPileOfCrates()
        {
            string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "world-plans");
            System.IO.Directory.CreateDirectory(dir);
            var cam = world.viewCamera;
            foreach (var (id, level) in new[] { ("heavy_strike", 1), ("heavy_strike", 3), ("impulse", 3), ("lightning", 3) })
            {
                var centre = Spot + new Vector3(0, 0, 6);
                var crates = new List<PhysicsProp>();
                for (int i = 0; i < 7; i++) crates.Add(Crate(centre + new Vector3((i % 3 - 1) * 1.1f, (i / 3) * 1f, (i % 2) * .4f)));
                yield return Wait(1.5f);
                var eye = centre + new Vector3(0, 3, -8);
                var ground = Physics.Raycast(centre + Vector3.up * 3, Vector3.down, out var hit, 10, ~(1 << 2), QueryTriggerInteraction.Ignore) ? hit.collider : null;
                world.ApplyImpact(Copy(id, level), Config.Find(id), crates[0].GetComponentInChildren<Collider>(), crates[0].body.worldCenterOfMass, Vector3.back, Vector3.forward * 15);
                for (int k = 0; k < 4; k++)
                {
                    yield return Wait(k == 0 ? .1f : .35f);
                    yield return Photo(cam, dir, $"arcade-{id}-{level}-{k}", eye, centre + Vector3.up);
                }
                foreach (var c in crates) if (c) Object.Destroy(c.gameObject);
                yield return Wait(.3f);
            }
        }

        static IEnumerator Photo(Camera cam, string dir, string name, Vector3 eye, Vector3 look)
        {
            yield return new WaitForEndOfFrame();
            var saved = (cam.transform.position, cam.transform.rotation);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            var rt = new RenderTexture(640, 360, 24);
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
    }
}
#endif
