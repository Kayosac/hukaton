using System.Collections.Generic;
using System.Linq;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Hakaton.Computer
{
    /// <summary>
    /// «Карты»: every owned copy in a scrolling grid, drawn like the brazier's list. Copies outside the deck are
    /// translucent; a click puts a copy into the deck or takes it out; dragging a copy onto an equal one of the same
    /// level merges them (I + I → II, II + II → III). The sort button cycles through <see cref="PcSort.Cycle"/>.
    /// </summary>
    public sealed class PcCardsWindow : MonoBehaviour
    {
        const string SortKey = "Hakaton.Computer.CardSort";

        public PcComputer computer;
        public PcWindow window;
        public PcScrollRect scroll;
        [Tooltip("The grid inside the scroll view.")]
        public RectTransform grid;
        public PcCardTile tilePrefab;
        [Tooltip("Above the scroll view's mask: a dragged card is never clipped.")]
        public RectTransform dragLayer;
        public PcTooltip tooltip;
        public TMP_Text deckCount, collectionCount, sortLabel;
        public Button sortButton;

        readonly List<PcCardTile> tiles = new List<PcCardTile>();
        PcSortMode sort;
        PcCardTile hovered, dragged, target;

        DeckPrototype Workshop => computer.workshop;
        DeckWorkshop Model => Workshop.Model;
        public IReadOnlyList<PcCardTile> Tiles => tiles;
        public PcSortMode Sort => sort;

        void Awake()
        {
            sort = (PcSortMode)Mathf.Clamp(PlayerPrefs.GetInt(SortKey, 0), 0, PcSort.Cycle.Length - 1);
            sortButton.onClick.AddListener(NextSort);
            window.Opened += OnOpened;
        }

        void OnEnable()
        {
            if (Workshop) Model.Changed += OnModelChanged;
        }

        void OnDisable()
        {
            if (Workshop) Model.Changed -= OnModelChanged;
            CancelDrag();
        }

        void OnOpened()
        {
            Refresh();
            scroll.verticalNormalizedPosition = 1;
            window.SetStatus("Клик — в колоду или из неё. Перетащите карту на такую же, чтобы объединить.");
        }

        void OnModelChanged()
        {
            if (window.IsOpen && !dragged) Refresh();
        }

        public void NextSort()
        {
            sort = PcSort.Next(sort);
            PlayerPrefs.SetInt(SortKey, (int)sort);
            computer.Ui(PcComputer.UiSound.Click);
            Refresh();
            window.SetStatus("Сортировка: " + PcSort.Label(sort) + ".");
        }

        public void Refresh()
        {
            var copies = PcSort.Apply(Model.Owned, sort, Model.IsSelected);
            while (tiles.Count < copies.Count)
            {
                var tile = Instantiate(tilePrefab, grid);
                tile.name = "Card " + (tiles.Count + 1);
                tile.BeginDragOn(dragLayer);
                tile.Clicked += OnClicked;
                tile.HoverStarted += OnHoverStarted;
                tile.HoverEnded += OnHoverEnded;
                tile.DragStarted += OnDragStarted;
                tile.Dragging += OnDragging;
                tile.Dropped += OnDropped;
                tiles.Add(tile);
            }
            for (int i = 0; i < tiles.Count; i++)
            {
                var tile = tiles[i];
                if (i >= copies.Count) { tile.Hide(); continue; }
                var copy = copies[i];
                tile.transform.SetSiblingIndex(i);
                tile.Bind(copy.Id, Workshop.config.Find(copy.Spec.Id)?.artwork, copy.Level, Model.IsSelected(copy.Id));
                tile.SetRole(PcCardTile.Role.None);
            }
            deckCount.text = $"Колода: {Model.Selected().Count} / {Model.DeckLimit}";
            collectionCount.text = $"Коллекция: {Model.Owned.Count} / {Model.CollectionLimit}";
            sortLabel.text = "Сортировка: " + PcSort.Label(sort);
            if (hovered && (!hovered.isActiveAndEnabled || Model.Find(hovered.CopyId) == null)) HideTooltip();
        }

        bool Ready => computer.Running && window.IsOpen;

        // ---------- deck ----------

        void OnClicked(PcCardTile tile)
        {
            if (!Ready || dragged) return;
            var copy = Model.Find(tile.CopyId);
            if (copy == null) return;
            bool wasIn = Model.IsSelected(copy.Id);
            string error = Model.Toggle(copy.Id);
            if (error != null)
            {
                tile.Refuse();
                window.SetStatus(error, true);
                computer.Ui(PcComputer.UiSound.Error);
                return;
            }
            computer.Play(Workshop.config.cardSound);
            window.SetStatus(wasIn ? $"Карта «{copy.Spec.Name}» убрана из колоды." : $"Карта «{copy.Spec.Name}» теперь в колоде.");
            Refresh();
            var bound = tiles.FirstOrDefault(t => t.isActiveAndEnabled && t.CopyId == copy.Id);
            if (bound) bound.Pop();
            ShowTooltip(bound);
        }

        // ---------- tooltip ----------

        void OnHoverStarted(PcCardTile tile)
        {
            hovered = tile;
            if (!dragged) ShowTooltip(tile);
        }

        void OnHoverEnded(PcCardTile tile)
        {
            if (hovered != tile) return;
            HideTooltip();
        }

        void ShowTooltip(PcCardTile tile)
        {
            if (!tile || !tooltip) return;
            var copy = Model.Find(tile.CopyId);
            var definition = copy == null ? null : Workshop.config.Find(copy.Spec.Id);
            if (definition == null) { HideTooltip(); return; }
            hovered = tile;
            string level = copy.Level <= 1 ? "Базовая" : "Улучшение " + CardCopy.UpgradeMark(copy.Level);
            string where = Model.IsSelected(copy.Id) ? "в колоде" : "в коллекции";
            string line = $"{level}  ·  {where}" + (copy.Protected ? "  ·  основа" : "");
            string power = DeckPrototype.PowerLine(definition, copy.Level).Replace("#F4D27A", "#1E5BD8");
            tooltip.Show(definition.displayName, $"<color=#555555>{line}</color>\n{power}\n{definition.description}");
        }

        void HideTooltip()
        {
            hovered = null;
            if (tooltip) tooltip.Hide();
        }

        void Update()
        {
            if (!tooltip || !hovered || dragged || !CrtRaycaster.Current || Mouse.current == null) return;
            CrtRaycaster.Current.ToLocal(tooltip.bounds, Mouse.current.position.ReadValue(), out var local);
            tooltip.Follow(local);
        }

        // ---------- merging ----------

        void OnDragStarted(PcCardTile tile)
        {
            if (!Ready) { tile.EndDragNow(); return; }
            dragged = tile;
            PcCursor.Grabbing = true;
            HideTooltip();
            var copy = Model.Find(tile.CopyId);
            var partners = new HashSet<int>(Model.MergePartners(copy).Select(c => c.Id));
            foreach (var t in tiles)
            {
                if (!t.isActiveAndEnabled) continue;
                t.SetRole(t == tile ? PcCardTile.Role.Source : partners.Contains(t.CopyId) ? PcCardTile.Role.Partner : PcCardTile.Role.Other);
            }
            computer.Play(Workshop.config.cardSound, .4f);
            window.SetStatus(partners.Count > 0
                ? $"Отпустите на другую «{copy.Spec.Name}», чтобы объединить."
                : NoPartnerReason(copy), partners.Count == 0);
        }

        string NoPartnerReason(CardCopy copy)
        {
            if (!copy.Spec.Mergeable) return "Эту карту нельзя объединить.";
            if (copy.Level >= Model.Rules.MaxCardLevel) return "Улучшение II — максимум.";
            return $"Нужна вторая «{copy.Spec.Name}» того же уровня.";
        }

        PcCardTile TileUnder(PointerEventData e)
        {
            var go = e.pointerCurrentRaycast.gameObject;
            var tile = go ? go.GetComponentInParent<PcCardTile>() : null;
            return tile && tile != dragged && tile.isActiveAndEnabled ? tile : null;
        }

        void OnDragging(PcCardTile tile, PointerEventData e)
        {
            var under = TileUnder(e);
            if (under == target) return;
            if (target) target.SetTargeted(false);
            target = under;
            if (target) target.SetTargeted(true);
        }

        void OnDropped(PcCardTile tile, PointerEventData e)
        {
            var under = TileUnder(e);
            if (target) target.SetTargeted(false);
            target = null;
            dragged = null;
            PcCursor.Grabbing = false;
            if (under == null || !Ready)
            {
                Restore();
                return;
            }
            var source = Model.Find(tile.CopyId);
            var into = Model.Find(under.CopyId);
            string refusal = Model.CanMerge(into, source);
            if (refusal != null)
            {
                Restore();
                under.Refuse();
                window.SetStatus(refusal == "Уровень 3 — максимум." || refusal == "Уровень III — максимум." ? "Улучшение II — максимум." : refusal, true);
                computer.Ui(PcComputer.UiSound.Error);
                return;
            }
            // The dropped copy goes into the one it was dropped on: the result keeps that one's place.
            string error = Model.Merge(into.Id, source.Id, out var result);
            if (error != null)
            {
                Restore();
                window.SetStatus(error, true);
                computer.Ui(PcComputer.UiSound.Error);
                return;
            }
            tile.Vanish();
            Refresh();
            var merged = tiles.FirstOrDefault(t => t.isActiveAndEnabled && t.CopyId == result.Id);
            if (merged) merged.Celebrate();
            computer.Play(Workshop.mergeSound ? Workshop.mergeSound : Workshop.config.coinSound);
            window.SetStatus($"Карта «{result.Spec.Name}» улучшена до {CardCopy.UpgradeMark(result.Level)}.");
        }

        void Restore()
        {
            foreach (var t in tiles) t.SetRole(PcCardTile.Role.None);
            window.SetStatus("Клик — в колоду или из неё. Перетащите карту на такую же, чтобы объединить.");
        }

        public void CancelDrag()
        {
            if (dragged) dragged.EndDragNow();
            if (target) target.SetTargeted(false);
            dragged = target = null;
            PcCursor.Grabbing = false;
            foreach (var t in tiles) if (t) t.SetRole(PcCardTile.Role.None);
            HideTooltip();
        }
    }
}
