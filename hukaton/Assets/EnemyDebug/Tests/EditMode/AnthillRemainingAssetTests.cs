using System.Linq;
using Hakaton.Expeditions;
using Hakaton.Loot;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.EnemyDebug.Tests
{
    public class AnthillRemainingAssetTests
    {
        static WorldCreature Load(string enemy) => AssetDatabase.LoadAssetAtPath<WorldCreature>("Assets/Game/Prefabs/" + enemy + ".prefab");

        static void SevenSprites(WorldCreature c)
        {
            var sprites = new[] { c.view.idle, c.view.idleBack, c.view.alarm, c.view.hurt, c.view.attack, c.view.death, c.view.deathBack };
            Assert.That(sprites.All(s => s), Is.True);
            Assert.That(sprites.Select(s => s.name).Distinct().Count(), Is.EqualTo(7));
            Assert.That(c.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(c.view.transform.localScale.x, Is.EqualTo(c.size).Within(1e-5));
            foreach (var transform in c.GetComponentsInChildren<Transform>(true))
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject), Is.Zero, transform.name);
        }

        static void Loot(WorldCreature c, string[] ids, int[] prices, float[] chances, int minimum)
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<LootCatalog>("Assets/Loot/Loot catalog.asset");
            Assert.That(c.drops.atLeastOne, Is.EqualTo(minimum > 0));
            Assert.That(c.drops.minimumDrops, Is.EqualTo(Mathf.Max(1, minimum)));
            Assert.That(c.drops.entries.Length, Is.EqualTo(ids.Length));
            for (int i = 0; i < ids.Length; i++)
            {
                var drop = c.drops.entries[i];
                Assert.That(drop.item.id, Is.EqualTo(ids[i]));
                Assert.That(drop.item.price, Is.EqualTo(prices[i]));
                Assert.That(drop.item.rarity, Is.EqualTo(LootRarity.Common));
                Assert.That(drop.item.icon, Is.Not.Null);
                Assert.That(drop.item.iconHeight, Is.EqualTo(.45f).Within(1e-5));
                Assert.That(catalogue.Contains(drop.item), Is.True, ids[i]);
                Assert.That(drop.chance, Is.EqualTo(chances[i]).Within(1e-5));
                Assert.That(drop.min, Is.EqualTo(1));
                Assert.That(drop.max, Is.EqualTo(1));
            }
        }

        [Test]
        public void MachineGunHasConfirmedBurstShoveBodyAndLoot()
        {
            var c = Load("AntMachineGunCrew");
            Assert.That(c, Is.Not.Null);
            Assert.That(c.temperament, Is.EqualTo(Temperament.Aggressive));
            Assert.That(c.maximumHealth, Is.EqualTo(8));
            Assert.That(c.kick, Is.EqualTo(KickResponse.Anchored));
            Assert.That(c.body.mass, Is.EqualTo(5));
            Assert.That(c.speed, Is.EqualTo(1.2f).Within(1e-5));
            Assert.That(c.sightRange, Is.EqualTo(15));
            Assert.That(c.hearingRange, Is.EqualTo(4));
            Assert.That(c.leash, Is.EqualTo(8));
            Assert.That(c.patrol.enabled, Is.False);
            Assert.That(c.fireVulnerability, Is.Null);
            Assert.That(c.spawnSizeRange, Is.EqualTo(new Vector2(.95f, 1.05f)));
            Assert.That(c.view.height * c.size, Is.EqualTo(1.6f).Within(1e-4));
            Assert.That(c.ranged.range, Is.EqualTo(15));
            Assert.That(c.ranged.minRange, Is.EqualTo(3.5f).Within(1e-5));
            Assert.That(c.ranged.aimSeconds, Is.EqualTo(.8f).Within(1e-5));
            Assert.That(c.ranged.burst, Is.EqualTo(5));
            Assert.That(c.ranged.burstInterval, Is.EqualTo(.1f).Within(1e-5));
            Assert.That(c.ranged.cooldown, Is.EqualTo(1.8f).Within(1e-5));
            Assert.That(c.ranged.recovery, Is.EqualTo(.5f).Within(1e-5));
            Assert.That(c.ranged.closeCombat, Is.EqualTo(CloseCombat.Shove));
            Assert.That(c.ranged.shoveDamage, Is.EqualTo(1));
            Assert.That(c.windup, Is.EqualTo(.6f).Within(1e-5));
            Assert.That(c.strikeSeconds, Is.EqualTo(.2f).Within(1e-5));
            Assert.That(c.recovery, Is.EqualTo(.8f).Within(1e-5));
            Assert.That(c.recoveryOnMiss, Is.EqualTo(1.2f).Within(1e-5));
            var p = c.ranged.projectile;
            Assert.That(p.prefab, Is.Not.Null);
            Assert.That(p.path, Is.EqualTo(EnemyProjectileProfile.Path.Straight));
            Assert.That(p.speed, Is.EqualTo(20));
            Assert.That(p.radius, Is.EqualTo(.08f).Within(1e-5));
            Assert.That(p.maxDistance, Is.EqualTo(30));
            Assert.That(p.homingTurnRate, Is.Zero);
            Assert.That(p.damage, Is.EqualTo(1));
            Assert.That(p.playerStatuses, Is.Empty);
            Assert.That(p.prefab.picture.sprite, Is.Not.Null);
            Assert.That(p.prefab.spin, Is.Zero);
            SevenSprites(c);
            Loot(c, new[] { "ant_machine_gun_barrel_jacket", "ant_machine_gun_ammo_belt", "ant_machine_gun_helmet" },
                new[] { 80, 250, 140 }, new[] { .5f, .15f, .25f }, 2);
        }

        [Test]
        public void WingedAntHasConfirmedFlightDiveRotPatrolAndLoot()
        {
            var c = Load("WingedAnt");
            Assert.That(c, Is.Not.Null);
            Assert.That(c.maximumHealth, Is.EqualTo(4));
            Assert.That(c.attackHalfHearts, Is.EqualTo(3));
            Assert.That(c.flying, Is.True);
            Assert.That(c.cruiseHeight, Is.EqualTo(1.8f).Within(1e-5));
            Assert.That(c.windupClimb, Is.EqualTo(.7f).Within(1e-5));
            Assert.That(c.dash, Is.True);
            Assert.That(c.dashSpeed, Is.EqualTo(10));
            Assert.That(c.windup, Is.EqualTo(.6f).Within(1e-5));
            Assert.That(c.strikeSeconds, Is.EqualTo(.4f).Within(1e-5));
            Assert.That(c.recovery, Is.EqualTo(1).Within(1e-5));
            Assert.That(c.recoveryOnMiss, Is.EqualTo(1.5f).Within(1e-5));
            Assert.That(c.speed, Is.EqualTo(4.5f).Within(1e-5));
            Assert.That(c.attackRange, Is.EqualTo(2.4f).Within(1e-5));
            Assert.That(c.contactRange, Is.EqualTo(1.8f).Within(1e-5));
            Assert.That(c.sightRange, Is.EqualTo(10));
            Assert.That(c.hearingRange, Is.EqualTo(3));
            Assert.That(c.leash, Is.EqualTo(14));
            Assert.That(c.patrol.enabled, Is.True);
            Assert.That(c.patrol.flank, Is.False);
            Assert.That(c.fireVulnerability, Is.Not.Null);
            Assert.That(c.fireVulnerability.settings, Is.Not.Null);
            Assert.That(c.strikeStatuses.Length, Is.EqualTo(1));
            Assert.That(c.strikeStatuses[0].status.kind, Is.EqualTo(PlayerStatusKind.Rot));
            Assert.That(c.strikeStatuses[0].chance, Is.EqualTo(.15f).Within(1e-5), "Гниль with 15 % of the dives");
            Assert.That(c.strikeStatuses[0].seconds, Is.Zero);
            Assert.That(c.strikeStatuses[0].strength, Is.Zero);
            Assert.That(c.view.height * c.size, Is.EqualTo(1.4f).Within(1e-4));
            SevenSprites(c);
            Loot(c, new[] { "winged_ant_goggles" }, new[] { 120 }, new[] { .5f }, 0);
        }

        [Test]
        public void LarvaHasConfirmedSelfDestructPlayerOnlyPuddleAndLoot()
        {
            var c = Load("BubbleLarva");
            Assert.That(c, Is.Not.Null);
            Assert.That(c.maximumHealth, Is.EqualTo(2));
            Assert.That(c.selfDestruct.enabled, Is.True);
            Assert.That(c.windup, Is.EqualTo(1.5f).Within(1e-5));
            Assert.That(c.attackRange, Is.EqualTo(2.5f).Within(1e-5));
            Assert.That(c.speed, Is.EqualTo(1).Within(1e-5));
            Assert.That(c.sightRange, Is.EqualTo(15));
            Assert.That(c.hearingRange, Is.EqualTo(4));
            Assert.That(c.leash, Is.EqualTo(23));
            Assert.That(c.patrol.enabled, Is.True);
            Assert.That(c.patrol.flank, Is.True);
            Assert.That(c.fireVulnerability, Is.Not.Null);
            var p = c.selfDestruct.explosion;
            Assert.That(p, Is.Not.Null);
            Assert.That(p.damage, Is.EqualTo(2));
            Assert.That(p.splashRadius, Is.EqualTo(2.5f).Within(1e-5));
            Assert.That(p.hitsCreatures, Is.False);
            Assert.That(p.puddle, Is.Not.Null);
            Assert.That(p.puddleRadius, Is.EqualTo(2.5f).Within(1e-5));
            Assert.That(p.puddleSeconds, Is.EqualTo(6).Within(1e-5));
            Assert.That(p.puddleDamage, Is.EqualTo(1));
            Assert.That(p.puddleInterval, Is.EqualTo(1).Within(1e-5));
            Assert.That(c.view.height * c.size, Is.EqualTo(.6f).Within(1e-4));
            SevenSprites(c);
            Loot(c, new[] { "bubble_larva_bubble_sac", "bubble_larva_jelly", "bubble_larva_shed_skin" },
                new[] { 100, 40, 60 }, new[] { .25f, .6f, .45f }, 1);
        }

        [Test]
        public void QueenHasConfirmedSlamTwoWorkerSummonsAndLoot()
        {
            var c = Load("AntQueen");
            Assert.That(c, Is.Not.Null);
            Assert.That(c.maximumHealth, Is.EqualTo(20));
            Assert.That(c.attackHalfHearts, Is.EqualTo(4));
            Assert.That(c.kick, Is.EqualTo(KickResponse.Anchored));
            Assert.That(c.body.mass, Is.EqualTo(15));
            Assert.That(c.windup, Is.EqualTo(1).Within(1e-5));
            Assert.That(c.strikeSeconds, Is.EqualTo(.35f).Within(1e-5));
            Assert.That(c.recovery, Is.EqualTo(2).Within(1e-5));
            Assert.That(c.recoveryOnMiss, Is.EqualTo(2.5f).Within(1e-5));
            Assert.That(c.strikeShake, Is.EqualTo(.5f).Within(1e-5));
            Assert.That(c.speed, Is.EqualTo(.4f).Within(1e-5));
            Assert.That(c.turnRate, Is.EqualTo(120));
            Assert.That(c.attackRange, Is.EqualTo(2.4f).Within(1e-5));
            Assert.That(c.contactRange, Is.EqualTo(2.2f).Within(1e-5));
            Assert.That(c.sightRange, Is.EqualTo(20));
            Assert.That(c.hearingRange, Is.EqualTo(6));
            Assert.That(c.leash, Is.EqualTo(20));
            Assert.That(c.patrol.enabled, Is.False);
            Assert.That(c.fireVulnerability, Is.Not.Null);
            Assert.That(c.strikeStatuses.Single().status.kind, Is.EqualTo(PlayerStatusKind.Deafen));
            Assert.That(c.strikeStatuses[0].chance, Is.EqualTo(.5f).Within(1e-5), "Оглушение with half the slams");
            Assert.That(c.offspring.enabled, Is.True);
            Assert.That(c.offspring.prefab.name, Is.EqualTo("AntWorker"));
            Assert.That(c.offspring.atHealth, Is.EqualTo(new[] { .6f, .3f }));
            Assert.That(c.offspring.count, Is.EqualTo(4));
            Assert.That(c.spawnSizeRange, Is.EqualTo(Vector2.one));
            Assert.That(c.view.height * c.size, Is.EqualTo(2.8f).Within(1e-4));
            SevenSprites(c);
            Loot(c, new[] { "ant_queen_crown", "ant_queen_cape", "ant_queen_egg" },
                new[] { 500, 150, 100 }, new[] { .1f, .35f, .5f }, 1);
        }
    }
}
