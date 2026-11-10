using System;
using System.Linq;
using Hakaton.Character;
using Hakaton.Trading;
using Hakaton.Trading.Unity;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hakaton.Tutorial
{
    /// <summary>
    /// The shop part of the tutorial (board, «Туториал · согласовано», 05–11). The goblin on the counter hands over two
    /// old wares (cap R = 60, fang R = 120); the player puts both on display and opens the shop. Two staged visitors
    /// answer exactly (<see cref="ScriptedHaggle"/>), the clock stands still, the way out and the backroom stay closed.
    /// Any two sales finish it: the shop closes, the goblin hints at the backroom and leaves in a poof.
    /// Explanations block movement and wait for E; lines during trading only comment.
    /// </summary>
    public sealed class ShopTutorial : MonoBehaviour
    {
        [Header("Scene")]
        public TradingPrototype trade;
        public PlayerControlGate control;
        public CounterGoblin goblin;
        public TutorialBubble bubble;

        [Header("The two wares")]
        public string capId = "hare_foot";
        public string fangId = "boar_tusk";

        [Header("Lines")]
        [TextArea] public string[] arrival =
        {
            "Вот моя лавка. Пока начнём со старых запасов.",
            "Шляпка и клык. Поставь их на витрину — пусть покупатели видят товар."
        };
        [TextArea] public string stockedLine = "Теперь становись за прилавок и открывай лавку.";
        [TextArea] public string bothFirst = "Сначала выставь оба товара.";
        [TextArea] public string capLesson = "Спрашивает цену. Число у товара — ориентир. Можешь назвать его или попросить больше.";
        [TextArea] public string fangLesson = "Этот уже назвал свою цену. Хочешь больше — предложи встречную. Но терпение у него не бесконечное.";
        [TextArea] public string cheapLine = "Можно было попросить больше. Но монеты уже у тебя.";
        [TextArea] public string walkedLine = "Ушёл? Товар-то остался. Следующий!";
        [TextArea] public string refusedLine = "Не хочешь — не продавай. Найдём другого.";
        [TextArea] public string[] finale =
        {
            "Вот и первые деньги. Теперь будет куда нести свои находки.",
            "Перед уходом загляни за занавеску. Там ещё кое-что есть.",
            "А у меня дела поважнее."
        };

        public ShopTutorialStage Stage { get; private set; } = ShopTutorialStage.Done;
        public bool Active => Stage != ShopTutorialStage.Done;
        public event Action Changed;

        int linesRead;
        bool stockGiven, soldCap, soldFang, blocking, continueRequested;
        int seenSettlement, seenDeparture;
        long seenCustomer;
        TradingSession Session => trade.Session;

        /// <summary>A new game: the goblin is on the counter and starts talking.</summary>
        public void Begin()
        {
            Stage = ShopTutorialStage.Arrival;
            linesRead = 0;
            stockGiven = soldCap = soldFang = false;
            Enter();
        }

        public TutorialSave Save() => new TutorialSave { stage = (int)Stage, linesRead = linesRead, stockGiven = stockGiven };

        /// <summary>
        /// A save made in the middle (board, 08): the shop is closed, an unread line is shown again whole, wares already
        /// sold are not needed again. Which ware is still unsold is read from the shop itself.
        /// </summary>
        public bool TryRestore(TutorialSave save)
        {
            if (save == null || save.stage < 0 || save.stage > (int)ShopTutorialStage.Done) return false;
            Stage = (ShopTutorialStage)save.stage;
            linesRead = Mathf.Max(0, save.linesRead);
            stockGiven = save.stockGiven;
            soldCap = stockGiven && !Has(capId);
            soldFang = stockGiven && !Has(fangId);
            Enter();
            return true;
        }

        void Enter()
        {
            seenSettlement = Session.SettlementSequence;
            seenDeparture = Session.DepartureSequence;
            seenCustomer = Session.CurrentCustomer?.Id ?? 0;
            goblin.Show(Active);
            Apply();
            Changed?.Invoke();
        }

        /// <summary>The shop rules that hold while the tutorial runs.</summary>
        void Apply()
        {
            bool on = Active;
            trade.ExitsLocked = on;
            Session.ClockPaused = on;
            Session.CustomerScript = on ? NextVisitor : null;
            trade.OpenRefusal = on && Stage < ShopTutorialStage.Trading ? bothFirst : null;
            if (!on) SetBlocking(false);
        }

        bool Has(string id) => Session.Inventory.Any(i => i.Definition.Id == id) || Session.Slots.Any(i => i != null && i.Definition.Id == id);
        int SlotOf(string id)
        {
            for (int i = 0; i < Session.Slots.Count; i++) if (Session.Slots[i] != null && Session.Slots[i].Definition.Id == id) return i;
            return -1;
        }

        /// <summary>The staged visitor for the next unsold ware on display: the cap first, then the fang.</summary>
        CustomerTerms NextVisitor()
        {
            if (!Active) return null;
            int cap = soldCap ? -1 : SlotOf(capId), fang = soldFang ? -1 : SlotOf(fangId);
            if (cap >= 0)
                return new CustomerTerms { RequestedSlot = cap, AsksPrice = true, MaxPrice = 69, Script = new ScriptedHaggle(69, 66, 90) };
            if (fang >= 0)
                return new CustomerTerms { RequestedSlot = fang, AsksPrice = false, OpeningRatio = .75f, MaxPrice = 104,
                    Script = new ScriptedHaggle(104, 108, 135) };
            return null;
        }

        void Update()
        {
            if (!Active || !trade || Session == null) return;
            Apply();
            switch (Stage)
            {
                case ShopTutorialStage.Arrival: Explain(arrival, ShopTutorialStage.Stocking); break;
                case ShopTutorialStage.Stocking:
                    if (blocking) Explain(new[] { stockedLine }, ShopTutorialStage.Trading);
                    else if (stockGiven && SlotOf(capId) >= 0 && SlotOf(fangId) >= 0 && !Session.IsOpen) { linesRead = 0; Explain(new[] { stockedLine }, ShopTutorialStage.Trading); }
                    break;
                case ShopTutorialStage.Trading: ObserveTrade(); break;
                case ShopTutorialStage.Finale: Explain(finale, ShopTutorialStage.Done); break;
            }
        }

        /// <summary>Shows the current line of a blocking explanation; E closes it. The last one moves to the next stage.</summary>
        void Explain(string[] lines, ShopTutorialStage next)
        {
            if (Session.IsOpen) return;
            if (linesRead >= lines.Length) { Advance(next); return; }
            if (!blocking)
            {
                SetBlocking(true);
                bubble.Say(lines[linesRead], true);
                goblin.Speak(linesRead == 0);
                // The wares are handed over with the line that names them, exactly once.
                if (Stage == ShopTutorialStage.Arrival && linesRead == lines.Length - 1) GiveStock();
                return;
            }
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.eKey.wasPressedThisFrame) continueRequested = true;
            if (!continueRequested) return;
            continueRequested = false;
            linesRead++;
            SetBlocking(false);
            bubble.Hide();
            Changed?.Invoke();
            if (linesRead >= lines.Length) Advance(next);
        }

        /// <summary>Closes the line on screen, as E does.</summary>
        public void Continue()
        {
            if (blocking) continueRequested = true;
        }

        void Advance(ShopTutorialStage next)
        {
            if (next == ShopTutorialStage.Done && goblin.isActiveAndEnabled) goblin.Poof();
            Stage = next;
            linesRead = 0;
            Apply();
            Changed?.Invoke();
        }

        void GiveStock()
        {
            if (stockGiven) return;
            stockGiven = true;
            foreach (var id in new[] { capId, fangId })
            {
                var definition = Session.FindDefinition(id);
                if (definition != null) Session.SeedInventory(definition, 1);
            }
            trade.SelectStock(capId);
            trade.RefreshPresentation();
            Changed?.Invoke();
        }

        void ObserveTrade()
        {
            if (Session.SettlementSequence != seenSettlement)
            {
                seenSettlement = Session.SettlementSequence;
                seenDeparture = Session.DepartureSequence;
                string sold = Session.LastSoldItem?.Definition.Id;
                if (sold == capId) soldCap = true;
                if (sold == fangId) soldFang = true;
                if (sold == fangId && Session.LastSalePrice == 90) bubble.Say(cheapLine, false);
                else bubble.Hide();
                Changed?.Invoke();
                if (soldCap && soldFang)
                {
                    // Any two sales end the lessons: the shop closes on its own.
                    trade.CloseShopNow();
                    Stage = ShopTutorialStage.Finale;
                    linesRead = 0;
                    Changed?.Invoke();
                }
                return;
            }
            if (Session.DepartureSequence != seenDeparture)
            {
                seenDeparture = Session.DepartureSequence;
                var outcome = Session.LastDeparture?.Outcome;
                if (outcome == CustomerOutcome.WalkedAway) bubble.Say(walkedLine, false);
                else if (outcome == CustomerOutcome.Refused) bubble.Say(refusedLine, false);
                else bubble.Hide();
            }
            var customer = Session.CurrentCustomer;
            if (customer != null && customer.Id != seenCustomer)
            {
                seenCustomer = customer.Id;
                bool cap = Session.CurrentDeal?.Item.Definition.Id == capId;
                bubble.Say(cap ? capLesson : fangLesson, false);
                goblin.Speak(false);
            }
            if (!Session.IsOpen && Session.CurrentCustomer == null && bubble.Visible && !blocking && Time.frameCount % 600 == 0) bubble.Hide();
        }

        void SetBlocking(bool value)
        {
            blocking = value;
            if (control) control.SetBlocked(this, value);
        }

        void OnDisable()
        {
            SetBlocking(false);
            if (trade)
            {
                trade.ExitsLocked = false;
                trade.OpenRefusal = null;
                if (Session != null) { Session.ClockPaused = false; Session.CustomerScript = null; }
            }
        }
    }
}
