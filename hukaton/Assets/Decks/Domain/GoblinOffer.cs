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
    /// </summary>
    public sealed class GoblinOffer
    {
        public const int Size = 10;
        /// <summary>Chances of ranks 1–5, in percent («стартовый баланс»).</summary>
        public static readonly int[] RankWeights = { 50, 30, 15, 4, 1 };

        readonly string[] cards;
        readonly bool[] sold;
        public bool Revealed { get; private set; }
        public int Count => cards.Length;

        GoblinOffer(string[] cards, bool[] sold, bool revealed)
        {
            this.cards = cards;
            this.sold = sold;
            Revealed = revealed;
        }

        /// <param name="dummyChance">Chance (0–1) for each place to hold the catalogue's dummy (<see cref="CardSpec.Dummy"/>)
        /// instead of a rolled card. Dummies never enter the rank draw; with no dummy or a zero chance the ten are rolled
        /// exactly as before (no extra random numbers are taken).</param>
        public static GoblinOffer Roll(IEnumerable<CardSpec> catalogue, Random random, double dummyChance = 0)
        {
            var specs = catalogue.ToList();
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
            return new GoblinOffer(picks, new bool[Size], false);
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
        /// face down it passes for any card, face up it is just cheap.
        /// </summary>
        public int PriceAt(int slot, DeckWorkshop workshop)
        {
            var spec = workshop.Spec(cards[slot]);
            return spec == null ? 0 : Revealed && !spec.Dummy ? spec.Price : spec.BlindPrice;
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

        /// <summary>Turns every card still on offer face up. Free; cannot be undone.</summary>
        public bool RevealAll()
        {
            if (Revealed) return false;
            Revealed = true;
            return true;
        }

        public GoblinOfferSave Save() => new GoblinOfferSave { cards = cards.ToList(), sold = sold.ToList(), revealed = Revealed };

        public static GoblinOffer Restore(GoblinOfferSave save, Func<string, bool> known)
        {
            if (save?.cards == null || save.sold == null || save.cards.Count != Size || save.sold.Count != Size
                || save.cards.Any(c => c == null || !known(c)))
                return null;
            return new GoblinOffer(save.cards.ToArray(), save.sold.ToArray(), save.revealed);
        }
    }

    /// <summary>
    /// When the goblin can be called. The first call is open at once; after a visit the next one needs an outing that
    /// came home and a worked shift (at least one sale, then the shift closed). The ten cards are rolled when he comes
    /// down and kept until the next call, saves included. The first conversation's lines are read once.
    /// </summary>
    public sealed class GoblinVisits
    {
        bool visited, outingSince, shiftSince;
        public GoblinOffer Offer { get; private set; }
        /// <summary>How many lines of the first conversation were closed.</summary>
        public int IntroductionRead { get; private set; }
        public event Action Changed;

        /// <summary>The rope calls him down (a fresh ten) only when the conditions are met.</summary>
        public bool CanCall => !visited || outingSince && shiftSince;

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
        /// A new visit: rolls a new ten. False when the goblin cannot be called yet. <paramref name="dummyChance"/>: each
        /// place's chance to hold a dummy (<see cref="GoblinOffer.Roll"/>); the very first visit, the introduction, never
        /// lays out a dummy.
        /// </summary>
        public bool Call(IEnumerable<CardSpec> catalogue, Random random, double dummyChance = 0)
        {
            if (!CanCall) return false;
            var offer = GoblinOffer.Roll(catalogue, random, visited ? dummyChance : 0);
            visited = true;
            outingSince = shiftSince = false;
            Offer = offer;
            Changed?.Invoke();
            return true;
        }

        public void ReadIntroductionLine(int total)
        {
            if (IntroductionRead >= total) return;
            IntroductionRead++;
            Changed?.Invoke();
        }

        public void Touch() => Changed?.Invoke();

        public GoblinSave Save() => new GoblinSave { visited = visited, outingSince = outingSince, shiftSince = shiftSince,
            introductionRead = IntroductionRead, offer = Offer?.Save() };

        public bool TryRestore(GoblinSave save, Func<string, bool> known)
        {
            if (save == null || save.introductionRead < 0) return false;
            var offer = save.offer?.cards == null || save.offer.cards.Count == 0 ? null : GoblinOffer.Restore(save.offer, known);
            if (save.offer?.cards != null && save.offer.cards.Count > 0 && offer == null) return false;
            visited = save.visited;
            outingSince = save.outingSince;
            shiftSince = save.shiftSince;
            IntroductionRead = save.introductionRead;
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
    }

    [Serializable]
    public sealed class GoblinSave
    {
        public bool visited, outingSince, shiftSince;
        public int introductionRead;
        public GoblinOfferSave offer;
    }
}
