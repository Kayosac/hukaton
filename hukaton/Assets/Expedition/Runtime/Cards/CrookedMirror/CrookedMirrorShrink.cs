using System;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Кривое зеркало» on a creature (added on demand, one per creature): it shrinks smoothly to the card's multiplier,
    /// stays small and crooked (a funhouse skew and lean of its drawing, a wobble after every change, purple sparks
    /// above the head), then grows back. While small its strikes and shots deal multiplier × damage (at least ½ heart,
    /// <see cref="WorldCreature.DamageScale"/>) and its Rigidbody mass is multiplier × mass, so Impulse throws it
    /// further. The size goes through <see cref="WorldCreature.size"/>, so the drawing, capsule, navigation agent and
    /// marks above the head shrink together. Everything is put back when it ends and at the end of the outing.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)] // after WorldCreature.LateUpdate has posed the drawing
    public sealed class CrookedMirrorShrink : MonoBehaviour, ICardStatus
    {
        /// <summary>How the shrinking looks (on the card's effect asset).</summary>
        [Serializable]
        public sealed class Look
        {
            [Tooltip("Seconds to shrink, and to grow back at the end.")]
            [Min(.05f)] public float shrinkSeconds = .7f, restoreSeconds = .9f;
            [Tooltip("Overshoot of the size change (out-back).")]
            [Min(0)] public float overshoot = 2.2f;
            [Tooltip("Steady funhouse skew of the drawing while small: width × (1 + s), height × (1 − 0.6 s).")]
            [Range(0, .5f)] public float skew = .16f;
            [Tooltip("Lean of the drawing while small, degrees (to a random side).")]
            [Range(0, 30)] public float lean = 8;
            [Tooltip("The crooked wobble after each change dies down in this many seconds.")]
            [Min(0)] public float wobbleSeconds = 1.4f;
            [Tooltip("Sparks above the head while small (a looping burst held for the duration).")]
            public VfxBurst aura;
            [Min(0)] public float auraScale = .6f, auraHeight = .25f;
            [Tooltip("Burst when it grows back.")]
            public VfxBurst restoreVfx;
            public SfxCue restoreSound;
            public Color tint = new Color(.74f, .56f, .96f);
        }

        WorldCreature creature;
        ExpeditionWorld world;
        Look look = new Look();
        float authoredSize = 1, authoredMass = 1, multiplier = 1, until, changeAt, from = 1, to = 1, current = 1, seed, side = 1;
        bool captured, small, tracked;
        VfxBurst aura;

        /// <summary>Size multiplier now (1: normal size).</summary>
        public float Current => current;
        /// <summary>Still small (not yet growing back).</summary>
        public bool Small => small;
        public float Multiplier => multiplier;
        public float Remaining => small ? Mathf.Max(0, until - Time.time) : 0;

        /// <summary>The status on this creature, added if missing and tracked for the end of the outing.</summary>
        public static CrookedMirrorShrink On(WorldCreature creature, ExpeditionWorld world)
        {
            if (!creature) return null;
            var status = creature.GetComponent<CrookedMirrorShrink>();
            if (!status) status = creature.gameObject.AddComponent<CrookedMirrorShrink>();
            status.creature = creature;
            if (world) status.world = world;
            if (world && !status.tracked) { status.tracked = true; world.TrackStatus(status); }
            return status;
        }

        /// <summary>Shrinks to <paramref name="factor"/> for <paramref name="seconds"/> (a second mirror: the new factor, the later end).</summary>
        public void Shrink(float factor, float seconds, Look settings)
        {
            if (!creature) return;
            if (settings != null) look = settings;
            if (!captured)
            {
                captured = true;
                authoredSize = creature.size;
                authoredMass = creature.body ? creature.body.mass : 1;
                seed = UnityEngine.Random.Range(0, 100f);
                side = UnityEngine.Random.value < .5f ? -1 : 1;
            }
            multiplier = Mathf.Clamp(factor, .05f, 1);
            until = CrookedMirrorRules.Until(small ? until : Time.time, Time.time, seconds);
            small = true;
            BeginChange(multiplier);
            creature.DamageScale = multiplier;
            if (creature.body) creature.body.mass = CrookedMirrorRules.Mass(authoredMass, multiplier);
            if (!aura && world) aura = world.Spawn(look.aura, creature.transform.position, Quaternion.identity, look.tint, look.auraScale);
            if (aura) aura.HoldFor(until - Time.time + 1);
            enabled = true;
            Apply();
        }

        void BeginChange(float target)
        {
            from = current;
            to = target;
            changeAt = Time.time;
        }

        void Update()
        {
            if (!creature) { Destroy(this); return; }
            if (small && Time.time >= until) GrowBack();
            Apply();
            if (!small && current == 1) { captured = false; enabled = false; }
        }

        void Apply()
        {
            float seconds = to < from ? look.shrinkSeconds : look.restoreSeconds;
            float t = Mathf.Clamp01((Time.time - changeAt) / Mathf.Max(.01f, seconds));
            current = t >= 1 ? to : Mathf.Max(.05f, Mathf.LerpUnclamped(from, to, Ease.OutBack(t, look.overshoot)));
            creature.size = authoredSize * current;
            if (aura) aura.Follow(creature.transform, Vector3.up * (creature.BodyHeight + look.auraHeight));
        }

        void GrowBack()
        {
            small = false;
            BeginChange(1);
            creature.DamageScale = 1;
            if (creature.body) creature.body.mass = authoredMass;
            if (aura) { aura.Release(.4f); aura = null; }
            if (world && creature.isActiveAndEnabled)
            {
                Vector3 at = creature.transform.position + Vector3.up * creature.BodyHeight * .5f;
                world.Spawn(look.restoreVfx, at, Quaternion.identity, look.tint, Mathf.Clamp(creature.BodyHeight / (multiplier > 0 ? multiplier : 1), .6f, 1.6f));
                world.Play(look.restoreSound, at);
            }
        }

        void LateUpdate()
        {
            if (!creature || !creature.view || creature.State == CreatureState.Frozen) return;
            // How far from normal size it is now: the crookedness grows with the shrinking and goes as it grows back.
            float amount = multiplier < 1 ? Mathf.Clamp01((1 - current) / (1 - multiplier)) : 0;
            if (amount <= .001f) return;
            var crook = CrookedMirrorRules.Crook(Time.time - changeAt, seed, look.skew * amount, look.lean * amount * side, look.wobbleSeconds);
            // The creature poses its drawing afresh every frame (scale and rotation); this bends that pose.
            var drawing = creature.view.transform;
            Vector3 scale = drawing.localScale;
            drawing.localScale = new Vector3(scale.x * crook.x, scale.y * crook.y, scale.z);
            drawing.rotation *= Quaternion.Euler(0, 0, crook.z);
        }

        /// <summary>End of the outing: full size, mass and damage at once.</summary>
        public void ClearStatus()
        {
            if (aura) aura.Release(.1f);
            aura = null;
            if (creature && captured)
            {
                creature.size = authoredSize;
                creature.DamageScale = 1;
                if (creature.body) creature.body.mass = authoredMass;
            }
            captured = small = false;
            current = from = to = multiplier = 1;
            tracked = false;
            if (this) Destroy(this);
        }
    }
}
