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
    /// level merges them. Purely presentation: <see cref="PcCardsWindow"/> decides what happens, <see cref="PcMergeShow"/>
    /// poses the two cards of a merge.
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
        /// <summary>Dropped, but the body stays on the drag layer: the merge show flies it.</summary>
        public bool Held { get; private set; }
        /// <summary>Merged away: invisible until bound to another copy.</summary>
        public bool Gone { get; private set; }

        public event Action<PcCardTile> Clicked, HoverStarted, HoverEnded, DragStarted;
        public event Action<PcCardTile, PointerEventData> Dragging, Dropped;

        SpringFloat lift, scale, shake, alpha, glowAmount;
        SpringVector3 offset;
        float flashAmount, shakeTime = -1;
        Vector2 grab;
        RectTransform dragLayer;

        // Merge show pose: only the art (face, glow, flash) moves, so the pixel-font level mark is never rotated or scaled.
        bool posed;
        Vector2 poseShift, poseScale = Vector2.one;
        float poseAngle, poseFlash, poseGlow;
        Color poseGlowColour = Color.white;
        SpringFloat flip, swell;
        float flipTarget = 1, flipFrequency = 14;
        Action atEdge;
        RectTransform[] artParts;
        Vector2[] artRest;
        Vector3 artScaleShown = Vector3.one;
        Vector2 artShiftShown;
        float artAngleShown;
        TextPunch markPunch;
        float markPunchUntil;

        void Awake()
        {
            scale.Snap(1);
            alpha.Snap(1);
            flip.Snap(1);
            artParts = new[] { face ? face.rectTransform : null, glow ? glow.rectTransform : null, flash ? flash.rectTransform : null };
            artRest = new Vector2[artParts.Length];
            for (int i = 0; i < artParts.Length; i++) if (artParts[i]) artRest[i] = artParts[i].anchoredPosition;
            if (levelMark && levelMark.transform.parent is RectTransform paper) markPunch = new TextPunch(paper);
        }

        public void Bind(int copyId, Sprite art, int level, bool inDeck)
        {
            bool fresh = CopyId != copyId || Gone;
            CopyId = copyId;
            InDeck = inDeck;
            face.sprite = art;
            CardLevelPresentation.SetMark(levelMark, level);
            gameObject.SetActive(true);
            if (!fresh) return;
            // Another card in this pooled tile: nothing of the previous one's look may carry over (no fade-in, no flash).
            lift.Snap(0);
            offset.Snap(Vector3.zero);
            scale.Snap(1);
            alpha.Snap(inDeck ? 1 : outOfDeckAlpha);
            flashAmount = 0;
            shakeTime = -1;
            Gone = false;
            ResetPose();
        }

        public void Hide()
        {
            EndDragNow();
            Hovering = false;
            Gone = false;
            ResetPose();
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
            if (e.button != PointerEventData.InputButton.Left || !dragLayer || !CrtRaycaster.Current || Held) return;
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
            if (Held)
            {
                // The merge show took the body over; it stays on the drag layer.
                Dragged = false;
                return;
            }
            EndDragNow();
        }

        /// <summary>Called from a <see cref="Dropped"/> handler: the body stays on the drag layer for the merge show.</summary>
        public void Hold()
        {
            if (Dragged) Held = true;
        }

        /// <summary>Puts a held body at a point of the drag layer (whole pixels).</summary>
        public void PlaceHeld(Vector2 point)
        {
            if (Held) body.anchoredPosition = new Vector2(Mathf.Round(point.x), Mathf.Round(point.y));
        }

        /// <summary>Back into the grid; the body glides home from where it was dropped.</summary>
        public void EndDragNow()
        {
            if (!Dragged && !Held) return;
            Dragged = Held = false;
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

        /// <summary>Merged away: no flight home, invisible until the tile shows another copy.</summary>
        public void Vanish()
        {
            EndDragNow();
            offset.Snap(Vector3.zero);
            alpha.Snap(0);
            Gone = true;
            EndPose();
        }

        /// <summary>
        /// The grid has just moved this card to another cell (the layout is already rebuilt): its body glides over
        /// from where it was drawn before, given in world space.
        /// </summary>
        public void GlideFrom(Vector3 bodyWorld)
        {
            if (Dragged || Held || Gone || !isActiveAndEnabled) return;
            Vector2 local = transform.InverseTransformPoint(bodyWorld);
            offset.Snap(new Vector3(local.x, local.y - lift.Value, 0));
            body.anchoredPosition = new Vector2(Mathf.Round(local.x), Mathf.Round(local.y));
        }

        // ---------- merge show ----------

        /// <summary>
        /// Poses the art for the merge show: whole-pixel shift, tilt, scale, white flash and a frame glow in a colour.
        /// A posed card is fully opaque and does not lift on hover.
        /// </summary>
        public void Pose(Vector2 shift, float angle, Vector2 artScale, float flashLevel, float glowLevel, Color glowColour)
        {
            posed = true;
            poseShift = shift;
            poseAngle = angle;
            poseScale = artScale;
            poseFlash = Mathf.Clamp01(flashLevel);
            poseGlow = Mathf.Clamp01(glowLevel);
            poseGlowColour = glowColour;
        }

        /// <summary>Back to the plain look; a flip or swell in progress settles by itself.</summary>
        public void EndPose()
        {
            posed = false;
            poseShift = Vector2.zero;
            poseScale = Vector2.one;
            poseAngle = poseFlash = poseGlow = 0;
            flipTarget = 1;
            atEdge = null;
        }

        void ResetPose()
        {
            EndPose();
            flip.Snap(1);
            swell.Snap(0);
            ApplyArt();
        }

        /// <summary>Squash-flips the art; <paramref name="edgeOn"/> runs at the edge-on moment, then it opens with an overshoot.</summary>
        public void FlipArt(Action edgeOn, float frequency = 14)
        {
            flipTarget = -1;
            flipFrequency = Mathf.Max(1, frequency);
            atEdge = edgeOn;
        }

        /// <summary>Kicks the art's scale (springs back with an overshoot).</summary>
        public void Swell(float kick) => swell.Kick(kick);

        public void HideMark()
        {
            if (levelMark) levelMark.transform.parent.gameObject.SetActive(false);
        }

        /// <summary>The new level after a merge (the copy stays the same).</summary>
        public void ShowLevel(int level, bool inDeck)
        {
            InDeck = inDeck;
            CardLevelPresentation.SetMark(levelMark, level);
        }

        /// <summary>The level mark hops up by about this many pixels; pixel-safe (moves only, whole pixels).</summary>
        public void HopMark(float pixels)
        {
            if (markPunch == null || pixels <= 0) return;
            markPunch.Hop(pixels);
            markPunchUntil = Time.unscaledTime + 1.5f;
        }

        void OnDisable()
        {
            if (Dragged || Held) EndDragNow();
            Hovering = false;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1 / 20f);
            bool home = !Dragged && !Held;
            bool lifted = (Hovering || Targeted) && home && !posed;
            lift.Step(lifted ? hoverLift : 0, 9, .55f, dt);
            scale.Step(Dragged ? 1.08f : Targeted ? 1.06f : lifted ? 1.03f : 1, 7, .45f, dt);
            if (home) offset.Step(Vector3.zero, 6, .8f, dt);
            float wantAlpha = Gone ? 0 : posed ? 1 : DragRole == Role.Other ? .22f : DragRole == Role.Partner || DragRole == Role.Source ? 1 : InDeck ? 1 : outOfDeckAlpha;
            alpha.Step(wantAlpha, 8, 1, dt);
            glowAmount.Step(DragRole == Role.Partner ? 1 : 0, 8, 1, dt);

            flip.Step(flipTarget, flipFrequency, .7f, dt);
            if (flipTarget < 0 && flip.Value <= 0)
            {
                // Edge-on: the other side shows, and the art opens back up with a little overshoot.
                flipTarget = 1;
                flip.Snap(0);
                flip.Kick(9);
                var edge = atEdge;
                atEdge = null;
                edge?.Invoke();
            }
            swell.Step(0, 7, .35f, dt);
            if (markPunch != null && Time.unscaledTime < markPunchUntil) markPunch.Update(dt);

            float shakeX = 0;
            if (shakeTime >= 0)
            {
                shakeTime += dt;
                shakeX = Mathf.Sin(shakeTime * 70) * 5 * Mathf.Max(0, 1 - shakeTime / .35f);
                if (shakeTime > .35f) shakeTime = -1;
            }
            flashAmount = Mathf.MoveTowards(flashAmount, 0, dt * 2.5f);

            if (home)
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
                if (posed && poseGlow > c.a)
                {
                    c = poseGlowColour;
                    c.a = poseGlow;
                }
                glow.color = c;
                glow.enabled = c.a > .01f;
            }
            if (flash)
            {
                float f = Mathf.Max(flashAmount * .8f, posed ? poseFlash : 0);
                flash.color = new Color(1, 1, 1, f);
                flash.enabled = f > .01f;
            }
            ApplyArt();
        }

        /// <summary>Writes the art's pose (only when it changes, so a resting grid does not rebuild its canvas).</summary>
        void ApplyArt()
        {
            if (artParts == null) return;
            float s = 1 + swell.Value;
            var scaleNow = new Vector3(Mathf.Max(.02f, Mathf.Abs(flip.Value)) * s * poseScale.x, s * poseScale.y, 1);
            var shiftNow = new Vector2(Mathf.Round(poseShift.x), Mathf.Round(poseShift.y));
            if (scaleNow == artScaleShown && shiftNow == artShiftShown && Mathf.Approximately(poseAngle, artAngleShown)) return;
            artScaleShown = scaleNow;
            artShiftShown = shiftNow;
            artAngleShown = poseAngle;
            var rotation = Quaternion.Euler(0, 0, poseAngle);
            for (int i = 0; i < artParts.Length; i++)
            {
                if (!artParts[i]) continue;
                artParts[i].localScale = scaleNow;
                artParts[i].localRotation = rotation;
                artParts[i].anchoredPosition = artRest[i] + shiftNow;
            }
        }
    }
}
