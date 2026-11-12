using System;
using System.Collections.Generic;
using Hakaton.Decks;
using Hakaton.Trading;

namespace Hakaton.Computer
{
    /// <summary>
    /// One thing the PC can improve, as a row of segments: bought steps are filled. Pressing the row buys the next
    /// step. New improvements are new tracks; the window lays out whatever it is given.
    /// </summary>
    public abstract class PcUpgradeTrack
    {
        public abstract string Title { get; }
        /// <summary>Steps bought so far.</summary>
        public abstract int Step { get; }
        /// <summary>Steps that can be bought in total.</summary>
        public abstract int Steps { get; }
        /// <summary>"3 → 4", or the final value when maxed.</summary>
        public abstract string Values { get; }
        public abstract int Price { get; }
        public bool Maxed => Step >= Steps;
        /// <summary>Buys the next step. Returns an error to show, or null.</summary>
        public abstract string Buy();
        /// <summary>Said after a successful purchase.</summary>
        public abstract string Bought { get; }

        /// <summary>"4 карты", "5 карт": Russian plural for a card count.</summary>
        public static string Cards(int n)
        {
            int last = n % 10, lastTwo = n % 100;
            string word = last == 1 && lastTwo != 11 ? "карта" : last >= 2 && last <= 4 && (lastTwo < 12 || lastTwo > 14) ? "карты" : "карт";
            return n + " " + word;
        }

        // The pixel font has no arrow: » reads as one.
        const string Arrow = " » ";

        /// <summary>"30 с": a whole number of seconds.</summary>
        public static string Seconds(float seconds) => Whole(seconds) + " с";

        static string Whole(float seconds) => ((int)Math.Round(seconds)).ToString();

        /// <summary>"15%": a chance (0..1) as whole percent.</summary>
        public static string Percent(float chance) => ((int)Math.Round(chance * 100)) + "%";

        /// <summary>
        /// The PC's improvements: hand size and the deck limit (deck workshop), then the shift length when the shop is
        /// given and has upgrades to sell, then the bulk buyer's chance — only once the player has met him (a surprise
        /// until then).
        /// </summary>
        public static IReadOnlyList<PcUpgradeTrack> For(DeckWorkshop model, TradingSession shop = null)
        {
            var tracks = new List<PcUpgradeTrack> { new HandTrack(model), new DeckTrack(model) };
            if (shop != null && shop.ShiftSteps > 0) tracks.Add(new ShiftTrack(shop));
            if (shop != null && shop.BulkBuyerMet && shop.BulkBuyerSteps > 0) tracks.Add(new BulkBuyerTrack(shop));
            return tracks;
        }

        sealed class HandTrack : PcUpgradeTrack
        {
            readonly DeckWorkshop model;
            public HandTrack(DeckWorkshop model) => this.model = model;
            public override string Title => "Размер руки";
            public override int Step => model.HandSize - model.Rules.InitialHand;
            public override int Steps => model.Rules.MaxHand - model.Rules.InitialHand;
            public override string Values => Maxed ? Cards(model.HandSize) : model.HandSize + Arrow + (model.HandSize + 1);
            public override int Price => Maxed ? 0 : model.HandPrice;
            public override string Buy() => model.ImproveHand();
            public override string Bought => $"В руке теперь {Cards(model.HandSize)}.";
        }

        sealed class DeckTrack : PcUpgradeTrack
        {
            readonly DeckWorkshop model;
            public DeckTrack(DeckWorkshop model) => this.model = model;
            public override string Title => "Размер колоды";
            public override int Step
            {
                get
                {
                    for (int i = 0; i < model.Rules.DeckSteps; i++)
                        if (model.Rules.DeckLimitAt(i) == model.DeckLimit) return i;
                    return 0;
                }
            }
            public override int Steps => model.Rules.DeckSteps - 1;
            public override string Values => model.DeckAtMaximum ? Cards(model.DeckLimit) : model.DeckLimit + Arrow + model.NextDeckLimit;
            public override int Price => model.DeckLimitPrice;
            public override string Buy() => model.ImproveDeckLimit();
            public override string Bought => $"Колода вмещает {Cards(model.DeckLimit)}, коллекция — {model.CollectionLimit}.";
        }

        sealed class ShiftTrack : PcUpgradeTrack
        {
            readonly TradingSession shop;
            public ShiftTrack(TradingSession shop) => this.shop = shop;
            public override string Title => "Время смены";
            public override int Step => shop.ShiftLevel;
            public override int Steps => shop.ShiftSteps;
            public override string Values => Maxed ? Seconds(shop.ShiftLength) : Whole(shop.ShiftLength) + Arrow + Seconds(shop.NextShiftLength);
            public override int Price => shop.ShiftPrice;
            public override string Buy() => shop.ImproveShift();
            public override string Bought => $"Смена длится {Seconds(shop.ShiftLength)}.";
        }

        sealed class BulkBuyerTrack : PcUpgradeTrack
        {
            readonly TradingSession shop;
            public BulkBuyerTrack(TradingSession shop) => this.shop = shop;
            public override string Title => "Скупщик";
            public override int Step => shop.BulkBuyerLevel;
            public override int Steps => shop.BulkBuyerSteps;
            public override string Values => Maxed ? Percent(shop.BulkBuyerChance)
                : Percent(shop.BulkBuyerChance) + Arrow + Percent(shop.NextBulkBuyerChance);
            public override int Price => shop.BulkBuyerUpgradePrice;
            public override string Buy() => shop.ImproveBulkBuyer();
            public override string Bought => $"Скупщик заходит чаще: {Percent(shop.BulkBuyerChance)}.";
        }
    }
}
