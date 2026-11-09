using System.Globalization;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using TMPro;
using Hakaton.Feel;

namespace Hakaton.Trading.Unity
{
    /// <summary>Presentation only: updates controls already authored in TradingHUD.prefab.</summary>
    public sealed class TradingHud : MonoBehaviour
    {
        [Header("Compact exploration HUD")]
        public RectTransform layoutRoot, interactionPanel, selectedItemPanel, contextPanel, noticePanel, staminaPanel;
        public CanvasGroup selectedVisibility, contextVisibility, noticeVisibility, staminaVisibility;
        public TMP_Text walletText, timeRemaining, contextPrompt, contextDetail, noticeText;
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
        [Header("Conversation")]
        public GameObject categoryPanel, bargainingPanel;
        public TMP_Text customerName, shiftEnded, categoryRequest, priceSummary, reaction, bargainHint, acceptLabel;
        public Button closeShop;
        public Button[] categoryButtons = new Button[6];
        public TMP_Text[] categoryLabels = new TMP_Text[6];
        public Button[] priceButtons = new Button[5];
        public TMP_Text[] priceLabels = new TMP_Text[5];
        public Button accept, refuseCategory, refuseBargain;
        [Header("Feedback")]
        [Min(0)] public float fadeSpeed = 8;
        [Min(20)] public float contextHeight = 44;
        [Min(20)] public float contextHeightWithDetail = 72;
        [Tooltip("Screen pixels from the bottom for the prompt while picking goods during a conversation.")]
        [Min(0)] public float displayContextBottom = 240;
        public Color staminaColor = new Color(.76f, .81f, .69f);
        public Color tiredColor = new Color(.88f, .48f, .32f);

        [Header("Haggling presentation")]
        public Color thinkingColour = new Color(.55f, .63f, .57f);
        public Color calmColour = new Color(.91f, .90f, .81f);
        public Color displeasedColour = new Color(.95f, .74f, .42f);
        public Color angryColour = new Color(.96f, .48f, .36f);
        [Tooltip("Hotkey hints on buttons.")]
        public Color keyColour = new Color(.55f, .63f, .57f);
        [Tooltip("Greedy asks (+25% and up) are tinted so their risk reads at a glance.")]
        public Color greedColour = new Color(.95f, .74f, .42f);

        [Tooltip("Set when a ShopFeel animates the wallet counter.")]
        public bool feelDrivesWallet;
        TradingPrototype controller;
        long shownCustomerId, shownDealId;
        int shownDealRevision;
        float displayedStamina = 1;
        Vector2 contextRestPosition;
        TextPunch reactionPunch;
        long revealedDealId;
        int revealedRevision = -1;

        public void Bind(TradingPrototype value)
        {
            if (controller != null) return;
            controller = value;
            contextRestPosition = contextPanel.anchoredPosition;
            closeShop.onClick.AddListener(controller.ToggleShop);
            for (int i = 0; i < categoryButtons.Length; i++)
            {
                int index = i;
                categoryButtons[i].onClick.AddListener(() => controller.SelectCustomerItem(index, shownCustomerId));
            }
            for (int i = 0; i < priceButtons.Length; i++)
            {
                int index = i;
                priceButtons[i].onClick.AddListener(() => controller.OfferPrice(shownDealId, shownDealRevision, index));
            }
            accept.onClick.AddListener(() => controller.AcceptPrice(shownDealId, shownDealRevision));
            refuseCategory.onClick.AddListener(() => controller.RefuseCustomer(shownCustomerId));
            refuseBargain.onClick.AddListener(() => controller.RefuseCustomer(shownCustomerId));
            selectedVisibility.alpha = contextVisibility.alpha = noticeVisibility.alpha = staminaVisibility.alpha = 0;
            reactionPunch = new TextPunch(reaction.rectTransform);
            string refuse = $"Отказать  <color=#{Hex(keyColour)}>Bksp</color>";
            foreach (var button in new[] { refuseCategory, refuseBargain })
            {
                var label = button.GetComponentInChildren<TMP_Text>(true);
                if (label) label.text = refuse;
            }
            Refresh();
        }

