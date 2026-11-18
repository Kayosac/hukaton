using System.Linq;
using Hakaton.Expeditions;
using Hakaton.Loot;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.EnemyDebug.Tests
{
    public class AntWorkerAssetTests
    {
        const string PrefabPath = "Assets/Game/Prefabs/AntWorker.prefab";

        [Test]
        public void WorkerHasConfirmedCombatMovementAndSevenSprites()
        {
            var creature = AssetDatabase.LoadAssetAtPath<WorldCreature>(PrefabPath);
            Assert.That(creature, Is.Not.Null);
            Assert.That(creature.temperament, Is.EqualTo(Temperament.Territorial));
            Assert.That(creature.maximumHealth, Is.EqualTo(4));
            Assert.That(creature.attackHalfHearts, Is.EqualTo(4));
            Assert.That(creature.kick, Is.EqualTo(KickResponse.Normal));
            Assert.That(creature.windup, Is.EqualTo(.8f).Within(1e-5));
            Assert.That(creature.strikeSeconds, Is.EqualTo(.2f).Within(1e-5));
            Assert.That(creature.recovery, Is.EqualTo(.9f).Within(1e-5));
            Assert.That(creature.recoveryOnMiss, Is.EqualTo(1.5f).Within(1e-5));
            Assert.That(creature.dash, Is.False);
            Assert.That(creature.strikeShake, Is.EqualTo(.15f).Within(1e-5));
            Assert.That(creature.sightRange, Is.EqualTo(10));
            Assert.That(creature.hearingRange, Is.EqualTo(4));
            Assert.That(creature.leash, Is.EqualTo(8));
            Assert.That(creature.speed, Is.EqualTo(3.2f).Within(1e-5));
            Assert.That(creature.wanderSpeed, Is.EqualTo(.8f).Within(1e-5));
            Assert.That(creature.territoryRadius, Is.EqualTo(4));
            Assert.That(creature.turnRate, Is.EqualTo(360));
            Assert.That(creature.attackRange, Is.EqualTo(1.8f).Within(1e-5));
            Assert.That(creature.contactRange, Is.EqualTo(1.9f).Within(1e-5));
            Assert.That(creature.threatRange, Is.EqualTo(7));
            Assert.That(creature.aggroRange, Is.EqualTo(5));
            Assert.That(creature.calmRange, Is.EqualTo(10));
            Assert.That(creature.provokeSeconds, Is.EqualTo(1.1f).Within(1e-5));
            Assert.That(creature.spawnSizeRange, Is.EqualTo(new Vector2(.9f, 1.1f)));
            Assert.That(creature.patrol.enabled, Is.True);
            Assert.That(creature.patrol.flank, Is.True);
            Assert.That(creature.fireVulnerability, Is.Null);
            Assert.That(creature.view.height * creature.size, Is.EqualTo(1.6f).Within(1e-4));
            var capsule = creature.GetComponent<CapsuleCollider>();
            Assert.That(capsule.radius * creature.size, Is.EqualTo(.5f).Within(1e-4));
            Assert.That(capsule.height * creature.size, Is.EqualTo(1.6f).Within(1e-4));
            Assert.That(creature.navigator.radius * creature.size, Is.EqualTo(.55f).Within(1e-4));
            Assert.That(creature.body.mass, Is.EqualTo(1.5f).Within(1e-5));
            Assert.That(creature.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(creature.view.transform.localScale.x, Is.EqualTo(creature.size).Within(1e-5));
            var sprites = new[] { creature.view.idle, creature.view.idleBack, creature.view.alarm, creature.view.hurt,
                creature.view.attack, creature.view.death, creature.view.deathBack };
            Assert.That(sprites.All(s => s), Is.True);
            Assert.That(sprites.Select(s => s.name).Distinct().Count(), Is.EqualTo(7));
            Assert.That(sprites.All(s => Mathf.Approximately(s.pixelsPerUnit, 123.0769f)), Is.True);
            Assert.That(sprites.All(s => s.textureRect.size == new Vector2(254, 210)), Is.True);
            Assert.That(creature.strikeStatuses.Length, Is.EqualTo(1));
            Assert.That(creature.strikeStatuses[0].status.kind, Is.EqualTo(PlayerStatusKind.Deafen));
            Assert.That(creature.strikeStatuses[0].chance, Is.EqualTo(.5f).Within(1e-5));
            Assert.That(creature.strikeStatuses[0].seconds, Is.Zero);
            Assert.That(creature.strikeStatuses[0].strength, Is.Zero);
            Assert.That(creature.strikeStatuses[0].status.seconds, Is.EqualTo(5));
            Assert.That(creature.strikeStatuses[0].status.strength, Is.EqualTo(600));
            foreach (var transform in creature.GetComponentsInChildren<Transform>(true))
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject), Is.Zero, transform.name);
        }

        [Test]
        public void WorkerLootHasAcceptedItemsPricesAndDropRules()
        {
            var creature = AssetDatabase.LoadAssetAtPath<WorldCreature>(PrefabPath);
            var catalogue = AssetDatabase.LoadAssetAtPath<LootCatalog>("Assets/Loot/Loot catalog.asset");
            var expected = new[]
            {
                (id: "ant_worker_pickaxe", name: "Тупая кирка", price: 120, chance: .2f),
                (id: "ant_worker_hardhat", name: "Помятая каска", price: 80, chance: .35f),
                (id: "ant_worker_mitten", name: "Рабочая рукавица", price: 50, chance: .5f)
            };
            Assert.That(creature.drops.atLeastOne, Is.True);
            Assert.That(creature.drops.entries.Length, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                var item = creature.drops.entries[i].item;
                Assert.That(item.id, Is.EqualTo(expected[i].id));
                Assert.That(item.displayName, Is.EqualTo(expected[i].name));
                Assert.That(item.price, Is.EqualTo(expected[i].price));
                Assert.That(item.category, Is.EqualTo("Вещи"));
                Assert.That(item.rarity, Is.EqualTo(LootRarity.Common));
                Assert.That(item.icon, Is.Not.Null);
                Assert.That(item.iconHeight, Is.EqualTo(.45f).Within(1e-5));
                Assert.That(catalogue.Contains(item), Is.True, item.id);
                Assert.That(creature.drops.entries[i].chance, Is.EqualTo(expected[i].chance).Within(1e-5));
                Assert.That(creature.drops.entries[i].min, Is.EqualTo(1));
                Assert.That(creature.drops.entries[i].max, Is.EqualTo(1));
            }
        }
    }
}
