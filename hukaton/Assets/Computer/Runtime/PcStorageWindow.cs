using System.Collections.Generic;
using System.Linq;
using Hakaton.Loot;
using Hakaton.Trading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Computer
{
    /// <summary>
    /// «Склад»: how many of every kind of goods the shop holds — in the storeroom (<see cref="TradingSession.Inventory"/>,
    /// not yet on the display) and on the display (<see cref="TradingSession.Slots"/>). One row per kind with at least
    /// one copy, in a scroll view; the totals on top; the sort button cycles through <see cref="PcStock.Cycle"/> and is
    /// remembered. Refreshes when it opens and whenever the shop session changes. Read-only: nothing here moves goods.
    /// </summary>
    public sealed class PcStorageWindow : MonoBehaviour
    {
        const string SortKey = "Hakaton.Computer.StockSort";

        public PcComputer computer;
        public PcWindow window;
        [Tooltip("Names, rarities and pictures of the items (Assets/Loot/Loot catalog.asset).")]
        public LootCatalog catalog;
        [Tooltip("Baked pictures for the items without their own drawing.")]
        public PcStockIcons icons;

        [Header("List")]
        public PcScrollRect scroll;
        public RectTransform rowsRoot;
        public PcStockRow rowPrefab;
        [Tooltip("The column captions over the list.")]
        public RectTransform header;
        [Tooltip("Shown over the list instead of rows: an empty storeroom, or no shop.")]
        public TMP_Text empty;

        [Header("Totals")]
        public RectTransform totals;
        public TMP_Text storedTotal, shelvedTotal, kindsTotal;
        [Tooltip("«ещё N находок в пути»: loot of the current outing that has not reached the storeroom yet.")]
        public TMP_Text pending;

        [Header("Sorting")]
        public Button sortButton;
        public TMP_Text sortLabel;

        [Header("Texts")]
        [TextArea] public string emptyText = "Склад пуст.\nПринеси находки с вылазки.";
        [TextArea] public string noShopText = "Склад недоступен:\nлавка ещё не открыта.";
        public string openStatus = "Сколько товара на складе и на витрине. Выкладывать товар — в лавке.";

        readonly List<PcStockRow> rows = new List<PcStockRow>();
        readonly List<PcStockLine> shown = new List<PcStockLine>();
        PcStockSortMode sort;
        TradingSession subscribed;
        bool dirty;
        int seenRevision = -1;

        public PcStockSortMode Sort => sort;
        /// <summary>The lines in the order they are shown (for tests and the guide).</summary>
        public IReadOnlyList<PcStockLine> Lines => shown;
        public IReadOnlyList<PcStockRow> Rows => rows;

        void Awake()
        {
            sort = (PcStockSortMode)Mathf.Clamp(PlayerPrefs.GetInt(SortKey, 0), 0, PcStock.Cycle.Length - 1);
            if (sortButton) sortButton.onClick.AddListener(NextSort);
            window.Opened += OnOpened;
        }

        void OnDisable() => Subscribe(null);

        void Subscribe(TradingSession session)
        {
            if (subscribed == session) return;
            if (subscribed != null) subscribed.Changed -= OnShopChanged;
            subscribed = session;
            if (subscribed != null) subscribed.Changed += OnShopChanged;
            dirty = true;
        }

        void OnShopChanged() => dirty = true;

        void OnOpened()
        {
            Refresh();
            if (scroll) scroll.verticalNormalizedPosition = 1;
            window.SetStatus(openStatus);
        }

        public void NextSort()
        {
            sort = PcStock.Next(sort);
            PlayerPrefs.SetInt(SortKey, (int)sort);
            computer.Ui(PcComputer.UiSound.Click);
            Refresh();
            window.SetStatus("Сортировка: " + PcStock.Label(sort) + ".");
        }

        /// <summary>The kind's picture: its own drawing or the baked one.</summary>
        public Sprite PictureOf(string id)
        {
            var item = catalog ? catalog.Find(id) : null;
            if (item && item.icon) return item.icon;
            return icons ? icons.Baked(id) : null;
        }

        PcStockInfo Info(string id)
        {
            var item = catalog ? catalog.Find(id) : null;
            var definition = subscribed?.FindDefinition(id);
            return new PcStockInfo
            {
                Name = item ? item.displayName : definition?.Name,
                Category = definition != null ? definition.Category : item ? item.category : "",
                Price = definition?.RecommendedPrice ?? (item ? item.price : 0),
                Rarity = item ? (int)item.rarity : 0
            };
        }

        public void Refresh()
        {
            var shop = computer ? computer.Shop : null;
            Subscribe(shop);
            dirty = false;
            seenRevision = shop?.Revision ?? -1;
            shown.Clear();
            if (shop != null)
            {
                var lines = PcStock.Count(shop.Inventory.Select(item => item.Definition.Id),
                    shop.Slots.Select(item => item?.Definition.Id), Info);
                shown.AddRange(PcStock.Sort(lines, sort));
            }
            while (rows.Count < shown.Count)
            {
                var row = Instantiate(rowPrefab, rowsRoot);
                row.name = "Row " + (rows.Count + 1);
                rows.Add(row);
            }
            for (int i = 0; i < rows.Count; i++)
            {
                bool used = i < shown.Count;
                rows[i].gameObject.SetActive(used);
                if (!used) continue;
                var line = shown[i];
                var item = catalog ? catalog.Find(line.Id) : null;
                rows[i].Bind(line, PictureOf(line.Id), item && catalog ? catalog.Colour(item.rarity) : Color.white, i % 2 == 1);
            }

            int stored = shown.Sum(l => l.Stored), shelved = shown.Sum(l => l.Shelved);
            storedTotal.text = shop != null ? $"На складе: {stored}" : "На складе: —";
            shelvedTotal.text = shop != null ? $"На витрине: {shelved}" : "На витрине: —";
            kindsTotal.text = shop != null ? $"Видов: {shown.Count}" : "Видов: —";
            if (empty)
            {
                empty.gameObject.SetActive(shown.Count == 0);
                empty.text = shop == null ? noShopText : emptyText;
            }
            if (header) header.gameObject.SetActive(shown.Count > 0);
            int waiting = shop?.PendingLootCount ?? 0;
            if (pending)
            {
                pending.gameObject.SetActive(waiting > 0);
                pending.text = waiting > 0 ? $"Ещё {PcStock.Finds(waiting)} с вылазки в пути: попадут на склад, когда вернёшься в лавку." : "";
            }
            if (sortLabel) sortLabel.text = "Сортировка: " + PcStock.Label(sort);
        }

        void Update()
        {
            if (!window.IsOpen) return;
            var shop = computer ? computer.Shop : null;
            // The session can arrive after the window (the game hands it over) or change under an open window.
            if (shop != subscribed || dirty || (shop != null && shop.Revision != seenRevision)) Refresh();
        }

        /// <summary>
        /// The parts of the window a guide can point at, by key: «icon», «totals», «sort», «header», «list», «empty»,
        /// «pending», «status», «close»; null when the part is absent or hidden.
        /// </summary>
        public RectTransform Spot(string key)
        {
            RectTransform Of(Component c) => c && c.gameObject.activeInHierarchy ? (RectTransform)c.transform : null;
            switch (key)
            {
                case "icon":
                    var program = computer && computer.desktop ? computer.desktop.programs.FirstOrDefault(p => p.window == window) : null;
                    return program?.icon ? (RectTransform)program.icon.transform : null;
                case "totals": return totals && totals.gameObject.activeInHierarchy ? totals : null;
                case "sort": return Of(sortButton);
                case "header": return header && header.gameObject.activeInHierarchy ? header : null;
                case "list": return Of(scroll);
                case "empty": return Of(empty);
                case "pending": return Of(pending);
                case "status": return window.status ? Of(window.status) : null;
                case "close": return Of(window.closeButton);
                default: return null;
            }
        }
    }
}
