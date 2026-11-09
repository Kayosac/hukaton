using Hakaton.Character;
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
        [Header("Effects (Retro Arsenal)")]
        [Tooltip("Where sale coins burst from: the deal spot on the counter.")]
        public Transform saleAnchor;
        public VfxBurst saleBurst, bigSaleBurst;
        [Tooltip("Coin size relative to the pack's default; keeps coins in proportion to the visitor.")]
        [Min(.05f)] public float saleScale = .12f, bigSaleScale = .15f;
        [Tooltip("Sales at or above this price use the big coin cascade.")]
        [Min(1)] public int bigSale = 100;
        public Color walletFlash = new Color(1f, .84f, .45f);

        long seenCustomer;
        int seenSettlement, seenDeparture, seenRevision = -1, lastTick = -1, displayedGold;
        bool seenOpen;
        float voiceAt = -1;
        CustomerMood pendingMood;
        long greetedCustomer;
        // The visitor whose voice we heard last: still speaks while walking out.
        NpcAppearance voice;
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

        static SfxCue Voice(SfxCue own, SfxCue common) => own ? own : common;

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

            // The visitor greets the shopkeeper in their own voice once they reach the counter.
            if (customer != null && customer.Id != greetedCustomer && trade.CustomerReady)
            {
                greetedCustomer = customer.Id;
                voice = trade.World.Customers.VisitorAppearance;
                if (voice && voice.voiceArrive) sfx.Play(voice.voiceArrive);
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
                // Each visitor answers in a voice of their own; the shop's common cues stand in when one is missing.
                sfx.Play(pendingMood == CustomerMood.Angry ? Voice(voice ? voice.voiceAngry : null, cues.npcAngry)
                    : pendingMood == CustomerMood.Happy ? Voice(voice ? voice.voiceHappy : null, cues.npcHappy)
                    : Voice(voice ? voice.voiceThinking : null, cues.npcNeutral));
            }

            if (session.SettlementSequence != seenSettlement)
            {
                seenSettlement = session.SettlementSequence;
                int price = session.LastSalePrice;
                sfx.Play(price >= bigSale ? cues.saleBig : cues.saleSmall);
                sfx.Play(Voice(voice ? voice.voiceHappy : null, cues.npcHappy), null, .8f);
                if (saleAnchor)
                    VfxBurst.Spawn(price >= bigSale && bigSaleBurst ? bigSaleBurst : saleBurst, saleAnchor.position,
                        Quaternion.identity, null, Color.white, price >= bigSale ? bigSaleScale : saleScale);
                if (feel) { feel.PunchFov(-14); feel.AddTrauma(.12f); }
                walletPunch?.Hop(3);
                walletGlow = 1;
            }

            if (session.DepartureSequence != seenDeparture)
            {
                seenDeparture = session.DepartureSequence;
                if (session.LastDeparture != null && session.LastDeparture.Outcome == CustomerOutcome.WalkedAway)
                {
                    sfx.Play(cues.walkOut);
                    sfx.Play(Voice(voice ? voice.voiceAngry : null, cues.npcAngry));
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
                hud.walletText.text = TradingHud.FormatMoney(displayedGold);
                walletGlow = Mathf.MoveTowards(walletGlow, 0, dt * 1.6f);
                hud.walletText.color = Color.Lerp(walletRest, walletFlash, walletGlow);
                walletPunch?.Update(dt);
            }

            if (cues.ambience && trade.World && trade.World.ViewCamera)
                sfx.Loop(this, cues.ambience, trade.World.ViewCamera.transform.position, session.ExpeditionId == 0 ? 1 : 0);
        }
    }
}
