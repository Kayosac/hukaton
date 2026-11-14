#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Expeditions.Tests.PlayMode
{
    /// <summary>The chest mimic in its lab scene (Assets/ChestArt/Scenes/MimicLab.unity), with a running outing.</summary>
    public sealed class ChestMimicPlayTests
    {
        ExpeditionWorld world;

        [UnitySetUp]
        public IEnumerator LoadLab()
        {
            Time.timeScale = 1;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/ChestArt/Scenes/MimicLab.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            world = Object.FindFirstObjectByType<ExpeditionWorld>();
            Assert.That(world, Is.Not.Null);
            float until = Time.time + 3;
            while (!world.Running && Time.time < until) yield return null;
            Assert.That(world.Running, "the lab opens an outing by itself");
        }

        static ChestMimic Lab(int rank) =>
            Object.FindObjectsByType<ChestMimic>(FindObjectsSortMode.None).First(m => m.chest.tier.rank == rank);

        /// <summary>A fresh mimic of the given rank, as a world would spawn it.</summary>
        ChestMimic Spawn(int rank, Vector3 at)
        {
            string name = new[] { "Mimic 1 Beggar", "Mimic 2 Common", "Mimic 3 Sturdy", "Mimic 4 Rich", "Mimic 5 Legendary" }[rank - 1];
            var prefab = AssetDatabase.LoadAssetAtPath<TreasureChest>("Assets/Expedition/Prefabs/Chests/Mimics/" + name + ".prefab");
            var chest = Object.Instantiate(prefab, at, Quaternion.identity);
            chest.world = world;
            return chest.GetComponent<ChestMimic>();
        }

        void StandInFront(ChestMimic mimic, float distance)
        {
            Vector3 at = mimic.transform.position - mimic.transform.forward * distance;
            world.motor.Teleport(at, Quaternion.LookRotation(mimic.transform.forward));
        }

        static IEnumerator Seconds(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }

        [UnityTest]
        public IEnumerator FrozenMimicOpensWithoutABiteForThePlainPayout()
        {
            var mimic = Lab(2);
            StandInFront(mimic, 1.8f);
            yield return null;
            mimic.Freeze(10);
            Assert.That(mimic.Frozen && mimic.chest.Available, "frozen it can be aimed at and opened");
            Assert.That(mimic.chest.Open(), Is.True);
            yield return Seconds(2.5f);
            Assert.That(mimic.Bites, Is.Zero);
            Assert.That(mimic.Current, Is.EqualTo(ChestMimic.State.Opened));
            Assert.That(mimic.chest.Opened, Is.True);
            Assert.That(mimic.chest.LastPayout, Is.InRange(mimic.chest.tier.coins.x, mimic.chest.tier.coins.y));
            Assert.That(mimic.Alive, Is.False, "an opened mimic is spent");
        }

        [UnityTest]
        public IEnumerator OpeningItAwakeEndsInABiteAndItRunsOffWithinItsCircle()
        {
            var mimic = Lab(3);
            mimic.hideAfter = new Vector2(1.5f, 1.5f);
            StandInFront(mimic, 1.8f);
            yield return null;
            Vector3 start = mimic.transform.position;
            Assert.That(mimic.chest.Open(), Is.True, "E is taken by the mimic");
            Assert.That(mimic.chest.Opened, Is.False, "it does not open");
            yield return null;
            Assert.That(mimic.Busy && !mimic.chest.Available, "warning: no prompt while it attacks");
            yield return Seconds(mimic.warnSeconds + .4f);
            Assert.That(mimic.Bites, Is.EqualTo(1), "the bite lands on a player who stayed");
            yield return Seconds(1.5f);
            Assert.That(Vector3.Distance(mimic.transform.position, start), Is.GreaterThan(1), "it ran off");
            Assert.That(Flat(mimic.transform.position - mimic.Home).magnitude, Is.LessThanOrEqualTo(mimic.leash));
            Assert.That(mimic.Current, Is.EqualTo(ChestMimic.State.Alert));

            // It plays chest again nearby, on guard: the next E is another bite.
            float until = Time.time + 6;
            while (mimic.Busy && Time.time < until) yield return null;
            Assert.That(mimic.Busy, Is.False, "plays chest again after a while");
            Assert.That(mimic.chest.Available, Is.True);
            StandInFront(mimic, 1.8f);
            yield return null;
            Assert.That(mimic.chest.Open(), Is.True);
            yield return Seconds(mimic.warnSeconds + .4f);
            Assert.That(mimic.Bites, Is.EqualTo(2));
            Assert.That(mimic.chest.Opened, Is.False);
        }

        [UnityTest]
        public IEnumerator UnderTheCrowsItNeverBites()
        {
            var mimic = Lab(3);
            StandInFront(mimic, 1.8f);
            yield return null;
            // Карта «Вороньё» сажает на мимика стаю (как CrowsEffect: 1 HP в секунду, 4 с).
            var effect = AssetDatabase.LoadAssetAtPath<Hakaton.Expeditions.Cards.CrowsEffect>("Assets/Game/Data/Effects/Crows.asset");
            Assert.That(effect && effect.flockPrefab, Is.True);
            var flock = world.SpawnForOuting(effect.flockPrefab, mimic.Centre, Quaternion.identity);
            flock.Attach(mimic, 1, 4, Time.time);
            Assert.That(mimic.Distracted, Is.True, "вороны отвлекли мимика");
            Assert.That(mimic.chest.Open(), Is.True, "E по сундуку");
            yield return Seconds(mimic.warnSeconds + .6f);
            Assert.That(mimic.Bites, Is.Zero, "под воронами не кусает — удирает");
            StandInFront(mimic, 1);
            yield return Seconds(1.5f);
            Assert.That(mimic.Bites, Is.Zero, "и вплотную тоже");
            Object.Destroy(flock.gameObject);
        }

        [UnityTest]
        public IEnumerator KilledItPaysMoreThanAPlainChestOfItsTier()
        {
            foreach (int rank in new[] { 1, 4 })
            {
                var mimic = Lab(rank);
                world.motor.Teleport(mimic.transform.position + new Vector3(0, 0, -12), Quaternion.identity);
                yield return null;
                Assert.That(mimic.TakeDamage(new DamageInfo(1000, Vector3.zero, mimic.Centre)), Is.True);
                Assert.That(mimic.Current, Is.EqualTo(ChestMimic.State.Dead));
                yield return Seconds(.8f);
                Assert.That(mimic.chest.Opened, Is.True);
                Assert.That(mimic.chest.LastPayout, Is.GreaterThan(mimic.chest.tier.coins.y), "more than any plain chest of rank " + rank);
                Assert.That(mimic.Bites, Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator CardsReachItLikeACreature()
        {
            var mimic = Lab(5);
            var fighters = new System.Collections.Generic.List<ICombatant>();
            world.Combatants(fighters);
            Assert.That(fighters, Does.Contain(mimic), "lightning and tar find it among the fighters");
            Assert.That(Combat.Of(mimic.chest.lid.Lid.GetComponent<Collider>()), Is.EqualTo(mimic), "a card hitting the lid hits the mimic");
            yield return null;
        }

        [UnityTest]
        public IEnumerator EveryStateComesBackAsItWasLeft()
        {
            Vector3 home = new Vector3(20, 0, 20);
            foreach (ChestMimic.State state in System.Enum.GetValues(typeof(ChestMimic.State)))
            {
                var mimic = Spawn(2, home);
                yield return null;
                var spot = home + new Vector3(3, 0, 1);
                mimic.Restore(new ChestMimic.Snapshot { state = state, position = spot, rotation = Quaternion.Euler(0, 40, 0), home = home });
                yield return null;
                Assert.That(mimic.Current, Is.EqualTo(state), state.ToString());
                Assert.That(mimic.Home, Is.EqualTo(home));
                var back = mimic.Capture();
                Assert.That(back.state, Is.EqualTo(state));
                switch (state)
                {
                    case ChestMimic.State.Closed:
                    case ChestMimic.State.Alert:
                        Assert.That(mimic.chest.Opened, Is.False);
                        Assert.That(mimic.Alive, Is.True);
                        Assert.That(Flat(mimic.transform.position - spot).magnitude, Is.LessThan(.01f), "stands where it was left");
                        break;
                    case ChestMimic.State.Dead:
                    case ChestMimic.State.Opened:
                        Assert.That(mimic.chest.Opened, Is.True, "nothing left inside");
                        Assert.That(mimic.Alive, Is.False);
                        Assert.That(mimic.chest.Open(), Is.False);
                        break;
                }
                Object.Destroy(mimic.gameObject);
                yield return null;
            }

            // The plain chest path a world already uses: SetEmpty gives a spent mimic, ResetForOuting a fresh one.
            var reused = Spawn(1, home);
            yield return null;
            reused.chest.SetEmpty();
            Assert.That(reused.Current, Is.EqualTo(ChestMimic.State.Opened));
            reused.chest.ResetForOuting();
            Assert.That(reused.Current, Is.EqualTo(ChestMimic.State.Closed));
            Assert.That(reused.Alive && reused.chest.Available, Is.True);
            Object.Destroy(reused.gameObject);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
    }
}
#endif
