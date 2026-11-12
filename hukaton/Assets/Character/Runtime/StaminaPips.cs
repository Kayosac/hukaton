using System.Collections.Generic;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Character
{
    /// <summary>
    /// Stamina as an arc of pips around the crosshair, under it (Game/Prefabs/UI/Stamina pips.prefab, one instance in the shop
    /// HUD and one in the outing HUD): one pip per second of sprint, and every «Выносливость» step bought on the PC adds a
    /// pip. Shown only while it matters (sprinting, getting the breath back, out of breath) and faded out soon after the
    /// budget is full. A spent pip pops up and goes hollow; pips fill back one by one with a small hop; out of breath the
    /// whole arc drops, shakes and every pip turns hollow and red until the recovery threshold.
    /// The HUD that owns the arc feeds it every frame (<see cref="Present"/>). The template pip is the first pip; clones of
    /// it are made as the capacity grows. Every offset is in whole layout units (the HUD's pixel grid): nothing rotates or
    /// scales.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class StaminaPips : MonoBehaviour
    {
        [Tooltip("Fades the whole arc in and out.")]
        public CanvasGroup visibility;
        [Tooltip("The first pip; the others are clones of it, made as the capacity grows. Its size is the pip's size (layout units).")]
        public Image pipTemplate;
        [Tooltip("A pip with stamina left in it.")]
        public Sprite fullPip;
        [Tooltip("A spent pip: hollow.")]
        public Sprite emptyPip;
        [Tooltip("Every pip while out of breath: hollow and red, until the recovery threshold.")]
        public Sprite tiredPip;

        [Header("Arc (layout units)")]
        [Tooltip("Distance between neighbouring pips along the arc.")]
        [Min(1)] public int spacing = 8;
        [Tooltip("Radius of the arc around the aim: the same as the arc root's distance under the aim, so the arc hugs the crosshair.")]
        [Min(1)] public int radius = 21;
        [Tooltip("The arc never spans more than this; a bigger budget packs the pips closer.")]
        [Range(30, 300)] public float maxSpanDegrees = 180;

        [Header("Motion (springs, rounded to whole units)")]
        [Tooltip("Upward kick (units/s) of a pip that has just been spent.")]
        public float spendKick = 160;
        [Tooltip("Upward kick of a pip that has just filled again, or appeared with a bought step.")]
        public float refillKick = 70;
        [Tooltip("Downward kick of every pip when the breath runs out.")]
        public float exhaustedDrop = 130;
        [Tooltip("Sideways kick of the whole arc when the breath runs out.")]
        public float exhaustedShake = 220;
        [Min(.1f)] public float frequency = 7;
        [Range(0, 1)] public float damping = .3f;

        [Header("Visibility")]
        [Min(.1f)] public float fadeSpeed = 6;
        [Tooltip("The arc stays this long after the budget is full again, then fades out.")]
        [Min(0)] public float holdSeconds = .6f;

        readonly List<Image> pips = new List<Image>();
        SpringFloat[] hops = new SpringFloat[0];
        SpringFloat shake;
        int shownLit = -1, shownCount;
        bool wasExhausted;
        float holdLeft, lastPresented = float.NegativeInfinity;

        /// <summary>Pips laid out at the last <see cref="Present"/>.</summary>
        public int Count => shownCount;
        /// <summary>Lit pips at the last <see cref="Present"/> (-1 before the first).</summary>
        public int Lit => shownLit;

        void Awake()
        {
            if (visibility) visibility.alpha = 0;
        }

        /// <summary>
        /// One frame of the arc for <paramref name="stamina"/>. <paramref name="allowed"/> is the HUD's own say (the aim is
        /// on screen, no modal open); the arc then still shows only while the stamina is not full.
        /// </summary>
        public void Present(PlayerStamina stamina, bool allowed, float deltaTime)
        {
            if (!pipTemplate || !visibility) return;
            float now = Time.unscaledTime;
            // Back after a pause (the other HUD had the screen): take the state as it is, without popping every pip.
            if (now - lastPresented > .25f)
            {
                shownLit = -1;
                holdLeft = 0;
                visibility.alpha = 0;
            }
            lastPresented = now;
            if (!stamina)
            {
                Fade(false, deltaTime);
                return;
            }

            float perPip = stamina.StaminaPerPip, value = stamina.Value, capacity = stamina.Capacity;
            bool exhausted = stamina.Exhausted;
            int count = StaminaGauge.Pips(capacity, perPip);
            int lit = StaminaGauge.Lit(value, perPip, count);
            Ensure(count);
            if (shownLit >= 0) Kick(count, lit, exhausted);
            shownLit = lit;
            shownCount = count;
            wasExhausted = exhausted;

            bool relevant = exhausted || value < capacity - perPip * .01f;
            holdLeft = relevant ? holdSeconds : Mathf.Max(0, holdLeft - deltaTime);
            Fade(allowed && (relevant || holdLeft > 0), deltaTime);

            int shakeX = Mathf.RoundToInt(shake.Step(0, frequency * 1.6f, damping, deltaTime));
            for (int i = 0; i < count; i++)
            {
                var (x, y) = StaminaGauge.Arc(i, count, spacing, radius, maxSpanDegrees);
                int hop = Mathf.RoundToInt(hops[i].Step(0, frequency, damping, deltaTime));
                var pip = pips[i];
                pip.rectTransform.anchoredPosition = new Vector2(x + shakeX, y + hop);
                var look = StaminaGauge.Look(i, lit, exhausted);
                var sprite = look == StaminaPipLook.Full ? fullPip : look == StaminaPipLook.Tired ? tiredPip : emptyPip;
                if (pip.sprite != sprite) pip.sprite = sprite;
            }
        }

        void Kick(int count, int lit, bool exhausted)
        {
            // A pip that a bought step has just added.
            for (int i = shownCount; i < count; i++) hops[i].Kick(refillKick);
            if (exhausted && !wasExhausted)
            {
                shake.Kick(exhaustedShake);
                for (int i = 0; i < count; i++) hops[i].Kick(-exhaustedDrop);
                return;
            }
            for (int i = lit; i < Mathf.Min(shownLit, count); i++) hops[i].Kick(spendKick);
            for (int i = shownLit; i < lit; i++) hops[i].Kick(refillKick);
            // Breath back: the lit pips turn from red to full with a hop.
            if (wasExhausted && !exhausted)
                for (int i = 0; i < Mathf.Min(lit, shownLit); i++) hops[i].Kick(refillKick);
        }

        void Ensure(int count)
        {
            if (pips.Count == 0) pips.Add(pipTemplate);
            while (pips.Count < count)
            {
                var pip = Instantiate(pipTemplate, pipTemplate.rectTransform.parent);
                pip.name = pipTemplate.name + " " + (pips.Count + 1);
                pips.Add(pip);
            }
            if (hops.Length < pips.Count) System.Array.Resize(ref hops, pips.Count);
            for (int i = 0; i < pips.Count; i++)
            {
                bool on = i < count;
                if (pips[i].gameObject.activeSelf != on) pips[i].gameObject.SetActive(on);
            }
        }

        void Fade(bool show, float deltaTime)
            => visibility.alpha = Mathf.MoveTowards(visibility.alpha, show ? 1 : 0, fadeSpeed * deltaTime);
    }
}
