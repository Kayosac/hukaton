using System.Globalization;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using TMPro;
using Hakaton.DialogueUI;
using Hakaton.Feel;

namespace Hakaton.Trading.Unity
{
    /// <summary>
    /// Presentation only: updates controls already authored in TradingHUD.prefab. Shop HUD and the trading
    /// conversation follow the 2026-11-10 UI handoff (736 × 368 reference layout): balance top left, shift clock
    /// top right, the visitor's lines in the stitched-linen bubble, linen buttons along the bottom. During a
    /// conversation the visitor's name, the goals block and the aim point are never shown.
    /// </summary>
    public sealed class TradingHud : MonoBehaviour
    {
        [Header("Compact exploration HUD")]
        public RectTransform layoutRoot, selectedItemPanel, contextPanel, noticePanel, staminaPanel;
        public CanvasGroup selectedVisibility, contextVisibility, noticeVisibility, staminaVisibility;
        public TMP_Text walletText, contextPrompt, contextDetail, noticeText;
        [Tooltip("Item card, first line: name and count (accent font).")]
        [FormerlySerializedAs("selectedItemText")] public TMP_Text selectedItemTitle;
        [Tooltip("Item card, second line: base price (body font).")]
        public TMP_Text selectedItemDetail;
        public Image staminaFill, contextProgressFill, reticleDot;
        public GameObject reticle;
        [Tooltip("Magnification of the shop HUD layout (736 × 368 mockup units).")]
        public ReferenceUiScale shopLayout;
        public Color reticleIdle = new Color32(0xE9, 0xD7, 0xA3, 0xFF);
        public Color reticleFocused = new Color32(0xE9, 0xD7, 0xA3, 0xFF);

        [Header("Shift clock")]
        [Tooltip("Shown while the shop is open.")]
        public GameObject shiftClock;
        [Tooltip("Dial, fill, frame and cross: faded together once the shift is over.")]
        public CanvasGroup clockBody;
        [Tooltip("Radial fill: remaining share of the shift.")]
        public Image clockFill;
        [Tooltip("The red cross on the clock closes the shop.")]
        [FormerlySerializedAs("closeShop")] public Button clockClose;
        [Tooltip("White resting face over the faded clock when the shift has ended (outside Clock Body, never faded).")]
        public GameObject clockRestingFace;
        [Range(0, 1)] public float endedClockAlpha = .38f;

        [Header("Conversation")]
        [Tooltip("Everything shown only while a visitor stands at the counter.")]
        public GameObject conversation;
        [Tooltip("Positions the speech bubble: at the counter or, while picking goods, on the left.")]
        public RectTransform speechAnchor;
        [Tooltip("The bubble itself; hops when the visitor answers.")]
        public RectTransform speechBubble;
        public TMP_Text speech;
        [Tooltip("Small red 'ПОСЛЕДНЕЕ' inside the bubble on a last offer.")]
        public GameObject lastOfferMark;
        public Vector2 speechAtCounter = new Vector2(410, -8);
        public Vector2 speechAtDisplay = new Vector2(8, -40);
        [Tooltip("Request: 'Выбрать с витрины' / 'К покупателю' and 'Отказать'.")]
        public GameObject requestActions;
        public StitchedLinenButton browseDisplay, backToCounter, refuseRequest;
        public TMP_Text browseLabel;
        [Tooltip("Haggling: price row and the two small actions.")]
        public GameObject dealActions;
        public StitchedLinenButton[] priceButtons = new StitchedLinenButton[5];
        [Tooltip("Top line of each price button: key and markup.")]
        public TMP_Text[] priceKeyLabels = new TMP_Text[5];
        [Tooltip("Price on each button.")]
        public TMP_Text[] priceLabels = new TMP_Text[5];
        public StitchedLinenButton accept, refuseDeal;
        public TMP_Text acceptLabel;
        [Tooltip("'Space' hint, shown only while selling is possible.")]
        public GameObject acceptKey;

        [Header("Approved tints")]
        public Color positiveTint = new Color32(0x94, 0xCE, 0x80, 0xFF);
        public Color negativeTint = new Color32(0xF0, 0x78, 0x72, 0xFF);
        public Color startPriceTint = new Color32(0xFF, 0xE4, 0x88, 0xFF);
        [Tooltip("The sell action while it cannot be pressed (waiting for the visitor's offer); authored on the button.")]
        public Color waitingTint = new Color32(0x9B, 0xA1, 0x91, 0xFF);

        [Header("Feedback")]
        [Min(0)] public float fadeSpeed = 8;
        [Min(4)] public float contextHeight = 12;
        [Min(4)] public float contextHeightWithDetail = 24;
        [Tooltip("Reference units from the bottom for the prompt while picking goods during a conversation (above the buttons).")]
        [Min(0)] public float displayContextBottom = 68;
        [Tooltip("Reference units the bubble hops when the visitor answers.")]
        [Min(0)] public float answerHop = 2;
        public Color staminaColor = new Color(.76f, .81f, .69f);
        public Color tiredColor = new Color(.88f, .48f, .32f);

