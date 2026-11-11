using System.Collections.Generic;
using Hakaton.Expeditions.Cards;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// The fire-vulnerable burn of one <see cref="WorldCreature"/>: scales its fire hits, holds the Retro Arsenal
    /// flames on it while it burns and sets alight what it touches. Vulnerable means one of two things:
    /// <list type="bullet">
    /// <item>the creature has the <see cref="FireVulnerability"/> trait: always, with the trait's settings;</item>
    /// <item>it was set alight while in tar (feet on unlit tar, or in the fire of burning tar): with the tar's
    /// <see cref="TarPuddleZone.tarredFire"/> settings, from that igniting hit until that burn ends (runs out, ice,
    /// death, a new outing). A creature with the trait is never doubled up by tar.</item>
    /// </list>
    /// </summary>
    public sealed class CreatureFire
    {
        /// <summary>Rounding carry a burn starts with: the first fire hit rounds half up.</summary>
        public const float FirstCarry = .5f;

        readonly WorldCreature creature;
        float carry = FirstCarry, nextTouch;
        VfxBurst burning;

        static readonly Collider[] touched = new Collider[32];
        static readonly List<IIgnitable> lit = new List<IIgnitable>();
        static readonly List<ICardZone> nearby = new List<ICardZone>();

        public CreatureFire(WorldCreature creature) { this.creature = creature; }

        /// <summary>Settings of the vulnerable burn in force (trait or tar); null: fire works as usual.</summary>
        public FireVulnerabilitySettings Active { get; private set; }
        /// <summary>The vulnerability comes from tar and lasts only as long as this burn.</summary>
        public bool FromTar { get; private set; }

        // ---------------- rules ----------------

        /// <summary>
        /// Fire damage times <paramref name="multiplier"/> in whole points. The fraction left over is kept in
        /// <paramref name="carry"/> for the next hit, so no damage is lost or invented over a run of hits.
        /// </summary>
        public static int ScaleDamage(int amount, float multiplier, ref float carry)
        {
            if (amount <= 0) return amount;
            float exact = amount * multiplier + carry;
            // The epsilon keeps 1.9999 from rounding down to 1.
            int whole = Mathf.FloorToInt(exact + 1e-4f);
            carry = Mathf.Max(0, exact - whole);
            return whole;
        }

        /// <summary>
        /// Scales a hit. Only fire hits count (<paramref name="burnSeconds"/> above zero). <paramref name="trait"/>: the
        /// creature's own trait settings or null; <paramref name="tar"/>: the tar settings where it stands right now or
        /// null; <paramref name="stillBurning"/>: the creature is alight from an earlier fire hit.
        /// </summary>
        public void Hit(ref int amount, ref float burnSeconds, FireVulnerabilitySettings trait, FireVulnerabilitySettings tar, bool stillBurning)
        {
            if (burnSeconds <= 0) return;
            // A tar burn that has run out (or was put out) ends before this new fire starts.
            if (FromTar && !stillBurning) EndTarBurn();
            if (trait != null)
            {
                if (FromTar) EndTarBurn();
                Active = trait;
            }
            else if (Active == null && tar != null)
            {
                Active = tar;
                FromTar = true;
                carry = FirstCarry;
            }
            if (Active == null) return;
            amount = ScaleDamage(amount, Active.damageMultiplier, ref carry);
            burnSeconds *= Active.burnMultiplier;
        }

        /// <summary>The tar settings at these feet (on unlit tar or in burning tar), or null.</summary>
        public static FireVulnerabilitySettings TarredAt(ExpeditionWorld world, Vector3 feet)
        {
            if (!world) return null;
            FireVulnerabilitySettings found = null;
            nearby.Clear();
            // Bounds of a puddle cover only its thin layer; the fire above it reaches higher.
            world.Zones.Near(feet, 2, nearby, CardZoneKind.TarPuddle);
            foreach (var zone in nearby)
                if (zone is TarPuddleZone tar && tar.Holds(feet)) { found = tar.tarredFire; break; }
            nearby.Clear();
            return found;
        }

        /// <summary>A new outing: the burn, its flames and the carry are gone.</summary>
        public void Reset()
        {
            EndTarBurn();
            carry = FirstCarry;
            nextTouch = 0;
            PutOut(0);
        }

        void EndTarBurn()
        {
            if (!FromTar) return;
            Active = null;
            FromTar = false;
            carry = FirstCarry;
        }

        // ---------------- burning ----------------

        /// <summary>Every frame: flames while burning, touch ignition, and the end of a tar burn.</summary>
        public void Tick()
        {
            if (!creature || !creature.Burning)
            {
                float fade = Active != null ? Active.flamesFadeSeconds : 0;
                EndTarBurn();
                PutOut(fade);
                return;
            }
            if (Active == null) return;
            ShowFlames();
            if (Time.time < nextTouch) return;
            nextTouch = Time.time + Active.touchInterval;
            IgniteTouched();
        }

        void ShowFlames()
        {
            var world = creature.world;
            if (!Active.flames || !world) return;
            float height = creature.Height;
            if (!burning)
            {
                burning = world.Spawn(Active.flames, creature.Position, Quaternion.identity, Color.white, Active.flamesSize * height);
                if (!burning) return;
            }
            burning.Follow(creature.transform, Vector3.up * height * Active.flamesHeight);
            burning.HoldFor(creature.BurnSecondsLeft + Active.flamesFadeSeconds);
        }

        public void PutOut(float fadeSeconds)
        {
            if (burning) burning.Release(fadeSeconds);
            burning = null;
        }

        /// <summary>Sets alight what the burning body touches: colliders around its capsule and card zones at its feet.</summary>
        void IgniteTouched()
        {
            var world = creature.world;
            if (!world || !world.Running) return;
            var capsule = creature.BodyCapsule;
            Vector3 centre = creature.Centre;
            float reach = Active.touchReach;
            int count;
            if (capsule)
            {
                var t = capsule.transform;
                Vector3 scale = t.lossyScale;
                Vector3 axis = capsule.direction == 0 ? t.right : capsule.direction == 2 ? t.forward : t.up;
                float axisScale = Mathf.Abs(capsule.direction == 0 ? scale.x : capsule.direction == 2 ? scale.z : scale.y);
                float radiusScale = capsule.direction == 0 ? Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z))
                    : capsule.direction == 2 ? Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y))
                    : Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                float radius = capsule.radius * radiusScale;
                float half = Mathf.Max(0, capsule.height * axisScale * .5f - radius);
                centre = t.TransformPoint(capsule.center);
                reach += radius;
                count = Physics.OverlapCapsuleNonAlloc(centre - axis * half, centre + axis * half, reach, touched, Active.touchLayers, QueryTriggerInteraction.Collide);
            }
            else
            {
                reach += creature.Height * .5f;
                count = Physics.OverlapSphereNonAlloc(centre, reach, touched, Active.touchLayers, QueryTriggerInteraction.Collide);
            }

            lit.Clear();
            for (int i = 0; i < count; i++)
            {
                var other = touched[i];
                touched[i] = null;
                if (!other || other.attachedRigidbody && other.attachedRigidbody == creature.body) continue;
                var ignitable = other.GetComponentInParent<IIgnitable>();
                if (ignitable == null || lit.Contains(ignitable)) continue;
                lit.Add(ignitable);
                ignitable.Ignite(IgnitionSource.Fire, other.bounds.ClosestPoint(centre));
            }
            // Zones without colliders of their own (tar puddle, dry grass) are found around the feet. Same search as
            // ExpeditionWorld.Ignite, into a reused list: this runs several times a second per burning creature.
            Vector3 feet = creature.Position;
            nearby.Clear();
            world.Zones.Near(feet, reach, nearby);
            for (int i = 0; i < nearby.Count; i++)
                if (nearby[i] is IIgnitable zone && !lit.Contains(zone))
                {
                    lit.Add(zone);
                    zone.Ignite(IgnitionSource.Fire, feet);
                }
            nearby.Clear();
            lit.Clear();
        }
    }
}
