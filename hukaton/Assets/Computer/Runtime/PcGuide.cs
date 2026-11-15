using System.Collections;
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
    /// the status line), «Прокачка» at its first opening. Nothing can merge while the helper talks. Esc skips all of
    /// it; the help window's «Показать экскурсию снова» brings it all back (the brazier's too).
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
        PcHapayWindow Hapay => computer.hapay;
        bool HapayHere => Hapay && Hapay.Available;
        int soldAtStep;
        bool sellStepRan;
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
            if (help) help.ReplayRequested -= Replay;
        }

        void OnReady()
        {
            // Windows left open from a short wake: carry on with the one that has a tour left.
            if (Try(desktopTour)) return;
            if (Cards.window.IsOpen && Try(cardsTour)) return;
            if (Upgrades.window.IsOpen && Try(upgradesTour)) return;
            if (HapayHere) Try(hapayTour);
        }

        // Opened by the player before the tour got to it (the tour still starts: its first step is then already done).
        void OnHapayOpened() { if (HapayHere) Try(hapayTour); }

        void OnCardsOpened() => Try(cardsTour);
        void OnUpgradesOpened() => Try(upgradesTour);

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
            if (next != null && computer.Running && (next == cardsTour ? Cards.window.IsOpen : next != upgradesTour || Upgrades.window.IsOpen))
                Begin(next);
            // The desktop's tour (or another) has ended: хаPAY's comes next if it is still to be shown.
            else if (next == null && computer.Running && HapayHere && id != hapayTour.id) Try(hapayTour);
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

        protected override void OnSkip(string id)
        {
            // хаPAY's tour is skipped on its own; skipping the others does not skip it.
            if (id == hapayTour.id) { Progress.Finish(hapayTour.id); pending = null; return; }
            // Esc: the whole PC guide counts as seen, not only this window's part.
            Progress.Finish(desktopTour.id);
            Progress.Finish(cardsTour.id);
            Progress.Finish(upgradesTour.id);
            pending = null;
        }

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
            return true;
        }

        PcDesktop.Program ProgramOf(PcWindow window) => Desktop.programs.FirstOrDefault(p => p.window == window);

        protected override RectTransform Resolve(string id, string key)
        {
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
                default: return null;
            }
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
