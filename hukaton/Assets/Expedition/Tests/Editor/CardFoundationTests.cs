using System.Collections.Generic;
using System.Linq;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions.Cards;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>Shared systems of the new cards: wet + Ice, the zone registry, the catalogue's effect assets.</summary>
    public sealed class CardFoundationTests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void DestroyCreated()
        {
            foreach (var item in created) if (item) Object.DestroyImmediate(item);
            created.Clear();
        }

        GameObject Make(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            created.Add(go);
            return go;
        }

        // ---------- wet + Ice ----------

        [Test]
        public void WetTargetFreezesTwiceAsLong()
        {
            Assert.That(FreezeEffect.FreezeSeconds(2, false, 2), Is.EqualTo(2));
            Assert.That(FreezeEffect.FreezeSeconds(2, true, 2), Is.EqualTo(4), "Board: base Ice 2 → 4 s when wet.");
            Assert.That(FreezeEffect.FreezeSeconds(3, true, 2), Is.EqualTo(6), "Board: upgraded Ice 3 → 6 s when wet.");
        }

        [Test]
        public void IceAssetsGiveTheBoardsWetDurations()
        {
            var ice = AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Game/Data/ice.asset");
            var effect = ice.effectAsset as FreezeEffect;
            Assert.That(effect, Is.Not.Null);
            Assert.That(effect.wetMultiplier, Is.EqualTo(2));
            float basePower = ice.basePower, upgradedPower = ice.basePower + ice.powerPerLevel;
            Assert.That(FreezeEffect.FreezeSeconds(basePower, true, effect.wetMultiplier), Is.EqualTo(4));
            Assert.That(FreezeEffect.FreezeSeconds(upgradedPower, true, effect.wetMultiplier), Is.EqualTo(6));
        }

        [Test]
        public void WetStatusLastsFromTheLastSoakWithoutAddingUp()
        {
            var body = Make("Wet body", Vector3.zero);
            var part = Make("Paw", Vector3.zero);
            part.transform.SetParent(body.transform);
            Assert.That(WetStatus.IsWet(part.transform), Is.False);
            var status = WetStatus.Soak(body.transform, null);
            Assert.That(WetStatus.IsWet(part.transform), Is.True, "A hit on any part of the body sees it wet.");
            Assert.That(status.Remaining, Is.EqualTo(WetStatus.DefaultLingerSeconds).Within(.01f));
            WetStatus.Soak(body.transform, null);
            WetStatus.Soak(body.transform, null, 5);
            Assert.That(body.GetComponents<WetStatus>().Length, Is.EqualTo(1));
            Assert.That(status.Remaining, Is.EqualTo(WetStatus.DefaultLingerSeconds).Within(.01f),
                "Soaking again never adds up and a shorter soak never dries it sooner.");
            status.Dry();
            Assert.That(WetStatus.IsWet(body.transform), Is.False);
        }

        // ---------- zones ----------

        FakeCardZone Zone(string name, Vector3 position, float radius, CardZoneKind kind)
        {
            var zone = ScriptableObject.CreateInstance<FakeCardZone>();
            zone.name = name;
            zone.position = position;
            zone.radius = radius;
            zone.kind = kind;
            created.Add(zone);
            return zone;
        }

        [Test]
        public void RegistryAnswersPointRadiusBoxAndKindQueries()
        {
            var registry = new CardZoneRegistry();
            var tar = Zone("Tar", Vector3.zero, 2, CardZoneKind.TarPuddle);
            var fog = Zone("Fog", new Vector3(10, 0, 0), 3, CardZoneKind.Fog);
            registry.Add(tar);
            registry.Add(fog);
            registry.Add(tar);
            Assert.That(registry.Count, Is.EqualTo(2), "Adding twice registers once.");

            var found = new List<ICardZone>();
            Assert.That(registry.Containing(new Vector3(1, 0, 0), found), Is.EqualTo(1));
            Assert.That(found[0], Is.SameAs(tar));
            found.Clear();
            Assert.That(registry.Near(new Vector3(5, 0, 0), 3.5f, found), Is.EqualTo(2), "Tar bounds 3 m away, fog bounds 2 m away.");
            found.Clear();
            Assert.That(registry.Near(new Vector3(5, 0, 0), 3.5f, found, CardZoneKind.Fog), Is.EqualTo(1));
            found.Clear();
            Assert.That(registry.Near(new Vector3(5, 0, 0), 2.5f, found), Is.EqualTo(1), "Only the fog is within 2.5 m.");
            found.Clear();
            var rain = new Bounds(new Vector3(2, 2, 0), new Vector3(6, 6, 6));
            Assert.That(registry.Overlapping(rain, found, CardZoneKind.TarPuddle), Is.EqualTo(1), "Rain over the puddle finds it.");
            var ignitables = new List<IIgnitable>();
            Assert.That(registry.OfType(ignitables), Is.EqualTo(2));
            Assert.That(registry.FirstContaining<IIgnitable>(new Vector3(10, 0, 2)), Is.SameAs(fog));
            Assert.That(registry.FirstContaining<IIgnitable>(new Vector3(30, 0, 0)), Is.Null);
        }

        [Test]
        public void DestroyedZonesLeaveTheRegistry()
        {
            var registry = new CardZoneRegistry();
            var tar = Zone("Tar", Vector3.zero, 2, CardZoneKind.TarPuddle);
            registry.Add(tar);
            Object.DestroyImmediate(tar);
            Assert.That(registry.Count, Is.Zero);
            var found = new List<ICardZone>();
            Assert.That(registry.Containing(Vector3.zero, found), Is.Zero);
        }

        // ---------- the catalogue's effects ----------

        [Test]
        public void EveryCardHasAnEffectAndNewCardsHaveTheirOwnStubs()
        {
            var config = AssetDatabase.LoadAssetAtPath<DeckPrototypeConfig>("Assets/Game/Data/Cards.asset");
            Assert.That(config.cards.Length, Is.EqualTo(22));
            Assert.That(config.cards.All(c => c.effectAsset is CardEffect), Is.True, "Every card can be thrown.");
            var newEffects = config.cards.Where(c => (int)c.action >= 10).Select(c => c.effectAsset).ToArray();
            Assert.That(newEffects.Length, Is.EqualTo(16));
            Assert.That(newEffects.Select(e => e.GetType()).Distinct().Count(), Is.EqualTo(16), "One effect class per new card.");
            Assert.That(newEffects.All(e => e.GetType().Namespace == "Hakaton.Expeditions.Cards"), Is.True);
            foreach (var definition in config.cards.Where(c => (int)c.action >= 10))
            {
                string script = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject((ScriptableObject)definition.effectAsset));
                string name = definition.effectAsset.GetType().Name.Replace("Effect", "");
                Assert.That(script, Is.EqualTo($"Assets/Expedition/Runtime/Cards/{name}/{name}Effect.cs"), "One folder per card.");
                bool stat = definition.effectAsset is ICardStatLine line && line.TryGetStat(out _, out _, out _, out _);
                Assert.That(stat, Is.EqualTo(definition.upgradable), definition.id + ": the workshop shows its main number.");
            }
            var moon = config.Find("moon_gravity").effectAsset as MoonGravityEffect;
            Assert.That(moon.durationBase, Is.EqualTo(12));
            Assert.That(moon.durationUpgraded, Is.EqualTo(45));
            Assert.That(moon.gravityMultiplier, Is.EqualTo(.25f));
        }
    }
}
