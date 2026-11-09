using Hakaton.Character;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// The «мокрый» state of any body: the player, a creature, an ally, a loose object. Added on demand by
    /// <see cref="Soak"/>. Board rule: wet at once under rain, stays wet while under it and
    /// <see cref="DefaultLingerSeconds"/> (30 s) after the last contact; wetting again refreshes the time, never adds up.
    /// Ice freezes a wet creature twice as long; lightning deals double damage and chains between wet bodies.
    /// While wet, non-player bodies drip (Retro Arsenal drip, <see cref="ExpeditionVfx.wetDrips"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WetStatus : MonoBehaviour, ICardStatus
    {
        public const float DefaultLingerSeconds = 30;

        TimedModifier wet;
        ExpeditionWorld world;
        VfxBurst drips;

        public bool Wet => wet.IsActive(Time.time);
        /// <summary>Seconds of wetness left (0 when dry).</summary>
        public float Remaining => wet.Remaining(Time.time);

        /// <summary>
        /// Wets <paramref name="body"/> now and keeps it wet for <paramref name="lingerSeconds"/> after this call.
        /// Call it every tick while the body is under rain: the 30 s count from the last call. Pass the body's root
        /// (the creature, <c>world.motor</c>, the ally); returns the status.
        /// </summary>
        public static WetStatus Soak(Component body, ExpeditionWorld world, float lingerSeconds = DefaultLingerSeconds)
        {
            if (!body) return null;
            var status = body.GetComponentInParent<WetStatus>();
            if (!status) status = body.gameObject.AddComponent<WetStatus>();
            status.Soak(world, lingerSeconds);
            return status;
        }

        /// <summary>Is this body (or the body this collider belongs to) wet right now?</summary>
        public static bool IsWet(Component body)
        {
            var status = body ? body.GetComponentInParent<WetStatus>() : null;
            return status && status.Wet;
        }

        /// <summary>Instance form of <see cref="Soak(Component, ExpeditionWorld, float)"/>.</summary>
        public void Soak(ExpeditionWorld owner, float lingerSeconds = DefaultLingerSeconds)
        {
            if (owner && owner != world)
            {
                world = owner;
                world.TrackStatus(this);
            }
            wet.Apply(1, lingerSeconds, Time.time);
            ShowDrips();
        }

        /// <summary>Dries the body at once.</summary>
        public void Dry()
        {
            wet.Clear();
            HideDrips();
        }

        public void ClearStatus() => Dry();

        void Update()
        {
            if (drips && !Wet) HideDrips();
        }

        void OnDisable() => HideDrips();

        void ShowDrips()
        {
            // First person: drops in front of the camera would only get in the way.
            if (!world || !world.vfx || !world.vfx.wetDrips || GetComponent<FirstPersonMotor>()) return;
            if (!drips)
            {
                drips = world.Spawn(world.vfx.wetDrips, transform.position, Quaternion.identity, Color.white);
                if (!drips) return;
                drips.Follow(transform, Vector3.up * TopOfBody());
            }
            drips.HoldFor(Remaining);
        }

        void HideDrips()
        {
            if (drips) drips.Release(.4f);
            drips = null;
        }

        float TopOfBody()
        {
            var colliders = GetComponentsInChildren<Collider>();
            if (colliders.Length == 0) return 1;
            var bounds = colliders[0].bounds;
            foreach (var c in colliders) bounds.Encapsulate(c.bounds);
            return Mathf.Max(.3f, bounds.max.y - transform.position.y);
        }
    }
}
