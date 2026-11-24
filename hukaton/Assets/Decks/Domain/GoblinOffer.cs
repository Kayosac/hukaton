using System;
using System.Collections.Generic;
using System.Linq;

namespace Hakaton.Decks
{
    /// <summary>
    /// Board, «Гоблин-торговец — актуальные правила» (09.11.2026). The goblin lays out ten random cards face down.
    /// For each place a rarity rank is rolled first (1 — 50%, 2 — 30%, 3 — 15%, 4 — 4%, 5 — 1%), then a card of that
    /// rank with equal chances; cards may repeat. A face-down card sells for half its price rounded up, without showing
    /// the card or the price; once bought it turns face up and its place stays empty. One free action turns all the
    /// remaining cards face up: from then on each costs its full price, and nothing can be hidden again.
    /// Playtest (12.11.2026): any place may instead hold a dummy («Пустышка», <see cref="CardSpec.Dummy"/>) by its own
    /// chance, the penalty for buying blind: it looks like any card face down and always costs its half price.
    /// Balance stage 4 (24.11.2026): a table may be laid «по-свойски» (<see cref="Friendly"/>, until the shop's first goal):
    /// every card face up from the start at its half price, no dummy, nothing to reveal. The caller decides the mode.
    /// </summary>
    public sealed class GoblinOffer
    {
        public const int Size = 10;
        /// <summary>Chances of ranks 1–5, in percent («стартовый баланс»).</summary>
        public static readonly int[] RankWeights = { 50, 30, 15, 4, 1 };

        readonly string[] cards;
        readonly bool[] sold;
        /// <summary>The cards lie face up: turned up by <see cref="RevealAll"/>, or laid so on a friendly table.</summary>
        public bool Revealed { get; private set; }
        /// <summary>
        /// A friendly table («по-свойски»): face up from the start, every card at its half price (<see cref="CardSpec.BlindPrice"/>),
        /// no dummy, and <see cref="RevealAll"/> has nothing to do. Kept in the save.
        /// </summary>
        public bool Friendly { get; }
        public int Count => cards.Length;

        GoblinOffer(string[] cards, bool[] sold, bool revealed, bool friendly)
        {
            this.cards = cards;
            this.sold = sold;
            Friendly = friendly;
            Revealed = revealed || friendly;
        }

