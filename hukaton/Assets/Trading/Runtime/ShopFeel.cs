using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Trading.Unity
{
    /// <summary>
    /// Sound and juice for the shop, driven only by observing the trade session: bells, visitor voices,
    /// coin bursts and a punchy wallet on sales, a ticking clock at the end of the shift, placement clacks.
    /// Never changes trade state.
    /// </summary>
    public sealed class ShopFeel : MonoBehaviour
    {
        public TradingPrototype trade;
        public TradingHud hud;
        public SfxPlayer sfx;
        public ShopAudio cues;
        public CameraFeel feel;
        [Tooltip("Authored coin burst at the counter, played on every sale.")]
        public ParticleSystem coinBurst;
        [Tooltip("Sales at or above this price use the big coin cascade.")]
        [Min(1)] public int bigSale = 100;
        public Color walletFlash = new Color(1f, .84f, .45f);

        long seenCustomer;
        int seenSettlement, seenDeparture, seenRevision = -1, lastTick = -1, displayedGold;
        bool seenOpen;
        float voiceAt = -1;
        CustomerMood pendingMood;
        TextPunch walletPunch;
        Color walletRest;
        float walletGlow;

        void OnEnable()
        {
            if (trade) { trade.ItemPlaced += OnPlaced; trade.ItemTaken += OnTaken; trade.SlotHovered += OnHovered; }
        }

        void OnDisable()
        {
            if (trade) { trade.ItemPlaced -= OnPlaced; trade.ItemTaken -= OnTaken; trade.SlotHovered -= OnHovered; }
            if (sfx) sfx.StopLoop(this);
        }

        void OnPlaced(int slot) => sfx?.Play(cues ? cues.placeItem : null, trade.World.SlotAnchors[slot].position);
        void OnTaken(int slot) => sfx?.Play(cues ? cues.takeItem : null, trade.World.SlotAnchors[slot].position);
        void OnHovered(int slot) => sfx?.Play(cues ? cues.slotHover : null);

        void Start()
        {
            var session = trade ? trade.Session : null;
            if (session == null) return;
            seenSettlement = session.SettlementSequence;
            seenDeparture = session.DepartureSequence;
            seenOpen = session.IsOpen;
            displayedGold = session.Gold;
            if (hud && hud.walletText)
            {
                walletPunch = new TextPunch(hud.walletText.rectTransform);
                walletRest = hud.walletText.color;
            }
        }

        void Update()
        {
            var session = trade ? trade.Session : null;
            if (session == null || !sfx || !cues) return;
            float dt = Time.unscaledDeltaTime;

            if (session.IsOpen != seenOpen)
            {
                seenOpen = session.IsOpen;
                sfx.Play(seenOpen ? cues.shopOpen : cues.shopClose);
            }

            var customer = session.CurrentCustomer;
            if (customer != null && customer.Id != seenCustomer)
            {
                seenCustomer = customer.Id;
                sfx.Play(cues.doorBell);
                seenRevision = -1;
            }

            var deal = session.CurrentDeal;
            if (deal != null && deal.Revision != seenRevision && trade.CustomerReady)
            {
                bool first = seenRevision < 0;
                seenRevision = deal.Revision;
                pendingMood = deal.Mood;
                // Lands together with the visitor's answer after the trade's thinking beat.
                voiceAt = Time.unscaledTime + (first ? .1f : trade.reactionBeat);
            }
            if (voiceAt > 0 && Time.unscaledTime >= voiceAt)
            {
                voiceAt = -1;
                sfx.Play(pendingMood == CustomerMood.Angry ? cues.npcAngry : pendingMood == CustomerMood.Happy ? cues.npcHappy : cues.npcNeutral);
            }

            if (session.SettlementSequence != seenSettlement)
            {
                seenSettlement = session.SettlementSequence;
                int price = session.LastSalePrice;
                sfx.Play(price >= bigSale ? cues.saleBig : cues.saleSmall);
                sfx.Play(cues.npcHappy, null, .8f);
                if (coinBurst)
                {
                    var emission = coinBurst.emission;
                    coinBurst.Emit(Mathf.Clamp(price / 6, 6, 40));
                }
                if (feel) { feel.PunchFov(-14); feel.AddTrauma(.12f); }
                walletPunch?.Hop(7);
                walletGlow = 1;
            }

            if (session.DepartureSequence != seenDeparture)
            {
                seenDeparture = session.DepartureSequence;
                if (session.LastDeparture != null && session.LastDeparture.Outcome == CustomerOutcome.WalkedAway)
                {
                    sfx.Play(cues.walkOut);
                    sfx.Play(cues.npcAngry);
                    if (feel) feel.AddTrauma(.15f);
                }
            }

            // Last ten seconds of a shift tick audibly.
            if (session.IsOpen && session.RemainingSeconds > 0 && session.RemainingSeconds <= 10)
            {
                int second = Mathf.CeilToInt(session.RemainingSeconds);
                if (second != lastTick) { lastTick = second; sfx.Play(cues.clockTick, null, second <= 3 ? 1 : .6f); }
            }
            else lastTick = -1;

            // Wallet counts up instead of jumping, with a coin tick per step and a punch.
            if (displayedGold != session.Gold)
            {
                int step = Mathf.Max(1, Mathf.Abs(session.Gold - displayedGold) / 12);
                displayedGold = displayedGold < session.Gold ? Mathf.Min(session.Gold, displayedGold + step) : Mathf.Max(session.Gold, displayedGold - step);
                if (displayedGold < session.Gold) sfx.Play(cues.coinTick, null, .35f);
            }
            if (hud && hud.walletText)
            {
                hud.walletText.text = $"{displayedGold} монет";
                walletGlow = Mathf.MoveTowards(walletGlow, 0, dt * 1.6f);
                hud.walletText.color = Color.Lerp(walletRest, walletFlash, walletGlow);
                walletPunch?.Update(dt);
            }

            if (cues.ambience && trade.World && trade.World.ViewCamera)
                sfx.Loop(this, cues.ambience, trade.World.ViewCamera.transform.position, session.ExpeditionId == 0 ? 1 : 0);
        }
    }
}
