#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
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
    public abstract class AnthillRemainingPlayFixture
    {
        protected EnemyDebugSandbox sandbox;
        protected readonly List<WorldLoot> madeLoot = new List<WorldLoot>();

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
            foreach (var loot in madeLoot) if (loot) Object.Destroy(loot.gameObject);
            madeLoot.Clear();
            yield return null;
        }

        protected static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }
        protected static IEnumerator Until(System.Func<bool> ready, float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until && !ready()) yield return null;
        }

        protected EnemyDebugPreview Preview(string enemy) => sandbox.Previews.First(p => p.source && p.source.name == enemy);

        protected WorldCreature Spawn(string enemy)
        {
            var preview = Preview(enemy);
            Vector3 at = preview.transform.position + Vector3.back * 3;
            Assert.That(NavMesh.SamplePosition(at, out var hit, 3, NavMesh.AllAreas), Is.True);
            var creature = Object.Instantiate(preview.source, hit.position, preview.transform.rotation, sandbox.liveRoot);
            creature.world = sandbox.world;
            creature.ResetForOuting(new System.Random(17));
            sandbox.world.creatures.Clear();
            sandbox.world.creatures.Add(creature);
            return creature;
        }

        protected void Stand(Vector3 at)
        {
            sandbox.motor.Teleport(at + Vector3.up * .05f, Quaternion.identity);
            Physics.SyncTransforms();
        }

        protected void StandOnGroundNear(WorldCreature creature, Vector3 offset)
        {
            Vector3 at = creature.Position + offset;
            Assert.That(NavMesh.SamplePosition(at, out var hit, 5, NavMesh.AllAreas), Is.True);
            Stand(hit.position);
        }

        protected void CollectNewLoot(List<WorldLoot> before) =>
            madeLoot.AddRange(Object.FindObjectsByType<WorldLoot>(FindObjectsSortMode.None).Except(before));
    }

    public class AntMachineGunCrewPlayTests : AnthillRemainingPlayFixture
    {
        [UnityTest]
        public IEnumerator BurstFiresHalfHeartBulletsAndDeathDropsAtLeastTwoCopies()
        {
            var crew = Spawn("AntMachineGunCrew");
            StandOnGroundNear(crew, Vector3.back * 10);
            yield return Until(() => sandbox.ReceivedDamage > 0, 9);
            Assert.That(crew.ranged.burst, Is.EqualTo(5));
            Assert.That(sandbox.ReceivedDamage, Is.EqualTo(1), "One bullet takes half a heart; later bullets in the burst meet the player's 0.75 s hit protection");
            StandOnGroundNear(crew, Vector3.back * 25);

            var rolled = new List<Hakaton.Loot.LootItem>();
            crew.drops.Roll(() => .99f, rolled);
            Assert.That(rolled.Count, Is.EqualTo(2), "minimumDrops adds two arbitrary copies, repeats allowed");

            var before = Object.FindObjectsByType<WorldLoot>(FindObjectsSortMode.None).ToList();
            crew.Damage(crew.maximumHealth, Vector3.zero, crew.Centre, 0);
            yield return Wait(crew.corpseSeconds + 1.6f);
            CollectNewLoot(before);
            Assert.That(madeLoot.Count, Is.GreaterThanOrEqualTo(2));
            var accepted = new[] { "ant_machine_gun_barrel_jacket", "ant_machine_gun_ammo_belt", "ant_machine_gun_helmet" };
            foreach (var loot in madeLoot) Assert.That(accepted, Contains.Item(loot.item.id));
            LogAssert.NoUnexpectedReceived();
        }
    }

    public class WingedAntPlayTests : AnthillRemainingPlayFixture
    {
        [UnityTest]
        public IEnumerator FlyingDiveTakesOneAndHalfHeartsAndRotChanceIsFifteenPercent()
        {
            var ant = Spawn("WingedAnt");
            yield return Wait(.35f);
            Assert.That(ant.flying, Is.True);
            Assert.That(ant.body.useGravity, Is.False);
            StandOnGroundNear(ant, Vector3.back * 1.6f);
            yield return Until(() => sandbox.ReceivedDamage > 0, 9);
            Assert.That(sandbox.ReceivedDamage, Is.EqualTo(3));
            sandbox.world.statuses.Remove(PlayerStatusKind.Rot);
            int applied = StatusInflict.ApplyAll(ant.strikeStatuses, sandbox.world.statuses, () => .14f);
            Assert.That(applied, Is.EqualTo(1));
            Assert.That(sandbox.world.statuses.Has(PlayerStatusKind.Rot), Is.True);
            sandbox.world.statuses.Remove(PlayerStatusKind.Rot);
            applied = StatusInflict.ApplyAll(ant.strikeStatuses, sandbox.world.statuses, () => .15f);
            Assert.That(applied, Is.Zero);

            var rolled = new List<Hakaton.Loot.LootItem>();
            ant.drops.Roll(() => .49f, rolled);
            Assert.That(rolled.Count, Is.EqualTo(1));
            rolled.Clear();
            ant.drops.Roll(() => .5f, rolled);
            Assert.That(rolled, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator WingedAntSpawnsAsPatrolWithoutFlanking()
        {
            var preview = Preview("WingedAnt");
            Stand(preview.transform.position + Vector3.back * 30);
            var first = sandbox.Spawn(preview);
            Assert.That(first.Squad, Is.Not.Null);
            Assert.That(first.Squad.Settings.flank, Is.False);
            Assert.That(first.Squad.Members.Count, Is.EqualTo(sandbox.squadSize));
            foreach (var member in first.Squad.Members) Assert.That(member.flying, Is.True);
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }
    }

    public class BubbleLarvaPlayTests : AnthillRemainingPlayFixture
    {
        [UnityTest]
        public IEnumerator FuseKillsLarvaDealsOneHeartThenPuddleTicksAndDropsLoot()
        {
            var larva = Spawn("BubbleLarva");
            var rolled = new List<Hakaton.Loot.LootItem>();
            larva.drops.Roll(() => .99f, rolled);
            Assert.That(rolled.Count, Is.EqualTo(1), "The larva table guarantees one copy");
            var before = Object.FindObjectsByType<WorldLoot>(FindObjectsSortMode.None).ToList();
            StandOnGroundNear(larva, Vector3.back * 2);
            yield return Until(() => larva.State == CreatureState.Windup, 5);
            Assert.That(larva.State, Is.EqualTo(CreatureState.Windup));
            Assert.That(larva.windup, Is.EqualTo(1.5f).Within(1e-5));
            yield return Until(() => sandbox.ReceivedDamage >= 3, 5);
            Assert.That(sandbox.ReceivedDamage, Is.EqualTo(3), "One heart blast plus one half-heart puddle tick");
            Assert.That(larva.Alive, Is.False);
            var puddle = Object.FindObjectsByType<EnemyPuddle>(FindObjectsSortMode.None).FirstOrDefault(p => p.Holds(sandbox.motor.transform.position));
            Assert.That(puddle, Is.Not.Null);
            Assert.That(puddle.Radius, Is.EqualTo(2.5f).Within(1e-5));
            StandOnGroundNear(larva, Vector3.back * 25);
            yield return Wait(larva.corpseSeconds + 2f);
            CollectNewLoot(before);
            var accepted = new[] { "bubble_larva_bubble_sac", "bubble_larva_jelly", "bubble_larva_shed_skin" };
            Assert.That(madeLoot.Count, Is.GreaterThanOrEqualTo(1), "Active world loot: " + string.Join(", ", Object.FindObjectsByType<WorldLoot>(FindObjectsSortMode.None).Select(l => l.item ? l.item.id : "null")));
            foreach (var loot in madeLoot) Assert.That(accepted, Contains.Item(loot.item.id));
            LogAssert.NoUnexpectedReceived();
        }
    }

    public class AntQueenPlayTests : AnthillRemainingPlayFixture
    {
        [UnityTest]
        public IEnumerator SlamTakesTwoHeartsAndDeafensHalfTheTime()
        {
            var queen = Spawn("AntQueen");
            StandOnGroundNear(queen, Vector3.back * 1.8f);
            yield return Until(() => sandbox.ReceivedDamage > 0, 8);
            Assert.That(sandbox.ReceivedDamage, Is.EqualTo(4));
            // Оглушение comes with half the slams: whatever this one rolled, the configured chance is checked exactly.
            sandbox.world.statuses.Remove(PlayerStatusKind.Deafen);
            Assert.That(StatusInflict.ApplyAll(queen.strikeStatuses, sandbox.world.statuses, () => .49f), Is.EqualTo(1));
            Assert.That(sandbox.world.statuses.Has(PlayerStatusKind.Deafen), Is.True);
            Assert.That(sandbox.world.statuses.Remaining(PlayerStatusKind.Deafen), Is.InRange(4.5f, 5.01f));
            sandbox.world.statuses.Remove(PlayerStatusKind.Deafen);
            Assert.That(StatusInflict.ApplyAll(queen.strikeStatuses, sandbox.world.statuses, () => .5f), Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator HealthThresholdsSummonFourWorkersTwiceAndDeathDropsLoot()
        {
            var queen = Spawn("AntQueen");
            StandOnGroundNear(queen, Vector3.back * 25);
            var before = Object.FindObjectsByType<WorldLoot>(FindObjectsSortMode.None).ToList();
            queen.Damage(9, Vector3.zero, queen.Centre, 0);
            yield return Wait(.3f);
            Assert.That(queen.Health, Is.EqualTo(11));
            Assert.That(sandbox.world.creatures.Count(c => c && c.name.StartsWith("AntWorker")), Is.EqualTo(4));
            queen.Damage(6, Vector3.zero, queen.Centre, 0);
            yield return Wait(.3f);
            Assert.That(queen.Health, Is.EqualTo(5));
            Assert.That(sandbox.world.creatures.Count(c => c && c.name.StartsWith("AntWorker")), Is.EqualTo(8));
            queen.Damage(1, Vector3.zero, queen.Centre, 0);
            yield return Wait(.2f);
            Assert.That(sandbox.world.creatures.Count(c => c && c.name.StartsWith("AntWorker")), Is.EqualTo(8), "Only two thresholds, at most eight workers");
            queen.Damage(queen.maximumHealth, Vector3.zero, queen.Centre, 0);
            yield return Wait(queen.corpseSeconds + 1.6f);
            CollectNewLoot(before);
            Assert.That(madeLoot.Count, Is.GreaterThanOrEqualTo(1));
            var accepted = new[] { "ant_queen_crown", "ant_queen_cape", "ant_queen_egg" };
            foreach (var loot in madeLoot) Assert.That(accepted, Contains.Item(loot.item.id));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
#endif