        [Tooltip("Set when a ShopFeel animates the wallet counter.")]
        public bool feelDrivesWallet;
        TradingPrototype controller;
        long shownCustomerId, shownDealId;
        int shownDealRevision;
        float displayedStamina = 1;
        Vector2 contextRestPosition;
        TextPunch speechPunch;
        long revealedDealId;
        int revealedRevision = -1;

        public void Bind(TradingPrototype value)
        {
            if (controller != null) return;
            controller = value;
            contextRestPosition = contextPanel.anchoredPosition;
            clockClose.onClick.AddListener(controller.ToggleShop);
            for (int i = 0; i < priceButtons.Length; i++)
            {
                int index = i;
                priceButtons[i].onClick.AddListener(() => controller.OfferPrice(shownDealId, shownDealRevision, index));
            }
            accept.onClick.AddListener(() => controller.AcceptPrice(shownDealId, shownDealRevision));
            refuseRequest.onClick.AddListener(() => controller.RefuseCustomer(shownCustomerId));
            refuseDeal.onClick.AddListener(() => controller.RefuseCustomer(shownCustomerId));
            browseDisplay.onClick.AddListener(() => { if (controller.UsesDisplayView) controller.World.ShopView.BeginSelection(); });
            backToCounter.onClick.AddListener(() => { if (controller.UsesDisplayView) controller.World.ShopView.ReturnToCounter(); });
            selectedVisibility.alpha = contextVisibility.alpha = noticeVisibility.alpha = staminaVisibility.alpha = 0;
            speechPunch = new TextPunch(speechBubble);
            Refresh();
        }

        /// <summary>True over a visible conversation button: clicks there never pick goods from the display.</summary>
        public bool ContainsBlockingPanel(Vector2 position)
        {
            if (!conversation.activeInHierarchy) return false;
            foreach (var button in conversation.GetComponentsInChildren<Selectable>())
                if (RectTransformUtility.RectangleContainsScreenPoint((RectTransform)button.transform, position)) return true;
            return false;
        }

        void LateUpdate()
        {
            if (controller == null || controller.Session == null) return;
            Refresh();
            speechPunch?.Update(Time.unscaledDeltaTime);
        }

        static readonly NumberFormatInfo MoneyFormat = new NumberFormatInfo { NumberGroupSeparator = " ", NumberGroupSizes = new[] { 3 } };
        /// <summary>Balance as shown next to the coin: digits only, a space between thousands ("1 250").</summary>
        public static string FormatMoney(int value) => value.ToString("#,0", MoneyFormat);

        /// <summary>Radial fill of the shift clock: the remaining share of the shift.</summary>
        public static float ClockFill(float remainingSeconds, float shiftSeconds)
            => shiftSeconds > 0 ? Mathf.Clamp01(remainingSeconds / shiftSeconds) : 0;

        void Refresh()
        {
            var session = controller.Session;
            bool exploring = !session.IsOpen;
            bool selecting = controller.SelectingFromDisplay && !controller.ViewTransitioning;
            // ShopFeel animates the wallet when present; otherwise show the balance directly.
            if (!feelDrivesWallet) walletText.text = FormatMoney(session.Gold);
            RefreshClock(session);
            reticle.SetActive(exploring && controller.GameplayActive);
            reticleDot.color = string.IsNullOrEmpty(controller.ContextPrompt) ? reticleIdle : reticleFocused;

            // The card describes what the aim rests on: the stock about to be placed on a free slot (with how many
            // are left), the item on the hovered slot, the goods hovered for a visitor, or the item being haggled over.
            var item = controller.FocusedItem;
            var deal = session.CurrentDeal;
            bool haggling = session.IsOpen && deal != null && !selecting && controller.CustomerReady && !controller.ViewTransitioning;
            bool showItem = haggling || (item != null && session.ExpeditionId == 0
                && ((exploring && controller.GameplayActive) || selecting));
            if (haggling)
            {
                selectedItemTitle.text = deal.Item.Definition.Name;
                selectedItemDetail.text = $"Базовая цена: {deal.R}";
            }
            else if (showItem)
            {
                if (exploring && controller.PlacementFocused)
                {
                    int count = 0;
                    foreach (var stock in session.Inventory) if (stock.Definition.Id == item.Definition.Id) count++;
                    selectedItemTitle.text = $"{item.Definition.Name} ×{count}";
                }
                else selectedItemTitle.text = item.Definition.Name;
                selectedItemDetail.text = $"Базовая цена: {item.Definition.RecommendedPrice}";
            }
            Fade(selectedVisibility, showItem);
            contextPanel.anchoredPosition = selecting ? new Vector2(contextRestPosition.x, displayContextBottom) : contextRestPosition;
            bool hasContext = ((exploring && controller.GameplayActive) || selecting) && !string.IsNullOrEmpty(controller.ContextPrompt);
            if (hasContext)
            {
                contextPrompt.text = controller.ContextPrompt;
                contextDetail.text = controller.ContextDetail;
                contextPanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                    string.IsNullOrEmpty(controller.ContextDetail) ? contextHeight : contextHeightWithDetail);
            }
            Fade(contextVisibility, hasContext);
            contextProgressFill.fillAmount = Mathf.Clamp01(controller.ContextProgress);
            contextProgressFill.transform.parent.gameObject.SetActive(controller.ContextProgress > 0);
            noticeText.text = controller.Notice;
            Fade(noticeVisibility, !string.IsNullOrEmpty(controller.Notice));

