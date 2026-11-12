using System;
using System.Text.RegularExpressions;
using Hakaton.Economy;
using NUnit.Framework;
using UnityEngine;

namespace Hakaton.Trading.Tests
{
    /// <summary>
    /// Playtest: the shift is cut to 30 s and the PC sells «Время смены», +10 s a step. The upgrade lives in the shop
    /// domain: it spends the shared wallet, lengthens the shift and the time left in it, survives saves, and old saves
    /// (no level, more time than a shift now lasts) still load.
    /// </summary>
    public sealed class TradingShiftUpgradeTests
    {
        static readonly ItemDefinition Find = new ItemDefinition("common_find", "Обычная находка", "Находки", 20);

        static TradeSettings Settings() => new TradeSettings {
            ShiftSeconds = 30, ShiftUpgradeSeconds = new[] { 10f, 10f, 10f }, ShiftUpgradePrices = new[] { 150, 250, 400 }
        };

        static TradingSession Create(CoinWallet wallet = null, TradeSettings settings = null)
        {
            var session = new TradingSession(settings ?? Settings(), 7, wallet);
            session.SeedInventory(Find, 2);
            Assert.That(session.PlaceItem(0, 0), Is.True);
            return session;
        }

        static void Trade(TradingSession session, float seconds)
        {
            Assert.That(session.Open(), Is.True);
            session.Tick(seconds);
            session.Close();
        }

        [Test]
        public void UpgradeSpendsTheSharedWalletLengthensTheShiftAndTheTimeLeft()
        {
            var wallet = new CoinWallet(1000);
            var session = Create(wallet);
            Assert.That((session.ShiftLevel, session.ShiftSteps, session.ShiftLength, session.RemainingSeconds), Is.EqualTo((0, 3, 30f, 30f)));
            Assert.That((session.ShiftPrice, session.NextShiftLength, session.ShiftAtMaximum), Is.EqualTo((150, 40f, false)));
            Trade(session, 12);
            Assert.That(session.RemainingSeconds, Is.EqualTo(18));

            int changes = 0;
            session.Changed += () => changes++;
            Assert.That(session.ImproveShift(), Is.Null);
            Assert.That((session.ShiftLevel, session.ShiftLength, session.RemainingSeconds), Is.EqualTo((1, 40f, 28f)));
            Assert.That((wallet.Balance, session.Gold, changes), Is.EqualTo((850, 850, 1)));
            Assert.That((session.ShiftPrice, session.NextShiftLength), Is.EqualTo((250, 50f)));

            Assert.That(session.ImproveShift(), Is.Null);
            Assert.That(session.ImproveShift(), Is.Null);
            Assert.That((session.ShiftLevel, session.ShiftLength, session.RemainingSeconds), Is.EqualTo((3, 60f, 48f)));
            Assert.That((session.ShiftAtMaximum, session.ShiftPrice, session.NextShiftLength), Is.EqualTo((true, 0, 60f)));
            Assert.That(wallet.Balance, Is.EqualTo(1000 - 150 - 250 - 400));
            Assert.That(session.ImproveShift(), Is.Not.Null, "Nothing left to buy.");
            Assert.That((session.ShiftLevel, wallet.Balance), Is.EqualTo((3, 200)));
        }

        [Test]
        public void UpgradeIsRefusedWhileTradingOnAnOutingOrWithoutCoins()
        {
            var wallet = new CoinWallet(1000);
            var session = Create(wallet);
            Assert.That(session.Open(), Is.True);
            Assert.That(session.ImproveShift(), Is.Not.Null, "Not while the shop is open.");
            session.Close();
            long outing = session.BeginExpedition();
            Assert.That(session.ImproveShift(), Is.Not.Null, "Not during an outing.");
            Assert.That(session.CompleteExpedition(outing), Is.True);
            Assert.That((session.ShiftLevel, wallet.Balance), Is.EqualTo((0, 1000)));

            Assert.That(wallet.TrySpend(901), Is.True);
            Assert.That(session.ImproveShift(), Is.Not.Null, "99 coins do not buy a 150-coin step.");
            Assert.That((session.ShiftLevel, session.ShiftLength, wallet.Balance), Is.EqualTo((0, 30f, 99)));
        }

        [Test]
        public void FinishedShiftWaitsForFreshLootWhichRefillsTheUpgradedLength()
        {
            var session = Create(new CoinWallet(1000));
            Trade(session, 30);
            Assert.That(session.RemainingSeconds, Is.Zero);
            Assert.That(session.ImproveShift(), Is.Null);
            Assert.That((session.ShiftLength, session.RemainingSeconds), Is.EqualTo((40f, 0f)), "A finished shift is not reopened.");

            long empty = session.BeginExpedition();
            Assert.That(session.CompleteExpedition(empty), Is.True);
            Assert.That(session.RemainingSeconds, Is.Zero, "Coming back empty-handed restores nothing.");
            long outing = session.BeginExpedition();
            Assert.That(session.CollectLoot(outing, Find), Is.True);
            Assert.That(session.CompleteExpedition(outing), Is.True);
            Assert.That(session.RemainingSeconds, Is.EqualTo(40), "Fresh loot refills the whole upgraded shift.");
        }

