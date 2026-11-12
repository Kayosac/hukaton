using Hakaton.Decks.Unity;
using Hakaton.Expeditions.Cards;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>«Чучело» (tmp/new-cards-2/BRIEF.md, 5): whom it draws, its health, growth, flare and the authored assets.</summary>
    public sealed class ScarecrowTests
    {
        [Test]
        public void DrawsOnlyAggressiveAndTerritorialCreaturesGoingAtThePlayerWithinReach()
        {
            Assert.That(ScarecrowRules.Draws(Temperament.Aggressive, CreatureState.Chase, true, false, 5, 12), Is.True);
            Assert.That(ScarecrowRules.Draws(Temperament.Territorial, CreatureState.Threat, true, false, 11.9f, 12), Is.True);
            Assert.That(ScarecrowRules.Draws(Temperament.Aggressive, CreatureState.Recover, true, false, 3, 12), Is.True);
            Assert.That(ScarecrowRules.Draws(Temperament.Peaceful, CreatureState.Chase, true, false, 5, 12), Is.False, "Peaceful ones are left alone.");
            Assert.That(ScarecrowRules.Draws(Temperament.Timid, CreatureState.Flee, true, false, 5, 12), Is.False);
            Assert.That(ScarecrowRules.Draws(Temperament.Aggressive, CreatureState.Idle, true, false, 5, 12), Is.False, "Only those going at the player.");
            Assert.That(ScarecrowRules.Draws(Temperament.Aggressive, CreatureState.Windup, true, false, 5, 12), Is.False, "A swing in progress finishes first.");
            Assert.That(ScarecrowRules.Draws(Temperament.Aggressive, CreatureState.Chase, false, false, 5, 12), Is.False, "Already fighting someone else (an ally).");
            Assert.That(ScarecrowRules.Draws(Temperament.Aggressive, CreatureState.Chase, true, true, 5, 12), Is.False, "Beast brawl rivals are skipped.");
            Assert.That(ScarecrowRules.Draws(Temperament.Aggressive, CreatureState.Chase, true, false, 12.1f, 12), Is.False);
        }

        [Test]
        public void LetsCreaturesGoWhenTheyCalmDownOrAreDraggedFarAway()
        {
            Assert.That(ScarecrowRules.Releases(CreatureState.Idle, 3, 12, 1.3f), Is.True);
            Assert.That(ScarecrowRules.Releases(CreatureState.Return, 3, 12, 1.3f), Is.True);
            Assert.That(ScarecrowRules.Releases(CreatureState.Chase, 14, 12, 1.3f), Is.False);
            Assert.That(ScarecrowRules.Releases(CreatureState.Chase, 16, 12, 1.3f), Is.True);
        }

        [Test]
        public void HealthGrowthAndFlare()
        {
            Assert.That(ScarecrowRules.AfterHit(6, 2), Is.EqualTo(4));
            Assert.That(ScarecrowRules.AfterHit(1, 5), Is.EqualTo(0));
            Assert.That(ScarecrowRules.AfterHit(6, -3), Is.EqualTo(6));

            Assert.That(ScarecrowRules.Growth(0, .55f, .14f), Is.EqualTo(.14f).Within(1e-4f));
            Assert.That(ScarecrowRules.Growth(.55f, .55f, .14f), Is.EqualTo(1).Within(1e-4f));
            Assert.That(ScarecrowRules.Growth(.4f, .55f, .14f), Is.GreaterThan(1), "Shoots up with a small overshoot.");
            Assert.That(ScarecrowRules.Growth(5, 0, .14f), Is.EqualTo(1));

            Assert.That(ScarecrowRules.Flares(2.5f, true), Is.True);
            Assert.That(ScarecrowRules.Flares(2.5f, false), Is.False, "Its time running out is no destruction.");
            Assert.That(ScarecrowRules.Flares(0, true), Is.False, "Levels I and II do not flare.");
            Assert.That(ScarecrowRules.CaughtByFlare(Vector3.zero, new Vector3(2.7f, 0, 0), .3f, 2.5f), Is.True);
            Assert.That(ScarecrowRules.CaughtByFlare(Vector3.zero, new Vector3(3f, 0, 0), .3f, 2.5f), Is.False);
            Assert.That(ScarecrowRules.CaughtByFlare(Vector3.zero, new Vector3(1f, 0, 0), .3f, 0), Is.False);
        }

        [Test]
        public void AssetsCarryTheNumbersAndTheCardIsItsOnlyCollider()
        {
            var effect = AssetDatabase.LoadAssetAtPath<ScarecrowEffect>("Assets/Game/Data/Effects/Scarecrow.asset");
            Assert.That(effect, Is.Not.Null);
            Assert.That((effect.health.i, effect.health.ii, effect.health.iii), Is.EqualTo((6, 10, 16)));
            Assert.That(effect.lifetime.At(1), Is.EqualTo(25));
            Assert.That(effect.drawRadius.At(3), Is.EqualTo(12));
            Assert.That(effect.burnSeconds.At(2), Is.EqualTo(4));
            Assert.That((effect.burstRadius.i, effect.burstRadius.ii, effect.burstRadius.iii), Is.EqualTo((0f, 0f, 2.5f)));
            Assert.That(effect.TryGetStat(out string label, out var values, out _), Is.True);
            Assert.That(label, Is.EqualTo("Здоровье"));
            Assert.That(values.iii, Is.EqualTo(16));
            var area = (ICardArea)effect;
            Assert.That(area.TryGetArea(null, 1, null, out float radius) && radius == 12, Is.True);
            Assert.That(area.LeavesOutlinedZone, Is.True);

            var definition = AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Game/Data/scarecrow.asset");
            Assert.That(definition.effectAsset, Is.SameAs(effect));

            var decoy = effect.scarecrowPrefab;
            Assert.That(decoy, Is.Not.Null);
            Assert.That(decoy.view && decoy.sway && decoy.body && decoy.overhead, Is.True);
            var colliders = decoy.GetComponentsInChildren<Collider>(true);
            Assert.That(colliders.Length, Is.EqualTo(1), "No invisible colliders: the standing card is the only one.");
            Assert.That(colliders[0], Is.SameAs(decoy.body));
            Assert.That(colliders[0].isTrigger, Is.False);
            Assert.That(decoy.body.transform.IsChildOf(decoy.sway) && decoy.view.transform.IsChildOf(decoy.sway), Is.True, "Collider sways and grows with the card.");
            var size = ((BoxCollider)decoy.body).size;
            Assert.That(size.y, Is.EqualTo(decoy.cardHeight).Within(.15f), "The collider is the size of the card.");
        }
    }
}
