using System;
using System.Collections.Generic;
using System.Linq;
using Hakaton.Character;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Hakaton.Decks.Unity
{
    /// <summary>
    /// The magical computer in the backroom: the collection, the deck, free merging of copies (I + I → II, II + II → III),
    /// the hand and the deck limit. The game loop starts and finishes outings through <see cref="TryDepart"/> and
    /// <see cref="CompleteOuting"/>; the expedition world owns everything in between. Cards are bought from the goblin
    /// (<c>GoblinCardShop</c>) and burnt at the brazier; both work on the same <see cref="Model"/>.
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
        [Tooltip("Played when two copies merge into one.")]
        public AudioClip mergeSound;

        [Header("Service shell")]
        public CanvasGroup embeddedVisibility;
        public Button embeddedCloseButton;
        public GameObject workshopPanel;
        public TMP_Text pageTitle, eyebrow, wallet, feedback;

        [Header("Workshop")]
        public Button previousPage, nextPage;
        public Button equipButton;
        [FormerlySerializedAs("upgradeButton")] public Button mergeButton;
        public Button handUpgradeButton, deckUpgradeButton;
        public TMP_Text collectionHeading, pageNumber, deckCount, handSize;
        public TMP_Text handUpgradeText, deckUpgradeText, equipText;
        [FormerlySerializedAs("upgradeText")] public TMP_Text mergeText;
        public DeckCardView[] collectionViews;
        public DeckRowView[] deckRows;
        public Image detailArt, detailAccent;
        public TMP_Text detailLevelMark;
        public TMP_Text detailTitle, detailIdentity, detailPower, detailDescription;

        public event Action Departed;
        public event Action<IReadOnlyList<LootSave>, bool> OutingCompleted;
        public event Action EmbeddedClosed;
        /// <summary>Two copies became one (the result).</summary>
        public event Action<CardCopy> Merged;

        DeckWorkshop model;
        /// <summary>The collection: created on first use, so the goblin and the brazier can reach it before the PC wakes.</summary>
        public DeckWorkshop Model => model ??= config.CreateWorkshop(walletHost ? walletHost.Wallet : null);
        /// <summary>True from departure until the outing is delivered back.</summary>
        public bool Busy { get; private set; }
        public int SelectedCopyId { get; private set; }
        public bool EmbeddedOpen { get; private set; }
        /// <summary>The copy waiting for a merge partner, 0 when not merging.</summary>
        public int MergeSource { get; private set; }

        int page;

        static readonly Color ErrorColour = new Color(.96f, .56f, .39f);
        static readonly Color MutedColour = new Color(.55f, .63f, .57f);

        void Awake()
        {
            _ = Model;
            previousPage.onClick.AddListener(() => ChangePage(-1));
            nextPage.onClick.AddListener(() => ChangePage(1));
            equipButton.onClick.AddListener(ToggleSelected);
            mergeButton.onClick.AddListener(MergeSelected);
            handUpgradeButton.onClick.AddListener(UpgradeHand);
            if (deckUpgradeButton) deckUpgradeButton.onClick.AddListener(UpgradeDeckLimit);
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
            if (!EmbeddedOpen || Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;
            if (MergeSource != 0) CancelMerge();
            else CloseEmbedded();
        }

        // ---------- opening and closing ----------

        public bool OpenEmbedded()
        {
            if (!isActiveAndEnabled || Model.IsAway || Busy) return false;
            EmbeddedOpen = true;
            MergeSource = 0;
            page = 0;
            if (control) control.SetBlocked(this, true);
            SetVisible(true);
            if (Model.Find(SelectedCopyId) == null) SelectFirst();
            else RefreshWorkshop();
            SetFeedback("Соберите колоду. Две одинаковые копии одного уровня объединяются бесплатно.");
            return true;
        }

        public void CloseEmbedded()
        {
            bool wasOpen = EmbeddedOpen;
            EmbeddedOpen = false;
            MergeSource = 0;
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
            if (!isActiveAndEnabled) return "Мастерская недоступна.";
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
            if (Model.IsAway || !Busy) return;
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
            RefreshWorkshop();
        }

        public void SelectCopy(int id)
        {
            var copy = Model.Find(id);
            if (copy == null || Busy || Model.IsAway) return;
            if (MergeSource != 0 && TryMergeWith(id)) return;
            SelectedCopyId = id;
            page = Model.Owned.ToList().FindIndex(c => c.Id == id) / collectionViews.Length;
            RefreshWorkshop();
        }

        public void ToggleSelected()
        {
            if (!EmbeddedOpen || Busy || Model.IsAway) return;
            CancelMerge(false);
            Result(Model.Toggle(SelectedCopyId), "Состав колоды сохранён.", config.cardSound);
        }

        /// <summary>
        /// First press: the selected copy waits for a partner and the matching copies light up. A single possible
        /// partner merges at once; otherwise the player clicks the one to use. Pressing again cancels.
        /// </summary>
        public void MergeSelected()
        {
            if (!EmbeddedOpen || Busy || Model.IsAway) return;
            if (MergeSource != 0)
            {
                CancelMerge();
                return;
            }
            var copy = Model.Find(SelectedCopyId);
            var partners = Model.MergePartners(copy).ToList();
            if (partners.Count == 0)
            {
                Result(MergeRefusal(copy), null, null);
                return;
            }
            if (partners.Count == 1)
            {
                MergeSource = copy.Id;
                TryMergeWith(partners[0].Id);
                return;
            }
            MergeSource = copy.Id;
            Sound(config.cardSound);
            SetFeedback($"Выберите вторую копию «{copy.Spec.Name}» уровня {copy.Level}.");
            RefreshWorkshop();
        }

        string MergeRefusal(CardCopy copy)
        {
            if (copy == null) return "Выберите копию.";
            if (!copy.Spec.Mergeable) return "Эту карту нельзя объединить.";
            if (copy.Level >= Model.Rules.MaxCardLevel) return "Уровень 3 — максимум.";
            return $"Нужна вторая «{copy.Spec.Name}» уровня {copy.Level}.";
        }

        bool TryMergeWith(int partner)
        {
            if (partner == MergeSource) return false;
            string error = Model.Merge(MergeSource, partner, out var result);
            MergeSource = 0;
            if (error != null)
            {
                Result(error, null, null);
                return true;
            }
            SelectedCopyId = result.Id;
            page = Model.Owned.ToList().FindIndex(c => c.Id == result.Id) / collectionViews.Length;
            Result(null, $"«{result.Spec.Name}» теперь уровня {result.Level}.", mergeSound ? mergeSound : config.coinSound);
            var tile = collectionViews.FirstOrDefault(v => v.gameObject.activeSelf && v.CopyId == result.Id);
            if (tile) tile.Celebrate();
            Merged?.Invoke(result);
            return true;
        }

        void CancelMerge(bool refresh = true)
        {
            if (MergeSource == 0) return;
            MergeSource = 0;
            if (!refresh) return;
            SetFeedback("Объединение отменено.");
            RefreshWorkshop();
        }

        public void UpgradeHand()
        {
            if (!EmbeddedOpen || Busy || Model.IsAway) return;
            CancelMerge(false);
            Result(Model.ImproveHand(), "В руке стало на одно место больше.", config.coinSound);
        }

        public void UpgradeDeckLimit()
        {
            if (!EmbeddedOpen || Busy || Model.IsAway) return;
            CancelMerge(false);
            Result(Model.ImproveDeckLimit(), $"Колода вмещает {Model.DeckLimit} карт, коллекция — {Model.CollectionLimit}.",
                config.coinSound);
        }

        void SelectTile(DeckCardView tile)
        {
            if (Busy || Model.IsAway) return;
            if (MergeSource != 0 && TryMergeWith(tile.CopyId)) return;
            SelectedCopyId = tile.CopyId;
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
            if (error != null || success != null) SetFeedback(error ?? success, error != null);
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

        public void RefreshWorkshop()
        {
            if (Model.IsAway || !workshopPanel) return;
            workshopPanel.SetActive(true);
            pageTitle.text = "Колода";
            eyebrow.text = "МАГИЧЕСКИЙ КОМПЬЮТЕР / ПОДГОТОВКА";
            wallet.text = Model.Coins.ToString();

            RefreshCollection();

            var equipped = Model.Selected();
            deckCount.text = $"{equipped.Count:00} / {Model.DeckLimit:00}";
            for (int i = 0; i < deckRows.Length; i++)
            {
                var copy = i < equipped.Count ? equipped[i] : null;
                deckRows[i].Bind(
                    copy, copy == null ? MutedColour : Definition(copy).accent, copy != null && copy.Id == SelectedCopyId);
            }

            int hand = Model.HandSize;
            bool maxHand = hand >= Model.Rules.MaxHand;
            handSize.text = $"Рука  {hand} / {Model.Rules.MaxHand}";
            handUpgradeText.text = maxHand ? "Рука улучшена" : "Рука +1   ·   " + Model.HandPrice;
            handUpgradeButton.interactable = !maxHand && Model.Coins >= Model.HandPrice;
            if (deckUpgradeButton)
            {
                deckUpgradeText.text = Model.DeckAtMaximum ? "Колода максимальна"
                    : $"Колода {Model.DeckLimit} → {Model.NextDeckLimit}   ·   {Model.DeckLimitPrice}";
                deckUpgradeButton.interactable = !Model.DeckAtMaximum && Model.Coins >= Model.DeckLimitPrice;
            }
            RefreshDetail();
        }

        void RefreshCollection()
        {
            var copies = Model.Owned.ToList();
            var source = Model.Find(MergeSource);
            int count = copies.Count;
            int pages = Mathf.Max(1, Mathf.CeilToInt((float)count / collectionViews.Length));
            page = Mathf.Clamp(page, 0, pages - 1);
            collectionHeading.text = $"Коллекция  {count} / {Model.CollectionLimit}";
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
                var copy = copies[index];
                bool inDeck = Model.IsSelected(copy.Id);
                view.Bind(Definition(copy).artwork, copy.Spec.Name, inDeck ? "В колоде" : "В коллекции",
                    copy.Id, copy.Spec.Id, SelectedCopyId == copy.Id, inDeck, copy.Level);
                // While merging, only the copies that fit light up.
                view.SetDimmed(source != null && copy.Id != source.Id && Model.CanMerge(source, copy) != null);
            }
        }

        void RefreshDetail()
        {
            var copy = Model.Find(SelectedCopyId);
            var definition = Definition(copy);
            if (definition == null)
            {
                detailArt.enabled = false;
                CardLevelPresentation.SetMark(detailLevelMark, 1);
                detailTitle.text = "Коллекция пуста";
                detailIdentity.text = "Карты продаёт гоблин-карточник.";
                detailPower.text = detailDescription.text = "";
                equipText.text = "Выберите карту";
                mergeText.text = "Объединить";
                equipButton.interactable = mergeButton.interactable = false;
                return;
            }
            detailArt.enabled = true;
            detailArt.sprite = definition.artwork;
            CardLevelPresentation.SetMark(detailLevelMark, copy.Level);
            detailAccent.color = definition.accent;
            detailTitle.text = definition.displayName;
            detailIdentity.text = $"№ {copy.Id:00}  ·  УРОВЕНЬ {copy.Level}" + (copy.Protected ? "  ·  ОСНОВА" : "");
            detailDescription.text = definition.description;
            detailPower.text = PowerLine(definition, copy.Level);

            bool inDeck = Model.IsSelected(copy.Id);
            equipText.text = inDeck ? "Убрать из колоды" : "Взять в колоду";
            equipButton.interactable = inDeck || Model.Selected().Count < Model.DeckLimit;
            bool canMerge = Model.MergePartners(copy).Any();
            mergeText.text = MergeSource != 0 ? "Отменить"
                : !definition.upgradable     ? "Не объединяется"
                : copy.Level >= Model.Rules.MaxCardLevel ? "Уровень 3"
                : canMerge                   ? $"Объединить  ·  {copy.Level} + {copy.Level} → {copy.Level + 1}"
                                             : "Нужна пара";
            mergeButton.interactable = MergeSource != 0 || canMerge;
        }

        /// <summary>"Урон  3 · 5 · 8": the card's main number at I / II / III, the copy's level in bold.</summary>
        public static string PowerLine(CardDefinition definition, int level)
        {
            // The dummy has no number to show: it does nothing at any level.
            if (definition.dummy) return "Польза  " + Mark(0, true);
            string label, unit;
            LevelFloat values;
            if (definition.effectAsset is ICardStatLine stats && stats.TryGetStat(out label, out values, out unit)) { }
            else if (definition.uses.iii > 1)
            {
                label = "Использований";
                values = definition.uses;
                unit = "";
            }
            else
                switch (definition.action)
                {
                    case CardAction.Impulse:
                    case CardAction.HeavyStrike:
                        label = definition.action == CardAction.Impulse ? "Толчок" : "Урон " + Levels(definition.power, level, "") + ", толчок";
                        values = definition.impulse;
                        unit = "";
                        break;
                    case CardAction.Freeze:
                        label = "Заморозка";
                        values = definition.power;
                        unit = " с";
                        break;
                    case CardAction.Heal:
                        label = "Лечение";
                        values = new LevelFloat(definition.power.i * .5f, definition.power.ii * .5f, definition.power.iii * .5f);
                        unit = " серд.";
                        break;
                    default:
                        label = "Урон";
                        values = definition.power;
                        unit = "";
                        break;
                }
            return label + "  " + Levels(values, level, unit);
        }

        static string Levels(LevelFloat values, int level, string unit) =>
            Mark(values.i, level == 1) + " · " + Mark(values.ii, level == 2) + " · " + Mark(values.iii, level >= 3) + unit;

        static string Mark(float value, bool current) => current ? "<b><color=#F4D27A>" + Number(value) + "</color></b>" : Number(value);

        static string Number(float value) =>
            value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');
    }
}
