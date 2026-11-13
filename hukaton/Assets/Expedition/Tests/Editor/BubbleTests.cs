using Hakaton.Decks.Unity;
using Hakaton.Expeditions.Cards;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>«Пузырь» (tmp/new-cards-2/BRIEF.md, 6): dome geometry, pushing creatures out, popping projectiles, assets.</summary>
    public sealed class BubbleTests
    {
        static readonly Vector3 Centre = new Vector3(10, 2, -4);

        [Test]
        public void DomeIsWidestAtTheGround()
        {
            Assert.That(BubbleRules.RadiusAt(3, 0), Is.EqualTo(3));
            Assert.That(BubbleRules.RadiusAt(3, -1), Is.EqualTo(3), "Below the centre (uneven ground) it is as wide as at the ground.");
            Assert.That(BubbleRules.RadiusAt(3, 3), Is.EqualTo(0));
            Assert.That(BubbleRules.RadiusAt(3, 1.8f), Is.EqualTo(2.4f).Within(1e-4f));
            Assert.That(BubbleRules.Inside(Centre + new Vector3(0, 2.9f, 0), Centre, 3), Is.True);
            Assert.That(BubbleRules.Inside(Centre + new Vector3(0, 3.1f, 0), Centre, 3), Is.False);
            Assert.That(BubbleRules.Inside(Centre + new Vector3(0, -1f, 0), Centre, 3), Is.False, "Not under the ground.");
        }

        [Test]
        public void CreaturesArePushedOutHorizontally()
        {
            float need = BubbleRules.Overlap(Centre + new Vector3(1, 0, 0), .4f, 1, Centre, 3, out var outward);
            Assert.That(need, Is.EqualTo(2.4f).Within(1e-4f));
            Assert.That(outward, Is.EqualTo(Vector3.right));
            Assert.That(BubbleRules.Overlap(Centre + new Vector3(0, 0, -3.5f), .4f, 1, Centre, 3, out _), Is.EqualTo(0), "Outside, clear of the film.");
            Assert.That(BubbleRules.Overlap(Centre + new Vector3(0, 0, -3.2f), .4f, 1, Centre, 3, out outward), Is.EqualTo(.2f).Within(1e-4f), "Leaning on the film.");
            Assert.That(outward, Is.EqualTo(Vector3.back));
            Assert.That(BubbleRules.Overlap(Centre + new Vector3(.5f, 3.2f, 0), .4f, 1, Centre, 3, out _), Is.EqualTo(0), "A flyer above the top.");
            Assert.That(BubbleRules.Overlap(Centre + new Vector3(.5f, -3f, 0), .4f, 1, Centre, 3, out _), Is.EqualTo(0), "Far below the ground line.");
            Assert.That(BubbleRules.Overlap(Centre, .4f, 1, Centre, 3, out outward), Is.GreaterThan(3), "Right in the middle: still pushed out.");
            Assert.That(outward.sqrMagnitude, Is.EqualTo(1).Within(1e-4f));

            Assert.That(BubbleRules.ExpelStep(.2f, .3f, 7, .02f), Is.EqualTo(.2f).Within(1e-5f), "A lean is undone at once.");
            Assert.That(BubbleRules.ExpelStep(2, .3f, 7, .02f), Is.EqualTo(.14f).Within(1e-5f), "Deep inside it is shoved out at the expel speed.");
            Assert.That(BubbleRules.ExpelStep(0, .3f, 7, .02f), Is.EqualTo(0));
        }

        [Test]
        public void InwardSpeedIsRemovedAndAChargeBouncesOff()
        {
            var v = BubbleRules.Deflect(new Vector3(-1, .5f, 2), Vector3.right, 3, .6f, out float inward);
            Assert.That(inward, Is.EqualTo(1).Within(1e-5f));
            Assert.That(v, Is.EqualTo(new Vector3(0, .5f, 2)));
            v = BubbleRules.Deflect(new Vector3(-9.6f, 0, 0), Vector3.right, 3, .6f, out inward);
            Assert.That(v.x, Is.EqualTo(9.6f * .6f).Within(1e-4f), "A charging boar bounces back.");
            v = BubbleRules.Deflect(new Vector3(2, 0, 0), Vector3.right, 3, .6f, out inward);
            Assert.That(v, Is.EqualTo(new Vector3(2, 0, 0)), "Leaving: untouched.");
        }

        [Test]
        public void ProjectilesFlyingInArePoppedOnTheFilm()
        {
            Vector3 c = Centre;
            Assert.That(BubbleRules.Enters(c + new Vector3(3.5f, 1, 0), c + new Vector3(2.5f, 1, 0), c, 3, out var point), Is.True);
            Assert.That(point.x - c.x, Is.EqualTo(Mathf.Sqrt(8)).Within(1e-3f));
            Assert.That(BubbleRules.Enters(c + new Vector3(6, 1, 0), c + new Vector3(4, 1, 0), c, 3, out _), Is.False, "Not there yet.");
            Assert.That(BubbleRules.Enters(c + new Vector3(3.2f, 1, 0), c + new Vector3(4, 1, 0), c, 3, out _), Is.False, "Flying away.");
            Assert.That(BubbleRules.Enters(c + new Vector3(3.5f, -1, 0), c + new Vector3(2.5f, -1, 0), c, 3, out _), Is.False, "Under the ground line.");
            Assert.That(BubbleRules.Enters(c + new Vector3(4, 4, 0), c + new Vector3(4, -4, 0), c, 3, out _), Is.False, "Passing beside.");
            Assert.That(BubbleRules.Enters(c + new Vector3(1, 1, 0), c + new Vector3(1.2f, 1, 0), c, 3, out point), Is.True, "Already inside.");
            Assert.That(point, Is.EqualTo(c + new Vector3(1, 1, 0)));
            Assert.That(BubbleRules.Enters(c + new Vector3(0, 5, 0), c + new Vector3(0, 1, 0), c, 3, out point), Is.True, "Falling on the top.");
            Assert.That(point.y - c.y, Is.EqualTo(3).Within(1e-3f));
        }

        [Test]
        public void StrikesFromOutsideOnSomeoneInsideHitTheFilm()
        {
            Vector3 c = Centre;
            Assert.That(BubbleRules.Shields(c + new Vector3(3.6f, 0, 0), c + new Vector3(2.9f, 0, 0), c, 3), Is.True, "The player right at the film.");
            Assert.That(BubbleRules.Shields(c + new Vector3(-5f, 0, 0), c, c, 3), Is.True);
            Assert.That(BubbleRules.Shields(c + new Vector3(3.6f, 0, 0), c + new Vector3(3.3f, 0, 0), c, 3), Is.False, "Outside, no cover.");
            Assert.That(BubbleRules.Shields(c + new Vector3(1, 0, 0), c, c, 3), Is.False, "An attacker inside is not kept off (it is pushed out anyway).");
            Vector3 film = BubbleRules.FilmPoint(c + new Vector3(0, 0, 6), c, 3, c.y + .9f);
            Assert.That((film - c).magnitude, Is.EqualTo(3).Within(1e-3f));
            Assert.That(film.z - c.z, Is.GreaterThan(2.5f), "On the attacker's side.");
        }

        [Test]
        public void AssetsCarryTheNumbersAndTheDomeHasNoCollider()
        {
            var effect = AssetDatabase.LoadAssetAtPath<BubbleEffect>("Assets/Game/Data/Effects/Bubble.asset");
            Assert.That(effect, Is.Not.Null);
            Assert.That((effect.duration.i, effect.duration.ii, effect.duration.iii), Is.EqualTo((4f, 6f, 9f)));
            Assert.That(effect.radius, Is.EqualTo(3));
            Assert.That(effect.TryGetStat(out string label, out var values, out string unit), Is.True);
            Assert.That(label, Is.EqualTo("Длительность"));
            Assert.That(unit, Is.EqualTo(" с"));
            var area = (ICardArea)effect;
            Assert.That(area.TryGetArea(null, 2, null, out float radius) && radius == 3, Is.True);
            Assert.That(area.LeavesOutlinedZone, Is.True);
            var definition = AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Game/Data/bubble.asset");
            Assert.That(definition.effectAsset, Is.SameAs(effect));

            var dome = effect.domePrefab;
            Assert.That(dome, Is.Not.Null);
            Assert.That(dome.GetComponentsInChildren<Collider>(true), Is.Empty, "No invisible colliders: creatures are kept out by script.");
            Assert.That(dome.film && dome.dome, Is.True);
            Assert.That(dome.film.sharedMaterial.shader.name, Is.EqualTo("Hakaton/Soap Bubble"));
            Assert.That(dome.popVfx, Is.Not.Null);
        }
    }
}