        public bool ContainsBlockingPanel(Vector2 position) => (interactionPanel.gameObject.activeInHierarchy
            && RectTransformUtility.RectangleContainsScreenPoint(interactionPanel, position))
            || (contextPanel.gameObject.activeInHierarchy && contextVisibility.alpha > .01f
            && RectTransformUtility.RectangleContainsScreenPoint(contextPanel, position));

        void LateUpdate()
        {
            if (controller == null || controller.Session == null) return;
            Refresh();
            reactionPunch?.Update(Time.unscaledDeltaTime);
        }

        static string Hex(Color colour) => ColorUtility.ToHtmlStringRGB(colour);

        static readonly NumberFormatInfo MoneyFormat = new NumberFormatInfo { NumberGroupSeparator = " ", NumberGroupSizes = new[] { 3 } };
        /// <summary>Balance as shown next to the coin: digits only, a space between thousands ("1 250").</summary>
        public static string FormatMoney(int value) => value.ToString("#,0", MoneyFormat);

        Color MoodColour(CustomerMood mood) => mood == CustomerMood.Angry ? angryColour
            : mood == CustomerMood.Displeased ? displeasedColour : calmColour;

        void Refresh()
        {
            var session = controller.Session;
            bool exploring = !session.IsOpen;
            bool selecting = controller.SelectingFromDisplay && !controller.ViewTransitioning;
            // ShopFeel animates the wallet when present; otherwise show the balance directly.
            if (!feelDrivesWallet) walletText.text = FormatMoney(session.Gold);
            timeRemaining.gameObject.SetActive(session.IsOpen || (session.RemainingSeconds <= 15 && session.ExpeditionId == 0));
            int seconds = Mathf.CeilToInt(session.RemainingSeconds);
            timeRemaining.text = seconds > 0 ? $"{seconds / 60}:{seconds % 60:00}" : "Смена завершена";
            timeRemaining.color = seconds <= 15 ? tiredColor : new Color(.88f, .9f, .88f);
            interactionPanel.gameObject.SetActive(session.IsOpen && controller.CustomerReady && !controller.ViewTransitioning);
            closeShop.interactable = !controller.ViewTransitioning;
            reticle.SetActive(exploring && controller.GameplayActive);
            reticleDot.color = string.IsNullOrEmpty(controller.ContextPrompt) ? reticleIdle : reticleFocused;

            // The card describes what the aim rests on: the stock about to be placed on a free slot
            // (with how many are left) or the item already lying on the hovered slot.
            var item = controller.FocusedItem;
            bool showItem = exploring && controller.GameplayActive && session.ExpeditionId == 0 && item != null;
            if (showItem)
            {
                if (controller.PlacementFocused)
                {
                    int count = 0;
                    foreach (var stock in session.Inventory) if (stock.Definition.Id == item.Definition.Id) count++;
                    selectedItemTitle.text = $"{item.Definition.Name} ×{count}";
                }
                else selectedItemTitle.text = item.Definition.Name;
                selectedItemDetail.text = $"Базовая цена: {item.Definition.RecommendedPrice}";
            }
            Fade(selectedVisibility, showItem);
            float layoutScale = shopLayout ? shopLayout.Scale : 1;
            contextPanel.anchoredPosition = selecting
                ? new Vector2(contextRestPosition.x, displayContextBottom / layoutScale) : contextRestPosition;
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
            if (session.IsOpen) RefreshTrade();
        }

        void Fade(CanvasGroup group, bool visible)
        {
            group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1 : 0, fadeSpeed * Time.unscaledDeltaTime);
        }

