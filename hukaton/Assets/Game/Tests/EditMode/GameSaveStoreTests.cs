using System;
using System.IO;
using System.Linq;
using Hakaton.Decks;
using Hakaton.Economy;
using Hakaton.Trading;
using NUnit.Framework;
using UnityEngine;

namespace Hakaton.Game.Tests
{
    /// <summary>Uses an isolated temporary directory; never reads or changes the player's save.</summary>
    public sealed class GameSaveStoreTests
    {
        static readonly ItemDefinition Find = new ItemDefinition("common_find", "Обычная находка", "Находки", 20);
        static readonly ItemDefinition Tusk = new ItemDefinition("boar_tusk", "Кабаний клык", "Трофеи", 120);
        static readonly CardSpec[] Cards = {
            new CardSpec("wind", "Импульс", DeckKind.Exploration, 30, 20, 3, 1),
            new CardSpec("hit", "Удар", DeckKind.Combat, 40, 20, 8, 2)
        };
        string directory, path;

        [SetUp]
        public void CreateTemporarySaveDirectory()
        {
            directory = Path.Combine(Path.GetTempPath(), "HakatonGameSaveTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "game.json");
        }

        [TearDown]
        public void RemoveOnlyThisTestsFiles()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        sealed class Domains
        {
            public readonly CoinWallet Wallet = new CoinWallet();
            public readonly DeckWorkshop Workshop;
            public readonly TradingSession Shop;
            public Domains()
            {
                Workshop = new DeckWorkshop(new DeckRules(maxCardLevel: 2), Cards, 1, Wallet);
                Shop = new TradingSession(wallet: Wallet);
                Shop.SeedInventory(Find, 0);
                Shop.SeedInventory(Tusk, 0);
            }
            public GameSave Capture() => new GameSave { coins = Wallet.Balance, cards = Workshop.Save(), shop = Shop.Save() };
            // The store accepts the caller's domain validator; these fixtures need no scene or MonoBehaviour setup.
            public bool Validate(GameSave save) => save != null && save.version == 1 && save.coins >= 0
                && save.cards != null && save.cards.coins == save.coins
                && (save.cards.goods == null || save.cards.goods.Count == 0)
                && Workshop.CanRestoreProgress(save.cards) && Shop.CanRestore(save.shop);
        }

        void Save(GameSave save)
        {
            Assert.That(GameSaveStore.Save(path, save, out var error), Is.True, error);
            Assert.That(error, Is.Null);
        }

        [Test]
        public void MissingFilesAreANewGameWithoutACorruptionWarning()
        {
            var domains = new Domains();
            Assert.That(GameSaveStore.Load(path, domains.Validate, out var save, out var warning), Is.False);
            Assert.That(save, Is.Null);
            Assert.That(warning, Is.Null);
            Assert.That(Directory.GetFiles(directory), Is.Empty);
        }

        [Test]
        public void ValidPrimaryWinsAndPreviousSnapshotIsRetainedAsBackup()
        {
            var domains = new Domains();
            domains.Wallet.Credit(20); Save(domains.Capture());
            domains.Wallet.Credit(70); Save(domains.Capture());
            Assert.That(GameSaveStore.Load(path, domains.Validate, out var loaded, out var warning), Is.True);
            Assert.That(loaded.coins, Is.EqualTo(90));
            Assert.That(warning, Is.Null);
            Assert.That(JsonUtility.FromJson<GameSave>(File.ReadAllText(path + ".bak")).coins, Is.EqualTo(20));
            Assert.That(File.Exists(path + ".tmp"), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingOrMalformedPrimaryFallsBackWithoutRewritingEitherFile(bool missing)
        {
            var domains = new Domains();
            domains.Wallet.Credit(20); Save(domains.Capture());
            domains.Wallet.Credit(70); Save(domains.Capture());
            string backup = File.ReadAllText(path + ".bak");
            const string malformed = "{ broken save";
            if (missing) File.Delete(path); else File.WriteAllText(path, malformed);
            Assert.That(GameSaveStore.Load(path, domains.Validate, out var loaded, out var warning), Is.True);
            Assert.That(loaded.coins, Is.EqualTo(20));
            Assert.That(warning, Is.Not.Null.And.Not.Empty);
            Assert.That(File.ReadAllText(path + ".bak"), Is.EqualTo(backup));
            if (missing) Assert.That(File.Exists(path), Is.False);
            else Assert.That(File.ReadAllText(path), Is.EqualTo(malformed));
        }

        [TestCase("wallet mismatch")]
        [TestCase("duplicate card")]
        [TestCase("unknown stock")]
        [TestCase("invalid slot")]
        [TestCase("invalid clock")]
        [TestCase("invalid revenue")]
        [TestCase("second goods inventory")]
        [TestCase("unsupported version")]
        public void StructurallyValidJsonRejectedByDomainsFallsBackToTheGoodBackup(string corruption)
        {
            var domains = new Domains();
            domains.Shop.SeedInventory(Find); domains.Shop.PlaceItem(0, 0);
            domains.Wallet.Credit(20); Save(domains.Capture());
            domains.Wallet.Credit(70); Save(domains.Capture());
            var damaged = domains.Capture();
            switch (corruption)
            {
                case "wallet mismatch": damaged.coins++; break;
                case "duplicate card": damaged.cards.copies[1].id = damaged.cards.copies[0].id; break;
                case "unknown stock": damaged.shop.items[0].definitionId = "missing"; break;
                case "invalid slot": damaged.shop.items[0].slot = 6; break;
                case "invalid clock": damaged.shop.remainingSeconds = -1; break;
                case "invalid revenue": damaged.shop.grossRevenue = -1; break;
                case "second goods inventory": damaged.cards.goods.Add(new LootSave { id = Find.Id, name = Find.Name, count = 1, value = 20 }); break;
                case "unsupported version": damaged.version = 999; break;
            }
            string json = JsonUtility.ToJson(damaged);
            File.WriteAllText(path, json);
            var destination = new Domains();
            Assert.That(GameSaveStore.Load(path, destination.Validate, out var loaded, out var warning), Is.True);
            Assert.That(loaded.coins, Is.EqualTo(20));
            Assert.That(warning, Is.Not.Null.And.Not.Empty);
            Assert.That(File.ReadAllText(path), Is.EqualTo(json));
            Assert.That(destination.Wallet.Balance, Is.Zero, "Reading must not mutate the live game.");
            Assert.That(destination.Shop.TotalStockCount, Is.Zero);
            Assert.That(destination.Workshop.Owned.Count, Is.EqualTo(2));
        }

        [Test]
        public void TwoInvalidFilesReturnNoSnapshotAndPreserveBothForRecovery()
        {
            const string primary = "{broken";
            const string backup = "{\"version\":999}";
            File.WriteAllText(path, primary);
            File.WriteAllText(path + ".bak", backup);
            var domains = new Domains();
            Assert.That(GameSaveStore.Load(path, domains.Validate, out var loaded, out var warning), Is.False);
            Assert.That(loaded, Is.Null);
            Assert.That(warning, Is.Not.Null.And.Not.Empty);
            Assert.That(File.ReadAllText(path), Is.EqualTo(primary));
            Assert.That(File.ReadAllText(path + ".bak"), Is.EqualTo(backup));
        }

        [Test]
        public void FailedTemporaryWriteCannotOverwriteThePreviouslySavedGame()
        {
            var domains = new Domains();
            domains.Wallet.Credit(20); Save(domains.Capture());
            string previous = File.ReadAllText(path);
            Directory.CreateDirectory(path + ".tmp");
            domains.Wallet.Credit(70);
            Assert.That(GameSaveStore.Save(path, domains.Capture(), out var error), Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
            Assert.That(File.ReadAllText(path), Is.EqualTo(previous));
            Assert.That(GameSaveStore.Load(path, domains.Validate, out var loaded, out _), Is.True);
            Assert.That(loaded.coins, Is.EqualTo(20));
        }

        [Test]
        public void WholeGameRoundTripKeepsRevenueDistinctFromSpentCoinsAndContinuesTheSameEconomy()
        {
            var source = new Domains();
            long outing = source.Shop.BeginExpedition();
            Assert.That(source.Workshop.Depart(42), Is.Null);
            source.Workshop.Current.DiscardHand();
            Assert.That(source.Workshop.Current.ReturnHome(), Is.True);
            var delivery = new[] { new StockDelivery(Tusk.Id, 2), new StockDelivery(Find.Id, 1) };
            Assert.That(source.Shop.CompleteExpedition(outing, delivery), Is.True);
            for (int slot = 0; slot < 3; slot++) Assert.That(source.Shop.PlaceItem(0, slot), Is.True);
            SellAtReferencePrice(source.Shop, 0);
            source.Shop.Tick(37.5f); source.Shop.Close();
            Assert.That(source.Workshop.Buy("wind", out var bought), Is.Null);
            Assert.That(source.Workshop.Upgrade(bought.Id), Is.Null);
            Assert.That(source.Workshop.ImproveHand(DeckKind.Exploration), Is.Null);
            int originalWind = source.Workshop.Owned.First(copy => copy.Spec.Id == "wind" && copy.Id != bought.Id).Id;
            source.Workshop.Toggle(originalWind); source.Workshop.Toggle(bought.Id);
            var captured = source.Capture();
            Save(captured);

            var restored = new Domains();
            Assert.That(GameSaveStore.Load(path, restored.Validate, out var loaded, out var warning), Is.True);
            Assert.That(warning, Is.Null);
            Assert.That(restored.Workshop.TryRestoreProgress(loaded.cards), Is.True);
            Assert.That(restored.Shop.TryRestore(loaded.shop), Is.True);
            Assert.That(restored.Wallet.Balance, Is.Zero, "Subsystem restore must not mint coins.");
            Assert.That(restored.Wallet.TryRestoreBalance(loaded.coins), Is.True);
            Assert.That(restored.Wallet.Balance, Is.EqualTo(10));
            Assert.That(restored.Shop.Gold, Is.EqualTo(10));
            Assert.That(restored.Workshop.Coins, Is.EqualTo(10));
            Assert.That(restored.Shop.GrossRevenue, Is.EqualTo(120));
            Assert.That(restored.Shop.RemainingSeconds, Is.EqualTo(82.5f));
            Assert.That(restored.Shop.Slots[0], Is.Null);
            Assert.That(restored.Shop.Slots[1].Id, Is.EqualTo(source.Shop.Slots[1].Id));
            Assert.That(restored.Shop.Slots[2].Definition.Id, Is.EqualTo(Find.Id));
            Assert.That(restored.Workshop.Owned.Count, Is.EqualTo(3));
            Assert.That(restored.Workshop.Find(bought.Id).Level, Is.EqualTo(2));
            Assert.That(restored.Workshop.Selected(DeckKind.Exploration).Single().Id, Is.EqualTo(bought.Id));
            Assert.That(restored.Workshop.HandSize(DeckKind.Exploration), Is.EqualTo(4));
            Assert.That(restored.Workshop.Journeys, Is.EqualTo(1));
            Assert.That(restored.Workshop.Goods, Is.Empty);
            Assert.That(restored.Shop.CompleteExpedition(outing, delivery), Is.False, "Old delivery cannot be credited after load.");

            SellAtReferencePrice(restored.Shop, 1);
            restored.Shop.Close();
            Assert.That(restored.Shop.GrossRevenue, Is.EqualTo(240));
            Assert.That(restored.Workshop.Coins, Is.EqualTo(130), "New sales credit the restored shared wallet.");
            Assert.That(restored.Workshop.Buy("hit", out _), Is.Null);
            Assert.That(restored.Shop.Gold, Is.EqualTo(90), "Card purchases debit the same restored wallet.");
            Assert.That(restored.Shop.GrossRevenue, Is.EqualTo(240), "Purchases never reduce the victory goal.");
            long nextOuting = restored.Shop.BeginExpedition();
            Assert.That(nextOuting, Is.GreaterThan(outing));
            Assert.That(restored.Workshop.Depart(84), Is.Null);
            Assert.That(restored.Workshop.Current.Active.Hand.Single().Level, Is.EqualTo(2));
        }

        static void SellAtReferencePrice(TradingSession shop, int slot)
        {
            Assert.That(shop.Open(), Is.True);
            shop.Refuse(shop.CurrentCustomer.Id);
            Assert.That(shop.AdvanceCustomer(new CustomerTerms { RequestedSlot = slot, AsksPrice = true, MaxPrice = 200 }), Is.True);
            var deal = shop.CurrentDeal;
            int revision = deal.Revision;
            Assert.That(shop.Offer(deal.Id, revision, 0), Is.True);
            Assert.That(shop.Offer(deal.Id, revision, 0), Is.False);
        }
    }
}
