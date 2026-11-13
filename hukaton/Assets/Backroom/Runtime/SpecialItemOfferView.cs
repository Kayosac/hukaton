using System;
using System.Collections;
using Hakaton.Feel;
using Hakaton.SpecialItems;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hakaton.Backroom
{
    /// <summary>
    /// A special item on the goblin dealer's table, in its own place left or right of the card row (board
    /// «Спецпредметы»): always there once it is open and not bought, as a black silhouette. Deals in with the cards, lifts and shows its price
    /// under the pointer, blinks while it is new (until the pointer first finds it), shakes on a refusal, and when
    /// bought shows its colours as it comes up to the middle of the screen, then leaves into the bag.
    /// </summary>
    public sealed class SpecialItemOfferView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public RectTransform body;
        public Image icon;
        [Tooltip("The price over the item, shown under the pointer.")]
        public TMP_Text price;
        public CanvasGroup group;
        public float hoverLift = 18;
        [Min(1)] public float hoverScale = 1.08f;
        [Tooltip("Until it is bought the item on the table is only a black silhouette; bought, it shows its colours.")]
        public Color silhouette = Color.black;
        [Tooltip("A new item blinks (its own picture) until the pointer first finds it.")]
        [Range(0, 1)] public float blinkMinimumAlpha = .35f;
        [Min(.1f)] public float blinkFrequency = 2.2f;
        [Tooltip("The panel under the item: when it is bought it slides off to its own side.")]
        public RectTransform panel;
        public float panelExit = 220;
        [Header("Bought")]
        [Tooltip("Where it is held up when bought, in the table's space, and how much bigger it gets there.")]
        public Vector2 inspectAt = new Vector2(0, 40);
        [Min(1)] public float inspectScale = 2.2f;
        [Min(.05f)] public float riseSeconds = .35f;
        [Min(0)] public float holdSeconds = .8f;
        [Min(.05f)] public float leaveSeconds = .35f;

        public event Action<SpecialItemOfferView> Clicked, Hovered;
        public SpecialItemDefinition Item { get; private set; }
        public bool IsNew { get; set; }
        public bool Busy => flight != null;

        SpringFloat lift, scale, shake;
        Vector2 home;
        bool hovering;
        float dealDelay;
        Coroutine flight;
        Vector2 iconHome, panelHome;
        Image panelImage;
        int homeSibling = -1;

        void Awake()
        {
            home = body.anchoredPosition;
            if (icon) iconHome = icon.rectTransform.anchoredPosition;
            if (panel) { panelHome = panel.anchoredPosition; panelImage = panel.GetComponent<Image>(); }
            homeSibling = transform.GetSiblingIndex();
            Hide();
        }

        public void Deal(SpecialItemDefinition item, bool isNew, float delay)
        {
            Item = item;
            IsNew = isNew;
            gameObject.SetActive(true);
            if (flight != null) { StopCoroutine(flight); flight = null; }
            if (icon) { icon.sprite = item.tableSprite ? item.tableSprite : item.menuIcon; icon.color = silhouette; }
            if (price) price.text = "";
            if (group) { group.alpha = 1; group.interactable = group.blocksRaycasts = true; }
            hovering = false;
            lift.Snap(-420);
            scale.Snap(.6f);
            shake.Snap(0);
            dealDelay = delay;
            Apply();
        }

        public void Hide()
        {
            if (flight != null) { StopCoroutine(flight); flight = null; }
            ResetPose();
            Item = null;
            hovering = false;
            gameObject.SetActive(false);
        }

        public void Refuse() => shake.Kick(80);

        /// <summary>Bought: the item (not its panel) up to the middle over the cards, held a moment, then off into the bag. <paramref name="done"/> when gone.</summary>
        public void Lift(Action done, Action stowed, Func<bool> keepShowing = null)
        {
            if (flight != null) StopCoroutine(flight);
            flight = StartCoroutine(Fly(done, stowed, keepShowing));
        }

        /// <summary>The bought item's show is skipped: gone at once.</summary>
        public void Finish(Action stowed)
        {
            if (flight == null) return;
            StopCoroutine(flight);
            flight = null;
            stowed?.Invoke();
            Hide();
        }

        IEnumerator Fly(Action done, Action stowed, Func<bool> keepShowing)
        {
            if (price) price.text = "";
            if (group) group.blocksRaycasts = false;
            if (icon) icon.color = Color.white;
            var iconRect = icon.rectTransform;
            float side = Mathf.Sign(((RectTransform)transform).anchoredPosition.x == 0 ? -1 : ((RectTransform)transform).anchoredPosition.x);
            // Only the item flies to the middle, over the cards; its panel slides off to its own side.
            transform.SetAsLastSibling();
            Vector3 from = iconRect.position;
            Vector3 to = transform.parent.TransformPoint(inspectAt);
            for (float t = 0; t < riseSeconds; t += Time.unscaledDeltaTime)
            {
                float k = 1 - Mathf.Pow(1 - t / riseSeconds, 3);
                iconRect.position = Vector3.LerpUnclamped(from, to, k);
                iconRect.localScale = Vector3.one * Mathf.Lerp(1, inspectScale, k);
                Panel(side, k);
                yield return null;
            }
            iconRect.position = to;
            iconRect.localScale = Vector3.one * inspectScale;
            Panel(side, 1);
            // Held up while the dealer still talks about it (keepShowing), at least holdSeconds.
            for (float t = 0; t < holdSeconds || keepShowing != null && keepShowing(); t += Time.unscaledDeltaTime)
            {
                iconRect.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 9) * 3 * Mathf.Lerp(1, .4f, Mathf.Clamp01(t - holdSeconds)));
                yield return null;
            }
            iconRect.localRotation = Quaternion.identity;
            stowed?.Invoke();
            Vector3 away = to + transform.parent.TransformVector(new Vector3(-40, -520, 0));
            for (float t = 0; t < leaveSeconds; t += Time.unscaledDeltaTime)
            {
                float k = t / leaveSeconds;
                k *= k;
                iconRect.position = Vector3.LerpUnclamped(to, away, k);
                iconRect.localScale = Vector3.one * Mathf.Lerp(inspectScale, .4f, k);
                iconRect.localRotation = Quaternion.Euler(0, 0, k * 220);
                yield return null;
            }
            flight = null;
            Hide();
            done?.Invoke();
        }

        /// <summary>The panel slides off towards its own side of the screen and fades (<paramref name="k"/> 0 … 1).</summary>
        void Panel(float side, float k)
        {
            if (!panel) return;
            panel.anchoredPosition = panelHome + new Vector2(side * panelExit * k, 0);
            if (panelImage) panelImage.color = new Color(1, 1, 1, 1 - k);
        }

        /// <summary>Back to rest after a purchase: the icon in its place, the panel under it.</summary>
        void ResetPose()
        {
            if (icon)
            {
                var iconRect = icon.rectTransform;
                iconRect.anchoredPosition = iconHome;
                iconRect.localScale = Vector3.one;
                iconRect.localRotation = Quaternion.identity;
            }
            if (panel) panel.anchoredPosition = panelHome;
            if (panelImage) panelImage.color = Color.white;
            if (homeSibling >= 0 && transform.parent && homeSibling < transform.parent.childCount) transform.SetSiblingIndex(homeSibling);
        }

        void Update()
        {
            if (flight != null) return;
            float dt = Time.unscaledDeltaTime;
            if (dealDelay > 0) { dealDelay -= dt; return; }
            lift.Step(hovering ? hoverLift : 0, 9, .55f, dt);
            scale.Step(hovering ? hoverScale : 1, 9, .5f, dt);
            shake.Step(0, 18, .2f, dt);
            if (icon && IsNew)
            {
                float wave = .5f + .5f * Mathf.Cos(Time.unscaledTime * blinkFrequency * Mathf.PI * 2);
                icon.color = new Color(silhouette.r, silhouette.g, silhouette.b, silhouette.a * Mathf.Lerp(blinkMinimumAlpha, 1, wave));
            }
            else if (icon) icon.color = silhouette;
            Apply();
        }

        void Apply()
        {
            body.anchoredPosition = new Vector2(Mathf.Round(shake.Value * .1f), Mathf.Round(lift.Value));
            body.localScale = Vector3.one * scale.Value;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (flight != null || !Item) return;
            hovering = true;
            scale.Kick(1.5f);
            if (price) price.text = Item.price.ToString();
            Hovered?.Invoke(this);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovering = false;
            if (price) price.text = "";
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (flight != null || !Item || eventData.button != PointerEventData.InputButton.Left) return;
            Clicked?.Invoke(this);
        }
    }
}
