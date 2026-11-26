using System.Linq;
using Hakaton.Expeditions;
using Hakaton.Loot;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.EnemyDebug.Tests
{
    public class AntPlugAssetTests
    {
        [Test]
        public void PlugHasConfirmedCombatBodyAndSevenSprites()
        {
            var creature = AssetDatabase.LoadAssetAtPath<WorldCreature>("Assets/Game/Prefabs/AntPlug.prefab");
            Assert.That(creature, Is.Not.Null);
            Assert.That(creature.temperament, Is.EqualTo(Temperament.Aggressive));
            Assert.That(creature.maximumHealth, Is.EqualTo(12));
            Assert.That(creature.attackHalfHearts, Is.EqualTo(5), "two and a half hearts: never three of four at once");
            Assert.That(creature.kick, Is.EqualTo(KickResponse.Anchored));
            Assert.That(creature.windup, Is.EqualTo(1.2f).Within(1e-5));
            Assert.That(creature.strikeSeconds, Is.EqualTo(.35f).Within(1e-5));
            Assert.That(creature.recovery, Is.EqualTo(1.5f).Within(1e-5));
            Assert.That(creature.recoveryOnMiss, Is.EqualTo(2.2f).Within(1e-5));
            Assert.That(creature.strikeShake, Is.EqualTo(.5f).Within(1e-5));
            Assert.That(creature.dash, Is.False);
            Assert.That(creature.speed, Is.EqualTo(2.2f).Within(1e-5));
            Assert.That(creature.wanderSpeed, Is.EqualTo(.6f).Within(1e-5));
            Assert.That(creature.turnRate, Is.EqualTo(240));
            Assert.That(creature.attackRange, Is.EqualTo(1.6f).Within(1e-5));
            Assert.That(creature.contactRange, Is.EqualTo(1.7f).Within(1e-5));
            Assert.That(creature.sightRange, Is.EqualTo(10));
            Assert.That(creature.hearingRange, Is.EqualTo(4));
            Assert.That(creature.leash, Is.EqualTo(8));
            Assert.That(creature.patrol.enabled, Is.False);
            Assert.That(creature.spawnSizeRange, Is.EqualTo(new Vector2(.95f, 1.05f)));
            Assert.That(creature.body.mass, Is.EqualTo(5));
            Assert.That(creature.view.height * creature.size, Is.EqualTo(2f).Within(1e-4));
            var capsule = creature.GetComponent<CapsuleCollider>();
            Assert.That(capsule.radius * creature.size, Is.EqualTo(.8f).Within(1e-4));
            Assert.That(capsule.height * creature.size, Is.EqualTo(2f).Within(1e-4));
            Assert.That(creature.navigator.radius * creature.size, Is.EqualTo(.85f).Within(1e-4));
            var sprites = new[] { creature.view.idle, creature.view.idleBack, creature.view.alarm, creature.view.hurt,
                creature.view.attack, creature.view.death, creature.view.deathBack };
            Assert.That(sprites.All(s => s), Is.True);
            Assert.That(sprites.Select(s => s.name).Distinct().Count(), Is.EqualTo(7));
            Assert.That(sprites.All(s => Mathf.Approximately(s.pixelsPerUnit, 125)), Is.True);
            Assert.That(sprites.All(s => s.textureRect.size == new Vector2(265, 233)), Is.True);
            Assert.That(creature.strikeStatuses.Length, Is.EqualTo(1));
            Assert.That(creature.strikeStatuses[0].status.kind, Is.EqualTo(PlayerStatusKind.Deafen));
            Assert.That(creature.strikeStatuses[0].chance, Is.EqualTo(.5f).Within(1e-5), "Оглушение with half the headbutts");
            Assert.That(creature.strikeStatuses[0].seconds, Is.Zero);
            Assert.That(creature.strikeStatuses[0].strength, Is.Zero);
            Assert.That(creature.fireVulnerability, Is.Null);
            foreach (var transform in creature.GetComponentsInChildren<Transform>(true))
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject), Is.Zero, transform.name);
        }

        [Test]
        public void PlugLootHasAcceptedPriceAndIndependentChance()
        {
            var creature = AssetDatabase.LoadAssetAtPath<WorldCreature>("Assets/Game/Prefabs/AntPlug.prefab");
            var catalogue = AssetDatabase.LoadAssetAtPath<LootCatalog>("Assets/Loot/Loot catalog.asset");
            Assert.That(creature.drops.atLeastOne, Is.False);
            Assert.That(creature.drops.entries.Length, Is.EqualTo(1));
            var drop = creature.drops.entries[0];
            Assert.That(drop.chance, Is.EqualTo(.8f).Within(1e-5));
            // One or two heads (balance, stage 8: a plug kill pays about 260).
            Assert.That(drop.min, Is.EqualTo(1));
            Assert.That(drop.max, Is.EqualTo(2));
            Assert.That(drop.item.id, Is.EqualTo("ant_plug_dead_head"));
            Assert.That(drop.item.displayName, Is.EqualTo("Мёртвая голова муравья-пробки"));
            Assert.That(drop.item.price, Is.EqualTo(217));
            Assert.That(drop.item.category, Is.EqualTo("Трофеи"));
            Assert.That(drop.item.rarity, Is.EqualTo(LootRarity.Common));
            Assert.That(drop.item.icon, Is.Not.Null);
            Assert.That(catalogue.Contains(drop.item), Is.True);
        }
    }
}
