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
    /// level merges them (I + I → II, II + II → III); <see cref="PcMergeShow"/> plays the merge in the grid, then the
    /// cards glide to their new cells. The sort button cycles through <see cref="PcSort.Cycle"/>.
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
        [Tooltip("Plays a merge right in the grid (optional: without it the merge shows at once).")]
        public PcMergeShow mergeShow;

        readonly List<PcCardTile> tiles = new List<PcCardTile>();
        PcSortMode sort;
        PcCardTile hovered, dragged, target;
        bool merging;

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
            // Being disabled: no refreshing now; the window refreshes when it opens again.
            if (mergeShow) mergeShow.Abandon();
            CancelDrag();
        }

        void OnOpened()
        {
            Refresh();
            scroll.verticalNormalizedPosition = 1;
            window.SetStatus("Клик — в колоду или из неё. Перетащите карту на такую же, чтобы объединить.");
        }

        bool ShowPlaying => mergeShow && mergeShow.Playing;

        void OnModelChanged()
        {
            // While a merge is being committed or shown, the grid waits for the show's end.
            if (window.IsOpen && !dragged && !merging && !ShowPlaying) Refresh();
        }

        public void NextSort()
        {
            if (mergeShow) mergeShow.Finish();
            sort = PcSort.Next(sort);
            PlayerPrefs.SetInt(SortKey, (int)sort);
            computer.Ui(PcComputer.UiSound.Click);
            Refresh();
            window.SetStatus("Сортировка: " + PcSort.Label(sort) + ".");
        }

        public void Refresh()
        {
            // Anyone refreshing mid-show gets the settled grid first (the show ends, then this refresh runs).
            if (ShowPlaying) mergeShow.Finish();
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
            if (!Ready || dragged || mergeShow && mergeShow.SwallowsClick) return;
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
            if (!Ready || ShowPlaying)
            {
                // A drag that starts mid-show was aimed at the old grid: skip the show and let the player pick again.
                if (ShowPlaying) mergeShow.Finish();
                tile.EndDragNow();
                return;
            }
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
            // The dropped copy goes into the one it was dropped on: the result keeps that one's place. The model is
            // merged right now (anything saved or read meanwhile sees the result); only the picture waits for the show.
            string error;
            CardCopy result;
            merging = true;
            try { error = Model.Merge(into.Id, source.Id, out result); }
            finally { merging = false; }
            if (error != null)
            {
                Restore();
                window.SetStatus(error, true);
                computer.Ui(PcComputer.UiSound.Error);
                return;
            }
            foreach (var t in tiles) t.SetRole(PcCardTile.Role.None);
            window.SetStatus($"Карта «{result.Spec.Name}» улучшена до {CardCopy.UpgradeMark(result.Level)}.");
            int resultId = result.Id;
            var definition = Workshop.config.Find(result.Spec.Id);
            tile.Hold();
            var show = new PcMergeShow.Request
            {
                ghost = tile, target = under, resultLevel = result.Level, resultInDeck = Model.IsSelected(resultId),
                accent = definition ? definition.accent : Color.white
            };
            if (mergeShow && mergeShow.Play(show, revealed => SettleMerge(resultId, revealed))) return;
            tile.Vanish();
            SettleMerge(resultId, false);
            computer.Play(Workshop.mergeSound ? Workshop.mergeSound : Workshop.config.coinSound);
        }

        /// <summary>
        /// After a merge: the grid shows the new collection, and every card that changed cell glides there from where
        /// it was (the merged card from the merge cell). <paramref name="revealed"/>: the show already presented the
        /// new level; otherwise the merged card flares up now.
        /// </summary>
        void SettleMerge(int resultId, bool revealed)
        {
            if (!computer || !Workshop) return;
            var drawnAt = new Dictionary<int, Vector3>();
            PcCardTile before = null;
            foreach (var t in tiles)
            {
                if (!t || !t.isActiveAndEnabled || t.Gone || t.Dragged || t.Held) continue;
                drawnAt[t.CopyId] = t.body.position;
                if (t.CopyId == resultId) before = t;
            }
            Refresh();
            LayoutRebuilder.ForceRebuildLayoutImmediate(grid);
            foreach (var t in tiles)
                if (t && t.isActiveAndEnabled && drawnAt.TryGetValue(t.CopyId, out var at)) t.GlideFrom(at);
            var merged = tiles.FirstOrDefault(t => t && t.isActiveAndEnabled && t.CopyId == resultId);
            if (!merged) return;
            if (!revealed) merged.Celebrate();
            else if (merged != before) merged.Pop();
        }

        void Restore()
        {
            foreach (var t in tiles) t.SetRole(PcCardTile.Role.None);
            window.SetStatus("Клик — в колоду или из неё. Перетащите карту на такую же, чтобы объединить.");
        }

        public void CancelDrag()
        {
            if (mergeShow) mergeShow.Stop();
            if (dragged) dragged.EndDragNow();
            if (target) target.SetTargeted(false);
            dragged = target = null;
            PcCursor.Grabbing = false;
            foreach (var t in tiles) if (t) t.SetRole(PcCardTile.Role.None);
            HideTooltip();
        }
    }
}
