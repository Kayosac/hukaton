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
        [Tooltip("«Коллекция 20 / 24»: the purse itself stays on the shop HUD.")]
        public TMP_Text revealLabel, collection;
        [Tooltip("The goblin's speech bubble over the table.")]
        public DialogueBubbleLayout bubble;
        public CanvasGroup bubbleGroup;
        [Tooltip("Shown under the bubble while a line waits to be closed.")]
        public TMP_Text continueHint;

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
        DeckWorkshop Model => workshop.Model;

        /// <summary>The scene's shared collection and the player's controls (the rig prefab has no scene references).</summary>
        public void Bind(DeckPrototype collection, PlayerControlGate controls)
        {
            workshop = collection;
            control = controls;
        }
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
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame || mouse != null && mouse.rightButton.wasPressedThisFrame) Close();
            if (lineUntil > 0 && Time.unscaledTime > lineUntil) HideBubble();
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
            if (!IsOpen || Talking || offer == null || slot < 0 || slot >= cards.Length) return false;
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
            if (blind)
            {
                // Bought face down: it turns over for the player first, then goes.
                view.Flip(Art(slot));
                Play(flipSound);
            }
            // A blind buy holds face up for a moment: the surprise is the point.
            view.FlyAway(blind ? .7f : .15f);
            Say(offer.AnyLeft ? Pick(boughtLines) : emptyLine);
            Visits.Touch();
            Bought?.Invoke(copy);
            Refresh();
            return true;
        }

        public void RevealAll()
        {
            var offer = Visits.Offer;
            if (!IsOpen || Talking || offer == null || !offer.RevealAll()) return;
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
            collection.text = $"Коллекция  {Model.Owned.Count} / {Model.CollectionLimit}";
            bool canReveal = offer != null && !offer.Revealed && offer.AnyLeft && !Talking;
            revealButton.gameObject.SetActive(offer != null && !offer.Revealed && offer.AnyLeft);
            revealButton.interactable = canReveal;
            // Alone, «Уйти» moves to the middle.
            ((RectTransform)leaveButton.transform).anchoredPosition =
                revealButton.gameObject.activeSelf ? leaveHome : new Vector2(0, leaveHome.y);
            revealLabel.text = "Раскрыть все";
            leaveButton.interactable = !Talking;
            foreach (var card in cards) if (card.group) card.group.interactable = !Talking;
        }

        Sprite Art(int slot) => workshop.config.Find(Visits.Offer.CardAt(slot))?.artwork;

        // ---------- the goblin speaks ----------

        void Say(string line, bool waitForPlayer = false, bool voiced = true)
        {
            if (!bubble) return;
            bubble.SetText(line);
            bubbleGroup.alpha = 1;
            if (continueHint) continueHint.gameObject.SetActive(waitForPlayer);
            lineUntil = waitForPlayer ? 0 : Time.unscaledTime + 2.6f;
            if (voiced && waitForPlayer && voice != null && voice.Length > 0) Play(voice[(lineSeed++ * 3 + Visits.IntroductionRead) % voice.Length]);
        }

        void HideBubble()
        {
            lineUntil = 0;
            if (bubbleGroup) bubbleGroup.alpha = 0;
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
