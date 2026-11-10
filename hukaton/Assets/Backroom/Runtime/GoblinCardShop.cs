using System;
using System.Linq;
using Hakaton.Character;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.DialogueUI;
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
    /// turns the card, a click anywhere sends it off into the bag and the table comes back.
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

        [Header("Lines")]
        [TextArea] public string[] introduction =
        {
            "О, партнёр! Чего уставился? В кустах я был без рабочей формы.",
            "Берёшь вслепую — полцены. Карту и цену до покупки не показываю. Сюрприз должен быть полным!",
            "Хочешь всё рассмотреть? Открою оставшиеся бесплатно. Но тогда каждая — за полную цену. Любопытство у нас дорогое.",
            "Можешь брать хоть несколько. Можешь ничего. Но первый вариант мне нравится больше."
        };
        [TextArea] public string[] boughtLines = { "Сюрприз!", "Хороший выбор. Наверное.", "Бери, пока тёплая.", "Ни капли не жалею." };
        [TextArea] public string[] poorLines = { "Монет маловато, партнёр.", "В долг не торгую. Пока." };
        [TextArea] public string fullLine = "Коллекция трещит по швам. Сожги что-нибудь в жаровне.";
        [TextArea] public string revealLine = "Любуйся. Теперь каждая по полной.";
        [TextArea] public string emptyLine = "Всё разобрали. Приходи после работы.";

        public GoblinVisits Visits { get; } = new GoblinVisits();
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
        /// <summary>Mouse travel, in pixels, after which a press turns the card instead of sending it away.</summary>
        const float DragSlop = 6;
        bool Talking => Visits.IntroductionRead < introduction.Length;
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
            revealButton.onClick.AddListener(RevealAll);
            leaveButton.onClick.AddListener(Close);
            SetVisible(false);
        }

        void OnDisable() => Close();

        /// <summary>The rope was pulled and he is on his way down: a fresh ten if he may come at all.</summary>
        public bool TryCall(int seed)
        {
            if (!workshop || !Visits.CanCall) return false;
            return Visits.Call(Model.Catalog, new System.Random(seed));
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
            Play(dealSound);
            if (Talking) Say(introduction[Visits.IntroductionRead], true);
            else if (!offer.AnyLeft) Say(emptyLine);
            else HideBubble();
            Refresh();
            return true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            EndInspect();
            SetVisible(false);
            if (control) control.SetBlocked(this, false);
            Closed?.Invoke();
        }

        void Update()
        {
            if (!IsOpen) return;
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (Talking)
            {
                bool next = keyboard != null && (keyboard.eKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame
                    || keyboard.enterKey.wasPressedThisFrame) || mouse != null && mouse.leftButton.wasPressedThisFrame;
                if (next) NextLine();
                return;
            }
            if (lineUntil > 0 && Time.unscaledTime > lineUntil) HideBubble();
            StepAside();
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
            Visits.ReadIntroductionLine(introduction.Length);
            if (Talking) Say(introduction[Visits.IntroductionRead], true);
            else
            {
                HideBubble();
                // The click that closed the last line must not also buy the card under it.
                talkEndedFrame = Time.frameCount;
            }
            Refresh();
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
            if (Time.frameCount > talkEndedFrame + 1) Choose(view.Slot);
        }

        /// <summary>Buys the card in this place at its current price. False when refused or not possible now.</summary>
        public bool Choose(int slot)
        {
            var offer = Visits.Offer;
            if (!IsOpen || Talking || inspecting || offer == null || slot < 0 || slot >= cards.Length) return false;
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
            if (!LiftUp(view, copy, blind))
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
            Say(offer.AnyLeft ? Pick(boughtLines) : emptyLine);
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
            leaveButton.interactable = !Talking && !inspecting;
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
            inspected = CardInspect.Launch(inspectCardPrefab, art.texture, back, copy.Level, camera, screen, width, roll, blind,
                HandOver, () => Play(flipSound), OnInspectGone);
            if (!inspected) return false;
            inspecting = true;
            pressed = turning = false;
            // The table's card holds still and fades out with the table over the world card; then it is put away.
            view.group.blocksRaycasts = false;
            handingOver = view;
            Refresh();
            return true;
        }

        void Inspect(Keyboard keyboard, Mouse mouse)
        {
            if (!inspected || inspected.Leaving) return;
            if (keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.eKey.wasPressedThisFrame
                || keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame)
                || mouse != null && mouse.rightButton.wasPressedThisFrame)
            {
                SendAway();
                return;
            }
            if (mouse == null) return;
            // Held: turns the card; a click without dragging (anywhere) sends it into the bag. Only a press that
            // started after the purchase counts, so the buying click never sends the card straight off.
            if (mouse.leftButton.wasPressedThisFrame)
            {
                pressed = true;
                turning = false;
                pressAt = mouse.position.ReadValue();
            }
            if (!pressed) return;
            if (mouse.leftButton.isPressed)
            {
                if (!turning && (mouse.position.ReadValue() - pressAt).sqrMagnitude > DragSlop * DragSlop) turning = true;
                if (turning && inspected.Arrived) inspected.Turn(mouse.delta.ReadValue());
                return;
            }
            pressed = false;
            if (turning) inspected.Release();
            else SendAway();
            turning = false;
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
            asideAlpha = Mathf.MoveTowards(asideAlpha, wanted, Time.unscaledDeltaTime * (wanted > 0 ? 4 : 1 / HandOver));
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
            if (continueHint) continueHint.Show(false);
        }

        string Pick(string[] lines) => lines.Length == 0 ? "" : lines[(lineSeed++ * 7 + DateTime.Now.Millisecond) % lines.Length];

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
