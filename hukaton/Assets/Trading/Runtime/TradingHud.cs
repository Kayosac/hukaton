using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Hakaton.Trading.Unity
{
    /// <summary>Presentation only: updates controls already authored in TradingHUD.prefab.</summary>
    public sealed class TradingHud : MonoBehaviour
    {
        [Header("Compact exploration HUD")]
        public RectTransform layoutRoot, interactionPanel, selectedItemPanel, contextPanel, noticePanel, staminaPanel;
        public CanvasGroup selectedVisibility, contextVisibility, noticeVisibility, staminaVisibility;
        public TMP_Text walletText, timeRemaining, selectedItemText, contextPrompt, contextDetail, noticeText;
        public Image staminaFill, contextProgressFill, reticleDot;
        public GameObject reticle;
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
        [Min(0)] public float displayContextBottom = 240;
        public Color staminaColor = new Color(.76f, .81f, .69f);
        public Color tiredColor = new Color(.88f, .48f, .32f);

        TradingPrototype controller;
        long shownCustomerId, shownDealId;
        int shownDealRevision;
        float displayedStamina = 1;
        Vector2 contextRestPosition;

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
            Refresh();
        }

        public bool ContainsBlockingPanel(Vector2 position) => (interactionPanel.gameObject.activeInHierarchy
            && RectTransformUtility.RectangleContainsScreenPoint(interactionPanel, position))
            || (contextPanel.gameObject.activeInHierarchy && contextVisibility.alpha > .01f
            && RectTransformUtility.RectangleContainsScreenPoint(contextPanel, position));

        void LateUpdate()
        {
            if (controller != null && controller.Session != null) Refresh();
        }

        void Refresh()
        {
            var session = controller.Session;
            bool exploring = !session.IsOpen;
            bool selecting = controller.SelectingFromDisplay && !controller.ViewTransitioning;
            walletText.text = $"{session.Gold} монет";
            timeRemaining.gameObject.SetActive(session.IsOpen || (session.RemainingSeconds <= 15 && session.ExpeditionId == 0));
            int seconds = Mathf.CeilToInt(session.RemainingSeconds);
            timeRemaining.text = seconds > 0 ? $"{seconds / 60}:{seconds % 60:00}" : "Смена завершена";
            timeRemaining.color = seconds <= 15 ? tiredColor : new Color(.88f, .9f, .88f);
            interactionPanel.gameObject.SetActive(session.IsOpen && controller.CustomerReady && !controller.ViewTransitioning);
            closeShop.interactable = !controller.ViewTransitioning;
            reticle.SetActive(exploring && controller.GameplayActive);
            reticleDot.color = string.IsNullOrEmpty(controller.ContextPrompt)
                ? new Color(1, 1, 1, .45f) : new Color(1, .92f, .68f, .95f);

            var item = controller.SelectedItem;
            bool showItem = exploring && controller.GameplayActive && session.ExpeditionId == 0 && item != null;
            if (showItem)
            {
                int count = 0;
                foreach (var stock in session.Inventory) if (stock.Definition.Id == item.Definition.Id) count++;
                selectedItemText.text = $"{item.Definition.Name}  ×{count}\nОриентир: {item.Definition.RecommendedPrice}";
            }
            Fade(selectedVisibility, showItem);
            contextPanel.anchoredPosition = selecting
                ? new Vector2(contextRestPosition.x, displayContextBottom) : contextRestPosition;
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
            reaction.text = deal.Reaction;
            var options = deal.GetPriceOptions();
            for (int i = 0; i < priceButtons.Length; i++)
            {
                var option = options[i];
                priceLabels[i].text = (option.Percent == 0 ? "Старт" : $"+{option.Percent}%") + $"\n{option.Price}";
                priceButtons[i].interactable = ready && option.Enabled;
            }
            acceptLabel.text = deal.N.HasValue ? $"Продать за {deal.N.Value}" : "Ждём предложение";
            accept.interactable = ready && deal.N.HasValue;
            bargainHint.text = deal.IsLastOffer ? "Последнее предложение" : string.Empty;
            bargainHint.gameObject.SetActive(deal.IsLastOffer);
        }
    }
}
