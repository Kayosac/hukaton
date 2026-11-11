#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Hakaton.Expeditions.Cards;
using Hakaton.Expeditions.Events;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hakaton.Expeditions.Tests.PlayMode
{
    /// <summary>The beast brawl event in its lab scene (Assets/Expedition/Scenes/BeastBrawlLab.unity, uneven ground, baked navigation).</summary>
    public sealed class BeastBrawlPlayTests
    {
        const string PrefabPath = "Assets/Expedition/Prefabs/Events/Beast brawl.prefab";
        // The far end of the lab's uneven ground, away from its two showcase brawls.
        static readonly Vector3 Spot = new Vector3(0, 0, -78);
        const int DogsAndRats = 0, RoosterAndRats = 1, BeesAndBushes = 2;

        ExpeditionWorld world;
        readonly List<BeastBrawl> made = new List<BeastBrawl>();

        [UnitySetUp]
        public IEnumerator LoadLab()
        {
            Time.timeScale = 1;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Expedition/Scenes/BeastBrawlLab.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            world = Object.FindFirstObjectByType<ExpeditionWorld>();
            Assert.That(world, Is.Not.Null);
            float until = Time.time + 3;
            while (!world.Running && Time.time < until) yield return null;
            Assert.That(world.Running, "the lab opens an outing by itself");
            // The showcase brawls stay out of the tests.
            foreach (var showcase in Object.FindObjectsByType<BeastBrawl>(FindObjectsSortMode.None)) Object.Destroy(showcase.gameObject);
            made.Clear();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            foreach (var brawl in made) if (brawl) Object.Destroy(brawl.gameObject);
            yield return null;
        }

        BeastBrawl Make(BeastBrawl.Variant variant, int pairing, int seed, Vector3 at)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<BeastBrawl>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            var brawl = Object.Instantiate(prefab, at, Quaternion.identity);
            brawl.world = world;
            brawl.variant = variant;
            brawl.pairing = pairing;
            brawl.Spawn(seed);
            made.Add(brawl);
            return brawl;
        }

        BeastBrawl Restored(BeastBrawl.Variant variant, BeastBrawl.Save save, Vector3 at)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<BeastBrawl>(PrefabPath);
            var brawl = Object.Instantiate(prefab, at, Quaternion.identity);
            brawl.world = world;
            brawl.variant = variant;
            // As a world would keep it: a plain serialized copy.
            brawl.Restore(JsonUtility.FromJson<BeastBrawl.Save>(JsonUtility.ToJson(save)));
            made.Add(brawl);
            return brawl;
        }

        static Vector3 OnGround(Vector3 at)
        {
            if (Physics.Raycast(at + Vector3.up * 10, Vector3.down, out var hit, 20, ~(1 << 2), QueryTriggerInteraction.Ignore)) at.y = hit.point.y;
            return at;
        }

        /// <summary>The player stands <paramref name="distance"/> metres south of the brawl, looking at it.</summary>
        void PlayerAt(BeastBrawl brawl, float distance)
        {
            Vector3 at = OnGround(brawl.Centre + Vector3.forward * distance);
            world.motor.Teleport(at + Vector3.up * .1f, Quaternion.LookRotation(-Vector3.forward));
        }

        /// <summary>The player well away from the test spot (a teleport lands on the next frame).</summary>
        IEnumerator PlayerAway()
        {
            Vector3 at = OnGround(Spot + Vector3.forward * 45);
            world.motor.Teleport(at + Vector3.up * .1f, Quaternion.LookRotation(-Vector3.forward));
            yield return null;
            yield return null;
        }

        static IEnumerator Seconds(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }

        static IEnumerable<WorldCreature> Alive(BeastBrawl brawl) => brawl.Beasts.Where(b => b && b.gameObject.activeSelf && b.Alive);

        static float Flat(Vector3 v) { v.y = 0; return v.magnitude; }

        /// <summary>Runs a fight to its end, checking every frame that the beasts stay in the circle while brawling.</summary>
        IEnumerator FightToTheEnd(BeastBrawl brawl, float timeout)
        {
            float until = Time.time + timeout;
            float furthest = 0;
            while (brawl.Current != BeastBrawl.Phase.Over && Time.time < until)
            {
                foreach (var beast in Alive(brawl))
                    if (beast.Brawling) furthest = Mathf.Max(furthest, Flat(beast.Position - brawl.Centre));
                yield return null;
            }
            Assert.That(brawl.Current, Is.EqualTo(BeastBrawl.Phase.Over), "the fight ends");
            Assert.That(furthest, Is.LessThanOrEqualTo(brawl.arenaRadius + .3f), "the whole fight stays in the event's circle");
        }

        // ---------------- the fight ----------------

        [UnityTest]
        public IEnumerator EveryPairingFightsForAReasonableTimeAndSomebodySurvives()
        {
            int wounded = 0;
            for (int pairing = 0; pairing < 3; pairing++)
            {
                yield return PlayerAway();
                var brawl = Make(BeastBrawl.Variant.AlreadyFighting, pairing, 11 + pairing, OnGround(Spot));
                yield return Seconds(.5f);
                Assert.That(brawl.Current, Is.EqualTo(BeastBrawl.Phase.NotStarted), "nobody saw it start; started by " + brawl.StartedBy);
                Assert.That(brawl.Beasts.All(b => b.Posturing), "until the player comes near the packs glare at each other");
                PlayerAt(brawl, 16);
                yield return null;
                yield return null;
                Assert.That(brawl.Current, Is.EqualTo(BeastBrawl.Phase.Fighting), "it starts within 20 m of the player");
                yield return FightToTheEnd(brawl, 70);
                string pair = brawl.pairings[pairing].name;
                Debug.Log($"[brawl] {pair}: {brawl.FightSeconds:0.0} s, survivors {brawl.AliveIn(0)} + {brawl.AliveIn(1)}");
                Assert.That(brawl.FightSeconds, Is.InRange(8f, 40f), pair + ": the fight lasts a while, but not forever");
                Assert.That(brawl.AliveIn(0) + brawl.AliveIn(1), Is.GreaterThan(0), pair + ": somebody survives");
                Assert.That(Alive(brawl).All(b => !b.Brawling), pair + ": the survivors are back to their usual selves");
                int winners = brawl.AliveIn(0) > 0 ? 0 : 1;
                if (brawl.Pack(winners).Any(b => !b.Alive || b.Health < b.maximumHealth)) wounded++;
                Object.Destroy(brawl.gameObject);
                yield return null;
            }
            // The winners usually come out of it wounded or short of a member: weak enough to finish off.
            Assert.That(wounded, Is.GreaterThanOrEqualTo(2), "the winners are usually wounded");
        }

        [UnityTest]
        public IEnumerator TheFallenDropTheirLoot()
        {
            var brawl = Make(BeastBrawl.Variant.AlreadyFighting, DogsAndRats, 5, OnGround(Spot));
            int lootBefore = Object.FindObjectsByType<WorldLoot>(FindObjectsSortMode.None).Length;
            PlayerAt(brawl, 16);
            yield return FightToTheEnd(brawl, 70);
            yield return Seconds(2.5f);
            int fallen = brawl.Beasts.Count(b => !b.Alive);
            int loot = Object.FindObjectsByType<WorldLoot>(FindObjectsSortMode.None).Length - lootBefore;
            Assert.That(fallen, Is.GreaterThan(0));
            // Every one of them rolls a table that drops at least one item.
            Assert.That(loot, Is.GreaterThanOrEqualTo(fallen), "each fallen beast left its loot");
        }

        [UnityTest]
        public IEnumerator ALureBetweenTwoCalmPacksStartsTheFight()
        {
            var brawl = Make(BeastBrawl.Variant.Calm, RoosterAndRats, 3, OnGround(Spot));
            PlayerAt(brawl, 40);
            yield return Seconds(3);
            Assert.That(brawl.Current, Is.EqualTo(BeastBrawl.Phase.NotStarted), "calm packs leave each other alone");
            Assert.That(brawl.Beasts.Any(b => b.Brawling), Is.False);

            var lure = AssetDatabase.LoadAssetAtPath<LureNoise>("Assets/Game/Prefabs/Cards/Lure/Lure.prefab");
            var noise = world.SpawnForOuting(lure, brawl.Centre, Quaternion.identity);
            noise.Setup(12, 10);
            float until = Time.time + 12;
            while (brawl.Current == BeastBrawl.Phase.NotStarted && Time.time < until) yield return null;
            Assert.That(brawl.Current, Is.EqualTo(BeastBrawl.Phase.Fighting), "both packs come to the noise and go for each other");
            yield return Seconds(brawl.standoffSeconds + .5f);
            Assert.That(Alive(brawl).Count(b => b.Brawling && b.Target is WorldCreature), Is.GreaterThanOrEqualTo(2), "beasts fight beasts");
        }

        [UnityTest]
        public IEnumerator AHitFromTheOtherPacksSideIsBlamedOnIt()
        {
            var brawl = Make(BeastBrawl.Variant.Calm, RoosterAndRats, 9, OnGround(Spot));
            yield return null;
            var rooster = brawl.Pack(0).First();
            var rat = brawl.Pack(1).OrderBy(r => Flat(r.Position - rooster.Position)).First();
            // The player behind the rats, throwing at the rooster across them.
            Vector3 across = rat.Position - rooster.Position; across.y = 0;
            Vector3 behind = OnGround(rat.Position + across.normalized * 12);
            world.motor.Teleport(behind + Vector3.up * .1f, Quaternion.LookRotation(-across));
            yield return null;
            rooster.Damage(1, -across.normalized * 2, rooster.Centre, 0);
            yield return null;
            Assert.That(brawl.Current, Is.EqualTo(BeastBrawl.Phase.Fighting), "the rooster blames the rats");
        }

        [UnityTest]
        public IEnumerator ABeastThePlayerHitsOrWalksUpToTurnsOnThePlayer()
        {
            var brawl = Make(BeastBrawl.Variant.AlreadyFighting, DogsAndRats, 7, OnGround(Spot));
            PlayerAt(brawl, 16);
            yield return Seconds(brawl.standoffSeconds + 1);
            Assert.That(brawl.Current, Is.EqualTo(BeastBrawl.Phase.Fighting));
            Assert.That(Alive(brawl).All(b => !ReferenceEquals(b.Target, world.Player)), "while they fight the player is ignored");

            // A card hit (no instigator, as cards deal it).
            var dog = brawl.Pack(0).First(b => b.Alive);
            dog.Damage(1, Vector3.zero, dog.Centre, 0);
            yield return null;
            Assert.That(brawl.AgainstPlayer(dog), "the struck beast leaves the brawl");
            Assert.That(dog.Brawling, Is.False);
            Assert.That(dog.Target, Is.SameAs(world.Player), "and goes for the player");
            yield return Seconds(.3f);
            var hunting = new[] { CreatureState.Chase, CreatureState.Windup, CreatureState.Strike, CreatureState.Recover, CreatureState.Reposition, CreatureState.Stunned, CreatureState.Search };
            Assert.That(hunting.Contains(dog.State), "hunting the player, not " + dog.State);

            // Walking right up to another one.
            var rat = Alive(brawl).First(b => b.Brawling && brawl.PackOf(b) == 1);
            Vector3 near = OnGround(rat.Position + Vector3.forward * 1.5f);
            world.motor.Teleport(near + Vector3.up * .1f, Quaternion.LookRotation(-Vector3.forward));
            yield return null;
            yield return null;
            Assert.That(brawl.AgainstPlayer(rat), "a beast the player comes within 3 m of turns on him");
            Assert.That(rat.Target, Is.SameAs(world.Player));
        }

        // ---------------- saving and restoring ----------------

        [UnityTest]
        public IEnumerator NotStartedComesBackAsItWas()
        {
            var brawl = Make(BeastBrawl.Variant.AlreadyFighting, BeesAndBushes, 21, OnGround(Spot));
            PlayerAt(brawl, 45);
            yield return Seconds(.5f);
            var save = brawl.Capture();
            Assert.That(save.phase, Is.EqualTo(BeastBrawl.Phase.NotStarted));
            int count = save.beasts.Length;
            Object.Destroy(brawl.gameObject);
            yield return null;
            Assert.That(world.creatures.Any(c => !c), Is.False, "unloading took the beasts off the world's list");

            var back = Restored(BeastBrawl.Variant.AlreadyFighting, save, OnGround(Spot));
            yield return null;
            Assert.That(back.Current, Is.EqualTo(BeastBrawl.Phase.NotStarted));
            Assert.That(back.PairingIndex, Is.EqualTo(BeesAndBushes));
            Assert.That(back.Beasts.Count(), Is.EqualTo(count));
            Assert.That(back.Beasts.All(b => b.Posturing), "still glaring, waiting for the player");
            Assert.That(back.Beasts.All(b => world.creatures.Contains(b)), "on the world's list again");
            PlayerAt(back, 16);
            yield return null;
            yield return null;
            Assert.That(back.Current, Is.EqualTo(BeastBrawl.Phase.Fighting), "and the fight still starts when the player comes");
        }

        [UnityTest]
        public IEnumerator AFightCutShortComesBackOverWithTheLiving()
        {
            var brawl = Make(BeastBrawl.Variant.AlreadyFighting, DogsAndRats, 13, OnGround(Spot));
            PlayerAt(brawl, 16);
            float until = Time.time + 40;
            while (brawl.Beasts.All(b => b.Alive) && Time.time < until) yield return null;
            Assert.That(brawl.Current, Is.EqualTo(BeastBrawl.Phase.Fighting), "mid-fight, somebody already down");
            var save = brawl.Capture();
            var living = save.beasts.Where(b => b.alive).Select(b => (b.pack, b.health)).OrderBy(b => b).ToList();
            Assert.That(save.beasts.Any(b => !b.alive));
            Object.Destroy(brawl.gameObject);
            yield return null;

            var back = Restored(BeastBrawl.Variant.AlreadyFighting, save, OnGround(Spot));
            yield return null;
            Assert.That(back.Current, Is.EqualTo(BeastBrawl.Phase.Over), "a fight cut short by unloading counts as over");
            var restored = back.Beasts.Select(b => (back.PackOf(b), b.Health)).OrderBy(b => b).ToList();
            Assert.That(restored, Is.EqualTo(living), "the same beasts are alive, with the same health");
            Assert.That(back.Beasts.All(b => !b.Brawling), "and they behave as usual");
            Assert.That(back.Capture().beasts.Length, Is.EqualTo(save.beasts.Length), "the fallen stay counted");
        }

        [UnityTest]
        public IEnumerator AFinishedFightComesBackOver()
        {
            var brawl = Make(BeastBrawl.Variant.Calm, DogsAndRats, 17, OnGround(Spot));
            PlayerAt(brawl, 45);
            yield return null;
            brawl.Begin();
            yield return FightToTheEnd(brawl, 70);
            var save = brawl.Capture();
            Assert.That(save.phase, Is.EqualTo(BeastBrawl.Phase.Over));
            var living = save.beasts.Where(b => b.alive).Select(b => (b.pack, b.health)).OrderBy(b => b).ToList();
            Object.Destroy(brawl.gameObject);
            yield return null;

            var back = Restored(BeastBrawl.Variant.Calm, save, OnGround(Spot));
            yield return Seconds(1);
            Assert.That(back.Current, Is.EqualTo(BeastBrawl.Phase.Over));
            Assert.That(back.Beasts.Select(b => (back.PackOf(b), b.Health)).OrderBy(b => b).ToList(), Is.EqualTo(living));
            Assert.That(back.Beasts.All(b => !b.Brawling));
        }
    }
}
#endif
