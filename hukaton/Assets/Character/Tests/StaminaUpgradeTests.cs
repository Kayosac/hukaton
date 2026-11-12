using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Character.Tests
{
    /// <summary>Playtest: «Выносливость» on the PC (5 » 10 s of sprint) and the arc of pips under the crosshair.</summary>
    public sealed class StaminaUpgradeTests
    {
        static readonly int[] Prices = { 200, 450, 800, 1250, 1800 };
        static StaminaUpgrade Upgrade() => new StaminaUpgrade(100, 20, 1, Prices);

        [Test]
        public void EachStepAddsOneSecondOfSprintFromFiveToTen()
        {
            var upgrade = Upgrade();
            Assert.That((upgrade.Level, upgrade.Steps, upgrade.Price, upgrade.Maxed), Is.EqualTo((0, 5, 200, false)));
            Assert.That(upgrade.StepCapacity, Is.EqualTo(20));
            for (int level = 0; level <= 5; level++)
            {
                Assert.That(upgrade.SecondsAt(level), Is.EqualTo(5 + level).Within(1e-4f));
                Assert.That(upgrade.CapacityAt(level), Is.EqualTo(100 + 20 * level).Within(1e-3f));
            }
            Assert.That(upgrade.SecondsAt(9), Is.EqualTo(10).Within(1e-4f), "Past the last step stays at the maximum.");
            Assert.That(upgrade.PriceAt(5), Is.Zero);

            int changes = 0;
            upgrade.Changed += () => changes++;
            var paid = 0;
            while (!upgrade.Maxed)
            {
                paid += upgrade.Price;
                float next = upgrade.NextSeconds;
                Assert.That(upgrade.Raise(), Is.True);
                Assert.That(upgrade.Seconds, Is.EqualTo(next).Within(1e-4f));
            }
            Assert.That((paid, changes, upgrade.Price, upgrade.Seconds), Is.EqualTo((4500, 5, 0, 10f)));
            Assert.That(upgrade.NextSeconds, Is.EqualTo(10));
            Assert.That(upgrade.Raise(), Is.False);
            Assert.That(upgrade.Maximize(), Is.False);
            Assert.That(changes, Is.EqualTo(5));
        }

        [Test]
        public void RestoreClampsAndCheatMaximizes()
        {
            var upgrade = Upgrade();
            int changes = 0;
            upgrade.Changed += () => changes++;
            upgrade.Restore(0);
            Assert.That(changes, Is.Zero, "Nothing changed, nothing raised.");
            upgrade.Restore(3);
            Assert.That((upgrade.Level, upgrade.Seconds, changes), Is.EqualTo((3, 8f, 1)));
            upgrade.Restore(99);
            Assert.That(upgrade.Level, Is.EqualTo(5), "A level saved under longer price lists is clamped.");
            upgrade.Restore(-4);
            Assert.That(upgrade.Level, Is.Zero);
            Assert.That(upgrade.Maximize(), Is.True);
            Assert.That((upgrade.Level, upgrade.Capacity), Is.EqualTo((5, 200f)));
        }

        [Test]
        public void InvalidTuningIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new StaminaUpgrade(0, 20, 1, Prices));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StaminaUpgrade(100, 0, 1, Prices));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StaminaUpgrade(100, 20, float.NaN, Prices));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StaminaUpgrade(100, 20, 1, new[] { 10, -1 }));
            Assert.Throws<ArgumentNullException>(() => new StaminaUpgrade(100, 20, 1, null));
            Assert.That(new StaminaUpgrade(100, 20, 1, new int[0]).Maxed, Is.True, "No prices: nothing to buy.");
        }

        [Test]
        public void PipsAreSecondsOfSprintAndLitWhileAnyStaminaIsLeft()
        {
            Assert.That(StaminaGauge.Pips(100, 20), Is.EqualTo(5));
            Assert.That(StaminaGauge.Pips(200, 20), Is.EqualTo(10));
            Assert.That(StaminaGauge.Pips(100, 0), Is.EqualTo(1));
            Assert.That(StaminaGauge.Pips(1e9f, 1), Is.EqualTo(StaminaGauge.MaxPips));
            Assert.That(StaminaGauge.Lit(100, 20, 5), Is.EqualTo(5));
            Assert.That(StaminaGauge.Lit(88, 20, 5), Is.EqualTo(5), "A jump does not empty a pip.");
            Assert.That(StaminaGauge.Lit(80, 20, 5), Is.EqualTo(4));
            Assert.That(StaminaGauge.Lit(.01f, 20, 5), Is.EqualTo(1), "The last pip goes out only at zero.");
            Assert.That(StaminaGauge.Lit(0, 20, 5), Is.Zero);
            Assert.That(StaminaGauge.Lit(500, 20, 5), Is.EqualTo(5));
            Assert.That(StaminaGauge.Look(1, 3, false), Is.EqualTo(StaminaPipLook.Full));
            Assert.That(StaminaGauge.Look(3, 3, false), Is.EqualTo(StaminaPipLook.Empty));
            Assert.That(StaminaGauge.Look(0, 3, true), Is.EqualTo(StaminaPipLook.Tired), "Out of breath every pip is hollow and red.");
        }

        [Test]
        public void TheArcIsCentredInWholeUnitsWithRisingEnds()
        {
            // Five pips 8 apart on a radius of 21: about 22 degrees between them, the ends rise a third of the radius.
            Assert.That(StaminaGauge.Arc(2, 5, 8, 21, 180), Is.EqualTo((0, 0)));
            Assert.That(StaminaGauge.Arc(0, 5, 8, 21, 180), Is.EqualTo((-14, 6)));
            Assert.That(StaminaGauge.Arc(4, 5, 8, 21, 180), Is.EqualTo((14, 6)));
            Assert.That(StaminaGauge.Arc(1, 5, 8, 21, 180), Is.EqualTo((-8, 2)));
            // Ten pips would wrap over the aim: the span is capped at 180 degrees, the ends level with the aim.
            Assert.That(StaminaGauge.Arc(0, 10, 8, 21, 180), Is.EqualTo((-21, 21)));
            Assert.That(StaminaGauge.Arc(9, 10, 8, 21, 180), Is.EqualTo((21, 21)));
            Assert.That(StaminaGauge.Arc(0, 1, 8, 21, 180), Is.EqualTo((0, 0)));
            // On a circle around the aim: every pip is the radius away from it (to a unit of rounding).
            for (int i = 0; i < 7; i++)
            {
                var (x, y) = StaminaGauge.Arc(i, 7, 8, 21, 180);
                Assert.That(Math.Sqrt(x * x + (21 - y) * (21 - y)), Is.EqualTo(21).Within(1), $"pip {i}");
            }
        }

        [Test]
        public void ThePlayersUpgradeResizesAndFillsTheBudget()
        {
            var go = new GameObject("Stamina test player");
            try
            {
                var stamina = go.AddComponent<PlayerStamina>();
                var upgrade = stamina.Upgrade;
                Assert.That((upgrade.Steps, upgrade.Price, upgrade.Seconds), Is.EqualTo((5, 200, 5f)));
                Assert.That(stamina.StaminaPerPip, Is.EqualTo(20));
                stamina.Tick(3, true);
                Assert.That(stamina.Value, Is.EqualTo(40).Within(1e-3f));
                Assert.That(upgrade.Raise(), Is.True);
                Assert.That((stamina.Capacity, stamina.Value), Is.EqualTo((120f, 120f)), "A bought step fills the new maximum.");
                upgrade.Restore(0);
                Assert.That((stamina.Capacity, stamina.Value), Is.EqualTo((100f, 100f)));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void ThePipArcFollowsSpendingExhaustionAndBoughtSteps()
        {
            var player = new GameObject("Stamina test player");
            var arc = new GameObject("Stamina pips", typeof(RectTransform), typeof(CanvasGroup));
            var template = new GameObject("Pip", typeof(RectTransform), typeof(Image));
            var texture = new Texture2D(6, 6);
            try
            {
                template.transform.SetParent(arc.transform, false);
                Sprite Make(string name)
                {
                    var sprite = Sprite.Create(texture, new Rect(0, 0, 6, 6), new Vector2(.5f, .5f));
                    sprite.name = name;
                    return sprite;
                }
                var pips = arc.AddComponent<StaminaPips>();
                pips.visibility = arc.GetComponent<CanvasGroup>();
                pips.pipTemplate = template.GetComponent<Image>();
                pips.fullPip = Make("full");
                pips.emptyPip = Make("empty");
                pips.tiredPip = Make("tired");
                pips.visibility.alpha = 0;
                var stamina = player.AddComponent<PlayerStamina>();
                _ = stamina.Upgrade;

                pips.Present(stamina, true, 1);
                Assert.That((pips.Count, pips.Lit, pips.visibility.alpha), Is.EqualTo((5, 5, 0f)), "Full: nothing to show.");

                stamina.Tick(1.5f, true);
                pips.Present(stamina, true, 1);
                Assert.That((pips.Count, pips.Lit, pips.visibility.alpha), Is.EqualTo((5, 4, 1f)));
                Assert.That(Sprites(arc), Is.EqualTo(new[] { "full", "full", "full", "full", "empty" }));
                pips.Present(stamina, false, 1);
                Assert.That(pips.visibility.alpha, Is.Zero, "The HUD can hide it (no aim on screen).");

                stamina.Tick(10, true);
                pips.Present(stamina, true, 1);
                Assert.That((stamina.Exhausted, pips.Lit), Is.EqualTo((true, 0)));
                Assert.That(Sprites(arc), Is.EqualTo(new[] { "tired", "tired", "tired", "tired", "tired" }));

                stamina.Upgrade.Raise();
                pips.Present(stamina, true, 1);
                Assert.That((pips.Count, pips.Lit), Is.EqualTo((6, 6)), "A bought step adds a pip and fills the budget.");
                Assert.That(Sprites(arc), Is.EqualTo(new[] { "full", "full", "full", "full", "full", "full" }));
                foreach (RectTransform pip in arc.transform)
                {
                    var position = pip.anchoredPosition;
                    Assert.That((position.x % 1, position.y % 1), Is.EqualTo((0f, 0f)), "Whole layout units only.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(arc);
                UnityEngine.Object.DestroyImmediate(player);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// Playtest regression (outing screenshots): the arc shows the real stamina — half spent is 3 full + 2 empty, empty
        /// is all hollow red, and it stays red until the recovery threshold. While out of breath a "sprinting" tick is a
        /// recovery tick (the budget cannot be spent), which is what a harness driving <c>Tick(dt, true)</c> sees.
        /// </summary>
        [Test]
        public void PipsFollowTheRealStaminaThroughSpendingExhaustionAndRecovery()
        {
            var player = new GameObject("Stamina test player");
            var arc = new GameObject("Stamina pips", typeof(RectTransform), typeof(CanvasGroup));
            var template = new GameObject("Pip", typeof(RectTransform), typeof(Image));
            var texture = new Texture2D(6, 6);
            try
            {
                template.transform.SetParent(arc.transform, false);
                Sprite Make(string name)
                {
                    var sprite = Sprite.Create(texture, new Rect(0, 0, 6, 6), new Vector2(.5f, .5f));
                    sprite.name = name;
                    return sprite;
                }
                var pips = arc.AddComponent<StaminaPips>();
                pips.visibility = arc.GetComponent<CanvasGroup>();
                pips.pipTemplate = template.GetComponent<Image>();
                pips.fullPip = Make("full");
                pips.emptyPip = Make("empty");
                pips.tiredPip = Make("tired");
                var stamina = player.AddComponent<PlayerStamina>();
                _ = stamina.Upgrade;
                const string F = "full", E = "empty", R = "tired";

                pips.Present(stamina, true, 1);
                Assert.That(Sprites(arc), Is.EqualTo(new[] { F, F, F, F, F }));

                // Half spent: 50 of 100 at 20 a pip.
                stamina.Tick(2.5f, true);
                pips.Present(stamina, true, 1);
                Assert.That((stamina.Value, stamina.Exhausted), Is.EqualTo((50f, false)));
                Assert.That(Sprites(arc), Is.EqualTo(new[] { F, F, F, E, E }));
                Assert.That(pips.visibility.alpha, Is.EqualTo(1));

                // Spent to the end: out of breath, every pip hollow and red.
                stamina.Tick(3, true);
                pips.Present(stamina, true, 1 / 60f);
                Assert.That((stamina.Value, stamina.Exhausted), Is.EqualTo((0f, true)));
                Assert.That(Sprites(arc), Is.EqualTo(new[] { R, R, R, R, R }));

                // Holding sprint while out of breath spends nothing: these ticks count towards recovery.
                for (int i = 0; i < 5; i++) stamina.Tick(.1f, true);
                pips.Present(stamina, true, 1 / 60f);
                Assert.That((stamina.Value, stamina.Exhausted), Is.EqualTo((0f, true)));
                Assert.That(Sprites(arc), Is.EqualTo(new[] { R, R, R, R, R }));

                // Getting the breath back: still red below the recovery threshold (25%), even with stamina in two pips.
                stamina.Tick(1.4f, false);
                pips.Present(stamina, true, 1 / 60f);
                Assert.That(stamina.Value, Is.EqualTo(22.5f).Within(.01f));
                Assert.That(stamina.Exhausted, Is.True);
                Assert.That(Sprites(arc), Is.EqualTo(new[] { R, R, R, R, R }));

                // Above it: back to normal, the filled pips lit.
                stamina.Tick(.2f, false);
                pips.Present(stamina, true, 1 / 60f);
                Assert.That(stamina.Value, Is.EqualTo(27.5f).Within(.01f));
                Assert.That(stamina.Exhausted, Is.False);
                Assert.That(Sprites(arc), Is.EqualTo(new[] { F, F, E, E, E }));
                Assert.That(pips.visibility.alpha, Is.GreaterThan(0), "Still shown while recovering.");

                // Full again: hidden after the hold.
                stamina.Tick(10, false);
                for (int i = 0; i < 3; i++) pips.Present(stamina, true, 1);
                Assert.That(Sprites(arc), Is.EqualTo(new[] { F, F, F, F, F }));
                Assert.That(pips.visibility.alpha, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(arc);
                UnityEngine.Object.DestroyImmediate(player);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        static string[] Sprites(GameObject arc)
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (Transform child in arc.transform)
                if (child.gameObject.activeSelf) names.Add(child.GetComponent<Image>().sprite.name);
            return names.ToArray();
        }
    }
}