        void RefreshTrade()
        {
            var session = controller.Session;
            var customer = session.CurrentCustomer;
            var deal = session.CurrentDeal;
            shownCustomerId = customer == null ? 0 : customer.Id;
            shownDealId = deal == null ? 0 : deal.Id;
            shownDealRevision = deal == null ? 0 : deal.Revision;
            bool ready = controller.ActionsReady;
            bool selecting = controller.SelectingFromDisplay;
            bool category = customer != null && deal == null;
            categoryPanel.SetActive(category);
            bargainingPanel.SetActive(deal != null && !selecting);
            if (controller.UsesDisplayView)
            {
                var view = controller.World.ShopView;
                if (view.browseButton)
                {
                    view.browseButton.gameObject.SetActive(category && !selecting && controller.HasMatchingStock);
                    view.browseButton.interactable = ready;
                }
                if (view.backButton)
                {
                    view.backButton.gameObject.SetActive(category && selecting);
                    view.backButton.interactable = ready;
                }
            }
            customerName.text = customer == null ? "Следующий покупатель…" : controller.CustomerName;
            shiftEnded.gameObject.SetActive(session.RemainingSeconds <= 0);
            if (customer == null) return;
            refuseCategory.interactable = refuseBargain.interactable = ready;
            if (deal == null)
            {
                categoryRequest.text = $"«Ищу: {customer.RequestedCategory}»."
                    + (controller.UsesDisplayView && !controller.HasMatchingStock ? "\nПодходящего товара нет." : string.Empty);
                for (int i = 0; i < categoryButtons.Length; i++)
                {
                    var item = session.Slots[i];
                    bool matches = item != null && item.Definition.Category == customer.RequestedCategory;
                    categoryButtons[i].gameObject.SetActive(matches && !controller.UsesDisplayView);
                    if (matches) categoryLabels[i].text = $"{item.Definition.Name}\n{item.Definition.RecommendedPrice} монет";
                    categoryButtons[i].interactable = ready && matches;
                }
                return;
            }
            priceSummary.text = controller.UsesDisplayView
                ? $"{deal.Item.Definition.Name}   ·   Ориентир {deal.R}"
                : $"{deal.Item.Definition.Name}   ·   Ориентир {deal.R}   ·   Старт {deal.B}   ·   Покупатель {(deal.N.HasValue ? deal.N.Value.ToString() : "—")}";
            // The answer, the counter and the face land together after the visitor's thinking beat.
            bool revealed = controller.ReactionRevealed;
            if (revealed && (deal.Id != revealedDealId || deal.Revision != revealedRevision))
            {
                bool answer = deal.Id == revealedDealId;
                revealedDealId = deal.Id;
                revealedRevision = deal.Revision;
                if (answer)
                {
                    reactionPunch.Hop(5);
                    if (deal.Mood == CustomerMood.Angry || deal.IsLastOffer) reactionPunch.Shake(.35f, 2);
                }
            }
            reaction.text = revealed ? deal.Reaction : "…";
            reaction.color = revealed ? MoodColour(deal.Mood) : thinkingColour;
            string key = Hex(keyColour), greed = Hex(greedColour);
            var options = deal.GetPriceOptions();
            for (int i = 0; i < priceButtons.Length; i++)
            {
                var option = options[i];
                string ask = option.Percent == 0 ? "Старт" : $"+{option.Percent}%";
                if (option.Percent >= 25) ask = $"<color=#{greed}>{ask}</color>";
                priceLabels[i].text = $"<color=#{key}>{i + 1}</color> {ask}\n{option.Price}";
                priceButtons[i].interactable = ready && option.Enabled;
            }
            acceptLabel.text = !revealed ? "…"
                : deal.N.HasValue ? $"Продать за {deal.N.Value}  <color=#{key}>Space</color>" : "Ждём предложение";
            accept.interactable = ready && revealed && deal.N.HasValue;
            bool lastOffer = revealed && deal.IsLastOffer;
            bargainHint.text = lastOffer ? "Последнее предложение — новая цена сорвёт сделку" : string.Empty;
            bargainHint.color = angryColour;
            bargainHint.alpha = .7f + .3f * Mathf.Sin(Time.unscaledTime * 6);
            bargainHint.gameObject.SetActive(lastOffer);
        }
    }
}