        [Test]
        public void ShiftLevelRoundTripsAndOldSavesLoadAtTheBaseShift()
        {
            var wallet = new CoinWallet(1000);
            var session = Create(wallet);
            Assert.That(session.ImproveShift(), Is.Null);
            Assert.That(session.ImproveShift(), Is.Null);
            Trade(session, 5);
            var save = session.Save();
            Assert.That((save.shiftLevel, save.remainingSeconds, save.version), Is.EqualTo((2, 45f, 1)));

            string json = JsonUtility.ToJson(save);
            var restored = Create(wallet);
            Assert.That(restored.TryRestore(JsonUtility.FromJson<TradingSave>(json)), Is.True);
            Assert.That((restored.ShiftLevel, restored.ShiftLength, restored.RemainingSeconds), Is.EqualTo((2, 50f, 45f)));
            Assert.That(restored.ShiftPrice, Is.EqualTo(400));

            // A save written before the upgrade existed has no level at all.
            Assert.That(json, Does.Contain("\"shiftLevel\":2"));
            string old = Regex.Replace(json, ",?\"shiftLevel\":\\d+", "");
            Assert.That(old, Does.Not.Contain("shiftLevel"));
            var oldSave = JsonUtility.FromJson<TradingSave>(old);
            Assert.That(oldSave.shiftLevel, Is.Zero);
            var fromOld = Create(wallet);
            Assert.That(fromOld.TryRestore(oldSave), Is.True);
            Assert.That((fromOld.ShiftLevel, fromOld.ShiftLength, fromOld.RemainingSeconds), Is.EqualTo((0, 30f, 30f)),
                "45 s left from the longer shift are clamped to the base 30 s.");
        }

        [Test]
        public void MoreTimeThanAShiftNowLastsIsClampedNotTakenForDamage()
        {
            var session = Create();
            var save = session.Save();
            save.remainingSeconds = 120; // the old 120-second shift
            Assert.That(session.CanRestore(save), Is.True);
            Assert.That(session.TryRestore(save), Is.True);
            Assert.That(session.RemainingSeconds, Is.EqualTo(30));

            save.shiftLevel = 99; // more upgrades than the settings sell now
            Assert.That(session.TryRestore(save), Is.True);
            Assert.That((session.ShiftLevel, session.ShiftLength, session.RemainingSeconds), Is.EqualTo((3, 60f, 60f)));

            int revision = session.Revision;
            foreach (var broken in new Action<TradingSave>[] {
                s => s.shiftLevel = -1, s => s.remainingSeconds = -1, s => s.remainingSeconds = float.NaN,
                s => s.remainingSeconds = float.PositiveInfinity })
            {
                var damaged = session.Save();
                broken(damaged);
                Assert.That(session.CanRestore(damaged), Is.False);
                Assert.That(session.TryRestore(damaged), Is.False);
            }
            Assert.That(session.Revision, Is.EqualTo(revision), "A damaged save changes nothing.");
        }

        [Test]
        public void SettingsValidateTheUpgradesAndKeepTheirOwnCopy()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TradingSession(new TradeSettings {
                ShiftUpgradeSeconds = new[] { 10f, 10f }, ShiftUpgradePrices = new[] { 150 } }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TradingSession(new TradeSettings {
                ShiftUpgradeSeconds = new[] { 0f }, ShiftUpgradePrices = new[] { 150 } }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TradingSession(new TradeSettings {
                ShiftUpgradeSeconds = new[] { 10f }, ShiftUpgradePrices = new[] { -1 } }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TradingSession(new TradeSettings {
                ShiftUpgradeSeconds = new[] { float.PositiveInfinity }, ShiftUpgradePrices = new[] { 1 } }));
            var defaults = new TradingSession();
            Assert.That((defaults.ShiftSteps, defaults.ShiftAtMaximum, defaults.ShiftPrice, defaults.ShiftLength), Is.EqualTo((0, true, 0, 120f)));

            var settings = Settings();
            var session = new TradingSession(settings);
            settings.ShiftUpgradeSeconds[0] = 500;
            settings.ShiftUpgradePrices[0] = 1;
            Assert.That((session.NextShiftLength, session.ShiftPrice), Is.EqualTo((40f, 150)), "Later edits to the settings do not leak in.");
        }

        [Test]
        public void CheatMaximizesTheShiftForFree()
        {
            var wallet = new CoinWallet(5);
            var session = Create(wallet);
            Trade(session, 10);
            Assert.That(session.MaximizeShift(), Is.True);
            Assert.That((session.ShiftLevel, session.ShiftLength, session.RemainingSeconds, wallet.Balance), Is.EqualTo((3, 60f, 50f, 5)));
            Assert.That(session.MaximizeShift(), Is.False);
        }
    }
}
