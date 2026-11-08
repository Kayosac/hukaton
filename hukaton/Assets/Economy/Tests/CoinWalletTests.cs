using System;
using Hakaton.Decks;
using Hakaton.Trading;
using NUnit.Framework;

namespace Hakaton.Economy.Tests
{
    public sealed class CoinWalletTests
    {
        static readonly CardSpec[] Cards = {
            new CardSpec("wind", "Вихрь", DeckKind.Exploration, 30, 20, 3, 1),
            new CardSpec("fire", "Огонь", DeckKind.Combat, 40, 20, 8, 2)
        };

        [Test]
        public void InvalidAmountsAndOverflowNeverChangeTheBalance()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CoinWallet(-1));
            var wallet = new CoinWallet(int.MaxValue);
            Assert.Throws<ArgumentOutOfRangeException>(() => wallet.Credit(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => wallet.TrySpend(-1));
            Assert.Throws<OverflowException>(() => wallet.Credit(1));
            Assert.That(wallet.Balance, Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void InsufficientFundsAreRejectedButFreeTransactionsAreAllowed()
        {
            var wallet = new CoinWallet(12);
            Assert.That(wallet.TrySpend(13), Is.False);
            Assert.That(wallet.Balance, Is.EqualTo(12));
            Assert.That(wallet.TrySpend(12), Is.True);
            Assert.That(wallet.TrySpend(0), Is.True);
            wallet.Credit(0);
            Assert.That(wallet.Balance, Is.Zero);
        }

        [Test]
        public void TradeIncomeBuysAndUpgradesCardsFromTheSameBalance()
        {
            var wallet = new CoinWallet();
            var trading = new TradingSession(wallet: wallet);
            var workshop = new DeckWorkshop(new DeckRules(startingCoins: 240), Cards, 1, wallet);
            Assert.That(trading.Gold, Is.Zero);
            Assert.That(workshop.Coins, Is.Zero, "Injected wallets never receive standalone seed money.");
            trading.SeedInventory(new ItemDefinition("horn", "Рог", "Трофеи", 100));
            Assert.That(trading.PlaceItem(0, 0), Is.True);
            Assert.That(trading.Open(), Is.True);
            trading.Refuse(trading.CurrentCustomer.Id);
            Assert.That(trading.AdvanceCustomer(new CustomerTerms {
                RequestedSlot = 0, AsksPrice = true, MaxPrice = 130, Patience = 3
            }), Is.True);
            var deal = trading.CurrentDeal;
            int revision = deal.Revision;
            Assert.That(trading.Offer(deal.Id, revision, 0), Is.True);
            Assert.That(workshop.Coins, Is.EqualTo(100));
            Assert.That(workshop.Buy("wind", out var copy), Is.Null);
            Assert.That(workshop.Upgrade(copy.Id), Is.Null);
            Assert.That(trading.Gold, Is.EqualTo(50));
            Assert.That(workshop.ImproveHand(DeckKind.Exploration), Is.Not.Null);
            Assert.That(workshop.HandSize(DeckKind.Exploration), Is.EqualTo(3));
            Assert.That(trading.Offer(deal.Id, revision, 0), Is.False, "A repeated sale cannot credit the shared wallet twice.");
            Assert.That(wallet.Balance, Is.EqualTo(50));
            Assert.That(trading.SalesCount, Is.EqualTo(1));
        }

        [Test]
        public void FailedSaleCreditPreservesStockAndSettlement()
        {
            var wallet = new CoinWallet(int.MaxValue);
            var trading = new TradingSession(wallet: wallet);
            trading.SeedInventory(new ItemDefinition("horn", "Рог", "Трофеи", 100));
            trading.PlaceItem(0, 0); trading.Open(); trading.Refuse(trading.CurrentCustomer.Id);
            trading.AdvanceCustomer(new CustomerTerms { RequestedSlot = 0, AsksPrice = true, MaxPrice = 100 });
            var deal = trading.CurrentDeal;
            Assert.Throws<OverflowException>(() => trading.Offer(deal.Id, deal.Revision, 0));
            Assert.That(trading.Slots[0], Is.SameAs(deal.Item));
            Assert.That(trading.SalesCount, Is.Zero);
            Assert.That(trading.SettlementSequence, Is.Zero);
            Assert.That(wallet.Balance, Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void StandaloneSaveRestoresItsOwnBalanceButCannotOverwriteSharedEconomy()
        {
            var standalone = new DeckWorkshop(new DeckRules(), Cards, 1);
            Assert.That(standalone.Coins, Is.EqualTo(240));
            standalone.Buy("wind", out _);
            var save = standalone.Save();
            standalone.Upgrade(1);
            Assert.That(standalone.TryRestore(save), Is.True);
            Assert.That(standalone.Coins, Is.EqualTo(210));

            var wallet = new CoinWallet(17);
            var shared = new DeckWorkshop(new DeckRules(), Cards, 1, wallet);
            Assert.That(shared.TryRestore(save), Is.False);
            Assert.That(shared.Owned.Count, Is.EqualTo(2));
            Assert.That(wallet.Balance, Is.EqualTo(17));
        }
    }
}
