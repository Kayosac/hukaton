using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hakaton.Computer
{
    /// <summary>One ad of «Закрой окно»: what it says and how it looks. Filled in the attack's inspector.</summary>
    [Serializable]
    public sealed class PcPopupAd
    {
        public string title = "Реклама";
        [TextArea] public string text = "";
        [Tooltip("The picture's frames (two blink in turn; one stays still; none hides the picture).")]
        public Sprite[] frames = Array.Empty<Sprite>();
        [Tooltip("The window's 9-slice frame; empty keeps the prefab's.")]
        public Sprite skin;
        [Tooltip("Window size in PC pixels; zero keeps the prefab's.")]
        public Vector2Int size;
    }

    /// <summary>
    /// A window of «Закрой окно» (an ad, or the small «Вы уверены?» with «Да» / «Нет»). It shows its content and
    /// reports clicks; <see cref="PcPopupAttack"/> decides what they do and moves the window (whole pixels).
    /// </summary>
    public sealed class PcPopup : MonoBehaviour, IPointerDownHandler
    {
        public Image frame;
        public TMP_Text title;
        public Image picture;
        public TMP_Text body;
        public Button closeButton;
        [Tooltip("The false cross beside the real one (off unless the window has the trick; the two swap places at random).")]
        public Button fakeCloseButton;
        [Tooltip("The confirmation's buttons (green «Да», red «Нет»); they swap places at random.")]
        public Button yesButton, noButton;
        [Tooltip("The title gives up this much width to the false cross when it is shown.")]
        [Min(0)] public float fakeCrossRoom = 23;
        [Min(.05f)] public float frameSeconds = .4f;
        [Min(.01f)] public float collapseSeconds = .18f;

        public event Action<PcPopup> CloseClicked, FakeClicked, YesClicked, NoClicked, Pressed;

        public RectTransform Rect => (RectTransform)transform;
        public PcPopupTrick Tricks { get; private set; }
        /// <summary>The ad this confirmation asks about (null for an ad).</summary>
        public PcPopup Owner { get; set; }
        /// <summary>The open confirmation of this ad.</summary>
        public PcPopup Confirmation { get; set; }
        public bool IsConfirmation => Owner;
        public int Dodges { get; set; }
        public bool Collapsing => collapseAt >= 0;
        /// <summary>The cross (the real one) — the runaway trick watches the pointer near it.</summary>
        public RectTransform CloseRect => closeButton ? (RectTransform)closeButton.transform : null;
        /// <summary>Exact position (the window is drawn at the rounded one).</summary>
        public Vector2 Position { get; private set; }
        public Vector2 Drift { get; set; }

        Sprite[] frames = Array.Empty<Sprite>();
        float clock, collapseAt = -1;
        Vector2 closeAt, fakeAt, yesAt, noAt;
        float titleWidth;
        bool placesRead;
        Action collapsed;

        void Awake()
        {
            ReadPlaces();
            if (closeButton) closeButton.onClick.AddListener(() => { if (!Collapsing) CloseClicked?.Invoke(this); });
            if (fakeCloseButton) fakeCloseButton.onClick.AddListener(() => { if (!Collapsing) FakeClicked?.Invoke(this); });
            if (yesButton) yesButton.onClick.AddListener(() => { if (!Collapsing) YesClicked?.Invoke(this); });
            if (noButton) noButton.onClick.AddListener(() => { if (!Collapsing) NoClicked?.Invoke(this); });
        }

        void ReadPlaces()
        {
            if (placesRead) return;
            placesRead = true;
            if (closeButton) closeAt = ((RectTransform)closeButton.transform).anchoredPosition;
            if (fakeCloseButton) fakeAt = ((RectTransform)fakeCloseButton.transform).anchoredPosition;
            if (yesButton) yesAt = ((RectTransform)yesButton.transform).anchoredPosition;
            if (noButton) noAt = ((RectTransform)noButton.transform).anchoredPosition;
            if (title) titleWidth = title.rectTransform.sizeDelta.x;
        }

        /// <summary>An ad with its tricks; <paramref name="swapCrosses"/> puts the false cross where the real one stands.</summary>
        public void ShowAd(PcPopupAd ad, PcPopupTrick tricks, bool swapCrosses)
        {
            ReadPlaces();
            Tricks = tricks;
            if (ad != null)
            {
                if (title) title.text = ad.title ?? "";
                // Ads with all their words baked into the picture have no text: the text block goes.
                if (body)
                {
                    body.text = ad.text ?? "";
                    body.gameObject.SetActive(!string.IsNullOrEmpty(ad.text));
                }
                if (frame && ad.skin) frame.sprite = ad.skin;
                if (ad.size.x > 0 && ad.size.y > 0) Rect.sizeDelta = ad.size;
                frames = ad.frames ?? Array.Empty<Sprite>();
            }
            if (picture)
            {
                picture.enabled = frames.Length > 0 && frames[0];
                if (picture.enabled) picture.sprite = frames[0];
            }
            bool fake = (tricks & PcPopupTrick.FakeClose) != 0 && fakeCloseButton;
            if (fakeCloseButton) fakeCloseButton.gameObject.SetActive(fake);
            if (title) title.rectTransform.sizeDelta = new Vector2(titleWidth - (fake ? fakeCrossRoom : 0), title.rectTransform.sizeDelta.y);
            Place(closeButton, fake && swapCrosses ? fakeAt : closeAt);
            Place(fakeCloseButton, fake && swapCrosses ? closeAt : fakeAt);
        }

        /// <summary>The small confirmation; <paramref name="swapAnswers"/> puts «Да» where «Нет» stands.</summary>
        public void ShowConfirmation(string titleText, string question, bool swapAnswers)
        {
            ReadPlaces();
            Tricks = PcPopupTrick.None;
            if (title && titleText != null) title.text = titleText;
            if (body && question != null) body.text = question;
            Place(yesButton, swapAnswers ? noAt : yesAt);
            Place(noButton, swapAnswers ? yesAt : noAt);
        }

        static void Place(Button button, Vector2 at)
        {
            if (button) ((RectTransform)button.transform).anchoredPosition = at;
        }

        /// <summary>Moves the window, drawn at whole pixels.</summary>
        public void MoveTo(Vector2 position)
        {
            Position = position;
            Rect.anchoredPosition = new Vector2(Mathf.Round(position.x), Mathf.Round(position.y));
        }

        /// <summary>Shrinks away, then destroys itself (after calling <paramref name="done"/>).</summary>
        public void Collapse(Action done = null)
        {
            if (Collapsing) return;
            collapseAt = 0;
            collapsed = done;
            foreach (var graphic in GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (Collapsing)
            {
                collapseAt += dt;
                float t = Mathf.Clamp01(collapseAt / collapseSeconds);
                float s = 1 - t * t;
                transform.localScale = new Vector3(s, Mathf.Max(0, 1 - t), 1);
                if (t >= 1)
                {
                    collapsed?.Invoke();
                    collapsed = null;
                    Destroy(gameObject);
                }
                return;
            }
            if (!picture || frames.Length < 2) return;
            clock += dt;
            int index = (int)(clock / frameSeconds) % frames.Length;
            if (frames[index] && picture.sprite != frames[index]) picture.sprite = frames[index];
        }

        public void OnPointerDown(PointerEventData eventData) => Pressed?.Invoke(this);
    }
}
