using System.Collections.Generic;
using System.Linq;
using Hakaton.Computer;
using Hakaton.Decks;
using NUnit.Framework;
using UnityEngine;

namespace Hakaton.SpecialItems.Tests
{
    public sealed class SlotMachineTests
    {
        [Test]
        public void TheReelsShowTheDrawnOutcome()
        {
            var random = new System.Random(5);
            foreach (PhoneOutcome outcome in System.Enum.GetValues(typeof(PhoneOutcome)))
                for (int i = 0; i < 200; i++)
                    Assert.That(SlotMachine.Read(SlotMachine.Show(outcome, random)), Is.EqualTo(outcome), outcome.ToString());
        }

        [Test]
        public void NothingShowsThreeDifferentSymbols()
        {
            var random = new System.Random(1);
            for (int i = 0; i < 300; i++)
                Assert.That(SlotMachine.Show(PhoneOutcome.Nothing, random).Distinct().Count(), Is.EqualTo(3));
        }

        [Test]
        public void RollFollowsTheTableAndNeverPicksAZeroChance()
        {
            var table = new[] { new PhoneOutcomeChance(PhoneOutcome.Nothing, 0), new PhoneOutcomeChance(PhoneOutcome.Coins, 75), new PhoneOutcomeChance(PhoneOutcome.Jackpot, 25) };
            var random = new System.Random(3);
            var counts = new Dictionary<PhoneOutcome, int>();
            for (int i = 0; i < 20000; i++)
            {
                var o = SlotMachine.Roll(table, random);
                counts[o] = counts.TryGetValue(o, out var c) ? c + 1 : 1;
            }
            Assert.That(counts.ContainsKey(PhoneOutcome.Nothing), Is.False);
            Assert.That(counts[PhoneOutcome.Jackpot] / 20000f, Is.EqualTo(.25f).Within(.02f));
        }

        [Test]
        public void TheDefaultTableAddsUpToAHundredAndIsAlmostEvenOnMoney()
        {
            var settings = ScriptableObject.CreateInstance<PhoneSettings>();
            Assert.That(settings.chances.Sum(c => c.percent), Is.EqualTo(100).Within(.001f));
            Assert.That(SlotMachine.ExpectedCoins(settings), Is.EqualTo(9.8).Within(.01), "Board: ~9.8 coins back per 10-coin spin.");
            Assert.That((settings.SpinsAt(0), settings.SpinsAt(1), settings.SpinsAt(2), settings.SpinsAt(3), settings.SpinsAt(9)), Is.EqualTo((10, 15, 20, 30, 30)));
            Assert.That((settings.BatteryPriceAt(0), settings.BatteryPriceAt(2), settings.BatteryPriceAt(3)), Is.EqualTo((1000, 4000, 0)));
            Object.DestroyImmediate(settings);
        }
    }

    public sealed class SpecialItemInventoryTests
    {
        [Test]
        public void TheSaveKeepsPurchasesIntroductionsAndTheBattery()
        {
            var inventory = new SpecialItemInventory();
            Assert.That(inventory.Add("phone"), Is.True);
            Assert.That(inventory.Add("phone"), Is.False, "Bought once and for good.");
            inventory.MarkIntroduced("phone");
            inventory.MarkSeen("phone");
            inventory.SetPhoneBattery(2);
            var copy = new SpecialItemInventory();
            copy.Restore(JsonUtility.FromJson<SpecialItemsSave>(JsonUtility.ToJson(inventory.Save())), id => id == "phone");
            Assert.That((copy.Owns("phone"), copy.WasIntroduced("phone"), copy.WasSeen("phone"), copy.LastSelected, copy.PhoneBatteryLevel),
                Is.EqualTo((true, true, true, "phone", 2)));
        }

        [Test]
        public void AnOldSaveHasNothingAndUnknownItemsAreDropped()
        {
            var inventory = new SpecialItemInventory();
            inventory.Restore(null, null);
            Assert.That(inventory.Owned, Is.Empty);
            inventory.Restore(new SpecialItemsSave { owned = new List<string> { "gone", "phone" }, lastSelected = "gone" }, id => id == "phone");
            Assert.That(inventory.Owned, Is.EqualTo(new[] { "phone" }));
            Assert.That(inventory.LastSelected, Is.EqualTo("phone"));
        }
    }

    public sealed class PhoneBatteryTrackTests
    {
        [Test]
        public void TheBatteryTrackShowsOnceThePhoneIsBoughtAndSpendsTheWallet()
        {
            var w = new DeckWorkshop(new DeckRules(startingCoins: 1500), new[] { new CardSpec("fire", "Огонь", 100) }, 0);
            var go = new GameObject("items");
            var host = go.AddComponent<SpecialItemsHost>();
            host.phone = ScriptableObject.CreateInstance<PhoneSettings>();
            Assert.That(PcUpgradeTrack.For(w, null, null, host).Any(t => t.Title == "Батарея телефона"), Is.False);
            host.Inventory.Add("phone");
            var battery = PcUpgradeTrack.For(w, null, null, host).Single(t => t.Title == "Батарея телефона");
            Assert.That((battery.Step, battery.Steps, battery.Price, battery.Values), Is.EqualTo((0, 3, 1000, "10 » 15 спинов")));
            Assert.That(battery.Buy(), Is.Null);
            Assert.That((host.Inventory.PhoneBatteryLevel, w.Coins), Is.EqualTo((1, 500)));
            Assert.That(battery.Buy(), Is.EqualTo("Не хватает монет."));
            Object.DestroyImmediate(host.phone);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void ThePhonesCardIsOneUseAndGoesOverTheHandLimit()
        {
            var teleport = new CardSpec("teleport", "Телепорт", 100, 3, usesPerLevel: new[] { 2, 3, 4 });
            var trip = new Expedition(new CardCopy[0], 1, 1);
            var first = trip.GrantToHand(teleport, 1, 1);
            var card = trip.GrantToHand(teleport, 1, 1);
            Assert.That(card.UsesLeft, Is.EqualTo(1), "One throw, though the level has two uses.");
            Assert.That(trip.Deck.Hand.Count, Is.EqualTo(2), "Over a hand of one.");
            Assert.That(trip.Deck.Hand, Does.Contain(first));
            Assert.That(trip.Deck.Hand, Does.Contain(card));
        }
    }
}