            displayedStamina = Mathf.Lerp(displayedStamina, controller.StaminaFraction, 1 - Mathf.Exp(-14 * Time.unscaledDeltaTime));
            staminaFill.fillAmount = displayedStamina;
            staminaFill.color = Color.Lerp(tiredColor, staminaColor, Mathf.InverseLerp(.12f, .4f, controller.StaminaFraction));
            Fade(staminaVisibility, exploring && controller.ShowStamina);
            bool talking = session.IsOpen && session.CurrentCustomer != null && controller.CustomerReady && !controller.ViewTransitioning;
            conversation.SetActive(talking);
            if (talking) RefreshTrade(selecting);
        }

        /// <summary>
        /// Radial fill of the remaining shift. Once time is up the cross goes (the shift is over already), the clock
        /// fades to 38% and the opaque white resting face lies over it; the current visitor can still be served.
        /// </summary>
        void RefreshClock(TradingSession session)
        {
            shiftClock.SetActive(session.IsOpen);
            if (!session.IsOpen) return;
            bool ended = session.RemainingSeconds <= 0;
            clockFill.fillAmount = ended ? 0 : ClockFill(session.RemainingSeconds, session.Settings.ShiftSeconds);
            clockClose.gameObject.SetActive(!ended);
            clockClose.interactable = !controller.ViewTransitioning;
            clockBody.alpha = ended ? endedClockAlpha : 1;
            clockRestingFace.SetActive(ended);
        }

        void Fade(CanvasGroup group, bool visible)
        {
            group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1 : 0, fadeSpeed * Time.unscaledDeltaTime);
        }

        void RefreshTrade(bool selecting)
        {
            var session = controller.Session;
            var customer = session.CurrentCustomer;
            var deal = session.CurrentDeal;
            if (customer.Id != shownCustomerId) TypedLine.Forget(speech);
            shownCustomerId = customer.Id;
            shownDealId = deal == null ? 0 : deal.Id;
            shownDealRevision = deal == null ? 0 : deal.Revision;
            bool ready = controller.ActionsReady;
            bool request = deal == null;
            speechAnchor.anchoredPosition = request && selecting ? speechAtDisplay : speechAtCounter;
            requestActions.SetActive(request);
            dealActions.SetActive(!request);
            if (request)
            {
                TypedLine.Say(speech, $"Ищу: {customer.RequestedCategory}.");
                lastOfferMark.SetActive(false);
                bool display = controller.UsesDisplayView;
                bool stock = controller.HasMatchingStock;
                browseDisplay.gameObject.SetActive(display && !selecting);
                backToCounter.gameObject.SetActive(display && selecting);
                browseDisplay.interactable = ready && stock;
                browseLabel.text = stock ? "Выбрать с витрины" : "Подходящего нет";
                backToCounter.interactable = refuseRequest.interactable = ready;
                return;
            }

            // The answer, the counter offer and the face land together after the visitor's thinking beat.
            bool revealed = controller.ReactionRevealed;
            if (revealed && (deal.Id != revealedDealId || deal.Revision != revealedRevision))
            {
                bool answer = deal.Id == revealedDealId;
                revealedDealId = deal.Id;
                revealedRevision = deal.Revision;
                if (answer)
                {
                    speechPunch.Hop(answerHop);
                    if (deal.Mood == CustomerMood.Angry || deal.IsLastOffer) speechPunch.Shake(.35f, 1);
                }
            }
            TypedLine.Say(speech, revealed ? deal.Reaction : "…");
            lastOfferMark.SetActive(revealed && deal.IsLastOffer);
            var options = deal.GetPriceOptions();
            for (int i = 0; i < priceButtons.Length; i++)
            {
                var option = options[i];
                priceKeyLabels[i].text = $"{i + 1} {(option.Percent == 0 ? "Старт" : $"+{option.Percent}%")}";
                priceLabels[i].text = option.Price.ToString(CultureInfo.InvariantCulture);
                priceButtons[i].interactable = ready && option.Enabled;
            }
            bool offer = revealed && deal.N.HasValue;
            acceptLabel.text = offer ? $"Продать за {deal.N.Value}" : "Ждём предложение";
            accept.interactable = ready && offer;
            acceptKey.SetActive(offer);
            refuseDeal.interactable = ready;
        }
    }
}
