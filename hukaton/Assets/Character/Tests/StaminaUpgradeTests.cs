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
            Assert.That(StaminaGauge.Arc(0, 5, 8, .004f), Is.EqualTo((-16, 1)));
            Assert.That(StaminaGauge.Arc(2, 5, 8, .004f), Is.EqualTo((0, 0)));
            Assert.That(StaminaGauge.Arc(4, 5, 8, .004f), Is.EqualTo((16, 1)));
            Assert.That(StaminaGauge.Arc(0, 10, 8, .004f), Is.EqualTo((-36, 5)));
            Assert.That(StaminaGauge.Arc(9, 10, 8, .004f), Is.EqualTo((36, 5)));
            Assert.That(StaminaGauge.Arc(0, 1, 8, .004f), Is.EqualTo((0, 0)));
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

        static string[] Sprites(GameObject arc)
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (Transform child in arc.transform)
                if (child.gameObject.activeSelf) names.Add(child.GetComponent<Image>().sprite.name);
            return names.ToArray();
        }
    }
}
