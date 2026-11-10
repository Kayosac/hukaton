using System;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hakaton.Computer
{
    /// <summary>
    /// One owned copy in the PC's card window. Click toggles it in the deck; dragging it onto an equal copy of the same
    /// level merges them. Purely presentation: <see cref="PcCardsWindow"/> decides what happens.
    /// </summary>
    public sealed class PcCardTile : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public enum Role { None, Source, Partner, Other }

        [Tooltip("The moving part: lifts on hover, follows the pointer while dragged.")]
        public RectTransform body;
        public CanvasGroup bodyGroup;
        public Image face;
        [Tooltip("The shared upgrade mark (CardLevelMark prefab): hidden for a base card.")]
        public TMP_Text levelMark;
        [Tooltip("A frame around the card: lights up for merge partners and the drop target.")]
        public Image glow;
        [Tooltip("A white layer over the face for flashes.")]
        public Image flash;
        public Color partnerGlow = new Color(.35f, .85f, .3f, 1), targetGlow = new Color(1, .78f, .2f, 1);
        [Range(0, 1)] public float outOfDeckAlpha = .45f;
        [Min(0)] public float hoverLift = 5;

        public int CopyId { get; private set; }
        public bool InDeck { get; private set; }
        public bool Hovering { get; private set; }
        public Role DragRole { get; private set; }
        public bool Targeted { get; private set; }
        public bool Dragged { get; private set; }

        public event Action<PcCardTile> Clicked, HoverStarted, HoverEnded, DragStarted;
        public event Action<PcCardTile, PointerEventData> Dragging, Dropped;

        SpringFloat lift, scale, shake, alpha, glowAmount;
        SpringVector3 offset;
        float flashAmount, shakeTime = -1;
        Vector2 grab;
        RectTransform dragLayer;

        void Awake()
        {
            scale.Snap(1);
            alpha.Snap(1);
        }

        public void Bind(int copyId, Sprite art, int level, bool inDeck)
        {
            if (CopyId != copyId) { lift.Snap(0); offset.Snap(Vector3.zero); }
            CopyId = copyId;
            InDeck = inDeck;
            face.sprite = art;
            CardLevelPresentation.SetMark(levelMark, level);
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            EndDragNow();
            Hovering = false;
            gameObject.SetActive(false);
        }

        public void SetRole(Role role)
        {
            DragRole = role;
            if (role != Role.Partner) Targeted = false;
        }

        public void SetTargeted(bool targeted) => Targeted = targeted && DragRole == Role.Partner;

        /// <summary>A small hop: put into or taken out of the deck.</summary>
        public void Pop()
        {
            scale.Kick(2.2f);
            flashAmount = Mathf.Max(flashAmount, .35f);
        }

        public void Refuse() => shakeTime = 0;

        /// <summary>The merged card flares up.</summary>
        public void Celebrate()
        {
            scale.Snap(.85f);
            scale.Kick(6);
            flashAmount = 1;
        }

        // ---------- pointer ----------

        public void OnPointerEnter(PointerEventData e)
        {
            Hovering = true;
            HoverStarted?.Invoke(this);
        }

        public void OnPointerExit(PointerEventData e)
        {
            Hovering = false;
            HoverEnded?.Invoke(this);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left && !Dragged) Clicked?.Invoke(this);
        }

        public void BeginDragOn(RectTransform layer) => dragLayer = layer;

        public void OnBeginDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || !dragLayer || !CrtRaycaster.Current) return;
            Dragged = true;
            // Lift the body out of the scroll view, keeping where it is and its size.
            Vector2 size = body.rect.size;
            Vector3 world = body.position;
            body.SetParent(dragLayer, false);
            body.anchorMin = body.anchorMax = new Vector2(.5f, .5f);
            body.sizeDelta = size;
            body.position = world;
            body.SetAsLastSibling();
            if (bodyGroup) bodyGroup.blocksRaycasts = false;
            CrtRaycaster.Current.ToLocal(dragLayer, e.position, out var pointer);
            grab = body.anchoredPosition - pointer;
            DragStarted?.Invoke(this);
        }

        public void OnDrag(PointerEventData e)
        {
            if (!Dragged || !CrtRaycaster.Current) return;
            CrtRaycaster.Current.ToLocal(dragLayer, e.position, out var pointer);
            body.anchoredPosition = pointer + grab;
            Dragging?.Invoke(this, e);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!Dragged) return;
            Dropped?.Invoke(this, e);
            EndDragNow();
        }

        /// <summary>Back into the grid; the body glides home from where it was dropped.</summary>
        public void EndDragNow()
        {
            if (!Dragged) return;
            Dragged = false;
            Vector3 world = body.position;
            body.SetParent(transform, false);
            body.anchorMin = Vector2.zero;
            body.anchorMax = Vector2.one;
            body.sizeDelta = Vector2.zero;
            body.SetAsFirstSibling();
            body.position = world;
            Vector2 from = body.anchoredPosition;
            body.anchoredPosition = Vector2.zero;
            offset.Snap(from);
            if (bodyGroup) bodyGroup.blocksRaycasts = true;
        }

        /// <summary>Merged away: no flight home.</summary>
        public void Vanish()
        {
            EndDragNow();
            offset.Snap(Vector3.zero);
            alpha.Snap(0);
        }

        void OnDisable()
        {
            if (Dragged) EndDragNow();
            Hovering = false;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1 / 20f);
            bool lifted = (Hovering || Targeted) && !Dragged;
            lift.Step(lifted ? hoverLift : 0, 9, .55f, dt);
            scale.Step(Dragged ? 1.08f : Targeted ? 1.06f : lifted ? 1.03f : 1, 7, .45f, dt);
            if (!Dragged) offset.Step(Vector3.zero, 6, .8f, dt);
            float wantAlpha = DragRole == Role.Other ? .22f : DragRole == Role.Partner || DragRole == Role.Source ? 1 : InDeck ? 1 : outOfDeckAlpha;
            alpha.Step(wantAlpha, 8, 1, dt);
            glowAmount.Step(DragRole == Role.Partner ? 1 : 0, 8, 1, dt);

            float shakeX = 0;
            if (shakeTime >= 0)
            {
                shakeTime += dt;
                shakeX = Mathf.Sin(shakeTime * 70) * 5 * Mathf.Max(0, 1 - shakeTime / .35f);
                if (shakeTime > .35f) shakeTime = -1;
            }
            flashAmount = Mathf.MoveTowards(flashAmount, 0, dt * 2.5f);

            if (!Dragged)
            {
                var o = offset.Value;
                body.anchoredPosition = new Vector2(Mathf.Round(o.x + shakeX), Mathf.Round(o.y + lift.Value));
            }
            body.localScale = Vector3.one * scale.Value;
            if (bodyGroup) bodyGroup.alpha = Mathf.Clamp01(alpha.Value);
            if (glow)
            {
                var c = Targeted ? targetGlow : partnerGlow;
                c.a = Mathf.Clamp01(glowAmount.Value) * (Targeted ? 1 : .55f + .25f * Mathf.Sin(Time.unscaledTime * 8));
                glow.color = c;
                glow.enabled = c.a > .01f;
            }
            if (flash)
            {
                flash.color = new Color(1, 1, 1, flashAmount * .8f);
                flash.enabled = flashAmount > .01f;
            }
        }
    }
}
