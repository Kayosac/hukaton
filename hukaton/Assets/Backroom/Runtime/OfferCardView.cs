using System;
using Hakaton.Feel;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hakaton.Backroom
{
    /// <summary>
    /// One card on the goblin's table, drawn over the game view. Deals in from below, lifts on hover, flips with a
    /// squash, shakes on a refusal and flies off when bought. Purely presentation: the shop decides what happens.
    /// </summary>
    public sealed class OfferCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public RectTransform body;
        public Image face;
        public RawImage back;
        [Tooltip("Price over the card: shown on hover, only for a face-up card.")]
        public TMP_Text price;
        [Tooltip("Optional line under the card: a short note under the card.")]
        public TMP_Text badge;
        public TMP_Text levelMark;
        public CanvasGroup group;
        [Tooltip("How far the card rises under the pointer.")]
        public float hoverLift = 26;
        [Tooltip("Overlapping cards (the goblin's fan): the one under the pointer comes to the front, like a card " +
                 "pulled from a hand, and goes back to its place in the row after.")]
        public bool raiseOnHover;
        [Tooltip("The brazier: under the pointer the card shivers, as if afraid of the fire.")]
        public bool trembleOnHover;

        public event Action<OfferCardView> Clicked, Hovered, Unhovered;
        public int Slot { get; set; }
        public bool Hovering { get; private set; }

        SpringFloat lift, scale, flip, shake;
        float fear, fearSeed;
        float dealDelay, flyTime = -1, flipTarget = 1;
        bool faceUp, showPrice, flying, toTarget;
        Vector2 home, target;
        Sprite pendingFace;
        System.Collections.Generic.Dictionary<Graphic, Color> shadeBase;
        bool dimmed;
        RectTransform guard;

        int restingIndex = -1;

        void Awake() => home = body.anchoredPosition;

        /// <summary>Puts the card back on the table: face down or up, with a staggered deal from below.</summary>
        public void Deal(Sprite art, bool up, float delay, int level = 1)
        {
            Lower();
            gameObject.SetActive(true);
            face.sprite = art;
            Hakaton.Decks.Unity.CardLevelPresentation.SetMark(levelMark, level);
            SetDimmed(false);
            SetGuarded(false);
            faceUp = up;
            flipTarget = 1;
            flip.Snap(1);
            lift.Snap(-420);
            scale.Snap(.6f);
            shake.Snap(0);
            dealDelay = delay;
            flyTime = -1;
            flying = false;
            toTarget = false;
            group.alpha = 1;
            group.blocksRaycasts = true;
            showPrice = false;
            Present();
        }

        public void Hide()
        {
            Lower();
            Hovering = false;
            gameObject.SetActive(false);
        }

        /// <summary>Shades the whole face, upgrade mark included (the brazier: a copy that is not in the deck, or one
        /// that cannot burn). The face's own colours are multiplied down, so nothing can sit on top and fail to draw.
        /// Every deal starts unshaded.</summary>
        public void SetDimmed(bool dimmed)
        {
            this.dimmed = dimmed;
            if (shadeBase == null)
            {
                if (!dimmed) return;
                shadeBase = new System.Collections.Generic.Dictionary<Graphic, Color>();
                foreach (var graphic in face.GetComponentsInChildren<Graphic>(true))
                    if (!guard || !graphic.transform.IsChildOf(guard)) shadeBase[graphic] = graphic.color;
            }
            foreach (var pair in shadeBase)
                if (pair.Key) pair.Key.color = dimmed ? Shaded(pair.Value) : pair.Value;
        }

        /// <summary>The multiplier <see cref="SetDimmed"/> puts on the face (white when not shaded).</summary>
        public Color ShadeTint => dimmed ? Shaded(Color.white) : Color.white;

        static Color Shaded(Color c) => new Color(c.r * .36f, c.g * .36f, c.b * .4f, c.a);

        /// <summary>
        /// A card that cannot be touched (the brazier's starting cards): a sign over the face with a line under it,
        /// and the card no longer answers the pointer at all. Every deal clears it.
        /// </summary>
        public void SetGuarded(bool guarded, Texture icon = null, string text = null, TMP_FontAsset font = null)
        {
            if (guarded && !guard)
            {
                guard = new GameObject("Guard", typeof(RectTransform)).GetComponent<RectTransform>();
                guard.SetParent(face.transform, false);
                guard.anchorMin = Vector2.zero;
                guard.anchorMax = Vector2.one;
                guard.offsetMin = guard.offsetMax = Vector2.zero;

                var band = new GameObject("Band", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
                band.SetParent(guard, false);
                band.anchorMin = new Vector2(0, .08f);
                band.anchorMax = new Vector2(1, .4f);
                band.offsetMin = band.offsetMax = Vector2.zero;
                var bandImage = band.GetComponent<Image>();
                bandImage.color = new Color(.05f, .06f, .1f, .78f);
                bandImage.raycastTarget = false;

                var sign = new GameObject("Sign", typeof(RectTransform), typeof(RawImage)).GetComponent<RectTransform>();
                sign.SetParent(guard, false);
                sign.anchorMin = sign.anchorMax = new Vector2(.5f, .66f);
                // Twice the icon's 32 px: whole pixels, no smearing.
                sign.sizeDelta = new Vector2(64, 64) * .75f;
                var signImage = sign.GetComponent<RawImage>();
                signImage.texture = icon;
                signImage.raycastTarget = false;

                var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
                label.rectTransform.SetParent(band, false);
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = new Vector2(-4, 1);
                label.rectTransform.offsetMax = new Vector2(4, -1);
                label.font = font ? font : price ? price.font : label.font;
                // The plain small pixel font at its own grid size.
                label.enableAutoSizing = false;
                label.fontSize = 9;
                label.lineSpacing = -10;
                label.textWrappingMode = TextWrappingModes.Normal;
                label.overflowMode = TextOverflowModes.Overflow;
                label.alignment = TextAlignmentOptions.Center;
                label.color = new Color(.9f, .95f, 1f);
                label.raycastTarget = false;
            }
            if (guard)
            {
                guard.gameObject.SetActive(guarded);
                guard.SetAsLastSibling();
                if (guarded) guard.GetComponentInChildren<TextMeshProUGUI>(true).text = text ?? "";
            }
            if (guarded)
            {
                Hovering = false;
                group.blocksRaycasts = false;
            }
        }

        /// <summary>
        /// Reads the pointer from the card's still slot instead of the moving card: a card that lifts, grows and
        /// shivers under the pointer would otherwise slide out from under it at the edge and flicker in and out.
        /// </summary>
        public void UseStillHitArea()
        {
            if (GetComponent<Graphic>()) return;
            foreach (var graphic in body.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            var hit = gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            hit.raycastTarget = true;
        }

        public void SetBadge(string text)
        {
            if (badge) badge.text = text ?? "";
        }

        public void SetPrice(string text)
        {
            price.text = text ?? "";
            showPrice = !string.IsNullOrEmpty(text);
        }

        /// <summary>Turns the card over; the art swaps at the edge-on moment.</summary>
        public void Flip(Sprite art, float delay = 0)
        {
            pendingFace = art;
            dealDelay = Mathf.Max(dealDelay, delay);
            flipTarget = -1;
        }

        public void Refuse() => shake.Kick(55);

        /// <summary>Bought: a pop, a moment to look at it, then the card drops towards the player; the place stays empty.</summary>
        public void FlyAway(float hold = 0)
        {
            flyTime = -Mathf.Max(0, hold) - .0001f;
            flying = true;
            scale.Kick(5);
            group.blocksRaycasts = false;
            Hovering = false;
        }

        /// <summary>Thrown at a point of the card's own parent space (the brazier's fire on screen): arcs in, spins, shrinks.</summary>
        public void FlyTo(Vector2 localTarget, float hold = 0)
        {
            FlyAway(hold);
            toTarget = true;
            target = localTarget;
        }

        public void OnPointerEnter(PointerEventData e)
        {
            Hovering = true;
            if (raiseOnHover && restingIndex < 0)
            {
                restingIndex = transform.GetSiblingIndex();
                transform.SetAsLastSibling();
            }
            scale.Kick(2.5f);
            Hovered?.Invoke(this);
        }

        public void OnPointerExit(PointerEventData e)
        {
            bool was = Hovering;
            Hovering = false;
            Lower();
            if (was) Unhovered?.Invoke(this);
        }

        void Lower()
        {
            if (restingIndex < 0) return;
            transform.SetSiblingIndex(restingIndex);
            restingIndex = -1;
        }
        public void OnPointerClick(PointerEventData e) { if (!flying) Clicked?.Invoke(this); }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dealDelay > 0) { dealDelay -= dt; Present(); return; }
            lift.Step(flying ? 34 : Hovering ? hoverLift : 0, 9, .55f, dt);
            scale.Step(Hovering ? 1.08f : 1, 11, .45f, dt);
            shake.Step(0, 18, .25f, dt);
            fear = Mathf.MoveTowards(fear, trembleOnHover && Hovering && !flying ? 1 : 0, dt * 6);
            flip.Step(flipTarget, 14, .7f, dt);
            if (flipTarget < 0 && flip.Value <= 0)
            {
                // Edge-on: show the other side and open back up with a little overshoot.
                faceUp = true;
                if (pendingFace) face.sprite = pendingFace;
                flipTarget = 1;
                flip.Snap(0);
                flip.Kick(9);
                scale.Kick(3);
            }
            if (flying)
            {
                flyTime += dt;
                if (flyTime < 0) { Present(); return; }
                float t = Mathf.Clamp01(flyTime / .55f);
                if (toTarget)
                {
                    // Into the fire: an arc over to the target, shrinking to a cinder.
                    var start = home + new Vector2(0, 34);
                    var position = Vector2.Lerp(start, target, Ease.InCubic(t)) + Vector2.up * (Mathf.Sin(t * Mathf.PI) * 70);
                    body.anchoredPosition = position;
                    body.localScale = Vector3.one * Mathf.Lerp(1.1f, .15f, Ease.InCubic(t));
                    body.localRotation = Quaternion.Euler(0, 0, t * 320);
                    group.alpha = t < .85f ? 1 : 1 - (t - .85f) / .15f;
                    if (t >= 1) gameObject.SetActive(false);
                    return;
                }
                lift.Snap(Mathf.Lerp(40, -700, Ease.InCubic(t)) + Mathf.Sin(t * Mathf.PI) * 90);
                group.alpha = 1 - Ease.InCubic(t);
                if (t >= 1) gameObject.SetActive(false);
            }
            Present();
        }

        void Present()
        {
            // Fear: a fast, uneven shiver (position and a little tilt), like chattering teeth.
            Vector2 shiver = Vector2.zero;
            float tilt = 0;
            if (fear > 0)
            {
                if (fearSeed == 0) fearSeed = UnityEngine.Random.Range(1f, 100f);
                float time = Time.unscaledTime * 34 + fearSeed;
                shiver = new Vector2(Mathf.PerlinNoise(time, 0) - .5f, Mathf.PerlinNoise(0, time) - .5f) * (9 * fear);
                tilt = (Mathf.PerlinNoise(time * .8f, time) - .5f) * 6 * fear;
            }
            body.anchoredPosition = home + new Vector2(shake.Value * .35f, lift.Value) + shiver;
            float sx = Mathf.Abs(flip.Value);
            body.localScale = new Vector3(Mathf.Max(.02f, sx) * scale.Value, scale.Value, 1);
            body.localRotation = Quaternion.Euler(0, 0, -shake.Value * .08f + tilt + (flying && flyTime > 0 ? flyTime * 40 : 0));
            face.enabled = faceUp;
            if (levelMark) levelMark.transform.parent.gameObject.SetActive(faceUp && levelMark.text.Length > 0);
            back.enabled = !faceUp;
            price.gameObject.SetActive(showPrice && faceUp && Hovering && !flying);
        }
    }
}
