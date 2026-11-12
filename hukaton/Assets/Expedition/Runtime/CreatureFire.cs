using Hakaton.Expeditions.Cards;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.Pool;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// The fire-vulnerable burn of one <see cref="WorldCreature"/>: scales its fire hits, holds the Retro Arsenal
    /// flames on it while it burns and sets alight what it touches. Vulnerable means one of two things:
    /// <list type="bullet">
    /// <item>the creature has the <see cref="FireVulnerability"/> trait: always, by the trait's settings;</item>
    /// <item>it was set alight while in tar (feet on unlit tar, or in the fire of burning tar): by the tar's
    /// <see cref="TarPuddleZone.tarredFire"/> settings, from that igniting hit until that burn ends (runs out, ice,
    /// death, a new outing). A creature with the trait is never doubled up by tar.</item>
    /// </list>
    /// The settings hold the rules; where the flames sit on the body is the creature's own
    /// (<see cref="WorldCreature.flamesSize"/>, <see cref="WorldCreature.flamesHeight"/>).
    /// </summary>
    public sealed class CreatureFire
    {
        /// <summary>Rounding carry every burn starts with: the first fire hit of a burn rounds half up.</summary>
        public const float FirstCarry = .5f;

        readonly WorldCreature creature;
        float carry = FirstCarry, nextTouch;
        VfxBurst burning;

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
        /// null; <paramref name="stillBurning"/>: the creature is alight from an earlier fire hit. A fire hit on a
        /// creature that is not burning starts a new burn: a tar burn that ran out (or was put out) is over, and the
        /// rounding starts again from a half, so the same hit always does the same damage as the first of a burn.
        /// </summary>
        public void Hit(ref int amount, ref float burnSeconds, FireVulnerabilitySettings trait, FireVulnerabilitySettings tar, bool stillBurning)
        {
            if (burnSeconds <= 0) return;
            if (!stillBurning)
            {
                EndTarBurn();
                carry = FirstCarry;
            }
            if (trait)
            {
                EndTarBurn();
                Active = trait;
            }
            else if (!Active && tar)
            {
                Active = tar;
                FromTar = true;
            }
            if (!Active) return;
            amount = ScaleDamage(amount, Active.damageMultiplier, ref carry);
            burnSeconds *= Active.burnMultiplier;
        }

        /// <summary>The tar settings at these feet (on unlit tar or in burning tar), or null.</summary>
        public static FireVulnerabilitySettings TarredAt(ExpeditionWorld world, Vector3 feet)
        {
            if (!world) return null;
            // A pooled list of its own: called from Damage, which may run inside another zone search.
            using (ListPool<ICardZone>.Get(out var nearby))
            {
                // Bounds of a puddle cover only its thin layer; the fire above it reaches higher.
                world.Zones.Near(feet, 2, nearby, CardZoneKind.TarPuddle);
                foreach (var zone in nearby)
                    if (zone is TarPuddleZone tar && tar && tar.tarredFire && tar.Holds(feet)) return tar.tarredFire;
            }
            return null;
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
        }

        // ---------------- burning ----------------

        /// <summary>Every frame: flames while burning, touch ignition, and the end of a tar burn.</summary>
        public void Tick()
        {
            if (creature is null || !creature.Burning)
            {
                // Not burning: nothing to do unless a burn has just ended.
                if (!(burning is null)) PutOut(Active ? Active.flamesFadeSeconds : 0);
                EndTarBurn();
                return;
            }
            if (!Active) return;
            ShowFlames();
            if (Time.time < nextTouch) return;
            nextTouch = Time.time + Active.touchInterval;
            var world = creature.world;
            if (world && world.Running)
                world.Ignite(creature.Position, creature.BodyHeight, creature.CapsuleRadius, Active.touchReach, IgnitionSource.Fire);
        }

        void ShowFlames()
        {
            if (!burning)
            {
                // Once per burn: placed by the creature (size per metre of its height, height share above the feet).
                var world = creature.world;
                if (!Active.flames || !world) return;
                float height = creature.BodyHeight;
                burning = world.Spawn(Active.flames, creature.Position, Quaternion.identity, Color.white, creature.flamesSize * height);
                if (!burning) return;
                burning.Follow(creature.transform, Vector3.up * height * creature.flamesHeight);
            }
            burning.HoldFor(creature.BurnSecondsLeft + Active.flamesFadeSeconds);
        }

        public void PutOut(float fadeSeconds)
        {
            if (burning) burning.Release(fadeSeconds);
            burning = null;
        }
    }
}
