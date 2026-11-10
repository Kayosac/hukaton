using System.Collections.Generic;
using Hakaton.Character;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// A small HUD badge under the wallet while a timed card modifier is on the player (Moon gravity, Lightness) or the
    /// player is wet (<see cref="WetStatus"/>, rain): a glossy symbol inside a golden ring, both turning about Y in
    /// opposite directions (drawn flipbooks); the two halves of the ring drain together with the remaining time, the
    /// badge blinks for the last seconds, pops when refreshed and disappears with a soft sound when the state ends.
    /// Optionally keeps an aura effect around the camera for as long. Instantiated from its prefab under the expedition
    /// HUD canvas and destroyed with the outing.
    /// </summary>
    public sealed class PlayerStatusBadge : MonoBehaviour
    {
        public enum Source { MotorModifier, Wet }

        [Tooltip("What the badge follows: a timed motor modifier, or the player being wet (the ring is full under the rain " +
                 "and drains over the 30 s after leaving it).")]
        public Source source = Source.MotorModifier;
        [Tooltip("The motor modifier this badge follows (Source = MotorModifier).")]
        public MotorModifier modifier;
        public RectTransform rect;
        public Image icon;
        [Tooltip("Filled images drained together with the remaining time: the far and the near half of the ring.")]
        public Image[] rings = new Image[0];
        [Header("Layout (in the HUD's status row, under the wallet)")]
        public Vector2 firstPosition = Vector2.zero;
        [Min(0)] public float spacing = 28;
        [Header("Timing")]
        [Tooltip("Blinks during the last seconds.")]
        [Min(0)] public float warnSeconds = 3;
        [Range(0, 1)] public float blinkMinimumAlpha = .25f;
        [Header("Feel")]
        public SfxCue startSound;
        public SfxCue endSound;
        [Tooltip("Optional effect around the camera while the modifier lasts (floating dust under Moon gravity).")]
        public VfxBurst aura;
        public Vector3 auraOffset = new Vector3(0, -.3f, 0);
        public Color tint = Color.white;
        [Min(.1f)] public float popFrequency = 4;
        [Range(0, 1)] public float popDamping = .3f;

        static readonly List<PlayerStatusBadge> Shown = new List<PlayerStatusBadge>();

        ExpeditionWorld world;
        float total;
        bool ending;
        SpringFloat pop;
        VfxBurst auraInstance;
        Color iconColour = Color.white;
        Color[] ringColours = new Color[0];

        /// <summary>Shows the badge for <paramref name="prefab"/>'s modifier, or refreshes the one already shown.</summary>
        public static PlayerStatusBadge Show(ExpeditionWorld world, PlayerStatusBadge prefab)
        {
            if (!world || !prefab || !world.motor) return null;
            Shown.RemoveAll(b => !b);
            foreach (var shown in Shown)
                if (shown.world == world && shown.Follows(prefab) && !shown.ending) { shown.Refresh(); return shown; }
            var row = world.hud ? (world.hud.statusRow ? (Transform)world.hud.statusRow : world.hud.canvas ? world.hud.canvas.transform : null) : null;
            if (!row) return null;
            var badge = Instantiate(prefab, row, false);
            world.TrackForOuting(badge.gameObject);
            badge.Begin(world);
            return badge;
        }

        void Begin(ExpeditionWorld owner)
        {
            world = owner;
            if (icon) iconColour = icon.color;
            ringColours = new Color[rings.Length];
            for (int i = 0; i < rings.Length; i++) ringColours[i] = rings[i] ? rings[i].color : Color.white;
            Shown.Add(this);
            world.Play(startSound);
            Refresh();
            Layout();
        }

        bool Follows(PlayerStatusBadge other) => source == other.source && (source == Source.Wet || modifier == other.modifier);

        /// <summary>Seconds left of what the badge follows (0 when it is over).</summary>
        float Remaining()
        {
            if (!world || !world.motor) return 0;
            if (source == Source.Wet)
            {
                var wet = world.motor.GetComponentInParent<WetStatus>();
                return wet ? wet.Remaining : 0;
            }
            return world.motor.ModifierRemaining(modifier);
        }

        void Refresh()
        {
            total = Mathf.Max(.01f, Remaining());
            pop.Kick(9);
            if (aura && world.viewCamera)
            {
                if (!auraInstance) { auraInstance = world.Spawn(aura, world.viewCamera.transform.position + auraOffset, Quaternion.identity, tint); }
                if (auraInstance) { auraInstance.Follow(world.viewCamera.transform, auraOffset); auraInstance.HoldFor(total + 1); }
            }
        }

        void LateUpdate()
        {
            if (ending) return;
            float remaining = Remaining();
            if (remaining <= 0 || !world.Running) { End(); return; }
            if (remaining > total) total = remaining;
            // The remaining time, not the turning phase: both halves always show the same share.
            foreach (var ring in rings) if (ring) ring.fillAmount = remaining / total;
            float alpha = 1;
            if (remaining < warnSeconds)
            {
                // Faster blinking as the end comes closer.
                float urgency = 1 - remaining / Mathf.Max(.01f, warnSeconds);
                float wave = .5f + .5f * Mathf.Cos(Time.time * Mathf.Lerp(7, 20, urgency));
                alpha = Mathf.Lerp(blinkMinimumAlpha, 1, wave);
            }
            if (icon) icon.color = new Color(iconColour.r, iconColour.g, iconColour.b, iconColour.a * alpha);
            for (int i = 0; i < rings.Length; i++)
                if (rings[i]) rings[i].color = new Color(ringColours[i].r, ringColours[i].g, ringColours[i].b, ringColours[i].a * alpha);
            float scale = 1 + pop.Step(0, popFrequency, popDamping, Time.unscaledDeltaTime) * .04f;
            transform.localScale = Vector3.one * Mathf.Max(.2f, scale);
            Layout();
        }

        void Layout()
        {
            if (!rect) return;
            int index = 0;
            foreach (var shown in Shown)
            {
                if (shown == this) break;
                if (shown && !shown.ending) index++;
            }
            rect.anchoredPosition = firstPosition + Vector2.right * spacing * index;
        }

        void End()
        {
            ending = true;
            if (world) world.Play(endSound);
            if (auraInstance) auraInstance.Release(.8f);
            auraInstance = null;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            Shown.Remove(this);
            if (auraInstance) auraInstance.Release(.5f);
        }
    }
}
