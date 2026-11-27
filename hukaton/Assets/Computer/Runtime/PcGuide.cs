using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Hakaton.Decks.Unity;
using Hakaton.Guide;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Computer
{
    /// <summary>
    /// The PC's first-open guide, told by «Гоблинсофт»'s helper, in parts, each when it is needed (balance stage 10):
    /// a short look at the desktop at the first start-up (who he is and what is here), «Карты» at their first opening
    /// (counts, shades, a click in and out of the deck, the hover card, sorting, the status line, «Пуск»), the merge
    /// (shown on two pretend cards) when «Карты» open with two equal copies to merge, «Прокачка» and «Склад» at their
    /// first opening. None starts by itself before the first outing (<see cref="GuideRunner.AutoStartAllowed"/>).
    /// Nothing can merge while the helper talks. Esc skips the part playing; the help window's «Показать экскурсию
    /// снова» plays them all one after another (and brings the brazier's back too).
    /// </summary>
    public sealed class PcGuide : GuideRunner
    {
        public const string Prefix = "pc.";

        public PcComputer computer;
        public PcHelper helper;
        public PcTutorialWindow help;
        [Tooltip("Where the pretend cards of the merge show are laid (over the cards window, under the light).")]
        public RectTransform demoLayer;
        public GuideTour desktopTour = new GuideTour { id = Prefix + "desktop" };
        public GuideTour cardsTour = new GuideTour { id = Prefix + "cards" };
        [Tooltip("The merge, when «Карты» open and two equal copies can be merged (or after «Карты» in the replay).")]
        public GuideTour mergeTour = new GuideTour { id = Prefix + "merge" };
        public GuideTour upgradesTour = new GuideTour { id = Prefix + "upgrades" };
        [Tooltip("хаPAY, at the first start-up after the account is bought (its own tour: the other PC tours being skipped does not skip it).")]
        public GuideTour hapayTour = new GuideTour
        {
            id = Prefix + "hapay",
            steps = new[]
            {
                new GuideStep { key = "hapay-icon", kind = GuideStepKind.Do,
                    text = "Ого! У тебя теперь аккаунт на хаPAY. Это аукцион коллекционеров. Открой его." },
                new GuideStep { key = "hapay-lots", kind = GuideStepKind.Read,
                    text = "Это лоты. Каждый коллекционер хочет набор целиком и платит за него больше, чем вышло бы на прилавке." },
                new GuideStep { key = "hapay-items", kind = GuideStepKind.Read,
                    text = "Под каждым предметом — сколько есть и сколько нужно. Оранжевая метка: этот заберут с витрины." },
                new GuideStep { key = "hapay-timer", kind = GuideStepKind.Read,
                    text = "Время лота идёт, пока ты играешь: и в лавке, и на вылазке. Не успел — коллекционер уйдёт, придёт другой." },
                new GuideStep { key = "hapay-sell", kind = GuideStepKind.Do,
                    text = "А этот набор у тебя уже есть. Жми «Продать»!" },
                new GuideStep { key = "hapay-sell-demo", kind = GuideStepKind.Watch,
                    text = "Собрал набор — жмёшь «Продать», и деньги сразу в кассе. Товар коллекционер заберёт сам." },
                new GuideStep { key = "hapay-end", kind = GuideStepKind.Read,
                    text = "Вот и всё. Удачной охоты за коллекциями. И не открывай дверь." }
            }
        };
        [Tooltip("At the first opening of «Склад». A new id: players who saw the rest of the PC guide get it too.")]
        public GuideTour storageTour = new GuideTour
        {
            id = Prefix + "storage",
            steps = new[]
            {
                Line(StorageWindowKey, "Это «Склад». Всё, что вы принесли с вылазок и ещё не продали, числится здесь. Я пересчитал. Дважды."),
                Line("storage-totals", "Сверху итог: сколько штук на складе, сколько уже стоит на витрине и сколько всего видов."),
                Line("storage-header", "Одна строка — один вид товара. Крупно — сколько на складе, рядом — на витрине. Цена рекомендуемая: от неё и торгуйтесь."),
                Line("storage-list", "Выкладывать на витрину — в лавке, руками. Компьютер только считает: двигать товар он боится."),
                Line("storage-empty", "Пока пусто. Сходите на вылазку: всё, что найдёте, само ляжет сюда, когда вернётесь в лавку."),
                Line("storage-sort", "Эта кнопка меняет порядок по кругу: по количеству, имени, категории, редкости, цене."),
                Line("storage-close", "Закрывается крестиком, как всё у нас. Пусть товар не залёживается!")
            }
        };

        /// <summary>The «Склад» tour's first step: the window's name in its title bar (the whole window would stand
        /// under the helper's balloon).</summary>
        public const string StorageWindowKey = "storage-window";
        /// <summary>The desktop tour's step about the «Склад» icon (inserted after «icons»).</summary>
        public const string StorageIconKey = "storage-icon";
        /// <summary>The keys of the «Склад» steps (and the desktop's icon step) and the part of the window each is about
        /// (<see cref="PcStorageWindow.Spot"/>). Three are lit tighter, to stay clear of the helper's balloon in the
        /// corner: «header» with the first row under it, «list» as its first rows, «empty» as the note's own lines.</summary>
        public static readonly IReadOnlyDictionary<string, string> StorageSpots = new Dictionary<string, string>
        {
            { StorageIconKey, "icon" },
            { "storage-totals", "totals" },
            { "storage-header", "header" },
            { "storage-list", "list" },
            { "storage-empty", "empty" },
            { "storage-pending", "pending" },
            { "storage-sort", "sort" },
            { "storage-status", "status" },
            { "storage-close", "close" }
        };

        /// <summary>A key the guide lights for the «Склад» window (the whole window or one of its parts).</summary>
        public static bool KnowsStorageKey(string key) => key == StorageWindowKey || (key != null && StorageSpots.ContainsKey(key));

        /// <summary>
        /// A record from before the parts (balance stage 10), for <see cref="GuideProgress.Restore"/>. Whoever saw a whole
        /// tour has seen all its parts; a tour left half-way counts the parts whose steps had already gone by. The old
        /// desktop was hello, icons, storage-icon, start, tray, open-cards (the first three are its part now; «start» went to
        /// «Карты», «tray» to «Прокачка», «open-cards» is gone); the old «Карты» had the merge as its fifth step (index 4).
        /// </summary>
        public static void UpgradeOlderRecord(GuideProgress progress, int fromVersion)
        {
            if (progress == null || fromVersion >= 1) return;
            string desktop = Prefix + "desktop", cards = Prefix + "cards", merge = Prefix + "merge";
            if (!progress.IsDone(desktop) && progress.StepOf(desktop) >= OldDesktopPartSteps) progress.Finish(desktop);
            if (progress.IsDone(cards)) progress.Finish(merge);
            else
            {
                int step = progress.StepOf(cards);
                if (step > OldMergeStep)
                {
                    progress.Finish(merge);
                    progress.SetStep(cards, step - 1);
                }
            }
        }

        /// <summary>The old desktop tour's steps that make the desktop part now (hello, icons, storage-icon).</summary>
        const int OldDesktopPartSteps = 3;
        /// <summary>Where the merge stood in the old «Карты» tour (counts, faded, toggle, toggle-back, merge…).</summary>
        const int OldMergeStep = 4;

        static GuideStep Line(string key, string text) => new GuideStep { key = key, text = text, kind = GuideStepKind.Read, padding = 2 };

        [Header("Merge show")]
        [Tooltip("Gap between the two pretend cards, in PC pixels.")]
        [Min(0)] public float cardGap = 24;
        [Min(.05f)] public float travelSeconds = .6f, dragSeconds = .9f;

        // Parts asked for while another one plays, in order; the replay's parts still to come.
        readonly List<GuideTour> pending = new List<GuideTour>();
        readonly List<GuideTour> replay = new List<GuideTour>();
        bool replaying;
        int deckAtStart, deckBefore;
        RectTransform pretendSource, pretendTarget;
        Rect pretendArea;

        PcDesktop Desktop => computer.desktop;
        PcCardsWindow Cards => computer.cards;
        PcUpgradesWindow Upgrades => computer.upgrades;
        PcHapayWindow Hapay => computer.hapay;
        bool HapayHere => Hapay && Hapay.Available;
        int soldAtStep;
        bool sellStepRan;
        PcStorageWindow Storage => computer.storage;
        bool StorageHasRows => Storage && Storage.Lines.Count > 0;
        int DeckCount => computer.workshop ? computer.workshop.Model.Selected().Count : 0;

        protected override IGuideVoice Voice => helper;

        void Awake()
        {
            if (!computer) return;
            computer.Ready += OnReady;
            computer.Closed += OnClosed;
            Cards.window.Opened += OnCardsOpened;
            Upgrades.window.Opened += OnUpgradesOpened;
            if (Hapay) Hapay.window.Opened += OnHapayOpened;
            if (Storage) Storage.window.Opened += OnStorageOpened;
            if (help) help.ReplayRequested += Replay;
            Finished += OnFinished;
        }

        void OnDestroy()
        {
            if (!computer) return;
            computer.Ready -= OnReady;
            computer.Closed -= OnClosed;
            if (Cards) Cards.window.Opened -= OnCardsOpened;
            if (Upgrades) Upgrades.window.Opened -= OnUpgradesOpened;
            if (Hapay) Hapay.window.Opened -= OnHapayOpened;
            if (Storage) Storage.window.Opened -= OnStorageOpened;
            if (help) help.ReplayRequested -= Replay;
        }

        void OnReady()
        {
            if (replaying) return;
            // Windows left open from a short wake: carry on with the one that has a part left.
            if (Try(desktopTour)) return;
            if (Cards.window.IsOpen && TryCards()) return;
            if (Upgrades.window.IsOpen && Try(upgradesTour)) return;
            if (Storage && Storage.window.IsOpen && Try(storageTour)) return;
            if (HapayHere) Try(hapayTour);
        }

        // Opened by the player before the tour got to it (the tour still starts: its first step is then already done).
        void OnHapayOpened() { if (HapayHere && !replaying) Try(hapayTour); }

        void OnCardsOpened() { if (!replaying) TryCards(); }
        void OnUpgradesOpened() { if (!replaying) Try(upgradesTour); }

        void OnStorageOpened()
        {
            if (replaying) return;
            // Counted first, whatever order the window and the guide heard «opened» in: the steps ask about the rows.
            Storage.Refresh();
            Try(storageTour);
        }

        /// <summary>«Карты»: their own part, then the merge if two equal copies can merge now. True when one began.</summary>
        bool TryCards()
        {
            bool began = Try(cardsTour);
            if (MergeDue) began |= Try(mergeTour);
            return began;
        }

        /// <summary>Two equal copies in the collection can be merged (the merge part is about them).</summary>
        public bool MergeDue
        {
            get
            {
                var model = computer && computer.workshop ? computer.workshop.Model : null;
                return model != null && !model.IsAway && model.Owned.Any(c => model.MergePartners(c).Any());
            }
        }

        /// <summary>A window's part waits for its window (the desktop's needs none).</summary>
        PcWindow WindowFor(GuideTour tour)
        {
            if (tour == cardsTour || tour == mergeTour) return Cards.window;
            if (tour == upgradesTour) return Upgrades.window;
            if (tour == storageTour) return Storage ? Storage.window : null;
            if (tour == hapayTour) return Hapay ? Hapay.window : null;
            return null;
        }

        bool WindowOpenFor(GuideTour tour)
        {
            if (tour == desktopTour || tour == hapayTour) return true;
            var window = WindowFor(tour);
            return window && window.IsOpen;
        }

        void OnClosed()
        {
            pending.Clear();
            replay.Clear();
            replaying = false;
            Abort();
        }

        /// <summary>
        /// Starts a part now, or right after the one playing (opening «Карты» during the desktop's). By itself only once the
        /// game allows it (not before the first outing); хаPAY's as before.
        /// </summary>
        bool Try(GuideTour tour)
        {
            if (Seen(tour.id) || !computer.Running) return false;
            if (tour != hapayTour && !replaying && !AutoStartAllowed) return false;
            if (Running) { if (Tour != tour.id && !pending.Contains(tour)) pending.Add(tour); return false; }
            return Begin(tour);
        }

        void OnFinished(string id)
        {
            if (!computer.Running) { pending.Clear(); return; }
            if (replaying) { NextReplayPart(); return; }
            // The parts asked for meanwhile, in order: the first whose window is still open (the merge: while it is due).
            while (pending.Count > 0)
            {
                var next = pending[0];
                pending.RemoveAt(0);
                if (Seen(next.id) || !WindowOpenFor(next) || next == mergeTour && !MergeDue) continue;
                if (Begin(next)) return;
            }
            // The desktop's part (or another) has ended: хаPAY's comes next if it is still to be shown.
            if (HapayHere && id != hapayTour.id) Try(hapayTour);
        }

        /// <summary>
        /// «Показать экскурсию снова»: every guide is forgotten (the brazier's too) and the PC's parts play one after another
        /// now — the desktop, «Карты», the merge, «Прокачка», «Склад» — each window opened for its part; then хаPAY's, if
        /// the account is there. Esc skips the part playing, switching the PC off ends it all.
        /// </summary>
        public void Replay()
        {
            if (Progress == null) return;
            Abort();
            pending.Clear();
            Progress.Forget();
            Desktop.CloseAll();
            replay.Clear();
            replay.Add(cardsTour);
            replay.Add(mergeTour);
            replay.Add(upgradesTour);
            if (Storage) replay.Add(storageTour);
            replaying = true;
            if (!Begin(desktopTour)) NextReplayPart();
        }

        void NextReplayPart()
        {
            while (replay.Count > 0 && computer.Running)
            {
                var next = replay[0];
                replay.RemoveAt(0);
                if (Seen(next.id)) continue;
                var window = WindowFor(next);
                if (window && !window.IsOpen)
                {
                    Desktop.CloseAll();
                    Desktop.Open(window);
                }
                if (next == storageTour && Storage) Storage.Refresh();
                if (Begin(next)) return;
            }
            replay.Clear();
            replaying = false;
            if (computer.Running && HapayHere) Try(hapayTour);
        }

        protected override void OnBegin(string id)
        {
            // хаPAY's clocks stand while the helper explains the site.
            if (id == hapayTour.id && computer.Hapay) computer.Hapay.Frozen = true;
            sellStepRan = false;
            computer.Guided = true;
            Cards.MergeLocked = true;
            deckAtStart = DeckCount;
        }

        protected override void OnEnd(string id, bool finished)
        {
            ClearPretend();
            if (!computer) return;
            if (computer.Hapay) computer.Hapay.Frozen = false;
            computer.Guided = false;
            Cards.MergeLocked = false;
        }

        // Esc skips the part playing only (balance stage 10): the others come when they are needed (the runner marks it seen).
        protected override void OnSkip(string id) { }

        protected override void OnStep(string id, GuideStep step)
        {
            deckBefore = DeckCount;
            // The card hint from the hover step must not cover the next lit place.
            if (step.key != "hover" && Cards.tooltip) Cards.tooltip.Hide();
            if (step.key == "toggle") deckAtStart = DeckCount;
            if (step.key == "merge") MakePretend();
            if (step.key == "hapay-sell" && computer.Hapay) { soldAtStep = computer.Hapay.Market.Sold; sellStepRan = true; }
            if (step.key.StartsWith("hapay-") && step.key != "hapay-icon" && Hapay && !Hapay.window.IsOpen) Desktop.Open(Hapay.window);
        }

        protected override bool Applies(string id, GuideStep step)
        {
            if (step.key == "bar" || step.key == "price" || step.key == "buy") return Upgrades.Rows.Count > 0;
            if (step.key == "toggle-back") return DeckCount != deckAtStart;
            if (step.key == "hapay-sell") return Hapay && Hapay.FirstSellable();
            if (step.key == "hapay-sell-demo") return !sellStepRan && Hapay && !Hapay.FirstSellable() && Hapay.lots.Length > 0 && Hapay.lots[0].Lot != null;
            if (step.key == StorageIconKey) return Storage && Storage.Spot("icon");
            // A full storeroom is shown by its rows, an empty one by its note (and with no shop at all, by neither).
            if (step.key == "storage-header" || step.key == "storage-list") return StorageHasRows;
            if (step.key == "storage-empty") return Storage && computer.Shop != null && !StorageHasRows;
            if (step.key == "storage-pending") return Storage && Storage.Spot("pending");
            return true;
        }

        PcDesktop.Program ProgramOf(PcWindow window) => Desktop.programs.FirstOrDefault(p => p.window == window);

        protected override RectTransform Resolve(string id, string key)
        {
            if (key == "storage-header" || key == "storage-list" || key == "storage-empty") return null; // areas below
            if (key != null && StorageSpots.TryGetValue(key, out var part)) return Storage ? Storage.Spot(part) : null;
            var cardsProgram = ProgramOf(Cards.window);
            var firstRow = Upgrades.Rows.Count > 0 ? Upgrades.Rows[0] : null;
            switch (key)
            {
                case "start": return Desktop.startButton ? (RectTransform)Desktop.startButton.transform : null;
                case "tray": return Desktop.coins ? Desktop.coins.transform.parent as RectTransform : null;
                case "open-cards": return cardsProgram?.icon ? (RectTransform)cardsProgram.icon.transform : null;
                case "counts": return Cards.deckCount ? Cards.deckCount.transform.parent as RectTransform : null;
                case "faded":
                case "toggle":
                case "toggle-back":
                case "hover": return Cards.scroll ? (RectTransform)Cards.scroll.transform : null;
                case "sort": return Cards.sortButton ? (RectTransform)Cards.sortButton.transform : null;
                case "status": return Cards.window.status ? Cards.window.status.rectTransform : null;
                case "close": return Cards.window.closeButton ? (RectTransform)Cards.window.closeButton.transform : null;
                case "rows": return Upgrades.rowsRoot ? Upgrades.rowsRoot.parent as RectTransform : null;
                case "bar": return firstRow ? firstRow.segments : null;
                case "price": return firstRow && firstRow.priceGroup ? (RectTransform)firstRow.priceGroup.transform : null;
                // The «+» itself, not the whole row (the row is the button).
                case "buy": return firstRow ? firstRow.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t => t.name == "Buy") : null;
                case "hapay-icon": { var p = Hapay ? ProgramOf(Hapay.window) : null; return p?.icon ? (RectTransform)p.icon.transform : null; }
                case "hapay-lots": return Hapay && Hapay.lots.Length > 0 && Hapay.lots[0] ? Hapay.lots[0].transform.parent as RectTransform : null;
                case "hapay-timer": return HapayLot && HapayLot.timer ? HapayLot.timer.rectTransform : null;
                case "hapay-sell": { var view = Hapay ? Hapay.FirstSellable() : null; return view && view.sell ? (RectTransform)view.sell.transform : null; }
                case "hapay-sell-demo": return HapayLot && HapayLot.sell ? (RectTransform)HapayLot.sell.transform : null;
                case "hapay-end": return Hapay ? (RectTransform)Hapay.window.transform : null;
                default: return null;
            }
        }

        protected override Rect? ResolveArea(string id, string key)
        {
            switch (key)
            {
                case "icons": return Union(Desktop.programs.Where(p => p.icon && p.icon.gameObject.activeInHierarchy).Select(p => (RectTransform)p.icon.transform).ToArray());
                case "merge": return pretendSource ? Grow(pretendArea, 2) : (Rect?)null;
                case "hapay-items":
                    return HapayLot ? Union(HapayLot.cells.Where(c => c && c.gameObject.activeInHierarchy).Select(c => (RectTransform)c.transform).ToArray()) : null;
                case StorageWindowKey: return StorageTitle();
                case "storage-header": return Storage ? Union(FirstRows(1).Prepend(Storage.Spot("header")).ToArray()) : null;
                case "storage-list": return Union(FirstRows(ListRowsLit));
                case "storage-empty": return Storage ? TextArea(Storage.empty) : null;
                default: return null;
            }
        }

        /// <summary>How many of the first «Склад» rows stand for the whole list (the rest goes under the balloon).</summary>
        const int ListRowsLit = 3;

        RectTransform[] FirstRows(int count) => Storage
            ? Storage.Rows.Where(r => r && r.gameObject.activeInHierarchy).Take(count).Select(r => (RectTransform)r.transform).ToArray()
            : new RectTransform[0];

        /// <summary>The lines a text really takes (its rect may stretch over the whole list).</summary>
        Rect? TextArea(TMPro.TMP_Text text)
        {
            if (!text || !text.gameObject.activeInHierarchy || !spotlight) return null;
            text.ForceMeshUpdate();
            var bounds = text.textBounds;
            var space = spotlight.rectTransform;
            Vector2 a = space.InverseTransformPoint(text.transform.TransformPoint(bounds.min));
            Vector2 b = space.InverseTransformPoint(text.transform.TransformPoint(bounds.max));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        /// <summary>The «Склад» window's icon and name in its title bar.</summary>
        Rect? StorageTitle()
        {
            if (!Storage || !Storage.window.titleText) return null;
            var window = Storage.window;
            var icon = window.GetComponentsInChildren<Image>(true)
                .FirstOrDefault(i => i.sprite && i.sprite == window.icon && i.transform.parent == window.transform);
            return Union(window.titleText.rectTransform, icon ? icon.rectTransform : null);
        }

        protected override bool Done(string id, string key)
        {
            switch (key)
            {
                case "open-cards": return Cards.window.IsOpen;
                case "toggle": return DeckCount != deckBefore;
                case "toggle-back": return DeckCount == deckAtStart;
                case "hover": return Cards.tooltip && Cards.tooltip.group && Cards.tooltip.group.alpha > .5f;
                case "hapay-icon": return Hapay && Hapay.window.IsOpen;
                case "hapay-sell": return computer.Hapay && computer.Hapay.Market.Sold > soldAtStep;
                default: return true;
            }
        }

        // ---------- the merge, shown on two pretend cards ----------

        void MakePretend()
        {
            ClearPretend();
            if (!demoLayer || !Cards.tilePrefab || !spotlight) return;
            var model = computer.workshop.Model;
            var copy = model.Owned.FirstOrDefault(c => c.Spec.Mergeable && !c.Spec.Dummy) ?? model.Owned.FirstOrDefault();
            var art = copy != null ? computer.workshop.config.Find(copy.Spec.Id)?.artwork : null;
            // In the middle of the card list, side by side.
            var area = spotlight.LocalRectOf((RectTransform)Cards.scroll.transform);
            var size = ((RectTransform)Cards.tilePrefab.transform).rect.size;
            var centre = area.center;
            pretendSource = Pretend(art, centre + new Vector2(-(size.x + cardGap) * .5f, 0));
            pretendTarget = Pretend(art, centre + new Vector2((size.x + cardGap) * .5f, 0));
            pretendArea = Rect.MinMaxRect(centre.x - size.x - cardGap * .5f, centre.y - size.y * .5f, centre.x + size.x + cardGap * .5f, centre.y + size.y * .5f);
        }

        static Rect Grow(Rect r, float by) => Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);

        RectTransform Pretend(Sprite art, Vector2 at)
        {
            var tile = Instantiate(Cards.tilePrefab, demoLayer);
            tile.name = "Pretend card";
            tile.Bind(-1, art, 1, true);
            // A picture only: its own behaviour (hover lift, drag) stays off, the guide moves it.
            tile.enabled = false;
            foreach (var graphic in tile.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            if (tile.glow) tile.glow.color = Color.clear;
            if (tile.flash) tile.flash.color = new Color(1, 1, 1, 0);
            var rect = (RectTransform)tile.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = Snap(at);
            return rect;
        }

        static Vector2 Snap(Vector2 v) => new Vector2(Mathf.Round(v.x), Mathf.Round(v.y));

        void ClearPretend()
        {
            if (pretendSource) Destroy(pretendSource.gameObject);
            if (pretendTarget) Destroy(pretendTarget.gameObject);
            pretendSource = pretendTarget = null;
            if (pointer) { pointer.Press(false); pointer.Show(false); }
        }

        /// <summary>The top lot on the site (the tour talks about it).</summary>
        PcHapayLotView HapayLot => Hapay && Hapay.lots.Length > 0 && Hapay.lots[0] && Hapay.lots[0].Lot != null ? Hapay.lots[0] : null;

        /// <summary>Nothing is complete yet: the ghost pointer goes to «Продать» and clicks (nothing is sold).</summary>
        IEnumerator SellDemo()
        {
            var view = HapayLot;
            if (!view || !view.sell || !spotlight) yield break;
            var at = spotlight.LocalRectOf((RectTransform)view.sell.transform).center;
            pointer.Place(at + new Vector2(-90, -60));
            pointer.Press(false);
            pointer.Show(true);
            yield return pointer.MoveTo(at + new Vector2(4, -2), travelSeconds);
            pointer.Press(true);
            computer.Ui(PcComputer.UiSound.Click);
            yield return GhostPointer.Wait(.2f);
            pointer.Press(false);
            yield return GhostPointer.Wait(1);
            pointer.Show(false);
            yield return GhostPointer.Wait(.6f);
        }

        protected override IEnumerator Demo(string id, string key)
        {
            if (key == "hapay-sell-demo" && pointer) { yield return SellDemo(); yield break; }
            if (key != "merge" || !pointer) yield break;
            if (!pretendSource) MakePretend();
            if (!pretendSource) yield break;
            var source = pretendSource.GetComponent<PcCardTile>();
            var target = pretendTarget.GetComponent<PcCardTile>();
            Vector2 sourceAt = pretendSource.anchoredPosition, targetAt = pretendTarget.anchoredPosition;
            // Pointer space = demo layer space (both stretched over the screen).
            pointer.Place(sourceAt + new Vector2(-60, -70));
            pointer.Press(false);
            pointer.Show(true);
            yield return pointer.MoveTo(sourceAt + new Vector2(6, 10), travelSeconds);
            pointer.Press(true);
            computer.Play(computer.workshop.config.cardSound, .4f);
            if (target.glow) target.glow.color = target.partnerGlow;
            yield return GhostPointer.Wait(.25f);
            // Drag the one onto the other: the card follows the pointer.
            var grab = pretendSource.anchoredPosition - pointer.Position;
            var drag = pointer.MoveTo(targetAt + new Vector2(6, 10), dragSeconds, 16);
            while (drag.MoveNext())
            {
                pretendSource.anchoredPosition = Snap(pointer.Position + grab);
                if (target.glow && (pretendSource.anchoredPosition - targetAt).magnitude < 40) target.glow.color = target.targetGlow;
                yield return drag.Current;
            }
            pointer.Press(false);
            // Merged: the dragged one is gone, the other flares up as II.
            pretendSource.gameObject.SetActive(false);
            if (target.glow) target.glow.color = Color.clear;
            CardLevelPresentation.SetMark(target.levelMark, 2);
            var config = computer.workshop;
            computer.Play(config.mergeSound ? config.mergeSound : config.config.coinSound);
            float t = 0;
            while (t < 1)
            {
                t = Mathf.Min(1, t + Time.unscaledDeltaTime / .5f);
                if (target.flash) target.flash.color = new Color(1, 1, 1, 1 - t);
                float pop = 1 + .18f * Mathf.Sin(t * Mathf.PI) * (1 - t * .5f);
                pretendTarget.localScale = Vector3.one * pop;
                yield return null;
            }
            pretendTarget.localScale = Vector3.one;
            pointer.Show(false);
            yield return GhostPointer.Wait(1.2f);
            // Back as it was, for the next round.
            CardLevelPresentation.SetMark(target.levelMark, 1);
            pretendSource.anchoredPosition = sourceAt;
            pretendSource.gameObject.SetActive(true);
        }

        protected override void EndDemo(string id, string key)
        {
            if (key == "merge") ClearPretend();
            if (key == "hapay-sell-demo" && pointer) { pointer.Press(false); pointer.Show(false); }
        }
    }
}