        /// <param name="dummyChance">Chance (0–1) for each place to hold the catalogue's dummy (<see cref="CardSpec.Dummy"/>)
        /// instead of a rolled card. Dummies never enter the rank draw; with no dummy or a zero chance the ten are rolled
        /// exactly as before (no extra random numbers are taken).</param>
        /// <param name="friendly">A friendly table (<see cref="Friendly"/>): face up, half price, never a dummy (the chance is ignored).</param>
        public static GoblinOffer Roll(IEnumerable<CardSpec> catalogue, Random random, double dummyChance = 0, bool friendly = false)
        {
            var specs = catalogue.ToList();
            if (friendly) dummyChance = 0;
            var dummy = dummyChance > 0 ? specs.Where(s => s.Dummy).OrderBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault() : null;
            var byRank = specs.Where(s => !s.Dummy).GroupBy(s => s.Rank)
                .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Id, StringComparer.Ordinal).ToList());
            if (byRank.Count == 0) throw new ArgumentException("Empty catalogue", nameof(catalogue));
            var picks = new string[Size];
            for (int i = 0; i < Size; i++)
            {
                if (dummy != null && random.NextDouble() < dummyChance)
                {
                    picks[i] = dummy.Id;
                    continue;
                }
                int rank = RollRank(byRank.Keys, random);
                var options = byRank[rank];
                picks[i] = options[random.Next(options.Count)].Id;
            }
            return new GoblinOffer(picks, new bool[Size], friendly, friendly);
        }

        /// <summary>A rank by the board's weights, among the ranks that have cards (the others' share is spread evenly).</summary>
        static int RollRank(ICollection<int> present, Random random)
        {
            int total = 0;
            for (int rank = 1; rank <= RankWeights.Length; rank++) if (present.Contains(rank)) total += RankWeights[rank - 1];
            int roll = random.Next(total);
            for (int rank = 1; rank <= RankWeights.Length; rank++)
            {
                if (!present.Contains(rank)) continue;
                roll -= RankWeights[rank - 1];
                if (roll < 0) return rank;
            }
            return present.Min();
        }

        public string CardAt(int slot) => cards[slot];
        public bool IsSold(int slot) => sold[slot];
        public bool AnyLeft => sold.Any(s => !s);

        /// <summary>
        /// What the slot costs now: half (rounded up) face down, full once revealed. A dummy costs its half either way:
        /// face down it passes for any card, face up it is just cheap. On a friendly table every card costs its half.
        /// </summary>
        public int PriceAt(int slot, DeckWorkshop workshop)
        {
            var spec = workshop.Spec(cards[slot]);
            return spec == null ? 0 : Revealed && !Friendly && !spec.Dummy ? spec.Price : spec.BlindPrice;
        }

        /// <summary>
        /// Buys the slot's card at its current price. A refusal leaves everything as it was and names no price or
        /// shortfall (the face-down price stays a secret). A bought card goes straight into the deck when it has room.
        /// </summary>
        public string Buy(int slot, DeckWorkshop workshop, out CardCopy copy)
        {
            copy = null;
            if (slot < 0 || slot >= cards.Length || sold[slot]) return "Это место пустое.";
            if (workshop.CollectionFull) return "В коллекции нет места.";
            if (workshop.Coins < PriceAt(slot, workshop)) return "Не хватает монет.";
            string error = workshop.Purchase(cards[slot], PriceAt(slot, workshop), out copy, equip: true);
            if (error != null) return error;
            sold[slot] = true;
            return null;
        }

        /// <summary>Turns every card still on offer face up. Free; cannot be undone. A friendly table is face up already: false.</summary>
        public bool RevealAll()
        {
            if (Revealed || Friendly) return false;
            Revealed = true;
            return true;
        }

        public GoblinOfferSave Save() => new GoblinOfferSave { cards = cards.ToList(), sold = sold.ToList(), revealed = Revealed, friendly = Friendly };

        public static GoblinOffer Restore(GoblinOfferSave save, Func<string, bool> known)
        {
            if (save?.cards == null || save.sold == null || save.cards.Count != Size || save.sold.Count != Size
                || save.cards.Any(c => c == null || !known(c)))
                return null;
            return new GoblinOffer(save.cards.ToArray(), save.sold.ToArray(), save.revealed, save.friendly);
        }
    }

    /// <summary>
    /// When the goblin can be called: always, with no conditions — every pull of the rope brings him down. Balance stage 4
    /// (24.11.2026): he brings the same table (the same cards in the same places, the bought ones gone, the revealed ones
    /// face up) until the player comes back from an outing; a new ten is rolled only on the very first visit, on the first
    /// call after a return from an outing, and when the table's mode (<see cref="GoblinOffer.Friendly"/>) is not the one
    /// asked for (the first call after the shop's first goal). The table is kept in the save. The first conversation's
    /// lines and the explanation of the usual trade are each read once.
    /// </summary>
    public sealed class GoblinVisits
    {
        /// <summary>The save's format: 0 — saved before balance stage 4 (a four-line introduction, no explanation).</summary>
        public const int SaveVersion = 1;

        bool visited, outingSince, shiftSince;
        public GoblinOffer Offer { get; private set; }
        /// <summary>How many lines of the first conversation were closed.</summary>
        public int IntroductionRead { get; private set; }
        /// <summary>How many lines of the explanation of the usual trade (face down, after the first goal) were closed.</summary>
        public int ExplanationRead { get; private set; }
        public event Action Changed;

        /// <summary>He has been called down at least once in this game (saved; a new game starts without it).</summary>
        public bool Visited => visited;

        /// <summary>The rope always calls him down: no outing or shift is required between visits (see <see cref="Call"/>).</summary>
        public bool CanCall => true;

        public void OutingReturned()
        {
            if (!visited || outingSince) return;
            outingSince = true;
            Changed?.Invoke();
        }

        public void ShiftClosed(bool soldSomething)
        {
            if (!visited || shiftSince || !soldSomething) return;
            shiftSince = true;
            Changed?.Invoke();
        }

        /// <summary>
        /// A visit. A new ten is rolled only when there is no table yet, when the player has come back from an outing since
        /// it was rolled, or when its mode is not <paramref name="friendly"/>; otherwise he brings the same table.
        /// <paramref name="dummyChance"/>: each place's chance to hold a dummy (<see cref="GoblinOffer.Roll"/>), unused on a
        /// friendly table. False when the goblin cannot be called.
        /// </summary>
        public bool Call(IEnumerable<CardSpec> catalogue, Random random, double dummyChance = 0, bool friendly = false)
        {
            if (!CanCall) return false;
            if (NeedsNewTable(friendly))
            {
                Offer = GoblinOffer.Roll(catalogue, random, dummyChance, friendly);
                outingSince = shiftSince = false;
            }
            visited = true;
            Changed?.Invoke();
            return true;
        }

        /// <summary>The next <see cref="Call"/> in this mode rolls a new ten (no table, an outing since, or the other mode).</summary>
        public bool NeedsNewTable(bool friendly) => Offer == null || outingSince || Offer.Friendly != friendly;

        public void ReadIntroductionLine(int total)
        {
            if (IntroductionRead >= total) return;
            IntroductionRead++;
            Changed?.Invoke();
        }

        public void ReadExplanationLine(int total)
        {
            if (ExplanationRead >= total) return;
            ExplanationRead++;
            Changed?.Invoke();
        }

        public void Touch() => Changed?.Invoke();

        public GoblinSave Save() => new GoblinSave { version = SaveVersion, visited = visited, outingSince = outingSince,
            shiftSince = shiftSince, introductionRead = IntroductionRead, explanationRead = ExplanationRead, offer = Offer?.Save() };

        /// <summary>
        /// Brings a save from before balance stage 4 (<see cref="GoblinSave.version"/> 0) up to date, in place; a current
        /// save is left as it is. <paramref name="friendlyTradeOver"/>: the friendly trade has ended (the caller knows why —
        /// in the game, the shop's first goal is reached). A player who knows the goblin and is past it has heard the old
        /// introduction, which told the usual rules: the explanation counts as read. A player who knows him but is not past
        /// it keeps the friendly trade (his old face-down table is not in that mode, so the next call rolls a friendly one)
        /// and hears the explanation later. The old introduction had four lines: what was read is clamped to
        /// <paramref name="introductionLines"/>.
        /// </summary>
        public static void UpgradeSave(GoblinSave save, bool friendlyTradeOver, int introductionLines, int explanationLines)
        {
            if (save == null || save.version >= SaveVersion) return;
            if (save.visited && friendlyTradeOver) save.explanationRead = Math.Max(save.explanationRead, explanationLines);
            save.introductionRead = Math.Min(save.introductionRead, Math.Max(0, introductionLines));
            save.version = SaveVersion;
        }

        public bool TryRestore(GoblinSave save, Func<string, bool> known)
        {
            if (save == null || save.introductionRead < 0 || save.explanationRead < 0) return false;
            var offer = save.offer?.cards == null || save.offer.cards.Count == 0 ? null : GoblinOffer.Restore(save.offer, known);
            if (save.offer?.cards != null && save.offer.cards.Count > 0 && offer == null) return false;
            visited = save.visited;
            outingSince = save.outingSince;
            shiftSince = save.shiftSince;
            IntroductionRead = save.introductionRead;
            ExplanationRead = save.explanationRead;
            Offer = offer;
            Changed?.Invoke();
            return true;
        }
    }

    [Serializable]
    public sealed class GoblinOfferSave
    {
        public List<string> cards = new List<string>();
        public List<bool> sold = new List<bool>();
        public bool revealed;
        /// <summary>A friendly table (<see cref="GoblinOffer.Friendly"/>); older saves: false, a usual face-down table.</summary>
        public bool friendly;
    }

    [Serializable]
    public sealed class GoblinSave
    {
        /// <summary><see cref="GoblinVisits.SaveVersion"/>; 0 in saves from before balance stage 4 (see <see cref="GoblinVisits.UpgradeSave"/>).</summary>
        public int version;
        public bool visited, outingSince, shiftSince;
        public int introductionRead;
        /// <summary>Lines of the explanation of the usual trade closed.</summary>
        public int explanationRead;
        public GoblinOfferSave offer;
    }
}
