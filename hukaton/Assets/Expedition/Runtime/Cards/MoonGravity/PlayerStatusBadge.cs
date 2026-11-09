using System.Collections.Generic;
using Hakaton.Character;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// A small HUD badge under the hearts while a timed card modifier is on the player (Moon gravity, Lightness): an
    /// icon inside a ring that drains with the remaining time and blinks for the last seconds; it pops when refreshed
    /// and disappears with a soft sound when the modifier ends. Optionally keeps an aura effect around the camera for
    /// as long. Instantiated from its prefab under the expedition HUD canvas and destroyed with the outing.
    /// </summary>
    public sealed class PlayerStatusBadge : MonoBehaviour
    {
        [Tooltip("The motor modifier this badge follows.")]
        public MotorModifier modifier;
        public RectTransform rect;
        public Image icon;
        [Tooltip("Filled image: drains with the remaining time.")]
        public Image ring;
        [Header("Layout (top-left, under the hearts)")]
        public Vector2 firstPosition = new Vector2(40, -100);
        [Min(0)] public float spacing = 60;
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
        Color iconColour = Color.white, ringColour = Color.white;

        /// <summary>Shows the badge for <paramref name="prefab"/>'s modifier, or refreshes the one already shown.</summary>
        public static PlayerStatusBadge Show(ExpeditionWorld world, PlayerStatusBadge prefab)
        {
            if (!world || !prefab || !world.motor) return null;
            Shown.RemoveAll(b => !b);
            foreach (var shown in Shown)
                if (shown.world == world && shown.modifier == prefab.modifier && !shown.ending) { shown.Refresh(); return shown; }
            var canvas = world.hud ? world.hud.canvas : null;
            if (!canvas) return null;
            var badge = Instantiate(prefab, canvas.transform, false);
            world.TrackForOuting(badge.gameObject);
            badge.Begin(world);
            return badge;
        }

        void Begin(ExpeditionWorld owner)
        {
            world = owner;
            if (icon) iconColour = icon.color;
            if (ring) ringColour = ring.color;
            Shown.Add(this);
            world.Play(startSound);
            Refresh();
            Layout();
        }

        void Refresh()
        {
            total = Mathf.Max(.01f, world.motor.ModifierRemaining(modifier));
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
            float remaining = world && world.motor ? world.motor.ModifierRemaining(modifier) : 0;
            if (remaining <= 0 || !world.Running) { End(); return; }
            if (remaining > total) total = remaining;
            if (ring) ring.fillAmount = remaining / total;
            float alpha = 1;
            if (remaining < warnSeconds)
            {
                // Faster blinking as the end comes closer.
                float urgency = 1 - remaining / Mathf.Max(.01f, warnSeconds);
                float wave = .5f + .5f * Mathf.Cos(Time.time * Mathf.Lerp(7, 20, urgency));
                alpha = Mathf.Lerp(blinkMinimumAlpha, 1, wave);
            }
            if (icon) icon.color = new Color(iconColour.r, iconColour.g, iconColour.b, iconColour.a * alpha);
            if (ring) ring.color = new Color(ringColour.r, ringColour.g, ringColour.b, ringColour.a * alpha);
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
