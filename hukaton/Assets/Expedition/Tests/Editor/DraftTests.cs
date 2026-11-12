using Hakaton.Decks.Unity;
using Hakaton.Expeditions.Cards;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>Rules of «Сквозняк»: the wind strip, fire carried downwind, tar blown out, the card's numbers.</summary>
    public sealed class DraftTests
    {
        static DraftStrip Strip(float length = 12) => new DraftStrip(Vector3.zero, new Vector3(0, -3, 5), length, 3, 1.5f, 5);

        [Test]
        public void WindBlowsAlongTheHorizontalDirectionOfTheThrow()
        {
            var direction = DraftRules.WindDirection(new Vector3(3, -8, 4), Vector3.zero, Vector3.one, Vector3.right);
            Assert.That(direction.y, Is.EqualTo(0));
            Assert.That(Vector3.Distance(direction, new Vector3(.6f, 0, .8f)), Is.LessThan(1e-4f));

            // Falling straight down: from where it was thrown to where it landed.
            direction = DraftRules.WindDirection(Vector3.down * 10, new Vector3(0, 2, 0), new Vector3(-4, 0, 0), Vector3.forward);
            Assert.That(Vector3.Distance(direction, Vector3.left), Is.LessThan(1e-4f));

            // Nothing to go by: the fallback (the camera's look), flattened.
            direction = DraftRules.WindDirection(Vector3.down, Vector3.zero, Vector3.zero, new Vector3(0, 5, -2));
            Assert.That(Vector3.Distance(direction, Vector3.back), Is.LessThan(1e-4f));
        }

        [Test]
        public void TheStripIsLengthLongAndWidthWideFromTheImpact()
        {
            var strip = Strip();
            Assert.That(strip.Direction, Is.EqualTo(Vector3.forward), "Horizontal, normalised.");
            Assert.That(strip.Contains(new Vector3(0, 0, 6)), Is.True);
            Assert.That(strip.Contains(new Vector3(1.4f, 0, 11.9f)), Is.True, "Within half the width, before the end.");
            Assert.That(strip.Contains(new Vector3(1.6f, 0, 6)), Is.False, "3 m wide: 1.5 to each side.");
            Assert.That(strip.Contains(new Vector3(0, 0, 12.5f)), Is.False, "Past the end.");
            Assert.That(strip.Contains(new Vector3(0, 0, -.5f)), Is.False, "Behind the start.");
            Assert.That(strip.Contains(new Vector3(0, 4.5f, 6)), Is.True, "Up to a rain cloud's height.");
            Assert.That(strip.Contains(new Vector3(0, 6, 6)), Is.False);
            Assert.That(strip.Contains(new Vector3(0, -2, 6)), Is.False);
            Assert.That(strip.Overlaps(new Vector3(3.5f, 0, 6), 2.5f), Is.True, "A puddle reaching into the strip.");
            Assert.That(strip.Overlaps(new Vector3(4.5f, 0, 6), 2.5f), Is.False);
        }

        [Test]
        public void ARainCloudIsCarriedToTheFarEndKeepingItsOffset()
        {
            var strip = Strip(8);
            var end = strip.CarriedToEnd(new Vector3(1, 0, 2));
            Assert.That(Vector3.Distance(end, new Vector3(1, 0, 8)), Is.LessThan(1e-4f));
            Assert.That(strip.CarriedToEnd(new Vector3(0, 0, 9)).z, Is.EqualTo(9), "Already past the end: stays.");
        }

        [Test]
        public void FireIsCarriedUpToFourMetresDownwind()
        {
            var wind = Vector3.forward;
            var fire = new Vector3(0, 0, 3);
            Assert.That(DraftRules.Downwind(fire, new Vector3(0, 0, 6.9f), wind, 4, 1.5f), Is.True);
            Assert.That(DraftRules.Downwind(fire, new Vector3(1.2f, 0, 5), wind, 4, 1.5f), Is.True);
            Assert.That(DraftRules.Downwind(fire, new Vector3(0, 0, 7.5f), wind, 4, 1.5f), Is.False, "Beyond 4 m.");
            Assert.That(DraftRules.Downwind(fire, new Vector3(0, 0, 1), wind, 4, 1.5f), Is.False, "Upwind of the fire.");
            Assert.That(DraftRules.Downwind(fire, new Vector3(2, 0, 4), wind, 4, 1.5f), Is.False, "Too far to the side.");
            Assert.That(DraftRules.Downwind(fire, new Vector3(0, 0, 2.9f), wind, 4, 1.5f), Is.True, "Standing in the flames.");
        }

        [Test]
        public void TheNearestPointOfAPuddleIsOnItsEdge()
        {
            var point = DraftRules.NearestOnDisc(new Vector3(0, 1, 0), 2, new Vector3(0, 0, -5));
            Assert.That(Vector3.Distance(point, new Vector3(0, 1, -2)), Is.LessThan(1e-4f));
            point = DraftRules.NearestOnDisc(Vector3.zero, 2, new Vector3(.5f, 3, 0));
            Assert.That(Vector3.Distance(point, new Vector3(.5f, 0, 0)), Is.LessThan(1e-4f), "Inside: the point itself, on the puddle.");
        }

        [Test]
        public void WindBlowsBurningTarOutAndItCanBeLitAgain()
        {
            var tar = new TarPuddleState(30);
            Assert.That(tar.Extinguish(), Is.False, "Unlit tar has no fire to blow out.");
            tar.Ignite();
            tar.Advance(10);
            Assert.That(tar.Extinguish(), Is.True);
            Assert.That(tar.Phase, Is.EqualTo(TarPhase.Tar), "The puddle stays.");
            Assert.That(tar.Slows, Is.True, "Unlit tar slows again.");
            Assert.That(tar.Ignite(), Is.True, "It can be lit again.");
            Assert.That(tar.BurnRemaining, Is.EqualTo(20).Within(1e-3f), "It burns on for what is left of its time.");
            tar.Advance(20);
            Assert.That(tar.Phase, Is.EqualTo(TarPhase.BurntOut));
            Assert.That(tar.Extinguish(), Is.False, "Burnt out: nothing to blow out.");
        }

        [Test]
        public void TheCardCarriesTheBoardNumbers()
        {
            var card = AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Game/Data/draft.asset");
            var draft = card.effectAsset as DraftEffect;
            Assert.That(draft, Is.Not.Null, "draft.asset → Effects/Draft.asset");
            Assert.That((draft.length.i, draft.length.ii, draft.length.iii), Is.EqualTo((8f, 12f, 16f)));
            Assert.That(draft.width, Is.EqualTo(3));
            Assert.That(draft.gustSeconds, Is.EqualTo(1.5f));
            Assert.That(draft.fireReach, Is.EqualTo(4));
            Assert.That(draft.pushImpulse, Is.EqualTo(4));
            Assert.That(draft.gustPrefab, Is.Not.Null);
            Assert.That(draft.gustPrefab.streams, Is.Not.Empty, "Visible wind streaks.");
            Assert.That(draft.impactVfx, Is.Not.Null);
            Assert.That(draft.impactSound, Is.Not.Null);
            Assert.That(draft.TryGetStat(out var label, out _, out _), Is.True);
            Assert.That(label, Is.EqualTo("Длина"));
        }
    }
}
