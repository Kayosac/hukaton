using System;
using System.Linq;
using Hakaton.Character;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.DialogueUI;
using Hakaton.SpecialItems;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Hakaton.Backroom
{
    /// <summary>
    /// The goblin card dealer's table, drawn right over the game view while the player keeps looking at him (board,
    /// «Гоблин-торговец»): ten cards in a row, face down at half price or all turned up at full price, chosen with the
    /// mouse; player movement is blocked. On the first visit he talks first (four lines), then the table opens.
    /// A bought card is brought up to the eyes (<see cref="CardInspect"/>): the table steps aside, the left button held
    /// turns the card. A click, E, Space or Enter at any moment (on its way up, held, or on its way out) puts it away at
    /// once and snaps the table back; Esc or the right button sends it off into the bag with the spin.
    /// A dummy («Пустышка») is mocked when it shows its face: at the flip at the eyes when bought blind, at once face up.
    /// The rules live in <see cref="GoblinOffer"/> / <see cref="GoblinVisits"/>; this is the presentation.
    /// </summary>
    public sealed class GoblinCardShop : MonoBehaviour
    {
        [Header("Sound")]
        public AudioSource audioSource;
        public AudioClip dealSound, flipSound, buySound, refuseSound, revealSound;
        [Tooltip("His voice: gruff merchant patter for lines, a smug laugh on a sale, a scoff when the buyer is short, " +
                 "a haggling «э-э?» on the reveal.")]
        public AudioClip[] voice;
        public AudioClip laughVoice, scoffVoice, haggleVoice;

        [Header("Authored overlay")]
        public CanvasGroup visibility;
        public OfferCardView[] cards = new OfferCardView[GoblinOffer.Size];
        public Button revealButton, leaveButton;
        [Tooltip("«20/24» over the corner of the row, as at the brazier: the purse itself stays on the shop HUD.")]
        public TMP_Text revealLabel, collection;
        [Tooltip("The goblin's speech bubble over the table.")]
        public DialogueBubbleLayout bubble;
        public CanvasGroup bubbleGroup;
        [Tooltip("«E — дальше» in the bottom-right corner while a line waits to be closed.")]
        public ContinueHint continueHint;

        [Header("Bought card")]
        [Tooltip("A two-sided world card (the expedition's Card debris); only its CardView is used. Empty: the bought " +
                 "card just drops off the table as before.")]
        public GameObject inspectCardPrefab;
        [Tooltip("What fades away while a bought card is held up to the eyes (the dim, the table, the buttons); the " +
                 "goblin's line and the collection count stay.")]
        public RectTransform[] stepAside = new RectTransform[0];
        [Tooltip("Sent into the bag: the whoosh as it spins up, the chime as it shoots off.")]
        public AudioClip stowSpinSound, stowSound;

        [Header("Fan")]
        [Tooltip("The cards overlap like a hand: centre-to-centre step, how far the arc drops at the ends of a full " +
                 "ten, the tilt of each next card. A bought card leaves and the rest close up.")]
        public float fanStep = 50;
        public float fanArc = 14, fanTiltPerCard = 2;
        [Tooltip("Where the collection count sits against the bottom-right corner of the row's last card (as at the " +
                 "brazier); it follows the row as it closes up.")]
        public Vector2 countOffset = new Vector2(20, 3);

        [Header("Special items")]
        [Tooltip("The special items' own places, left and right of the card row (board «Спецпредметы»): an open, unbought " +
                 "item lies there on every visit. He introduces each the first time it lies there.")]
        public SpecialItemOfferView leftItem;
        public SpecialItemOfferView rightItem;

        [Header("Lines")]
        [TextArea] public string[] introduction =
        {
            "О, партнёр! Чего уставился? В кустах я был без рабочей формы.",
            "Берёшь вслепую — полцены. Карту и цену до покупки не показываю. Сюрприз должен быть полным!",
            "Хочешь всё рассмотреть? Открою оставшиеся бесплатно. Но тогда каждая — за полную цену. Любопытство у нас дорогое.",
            "Можешь брать хоть несколько. Можешь ничего. Но первый вариант мне нравится больше."
        };
        [TextArea] public string[] boughtLines = { "Сюрприз!", "Хороший выбор. Наверное.", "Бери, пока тёплая.", "Ни капли не жалею." };
        [Tooltip("He mocks the buyer of a dummy, instead of the bought line: when it turns over at the eyes (bought blind) " +
                 "or at once (bought face up).")]
        [TextArea] public string[] dummyLines = { "Извини, партнёр.", "Ничего личного.", "Бизнес есть бизнес." };
        [TextArea] public string[] poorLines = { "Монет маловато, партнёр.", "В долг не торгую. Пока." };
        [TextArea] public string fullLine = "Коллекция трещит по швам. Сожги что-нибудь в жаровне.";
        [TextArea] public string revealLine = "Любуйся. Теперь каждая по полной.";
        [TextArea] public string emptyLine = "Всё разобрали. Приходи после работы.";

        public GoblinVisits Visits { get; } = new GoblinVisits();
        /// <summary>The scene's special items (the game hands them over, like the collection in <see cref="Bind"/>).</summary>
        public SpecialItemsHost SpecialItems { get; set; }
        /// <summary>A special item was bought.</summary>
        public event Action<SpecialItemDefinition> ItemBought;
        public bool IsOpen { get; private set; }
        public event Action Closed;
        public event Action<CardCopy> Bought;
        public event Action Refused;

        DeckPrototype workshop;
        PlayerControlGate control;
        Camera viewCamera;
        DeckWorkshop Model => workshop.Model;

        /// <summary>The scene's shared collection, the player's controls and view (the rig prefab has no scene references).</summary>
        public void Bind(DeckPrototype collection, PlayerControlGate controls, Camera view = null)
        {
            workshop = collection;
            control = controls;
            viewCamera = view;
        }

        /// <summary>A bought card is up at the eyes (or on its way there or away).</summary>
        public bool Inspecting => inspecting;
        CardInspect inspected;
        bool inspecting, pressed, turning;
        Vector2 pressAt;
        OfferCardView handingOver;
        CanvasGroup[] asideGroups;
        float asideAlpha = 1;
        /// <summary>Seconds the table takes to fade off the bought card that takes the table card's place.</summary>
        const float HandOver = .12f;
        /// <summary>Seconds the table takes to come back once the card is put away: as usual, and after a skip.</summary>
        const float ComeBack = .25f, QuickComeBack = .06f;
        float comeBack = ComeBack;
        /// <summary>Mouse travel, in pixels, after which a press turns the card instead of sending it away.</summary>
        const float DragSlop = 6;
        /// <summary>
        /// The skipping click's release must not buy the card under the pointer once the table is back (as the PC's
        /// merge show does); the frame of a skip guards the buttons against the same key or click.
        /// </summary>
        bool swallowRelease;
        int skippedFrame = -10;
        bool JustSkipped => swallowRelease || Time.frameCount <= skippedFrame + 1;
        bool CardIntroduction => Visits.IntroductionRead < introduction.Length;
        bool Talking => CardIntroduction || introducing || explaining;
        /// <summary>The special item he is introducing now (its lines wait for E), or null.</summary>
        SpecialItemDefinition introducing;
        int introducingLine;
        /// <summary>The special item just bought: its bought line, then how it works (each waits for E), or null.</summary>
        SpecialItemDefinition explaining;
        int explainingLine, explainStartedFrame = -1;
        SpecialItemOfferView itemShown;
        float lineUntil;
        int lineSeed, talkEndedFrame = -1;
        Vector2 leaveHome;

        void Awake()
        {
            for (int i = 0; i < cards.Length; i++)
            {
                cards[i].Slot = i;
                cards[i].Clicked += OnCardClicked;
                cards[i].Hovered += OnCardHovered;
            }
            leaveHome = ((RectTransform)leaveButton.transform).anchoredPosition;
            asideGroups = new CanvasGroup[stepAside.Length];
            for (int i = 0; i < stepAside.Length; i++)
            {
                if (!stepAside[i]) continue;
                var group = stepAside[i].GetComponent<CanvasGroup>();
                asideGroups[i] = group ? group : stepAside[i].gameObject.AddComponent<CanvasGroup>();
            }
            foreach (var slot in ItemSlots)
            {
                slot.Clicked += OnItemClicked;
                slot.Hovered += OnItemHovered;
            }
            revealButton.onClick.AddListener(() => { if (!JustSkipped) RevealAll(); });
            leaveButton.onClick.AddListener(() => { if (!JustSkipped) Close(); });
            SetVisible(false);
        }

        void OnDisable() => Close();

        /// <summary>The rope was pulled and he is on his way down: a fresh ten if he may come at all.</summary>
        public bool TryCall(int seed)
        {
            if (!workshop || !Visits.CanCall) return false;
            return Visits.Call(Model.Catalog, new System.Random(seed), workshop.config ? workshop.config.dummyChance : 0);
        }

        /// <summary>He is down: lay out the table (the current ten).</summary>
        public bool Open()
        {
            if (IsOpen || !isActiveAndEnabled || !workshop || Visits.Offer == null || Model.IsAway) return false;
            IsOpen = true;
            if (control) control.SetBlocked(this, true);
            SetVisible(true);
            var offer = Visits.Offer;
            for (int i = 0; i < cards.Length; i++)
            {
                if (offer.IsSold(i)) { cards[i].Hide(); continue; }
                cards[i].Deal(Art(i), offer.Revealed, i * .045f);
            }
            Fan(0);
            EndInspect();
            DealItems();
            Play(dealSound);
            if (CardIntroduction) Say(introduction[Visits.IntroductionRead], true);
            else if (StartItemIntroduction()) { }
            else if (!offer.AnyLeft && !AnyItemOnSale) Say(emptyLine);
            else HideBubble();
            Refresh();
            return true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            EndInspect();
            EndItemShow();
            introducing = null;
            explaining = null;
            SetVisible(false);
            if (control) control.SetBlocked(this, false);
            Closed?.Invoke();
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            // Swallowed until the skipping press is let go (its release frame included).
            if (swallowRelease && (mouse == null || !mouse.leftButton.isPressed && !mouse.leftButton.wasReleasedThisFrame)) swallowRelease = false;
            if (!IsOpen) return;
            if (Talking)
            {
                bool next = keyboard != null && (keyboard.eKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame
                    || keyboard.enterKey.wasPressedThisFrame) || mouse != null && mouse.leftButton.wasPressedThisFrame;
                // The click that bought the item does not also skip its first line.
                if (explaining && Time.frameCount <= explainStartedFrame) next = false;
                if (next && !(bubble && bubble.SkipTyping())) NextLine();
                return;
            }
            if (lineUntil > 0 && bubble && bubble.IsTyping) lineUntil = Time.unscaledTime + 2.6f;
            if (lineUntil > 0 && Time.unscaledTime > lineUntil) HideBubble();
            StepAside();
            if (itemShown)
            {
                bool skip = keyboard != null && (keyboard.eKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame
                    || keyboard.enterKey.wasPressedThisFrame) || mouse != null && mouse.leftButton.wasPressedThisFrame;
                if (skip) { swallowRelease |= mouse != null && mouse.leftButton.wasPressedThisFrame; skippedFrame = Time.frameCount; EndItemShow(); }
                return;
            }
            if (inspecting)
            {
                Inspect(keyboard, mouse);
                return;
            }
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame || mouse != null && mouse.rightButton.wasPressedThisFrame) Close();
        }

        void LateUpdate()
        {
            if (IsOpen) Fan(Time.unscaledDeltaTime);
        }

        /// <summary>Lays the unsold cards out as a fan around the table's centre; <paramref name="dt"/> 0 snaps.</summary>
        void Fan(float dt)
        {
            var offer = Visits.Offer;
            if (offer == null) return;
            int count = 0;
            for (int i = 0; i < cards.Length; i++) if (!offer.IsSold(i)) count++;
            float middle = (count - 1) / 2f, ends = (cards.Length - 1) / 2f;
            float follow = dt <= 0 ? 1 : 1 - Mathf.Exp(-14 * dt);
            int place = 0;
            Vector2? corner = null;
            for (int i = 0; i < cards.Length; i++)
            {
                if (offer.IsSold(i)) continue;
                float offset = place++ - middle;
                var rect = (RectTransform)cards[i].transform;
                var position = new Vector2(Mathf.Round(offset * fanStep), Mathf.Round(-offset * offset / (ends * ends) * fanArc));
                var tilt = Quaternion.Euler(0, 0, -offset * fanTiltPerCard);
                rect.anchoredPosition = Vector2.Lerp(rect.anchoredPosition, position, follow);
                rect.localRotation = Quaternion.Slerp(rect.localRotation, tilt, follow);
                // The last card's bottom-right corner, where it will rest, in the table's parent space.
                var half = rect.rect.size * .5f;
                corner = (Vector2)rect.parent.localPosition + position + (Vector2)(tilt * new Vector3(half.x, -half.y));
            }
            if (corner.HasValue && collection && collection.transform.parent == cards[0].transform.parent.parent)
            {
                var label = (RectTransform)collection.transform;
                var target = corner.Value + countOffset;
                label.anchoredPosition = dt <= 0 ? target : Vector2.Lerp(label.anchoredPosition, target, follow);
            }
        }

        void NextLine()
        {
            if (CardIntroduction)
            {
                Visits.ReadIntroductionLine(introduction.Length);
                if (CardIntroduction) { Say(introduction[Visits.IntroductionRead], true); Refresh(); return; }
                if (StartItemIntroduction()) { Refresh(); return; }
            }
            else if (explaining)
            {
                explainingLine++;
                if (explainingLine <= explaining.howToUse.Length) { Say(explaining.howToUse[explainingLine - 1], true); Refresh(); return; }
                explaining = null;
                HideBubble();
                // The held-up item now goes into the bag by itself: its flight carries on.
                talkEndedFrame = Time.frameCount;
                Refresh();
                return;
            }
            else if (introducing)
            {
                introducingLine++;
                if (introducingLine < introducing.introduction.Length) { Say(introducing.introduction[introducingLine], true); Refresh(); return; }
                SpecialItems.Inventory.MarkIntroduced(introducing.id);
                introducing = null;
                if (StartItemIntroduction()) { Refresh(); return; }
            }
            HideBubble();
            // The click that closed the last line must not also buy the card under it.
            talkEndedFrame = Time.frameCount;
            Refresh();
        }

        // ---------- special items ----------

        SpecialItemOfferView[] ItemSlots
        {
            get
            {
                if (leftItem && rightItem) return new[] { leftItem, rightItem };
                if (leftItem) return new[] { leftItem };
                return rightItem ? new[] { rightItem } : new SpecialItemOfferView[0];
            }
        }

        bool AnyItemOnSale
        {
            get
            {
                foreach (var slot in ItemSlots) if (slot.Item) return true;
                return false;
            }
        }

        /// <summary>Lays the open, unbought special items in their places (one per side, in catalogue order).</summary>
        void DealItems()
        {
            foreach (var slot in ItemSlots) slot.Hide();
            if (!SpecialItems || !SpecialItems.catalog) return;
            bool leftUsed = false, rightUsed = false;
            foreach (var item in SpecialItems.catalog.items)
            {
                if (!SpecialItems.OnSale(item)) continue;
                var slot = item.leftOfCards ? (leftUsed ? null : leftItem) : (rightUsed ? null : rightItem);
                if (!slot) continue;
                if (slot == leftItem) leftUsed = true; else rightUsed = true;
                slot.Deal(item, !SpecialItems.Inventory.WasSeen(item.id), .3f);
            }
        }

        /// <summary>Starts introducing the first item on the table he has not introduced yet. False when there is none.</summary>
        bool StartItemIntroduction()
        {
            if (!SpecialItems) return false;
            foreach (var slot in ItemSlots)
            {
                var item = slot.Item;
                if (!item || SpecialItems.Inventory.WasIntroduced(item.id)) continue;
                if (item.introduction == null || item.introduction.Length == 0) { SpecialItems.Inventory.MarkIntroduced(item.id); continue; }
                introducing = item;
                introducingLine = 0;
                Say(item.introduction[0], true);
                return true;
            }
            return false;
        }

        void OnItemHovered(SpecialItemOfferView view)
        {
            if (!view.Item || !SpecialItems) return;
            view.IsNew = false;
            SpecialItems.Inventory.MarkSeen(view.Item.id);
        }

        void OnItemClicked(SpecialItemOfferView view)
        {
            if (Time.frameCount > talkEndedFrame + 1 && !JustSkipped) BuyItem(view);
        }

        /// <summary>Buys the special item in this place for its price (once and for good). False when refused.</summary>
        public bool BuyItem(SpecialItemOfferView view)
        {
            var item = view ? view.Item : null;
            if (!IsOpen || Talking || inspecting || itemShown || !item || !SpecialItems || Model.IsAway) return false;
            if (!SpecialItems.OnSale(item)) return false;
            if (!Model.Wallet.TrySpend(item.price))
            {
                view.Refuse();
                Play(refuseSound);
                Play(scoffVoice);
                Say(Pick(poorLines));
                Refused?.Invoke();
                return false;
            }
            SpecialItems.Inventory.MarkSeen(item.id);
            SpecialItems.Grant(item);
            Play(buySound);
            Play(laughVoice);
            itemShown = view;
            bool explains = item.howToUse != null && item.howToUse.Length > 0;
            string bought = string.IsNullOrEmpty(item.boughtLine) ? Pick(boughtLines) : item.boughtLine;
            if (explains)
            {
                // He holds the bought item up in the middle and explains it, line by line (E).
                explaining = item;
                explainingLine = 0;
                explainStartedFrame = Time.frameCount;
                Say(bought, true);
            }
            else Say(bought);
            view.Lift(() => { if (itemShown == view) itemShown = null; if (IsOpen) Refresh(); }, () => Play(stowSound),
                () => explaining == item && IsOpen);
            Visits.Touch();
            ItemBought?.Invoke(item);
            Refresh();
            return true;
        }

        void EndItemShow()
        {
            var view = itemShown;
            itemShown = null;
            if (view) view.Finish(() => Play(stowSound));
        }

        // ---------- the table ----------

        void OnCardHovered(OfferCardView view)
        {
            var offer = Visits.Offer;
            // A face-down card hides its price, on hover too.
            view.SetPrice(offer != null && offer.Revealed ? offer.PriceAt(view.Slot, Model).ToString() : null);
        }

        void OnCardClicked(OfferCardView view)
        {
            if (Time.frameCount > talkEndedFrame + 1 && !JustSkipped) Choose(view.Slot);
        }

        /// <summary>Buys the card in this place at its current price. False when refused or not possible now.</summary>
        public bool Choose(int slot)
        {
            var offer = Visits.Offer;
            if (!IsOpen || Talking || inspecting || itemShown || offer == null || slot < 0 || slot >= cards.Length) return false;
            var view = cards[slot];
            bool blind = !offer.Revealed;
            string error = offer.Buy(slot, Model, out var copy);
            if (error != null)
            {
                view.Refuse();
                Play(refuseSound);
                Play(scoffVoice);
                Say(Model.CollectionFull ? fullLine : Pick(poorLines));
                Refused?.Invoke();
                Visits.Touch();
                return false;
            }
            Play(buySound);
            Play(laughVoice);
            bool dummy = copy.Spec.Dummy;
            bool lifted = LiftUp(view, copy, blind);
            if (!lifted)
            {
                if (blind)
                {
                    // Bought face down: it turns over for the player first, then goes.
                    view.Flip(Art(slot));
                    Play(flipSound);
                }
                // A blind buy holds face up for a moment: the surprise is the point.
                view.FlyAway(blind ? .7f : .15f);
            }
            // A dummy bought blind is mocked when it turns over at the eyes (LiftUp); any other dummy right now.
            if (dummy) { if (!(lifted && blind)) Mock(false); }
            else Say(offer.AnyLeft ? Pick(boughtLines) : emptyLine);
            Visits.Touch();
            Bought?.Invoke(copy);
            Refresh();
            return true;
        }

        public void RevealAll()
        {
            var offer = Visits.Offer;
            if (!IsOpen || Talking || inspecting || offer == null || !offer.RevealAll()) return;
            Play(revealSound);
            Play(haggleVoice);
            // A cascade left to right.
            int order = 0;
            for (int i = 0; i < cards.Length; i++)
                if (!offer.IsSold(i)) cards[i].Flip(Art(i), order++ * .07f);
            Say(revealLine);
            Visits.Touch();
            Refresh();
        }

        void Refresh()
        {
            var offer = Visits.Offer;
            // The same count as the brazier's, over the corner of the row.
            collection.text = $"{Model.Owned.Count}/{Model.CollectionLimit}";
            bool canReveal = offer != null && !offer.Revealed && offer.AnyLeft && !Talking;
            revealButton.gameObject.SetActive(offer != null && !offer.Revealed && offer.AnyLeft);
            revealButton.interactable = canReveal;
            // Alone, «Уйти» moves to the middle.
            ((RectTransform)leaveButton.transform).anchoredPosition =
                revealButton.gameObject.activeSelf ? leaveHome : new Vector2(0, leaveHome.y);
            revealLabel.text = "Раскрыть все";
            leaveButton.interactable = !Talking && !inspecting && !itemShown;
            foreach (var slot in ItemSlots) if (slot.group && slot.Item) slot.group.interactable = slot.group.blocksRaycasts = !Talking && !itemShown;
            foreach (var card in cards) if (card.group) card.group.interactable = !Talking;
        }

        // ---------- the bought card at the eyes ----------

        /// <summary>
        /// The bought card leaves the table where it lay and comes up to the eyes as a world card; the table fades off
        /// it. False when it cannot (no card prefab or camera): the old drop off the table is used then.
        /// </summary>
        bool LiftUp(OfferCardView view, CardCopy copy, bool blind)
        {
            var camera = viewCamera ? viewCamera : Camera.main;
            var art = workshop.config.Find(copy.Spec.Id)?.artwork;
            if (!inspectCardPrefab || !camera || !art) return false;
            // Where the card lies, as fractions of the screen, read in the table's own root canvas.
            var corners = new Vector3[4];
            view.body.GetWorldCorners(corners);
            var root = (RectTransform)view.GetComponentInParent<Canvas>().rootCanvas.transform;
            Vector2 Fraction(Vector3 world)
            {
                Vector2 local = root.InverseTransformPoint(world);
                return new Vector2((local.x - root.rect.xMin) / root.rect.width, (local.y - root.rect.yMin) / root.rect.height);
            }
            Vector2 bottomLeft = Fraction(corners[0]), bottomRight = Fraction(corners[3]);
            Vector2 screen = Fraction((corners[0] + corners[2]) * .5f);
            // Width and tilt from the bottom edge, in pixels so the screen's aspect does not skew the angle.
            var edge = Vector2.Scale(bottomRight - bottomLeft, new Vector2(Screen.width, Screen.height));
            float width = edge.magnitude / Mathf.Max(1, Screen.width);
            float roll = Mathf.Atan2(edge.y, edge.x) * Mathf.Rad2Deg;
            var back = view.back ? view.back.texture : null;
            // A dummy bought blind: he waits for it to show its face, then mocks the buyer.
            bool mock = blind && copy.Spec.Dummy;
            inspected = CardInspect.Launch(inspectCardPrefab, art.texture, back, copy.Level, camera, screen, width, roll, blind,
                HandOver, () => { Play(flipSound); if (mock) Mock(true); }, OnInspectGone);
            if (!inspected) return false;
            inspecting = true;
            pressed = turning = false;
            comeBack = ComeBack;
            // The table's card holds still and fades out with the table over the world card; then it is put away.
            view.group.blocksRaycasts = false;
            handingOver = view;
            Refresh();
            return true;
        }

        void Inspect(Keyboard keyboard, Mouse mouse)
        {
            if (!inspected) return;
            bool click = mouse != null && mouse.leftButton.wasPressedThisFrame;
            bool key = keyboard != null && (keyboard.eKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame
                || keyboard.enterKey.wasPressedThisFrame);
            // Skipped on its way up (E / Space / Enter or a click): only the flight is skipped, the card is at the eyes at
            // once, so the player still sees what he bought; the next press puts it away. Skipped at the eyes (a key) or on
            // its way out (a key or a click): put away at once. Only a press that started after the purchase counts, so
            // the buying click never skips.
            if ((key || click) && !inspected.Leaving && !inspected.Arrived)
            {
                SkipToEyes(click);
                return;
            }
            if (key || click && inspected.Leaving)
            {
                Skip(click);
                return;
            }
            if (inspected.Leaving) return;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame || mouse != null && mouse.rightButton.wasPressedThisFrame)
            {
                SendAway();
                return;
            }
            if (mouse == null) return;
            // Held: turns the card; a click without dragging (anywhere) puts it away at once.
            if (click)
            {
                pressed = true;
                turning = false;
                pressAt = mouse.position.ReadValue();
            }
            if (!pressed) return;
            if (mouse.leftButton.isPressed)
            {
                if (!turning && (mouse.position.ReadValue() - pressAt).magnitude > DragSlop * Hakaton.Feel.UiReference.ScreenScale) turning = true;
                if (turning && inspected.Arrived) inspected.Turn(mouse.delta.ReadValue());
                return;
            }
            pressed = false;
            if (turning) inspected.Release();
            else Skip(true);
            turning = false;
        }

        /// <summary>
        /// A skip press: the card at the eyes is put away at once (a blind card still turns, the stow chime plays), the
        /// goblin's line stops typing, the table snaps back after a very short fade. A click's release is swallowed so
        /// it cannot buy the card under the pointer.
        /// </summary>
        void Skip(bool byMouse)
        {
            swallowRelease |= byMouse || pressed;
            skippedFrame = Time.frameCount;
            if (bubble) bubble.SkipTyping();
            var card = inspected;
            if (!card) return;
            comeBack = QuickComeBack;
            // Calls OnInspectGone before it returns: the table is no longer stepping aside.
            card.Finish(() => Play(stowSound));
            // The table's card is put away at once (as EndInspect does), not left to fade back in with the table.
            if (handingOver) handingOver.Hide();
            handingOver = null;
        }

        /// <summary>
        /// A skip press while the card is still on its way up: it is at the eyes at once (a blind card turned), the table
        /// is gone from over it and the goblin's line is typed out. The click's release is swallowed so it does not also
        /// put the card away.
        /// </summary>
        void SkipToEyes(bool byMouse)
        {
            swallowRelease |= byMouse;
            skippedFrame = Time.frameCount;
            pressed = turning = false;
            if (bubble) bubble.SkipTyping();
            if (inspected) inspected.ArriveNow();
            asideAlpha = 0;
            ApplyAside();
            if (handingOver) handingOver.Hide();
            handingOver = null;
        }

        void SendAway()
        {
            if (!inspected || inspected.Leaving) return;
            inspected.Dismiss(() => Play(stowSound));
            Play(stowSpinSound ? stowSpinSound : dealSound);
        }

        /// <summary>The card is in the bag: the table comes back.</summary>
        void OnInspectGone()
        {
            inspected = null;
            inspecting = false;
            pressed = turning = false;
            if (IsOpen) Refresh();
        }

        /// <summary>Drops a card still at the eyes at once (the table closes or is laid again).</summary>
        void EndInspect()
        {
            if (inspected) inspected.Drop();
            inspected = null;
            inspecting = false;
            if (handingOver) handingOver.Hide();
            handingOver = null;
            asideAlpha = 1;
            ApplyAside();
        }

        /// <summary>While a card is at the eyes the table fades off it; it comes back once the card is put away.</summary>
        void StepAside()
        {
            float wanted = inspecting ? 0 : 1;
            asideAlpha = Mathf.MoveTowards(asideAlpha, wanted, Time.unscaledDeltaTime / (wanted > 0 ? comeBack : HandOver));
            // The table's card stays until the table has faded over the world card taking its place.
            if (handingOver && asideAlpha <= 0) { handingOver.Hide(); handingOver = null; }
            ApplyAside();
        }

        void ApplyAside()
        {
            if (asideGroups == null) return;
            foreach (var group in asideGroups)
            {
                if (!group) continue;
                group.alpha = asideAlpha;
                group.interactable = group.blocksRaycasts = asideAlpha > .99f;
            }
        }

        Sprite Art(int slot) => workshop.config.Find(Visits.Offer.CardAt(slot))?.artwork;

        // ---------- the goblin speaks ----------

        void Say(string line, bool waitForPlayer = false, bool voiced = true)
        {
            if (!bubble) return;
            bubble.SetText(line);
            bubbleGroup.alpha = 1;
            if (continueHint) continueHint.Show(waitForPlayer);
            lineUntil = waitForPlayer ? 0 : Time.unscaledTime + 2.6f;
            if (voiced && waitForPlayer && voice != null && voice.Length > 0) Play(voice[(lineSeed++ * 3 + Visits.IntroductionRead) % voice.Length]);
        }

        void HideBubble()
        {
            lineUntil = 0;
            if (bubbleGroup) bubbleGroup.alpha = 0;
            if (bubble) bubble.ForgetLine();
            if (continueHint) continueHint.Show(false);
        }

        string Pick(string[] lines) => lines == null || lines.Length == 0 ? "" : lines[(lineSeed++ * 7 + DateTime.Now.Millisecond) % lines.Length];

        /// <summary>A dummy shows its face: he mocks the buyer; with a scoff at the flip at the eyes (<paramref name="scoff"/>), the sale's laugh being over.</summary>
        void Mock(bool scoff)
        {
            Say(dummyLines != null && dummyLines.Length > 0 ? Pick(dummyLines) : Pick(boughtLines));
            if (scoff) Play(scoffVoice ? scoffVoice : laughVoice);
        }

        void Play(AudioClip clip)
        {
            if (clip && audioSource) audioSource.PlayOneShot(clip, .7f);
        }

        void SetVisible(bool visible)
        {
            if (!visibility) return;
            visibility.alpha = visible ? 1 : 0;
            visibility.interactable = visible;
            visibility.blocksRaycasts = visible;
            if (!visible) HideBubble();
        }
    }
}
