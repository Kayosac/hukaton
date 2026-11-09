using System;
using System.Collections.Generic;
using System.Linq;
using Hakaton.Character;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Hakaton.Decks.Unity
{
    /// <summary>
    /// The deck workshop as a shop service: the magical computer (collection, the deck, upgrades) and the goblin
    /// (card purchases) open the same authored panel. The game loop starts and finishes outings through
    /// <see cref="TryDepart"/> and <see cref="CompleteOuting"/>; the expedition world owns everything in between.
    /// </summary>
    public sealed class DeckPrototype : MonoBehaviour
    {
        [Header("Content and ownership")]
        public DeckPrototypeConfig config;
        public PlayerControlGate control;
        [Tooltip("The game's shared wallet. Without one the workshop keeps its own (isolated tests).")]
        public Hakaton.Economy.Unity.CoinWalletHost walletHost;
        public AudioSource audioSource;
        public Canvas presentation;

        [Header("Service shell")]
        public CanvasGroup embeddedVisibility;
        public Button embeddedCloseButton;
        public GameObject workshopPanel;
        public TMP_Text pageTitle, eyebrow, wallet, feedback;

        [Header("Workshop")]
        public Button collectionTab, marketTab, previousPage, nextPage;
        public Button equipButton, upgradeButton, handUpgradeButton;
        public TMP_Text collectionHeading, pageNumber, deckCount, handSize;
        public TMP_Text handUpgradeText, equipText, upgradeText;
        public DeckCardView[] collectionViews;
        public DeckRowView[] deckRows;
        public Image detailArt, detailAccent;
        public TMP_Text detailTitle, detailIdentity, detailPower, detailDescription;

        public event Action Departed;
        public event Action<IReadOnlyList<LootSave>, bool> OutingCompleted;
        public event Action EmbeddedClosed;
        /// <summary>A copy was bought at the goblin's market.</summary>
        public event Action Bought;

        public DeckWorkshop Model { get; private set; }
        /// <summary>True from departure until the outing is delivered back.</summary>
        public bool Busy { get; private set; }
        public bool Market { get; private set; }
        public int SelectedCopyId { get; private set; }
        public string SelectedDefinitionId { get; private set; }
        public bool EmbeddedOpen { get; private set; }

        /// <summary>Opened at the goblin: only the card shop, no deck editing.</summary>
        bool marketOnly;
        int page;

        static readonly Color ErrorColour = new Color(.96f, .56f, .39f);
        static readonly Color MutedColour = new Color(.55f, .63f, .57f);

        void Awake()
        {
            Model = config.CreateWorkshop(walletHost ? walletHost.Wallet : null);
            collectionTab.onClick.AddListener(() => ChooseMarket(false));
            marketTab.onClick.AddListener(() => ChooseMarket(true));
            previousPage.onClick.AddListener(() => ChangePage(-1));
            nextPage.onClick.AddListener(() => ChangePage(1));
            equipButton.onClick.AddListener(EquipOrBuy);
            upgradeButton.onClick.AddListener(UpgradeSelected);
            handUpgradeButton.onClick.AddListener(UpgradeHand);
            foreach (var view in collectionViews)
            {
                var tile = view;
                tile.button.onClick.AddListener(() => SelectTile(tile));
            }
            foreach (var row in deckRows)
            {
                var deckRow = row;
                deckRow.button.onClick.AddListener(() => SelectCopy(deckRow.CopyId));
            }
            SelectFirst();
            SetFeedback("Выберите карту. Каждый экземпляр берётся в путь отдельно.");
            if (!embeddedVisibility || !embeddedCloseButton)
            {
                Debug.LogError("The deck workshop needs its authored visibility group and close button.", this);
                enabled = false;
                return;
            }
            embeddedCloseButton.onClick.AddListener(CloseEmbedded);
            SetVisible(false);
        }

        void OnEnable()
        {
            if (control) control.SetBlocked(this, EmbeddedOpen);
        }

        void OnDisable()
        {
            CloseEmbedded();
            if (control) control.SetBlocked(this, false);
        }

        void Update()
        {
            if (EmbeddedOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) CloseEmbedded();
        }

        // ---------- opening and closing ----------

        /// <summary>Opens the panel: the collection at the computer, or the card shop at the goblin.</summary>
        public bool OpenEmbedded(bool market = false)
        {
            if (!isActiveAndEnabled || Model == null || Model.IsAway || Busy) return false;
            marketOnly = market;
            EmbeddedOpen = true;
            Market = market;
            page = 0;
            if (control) control.SetBlocked(this, true);
            SetVisible(true);
            if (Model.Find(SelectedCopyId) == null) SelectFirst();
            else RefreshWorkshop();
            SetFeedback(market ? "Выберите карту: купленная копия останется в коллекции."
                               : "Соберите колоду и улучшите нужные экземпляры или руку.");
            return true;
        }

        public void CloseEmbedded()
        {
            bool wasOpen = EmbeddedOpen;
            EmbeddedOpen = false;
            SetVisible(false);
            if (control) control.SetBlocked(this, false);
            if (wasOpen) EmbeddedClosed?.Invoke();
        }

        void SetVisible(bool visible)
        {
            if (!embeddedVisibility) return;
            embeddedVisibility.alpha = visible ? 1 : 0;
            embeddedVisibility.interactable = visible;
            embeddedVisibility.blocksRaycasts = visible;
        }

        // ---------- outings ----------

        /// <summary>Starts an outing with the equipped deck. Returns an error to show, or null on success.</summary>
        public string TryDepart()
        {
            if (!isActiveAndEnabled || Model == null) return "Мастерская недоступна.";
            if (Busy) return "Сначала завершите текущую вылазку.";
            if (Departed == null || OutingCompleted == null) return "Вылазка не подключена.";
            string error = Model.Depart(unchecked(Environment.TickCount * 397 + Model.Journeys));
            if (error != null) return error;
            Busy = true;
            CloseEmbedded();
            presentation.enabled = false;
            Sound(config.cardSound);
            Departed.Invoke();
            return null;
        }

        /// <summary>Delivers a finished outing; the game loop moves the loot into the shop.</summary>
        public void CompleteOuting(string notice, IReadOnlyList<LootSave> loot, bool defeated)
        {
            if (Model == null || Model.IsAway || !Busy) return;
            Busy = false;
            var delivered = (loot ?? Array.Empty<LootSave>())
                                .Select(g => new LootSave { id = g.id, name = g.name, count = g.count, value = g.value })
                                .ToArray();
            presentation.enabled = true;
            CloseEmbedded();
            RefreshWorkshop();
            SetFeedback(notice);
            OutingCompleted?.Invoke(delivered, defeated);
        }

        // ---------- workshop actions ----------

        void SelectFirst()
        {
            if (Model.IsAway || Busy) return;
            page = 0;
            SelectedCopyId = Model.Owned.FirstOrDefault()?.Id ?? 0;
            SelectedDefinitionId = config.cards.First().id;
            RefreshWorkshop();
        }

        public void ChooseMarket(bool market)
        {
            // Each service shows only its own page: the computer the collection, the goblin the shop.
            if (Model.IsAway || Busy || (EmbeddedOpen && market != marketOnly)) return;
            Market = market;
            page = 0;
            RefreshWorkshop();
        }

        public void SelectCopy(int id)
        {
            var copy = Model.Find(id);
            if (copy == null || Busy || Model.IsAway || marketOnly) return;
            Market = false;
            SelectedCopyId = id;
            SelectedDefinitionId = copy.Spec.Id;
            page = Model.Owned.ToList().FindIndex(c => c.Id == id) / collectionViews.Length;
            RefreshWorkshop();
        }

        public void EquipOrBuy()
        {
            if (!EmbeddedOpen || Busy || Model.IsAway) return;
            if (Market)
            {
                string error = Model.Buy(SelectedDefinitionId, out var copy);
                if (copy != null && !marketOnly) SelectCopy(copy.Id);
                Result(error, "Новая копия в коллекции. Теперь её можно взять в путь.", config.coinSound);
                if (copy != null) Bought?.Invoke();
            }
            else
                Result(Model.Toggle(SelectedCopyId), "Состав колоды сохранён.", config.cardSound);
        }

        public void UpgradeSelected()
        {
            if (!EmbeddedOpen || Busy || Market || Model.IsAway) return;
            Result(Model.Upgrade(SelectedCopyId), "Улучшен только выбранный экземпляр.", config.coinSound);
        }

        public void UpgradeHand()
        {
            if (!EmbeddedOpen || Busy || Model.IsAway || marketOnly) return;
            Result(Model.ImproveHand(), "В руке стало на одно место больше.", config.coinSound);
        }

        void SelectTile(DeckCardView tile)
        {
            if (Busy || Model.IsAway) return;
            SelectedCopyId = tile.CopyId;
            SelectedDefinitionId = tile.DefinitionId;
            Sound(config.cardSound);
            RefreshWorkshop();
        }

        void ChangePage(int delta)
        {
            page = Mathf.Max(0, page + delta);
            RefreshWorkshop();
        }

        void Result(string error, string success, AudioClip sound)
        {
            SetFeedback(error ?? success, error != null);
            if (error == null) Sound(sound);
            RefreshWorkshop();
        }

        public void SetFeedback(string text, bool error = false)
        {
            feedback.text = text;
            feedback.color = error ? ErrorColour : MutedColour;
        }

        void Sound(AudioClip clip)
        {
            if (clip && audioSource) audioSource.PlayOneShot(clip, .65f);
        }

        // ---------- presentation ----------

        CardDefinition Definition(CardCopy copy) => copy == null ? null : config.Find(copy.Spec.Id);
        Sprite Artwork(CardCopy copy) => copy.IsReturn ? config.returnArtwork : Definition(copy).artwork;

        public void RefreshWorkshop()
        {
            if (Model == null || Model.IsAway) return;
            workshopPanel.SetActive(true);
            pageTitle.text = marketOnly ? "Карточник" : "Колода";
            eyebrow.text = marketOnly ? "ГОБЛИН / ПОКУПКА КАРТ" : "МАГИЧЕСКИЙ КОМПЬЮТЕР / ПОДГОТОВКА";
            wallet.text = Model.Coins.ToString();
            collectionTab.gameObject.SetActive(!marketOnly);
            marketTab.gameObject.SetActive(marketOnly);
            handUpgradeButton.gameObject.SetActive(!marketOnly);
            upgradeButton.gameObject.SetActive(!marketOnly);

            TintTab(collectionTab, !Market);
            TintTab(marketTab, Market);

            RefreshCollection();

            var equipped = Model.Selected();
            deckCount.text = $"{equipped.Count:00} / {config.deckLimit:00}";
            for (int i = 0; i < deckRows.Length; i++)
            {
                var copy = i < equipped.Count ? equipped[i] : null;
                deckRows[i].Bind(
                    copy, copy == null ? MutedColour : Definition(copy).accent, copy != null && copy.Id == SelectedCopyId);
            }

            int hand = Model.HandSize;
            bool maxHand = hand >= config.maximumHand;
            handSize.text = $"Рука  {hand} / {config.maximumHand}";
            handUpgradeText.text = maxHand ? "Рука улучшена" : "+1 место   ·   " + Model.HandPrice;
            handUpgradeButton.interactable = !maxHand && Model.Coins >= Model.HandPrice;
            RefreshDetail();
        }

        void RefreshCollection()
        {
            var copies = Model.Owned.ToList();
            var offers = config.cards.ToList();
            int count = Market ? offers.Count : copies.Count;
            int pages = Mathf.Max(1, Mathf.CeilToInt((float)count / collectionViews.Length));
            page = Mathf.Clamp(page, 0, pages - 1);
            collectionHeading.text = Market ? "Карточная лавка" : "Моя коллекция";
            pageNumber.text = $"{page + 1} / {pages}";
            previousPage.interactable = page > 0;
            nextPage.interactable = page + 1 < pages;
            for (int i = 0; i < collectionViews.Length; i++)
            {
                int index = page * collectionViews.Length + i;
                var view = collectionViews[i];
                if (index >= count)
                {
                    view.gameObject.SetActive(false);
                    continue;
                }
                if (Market)
                {
                    var offer = offers[index];
                    view.Bind(offer.artwork, offer.displayName, offer.purchasePrice + " монет", 0, offer.id,
                        SelectedDefinitionId == offer.id, false);
                }
                else
                {
                    var copy = copies[index];
                    bool inDeck = Model.IsSelected(copy.Id);
                    view.Bind(Artwork(copy), $"№ {copy.Id:00}  /  ур. {copy.Level}", inDeck ? "В колоде" : "В коллекции", copy.Id,
                        copy.Spec.Id, SelectedCopyId == copy.Id, inDeck);
                }
            }
        }

        void RefreshDetail()
        {
            var copy = Market ? null : Model.Find(SelectedCopyId);
            var definition = Market ? config.Find(SelectedDefinitionId) : Definition(copy);
            if (definition == null)
            {
                detailArt.enabled = false;
                detailTitle.text = "Коллекция пуста";
                detailIdentity.text = "Загляните в карточную лавку.";
                detailPower.text = detailDescription.text = "";
                equipText.text = "Выберите карту";
                upgradeText.text = "Улучшить";
                equipButton.interactable = upgradeButton.interactable = false;
                return;
            }
            detailArt.enabled = true;
            detailArt.sprite = definition.artwork;
            detailAccent.color = definition.accent;
            detailTitle.text = definition.displayName;
            detailIdentity.text =
                Market ? "Новая копия" : $"№ {copy.Id:00}  ·  УРОВЕНЬ {copy.Level}";
            detailDescription.text = definition.description;
            detailPower.text = PowerLine(definition, copy);

            if (Market)
            {
                equipText.text = "Купить  ·  " + definition.purchasePrice;
                equipButton.interactable = Model.Coins >= definition.purchasePrice;
                upgradeText.text = "Сначала купите копию";
                upgradeButton.interactable = false;
                return;
            }
            bool inDeck = Model.IsSelected(copy.Id);
            equipText.text = inDeck ? "Убрать из колоды" : "Взять в колоду";
            equipButton.interactable = inDeck || Model.Selected().Count < config.deckLimit;
            bool maxLevel = copy.Level >= config.maximumCardLevel;
            upgradeText.text = !definition.upgradable ? "Не улучшается"
                : maxLevel                           ? "Максимальный уровень"
                                                     : "Улучшить  ·  " + Model.UpgradePrice(copy);
            upgradeButton.interactable = Model.CanUpgrade(copy) && Model.Coins >= Model.UpgradePrice(copy);
        }

        /// <summary>"Импульс 8 → 9,5": the card's main number now and after the next upgrade.</summary>
        string PowerLine(CardDefinition definition, CardCopy copy)
        {
            int level = copy?.Level ?? 1;
            bool canUpgrade = copy != null && definition.upgradable && copy.Level < config.maximumCardLevel;
            // New cards name their main number in their effect asset (one source for the number).
            if (definition.effectAsset is ICardStatLine stats
                && stats.TryGetStat(out var statLabel, out var statBase, out var statUpgraded, out var statUnit))
                return statLabel + "  " + Number(level > 1 ? statUpgraded : statBase)
                    + (canUpgrade ? "  →  " + Number(statUpgraded) : "") + statUnit;
            if (!definition.upgradable) return "Без улучшения";
            bool physical = definition.action == CardAction.Impulse || definition.action == CardAction.HeavyStrike;
            float power =
                physical ? definition.ImpulseAtLevel(level) : definition.basePower + (level - 1) * definition.powerPerLevel;
            float next = physical ? definition.ImpulseAtLevel(level + 1) : power + definition.powerPerLevel;
            string label = physical                      ? "Импульс"
                : definition.action == CardAction.Freeze ? "Заморозка"
                : definition.action == CardAction.Heal   ? "Лечение"
                                                         : "Сила";
            string unit = definition.action == CardAction.Freeze ? " с" : definition.action == CardAction.Heal ? " сердец" : "";
            if (definition.action == CardAction.Heal)
            {
                // Heal power is in half hearts.
                power *= .5f;
                next *= .5f;
            }
            return label + "  " + Number(power) + (canUpgrade ? "  →  " + Number(next) : "") + unit;
        }

        static string Number(
            float value) => value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');

        static void TintTab(Button button, bool active)
        {
            var colours = button.colors;
            colours.normalColor = active ? new Color(.28f, .34f, .28f) : new Color(.13f, .17f, .15f);
            colours.highlightedColor = new Color(.33f, .38f, .31f);
            colours.selectedColor = colours.normalColor;
            button.colors = colours;
        }
    }
}
