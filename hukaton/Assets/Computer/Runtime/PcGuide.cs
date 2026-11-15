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
    /// The PC's first-open guide, told by «Гоблинсофт»'s helper, one window at a time instead of one long lecture:
    /// the desktop at the first start-up (it ends with the player opening «Карты»), «Карты» at their first opening
    /// (counts, shades, a click in and out of the deck, a merge shown on two pretend cards, the hover card, sorting,
    /// the status line), «Прокачка» and «Склад» at their first opening. Nothing can merge while the helper talks. Esc
    /// skips all of it; the help window's «Показать экскурсию снова» brings it all back (the brazier's too).
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
        public GuideTour upgradesTour = new GuideTour { id = Prefix + "upgrades" };
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

        static GuideStep Line(string key, string text) => new GuideStep { key = key, text = text, kind = GuideStepKind.Read, padding = 2 };

        [Header("Merge show")]
        [Tooltip("Gap between the two pretend cards, in PC pixels.")]
        [Min(0)] public float cardGap = 24;
        [Min(.05f)] public float travelSeconds = .6f, dragSeconds = .9f;

        GuideTour pending;
        int deckAtStart, deckBefore;
        RectTransform pretendSource, pretendTarget;
        Rect pretendArea;

        PcDesktop Desktop => computer.desktop;
        PcCardsWindow Cards => computer.cards;
        PcUpgradesWindow Upgrades => computer.upgrades;
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
            if (Storage) Storage.window.Opened -= OnStorageOpened;
            if (help) help.ReplayRequested -= Replay;
        }

        void OnReady()
        {
            // Windows left open from a short wake: carry on with the one that has a tour left.
            if (Try(desktopTour)) return;
            if (Cards.window.IsOpen && Try(cardsTour)) return;
            if (Upgrades.window.IsOpen && Try(upgradesTour)) return;
            if (Storage && Storage.window.IsOpen) Try(storageTour);
        }

        void OnCardsOpened() => Try(cardsTour);
        void OnUpgradesOpened() => Try(upgradesTour);

        void OnStorageOpened()
        {
            // Counted first, whatever order the window and the guide heard «opened» in: the steps ask about the rows.
            Storage.Refresh();
            Try(storageTour);
        }

        /// <summary>A window's part waits for its window (the desktop's needs none).</summary>
        bool WindowOpenFor(GuideTour tour)
        {
            if (tour == cardsTour) return Cards.window.IsOpen;
            if (tour == upgradesTour) return Upgrades.window.IsOpen;
            if (tour == storageTour) return Storage && Storage.window.IsOpen;
            return true;
        }

        void OnClosed()
        {
            pending = null;
            Abort();
        }

        /// <summary>Starts a tour now, or right after the one playing (opening «Карты» ends the desktop's).</summary>
        bool Try(GuideTour tour)
        {
            if (Seen(tour.id) || !computer.Running) return false;
            if (Running) { if (Tour != tour.id) pending = tour; return false; }
            return Begin(tour);
        }

        void OnFinished(string id)
        {
            var next = pending;
            pending = null;
            if (next != null && computer.Running && WindowOpenFor(next)) Begin(next);
        }

        /// <summary>«Показать экскурсию снова»: every guide is forgotten, the desktop's starts over now.</summary>
        public void Replay()
        {
            if (Progress == null) return;
            Abort();
            Progress.Forget();
            Desktop.CloseAll();
            Begin(desktopTour);
        }

        protected override void OnBegin(string id)
        {
            computer.Guided = true;
            Cards.MergeLocked = true;
            deckAtStart = DeckCount;
        }

        protected override void OnEnd(string id, bool finished)
        {
            ClearPretend();
            if (!computer) return;
            computer.Guided = false;
            Cards.MergeLocked = false;
        }

        protected override void OnSkip(string id)
        {
            // Esc: the whole PC guide counts as seen, not only this window's part.
            Progress.Finish(desktopTour.id);
            Progress.Finish(cardsTour.id);
            Progress.Finish(upgradesTour.id);
            Progress.Finish(storageTour.id);
            pending = null;
        }

        protected override void OnStep(string id, GuideStep step)
        {
            deckBefore = DeckCount;
            // The card hint from the hover step must not cover the next lit place.
            if (step.key != "hover" && Cards.tooltip) Cards.tooltip.Hide();
            if (step.key == "toggle") deckAtStart = DeckCount;
            if (step.key == "merge") MakePretend();
        }

        protected override bool Applies(string id, GuideStep step)
        {
            if (step.key == "bar" || step.key == "price" || step.key == "buy") return Upgrades.Rows.Count > 0;
            if (step.key == "toggle-back") return DeckCount != deckAtStart;
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
                default: return null;
            }
        }

        protected override Rect? ResolveArea(string id, string key)
        {
            switch (key)
            {
                case "icons": return Union(Desktop.programs.Where(p => p.icon).Select(p => (RectTransform)p.icon.transform).ToArray());
                case "merge": return pretendSource ? Grow(pretendArea, 2) : (Rect?)null;
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

        protected override IEnumerator Demo(string id, string key)
        {
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
        }
    }
}
