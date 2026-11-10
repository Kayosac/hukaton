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
        [Tooltip("Optional line under the card: level, «в колоде» (the brazier's list).")]
        public TMP_Text badge;
        public CanvasGroup group;

        public event Action<OfferCardView> Clicked, Hovered;
        public int Slot { get; set; }
        public bool Hovering { get; private set; }

        SpringFloat lift, scale, flip, shake;
        float dealDelay, flyTime = -1, flipTarget = 1;
        bool faceUp, showPrice, flying, toTarget;
        Vector2 home, target;
        Sprite pendingFace;

        void Awake() => home = body.anchoredPosition;

        /// <summary>Puts the card back on the table: face down or up, with a staggered deal from below.</summary>
        public void Deal(Sprite art, bool up, float delay)
        {
            gameObject.SetActive(true);
            face.sprite = art;
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
            Hovering = false;
            gameObject.SetActive(false);
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
            scale.Kick(2.5f);
            Hovered?.Invoke(this);
        }

        public void OnPointerExit(PointerEventData e) => Hovering = false;
        public void OnPointerClick(PointerEventData e) { if (!flying) Clicked?.Invoke(this); }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dealDelay > 0) { dealDelay -= dt; Present(); return; }
            lift.Step(flying ? 34 : Hovering ? 26 : 0, 9, .55f, dt);
            scale.Step(Hovering ? 1.08f : 1, 11, .45f, dt);
            shake.Step(0, 18, .25f, dt);
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
            body.anchoredPosition = home + new Vector2(shake.Value * .35f, lift.Value);
            float sx = Mathf.Abs(flip.Value);
            body.localScale = new Vector3(Mathf.Max(.02f, sx) * scale.Value, scale.Value, 1);
            body.localRotation = Quaternion.Euler(0, 0, -shake.Value * .08f + (flying && flyTime > 0 ? flyTime * 40 : 0));
            face.enabled = faceUp;
            back.enabled = !faceUp;
            price.gameObject.SetActive(showPrice && faceUp && Hovering && !flying);
        }
    }
}
